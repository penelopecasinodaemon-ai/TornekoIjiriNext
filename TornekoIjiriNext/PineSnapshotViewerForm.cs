using System.Buffers.Binary;
using System.Text;

namespace TornekoIjiriNext;

/// <summary>
/// Displays an immutable, read-only PINE session snapshot.
/// Export writes a separate BIN file and never writes to game memory.
/// </summary>
internal sealed class PineSnapshotViewerForm : Form
{
    private readonly PineSessionSnapshot _snapshot;
    private readonly ListBox _regions = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false
    };
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 10)
    };
    private readonly Button _save = new()
    {
        Text = "選択領域をBIN保存…",
        AutoSize = true,
        Enabled = false
    };

    private readonly Button _saveAll = new()
    {
        Text = "全領域をBIN一括保存…",
        AutoSize = true,
        Enabled = false
    };

    private readonly Button _edit = new()
    {
        Text = "選択領域をバイナリ編集…",
        AutoSize = true,
        Enabled = false
    };
    private readonly Button _importSelected = new()
    {
        Text = "選択領域をプロジェクトへ取り込む…",
        AutoSize = true
    };

    private readonly Button _importAll = new()
    {
        Text = "全領域をプロジェクトへ一括取り込み…",
        AutoSize = true
    };
    public PineSnapshotViewerForm(PineSessionSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        Text = "PINE取得データ（読み取り専用）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(780, 540);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 2,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var metadata = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(940, 0),
            Text =
                $"ゲームID: {_snapshot.GameId}    CRC: {_snapshot.GameCrc}    版: {_snapshot.GameVersion}" +
                Environment.NewLine +
                $"接続世代: {_snapshot.ConnectionGeneration}    取得日時: {_snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}" +
                Environment.NewLine +
                $"状態: {_snapshot.StatusBefore} → {_snapshot.StatusAfter}    領域数: {_snapshot.Regions.Count}",
            Padding = new Padding(2, 2, 2, 10)
        };
        root.Controls.Add(metadata, 0, 0);
        root.SetColumnSpan(metadata, 2);

        _saveAll.Enabled = _snapshot.Regions.Count > 0;

        foreach (var region in _snapshot.Regions)
            _regions.Items.Add($"0x{region.Address:X8}    {region.Length} bytes");

        root.Controls.Add(_regions, 0, 1);
        root.Controls.Add(_details, 1, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true
        };
        footer.Controls.Add(_saveAll);
        footer.Controls.Add(_save);
        footer.Controls.Add(_edit);
        footer.Controls.Add(_importSelected);
        footer.Controls.Add(_importAll);
        footer.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "BIN保存は選択領域のraw bytesのみ。ゲームメモリへの書き込みは行いません。",
            Margin = new Padding(3, 8, 12, 0)
        });
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);

        Controls.Add(root);

        _regions.SelectedIndexChanged += (_, _) =>
        {
            ShowSelectedRegion();
            _edit.Enabled = GetSelectedRegion() is not null;
        };
        _saveAll.Click += (_, _) => SaveAllRegions();
        _save.Click += (_, _) => SaveSelectedRegion();
        _edit.Click += (_, _) => OpenSelectedInBinaryEditor();
        _importSelected.Click += (_, _) => ImportRegionsToProject(false);
        _importAll.Click += (_, _) => ImportRegionsToProject(true);

        if (_regions.Items.Count > 0)
            _regions.SelectedIndex = 0;
    }

    private void OpenSelectedInBinaryEditor()
    {
        var region = GetSelectedRegion();

        if (region is null)
            return;

        try
        {
            var displayName =
                $"PINE_{_snapshot.GameId}_{region.Address:X8}_{region.Length}bytes.bin";

            using var editor = new LegacyBinaryEditorForm(
                region.CopyBytes(),
                displayName);

            editor.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "バイナリエディタを開けません",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c =>
            invalid.Contains(c) || !(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                ? '_'
                : c).ToArray());

        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private string BuildPineAssetName(PineCachedRegion region)
    {
        var game = SafeFilePart(_snapshot.GameId);
        var crc = SafeFilePart(_snapshot.GameCrc);
        var version = SafeFilePart(_snapshot.GameVersion);
        var timestamp = _snapshot.CapturedAt.ToUniversalTime().ToString("yyyyMMdd_HHmmss");

        return $"PINE_{game}_{crc}_{version}_0x{region.Address:X8}_{region.Length}bytes_{timestamp}.bin";
    }

    private void ImportRegionsToProject(bool importAll)
    {
        var selected = GetSelectedRegion();

        if (!importAll && selected is null)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "取り込み先のトルネコ Nextプロジェクトを選択",
            Filter = "Torneko Next project (*.tijproj)|*.tijproj|JSON manifest (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var project = ProjectBundleModel.Open(dialog.FileName);

            if (!string.Equals(project.TargetGameId, _snapshot.GameId, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    $"プロジェクトの対象IDは {project.TargetGameId}、PINE取得データのゲームIDは {_snapshot.GameId} です。別のゲームのデータは取り込めません。",
                    "ゲームID不一致",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var regions = importAll
                ? _snapshot.Regions.ToArray()
                : new[] { selected! };

            var imported = new List<string>();

            foreach (var region in regions)
            {
                var name = BuildPineAssetName(region);
                var bytes = region.CopyBytes();

                var metadata = new PineAssetMetadata(
                    _snapshot.GameId,
                    _snapshot.GameCrc,
                    _snapshot.GameVersion,
                    region.Address,
                    region.Length,
                    _snapshot.CapturedAt,
                    _snapshot.ConnectionGeneration);

                var storedName = project.AddAssetBytes(
                    name,
                    "その他",
                    bytes,
                    pineSource: metadata);

                imported.Add(storedName);
            }

            MessageBox.Show(
                this,
                $"PINE取得データをプロジェクトへ取り込みました。\n\nプロジェクト: {project.Name}\n取り込み数: {imported.Count}\n\n元のPINEキャッシュとゲームメモリは変更していません。",
                "プロジェクト取り込み完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "PINEデータ取り込み失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
    private PineCachedRegion? GetSelectedRegion()
    {
        var index = _regions.SelectedIndex;
        if (index < 0 || index >= _snapshot.Regions.Count)
            return null;

        return _snapshot.Regions[index];
    }

    private void ShowSelectedRegion()
    {
        var region = GetSelectedRegion();
        _save.Enabled = region is not null;

        if (region is null)
        {
            _details.Clear();
            return;
        }

        var bytes = region.CopyBytes();
        var output = new StringBuilder();

        output.AppendLine($"アドレス: 0x{region.Address:X8}");
        output.AppendLine($"長さ: {bytes.Length} bytes");
        output.AppendLine("データの意味・構造は未判定です。");
        output.AppendLine();
        output.AppendLine("HEX:");

        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);
            var hex = string.Join(" ",
                bytes.AsSpan(offset, count).ToArray().Select(x => x.ToString("X2")));
            output.AppendLine($"0x{region.Address + (uint)offset:X8}: {hex}");
        }

        output.AppendLine();
        output.AppendLine("DWORD (4-byte little-endian):");

        for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset, 4));
            output.AppendLine(
                $"0x{region.Address + (uint)offset:X8}: 0x{value:X8}");
        }

        var remainder = bytes.Length % 4;
        if (remainder > 0)
            output.AppendLine($"末尾 {remainder} bytes はDWORD表示に含めていません。");

        _details.Text = output.ToString();
    }

    private void SaveAllRegions()
    {
        if (_snapshot.Regions.Count == 0)
            return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "PINE取得データの保存先フォルダを選択してください。",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string outputDirectory;

        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseName = $"PINE_{_snapshot.GameId}_{stamp}";
            outputDirectory = Path.Combine(dialog.SelectedPath, baseName);

            var suffix = 1;
            while (Directory.Exists(outputDirectory))
            {
                outputDirectory = Path.Combine(
                    dialog.SelectedPath,
                    $"{baseName}_{suffix++}");
            }

            Directory.CreateDirectory(outputDirectory);

            var manifest = new StringBuilder();
            manifest.AppendLine("PINE session snapshot");
            manifest.AppendLine($"GameId={_snapshot.GameId}");
            manifest.AppendLine($"GameCrc={_snapshot.GameCrc}");
            manifest.AppendLine($"GameVersion={_snapshot.GameVersion}");
            manifest.AppendLine($"ConnectionGeneration={_snapshot.ConnectionGeneration}");
            manifest.AppendLine($"CapturedAt={_snapshot.CapturedAt:O}");
            manifest.AppendLine($"StatusBefore={_snapshot.StatusBefore}");
            manifest.AppendLine($"StatusAfter={_snapshot.StatusAfter}");
            manifest.AppendLine();
            manifest.AppendLine("Address,Length,File");

            foreach (var region in _snapshot.Regions)
            {
                var fileName =
                    $"pine_{_snapshot.GameId}_{region.Address:X8}_{region.Length}bytes.bin";

                var filePath = Path.Combine(outputDirectory, fileName);
                File.WriteAllBytes(filePath, region.CopyBytes());

                manifest.AppendLine(
                    $"0x{region.Address:X8},{region.Length},{fileName}");
            }

            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.txt"),
                manifest.ToString(),
                new UTF8Encoding(false));

            MessageBox.Show(
                this,
                $"{_snapshot.Regions.Count}領域を保存しました。{Environment.NewLine}{outputDirectory}",
                "一括保存完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "一括保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
    private void SaveSelectedRegion()
    {
        var region = GetSelectedRegion();
        if (region is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "選択したPINE領域をBIN保存",
            Filter = "BINファイル (*.bin)|*.bin",
            DefaultExt = "bin",
            AddExtension = true,
            FileName = $"pine_{_snapshot.GameId}_{region.Address:X8}.bin",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, region.CopyBytes());
            MessageBox.Show(this,
                $"BINファイルを保存しました。{Environment.NewLine}{dialog.FileName}",
                "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "BIN保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}