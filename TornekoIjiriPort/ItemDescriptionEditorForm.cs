using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class ItemDescriptionEditorForm : Form
{
    private const int EeRamSize = 32 * 1024 * 1024;
    private const uint EeRamStart = 0x00000000;

    private sealed class Candidate
    {
        public long Address;
        public byte[] Bytes = Array.Empty<byte>();
        public string EncodingName = "";
        public string Text = "";
        public int Score;
    }

    private readonly int _pid;
    private readonly List<Candidate> _all = new();

    private readonly ListView _list = new();
    private readonly TextBox _filter = new();
    private readonly TextBox _detail = new();
    private readonly Label _status = new();
    private readonly Button _scanButton = new();

    private bool _scanning;

    private static readonly Encoding Cp932 = CreateEncoding(932);
    private static readonly Encoding EucJp = CreateEncoding(51932);
    private static readonly Encoding Iso2022Jp = CreateEncoding(50220);
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16LE = new UnicodeEncoding(false, false, true);
    private static readonly Encoding Utf16BE = new UnicodeEncoding(true, false, true);

    private static Encoding CreateEncoding(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    public ItemDescriptionEditorForm(int pid)
    {
        _pid = pid;

        Text = "説明文を改造 - PINE高速日本語検索";
        Width = 1150;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;

        BuildUI();
        _list.DoubleClick += (s, e) => EditSelectedCandidate();
    }

    private void BuildUI()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };

        _scanButton.Text = "EE RAM 32MBを検索";
        _scanButton.Width = 210;
        _scanButton.Height = 32;
        _scanButton.Click += async (_, _) => await ScanAsync();

        var hint = new Label
        {
            Text = "ゲーム内で対象アイテムの情報を表示してから検索してください。",
            AutoSize = true,
            Padding = new Padding(8, 8, 0, 0)
        };

        top.Controls.Add(_scanButton);
        top.Controls.Add(hint);

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };

        filterPanel.Controls.Add(new Label
        {
            Text = "検索・絞り込み:",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        });

        _filter.Width = 380;
        _filter.Font = new Font("Yu Gothic UI", 11);
        _filter.Text = "えれふぁんとくろう";
        _filter.TextChanged += (_, _) => RefreshList();

        var clear = new Button
        {
            Text = "全候補表示",
            Width = 100,
            Height = 28
        };
        clear.Click += (_, _) => _filter.Clear();

        filterPanel.Controls.Add(_filter);
        filterPanel.Controls.Add(clear);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.Font = new Font("Yu Gothic UI", 9);

        _list.Columns.Add("アドレス", 125);
        _list.Columns.Add("文字コード", 125);
        _list.Columns.Add("復号結果", 650);
        _list.Columns.Add("バイト数", 75);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        _detail.Dock = DockStyle.Fill;
        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.ScrollBars = ScrollBars.Both;
        _detail.WordWrap = false;
        _detail.Font = new Font("Consolas", 10);

        _status.Dock = DockStyle.Fill;
        _status.Text = "PCSX2を起動し、PINEを有効にしてください。";
        _status.AutoSize = false;

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(filterPanel, 0, 1);
        root.Controls.Add(_list, 0, 2);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };

        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        bottom.Controls.Add(_detail, 0, 0);
        bottom.Controls.Add(_status, 0, 1);

        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);
    }

    private static bool IsJapanese(char c)
    {
        return
            (c >= '\u3040' && c <= '\u309F') ||
            (c >= '\u30A0' && c <= '\u30FF') ||
            (c >= '\u4E00' && c <= '\u9FFF') ||
            (c >= '\uFF66' && c <= '\uFF9D') ||
            (c >= '\uF900' && c <= '\uFAFF');
    }

    private static int Evaluate(string text)
    {
        int score = 0;
        int japanese = 0;

        foreach (char c in text)
        {
            if (IsJapanese(c))
            {
                japanese++;
                score += 5;
                continue;
            }

            if (c == '\uFFFD' || char.IsControl(c))
            {
                score -= 10;
                continue;
            }

            if ("。、！？「」『』（）・…ー〜：；".Contains(c))
            {
                score += 2;
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == ' ')
                score++;
        }

        if (japanese < 2)
            return -1000;

        return score;
    }

    private static IEnumerable<(string Name, Encoding Encoding)> GetEncodings()
    {
        yield return ("CP932 / Shift-JIS", Cp932);
        yield return ("UTF-8", Utf8);
        yield return ("EUC-JP", EucJp);
        yield return ("ISO-2022-JP", Iso2022Jp);
        yield return ("UTF-16 LE", Utf16LE);
        yield return ("UTF-16 BE", Utf16BE);
    }

    private static void AddDecodedCandidates(
        byte[] data,
        int offset,
        int length,
        long address,
        List<Candidate> output,
        HashSet<string> seen)
    {
        if (length < 4 || length > 512)
            return;

        foreach (var item in GetEncodings())
        {
            try
            {
                string decoded = item.Encoding.GetString(data, offset, length)
                    .Trim('\0', '\r', '\n', ' ', '\t');

                if (decoded.Length < 2 || decoded.Length > 220)
                    continue;

                int score = Evaluate(decoded);

                if (score < 0)
                    continue;

                string key = address.ToString("X") + "|" + item.Name + "|" + decoded;

                if (!seen.Add(key))
                    continue;

                byte[] raw = new byte[length];
                Array.Copy(data, offset, raw, 0, length);

                output.Add(new Candidate
                {
                    Address = address,
                    Bytes = raw,
                    EncodingName = item.Name,
                    Text = decoded,
                    Score = score
                });
            }
            catch
            {
            }
        }
    }

    private static void ScanByteStrings(
        byte[] data,
        int count,
        long baseAddress,
        List<Candidate> output,
        HashSet<string> seen)
    {
        int pos = 0;

        while (pos < count)
        {
            int start = pos;

            while (pos < count)
            {
                byte b = data[pos];

                if (b == 0 || b < 0x20 || b > 0xFC)
                    break;

                pos++;
            }

            int length = pos - start;

            if (pos < count && data[pos] == 0 &&
                length >= 4 && length <= 512)
            {
                AddDecodedCandidates(
                    data,
                    start,
                    length,
                    baseAddress + start,
                    output,
                    seen);
            }

            if (pos <= start)
                pos++;

            while (pos < count &&
                (data[pos] == 0 || data[pos] < 0x20 || data[pos] > 0xFC))
            {
                pos++;
            }
        }
    }

    private static void ScanUtf16Strings(
        byte[] data,
        int count,
        long baseAddress,
        List<Candidate> output,
        HashSet<string> seen)
    {
        for (int endian = 0; endian < 2; endian++)
        {
            int pos = 0;

            while (pos + 1 < count)
            {
                int start = pos;
                int units = 0;

                while (pos + 1 < count)
                {
                    ushort value = endian == 0
                        ? (ushort)(data[pos] | (data[pos + 1] << 8))
                        : (ushort)((data[pos] << 8) | data[pos + 1]);

                    if (value == 0)
                        break;

                    char c = (char)value;

                    if (char.IsControl(c) && c != '\r' && c != '\n')
                        break;

                    pos += 2;
                    units++;
                }

                int length = pos - start;

                if (pos + 1 < count &&
                    data[pos] == 0 && data[pos + 1] == 0 &&
                    units >= 2 && length >= 4 && length <= 512)
                {
                    AddDecodedCandidates(
                        data,
                        start,
                        length,
                        baseAddress + start,
                        output,
                        seen);
                }

                if (pos <= start)
                    pos += 2;

                while (pos + 1 < count &&
                    data[pos] == 0 && data[pos + 1] == 0)
                {
                    pos += 2;
                }
            }
        }
    }

    private static List<Candidate> ScanRam(byte[] ram)
    {
        var results = new List<Candidate>();
        var seen = new HashSet<string>();

        ScanByteStrings(
            ram, ram.Length, EeRamStart, results, seen);

        ScanUtf16Strings(
            ram, ram.Length, EeRamStart, results, seen);

        return results;
    }

    private static byte[] ReadExact(NetworkStream stream, int length)
    {
        byte[] result = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int received = stream.Read(
                result, offset, length - offset);

            if (received <= 0)
                throw new EndOfStreamException(
                    "PINEからの応答が途中で切断されました。");

            offset += received;
        }

        return result;
    }

        private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern System.IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(
        System.IntPtr handle);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        System.IntPtr process,
        System.IntPtr address,
        byte[] buffer,
        System.UIntPtr size,
        out System.UIntPtr read);

    
