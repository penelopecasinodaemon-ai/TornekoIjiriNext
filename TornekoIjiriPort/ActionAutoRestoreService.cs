using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TornekoIjiriPort;

internal sealed class ActionAutoRestoreService : IDisposable
{
    // 敵特技呼び出し命令
    private const uint PatchAddress = 0x002435C4u;

    // 元の jal dgEnemyAttack
    private const uint OriginalCallInstruction = 0x0C08F1D4u;

    // 元の遅延スロット lh s1,4(v0)
    private const uint DelaySlotAddress = 0x002435C8u;
    private const uint OriginalDelayInstruction = 0x84510004u;

    // 旧版の全敵共通パッチ ori s1,zero,actionId
    private const uint LegacyPatchBase = 0x34110000u;

    // EE RAMの高位領域から512バイトの空き領域を探す
    private const uint CaveSearchStart = 0x01FF0000u;
    private const uint CaveSearchEnd = 0x01F00000u;
    private const uint CaveSearchStep = 0x00010000u;

    private const uint CaveSize = 0x200u;
    private const uint TableOffset = 0x100u;
    private const int TableSize = 256;
    // コロマージ呪文分岐調査フック
    private const uint SpellDispatchAddress = 0x002CF600u;
    // MOD版コロマージ呪文成功判定: slt v0,v0,v1 を li v0,1 に置換
    private const uint IonazunCompareAddress = 0x00228DE8u;
    private const uint IonazunCompareOriginal = 0x0043102Au;
    private const uint IonazunCompareForced = 0x24020001u;

    // 使用済みイオナズン判定の種族別フック
    private const uint IonazunUsedBranchAddress = 0x00228DD0u;
    private const uint IonazunUsedBranchOriginal = 0x144000E3u;
    private const uint IonazunRepeatCaveStart = 0x01DF0000u;
    private const uint IonazunRepeatCaveEnd = 0x01D00000u;
    private const uint IonazunRepeatCaveStep = 0x00010000u;
    private const uint IonazunRepeatCaveSize = 0x200u;
    private const uint SpellDispatchOriginalInstruction = 0x2A020046u;
    private const uint SpellDispatchDelayAddress = 0x002CF604u;
    private const uint SpellDispatchOriginalDelay = 0x10400013u;
    private const uint SpellTraceDataOffset = 0x100u;
    private const uint SpellTraceCaveSearchStart = 0x01EF0000u;
    private const uint SpellTraceCaveSearchEnd = 0x01E00000u;
    private const uint SpellTraceCaveSearchStep = 0x00010000u;
    private const uint SpellTraceCaveSize = 0x200u;
    private static uint _spellTraceCaveAddress;

        internal static readonly (ushort Id, string Name)[] ActionChoices =
    {
        (0x08, "体力が回復しなくなった"),
        (0x09, "体力が減っていく"),
        (0x13, "かなしばり"),
        (0x1A, "仲間たちを呼ぶ"),
        (0x1C, "凍える吹雪"),
        (0x1F, "呪いの言葉"),
        (0x20, "杖の使用回数0"),
        (0x21, "睨みつけ"),
        (0x22, "アストロン"),
        (0x23, "力を溜めた(トロル)"),
        (0x24, "力を溜めた(トロルボンバー)"),
        (0x25, "超ちからため(トロルキング)"),
        (0x26, "くさった死体のくさった液"),
        (0x27, "リビングデッドのくさった液"),
        (0x28, "グールのくさった液"),
        (0x29, "どくどくゾンビのくさった液"),
        (0x2B, "キースのホーミングブレス"),
        (0x2C, "ダースのホーミングブレス"),
        (0x2D, "装備弾き"),
        (0x2E, "しりょうのきしのアイテム弾き"),
        (0x2F, "かげのきしのアイテム弾き"),
        (0x33, "わらいぶくろのお金盗み"),
        (0x34, "毒を受けて力が1下がった"),
        (0x35, "毒を受けて力が3下がった(マタンゴ)"),
        (0x36, "毒を受けて力が3下がった(マージマタンゴ)"),
        (0x37, "ラリホー"),
        (0x38, "催眠"),
        (0x39, "ベビーサタンのアイテム盗み"),
        (0x3A, "ミニデーモンの壺盗み"),
        (0x3B, "つかいまの装備盗み"),
        (0x3C, "ミステリドールの吸収光線"),
        (0x3D, "いしにんぎょうの吸収光線"),
        (0x3E, "おどる宝石のお金盗み、バリア貫通型さそう踊り"),
        (0x3F, "レベルダウンダンス"),
        (0x4B, "さそう踊り(ダンスキャロット)"),
        (0x4C, "満腹度-10"),
        (0x4D, "さそう踊り(テンツク)"),
        (0x4E, "さそう踊り(スーパーテンツク)"),
        (0x4F, "さそう踊り(ラストテンツク)"),
        (0x54, "バシルーラ"),
        (0x55, "ゲーツ"),
        (0x57, "力を溜めた(あくましんかん)"),
        (0x58, "超ちからため(じごくのつかい)"),
        (0x5E, "強化攻撃(さつじんき)"),
        (0x5F, "強化攻撃(エリミネーター)"),
        (0x60, "強化攻撃(デスストーカー)"),
        (0x62, "口をふさぐ"),
        (0x68, "マホトーン"),
        (0x6C, "しかし力が出ず出来なかった(ギガンテス)"),
        (0x6D, "力を溜めた(アトラス)"),
        (0x6F, "力を溜めた(ベリアル)"),
        (0x70, "力を溜めた(おおきづち)"),
        (0x71, "力を溜めた(ブラウニー)"),
        (0x79, "しかし発生できなかった(ぐんたいアリを召喚？)"),
        (0x81, "50ダメージ(ガーゴイル)"),
        (0x82, "強化攻撃(モシャスナイト)"),
        (0x83, "強化攻撃(シャドーナイト)"),
        (0x84, "マダンテ"),
        (0x8C, "3ターンかなしばり(ちゅうまじゅう)"),
        (0x8D, "3ターンかなしばり(ストローマウス)"),
        (0x8E, "3ターンかなしばり(あめふらし)"),
        (0x90, "サンダーラットの眩しい光"),
        (0x91, "プラズママウスの眩しい光"),
        (0x95, "バブリン分裂"),
        (0x96, "プヨンターゲット分裂"),
        (0x97, "ポムポムボム分裂"),
        (0x98, "リビングハンマーの自爆"),
        (0x99, "キラースターの自爆"),
        (0x9A, "デビルアンカーの自爆の構え"),
        (0xA6, "力を溜めた(レノファイター)"),
        (0xA7, "力を溜めた(グレイトホーン)"),
        (0xA9, "25ダメージ(フライングデビル)"),
        (0xAA, "25ダメージ(ランガー)"),
        (0xAB, "真空斬り、バイキルト"),
        (0xAC, "真空斬り"),
        (0xAD, "思いっきり剣を振った"),
        (0xAE, "封印攻撃"),
        (0xAF, "スカラ"),
        (0xB0, "殻に入る(つのうしがい)"),
        (0xB1, "殻に入る(しびれマイマイ)"),
        (0xB2, "殻に入る(あんこくつむり)"),
        (0xB6, "攻撃力-1"),
        (0xB7, "防御力-1"),
        (0xB8, "メダパニ、仲間呼び(同種)"),
        (0xB9, "メダパニ、仲間呼び(同種)"),
        (0xBA, "薙ぎ払い"),
        (0xBC, "50ダメージ(ジャスティス兄)"),
        (0xBD, "失敗した(コロヒーロー)"),
        (0xBF, "コロマージの魔法"),
        (0xC0, "しかし誰に対しても効果が無かった(コロプリースト)"),
        (0xC1, "失敗した(プチヒーロー)"),
        (0xC3, "プチマージの魔法"),
        (0xC4, "しかし誰に対しても効果が無かった(プチプリースト)"),
        (0xC5, "50ダメージ(ヘルジャスティス)")
    };

