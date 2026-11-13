using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace TornekoIjiriNext;

/// <summary>Offline editor for the legacy per-growth-type, per-level vectors.</summary>
internal sealed class MonsterGrowthTableForm : Form
{
    private const int TypeCount = 12;
    private const int SlotsPerType = 100; // 200-byte type stride / 2-byte level stride.
    private const int AttackBase = 2402;
    private const int DefenseBase = 4802;
    private const int ExperienceBase = 9608;
    private const int MinimumDumpLength = ExperienceBase + (TypeCount - 1) * 400 + (SlotsPerType - 1) * 4 + 4;
    private readonly DataTable _rows = new("MonsterGrowth");
    private readonly DataGridView _grid = new();
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(1000, 0) };
    private byte[]? _bytes;
    private bool _loading;
    private readonly Func<byte[], string>? _saveToProject;

    public MonsterGrowthTableForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "モンスター成長テーブル（オフライン）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(760, 500);
        _rows.Columns.Add("LevelSlot", typeof(int));
        _rows.Columns.Add("HP加算", typeof(int));
        _rows.Columns.Add("攻撃加算", typeof(int));
        _rows.Columns.Add("防御加算", typeof(int));
        _rows.Columns.Add("経験値基準", typeof(long));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var open = new Button { Text = "旧配置のraw dump / captureを開く…", AutoSize = true }; open.Click += (_, _) => OpenDump(); bar.Controls.Add(open);
        bar.Controls.Add(new Label { Text = "成長タイプ", AutoSize = true, Margin = new Padding(10, 8, 3, 0) });
        for (var i = 0; i < TypeCount; i++) _type.Items.Add(i);
        _type.SelectedIndex = 0; _type.SelectedIndexChanged += (_, _) => DisplayType(); bar.Controls.Add(_type);
        var save = new Button { Text = "編集済みraw dumpを保存…", AutoSize = true }; save.Click += (_, _) => SaveDump(); bar.Controls.Add(save);
        root.Controls.Add(bar, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.DataSource = _rows; _grid.CellValidating += ValidateCell; _grid.CellValueChanged += CellChanged;
        _grid.DataBindingComplete += (_, _) => { foreach (DataGridViewColumn c in _grid.Columns) c.ReadOnly = c.Name == "LevelSlot"; };
        root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = "旧HSPのvar_622/623/624/626を参照。12タイプ×最大100スロットをタイプ別表示します。スロットのゲーム内レベル対応・開始アドレスは未検証。PINE read/writeなし。";
        root.Controls.Add(_status, 0, 2); Controls.Add(root);
        if (!string.IsNullOrWhiteSpace(initialPath)) LoadDump(initialPath);
    }

    private void OpenDump()
    {
        using var dialog = new OpenFileDialog { Filter = "Raw binary/capture JSON (*.bin;*.dat;*.json)|*.bin;*.dat;*.json|すべてのファイル (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadDump(dialog.FileName);
    }

    private void LoadDump(string filePath)
    {
        try
        {
            var input = File.ReadAllBytes(filePath);
            if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(input); var root = doc.RootElement;
                var prop = root.EnumerateObject().FirstOrDefault(p => p.Name.Equals("BytesHex", StringComparison.OrdinalIgnoreCase));
                if (prop.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("単一領域capture JSONのBytesHexが見つかりません。");
                input = Convert.FromHexString(prop.Value.GetString() ?? "");
            }
            if (input.Length < MinimumDumpLength) throw new InvalidDataException($"旧配置の12タイプ×100スロットを含む連続raw dumpが必要です。最低{MinimumDumpLength} bytes、入力{input.Length} bytes。");
            _bytes = input; _type.SelectedIndex = 0; DisplayType();
            _status.Text = $"{Path.GetFileName(filePath)} を読込。旧HSPの各タイプ/スロットstrideに基づいて表示中。正しい開始アドレスと実機上の値は未検証、ローカル編集のみ。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DisplayType()
    {
        if (_bytes is null || _type.SelectedIndex < 0) return;
        _loading = true;
        try
        {
            _rows.Rows.Clear(); var type = _type.SelectedIndex;
            for (var slot = 0; slot < SlotsPerType; slot++)
            {
                var hp = _bytes[type * 200 + slot * 2];
                var attack = _bytes[AttackBase + type * 200 + slot * 2];
                var defense = _bytes[DefenseBase + type * 200 + slot * 2];
                var exp = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(ExperienceBase + type * 400 + slot * 4, 4));
                _rows.Rows.Add(slot, hp, attack, defense, (long)exp);
            }
        }
        finally { _loading = false; }
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
        var max = e.ColumnIndex == 4 ? uint.MaxValue : byte.MaxValue;
        if (!ulong.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value > max)
        { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = $"0〜{max}の整数を入力してください。"; }
        else _grid.Rows[e.RowIndex].ErrorText = "";
    }

    private void CellChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _bytes is null || e.RowIndex < 0 || e.ColumnIndex <= 0 || _type.SelectedIndex < 0) return;
        try
        {
            var row = _rows.Rows[e.RowIndex]; var slot = Convert.ToInt32(row["LevelSlot"], CultureInfo.InvariantCulture); var type = _type.SelectedIndex;
            var value = Convert.ToUInt64(row[e.ColumnIndex], CultureInfo.InvariantCulture);
            switch (e.ColumnIndex)
            {
                case 1: _bytes[type * 200 + slot * 2] = checked((byte)value); break;
                case 2: _bytes[AttackBase + type * 200 + slot * 2] = checked((byte)value); break;
                case 3: _bytes[DefenseBase + type * 200 + slot * 2] = checked((byte)value); break;
                case 4: BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(ExperienceBase + type * 400 + slot * 4, 4), checked((uint)value)); break;
            }
            _status.Text = "未保存のローカル編集あり。選択タイプ/slotの値を旧配置offsetへ反映しました。ゲームメモリには書き込みません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "値エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveDump()
    {
        if (_bytes is null) { MessageBox.Show(this, "先にraw dumpを開いてください。"); return; }
        _grid.EndEdit();
        if (_saveToProject is not null)
        {
            try { var name = _saveToProject(_bytes.ToArray()); _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {name}。元assetは保持し、PCSX2には反映していません。"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "Raw binary (*.bin)|*.bin|すべてのファイル (*.*)|*.*", FileName = "monster-growth-edited.bin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dialog.FileName, _bytes); _status.Text = $"編集済みraw dumpを保存しました: {dialog.FileName}。PCSX2には反映していません。"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
