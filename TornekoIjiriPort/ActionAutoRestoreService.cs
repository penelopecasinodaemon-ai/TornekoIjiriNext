using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TornekoIjiriPort;

internal sealed class ActionAutoRestoreService : IDisposable
{
    private const uint BaseActionAddress = 0x00396A74u;
    private const uint SlotStride = 0x370u;
    private const int EnemySlotCount = 39;

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
        (0x23, "力を溜めた（トロル）"),
        (0x24, "力を溜めた（トロルボンバー）"),
        (0x25, "超ちからため（トロルキング）"),
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
        (0x34, "毒で力-1"),
        (0x35, "毒で力-3（マタンゴ）"),
        (0x36, "毒で力-3（マージマタンゴ）"),
        (0x37, "ラリホー"),
        (0x38, "催眠"),
        (0x39, "ベビーサタンのアイテム盗み"),
        (0x3A, "ミニデーモンの壺盗み"),
        (0x3B, "つかいまの装備盗み"),
        (0x3C, "ミステリドールの吸収光線"),
        (0x3D, "いしにんぎょうの吸収光線"),
        (0x3E, "おどる宝石のお金盗み"),
        (0x3F, "レベルダウンダンス"),
        (0x4B, "さそう踊り（ダンスキャロット）"),
        (0x4C, "満腹度-10"),
        (0x4D, "さそう踊り（テンツク）"),
        (0x4E, "さそう踊り（スーパーテンツク）"),
        (0x4F, "さそう踊り（ラストテンツク）"),
        (0x54, "バシルーラ"),
        (0x55, "ゲーツ"),
        (0x57, "力を溜めた（あくましんかん）"),
        (0x58, "超ちからため（じごくのつかい）"),
        (0x5E, "強化攻撃（さつじんき）"),
        (0x5F, "強化攻撃（エリミネーター）"),
        (0x60, "強化攻撃（デスストーカー）"),
        (0x62, "口をふさぐ"),
        (0x68, "マホトーン"),
        (0x6C, "力が出ず出来なかった（ギガンテス）"),
        (0x6D, "力を溜めた（アトラス）"),
        (0x6F, "力を溜めた（ベリアル）"),
        (0x70, "力を溜めた（おおきづち）"),
        (0x71, "力を溜めた（ブラウニー）"),
        (0x79, "軍隊アリ召喚失敗？"),
        (0x81, "50ダメージ（ガーゴイル）"),
        (0x82, "強化攻撃（モシャスナイト）"),
        (0x83, "強化攻撃（シャドーナイト）"),
        (0x84, "マダンテ"),
        (0x8C, "3ターンかなしばり（ちゅうまじゅう）"),
        (0x8D, "3ターンかなしばり（ストローマウス）"),
        (0x8E, "3ターンかなしばり（あめふらし）"),
        (0x90, "眩しい光（サンダーラット）"),
        (0x91, "眩しい光（プラズママウス）"),
        (0x95, "分裂（バブリン）"),
        (0x96, "分裂（プヨンターゲット）"),
        (0x97, "分裂（ポムポムボム）"),
        (0x98, "自爆（リビングハンマー）"),
        (0x99, "自爆（キラースター）"),
        (0x9A, "自爆の構え（デビルアンカー）"),
        (0xA6, "力を溜めた（レノファイター）"),
        (0xA7, "力を溜めた（グレイトホーン）"),
        (0xA9, "25ダメージ（フライングデビル）"),
        (0xAA, "25ダメージ（ランガー）"),
        (0xAB, "真空斬り・バイキルト"),
        (0xAC, "真空斬り"),
        (0xAD, "思いっきり剣を振った"),
        (0xAE, "封印攻撃"),
        (0xAF, "スカラ"),
        (0xB0, "殻に入る（つのうしがい）"),
        (0xB1, "殻に入る（しびれマイマイ）"),
        (0xB2, "殻に入る（あんこくつむり）"),
        (0xB6, "攻撃力-1"),
        (0xB7, "防御力-1"),
        (0xB8, "メダパニ・仲間呼び（同種）"),
        (0xB9, "メダパニ・仲間呼び（同種・別ID）"),
        (0xBA, "薙ぎ払い"),
        (0xBC, "50ダメージ（ジャスティス兄）"),
        (0xBD, "失敗（コロヒーロー）"),
        (0xBF, "コロマージの魔法"),
        (0xC0, "効果なし（コロプリースト）"),
        (0xC1, "失敗（プチヒーロー）"),
        (0xC3, "プチマージの魔法"),
        (0xC4, "効果なし（プチプリースト）"),
        (0xC5, "50ダメージ（ヘルジャスティス）")
    };

    private readonly PineClient _pine = new PineClient();
    private readonly object _pendingLock = new object();
    private readonly Dictionary<int, ushort> _pending = new Dictionary<int, ushort>();
    private readonly System.Threading.Timer _timer;
    private int _pollBusy;
    private bool _sawRunning;
    private bool _handledPause;
    private bool _disposed;
    private string _lastStatus = "";

    internal event Action<string>? StatusChanged;

    internal ActionAutoRestoreService()
    {
        _timer = new System.Threading.Timer(Poll, null, 500, 100);
    }

    internal bool TryApply(int monsterListId, ushort actionId, out string message)
    {
        message = "";
        if (monsterListId < 0)
        {
            message = "モンスターが選択されていません。";
            return false;
        }

        try
        {
            if (!_pine.TryConnect())
            {
                message = "PCSX2のPINEに接続できません。PCSX2側でPINEを有効にしてください（標準ポート TCP 28011）。";
                return false;
            }

            if (_pine.GetStatus() != 0)
            {
                message = "特技変更はゲーム実行中に行ってください。PCSX2が一時停止中、またはVM停止中です。";
                return false;
            }

            // HSP側IDはC#モンスター一覧番号+1。個体レコード+2の元IDと照合する。
            ushort targetMonsterId = checked((ushort)(monsterListId + 1));
            int changed = 0;
            int skipped = 0;

            for (int slot = 1; slot <= EnemySlotCount; slot++)
            {
                uint actionAddress = BaseActionAddress + (uint)(slot - 1) * SlotStride;
                ushort originalId = _pine.Read16(actionAddress + 2u);
                if (originalId != targetMonsterId)
                    continue;

                ushort currentId = _pine.Read16(actionAddress);
                bool hasPending;
                ushort previousAction;
                lock (_pendingLock)
                    hasPending = _pending.TryGetValue(slot, out previousAction);

                if (hasPending && currentId != previousAction && currentId != originalId)
                {
                    skipped++;
                    continue;
                }
                if (!hasPending && currentId != originalId)
                {
                    skipped++;
                    continue;
                }

                lock (_pendingLock)
                    _pending[slot] = actionId;

                _pine.Write16(actionAddress, actionId);
                if (_pine.Read16(actionAddress) != actionId)
                    throw new InvalidOperationException("行動IDの読み戻し検証に失敗しました。敵枠 " + slot.ToString("D2"));

                changed++;
            }

            if (changed == 0)
            {
                message = "現在のフロアに対象モンスターがいないか、行動IDが別の値に変更されているため安全に変更できません。";
                return false;
            }

            message = "特技変更成功：0x" + actionId.ToString("X2") +
                " ／ 対象 " + changed + "枠" +
                (skipped > 0 ? " ／ 安全確認でスキップ " + skipped + "枠" : "") +
                "。EE 002435D0で停止すると元IDへ復元します。";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private void Poll(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _pollBusy, 1) != 0)
            return;

        try
        {
            if (!_pine.TryConnect())
            {
                RaiseStatus("PINE未接続：PCSX2側でPINEを有効化してください（TCP 28011）。");
                return;
            }

            uint status = _pine.GetStatus();
            if (status == 0)
            {
                _sawRunning = true;
                _handledPause = false;
                RaiseStatus("PINE接続中：特技処理後の一時停止を監視しています。");
                return;
            }

            if (status == 1)
            {
                if (_sawRunning && !_handledPause)
                {
                    _handledPause = true;
                    int restored = RestorePending();
                    RaiseStatus("PCSX2の一時停止を検知：元モンスターIDへ " +
                        restored + "枠を復元しました。デバッガーで実行を再開してください。");
                }
                else
                {
                    RaiseStatus("PCSX2一時停止中：実行再開後に監視を続けます。");
                }
                return;
            }

            _sawRunning = false;
            _handledPause = false;
            RaiseStatus("PCSX2の仮想マシンが停止しています。");
        }
        catch (Exception ex)
        {
            _pine.Disconnect();
            RaiseStatus("PINE監視エラー：" + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _pollBusy, 0);
        }
    }

    private int RestorePending()
    {
        KeyValuePair<int, ushort>[] pending;
        lock (_pendingLock)
            pending = _pending.ToArray();

        int restored = 0;
        foreach (var item in pending)
        {
            int slot = item.Key;
            ushort expectedAction = item.Value;
            uint actionAddress = BaseActionAddress + (uint)(slot - 1) * SlotStride;

            try
            {
                ushort originalId = _pine.Read16(actionAddress + 2u);
                ushort currentId = _pine.Read16(actionAddress);

                if (currentId == expectedAction)
                {
                    _pine.Write16(actionAddress, originalId);
                    if (_pine.Read16(actionAddress) != originalId)
                        throw new InvalidOperationException("復元確認失敗：敵枠 " + slot.ToString("D2"));
                    restored++;
                }
                else if (currentId != originalId)
                {
                    // 他の処理が値を変更していた場合は上書きしない。
                    continue;
                }

                lock (_pendingLock)
                    _pending.Remove(slot);
            }
            catch
            {
                // 失敗した枠は次の停止時に再試行する。
            }
        }
        return restored;
    }

    private void RaiseStatus(string message)
    {
        if (StatusChanged == null || string.Equals(_lastStatus, message, StringComparison.Ordinal))
            return;
        _lastStatus = message;
        try { StatusChanged?.Invoke(message); } catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Dispose();
        _pine.Dispose();
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
                    IAsyncResult result = client.BeginConnect(IPAddress.Loopback, Port, null, null);
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
            return BitConverter.ToUInt32(Request(0x0F, Array.Empty<byte>(), 4), 0);
        }

        internal ushort Read16(uint address)
        {
            byte[] args = new byte[4];
            WriteUInt32(args, 0, address);
            return BitConverter.ToUInt16(Request(0x01, args, 2), 0);
        }

        internal void Write16(uint address, ushort value)
        {
            byte[] args = new byte[6];
            WriteUInt32(args, 0, address);
            args[4] = (byte)(value & 0xFF);
            args[5] = (byte)(value >> 8);
            Request(0x05, args, 0);
        }

        private byte[] Request(byte opcode, byte[] args, int expectedPayloadLength)
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
                        throw new InvalidOperationException("PINE応答サイズが不正です: " + responseLength);

                    byte[] response = ReadExactly(_stream, checked((int)responseLength - 4));
                    if (response[0] != 0)
                        throw new InvalidOperationException("PCSX2 PINEが要求を拒否しました。");

                    int actualPayloadLength = response.Length - 1;
                    if (actualPayloadLength != expectedPayloadLength)
                        throw new InvalidOperationException("PINE応答長が一致しません。");

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
                    throw new InvalidOperationException("PINE接続が途中で切断されました。");
                offset += read;
            }
            return data;
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
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