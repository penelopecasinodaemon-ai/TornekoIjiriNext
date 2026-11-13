namespace TornekoIjiriNext;

internal sealed class ProjectCaptureCompareForm : Form
{
    private sealed record CaptureChoice(ProjectBundleEntry Entry, CaptureDescriptor Capture)
    {
        public override string ToString() =>
            $"{Entry.FileName} | {Capture.GameId} CRC {Capture.GameCrc} | {Capture.CapturedAtUtc:u} | {Capture.Kind} ({Capture.Regions.Count} region)";
    }

    private readonly ProjectBundleModel _project;
    private readonly ComboBox _first = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _second = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _viewMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly TextBox _filter = new() { Width = 240 };
    private readonly TextBox _output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 10)
    };

    public ProjectCaptureCompareForm(ProjectBundleModel project)
    {
        _project = project;

        Text = "PINE capture project compare (read-only)";
        Width = 1000;
        Height = 700;
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var entry in project.Entries.Where(entry =>
            entry.Category.StartsWith("PINE capture ", StringComparison.Ordinal)))
        {
            try
            {
                var capture = ProjectCaptureModel.Read(
                    Path.Combine(project.RootDirectory, "assets", entry.FileName));

                _first.Items.Add(new CaptureChoice(entry, capture));
                _second.Items.Add(new CaptureChoice(entry, capture));
            }
            catch
            {
                // Invalid/tampered captures are excluded; project integrity is separately reported.
            }
        }

        if (_first.Items.Count > 0) _first.SelectedIndex = 0;
        if (_second.Items.Count > 1) _second.SelectedIndex = 1;
        else if (_second.Items.Count > 0) _second.SelectedIndex = 0;

        _viewMode.Items.AddRange(new object[]
        {
                        "BYTE + WORD + DWORD",
            "BYTE + DWORD",
            "BYTEのみ",
            "WORDのみ",
            "DWORDのみ"
        });
        _viewMode.SelectedIndex = 0;

        var compare = new Button
        {
            Text = "メタデータ・差分を確認",
            AutoSize = true
        };
        compare.Click += (_, _) => Compare();

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2
        };

        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        top.Controls.Add(_first, 0, 0);
        top.Controls.Add(new Label
        {
            Text = "→",
            AutoSize = true,
            Anchor = AnchorStyles.None
        }, 1, 0);
        top.Controls.Add(_second, 2, 0);
        top.Controls.Add(compare, 1, 1);

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 6)
        };

        filterPanel.Controls.Add(new Label
        {
            Text = "表示:",
            AutoSize = true,
            Margin = new Padding(3, 7, 3, 3)
        });
        filterPanel.Controls.Add(_viewMode);

        filterPanel.Controls.Add(new Label
        {
            Text = "差分検索:",
            AutoSize = true,
            Margin = new Padding(12, 7, 3, 3)
        });
        filterPanel.Controls.Add(_filter);

        var applyFilter = new Button
        {
            Text = "表示更新",
            AutoSize = true
        };
        applyFilter.Click += (_, _) => Compare();
        filterPanel.Controls.Add(applyFilter);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(filterPanel, 0, 1);
        layout.Controls.Add(_output, 0, 2);

        Controls.Add(layout);

        if (_first.Items.Count == 0)
        {
            _output.Text =
                "検証できるPINE captureがありません。『PINE capture JSON追加…』から登録してください。";
        }
        else
        {
            Compare();
        }
    }

    private static bool MatchesFilter(string line, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || line.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void Compare()
    {
        if (_first.SelectedItem is not CaptureChoice first
            || _second.SelectedItem is not CaptureChoice second)
            return;

        try
        {
            var integrityIssues = _project.VerifyAssets();

            if (integrityIssues.Count != 0)
            {
                throw new InvalidDataException(
                    "Project asset integrity check failed: "
                    + string.Join(", ", integrityIssues));
            }

            var left = first.Capture;
            var right = second.Capture;

            var comparison = ProjectCaptureModel.Compare(left, right);
            var report = new System.Text.StringBuilder();

            var filter = _filter.Text.Trim();
            var viewMode = _viewMode.SelectedItem?.ToString() ?? "BYTE + WORD + DWORD";

            var showBytes = viewMode != "WORDのみ" && viewMode != "DWORDのみ";
            var showWords = viewMode != "BYTEのみ" && viewMode != "DWORDのみ";
            var showDwords = viewMode != "BYTEのみ" && viewMode != "WORDのみ";

            report.AppendLine($"元: {left.CapturedAtUtc:O} | {left.Pcsx2Version} | {left.GameTitle} | {left.GameId} CRC {left.GameCrc} v{left.GameVersion} | {left.Kind}");
            report.AppendLine($"先: {right.CapturedAtUtc:O} | {right.Pcsx2Version} | {right.GameTitle} | {right.GameId} CRC {right.GameCrc} v{right.GameVersion} | {right.Kind}");
            report.AppendLine($"表示モード: {viewMode}");

            if (!string.IsNullOrWhiteSpace(filter))
                report.AppendLine($"検索文字列: {filter}");

            if (!comparison.GameIdentityMatches)
            {
                report.AppendLine();
                report.AppendLine("【警告】ゲーム識別情報が異なります。");
                report.AppendLine("メモリ値の差を同一ゲームの状態変化とは扱えません。");
            }

            if (!comparison.RegionLayoutMatches)
            {
                report.AppendLine();
                report.AppendLine("範囲構成が異なります。メタデータのみ表示し、byte比較は行いません。");
            }
            else if (first.Entry.FileName == second.Entry.FileName)
            {
                report.AppendLine();
                report.AppendLine("同一assetのため差分はありません。");
            }
            else
            {
                var matchedLines = 0;

                for (var regionIndex = 0;
                    regionIndex < comparison.Regions.Count;
                    regionIndex++)
                {
                    var region = comparison.Regions[regionIndex];

                    var changeRate = region.Length > 0
                        ? region.ChangedBytes * 100.0 / region.Length
                        : 0.0;

                    report.AppendLine();
                    report.AppendLine(
                        $"範囲{regionIndex + 1}: 0x{region.Address:X8}, "
                        + $"{region.Length:N0} bytes");

                    report.AppendLine(
                        $"BYTE相違: {region.ChangedBytes:N0} "
                        + $"({changeRate:F2}%)");

                    if (showBytes)
                    {
                        foreach (var change in region.Changes)
                        {
                            var line =
                                $"BYTE +0x{change.Offset:X}: "
                                + $"{change.Before:X2} → {change.After:X2}";

                            if (MatchesFilter(line, filter))
                            {
                                report.AppendLine(line);
                                matchedLines++;
                            }
                        }

                        if (region.IsTruncated)
                        {
                            report.AppendLine(
                                $"BYTE表示は先頭{region.Changes.Count:N0}件までです。");
                        }
                    }

                    report.AppendLine(
                        $"相違DWORD (little-endian): {region.ChangedDwords:N0}");

                    if (showDwords)
                    {
                        foreach (var change in region.DwordChanges)
                        {
                            var line =
                                $"DWORD +0x{change.Offset:X}: "
                                + $"{change.Before:X8} → {change.After:X8}";

                            if (MatchesFilter(line, filter))
                            {
                                report.AppendLine(line);
                                matchedLines++;
                            }
                        }

                        if (region.DwordChangesTruncated)
                        {
                            report.AppendLine(
                                $"DWORD表示は先頭{region.DwordChanges.Count:N0}件までです。");
                        }
                    }

                    report.AppendLine(
                        $"相違WORD (16-bit little-endian): {region.ChangedWords:N0}");

                    if (showWords)
                    {
                        foreach (var change in region.WordChanges)
                        {
                            var line =
                                $"WORD +0x{change.Offset:X}: "
                                + $"{change.Before:X4} → {change.After:X4}";

                            if (MatchesFilter(line, filter))
                            {
                                report.AppendLine(line);
                                matchedLines++;
                            }
                        }

                        if (region.WordChangesTruncated)
                        {
                            report.AppendLine(
                                $"WORD表示は先頭{region.WordChanges.Count:N0}件までです。");
                        }
                    }

                    if (region.TrailingBytes > 0)
                    {
                        report.AppendLine(
                            $"末尾{region.TrailingBytes} byteはDWORD集計対象外です"
                            + "（BYTE比較には含まれます）。");
                    }
                }

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    report.AppendLine();
                    report.AppendLine($"検索一致した差分行: {matchedLines:N0}件");

                    if (matchedLines == 0)
                        report.AppendLine("一致する差分はありません。");
                }
            }

            report.AppendLine();
            report.AppendLine(
                "注意: 変更率はBYTE単位の変化割合です。DWORDの個数割合ではありません。");

            report.AppendLine(
                "キャプチャはPINE read-only測定値です。構造・意味の断定やゲームメモリ書き込みは行いません。");

            _output.Text = report.ToString();
        }
        catch (Exception ex)
        {
            _output.Text = $"比較を中止: {ex.Message}";
        }
    }
}
