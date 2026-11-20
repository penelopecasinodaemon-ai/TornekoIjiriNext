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

    // はやぶさの爪のアイテム名が格納されているアドレス
    private const long ItemNameAddress = 0x1B322C27C42L;
    private const string ExpectedItemName = "ﾊﾔぶｻﾉ爪";

    // 装備中武器ID
    private const uint EquippedWeaponAddress = 0x006A54A4;
    private const uint HayabusaWeaponValue = 0x0000DFD8;

    // モンスター勧誘後に装備表示と食い違った武器ID。
    // この値だけでは装備解除と判定しない。
    private const uint AmbiguousAfterRecruitmentWeaponValue = 0x0000516C;

    // 二回攻撃命令
    private const uint AttackPatchAddress = 0x0020B264;
    private const uint AttackPatchValue = 0x24020001;

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    private static System.Windows.Forms.Timer? _timer;
    private static Label? _statusLabel;

    private static bool _polling;
    private static bool _exitHooked;

    private static string _status =
        "自動監視の起動待ちです。";

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
        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(932);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(
        IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr read);

    // アプリ起動時に呼び出す。
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

        SetStatus(
            "自動監視を開始しました。PCSX2接続待ちです。");
    }

    // 設定画面から呼び出す。
    public static void Start(Label statusLabel)
    {
        _statusLabel = statusLabel;
        Start();

        SetStatus(
            "はやぶさの爪の自動監視が動作中です。");
    }

    // 状態確認ボタンから呼び出す。
    public static void ShowStatus(Label statusLabel)
    {
        _statusLabel = statusLabel;

        if (_timer != null && _timer.Enabled)
        {
            SetStatus(_status);
            return;
        }

        SetStatus(
            "自動監視は停止しています。");
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
            SetStatus("自動監視中。PCSX2接続待ちです。");
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
            WriteDiagnostic(
                "POLL ERROR: " + ex.ToString());

            SetStatus("自動監視エラー: " + ex.Message);
        }
        finally
        {
            _polling = false;
        }
    }

    private static void Evaluate(int processId)
    {
        const uint ExpectedOriginalInstruction = 0x0C0B3934u;

        uint rawWeaponValue =
            ReadPine32(EquippedWeaponAddress);

        uint equippedWeapon =
            rawWeaponValue & 0xFFFFu;

        uint currentInstruction =
            ReadPine32(AttackPatchAddress);

        bool isHayabusaEquipped =
            equippedWeapon == HayabusaWeaponValue;

        bool hasState = TryLoadState(
            out int savedProcessId,
            out uint originalInstruction);

        if (isHayabusaEquipped)
        {
            if (currentInstruction == AttackPatchValue)
            {
                if (hasState && savedProcessId == processId)
                {
                    SetStatus("はやぶさの爪を検出：二回攻撃パッチ適用中です。");
                    return;
                }

                WriteDiagnostic(
                    "PATCH CONFLICT: パッチ命令は存在しますが、対応する復元情報がありません。" +
                    " PID=" + processId +
                    " Instruction=0x" + currentInstruction.ToString("X8"));

                SetStatus("パッチ状態が不明です。安全のため書き込みを停止しました。");
                return;
            }

            if (hasState && savedProcessId != processId)
            {
                if (currentInstruction != ExpectedOriginalInstruction)
                {
                    WriteDiagnostic(
                        "STALE STATE CONFLICT" +
                        " PID=" + processId +
                        " SavedPID=" + savedProcessId +
                        " Instruction=0x" + currentInstruction.ToString("X8"));

                    SetStatus("以前の復元情報と命令が一致しません。書き込みを停止しました。");
                    return;
                }

                DeleteState();
                hasState = false;
            }

            if (hasState && savedProcessId == processId)
            {
                if (currentInstruction != originalInstruction)
                {
                    WriteDiagnostic(
                        "ORIGINAL INSTRUCTION MISMATCH" +
                        " PID=" + processId +
                        " Expected=0x" + originalInstruction.ToString("X8") +
                        " Actual=0x" + currentInstruction.ToString("X8"));

                    SetStatus("元の命令が予想と異なります。安全のため停止しました。");
                    return;
                }
            }

            if (!hasState)
            {
                if (currentInstruction != ExpectedOriginalInstruction)
                {
                    WriteDiagnostic(
                        "UNEXPECTED ORIGINAL INSTRUCTION" +
                        " PID=" + processId +
                        " Instruction=0x" + currentInstruction.ToString("X8"));

                    SetStatus(
                        "パッチ対象命令が想定外です: 0x" +
                        currentInstruction.ToString("X8"));

                    return;
                }

                originalInstruction = currentInstruction;

                SaveState(
                    processId,
                    originalInstruction);
            }

            try
            {
                WritePine32(
                    AttackPatchAddress,
                    AttackPatchValue);

                uint verify =
                    ReadPine32(AttackPatchAddress);

                if (verify != AttackPatchValue)
                {
                    throw new InvalidOperationException(
                        "二回攻撃パッチの書き込み検証に失敗しました。" +
                        " 読み戻し値: 0x" + verify.ToString("X8"));
                }

                WriteDiagnostic(
                    "APPLY SUCCESS" +
                    " PID=" + processId +
                    " WeaponID=0x" + equippedWeapon.ToString("X4") +
                    " Original=0x" + originalInstruction.ToString("X8") +
                    " Applied=0x" + verify.ToString("X8"));

                SetStatus("はやぶさの爪を検出：二回攻撃パッチ適用済みです。");
            }
            catch
            {
                TryRestoreAfterWriteFailure(
                    processId,
                    originalInstruction);

                uint afterFailure =
                    ReadPine32(AttackPatchAddress);

                if (afterFailure == originalInstruction)
                    DeleteState();

                throw;
            }

            return;
        }

        if (equippedWeapon == AmbiguousAfterRecruitmentWeaponValue &&
            hasState &&
            savedProcessId == processId &&
            currentInstruction == AttackPatchValue)
        {
            WriteDiagnostic(
                "HOLD AMBIGUOUS WEAPON" +
                " PID=" + processId +
                " WeaponID=0x" + equippedWeapon.ToString("X4") +
                " Patch=0x" + currentInstruction.ToString("X8") +
                " Reason=Recruitment-related equipment read mismatch");

            SetStatus(
                "勧誘後の装備IDが不確定です。誤解除防止のため二回攻撃を維持しています。");

            return;
        }

        if (!hasState)
        {
            SetStatus("はやぶさの爪は装備されていません。");
            return;
        }

        if (savedProcessId != processId)
        {
            SetStatus("別のPCSX2プロセスの復元情報があります。");
            return;
        }

        if (currentInstruction == AttackPatchValue)
        {
            WritePine32(
                AttackPatchAddress,
                originalInstruction);

            uint verify =
                ReadPine32(AttackPatchAddress);

            if (verify != originalInstruction)
            {
                throw new InvalidOperationException(
                    "二回攻撃パッチの復元に失敗しました。" +
                    " 読み戻し値: 0x" + verify.ToString("X8"));
            }

            DeleteState();

            WriteDiagnostic(
                "RESTORE SUCCESS" +
                " PID=" + processId +
                " WeaponID=0x" + equippedWeapon.ToString("X4") +
                " Restored=0x" + verify.ToString("X8"));

            SetStatus("はやぶさの爪を解除：元の攻撃命令へ復元しました。");
            return;
        }

        if (currentInstruction == originalInstruction)
        {
            DeleteState();

            WriteDiagnostic(
                "RESTORE ALREADY ORIGINAL" +
                " PID=" + processId +
                " Instruction=0x" + currentInstruction.ToString("X8"));

            SetStatus("はやぶさの爪は未装備です。元の命令を確認しました。");
            return;
        }

        WriteDiagnostic(
            "RESTORE CONFLICT" +
            " PID=" + processId +
            " ExpectedPatch=0x" + AttackPatchValue.ToString("X8") +
            " ExpectedOriginal=0x" + originalInstruction.ToString("X8") +
            " Actual=0x" + currentInstruction.ToString("X8"));

        SetStatus("復元対象の命令が想定外です。安全のため復元を停止しました。");
    }
    private static void TryRestoreAfterWriteFailure(
        int processId,
        uint originalInstruction)
    {
        try
        {
            if (Pcsx2LiveSession.ProcessId != processId)
                return;

            uint current =
                ReadPine32(AttackPatchAddress);

            if (current == AttackPatchValue)
            {
                WritePine32(
                    AttackPatchAddress,
                    originalInstruction);

                uint verify =
                    ReadPine32(AttackPatchAddress);

                if (verify == originalInstruction)
                    DeleteState();
            }
        }
        catch
        {
            // 復元できなかった場合は状態ファイルを残す。
        }
    }

    private static string ReadItemName(int processId)
    {
        IntPtr process = OpenProcess(
            ProcessVmRead | ProcessQueryInformation,
            false,
            processId);

        if (process == IntPtr.Zero)
        {
            throw new IOException(
                "アイテム名の読み取り用にPCSX2を開けません。" +
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

            if (!success ||
                read.ToUInt64() != (ulong)buffer.Length)
            {
                throw new IOException(
                    "アイテム名の読み取りに失敗しました。" +
                    " Win32エラー: " +
                    Marshal.GetLastWin32Error());
            }

            int length =
                Array.IndexOf(buffer, (byte)0);

            if (length < 0)
            {
                throw new IOException(
                    "アイテム名の終端を確認できません。");
            }

            return Cp932.GetString(
                buffer,
                0,
                length);
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
        out uint originalInstruction)
    {
        processId = 0;
        originalInstruction = 0;

        if (!File.Exists(StatePath))
            return false;

        string content =
            File.ReadAllText(StatePath).Trim();

        string[] parts =
            content.Split(
                new[] { '|', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
            return false;

        if (!int.TryParse(
            parts[0],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out processId))
        {
            return false;
        }

        if (!uint.TryParse(
            parts[1],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out originalInstruction))
        {
            return false;
        }

        return processId > 0;
    }

    private static void SaveState(
        int processId,
        uint originalInstruction)
    {
        string directory =
            Path.GetDirectoryName(StatePath)!;

        Directory.CreateDirectory(directory);

        string temporaryPath =
            StatePath + ".tmp";

        string content =
            processId.ToString(
                CultureInfo.InvariantCulture) +
            "|" +
            originalInstruction.ToString("X8");

        File.WriteAllText(
            temporaryPath,
            content,
            Encoding.UTF8);

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
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId <= 0)
            {
                return;
            }

            int processId =
                Pcsx2LiveSession.ProcessId;

            bool hasState = TryLoadState(
                out int savedProcessId,
                out uint originalInstruction);

            if (!hasState ||
                savedProcessId != processId)
            {
                return;
            }

            uint current =
                ReadPine32(AttackPatchAddress);

            if (current == AttackPatchValue)
            {
                WritePine32(
                    AttackPatchAddress,
                    originalInstruction);

                uint verify =
                    ReadPine32(AttackPatchAddress);

                if (verify == originalInstruction)
                    DeleteState();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                "[はやぶさの爪] 終了時の復元に失敗: " +
                ex.Message);
        }
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TornekoIjiriNext");

            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                "HayabusaDoubleAttack.log");

            string line =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                " " + message + Environment.NewLine;

            File.AppendAllText(
                path,
                line,
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                "[はやぶさの爪] ログ出力失敗: " + ex.Message);
        }
    }

    private static void SetStatus(string value)
    {
        if (_status == value)
            return;

        _status = value;

        Debug.WriteLine(
            "[はやぶさの爪] " + value);

        if (_statusLabel != null &&
            !_statusLabel.IsDisposed &&
            _statusLabel.IsHandleCreated)
        {
            Label label = _statusLabel;

            try
            {
                if (label.InvokeRequired)
                {
                    label.BeginInvoke(new Action(() =>
                    {
                        if (!label.IsDisposed)
                            label.Text = value;
                    }));
                }

                if (!label.InvokeRequired &&
                    !label.IsDisposed)
                {
                    label.Text = value;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
