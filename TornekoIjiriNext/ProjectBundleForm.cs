namespace TornekoIjiriNext;

internal sealed class ProjectBundleForm : Form
{
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label _summary = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "ローカルの旧データを一つのプロジェクトにまとめます。PCSX2には接続しません。" };
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private ProjectBundleModel? _project;
    private readonly Button _openEditorButton;
    private readonly Button _historyButton;

    public ProjectBundleForm()
    {
        Text = "旧データプロジェクト（オフライン）";
        Width = 900; Height = 560; MinimumSize = new Size(700, 420); StartPosition = FormStartPosition.CenterParent;
        _category.Items.AddRange(new object[] { "INI設定", "モンスター出現率", "モンスター能力", "成長テーブル", "フロア設定", "アイテム出現率", "アイテムカテゴリ率", "個別アイテム率", "固定部屋", "固定部屋object table", "snapshot", "その他" });
        _category.SelectedIndex = 0;
        var create = new Button { Text = "新規プロジェクト…", AutoSize = true };
        create.Click += (_, _) => CreateProject();
        var open = new Button { Text = "プロジェクトを開く…", AutoSize = true };
        open.Click += (_, _) => OpenProject();
        var add = new Button { Text = "旧データを追加…", AutoSize = true, Enabled = false };
        add.Click += (_, _) => AddFiles();
        var verify = new Button { Text = "整合性を確認", AutoSize = true, Enabled = false };
        verify.Click += (_, _) => Verify();
        _openEditorButton = new Button { Text = "選択assetをeditorで開く…", AutoSize = true, Enabled = false };
        _openEditorButton.Click += (_, _) => OpenSelectedEditor();
        _historyButton = new Button { Text = "履歴・差分…", AutoSize = true, Enabled = false };
        _historyButton.Click += (_, _) => OpenSelectedHistory();
        var addCaptures = new Button { Text = "PINE capture JSON追加…", AutoSize = true, Enabled = false };
        addCaptures.Click += (_, _) => AddCaptureFiles();
        var compareCaptures = new Button { Text = "capture比較…", AutoSize = true, Enabled = false };
        compareCaptures.Click += (_, _) => { if (_project is not null) new ProjectCaptureCompareForm(_project).ShowDialog(this); };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange(new Control[] { create, open, _category, add, addCaptures, compareCaptures, verify, _openEditorButton, _historyButton });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(_grid, 0, 1); layout.Controls.Add(_summary, 0, 2);
        Controls.Add(layout);
        _addButton = add; _verifyButton = verify;
        _addCaptureButton = addCaptures; _compareCaptureButton = compareCaptures;
        ConfigureGrid();
    }

    private readonly Button _addButton;
    private readonly Button _verifyButton;
    private readonly Button _addCaptureButton;
    private readonly Button _compareCaptureButton;

    private void ConfigureGrid()
    {
        _grid.Columns.Add("FileName", "ファイル");
        _grid.Columns.Add("Category", "分類");
        _grid.Columns.Add("Length", "サイズ (bytes)");
        _grid.Columns.Add("Parent", "元asset");
        _grid.Columns.Add("Hash", "SHA-256");
        _grid.Columns.Add("Added", "取り込み日時 (UTC)");
    }

    private void CreateProject()
    {
        using var dialog = new SaveFileDialog { Filter = "Torneko Next project (*.tijproj)|*.tijproj", DefaultExt = "tijproj", AddExtension = true, FileName = "TornekoProject.tijproj" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SetProject(ProjectBundleModel.Create(dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName))); }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog { Filter = "Torneko Next project (*.tijproj)|*.tijproj|JSON manifest (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SetProject(ProjectBundleModel.Open(dialog.FileName)); Verify(); }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void AddFiles()
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog { Filter = "旧データ (*.bin;*.json;*.ini;*.txt)|*.bin;*.json;*.ini;*.txt|すべてのファイル (*.*)|*.*", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _project.AddFiles(dialog.FileNames.Select(path => (path, _category.SelectedItem?.ToString() ?? "その他")));
            RefreshList();
            _summary.Text = $"{_project.Name} — { _project.Entries.Count } files。元ファイルは変更せず、assetsへコピーして管理します。";
        }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void AddCaptureFiles()
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog { Title = "PINE read-only capture JSONを選択", Filter = "JSON (*.json)|*.json", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imports = dialog.FileNames.Select(path => (path, ProjectCaptureModel.BuildCategory(ProjectCaptureModel.Read(path)))).ToArray();
            _project.AddFiles(imports);
            RefreshList();
            _summary.Text = $"PINE captureを登録しました。各JSONのgame ID/CRC/版・範囲は保持されています。PINEへ接続せず、ゲームメモリは変更していません。";
        }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void Verify()
    {
        if (_project is null) return;
        var errors = _project.VerifyAssets();
        var message = errors.Count == 0 ? $"整合性OK: {_project.Entries.Count} files" : "検証に失敗しました:\n" + string.Join("\n", errors);
        _summary.Text = $"{_project.Name} — {message}";
        if (errors.Count > 0) MessageBox.Show(this, message, "プロジェクト検証", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SetProject(ProjectBundleModel project)
    {
        _project = project;
        _addButton.Enabled = _verifyButton.Enabled = true;
        _openEditorButton.Enabled = true;
        _historyButton.Enabled = true;
        _addCaptureButton.Enabled = true;
        _compareCaptureButton.Enabled = true;
        RefreshList();
        _summary.Text = $"{project.Name} — 対象IDラベル: {project.TargetGameId}（manifest上の分類であり、ゲーム接続/検証を示しません） — {project.ManifestPath}";
    }

    private void RefreshList()
    {
        _grid.Rows.Clear();
        if (_project is null) return;
        foreach (var entry in _project.Entries) _grid.Rows.Add(entry.FileName, entry.Category, entry.Length.ToString("N0"), entry.ParentFileName ?? "—", entry.Sha256, entry.AddedUtc.ToString("u"));
    }

    private void OpenSelectedHistory()
    {
        if (_project is null || _grid.SelectedRows.Count == 0) { MessageBox.Show(this, "先にassetを選択してください。"); return; }
        var row = _grid.SelectedRows[0].Index;
        if (row < 0 || row >= _project.Entries.Count) return;
        using var history = new ProjectBundleHistoryForm(_project, _project.Entries[row].FileName);
        history.ShowDialog(this);
    }

    private void OpenSelectedEditor()
    {
        if (_project is null || _grid.SelectedRows.Count == 0) { MessageBox.Show(this, "先にassetを選択してください。"); return; }
        var row = _grid.SelectedRows[0].Index;
        if (row < 0 || row >= _project.Entries.Count) return;
        var entry = _project.Entries[row];
        var editorKind = ProjectBundleEditorRouter.Resolve(entry, entry.Length);
        if (editorKind is null)
        { MessageBox.Show(this, "assetの分類と拡張子/サイズに対応するeditorがありません。形式を推測して開かず、分類を確認してください。", "editor未対応", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var problems = _project.VerifyAssets();
        if (problems.Count != 0) { MessageBox.Show(this, "project整合性が崩れているため編集を止めました。先に検証してください。\n" + string.Join("\n", problems), "asset検証失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var path = Path.Combine(_project.RootDirectory, "assets", entry.FileName);
        var project = _project;
        if (editorKind == ProjectBundleEditorKind.FixedRoomObjectTable)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != FixedRoomObjectEditorForm.RecordCount * FixedRoomObjectEditorForm.RecordSize)
            { MessageBox.Show(this, "配置object tableは220×20=4400 bytesである必要があります。", "形式エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using var objectEditor = new FixedRoomObjectEditorForm(bytes);
            if (objectEditor.ShowDialog(this) == DialogResult.OK && objectEditor.EditedBytes is { } edited)
            {
                try { project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, edited, entry.FileName, entry.Sha256); RefreshList(); }
                catch (Exception ex) { ShowFailure(ex); }
            }
            return;
        }
        Form? editor = editorKind switch
        {
            ProjectBundleEditorKind.LegacyIni =>
                new LegacyIniForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.ini", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.MonsterSpawnTable =>
                new MonsterSpawnTableForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.FloorData =>
                new FloorDataEditorForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.ItemCategoryRates =>
                new ItemCategoryRateForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_category_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.IndividualItemRates =>
                new ItemIndividualRateForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_individual_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.MonsterStatus =>
                new MonsterStatusEditorForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.MonsterGrowth =>
                new MonsterGrowthTableForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.FixedRoomMap =>
                new FixedRoomMapForm(path,
                    bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.room.json", entry.Category, bytes, entry.FileName, entry.Sha256),
                    bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited.bin", entry.Category, bytes, entry.FileName, entry.Sha256)),
            ProjectBundleEditorKind.LegacySnapshot =>
                new LegacyMemorySnapshotForm(path),
            ProjectBundleEditorKind.RawBinary =>
                new LegacyBinaryEditorForm(path, bytes => project.AddAssetBytes(Path.GetFileNameWithoutExtension(entry.FileName) + "_edited" + Path.GetExtension(entry.FileName), entry.Category, bytes, entry.FileName, entry.Sha256)),
            _ => null
        };
        if (editor is null)
        {
            MessageBox.Show(this, "選択ファイルの分類と形式に対応する編集画面がありません。カテゴリを確認してください。", "editor未対応", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (editor) editor.ShowDialog(this);
        RefreshList();
    }

    private void ShowFailure(Exception ex) => MessageBox.Show(this, ex.Message, "プロジェクト操作に失敗しました", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
