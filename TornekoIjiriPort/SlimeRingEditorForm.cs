using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class SlimeRingMonsterSettings
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

    public SlimeRingMonsterSettings Clone()
    {
        return new SlimeRingMonsterSettings
        {
            SkillRange = SkillRange,
            EnemyThrowItem = EnemyThrowItem,
            AllyThrowItem1 = AllyThrowItem1,
            AllyThrowItem2 = AllyThrowItem2,
            SkillMessage = SkillMessage,
            SkillRate = SkillRate,
            ActionId = ActionId,
            GanirasuBarrier = GanirasuBarrier,
            CoromageIonazunAlways = CoromageIonazunAlways
        };
    }
}

internal sealed class SlimeRingSettings
{
    public Dictionary<int, SlimeRingMonsterSettings> Monsters { get; set; } = new();

    // 旧バージョンの単一設定ファイル読み込み用。新規保存時にも互換性のため残す。
    public int SkillRange { get; set; } = 0;
    public int EnemyThrowItem { get; set; } = 0;
    public int AllyThrowItem1 { get; set; } = 0;
    public int AllyThrowItem2 { get; set; } = 0;
    public int SkillMessage { get; set; } = 0;
    public int SkillRate { get; set; } = 0;
    public int ActionId { get; set; } = -1;
    public bool GanirasuBarrier { get; set; }
    public bool CoromageIonazunAlways { get; set; }

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TornekoIjiriPort",
        "slime-ring-settings.json");

    public static SlimeRingSettings Load(IEnumerable<int> targetIds)
    {
        SlimeRingSettings result;
        try
        {
            if (!File.Exists(SettingsPath))
                result = new SlimeRingSettings();
            else
            {
                string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                result = JsonSerializer.Deserialize<SlimeRingSettings>(json)
                    ?? new SlimeRingSettings();
            }
        }
        catch
        {
            result = new SlimeRingSettings();
        }

        result.NormalizeTargetMonsters(targetIds);
        return result;
    }

    private void NormalizeTargetMonsters(IEnumerable<int> targetIds)
    {
        Monsters ??= new Dictionary<int, SlimeRingMonsterSettings>();

        // 旧形式（全モンスター共通の設定）しかない場合は、従来の設定を17種すべてへ引き継ぐ。
        if (Monsters.Count == 0)
        {
            SlimeRingMonsterSettings legacy = CreateLegacySettings();
            foreach (int id in targetIds)
                Monsters[id] = legacy.Clone();
        }

        foreach (int id in targetIds)
        {
            if (!Monsters.TryGetValue(id, out SlimeRingMonsterSettings? settings) ||
                settings == null)
            {
                Monsters[id] = new SlimeRingMonsterSettings();
            }
        }
    }

    // .Torneko4への保存用。現在のローカル設定を正規化してJSON化する。
    public static string ExportJson()
    {
        SlimeRingSettings settings =
            Load(SlimeRingEditorForm.TargetMonsterIds);

        return JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions { WriteIndented = false });
    }

    // .Torneko4からの復元用。壊れたJSONを空設定として黙って扱わず、呼び出し元へ通知する。
    public static void ImportJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("スライムの指輪設定JSONが空です。");

        SlimeRingSettings imported;
        try
        {
            imported = JsonSerializer.Deserialize<SlimeRingSettings>(json)
                ?? throw new InvalidDataException(
                    "スライムの指輪設定JSONを読み取れませんでした。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "スライムの指輪設定JSONの形式が不正です。", ex);
        }

        imported.NormalizeTargetMonsters(
            SlimeRingEditorForm.TargetMonsterIds);
        imported.Save();
    }

    private SlimeRingMonsterSettings CreateLegacySettings()
    {
        return new SlimeRingMonsterSettings
        {
            SkillRange = SkillRange,
            EnemyThrowItem = EnemyThrowItem,
            AllyThrowItem1 = AllyThrowItem1,
            AllyThrowItem2 = AllyThrowItem2,
            SkillMessage = SkillMessage,
            SkillRate = SkillRate,
            ActionId = ActionId,
            GanirasuBarrier = GanirasuBarrier,
            CoromageIonazunAlways = CoromageIonazunAlways
        };
    }

    public SlimeRingMonsterSettings GetMonster(int monsterId)
    {
        if (!Monsters.TryGetValue(monsterId, out var value) || value == null)
        {
            value = new SlimeRingMonsterSettings();
            Monsters[monsterId] = value;
        }
        return value;
    }

    public void SetMonster(int monsterId, SlimeRingMonsterSettings value)
    {
        Monsters[monsterId] = value.Clone();
    }

    public void Save()
    {
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
    }
}

internal sealed class SlimeRingEditorForm : Form
{
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
    private readonly ListBox _monsterList = new ListBox();
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
    private readonly Label _selectedMonsterLabel = new Label();
    private int _selectedIndex = -1;
    private bool _loadingControls;