[System.Runtime.InteropServices.DllImport(
    "kernel32.dll",
    SetLastError = true
)]
private static extern bool WriteProcessMemory(
    IntPtr process,
    IntPtr address,
    byte[] buffer,
    UIntPtr size,
    out UIntPtr written
);

private void EditSelectedCandidate()
{
    if (_list.SelectedItems.Count == 0)
        return;

    Candidate candidate = _list.SelectedItems[0].Tag as Candidate;

    if (candidate == null)
        return;

    using Form dialog = new Form();

    dialog.Text = "説明文を編集";
    dialog.Width = 620;
    dialog.Height = 430;
    dialog.StartPosition = FormStartPosition.CenterParent;
    dialog.FormBorderStyle = FormBorderStyle.Sizable;
    dialog.MinimizeBox = false;
    dialog.MaximizeBox = false;

    Label addressLabel = new Label();
    addressLabel.Text =
        "EE RAMアドレス: 0x" +
        candidate.Address.ToString("X8") +
        " / 元データ: " +
        candidate.Bytes.Length +
        " バイト";
    addressLabel.Left = 12;
    addressLabel.Top = 12;
    addressLabel.Width = 570;
    addressLabel.Height = 25;

    Label encodingLabel = new Label();
    encodingLabel.Text = "文字コード: CP932 / Shift-JIS";
    encodingLabel.Left = 12;
    encodingLabel.Top = 40;
    encodingLabel.Width = 570;
    encodingLabel.Height = 22;

    TextBox editor = new TextBox();
    editor.Left = 12;
    editor.Top = 70;
    editor.Width = 580;
    editor.Height = 260;
    editor.Multiline = true;
    editor.AcceptsReturn = true;
    editor.AcceptsTab = true;
    editor.ScrollBars = ScrollBars.Both;
    editor.WordWrap = false;
    editor.Font = new System.Drawing.Font("ＭＳ ゴシック", 11);
    editor.Text = candidate.Text;

    Button writeButton = new Button();
    writeButton.Text = "メモリに反映";
    writeButton.Left = 370;
    writeButton.Top = 345;
    writeButton.Width = 110;
    writeButton.Height = 32;

    Button cancelButton = new Button();
    cancelButton.Text = "閉じる";
    cancelButton.Left = 490;
    cancelButton.Top = 345;
    cancelButton.Width = 100;
    cancelButton.Height = 32;

    writeButton.Click += (s, e) =>
    {
        try
        {
            WriteCandidateText(candidate, editor.Text);

            _detail.Text = editor.Text;

            MessageBox.Show(
                dialog,
                "説明文をEE RAMへ書き込みました。",
                "書き込み成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                dialog,
                ex.Message,
                "書き込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    };

    cancelButton.Click += (s, e) =>
    {
        dialog.Close();
    };

    dialog.Controls.Add(addressLabel);
    dialog.Controls.Add(encodingLabel);
    dialog.Controls.Add(editor);
    dialog.Controls.Add(writeButton);
    dialog.Controls.Add(cancelButton);

    dialog.AcceptButton = writeButton;
    dialog.CancelButton = cancelButton;

    dialog.ShowDialog(this);
}

private void WriteCandidateText(Candidate candidate, string newText)
{
    if (candidate == null)
        throw new ArgumentNullException(nameof(candidate));

    if (string.IsNullOrEmpty(newText))
        throw new InvalidOperationException("説明文が空です。");

    System.Text.Encoding.RegisterProvider(
        System.Text.CodePagesEncodingProvider.Instance
    );

    System.Text.Encoding encoding =
        System.Text.Encoding.GetEncoding(932);

    byte[] encoded = encoding.GetBytes(newText);

    int originalLength = candidate.Bytes.Length;

    if (originalLength <= 0)
        throw new InvalidOperationException("元データのサイズが不正です。");

    if (encoded.Length + 1 > originalLength)
    {
        throw new InvalidOperationException(
            "変更後の文章が元データの領域を超えています。`r`n" +
            "元データ: " + originalLength + " バイト`r`n" +
            "変更後: " + (encoded.Length + 1) + " バイト`r`n" +
            "文字数を減らしてください。"
        );
    }

    long offset = candidate.Address - EeRamStart;

    if (offset < 0 || offset + originalLength > EeRamSize)
        throw new InvalidOperationException("EE RAMの範囲外です。");

    long eeBase =
        Pcsx2MemoryLayoutBuilder.GetEEmemBase(_pid);

    if (eeBase <= 0)
        throw new InvalidOperationException("EE RAMのベースアドレスを取得できません。");

    byte[] buffer = new byte[originalLength];

    Buffer.BlockCopy(
        encoded,
        0,
        buffer,
        0,
        encoded.Length
    );

    buffer[encoded.Length] = 0;

    const uint PROCESS_VM_OPERATION = 0x0008;
    const uint PROCESS_VM_WRITE = 0x0020;
    const uint PROCESS_QUERY_INFORMATION = 0x0400;

    IntPtr handle = OpenProcess(
        PROCESS_VM_OPERATION |
        PROCESS_VM_WRITE |
        PROCESS_QUERY_INFORMATION,
        false,
        _pid
    );

    if (handle == IntPtr.Zero)
    {
        throw new System.ComponentModel.Win32Exception(
            System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
            "PCSX2のプロセスを開けませんでした。"
        );
    }

    try
    {
        IntPtr targetAddress = new IntPtr(eeBase + offset);

        bool success = WriteProcessMemory(
            handle,
            targetAddress,
            buffer,
            new UIntPtr((uint)buffer.Length),
            out UIntPtr written
        );

        ulong actual = written.ToUInt64();

        if (!success || actual != (ulong)buffer.Length)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "EE RAMへの書き込みに失敗しました。書き込みバイト数: " +
                actual
            );
        }
    }
    finally
    {
        CloseHandle(handle);
    }
}

private static byte[] ReadEeRam(Action<string> progress, int processId)
    {
        byte[] ram = new byte[EeRamSize];

        progress("PCSX2のEE RAMアドレスを特定しています...");

        long eeBase = Pcsx2MemoryLayoutBuilder.GetEEmemBase(processId);

        if (eeBase <= 0)
            throw new InvalidOperationException(
                "PCSX2のEE RAMアドレスを取得できませんでした。");

        System.IntPtr handle = OpenProcess(
            PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle == System.IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "PCSX2のプロセスメモリを開けません。");

        try
        {
            const int ChunkSize = 1024 * 1024;

            int completed = 0;
            int chunkNumber = 0;
            int totalChunks = (EeRamSize + ChunkSize - 1) / ChunkSize;

            while (completed < EeRamSize)
            {
                int count = Math.Min(
                    ChunkSize,
                    EeRamSize - completed);

                byte[] buffer = new byte[count];
                System.UIntPtr bytesRead;

                long currentAddress = checked(eeBase + completed);

                bool success = ReadProcessMemory(
                    handle,
                    new System.IntPtr(currentAddress),
                    buffer,
                    new System.UIntPtr((uint)count),
                    out bytesRead);

                ulong actual = bytesRead.ToUInt64();

                if (!success || actual != (ulong)count)
                {
                    throw new System.ComponentModel.Win32Exception(
                        System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                        "EE RAMの読み取りに失敗しました。" +
                        "\r\nホストアドレス: 0x" +
                        currentAddress.ToString("X") +
                        "\r\n要求サイズ: " + count +
                        "\r\n取得サイズ: " + actual);
                }

                Buffer.BlockCopy(
                    buffer,
                    0,
                    ram,
                    completed,
                    count);

                completed += count;
                chunkNumber++;

                int percent = (int)(
                    (long)completed * 100 / EeRamSize);

                progress(
                    "PCSX2のEE RAMを直接読み取り中...\r\n" +
                    "進捗: " + percent + "%\r\n" +
                    "取得済み: " +
                    (completed / 1024 / 1024) +
                    " / 32 MB\r\n" +
                    "読み取り: " + chunkNumber +
                    " / " + totalChunks);
            }
        }
        finally
        {
            CloseHandle(handle);
        }

        return ram;
    }

    private async Task ScanAsync()
    {
        if (_scanning)
            return;

        _scanning = true;
        _scanButton.Enabled = false;
        _all.Clear();
        _list.Items.Clear();
        _detail.Clear();

        try
        {
            _status.Text = "PCSX2のEE RAMを読み取っています...";

            byte[] ram = await Task.Run(() =>
                ReadEeRam(message =>
                {
                    if (IsDisposed || !IsHandleCreated)
                        return;

                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed)
                                _status.Text = message;
                        }));
                    }
                    catch
                    {
                    }
                }, _pid));

            if (IsDisposed)
                return;

            _status.Text = "32MB取得完了。日本語文字コードを解析しています...";

            var results = await Task.Run(() => ScanRam(ram));

            if (IsDisposed)
                return;

            _all.AddRange(results);

            RefreshList();

            _status.Text =
                "検索完了 / EE RAM 32MB / 候補: " +
                _all.Count + "件";

            MessageBox.Show(
                this,
                "EE RAMの取得と文字コード解析が完了しました。\r\n\r\n" +
                "取得サイズ: 32MB\r\n" +
                "検出候補: " + _all.Count + "件",
                "検索完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "検索エラー: " + ex.Message;

            MessageBox.Show(
                this,
                ex.ToString(),
                "PINE検索エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _scanning = false;

            if (!IsDisposed)
                _scanButton.Enabled = true;
        }
    }

    private void RefreshList()
    {
        string keyword = (_filter.Text ?? "").Trim();

        _list.BeginUpdate();

        try
        {
            _list.Items.Clear();

            IEnumerable<Candidate> filtered = _all;

            if (keyword.Length > 0)
            {
                filtered = filtered.Where(c =>
                    c.Text.Contains(
                        keyword,
                        StringComparison.OrdinalIgnoreCase) ||
                    c.EncodingName.Contains(
                        keyword,
                        StringComparison.OrdinalIgnoreCase) ||
                    c.Address.ToString("X").Contains(
                        keyword.Replace("0x", ""),
                        StringComparison.OrdinalIgnoreCase));
            }

            foreach (Candidate c in filtered.Take(10000))
            {
                var item = new ListViewItem(
                    "0x" + c.Address.ToString("X8"));

                item.SubItems.Add(c.EncodingName);
                item.SubItems.Add(
                    c.Text.Replace("\r", " ").Replace("\n", " "));
                item.SubItems.Add(c.Bytes.Length.ToString());

                item.Tag = c;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _status.Text =
            "総候補: " + _all.Count +
            "件 / 表示: " + _list.Items.Count + "件";
    }

    private void ShowSelected()
    {
        if (_list.SelectedItems.Count == 0)
            return;

        if (_list.SelectedItems[0].Tag is not Candidate c)
            return;

        _detail.Text =
            "EE RAMアドレス: 0x" + c.Address.ToString("X8") +
            Environment.NewLine +
            "文字コード: " + c.EncodingName +
            Environment.NewLine +
            "評価スコア: " + c.Score +
            Environment.NewLine +
            "バイト数: " + c.Bytes.Length +
            Environment.NewLine +
            Environment.NewLine +
            "復号結果:" + Environment.NewLine +
            c.Text +
            Environment.NewLine +
            Environment.NewLine +
            "元データ HEX:" + Environment.NewLine +
            BitConverter.ToString(c.Bytes);
    }
}