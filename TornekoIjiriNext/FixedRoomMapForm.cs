using System.Text;
using System.Text.Json;
using System.Data;

namespace TornekoIjiriNext;

internal sealed record FixedRoomProject(int FormatVersion, int Width, int Height, byte[] Tiles, byte[] ObjectIds, string? ObjectRecordsHex = null);
internal sealed record RoomPaletteItem(byte Tile, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Offline fixed-room tile editor with a reversible implementation of the HSP RLE token stream.</summary>
internal sealed class FixedRoomMapForm : Form
{
    private const int MaximumDimension = 128;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = MaximumDimension, Value = 16, Width = 65 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = MaximumDimension, Value = 12, Width = 65 };
    private readonly ComboBox _palette = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly NumericUpDown _objectId = new() { Minimum = 0, Maximum = 255, Value = 0, Width = 65 };
    private readonly Panel _canvas = new() { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(940, 0), ForeColor = Color.DarkSlateBlue };
    private byte[] _tiles = new byte[16 * 12];
    private byte[] _objectIds = new byte[16 * 12];
    private byte[]? _originalLegacyBlock;
    private byte[]? _objectRecords;
    private int _mapWidth = 16;
    private int _mapHeight = 12;
    private bool _dirty;
    private Point _lastPainted = new(-1, -1);
    private readonly Func<byte[], string>? _saveProjectToBundle;
    private readonly Func<byte[], string>? _savePayloadToBundle;

    public FixedRoomMapForm(string? initialPath = null, Func<byte[], string>? saveProjectToBundle = null, Func<byte[], string>? savePayloadToBundle = null)
    {
        _saveProjectToBundle = saveProjectToBundle;
        _savePayloadToBundle = savePayloadToBundle;
        Text = "固定部屋マップ (legacy RLE / offline)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 760);
        MinimumSize = new Size(700, 520);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "部屋データ/JSONを開く…", AutoSize = true };
        open.Click += (_, _) => OpenData();
        toolbar.Controls.Add(open);
        toolbar.Controls.Add(new Label { Text = "幅", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }); toolbar.Controls.Add(_width);
        toolbar.Controls.Add(new Label { Text = "高さ", AutoSize = true, Margin = new Padding(6, 8, 2, 0) }); toolbar.Controls.Add(_height);
        var resize = new Button { Text = "サイズ適用", AutoSize = true };
        resize.Click += (_, _) => ResizeMap((int)_width.Value, (int)_height.Value);
        toolbar.Controls.Add(resize);
        var palette = new List<RoomPaletteItem>();
        for (byte tile = 0; tile < 16; tile++) palette.Add(new RoomPaletteItem(tile, $"Tile {tile:X2}"));
        palette.Add(new RoomPaletteItem(byte.MaxValue, "Object marker (0E + ID)"));
        _palette.Items.AddRange(palette.Cast<object>().ToArray()); _palette.SelectedIndex = 0;
        toolbar.Controls.Add(new Label { Text = "パレット", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }); toolbar.Controls.Add(_palette);
        toolbar.Controls.Add(new Label { Text = "Object ID", AutoSize = true, Margin = new Padding(4, 8, 2, 0) }); toolbar.Controls.Add(_objectId);
        var saveJson = new Button { Text = "編集プロジェクトを保存…", AutoSize = true };
        saveJson.Click += (_, _) => SaveProject(); toolbar.Controls.Add(saveJson);
        var export = new Button { Text = "旧RLE部屋payloadを保存…", AutoSize = true };
        export.Click += (_, _) => ExportLegacyPayload(); toolbar.Controls.Add(export);
        var loadObjects = new Button { Text = "20-byte配置object tableを開く…", AutoSize = true };
        loadObjects.Click += (_, _) => OpenObjectRecords(); toolbar.Controls.Add(loadObjects);
        var editObjects = new Button { Text = "配置object編集…", AutoSize = true };
        editObjects.Click += (_, _) => EditObjectRecords(); toolbar.Controls.Add(editObjects);
        root.Controls.Add(toolbar, 0, 0);

        _canvas.Paint += DrawMap;
        _canvas.MouseDown += PaintAtMouse;
        _canvas.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) PaintAt(e.Location); };
        root.Controls.Add(_canvas, 0, 1);
        _status.Text = "HSP由来のRLE: width/height byte、reserved byte、tile stream。通常tokenはtile high-nibble/run low-nibble、0x0Eはobject marker+ID。タイル名の意味とゲームアドレスは未検証。編集・保存はオフラインです。";
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadFile(initialPath);
    }

    private void DrawMap(object? sender, PaintEventArgs e)
    {
        if (_mapWidth == 0 || _mapHeight == 0) return;
        var cellWidth = (float)_canvas.ClientSize.Width / _mapWidth;
        var cellHeight = (float)_canvas.ClientSize.Height / _mapHeight;
        using var gridPen = new Pen(Color.FromArgb(180, 180, 180));
        using var textBrush = new SolidBrush(Color.Black);
        using var objectBrush = new SolidBrush(Color.DarkMagenta);
        for (var y = 0; y < _mapHeight; y++)
        for (var x = 0; x < _mapWidth; x++)
        {
            var index = y * _mapWidth + x;
            var tile = _tiles[index];
            var rect = new RectangleF(x * cellWidth, y * cellHeight, cellWidth, cellHeight);
            using var brush = new SolidBrush(TileColor(tile));
            e.Graphics.FillRectangle(brush, rect);
            e.Graphics.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width, rect.Height);
            if (tile == byte.MaxValue && cellWidth >= 18 && cellHeight >= 15)
                e.Graphics.DrawString(_objectIds[index].ToString("X2"), Font, objectBrush, rect.X + 1, rect.Y + 1);
            else if (cellWidth >= 24 && cellHeight >= 20)
                e.Graphics.DrawString(tile.ToString("X"), Font, textBrush, rect.X + 2, rect.Y + 1);
        }
    }

    private static Color TileColor(byte tile) => tile switch
    {
        0 => Color.WhiteSmoke, 1 => Color.Silver, 2 => Color.LightGray, 3 => Color.SandyBrown,
        4 => Color.LightBlue, 5 => Color.SteelBlue, 6 => Color.DarkSeaGreen, 7 => Color.DarkSlateGray,
        8 => Color.PeachPuff, 9 => Color.LightGoldenrodYellow, 10 => Color.LightGreen, 11 => Color.Tan,
        12 => Color.MistyRose, 13 => Color.Khaki, 14 => Color.Plum, 15 => Color.LightSalmon, _ => Color.Magenta
    };

    private void PaintAtMouse(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _lastPainted = new Point(-1, -1); PaintAt(e.Location); }
    }

    private void PaintAt(Point point)
    {
        if (_mapWidth < 1 || _mapHeight < 1 || _canvas.ClientSize.Width < 1 || _canvas.ClientSize.Height < 1) return;
        var x = Math.Clamp((int)((long)point.X * _mapWidth / _canvas.ClientSize.Width), 0, _mapWidth - 1);
        var y = Math.Clamp((int)((long)point.Y * _mapHeight / _canvas.ClientSize.Height), 0, _mapHeight - 1);
        if (_lastPainted == new Point(x, y)) return;
        _lastPainted = new Point(x, y);
        var item = _palette.SelectedItem as RoomPaletteItem;
        if (item is null) return;
        var index = y * _mapWidth + x;
        _tiles[index] = item.Tile;
        _objectIds[index] = item.Tile == byte.MaxValue ? (byte)_objectId.Value : (byte)0;
        _dirty = true;
        _canvas.Invalidate();
        UpdateStatus("map");
    }

    private void ResizeMap(int width, int height)
    {
        var tiles = new byte[checked(width * height)];
        var objects = new byte[tiles.Length];
        for (var y = 0; y < Math.Min(height, _mapHeight); y++)
        for (var x = 0; x < Math.Min(width, _mapWidth); x++)
        {
            tiles[y * width + x] = _tiles[y * _mapWidth + x];
            objects[y * width + x] = _objectIds[y * _mapWidth + x];
        }
        _mapWidth = width; _mapHeight = height; _tiles = tiles; _objectIds = objects;
        _dirty = true;
        _canvas.Invalidate(); UpdateStatus("size");
    }

    private void OpenData()
    {
        using var dialog = new OpenFileDialog { Filter = "固定部屋payload/project (*.bin;*.json)|*.bin;*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dialog.FileName);
    }

    private void LoadFile(string filePath)
    {
        try
        {
            if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(filePath));
                var root = doc.RootElement;
                if (HasProperty(root, "Width") && HasProperty(root, "Height") && HasProperty(root, "Tiles"))
                {
                    var project = JsonSerializer.Deserialize<FixedRoomProject>(File.ReadAllText(filePath), JsonOptions)
                        ?? throw new InvalidDataException("Room project JSONが空です。");
                    LoadProject(project);
                }
                else
                {
                    var hex = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("BytesHex", StringComparison.OrdinalIgnoreCase)).Value;
                    if (hex.ValueKind != JsonValueKind.String) throw new InvalidDataException("Room projectまたは単一capture JSONのBytesHexが必要です。");
                    LoadLegacyPayload(Convert.FromHexString(hex.GetString() ?? ""));
                }
            }
            else LoadLegacyPayload(File.ReadAllBytes(filePath));
            UpdateStatus(Path.GetFileName(filePath));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "固定部屋データ読込失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static bool HasProperty(JsonElement element, string name) =>
        element.EnumerateObject().Any(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void LoadProject(FixedRoomProject project)
    {
        if (project.FormatVersion is not (1 or 2) || project.Width is < 1 or > MaximumDimension || project.Height is < 1 or > MaximumDimension || project.Tiles.Length != project.Width * project.Height || project.ObjectIds.Length != project.Tiles.Length || project.Tiles.Any(tile => tile > 15 && tile != byte.MaxValue))
            throw new InvalidDataException("固定部屋JSONのversion、サイズ、またはtile配列が不正です。");
        _mapWidth = project.Width; _mapHeight = project.Height; _tiles = project.Tiles.ToArray(); _objectIds = project.ObjectIds.ToArray();
        _objectRecords = project.ObjectRecordsHex is null ? null : Convert.FromHexString(project.ObjectRecordsHex);
        if (_objectRecords is not null && _objectRecords.Length != FixedRoomObjectEditorForm.RecordCount * FixedRoomObjectEditorForm.RecordSize)
            throw new InvalidDataException("JSONのobject tableは220×20 bytesである必要があります。");
        _width.Value = _mapWidth; _height.Value = _mapHeight; _originalLegacyBlock = null; _dirty = false;
        _canvas.Invalidate();
    }

    private void LoadLegacyPayload(byte[] payload)
    {
        if (payload.Length < 4) throw new InvalidDataException("legacy room payloadが短すぎます。");
        var width = payload[0]; var height = payload[1];
        if (width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension) throw new InvalidDataException($"旧payloadの幅/高さが不正です: {width}×{height}");
        var cells = checked(width * height);
        var tiles = new byte[cells]; var objects = new byte[cells];
        var cursor = 3; var filled = 0;
        while (filled < cells)
        {
            if (cursor >= payload.Length) throw new InvalidDataException($"RLE streamが途中で終わりました。{filled}/{cells} cellsを復号しました。");
            var token = payload[cursor++];
            if (token == 0x0E)
            {
                if (cursor >= payload.Length) throw new InvalidDataException("object marker 0x0Eの後ろにobject ID byteがありません。");
                tiles[filled] = byte.MaxValue; objects[filled] = payload[cursor++]; filled++;
                continue;
            }
            var tile = (byte)(token >> 4); var run = (token & 0x0F) + 1;
            if (filled + run > cells) throw new InvalidDataException("RLE runが指定width×heightを超えています。");
            Array.Fill(tiles, tile, filled, run); filled += run;
        }
        _mapWidth = width; _mapHeight = height; _tiles = tiles; _objectIds = objects;
        _width.Value = width; _height.Value = height; _originalLegacyBlock = payload; _objectRecords = null; _dirty = false;
        _canvas.Invalidate();
    }

    private byte[] EncodeRle()
    {
        var output = new List<byte>(_tiles.Length);
        var i = 0;
        while (i < _tiles.Length)
        {
            var tile = _tiles[i];
            if (tile == byte.MaxValue) { output.Add(0x0E); output.Add(_objectIds[i]); i++; continue; }
            var maximumRun = tile == 0 ? 14 : 16; // 0x0E is reserved for an object marker.
            var run = 1;
            while (i + run < _tiles.Length && _tiles[i + run] == tile && run < maximumRun) run++;
            output.Add((byte)((tile << 4) | (run - 1)));
            i += run;
        }
        return output.ToArray();
    }

    private void SaveProject()
    {
        try
        {
            var project = new FixedRoomProject(2, _mapWidth, _mapHeight, _tiles, _objectIds, _objectRecords is null ? null : Convert.ToHexString(_objectRecords));
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, JsonOptions));
            if (_saveProjectToBundle is not null)
            {
                var name = _saveProjectToBundle(bytes); _dirty = false; UpdateStatus(name); return;
            }
            using var dialog = new SaveFileDialog { Filter = "Fixed-room project (*.room.json)|*.room.json", DefaultExt = "json", AddExtension = true, FileName = "fixed-room.room.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, bytes);
            _dirty = false; UpdateStatus(Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "JSON保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportLegacyPayload()
    {
        var encoded = EncodeRle();
        byte[] payload;
        if (_originalLegacyBlock is not null)
        {
            var capacity = _originalLegacyBlock.Length - 3;
            if (encoded.Length > capacity) { MessageBox.Show(this, $"圧縮後RLE {encoded.Length} bytesが元payload容量 {capacity} bytesを超えています。サイズを戻すか、プロジェクトJSONで保存してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            payload = _originalLegacyBlock.ToArray();
            payload[0] = (byte)_mapWidth; payload[1] = (byte)_mapHeight;
            Array.Copy(encoded, 0, payload, 3, encoded.Length);
        }
        else
        {
            payload = new byte[3 + encoded.Length]; payload[0] = (byte)_mapWidth; payload[1] = (byte)_mapHeight;
            Array.Copy(encoded, 0, payload, 3, encoded.Length);
        }
        try
        {
            if (_savePayloadToBundle is not null) { var name = _savePayloadToBundle(payload); UpdateStatus($"project保存: {name} / RLE {encoded.Length} bytes"); return; }
            using var dialog = new SaveFileDialog { Filter = "Legacy room payload (*.bin)|*.bin", DefaultExt = "bin", AddExtension = true, FileName = "room_payload.bin" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, payload); UpdateStatus($"payload保存: {Path.GetFileName(dialog.FileName)} / RLE {encoded.Length} bytes。ゲームメモリは変更していません。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "payload保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void UpdateStatus(string source = "編集")
    {
        var encodedLength = EncodeRle().Length;
        var capacityText = _originalLegacyBlock is null ? "新規payload" : $"旧payload容量 {_originalLegacyBlock.Length - 3} bytes";
        _status.Text = $"{source}: {_mapWidth}×{_mapHeight} / RLE {encodedLength} bytes / {capacityText} / {(_dirty ? "未保存" : "保存済み")}. Tile IDの意味は未判定。オフライン専用でPCSX2 memoryへ書き込みません。";
    }

    private void OpenObjectRecords()
    {
        using var dialog = new OpenFileDialog { Filter = "220×20-byte object table (*.bin)|*.bin|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            if (bytes.Length != FixedRoomObjectEditorForm.RecordCount * FixedRoomObjectEditorForm.RecordSize)
                throw new InvalidDataException($"旧HSPのobject tableは{FixedRoomObjectEditorForm.RecordCount * FixedRoomObjectEditorForm.RecordSize} bytesを想定します。入力は{bytes.Length} bytesです。");
            _objectRecords = bytes;
            _dirty = true;
            UpdateStatus($"Object table読込: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Object table読込失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void EditObjectRecords()
    {
        if (_objectRecords is null)
        {
            MessageBox.Show(this, "先に220×20-byteの旧object tableファイルを開くか、room project JSONから読み込んでください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var editor = new FixedRoomObjectEditorForm(_objectRecords);
        if (editor.ShowDialog(this) == DialogResult.OK && editor.EditedBytes is not null)
        {
            _objectRecords = editor.EditedBytes;
            _dirty = true;
            UpdateStatus("配置object tableを編集");
        }
    }
}

internal sealed class FixedRoomObjectEditorForm : Form
{
    public const int RecordCount = 220;
    public const int RecordSize = 20;
    private sealed record Field(string Column, string Header, int Offset, bool Wide, string Note = "");
    private static readonly Field[] Fields =
    [
        new("Param00", "U16 @00 / var1055", 0, true, "list index/type reference"),
        new("Param02", "U16 @02 / var1062", 2, true, "shown as G for ID 341"),
        new("Param04", "U8 @04 / var1076", 4, false, "meaning unknown"),
        new("Param08", "U16 @08 / var1056", 8, true, "display lookup uses monster master"),
        new("Param0A", "U16 @0A / var1057", 10, true, "display lookup uses trap master"),
        new("Param0C", "U8 @0C / var1059", 12, false, "display lookup index"),
        new("Param0E", "U8 @0E / var1060", 14, false, "display category index"),
        new("Param0F", "U8 @0F / var1058", 15, false, "display lookup index"),
        new("Param10", "U8 @10 / var1061", 16, false, "hidden flag used by UI"),
        new("Param11", "U8 @11 / var1063", 17, false, "meaning unknown")
    ];
    private readonly DataTable _rows = new();
    private readonly DataGridView _grid = new();
    private byte[] _working;

    public byte[]? EditedBytes { get; private set; }

    public FixedRoomObjectEditorForm(byte[] records)
    {
        if (records.Length != RecordCount * RecordSize) throw new ArgumentException("Object table must contain 220 records of 20 bytes.", nameof(records));
        _working = records.ToArray();
        Text = "固定部屋 object records (offline / raw fields)";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 700);
        MinimumSize = new Size(850, 500);
        _rows.Columns.Add("Index", typeof(int));
        foreach (var field in Fields) _rows.Columns.Add(field.Column, typeof(int));
        _rows.Columns.Add("Note", typeof(string));
        for (var i = 0; i < RecordCount; i++)
        {
            var start = i * RecordSize;
            var values = new object[Fields.Length + 2];
            values[0] = i;
            for (var j = 0; j < Fields.Length; j++)
            {
                var field = Fields[j];
                values[j + 1] = field.Wide
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_working.AsSpan(start + field.Offset, 2))
                    : _working[start + field.Offset];
            }
            values[^1] = "reserved bytes +06,+07,+0D,+12,+13 are preserved; source meaning is not asserted";
            _rows.Rows.Add(values);
        }
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "HSP label_133 accesses a 220×20-byte table. The named variables/offset widths below are source observations; object semantics and game version mapping remain unverified. Reserved bytes are preserved. No PINE write path.", AutoSize = true, MaximumSize = new Size(1040, 0), ForeColor = Color.DarkSlateBlue, Padding = new Padding(0, 0, 0, 8) }, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false; _grid.DataSource = _rows;
        _grid.CellValidating += ValidateCell; _grid.CellValueChanged += OnCellChanged; _grid.DataBindingComplete += (_, _) => ConfigureColumns();
        root.Controls.Add(_grid, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var apply = new Button { Text = "適用して戻る", AutoSize = true, DialogResult = DialogResult.OK };
        apply.Click += (_, _) => { _grid.EndEdit(); EditedBytes = _working.ToArray(); };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(apply); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 2);
        AcceptButton = apply; CancelButton = cancel; Controls.Add(root);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderText = column.Name == "Index" ? "record index" : column.Name == "Note" ? "source note" : Fields.FirstOrDefault(field => field.Column == column.Name)?.Header ?? column.Name;
            column.ReadOnly = column.Name is "Index" or "Note";
        }
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var field = Fields.FirstOrDefault(candidate => candidate.Column == _grid.Columns[e.ColumnIndex].Name);
        if (field is null) return;
        var max = field.Wide ? ushort.MaxValue : byte.MaxValue;
        if (!int.TryParse(Convert.ToString(e.FormattedValue, System.Globalization.CultureInfo.InvariantCulture), out var value) || value < 0 || value > max)
        { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = $"Value must be 0..{max}."; }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private void OnCellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var field = Fields.FirstOrDefault(candidate => candidate.Column == _grid.Columns[e.ColumnIndex].Name);
        if (field is null) return;
        var rowIndex = Convert.ToInt32(_rows.Rows[e.RowIndex]["Index"], System.Globalization.CultureInfo.InvariantCulture);
        var value = Convert.ToInt32(_rows.Rows[e.RowIndex][field.Column], System.Globalization.CultureInfo.InvariantCulture);
        var offset = rowIndex * RecordSize + field.Offset;
        if (field.Wide) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(_working.AsSpan(offset, 2), (ushort)value);
        else _working[offset] = (byte)value;
    }
}
