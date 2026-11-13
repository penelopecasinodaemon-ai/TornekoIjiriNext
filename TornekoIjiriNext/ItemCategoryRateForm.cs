using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

/// <summary>Offline editor for the 14 cumulative category rates at the start of a 768-byte HSP item table.</summary>
internal sealed class ItemCategoryRateForm : Form
{
    private const int TableSize = 768;
    private static readonly string[] CategoryNames = ["剣", "爪", "盾", "杖", "矢", "石", "大砲", "指輪", "巻物", "壷", "草", "パン", "石像", "他"];
    private static ushort[]? _categoryClipboard;
    private readonly ComboBox _block = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly Button _paste = new() { Text = "カテゴリ率を貼り付け", AutoSize = true, Enabled = false };
    private readonly DataGridView _grid = new();
    private readonly DataTable _table = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(940, 0) };
    private byte[]? _bytes;
    private string _source = "未読込";
    private bool _loading;
    private readonly Func<byte[], string>? _saveToProject;

    public ItemCategoryRateForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "アイテム出現率・カテゴリ層 (768-byte legacy table / offline)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(850, 650);
        MinimumSize = new Size(700, 480);
        _table.Columns.Add("CategoryId", typeof(int));
        _table.Columns.Add("CategoryName", typeof(string));
        _table.Columns.Add("Weight", typeof(int));
        _table.Columns.Add("Cumulative", typeof(int));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "768-byteデータを開く…", AutoSize = true };
        open.Click += (_, _) => OpenData();
        bar.Controls.Add(open);
        bar.Controls.Add(new Label { Text = "ブロック", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        bar.Controls.Add(_block);
        var average = new Button { Text = "有効カテゴリを平均化", AutoSize = true };
        average.Click += (_, _) => AverageWeights();
        bar.Controls.Add(average);
        var normalize = new Button { Text = "合計を999に調整", AutoSize = true };
        normalize.Click += (_, _) => NormalizeWeights();
        bar.Controls.Add(normalize);
        var copy = new Button { Text = "カテゴリ率をコピー", AutoSize = true };
        copy.Click += (_, _) => CopyRates();
        bar.Controls.Add(copy);
        _paste.Enabled = _categoryClipboard is not null;
        _paste.Click += (_, _) => PasteRates();
        bar.Controls.Add(_paste);
        var save = new Button { Text = "旧形式binとして保存…", AutoSize = true };
        save.Click += (_, _) => SaveData();
        bar.Controls.Add(save);
        root.Controls.Add(bar, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.DataSource = _table;
        _grid.CellValidating += ValidateCell;
        _grid.CellValueChanged += (_, _) => { if (!_loading) RebuildCurrentBlock(); };
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        _status.Text = "旧HSPではitem tableは768 bytes/block。先頭14×uint16 LE (28 bytes)をカテゴリCDFとして表示します。個別item tableを含む残り740 bytesは不透明データとして保持します。現行ゲームとの対応未検証・PINE書き込みなし。";
        _status.ForeColor = Color.DarkSlateBlue;
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        _block.SelectedIndexChanged += (_, _) => DisplayBlock();
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadFile(initialPath);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderText = column.Name switch { "CategoryId" => "旧版カテゴリID", "CategoryName" => "カテゴリ", "Weight" => "出現重み", "Cumulative" => "累積値 (CDF)", _ => column.Name };
            column.ReadOnly = column.Name != "Weight";
        }
        foreach (var name in new[] { "CategoryId", "CategoryName", "Weight", "Cumulative" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].FillWeight = name switch { "CategoryName" => 160, "CategoryId" => 80, _ => 110 };
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
            var bytes = File.ReadAllBytes(filePath);
            var source = Path.GetFileName(filePath);
            if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(bytes);
                var hex = doc.RootElement.EnumerateObject().FirstOrDefault(property => property.Name.Equals("BytesHex", StringComparison.OrdinalIgnoreCase)).Value;
                if (hex.ValueKind != JsonValueKind.String) throw new InvalidDataException("単一領域PINE capture JSONのBytesHexが必要です。batch JSONには未対応です。");
                bytes = Convert.FromHexString(hex.GetString() ?? "");
                var address = doc.RootElement.EnumerateObject().FirstOrDefault(property => property.Name.Equals("Address", StringComparison.OrdinalIgnoreCase)).Value;
                if (address.TryGetUInt32(out var ee)) source += $" / EE 0x{ee:X8}";
            }
            LoadBytes(bytes, source);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読み込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadBytes(byte[] bytes, string source)
    {
        if (bytes.Length == 0 || bytes.Length % TableSize != 0) throw new InvalidDataException($"データ長は768の倍数である必要があります。現在: {bytes.Length} bytes");
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("入力サイズ上限を超えています。");
        _bytes = bytes;
        _source = source;
        _block.Items.Clear();
        for (var i = 0; i < bytes.Length / TableSize; i++) _block.Items.Add($"block {i + 1} (offset 0x{i * TableSize:X})");
        _block.SelectedIndex = 0;
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = $"読み込み: {source} / {bytes.Length / TableSize} blocks。カテゴリCDF14件のみ編集します。残り740 bytesは未解釈のまま保持します。";
    }

    private void DisplayBlock()
    {
        if (_bytes is null || _block.SelectedIndex < 0) return;
        _loading = true;
        try
        {
            _table.Rows.Clear();
            var start = _block.SelectedIndex * TableSize;
            var previous = 0;
            for (var i = 0; i < CategoryNames.Length; i++)
            {
                var cumulative = (int)BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(start + i * 2, 2));
                _table.Rows.Add(i + 1, CategoryNames[i], cumulative - previous, cumulative);
                previous = cumulative;
            }
        }
        finally { _loading = false; }
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Weight") return;
        if (!int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) || weight is < 0 or > 999)
        {
            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = "重みは0〜999で指定してください。";
        }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private int[] ReadWeights() => _table.Rows.Cast<DataRow>().Select(row => Convert.ToInt32(row["Weight"], CultureInfo.InvariantCulture)).ToArray();

    private static bool Normalize(int[] weights)
    {
        var active = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0).ToArray();
        if (active.Length == 0) return weights.Sum() == 999;
        var remaining = 999 - weights.Sum();
        while (remaining != 0)
        {
            var changed = false;
            foreach (var index in active)
            {
                if (remaining > 0) { weights[index]++; remaining--; changed = true; }
                else if (weights[index] > 0) { weights[index]--; remaining++; changed = true; }
                if (remaining == 0) break;
            }
            if (!changed) return false;
        }
        return true;
    }

    private void AverageWeights()
    {
        if (_bytes is null || _table.Rows.Count == 0) return;
        var weights = ReadWeights();
        if (weights.Any(weight => weight < 0)) { MessageBox.Show(this, "カテゴリCDFが降順です。元データを確認してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var active = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0).ToArray();
        if (active.Length == 0) { MessageBox.Show(this, "正の重みがあるカテゴリがありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        foreach (var index in active) weights[index] = 999 / active.Length;
        if (Normalize(weights)) WriteWeights(weights);
    }

    private void NormalizeWeights()
    {
        if (_bytes is null || _table.Rows.Count == 0) return;
        var weights = ReadWeights();
        if (weights.Any(weight => weight < 0)) { MessageBox.Show(this, "カテゴリCDFが降順です。元データを確認してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!Normalize(weights)) { MessageBox.Show(this, "正の重みがあるカテゴリがありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        WriteWeights(weights);
    }

    private void WriteWeights(int[] weights)
    {
        if (_bytes is null || _block.SelectedIndex < 0) return;
        var offset = _block.SelectedIndex * TableSize;
        var cumulative = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(offset + i * 2, 2), (ushort)cumulative);
        }
        DisplayBlock();
        _status.ForeColor = Color.DarkOrange;
        _status.Text = $"block {_block.SelectedIndex + 1}のカテゴリCDFを編集しました。合計999。その他740 bytesは保持。ゲームメモリへの書き込みはありません。";
    }

    private void RebuildCurrentBlock()
    {
        if (_loading || _bytes is null || _block.SelectedIndex < 0 || _table.Rows.Count != 14) return;
        var weights = ReadWeights();
        var sum = weights.Sum();
        var offset = _block.SelectedIndex * TableSize;
        var cumulative = 0;
        _loading = true;
        try
        {
            for (var i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                _table.Rows[i]["Cumulative"] = cumulative;
                BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(offset + i * 2, 2), (ushort)cumulative);
            }
        }
        finally { _loading = false; }
        _status.ForeColor = sum > 999 ? Color.Firebrick : Color.DarkOrange;
        _status.Text = $"編集中 block {_block.SelectedIndex + 1}: CDF合計 {sum}/999。{(sum > 999 ? "999調整後に保存してください。" : "ローカル編集のみ。") }残り740 bytesを保持しています。";
    }

    private void SaveData()
    {
        if (_bytes is null) { MessageBox.Show(this, "先にデータを開いてください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _grid.EndEdit();
        var weights = ReadWeights();
        if (weights.Any(weight => weight < 0 || weight > 999) || weights.Sum() > 999)
        { MessageBox.Show(this, "現在のカテゴリ重み合計が999を超えています。調整してから保存してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_saveToProject is not null)
        {
            try { var name = _saveToProject(_bytes.ToArray()); _status.ForeColor = Color.SeaGreen; _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "旧版アイテムテーブル (*.bin)|*.bin|すべてのファイル (*.*)|*.*", DefaultExt = "bin", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_source) + "_edited.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, _bytes);
            _status.ForeColor = Color.SeaGreen;
            _status.Text = $"ローカルbinを保存しました: {dialog.FileName}。各768-byte blockの残り740 bytesは保持し、PCSX2メモリは変更していません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void CopyRates()
    {
        if (_bytes is null || _block.SelectedIndex < 0) return;
        var start = _block.SelectedIndex * TableSize;
        _categoryClipboard = Enumerable.Range(0, 14)
            .Select(index => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(start + index * 2, 2)))
            .ToArray();
        _paste.Enabled = true;
        _status.Text = $"block {_block.SelectedIndex + 1}のカテゴリCDF14件をアプリ内コピー領域へ保存しました。個別アイテム領域はコピーしていません。";
    }

    private void PasteRates()
    {
        if (_bytes is null || _block.SelectedIndex < 0 || _categoryClipboard is null) return;
        var answer = MessageBox.Show(this, "コピーした14カテゴリのCDF値を現在のblockへ貼り付けますか？\n個別アイテム領域740 bytesは変更しません。", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        var start = _block.SelectedIndex * TableSize;
        for (var i = 0; i < _categoryClipboard.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(start + i * 2, 2), _categoryClipboard[i]);
        DisplayBlock();
        _status.ForeColor = Color.DarkOrange;
        _status.Text = $"block {_block.SelectedIndex + 1}へカテゴリCDF14件を貼り付けました。個別アイテム領域は保持し、ゲームメモリは変更していません。";
    }
}
