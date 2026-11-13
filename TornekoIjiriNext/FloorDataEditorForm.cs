using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

internal enum LegacyFieldWidth { Byte, UInt16 }
internal sealed record FloorFieldSpec(string Name, int Offset, LegacyFieldWidth Width, string Note = "");

/// <summary>
/// Offline editor for the 112-byte FloorData layout documented from the legacy HSP.
/// Reserved bytes are preserved verbatim. This form has no PINE/game-memory write path.
/// </summary>
internal sealed class FloorDataEditorForm : Form
{
    private const int FloorSize = 112;
    private const int MaximumFloors = 4096;
    private static readonly FloorFieldSpec[] Fields = BuildFields();
    private static byte[]? _copiedFloor;
    private readonly ComboBox _floor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly DataGridView _grid = new();
    private readonly DataTable _fieldTable = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(940, 0), ForeColor = Color.DarkSlateBlue };
    private byte[]? _workingBytes;
    private string _sourceDescription = "未読込";
    private bool _loading;
    private readonly Func<byte[], string>? _saveToProject;

    public FloorDataEditorForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "フロア設定 (112-byte legacy layout / offline)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 720);
        MinimumSize = new Size(760, 520);
        _fieldTable.Columns.Add("Name", typeof(string));
        _fieldTable.Columns.Add("Offset", typeof(string));
        _fieldTable.Columns.Add("Width", typeof(string));
        _fieldTable.Columns.Add("Value", typeof(int));
        _fieldTable.Columns.Add("Note", typeof(string));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "112-byteデータを開く…", AutoSize = true };
        open.Click += (_, _) => OpenData();
        toolbar.Controls.Add(open);
        toolbar.Controls.Add(new Label { Text = "フロア", AutoSize = true, Margin = new Padding(10, 8, 3, 0) });
        toolbar.Controls.Add(_floor);
        var save = new Button { Text = "旧形式binとして保存…", AutoSize = true };
        save.Click += (_, _) => SaveData();
        toolbar.Controls.Add(save);
        var copyFloor = new Button { Text = "選択フロアをコピー", AutoSize = true };
        copyFloor.Click += (_, _) => CopyFloor();
        toolbar.Controls.Add(copyFloor);
        var pasteFloor = new Button { Text = "コピーしたフロアを貼り付け", AutoSize = true };
        pasteFloor.Click += (_, _) => PasteFloor();
        toolbar.Controls.Add(pasteFloor);
        var trapAverage = new Button { Text = "罠CDFを平均化", AutoSize = true };
        trapAverage.Click += (_, _) => AverageTrapWeights();
        toolbar.Controls.Add(trapAverage);
        var trapNormalize = new Button { Text = "罠CDFを999に調整", AutoSize = true };
        trapNormalize.Click += (_, _) => NormalizeTrapWeights();
        toolbar.Controls.Add(trapNormalize);
        _floor.SelectedIndexChanged += (_, _) => DisplayFloor();
        root.Controls.Add(toolbar, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.DataSource = _fieldTable;
        _grid.CellValidating += ValidateCell;
        _grid.CellValueChanged += FieldChanged;
        _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        _status.Text = "旧解析資料の112-byte FloorData layout。単体または連続レコードとして表示します。予約byteは保存時も保持します。対象ゲームSLPM-62199の実アドレスとの対応は未検証で、ゲームメモリには書き込みません。";
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadFile(initialPath);
    }

    private static FloorFieldSpec[] BuildFields()
    {
        var fields = new List<FloorFieldSpec>
        {
            new("部屋形状/輪", 0x00, LegacyFieldWidth.UInt16),
            new("部屋分割タイプ", 0x02, LegacyFieldWidth.UInt16),
            new("水路/特殊地形", 0x04, LegacyFieldWidth.UInt16),
            new("フロア属性flags", 0x06, LegacyFieldWidth.UInt16),
            new("店出現率", 0x08, LegacyFieldWidth.UInt16),
            new("モンスターハウス率", 0x0A, LegacyFieldWidth.UInt16),
            new("呪い率", 0x10, LegacyFieldWidth.Byte),
            new("未知flag (var_482)", 0x12, LegacyFieldWidth.Byte, "意味未確定"),
            new("未知flag (var_485)", 0x16, LegacyFieldWidth.Byte, "意味未確定"),
            new("チェックflags", 0x18, LegacyFieldWidth.Byte),
            new("最大床落ちアイテム数", 0x1C, LegacyFieldWidth.Byte),
            new("最大罠数", 0x1E, LegacyFieldWidth.Byte),
            new("初期敵数", 0x20, LegacyFieldWidth.Byte),
            new("最大敵数", 0x22, LegacyFieldWidth.Byte),
            new("祝福率", 0x24, LegacyFieldWidth.Byte),
            new("AG value", 0x26, LegacyFieldWidth.UInt16),
            new("床落ちアイテムテーブル番号", 0x28, LegacyFieldWidth.UInt16),
            new("店アイテムテーブル番号", 0x2A, LegacyFieldWidth.UInt16),
            new("変化の壷アイテムテーブル番号", 0x2C, LegacyFieldWidth.UInt16),
            new("モノカの杖アイテムテーブル番号", 0x2E, LegacyFieldWidth.UInt16),
            new("壁埋まりアイテムテーブル番号", 0x30, LegacyFieldWidth.UInt16),
            new("固定部屋ID", 0x32, LegacyFieldWidth.Byte),
            new("壁アイテム数", 0x36, LegacyFieldWidth.Byte),
            new("視界範囲", 0x3A, LegacyFieldWidth.Byte),
            new("地形タイプ", 0x64, LegacyFieldWidth.Byte),
            new("フロアBGM番号", 0x66, LegacyFieldWidth.Byte),
            new("ひとくいばこ化率", 0x68, LegacyFieldWidth.Byte)
        };
        for (var i = 0; i < 20; i++) fields.Add(new FloorFieldSpec($"罠CDF[{i}]", 0x3C + i * 2, LegacyFieldWidth.UInt16, "20種の累積値"));
        return fields.OrderBy(field => field.Offset).ToArray();
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderText = column.Name switch
            {
                "Name" => "フィールド", "Offset" => "offset", "Width" => "幅", "Value" => "値 (decimal)", "Note" => "注記", _ => column.Name
            };
            column.ReadOnly = column.Name != "Value";
        }
        foreach (var name in new[] { "Name", "Offset", "Width", "Value", "Note" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].FillWeight = name switch { "Name" => 180, "Offset" => 60, "Width" => 55, "Value" => 75, _ => 150 };
    }

    private void OpenData()
    {
        using var dialog = new OpenFileDialog { Filter = "Legacy floor data (*.bin;*.json)|*.bin;*.json|すべてのファイル (*.*)|*.*" };
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
                var hex = root.EnumerateObject().FirstOrDefault(property => property.Name.Equals("BytesHex", StringComparison.OrdinalIgnoreCase)).Value;
                if (hex.ValueKind != JsonValueKind.String) throw new InvalidDataException("単一領域キャプチャJSONのBytesHexが必要です。");
                bytes = Convert.FromHexString(hex.GetString() ?? "");
                var address = root.EnumerateObject().FirstOrDefault(property => property.Name.Equals("Address", StringComparison.OrdinalIgnoreCase)).Value;
                if (address.TryGetUInt32(out var eeAddress)) description += $" / EE 0x{eeAddress:X8}";
            }
            if (bytes.Length == 0 || bytes.Length % FloorSize != 0) throw new InvalidDataException($"読み込みデータ長は112の倍数である必要があります。現在: {bytes.Length} bytes");
            if (bytes.Length / FloorSize > MaximumFloors) throw new InvalidDataException($"上限{MaximumFloors}フロアを超えています。");
            _workingBytes = bytes;
            _sourceDescription = description;
            _floor.Items.Clear();
            for (var i = 0; i < bytes.Length / FloorSize; i++) _floor.Items.Add($"{i + 1} (offset 0x{i * FloorSize:X})");
            _floor.SelectedIndex = 0;
            _status.ForeColor = Color.DarkSlateBlue;
            _status.Text = $"読み込み: {description} / {bytes.Length / FloorSize}レコード。レコード配置は旧版由来で、現行ゲームへの適用可否は未検証。予約byteは保持します。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読み込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DisplayFloor()
    {
        if (_workingBytes is null || _floor.SelectedIndex < 0) return;
        _loading = true;
        try
        {
            _fieldTable.Rows.Clear();
            var start = _floor.SelectedIndex * FloorSize;
            foreach (var field in Fields)
            {
                var value = field.Width == LegacyFieldWidth.Byte
                    ? _workingBytes[start + field.Offset]
                    : BinaryPrimitives.ReadUInt16LittleEndian(_workingBytes.AsSpan(start + field.Offset, 2));
                _fieldTable.Rows.Add(field.Name, $"0x{field.Offset:X2}", field.Width == LegacyFieldWidth.Byte ? "uint8" : "uint16 LE", (int)value, field.Note);
            }
        }
        finally { _loading = false; }
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Value") return;
        var name = Convert.ToString(_fieldTable.Rows[e.RowIndex]["Name"], CultureInfo.InvariantCulture) ?? "";
        var spec = Fields.First(field => field.Name == name);
        var max = spec.Width == LegacyFieldWidth.Byte ? byte.MaxValue : ushort.MaxValue;
        if (!int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0 || value > max)
        {
            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = $"値は0〜{max}の範囲で入力してください。";
        }
        else if (name.StartsWith("罠CDF[", StringComparison.Ordinal) &&
                 !IsValidTrapCumulative(name, value))
        {
            e.Cancel = true;
            _grid.Rows[e.RowIndex].ErrorText = "罠CDFは0〜999の非減少値で入力してください。";
        }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private bool IsValidTrapCumulative(string name, int value)
    {
        var open = name.IndexOf('[');
        var close = name.IndexOf(']');
        if (open < 0 || close <= open || !int.TryParse(name.AsSpan(open + 1, close - open - 1), out var index)) return false;
        if (value > 999) return false;
        int? previous = index > 0 ? ReadDisplayedFieldValue($"罠CDF[{index - 1}]") : null;
        int? next = index < 19 ? ReadDisplayedFieldValue($"罠CDF[{index + 1}]") : null;
        return (!previous.HasValue || value >= previous.Value) && (!next.HasValue || value <= next.Value);
    }

    private int? ReadDisplayedFieldValue(string fieldName)
    {
        foreach (DataRow row in _fieldTable.Rows)
            if (string.Equals(Convert.ToString(row["Name"], CultureInfo.InvariantCulture), fieldName, StringComparison.Ordinal))
                return Convert.ToInt32(row["Value"], CultureInfo.InvariantCulture);
        return null;
    }

    private void FieldChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.ColumnIndex < 0 || _workingBytes is null || _grid.Columns[e.ColumnIndex].Name != "Value" || _floor.SelectedIndex < 0) return;
        var row = _fieldTable.Rows[e.RowIndex];
        var name = Convert.ToString(row["Name"], CultureInfo.InvariantCulture) ?? "";
        var field = Fields.First(spec => spec.Name == name);
        var value = Convert.ToUInt16(row["Value"], CultureInfo.InvariantCulture);
        var offset = _floor.SelectedIndex * FloorSize + field.Offset;
        if (field.Width == LegacyFieldWidth.Byte) _workingBytes[offset] = (byte)value;
        else BinaryPrimitives.WriteUInt16LittleEndian(_workingBytes.AsSpan(offset, 2), value);
        _status.ForeColor = Color.DarkOrange;
        _status.Text = $"未保存のローカル編集あり: {_sourceDescription} / {_floor.Items.Count} floors。PINE/PCSX2 memoryへの書き込みはありません。未解釈フィールドは編集前に意味を確認してください。";
    }

    private void SaveData()
    {
        if (_workingBytes is null) { MessageBox.Show(this, "先にフロアデータを開いてください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _grid.EndEdit();
        if (_saveToProject is not null)
        {
            try
            {
                var name = _saveToProject(_workingBytes.ToArray());
                _status.ForeColor = Color.SeaGreen;
                _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません。";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "Legacy floor data (*.bin)|*.bin|すべてのファイル (*.*)|*.*", DefaultExt = "bin", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(_sourceDescription) + "_edited.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, _workingBytes);
            _status.ForeColor = Color.SeaGreen;
            _status.Text = $"旧形式データをローカル保存しました: {dialog.FileName}。ゲームメモリには反映していません。予約byteは入力値を保持しています。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void CopyFloor()
    {
        if (_workingBytes is null || _floor.SelectedIndex < 0) return;
        _grid.EndEdit();
        _copiedFloor = _workingBytes.AsSpan(_floor.SelectedIndex * FloorSize, FloorSize).ToArray();
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = $"フロア{_floor.SelectedIndex + 1}をローカルコピーしました。貼り付け対象は選択中の112-byte recordです。";
    }

    private void PasteFloor()
    {
        if (_workingBytes is null || _floor.SelectedIndex < 0 || _copiedFloor is null) return;
        _grid.EndEdit();
        var answer = MessageBox.Show(this,
            $"選択中のフロア{_floor.SelectedIndex + 1}の112 bytes全体を、コピー元recordで置き換えますか？\nこの操作はアプリ内の読込データだけを変更します。",
            "ローカルデータの置換", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        _copiedFloor.CopyTo(_workingBytes.AsSpan(_floor.SelectedIndex * FloorSize, FloorSize));
        DisplayFloor();
        _status.ForeColor = Color.DarkOrange;
        _status.Text = $"未保存のローカル編集あり: フロア{_floor.SelectedIndex + 1}をコピー内容で置換しました。ゲームメモリへの書き込みはありません。";
    }

    private int[]? ReadTrapWeights()
    {
        if (_workingBytes is null || _floor.SelectedIndex < 0) return null;
        var start = _floor.SelectedIndex * FloorSize;
        var values = Enumerable.Range(0, 20).Select(index => (int)BinaryPrimitives.ReadUInt16LittleEndian(_workingBytes.AsSpan(start + 0x3C + index * 2, 2))).ToArray();
        var weights = new int[20];
        var previous = 0;
        for (var i = 0; i < 20; i++) { weights[i] = values[i] - previous; previous = values[i]; }
        return weights;
    }

    private void AverageTrapWeights()
    {
        var weights = ReadTrapWeights();
        if (weights is null) return;
        if (weights.Any(weight => weight < 0)) { MessageBox.Show(this, "罠CDFが降順です。元データを確認してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var active = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0).ToArray();
        if (active.Length == 0) { MessageBox.Show(this, "出現重みが正の罠がありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var average = 999 / active.Length;
        foreach (var index in active) weights[index] = average;
        AdjustTrapResidual(weights);
        WriteTrapWeights(weights);
    }

    private void NormalizeTrapWeights()
    {
        var weights = ReadTrapWeights();
        if (weights is null) return;
        if (weights.Any(weight => weight < 0)) { MessageBox.Show(this, "罠CDFが降順です。元データを確認してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!AdjustTrapResidual(weights)) { MessageBox.Show(this, "出現重みが正の罠がありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        WriteTrapWeights(weights);
    }

    private static bool AdjustTrapResidual(int[] weights)
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

    private void WriteTrapWeights(int[] weights)
    {
        if (_workingBytes is null || _floor.SelectedIndex < 0) return;
        var start = _floor.SelectedIndex * FloorSize;
        var cumulative = 0;
        _loading = true;
        try
        {
            for (var i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                BinaryPrimitives.WriteUInt16LittleEndian(_workingBytes.AsSpan(start + 0x3C + i * 2, 2), (ushort)cumulative);
            }
        }
        finally { _loading = false; }
        DisplayFloor();
        _status.ForeColor = Color.DarkOrange;
        _status.Text = $"罠CDFを更新しました。合計999。ローカル編集のみで、ゲームメモリには書き込んでいません。";
    }
}
