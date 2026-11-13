using System.Data;
using System.Globalization;

namespace TornekoIjiriNext;

/// <summary>Read-only offline browser/comparer for legacy 512 KiB snapshot files.</summary>
internal sealed class LegacyMemorySnapshotForm : Form
{
    private readonly DataTable _table = new("SnapshotSegments");
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(980, 0) };
    private LegacyMemorySnapshotModel? _snapshot;
    private string _source = "";

    public LegacyMemorySnapshotForm(string? initialPath = null)
    {
        Text = "旧版512 KiB memory snapshot（offline / read-only）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1050, 700);
        MinimumSize = new Size(780, 500);
        _table.Columns.Add("Name", typeof(string)); _table.Columns.Add("Offset", typeof(string));
        _table.Columns.Add("Length", typeof(int)); _table.Columns.Add("Preview", typeof(string));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "512 KiB snapshotを開く…", AutoSize = true }; open.Click += (_, _) => OpenSnapshot(); bar.Controls.Add(open);
        var compare = new Button { Text = "別snapshotと比較…", AutoSize = true }; compare.Click += (_, _) => CompareSnapshot(); bar.Controls.Add(compare);
        var extract = new Button { Text = "選択領域をbin抽出…", AutoSize = true }; extract.Click += (_, _) => ExtractSelection(); bar.Controls.Add(extract);
        root.Controls.Add(bar, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.DataSource = _table;
        _grid.DataBindingComplete += (_, _) => ConfigureColumns(); root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = "旧label_151..156の512 KiB containerとversioned extensionをローカル閲覧/比較します。Restore・PCSX2接続・memory writeはありません。";
        root.Controls.Add(_status, 0, 2); Controls.Add(root);
        if (!string.IsNullOrWhiteSpace(initialPath)) OpenSnapshot(initialPath);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
            c.HeaderText = c.Name switch { "Name" => "snapshot region", "Offset" => "file offset", "Length" => "length (bytes)", "Preview" => "先頭byte (hex)", _ => c.Name };
        foreach (var name in new[] { "Name", "Offset", "Length", "Preview" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].FillWeight = name switch { "Name" => 180, "Offset" => 70, "Length" => 60, _ => 180 };
    }

    private void OpenSnapshot()
    {
        using var dialog = new OpenFileDialog { Filter = "Legacy memory snapshot (*.bin;*.dat)|*.bin;*.dat|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        OpenSnapshot(dialog.FileName);
    }

    private void OpenSnapshot(string path)
    {
        try
        {
            var model = new LegacyMemorySnapshotModel(File.ReadAllBytes(path));
            _snapshot = model; _source = path; DisplaySnapshot();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "snapshot読込失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DisplaySnapshot()
    {
        if (_snapshot is null) return;
        _table.Rows.Clear();
        var raw = _snapshot.ReadRawContainer();
        _table.Rows.Add("512 KiB container", "0x00000", raw.Length, Convert.ToHexString(raw.AsSpan(0, 16)));
        foreach (var segment in _snapshot.Segments)
        {
            var bytes = _snapshot.ReadSegment(segment);
            _table.Rows.Add(segment.Name, "0x" + segment.Offset.ToString("X", CultureInfo.InvariantCulture), segment.Length, Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length))));
        }
        _status.Text = $"{Path.GetFileName(_source)} / marker 0x{_snapshot.Version:X2} ({_snapshot.VersionDescription}) / {_snapshot.Segments.Count} extension regions。旧形式の構造候補表示であり、データ意味やゲーム対応は未検証。ファイルを変更しません。";
    }

    private void CompareSnapshot()
    {
        if (_snapshot is null) { MessageBox.Show(this, "先に基準snapshotを開いてください。"); return; }
        using var dialog = new OpenFileDialog { Filter = "Legacy memory snapshot (*.bin;*.dat)|*.bin;*.dat|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var other = new LegacyMemorySnapshotModel(File.ReadAllBytes(dialog.FileName));
            var lines = new List<string>
            {
                $"A: {Path.GetFileName(_source)} (marker 0x{_snapshot.Version:X2})",
                $"B: {Path.GetFileName(dialog.FileName)} (marker 0x{other.Version:X2})",
                $"Container byte differences: {LegacyMemorySnapshotModel.CountDifferentBytes(_snapshot.ReadRawContainer(), other.ReadRawContainer())} / {LegacyMemorySnapshotModel.SnapshotLength}"
            };
            var byName = other.Segments.ToDictionary(s => s.Name, StringComparer.Ordinal);
            foreach (var segment in _snapshot.Segments)
            {
                if (!byName.TryGetValue(segment.Name, out var match)) { lines.Add($"{segment.Name}: Bにはありません"); continue; }
                if (segment.Length != match.Length) { lines.Add($"{segment.Name}: サイズ違い"); continue; }
                var a = _snapshot.ReadSegment(segment); var b = other.ReadSegment(match);
                lines.Add($"{segment.Name}: {LegacyMemorySnapshotModel.CountDifferentBytes(a, b)} / {a.Length} bytes differ");
            }
            MessageBox.Show(this, string.Join(Environment.NewLine, lines), "offline snapshot比較", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "比較失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExtractSelection()
    {
        if (_snapshot is null || _grid.CurrentRow is null) return;
        var name = Convert.ToString(_grid.CurrentRow.Cells["Name"].Value, CultureInfo.InvariantCulture);
        byte[] bytes;
        if (name == "512 KiB container") bytes = _snapshot.ReadRawContainer();
        else
        {
            var segment = _snapshot.Segments.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            if (segment is null) return;
            bytes = _snapshot.ReadSegment(segment);
        }
        using var dialog = new SaveFileDialog { Filter = "Binary (*.bin)|*.bin|すべてのファイル (*.*)|*.*", DefaultExt = "bin", AddExtension = true, FileName = (name ?? "snapshot-region").Replace(' ', '_') + ".bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dialog.FileName, bytes); _status.Text = $"選択内容をローカル抽出しました: {dialog.FileName}。PCSX2には反映していません。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "抽出失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