    public SlimeRingEditorForm()
    {
        _settings = SlimeRingSettings.Load(TargetMonsterIds);

        Text = "スライムの指輪編集";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1080;
        Height = 760;
        MinimumSize = new Size(900, 620);
        Font = new Font("Yu Gothic UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));

        var intro = new Label
        {
            Dock = DockStyle.Fill,
            Text = "左側でモンスターを選択し、右側で個別設定します。モンスターごとに設定は独立して保存されます。",
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(intro, 0, 0);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 10, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var listTitle = new Label
        {
            Text = "対象モンスター（17種）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        };
        _monsterList.Dock = DockStyle.Fill;
        _monsterList.IntegralHeight = false;
        _monsterList.Items.AddRange(TargetMonsterNames);
        _monsterList.SelectedIndexChanged += (_, _) => SelectMonster(_monsterList.SelectedIndex);
        left.Controls.Add(listTitle, 0, 0);
        left.Controls.Add(_monsterList, 0, 1);
        main.Controls.Add(left, 0, 0);

        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 4, 0) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4, 4, 4, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _selectedMonsterLabel.Text = "モンスターを選択してください";
        _selectedMonsterLabel.Dock = DockStyle.Fill;
        _selectedMonsterLabel.Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
        _selectedMonsterLabel.Height = 42;
        AddRow(grid, "設定対象", _selectedMonsterLabel);

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
        AddRow(grid, "追加効果", _ganirasuBarrier);

        _ionazunAlways.Text = "コロマージのイオナズンを確定で唱える（回数制限なし）";
        _ionazunAlways.AutoSize = true;
        AddRow(grid, "コロマージ専用", _ionazunAlways);

        right.Controls.Add(grid);
        main.Controls.Add(right, 1, 0);
        root.Controls.Add(main, 0, 1);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "設定を編集して「保存」を押してください。";
        bottom.Controls.Add(_status, 0, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var closeButton = new Button { Text = "閉じる", Width = 90, Height = 34 };
        closeButton.Click += (_, _) => Close();
        var saveButton = new Button { Text = "全モンスター設定を保存", Width = 190, Height = 34 };
        saveButton.Click += (_, _) => SaveSettings();
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(saveButton);
        bottom.Controls.Add(buttonPanel, 1, 0);
        root.Controls.Add(bottom, 0, 2);

        Controls.Add(root);
        _monsterList.SelectedIndex = 0;
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

    private void SelectMonster(int index)
    {
        if (index < 0 || index >= TargetMonsterIds.Length)
            return;

        // 切り替え前の画面内容を、選択中モンスターの設定として退避する。
        if (_selectedIndex >= 0)
            StoreCurrentControls();

        _selectedIndex = index;
        int monsterId = TargetMonsterIds[index];
        _selectedMonsterLabel.Text = TargetMonsterNames[index] + "  (ID " + monsterId + ")";
        LoadControls(_settings.GetMonster(monsterId));
        _status.Text = TargetMonsterNames[index] + " の個別設定を編集中です。";
    }

    private void LoadControls(SlimeRingMonsterSettings settings)
    {
        _loadingControls = true;
        try
        {
            _skillRange.SelectedIndex = ClampIndex(settings.SkillRange, _skillRange.Items.Count);
            _enemyThrowItem.SelectedIndex = ClampIndex(settings.EnemyThrowItem, _enemyThrowItem.Items.Count);
            _allyThrowItem1.SelectedIndex = ClampIndex(settings.AllyThrowItem1, _allyThrowItem1.Items.Count);
            _allyThrowItem2.SelectedIndex = ClampIndex(settings.AllyThrowItem2, _allyThrowItem2.Items.Count);
            _skillMessage.SelectedIndex = ClampIndex(settings.SkillMessage, _skillMessage.Items.Count);
            _skillRate.Value = Math.Max(_skillRate.Minimum, Math.Min(_skillRate.Maximum, settings.SkillRate));
            _ganirasuBarrier.Checked = settings.GanirasuBarrier;
            _ionazunAlways.Checked = settings.CoromageIonazunAlways;

            int actionIndex = 0;
            if (settings.ActionId >= 0)
            {
                for (int i = 0; i < ActionAutoRestoreService.ActionChoices.Length; i++)
                {
                    if (ActionAutoRestoreService.ActionChoices[i].Id == settings.ActionId)
                    {
                        actionIndex = i + 1;
                        break;
                    }
                }
            }
            _actionChange.SelectedIndex = actionIndex;
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private void StoreCurrentControls()
    {
        if (_loadingControls || _selectedIndex < 0)
            return;

        var settings = new SlimeRingMonsterSettings
        {
            SkillRange = Math.Max(0, _skillRange.SelectedIndex),
            EnemyThrowItem = Math.Max(0, _enemyThrowItem.SelectedIndex),
            AllyThrowItem1 = Math.Max(0, _allyThrowItem1.SelectedIndex),
            AllyThrowItem2 = Math.Max(0, _allyThrowItem2.SelectedIndex),
            SkillMessage = Math.Max(0, _skillMessage.SelectedIndex),
            SkillRate = decimal.ToInt32(_skillRate.Value),
            ActionId = _actionChange.SelectedIndex <= 0
                ? -1
                : ActionAutoRestoreService.ActionChoices[_actionChange.SelectedIndex - 1].Id,
            GanirasuBarrier = _ganirasuBarrier.Checked,
            CoromageIonazunAlways = _ionazunAlways.Checked
        };
        _settings.SetMonster(TargetMonsterIds[_selectedIndex], settings);
    }

    private void SaveSettings()
    {
        try
        {
            StoreCurrentControls();
            _settings.Save();
            _status.Text = "17種の個別設定を保存しました: " + SlimeRingSettings.SettingsPath;
            MessageBox.Show(this, "17種それぞれの設定を保存しました。", "保存完了",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "設定の保存に失敗しました。\r\n" + ex,
                "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int ClampIndex(int value, int count)
    {
        if (count <= 0)
            return -1;
        if (value < 0 || value >= count)
            return 0;
        return value;
    }
}
