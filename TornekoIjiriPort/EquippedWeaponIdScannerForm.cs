using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal static class HayabusaDoubleAttackMonitor
{
    private const string PineHost = "127.0.0.1";
    private const int PinePort = 28011;

    private const uint EquippedWeaponAddress = 0x006A54A4;
    private const uint HayabusaWeaponValue = 0x0000DFD8;

    private const uint AttackPatchAddress = 0x0020B264;
    private const uint AttackPatchValue = 0x24020001;

    private static System.Windows.Forms.Timer? _timer;
    private static Label? _statusLabel;

    private static int _processId;
    private static uint? _originalInstruction;
    private static bool _patchApplied;
    private static bool _exitHooked;

    private static string StatePath
    {
        get
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TornekoIjiriNext");

            return Path.Combine(
                directory,
                "HayabusaDoubleAttack.state");
        }
    }

    public static void Start(Label statusLabel)
    {
        _statusLabel = statusLabel;

        try
        {
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId <= 0)
            {
                throw new InvalidOperationException(
                    "先にPCSX2へ接続してください。");
            }

            int pid = Pcsx2LiveSession.ProcessId;

            if (_timer != null &&
                _timer.Enabled &&
                _processId == pid)
            {
                SetStatus(
                    "はやぶさの爪の自動監視は既に動作中です。");
                return;
            }

            if (_patchApplied &&
                _originalInstruction.HasValue)
            {
                TryRestorePatch();
            }

            _processId = pid;

            uint currentInstruction =
                ReadPine32(AttackPatchAddress);

            uint savedOriginal;

            if (currentInstruction == AttackPatchValue)
            {
                if (!TryLoadState(pid, out savedOriginal))
                {
                    throw new InvalidOperationException(
                        "二回攻撃コードが既に存在しますが、" +
                        "元の命令を保存した記録がありません。\r\n" +
                        "安全のため自動監視を開始しません。");
                }

                _originalInstruction = savedOriginal;
                _patchApplied = true;
            }

            if (currentInstruction != AttackPatchValue)
            {
                _originalInstruction = currentInstruction;
                _patchApplied = false;

                SaveState(pid, currentInstruction);
            }

            if (!_exitHooked)
            {
                Application.ApplicationExit += OnApplicationExit;
                _exitHooked = true;
            }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 500;
            _timer.Tick += (_, _) => Poll();
            _timer.Start();

            SetStatus(
                "自動監視開始。はやぶさの爪を確認しています。");

            Poll();
        }
        catch (Exception ex)
        {
            SetStatus(
                "二回攻撃の自動監視を開始できません: " +
                ex.Message);

            MessageBox.Show(
                ex.ToString(),
                "自動監視の開始エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public static void ShowStatus(Label statusLabel)
    {
        _statusLabel = statusLabel;

        if (_timer != null && _timer.Enabled)
        {
            SetStatus(
                "二回攻撃の自動監視は動作中です。");
            return;
        }

        SetStatus(
            "はやぶさの爪への変更ボタンを押すと自動監視を開始します。");
    }

    private static void Poll()
    {
        if (_timer == null || !_timer.Enabled)
            return;

        try
        {
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId != _processId)
            {
                SetStatus(
                    "PCSX2の接続状態が変化しました。復元を試みています。");

                TryRestorePatch();

                _timer.Stop();
                return;
            }

            uint weaponValue =
                ReadPine32(EquippedWeaponAddress) & 0xFFFF;

            uint currentInstruction =
                ReadPine32(AttackPatchAddress);

            bool isHayabusa =
                weaponValue == HayabusaWeaponValue;

            if (isHayabusa)
            {
                if (currentInstruction == AttackPatchValue)
                {
                    _patchApplied = true;

                    SetStatus(
                        "はやぶさの爪を検出。二回攻撃は有効です。");

                    return;
                }

                if (!_originalInstruction.HasValue ||
                    _originalInstruction.Value == AttackPatchValue)
                {
                    _originalInstruction = currentInstruction;
                    SaveState(_processId, currentInstruction);
                }

                WritePine32(
                    AttackPatchAddress,
                    AttackPatchValue);

                uint verification =
                    ReadPine32(AttackPatchAddress);

                if (verification != AttackPatchValue)
                {
                    throw new IOException(
                        "二回攻撃コードの読み戻し検証に失敗しました。\r\n" +
                        "実際値: 0x" +
                        verification.ToString("X8"));
                }

                _patchApplied = true;

                SetStatus(
                    "はやぶさの爪を検出。二回攻撃を自動適用しました。");

                return;
            }

            if (_patchApplied)
            {
                if (currentInstruction == AttackPatchValue)
                {
                    if (!_originalInstruction.HasValue)
                    {
                        throw new InvalidOperationException(
                            "復元する元の命令がありません。");
                    }

                    WritePine32(
                        AttackPatchAddress,
                        _originalInstruction.Value);

                    uint verification =
                        ReadPine32(AttackPatchAddress);

                    if (verification != _originalInstruction.Value)
                    {
                        throw new IOException(
                            "元の命令への復元に失敗しました。\r\n" +
                            "期待値: 0x" +
                            _originalInstruction.Value.ToString("X8") +
                            "\r\n実際値: 0x" +
                            verification.ToString("X8"));
                    }

                    _patchApplied = false;

                    SetStatus(
                        "はやぶさの爪以外を検出。元の命令へ復元しました。");

                    return;
                }

                _patchApplied = false;
                _originalInstruction = currentInstruction;
                SaveState(_processId, currentInstruction);

                SetStatus(
                    "適用先の命令が外部から変更されました。状態を再取得しました。");

                return;
            }

            SetStatus(
                "監視中。装備値: 0x" +
                weaponValue.ToString("X4") +
                " / はやぶさの爪ではありません。");
        }
        catch (Exception ex)
        {
            SetStatus(
                "自動監視エラー: " + ex.Message);
        }
    }

    private static byte[] ReadExact(
        NetworkStream stream,
        int count)
    {
        byte[] result = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int received = stream.Read(
                result,
                offset,
                count - offset);

            if (received <= 0)
            {
                throw new IOException(
                    "PINE接続が途中で切断されました。");
            }

            offset += received;
        }

        return result;
    }

    private static uint ReadUInt32(
        byte[] data,
        int offset)
    {
        return (uint)(
            data[offset] |
            (data[offset + 1] << 8) |
            (data[offset + 2] << 16) |
            (data[offset + 3] << 24));
    }

    private static void WriteUInt32(
        byte[] data,
        int offset,
        uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static byte[] SendPineCommand(byte[] command)
    {
        byte[] request = new byte[4 + command.Length];

        WriteUInt32(request, 0, (uint)request.Length);

        Buffer.BlockCopy(
            command,
            0,
            request,
            4,
            command.Length);

        using var client = new TcpClient();

        client.ReceiveTimeout = 4000;
        client.SendTimeout = 4000;
        client.Connect(PineHost, PinePort);

        using NetworkStream stream = client.GetStream();

        stream.Write(request, 0, request.Length);
        stream.Flush();

        byte[] header = ReadExact(stream, 4);
        uint responseLength = ReadUInt32(header, 0);

        if (responseLength < 5 ||
            responseLength > 1024)
        {
            throw new IOException(
                "PINE応答サイズが不正です: " +
                responseLength);
        }

        return ReadExact(
            stream,
            (int)responseLength - 4);
    }

    private static uint ReadPine32(uint address)
    {
        byte[] command = new byte[5];

        command[0] = 2;
        WriteUInt32(command, 1, address);

        byte[] response = SendPineCommand(command);

        if (response.Length != 5)
        {
            throw new IOException(
                "PINE Read32応答サイズが一致しません。");
        }

        if (response[0] != 0)
        {
            throw new IOException(
                "PINE Read32失敗。ステータス: " +
                response[0].ToString("X2"));
        }

        return ReadUInt32(response, 1);
    }

    private static void WritePine32(
        uint address,
        uint value)
    {
        byte[] command = new byte[9];

        command[0] = 6;
        WriteUInt32(command, 1, address);
        WriteUInt32(command, 5, value);

        byte[] response = SendPineCommand(command);

        if (response.Length != 1)
        {
            throw new IOException(
                "PINE Write32応答サイズが一致しません。");
        }

        if (response[0] != 0)
        {
            throw new IOException(
                "PINE Write32失敗。ステータス: " +
                response[0].ToString("X2"));
        }
    }

    private static void SaveState(
        int pid,
        uint original)
    {
        string directory = Path.GetDirectoryName(StatePath)!;

        Directory.CreateDirectory(directory);

        string content =
            pid.ToString(CultureInfo.InvariantCulture) +
            Environment.NewLine +
            original.ToString("X8", CultureInfo.InvariantCulture);

        File.WriteAllText(
            StatePath,
            content,
            new UTF8Encoding(false));
    }

    private static bool TryLoadState(
        int pid,
        out uint original)
    {
        original = 0;

        if (!File.Exists(StatePath))
            return false;

        string[] lines = File.ReadAllLines(StatePath);

        if (lines.Length < 2)
            return false;

        if (!int.TryParse(
            lines[0],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int savedPid))
        {
            return false;
        }

        if (savedPid != pid)
            return false;

        if (!uint.TryParse(
            lines[1],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out original))
        {
            return false;
        }

        if (original == AttackPatchValue)
            return false;

        return true;
    }

    private static bool TryRestorePatch()
    {
        try
        {
            if (!_originalInstruction.HasValue)
                return false;

            uint current =
                ReadPine32(AttackPatchAddress);

            if (current == AttackPatchValue)
            {
                WritePine32(
                    AttackPatchAddress,
                    _originalInstruction.Value);

                uint verification =
                    ReadPine32(AttackPatchAddress);

                if (verification != _originalInstruction.Value)
                {
                    throw new IOException(
                        "復元後の読み戻し値が一致しません。");
                }
            }

            _patchApplied = false;

            if (File.Exists(StatePath))
                File.Delete(StatePath);

            SetStatus(
                "二回攻撃コードを元の命令へ復元しました。");

            return true;
        }
        catch (Exception ex)
        {
            SetStatus(
                "二回攻撃コードの復元に失敗しました: " +
                ex.Message);

            return false;
        }
    }

    private static void OnApplicationExit(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (_patchApplied &&
                _originalInstruction.HasValue)
            {
                TryRestorePatch();
            }
        }
        catch
        {
        }

        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }
    }

    private static void SetStatus(string message)
    {
        Label? label = _statusLabel;

        if (label == null ||
            label.IsDisposed ||
            label.Disposing)
        {
            return;
        }

        try
        {
            if (label.InvokeRequired)
            {
                label.BeginInvoke(
                    new Action(() =>
                    {
                        if (!label.IsDisposed)
                            label.Text = message;
                    }));

                return;
            }

            label.Text = message;
        }
        catch
        {
        }
    }
}