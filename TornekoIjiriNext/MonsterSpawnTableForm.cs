using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

/// <summary>
/// Offline editor for the legacy HSP six-byte spawn entry (ID, cumulative probability, level).
/// This form deliberately has no PineClient reference and cannot write game memory.
/// </summary>
internal sealed class MonsterSpawnTableForm : Form
{
    private const int EntrySize = 6;
    private const int MaxEntries = 512; // Legacy HSP arrays were sized to 512 entries.
    private readonly DataTable _table = CreateTable();
    private readonly DataGridView _grid = new();
    private readonly Button _pasteButton = new() { Text = "コピーした表を貼り付け", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(940, 0) };
    private byte[]? _originalBytes;
    private static byte[]? _copiedTable;
    private string _sourceDescription = "未読込";
    private bool _loading;
    private bool _dirty;
    private readonly Func<byte[], string>? _saveToProject;

    public MonsterSpawnTableForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "モンスター出現テーブル（オフライン編集・未検証形式）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(760, 520);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "旧形式ファイル/単一キャプチャを開く…", AutoSize = true };
        open.Click += (_, _) => OpenData();
        actions.Controls.Add(open);
        var average = new Button { Text = "有効行を平均化", AutoSize = true };
        average.Click += (_, _) => ApplyAverage();
        actions.Controls.Add(average);
        var normalize = new Button { Text = "有効確率を999に調整", AutoSize = true };
        normalize.Click += (_, _) => ApplyNormalize();
        actions.Controls.Add(normalize);
        var copy = new Button { Text = "テーブルをコピー", AutoSize = true };
        copy.Click += (_, _) => CopyTable();
        actions.Controls.Add(copy);
        _pasteButton.Enabled = _copiedTable is not null;
        _pasteButton.Click += (_, _) => PasteTable();
        actions.Controls.Add(_pasteButton);
        var export = new Button { Text = "旧形式binとして保存…", AutoSize = true };
        export.Click += (_, _) => ExportData();
        actions.Controls.Add(export);
        root.Controls.Add(actions, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.DataSource = _table;
        _grid.CellValidating += ValidateCell;
        _grid.CellValueChanged += (_, _) => { if (!_loading) { _dirty = true; RecalculateCumulative(); UpdateStatus(); } };
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        _status.Text = "旧HSPソース由来の形式: 6-byte entry = uint16 monster ID, uint16 cumulative weight, uint16 level (little-endian)。新ゲームの当該アドレスにこの形式が存在するとは未確認です。PINE接続/ゲーム書き込み機能はありません。";
        _status.ForeColor = Color.DarkSlateBlue;
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        UpdateStatus();
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadFile(initialPath);
    }

