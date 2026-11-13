using System.Data;
using System.Globalization;

namespace TornekoIjiriNext;

/// <summary>Offline editor/round-tripper for legacy ordered tr3_ijiri.ini settings.</summary>
internal sealed class LegacyIniForm : Form
{
    private readonly DataTable _table = new("LegacyIni");
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(980, 0) };
    private LegacyIniModel? _model;
    private string _source = "";
    private readonly Func<byte[], string>? _saveToProject;
    private bool _loading;

    public LegacyIniForm(string? initialPath = null, Func<byte[], string>? saveToProject = null)
    {
        _saveToProject = saveToProject;
        Text = "旧版tr3_ijiri.ini（offline / no game writes）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        MinimumSize = new Size(780, 500);
        _table.Columns.Add("Index", typeof(int)); _table.Columns.Add("Variable", typeof(string));
        _table.Columns.Add("Description", typeof(string)); _table.Columns.Add("Value", typeof(string));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        var open = new Button { Text = "旧INI (22/25 lines)を開く…", AutoSize = true }; open.Click += (_, _) => OpenSettings(); bar.Controls.Add(open);
        var save = new Button { Text = "旧形式INIとして保存…", AutoSize = true }; save.Click += (_, _) => SaveSettings(); bar.Controls.Add(save);
        root.Controls.Add(bar, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.DataSource = _table; _grid.CellValidating += ValidateCell; _grid.CellValueChanged += ValueChanged;
        _grid.DataBindingComplete += (_, _) => ConfigureColumns(); root.Controls.Add(_grid, 0, 1);
        _status.ForeColor = Color.DarkSlateBlue;
        _status.Text = "旧HSP label_157..159の22/25 positional linesを扱います。名称が確定しない設定はvariable番号とsource roleを表示。ゲームへの変更適用はありません。";
        root.Controls.Add(_status, 0, 2); Controls.Add(root);
        if (!string.IsNullOrWhiteSpace(initialPath)) OpenSettings(initialPath);
    }

    private void ConfigureColumns()
    {
        foreach (DataGridViewColumn c in _grid.Columns)
        {
            c.HeaderText = c.Name switch { "Index" => "旧INI行 (0-based)", "Variable" => "HSP variable", "Description" => "旧版UI/役割 (source hypothesis)", "Value" => "保存値", _ => c.Name };
            c.ReadOnly = c.Name != "Value";
        }
        foreach (var name in new[] { "Index", "Variable", "Description", "Value" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].FillWeight = name switch { "Index" => 60, "Variable" => 85, "Description" => 220, _ => 120 };
    }

    private void OpenSettings(string? selectedPath = null)
    {
        if (selectedPath is null)
        {
            using var dialog = new OpenFileDialog { Filter = "Legacy ini (*.ini)|*.ini|すべてのファイル (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            selectedPath = dialog.FileName;
        }
        try { _model = LegacyIniModel.Parse(File.ReadAllBytes(selectedPath)); _source = selectedPath; DisplaySettings(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "INI読込失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DisplaySettings()
    {
        if (_model is null) return;
        _loading = true;
        try
        {
            _table.Rows.Clear();
            for (var i = 0; i < _model.Count; i++)
            {
                var field = LegacyIniModel.Fields[i];
                _table.Rows.Add(i, field.Variable, field.Description, _model.Values[i]);
            }
        }
        finally { _loading = false; }
        _status.Text = $"{Path.GetFileName(_source)} を読込: {_model.Count} lines。Shift-JIS/CP932で出力し、旧版の行数を維持します。数値範囲・行構成以外の意味は未検証。";
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Value" || _model is null) return;
        try
        {
            var value = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? "";
            var field = LegacyIniModel.Fields[e.RowIndex];
            if (field.IsText)
            {
                if (value.Contains('\n') || value.Contains('\r') || System.Text.Encoding.GetEncoding(932).GetByteCount(value) > field.Maximum)
                    throw new ArgumentException("文字列は改行なし、Shift-JISで512 bytes以内にしてください。");
            }
            else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0 || number > field.Maximum)
                throw new ArgumentException($"値は0〜{field.Maximum}の整数にしてください。");
            _grid.Rows[e.RowIndex].ErrorText = "";
        }
        catch (Exception ex) { e.Cancel = true; _grid.Rows[e.RowIndex].ErrorText = ex.Message; }
    }

    private void ValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _model is null || e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Value") return;
        try
        {
            _model.SetValue(e.RowIndex, Convert.ToString(_table.Rows[e.RowIndex]["Value"], CultureInfo.InvariantCulture) ?? "");
            _status.ForeColor = Color.DarkOrange; _status.Text = "未保存のローカルINI編集あり。旧ini fileだけを書き出します。ゲームmemoryは変更しません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定値エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveSettings()
    {
        if (_model is null) { MessageBox.Show(this, "先に旧INIを開いてください。"); return; }
        _grid.EndEdit();
        if (_saveToProject is not null)
        {
            try
            {
                var savedName = _saveToProject(_model.Serialize());
                _status.ForeColor = Color.SeaGreen; _status.Text = $"編集内容をproject内の新しいassetとして保存しました: {savedName}。元ファイルは保持しています。PCSX2には適用していません。";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "projectへの保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        using var dialog = new SaveFileDialog { Filter = "Legacy ini (*.ini)|*.ini|すべてのファイル (*.*)|*.*", DefaultExt = "ini", AddExtension = true, FileName = Path.GetFileName(_source) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, _model.Serialize());
            _status.ForeColor = Color.SeaGreen; _status.Text = $"旧版互換の{_model.Count}-line iniを保存しました: {dialog.FileName}。PCSX2には適用していません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "INI保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
