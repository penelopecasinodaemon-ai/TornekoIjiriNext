using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

internal sealed class PineCaptureProjectImportForm : Form
{
    private readonly TextBox _projectPath = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        PlaceholderText = "プロジェクトのmanifest.jsonを選択してください"
    };

    private readonly ListBox _files = new()
    {
        Dock = DockStyle.Fill,
        HorizontalScrollbar = true
    };

    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "プロジェクト未選択"
    };

    private readonly Button _selectProject = new()
    {
        Text = "プロジェクトを選択",
        AutoSize = true
    };

    private readonly Button _selectFiles = new()
    {
        Text = "候補キャプチャJSONを追加",
        AutoSize = true
    };

    private readonly Button _removeFile = new()
    {
        Text = "選択項目を除外",
        AutoSize = true
    };

    private readonly Button _import = new()
    {
        Text = "プロジェクトへ一括取り込み",
        AutoSize = true,
        Enabled = false
    };

    private string? _manifestPath;

    private sealed record ValidatedCapture(
        string Path,
        string GameId,
        string GameCrc,
        string GameVersion,
        int RegionCount,
        int TotalBytes);

    public PineCaptureProjectImportForm()
    {
        Text = "PINE候補キャプチャ・プロジェクト取り込み";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        Size = new Size(950, 680);
        Font = new Font("Yu Gothic UI", 9.5f);

        BuildUi();

        _selectProject.Click += (_, _) => SelectProject();
        _selectFiles.Click += (_, _) => SelectFiles();
        _removeFile.Click += (_, _) => RemoveSelectedFile();
        _import.Click += (_, _) => ImportFiles();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(root);

        var projectLabel = new Label
        {
            Text = "取り込み先プロジェクト",
            AutoSize = true
        };

        root.Controls.Add(projectLabel, 0, 0);

        var projectRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2
        };

        projectRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        projectRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));

        projectRow.Controls.Add(_projectPath, 0, 0);
        projectRow.Controls.Add(_selectProject, 1, 0);

        root.Controls.Add(projectRow, 0, 1);

        root.Controls.Add(_files, 0, 2);

        var fileButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        fileButtons.Controls.Add(_selectFiles);
        fileButtons.Controls.Add(_removeFile);

        root.Controls.Add(fileButtons, 0, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(2)
        };

        footer.Controls.Add(_status);
        footer.Controls.Add(_import);

        root.Controls.Add(footer, 0, 4);
    }

    private void SelectProject()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "プロジェクトのmanifest.jsonを選択",
            Filter = "プロジェクトマニフェスト (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var project = ProjectBundleModel.Open(dialog.FileName);

            _manifestPath = Path.GetFullPath(dialog.FileName);
            _projectPath.Text = _manifestPath;

            _status.Text =
                $"プロジェクト: {project.Name} / 登録資産: {project.Entries.Count}件";

            UpdateImportButton();
        }
        catch (Exception ex)
        {
            _manifestPath = null;
            _projectPath.Clear();

            UpdateImportButton();

            MessageBox.Show(
                this,
                ex.Message,
                "プロジェクト読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SelectFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "プロジェクトへ取り込むPINE候補キャプチャを選択",
            Filter = "候補キャプチャJSON (*.json)|*.json",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var existing = _files.Items
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in dialog.FileNames)
        {
            var fullPath = Path.GetFullPath(path);

            if (existing.Add(fullPath))
                _files.Items.Add(fullPath);
        }

        UpdateImportButton();
    }

    private void RemoveSelectedFile()
    {
        if (_files.SelectedIndex < 0)
            return;

        _files.Items.RemoveAt(_files.SelectedIndex);
        UpdateImportButton();
    }

    private void UpdateImportButton()
    {
        _import.Enabled =
            !string.IsNullOrWhiteSpace(_manifestPath) &&
            _files.Items.Count > 0;

        if (_manifestPath is null)
            _status.Text = "プロジェクトを選択してください";
        else if (_files.Items.Count == 0)
            _status.Text = "取り込むキャプチャを選択してください";
        else
            _status.Text = $"取り込み候補: {_files.Items.Count}件";
    }

    private void ImportFiles()
    {
        if (_manifestPath is null)
            return;

        var paths = _files.Items
            .Cast<string>()
            .ToArray();

        if (paths.Length == 0)
            return;

        try
        {
            // まず全ファイルを検査する。
            // 1件でも不正ならプロジェクトへの書き込みは行わない。
            var validated = new List<ValidatedCapture>();

            foreach (var path in paths)
                validated.Add(ValidateCapture(path));

            var first = validated[0];

            foreach (var capture in validated)
            {
                if (!string.Equals(
                        first.GameId,
                        capture.GameId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        first.GameCrc,
                        capture.GameCrc,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        first.GameVersion,
                        capture.GameVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "異なるゲームID・CRC・バージョンのキャプチャが混在しています。");
                }
            }

            var summary = new StringBuilder();

            summary.AppendLine(
                $"取り込み先: {Path.GetFileName(_manifestPath)}");

            summary.AppendLine(
                $"ゲームID: {first.GameId}");

            summary.AppendLine(
                $"CRC: {first.GameCrc}");

            summary.AppendLine(
                $"ゲームバージョン: {first.GameVersion}");

            summary.AppendLine();
            summary.AppendLine(
                $"取り込みファイル数: {validated.Count}件");

            summary.AppendLine();
            summary.AppendLine(
                "次のファイルをプロジェクトへ追加します:");

            foreach (var capture in validated)
            {
                summary.AppendLine(
                    $"・{Path.GetFileName(capture.Path)} " +
                    $"({capture.RegionCount}領域 / {capture.TotalBytes:N0} bytes)");
            }

            summary.AppendLine();
            summary.AppendLine(
                "元のJSONファイルは変更せず、プロジェクト内へコピーします。");

            if (MessageBox.Show(
                    this,
                    summary.ToString(),
                    "取り込み確認",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information) != DialogResult.OK)
            {
                return;
            }

            var project = ProjectBundleModel.Open(_manifestPath);

            project.AddFiles(
                validated.Select(x =>
                    (x.Path, "PINE候補キャプチャ")));

            var result = new StringBuilder();

            result.AppendLine(
                "PINE候補キャプチャをプロジェクトへ取り込みました。");

            result.AppendLine();

            result.AppendLine(
                $"追加件数: {validated.Count}件");

            result.AppendLine(
                $"プロジェクト: {project.Name}");

            result.AppendLine(
                $"登録資産総数: {project.Entries.Count}件");

            result.AppendLine();

            result.AppendLine(
                "取り込んだファイルはプロジェクトのassetsフォルダに保存されています。");

            _status.Text =
                $"取り込み完了: {validated.Count}件";

            _files.Items.Clear();
            _import.Enabled = false;

            MessageBox.Show(
                this,
                result.ToString(),
                "取り込み完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "取り込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static ValidatedCapture ValidateCapture(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
            throw new FileNotFoundException(
                "候補キャプチャが見つかりません。",
                path);

        if (info.Length > 64L * 1024 * 1024)
            throw new InvalidDataException(
                $"ファイルサイズが64MiBを超えています: {Path.GetFileName(path)}");

        using var document = JsonDocument.Parse(
            File.ReadAllBytes(path),
            new JsonDocumentOptions { MaxDepth = 32 });

        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "JSONのルート形式が不正です。");

        if (ReadInt(root, "FormatVersion") != 1)
            throw new InvalidDataException(
                $"未対応の形式バージョンです: {Path.GetFileName(path)}");

        var gameId = ReadString(root, "GameId");
        var crc = ReadString(root, "GameCrc");
        var version = ReadString(root, "GameVersion");

        _ = ReadString(root, "CapturedAtUtc");
        _ = ReadString(root, "Pcsx2Version");

        if (!TryGet(root, "Regions", out var regions) ||
            regions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Regions配列がありません。");
        }

        if (regions.GetArrayLength() is < 1 or > 64)
            throw new InvalidDataException(
                "候補領域は1～64個である必要があります。");

        var names = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var totalBytes = 0;

        foreach (var region in regions.EnumerateArray())
        {
            var name = ReadString(region, "Name");
            var address = ReadUInt(region, "Address");
            var length = ReadInt(region, "Length");
            var hex = ReadString(region, "BytesHex");

            if (!names.Add(name))
                throw new InvalidDataException(
                    $"候補領域名が重複しています: {name}");

            if (length is < 1 or > 4096)
                throw new InvalidDataException(
                    $"候補領域の長さが不正です: {name}");

            if (hex.Length != length * 2 ||
                (hex.Length & 1) != 0 ||
                !hex.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    $"候補領域のHEXデータが不正です: {name}");
            }

            if ((ulong)address + (uint)length - 1 > uint.MaxValue)
                throw new InvalidDataException(
                    $"候補領域のアドレス範囲が不正です: {name}");

            totalBytes += length;

            if (totalBytes > 65536)
                throw new InvalidDataException(
                    "候補領域の合計サイズが65536 bytesを超えています。");

            ValidateOptionalContext(region, "ContextBeforeHex");
            ValidateOptionalContext(region, "ContextAfterHex");
        }

        return new ValidatedCapture(
            Path.GetFullPath(path),
            gameId,
            crc,
            version,
            regions.GetArrayLength(),
            totalBytes);
    }

    private static void ValidateOptionalContext(
        JsonElement region,
        string propertyName)
    {
        if (!TryGet(region, propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return;

        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(
                $"{propertyName}の形式が不正です。");

        var hex = value.GetString() ?? "";

        if (hex.Length > 512 ||
            hex.Length % 2 != 0 ||
            !hex.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"{propertyName}のHEXデータが不正です。");
        }
    }

    private static bool TryGet(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(
        JsonElement element,
        string name)
    {
        if (!TryGet(element, name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"必須文字列がありません: {name}");
        }

        return value.GetString()!;
    }

    private static int ReadInt(
        JsonElement element,
        string name)
    {
        if (!TryGet(element, name, out var value) ||
            !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException(
                $"整数フィールドが不正です: {name}");
        }

        return result;
    }

    private static uint ReadUInt(
        JsonElement element,
        string name)
    {
        if (!TryGet(element, name, out var value))
            throw new InvalidDataException(
                $"数値フィールドがありません: {name}");

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetUInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim() ?? "";

            if (text.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
                text = text[2..];

            if (uint.TryParse(
                    text,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var hex))
                return hex;
        }

        throw new InvalidDataException(
            $"アドレス値が不正です: {name}");
    }
}