    private static DataTable CreateTable()
    {
        var table = new DataTable("MonsterSpawnEntries");
        table.Columns.Add("Index", typeof(int));
        table.Columns.Add("MonsterId", typeof(int));
        table.Columns.Add("Weight", typeof(int));
        table.Columns.Add("Cumulative", typeof(int));
        table.Columns.Add("Level", typeof(int));
        return table;
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderText = column.Name switch
            {
                "Index" => "行",
                "MonsterId" => "モンスターID",
                "Weight" => "重み（CDF差分）",
                "Cumulative" => "累積値",
                "Level" => "レベル",
                _ => column.Name
            };
            column.ReadOnly = column.Name is "Index" or "Cumulative";
        }
        if (_grid.Columns.Contains("Index")) _grid.Columns["Index"].FillWeight = 45;
        if (_grid.Columns.Contains("MonsterId")) _grid.Columns["MonsterId"].FillWeight = 100;
        if (_grid.Columns.Contains("Weight")) _grid.Columns["Weight"].FillWeight = 120;
        if (_grid.Columns.Contains("Cumulative")) _grid.Columns["Cumulative"].FillWeight = 100;
        if (_grid.Columns.Contains("Level")) _grid.Columns["Level"].FillWeight = 80;
    }

    private void OpenData()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "旧形式6-byteテーブルまたはPINE単一領域キャプチャを選択",
            Filter = "旧形式bin/JSON (*.bin;*.json)|*.bin;*.json|すべてのファイル (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dialog.FileName);
    }

    private void LoadFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var description = Path.GetFileName(filePath);
            if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (!TryGetProperty(root, "BytesHex", out var hexValue) || hexValue.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("対応するJSONは単一PINEキャプチャ形式のBytesHexを含む必要があります。16領域batch JSONは未対応です。");
                var hex = hexValue.GetString() ?? "";
                try { bytes = Convert.FromHexString(hex); }
                catch (FormatException ex) { throw new InvalidDataException("BytesHexが不正です。", ex); }
                if (TryGetProperty(root, "Address", out var addressValue) && addressValue.TryGetUInt32(out var address))
                    description += $" / EE 0x{address:X8}";
                if (TryGetProperty(root, "GameId", out var gameId) && gameId.ValueKind == JsonValueKind.String)
                    description += $" / ID {gameId.GetString()}";
            }
            LoadBytes(bytes, description);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "読み込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default;
        return false;
    }

    private void LoadBytes(byte[] bytes, string description)
    {
        if (bytes.Length == 0 || bytes.Length % EntrySize != 0)
            throw new InvalidDataException($"データ長は6の倍数である必要があります (取得: {bytes.Length} bytes)。形式の意味は未確定です。");
        var count = bytes.Length / EntrySize;
        if (count > MaxEntries) throw new InvalidDataException($"旧版の表示配列上限{MaxEntries}行を超えています: {count}行。");

        _loading = true;
        try
        {
            _table.Rows.Clear();
            var previousCumulative = 0;
            for (var i = 0; i < count; i++)
            {
                var span = bytes.AsSpan(i * EntrySize, EntrySize);
                var id = BinaryPrimitives.ReadUInt16LittleEndian(span[..2]);
                var cumulative = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
                var level = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
                _table.Rows.Add(i, (int)id, (int)cumulative - previousCumulative, (int)cumulative, (int)level);
                previousCumulative = cumulative;
            }
            _originalBytes = bytes;
            _sourceDescription = description;
            _dirty = false;
        }
        finally { _loading = false; }
        UpdateStatus();
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name is not ("MonsterId" or "Weight" or "Level")) return;
        var min = _grid.Columns[e.ColumnIndex].Name == "Weight" ? 0 : 0;
        var max = _grid.Columns[e.ColumnIndex].Name == "Weight" ? 999 : ushort.MaxValue;
        if (!int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = $"{min}〜{max}の整数を入力してください。";
        }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private void RecalculateCumulative()
    {
        _loading = true;
        try
        {
            var total = 0;
            foreach (DataRow row in _table.Rows)
            {
                total += Convert.ToInt32(row["Weight"], CultureInfo.InvariantCulture);
                row["Cumulative"] = total;
            }
        }
        finally { _loading = false; }
    }

    private int[] GetWeights() => _table.Rows.Cast<DataRow>().Select(row => Convert.ToInt32(row["Weight"], CultureInfo.InvariantCulture)).ToArray();

    private void SetWeights(int[] weights)
    {
        _loading = true;
        try
        {
            var cumulative = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                _table.Rows[i]["Weight"] = weights[i];
                cumulative += weights[i];
                _table.Rows[i]["Cumulative"] = cumulative;
            }
            _dirty = true;
        }
        finally { _loading = false; }
        UpdateStatus();
    }

    // Port of legacy heikinwari + hurinaosi: equalize active rows to 999,
    // then distribute integer remainder in row order.
    private void ApplyAverage()
    {
        if (_table.Rows.Count == 0) return;
        var weights = GetWeights();
        var active = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0).ToArray();
        if (active.Length == 0) { MessageBox.Show(this, "重みが正の行がありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var equal = 999 / active.Length;
        foreach (var index in active) weights[index] = equal;
        DistributeResidual(weights, 999);
        SetWeights(weights);
    }

    // Port of legacy hurinaosi: adjust positive entries round-robin in original order.
    private void ApplyNormalize()
    {
        if (_table.Rows.Count == 0) return;
        var weights = GetWeights();
        if (weights.Any(weight => weight < 0)) { MessageBox.Show(this, "累積値が降順の行があります。個別の重みを修正してから調整してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!DistributeResidual(weights, 999)) { MessageBox.Show(this, "重みが正の行がありません。0を999に調整できません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        SetWeights(weights);
    }

    private static bool DistributeResidual(int[] weights, int target)
    {
        var active = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0).ToArray();
        if (active.Length == 0) return weights.Sum() == target;
        var residual = target - weights.Sum();
        while (residual != 0)
        {
            var changed = false;
            foreach (var index in active)
            {
                if (residual > 0) { weights[index]++; residual--; changed = true; }
                else if (weights[index] > 0) { weights[index]--; residual++; changed = true; }
                if (residual == 0) break;
            }
            if (!changed) return false;
        }
        return true;
    }

    private void ExportData()
    {
        var bytes = BuildCurrentBytes();
        if (bytes is null) return;

        if (_saveToProject is not null)
        {
            try { var name = _saveToProject(bytes); _dirty = false; _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }

        using var dialog = new SaveFileDialog { Filter = "旧版出現テーブル (*.bin)|*.bin|すべてのファイル (*.*)|*.*", DefaultExt = "bin", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_sourceDescription) + "_edited.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dialog.FileName, bytes); _status.Text = $"ローカルファイルに保存しました: {dialog.FileName}。PCSX2のゲームメモリは変更していません。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private byte[]? BuildCurrentBytes()
    {
        _grid.EndEdit();
        if (_table.Rows.Count == 0 || _originalBytes is null) { MessageBox.Show(this, "先に6-byteテーブルを読み込んでください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
        if (!_dirty) return _originalBytes.ToArray();
        var weights = GetWeights();
        var total = 0;
        foreach (var weight in weights)
        {
            if (weight < 0 || weight > 999) { MessageBox.Show(this, "重みは0〜999で指定してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
            total += weight;
        }
        if (total > 999) { MessageBox.Show(this, $"累積重みの合計が999を超えています: {total}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }

        var bytes = new byte[_table.Rows.Count * EntrySize];
        var cumulative = 0;
        for (var i = 0; i < _table.Rows.Count; i++)
        {
            var row = _table.Rows[i];
            var id = Convert.ToInt32(row["MonsterId"], CultureInfo.InvariantCulture);
            var level = Convert.ToInt32(row["Level"], CultureInfo.InvariantCulture);
            if (id is < 0 or > ushort.MaxValue || level is < 0 or > ushort.MaxValue)
            { MessageBox.Show(this, $"行{i}のIDまたはレベルが16-bit範囲外です。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
            cumulative += weights[i];
            var span = bytes.AsSpan(i * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt16LittleEndian(span[..2], (ushort)id);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2, 2), (ushort)cumulative);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)level);
        }
        return bytes;
    }

    private void CopyTable()
    {
        _copiedTable = BuildCurrentBytes();
        if (_copiedTable is not null)
        {
            _pasteButton.Enabled = true;
            _status.Text = $"{_table.Rows.Count}行テーブルをアプリ内コピー領域へ保存しました。PCSX2メモリは変更していません。";
        }
    }

    private void PasteTable()
    {
        _grid.EndEdit();
        if (_copiedTable is null) return;
        if (_copiedTable.Length / EntrySize != _table.Rows.Count)
        {
            MessageBox.Show(this, $"行数が一致しません。コピー側={_copiedTable.Length / EntrySize}, 現在={_table.Rows.Count}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var answer = MessageBox.Show(this, "コピーした旧形式テーブルで現在の表示データを置き換えますか？\nPCSX2のゲームメモリには反映しません。", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) LoadBytes(_copiedTable.ToArray(), "アプリ内コピー領域");
    }

    private void UpdateStatus()
    {
        if (_table.Rows.Count == 0) return;
        var weights = GetWeights();
        var sum = weights.Sum();
        var invalid = weights.Any(weight => weight < 0 || weight > 999) || sum > 999;
        _status.Text = $"読み込み: {_sourceDescription} / {_table.Rows.Count}行 / 重み合計 {sum}/999 / {(invalid ? "要確認" : "行データ確認可")}。出力はローカルbinのみ。ゲームメモリへの書き込みは行いません。";
        _status.ForeColor = invalid ? Color.Firebrick : Color.DarkSlateBlue;
    }
}
