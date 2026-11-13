using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace TornekoIjiriNext;

/// <summary>
/// PINE候補キャプチャJSONからオンライン編集項目候補を作成する。
/// 生成項目はすべて未検証状態であり、書き込みは許可されない。
/// </summary>
internal sealed class PineOnlineCandidateImportForm : Form
{
    private const int MaximumFiles = 128;
    private const int MaximumFields = 512;
    private const int MaximumFileBytes = 64 * 1024 * 1024;

    private readonly PineOnlineEditService _service;
    private readonly ListBox _fileList;
    private readonly TextBox _log;
    private readonly Label _summary;
    private readonly ComboBox _widthSelector;
    private readonly CheckBox _changedOnly;
    private readonly Button _addFiles;
    private readonly Button _clearFiles;
    private readonly Button _build;
    private readonly Button _close;

    private readonly List<string> _paths = new();

    private sealed record CaptureRegion(
        string Name,
        uint Address,
        int Length,
        byte[] Bytes);

    private sealed record CaptureData(
        string GameId,
        string GameCrc,
        string GameVersion,
        string CapturedAtUtc,
        List<CaptureRegion> Regions);

    public PineOnlineCandidateImportForm(PineOnlineEditService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        Text = "PINE候補キャプチャから編集項目を作成";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 520);
        Size = new Size(900, 680);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = "候補キャプチャを複数選択して編集項目候補を生成します。",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 10)
        };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        _addFiles = new Button
        {
            Text = "キャプチャJSONを追加...",
            AutoSize = true
        };

        _clearFiles = new Button
        {
            Text = "一覧をクリア",
            AutoSize = true
        };

        _addFiles.Click += (_, _) => AddFiles();
        _clearFiles.Click += (_, _) => ClearFiles();

        toolbar.Controls.Add(_addFiles);
        toolbar.Controls.Add(_clearFiles);

        toolbar.Controls.Add(new Label
        {
            Text = "生成単位:",
            AutoSize = true,
            Margin = new Padding(12, 8, 3, 3)
        });

        _widthSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
            Margin = new Padding(3)
        };

        _widthSelector.Items.AddRange(new object[]
        {
            "BYTE (1)",
            "WORD (2)",
            "DWORD (4)"
        });

        _widthSelector.SelectedIndex = 0;
        toolbar.Controls.Add(_widthSelector);

        _changedOnly = new CheckBox
        {
            Text = "変化したデータだけ登録",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(12, 7, 3, 3)
        };

        toolbar.Controls.Add(_changedOnly);

        _fileList = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = true
        };

        _summary = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "キャプチャファイルを2つ以上追加してください。",
            Padding = new Padding(4)
        };

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };

        _close = new Button
        {
            Text = "閉じる",
            AutoSize = true
        };
        _close.Click += (_, _) => Close();

        _build = new Button
        {
            Text = "編集項目候補を生成して開く",
            AutoSize = true,
            Enabled = false
        };
        _build.Click += (_, _) => BuildWorkspace();

        footer.Controls.Add(_close);
        footer.Controls.Add(_build);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_fileList, 0, 2);
        root.Controls.Add(_log, 0, 3);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);

        _summary.Text = "2つ以上の同一ゲームの候補キャプチャを選択してください。";
        _log.Text =
            "安全上の注意:\r\n" +
            "・生成項目はすべて未検証状態です。\r\n" +
            "・候補アドレスを発見しただけでは書き込みできません。\r\n" +
            "・WORD/DWORDはリトルエンディアンで初期値を読み取ります。\r\n" +
            "・変化したデータだけを抽出する設定が初期状態で有効です。\r\n" +
            "・変化したことはデータの意味や安全性を保証しません。\r\n" +
            "・データの意味を自動判定する機能ではありません。\r\n";
    }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "PINE候補キャプチャJSONを選択",
            Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        foreach (string path in dialog.FileNames)
        {
            if (_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                continue;

            if (_paths.Count >= MaximumFiles)
            {
                MessageBox.Show(
                    this,
                    $"ファイルは最大{MaximumFiles}件までです。",
                    "上限",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;
            }

            _paths.Add(path);
            _fileList.Items.Add(path);
        }

        _build.Enabled = _paths.Count >= 2;
        _summary.Text = $"選択ファイル: {_paths.Count}件";
    }

    private void ClearFiles()
    {
        _paths.Clear();
        _fileList.Items.Clear();
        _build.Enabled = false;
        _summary.Text = "キャプチャファイルを2つ以上追加してください。";
        _log.Clear();
    }

    private void BuildWorkspace()
    {
        if (_paths.Count < 2)
        {
            MessageBox.Show(
                this,
                "同一ゲームのキャプチャを2つ以上選択してください。",
                "ファイル不足",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _build.Enabled = false;

        try
        {
            var captures = new List<CaptureData>();

            foreach (string path in _paths)
                captures.Add(ReadCapture(path));

            var first = captures[0];

            foreach (var capture in captures.Skip(1))
            {
                if (!SameIdentity(first, capture))
                    throw new InvalidDataException(
                        "ゲームID・CRC・バージョンが異なるキャプチャが含まれています。");
            }

            var firstRegions = first.Regions
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Address)
                .ToList();

            foreach (var capture in captures.Skip(1))
            {
                var regions = capture.Regions
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Address)
                    .ToList();

                if (regions.Count != firstRegions.Count)
                    throw new InvalidDataException(
                        "キャプチャ間で候補領域の数が異なります。");

                for (int i = 0; i < regions.Count; i++)
                {
                    if (!string.Equals(
                        regions[i].Name,
                        firstRegions[i].Name,
                        StringComparison.OrdinalIgnoreCase) ||
                        regions[i].Address != firstRegions[i].Address ||
                        regions[i].Length != firstRegions[i].Length)
                    {
                        throw new InvalidDataException(
                            "キャプチャ間で候補領域の構成が一致しません。");
                    }
                }
            }

            int width = _widthSelector.SelectedIndex switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                _ => throw new InvalidOperationException("生成単位が不正です。")
            };

            var definitions = new List<PineOnlineEditFieldDefinition>();

            foreach (var region in firstRegions)
            {
                for (int offset = 0;
                     offset <= region.Length - width;
                     offset += width)
                {
                    if (definitions.Count >= MaximumFields)
                        break;

                    // 全キャプチャで同じ位置のデータを比較する。
                    // 変化したデータだけを抽出する設定なら、
                    // 全キャプチャで同一の値だった項目を除外する。
                    if (_changedOnly.Checked)
                    {
                        bool changed = false;

                        foreach (var capture in captures.Skip(1))
                        {
                            var matchingRegion = capture.Regions.FirstOrDefault(
                                x => string.Equals(
                                    x.Name,
                                    region.Name,
                                    StringComparison.OrdinalIgnoreCase) &&
                                    x.Address == region.Address &&
                                    x.Length == region.Length);

                            if (matchingRegion is null)
                                throw new InvalidDataException(
                                    "比較対象の候補領域が見つかりません。");

                            if (!region.Bytes.AsSpan(offset, width).SequenceEqual(
                                matchingRegion.Bytes.AsSpan(offset, width)))
                            {
                                changed = true;
                                break;
                            }
                        }

                        if (!changed)
                            continue;
                    }

                    uint address = checked(region.Address + (uint)offset);

                    uint initialValue = width switch
                    {
                        1 => region.Bytes[offset],

                        2 => (uint)(
                            region.Bytes[offset] |
                            (region.Bytes[offset + 1] << 8)),

                        4 => (uint)(
                            region.Bytes[offset] |
                            (region.Bytes[offset + 1] << 8) |
                            (region.Bytes[offset + 2] << 16) |
                            (region.Bytes[offset + 3] << 24)),

                        _ => throw new InvalidOperationException(
                            "未対応のデータ幅です。")
                    };

                    string unitName = width switch
                    {
                        1 => "BYTE",
                        2 => "WORD",
                        4 => "DWORD",
                        _ => "UNKNOWN"
                    };

                    string fieldName =
                        $"{region.Name}_{unitName}_+0x{offset:X}";

                    definitions.Add(
                        new PineOnlineEditFieldDefinition(
                            region.Name,
                            fieldName,
                            address,
                            width,
                            initialValue,
                            false,
                            $"候補キャプチャ{captures.Count}件から生成。" +
                            $"{unitName}・リトルエンディアン・未検証。"
                        ));
                }

                if (definitions.Count >= MaximumFields)
                    break;
            }

            if (definitions.Count == 0)
                throw new InvalidDataException("編集項目候補を生成できませんでした。");

            var workspace = new PineOnlineEditWorkspaceForm(
                _service,
                definitions,
                "PINE候補から生成したオンライン編集項目");

            _log.AppendText(
                "\r\n生成結果:\r\n" +
                $"ゲームID: {first.GameId}\r\n" +
                $"CRC: {first.GameCrc}\r\n" +
                $"バージョン: {first.GameVersion}\r\n" +
                $"キャプチャ: {captures.Count}件\r\n" +
                $"候補領域: {firstRegions.Count}件\r\n" +
                $"生成単位: {_widthSelector.SelectedItem}\r\n" +
                $"変化したデータのみ: {(_changedOnly.Checked ? "有効" : "無効")}\r\n" +
                $"生成項目: {definitions.Count}件\r\n" +
                "\r\n全項目を未検証状態に設定しました。\r\n");

            _summary.Text =
                $"生成成功: {definitions.Count}項目 / 全項目未検証";

            workspace.Show(this);
        }
        catch (Exception ex)
        {
            _log.AppendText("\r\n生成エラー:\r\n" + ex.Message + "\r\n");

            MessageBox.Show(
                this,
                ex.Message,
                "候補キャプチャ読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _build.Enabled = _paths.Count >= 2;
        }
    }

    private static bool SameIdentity(CaptureData a, CaptureData b)
    {
        return string.Equals(a.GameId, b.GameId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.GameCrc, b.GameCrc, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.GameVersion, b.GameVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static CaptureData ReadCapture(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
            throw new FileNotFoundException("キャプチャファイルがありません。", path);

        if (info.Length < 1 || info.Length > MaximumFileBytes)
            throw new InvalidDataException(
                $"ファイルサイズが不正です: {Path.GetFileName(path)}");

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions { MaxDepth = 32 });

        JsonElement root = document.RootElement;

        int formatVersion = ReadInt(root, "FormatVersion");
        if (formatVersion != 1)
            throw new InvalidDataException(
                $"未対応のキャプチャ形式です: {Path.GetFileName(path)}");

        string gameId = ReadString(root, "GameId");
        string gameCrc = ReadString(root, "GameCrc");
        string gameVersion = ReadString(root, "GameVersion");
        string capturedAt = ReadString(root, "CapturedAtUtc");

        if (string.IsNullOrWhiteSpace(gameId) ||
            string.IsNullOrWhiteSpace(gameCrc) ||
            string.IsNullOrWhiteSpace(gameVersion))
        {
            throw new InvalidDataException("ゲーム識別情報が不足しています。");
        }

        if (!root.TryGetProperty("Regions", out JsonElement regionsElement) ||
            regionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Regions配列がありません。");
        }

        if (regionsElement.GetArrayLength() is < 1 or > 64)
            throw new InvalidDataException("候補領域数が範囲外です。");

        var regions = new List<CaptureRegion>();
        int totalBytes = 0;

        foreach (JsonElement item in regionsElement.EnumerateArray())
        {
            string name = ReadString(item, "Name");
            uint address = ReadUInt(item, "Address");
            int length = ReadInt(item, "Length");
            string hex = ReadString(item, "BytesHex");

            if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                throw new InvalidDataException("候補領域名が不正です。");

            if (length is < 1 or > 4096)
                throw new InvalidDataException("候補領域の長さが不正です。");

            ulong end = (ulong)address + (uint)length - 1;
            if (end > uint.MaxValue)
                throw new InvalidDataException("候補アドレス範囲が不正です。");

            if (hex.Length != checked(length * 2))
                throw new InvalidDataException(
                    $"HEXデータ長が一致しません: {name}");

            byte[] bytes;

            try
            {
                bytes = Convert.FromHexString(hex);
            }
            catch
            {
                throw new InvalidDataException(
                    $"HEXデータが不正です: {name}");
            }

            totalBytes = checked(totalBytes + bytes.Length);

            if (totalBytes > 65536)
                throw new InvalidDataException(
                    "候補領域の合計が65536バイトを超えています。");

            regions.Add(new CaptureRegion(
                name,
                address,
                length,
                bytes));
        }

        return new CaptureData(
            gameId,
            gameCrc,
            gameVersion,
            capturedAt,
            regions);
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            throw new InvalidDataException($"必須項目がありません: {name}");

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => throw new InvalidDataException(
                $"文字列項目が不正です: {name}")
        };
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) ||
            !value.TryGetInt32(out int result))
        {
            throw new InvalidDataException($"整数項目が不正です: {name}");
        }

        return result;
    }

    private static uint ReadUInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            throw new InvalidDataException($"数値項目がありません: {name}");

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetUInt32(out uint numeric))
            return numeric;

        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? "";

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];

            if (uint.TryParse(
                text,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out uint hex))
                return hex;

            if (uint.TryParse(text, out uint decimalValue))
                return decimalValue;
        }

        throw new InvalidDataException($"アドレス項目が不正です: {name}");
    }
}