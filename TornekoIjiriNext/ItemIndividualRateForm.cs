using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace TornekoIjiriNext;

/// <summary>Offline editor for the 354 per-item cumulative rates recovered from the legacy HSP.</summary>
internal sealed class ItemIndividualRateForm : Form
{
    private static int[]? _clipboard;
    private readonly ComboBox _block = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Button _paste = new() { Text = "個別率を貼り付け", AutoSize = true, Enabled = false };
    private readonly DataTable _table = new("IndividualItemRates");
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(980, 0) };
    private readonly List<LegacyItemDefinition> _items;
    private readonly List<(int Id, string Name)> _categories;
    private ItemRateTableModel? _model;
    private string _source = "未読込";
    private bool _loading;
    private readonly Func<byte[], string>? _saveToProject;

    public ItemIndividualRateForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "個別アイテム出現率（legacy 768-byte / offline）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 700);
        MinimumSize = new Size(820, 520);
        (_items, _categories) = LoadCatalog();
        _table.Columns.Add("Id", typeof(int)); _table.Columns.Add("Name", typeof(string));
        _table.Columns.Add("Category", typeof(string)); _table.Columns.Add("Weight", typeof(int)); _table.Columns.Add("Cumulative", typeof(int));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "768-byteテーブルを開く…", AutoSize = true }; open.Click += (_, _) => OpenData(); top.Controls.Add(open);
        top.Controls.Add(new Label { Text = "ブロック", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        top.Controls.Add(_block);
        top.Controls.Add(new Label { Text = "カテゴリ", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        top.Controls.Add(_category);
        var average = new Button { Text = "有効個別率を平均化", AutoSize = true }; average.Click += (_, _) => AverageRates(); top.Controls.Add(average);
        var normalize = new Button { Text = "選択カテゴリを999に調整", AutoSize = true }; normalize.Click += (_, _) => NormalizeRates(); top.Controls.Add(normalize);
        var copy = new Button { Text = "カテゴリ個別率をコピー", AutoSize = true }; copy.Click += (_, _) => CopyRates(); top.Controls.Add(copy);
        _paste.Click += (_, _) => PasteRates(); top.Controls.Add(_paste);
        var save = new Button { Text = "旧形式binとして保存…", AutoSize = true }; save.Click += (_, _) => SaveData(); top.Controls.Add(save);
        root.Controls.Add(top, 0, 0);

        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.DataSource = _table; _grid.CellValidating += ValidateCell; _grid.CellValueChanged += ItemChanged;
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = "旧HSPのvar_517 (354 items) とoffset=30+itemID×2を採用。カテゴリ別CDFを表示します。現行ゲームのID/値の実機検証は未実施。ローカル専用でPINE書き込みなし。";
        root.Controls.Add(_status, 0, 2); Controls.Add(root);
        _block.SelectedIndexChanged += (_, _) => DisplayRates();
        _category.SelectedIndexChanged += (_, _) => DisplayRates();
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadFile(initialPath);
    }

    private static (List<LegacyItemDefinition> Items, List<(int Id, string Name)> Categories) LoadCatalog()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "game-catalog.json")));
        var root = doc.RootElement;
        var items = root.GetProperty("items").EnumerateArray().Select(e => new LegacyItemDefinition(
            e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString() ?? "", e.GetProperty("categoryId").GetInt32())).OrderBy(i => i.Id).ToList();
        var categories = root.GetProperty("itemCategories").EnumerateArray().Select(e => (e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString() ?? "")).OrderBy(c => c.Item1).ToList();
        if (items.Count != 354 || items.Where((item, index) => item.Id != index).Any()) throw new InvalidDataException("カタログのitem ID 0..353が連続していません。");
        return (items, categories);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            c.HeaderText = c.Name switch { "Id" => "旧版item ID", "Name" => "名称候補", "Category" => "旧版カテゴリ", "Weight" => "個別出現重み", "Cumulative" => "カテゴリ内CDF", _ => c.Name };
            c.ReadOnly = c.Name is not "Weight";
        }
        foreach (var name in new[] { "Id", "Name", "Category", "Weight", "Cumulative" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].FillWeight = name switch { "Id" => 55, "Name" => 190, "Category" => 75, _ => 100 };
    }

    private void OpenData()
    {
        using var dialog = new OpenFileDialog { Filter = "Legacy item-rate data (*.bin;*.json)|*.bin;*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dialog.FileName);
    }

    private void LoadFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath); var source = Path.GetFileName(filePath);
            if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(bytes); var root = doc.RootElement;
                var hex = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("BytesHex", StringComparison.OrdinalIgnoreCase)).Value;
                if (hex.ValueKind != JsonValueKind.String) throw new InvalidDataException("単一領域capture JSONのBytesHexが必要です。batch JSONは未対応です。");
                bytes = Convert.FromHexString(hex.GetString() ?? "");
                var address = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("Address", StringComparison.OrdinalIgnoreCase)).Value;
                if (address.ValueKind == JsonValueKind.Number && address.TryGetUInt32(out var ee)) source += $" / EE 0x{ee:X8}";
            }
            _model = new ItemRateTableModel(bytes); _source = source;
            _block.Items.Clear(); for (var i = 0; i < _model.BlockCount; i++) _block.Items.Add($"block {i + 1} (offset 0x{i * ItemRateTableModel.BlockSize:X})");
            _category.Items.Clear(); foreach (var category in _categories) _category.Items.Add($"{category.Id}: {category.Name}");
            _block.SelectedIndex = 0; _category.SelectedIndex = 0; DisplayRates();
            _status.Text = $"読み込み: {source} / {_model.BlockCount} blocks。個別領域offset 30..737をID対応で表示。reserved 28..29, 738..767は保持。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読み込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private int SelectedCategoryId => _categories[_category.SelectedIndex].Id;
    private LegacyItemDefinition[] SelectedItems => _items.Where(i => i.CategoryId == SelectedCategoryId).ToArray();

    private void DisplayRates()
    {
        if (_model is null || _block.SelectedIndex < 0 || _category.SelectedIndex < 0) return;
        _loading = true;
        try
        {
            _table.Rows.Clear(); var category = _categories[_category.SelectedIndex];
            var group = SelectedItems; var weights = _model.ReadItemWeights(_block.SelectedIndex, category.Id, _items);
            for (var i = 0; i < group.Length; i++)
            {
                var cumulative = _model.ReadItemCumulative(_block.SelectedIndex, group[i].Id);
                _table.Rows.Add(group[i].Id, group[i].Name, category.Name, weights[i], cumulative);
            }
            SetStatusForCurrent();
        }
        finally { _loading = false; }
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Weight") return;
        if (!int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 999)
        { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = "個別重みは0〜999で入力してください。"; }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private int[] ReadVisibleWeights() => _table.Rows.Cast<DataRow>().Select(r => Convert.ToInt32(r["Weight"], CultureInfo.InvariantCulture)).ToArray();

    private void ItemChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _model is null || e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Weight") return;
        ApplyVisibleWeights();
    }

    private void ApplyVisibleWeights()
    {
        if (_model is null || _block.SelectedIndex < 0 || _category.SelectedIndex < 0) return;
        var weights = ReadVisibleWeights();
        if (weights.Any(w => w < 0)) { _status.Text = "読み込んだCDFが降順です。負の行を0以上へ修正してください。"; return; }
        try
        {
            _model.WriteItemWeights(_block.SelectedIndex, SelectedCategoryId, _items, weights);
            _loading = true;
            try { var cumulative = 0; for (var i = 0; i < _table.Rows.Count; i++) { cumulative += weights[i]; _table.Rows[i]["Cumulative"] = cumulative; } }
            finally { _loading = false; }
            SetStatusForCurrent();
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void SetStatusForCurrent()
    {
        if (_model is null) return;
        var weights = ReadVisibleWeights(); var sum = weights.Sum();
        _status.ForeColor = sum > 999 ? Color.Firebrick : Color.DarkOrange;
        _status.Text = $"未保存のローカル状態: block {_block.SelectedIndex + 1} / category {SelectedCategoryId} / 有効item {_table.Rows.Count}件 / CDF合計 {sum}/999。reserved bytesを保持。PINE memoryには書き込みません。";
    }

    private void AverageRates()
    {
        if (_model is null) return;
        var weights = ReadVisibleWeights(); var active = weights.Count(w => w > 0);
        if (active == 0) { MessageBox.Show(this, "有効な個別率がありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var result = new int[weights.Length]; for (var i = 0; i < weights.Length; i++) if (weights[i] > 0) result[i] = 999 / active;
        DistributeRemainder(result, weights); ApplyResult(result);
    }

    private void NormalizeRates()
    {
        if (_model is null) return;
        try { ApplyResult(ItemRateTableModel.NormalizeTo999(ReadVisibleWeights())); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static void DistributeRemainder(int[] target, int[] source)
    {
        var active = Enumerable.Range(0, source.Length).Where(i => source[i] > 0).ToArray(); var remaining = 999 - target.Sum();
        for (var i = 0; remaining > 0; i++, remaining--) target[active[i % active.Length]]++;
    }

    private void ApplyResult(int[] weights)
    {
        _loading = true; try { for (var i = 0; i < weights.Length; i++) _table.Rows[i]["Weight"] = weights[i]; } finally { _loading = false; }
        ApplyVisibleWeights();
    }

    private void CopyRates()
    {
        if (_model is null) return; _clipboard = ReadVisibleWeights(); _paste.Enabled = true;
        _status.Text = $"category {SelectedCategoryId} の個別重み{_clipboard.Length}件をコピーしました。ローカル編集のみ。";
    }

    private void PasteRates()
    {
        if (_model is null || _clipboard is null || _clipboard.Length != _table.Rows.Count) return;
        if (MessageBox.Show(this, $"コピーしたcategory {SelectedCategoryId} の{_clipboard.Length}件の個別重みを、このblockへ貼り付けますか？", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        ApplyResult((int[])_clipboard.Clone());
    }

    private void SaveData()
    {
        if (_model is null) { MessageBox.Show(this, "先にデータを読み込んでください。"); return; }
        _grid.EndEdit();
        var visibleWeights = ReadVisibleWeights();
        if (visibleWeights.Any(w => w < 0 || w > 999) || visibleWeights.Sum() > 999)
        { MessageBox.Show(this, "表示中のカテゴリ率が不正または999超過です。調整してから保存してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        for (var block = 0; block < _model.BlockCount; block++)
        {
            foreach (var category in _categories)
            {
                var weights = _model.ReadItemWeights(block, category.Id, _items);
                if (weights.Any(w => w < 0) || weights.Sum() > 999)
                { MessageBox.Show(this, $"block {block + 1}, category {category.Id} のCDFが不正または999超過です。調整してから保存してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
        }
        var bytes = _model.ExportBytes();
        if (_saveToProject is not null)
        {
            try { var name = _saveToProject(bytes); _status.ForeColor = Color.SeaGreen; _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "Legacy item-rate binary (*.bin)|*.bin|すべてのファイル (*.*)|*.*", DefaultExt = "bin", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_source) + "_edited.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dialog.FileName, bytes); _status.ForeColor = Color.SeaGreen; _status.Text = $"保存しました: {dialog.FileName}。カテゴリ/個別item率を保存し、reserved 28..29/738..767と他blockを保持。PCSX2には反映していません。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
