namespace TornekoIjiriNext;

internal sealed class ProjectCaptureCompareForm : Form
{
    private sealed record CaptureChoice(ProjectBundleEntry Entry, CaptureDescriptor Capture)
    {
        public override string ToString() => $"{Entry.FileName} | {Capture.GameId} CRC {Capture.GameCrc} | {Capture.CapturedAtUtc:u} | {Capture.Kind} ({Capture.Regions.Count} region)";
    }

    private readonly ProjectBundleModel _project;
    private readonly ComboBox _first = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _second = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10) };

    public ProjectCaptureCompareForm(ProjectBundleModel project)
    {
        _project = project;
        Text = "PINE capture project compare (read-only)";
        Width = 900; Height = 620; MinimumSize = new Size(720, 440); StartPosition = FormStartPosition.CenterParent;
        foreach (var entry in project.Entries.Where(entry => entry.Category.StartsWith("PINE capture ", StringComparison.Ordinal)))
        {
            try { var capture = ProjectCaptureModel.Read(Path.Combine(project.RootDirectory, "assets", entry.FileName)); _first.Items.Add(new CaptureChoice(entry, capture)); _second.Items.Add(new CaptureChoice(entry, capture)); }
            catch { /* Invalid/tampered captures are excluded; project integrity is separately reported. */ }
        }
        if (_first.Items.Count > 0) _first.SelectedIndex = 0;
        if (_second.Items.Count > 1) _second.SelectedIndex = 1; else if (_second.Items.Count > 0) _second.SelectedIndex = 0;
        var compare = new Button { Text = "メタデータ・差分を確認", AutoSize = true };
        compare.Click += (_, _) => Compare();
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.Controls.Add(_first, 0, 0); top.Controls.Add(new Label { Text = "→", AutoSize = true, Anchor = AnchorStyles.None }, 1, 0); top.Controls.Add(_second, 2, 0);
        top.Controls.Add(compare, 1, 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(top, 0, 0); layout.Controls.Add(_output, 0, 1); Controls.Add(layout);
        if (_first.Items.Count == 0) _output.Text = "検証できるPINE captureがありません。『PINE capture JSON追加…』から登録してください。";
        else Compare();
    }

    private void Compare()
    {
        if (_first.SelectedItem is not CaptureChoice first || _second.SelectedItem is not CaptureChoice second) return;
        try
        {
            var integrityIssues = _project.VerifyAssets();
            if (integrityIssues.Count != 0) throw new InvalidDataException("Project asset integrity check failed: " + string.Join(", ", integrityIssues));
            var left = first.Capture; var right = second.Capture;
            var comparison = ProjectCaptureModel.Compare(left, right);
            var report = new System.Text.StringBuilder();
            report.AppendLine($"元: {left.CapturedAtUtc:O} | {left.Pcsx2Version} | {left.GameTitle} | {left.GameId} CRC {left.GameCrc} v{left.GameVersion} | {left.Kind}");
            report.AppendLine($"先: {right.CapturedAtUtc:O} | {right.Pcsx2Version} | {right.GameTitle} | {right.GameId} CRC {right.GameCrc} v{right.GameVersion} | {right.Kind}");
            if (!comparison.GameIdentityMatches)
                report.AppendLine("注意: ゲーム識別情報が異なります。メモリ値の差を同一ゲームの状態変化とは扱えません。");
            if (!comparison.RegionLayoutMatches) report.AppendLine("範囲構成が異なります。メタデータのみ表示し、byte比較は行いません。");
            else if (first.Entry.FileName == second.Entry.FileName) report.AppendLine("同一assetのため差分はありません。");
            else
            {
                for (var regionIndex = 0; regionIndex < comparison.Regions.Count; regionIndex++)
                {
                    var region = comparison.Regions[regionIndex];
                    report.AppendLine($"範囲{regionIndex + 1}: 0x{region.Address:X8}, {region.Length} bytes / 相違byte: {region.ChangedBytes:N0}");
                    foreach (var change in region.Changes) report.AppendLine($"+0x{change.Offset:X}: {change.Before:X2} → {change.After:X2}");
                    if (region.IsTruncated) report.AppendLine($"byte表示は先頭{region.Changes.Count:N0}件までです。");
                }
            }
            report.AppendLine("キャプチャはPINE read-only測定値です。構造・意味の断定やゲームメモリ書き込みは行いません。");
            _output.Text = report.ToString();
        }
        catch (Exception ex) { _output.Text = $"比較を中止: {ex.Message}"; }
    }
}
