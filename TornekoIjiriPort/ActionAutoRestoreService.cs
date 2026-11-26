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

    // 味方側AIの特技使用可否判定と行動ID読み出し。
    private const uint AllySkillCheckAddress1 = 0x00241D48u;
    private const uint AllySkillCheckAddress2 = 0x00241DB8u;
    private const uint AllyActionLoadAddress = 0x00241D5Cu;
    private const uint OriginalAllySkillCheckCall = 0x0C08F1D4u;
    private const uint OriginalAllyActionLoad = 0x9102003Au;
    private const uint AllySkillCheckDelay1Address = 0x00241D4Cu;
    private const uint AllySkillCheckDelay2Address = 0x00241DBCu;
    private const uint AllyActionLoadDelayAddress = 0x00241D60u;
    private const uint OriginalAllySkillCheckDelay = 0x9045003Au;
    private const uint OriginalAllyActionLoadDelay = 0x3C03008Eu;

    // 0x002CA3CC内の行動対象判定も同じ種族別アクションIDを参照する。
    private const uint AllyTargetCheckActionLoadAddress = 0x002CA3DCu;
    private const uint OriginalAllyTargetCheckActionLoad = 0x90A2003Au;
    private const uint AllyTargetCheckDelayAddress = 0x002CA3E0u;
    private const uint OriginalAllyTargetCheckDelay = 0x24631FD0u;

    private const uint AllySkillCheckStubOffset = 0x800u;
    private const uint AllyActionLoadStubOffset = 0x880u;
    private const uint AllyTargetCheckStubOffset = 0x940u;
    private const uint AllyHookMarkerOffset = 0xA7Cu;
    private const uint AllyHookMarker = 0x534C4148u;
    private const uint AllyHookReservedSize = 0xA80u;

    // 旧版の全敵共通パッチ ori s1,zero,actionId
    private const uint LegacyPatchBase = 0x34110000u;

    // 共有ランタイム領域は4KBページ単位で一度だけ確保する。
    // スライム指輪のコード・データはページ先頭から0x510まで使用し、
    // 種族別アクションディスパッチャーは0x510以降を使用する。
    private const uint CaveSearchStart = 0x01FF0000u;
    private const uint CaveSearchEnd = 0x01E00000u;
    private const uint SharedRuntimeCaveStep = 0x1000u;
    private const uint SharedRuntimeCaveSize = 0x1000u;
    internal const uint SharedActionCaveOffset = 0x510u;
    private const uint SharedRuntimeCaveMarkerOffset = 0xFFCu;
    private const uint SharedRuntimeCaveMarker = 0x54524E43u;
    private const uint SharedRingCaveMarkerOffset = 0x3F0u;
    private const uint SharedRingCaveMarker = 0x534C5254u;
    private const uint SharedActionCaveMarkerOffset = SharedActionCaveOffset + 0xA7Cu;
    private const uint SharedActionCaveMarker = 0x534C4148u;
    private const uint SharedActionAddressMax = CaveSearchStart + SharedActionCaveOffset;
    private static readonly object SharedRuntimeCaveSync = new object();
    // コロマージ呪文分岐調査フック
    private const uint SpellDispatchAddress = 0x002CF600u;
    // MOD版コロマージ呪文成功判定: slt v0,v0,v1 を li v0,1 に置換
    private const uint IonazunCompareAddress = 0x00228DE8u;
    private const uint IonazunCompareOriginal = 0x0043102Au;

    // 使用済みイオナズン判定の種族別フック
    private const uint IonazunUsedBranchAddress = 0x00228DD0u;
    private const uint IonazunUsedBranchOriginal = 0x144000E3u;
    private const uint IonazunUsedBranchDelayAddress = 0x00228DD4u;
    private const uint IonazunUsedBranchDelayOriginal = 0x24040003u;

    // 確率判定は2命令まとめてフックする
    private const uint IonazunCompareBranchAddress = 0x00228DECu;
    private const uint IonazunCompareBranchOriginal = 0x104000A1u;
    private const uint IonazunCompareBranchDelayAddress = 0x00228DF0u;
    private const uint IonazunCompareBranchDelayOriginal = 0x3C05008Fu;
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

    // 指輪装備中に限り、味方の対象17種へ優先適用する別テーブル。
    private Dictionary<int, ushort> _allyRingActions =
        new Dictionary<int, ushort>();

    private HashSet<int> _allyRingIonazunForce =
        new HashSet<int>();

    // nullなら通常のモンスタークリエイター設定を使用する。
    // 指輪適用中は専用のイオナズン確定設定を使用する。
    private HashSet<int>? _ionazunForceOverride;

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
               target <= SharedActionAddressMax;
    }

    private static uint DecodeDispatcherAddress(uint instruction)
    {
        return (instruction & 0x03FFFFFFu) << 2;
    }

    private static uint EncodeJal(uint address)
    {
        return EncodeJalFrom(PatchAddress, address);
    }

    private static uint EncodeJalFrom(uint instructionAddress, uint targetAddress)
    {
        if (((instructionAddress + 4u) & 0xF0000000u) !=
            (targetAddress & 0xF0000000u))
            throw new InvalidOperationException("JALフックの接続先が到達範囲外です。");
        return 0x0C000000u | ((targetAddress >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJalFrom(uint instructionAddress, uint instruction)
    {
        return ((instructionAddress + 4u) & 0xF0000000u) |
            ((instruction & 0x03FFFFFFu) << 2);
    }

    private static uint EncodeI(int opcode, int rs, int rt, int immediate)
    {
        return ((uint)opcode << 26) | ((uint)rs << 21) |
            ((uint)rt << 16) | ((uint)immediate & 0xFFFFu);
    }

    internal bool TryApply(
        IEnumerable<KeyValuePair<int, ushort>> monsterActions,
        out string message)
    {
        return TryApply(monsterActions, null, out message);
    }

    // 指輪専用設定を共通ストアに書き込まずに適用する。
    internal bool TryApply(
        IEnumerable<KeyValuePair<int, ushort>> monsterActions,
        ISet<int>? ionazunForceOverride,
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
                _ionazunForceOverride =
                    ionazunForceOverride == null
                        ? null
                        : new HashSet<int>(ionazunForceOverride);

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
                    int ringOverrideCount;
                    lock (_stateSync)
                        ringOverrideCount = _allyRingActions.Count;

                    if (ringOverrideCount == 0)
                    {
                        message =
                            "種族別特技設定をすべて解除しました。\n" +
                            "元の特技処理命令へ復元しました。";
                        RaiseStatus("種族別特技設定なし：元の命令へ復元済みです。");
                    }
                    else
                    {
                        message =
                            "モンスタークリエイターの通常設定を解除しました。\n" +
                            "指輪装備中の味方専用上書き " + ringOverrideCount +
                            "種類は引き続き有効です。";
                        RaiseStatus("通常設定なし：指輪専用の味方上書き " +
                            ringOverrideCount + "種類を適用中です。");
                    }
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
                string detail =
                    "発生日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    Environment.NewLine + ex.ToString();

                string logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "トルネコいじり_種族別特技エラー.txt");

                try
                {
                    System.IO.File.WriteAllText(
                        logPath,
                        detail,
                        new System.Text.UTF8Encoding(false));
                }
                catch
                {
                }

                message =
                    "種族別特技の適用に失敗しました。\n" +
                    ex.Message +
                    "\n詳細ログ: " + logPath;
                return false;
            }
        }
    }

    // 指輪専用の味方上書きだけを更新する。通常のモンスタークリエイター設定は変更しない。
    internal bool TryApplyAllyRingOverrides(
        IEnumerable<KeyValuePair<int, ushort>> ringActions,
        ISet<int>? ionazunForce,
        out string message)
    {
        message = "";

        var copy = new Dictionary<int, ushort>();
        if (ringActions != null)
        {
            foreach (var pair in ringActions)
                copy[pair.Key] = pair.Value;
        }

        var forceCopy = ionazunForce == null
            ? new HashSet<int>()
            : new HashSet<int>(ionazunForce);

        lock (_stateSync)
        {
            _allyRingActions = copy;
            _allyRingIonazunForce = forceCopy;
        }

        lock (_operationSync)
        {
            try
            {
                if (!_pine.TryConnect())
                {
                    message = "指輪設定は保持しました。PCSX2接続後に反映します。";
                    return false;
                }

                uint status = _pine.GetStatus();
                if (status != 0)
                {
                    message = "指輪設定は保持しました。PCSX2をゲーム実行状態にしてください。";
                    return false;
                }

                Dictionary<int, ushort> normal;
                lock (_stateSync)
                    normal = new Dictionary<int, ushort>(_actions);

                ApplyRuntime(normal);

                message =
                    "指輪専用の味方特技上書きを更新しました。\n" +
                    "対象種族数: " + copy.Count + "\n" +
                    "敵は通常のモンスタークリエイター設定を使用します。";
                return true;
            }
            catch (Exception ex)
            {
                message = "指輪専用特技上書きの適用に失敗しました: " + ex.Message;
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

    private uint[] BuildIonazunRepeatCode(uint dispatcherAddress)
    {
        uint traceAddress = dispatcherAddress + 0xD4u;
        uint routineAddress = dispatcherAddress + 0x58u;

        return new uint[]
        {
            // k0 = 記録領域のアドレス
            0x3C1A0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x375A0000u | (traceAddress & 0xFFFFu),

            // k0 = trace[3]（現在の種族に設定された特技ID）
            0x8F5A000Cu,

            // 0xFFなら0、そうでなければ0以外
            0x3B5A00FFu,

            // OFFなら元のv0を維持
            0x17400002u,
            0x00000000u,

            // ONなら使用済み判定を通過させる
            0x24020000u,

            // 元の bne v0,zero,0x00229160
            0x14400003u,
            0x00000000u,

            // 分岐しない場合は元の次命令へ
            EncodeIonazunRepeatJump(
                routineAddress + 9u * 4u,
                0x00228DD8u),
            0x00000000u,

            // 元の分岐先
            EncodeIonazunRepeatJump(
                routineAddress + 11u * 4u,
                0x00229160u),
            0x00000000u
        };
    }

    private uint[] BuildIonazunCompareCode(uint dispatcherAddress)
    {
        uint traceAddress = dispatcherAddress + 0xD4u;
        uint routineAddress = dispatcherAddress + 0x90u;

        return new uint[]
        {
            // k0 = 記録領域のアドレス
            0x3C1A0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x375A0000u | (traceAddress & 0xFFFFu),

            // k0 = trace[3]
            0x8F5A000Cu,

            // 0xFFなら0、そうでなければ0以外
            0x3B5A00FFu,

            // OFFなら元のsltへ進む
            0x17400004u,
            0x00000000u,

            // ONなら成功扱い
            0x24020001u,

            // 元の分岐処理へ移動
            0x10000003u,
            0x00000000u,

            // OFF時に実行する元の命令
            // slt v0,v0,v1
            0x0043102Au,
            0x00000000u,

            // 元の beq v0,zero,0x00228E74
            0x10400003u,

            // 元の遅延スロット
            0x3C05008Fu,

            // 分岐しなかった場合の続き
            EncodeIonazunRepeatJump(
                routineAddress + 13u * 4u,
                0x00228DF4u),
            0x00000000u,

            // 元の分岐先
            EncodeIonazunRepeatJump(
                routineAddress + 15u * 4u,
                0x00229074u),
            0x00000000u
        };
    }

    private void ApplyIonazunRepeatPatch(
        List<(byte SpeciesId, byte Marker, byte ActualAction)> normalEntries,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> allyOverrideEntries)
    {
        bool shouldPatch =
            normalEntries.Exists(x => x.Marker == 0xFF) ||
            allyOverrideEntries.Exists(x => x.Marker == 0xFF);

        if (!shouldPatch)
        {
            RestoreIonazunRepeatPatch();
            return;
        }

        if (_codeCaveAddress == 0 ||
            !IsDispatcherCode(_codeCaveAddress, normalEntries, allyOverrideEntries))
        {
            throw new InvalidOperationException(
                "既存ディスパッチャーを検証できません。");
        }

        uint usedJump = EncodeIonazunRepeatJump(
            IonazunUsedBranchAddress,
            _codeCaveAddress + 0x58u);

        uint compareJump = EncodeIonazunRepeatJump(
            IonazunCompareAddress,
            _codeCaveAddress + 0x90u);

        uint currentUsed =
            _pine.Read32(IonazunUsedBranchAddress);

        uint currentUsedDelay =
            _pine.Read32(IonazunUsedBranchDelayAddress);

        uint currentCompare =
            _pine.Read32(IonazunCompareAddress);

        // 前回の処理で比較フック位置に残った強制成功命令を復旧する
        if (currentCompare == 0x24020001u)
        {
            _pine.Write32(
                IonazunCompareAddress,
                IonazunCompareOriginal);

            currentCompare =
                _pine.Read32(IonazunCompareAddress);

            if (currentCompare != IonazunCompareOriginal)
            {
                throw new InvalidOperationException(
                    "確率判定命令の復旧に失敗しました。");
            }
        }

        uint currentCompareBranch =
            _pine.Read32(IonazunCompareBranchAddress);

        uint currentCompareDelay =
            _pine.Read32(IonazunCompareBranchDelayAddress);

        if (currentUsedDelay != IonazunUsedBranchDelayOriginal)
        {
            throw new InvalidOperationException(
                "使用済み判定フックの遅延スロットが想定外です。");
        }

        if (currentCompareDelay != IonazunCompareBranchDelayOriginal)
        {
            throw new InvalidOperationException(
                "確率判定フックの遅延スロットが想定外です。" +
                "\nアドレス: 0x" + IonazunCompareBranchDelayAddress.ToString("X8") +
                "\n実際の値: 0x" + currentCompareDelay.ToString("X8") +
                "\n期待値: 0x" + IonazunCompareBranchDelayOriginal.ToString("X8"));
        }

        if (currentUsed != IonazunUsedBranchOriginal &&
            currentUsed != usedJump)
        {
            throw new InvalidOperationException(
                "使用済み判定命令が想定外です: 0x" +
                currentUsed.ToString("X8"));
        }

        bool compareOriginal =
            currentCompare == IonazunCompareOriginal &&
            currentCompareBranch == IonazunCompareBranchOriginal;

        bool compareInstalled =
            currentCompare == compareJump &&
            currentCompareBranch == 0u;

        // 分岐命令だけNOPになった中間状態も復旧可能にする
        bool compareHalfInstalled =
            currentCompare == IonazunCompareOriginal &&
            currentCompareBranch == 0u;

        if (!compareOriginal &&
            !compareInstalled &&
            !compareHalfInstalled)
        {
            throw new InvalidOperationException(
                "確率判定フックの命令ペアが想定外です。\n" +
                "DE8: 0x" + currentCompare.ToString("X8") + "\n" +
                "DEC: 0x" + currentCompareBranch.ToString("X8"));
        }

        uint[] repeatCode =
            BuildIonazunRepeatCode(_codeCaveAddress);

        uint[] compareCode =
            BuildIonazunCompareCode(_codeCaveAddress);

        uint repeatAddress = _codeCaveAddress + 0x58u;
        uint compareAddress = _codeCaveAddress + 0x90u;

        for (int i = 0; i < repeatCode.Length; i++)
        {
            _pine.Write32(
                repeatAddress + (uint)(i * 4),
                repeatCode[i]);
        }

        for (int i = 0; i < compareCode.Length; i++)
        {
            _pine.Write32(
                compareAddress + (uint)(i * 4),
                compareCode[i]);
        }

        for (int i = 0; i < repeatCode.Length; i++)
        {
            if (_pine.Read32(
                repeatAddress + (uint)(i * 4)) != repeatCode[i])
            {
                throw new InvalidOperationException(
                    "イオナズン連発コードの検証に失敗しました。");
            }
        }

        for (int i = 0; i < compareCode.Length; i++)
        {
            if (_pine.Read32(
                compareAddress + (uint)(i * 4)) != compareCode[i])
            {
                throw new InvalidOperationException(
                    "イオナズン確率判定コードの検証に失敗しました。");
            }
        }

        // 比較命令の次の命令を先にNOPにする。
        // これによりJ命令の遅延スロットで元のbeqが実行されない。
        if (currentCompareBranch == IonazunCompareBranchOriginal)
        {
            _pine.Write32(IonazunCompareBranchAddress, 0u);

            if (_pine.Read32(IonazunCompareBranchAddress) != 0u)
            {
                throw new InvalidOperationException(
                    "確率判定の分岐命令をNOPにできませんでした。");
            }
        }

        if (currentCompare == IonazunCompareOriginal)
        {
            _pine.Write32(IonazunCompareAddress, compareJump);

            if (_pine.Read32(IonazunCompareAddress) != compareJump)
            {
                throw new InvalidOperationException(
                    "確率判定フックの設置に失敗しました。");
            }
        }

        if (currentUsed == IonazunUsedBranchOriginal)
        {
            _pine.Write32(IonazunUsedBranchAddress, usedJump);

            if (_pine.Read32(IonazunUsedBranchAddress) != usedJump)
            {
                throw new InvalidOperationException(
                    "使用済み判定フックの設置に失敗しました。");
            }
        }

        _ionazunRepeatCaveAddress = _codeCaveAddress;
    }

    private void RestoreIonazunRepeatPatch()
    {
        uint currentUsed =
            _pine.Read32(IonazunUsedBranchAddress);

        uint currentCompare =
            _pine.Read32(IonazunCompareAddress);

        // 前回の処理で比較フック位置に残った強制成功命令を復旧する
        if (currentCompare == 0x24020001u)
        {
            _pine.Write32(
                IonazunCompareAddress,
                IonazunCompareOriginal);

            currentCompare =
                _pine.Read32(IonazunCompareAddress);

            if (currentCompare != IonazunCompareOriginal)
            {
                throw new InvalidOperationException(
                    "確率判定命令の復旧に失敗しました。");
            }
        }

        uint currentCompareBranch =
            _pine.Read32(IonazunCompareBranchAddress);

        // まず使用済み判定フックを復元
        if (currentUsed != IonazunUsedBranchOriginal)
        {
            if (!IsIonazunRepeatJump(currentUsed))
            {
                throw new InvalidOperationException(
                    "使用済み判定命令が想定外です: 0x" +
                    currentUsed.ToString("X8"));
            }

            uint target = DecodeIonazunRepeatJump(currentUsed);

            if (target < 0x58u)
                throw new InvalidOperationException(
                    "使用済み判定フックのジャンプ先が不正です。");

            uint cave = target - 0x58u;

            if (target != cave + 0x58u || cave < CaveSearchEnd || cave > SharedActionAddressMax)
            {
                throw new InvalidOperationException(
                    "使用済み判定フックの接続先を検証できません。");
            }

            if (_codeCaveAddress != 0 &&
                _codeCaveAddress != cave)
            {
                throw new InvalidOperationException(
                    "使用済み判定フックが別のディスパッチャーを参照しています。");
            }

            _codeCaveAddress = cave;

            _pine.Write32(
                IonazunUsedBranchAddress,
                IonazunUsedBranchOriginal);

            if (_pine.Read32(IonazunUsedBranchAddress) !=
                IonazunUsedBranchOriginal)
            {
                throw new InvalidOperationException(
                    "使用済み判定命令の復元に失敗しました。");
            }
        }

        // 比較フックはDE8を先に戻し、その後DECを戻す
        if (currentCompare != IonazunCompareOriginal)
        {
            if (!IsIonazunRepeatJump(currentCompare))
            {
                throw new InvalidOperationException(
                    "確率判定命令が想定外です: 0x" +
                    currentCompare.ToString("X8"));
            }

            uint target = DecodeIonazunRepeatJump(currentCompare);

            if (target < 0x90u)
                throw new InvalidOperationException(
                    "確率判定フックのジャンプ先が不正です。");

            uint cave = target - 0x90u;

            if (target != cave + 0x90u || cave < CaveSearchEnd || cave > SharedActionAddressMax)
            {
                throw new InvalidOperationException(
                    "確率判定フックの接続先を検証できません。");
            }

            if (_codeCaveAddress != 0 &&
                _codeCaveAddress != cave)
            {
                throw new InvalidOperationException(
                    "確率判定フックが別のディスパッチャーを参照しています。");
            }

            _codeCaveAddress = cave;

            _pine.Write32(
                IonazunCompareAddress,
                IonazunCompareOriginal);

            if (_pine.Read32(IonazunCompareAddress) !=
                IonazunCompareOriginal)
            {
                throw new InvalidOperationException(
                    "確率判定命令の復元に失敗しました。");
            }
        }

        if (currentCompareBranch != IonazunCompareBranchOriginal)
        {
            if (currentCompareBranch != 0u)
            {
                throw new InvalidOperationException(
                    "確率判定の分岐命令が想定外です: 0x" +
                    currentCompareBranch.ToString("X8"));
            }

            _pine.Write32(
                IonazunCompareBranchAddress,
                IonazunCompareBranchOriginal);

            if (_pine.Read32(IonazunCompareBranchAddress) !=
                IonazunCompareBranchOriginal)
            {
                throw new InvalidOperationException(
                    "確率判定の分岐命令の復元に失敗しました。");
            }
        }

        _ionazunRepeatCaveAddress = 0;
    }

    private List<(byte SpeciesId, byte Marker, byte ActualAction)>
        BuildActionEntries(
            Dictionary<int, ushort> actions,
            ISet<int>? forceOverride = null)
    {
        var entries =
            new List<(byte SpeciesId, byte Marker, byte ActualAction)>();

        var enabled = forceOverride != null
            ? new HashSet<int>(forceOverride)
            : _ionazunForceOverride != null
                ? new HashSet<int>(_ionazunForceOverride)
                : Torneko4ActionStore.GetIonazunForceCopy();

        foreach (var pair in actions)
        {
            if (pair.Key < 0 || pair.Key >= 255)
                throw new InvalidOperationException("モンスター一覧番号が範囲外です: " + pair.Key);

            if (pair.Value == 0 || pair.Value > 0xFF)
                throw new InvalidOperationException("特技IDが範囲外です: " + pair.Value);

            bool knownAction = Array.Exists(ActionChoices, x => x.Id == pair.Value);
            if (!knownAction)
                throw new InvalidOperationException("未登録の特技IDです: 0x" + pair.Value.ToString("X2"));

            bool forceIonazun = pair.Value == 0x00BF && enabled.Contains(pair.Key);
            byte marker = forceIonazun ? (byte)0xFF : (byte)pair.Value;
            byte actualAction = forceIonazun ? (byte)0xBF : (byte)pair.Value;

            entries.Add(((byte)(pair.Key + 1), marker, actualAction));
        }

        entries.Sort((a, b) => a.SpeciesId.CompareTo(b.SpeciesId));
        return entries;
    }

    private static byte[] BuildActionData(
        List<(byte SpeciesId, byte Marker, byte ActualAction)> entries)
    {
        byte[] data = new byte[entries.Count * 3];

        for (int i = 0; i < entries.Count; i++)
        {
            data[i * 3] = entries[i].SpeciesId;
            data[i * 3 + 1] = entries[i].Marker;
            data[i * 3 + 2] = entries[i].ActualAction;
        }

        return data;
    }

    private static uint[] BuildDispatcherStub(uint address)
    {
        uint traceAddress = address + 0xD4u;
        uint lookupAddress = address + 0xE4u;

        return new uint[]
        {
            // EEの128ビット汎用レジスタを完全に退避する。
            // 16バイト単位で6レジスタ分、0x80バイト確保。
            0x27BDFF80u, // addiu sp,sp,-0x80

            0x7FA80000u, // sq t0,0(sp)
            0x7FA90010u, // sq t1,0x10(sp)
            0x7FAA0020u, // sq t2,0x20(sp)
            0x7FAB0030u, // sq t3,0x30(sp)
            0x7FAC0040u, // sq t4,0x40(sp)
            0x7FAD0050u, // sq t5,0x50(sp)

            // 実行時種族ID
            0x90480004u, // lbu t0,4(v0)

            // トレース領域
            0x3C0B0000u | ((traceAddress >> 16) & 0xFFFFu),
            0x356B0000u | (traceAddress & 0xFFFFu),

            // 元のs1と実行時種族IDを記録
            0xAD710000u,
            0xAD680004u,

            // 呼び出し回数
            0x8D6C0008u,
            0x258C0001u,
            0xAD6C0008u,

            // 今回の特技マーカーを初期化
            0xAD60000Cu,

            // 特技テーブル検索へ
            EncodeIonazunRepeatJump(
                address + 16u * 4u,
                lookupAddress),

            0x00000000u
        };
    }
    private static uint[] BuildSparseLookupCode(
        uint address,
        int normalEntryCount,
        int allyOverrideEntryCount)
    {
        uint dataAddress = address + 0x200u;
        uint normalDataAddress =
            dataAddress + (uint)allyOverrideEntryCount * 3u;
        uint traceAddress = address + 0xD4u;

        var code = new List<uint>();

        void Emit(uint instruction) => code.Add(instruction);

        void EmitRestoreAndJump()
        {
            // EEの128ビット汎用レジスタを完全に復元する。
            Emit(0x7BA80000u); // lq t0,0(sp)
            Emit(0x7BA90010u); // lq t1,0x10(sp)
            Emit(0x7BAA0020u); // lq t2,0x20(sp)
            Emit(0x7BAB0030u); // lq t3,0x30(sp)
            Emit(0x7BAC0040u); // lq t4,0x40(sp)
            Emit(0x7BAD0050u); // lq t5,0x50(sp)

            // 元のスタック位置へ戻す
            Emit(0x27BD0080u); // addiu sp,sp,0x80

            // 元の特技処理関数へ戻る
            Emit(0x0808F1D4u); // j 0x0023C750
            Emit(0x00000000u); // nop
        }


        int EmitBranch(uint opcode, int targetIndex)
        {
            int index = code.Count;
            code.Add(opcode);
            code.Add(0u);

            int displacement = targetIndex - (index + 1);
            code[index] = opcode | (uint)(ushort)displacement;

            return index;
        }

        // --------------------------------------------------
        // 呼び出し元 0x002435BC で v0 = *(s2 + 8) が実行済み。
        //
        // v0     = 個体データポインタ
        // v0 + 4 = 実行時種族ID
        // v0+1D3 = 陣営フラグ
        //
        // BuildDispatcherStub の最初の命令で
        // t0 = lbu(v0 + 4) が実行済み。
        //
        // ここでは t0 を絶対に上書きしない。
        // --------------------------------------------------

        // v0 + 0x1D3: 0=味方、0以外=敵
        Emit(0x904D01D3u);                 // lbu t5,0x1D3(v0)

        // 敵なら通常設定へ
        int enemyToNormalBranch =
            EmitBranch(0x15A00000u, 0);    // bne t5,zero,normalSetup
        Emit(0x00000000u);

        // --------------------------------------------------
        // 味方専用：指輪上書きテーブル
        // --------------------------------------------------

        Emit(0x3C090000u | ((dataAddress >> 16) & 0xFFFFu));
        Emit(0x35290000u | (dataAddress & 0xFFFFu));
        Emit(0x240C0000u | ((uint)allyOverrideEntryCount & 0xFFFFu));

        int noAllyOverridesBranch =
            EmitBranch(0x11800000u, 0);

        int allyLoop = code.Count;

        Emit(0x912A0000u);                 // lbu t2,0(t1)
        int allyMatchBranch =
            EmitBranch(0x110A0000u, 0);    // beq t0,t2,allyMatch

        Emit(0x25290003u);                 // addiu t1,t1,3
        Emit(0x258CFFFFu);                 // addiu t4,t4,-1

        int allyRepeatBranch =
            EmitBranch(0x15800000u, allyLoop);

        // --------------------------------------------------
        // 通常設定テーブル
        // --------------------------------------------------

        int normalSetup = code.Count;

        Emit(0x3C090000u | ((normalDataAddress >> 16) & 0xFFFFu));
        Emit(0x35290000u | (normalDataAddress & 0xFFFFu));
        Emit(0x240C0000u | ((uint)normalEntryCount & 0xFFFFu));

        int noNormalEntriesBranch =
            EmitBranch(0x11800000u, 0);

        int normalLoop = code.Count;

        Emit(0x912A0000u);                 // lbu t2,0(t1)
        int normalMatchBranch =
            EmitBranch(0x110A0000u, 0);    // beq t0,t2,normalMatch

        Emit(0x25290003u);                 // addiu t1,t1,3
        Emit(0x258CFFFFu);                 // addiu t4,t4,-1

        int normalRepeatBranch =
            EmitBranch(0x15800000u, normalLoop);

        // --------------------------------------------------
        // 一致しなかった場合は元の処理へ
        // --------------------------------------------------

        int noMatch = code.Count;

        EmitRestoreAndJump();

        // --------------------------------------------------
        // 指輪設定に一致した場合
        // --------------------------------------------------

        int allyMatch = code.Count;

        Emit(0x912C0001u);                 // lbu t4,1(t1): marker
        Emit(0x912A0002u);                 // lbu t2,2(t1): action
        Emit(0xAD6C000Cu);                 // sw t4,12(t3): trace marker
        Emit(0x01408821u);                 // addu s1,t2,zero

        EmitRestoreAndJump();

        // --------------------------------------------------
        // 通常設定に一致した場合
        // --------------------------------------------------

        int normalMatch = code.Count;

        Emit(0x912C0001u);                 // lbu t4,1(t1)
        Emit(0x912A0002u);                 // lbu t2,2(t1)
        Emit(0xAD6C000Cu);                 // sw t4,12(t3)
        Emit(0x01408821u);                 // addu s1,t2,zero

        EmitRestoreAndJump();

        // --------------------------------------------------
        // 分岐先の確定
        // --------------------------------------------------

        void PatchBranch(int index, int targetIndex, uint opcode)
        {
            int displacement = targetIndex - (index + 1);
            code[index] = opcode | (uint)(ushort)displacement;
        }

        PatchBranch(
            enemyToNormalBranch,
            normalSetup,
            0x15A00000u);

        PatchBranch(
            noAllyOverridesBranch,
            normalSetup,
            0x11800000u);

        PatchBranch(
            allyMatchBranch,
            allyMatch,
            0x110A0000u);

        PatchBranch(
            allyRepeatBranch,
            allyLoop,
            0x15800000u);

        PatchBranch(
            noNormalEntriesBranch,
            noMatch,
            0x11800000u);

        PatchBranch(
            normalMatchBranch,
            normalMatch,
            0x110A0000u);

        PatchBranch(
            normalRepeatBranch,
            normalLoop,
            0x15800000u);

        return code.ToArray();
    }
    private static int EmitBranchWithDelay(List<uint> code, uint instruction)
    {
        int index = code.Count;
        code.Add(instruction);
        code.Add(0u);
        return index;
    }

    private static void PatchBranch(List<uint> code, int branchIndex, int targetIndex)
    {
        int displacement = targetIndex - (branchIndex + 1);
        code[branchIndex] = (code[branchIndex] & 0xFFFF0000u) | (uint)(ushort)displacement;
    }

    private static uint[] BuildAllySkillCheckStub(uint caveAddress, int entryCount)
    {
        uint dataAddress = caveAddress + 0x200u;
        var code = new List<uint>();
        void Emit(uint x) => code.Add(x);

        Emit(0x27BDFFF0u);                    // addiu sp,sp,-0x10
        Emit(0xAFBF0000u);                    // sw ra,0(sp)
        Emit(EncodeI(0x23, 4, 8, 8));         // lw t0,8(a0)
        int nullActor = EmitBranchWithDelay(code, EncodeI(0x04, 8, 0, 0));
        Emit(0u);
        Emit(EncodeI(0x24, 8, 9, 0x1D3));     // lbu t1,0x1D3(t0)
        int enemy = EmitBranchWithDelay(code, EncodeI(0x05, 9, 0, 0));
        Emit(0u);
        Emit(EncodeI(0x24, 8, 10, 4));        // lbu t2,4(t0)
        Emit(EncodeI(0x0F, 0, 11, (int)(dataAddress >> 16)));
        Emit(EncodeI(0x0D, 11, 11, (int)(dataAddress & 0xFFFFu)));
        Emit(EncodeI(0x09, 0, 12, entryCount));
        int noEntries = EmitBranchWithDelay(code, EncodeI(0x04, 12, 0, 0));
        Emit(0u);

        int loop = code.Count;
        Emit(EncodeI(0x24, 11, 9, 0));        // lbu t1,0(t3)
        int found = EmitBranchWithDelay(code, EncodeI(0x04, 9, 10, 0));
        Emit(0u);
        Emit(EncodeI(0x09, 11, 11, 3));       // addiu t3,t3,3
        Emit(EncodeI(0x09, 12, 12, -1));      // addiu t4,t4,-1
        int repeat = EmitBranchWithDelay(code, EncodeI(0x05, 12, 0, 0));
        Emit(0u);

        int callOriginal = code.Count;
        Emit(0x0C08F1D4u);                    // jal 0x0023C750
        Emit(0u);
        Emit(0x8FBF0000u);                    // lw ra,0(sp)
        Emit(0x27BD0010u);                    // addiu sp,sp,0x10
        Emit(0x03E00008u);                    // jr ra
        Emit(0u);

        int foundLabel = code.Count;
        Emit(EncodeI(0x24, 11, 5, 2));        // lbu a1,2(t3)
        int foundToCall = EmitBranchWithDelay(code, EncodeI(0x04, 0, 0, 0));
        Emit(0u);

        PatchBranch(code, nullActor, callOriginal);
        PatchBranch(code, enemy, callOriginal);
        PatchBranch(code, noEntries, callOriginal);
        PatchBranch(code, found, foundLabel);
        PatchBranch(code, repeat, loop);
        PatchBranch(code, foundToCall, callOriginal);
        return code.ToArray();
    }

    private static uint[] BuildAllyActionLoadStub(uint caveAddress, int entryCount)
    {
        uint dataAddress = caveAddress + 0x200u;
        uint traceMarkerAddress = caveAddress + 0xE0u;
        var code = new List<uint>();
        void Emit(uint x) => code.Add(x);

        Emit(0x27BDFFB0u);                    // addiu sp,sp,-0x50
        Emit(0x7FA90000u);                    // sq t1,0(sp)
        Emit(0x7FAA0010u);                    // sq t2,0x10(sp)
        Emit(0x7FAB0020u);                    // sq t3,0x20(sp)
        Emit(0x7FAC0030u);                    // sq t4,0x30(sp)
        Emit(0xAFBF0040u);                    // sw ra,0x40(sp)

        Emit(EncodeI(0x24, 8, 9, 0x1D3));     // lbu t1,0x1D3(t0)
        int enemy = EmitBranchWithDelay(code, EncodeI(0x05, 9, 0, 0));
        Emit(0u);

        // 味方だけ連発判定用マーカーを初期化する。敵側の通常設定・マーカーには触れない。
        Emit(EncodeI(0x0F, 0, 10, (int)(traceMarkerAddress >> 16)));
        Emit(EncodeI(0x0D, 10, 10, (int)(traceMarkerAddress & 0xFFFFu)));
        Emit(EncodeI(0x28, 10, 0, 0));        // sb zero,0(t2)

        Emit(EncodeI(0x24, 8, 10, 4));        // lbu t2,4(t0)
        Emit(EncodeI(0x0F, 0, 11, (int)(dataAddress >> 16)));
        Emit(EncodeI(0x0D, 11, 11, (int)(dataAddress & 0xFFFFu)));
        Emit(EncodeI(0x09, 0, 12, entryCount));
        int noEntries = EmitBranchWithDelay(code, EncodeI(0x04, 12, 0, 0));
        Emit(0u);

        int loop = code.Count;
        Emit(EncodeI(0x24, 11, 9, 0));        // lbu t1,0(t3)
        int found = EmitBranchWithDelay(code, EncodeI(0x04, 9, 10, 0));
        Emit(0u);
        Emit(EncodeI(0x09, 11, 11, 3));       // addiu t3,t3,3
        Emit(EncodeI(0x09, 12, 12, -1));      // addiu t4,t4,-1
        int repeat = EmitBranchWithDelay(code, EncodeI(0x05, 12, 0, 0));
        Emit(0u);

        int noMatch = code.Count;
        Emit(EncodeI(0x24, 8, 2, 0x3A));      // lbu v0,0x3A(t0)
        int originalToRestore = EmitBranchWithDelay(code, EncodeI(0x04, 0, 0, 0));
        Emit(0u);

        int foundLabel = code.Count;
        Emit(EncodeI(0x24, 11, 9, 1));       // lbu t1,1(t3): Ionazun force marker
        Emit(EncodeI(0x0F, 0, 10, (int)(traceMarkerAddress >> 16)));
        Emit(EncodeI(0x0D, 10, 10, (int)(traceMarkerAddress & 0xFFFFu)));
        Emit(EncodeI(0x28, 10, 9, 0));        // sb t1,0(t2)
        Emit(EncodeI(0x24, 11, 2, 2));       // lbu v0,2(t3): ring action ID

        int restore = code.Count;
        Emit(0x7BA90000u);                    // lq t1,0(sp)
        Emit(0x7BAA0010u);                    // lq t2,0x10(sp)
        Emit(0x7BAB0020u);                    // lq t3,0x20(sp)
        Emit(0x7BAC0030u);                    // lq t4,0x30(sp)
        Emit(0x8FBF0040u);                    // lw ra,0x40(sp)
        Emit(0x27BD0050u);                    // addiu sp,sp,0x50
        Emit(0x03E00008u);                    // jr ra
        Emit(0u);

        PatchBranch(code, enemy, noMatch);
        PatchBranch(code, noEntries, noMatch);
        PatchBranch(code, found, foundLabel);
        PatchBranch(code, repeat, loop);
        PatchBranch(code, originalToRestore, restore);
        return code.ToArray();
    }

    // 0x002CA3CCの対象有無判定用。a1=個体データポインタ。
    // 味方の対象種族だけ指輪設定の特技IDを返し、敵や他種族は元の値を返す。
    private static uint[] BuildAllyTargetCheckStub(uint caveAddress, int entryCount)
    {
        uint dataAddress = caveAddress + 0x200u;
        uint traceMarkerAddress = caveAddress + 0xE0u;
        var code = new List<uint>();
        void Emit(uint x) => code.Add(x);

        Emit(0x27BDFFB0u);                    // addiu sp,sp,-0x50
        Emit(0x7FA90000u);                    // sq t1,0(sp)
        Emit(0x7FAA0010u);                    // sq t2,0x10(sp)
        Emit(0x7FAB0020u);                    // sq t3,0x20(sp)
        Emit(0x7FAC0030u);                    // sq t4,0x30(sp)
        Emit(0xAFBF0040u);                    // sw ra,0x40(sp)

        Emit(EncodeI(0x24, 5, 9, 0x1D3));     // lbu t1,0x1D3(a1): faction
        int enemy = EmitBranchWithDelay(code, EncodeI(0x05, 9, 0, 0));
        Emit(0u);

        // 味方だけ連発判定マーカーを初期化する。敵側の状態は変更しない。
        Emit(EncodeI(0x0F, 0, 10, (int)(traceMarkerAddress >> 16)));
        Emit(EncodeI(0x0D, 10, 10, (int)(traceMarkerAddress & 0xFFFFu)));
        Emit(EncodeI(0x28, 10, 0, 0));        // sb zero,0(t2)

        Emit(EncodeI(0x24, 5, 10, 4));        // lbu t2,4(a1): runtime species ID
        Emit(EncodeI(0x0F, 0, 11, (int)(dataAddress >> 16)));
        Emit(EncodeI(0x0D, 11, 11, (int)(dataAddress & 0xFFFFu)));
        Emit(EncodeI(0x09, 0, 12, entryCount));
        int noEntries = EmitBranchWithDelay(code, EncodeI(0x04, 12, 0, 0));
        Emit(0u);

        int loop = code.Count;
        Emit(EncodeI(0x24, 11, 9, 0));        // lbu t1,0(t3)
        int found = EmitBranchWithDelay(code, EncodeI(0x04, 9, 10, 0));
        Emit(0u);
        Emit(EncodeI(0x09, 11, 11, 3));       // addiu t3,t3,3
        Emit(EncodeI(0x09, 12, 12, -1));      // addiu t4,t4,-1
        int repeat = EmitBranchWithDelay(code, EncodeI(0x05, 12, 0, 0));
        Emit(0u);

        int noMatch = code.Count;
        Emit(EncodeI(0x24, 5, 2, 0x3A));      // lbu v0,0x3A(a1)
        int originalToRestore = EmitBranchWithDelay(code, EncodeI(0x04, 0, 0, 0));
        Emit(0u);

        int foundLabel = code.Count;
        Emit(EncodeI(0x24, 11, 9, 1));       // lbu t1,1(t3): Ionazun force marker
        Emit(EncodeI(0x0F, 0, 10, (int)(traceMarkerAddress >> 16)));
        Emit(EncodeI(0x0D, 10, 10, (int)(traceMarkerAddress & 0xFFFFu)));
        Emit(EncodeI(0x28, 10, 9, 0));        // sb t1,0(t2)
        Emit(EncodeI(0x24, 11, 2, 2));       // lbu v0,2(t3): ring action ID

        int restore = code.Count;
        Emit(0x7BA90000u);                    // lq t1,0(sp)
        Emit(0x7BAA0010u);                    // lq t2,0x10(sp)
        Emit(0x7BAB0020u);                    // lq t3,0x20(sp)
        Emit(0x7BAC0030u);                    // lq t4,0x30(sp)
        Emit(0x8FBF0040u);                    // lw ra,0x40(sp)
        Emit(0x27BD0050u);                    // addiu sp,sp,0x50
        Emit(0x03E00008u);                    // jr ra
        Emit(0u);

        PatchBranch(code, enemy, noMatch);
        PatchBranch(code, noEntries, noMatch);
        PatchBranch(code, found, foundLabel);
        PatchBranch(code, repeat, loop);
        PatchBranch(code, originalToRestore, restore);
        return code.ToArray();
    }

    private bool IsAllyHookMarker(uint caveAddress)
    {
        return _pine.Read32(caveAddress + AllyHookMarkerOffset) == AllyHookMarker;
    }

    private void RestoreAllyActionHooks(uint preferredCaveAddress = 0u)
    {
        uint check1 = _pine.Read32(AllySkillCheckAddress1);
        uint check2 = _pine.Read32(AllySkillCheckAddress2);
        uint actionLoad = _pine.Read32(AllyActionLoadAddress);
        uint targetCheckLoad = _pine.Read32(AllyTargetCheckActionLoadAddress);

        bool anyHook = check1 != OriginalAllySkillCheckCall ||
            check2 != OriginalAllySkillCheckCall ||
            actionLoad != OriginalAllyActionLoad ||
            targetCheckLoad != OriginalAllyTargetCheckActionLoad;
        if (!anyHook)
            return;

        uint DeriveCave(uint hookAddress, uint instruction, uint original, uint offset)
        {
            if (instruction == original) return 0u;
            if ((instruction >> 26) != 3u)
                throw new InvalidOperationException(
                    "味方側特技フック命令が想定外です。位置=0x" +
                    hookAddress.ToString("X8") + " 値=0x" + instruction.ToString("X8"));
            uint target = DecodeJalFrom(hookAddress, instruction);
            if (target < offset)
                throw new InvalidOperationException("味方側特技フックの接続先が不正です。");
            return target - offset;
        }

        uint[] derived =
        {
            DeriveCave(AllySkillCheckAddress1, check1, OriginalAllySkillCheckCall, AllySkillCheckStubOffset),
            DeriveCave(AllySkillCheckAddress2, check2, OriginalAllySkillCheckCall, AllySkillCheckStubOffset),
            DeriveCave(AllyActionLoadAddress, actionLoad, OriginalAllyActionLoad, AllyActionLoadStubOffset),
            DeriveCave(AllyTargetCheckActionLoadAddress, targetCheckLoad, OriginalAllyTargetCheckActionLoad, AllyTargetCheckStubOffset)
        };

        uint caveAddress = preferredCaveAddress;
        if (caveAddress == 0u)
            foreach (uint value in derived)
                if (value != 0u) { caveAddress = value; break; }

        if (caveAddress == 0u || !IsAllyHookMarker(caveAddress))
            throw new InvalidOperationException(
                "味方側特技フックの所有マーカーを確認できないため、復元を中止しました。");

        foreach (uint value in derived)
            if (value != 0u && value != caveAddress)
                throw new InvalidOperationException(
                    "味方側特技フックが複数のコードケーブを参照しています。");

        uint expected1 = EncodeJalFrom(AllySkillCheckAddress1, caveAddress + AllySkillCheckStubOffset);
        uint expected2 = EncodeJalFrom(AllySkillCheckAddress2, caveAddress + AllySkillCheckStubOffset);
        uint expectedLoad = EncodeJalFrom(AllyActionLoadAddress, caveAddress + AllyActionLoadStubOffset);
        uint expectedTargetCheck = EncodeJalFrom(
            AllyTargetCheckActionLoadAddress,
            caveAddress + AllyTargetCheckStubOffset);

        if (check1 != OriginalAllySkillCheckCall && check1 != expected1)
            throw new InvalidOperationException("0x00241D48のフック先が想定外です。");
        if (check2 != OriginalAllySkillCheckCall && check2 != expected2)
            throw new InvalidOperationException("0x00241DB8のフック先が想定外です。");
        if (actionLoad != OriginalAllyActionLoad && actionLoad != expectedLoad)
            throw new InvalidOperationException("0x00241D5Cのフック先が想定外です。");
        if (targetCheckLoad != OriginalAllyTargetCheckActionLoad &&
            targetCheckLoad != expectedTargetCheck)
            throw new InvalidOperationException("0x002CA3DCのフック先が想定外です。");

        if (_pine.Read32(AllySkillCheckDelay1Address) != OriginalAllySkillCheckDelay ||
            _pine.Read32(AllySkillCheckDelay2Address) != OriginalAllySkillCheckDelay ||
            _pine.Read32(AllyActionLoadDelayAddress) != OriginalAllyActionLoadDelay ||
            _pine.Read32(AllyTargetCheckDelayAddress) != OriginalAllyTargetCheckDelay)
            throw new InvalidOperationException(
                "味方側特技フックの遅延スロットが想定外のため復元を中止しました。");

        if (check1 == expected1) _pine.Write32(AllySkillCheckAddress1, OriginalAllySkillCheckCall);
        if (check2 == expected2) _pine.Write32(AllySkillCheckAddress2, OriginalAllySkillCheckCall);
        if (actionLoad == expectedLoad) _pine.Write32(AllyActionLoadAddress, OriginalAllyActionLoad);
        if (targetCheckLoad == expectedTargetCheck)
            _pine.Write32(AllyTargetCheckActionLoadAddress, OriginalAllyTargetCheckActionLoad);

        if (_pine.Read32(AllySkillCheckAddress1) != OriginalAllySkillCheckCall ||
            _pine.Read32(AllySkillCheckAddress2) != OriginalAllySkillCheckCall ||
            _pine.Read32(AllyActionLoadAddress) != OriginalAllyActionLoad ||
            _pine.Read32(AllyTargetCheckActionLoadAddress) != OriginalAllyTargetCheckActionLoad)
            throw new InvalidOperationException("味方側特技フックを元の命令へ復元できませんでした。");
    }

    private void ApplyAllyActionHooks(uint caveAddress, int allyOverrideEntryCount)
    {
        if (allyOverrideEntryCount <= 0)
        {
            RestoreAllyActionHooks(caveAddress);
            return;
        }

        if (!IsAllyHookMarker(caveAddress))
            throw new InvalidOperationException("味方側特技フックのコードケーブ識別マーカーがありません。");

        if (_pine.Read32(AllySkillCheckDelay1Address) != OriginalAllySkillCheckDelay ||
            _pine.Read32(AllySkillCheckDelay2Address) != OriginalAllySkillCheckDelay ||
            _pine.Read32(AllyActionLoadDelayAddress) != OriginalAllyActionLoadDelay ||
            _pine.Read32(AllyTargetCheckDelayAddress) != OriginalAllyTargetCheckDelay)
            throw new InvalidOperationException(
                "味方側AIの遅延スロットが想定値と異なります。安全のためフックを中止しました。");

        uint expected1 = EncodeJalFrom(AllySkillCheckAddress1, caveAddress + AllySkillCheckStubOffset);
        uint expected2 = EncodeJalFrom(AllySkillCheckAddress2, caveAddress + AllySkillCheckStubOffset);
        uint expectedLoad = EncodeJalFrom(AllyActionLoadAddress, caveAddress + AllyActionLoadStubOffset);
        uint expectedTargetCheck = EncodeJalFrom(
            AllyTargetCheckActionLoadAddress,
            caveAddress + AllyTargetCheckStubOffset);

        uint current1 = _pine.Read32(AllySkillCheckAddress1);
        uint current2 = _pine.Read32(AllySkillCheckAddress2);
        uint currentLoad = _pine.Read32(AllyActionLoadAddress);
        uint currentTargetCheck = _pine.Read32(AllyTargetCheckActionLoadAddress);

        if ((current1 != OriginalAllySkillCheckCall && current1 != expected1) ||
            (current2 != OriginalAllySkillCheckCall && current2 != expected2) ||
            (currentLoad != OriginalAllyActionLoad && currentLoad != expectedLoad) ||
            (currentTargetCheck != OriginalAllyTargetCheckActionLoad &&
             currentTargetCheck != expectedTargetCheck))
            throw new InvalidOperationException(
                "味方側AIのフック位置に想定外の命令があります。既存の処理を保護するため中止しました。");

        if (current1 == OriginalAllySkillCheckCall) _pine.Write32(AllySkillCheckAddress1, expected1);
        if (current2 == OriginalAllySkillCheckCall) _pine.Write32(AllySkillCheckAddress2, expected2);
        if (currentLoad == OriginalAllyActionLoad) _pine.Write32(AllyActionLoadAddress, expectedLoad);
        if (currentTargetCheck == OriginalAllyTargetCheckActionLoad)
            _pine.Write32(AllyTargetCheckActionLoadAddress, expectedTargetCheck);

        if (_pine.Read32(AllySkillCheckAddress1) != expected1 ||
            _pine.Read32(AllySkillCheckAddress2) != expected2 ||
            _pine.Read32(AllyActionLoadAddress) != expectedLoad ||
            _pine.Read32(AllyTargetCheckActionLoadAddress) != expectedTargetCheck)
            throw new InvalidOperationException("味方側AIフックの設置検証に失敗しました。");
    }

    private static uint GetRequiredCaveSize(
        List<(byte SpeciesId, byte Marker, byte ActualAction)> normalEntries,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> allyOverrideEntries)
    {
        uint dataLength =
            (uint)(normalEntries.Count + allyOverrideEntries.Count) * 3u;
        uint paddedDataLength = (dataLength + 3u) & ~3u;
        return Math.Max(AllyHookReservedSize, 0x200u + paddedDataLength);
    }

    private string _lastDispatcherVerificationFailure = "";

    private bool DispatcherWordMatches(
        uint address,
        uint expected,
        string section)
    {
        uint actual = _pine.Read32(address);
        if (actual == expected)
            return true;

        _lastDispatcherVerificationFailure =
            section +
            " / アドレス=0x" + address.ToString("X8") +
            " / 期待値=0x" + expected.ToString("X8") +
            " / 実際値=0x" + actual.ToString("X8");
        return false;
    }

    private bool IsDispatcherCode(
        uint address,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> normalEntries,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> allyOverrideEntries)
    {
        _lastDispatcherVerificationFailure = "";

        uint[] stub = BuildDispatcherStub(address);
        uint[] lookup = BuildSparseLookupCode(
            address, normalEntries.Count, allyOverrideEntries.Count);

        var allEntries = new List<(byte SpeciesId, byte Marker, byte ActualAction)>();
        allEntries.AddRange(allyOverrideEntries);
        allEntries.AddRange(normalEntries);
        byte[] data = BuildActionData(allEntries);

        for (int i = 0; i < stub.Length; i++)
            if (!DispatcherWordMatches(
                address + (uint)(i * 4), stub[i], "メインディスパッチャー"))
                return false;

        for (uint offset = (uint)(stub.Length * 4); offset < 0x58u; offset += 4u)
            if (!DispatcherWordMatches(
                address + offset, 0u, "メインコード余白"))
                return false;

        uint lookupAddress = address + 0xE4u;
        for (int i = 0; i < lookup.Length; i++)
            if (!DispatcherWordMatches(
                lookupAddress + (uint)(i * 4), lookup[i], "種族検索コード"))
                return false;

        uint[] allyCheck = BuildAllySkillCheckStub(address, allyOverrideEntries.Count);
        uint[] allyLoad = BuildAllyActionLoadStub(address, allyOverrideEntries.Count);
        uint[] allyTargetCheck = BuildAllyTargetCheckStub(address, allyOverrideEntries.Count);
        for (int i = 0; i < allyCheck.Length; i++)
            if (!DispatcherWordMatches(
                address + AllySkillCheckStubOffset + (uint)(i * 4),
                allyCheck[i], "味方特技使用判定スタブ"))
                return false;
        for (int i = 0; i < allyLoad.Length; i++)
            if (!DispatcherWordMatches(
                address + AllyActionLoadStubOffset + (uint)(i * 4),
                allyLoad[i], "味方特技ID読込スタブ"))
                return false;
        for (int i = 0; i < allyTargetCheck.Length; i++)
            if (!DispatcherWordMatches(
                address + AllyTargetCheckStubOffset + (uint)(i * 4),
                allyTargetCheck[i], "味方対象判定スタブ"))
                return false;

        if (!DispatcherWordMatches(
            address + AllyHookMarkerOffset,
            AllyHookMarker,
            "味方フック所有マーカー"))
            return false;

        uint dataAddress = address + 0x200u;
        uint paddedLength = ((uint)data.Length + 3u) & ~3u;

        for (uint offset = 0; offset < paddedLength; offset += 4u)
        {
            uint expected = 0u;
            for (int b = 0; b < 4; b++)
            {
                uint index = offset + (uint)b;
                if (index < (uint)data.Length)
                    expected |= (uint)data[index] << (b * 8);
            }
            if (!DispatcherWordMatches(
                dataAddress + offset, expected, "種族別アクションデータ"))
                return false;
        }

        return true;
    }

    internal static uint FindSharedRuntimeCave()
    {
        lock (SharedRuntimeCaveSync)
        {
            return FindSharedRuntimeCaveCore(false);
        }
    }

    internal static uint FindOrCreateSharedRuntimeCave()
    {
        lock (SharedRuntimeCaveSync)
        {
            return FindSharedRuntimeCaveCore(true);
        }
    }

    private static uint FindSharedRuntimeCaveCore(bool createIfMissing)
    {
        // 現行版の共有マーカーに加え、旧版の指輪・味方フックのマーカーも
        // 検出して既存の4KB領域を再利用する。
        for (uint page = CaveSearchStart;
             page >= CaveSearchEnd;
             page -= SharedRuntimeCaveStep)
        {
            uint managerMarker = ReadShared32(
                page + SharedRuntimeCaveMarkerOffset);

            if (managerMarker == SharedRuntimeCaveMarker)
                return page;

            uint ringMarker = ReadShared32(
                page + SharedRingCaveMarkerOffset);

            uint actionMarker = ReadShared32(
                page + SharedActionCaveMarkerOffset);

            if (ringMarker == SharedRingCaveMarker ||
                actionMarker == SharedActionCaveMarker)
            {
                if (managerMarker != 0u)
                {
                    throw new InvalidOperationException(
                        "共有ランタイム領域の管理マーカー位置が他のデータで使用されています。" +
                        "ページ: 0x" + page.ToString("X8"));
                }

                WriteShared32(
                    page + SharedRuntimeCaveMarkerOffset,
                    SharedRuntimeCaveMarker);

                if (ReadShared32(page + SharedRuntimeCaveMarkerOffset) !=
                    SharedRuntimeCaveMarker)
                {
                    throw new InvalidOperationException(
                        "共有ランタイム領域の管理マーカーを書き込めませんでした。");
                }

                return page;
            }
        }

        if (!createIfMissing)
            return 0u;

        // 既存機能が使っているページを避け、4KB全体がゼロのページを一つだけ確保する。
        for (uint page = CaveSearchStart;
             page >= CaveSearchEnd;
             page -= SharedRuntimeCaveStep)
        {
            bool empty = true;

            for (uint offset = 0; offset < SharedRuntimeCaveSize; offset += 4u)
            {
                if (ReadShared32(page + offset) != 0u)
                {
                    empty = false;
                    break;
                }
            }

            if (!empty)
                continue;

            WriteShared32(
                page + SharedRuntimeCaveMarkerOffset,
                SharedRuntimeCaveMarker);

            if (ReadShared32(page + SharedRuntimeCaveMarkerOffset) !=
                SharedRuntimeCaveMarker)
            {
                throw new InvalidOperationException(
                    "共有ランタイム領域の確保マーカーを書き込めませんでした。");
            }

            return page;
        }

        throw new InvalidOperationException(
            "EE RAMに共有ランタイム領域（4KB）の空きページがありません。\n" +
            "指輪機能と種族別特技機能が同じページを共有する方式で探索しました。");
    }

    private uint FindCodeCave(uint requiredSize)
    {
        if (requiredSize < 0x200u ||
            SharedActionCaveOffset + requiredSize > SharedRuntimeCaveSize)
        {
            throw new InvalidOperationException(
                "共有ページ内に種族別特技コードを配置できません。" +
                "必要サイズ: " + requiredSize);
        }

        uint sharedPage = FindOrCreateSharedRuntimeCave();
        return sharedPage + SharedActionCaveOffset;
    }

    private void WriteDispatcherCode(
        uint address,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> normalEntries,
        List<(byte SpeciesId, byte Marker, byte ActualAction)> allyOverrideEntries)
    {
        uint[] stub = BuildDispatcherStub(address);
        uint[] lookup = BuildSparseLookupCode(
            address, normalEntries.Count, allyOverrideEntries.Count);

        var allEntries = new List<(byte SpeciesId, byte Marker, byte ActualAction)>();
        allEntries.AddRange(allyOverrideEntries);
        allEntries.AddRange(normalEntries);
        byte[] data = BuildActionData(allEntries);

        for (int i = 0; i < stub.Length; i++)
            _pine.Write32(address + (uint)(i * 4), stub[i]);

        for (uint offset = (uint)(stub.Length * 4); offset < 0x58u; offset += 4u)
            _pine.Write32(address + offset, 0u);

        uint lookupAddress = address + 0xE4u;
        for (int i = 0; i < lookup.Length; i++)
            _pine.Write32(lookupAddress + (uint)(i * 4), lookup[i]);

        uint[] allyCheck = BuildAllySkillCheckStub(address, allyOverrideEntries.Count);
        uint[] allyLoad = BuildAllyActionLoadStub(address, allyOverrideEntries.Count);
        uint[] allyTargetCheck = BuildAllyTargetCheckStub(address, allyOverrideEntries.Count);
        for (int i = 0; i < allyCheck.Length; i++)
            _pine.Write32(address + AllySkillCheckStubOffset + (uint)(i * 4), allyCheck[i]);
        for (int i = 0; i < allyLoad.Length; i++)
            _pine.Write32(address + AllyActionLoadStubOffset + (uint)(i * 4), allyLoad[i]);
        for (int i = 0; i < allyTargetCheck.Length; i++)
            _pine.Write32(address + AllyTargetCheckStubOffset + (uint)(i * 4), allyTargetCheck[i]);

        uint dataAddress = address + 0x200u;
        uint paddedLength = ((uint)data.Length + 3u) & ~3u;
        for (uint offset = 0; offset < paddedLength; offset += 4u)
        {
            uint value = 0u;
            for (int b = 0; b < 4; b++)
            {
                uint index = offset + (uint)b;
                if (index < (uint)data.Length)
                    value |= (uint)data[index] << (b * 8);
            }
            _pine.Write32(dataAddress + offset, value);
        }

        uint traceAddress = address + 0xD4u;
        _pine.Write32(traceAddress, 0u);
        _pine.Write32(traceAddress + 4u, 0u);
        _pine.Write32(traceAddress + 8u, 0u);
        _pine.Write32(traceAddress + 12u, 0u);
        _pine.Write32(address + AllyHookMarkerOffset, AllyHookMarker);

        if (!IsDispatcherCode(address, normalEntries, allyOverrideEntries))
            throw new InvalidOperationException(
                "敵味方別特技ディスパッチャーの検証に失敗しました。\n" +
                _lastDispatcherVerificationFailure);
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
        Dictionary<int, ushort> allyRingActions;
        HashSet<int> allyRingIonazunForce;
        lock (_stateSync)
        {
            allyRingActions = new Dictionary<int, ushort>(_allyRingActions);
            allyRingIonazunForce = new HashSet<int>(_allyRingIonazunForce);
        }

        var normalEntries = BuildActionEntries(actions);
        var allyOverrideEntries = BuildActionEntries(allyRingActions, allyRingIonazunForce);

        uint currentCall = _pine.Read32(PatchAddress);
        bool currentIsDispatcher = IsDispatcherCall(currentCall);
        if (currentCall != OriginalCallInstruction && !currentIsDispatcher)
            throw new InvalidOperationException(
                "敵特技呼び出し命令が想定外です。現在値: 0x" + currentCall.ToString("X8"));

        uint delayInstruction = _pine.Read32(DelaySlotAddress);
        if (IsLegacyPatch(delayInstruction))
        {
            _pine.Write32(DelaySlotAddress, OriginalDelayInstruction);
            delayInstruction = _pine.Read32(DelaySlotAddress);
        }
        if (delayInstruction != OriginalDelayInstruction)
            throw new InvalidOperationException(
                "遅延スロットが想定外です。現在値: 0x" + delayInstruction.ToString("X8"));

        if (!currentIsDispatcher && _codeCaveAddress == 0u)
            RestoreAllyActionHooks();

        if (currentIsDispatcher)
        {
            uint detectedCave = DecodeDispatcherAddress(currentCall);
            if (IsDispatcherCode(detectedCave, normalEntries, allyOverrideEntries))
            {
                _codeCaveAddress = detectedCave;
            }
            else
            {
                RestoreAllyActionHooks(detectedCave);
                RestoreIonazunRepeatPatch();
                RestoreCallInstruction();
                _codeCaveAddress = 0u;
                currentIsDispatcher = false;
            }
        }

        if (_codeCaveAddress != 0u &&
            !IsDispatcherCode(_codeCaveAddress, normalEntries, allyOverrideEntries))
        {
            RestoreAllyActionHooks(_codeCaveAddress);
            RestoreIonazunRepeatPatch();
            if (currentIsDispatcher)
                RestoreCallInstruction();
            _codeCaveAddress = 0u;
        }

        if (normalEntries.Count == 0 && allyOverrideEntries.Count == 0)
        {
            RestoreAllyActionHooks(_codeCaveAddress);
            RestoreIonazunRepeatPatch();
            if (currentIsDispatcher)
                RestoreCallInstruction();
            _codeCaveAddress = 0u;
            return;
        }

        if (_codeCaveAddress == 0u)
        {
            uint requiredSize = GetRequiredCaveSize(normalEntries, allyOverrideEntries);
            _codeCaveAddress = FindCodeCave(requiredSize);
            WriteDispatcherCode(_codeCaveAddress, normalEntries, allyOverrideEntries);
        }

        uint newCall = EncodeJal(_codeCaveAddress);
        _pine.Write32(PatchAddress, newCall);
        uint verifyCall = _pine.Read32(PatchAddress);
        if (verifyCall != newCall)
            throw new InvalidOperationException(
                "ディスパッチャー呼び出し命令の検証に失敗しました。書き込み値: 0x" +
                newCall.ToString("X8") + " 読み戻し値: 0x" + verifyCall.ToString("X8"));

        ApplyIonazunRepeatPatch(normalEntries, allyOverrideEntries);
        ApplyAllyActionHooks(_codeCaveAddress, allyOverrideEntries.Count);
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

            if (cave < CaveSearchEnd || cave > SharedActionAddressMax)
            {
                throw new InvalidOperationException(
                    "コードケーブのアドレスが範囲外です。");
            }

            uint traceAddress = cave + 0xD4u;
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
                int ringOverrideCount;

                lock (_stateSync)
                {
                    snapshot = new Dictionary<int, ushort>(_actions);
                    ringOverrideCount = _allyRingActions.Count;
                }

                if (snapshot.Count == 0)
                {
                    RaiseStatus(
                        "種族別特技設定なし：元の命令を維持しています。");
                }

                if (snapshot.Count > 0 ||
                    ringOverrideCount > 0 ||
                    _codeCaveAddress != 0 ||
                    _ionazunRepeatCaveAddress != 0)
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
                            RaiseStatus(snapshot.Count > 0 || ringOverrideCount > 0
                                ? "種族別特技ディスパッチャー適用中：通常設定 " +
                                  snapshot.Count + "種類 / 指輪上書き " +
                                  ringOverrideCount + "種類"
                                : "種族別特技設定なし：元の命令へ復元済みです。");
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
            string logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "トルネコいじり_種族別特技監視エラー.txt");

            try
            {
                System.IO.File.WriteAllText(
                    logPath,
                    "発生日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    Environment.NewLine + ex.ToString());

                string oneLineMessage = ex.Message
                    .Replace("\\r", " ")
                    .Replace("\\n", " ");

                RaiseStatus(
                    "種族別特技監視エラー: " + oneLineMessage +
                    " / 詳細ログ: デスクトップの「トルネコいじり_種族別特技監視エラー.txt」");
            }
            catch (Exception logException)
            {
                RaiseStatus(
                    "種族別特技監視エラー: " + ex.Message +
                    " / ログ保存にも失敗: " + logException.Message);
            }
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
                    RestoreAllyActionHooks(_codeCaveAddress);
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


