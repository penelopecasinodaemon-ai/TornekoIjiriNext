using System.Globalization;
using System.Text;

namespace TornekoIjiriNext;

/// <summary>
/// Compares two read-only PINE snapshots byte by byte.
/// This form never writes to game memory.
/// </summary>
internal sealed class PineSnapshotCompareForm : Form
{
    private sealed record ByteDifference(
        uint RegionAddress,
        int Offset,
        byte OldValue,
        byte NewValue)
    {
        public uint AbsoluteAddress => RegionAddress + (uint)Offset;
    }

    private readonly List<ByteDifference> _differences = new();

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false
    };

    private readonly Label _summary = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        Padding = new Padding(4, 8, 4, 8)
    };

    public PineSnapshotCompareForm(
        PineSessionSnapshot previous,
        PineSessionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.ConnectionGeneration != current.ConnectionGeneration ||
            !string.Equals(previous.GameId, current.GameId, StringComparison.Ordinal) ||
            !string.Equals(previous.GameCrc, current.GameCrc, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previous.GameVersion, current.GameVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "異なる接続またはゲームのスナップショットは比較できません。");
        }

        Text = "PINE再取得・バイト差分比較";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1050, 720);
        MinimumSize = new Size(780, 500);

        _list.Columns.Add("絶対アドレス", 130);
        _list.Columns.Add("領域アドレス", 130);
        _list.Columns.Add("領域内オフセット", 130);
        _list.Columns.Add("旧値", 90);
        _list.Columns.Add("新値", 90);

        BuildDifferences(previous, current);

        var changedRegions = _differences
            .Select(x => x.RegionAddress)
            .Distinct()
            .Count();

        _summary.Text =
            $"ゲームID: {current.GameId}    CRC: {current.GameCrc}    Version: {current.GameVersion}" +
            Environment.NewLine +
            $"比較元: {previous.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}    再取得: {current.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}" +
            Environment.NewLine +
            $"比較領域: {previous.Regions.Count}    変更領域: {changedRegions}    変更バイト数: {_differences.Count}" +
            (_differences.Count == 0
                ? Environment.NewLine + "差分はありません。"
                : string.Empty);

        foreach (var difference in _differences)
        {
            var item = new ListViewItem($"0x{difference.AbsoluteAddress:X8}");
            item.SubItems.Add($"0x{difference.RegionAddress:X8}");
            item.SubItems.Add($"0x{difference.Offset:X2} ({difference.Offset})");
            item.SubItems.Add($"0x{difference.OldValue:X2}");
            item.SubItems.Add($"0x{difference.NewValue:X2}");
            _list.Items.Add(item);
        }

        var save = new Button
        {
            Text = "差分をCSV保存…",
            AutoSize = true,
            Enabled = _differences.Count > 0
        };
        save.Click += (_, _) => SaveCsv();

        var close = new Button
        {
            Text = "閉じる",
            AutoSize = true
        };
        close.Click += (_, _) => Close();

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true
        };
        footer.Controls.Add(close);
        footer.Controls.Add(save);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(_summary, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
    }

    private void BuildDifferences(
        PineSessionSnapshot previous,
        PineSessionSnapshot current)
    {
        var oldRegions = previous.Regions.ToDictionary(x => x.Address);
        var newRegions = current.Regions.ToDictionary(x => x.Address);

        if (oldRegions.Count != newRegions.Count ||
            oldRegions.Keys.Except(newRegions.Keys).Any() ||
            newRegions.Keys.Except(oldRegions.Keys).Any())
        {
            throw new InvalidOperationException(
                "比較元と再取得データの領域構成が一致しません。");
        }

        foreach (var address in oldRegions.Keys.OrderBy(x => x))
        {
            var oldRegion = oldRegions[address];
            var newRegion = newRegions[address];

            if (oldRegion.Length != newRegion.Length)
            {
                throw new InvalidOperationException(
                    $"0x{address:X8} の領域サイズが一致しません。");
            }

            var oldBytes = oldRegion.CopyBytes();
            var newBytes = newRegion.CopyBytes();

            for (var offset = 0; offset < oldBytes.Length; offset++)
            {
                if (oldBytes[offset] != newBytes[offset])
                {
                    _differences.Add(new ByteDifference(
                        address,
                        offset,
                        oldBytes[offset],
                        newBytes[offset]));
                }
            }
        }
    }

    private void SaveCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "PINE差分をCSV保存",
            Filter = "CSVファイル (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"pine_diff_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var lines = new List<string>
            {
                "AbsoluteAddress,RegionAddress,Offset,OldValue,NewValue"
            };

            lines.AddRange(_differences.Select(x =>
                string.Join(",",
                    $"0x{x.AbsoluteAddress:X8}",
                    $"0x{x.RegionAddress:X8}",
                    x.Offset.ToString(CultureInfo.InvariantCulture),
                    $"0x{x.OldValue:X2}",
                    $"0x{x.NewValue:X2}")));

            File.WriteAllLines(
                dialog.FileName,
                lines,
                new UTF8Encoding(true));

            MessageBox.Show(this,
                $"差分 {_differences.Count} 件を保存しました。\n{dialog.FileName}",
                "CSV保存完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "CSV保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}