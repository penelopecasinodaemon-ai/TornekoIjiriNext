namespace TornekoIjiriNext;

internal sealed class ProjectBundleHistoryForm : Form
{
    private sealed record AssetChoice(ProjectBundleEntry Entry)
    {
        public override string ToString() => $"{Entry.FileName}  |  {Entry.Length:N0} B  |  {Entry.AddedUtc:u}";
    }

    private readonly ProjectBundleModel _project;
    private readonly ComboBox _before = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _after = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ListBox _changes = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label _summary = new() { AutoSize = true, Dock = DockStyle.Fill };

    public ProjectBundleHistoryForm(ProjectBundleModel project, string selectedFileName)
    {
        _project = project;
        Text = "project asset履歴・byte差分";
        Width = 900; Height = 620; MinimumSize = new Size(700, 420); StartPosition = FormStartPosition.CenterParent;
        var selected = project.Entries.FirstOrDefault(entry => string.Equals(entry.FileName, selectedFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Project asset not found: {selectedFileName}");
        var history = project.GetAssetHistory(selectedFileName);
        foreach (var entry in history)
        {
            var choice = new AssetChoice(entry);
            _before.Items.Add(choice);
            _after.Items.Add(choice);
        }
        var selectedIndex = history.ToList().FindIndex(entry => string.Equals(entry.FileName, selected.FileName, StringComparison.OrdinalIgnoreCase));
        var afterIndex = selectedIndex == 0 && history.Count > 1 ? history.Count - 1 : Math.Max(0, selectedIndex);
        _before.SelectedIndex = selectedIndex > 0 ? selectedIndex - 1 : 0;
        _after.SelectedIndex = afterIndex;

        var compare = new Button { Text = "差分を表示", AutoSize = true };
        compare.Click += (_, _) => CompareSelected();
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize)); top.RowStyles.Add(new RowStyle(SizeType.AutoSize)); top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.Controls.Add(new Label { Text = "変更前", AutoSize = true }, 0, 0);
        top.Controls.Add(new Label { Text = "変更後", AutoSize = true }, 2, 0);
        top.Controls.Add(_before, 0, 1); top.Controls.Add(new Label { Text = "→", AutoSize = true, Anchor = AnchorStyles.None }, 1, 1); top.Controls.Add(_after, 2, 1);
        top.Controls.Add(compare, 1, 0);
        top.Controls.Add(_summary, 0, 2); top.SetColumnSpan(_summary, 3);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(top, 0, 0); layout.Controls.Add(_changes, 0, 1);
        Controls.Add(layout);
        CompareSelected();
    }

    private void CompareSelected()
    {
        if (_before.SelectedItem is not AssetChoice before || _after.SelectedItem is not AssetChoice after) return;
        try
        {
            var diff = _project.CompareAssets(before.Entry.FileName, after.Entry.FileName);
            _changes.Items.Clear();
            foreach (var change in diff.Changes)
                _changes.Items.Add($"0x{change.Offset:X8}: {(change.Before.HasValue ? change.Before.Value.ToString("X2") : "--")} → {(change.After.HasValue ? change.After.Value.ToString("X2") : "--")}");
            var truncated = diff.IsTruncated ? $" 表示は先頭{diff.Changes.Count:N0}件まで。" : "";
            _summary.Text = $"{before.Entry.FileName} ({diff.BeforeLength:N0} bytes) → {after.Entry.FileName} ({diff.AfterLength:N0} bytes): {diff.TotalChangedBytes:N0} changed bytes。SHA-256を検証して比較しました。{truncated} ゲームメモリ/PINEは未使用。";
        }
        catch (Exception ex)
        {
            _changes.Items.Clear();
            _summary.Text = $"比較中止: {ex.Message}";
        }
    }
}
