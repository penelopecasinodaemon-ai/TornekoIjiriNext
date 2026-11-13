using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

internal sealed record MemoryCapture(
    int FormatVersion,
    DateTime CapturedAtUtc,
    string Pcsx2Version,
    string GameTitle,
    string GameId,
    string GameCrc,
    string GameVersion,
    uint Address,
    int Length,
    string BytesHex);

/// <summary>Read-only PINE capture/save/compare utility. This form has no write path.</summary>
internal sealed class MemoryCaptureForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly Func<PineClient?> _getClient;
    private readonly Func<PineClient, bool> _isActive;
    private readonly TextBox _address = new() { Text = "00180020", Width = 125 };
    private readonly NumericUpDown _length = new() { Minimum = 1, Maximum = 4096, Value = 64, Width = 85 };
    private readonly TextBox _output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly Button _capture = new() { Text = "読み取り・保存…", AutoSize = true };

    public MemoryCaptureForm(Func<PineClient?> getClient, Func<PineClient, bool> isActive)
    {
        _getClient = getClient;
        _isActive = isActive;
        Text = "実測メモリキャプチャ（読み取り専用）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(960, 700);
        MinimumSize = new Size(760, 520);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        toolbar.Controls.Add(new Label { Text = "EEアドレス (hex)", AutoSize = true, Margin = new Padding(2, 8, 2, 0) });
        toolbar.Controls.Add(_address);
        toolbar.Controls.Add(new Label { Text = "長さ (bytes)", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        toolbar.Controls.Add(_length);
        toolbar.Controls.Add(_capture);
        var compare = new Button { Text = "保存済みキャプチャを比較…", AutoSize = true };
        compare.Click += (_, _) => CompareFiles();
        toolbar.Controls.Add(compare);
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_output, 0, 1);
        root.Controls.Add(new Label { Text = "この画面は公式PINEの読み取りコマンドのみを使用します。キャプチャはメタデータ付きJSONで保存します。比較は同一アドレス・同一長のスナップショットに限ります。", AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Color.DimGray, Padding = new Padding(2, 8, 2, 0) }, 0, 2);
        Controls.Add(root);
        _capture.Click += async (_, _) => await CaptureAndSaveAsync();
    }

    private async Task CaptureAndSaveAsync()
    {
        if (!TryParseRange(out var address, out var length)) return;
        var client = _getClient();
        if (client is null || !_isActive(client))
        {
            MessageBox.Show(this, "PINE接続と起動中のゲームが必要です。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _capture.Enabled = false;
        try
        {
            var capture = await Task.Run(() => CaptureNow(client, address, length));
            if (!_isActive(client)) throw new IOException("キャプチャ中にPINEセッションが切断または切り替わりました。");
            using var dialog = new SaveFileDialog { Filter = "PINEメモリキャプチャ (*.json)|*.json", DefaultExt = "json", AddExtension = true, FileName = $"capture_{capture.GameId}_{address:X8}_{capture.CapturedAtUtc:yyyyMMdd_HHmmss}.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(capture, JsonOptions), new UTF8Encoding(false));
            _output.Text = FormatCapture(capture) + $"\r\n保存先: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _output.Text = $"キャプチャ失敗（成功扱いではありません）\r\n{ex}";
            MessageBox.Show(this, ex.Message, "キャプチャ失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _capture.Enabled = true; }
    }

    private MemoryCapture CaptureNow(PineClient client, uint address, int length)
    {
        if (!_isActive(client)) throw new IOException("PINEセッションが有効ではありません。");
        var statusBefore = client.GetStatus();
        if (statusBefore is not (0 or 1)) throw new IOException("ゲームが起動していません。");
        var infoBefore = client.GetGameInfo();
        var version = client.GetPcsx2Version();
        var bytes = client.ReadMemory(address, length);
        var statusAfter = client.GetStatus();
        if (statusAfter is not (0 or 1)) throw new IOException("読み取り中にゲームが停止しました。");
        var infoAfter = client.GetGameInfo();
        if (!string.Equals(infoBefore.Id, infoAfter.Id, StringComparison.Ordinal) ||
            !string.Equals(infoBefore.Crc, infoAfter.Crc, StringComparison.Ordinal) ||
            !string.Equals(infoBefore.GameVersion, infoAfter.GameVersion, StringComparison.Ordinal))
            throw new IOException("読み取り中に接続先ゲームが変化しました。キャプチャを破棄しました。");
        if (bytes.Length != length) throw new IOException($"読み取り長が一致しません: {bytes.Length}/{length}");
        return new MemoryCapture(1, DateTime.UtcNow, version, infoAfter.Title, infoAfter.Id, infoAfter.Crc, infoAfter.GameVersion, address, length, Convert.ToHexString(bytes));
    }

    private void CompareFiles()
    {
        try
        {
            var first = LoadCapture("比較元");
            if (first is null) return;
            var second = LoadCapture("比較先");
            if (second is null) return;
            if (first.Address != second.Address || first.Length != second.Length)
                throw new InvalidDataException("同じアドレス・同じ長さのキャプチャを選択してください。");
            var left = Convert.FromHexString(first.BytesHex);
            var right = Convert.FromHexString(second.BytesHex);
            if (left.Length != first.Length || right.Length != second.Length)
                throw new InvalidDataException("キャプチャに記録された長さとバイト列が一致しません。");
            _output.Text = FormatComparison(first, second, left, right);
        }
        catch (Exception ex)
        {
            _output.Text = $"比較失敗\r\n{ex}";
            MessageBox.Show(this, ex.Message, "比較失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private MemoryCapture? LoadCapture(string title)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = "PINEメモリキャプチャ (*.json)|*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        var capture = JsonSerializer.Deserialize<MemoryCapture>(File.ReadAllText(dialog.FileName), JsonOptions)
            ?? throw new InvalidDataException("キャプチャJSONが空か、形式が不正です。");
        if (capture.FormatVersion != 1 || capture.Length is < 1 or > 4096)
            throw new InvalidDataException("未対応のキャプチャ形式または長さです。");
        return capture;
    }

    private bool TryParseRange(out uint address, out int length)
    {
        var input = _address.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        length = (int)_length.Value;
        if (!uint.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) || (ulong)address + (uint)length - 1 > uint.MaxValue)
        {
            MessageBox.Show(this, "アドレスは有効な32-bit EEアドレスで入力してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private static string FormatCapture(MemoryCapture capture)
    {
        var bytes = Convert.FromHexString(capture.BytesHex);
        var sb = new StringBuilder();
        sb.AppendLine($"形式: {capture.FormatVersion} / UTC: {capture.CapturedAtUtc:O}");
        sb.AppendLine($"PCSX2: {capture.Pcsx2Version} / ゲーム: {capture.GameTitle} / ID: {capture.GameId} / CRC: {capture.GameCrc} / 版: {capture.GameVersion}");
        sb.AppendLine($"アドレス: 0x{capture.Address:X8} / 長さ: {capture.Length} bytes");
        sb.AppendLine("意味・構造は未判定。表示はraw bytesと4-byte little-endian DWORDです。");
        AppendDump(sb, capture.Address, bytes);
        return sb.ToString();
    }

    private static string FormatComparison(MemoryCapture first, MemoryCapture second, byte[] left, byte[] right)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"比較元: {first.CapturedAtUtc:O} / {first.GameId} / CRC {first.GameCrc}");
        sb.AppendLine($"比較先: {second.CapturedAtUtc:O} / {second.GameId} / CRC {second.GameCrc}");
        if (first.GameId != second.GameId || first.GameCrc != second.GameCrc || first.GameVersion != second.GameVersion)
            sb.AppendLine("注意: ゲームID/CRC/版が異なります。値の差を同一ゲーム状態の変化とは解釈できません。");
        sb.AppendLine($"範囲: 0x{first.Address:X8}, {first.Length} bytes / 相違バイト数: {left.Where((value, i) => value != right[i]).Count()}");
        sb.AppendLine("BYTE差分 (offset: before -> after):");
        var changedBytes = 0;
        for (var i = 0; i < left.Length; i++)
            if (left[i] != right[i]) { sb.AppendLine($"+0x{i:X4} (0x{first.Address + (uint)i:X8}): {left[i]:X2} -> {right[i]:X2}"); changedBytes++; }
        if (changedBytes == 0) sb.AppendLine("(差分なし)");
        sb.AppendLine();
        sb.AppendLine("DWORD差分 (4-byte little-endian、範囲先頭から整列):");
        var changedWords = 0;
        for (var i = 0; i + 4 <= left.Length; i += 4)
        {
            var before = BinaryPrimitives.ReadUInt32LittleEndian(left.AsSpan(i, 4));
            var after = BinaryPrimitives.ReadUInt32LittleEndian(right.AsSpan(i, 4));
            if (before == after) continue;
            sb.AppendLine($"+0x{i:X4} (0x{first.Address + (uint)i:X8}): 0x{before:X8} -> 0x{after:X8}");
            changedWords++;
        }
        if (changedWords == 0) sb.AppendLine("(DWORD差分なし)");
        sb.AppendLine("差分は観測値です。値の意味やゲーム構造を示すものではありません。");
        return sb.ToString();
    }

    private static void AppendDump(StringBuilder sb, uint address, byte[] bytes)
    {
        sb.AppendLine("HEX bytes:");
        for (var i = 0; i < bytes.Length; i += 16)
        {
            var count = Math.Min(16, bytes.Length - i);
            sb.AppendLine($"0x{address + (uint)i:X8}: {string.Join(" ", bytes.AsSpan(i, count).ToArray().Select(b => b.ToString("X2")))}");
        }
        sb.AppendLine("DWORD (little-endian):");
        for (var i = 0; i + 4 <= bytes.Length; i += 4)
            sb.AppendLine($"0x{address + (uint)i:X8}: 0x{BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)):X8}");
        var remainder = bytes.Length % 4;
        if (remainder > 0) sb.AppendLine($"末尾{remainder} byteはDWORDに含めていません。");
    }
}
