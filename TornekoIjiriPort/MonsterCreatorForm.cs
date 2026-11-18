using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class MonsterCreatorForm : Form
{
    private static readonly string[] MonsterNames = @"
スモールグール
ベロベロ
はぐれメタル
イエティ
ビッグスロース
マドハンド
ブラッドハンド
はねせんにん
ハエまどう
リリパット
アローインプ
どくやずきん
ドッグスナイパー
ゴーレム
ストーンマン
ゴールドマン
ホイミスライム
ベホマスライム
しびれくらげ
スライム
スライムベス
メタルスライム
？鎧モンスター
オニオーン
たまねぎマン
エビルポット
ようがんまじん
ひょうがまじん
ゆうれい
しにがみ
シャーマン
ゾンビマスター
おおめだま
スペクテット
トロル
トロルボンバー
トロルキング
くさった死体
リビングデッド
グール
どくどくゾンビ
ドラゴン
キースドラゴン
ダースドラゴン
がいこつけんし
しりょうのきし
かげのきし
シルバーデビル
バズズ
デビルロード
笑いぶくろ
おばけキノコ
マタンゴ
マージマタンゴ
まどうし
だいまどう
ベビーサタン
ミニデーモン
つかいま
ミステリドール
いしにんぎょう
おどる宝石
どろにんぎょう
パペットマン
ミミック
ひとくいばこ
バーサーカー
ドラキー
タホドラキー
ドラキーマ
シャドー
あやしいかげ
？姿ｽﾗｲﾑ(フレイム)
？姿ｽﾗｲﾑ(ブﾘザｰド)
ダンスキャロット
マンドラゴラ
テンツク
スーパーテンツク
ラストテンツク
いたずらもぐら
キラースコップ
キラーマシン
メタルハンター
きめんどうし
げんじゅつし
ドルイド
あくましんかん
じごくのつかい
うごくせきぞう
だいまじん
ばくだん岩
メガザルロック
？マネマネ(CG無ﾊﾝｸﾞ)
さつじんき
エリミネーター
デスストーカー
ももんじゃ
メイジももんじゃ
おおナメクジ
ミイラおとこ
マミー
キメラ
スターキメラ
メイジキメラ
さまようよろい
キラーアーマー
じごくのよろい
ギガンテス
アトラス
アークデーモン
ベリアル
おおきづち
ブラウニー
ガニラス
じごくのハサミ
ぐんたいガニ
ゴースト
メトロゴースト
ヘルゴースト
アイアンアント
ぐんたいアリ
ラリホーアント
きとうし
ようじゅつし
ファーラット
ケダモン
アイアンタートル
ランドアーマー
ガーゴイル
モシャスナイト
シャドーナイト
ゴールデンスライム
スライムエンペラー
プラチナキング
バブルスライム
？ダークアーマー
？姿ﾆｾﾄﾙ(ポポロ)
ロサ
イネス
ちゅうまじゅう
ストローマウス
あめふらし
はりせんもぐら
サンダーラット
プラズママウス
ドラゴスライム
スライムブレス
ドラゴメタル
バブリン
プヨンターゲット
ポムポムボム
リビングハンマー
キラースター
デビルアンカー
さそりかまきり
キラーマンティス
メダパニシックル
タマゴロン
ワンダーエッグ
スカイフロッグ
ファイヤーケロッグ
デーモントード
キングスライム
スライムベホマズン
メタルキング
レノファイター
グレイトホーン
？ボルンガ(ｸﾞﾚﾎ)
フライングデビル
ランガー
どぐう戦士
キラープラスター
こうてつまじん
エビルエスターク
デスマシーン
つのうしがい
しびれマイマイ
あんこくつむり
ベビーニュート
ドラゴンキッズ
メラリザード
おばけヒトデ
マージスター
タップペンギー
岩とびあくま
グレイトマーマン
？ジｬスティス別ver
ジャスティス兄
コロヒーロー
コロファイター
コロマージ
コロプリースト
プチヒーロー
プチファイター
プチマージ
プチプリースト
ヘルジャスティス
？鎧ﾓﾝｽﾀｰ別ver
ニセトル
".Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private const int MonsterRecordSize = 24;
    private const int MonsterRecordCount = 199;
    private const int RecruitModifierDataOffset = 365123;
    private const int RecoveryConstantDataOffset = 364595;
    private const int SkillVersionOffset = 355056;
    private const int SkillRangeDataOffset = 382206;
    private const int SkillAllyItemDataOffset = 386212;
    private const int SkillMessageCount = 41;
    private const int SkillExtensionRequiredEnd = 404356;
    private const uint EeAddressMask = 0x1FFFFFFF;
    private const uint EeMemoryBaseAddress = 0x00180000;
    private const uint SkillMessagePointerBase = EeMemoryBaseAddress + 0x009126D0;

    private readonly byte[]? _bin;
    private readonly Action? _onChanged;
    private readonly TextBox _search = new TextBox();
    private readonly ListBox _list = new ListBox();
    private readonly Label _count = new Label();
    private readonly Label _selectedTitle = new Label();
    private readonly TextBox _selectedInfo = new TextBox();
    private readonly Label _status = new Label();
    private readonly NumericUpDown _hp = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _attack = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _defense = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _experience = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _maxLevel = CreateNumber(byte.MaxValue);
    private static readonly string[] GrowthTypeNames =
    {
        "トルネコ", "ポポロ", "イネス", "ロサ", "万能晩成", "攻撃特殊",
        "防御晩成", "防御早熟", "攻撃晩成", "攻撃早熟", "守備特殊", "普通早熟"
    };

    private static readonly string[] MoveSpeedNames =
    {
        "鈍足", "等速", "倍速一回攻撃", "倍速二回攻撃",
        "未使用4", "未使用5", "未使用6", "未使用7", "未使用8"
    };

    private static readonly string[] SystemFamilyNames =
    {
        "悪魔", "人形", "爆弾", "炎", "飛行", "スライム", "水", "ゾンビ"
    };

    private static readonly string[] SkillFamilyNames =
    {
        "魔法", "技", "剣", "影", "杖"
    };

    private readonly ComboBox _growthType = CreateChoice(GrowthTypeNames);
    private readonly ComboBox _moveSpeed = CreateChoice(MoveSpeedNames);
    private readonly CheckBox[] _systemChecks = CreateChecks(SystemFamilyNames);
    private readonly CheckBox[] _skillChecks = CreateChecks(SkillFamilyNames);
    private readonly NumericUpDown _dropRate = CreateNumber(byte.MaxValue);
    private readonly ComboBox _dropItem = CreateChoice(MonsterSkillData.DropItemChoices);
    private readonly NumericUpDown _sleepRate = CreateNumber(byte.MaxValue);
    private readonly NumericUpDown _skillRate = CreateNumber(byte.MaxValue);
    private readonly NumericUpDown _recruitModifier = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _recoveryConstant = CreateNumber(ushort.MaxValue);

    private static readonly string[] SkillRangeNames =
    {
        "なし",
        "隣接",
        "直線投げ",
        "暗闇から直線投げ",
        "一マス以内投げ",
        "直線＋仲間振り",
        "部屋内",
        "フロア内"
    };

    private readonly ComboBox _skillRange = CreateChoice(SkillRangeNames);
    private readonly ComboBox _enemyThrowItem = CreateChoice(MonsterSkillData.ThrowItemChoices);
    private readonly ComboBox _allyThrowItem1 = CreateChoice(MonsterSkillData.ThrowItemChoices);
    private readonly ComboBox _allyThrowItem2 = CreateChoice(MonsterSkillData.ThrowItemChoices);
    private readonly ComboBox _skillMessage = CreateChoice(new[] { "特技メッセージ無し" });
    private bool _skillDataReady;
    private readonly Button _applyButton = new Button();
    private readonly List<int> _visibleIds = new List<int>();

    private int _recordTableOffset = -1;
    private int _selectedMonsterId = -1;
    private bool _dataReady;
    private bool _loadingSelection;
    private bool _automaticLiveApply;

    public MonsterCreatorForm() : this(null, null)
    {
    }

    public MonsterCreatorForm(byte[]? bin, Action? onChanged)
    {
        _bin = bin;
        _onChanged = onChanged;

        Text = "モンスタークリエイター";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1050;
        Height = 650;
        MinimumSize = new Size(850, 560);
        Font = new Font("Yu Gothic UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchLabel = new Label
        {
            Text = "モンスター名・番号で検索",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "例：ドラゴン / 042";
        _search.TextChanged += (_, _) => RefreshList();

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.Font = new Font("Yu Gothic UI", 10);
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();

        left.Controls.Add(searchLabel, 0, 0);
        left.Controls.Add(_search, 0, 1);
        left.Controls.Add(_list, 0, 2);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20, 8, 8, 8)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));

        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Font = new Font("Yu Gothic UI", 10, FontStyle.Bold);

        _selectedTitle.Dock = DockStyle.Fill;
        _selectedTitle.TextAlign = ContentAlignment.MiddleLeft;
        _selectedTitle.Font = new Font("Yu Gothic UI", 18, FontStyle.Bold);

        _selectedInfo.Dock = DockStyle.Fill;
        _selectedInfo.Multiline = true;
        _selectedInfo.ReadOnly = true;
        _selectedInfo.ScrollBars = ScrollBars.Vertical;
        _selectedInfo.WordWrap = true;
        _selectedInfo.Font = new Font("Yu Gothic UI", 9);

        var fieldGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 13,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.Controls.Add(CreateField("HP", _hp), 0, 0);
        fieldGrid.Controls.Add(CreateField("攻撃力", _attack), 1, 0);
        fieldGrid.Controls.Add(CreateField("防御力", _defense), 0, 1);
        fieldGrid.Controls.Add(CreateField("経験値", _experience), 1, 1);
        fieldGrid.Controls.Add(CreateField("最大レベル", _maxLevel), 0, 2);
        fieldGrid.Controls.Add(CreateField("成長タイプ", _growthType), 1, 2);
        fieldGrid.Controls.Add(CreateCheckGroup("系統", _systemChecks), 0, 3);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);
        fieldGrid.Controls.Add(CreateCheckGroup("特技系統", _skillChecks), 0, 4);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);
        fieldGrid.Controls.Add(CreateField("移動速度", _moveSpeed), 0, 5);
        fieldGrid.Controls.Add(CreateField("ドロップ率（%）", _dropRate), 1, 5);
        fieldGrid.Controls.Add(CreateField("ドロップアイテム（名前選択）", _dropItem), 0, 6);
        fieldGrid.Controls.Add(CreateField("スポーン時睡眠率（%）", _sleepRate), 1, 6);
        fieldGrid.Controls.Add(CreateField("特技使用範囲", _skillRange), 0, 7);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);
        fieldGrid.Controls.Add(CreateField("敵に投げるアイテム", _enemyThrowItem), 0, 8);
        fieldGrid.Controls.Add(CreateField("味方に投げるアイテム 1", _allyThrowItem1), 1, 8);
        fieldGrid.Controls.Add(CreateField("味方に投げるアイテム 2", _allyThrowItem2), 0, 9);
        fieldGrid.Controls.Add(CreateField("特技メッセージ", _skillMessage), 0, 10);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);
        fieldGrid.Controls.Add(CreateField("勧誘修正値", _recruitModifier), 0, 11);
        fieldGrid.Controls.Add(CreateField("回復定数", _recoveryConstant), 1, 11);
        fieldGrid.Controls.Add(CreateField("特技使用率（A/B共通）", _skillRate), 0, 12);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);

        var explanation = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text =
                "旧版24バイト基本レコードの確認済み項目を編集します。\r\n" +
                "HP(+0)、攻撃力(+2)、防御力(+4)、系統フラグ(+6)、特技系統フラグ(+7)。\r\n" +
                "移動速度(+9)、ドロップ率(+10)、最大レベル(+11)、経験値(+12)、ドロップアイテム(+16)、睡眠率(+18)、成長タイプ(+20)。\r\n" +
                "特技使用率(+22、+23)は両方に同じ値を書き込みます。\r\n" +
                "勧誘修正値(365123)、回復定数(364595)も編集できます。\r\n" +
                "特技範囲・特技メッセージ・投擲アイテム設定はバージョン5拡張領域を編集します。\r\n" +
                "PCSX2接続中は、モンスター選択時にゲーム内の現在値を読み込みます。数値や選択肢を変更すると、その場でゲームメモリへ即時反映します。\r\n" +
                "未接続時はBIN作業データを編集し、「再適用」で反映できます。"
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };
        content.Controls.Add(explanation);
        content.Controls.Add(fieldGrid);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DarkSlateBlue;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _applyButton.Text = "再適用";
        _applyButton.Width = 130;
        _applyButton.Height = 36;
        _applyButton.Click += (_, _) => ApplySelected();

        var reloadButton = new Button
        {
            Text = "再読込",
            Width = 130,
            Height = 36
        };
        reloadButton.Click += (_, _) => UpdateSelection();

        var closeButton = new Button
        {
            Text = "閉じる",
            Width = 100,
            Height = 36
        };
        closeButton.Click += (_, _) => Close();

        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(reloadButton);
        buttons.Controls.Add(closeButton);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(buttons, 0, 1);

        right.Controls.Add(_count, 0, 0);
        right.Controls.Add(_selectedTitle, 0, 1);
        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

        var copyInfoButton = new Button
        {
            Text = "診断コピー",
            Dock = DockStyle.Fill
        };
        copyInfoButton.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_selectedInfo.Text))
            {
                Clipboard.SetText(_selectedInfo.Text);
                MessageBox.Show(this, "診断情報を全文コピーしました。", "コピー完了");
            }
        };

        infoPanel.Controls.Add(_selectedInfo, 0, 0);
        infoPanel.Controls.Add(copyInfoButton, 1, 0);
        right.Controls.Add(infoPanel, 0, 2);
        right.Controls.Add(content, 0, 3);
        right.Controls.Add(bottom, 0, 4);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);

        _dataReady = TryResolveRecordTable(_bin, out _recordTableOffset, out string reason);
        _skillDataReady = TryResolveSkillExtension(_bin);

        if (_skillDataReady && _bin != null)
        {
            _skillMessage.Items.Clear();
            _skillMessage.Items.AddRange(ReadSkillMessageChoices(_bin));
        }
        _applyButton.Enabled = _dataReady;

        if (_dataReady)
        {
            _status.Text =
                "BIN読込済み：基本レコード位置 0x" +
                _recordTableOffset.ToString("X") +
                " ／ 199件・24 bytes";
            if (!_skillDataReady)
                _status.Text += " ／ 特技設定はバージョン5拡張データ未検出";
            else
                _status.Text += " ／ 特技設定データ読込済み";
        }
        else
        {
            _status.Text = reason;
            SetEditorEnabled(false);
        }

        BindLiveEditEvents();
        RefreshList();
    }

    private void BindLiveEditEvents()
    {
        _hp.ValueChanged += (_, _) => ApplyLiveEdit();
        _attack.ValueChanged += (_, _) => ApplyLiveEdit();
        _defense.ValueChanged += (_, _) => ApplyLiveEdit();
        _experience.ValueChanged += (_, _) => ApplyLiveEdit();
        _maxLevel.ValueChanged += (_, _) => ApplyLiveEdit();
        _growthType.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _moveSpeed.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        foreach (var check in _systemChecks)
            check.CheckedChanged += (_, _) => ApplyLiveEdit();
        foreach (var check in _skillChecks)
            check.CheckedChanged += (_, _) => ApplyLiveEdit();
        _dropRate.ValueChanged += (_, _) => ApplyLiveEdit();
        _dropItem.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _sleepRate.ValueChanged += (_, _) => ApplyLiveEdit();
        _skillRate.ValueChanged += (_, _) => ApplyLiveEdit();
        _recruitModifier.ValueChanged += (_, _) => ApplyLiveEdit();
        _recoveryConstant.ValueChanged += (_, _) => ApplyLiveEdit();
        _skillRange.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _enemyThrowItem.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _allyThrowItem1.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _allyThrowItem2.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
        _skillMessage.SelectedIndexChanged += (_, _) => ApplyLiveEdit();
    }

    private void ApplyLiveEdit()
    {
        if (_loadingSelection || _automaticLiveApply ||
            !Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.MemoryLayout == null)
            return;

        _automaticLiveApply = true;
        try
        {
            ApplySelected();
        }
        finally
        {
            _automaticLiveApply = false;
        }
    }

    private static NumericUpDown CreateNumber(decimal maximum)
    {
        return new NumericUpDown
        {
            Minimum = 0,
            Maximum = maximum,
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Width = 135,
            Dock = DockStyle.Fill
        };
    }

    private static ComboBox CreateChoice(string[] items)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = true
        };
        combo.Items.AddRange(items);
        return combo;
    }

    private static CheckBox[] CreateChecks(string[] names)
    {
        var checks = new CheckBox[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            checks[i] = new CheckBox
            {
                Text = names[i],
                AutoSize = true,
                Margin = new Padding(3, 4, 8, 2)
            };
        }
        return checks;
    }

    private static Control CreateCheckGroup(string title, CheckBox[] checks)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4, 2, 4, 2)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        foreach (var check in checks)
            flow.Controls.Add(check);

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(flow, 0, 1);
        return panel;
    }

    private static Control CreateField(string title, Control input)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4, 2, 12, 2)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(input, 0, 1);
        return panel;
    }

    private static bool TryResolveRecordTable(
        byte[]? bin,
        out int tableOffset,
        out string reason)
    {
        tableOffset = -1;
        reason = "BINファイルを読み込むと基本能力値を編集できます。";

        if (bin == null)
            return false;

        if (bin.Length < BinFileService.ExpectedSize)
        {
            reason = "BINサイズが524,288 bytes未満のため編集できません。";
            return false;
        }

        // 旧HSP label_073:
        // var_142 = peek(var_134, 3)
        // var_143 = var_142 * 0x01000000
        // var_149 = DWORD(var_134 + 16) + var_143
        uint rawPointer = BinFileService.ReadUInt32(bin, 16);
        uint correction = (uint)bin[3] << 24;
        uint pointer = unchecked(rawPointer + correction);

        // 旧版のEEアドレス 0x20180000 を物理RAM側へ正規化。
        uint physicalAddress = pointer & EeAddressMask;

        if (physicalAddress < EeMemoryBaseAddress)
        {
            reason = "モンスターデータへのポインタが不正です。BINの種類を確認してください。";
            return false;
        }

        long offset = (long)physicalAddress - EeMemoryBaseAddress;

        // モンスターIDとBINレコードの対応を1レコード分補正

        offset += MonsterRecordSize;
        long requiredEnd = offset + (long)MonsterRecordCount * MonsterRecordSize;

        if (offset < 0 || requiredEnd > bin.Length)
        {
            reason =
                "モンスターデータ領域がBIN内にありません。\r\n" +
                "推定位置：0x" + offset.ToString("X") +
                " ／ 必要長：" + (MonsterRecordCount * MonsterRecordSize) + " bytes";
            return false;
        }

        tableOffset = checked((int)offset);
        reason = "";
        return true;
    }

    private static string[] ReadSkillMessageChoices(byte[] bin)
    {
        var choices = new List<string>(SkillMessageCount + 1);
        choices.Add("特技メッセージ無し");

        for (int i = 0; i < SkillMessageCount; i++)
            choices.Add("メッセージ " + (i + 1).ToString("D2"));

        return choices.ToArray();
    }

    private static int ResolveSkillMessageIndex(uint pointer)
    {
        if (pointer == 0)
            return 0;

        for (int i = 0; i < SkillMessageCount; i++)
        {
            uint expected = SkillMessagePointerBase + (uint)i * 16;
            if (pointer == expected)
                return i + 1;
        }

        return -1;
    }

    private static uint ReadUInt32LE(byte[] data, int offset)
    {
        BinFileService.CheckRange(data, offset, 4);
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }

    private static void WriteUInt32LE(byte[] data, int offset, uint value)
    {
        BinFileService.CheckRange(data, offset, 4);
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static bool TryResolveSkillExtension(byte[]? bin)
    {
        if (bin == null)
            return false;

        if (bin.Length < SkillExtensionRequiredEnd)
            return false;

        // 旧HSPの保存処理では、355056 byte に拡張バージョン5を記録する。
        return bin[SkillVersionOffset] == 5;
    }

    private void SetEditorEnabled(bool enabled)
    {
        _hp.Enabled = enabled;
        _attack.Enabled = enabled;
        _defense.Enabled = enabled;
        _experience.Enabled = enabled;
        _maxLevel.Enabled = enabled;
        _growthType.Enabled = enabled;
        _moveSpeed.Enabled = enabled;
        foreach (var check in _systemChecks) check.Enabled = enabled;
        foreach (var check in _skillChecks) check.Enabled = enabled;
        _dropRate.Enabled = enabled;
        _dropItem.Enabled = enabled;
        _sleepRate.Enabled = enabled;
        _skillRate.Enabled = enabled;
        _recruitModifier.Enabled = enabled;
        _recoveryConstant.Enabled = enabled;
        _skillRange.Enabled = enabled && _skillDataReady;
        _enemyThrowItem.Enabled = enabled && _skillDataReady;
        _allyThrowItem1.Enabled = enabled && _skillDataReady;
        _allyThrowItem2.Enabled = enabled && _skillDataReady;
        _skillMessage.Enabled = enabled && _skillDataReady;
        _applyButton.Enabled = enabled;
    }

    private void RefreshList()
    {
        string query = _search.Text.Trim();

        _list.BeginUpdate();
        _list.Items.Clear();
        _visibleIds.Clear();

        for (int i = 0; i < MonsterNames.Length; i++)
        {
            string id = i.ToString("D3");
            string name = MonsterNames[i];

            bool matches =
                query.Length == 0 ||
                name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                i.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            if (matches)
            {
                _visibleIds.Add(i);
                _list.Items.Add(id + "  " + name);
            }
        }

        _list.EndUpdate();

        _count.Text =
            "登録モンスター：" + MonsterNames.Length +
            "種類　／　検索結果：" + _visibleIds.Count + "種類";

        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        int index = _list.SelectedIndex;

        if (index < 0 || index >= _visibleIds.Count)
        {
            _selectedMonsterId = -1;
            _loadingSelection = false;
            _selectedTitle.Text = "モンスター未選択";
            _selectedInfo.Text = "検索条件に一致するモンスターを選択してください。";
            SetEditorEnabled(false);
            return;
        }

        int id = _visibleIds[index];
        _selectedMonsterId = id;
        _loadingSelection = true;
        _selectedTitle.Text = MonsterNames[id];

        if (!_dataReady || _bin == null)
        {
            _selectedInfo.Text =
                "モンスター番号：" + id.ToString("D3") +
                "\r\n一覧登録名：" + MonsterNames[id] +
                "\r\n\r\n" + _status.Text;
            SetEditorEnabled(false);
            _loadingSelection = false;
            return;
        }

        int offset = checked(_recordTableOffset + id * MonsterRecordSize);
        BinFileService.CheckRange(_bin, offset, MonsterRecordSize);

        int parameterIndex = id + 1;
        int recruitOffset = RecruitModifierDataOffset + parameterIndex * 2;
        int recoveryOffset = RecoveryConstantDataOffset + parameterIndex * 2;

        if (Pcsx2LiveSession.IsConnected &&
            Pcsx2LiveSession.ProcessId > 0 &&
            Pcsx2LiveSession.MemoryLayout != null)
        {
            try
            {
                var requested = new List<Tuple<int, int, string>>
                {
                    Tuple.Create(offset, MonsterRecordSize, "モンスターレコード"),
                    Tuple.Create(recruitOffset, 2, "勧誘補正値"),
                    Tuple.Create(recoveryOffset, 2, "回復定数")
                };

                if (_skillDataReady)
                {
                    int liveRangeOffset = SkillRangeDataOffset + id * 20;
                    int liveAllyOffset = SkillAllyItemDataOffset + id * 8;
                    requested.Add(Tuple.Create(liveRangeOffset, 20, "特技範囲データ"));
                    requested.Add(Tuple.Create(liveAllyOffset, 8, "仲間投げアイテムデータ"));
                }

                var liveData = Pcsx2BinApplier.ReadRanges(
                    Pcsx2LiveSession.ProcessId,
                    Pcsx2LiveSession.MemoryLayout,
                    requested);

                foreach (var item in liveData)
                    Array.Copy(item.Item2, 0, _bin, item.Item1, item.Item2.Length);

                _status.Text = MonsterNames[id] + " の現在値をPCSX2から読み込みました。";
            }
            catch (Exception ex)
            {
                _status.Text = "PCSX2ライブ読み込み失敗。直前の作業データを表示します。 " + ex.Message;
            }
        }

        _hp.Value = BinFileService.ReadUInt16(_bin, offset);
        _attack.Value = BinFileService.ReadUInt16(_bin, offset + 2);
        _defense.Value = BinFileService.ReadUInt16(_bin, offset + 4);
        _maxLevel.Value = _bin[offset + 11];
        _experience.Value = BinFileService.ReadUInt16(_bin, offset + 12);
        byte systemFlags = _bin[offset + 6];
        byte skillFlags = _bin[offset + 7];
        for (int i = 0; i < _systemChecks.Length; i++)
            _systemChecks[i].Checked = (systemFlags & (1 << i)) != 0;
        for (int i = 0; i < _skillChecks.Length; i++)
            _skillChecks[i].Checked = (skillFlags & (1 << i)) != 0;

        int growthValue = _bin[offset + 20];
        _growthType.SelectedIndex = growthValue < _growthType.Items.Count ? growthValue : -1;
        int speedValue = _bin[offset + 9];
        _moveSpeed.SelectedIndex = speedValue < _moveSpeed.Items.Count ? speedValue : -1;
        _dropRate.Value = _bin[offset + 10];
        ushort dropItemValue = BinFileService.ReadUInt16(_bin, offset + 16);
        _dropItem.SelectedIndex = dropItemValue < _dropItem.Items.Count ? dropItemValue : -1;
        _sleepRate.Value = _bin[offset + 18];
        _skillRate.Value = _bin[offset + 22];

        // HSP側のモンスター選択リストは先頭に空欄があるため +1 する。
        BinFileService.CheckRange(_bin, recruitOffset, 2);
        BinFileService.CheckRange(_bin, recoveryOffset, 2);

        _recruitModifier.Value = BinFileService.ReadUInt16(_bin, recruitOffset);
        _recoveryConstant.Value = BinFileService.ReadUInt16(_bin, recoveryOffset);

        string skillDebug = "";

        if (_skillDataReady)
        {
            int rangeOffset = SkillRangeDataOffset + id * 20;
            int allyOffset = SkillAllyItemDataOffset + id * 8;

            ushort rangeValue = BinFileService.ReadUInt16(_bin, rangeOffset + 6);
            uint messagePointer = ReadUInt32LE(_bin, rangeOffset + 10);
            ushort enemyItemValue = BinFileService.ReadUInt16(_bin, rangeOffset + 18);
            ushort allyItemValue1 = BinFileService.ReadUInt16(_bin, allyOffset);
            ushort allyItemValue2 = BinFileService.ReadUInt16(_bin, allyOffset + 2);
            skillDebug =
                "\r\n--- 特技データ診断 ---" +
                "\r\nモンスターID：" + id +
                "\r\n範囲位置：" + rangeOffset +
                " / 生値：" + rangeValue +
                "\r\n敵投擲位置：" + (rangeOffset + 18) +
                " / 生値：" + enemyItemValue +
                "\r\n味方投擲位置：" + allyOffset +
                " / 生値：" + allyItemValue1 +
                "\r\n味方投擲2位置：" + (allyOffset + 2) +
                " / 生値：" + allyItemValue2;

            _skillRange.SelectedIndex = rangeValue < _skillRange.Items.Count ? rangeValue : -1;
            _skillMessage.SelectedIndex = ResolveSkillMessageIndex(messagePointer);
            _enemyThrowItem.SelectedIndex = enemyItemValue < _enemyThrowItem.Items.Count ? enemyItemValue : -1;
            _allyThrowItem1.SelectedIndex = allyItemValue1 < _allyThrowItem1.Items.Count ? allyItemValue1 : -1;
            _allyThrowItem2.SelectedIndex = allyItemValue2 < _allyThrowItem2.Items.Count ? allyItemValue2 : -1;
        }

        _selectedInfo.Text =
            "モンスター番号：" + id.ToString("D3") +
            "　／　BINレコード位置：0x" + offset.ToString("X") +
            "\r\nレコード長：24 bytes　／　未解析領域は変更しません。" +
            skillDebug;

        _loadingSelection = false;
        SetEditorEnabled(true);
    }

    private void ApplySelected()
    {
        if (!_dataReady || _bin == null || _selectedMonsterId < 0)
            return;

        int offset = checked(
            _recordTableOffset + _selectedMonsterId * MonsterRecordSize);

        BinFileService.CheckRange(_bin, offset, MonsterRecordSize);

        BinFileService.WriteUInt16(_bin, offset, (ushort)_hp.Value);
        BinFileService.WriteUInt16(_bin, offset + 2, (ushort)_attack.Value);
        BinFileService.WriteUInt16(_bin, offset + 4, (ushort)_defense.Value);
        _bin[offset + 11] = (byte)_maxLevel.Value;
        BinFileService.WriteUInt16(_bin, offset + 12, (ushort)_experience.Value);
        byte systemFlags = 0;
        for (int i = 0; i < _systemChecks.Length; i++)
            if (_systemChecks[i].Checked) systemFlags |= (byte)(1 << i);
        byte skillFlags = 0;
        for (int i = 0; i < _skillChecks.Length; i++)
            if (_skillChecks[i].Checked) skillFlags |= (byte)(1 << i);
        _bin[offset + 6] = systemFlags;
        _bin[offset + 7] = skillFlags;
        if (_moveSpeed.SelectedIndex >= 0)
            _bin[offset + 9] = (byte)_moveSpeed.SelectedIndex;
        _bin[offset + 10] = (byte)_dropRate.Value;
        if (_dropItem.SelectedIndex >= 0)
            BinFileService.WriteUInt16(_bin, offset + 16, (ushort)_dropItem.SelectedIndex);
        _bin[offset + 18] = (byte)_sleepRate.Value;
        _bin[offset + 22] = (byte)_skillRate.Value;
        _bin[offset + 23] = (byte)_skillRate.Value;

        // HSP側の先頭空欄分を補正して書き込む。
        int parameterIndex = _selectedMonsterId + 1;
        int recruitOffset = RecruitModifierDataOffset + parameterIndex * 2;
        int recoveryOffset = RecoveryConstantDataOffset + parameterIndex * 2;

        BinFileService.CheckRange(_bin, recruitOffset, 2);
        BinFileService.CheckRange(_bin, recoveryOffset, 2);

        BinFileService.WriteUInt16(_bin, recruitOffset, (ushort)_recruitModifier.Value);
        BinFileService.WriteUInt16(_bin, recoveryOffset, (ushort)_recoveryConstant.Value);

        if (_skillDataReady)
        {
            int rangeOffset = SkillRangeDataOffset + _selectedMonsterId * 20;
            int allyOffset = SkillAllyItemDataOffset + _selectedMonsterId * 8;

            uint messagePointer = 0;
            if (_skillMessage.SelectedIndex > 0)
                messagePointer = SkillMessagePointerBase + (uint)(_skillMessage.SelectedIndex - 1) * 16;

            WriteUInt32LE(_bin, rangeOffset + 10, messagePointer);
            WriteUInt32LE(_bin, rangeOffset + 14, messagePointer);

            if (_skillRange.SelectedIndex >= 0)
                BinFileService.WriteUInt16(_bin, rangeOffset + 6, (ushort)_skillRange.SelectedIndex);

            if (_enemyThrowItem.SelectedIndex >= 0)
                BinFileService.WriteUInt16(_bin, rangeOffset + 18, (ushort)_enemyThrowItem.SelectedIndex);

            if (_allyThrowItem1.SelectedIndex >= 0)
                BinFileService.WriteUInt16(_bin, allyOffset, (ushort)_allyThrowItem1.SelectedIndex);

            if (_allyThrowItem2.SelectedIndex >= 0)
                BinFileService.WriteUInt16(_bin, allyOffset + 2, (ushort)_allyThrowItem2.SelectedIndex);
        }
        if (_growthType.SelectedIndex >= 0)
            _bin[offset + 20] = (byte)_growthType.SelectedIndex;

        _onChanged?.Invoke();

        if (Pcsx2LiveSession.IsConnected &&
            Pcsx2LiveSession.ProcessId > 0 &&
            Pcsx2LiveSession.MemoryLayout != null)
        {
            try
            {
                var writes = new List<Tuple<int, byte[], string>>();

                byte[] CopyRange(int start, int length)
                {
                    BinFileService.CheckRange(_bin, start, length);
                    byte[] result = new byte[length];
                    Array.Copy(_bin, start, result, 0, length);
                    return result;
                }

                writes.Add(Tuple.Create(
                    offset,
                    CopyRange(offset, MonsterRecordSize),
                    "モンスターレコード"));

                writes.Add(Tuple.Create(
                    recruitOffset,
                    CopyRange(recruitOffset, 2),
                    "勧誘補正値"));

                writes.Add(Tuple.Create(
                    recoveryOffset,
                    CopyRange(recoveryOffset, 2),
                    "回復定数"));

                if (_skillDataReady)
                {
                    int rangeOffset = SkillRangeDataOffset + _selectedMonsterId * 20;
                    int allyOffset = SkillAllyItemDataOffset + _selectedMonsterId * 8;

                    writes.Add(Tuple.Create(
                        rangeOffset,
                        CopyRange(rangeOffset, 20),
                        "特技範囲データ"));

                    writes.Add(Tuple.Create(
                        allyOffset,
                        CopyRange(allyOffset, 8),
                        "仲間投げアイテムデータ"));
                }

                string resultText = Pcsx2BinApplier.ApplyRanges(
                    Pcsx2LiveSession.ProcessId,
                    Pcsx2LiveSession.MemoryLayout,
                    writes);

                _status.Text =
                    MonsterNames[_selectedMonsterId] +
                    " の変更をPCSX2へ反映しました。\r\n" +
                    resultText;
            }
            catch (Exception ex)
            {
                _status.Text =
                    "BIN作業データは更新済みですが、PCSX2への反映に失敗しました。\r\n" +
                    ex.Message;

                if (!_automaticLiveApply)
                {
                    MessageBox.Show(
                        this,
                        ex.ToString(),
                        "PCSX2反映エラー",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.MemoryLayout == null)
        {
            _status.Text =
                MonsterNames[_selectedMonsterId] +
                " の変更をBIN作業データへ反映しました。\r\n" +
                "PCSX2未接続のためゲームメモリには未適用です。";
        }

        _selectedInfo.Text =
            "モンスター番号：" + _selectedMonsterId.ToString("D3") +
            "　／　BINレコード位置：0x" + offset.ToString("X") +
            "\r\nHP・攻撃力・防御力・最大レベル・経験値を更新しました。";    }
}
