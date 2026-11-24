using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class SlimeRingSettings
{
    public int SkillRange { get; set; } = 0;
    public int EnemyThrowItem { get; set; } = 0;
    public int AllyThrowItem1 { get; set; } = 0;
    public int AllyThrowItem2 { get; set; } = 0;
    public int SkillMessage { get; set; } = 0;
    public int SkillRate { get; set; } = 0;
    public int ActionId { get; set; } = -1;
    public bool GanirasuBarrier { get; set; }
    public bool CoromageIonazunAlways { get; set; }

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriPort",
            "slime-ring-settings.json");

    public static SlimeRingSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new SlimeRingSettings();

            string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<SlimeRingSettings>(json)
                ?? new SlimeRingSettings();
        }
        catch
        {
            return new SlimeRingSettings();
        }
    }

    public void Save()
    {
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
    }
}

internal sealed class SlimeRingEditorForm : Form
{
    // MonsterCreatorForm.MonsterNames から取得した実際の配列ID。
    internal static readonly int[] TargetMonsterIds =
    {
        19, 20, 134, 18, 16, 17, 162, 132, 163,
        145, 146, 147, 21, 2, 164, 133, 131
    };

    internal static readonly string[] TargetMonsterNames =
    {
        "スライム", "スライムベス", "バブルスライム", "しびれくらげ",
        "ホイミスライム", "ベホマスライム", "キングスライム",
        "スライムエンペラー", "スライムベホマズン", "ドラゴスライム",
        "スライムブレス", "ドラゴメタル", "メタルスライム",
        "はぐれメタル", "メタルキング", "プラチナキング",
        "ゴールデンスライム"
    };

    private static readonly string[] SkillRangeNames =
    {
        "なし", "隣接", "直線投げ", "暗闇から直線投げ",
        "一マス以内投げ", "直線＋仲間振り", "部屋内", "フロア内"
    };

    private static readonly string[] SkillMessageNames =
    {
        "特技メッセージ無し",
        "アルテマソード", "ギガデイン", "持っている武器を思いきり投げつけた",
        "なぎはらい", "ムーンサルト", "仲間を吐こうとした", "強い光を出した",
        "なめまわした", "はげしく攻撃", "剣にちからをこめた",
        "○○を振った！", "○○を振った！2", "吹き飛ばすほどの攻撃",
        "マホトーン", "口をふさいだ", "口をふさいできた",
        "思いっきり斧を振った", "メガザル", "○○を振った！3",
        "○○を振った！4", "バシルーラ", "ハラヘリ攻撃", "さそう踊り",
        "不思議な踊り", "催眠", "ラリホー", "毒胞子攻撃", "アイテム弾き",
        "アイテム弾き2", "装備弾き", "炎ブレス", "くさった液", "アストロン",
        "にらみつけた", "凍える息", "仲間を呼んだ！", "マダンテ",
        "しびれ攻撃", "ベホマ", "ホイミ", "変な呪文"
    };

    private readonly SlimeRingSettings _settings;
    private readonly ComboBox _skillRange = CreateCombo();
    private readonly ComboBox _enemyThrowItem = CreateCombo();
    private readonly ComboBox _allyThrowItem1 = CreateCombo();
    private readonly ComboBox _allyThrowItem2 = CreateCombo();
    private readonly ComboBox _skillMessage = CreateCombo();
    private readonly ComboBox _actionChange = CreateCombo();
    private readonly NumericUpDown _skillRate = new NumericUpDown();
    private readonly CheckBox _ganirasuBarrier = new CheckBox();
    private readonly CheckBox _ionazunAlways = new CheckBox();
    private readonly Label _status = new Label();

