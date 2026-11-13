using System.Text.Json;

namespace TornekoIjiriNext;

internal sealed record TempoPatchProfile
{
    public int FormatVersion { get; init; } = 1;
    public bool PatchEnabled { get; init; }
    public bool OtherActionsFastest { get; init; }
    public bool MonsterActionsFastest { get; init; }
    public bool ShowTurnsInAdventureResult { get; init; }
    public bool AutoSaveSettings { get; init; }
    public string Notes { get; init; } = "";
}

/// <summary>
/// Offline editor for the intent/settings exposed by the legacy tempo patch UI.
/// It deliberately does not contain game addresses or a PINE write path.
/// </summary>
internal sealed class TempoPatchProfileForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly CheckBox _enabled = new() { Text = "テンポ改善パッチを有効にする", AutoSize = true };
    private readonly CheckBox _otherFastest = new() { Text = "その他（壷入れ等）を最速設定にする", AutoSize = true };
    private readonly CheckBox _monsterFastest = new() { Text = "モンスター関連を最速設定にする", AutoSize = true };
    private readonly CheckBox _showTurns = new() { Text = "冒険結果にターン数を表示する", AutoSize = true };
    private readonly CheckBox _autoSave = new() { Text = "設定を自動セーブする", AutoSize = true };
    private readonly TextBox _notes = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 110 };

    public TempoPatchProfileForm()
    {
        Text = "テンポ改善パッチ設定（オフライン）";
        MinimumSize = new Size(650, 520);
        Size = new Size(760, 590);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Yu Gothic UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "旧版 start.hsp のテンポ改善パッチ画面にあった設定項目を、ローカルの設定プロファイルとして管理します。",
            AutoSize = true,
            MaximumSize = new Size(680, 0)
        }, 0, 0);

        var patchGroup = new GroupBox { Text = "パッチ設定", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var patchFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        patchFlow.Controls.Add(_enabled);
        patchFlow.Controls.Add(_otherFastest);
        patchFlow.Controls.Add(_monsterFastest);
        patchGroup.Controls.Add(patchFlow);
        root.Controls.Add(patchGroup, 0, 1);

        var appGroup = new GroupBox { Text = "アプリ側の補助設定", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        var appFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        appFlow.Controls.Add(_showTurns);
        appFlow.Controls.Add(_autoSave);
        appGroup.Controls.Add(appFlow);
        root.Controls.Add(appGroup, 0, 2);

        var notesGroup = new GroupBox { Text = "メモ（旧版の値・対応関係など）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        notesGroup.Controls.Add(_notes);
        root.Controls.Add(notesGroup, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        var fastest = new Button { Text = "全部 最速設定", AutoSize = true };
        fastest.Click += (_, _) => { _otherFastest.Checked = true; _monsterFastest.Checked = true; };
        var load = new Button { Text = "設定 LOAD", AutoSize = true };
        load.Click += (_, _) => LoadProfile();
        var save = new Button { Text = "設定 SAVE", AutoSize = true };
        save.Click += (_, _) => SaveProfile();
        var close = new Button { Text = "閉じる", AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange([fastest, load, save, close]);
        root.Controls.Add(buttons, 0, 4);

        var notice = new Label
        {
            Text = "注意：この画面は設定の編集・JSON保存のみを行います。ゲームメモリの読み書き、パッチ適用、アドレス推定は行いません。旧版の個別待機値や版別アドレス対応は未確定のため、最速設定は意図を記録するフラグとして保存されます。",
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            ForeColor = Color.DarkGoldenrod,
            Padding = new Padding(0, 8, 0, 0)
        };
        root.Controls.Add(notice, 0, 4);
        buttons.Controls.SetChildIndex(notice, 0);
    }

    private TempoPatchProfile ReadForm() => new()
    {
        PatchEnabled = _enabled.Checked,
        OtherActionsFastest = _otherFastest.Checked,
        MonsterActionsFastest = _monsterFastest.Checked,
        ShowTurnsInAdventureResult = _showTurns.Checked,
        AutoSaveSettings = _autoSave.Checked,
        Notes = _notes.Text
    };

    private void ApplyProfile(TempoPatchProfile profile)
    {
        _enabled.Checked = profile.PatchEnabled;
        _otherFastest.Checked = profile.OtherActionsFastest;
        _monsterFastest.Checked = profile.MonsterActionsFastest;
        _showTurns.Checked = profile.ShowTurnsInAdventureResult;
        _autoSave.Checked = profile.AutoSaveSettings;
        _notes.Text = profile.Notes;
    }

    private void SaveProfile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "テンポ改善設定プロファイルを保存",
            Filter = "テンポ改善設定 (*.tempo.json)|*.tempo.json|JSONファイル (*.json)|*.json",
            DefaultExt = "tempo.json",
            AddExtension = true,
            FileName = "torneko-tempo.tempo.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var json = JsonSerializer.Serialize(ReadForm(), JsonOptions);
            File.WriteAllText(dialog.FileName, json, new System.Text.UTF8Encoding(false));
            MessageBox.Show(this, "設定プロファイルを保存しました。ゲームには適用されていません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存に失敗しました。\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "テンポ改善設定プロファイルを開く",
            Filter = "テンポ改善設定 (*.tempo.json;*.json)|*.tempo.json;*.json|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var profile = JsonSerializer.Deserialize<TempoPatchProfile>(json)
                ?? throw new InvalidDataException("設定ファイルが空、またはJSONとして認識できません。");
            if (profile.FormatVersion != 1)
                throw new InvalidDataException($"未対応の形式バージョンです: {profile.FormatVersion}");
            ApplyProfile(profile);
            MessageBox.Show(this, "設定プロファイルを読み込みました。ゲームには適用されていません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"読み込みに失敗しました。\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