    private static readonly PineClient SharedPine = new PineClient();
    private static readonly object SharedPineAccessSync = new object();
    private readonly PineClient _pine = SharedPine;
    private readonly System.Threading.Timer _timer;

    private readonly object _operationSync = new object();
    private readonly object _stateSync = new object();

    private Dictionary<int, ushort> _actions =
        new Dictionary<int, ushort>();

    private byte[]? _lastTable;
    private uint _codeCaveAddress;

    private int _pollBusy;
    private bool _disposed;

    private uint _ionazunRepeatCaveAddress;
    private string _lastStatus = "";

    internal event Action<string>? StatusChanged;


    internal ActionAutoRestoreService()
    {
        _timer = new System.Threading.Timer(Poll, null, 500, 500);
    }

    internal static uint ReadShared32(uint address)
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            return SharedPine.Read32(address);
        }
    }

    internal static void WriteShared32(uint address, uint value)
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            SharedPine.Write32(address, value);
        }
    }

    internal static uint GetSharedStatus()
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            return SharedPine.GetStatus();
        }
    }

    private static bool IsLegacyPatch(uint instruction)
    {
        return (instruction & 0xFFFF0000u) == LegacyPatchBase;
    }

    private static bool IsDispatcherCall(uint instruction)
    {
        if ((instruction >> 26) != 3u)
            return false;

        uint target = (instruction & 0x03FFFFFFu) << 2;

        return target >= CaveSearchEnd &&
               target <= CaveSearchStart;
    }

    private static uint DecodeDispatcherAddress(uint instruction)
    {
        return (instruction & 0x03FFFFFFu) << 2;
    }

    private static uint EncodeJal(uint address)
    {
        if ((address & 0xF0000000u) !=
            ((PatchAddress + 4u) & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "ディスパッチャーのアドレスがJALの到達範囲外です。");
        }

        return 0x0C000000u | ((address >> 2) & 0x03FFFFFFu);
    }

    internal bool TryApply(
        IEnumerable<KeyValuePair<int, ushort>> monsterActions,
        out string message)
    {
        message = "";

        if (monsterActions == null)
        {
            message = "モンスター別特技設定がありません。";
            return false;
        }

        var copy = new Dictionary<int, ushort>();

        foreach (var pair in monsterActions)
            copy[pair.Key] = pair.Value;

        lock (_stateSync)
            _actions = copy;

        lock (_operationSync)
        {
            try
            {
                if (!_pine.TryConnect())
                {
                    message =
                        "設定はアプリ内に保持しました。\n" +
                        "PCSX2のPINE TCP 28011に接続できません。";
                    return false;
                }

                uint status = _pine.GetStatus();

                if (status != 0)
                {
                    message =
                        "設定は保持しました。\n" +
                        "PCSX2をゲーム実行状態にしてください。";
                    return false;
                }

                ApplyRuntime(copy);

                if (copy.Count == 0)
                {
                    message =
                        "種族別特技設定をすべて解除しました。\n" +
                        "元の特技処理命令へ復元しました。";
                    RaiseStatus("種族別特技設定なし：元の命令へ復元済みです。");
                    return true;
                }

                message =
                    "種族別特技設定をPCSX2へ適用しました。\n" +
                    "設定種族数: " + copy.Count + "\n" +
                    "種族IDはモンスター一覧番号+1で照合します。\n" +
                    "個体データは変更していません。\n" +
                    "実際の特技動作はゲーム内で確認してください。";

                RaiseStatus(
                    "種族別特技ディスパッチャー適用中：" +
                    copy.Count + "種類");

                return true;
            }
            catch (Exception ex)
            {
                message =
                    "種族別特技の適用に失敗しました。\n" +
                    ex.Message;
                return false;
            }
        }
    }

    internal bool IsIonazunForceEnabled(int monsterId)
    {
        return Torneko4ActionStore.GetIonazunForce(monsterId);
    }

    internal bool SetIonazunForce(
        int monsterId,
        bool enabled,
        out string message)
    {
        Torneko4ActionStore.SetIonazunForce(monsterId, enabled);
        return TryApply(Torneko4ActionStore.GetCopy(), out message);
    }
    private static bool IsIonazunRepeatJump(uint instruction)
    {
        return (instruction & 0xFC000000u) == 0x08000000u;
    }

    private static uint DecodeIonazunRepeatJump(uint instruction)
    {
        return (instruction & 0x03FFFFFFu) << 2;
    }

    private static uint EncodeIonazunRepeatJump(
        uint fromAddress,
        uint targetAddress)
    {
        if (((fromAddress + 4u) & 0xF0000000u) !=
            (targetAddress & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "イオナズン分岐フックのジャンプ先が範囲外です。");
        }

        return 0x08000000u |
            ((targetAddress >> 2) & 0x03FFFFFFu);
    }

    private uint FindIonazunRepeatCave()
    {
        for (
            uint candidate = IonazunRepeatCaveStart;
            candidate >= IonazunRepeatCaveEnd;
            candidate -= IonazunRepeatCaveStep)
        {
            bool free = true;

            for (
                uint offset = 0;
                offset < IonazunRepeatCaveSize;
                offset += 4u)
            {
                if (_pine.Read32(candidate + offset) != 0u)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "イオナズン分岐フック用の空き領域が見つかりません。");
    }

    private uint[] BuildIonazunRepeatCode(
        uint caveAddress,
        uint dispatcherAddress)
    {
        uint traceAddress = dispatcherAddress + 0x80u;
        uint flagTableAddress = caveAddress + 0x100u;

        return new uint[]
        {
            0x27BDFFF0u,
            0xAFA80000u,
            0xAFA90004u,
            0xAFAA0008u,

            // 現在行動中の種族ID（1始まり）
            0x3C080000u | ((traceAddress >> 16) & 0xFFFFu),
            0x35080000u | (traceAddress & 0xFFFFu),
            0x8D090004u,

            // 種族別イオナズンON/OFFテーブル
            0x3C080000u | ((flagTableAddress >> 16) & 0xFFFFu),
            0x35080000u | (flagTableAddress & 0xFFFFu),
            0x01094021u,
            0x910A0000u,

            // OFFなら元の使用済み判定を維持。ONならv0=0にする。
            0x11400002u,
            0x00000000u,
            0x00001021u,

            0x8FAA0008u,
            0x8FA90004u,
            0x8FA80000u,
            0x27BD0010u,

            // 元の bne v0,zero,0x00229160
            0x14400003u,
            0x00000000u,

            EncodeIonazunRepeatJump(
                caveAddress + 20u * 4u,
                0x00228DD8u),
            0x00000000u,

            EncodeIonazunRepeatJump(
                caveAddress + 22u * 4u,
                0x00229160u),
            0x00000000u
        };
    }

    private uint[] BuildIonazunCompareCode(
        uint caveAddress,
        uint dispatcherAddress)
    {
        uint routineAddress = caveAddress + 0x80u;
        uint traceAddress = dispatcherAddress + 0x80u;
        uint flagTableAddress = caveAddress + 0x100u;
        uint restoreAddress = routineAddress + 18u * 4u;

        return new uint[]
        {
            // t0,t1,t2を退避
            0x27BDFFF0u,
            0xAFA80000u,
            0xAFA90004u,
            0xAFAA0008u,

            // 現在行動中の種族ID
            0x3C080000u | ((traceAddress >> 16) & 0xFFFFu),
            0x35080000u | (traceAddress & 0xFFFFu),
            0x8D090004u,

            // 種族別ON/OFFテーブルを参照
            0x3C080000u | ((flagTableAddress >> 16) & 0xFFFFu),
            0x35080000u | (flagTableAddress & 0xFFFFu),
            0x01094021u,
            0x910A0000u,

            // OFFなら元のslt、ONならv0=1
            0x11400004u,
            0x00000000u,
            0x24020001u,
            EncodeIonazunRepeatJump(
                routineAddress + 14u * 4u,
                restoreAddress),
            0x00000000u,

            0x0043102Au,
            0x00000000u,

            // レジスタ復元
            0x8FAA0008u,
            0x8FA90004u,
            0x8FA80000u,
            0x27BD0010u,

            // 元の比較命令の次（0x00228DEC）へ戻る
            EncodeIonazunRepeatJump(
                routineAddress + 22u * 4u,
                0x00228DECu),
            0x00000000u
        };
    }

    private byte[] BuildIonazunFlagTable(
        Dictionary<int, ushort> actions)
    {
        var table = new byte[TableSize];
        var enabled = Torneko4ActionStore.GetIonazunForceCopy();

        foreach (var pair in actions)
        {
            if (pair.Key < 0 || pair.Key >= 199)
                continue;

            // チェックONかつ、その種族の行動設定がコロマージ魔法(BF)の場合のみON。
            if (pair.Value == 0x00BF && enabled.Contains(pair.Key))
                table[pair.Key + 1] = 1;
        }

        return table;
    }

    private void WriteIonazunFlagTable(uint address, byte[] table)
    {
        for (int i = 0; i < table.Length; i += 4)
        {
            uint value =
                (uint)table[i] |
                ((uint)table[i + 1] << 8) |
                ((uint)table[i + 2] << 16) |
                ((uint)table[i + 3] << 24);
            _pine.Write32(address + (uint)i, value);
        }

        for (int i = 0; i < table.Length; i += 4)
        {
            uint expected =
                (uint)table[i] |
                ((uint)table[i + 1] << 8) |
                ((uint)table[i + 2] << 16) |
                ((uint)table[i + 3] << 24);
            if (_pine.Read32(address + (uint)i) != expected)
                throw new InvalidOperationException(
                    "種族別イオナズン設定テーブルの検証に失敗しました。");
        }
    }

    private void ApplyIonazunRepeatPatch(
        Dictionary<int, ushort> actions)
    {
        byte[] flags = BuildIonazunFlagTable(actions);
        bool shouldPatch = Array.Exists(flags, x => x != 0);

        uint currentUsed = _pine.Read32(IonazunUsedBranchAddress);
        uint currentCompare = _pine.Read32(IonazunCompareAddress);

        if (!shouldPatch)
        {
            RestoreIonazunRepeatPatch();
            return;
        }

        if (_codeCaveAddress == 0)
            throw new InvalidOperationException(
                "種族別特技ディスパッチャーが未設置です。");

        uint caveAddress;
        bool newCave = false;

        if (currentUsed == IonazunUsedBranchOriginal)
        {
            caveAddress = FindIonazunRepeatCave();
            newCave = true;
        }
        else if (IsIonazunRepeatJump(currentUsed))
        {
            caveAddress = DecodeIonazunRepeatJump(currentUsed);
            if (caveAddress < IonazunRepeatCaveEnd ||
                caveAddress > IonazunRepeatCaveStart)
                throw new InvalidOperationException(
                    "イオナズン分岐フックのジャンプ先が想定範囲外です。");

            // 旧版は0x100 byteだけを空き領域として確保していた。
            // 拡張する後半0x100 byteに未知のデータがあれば絶対に上書きしない。
            bool tailSafe = true;
            for (uint offset = 0x100u; offset < 0x200u; offset += 4u)
            {
                uint word = _pine.Read32(caveAddress + offset);
                for (int b = 0; b < 4; b++)
                {
                    byte value = (byte)(word >> (b * 8));
                    if (value > 1)
                        tailSafe = false;
                }
            }
            if (!tailSafe)
                throw new InvalidOperationException(
                    "既存イオナズンケーブの後半0x100 byteに未知のデータがあります。\n" +
                    "安全のため上書きを中止しました。ゲームを再起動してから再適用してください。");
        }
        else
        {
            throw new InvalidOperationException(
                "イオナズン使用済み判定命令が想定外です。\n" +
                "現在値: 0x" + currentUsed.ToString("X8"));
        }

        uint compareTarget = 0;
        if (currentCompare != IonazunCompareOriginal &&
            currentCompare != IonazunCompareForced)
        {
            if (!IsIonazunRepeatJump(currentCompare))
                throw new InvalidOperationException(
                    "イオナズン確率判定命令が想定外です。\n" +
                    "現在値: 0x" + currentCompare.ToString("X8"));

            compareTarget = DecodeIonazunRepeatJump(currentCompare);
            if (compareTarget != caveAddress + 0x80u)
                throw new InvalidOperationException(
                    "確率判定フックが別のケーブを参照しています。");
        }

        uint[] repeatCode = BuildIonazunRepeatCode(
            caveAddress,
            _codeCaveAddress);
        uint[] compareCode = BuildIonazunCompareCode(
            caveAddress,
            _codeCaveAddress);

        for (int i = 0; i < repeatCode.Length; i++)
            _pine.Write32(caveAddress + (uint)(i * 4), repeatCode[i]);

        for (int i = 0; i < compareCode.Length; i++)
            _pine.Write32(caveAddress + 0x80u + (uint)(i * 4), compareCode[i]);

        WriteIonazunFlagTable(caveAddress + 0x100u, flags);

        for (int i = 0; i < repeatCode.Length; i++)
            if (_pine.Read32(caveAddress + (uint)(i * 4)) != repeatCode[i])
                throw new InvalidOperationException("イオナズン連発コードの検証に失敗しました。");

        for (int i = 0; i < compareCode.Length; i++)
            if (_pine.Read32(caveAddress + 0x80u + (uint)(i * 4)) != compareCode[i])
                throw new InvalidOperationException("イオナズン確率判定コードの検証に失敗しました。");

        uint usedJump = EncodeIonazunRepeatJump(
            IonazunUsedBranchAddress,
            caveAddress);
        uint compareJump = EncodeIonazunRepeatJump(
            IonazunCompareAddress,
            caveAddress + 0x80u);

        if (currentUsed == IonazunUsedBranchOriginal || newCave)
        {
            _pine.Write32(IonazunUsedBranchAddress, usedJump);
            if (_pine.Read32(IonazunUsedBranchAddress) != usedJump)
                throw new InvalidOperationException("イオナズン使用済み判定フックの設置に失敗しました。");
        }

        if (currentCompare != compareJump)
        {
            if (currentCompare != IonazunCompareOriginal &&
                currentCompare != IonazunCompareForced)
                throw new InvalidOperationException("確率判定フックの設置前検証に失敗しました。");

            _pine.Write32(IonazunCompareAddress, compareJump);
            if (_pine.Read32(IonazunCompareAddress) != compareJump)
                throw new InvalidOperationException("イオナズン確率判定フックの設置に失敗しました。");
        }

        _ionazunRepeatCaveAddress = caveAddress;
    }

    private void RestoreIonazunRepeatPatch()
    {
        uint currentUsed = _pine.Read32(IonazunUsedBranchAddress);
        if (currentUsed != IonazunUsedBranchOriginal)
        {
            if (!IsIonazunRepeatJump(currentUsed))
                throw new InvalidOperationException(
                    "イオナズン使用済み判定の命令が想定外です。\n現在値: 0x" +
                    currentUsed.ToString("X8"));

            uint usedCave = DecodeIonazunRepeatJump(currentUsed);
            if (usedCave < IonazunRepeatCaveEnd ||
                usedCave > IonazunRepeatCaveStart)
                throw new InvalidOperationException(
                    "別のフックと思われるため使用済み判定の復元を中止しました。");

            _pine.Write32(IonazunUsedBranchAddress, IonazunUsedBranchOriginal);
            if (_pine.Read32(IonazunUsedBranchAddress) != IonazunUsedBranchOriginal)
                throw new InvalidOperationException("使用済み判定の復元に失敗しました。");
        }

        uint currentCompare = _pine.Read32(IonazunCompareAddress);
        if (currentCompare != IonazunCompareOriginal)
        {
            bool known = currentCompare == IonazunCompareForced;
            if (IsIonazunRepeatJump(currentCompare))
            {
                uint target = DecodeIonazunRepeatJump(currentCompare);
                known = target >= IonazunRepeatCaveEnd &&
                    target <= IonazunRepeatCaveStart + 0x80u;
            }

            if (!known)
                throw new InvalidOperationException(
                    "イオナズン確率判定の命令が想定外です。\n現在値: 0x" +
                    currentCompare.ToString("X8"));

            _pine.Write32(IonazunCompareAddress, IonazunCompareOriginal);
            if (_pine.Read32(IonazunCompareAddress) != IonazunCompareOriginal)
                throw new InvalidOperationException("確率判定の復元に失敗しました。");
        }

        _ionazunRepeatCaveAddress = 0;
    }
    private byte[] BuildActionTable(
        Dictionary<int, ushort> actions)
    {
        var table = new byte[TableSize];

        foreach (var pair in actions)
        {
            if (pair.Key < 0 || pair.Key >= 255)
            {
                throw new InvalidOperationException(
                    "モンスター一覧番号が範囲外です: " + pair.Key);
            }

            if (pair.Value == 0 || pair.Value > 0xFF)
            {
                throw new InvalidOperationException(
                    "特技IDが範囲外です: " + pair.Value);
            }

            bool knownAction = Array.Exists(
                ActionChoices,
                x => x.Id == pair.Value);

            if (!knownAction)
            {
                throw new InvalidOperationException(
                    "未登録の特技IDです: 0x" +
                    pair.Value.ToString("X2"));
            }

            // C#の一覧番号は0始まり。
            // ゲーム内の種族IDは一覧番号+1。
            int speciesId = pair.Key + 1;

            table[speciesId] = (byte)pair.Value;
        }

        return table;
    }

    private static bool TablesEqual(byte[]? a, byte[] b)
    {
        if (a == null || a.Length != b.Length)
            return false;

        for (int i = 0; i < b.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private bool IsDispatcherCode(uint address)
    {
        uint tableAddress = address + TableOffset;
        uint traceAddress = address + 0x80u;

        uint[] expected =
        {
            0x90480006u,

            0x3C0B0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x356B0000u | (traceAddress & 0xFFFFu),
            0xAD710000u,
            0xAD680004u,
            0x8D6C0008u,
            0x258C0001u,
            0xAD6C0008u,

            0x3C090000u | ((tableAddress >> 16) & 0xFFFFu),
            0x35290000u | (tableAddress & 0xFFFFu),
            0x01284821u,
            0x912A0000u,
            0x11400002u,
            0x00000000u,
            0x01408821u,
            0x0808F1D4u,
            0x00000000u
        };

        for (int i = 0; i < expected.Length; i++)
        {
            if (_pine.Read32(address + (uint)(i * 4)) != expected[i])
                return false;
        }

        return true;
    }

    private uint FindCodeCave()
    {
        for (
            uint candidate = CaveSearchStart;
            candidate >= CaveSearchEnd;
            candidate -= CaveSearchStep)
        {
            bool free = true;

            for (uint offset = 0; offset < CaveSize; offset += 4)
            {
                if (_pine.Read32(candidate + offset) != 0)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "EE RAMの指定範囲に512バイトの空き領域が見つかりません。\n" +
            "安全確認のため、コードを書き込みませんでした。");
    }

    private void WriteDispatcherCode(uint address)
    {
        uint tableAddress = address + TableOffset;
        uint traceAddress = address + 0x80u;

        uint[] code =
        {
            // lbu t0,6(v0) : 種族ID
            0x90480006u,

            // 記録領域のアドレスをt3へ設定
            0x3C0B0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x356B0000u | (traceAddress & 0xFFFFu),

            // 元の特技ID s1 と種族ID t0 を保存
            0xAD710000u,
            0xAD680004u,

            // 記録回数を1増やす
            0x8D6C0008u,
            0x258C0001u,
            0xAD6C0008u,

            // 特技テーブルのアドレス
            0x3C090000u | ((tableAddress >> 16) & 0xFFFFu),
            0x35290000u | (tableAddress & 0xFFFFu),

            // 種族IDを使って特技テーブルを検索
            0x01284821u,
            0x912A0000u,

            // 設定があれば特技IDを上書き
            0x11400002u,
            0x00000000u,
            0x01408821u,

            // 元の敵特技処理へ戻る
            0x0808F1D4u,
            0x00000000u
        };

        for (int i = 0; i < code.Length; i++)
            _pine.Write32(address + (uint)(i * 4), code[i]);

        _pine.Write32(traceAddress, 0u);
        _pine.Write32(traceAddress + 4u, 0u);
        _pine.Write32(traceAddress + 8u, 0u);

        if (!IsDispatcherCode(address))
        {
            throw new InvalidOperationException(
                "MIPSディスパッチャーの書き込み検証に失敗しました。");
        }
    }

    private void WriteActionTable(uint address, byte[] table)
    {
        for (int i = 0; i < table.Length; i += 4)
        {
            uint value =
                (uint)table[i] |
                ((uint)table[i + 1] << 8) |
                ((uint)table[i + 2] << 16) |
                ((uint)table[i + 3] << 24);

            _pine.Write32(address + (uint)i, value);
        }

        for (int i = 0; i < table.Length; i += 4)
        {
            uint expected =
                (uint)table[i] |
                ((uint)table[i + 1] << 8) |
                ((uint)table[i + 2] << 16) |
                ((uint)table[i + 3] << 24);

            uint actual = _pine.Read32(address + (uint)i);

            if (actual != expected)
            {
                throw new InvalidOperationException(
                    "種族別特技テーブルの検証に失敗しました。位置: 0x" +
                    (address + (uint)i).ToString("X8"));
            }
        }
    }

    private void RestoreCallInstruction()
    {
        uint current = _pine.Read32(PatchAddress);

        if (current == OriginalCallInstruction)
            return;

        if (!IsDispatcherCall(current))
        {
            throw new InvalidOperationException(
                "呼び出し命令が想定外のため復元を中止しました: 0x" +
                current.ToString("X8"));
        }

        _pine.Write32(PatchAddress, OriginalCallInstruction);

        uint verify = _pine.Read32(PatchAddress);

        if (verify != OriginalCallInstruction)
        {
            throw new InvalidOperationException(
                "元の呼び出し命令への復元に失敗しました。");
        }
    }

    private void ApplyRuntime(Dictionary<int, ushort> actions)
    {
        uint currentCall = _pine.Read32(PatchAddress);

        bool currentIsDispatcher = IsDispatcherCall(currentCall);

        if (currentCall != OriginalCallInstruction &&
            !currentIsDispatcher)
        {
            throw new InvalidOperationException(
                "敵特技呼び出し命令が想定外です。\n" +
                "現在値: 0x" + currentCall.ToString("X8"));
        }

        uint delayInstruction = _pine.Read32(DelaySlotAddress);

        if (IsLegacyPatch(delayInstruction))
        {
            _pine.Write32(
                DelaySlotAddress,
                OriginalDelayInstruction);

            delayInstruction = _pine.Read32(DelaySlotAddress);
        }

        if (delayInstruction != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "遅延スロットが想定外です。\n" +
                "現在値: 0x" + delayInstruction.ToString("X8"));
        }

        if (currentIsDispatcher)
        {
            uint detectedCave = DecodeDispatcherAddress(currentCall);

            if (!IsDispatcherCode(detectedCave))
            {
                RestoreCallInstruction();

                throw new InvalidOperationException(
                    "既存ディスパッチャーのコード検証に失敗しました。\n" +
                    "安全のため元の呼び出し命令へ戻しました。");
            }

            _codeCaveAddress = detectedCave;
        }

        if (actions.Count == 0)
        {
            RestoreIonazunRepeatPatch();

            if (currentIsDispatcher)
                RestoreCallInstruction();

            return;
        }

        byte[] table = BuildActionTable(actions);

        if (currentIsDispatcher && TablesEqual(_lastTable, table))
        {
            ApplyIonazunRepeatPatch(actions);
            return;
        }

        if (currentIsDispatcher)
            RestoreCallInstruction();

        bool newCave = false;

        if (_codeCaveAddress == 0 ||
            !IsDispatcherCode(_codeCaveAddress))
        {
            _codeCaveAddress = FindCodeCave();
            WriteDispatcherCode(_codeCaveAddress);
            newCave = true;
        }

        uint tableAddress = _codeCaveAddress + TableOffset;

        if (newCave || !TablesEqual(_lastTable, table))
        {
            WriteActionTable(tableAddress, table);
            _lastTable = (byte[])table.Clone();
        }

        uint newCall = EncodeJal(_codeCaveAddress);

        _pine.Write32(PatchAddress, newCall);

        uint verifyCall = _pine.Read32(PatchAddress);

        if (verifyCall != newCall)
        {
            throw new InvalidOperationException(
                "ディスパッチャー呼び出し命令の検証に失敗しました。\n" +
                "書き込み値: 0x" + newCall.ToString("X8") +
                "\n読み戻し値: 0x" + verifyCall.ToString("X8"));
        }

        ApplyIonazunRepeatPatch(actions);
    }

    internal static string ReadActionTrace()
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            uint call = SharedPine.Read32(PatchAddress);

            if ((call >> 26) != 3u)
            {
                throw new InvalidOperationException(
                    "敵特技フックが見つかりません。特技設定を適用してください。");
            }

            uint cave = (call & 0x03FFFFFFu) << 2;

            if (cave < CaveSearchEnd || cave > CaveSearchStart)
            {
                throw new InvalidOperationException(
                    "コードケーブのアドレスが範囲外です。");
            }

            uint traceAddress = cave + 0x80u;
            uint actionId = SharedPine.Read32(traceAddress);
            uint speciesId = SharedPine.Read32(traceAddress + 4u);
            uint count = SharedPine.Read32(traceAddress + 8u);

            string[] lines =
            {
                "ENEMY ACTION TRACE",
                "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "Read only: YES",
                "Hook address: 0x" + PatchAddress.ToString("X8"),
                "Code cave: 0x" + cave.ToString("X8"),
                "Trace address: 0x" + traceAddress.ToString("X8"),
                "",
                "Recorded calls: " + count.ToString(),
                "Species ID: " + speciesId.ToString(),
                "Action ID: 0x" + (actionId & 0xFFFFu).ToString("X4"),
                "Action ID signed: " + unchecked((short)(actionId & 0xFFFFu)).ToString(),
                "",
                "Species ID is the runtime ID used by the game.",
                "Action ID is captured before the per-species override."
            };

            return string.Join(Environment.NewLine, lines);
        }
    }
    private static bool IsSpellTraceJump(uint instruction)
    {
        return (instruction & 0xFC000000u) == 0x08000000u;
    }

    private static uint DecodeSpellTraceJump(uint instruction)
    {
        return (instruction & 0x03FFFFFFu) << 2;
    }

    private static uint EncodeSpellTraceJump(uint address)
    {
        if ((address & 0xF0000000u) !=
            ((SpellDispatchAddress + 4u) & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "呪文分岐フックのジャンプ先が範囲外です。");
        }

        return 0x08000000u | ((address >> 2) & 0x03FFFFFFu);
    }

    private static uint[] BuildSpellTraceCode(uint caveAddress)
    {
        uint traceAddress = caveAddress + SpellTraceDataOffset;

        return new uint[]
        {
            0x27BDFFF0u,
            0xAFA80000u,
            0xAFA90004u,
            0xAFBA0008u,
            0xAFBB000Cu,

            0x3C1A0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x375A0000u | (traceAddress & 0xFFFFu),

            0xAF500000u,
            0xAF510004u,
            0xAF520008u,
            0xAF53000Cu,
            0xAF540010u,
            0xAF5F0014u,

            0x8F5B0018u,
            0x277B0001u,
            0xAF5B0018u,

            0x8FA80000u,
            0x8FA90004u,
            0x8FBA0008u,
            0x8FBB000Cu,
            0x27BD0010u,

            // 元の slti v0,s0,70
            0x2A020046u,

            // 条件分岐。遅延スロットも元の命令を再現する。
            0x10400004u,
            0x24020048u,

            // 条件成立側：元の 0x002CF60C へ戻る
            0x08000000u | ((0x002CF60Cu >> 2) & 0x03FFFFFFu),
            0x00000000u,

            // 条件不成立側：元の 0x002CF654 へ戻る
            0x08000000u | ((0x002CF654u >> 2) & 0x03FFFFFFu),
            0x00000000u
        };
    }

    private static bool IsSpellTraceCode(uint caveAddress)
    {
        uint[] expected = BuildSpellTraceCode(caveAddress);

        for (int i = 0; i < expected.Length; i++)
        {
            uint actual = SharedPine.Read32(
                caveAddress + (uint)(i * 4));

            if (actual != expected[i])
                return false;
        }

        return true;
    }

    private static uint FindSpellTraceCave()
    {
        for (
            uint candidate = SpellTraceCaveSearchStart;
            candidate >= SpellTraceCaveSearchEnd;
            candidate -= SpellTraceCaveSearchStep)
        {
            bool free = true;

            for (
                uint offset = 0;
                offset < SpellTraceCaveSize;
                offset += 4)
            {
                if (SharedPine.Read32(candidate + offset) != 0)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "呪文分岐調査用の空き領域が見つかりません。\n" +
            "安全のためメモリへの書き込みを中止しました。");
    }

    private static void WriteSpellTraceCode(uint caveAddress)
    {
        uint[] code = BuildSpellTraceCode(caveAddress);

        for (int i = 0; i < code.Length; i++)
        {
            SharedPine.Write32(
                caveAddress + (uint)(i * 4),
                code[i]);
        }

        uint traceAddress = caveAddress + SpellTraceDataOffset;

        for (uint offset = 0; offset < 28u; offset += 4u)
            SharedPine.Write32(traceAddress + offset, 0u);

        if (!IsSpellTraceCode(caveAddress))
        {
            throw new InvalidOperationException(
                "呪文分岐調査コードの書き込み検証に失敗しました。");
        }
    }

    internal static string InstallSpellDispatchTrace()
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            uint status = SharedPine.GetStatus();

            if (status != 1u)
            {
                throw new InvalidOperationException(
                    "PCSX2を一時停止してから設置してください。");
            }

            uint currentCall =
                SharedPine.Read32(SpellDispatchAddress);

            uint currentDelay =
                SharedPine.Read32(SpellDispatchDelayAddress);

            if (IsSpellTraceJump(currentCall))
            {
                uint detectedCave = DecodeSpellTraceJump(currentCall);

                if (detectedCave < SpellTraceCaveSearchEnd ||
                    detectedCave > SpellTraceCaveSearchStart ||
                    currentDelay != 0u ||
                    !IsSpellTraceCode(detectedCave))
                {
                    throw new InvalidOperationException(
                        "既存の呪文分岐フックを検証できません。");
                }

                _spellTraceCaveAddress = detectedCave;

                return
                    "呪文分岐フックは既に設置されています。\n" +
                    "コードケーブ: 0x" + detectedCave.ToString("X8");
            }

            if (currentCall != SpellDispatchOriginalInstruction)
            {
                throw new InvalidOperationException(
                    "0x002CF600の命令が想定値と異なります。\n" +
                    "現在値: 0x" + currentCall.ToString("X8"));
            }

            if (currentDelay != SpellDispatchOriginalDelay)
            {
                throw new InvalidOperationException(
                    "0x002CF604の命令が想定値と異なります。\n" +
                    "現在値: 0x" + currentDelay.ToString("X8"));
            }

            uint caveAddress = FindSpellTraceCave();

            WriteSpellTraceCode(caveAddress);

            uint traceAddress = caveAddress + SpellTraceDataOffset;

            uint verifyS0 = SharedPine.Read32(traceAddress);
            uint verifyCount = SharedPine.Read32(traceAddress + 24u);

            if (verifyS0 != 0u || verifyCount != 0u)
            {
                throw new InvalidOperationException(
                    "記録領域の初期化検証に失敗しました。");
            }

            uint jumpInstruction =
                EncodeSpellTraceJump(caveAddress);

            SharedPine.Write32(
                SpellDispatchDelayAddress,
                0u);

            if (SharedPine.Read32(SpellDispatchDelayAddress) != 0u)
            {
                SharedPine.Write32(
                    SpellDispatchDelayAddress,
                    SpellDispatchOriginalDelay);

                throw new InvalidOperationException(
                    "分岐遅延スロットの書き込みに失敗しました。");
            }

            try
            {
                SharedPine.Write32(
                    SpellDispatchAddress,
                    jumpInstruction);

                uint verifyCall =
                    SharedPine.Read32(SpellDispatchAddress);

                if (verifyCall != jumpInstruction)
                {
                    throw new InvalidOperationException(
                        "呪文分岐フックの設置検証に失敗しました。");
                }
            }
            catch
            {
                SharedPine.Write32(
                    SpellDispatchAddress,
                    SpellDispatchOriginalInstruction);

                SharedPine.Write32(
                    SpellDispatchDelayAddress,
                    SpellDispatchOriginalDelay);

                throw;
            }

            _spellTraceCaveAddress = caveAddress;

            return
                "呪文分岐フックを設置しました。\n" +
                "フック: 0x" + SpellDispatchAddress.ToString("X8") + "\n" +
                "コードケーブ: 0x" + caveAddress.ToString("X8") + "\n" +
                "記録領域: 0x" + traceAddress.ToString("X8") + "\n\n" +
                "PCSX2を再開してコロマージに呪文を使わせてください。";
        }
    }

    internal static string ReadSpellDispatchTrace()
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            if (SharedPine.GetStatus() != 1u)
            {
                throw new InvalidOperationException(
                    "記録を読む前にPCSX2を一時停止してください。");
            }

            uint call = SharedPine.Read32(SpellDispatchAddress);

            if (!IsSpellTraceJump(call))
            {
                throw new InvalidOperationException(
                    "呪文分岐フックが設置されていません。");
            }

            uint caveAddress = DecodeSpellTraceJump(call);

            if (caveAddress < SpellTraceCaveSearchEnd ||
                caveAddress > SpellTraceCaveSearchStart)
            {
                throw new InvalidOperationException(
                    "呪文分岐コードケーブのアドレスが範囲外です。");
            }

            if (SharedPine.Read32(SpellDispatchDelayAddress) != 0u ||
                !IsSpellTraceCode(caveAddress))
            {
                throw new InvalidOperationException(
                    "呪文分岐フックのコード検証に失敗しました。");
            }

            uint traceAddress = caveAddress + SpellTraceDataOffset;

            uint s0 = SharedPine.Read32(traceAddress);
            uint s1 = SharedPine.Read32(traceAddress + 4u);
            uint s2 = SharedPine.Read32(traceAddress + 8u);
            uint s3 = SharedPine.Read32(traceAddress + 12u);
            uint s4 = SharedPine.Read32(traceAddress + 16u);
            uint ra = SharedPine.Read32(traceAddress + 20u);
            uint count = SharedPine.Read32(traceAddress + 24u);

            string[] lines =
            {
                "COROMAGE SPELL DISPATCH TRACE",
                "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                "Read only: YES",
                "Hook address: 0x" + SpellDispatchAddress.ToString("X8"),
                "Code cave: 0x" + caveAddress.ToString("X8"),
                "Trace address: 0x" + traceAddress.ToString("X8"),
                "",
                "Recorded calls: " + count.ToString(),
                "s0: " + s0.ToString() + " (0x" + s0.ToString("X8") + ")",
                "s1: " + s1.ToString() + " (0x" + s1.ToString("X8") + ")",
                "s2: " + s2.ToString() + " (0x" + s2.ToString("X8") + ")",
                "s3: " + s3.ToString() + " (0x" + s3.ToString("X8") + ")",
                "s4: " + s4.ToString() + " (0x" + s4.ToString("X8") + ")",
                "ra: 0x" + ra.ToString("X8"),
                "",
                "s0 is the primary spell-dispatch candidate.",
                "Values are the latest captured register state."
            };

            return string.Join(Environment.NewLine, lines);
        }
    }

    internal static string RestoreSpellDispatchTrace()
    {
        lock (SharedPineAccessSync)
        {
            if (!SharedPine.TryConnect())
            {
                throw new InvalidOperationException(
                    "PCSX2 PINE TCP 28011に接続できません。");
            }

            if (SharedPine.GetStatus() != 1u)
            {
                throw new InvalidOperationException(
                    "PCSX2を一時停止してから解除してください。");
            }

            uint currentCall =
                SharedPine.Read32(SpellDispatchAddress);

            uint currentDelay =
                SharedPine.Read32(SpellDispatchDelayAddress);

            if (currentCall == SpellDispatchOriginalInstruction &&
                currentDelay == SpellDispatchOriginalDelay)
            {
                return "呪文分岐フックは既に解除されています。";
            }

            if (currentCall != SpellDispatchOriginalInstruction &&
                !IsSpellTraceJump(currentCall))
            {
                throw new InvalidOperationException(
                    "フック命令が想定外のため解除を中止しました。\n" +
                    "現在値: 0x" + currentCall.ToString("X8"));
            }

            if (IsSpellTraceJump(currentCall))
            {
                uint caveAddress = DecodeSpellTraceJump(currentCall);

                if (caveAddress < SpellTraceCaveSearchEnd ||
                    caveAddress > SpellTraceCaveSearchStart ||
                    !IsSpellTraceCode(caveAddress))
                {
                    throw new InvalidOperationException(
                        "コードケーブを検証できないため解除を中止しました。");
                }

                if (currentDelay != 0u)
                {
                    throw new InvalidOperationException(
                        "遅延スロットが想定値と異なるため解除を中止しました。");
                }
            }

            SharedPine.Write32(
                SpellDispatchAddress,
                SpellDispatchOriginalInstruction);

            if (SharedPine.Read32(SpellDispatchAddress) !=
                SpellDispatchOriginalInstruction)
            {
                throw new InvalidOperationException(
                    "元の分岐命令への復元に失敗しました。");
            }

            SharedPine.Write32(
                SpellDispatchDelayAddress,
                SpellDispatchOriginalDelay);

            if (SharedPine.Read32(SpellDispatchDelayAddress) !=
                SpellDispatchOriginalDelay)
            {
                throw new InvalidOperationException(
                    "元の遅延スロットへの復元に失敗しました。");
            }

            _spellTraceCaveAddress = 0u;

            return
                "呪文分岐フックを解除しました。\n" +
                "元の2命令を復元しました。";
        }
    }

    private void Poll(object? state)
    {
        if (_disposed ||
            Interlocked.Exchange(ref _pollBusy, 1) != 0)
            return;

        try
        {
            lock (_operationSync)
            {
                Dictionary<int, ushort> snapshot;

                lock (_stateSync)
                {
                    snapshot = new Dictionary<int, ushort>(_actions);
                }

                if (snapshot.Count == 0)
                {
                    RaiseStatus(
                        "種族別特技設定なし：元の命令を維持しています。");
                }

                if (snapshot.Count > 0 || _ionazunRepeatCaveAddress != 0)
                {
                    bool connected = _pine.TryConnect();

                    if (!connected)
                    {
                        RaiseStatus(
                            "PINE未接続：PCSX2のTCP 28011を確認してください。");
                    }

                    if (connected)
                    {
                        uint status = _pine.GetStatus();

                        if (status == 0)
                        {
                            ApplyRuntime(snapshot);
                            RaiseStatus(snapshot.Count > 0
                                ? "種族別特技ディスパッチャー適用中：" + snapshot.Count + "種類"
                                : "種族別特技設定なし：元の命令を維持しています。");
                        }

                        if (status == 1)
                        {
                            RaiseStatus(
                                "PCSX2一時停止中：再開後に設定を確認します。");
                        }

                        if (status != 0 && status != 1)
                        {
                            RaiseStatus("PCSX2の仮想マシンが停止しています。");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            RaiseStatus("種族別特技監視エラー: " + ex.Message);
        }

        Interlocked.Exchange(ref _pollBusy, 0);
    }
    private void RaiseStatus(string message)
    {
        if (StatusChanged == null)
            return;

        if (string.Equals(
            _lastStatus,
            message,
            StringComparison.Ordinal))
            return;

        _lastStatus = message;

        try
        {
            StatusChanged?.Invoke(message);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();

        lock (_operationSync)
        {
            try
            {
                if (_pine.TryConnect() && _pine.GetStatus() == 0)
                {
                    RestoreIonazunRepeatPatch();
                    RestoreCallInstruction();
                }
            }
            catch
            {
            }

        }
    }
    private sealed class PineClient : IDisposable
    {
        private const int Port = 28011;

        private readonly object _sync = new object();

        private TcpClient? _client;
        private NetworkStream? _stream;

        internal bool TryConnect()
        {
            lock (_sync)
            {
                if (_client != null && _stream != null && _client.Connected)
                    return true;

                DisconnectCore();

                var client = new TcpClient();

                try
                {
                    IAsyncResult result =
                        client.BeginConnect(IPAddress.Loopback, Port, null, null);

                    using (result.AsyncWaitHandle)
                    {
                        if (!result.AsyncWaitHandle.WaitOne(250))
                        {
                            client.Close();
                            return false;
                        }
                    }

                    client.EndConnect(result);

                    client.ReceiveTimeout = 500;
                    client.SendTimeout = 500;

                    _client = client;
                    _stream = client.GetStream();

                    return true;
                }
                catch
                {
                    client.Close();
                    return false;
                }
            }
        }

        internal uint GetStatus()
        {
            return BitConverter.ToUInt32(
                Request(0x0F, Array.Empty<byte>(), 4), 0);
        }

        internal uint Read32(uint address)
        {
            byte[] args = new byte[4];
            WriteUInt32(args, 0, address);

            return BitConverter.ToUInt32(
                Request(0x02, args, 4), 0);
        }

        internal void Write32(uint address, uint value)
        {
            byte[] args = new byte[8];

            WriteUInt32(args, 0, address);
            WriteUInt32(args, 4, value);

            Request(0x06, args, 0);
        }

        private byte[] Request(
            byte opcode,
            byte[] args,
            int expectedPayloadLength)
        {
            lock (_sync)
            {
                if (_stream == null || _client == null || !_client.Connected)
                    throw new InvalidOperationException("PINE接続がありません。");

                byte[] body = new byte[1 + args.Length];
                body[0] = opcode;

                Array.Copy(args, 0, body, 1, args.Length);

                byte[] packet = new byte[4 + body.Length];

                WriteUInt32(packet, 0, (uint)packet.Length);
                Array.Copy(body, 0, packet, 4, body.Length);

                try
                {
                    _stream.Write(packet, 0, packet.Length);

                    byte[] header = ReadExactly(_stream, 4);
                    uint responseLength = BitConverter.ToUInt32(header, 0);

                    if (responseLength < 5 || responseLength > 1024)
                        throw new InvalidOperationException(
                            "PINE応答サイズが不正です: " + responseLength);

                    byte[] response =
                        ReadExactly(_stream, checked((int)responseLength - 4));

                    if (response[0] != 0)
                        throw new InvalidOperationException(
                            "PCSX2 PINEが要求を拒否しました。");

                    int actualPayloadLength = response.Length - 1;

                    if (actualPayloadLength != expectedPayloadLength)
                        throw new InvalidOperationException(
                            "PINE応答長が一致しません。");

                    byte[] payload = new byte[actualPayloadLength];

                    if (actualPayloadLength > 0)
                        Array.Copy(response, 1, payload, 0, actualPayloadLength);

                    return payload;
                }
                catch
                {
                    DisconnectCore();
                    throw;
                }
            }
        }

        private static byte[] ReadExactly(NetworkStream stream, int length)
        {
            byte[] data = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int read = stream.Read(data, offset, length - offset);

                if (read <= 0)
                    throw new InvalidOperationException(
                        "PINE接続が途中で切断されました。");

                offset += read;
            }

            return data;
        }

        private static void WriteUInt32(
            byte[] data,
            int offset,
            uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        internal void Disconnect()
        {
            lock (_sync)
                DisconnectCore();
        }

        private void DisconnectCore()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }

            _stream = null;
            _client = null;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}