    public SlimeRingEditorForm()
    {
        _settings = SlimeRingSettings.Load();

        Text = "スライムの指輪編集";
        StartPosition = FormStartPosition.CenterParent;
        Width = 850;
        Height = 760;
        MinimumSize = new Size(700, 600);
        Font = new Font("Yu Gothic UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

        var intro = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "スライムの指輪を装備している対象モンスターに適用する専用設定です。\r\n" +
                "対象は下記17種に固定されています。通常のモンスタークリエイター設定とは別に保存します。",
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(intro, 0, 0);

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4, 8, 4, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _skillRange.Items.AddRange(SkillRangeNames);
        _enemyThrowItem.Items.AddRange(MonsterSkillData.ThrowItemChoices);
        _allyThrowItem1.Items.AddRange(MonsterSkillData.ThrowItemChoices);
        _allyThrowItem2.Items.AddRange(MonsterSkillData.ThrowItemChoices);
        _skillMessage.Items.AddRange(SkillMessageNames);

        _actionChange.Items.Add("（特技変更なし）");
        foreach (var action in ActionAutoRestoreService.ActionChoices)
            _actionChange.Items.Add("0x" + action.Id.ToString("X2") + "  " + action.Name);

        _skillRate.Minimum = 0;
        _skillRate.Maximum = byte.MaxValue;
        _skillRate.Width = 150;

        AddRow(grid, "特技使用範囲", _skillRange);
        AddRow(grid, "敵に投げるアイテム", _enemyThrowItem);
        AddRow(grid, "味方に投げるアイテム1", _allyThrowItem1);
        AddRow(grid, "味方に投げるアイテム2", _allyThrowItem2);
        AddRow(grid, "特技メッセージ", _skillMessage);
        AddRow(grid, "特技使用率（Aのみ）", _skillRate);
        AddRow(grid, "特技変更", _actionChange);

        _ganirasuBarrier.Text = "ガニラスバリアを所持する";
        _ganirasuBarrier.AutoSize = true;
        _ganirasuBarrier.Margin = new Padding(4, 12, 4, 12);
        AddRow(grid, "追加効果", _ganirasuBarrier);

        _ionazunAlways.Text = "コロマージのイオナズンを確定で唱える（回数制限なし）";
        _ionazunAlways.AutoSize = true;
        _ionazunAlways.Margin = new Padding(4, 12, 4, 12);
        AddRow(grid, "コロマージ専用", _ionazunAlways);

        var targetText = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 170,
            Dock = DockStyle.Fill,
            Text = string.Join("、", TargetMonsterNames)
        };
        AddRow(grid, "適用対象（17種固定）", targetText);

        _skillRange.SelectedIndex = ClampIndex(_settings.SkillRange, _skillRange.Items.Count);
        _enemyThrowItem.SelectedIndex = ClampIndex(_settings.EnemyThrowItem, _enemyThrowItem.Items.Count);
        _allyThrowItem1.SelectedIndex = ClampIndex(_settings.AllyThrowItem1, _allyThrowItem1.Items.Count);
        _allyThrowItem2.SelectedIndex = ClampIndex(_settings.AllyThrowItem2, _allyThrowItem2.Items.Count);
        _skillMessage.SelectedIndex = ClampIndex(_settings.SkillMessage, _skillMessage.Items.Count);
        _skillRate.Value = Math.Max(0, Math.Min(byte.MaxValue, _settings.SkillRate));
        _ganirasuBarrier.Checked = _settings.GanirasuBarrier;
        _ionazunAlways.Checked = _settings.CoromageIonazunAlways;

        int actionIndex = 0;
        if (_settings.ActionId >= 0)
        {
            for (int i = 0; i < ActionAutoRestoreService.ActionChoices.Length; i++)
            {
                if (ActionAutoRestoreService.ActionChoices[i].Id == _settings.ActionId)
                {
                    actionIndex = i + 1;
                    break;
                }
            }
        }
        _actionChange.SelectedIndex = actionIndex;

        content.Controls.Add(grid);
        root.Controls.Add(content, 0, 1);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "設定はまだ保存されていません。";
        bottom.Controls.Add(_status, 0, 0);
        bottom.SetColumnSpan(_status, 2);

        var saveButton = new Button
        {
            Text = "保存して閉じる",
            Width = 150,
            Height = 34,
            Anchor = AnchorStyles.Right
        };
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "キャンセル",
            Width = 100,
            Height = 34
        };
        cancelButton.Click += (_, _) => Close();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(saveButton);
        bottom.Controls.Add(buttonPanel, 1, 1);

        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
    }

    private static ComboBox CreateCombo()
    {
        return new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            DropDownHeight = 420
        };
    }

    private static int ClampIndex(int value, int count)
    {
        if (count <= 0)
            return -1;
        if (value < 0 || value >= count)
            return 0;
        return value;
    }

    private static void AddRow(TableLayoutPanel grid, string title, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 8, 4, 8),
            AutoSize = true
        };

        control.Margin = new Padding(4, 6, 4, 6);
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void SaveAndClose()
    {
        if (_skillRange.SelectedIndex < 0 ||
            _enemyThrowItem.SelectedIndex < 0 ||
            _allyThrowItem1.SelectedIndex < 0 ||
            _allyThrowItem2.SelectedIndex < 0 ||
            _skillMessage.SelectedIndex < 0 ||
            _actionChange.SelectedIndex < 0)
        {
            MessageBox.Show(
                this,
                "すべての選択項目を確認してください。",
                "入力確認",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _settings.SkillRange = _skillRange.SelectedIndex;
        _settings.EnemyThrowItem = _enemyThrowItem.SelectedIndex;
        _settings.AllyThrowItem1 = _allyThrowItem1.SelectedIndex;
        _settings.AllyThrowItem2 = _allyThrowItem2.SelectedIndex;
        _settings.SkillMessage = _skillMessage.SelectedIndex;
        _settings.SkillRate = decimal.ToInt32(_skillRate.Value);
        _settings.ActionId = _actionChange.SelectedIndex == 0
            ? -1
            : ActionAutoRestoreService.ActionChoices[_actionChange.SelectedIndex - 1].Id;
        _settings.GanirasuBarrier = _ganirasuBarrier.Checked;
        _settings.CoromageIonazunAlways = _ionazunAlways.Checked;

        try
        {
            _settings.Save();
            _status.Text = "保存しました: " + SlimeRingSettings.SettingsPath;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "設定の保存に失敗しました。\r\n" + ex,
                "保存エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
