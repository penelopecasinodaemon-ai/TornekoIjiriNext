using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal static class HayabusaDoubleAttackMonitor
{
    private const string PineHost = "127.0.0.1";
    private const int PinePort = 28011;

    private const long ItemNameAddress = 0x1B322C27C42L;
    private const string ExpectedItemName = "ﾊﾔぶｻﾉ爪";

    private const uint EquippedWeaponAddress = 0x006A54A4;
    private const uint HayabusaWeaponValue = 0x0000DFD8;

    private const uint AttackPatchAddress = 0x0020B264;
    private const uint AttackPatchValue = 0x24020001;

    private static System.Windows.Forms.Timer? _timer;
    private static bool _polling;
    private static string _status = "自動監視の起動待ちです。";

    private static readonly Encoding Cp932 = CreateCp932();

    private static string StatePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriNext",
            "HayabusaDoubleAttack.state");

    public static string Status => _status;

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr read);

    public static void Start()
    {
        if (_timer != null)
            return;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };

        _timer.Tick += Poll;
        _timer.Start();

        SetStatus("自動監視を開始しました。PCSX2接続待ちです。");
    }

    private static void Poll(object? sender, EventArgs e)
    {
        if (_polling)
            return;

        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            SetStatus("自動監視中。PCSX2接続待ちです。");
            return;
        }

        _polling = true;

        try
        {
            Evaluate(Pcsx2LiveSession.ProcessId);
        }
        catch (Exception ex)
        {
            SetStatus("自動監視エラー: " + ex.Message);
        }
        finally
        {
            _polling = false;
        }
    }

    private static void Evaluate(int processId)
    {
        string itemName = ReadItemName(processId);

        uint equippedWeapon = ReadPine32(
            EquippedWeaponAddress);

        uint currentInstruction = ReadPine32(
            AttackPatchAddress);

        bool shouldEnable =
            string.Equals(
                itemName,
                ExpectedItemName,
                StringComparison.Ordinal) &&
            equippedWeapon == HayabusaWeaponValue;

        bool hasState = TryLoadState(
            out int savedProcessId,
            out uint originalInstruction);

        if (shouldEnable)
        {
            if (currentInstruction == AttackPatchValue)
            {
                if (hasState && savedProcessId == processId)
                    SetStatus("はやぶさの爪を確認。二回攻撃は有効です。");

                if (hasState && savedProcessId != processId)
                    SetStatus("二回攻撃コードは適用済みですが、保存された元命令のPIDが一致しません。");

                if (!hasState)
                    SetStatus("二回攻撃コードは既に適用されていますが、元命令の保存データがありません。");

                return;
            }

            if (hasState && savedProcessId == processId)
            {
                if (currentInstruction != originalInstruction)
                {
                    SetStatus("二回攻撃の適用を保留中。対象命令が保存済みの元命令と一致しません。");
                    return;
                }

                DeleteState();
                hasState = false;
            }

            if (hasState && savedProcessId != processId)
            {
                DeleteState();
                hasState = false;
            }

            uint original = currentInstruction;

            SaveState(processId, original);

            try
            {
                WritePine32(
                    AttackPatchAddress,
                    AttackPatchValue);

                uint verify = ReadPine32(
                    AttackPatchAddress);

                if (verify != AttackPatchValue)
                    throw new IOException(
                        "二回攻撃コードの読み戻し検証に失敗しました。");

                SetStatus(
                    "はやぶさの爪を検出。二回攻撃を有効化しました。元命令: 0x" +
                    original.ToString("X8"));
            }
            catch (Exception writeError)
            {
                bool restored = false;

                try
                {
                    uint current = ReadPine32(
                        AttackPatchAddress);

                    if (current == AttackPatchValue)
                    {
                        WritePine32(
                            AttackPatchAddress,
                            original);

                        restored =
                            ReadPine32(AttackPatchAddress) == original;
                    }
                }
                catch
                {
                }

                if (restored)
                    DeleteState();

                throw new IOException(
                    "二回攻撃の適用に失敗しました: " +
                    writeError.Message,
                    writeError);
            }

            return;
        }

        if (currentInstruction == AttackPatchValue)
        {
            if (hasState && savedProcessId == processId)
            {
                WritePine32(
                    AttackPatchAddress,
                    originalInstruction);

                uint verify = ReadPine32(
                    AttackPatchAddress);

                if (verify != originalInstruction)
                    throw new IOException(
                        "元の命令への復元に失敗しました。");

                DeleteState();

                SetStatus(
                    "はやぶさの爪以外を検出。二回攻撃を解除しました。元命令: 0x" +
                    originalInstruction.ToString("X8"));

                return;
            }

            SetStatus(
                "二回攻撃コードが残っていますが、同じPCSX2プロセスの元命令保存データがありません。安全のため復元を保留しています。");

            return;
        }

        if (hasState && savedProcessId != processId)
        {
            DeleteState();
            hasState = false;
        }

        if (hasState && savedProcessId == processId)
        {
            if (currentInstruction == originalInstruction)
            {
                DeleteState();
                hasState = false;
            }

            if (currentInstruction != originalInstruction)
            {
                SetStatus("対象命令が外部から変更されています。安全のため監視による書き換えを保留しています。");
                return;
            }
        }

        SetStatus(
            "自動監視中。アイテム名: " + itemName +
            " / 装備ID: 0x" + equippedWeapon.ToString("X8") +
            " / 二回攻撃: 無効");
    }

    private static string ReadItemName(int processId)
    {
        IntPtr process = OpenProcess(
            0x0010 | 0x0400,
            false,
            processId);

        if (process == IntPtr.Zero)
        {
            throw new IOException(
                "アイテム名の読み取り用にPCSX2を開けません。Win32エラー: " +
                Marshal.GetLastWin32Error());
        }

        try
        {
            byte[] buffer = new byte[64];
            UIntPtr read;

            bool success = ReadProcessMemory(
                process,
                new IntPtr(ItemNameAddress),
                buffer,
                new UIntPtr((uint)buffer.Length),
                out read);

            if (!success || read.ToUInt64() < 2)
            {
                throw new IOException(
                    "アイテム名の読み取りに失敗しました。Win32エラー: " +
                    Marshal.GetLastWin32Error());
            }

            int length = Array.IndexOf(buffer, (byte)0);

            if (length < 0)
                throw new IOException(
                    "アイテム名の終端を確認できません。");

            return Cp932.GetString(buffer, 0, length);
        }
        finally
        {
            CloseHandle(process);
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
                throw new IOException(
                    "PINE接続が途中で切断されました。");

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

        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;
        client.Connect(PineHost, PinePort);

        using NetworkStream stream = client.GetStream();

        stream.Write(request, 0, request.Length);
        stream.Flush();

        byte[] header = ReadExact(stream, 4);
        uint responseLength = ReadUInt32(header, 0);

        if (responseLength < 5 || responseLength > 1024)
            throw new IOException(
                "PINE応答サイズが不正です: " + responseLength);

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
            throw new IOException(
                "PINE Read32応答サイズが一致しません。");

        if (response[0] != 0)
            throw new IOException(
                "PINE Read32に失敗しました。ステータス: " +
                response[0].ToString("X2"));

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
            throw new IOException(
                "PINE Write32応答サイズが一致しません。");

        if (response[0] != 0)
            throw new IOException(
                "PINE Write32に失敗しました。ステータス: " +
                response[0].ToString("X2"));
    }

    private static bool TryLoadState(
        out int processId,
        out uint originalInstruction)
    {
        processId = 0;
        originalInstruction = 0;

        if (!File.Exists(StatePath))
            return false;

        string[] parts = File.ReadAllText(StatePath)
            .Trim()
            .Split('|');

        if (parts.Length != 2)
            return false;

        if (!int.TryParse(
            parts[0],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out processId))
            return false;

        if (!uint.TryParse(
            parts[1],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out originalInstruction))
            return false;

        return true;
    }

    private static void SaveState(
        int processId,
        uint originalInstruction)
    {
        string directory = Path.GetDirectoryName(StatePath)!;

        Directory.CreateDirectory(directory);

        string temporaryPath = StatePath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            processId.ToString(CultureInfo.InvariantCulture) +
            "|" +
            originalInstruction.ToString("X8"));

        File.Move(
            temporaryPath,
            StatePath,
            true);
    }

    private static void DeleteState()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }

    private static void SetStatus(string value)
    {
        if (_status == value)
            return;

        _status = value;
        Debug.WriteLine("[はやぶさの爪] " + value);
    }
}