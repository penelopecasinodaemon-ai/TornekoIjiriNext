using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

internal sealed class PineCandidateHistoryForm : Form
{
    private sealed record HistoryItem(
        DateTime CapturedAtUtc,
        string FileName,
        string CandidateName,
        uint Address,
        int Length,
        string BytesHex,
        string ContextBeforeHex,
        string ContextAfterHex,
        uint? StatusBefore,
        uint? StatusAfter,
        string SceneNote);

    private readonly ProjectBundleModel _project;
    private readonly List<HistoryItem> _allItems = new();

    private readonly ComboBox _candidateSelector = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 320
    };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false
    };

    private readonly RichTextBox _detail = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = true,
        Height = 110
    };

    private readonly Label _summary = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true
    };

    public PineCandidateHistoryForm(ProjectBundleModel project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        Text = $"PINE候補アドレス調査履歴 — {_project.Name}";
        Width = 1250;
        Height = 800;
        MinimumSize = new Size(850, 560);
        StartPosition = FormStartPosition.CenterParent;

        ConfigureGrid();

        var load = new Button
        {
            Text = "履歴を再読み込み",
            AutoSize = true
        };
        load.Click += (_, _) => LoadHistory();

        var export = new Button
        {
            Text = "表示中の履歴をCSV出力",
            AutoSize = true
        };
        export.Click += (_, _) => ExportCsv();

        var offsetAnalysis = new Button
        {
            Text = "BYTE位置分析…",
            AutoSize = true
        };
        offsetAnalysis.Click += (_, _) => ShowOffsetAnalysis();

        var multiByteAnalysis = new Button
        {
            Text = "WORD・DWORD分析…",
            AutoSize = true
        };
        multiByteAnalysis.Click += (_, _) => ShowMultiByteAnalysis();

        var crossAnalysis = new Button
        {
            Text = "BYTE・WORD・DWORD横断比較…",
            AutoSize = true
        };
        crossAnalysis.Click += (_, _) => ShowCrossAnalysis();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        toolbar.Controls.Add(new Label
        {
            Text = "候補アドレス:",
            AutoSize = true,
            Padding = new Padding(0, 7, 0, 0)
        });
        toolbar.Controls.Add(_candidateSelector);
        toolbar.Controls.Add(load);
        toolbar.Controls.Add(export);
        toolbar.Controls.Add(offsetAnalysis);
        toolbar.Controls.Add(multiByteAnalysis);
        toolbar.Controls.Add(crossAnalysis);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 480,
            Panel1MinSize = 180,
            Panel2MinSize = 100
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(_summary, 0, 2);

        Controls.Add(layout);

        _candidateSelector.SelectedIndexChanged += (_, _) => RefreshRows();
        _grid.SelectionChanged += (_, _) => ShowSelectedDetail();

        LoadHistory();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add("Time", "取得日時 (UTC)");
        _grid.Columns.Add("Address", "アドレス");
        _grid.Columns.Add("Length", "長さ");
        _grid.Columns.Add("Changed", "前回からの変更BYTE");
        _grid.Columns.Add("Different", "前回との差");
        _grid.Columns.Add("Status", "状態");
        _grid.Columns.Add("File", "元キャプチャ");
    }

    private void LoadHistory()
    {
        try
        {
            _allItems.Clear();

            var entries = _project.Entries
                .Where(entry => string.Equals(
                    entry.Category,
                    "PINE候補キャプチャ",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.AddedUtc)
                .ToList();

            var errors = new List<string>();

            foreach (var entry in entries)
            {
                try
                {
                    var path = Path.Combine(
                        _project.RootDirectory,
                        "assets",
                        entry.FileName);

                    if (!File.Exists(path))
                        throw new InvalidDataException("ファイルがありません。");

                    var info = new FileInfo(path);
                    if (info.Length != entry.Length ||
                        info.Length > 64L * 1024 * 1024)
                    {
                        throw new InvalidDataException("サイズ不一致。");
                    }

                    var bytes = File.ReadAllBytes(path);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes));

                    if (!string.Equals(
                        hash,
                        entry.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("SHA-256不一致。");
                    }

                    ParseCapture(bytes, entry.FileName);
                }
                catch (Exception ex)
                {
                    errors.Add($"{entry.FileName}: {ex.Message}");
                }
            }

            var candidates = _allItems
                .Select(x => new
                {
                    x.CandidateName,
                    x.Address,
                    x.Length
                })
                .Distinct()
                .OrderBy(x => x.CandidateName)
                .ThenBy(x => x.Address)
                .ToList();

            _candidateSelector.Items.Clear();

            foreach (var candidate in candidates)
            {
                _candidateSelector.Items.Add(
                    new CandidateChoice(
                        candidate.CandidateName,
                        candidate.Address,
                        candidate.Length));
            }

            if (_candidateSelector.Items.Count > 0)
                _candidateSelector.SelectedIndex = 0;
            else
                RefreshRows();

            _summary.Text =
                $"キャプチャ内候補: {_allItems.Count:N0}件 / " +
                $"候補種類: {candidates.Count:N0}件 / " +
                $"読み込みエラー: {errors.Count:N0}件";

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, errors),
                    "一部キャプチャの読み込みエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "履歴読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private sealed record CandidateChoice(
        string Name,
        uint Address,
        int Length)
    {
        public override string ToString() =>
            $"{Name}  |  0x{Address:X8}  |  {Length:N0} bytes";
    }

    private void ParseCapture(byte[] bytes, string fileName)
    {
        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions { MaxDepth = 48 });

        var root = document.RootElement;

        if (GetInt(root, "FormatVersion") != 1)
            throw new InvalidDataException("未対応の形式バージョンです。");

        var gameId = GetString(root, "GameId");
        var crc = GetString(root, "GameCrc");
        var gameVersion = GetString(root, "GameVersion");

        if (string.IsNullOrWhiteSpace(gameId) ||
            string.IsNullOrWhiteSpace(crc) ||
            string.IsNullOrWhiteSpace(gameVersion))
        {
            throw new InvalidDataException("ゲーム識別情報がありません。");
        }

        var capturedAt = GetDateTime(root, "CapturedAtUtc");
        var statusBefore = GetNullableUInt(root, "StatusBefore");
        var statusAfter = GetNullableUInt(root, "StatusAfter");
        var note = GetString(root, "SceneNote");

        if (!root.TryGetProperty("Regions", out var regions) ||
            regions.ValueKind != JsonValueKind.Array ||
            regions.GetArrayLength() < 1 ||
            regions.GetArrayLength() > 64)
        {
            throw new InvalidDataException("Regionsの形式が不正です。");
        }

        foreach (var region in regions.EnumerateArray())
        {
            var name = GetString(region, "Name");
            var address = GetUInt(region, "Address");
            var length = GetInt(region, "Length");
            var hex = GetString(region, "BytesHex");
            var before = GetString(region, "ContextBeforeHex");
            var after = GetString(region, "ContextAfterHex");

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("候補名がありません。");

            if (length < 1 || length > 4096)
                throw new InvalidDataException("候補長が範囲外です。");

            if ((ulong)address + (uint)length > 0x100000000UL)
                throw new InvalidDataException("アドレス範囲が不正です。");

            ValidateHex(hex, length);
            ValidateOptionalHex(before, 256);
            ValidateOptionalHex(after, 256);

            _allItems.Add(new HistoryItem(
                capturedAt,
                fileName,
                name,
                address,
                length,
                NormalizeHex(hex),
                NormalizeHex(before),
                NormalizeHex(after),
                statusBefore,
                statusAfter,
                note));
        }
    }

    private void RefreshRows()
    {
        _grid.Rows.Clear();
        _detail.Clear();

        if (_candidateSelector.SelectedItem is not CandidateChoice selected)
            return;

        var items = _allItems
            .Where(x =>
                x.CandidateName == selected.Name &&
                x.Address == selected.Address &&
                x.Length == selected.Length)
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();

        string? previousHex = null;

        foreach (var item in items)
        {
            var changed = previousHex is null
                ? 0
                : CountChangedBytes(previousHex, item.BytesHex);

            var different = previousHex is null
                ? "初回"
                : changed == 0 ? "変化なし" : "変化あり";

            var status =
                $"{FormatStatus(item.StatusBefore)} → {FormatStatus(item.StatusAfter)}";

            var row = _grid.Rows.Add(
                item.CapturedAtUtc.ToString("u"),
                $"0x{item.Address:X8}",
                item.Length.ToString("N0"),
                previousHex is null ? "—" : changed.ToString("N0"),
                different,
                status,
                item.FileName);

            _grid.Rows[row].Tag = item;
            previousHex = item.BytesHex;
        }

        _summary.Text =
            $"{selected.Name} / 0x{selected.Address:X8} / " +
            $"{items.Count}回取得 / " +
            $"{(items.Count > 1 ? "時系列比較可能" : "比較データ不足")}";

        if (_grid.Rows.Count > 0)
            _grid.Rows[0].Selected = true;

        ShowSelectedDetail();
    }

    private void ShowSelectedDetail()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not HistoryItem item)
            return;

        _detail.Text =
            $"取得日時: {item.CapturedAtUtc:u}\r\n" +
            $"候補: {item.CandidateName}\r\n" +
            $"アドレス: 0x{item.Address:X8} / {item.Length} bytes\r\n" +
            $"状態: {FormatStatus(item.StatusBefore)} → {FormatStatus(item.StatusAfter)}\r\n" +
            $"場面メモ: {item.SceneNote}\r\n\r\n" +
            $"データ HEX:\r\n{FormatHex(item.BytesHex)}\r\n\r\n" +
            $"前方周辺データ: {FormatHex(item.ContextBeforeHex)}\r\n" +
            $"後方周辺データ: {FormatHex(item.ContextAfterHex)}";
    }

    private void ShowCrossAnalysis()
    {
        if (_candidateSelector.SelectedItem is not CandidateChoice selected)
        {
            MessageBox.Show(this, "候補アドレスを選択してください。");
            return;
        }

        var items = _allItems
            .Where(x =>
                x.CandidateName == selected.Name &&
                x.Address == selected.Address &&
                x.Length == selected.Length)
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();

        if (items.Count < 2)
        {
            MessageBox.Show(this, "横断比較には2回以上のキャプチャが必要です。");
            return;
        }

        using var form = new PineCrossAnalysisForm(
            selected.Name,
            selected.Address,
            items);
        form.ShowDialog(this);
    }

    private void ShowMultiByteAnalysis()
    {
        if (_candidateSelector.SelectedItem is not CandidateChoice selected)
        {
            MessageBox.Show(this, "候補アドレスを選択してください。");
            return;
        }

        var items = _allItems
            .Where(x =>
                x.CandidateName == selected.Name &&
                x.Address == selected.Address &&
                x.Length == selected.Length)
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();

        if (items.Count < 2)
        {
            MessageBox.Show(this, "WORD・DWORD分析には2回以上のキャプチャが必要です。");
            return;
        }

        using var form = new PineMultiByteAnalysisForm(
            _project,
            selected.Name,
            selected.Address,
            items);
        form.ShowDialog(this);
    }

    private void ShowOffsetAnalysis()
    {
        if (_candidateSelector.SelectedItem is not CandidateChoice selected)
        {
            MessageBox.Show(this, "候補アドレスを選択してください。");
            return;
        }

        var items = _allItems
            .Where(x =>
                x.CandidateName == selected.Name &&
                x.Address == selected.Address &&
                x.Length == selected.Length)
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();

        if (items.Count < 2)
        {
            MessageBox.Show(this, "BYTE位置分析には2回以上のキャプチャが必要です。");
            return;
        }

        using var form = new PineByteOffsetAnalysisForm(
            selected.Name,
            selected.Address,
            items);
        form.ShowDialog(this);
    }

    private void ExportCsv()
    {
        if (_candidateSelector.SelectedItem is not CandidateChoice selected)
            return;

        var items = _allItems
            .Where(x =>
                x.CandidateName == selected.Name &&
                x.Address == selected.Address &&
                x.Length == selected.Length)
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();

        if (items.Count == 0)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "候補アドレス履歴をCSV出力",
            Filter = "CSV (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = "csv",
            FileName = $"PINE_History_{selected.Address:X8}.csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var builder = new StringBuilder();
        builder.AppendLine(
            "CapturedAtUtc,CandidateName,Address,Length,ChangedBytes,StatusBefore,StatusAfter,SceneNote,BytesHex,ContextBeforeHex,ContextAfterHex,SourceFile");

        string? previous = null;

        foreach (var item in items)
        {
            var changed = previous is null
                ? ""
                : CountChangedBytes(previous, item.BytesHex).ToString(CultureInfo.InvariantCulture);

            builder.AppendLine(string.Join(",",
                Csv(item.CapturedAtUtc.ToString("O")),
                Csv(item.CandidateName),
                Csv($"0x{item.Address:X8}"),
                item.Length.ToString(CultureInfo.InvariantCulture),
                changed,
                item.StatusBefore?.ToString(CultureInfo.InvariantCulture) ?? "",
                item.StatusAfter?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(item.SceneNote),
                Csv(item.BytesHex),
                Csv(item.ContextBeforeHex),
                Csv(item.ContextAfterHex),
                Csv(item.FileName)));

            previous = item.BytesHex;
        }

        File.WriteAllText(
            dialog.FileName,
            builder.ToString(),
            new UTF8Encoding(true));

        MessageBox.Show(
            this,
            $"CSVを出力しました。\n{dialog.FileName}",
            "CSV出力完了",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static int CountChangedBytes(string before, string after)
    {
        var a = ParseHex(before);
        var b = ParseHex(after);

        var count = Math.Abs(a.Length - b.Length);
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
                count++;
        }
        return count;
    }

    private static byte[] ParseHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<byte>();

        return Convert.FromHexString(NormalizeHex(value));
    }

    private static string NormalizeHex(string value)
    {
        return new string((value ?? "")
            .Where(Uri.IsHexDigit)
            .ToArray()).ToUpperInvariant();
    }

    private static void ValidateHex(string value, int expectedBytes)
    {
        var normalized = NormalizeHex(value);

        if (normalized.Length != expectedBytes * 2)
            throw new InvalidDataException("BytesHexの長さが一致しません。");

        _ = Convert.FromHexString(normalized);
    }

    private static void ValidateOptionalHex(string value, int maxBytes)
    {
        var normalized = NormalizeHex(value);

        if (normalized.Length > maxBytes * 2 ||
            normalized.Length % 2 != 0)
            throw new InvalidDataException("周辺HEXデータの長さが不正です。");

        if (normalized.Length > 0)
            _ = Convert.FromHexString(normalized);
    }

    private static string FormatHex(string hex)
    {
        var bytes = ParseHex(hex);
        if (bytes.Length == 0)
            return "（なし）";

        var lines = new List<string>();
        for (var i = 0; i < bytes.Length; i += 16)
        {
            lines.Add(
                $"{i:X4}: " +
                string.Join(" ", bytes.Skip(i).Take(16)
                    .Select(x => x.ToString("X2"))));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatStatus(uint? status)
    {
        return status switch
        {
            0 => "実行中",
            1 => "一時停止",
            2 => "終了",
            null => "不明",
            _ => $"不明({status})"
        };
    }

    private static string Csv(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static uint GetUInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            throw new InvalidDataException($"{name}がありません。");

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetUInt32(out var numeric))
            return numeric;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];

            if (uint.TryParse(
                text,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var hex))
                return hex;
        }

        throw new InvalidDataException($"{name}の値が不正です。");
    }

    private static uint? GetNullableUInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        return value.TryGetUInt32(out var result)
            ? result
            : null;
    }

    private static DateTime GetDateTime(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result))
        {
            throw new InvalidDataException($"{name}の日時が不正です。");
        }

        return result.Kind == DateTimeKind.Utc
            ? result
            : result.ToUniversalTime();
    }

    private sealed class PineByteOffsetAnalysisForm : Form
    {
        private sealed record OffsetResult(
            int Offset,
            uint Address,
            int CaptureCount,
            int ChangedCount,
            int DistinctCount,
            byte FirstValue,
            byte LastValue,
            byte MostFrequentValue,
            int MostFrequentCount);

        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        private readonly List<OffsetResult> _results = new();

        public PineByteOffsetAnalysisForm(
            string candidateName,
            uint baseAddress,
            IReadOnlyList<HistoryItem> items)
        {
            Text = $"BYTE位置分析 — {candidateName} / 0x{baseAddress:X8}";
            Width = 1050;
            Height = 700;
            MinimumSize = new Size(760, 450);
            StartPosition = FormStartPosition.CenterParent;

            _grid.Columns.Add("Offset", "相対位置");
            _grid.Columns.Add("Address", "実アドレス");
            _grid.Columns.Add("Captures", "取得回数");
            _grid.Columns.Add("Changed", "変化回数");
            _grid.Columns.Add("Distinct", "異なる値");
            _grid.Columns.Add("First", "初回値");
            _grid.Columns.Add("Last", "最新値");
            _grid.Columns.Add("Frequent", "最多観測値");
            _grid.Columns.Add("Frequency", "最多観測回数");

            BuildResults(baseAddress, items);

            foreach (var result in _results)
            {
                _grid.Rows.Add(
                    $"0x{result.Offset:X4} ({result.Offset})",
                    $"0x{result.Address:X8}",
                    result.CaptureCount,
                    result.ChangedCount,
                    result.DistinctCount,
                    $"0x{result.FirstValue:X2}",
                    $"0x{result.LastValue:X2}",
                    $"0x{result.MostFrequentValue:X2}",
                    result.MostFrequentCount);
            }

            var summary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text =
                    $"分析対象: {candidateName} / 0x{baseAddress:X8} / " +
                    $"{items.Count} captures / {items[0].Length} bytes"
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_grid, 0, 0);
            layout.Controls.Add(summary, 0, 1);
            Controls.Add(layout);
        }

        private void BuildResults(
            uint baseAddress,
            IReadOnlyList<HistoryItem> items)
        {
            var parsed = items
                .Select(x => ParseHex(x.BytesHex))
                .ToList();

            if (parsed.Count < 2)
                throw new InvalidDataException("比較データが不足しています。");

            var length = parsed[0].Length;

            if (length == 0 || parsed.Any(x => x.Length != length))
                throw new InvalidDataException("キャプチャ間でBYTE長が一致しません。");

            for (var offset = 0; offset < length; offset++)
            {
                var counts = new Dictionary<byte, int>();
                var changed = 0;

                for (var capture = 0; capture < parsed.Count; capture++)
                {
                    var value = parsed[capture][offset];

                    if (!counts.TryAdd(value, 1))
                        counts[value]++;

                    if (capture > 0 &&
                        parsed[capture - 1][offset] != value)
                    {
                        changed++;
                    }
                }

                var mostFrequent = counts
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .First();

                var address64 = (ulong)baseAddress + (uint)offset;
                if (address64 > uint.MaxValue)
                    throw new InvalidDataException("BYTE位置のアドレスが範囲外です。");

                _results.Add(new OffsetResult(
                    offset,
                    (uint)address64,
                    parsed.Count,
                    changed,
                    counts.Count,
                    parsed[0][offset],
                    parsed[^1][offset],
                    mostFrequent.Key,
                    mostFrequent.Value));
            }

            _results.Sort((a, b) =>
            {
                var changed = b.ChangedCount.CompareTo(a.ChangedCount);
                if (changed != 0)
                    return changed;

                var distinct = b.DistinctCount.CompareTo(a.DistinctCount);
                if (distinct != 0)
                    return distinct;

                return a.Offset.CompareTo(b.Offset);
            });
        }
    }
    private sealed class PineMultiByteAnalysisForm : Form
    {
        private sealed record ValueResult(
            int Offset,
            uint Address,
            ulong FirstValue,
            ulong LastValue,
            ulong Minimum,
            ulong Maximum,
            ulong MostFrequent,
            int CaptureCount,
            int ChangedCount,
            int DistinctCount,
            int MostFrequentCount);

        private readonly ProjectBundleModel _project;
        private readonly string _candidateName;
        private readonly uint _baseAddress;
        private readonly IReadOnlyList<HistoryItem> _items;
        private readonly List<ValueResult> _results = new();

        private readonly ComboBox _widthSelector = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120
        };

        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false
        };

        private readonly Label _summary = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        public PineMultiByteAnalysisForm(
            ProjectBundleModel project,
            string candidateName,
            uint baseAddress,
            IReadOnlyList<HistoryItem> items)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _candidateName = candidateName;
            _baseAddress = baseAddress;
            _items = items;

            Text = $"WORD・DWORD分析 — {candidateName} / 0x{baseAddress:X8}";
            Width = 1150;
            Height = 720;
            MinimumSize = new Size(800, 480);
            StartPosition = FormStartPosition.CenterParent;

            _widthSelector.Items.AddRange(new object[]
            {
                "WORD (16bit)",
                "DWORD (32bit)"
            });
            _widthSelector.SelectedIndex = 0;
            _widthSelector.SelectedIndexChanged += (_, _) => Rebuild();

            var saveReport = new Button
            {
                Text = "この分析をプロジェクトに保存",
                AutoSize = true
            };
            saveReport.Click += (_, _) => SaveAnalysisReport();

            _grid.Columns.Add("Offset", "相対位置");
            _grid.Columns.Add("Address", "実アドレス");
            _grid.Columns.Add("First", "初回値");
            _grid.Columns.Add("Last", "最新値");
            _grid.Columns.Add("Min", "最小値");
            _grid.Columns.Add("Max", "最大値");
            _grid.Columns.Add("Mode", "最多観測値");
            _grid.Columns.Add("Captures", "取得回数");
            _grid.Columns.Add("Changed", "変化回数");
            _grid.Columns.Add("Distinct", "異なる値");
            _grid.Columns.Add("Frequency", "最多観測回数");

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            toolbar.Controls.Add(new Label
            {
                Text = "解析単位:",
                AutoSize = true,
                Padding = new Padding(0, 7, 0, 0)
            });
            toolbar.Controls.Add(_widthSelector);
            toolbar.Controls.Add(saveReport);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(_grid, 0, 1);
            layout.Controls.Add(_summary, 0, 2);
            Controls.Add(layout);

            Rebuild();
        }

        private void SaveAnalysisReport()
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(this, "保存できる分析結果がありません。");
                return;
            }

            try
            {
                var width = _widthSelector.SelectedIndex == 1 ? 4 : 2;
                var bits = width * 8;
                var digits = width * 2;

                var report = new
                {
                    FormatVersion = 1,
                    ReportType = "PINE_MULTI_BYTE_ANALYSIS",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    CandidateName = _candidateName,
                    BaseAddress = $"0x{_baseAddress:X8}",
                    Unit = width == 2 ? "WORD" : "DWORD",
                    BitWidth = bits,
                    Endianness = "LittleEndian",
                    CaptureCount = _items.Count,
                    SourceCaptures = _items.Select(item => new
                    {
                        item.CapturedAtUtc,
                        item.FileName,
                        item.SceneNote,
                        item.StatusBefore,
                        item.StatusAfter
                    }).ToArray(),
                    Results = _results.Select(result => new
                    {
                        Offset = result.Offset,
                        Address = $"0x{result.Address:X8}",
                        FirstValue = $"0x{result.FirstValue.ToString($"X{digits}")}",
                        LastValue = $"0x{result.LastValue.ToString($"X{digits}")}",
                        Minimum = $"0x{result.Minimum.ToString($"X{digits}")}",
                        Maximum = $"0x{result.Maximum.ToString($"X{digits}")}",
                        MostFrequent = $"0x{result.MostFrequent.ToString($"X{digits}")}",
                        result.CaptureCount,
                        result.ChangedCount,
                        result.DistinctCount,
                        result.MostFrequentCount
                    }).ToArray()
                };

                var json = JsonSerializer.SerializeToUtf8Bytes(
                    report,
                    new JsonSerializerOptions { WriteIndented = true });

                var safeName = string.Concat(
                    _candidateName.Select(ch =>
                        Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "Candidate";

                var fileName =
                    $"PINE_{(width == 2 ? "WORD" : "DWORD")}_{safeName}_{_baseAddress:X8}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";

                var savedName = _project.AddAssetBytes(
                    fileName,
                    "PINE数値分析レポート",
                    json);

                MessageBox.Show(
                    this,
                    $"分析レポートをプロジェクトに保存しました。\n{savedName}",
                    "保存完了",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "分析レポート保存エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void Rebuild()
        {
            var width = _widthSelector.SelectedIndex == 1 ? 4 : 2;
            var bits = width * 8;
            var digits = width * 2;

            _grid.Rows.Clear();
            _results.Clear();

            var parsed = _items
                .Select(x => ParseHex(x.BytesHex))
                .ToList();

            if (parsed.Count < 2)
            {
                _summary.Text = "比較データが不足しています。";
                return;
            }

            var length = parsed[0].Length;

            if (length < width || parsed.Any(x => x.Length != length))
            {
                _summary.Text = "キャプチャ間でデータ長が一致しないか、解析幅が不足しています。";
                return;
            }

            // WORDは2BYTE、DWORDは4BYTE単位で走査する。
            for (var offset = 0; offset <= length - width; offset += width)
            {
                var values = new List<ulong>(parsed.Count);

                foreach (var bytes in parsed)
                    values.Add(ReadLittleEndian(bytes, offset, width));

                var frequencies = new Dictionary<ulong, int>();
                var changed = 0;

                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];

                    if (!frequencies.TryAdd(value, 1))
                        frequencies[value]++;

                    if (i > 0 && values[i - 1] != value)
                        changed++;
                }

                var mode = frequencies
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .First();

                var address64 = (ulong)_baseAddress + (uint)offset;
                if (address64 > uint.MaxValue)
                    throw new InvalidDataException("解析対象アドレスが範囲外です。");

                _results.Add(new ValueResult(
                    offset,
                    (uint)address64,
                    values[0],
                    values[^1],
                    values.Min(),
                    values.Max(),
                    mode.Key,
                    values.Count,
                    changed,
                    frequencies.Count,
                    mode.Value));
            }

            // 変化回数の多い位置を上に表示。
            _results.Sort((a, b) =>
            {
                var changed = b.ChangedCount.CompareTo(a.ChangedCount);
                if (changed != 0)
                    return changed;

                var distinct = b.DistinctCount.CompareTo(a.DistinctCount);
                if (distinct != 0)
                    return distinct;

                return a.Offset.CompareTo(b.Offset);
            });

            foreach (var result in _results)
            {
                _grid.Rows.Add(
                    $"0x{result.Offset:X4}",
                    $"0x{result.Address:X8}",
                    $"0x{result.FirstValue.ToString($"X{digits}")}",
                    $"0x{result.LastValue.ToString($"X{digits}")}",
                    $"0x{result.Minimum.ToString($"X{digits}")}",
                    $"0x{result.Maximum.ToString($"X{digits}")}",
                    $"0x{result.MostFrequent.ToString($"X{digits}")}",
                    result.CaptureCount,
                    result.ChangedCount,
                    result.DistinctCount,
                    result.MostFrequentCount);
            }

            _summary.Text =
                $"{_candidateName} / 0x{_baseAddress:X8} / " +
                $"{bits}bit / リトルエンディアン / " +
                $"{_items.Count} captures / " +
                $"{_results.Count} values";
        }

        private static ulong ReadLittleEndian(
            byte[] bytes,
            int offset,
            int width)
        {
            ulong value = 0;

            for (var i = 0; i < width; i++)
                value |= (ulong)bytes[offset + i] << (8 * i);

            return value;
        }
    }
    private sealed class PineCrossAnalysisForm : Form
    {
        private sealed record CrossResult(
            string Unit,
            int Width,
            int Offset,
            uint Address,
            int CaptureCount,
            int ChangedCount,
            int DistinctCount,
            ulong FirstValue,
            ulong LastValue,
            ulong Minimum,
            ulong Maximum);

        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false
        };

        private readonly Label _summary = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        private readonly List<CrossResult> _results = new();
        private string _candidateName = "";
        private uint _baseAddress;
        private int _captureCount;

        public PineCrossAnalysisForm(
            string candidateName,
            uint baseAddress,
            IReadOnlyList<HistoryItem> items)
        {
            _candidateName = candidateName;
            _baseAddress = baseAddress;
            _captureCount = items.Count;

            Text = $"BYTE・WORD・DWORD横断比較 — {candidateName}";
            Width = 1250;
            Height = 760;
            MinimumSize = new Size(850, 500);
            StartPosition = FormStartPosition.CenterParent;

            _grid.Columns.Add("Unit", "解析単位");
            _grid.Columns.Add("Offset", "相対位置");
            _grid.Columns.Add("Address", "実アドレス");
            _grid.Columns.Add("Captures", "取得回数");
            _grid.Columns.Add("Changed", "変化回数");
            _grid.Columns.Add("Distinct", "異なる値");
            _grid.Columns.Add("First", "初回値");
            _grid.Columns.Add("Last", "最新値");
            _grid.Columns.Add("Min", "最小値");
            _grid.Columns.Add("Max", "最大値");

            BuildRows(baseAddress, items);

            var exportPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };

            var exportCsv = new Button
            {
                Text = "CSV出力…",
                AutoSize = true
            };
            exportCsv.Click += (_, _) => ExportCsv();

            var exportJson = new Button
            {
                Text = "JSON出力…",
                AutoSize = true
            };
            exportJson.Click += (_, _) => ExportJson();

            exportPanel.Controls.Add(exportCsv);
            exportPanel.Controls.Add(exportJson);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_grid, 0, 0);
            layout.Controls.Add(_summary, 0, 1);
            layout.Controls.Add(exportPanel, 0, 2);
            Controls.Add(layout);

            _summary.Text =
                $"候補: {candidateName} / 0x{baseAddress:X8} / " +
                $"{items.Count} captures / BYTE・WORD・DWORD横断比較";
        }

        private void BuildRows(
            uint baseAddress,
            IReadOnlyList<HistoryItem> items)
        {
            var captures = items
                .Select(x => ParseHex(x.BytesHex))
                .ToList();

            if (captures.Count < 2)
                throw new InvalidDataException("比較データが不足しています。");

            var length = captures[0].Length;

            if (length == 0 || captures.Any(x => x.Length != length))
                throw new InvalidDataException("キャプチャ間のデータ長が一致しません。");

            _results.Clear();

            AddUnitResults(_results, captures, baseAddress, 1, "BYTE");
            AddUnitResults(_results, captures, baseAddress, 2, "WORD");
            AddUnitResults(_results, captures, baseAddress, 4, "DWORD");

            // 同じ解析単位の中では変化の大きい位置を先に表示する。
            foreach (var group in _results
                .GroupBy(x => x.Unit)
                .OrderBy(x => x.Key == "BYTE" ? 0 :
                              x.Key == "WORD" ? 1 : 2))
            {
                foreach (var result in group
                    .OrderByDescending(x => x.ChangedCount)
                    .ThenByDescending(x => x.DistinctCount)
                    .ThenBy(x => x.Offset))
                {
                    var digits = result.Width * 2;

                    _grid.Rows.Add(
                        result.Unit,
                        $"0x{result.Offset:X4}",
                        $"0x{result.Address:X8}",
                        result.CaptureCount,
                        result.ChangedCount,
                        result.DistinctCount,
                        $"0x{result.FirstValue.ToString($"X{digits}")}",
                        $"0x{result.LastValue.ToString($"X{digits}")}",
                        $"0x{result.Minimum.ToString($"X{digits}")}",
                        $"0x{result.Maximum.ToString($"X{digits}")}");
                }
            }

            _summary.Text =
                $"BYTE: {_results.Count(x => x.Unit == "BYTE"):N0}位置 / " +
                $"WORD: {_results.Count(x => x.Unit == "WORD"):N0}位置 / " +
                $"DWORD: {_results.Count(x => x.Unit == "DWORD"):N0}位置 / " +
                $"合計 {_results.Count:N0}行";
        }

        private void ExportCsv()
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(this, "出力する解析結果がありません。");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "横断比較結果をCSV出力",
                Filter = "CSVファイル (*.csv)|*.csv",
                FileName = $"PINE横断比較_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                AddExtension = true,
                DefaultExt = "csv"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            static string Csv(string? value)
            {
                value ??= "";
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            var lines = new List<string>
            {
                string.Join(",", new[]
                {
                    "CandidateName", "BaseAddress", "CaptureCount",
                    "Unit", "Width", "Offset", "Address",
                    "CaptureCountAtPosition", "ChangedCount",
                    "DistinctCount", "FirstValue", "LastValue",
                    "Minimum", "Maximum"
                })
            };

            foreach (var item in _results)
            {
                var digits = item.Width * 2;

                lines.Add(string.Join(",", new[]
                {
                    Csv(_candidateName),
                    Csv($"0x{_baseAddress:X8}"),
                    _captureCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    Csv(item.Unit),
                    item.Width.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    Csv($"0x{item.Offset:X4}"),
                    Csv($"0x{item.Address:X8}"),
                    item.CaptureCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    item.ChangedCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    item.DistinctCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    Csv($"0x{item.FirstValue.ToString($"X{digits}")}"),
                    Csv($"0x{item.LastValue.ToString($"X{digits}")}"),
                    Csv($"0x{item.Minimum.ToString($"X{digits}")}"),
                    Csv($"0x{item.Maximum.ToString($"X{digits}")}")
                }));
            }

            File.WriteAllText(
                dialog.FileName,
                string.Join(Environment.NewLine, lines),
                new System.Text.UTF8Encoding(true));

            MessageBox.Show(
                this,
                $"CSVを出力しました。\n{dialog.FileName}",
                "出力完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ExportJson()
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(this, "出力する解析結果がありません。");
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "横断比較結果をJSON出力",
                Filter = "JSONファイル (*.json)|*.json",
                FileName = $"PINE横断比較_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                AddExtension = true,
                DefaultExt = "json"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var report = new
            {
                FormatVersion = 1,
                ReportType = "PINE_BYTE_WORD_DWORD_CROSS_ANALYSIS",
                CandidateName = _candidateName,
                BaseAddress = $"0x{_baseAddress:X8}",
                CaptureCount = _captureCount,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Endianness = "LittleEndian",
                Units = new[] { "BYTE", "WORD", "DWORD" },
                Results = _results.Select(item => new
                {
                    item.Unit,
                    item.Width,
                    Offset = $"0x{item.Offset:X4}",
                    Address = $"0x{item.Address:X8}",
                    item.CaptureCount,
                    item.ChangedCount,
                    item.DistinctCount,
                    FirstValue = $"0x{item.FirstValue.ToString($"X{item.Width * 2}")}",
                    LastValue = $"0x{item.LastValue.ToString($"X{item.Width * 2}")}",
                    Minimum = $"0x{item.Minimum.ToString($"X{item.Width * 2}")}",
                    Maximum = $"0x{item.Maximum.ToString($"X{item.Width * 2}")}"
                }).ToList()
            };

            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = System.Text.Json.JsonSerializer.Serialize(report, options);

            File.WriteAllText(
                dialog.FileName,
                json,
                new System.Text.UTF8Encoding(false));

            MessageBox.Show(
                this,
                $"JSONを出力しました。\n{dialog.FileName}",
                "出力完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static void AddUnitResults(
            List<CrossResult> output,
            IReadOnlyList<byte[]> captures,
            uint baseAddress,
            int width,
            string unit)
        {
            var length = captures[0].Length;

            for (var offset = 0; offset <= length - width; offset += width)
            {
                var values = new List<ulong>(captures.Count);

                foreach (var bytes in captures)
                {
                    ulong value = 0;
                    for (var i = 0; i < width; i++)
                        value |= (ulong)bytes[offset + i] << (8 * i);

                    values.Add(value);
                }

                var distinct = values.Distinct().Count();
                var changed = 0;

                for (var i = 1; i < values.Count; i++)
                {
                    if (values[i] != values[i - 1])
                        changed++;
                }

                var address64 = (ulong)baseAddress + (uint)offset;
                if (address64 > uint.MaxValue)
                    throw new InvalidDataException("解析アドレスが範囲外です。");

                output.Add(new CrossResult(
                    unit,
                    width,
                    offset,
                    (uint)address64,
                    values.Count,
                    changed,
                    distinct,
                    values[0],
                    values[^1],
                    values.Min(),
                    values.Max()));
            }
        }
    }}