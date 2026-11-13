using System.Data;

namespace TornekoIjiriNext;

/// <summary>Offline duplicate-ID comparison for imported legacy 6-byte spawn-table files.</summary>
internal sealed class MonsterSpawnCompareForm : Form
{
    private readonly DataTable _table = new("Duplicates");
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(1000, 0) };
    private readonly List<SpawnTableSnapshot> _snapshots = [];

    public MonsterSpawnCompareForm()
    {
        Text = "モンスター出現IDの重複比較（オフライン）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1050, 680);
        MinimumSize = new Size(780, 500);
        _table.Columns.Add("MonsterId", typeof(int)); _table.Columns.Add("Kind", typeof(string));
        _table.Columns.Add("TableCount", typeof(int)); _table.Columns.Add("Occurrences", typeof(int)); _table.Columns.Add("Locations", typeof(string));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "6-byteテーブルを複数選択…", AutoSize = true }; open.Click += (_, _) => OpenFiles(); bar.Controls.Add(open);
        var clear = new Button { Text = "読込リストをクリア", AutoSize = true }; clear.Click += (_, _) => { _snapshots.Clear(); RefreshDuplicates(); }; bar.Controls.Add(clear);
        root.Controls.Add(bar, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.DataSource = _table;
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = "旧形式6-byte entry (uint16 monster ID, uint16 CDF, uint16 level) のbinを複数比較します。file名はtable識別子として使います。dungeon/floorやID名は推測しません。PINE/PCSX2へ接続しません。";
        root.Controls.Add(_status, 0, 2); Controls.Add(root);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
            c.HeaderText = c.Name switch { "MonsterId" => "旧版Monster ID (LE)", "Kind" => "重複種別", "TableCount" => "table数", "Occurrences" => "出現行数", "Locations" => "出現場所(file/row)・Lv・CDF", _ => c.Name };
    }

    private void OpenFiles()
    {
        using var dialog = new OpenFileDialog { Filter = "Legacy spawn table (*.bin;*.dat)|*.bin;*.dat|すべてのファイル (*.*)|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var pending = new List<SpawnTableSnapshot>();
            foreach (var path in dialog.FileNames)
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0 || bytes.Length % MonsterSpawnDuplicateScanner.EntrySize != 0)
                    throw new InvalidDataException($"{Path.GetFileName(path)}: file length {bytes.Length} is not a multiple of six.");
                if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException($"{Path.GetFileName(path)} exceeds 4 MiB.");
                pending.Add(new SpawnTableSnapshot(Path.GetFileName(path), bytes));
            }
            var names = _snapshots.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pending)
            {
                if (!names.Add(entry.Name)) throw new InvalidDataException($"同名fileが既に読込済みです: {entry.Name}");
            }
            _snapshots.AddRange(pending);
            RefreshDuplicates();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読み込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshDuplicates()
    {
        try
        {
            var result = MonsterSpawnDuplicateScanner.FindDuplicates(_snapshots);
            _table.Rows.Clear();
            foreach (var row in result) _table.Rows.Add(row.MonsterId, row.Kind, row.TableCount, row.OccurrenceCount, row.Locations);
            _status.ForeColor = Color.DarkSlateBlue;
            _status.Text = $"読み込みtable {_snapshots.Count}件 / 重複ID {result.Count}件。これはIDの機械的比較です。dungeon/floor名とmonster名称、ゲーム内効果は未検証。すべてローカルデータのみ。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "比較失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
