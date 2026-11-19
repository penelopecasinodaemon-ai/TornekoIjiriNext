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
                    _pine.Disconnect();
                    return false;
                }

                ApplyRuntime(copy);

                if (copy.Count == 0)
                {
                    _pine.Disconnect();
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
                _pine.Disconnect();
                message =
                    "種族別特技の適用に失敗しました。\n" +
                    ex.Message;
                return false;
            }
        }
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

        uint[] expected =
        {
            0x90480006u,
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

        uint[] code =
        {
            // lbu t0,6(v0) : 1バイトの種族IDを読み取る
            0x90480006u,

            // lui t1,hi(table)
            0x3C090000u | ((tableAddress >> 16) & 0xFFFFu),

            // ori t1,t1,lo(table)
            0x35290000u | (tableAddress & 0xFFFFu),

            // addu t1,t1,t0
            0x01284821u,

            // lbu t2,0(t1)
            0x912A0000u,

            // beq t2,zero,skip
            0x11400002u,

            // nop
            0x00000000u,

            // addu s1,t2,zero
            0x01408821u,

            // j dgEnemyAttack
            0x0808F1D4u,

            // nop
            0x00000000u
        };

        for (int i = 0; i < code.Length; i++)
            _pine.Write32(address + (uint)(i * 4), code[i]);

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
            if (currentIsDispatcher)
                RestoreCallInstruction();

            return;
        }

        byte[] table = BuildActionTable(actions);

        if (currentIsDispatcher && TablesEqual(_lastTable, table))
            return;

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
                    snapshot = new Dictionary<int, ushort>(_actions);

                if (snapshot.Count == 0)
                {
                    _pine.Disconnect();
                    RaiseStatus(
                        "種族別特技設定なし：元の命令を維持しています。");
                }

                if (snapshot.Count > 0)
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
                            RaiseStatus(
                                "種族別特技ディスパッチャー適用中：" +
                                snapshot.Count + "種類");
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
            _pine.Disconnect();
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
                    RestoreCallInstruction();
            }
            catch
            {
            }

            _pine.Dispose();
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

