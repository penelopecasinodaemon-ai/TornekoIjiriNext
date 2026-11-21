using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace TornekoIjiriPort;

internal sealed class ExtendedItemNameLiveSnapshotForm : Form
{
    private const int EeRamSize = 0x02000000;
    private const int ChunkSize = 0x00100000;

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    private readonly int _processId;
    private readonly Button _captureAButton = new Button();
    private readonly Button _captureBButton = new Button();
    private readonly Button _compareButton = new Button();
    private readonly Label _status = new Label();
    private readonly TextBox _output = new TextBox();

    private Snapshot? _snapshotA;
    private Snapshot? _snapshotB;
    private bool _busy;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        int size,
        out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private sealed class Snapshot
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public long EeBase { get; init; }
        public DateTime CapturedAt { get; init; }
    }

    public ExtendedItemNameLiveSnapshotForm(int processId)
    {
        _processId = processId;

        Text = "アイテム名表示・EE RAM差分診断";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(850, 580);
        Size = new Size(1120, 780);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var description = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(4, 4, 4, 10),
            Text =
                "読み取り専用のEE RAM差分診断です。\r\n" +
                "A: アイテム名を表示して対象アイテムを選択した状態で取得。\r\n" +
                "B: 同じ画面のまま別のアイテムへ選択を移動して取得。\r\n" +
                "両方のスナップショットを比較し、変化したメモリ領域を抽出します。\r\n" +
                "EE RAM 32MiBを読み取ります。ゲームメモリへの書き込みは行いません。"
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 4, 0, 8)
        };

        _captureAButton.Text = "A：現在のアイテムを記録";
        _captureAButton.Width = 210;
        _captureAButton.Height = 42;
        _captureAButton.Click += async (_, _) => await CaptureAsync(true);

        _captureBButton.Text = "B：選択変更後を記録";
        _captureBButton.Width = 210;
        _captureBButton.Height = 42;
        _captureBButton.Click += async (_, _) => await CaptureAsync(false);

        _compareButton.Text = "A・Bの差分を比較";
        _compareButton.Width = 190;
        _compareButton.Height = 42;
        _compareButton.Enabled = false;
        _compareButton.Click += (_, _) => CompareSnapshots();

        buttonPanel.Controls.Add(_captureAButton);
        buttonPanel.Controls.Add(_captureBButton);
        buttonPanel.Controls.Add(_compareButton);

        _status.Dock = DockStyle.Fill;
        _status.AutoSize = true;
        _status.Padding = new Padding(4, 4, 4, 8);
        _status.Text = "PCSX2 PID: " + _processId + "\r\nA・B未取得";

        _output.Dock = DockStyle.Fill;
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.ScrollBars = ScrollBars.Both;
        _output.WordWrap = false;
        _output.Font = new Font("Consolas", 9F);
        _output.HideSelection = false;

        root.Controls.Add(description, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        root.Controls.Add(_status, 0, 2);
        root.Controls.Add(_output, 0, 3);

        Controls.Add(root);
    }

    private void SetBusy(bool value)
    {
        _busy = value;

        _captureAButton.Enabled = !value;
        _captureBButton.Enabled = !value;
        _compareButton.Enabled =
            !value && _snapshotA != null && _snapshotB != null;
    }

    private void Append(string value)
    {
        _output.AppendText(value + Environment.NewLine);
    }

    private async Task CaptureAsync(bool slotA)
    {
        if (_busy)
            return;

        SetBusy(true);

        string slotName = slotA ? "A" : "B";
        _status.Text = slotName + "スナップショット取得中...";
        _output.Clear();

        try
        {
            Snapshot captured = await Task.Run(
                () => CaptureEeRam(_processId));

            if (slotA)
            {
                _snapshotA = captured;
            }

            if (!slotA)
            {
                _snapshotB = captured;
            }

            Append(slotName + "取得完了");
            Append("PID: " + _processId);
            Append("EE RAM Base: 0x" + captured.EeBase.ToString("X"));
            Append("サイズ: " + captured.Data.Length.ToString("N0") + " bytes");
            Append("取得時刻: " + captured.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            Append("メモリ書き込み: なし");

            _status.Text =
                "A: " + (_snapshotA == null ? "未取得" : "取得済み") +
                "　|　B: " + (_snapshotB == null ? "未取得" : "取得済み");

            if (_snapshotA != null && _snapshotB != null)
            {
                Append("");
                Append("A・Bが揃いました。「A・Bの差分を比較」を実行できます。");
            }
        }
        catch (Exception ex)
        {
            _status.Text = slotName + "取得エラー";
            Append(ex.ToString());
            MessageBox.Show(
                this,
                ex.ToString(),
                "EE RAM取得エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        SetBusy(false);
    }

    private static Snapshot CaptureEeRam(int processId)
    {
        long eeBase = Pcsx2MemoryLayoutBuilder.GetEEmemBase(processId);

        if (eeBase <= 0)
            throw new InvalidOperationException("EE RAM Baseが無効です。");

        IntPtr process = OpenProcess(
            ProcessVmRead | ProcessQueryInformation,
            false,
            processId);

        if (process == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "PCSX2プロセスを読み取り用に開けません。");
        }

        byte[] data = new byte[EeRamSize];

        try
        {
            int completed = 0;

            while (completed < EeRamSize)
            {
                int requested = Math.Min(
                    ChunkSize,
                    EeRamSize - completed);

                byte[] chunk = new byte[requested];

                IntPtr address = new IntPtr(
                    checked(eeBase + completed));

                bool ok = ReadProcessMemory(
                    process,
                    address,
                    chunk,
                    requested,
                    out IntPtr bytesRead);

                if (!ok || bytesRead.ToInt64() != requested)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "EE RAM読み取り失敗。Offset=0x" +
                        completed.ToString("X") +
                        " Requested=" + requested +
                        " Read=" + bytesRead.ToInt64());
                }

                Buffer.BlockCopy(
                    chunk,
                    0,
                    data,
                    completed,
                    requested);

                completed += requested;
            }
        }
        catch
        {
            CloseHandle(process);
            throw;
        }

        if (!CloseHandle(process))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "PCSX2プロセスハンドルを閉じられませんでした。");
        }

        return new Snapshot
        {
            Data = data,
            EeBase = eeBase,
            CapturedAt = DateTime.Now
        };
    }

    private void CompareSnapshots()
    {
        if (_snapshotA == null || _snapshotB == null)
        {
            MessageBox.Show(this, "AとBの両方を取得してください。");
            return;
        }

        if (_snapshotA.EeBase != _snapshotB.EeBase)
        {
            MessageBox.Show(
                this,
                "AとBのEE RAM Baseが異なります。比較を中止しました。",
                "基準アドレス不一致",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_snapshotA.Data.Length != _snapshotB.Data.Length)
        {
            MessageBox.Show(this, "スナップショットのサイズが異なります。");
            return;
        }

        try
        {
            var report = new StringBuilder();

            report.AppendLine("トルネコいじりネクスト アイテム名表示・EE RAM差分診断");
            report.AppendLine("PID: " + _processId);
            report.AppendLine("EE RAM Base: 0x" + _snapshotA.EeBase.ToString("X"));
            report.AppendLine("A取得: " + _snapshotA.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            report.AppendLine("B取得: " + _snapshotB.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            report.AppendLine("RAMサイズ: 0x" + EeRamSize.ToString("X"));
            report.AppendLine("書き込み: なし");
            report.AppendLine();

            AppendKnownRegion(
                report,
                "アイテムIDポインターテーブル",
                0x00100758,
                0x60);

            AppendKnownRegion(
                report,
                "item_164 ID・名前周辺",
                0x00105420,
                0x60);

            report.AppendLine();
            report.AppendLine("===== EE RAM変更箇所 =====");

            int index = 0;
            long changedBytes = 0;
            int groupCount = 0;
            int shownGroups = 0;
            const int MaximumGroups = 300;

            while (index < EeRamSize)
            {
                if (_snapshotA.Data[index] == _snapshotB.Data[index])
                {
                    index++;
                    continue;
                }

                int groupStart = index;
                int lastChanged = index;
                long groupChanged = 1;
                index++;

                while (index < EeRamSize)
                {
                    int probe = index;

                    while (probe < EeRamSize &&
                           probe - lastChanged <= 8 &&
                           _snapshotA.Data[probe] == _snapshotB.Data[probe])
                    {
                        probe++;
                    }

                    if (probe >= EeRamSize ||
                        probe - lastChanged > 8)
                    {
                        index = probe;
                        break;
                    }

                    lastChanged = probe;
                    groupChanged++;
                    index = probe + 1;
                }

                groupCount++;
                changedBytes += groupChanged;

                if (shownGroups < MaximumGroups)
                {
                    int viewStart = Math.Max(0, groupStart - 8);
                    int viewEnd = Math.Min(EeRamSize, lastChanged + 9);

                    if (viewEnd - viewStart > 64)
                    {
                        viewEnd = viewStart + 64;
                    }

                    int viewLength = viewEnd - viewStart;

                    report.AppendLine();
                    report.AppendLine(
                        "グループ " + groupCount +
                        " / EE+0x" + groupStart.ToString("X8") +
                        " ～ EE+0x" + lastChanged.ToString("X8"));

                    report.AppendLine(
                        "変更バイト数: " + groupChanged +
                        " / 表示範囲: " + viewLength + " bytes");

                    report.AppendLine(
                        "A: " + BitConverter.ToString(
                            _snapshotA.Data,
                            viewStart,
                            viewLength));

                    report.AppendLine(
                        "B: " + BitConverter.ToString(
                            _snapshotB.Data,
                            viewStart,
                            viewLength));

                    shownGroups++;
                }
            }

            report.AppendLine();
            report.AppendLine("===== 集計 =====");
            report.AppendLine("変更グループ数: " + groupCount);
            report.AppendLine("変更バイト総数: " + changedBytes);
            report.AppendLine("表示グループ数: " + shownGroups);

            if (groupCount > shownGroups)
            {
                report.AppendLine(
                    "表示上限により省略したグループ数: " +
                    (groupCount - shownGroups));
            }

            report.AppendLine();
            report.AppendLine("注意: 変更箇所は状態差分であり、名前表示処理だとは未確定です。");

            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

            string fileName =
                "TornekoItemNameLiveDiff_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                ".txt";

            string outputPath = Path.Combine(desktop, fileName);

            File.WriteAllText(
                outputPath,
                report.ToString(),
                new UTF8Encoding(false));

            _output.Text = report.ToString();

            _status.Text =
                "比較完了　変更グループ: " + groupCount +
                "　変更バイト: " + changedBytes;

            MessageBox.Show(
                this,
                "差分比較が完了しました。\r\n\r\n" +
                "変更グループ: " + groupCount + "\r\n" +
                "変更バイト: " + changedBytes + "\r\n\r\n" +
                "保存先:\r\n" + outputPath,
                "EE RAM差分診断完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "比較エラー";
            MessageBox.Show(
                this,
                ex.ToString(),
                "差分比較エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void AppendKnownRegion(
        StringBuilder report,
        string title,
        int offset,
        int length)
    {
        report.AppendLine();
        report.AppendLine("===== " + title + " =====");
        report.AppendLine("EE+0x" + offset.ToString("X8"));

        report.AppendLine(
            "A: " + BitConverter.ToString(
                _snapshotA!.Data,
                offset,
                length));

        report.AppendLine(
            "B: " + BitConverter.ToString(
                _snapshotB!.Data,
                offset,
                length));
    }
}