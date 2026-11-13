using System.Buffers.Binary;
using System.Data;
using System.Globalization;

namespace TornekoIjiriNext;

/// <summary>Offline-only editor for the source-observed 24-byte legacy monster status row.</summary>
internal sealed class MonsterStatusEditorForm : Form
{
    private const int RecordSize = 24;
    private const int MaxRecords = 512;
    private readonly DataTable _data = new("MonsterStatus");
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(1050, 0) };
    private byte[]? _original;
    private bool _loading;
    private bool _dirty;
    private readonly Func<byte[], string>? _saveToProject;

    public MonsterStatusEditorForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "モンスター基本能力テーブル（オフライン編集）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 720);
        MinimumSize = new Size(850, 520);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var open = new Button { Text = "24-byte recordsを開く…", AutoSize = true }; open.Click += (_, _) => OpenRecords(); actions.Controls.Add(open);
        var save = new Button { Text = "旧形式binとして保存…", AutoSize = true }; save.Click += (_, _) => SaveRecords(); actions.Controls.Add(save);
        root.Controls.Add(actions, 0, 0);
        BuildColumns();
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.DataSource = _data; _grid.CellValidating += ValidateCell;
        _grid.CellValueChanged += (_, _) => { if (!_loading) { _dirty = true; UpdateStatus(); } };
        root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        root.Controls.Add(_status, 0, 2); Controls.Add(root); UpdateStatus();
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadRecords(initialPath);
    }

    private void BuildColumns()
    {
        _data.Columns.Add("Index", typeof(int));
        Add("HP +00", typeof(int)); Add("攻撃 +02", typeof(int)); Add("防御 +04", typeof(int));
        Add("特性 +06", typeof(int)); Add("視界 +07", typeof(int)); Add("予約 +08", typeof(int));
        Add("速度 +09", typeof(int)); Add("系統 +0A", typeof(int)); Add("勧誘率 +0B", typeof(int));
        Add("経験値 +0C", typeof(int)); Add("予約 +0E", typeof(int)); Add("特技ID +10", typeof(int));
        Add("ドロップ +12", typeof(int)); Add("成長型 +14", typeof(int));
        Add("予約 +15..17 (hex)", typeof(string));
    }
    private void Add(string name, Type type) => _data.Columns.Add(name, type);

    private void OpenRecords()
    {
        using var dialog = new OpenFileDialog { Filter = "Legacy status binary (*.bin;*.dat)|*.bin;*.dat|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadRecords(dialog.FileName);
    }

    private void LoadRecords(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0 || bytes.Length % RecordSize != 0 || bytes.Length / RecordSize > MaxRecords)
                throw new InvalidDataException($"ファイルは1〜{MaxRecords}件の完全な24-byte recordである必要があります。");
            _loading = true; _data.Clear(); _original = bytes;
            for (var i = 0; i < bytes.Length / RecordSize; i++)
            {
                var b = bytes.AsSpan(i * RecordSize, RecordSize);
                _data.Rows.Add(i,
                    U16(b, 0), U16(b, 2), U16(b, 4), b[6], b[7], b[8], b[9], b[10], b[11],
                    U16(b, 12), U16(b, 14), U16(b, 16), U16(b, 18), b[20], Convert.ToHexString(b.Slice(21, 3)));
            }
            _dirty = false; _loading = false; UpdateStatus();
        }
        catch (Exception ex) { _loading = false; MessageBox.Show(this, ex.Message, "読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveRecords()
    {
        if (_original is null) { MessageBox.Show(this, "先に元データを読み込んでください。"); return; }
        if (!_grid.EndEdit()) return;
        try
        {
            var bytes = (byte[])_original.Clone();
            for (var i = 0; i < _data.Rows.Count; i++)
            {
                var b = bytes.AsSpan(i * RecordSize, RecordSize); var row = _data.Rows[i];
                Put16(b, 0, row, "HP +00"); Put16(b, 2, row, "攻撃 +02"); Put16(b, 4, row, "防御 +04");
                b[6] = Byte(row, "特性 +06"); b[7] = Byte(row, "視界 +07"); b[8] = Byte(row, "予約 +08");
                b[9] = Byte(row, "速度 +09"); b[10] = Byte(row, "系統 +0A"); b[11] = Byte(row, "勧誘率 +0B");
                Put16(b, 12, row, "経験値 +0C"); Put16(b, 14, row, "予約 +0E"); Put16(b, 16, row, "特技ID +10");
                Put16(b, 18, row, "ドロップ +12"); b[20] = Byte(row, "成長型 +14");
                var reserved = Convert.FromHexString(Convert.ToString(row["予約 +15..17 (hex)"])!);
                if (reserved.Length != 3) throw new FormatException("予約 +15..17 は6桁のhexで入力してください。");
                reserved.CopyTo(b.Slice(21, 3));
            }
            if (_saveToProject is not null)
            {
                var name = _saveToProject(bytes); _dirty = false; UpdateStatus();
                _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません.";
                return;
            }
            using var dialog = new SaveFileDialog { Filter = "Legacy status binary (*.bin)|*.bin|All files (*.*)|*.*", FileName = "monster-status.bin" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, bytes); _dirty = false; UpdateStatus();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static ushort U16(Span<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static void Put16(Span<byte> bytes, int offset, DataRow row, string field) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(offset, 2), checked((ushort)Convert.ToInt32(row[field], CultureInfo.InvariantCulture)));
    private static byte Byte(DataRow row, string field) => checked((byte)Convert.ToInt32(row[field], CultureInfo.InvariantCulture));
    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
        var column = _data.Columns[e.ColumnIndex].ColumnName;
        if (column.Contains("hex", StringComparison.Ordinal))
        {
            var text = Convert.ToString(e.FormattedValue)?.Trim() ?? "";
            if (text.Length != 6 || !text.All(Uri.IsHexDigit)) { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = "6桁の16進数を入力してください。"; }
            return;
        }
        var max = column is "HP +00" or "攻撃 +02" or "防御 +04" or "経験値 +0C" or "予約 +0E" or "特技ID +10" or "ドロップ +12" ? ushort.MaxValue : byte.MaxValue;
        if (!int.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0 || value > max)
        { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = $"0〜{max}の整数を入力してください。"; }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }
    private void UpdateStatus() => _status.Text = _original is null
        ? "CORE_ENGINE_ANALYSIS.md / start.hspの旧実装から24-byte strideと各フィールドoffsetを採用。ID対応と現在のゲーム内データ位置は未検証です。ローカルファイル専用で、PINE読み書きはありません。"
        : $"{_data.Rows.Count} records読込済み。{(_dirty ? "未保存の変更あり。" : "変更なし。")}予約byteも保持します。値の意味は旧ソース上の呼称で、現行ゲームでの実機照合は未実施です。";
}
