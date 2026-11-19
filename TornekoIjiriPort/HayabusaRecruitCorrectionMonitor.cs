using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal static class HayabusaRecruitCorrectionMonitor
{
    private const string PineHost = "127.0.0.1";
    private const int PinePort = 28011;

    private const long ItemNameAddress = 0x1B322C27C42L;
    private const string ExpectedItemName = "ﾊﾔぶｻﾉ爪";

    private const uint EquippedWeaponAddress = 0x006A54A4;
    private const uint HayabusaWeaponValue = 0x0000DFD8;

    private const uint RecruitCorrectionAddress = 0x008F39C4;

    // -9999 = 0xD8F1
    private const ushort OriginalRecruitValue = 0xD8F1;

    // 70 = 0x0046
    private const ushort TargetRecruitValue = 70;

    private static System.Windows.Forms.Timer? _timer;
    private static bool _polling;
    private static bool _exitHooked;
    private static string _status = "";

    private static readonly Encoding Cp932 = CreateCp932();

    private static string StatePath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriNext",
            "HayabusaRecruitCorrection.state");

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

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    public static void Start()
    {
        if (_timer != null)
            return;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        _timer.Tick += Poll;
        _timer.Start();

        if (!_exitHooked)
        {
            Application.ApplicationExit += OnApplicationExit;
            _exitHooked = true;
        }

        SetStatus("勧誘修正値の自動監視を開始しました。PCSX2接続待ちです。");
    }

    private static async void Poll(
        object? sender,
        EventArgs e)
    {
        if (_polling)
            return;

        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            SetStatus("勧誘修正値監視中。PCSX2接続待ちです。");
            return;
        }

        _polling = true;
        int processId = Pcsx2LiveSession.ProcessId;

        try
        {
            await System.Threading.Tasks.Task.Run(
                () => Evaluate(processId));
        }
        catch (Exception ex)
        {
            SetStatus("勧誘修正値の自動変更エラー: " + ex.Message);
        }
        finally
        {
            _polling = false;
        }
    }

    private static void Evaluate(int processId)
    {


        uint equippedWeapon =
            ReadPine32(EquippedWeaponAddress) & 0xFFFF;

        bool isHayabusa =
            equippedWeapon == HayabusaWeaponValue;

        uint currentWord =
            ReadPine32(RecruitCorrectionAddress);

        ushort currentValue =
            (ushort)(currentWord & 0xFFFF);

        bool hasState = TryLoadState(
            out int savedProcessId,
            out ushort originalValue);

        if (hasState && savedProcessId != processId)
        {
            DeleteState();
            hasState = false;
        }

        if (isHayabusa)
        {
            if (!hasState)
            {
                originalValue = OriginalRecruitValue;
                SaveState(processId, originalValue);
            }

            if (currentValue != TargetRecruitValue)
            {
                uint newWord =
                    (currentWord & 0xFFFF0000u) |
                    TargetRecruitValue;

                WritePine32(
                    RecruitCorrectionAddress,
                    newWord);

                uint verification =
                    ReadPine32(RecruitCorrectionAddress);

                ushort verifiedValue =
                    (ushort)(verification & 0xFFFF);

                if (verifiedValue != TargetRecruitValue)
                {
                    throw new IOException(
                        "勧誘修正値の書き込み検証に失敗しました。" +
                        " 読み戻し値: " +
                        verifiedValue.ToString());
                }

                SetStatus(
                    "はやぶさの爪を検出。勧誘修正値を70に変更しました。");
                return;
            }

            SetStatus(
                "はやぶさの爪を検出。勧誘修正値70は適用済みです。");
            return;
        }

        if (hasState)
        {
            if (currentValue != originalValue)
            {
                uint restoredWord =
                    (currentWord & 0xFFFF0000u) |
                    originalValue;

                WritePine32(
                    RecruitCorrectionAddress,
                    restoredWord);

                uint verification =
                    ReadPine32(RecruitCorrectionAddress);

                ushort verifiedValue =
                    (ushort)(verification & 0xFFFF);

                if (verifiedValue != originalValue)
                {
                    throw new IOException(
                        "勧誘修正値の復元に失敗しました。" +
                        " 読み戻し値: " +
                        verifiedValue.ToString());
                }
            }

            DeleteState();

            SetStatus(
                "はやぶさの爪以外を検出。勧誘修正値を-9999に戻しました。");
            return;
        }

        SetStatus(
            "監視中。装備ID: 0x" +
            equippedWeapon.ToString("X4") +
            " / 勧誘修正値: " +
            unchecked((short)currentValue).ToString());
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
                "PCSX2のアイテム名を読み取れません。" +
                " Win32エラー: " +
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
                    "アイテム名の読み取りに失敗しました。");
            }

            int length = Array.IndexOf(buffer, (byte)0);

            if (length < 0)
            {
                throw new IOException(
                    "アイテム名の終端を確認できません。");
            }

            return Cp932.GetString(buffer, 0, length);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static uint ReadPine32(uint address)
    {
        return ActionAutoRestoreService.ReadShared32(address);
    }

    private static void WritePine32(uint address, uint value)
    {
        ActionAutoRestoreService.WriteShared32(address, value);
    }
    private static bool TryLoadState(
        out int processId,
        out ushort originalValue)
    {
        processId = 0;
        originalValue = OriginalRecruitValue;

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

        if (!ushort.TryParse(
            parts[1],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out originalValue))
            return false;

        return true;
    }

    private static void SaveState(
        int processId,
        ushort originalValue)
    {
        string directory =
            Path.GetDirectoryName(StatePath)!;

        Directory.CreateDirectory(directory);

        string temporaryPath = StatePath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            processId.ToString(CultureInfo.InvariantCulture) +
            "|" +
            originalValue.ToString("X4"));

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

    private static void OnApplicationExit(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (Pcsx2LiveSession.IsConnected &&
                Pcsx2LiveSession.ProcessId > 0)
            {
                int processId = Pcsx2LiveSession.ProcessId;

                bool hasState = TryLoadState(
                    out int savedProcessId,
                    out ushort originalValue);

                if (hasState && savedProcessId == processId)
                {
                    uint currentWord =
                        ReadPine32(RecruitCorrectionAddress);

                    uint restoredWord =
                        (currentWord & 0xFFFF0000u) |
                        originalValue;

                    WritePine32(
                        RecruitCorrectionAddress,
                        restoredWord);

                    DeleteState();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                "[勧誘修正値] 終了時の復元エラー: " +
                ex.Message);
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
        if (_status == message)
            return;

        _status = message;
        Debug.WriteLine("[勧誘修正値] " + message);
    }
}
