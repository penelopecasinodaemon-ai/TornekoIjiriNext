using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    private const int LegacyModifierArraySize = 512;
    private const int SkillVersionOffset = 355056;
    private const int SkillRangeDataOffset = 382206;
    private const int SkillAllyItemDataOffset = 386212;
    private static readonly string[] SkillMessageNames =
{
    "アルテマソード",
    "ギガデイン",
    "持っている武器を思いきり投げつけた",
    "なぎはらい",
    "ムーンサルト",
    "仲間を吐こうとした",
    "強い光を出した",
    "なめまわした",
    "はげしく攻撃",
    "剣にちからをこめた",
    "○○を振った！",
    "○○を振った！2",
    "吹き飛ばすほどの攻撃",
    "マホトーン",
    "口をふさいだ",
    "口をふさいできた",
    "思いっきり斧を振った",
    "メガザル",
    "○○を振った！3",
    "○○を振った！4",
    "バシルーラ",
    "ハラヘリ攻撃",
    "さそう踊り",
    "不思議な踊り",
    "催眠",
    "ラリホー",
    "毒胞子攻撃",
    "アイテム弾き",
    "アイテム弾き2",
    "装備弾き",
    "炎ブレス",
    "くさった液",
    "アストロン",
    "にらみつけた",
    "凍える息",
    "仲間を呼んだ！",
    "マダンテ",
    "しびれ攻撃",
    "ベホマ",
    "ホイミ",
    "変な呪文",
};
    private const int SkillMessageCount = 41;
    private const int SkillExtensionRequiredEnd = 404356;
    private const uint EeAddressMask = 0x1FFFFFFF;
    private const uint EeMemoryBaseAddress = 0x00180000;
    private const uint SkillMessagePointerBase = 0x209126D0;

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
    private readonly ComboBox _actionChange = CreateActionChoice();
    private readonly CheckBox _ionazunAlways = new CheckBox
    {
        Text = "コロマージのイオナズンを確定で唱える（回数制限なし）",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    private readonly CheckBox _ganirasuBarrier = new CheckBox
    {
        Text = "ガニラスバリアを所持する",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    private readonly Label _actionMonitorStatus = new Label();
    private readonly ActionAutoRestoreService _actionRestore;
    private readonly bool _ownsActionRestore;

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
    private int _loadedRecruitModifier;
    private int _loadedRecoveryConstant;
    private bool _dataReady;
    private bool _loadingSelection;
    private bool _automaticLiveApply;

    public MonsterCreatorForm() : this(null, null, null)
    {
    }

    public MonsterCreatorForm(byte[]? bin, Action? onChanged)
        : this(bin, onChanged, null)
    {
    }

    public MonsterCreatorForm(
        byte[]? bin,
        Action? onChanged,
        ActionAutoRestoreService? actionRestore)
    {
        _actionRestore = actionRestore ??
            new ActionAutoRestoreService();

        _ownsActionRestore = actionRestore == null;

        _bin = bin;
        _onChanged = onChanged;
        _recruitModifier.Minimum = short.MinValue;
        _recruitModifier.Maximum = short.MaxValue;

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
            RowCount = 16,
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
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        fieldGrid.Controls.Add(_ganirasuBarrier, 0, 14);
        fieldGrid.SetColumnSpan(_ganirasuBarrier, 2);
        fieldGrid.Controls.Add(_ionazunAlways, 0, 15);
        fieldGrid.SetColumnSpan(_ionazunAlways, 2);
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
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
        fieldGrid.Controls.Add(CreateField("特技使用率（Aのみ）", _skillRate), 0, 12);
        fieldGrid.SetColumnSpan(fieldGrid.Controls[fieldGrid.Controls.Count - 1], 2);
        var actionChangePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        _actionChange.Dock = DockStyle.Fill;
        actionChangePanel.Controls.Add(_actionChange);

        fieldGrid.Controls.Add(CreateField("特技変更", actionChangePanel), 0, 13);
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
                "特技使用率(+22)はAのみを書き込みます。+23のBは変更しません。\r\n" +
                "勧誘修正値・回復定数は旧HSPの独立配列（各256件）を編集します。\r\n" +
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
            WrapContents = true,
            AutoScroll = true
        };

        _applyButton.Text = "再適用";
        _applyButton.Width = 130;
        _applyButton.Height = 36;
        _applyButton.Click += (_, _) => ApplySelected();

        _ionazunAlways.Checked = _actionRestore.IonazunForceEnabled;
        _ionazunAlways.CheckedChanged += (_, _) =>
        {
            if (_loadingSelection || _selectedMonsterId < 0)
                return;

            if (_actionRestore.SetIonazunForce(
                _ionazunAlways.Checked,
                out string ionazunMessage))
            {
                _status.Text = ionazunMessage;
            }
            else
            {
                _status.Text = ionazunMessage;
            }
        };

        _ganirasuBarrier.CheckedChanged += (_, _) =>
        {
            if (_loadingSelection || _selectedMonsterId < 0)
                return;

            Torneko4GanirasuBarrierStore.Set(
                _selectedMonsterId,
                _ganirasuBarrier.Checked);

            bool applied =
                Torneko4GanirasuBarrierRuntime.TryApply(
                    Torneko4GanirasuBarrierStore.GetCopy(),
                    out string barrierMessage);

            _status.Text =
                MonsterNames[_selectedMonsterId] +
                " のガニラスバリア設定を更新しました。" +
                Environment.NewLine +
                barrierMessage;
        };

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
        var numericExperimentButton = new Button
        {
            Text = "演出値 201↔202",
            Width = 145,
            Height = 36
        };

        numericExperimentButton.Click += (_, _) =>
        {
            try
            {
                uint status = ActionAutoRestoreService.GetSharedStatus();

                if (status != 1)
                    throw new InvalidOperationException(
                        "PCSX2を一時停止してから実行してください。");

                const uint address = 0x002B46D0u;
                const uint original = 0x240500C9u;
                const uint changed = 0x240500CAu;

                uint current =
                    ActionAutoRestoreService.ReadShared32(address);

                uint target = current == original
                    ? changed
                    : current == changed
                        ? original
                        : throw new InvalidOperationException(
                            "対象命令が想定値ではありません: 0x" +
                            current.ToString("X8"));

                ActionAutoRestoreService.WriteShared32(address, target);

                uint verify =
                    ActionAutoRestoreService.ReadShared32(address);

                if (verify != target)
                    throw new InvalidOperationException(
                        "書き込み後の読み戻し検証に失敗しました。");

                string result = target == changed
                    ? "201 → 202 に変更しました。"
                    : "元の201へ復元しました。";

                MessageBox.Show(
                    this,
                    result + "\r\n現在値: 0x" +
                    verify.ToString("X8") +
                    "\r\nこれは演出側の数値で、呪文行動IDの確定ではありません。",
                    "数値実験",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "数値実験エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(numericExperimentButton);

        var actionTraceButton = new Button
        {
            Text = "直近の敵行動ID記録",
            Width = 165,
            Height = 36
        };

        actionTraceButton.Click += (_, _) =>
        {
            try
            {
                string report = ActionAutoRestoreService.ReadActionTrace();

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "EnemyActionTrace_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report,
                    System.Text.Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "行動IDの読み取りが完了しました。\r\n\r\n" + outputPath +
                    "\r\n\r\n" + report,
                    "敵行動ID記録",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "敵行動ID記録エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(actionTraceButton);

        var coromageCaveScanButton = new Button
        {
            Text = "コロマージ空き領域調査",
            Width = 190,
            Height = 36
        };

        coromageCaveScanButton.Click += (_, _) =>
        {
            try
            {
                string report = CoromageCaveScan.Run();

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "CoromageCaveScan_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report,
                    System.Text.Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "調査結果を保存しました。\r\n" +
                    outputPath +
                    "\r\n\r\n" +
                    report,
                    "コロマージ空き領域調査",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "空き領域調査エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageCaveScanButton);

        var coromageCallsiteInstallButton = new Button
        {
            Text = "呼出地点フック設置",
            Width = 190,
            Height = 36
        };

        coromageCallsiteInstallButton.Click += (_, _) =>
        {
            try
            {
                string result = CoromageCallsiteTrace.Install();

                MessageBox.Show(
                    this,
                    result,
                    "呼出地点フック設置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呼出地点フック設置エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageCallsiteInstallButton);
        buttons.Controls.SetChildIndex(coromageCallsiteInstallButton, 0);

        var coromageCallsiteReadButton = new Button
        {
            Text = "呼出記録読取",
            Width = 190,
            Height = 36
        };

        coromageCallsiteReadButton.Click += (_, _) =>
        {
            try
            {
                string report = CoromageCallsiteTrace.Read();

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "CoromageCallsiteTrace_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report,
                    System.Text.Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "記録を保存しました。\r\n" + outputPath + "\r\n\r\n" + report,
                    "呼出記録読取",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呼出記録読取エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageCallsiteReadButton);

        var coromageCallsiteRestoreButton = new Button
        {
            Text = "呼出地点フック解除",
            Width = 190,
            Height = 36
        };

        coromageCallsiteRestoreButton.Click += (_, _) =>
        {
            try
            {
                string result = CoromageCallsiteTrace.Restore();

                MessageBox.Show(
                    this,
                    result,
                    "呼出地点フック解除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呼出地点フック解除エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageCallsiteRestoreButton);

        var coromageInstallButton = new Button
        {
            Text = "関数入口フック設置",
            Width = 150,
            Height = 36
        };

        coromageInstallButton.Click += (_, _) =>
        {
            try
            {
                MessageBox.Show(
                    this,
                    CoromageFunctionTrace.Install(),
                    "関数入口フック",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "関数入口フック設置エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageInstallButton);

        var coromageReadButton = new Button
        {
            Text = "関数記録読取",
            Width = 130,
            Height = 36
        };

        coromageReadButton.Click += (_, _) =>
        {
            try
            {
                string report = CoromageFunctionTrace.Read();

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "CoromageFunctionTrace_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report,
                    System.Text.Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "記録を保存しました。\r\n" + outputPath +
                    "\r\n\r\n" + report,
                    "関数記録読取",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "関数記録読取エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageReadButton);

        var coromageRestoreButton = new Button
        {
            Text = "関数フック解除",
            Width = 130,
            Height = 36
        };

        coromageRestoreButton.Click += (_, _) =>
        {
            try
            {
                MessageBox.Show(
                    this,
                    CoromageFunctionTrace.Restore(),
                    "関数フック解除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "関数フック解除エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(coromageRestoreButton);
        var spellTraceInstallButton = new Button
        {
            Text = "呪文分岐フック設置",
            Width = 155,
            Height = 36
        };

        spellTraceInstallButton.Click += (_, _) =>
        {
            try
            {
                string result =
                    ActionAutoRestoreService.InstallSpellDispatchTrace();

                MessageBox.Show(
                    this,
                    result,
                    "呪文分岐フック",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呪文分岐フック設置エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        var spellTraceReadButton = new Button
        {
            Text = "呪文分岐記録読取",
            Width = 155,
            Height = 36
        };

        spellTraceReadButton.Click += (_, _) =>
        {
            try
            {
                string report =
                    ActionAutoRestoreService.ReadSpellDispatchTrace();

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "CoromageSpellDispatchTrace_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report,
                    System.Text.Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "呪文分岐記録を保存しました。\n\n" +
                    outputPath + "\n\n" + report,
                    "呪文分岐記録",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呪文分岐記録エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        var spellTraceRestoreButton = new Button
        {
            Text = "呪文分岐フック解除",
            Width = 155,
            Height = 36
        };

        spellTraceRestoreButton.Click += (_, _) =>
        {
            try
            {
                string result =
                    ActionAutoRestoreService.RestoreSpellDispatchTrace();

                MessageBox.Show(
                    this,
                    result,
                    "呪文分岐フック解除",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "呪文分岐フック解除エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(spellTraceInstallButton);
        buttons.Controls.Add(spellTraceReadButton);
        buttons.Controls.Add(spellTraceRestoreButton);

        var diagnosticActions = new Dictionary<string, Button>
        {
            { "呼出地点フック設置", coromageCallsiteInstallButton },
            { "呼出記録読取", coromageCallsiteReadButton },
            { "呼出地点フック解除", coromageCallsiteRestoreButton },
            { "関数入口フック設置", coromageInstallButton },
            { "関数記録読取", coromageReadButton },
            { "関数フック解除", coromageRestoreButton },
            { "呪文分岐フック設置", spellTraceInstallButton },
            { "呪文分岐記録読取", spellTraceReadButton },
            { "呪文分岐フック解除", spellTraceRestoreButton },
            { "コロマージ空き領域調査", coromageCaveScanButton },
            { "直近の敵行動ID記録", actionTraceButton },
            { "演出値 201↔202 実験", numericExperimentButton }
        };

        var diagnosticChoice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 390,
            Height = 36,
            Margin = new Padding(3, 3, 8, 3)
        };

        diagnosticChoice.Items.AddRange(
            diagnosticActions.Keys.Cast<object>().ToArray());

        diagnosticChoice.SelectedIndex = 0;

        var diagnosticApplyButton = new Button
        {
            Text = "選択した処理を実行",
            Width = 160,
            Height = 36,
            Margin = new Padding(3)
        };

        diagnosticApplyButton.Click += (_, _) =>
        {
            if (diagnosticChoice.SelectedItem is string selectedName &&
                diagnosticActions.TryGetValue(
                    selectedName,
                    out Button selectedAction))
            {
                selectedAction.PerformClick();
            }
        };

        var diagnosticPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0)
        };

        diagnosticPanel.Controls.Add(diagnosticChoice);
        diagnosticPanel.Controls.Add(diagnosticApplyButton);

        buttons.Controls.Clear();
        buttons.FlowDirection = FlowDirection.LeftToRight;
        buttons.WrapContents = false;
        buttons.AutoScroll = false;
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(reloadButton);
        buttons.Controls.Add(closeButton);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };

        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(diagnosticPanel, 0, 1);
        bottom.Controls.Add(buttons, 0, 2);
right.Controls.Add(_count, 0, 0);
        right.Controls.Add(_selectedTitle, 0, 1);
        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _actionMonitorStatus.Dock = DockStyle.Fill;
        _actionMonitorStatus.AutoEllipsis = true;
        _actionMonitorStatus.ForeColor = Color.DarkSlateBlue;
        _actionMonitorStatus.Text = "PCSX2のPINE接続を確認しています（TCP 28011）。";

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

        var coromageProbeButton = new Button
        {
            Text = "コロマージ調査",
            Dock = DockStyle.Fill
        };

        coromageProbeButton.Click += (_, _) =>
        {
            try
            {
                var addresses = new List<uint>();

                for (uint address = 0x00228BB8; address <= 0x00228C20; address += 4)
                    addresses.Add(address);

                for (uint address = 0x00229150; address <= 0x00229220; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002B4680; address <= 0x002B4710; address += 4)
                    addresses.Add(address);

                for (uint address = 0x0029ABE0; address <= 0x0029AC80; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002F3C60; address <= 0x002F3D80; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002EC564; address <= 0x002EC740; address += 4)
                    addresses.Add(address);

                for (uint address = 0x00228BB8; address <= 0x00229300; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002B4714; address <= 0x002B4750; address += 4)
                    addresses.Add(address);
                for (uint address = 0x002F29A4; address <= 0x002F2B20; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002FD6BC; address <= 0x002FD7C0; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002FFB00; address <= 0x002FFC00; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002F0768; address <= 0x002F0860; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002F06C8; address <= 0x002F07C0; address += 4)
                    addresses.Add(address);

                for (uint address = 0x0029AC94; address <= 0x0029AD80; address += 4)
                    addresses.Add(address);

                uint[] dispatchAddresses =
                {
                    0x002F29A4,
                    0x002F2A30,
                    0x002FD6BC,
                    0x002FFB00,
                    0x002F0768,
                    0x002F06C8
                };

                addresses.AddRange(dispatchAddresses);

                for (uint address = 0x002EC540; address <= 0x002EC640; address += 4)
                    addresses.Add(address);

                for (uint address = 0x0029AC5C; address <= 0x0029AD00; address += 4)
                    addresses.Add(address);

                uint[] additionalAddresses =
                {
                    0x002B46C8,
                    0x002B46CC,
                    0x002B46D0,
                    0x002B49A0,
                    0x002B49C4,
                    0x002B4A78
                };

                addresses.AddRange(additionalAddresses);
                for (uint address = 0x002C94C0; address < 0x002C9580; address += 4)
                    addresses.Add(address);
                for (uint address = 0x002C752C; address <= 0x002C7C00; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002C9694; address <= 0x002C9C00; address += 4)
                    addresses.Add(address);
                for (uint address = 0x002C74C0; address <= 0x002C7600; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002C9300; address <= 0x002C94C0; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002C9580; address <= 0x002C9700; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002CCE40; address <= 0x002CCFA0; address += 4)
                    addresses.Add(address);

                for (uint address = 0x002CDF00; address <= 0x002CE100; address += 4)
                    addresses.Add(address);
                for (uint spellDispatchAddress = 0x002CF600; spellDispatchAddress <= 0x002D06CC; spellDispatchAddress += 4)
                    addresses.Add(spellDispatchAddress);

                for (uint spellHandlerAddress = 0x002B5CCC; spellHandlerAddress <= 0x002B8AC0; spellHandlerAddress += 4)
                    addresses.Add(spellHandlerAddress);
                for (uint coromageActionAddress = 0x00229480; coromageActionAddress <= 0x00229680; coromageActionAddress += 4)
                    addresses.Add(coromageActionAddress);

                for (uint coromageCandidateAddress = 0x008FCF20; coromageCandidateAddress <= 0x008FCFD0; coromageCandidateAddress += 4)
                    addresses.Add(coromageCandidateAddress);
                for (uint coromageTailAddress = 0x00229300; coromageTailAddress <= 0x00229500; coromageTailAddress += 4)
                    addresses.Add(coromageTailAddress);

                for (uint coromageTableAddress = 0x0065D100; coromageTableAddress <= 0x0065D220; coromageTableAddress += 4)
                    addresses.Add(coromageTableAddress);

                var report = new StringBuilder();

                report.AppendLine("COROMAGE SPELL MEMORY READ");
                report.AppendLine("READ ONLY");
                report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("Address base: 0x00200000");
                report.AppendLine();

                foreach (uint address in addresses)
                {
                    uint value = ActionAutoRestoreService.ReadShared32(address);

                    report.AppendLine(
                        "0x" + address.ToString("X8") +
                        "  0x" + value.ToString("X8"));
                }

                string desktop =
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                string outputPath = System.IO.Path.Combine(
                    desktop,
                    "CoromageSpellMemoryRead_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".txt");

                System.IO.File.WriteAllText(
                    outputPath,
                    report.ToString(),
                    Encoding.UTF8);

                MessageBox.Show(
                    this,
                    "読み取り専用調査が完了しました。\n\n" + outputPath,
                    "コロマージ調査",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "PINE読み取りに失敗しました。\n\n" + ex.Message,
                    "コロマージ調査",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        infoPanel.Controls.Add(_actionMonitorStatus, 0, 0);
        infoPanel.Controls.Add(coromageProbeButton, 1, 0);
        infoPanel.Controls.Add(_selectedInfo, 0, 1);
        infoPanel.Controls.Add(copyInfoButton, 1, 1);
        right.Controls.Add(infoPanel, 0, 2);
        right.Controls.Add(content, 0, 3);
        right.RowStyles[4] = new RowStyle(SizeType.Absolute, 112);
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
        Action<string> actionStatusHandler = message =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                try
                {
                    BeginInvoke(
                        (Action)(() => _actionMonitorStatus.Text = message));
                }
                catch
                {
                }
            }
        };

        _actionRestore.StatusChanged += actionStatusHandler;

        FormClosed += (_, _) =>
        {
            _actionRestore.StatusChanged -= actionStatusHandler;

            // MainFormから注入されたサービスは破棄しない。
            if (_ownsActionRestore)
                _actionRestore.Dispose();
        };
        // 画面ハンドル生成後に保存済み種族別特技設定を再適用する。
        // コンストラクター中のイベント通知取りこぼしを防止する。
        Shown += (_, _) =>
        {
            var savedActions = Torneko4ActionStore.GetCopy();

            if (savedActions.Count == 0)
            {
                _actionMonitorStatus.Text =
                    "種族別特技設定なし：元の命令を維持しています。";
            }

            if (savedActions.Count > 0)
            {
                if (!_actionRestore.TryApply(
                    savedActions,
                    out string applyMessage))
                {
                    _actionMonitorStatus.Text =
                        "保存済み種族別特技の再適用に失敗: " +
                        applyMessage;
                }
            }

            Torneko4GanirasuBarrierRuntime.TryApply(
                Torneko4GanirasuBarrierStore.GetCopy(),
                out string barrierMessage);

            _actionMonitorStatus.Text =
                _actionMonitorStatus.Text +
                Environment.NewLine +
                barrierMessage;
        };
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
        _actionChange.SelectedIndexChanged += (_, _) => ApplySelectedActionChange();
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
        {
            string label = "メッセージ " + (i + 1).ToString("D3");
            if (i < SkillMessageNames.Length)
            {
                label = SkillMessageNames[i];
            }
            choices.Add(label);
        }

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
        bool modifierDataAvailable = _bin != null &&
            _bin.Length >= RecruitModifierDataOffset + LegacyModifierArraySize;
        _recruitModifier.Enabled = enabled && modifierDataAvailable;
        _recoveryConstant.Enabled = enabled && modifierDataAvailable;
        _skillRange.Enabled = enabled && _skillDataReady;
        // var_411 は拡張BINの有無に関係なく、接続中ならPCSX2から直接読み書きする。
        _enemyThrowItem.Enabled = enabled &&
            (_skillDataReady || Pcsx2LiveSession.IsConnected);
        _allyThrowItem1.Enabled = enabled && _skillDataReady;
        _allyThrowItem2.Enabled = enabled && _skillDataReady;
        _skillMessage.Enabled = enabled && _skillDataReady;
        _applyButton.Enabled = enabled;
        _actionChange.Enabled = _selectedMonsterId >= 0;
        _ganirasuBarrier.Enabled = enabled && _selectedMonsterId >= 0;
        _ionazunAlways.Enabled = enabled && IsIonazunActionSelected();
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

        _ganirasuBarrier.Checked =
            Torneko4GanirasuBarrierStore.Get(id);

        // まず未選択状態に戻し、このモンスター専用の設定があれば復元する。
        _actionChange.SelectedIndex = 0;

        if (Torneko4ActionStore.TryGet(id, out ushort savedActionId))
        {
            int savedIndex = Array.FindIndex(
                ActionAutoRestoreService.ActionChoices,
                x => x.Id == savedActionId);

            if (savedIndex >= 0)
                _actionChange.SelectedIndex = savedIndex + 1;
        }

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

        // HSP var_407 は先頭が空行。実モンスターのHSP IDはC# ID + 1。
        int hspMonsterIndex = id + 1;
        int recruitOffset = RecruitModifierDataOffset + hspMonsterIndex * 2;
        int recoveryOffset = RecoveryConstantDataOffset + hspMonsterIndex * 2;

        if (Pcsx2LiveSession.IsConnected &&
            Pcsx2LiveSession.ProcessId > 0 &&
            Pcsx2LiveSession.MemoryLayout != null)
        {
            try
            {
                var requested = new List<Tuple<int, int, string>>
                {
                    Tuple.Create(offset, MonsterRecordSize, "モンスターレコード"),
                    Tuple.Create(recruitOffset, 2, "勧誘修正値"),
                    Tuple.Create(recoveryOffset, 2, "回復定数")
                };
                // HSPは回復配列の先頭2 bytesで配列全体の有効/未初期化を判定する。
                if (hspMonsterIndex != 0)
                    requested.Add(Tuple.Create(RecoveryConstantDataOffset, 2, "回復定数配列識別"));

                int liveRangeOffset = SkillRangeDataOffset + hspMonsterIndex * 20;
                int liveAllyOffset = SkillAllyItemDataOffset + hspMonsterIndex * 8;
                if (_skillDataReady)
                {
                    requested.Add(Tuple.Create(liveRangeOffset, 20, "特技範囲データ"));
                    requested.Add(Tuple.Create(liveAllyOffset, 8, "仲間投げアイテムデータ"));
                }
                else
                {
                    // 旧HSP var_411 = ReadProcessMemory(var_646 + 20 * var_542, 2)
                    requested.Add(Tuple.Create(liveRangeOffset + 18, 2, "var_411 敵投擲アイテム"));
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

        // HSPのvar_166（回復）とvar_168（勧誘）は512 bytesずつの独立配列。
        // var_166先頭が0x5555なら未初期化扱いなので、表示値は0にする。
        bool modifierArraysMissing =
            _bin[RecoveryConstantDataOffset] == 0x55 &&
            _bin[RecoveryConstantDataOffset + 1] == 0x55;
        int recruitValue = modifierArraysMissing
            ? 0
            : unchecked((short)BinFileService.ReadUInt16(_bin, recruitOffset));
        int recoveryValue = modifierArraysMissing
            ? 0
            : BinFileService.ReadUInt16(_bin, recoveryOffset);
        _recruitModifier.Value = recruitValue;
        _recoveryConstant.Value = recoveryValue;
        _loadedRecruitModifier = recruitValue;
        _loadedRecoveryConstant = recoveryValue;

        string skillDebug = "";

        // var_411 は常にレコード+18。特技拡張BINがなくてもライブ読込値を表示する。
        int enemyItemOffset = SkillRangeDataOffset + hspMonsterIndex * 20 + 18;
        ushort enemyItemValue = BinFileService.ReadUInt16(_bin, enemyItemOffset);
        bool canShowLiveEnemyItem =
            Pcsx2LiveSession.IsConnected && Pcsx2LiveSession.MemoryLayout != null;
        if (_skillDataReady || canShowLiveEnemyItem)
        {
            _enemyThrowItem.SelectedIndex =
                enemyItemValue < _enemyThrowItem.Items.Count ? enemyItemValue : -1;
        }

        if (_skillDataReady)
        {
            int rangeOffset = SkillRangeDataOffset + hspMonsterIndex * 20;
            int allyOffset = SkillAllyItemDataOffset + hspMonsterIndex * 8;

            ushort rangeValue = BinFileService.ReadUInt16(_bin, rangeOffset + 6);
            uint messagePointer = ReadUInt32LE(_bin, rangeOffset + 10);
            ushort allyItemValue1 = BinFileService.ReadUInt16(_bin, allyOffset);
            ushort allyItemValue2 = BinFileService.ReadUInt16(_bin, allyOffset + 2);
            skillDebug =
                "\r\n--- 特技データ診断 ---" +
                "\r\nモンスターID：" + id +
                "\r\n範囲位置：" + rangeOffset +
                " / 生値：" + rangeValue +
                "\r\nvar_411 敵投擲位置：" + enemyItemOffset +
                " / 生値：" + enemyItemValue +
                "\r\n味方投擲位置：" + allyOffset +
                " / 生値：" + allyItemValue1 +
                "\r\n味方投擲2位置：" + (allyOffset + 2) +
                " / 生値：" + allyItemValue2;

            _skillRange.SelectedIndex = rangeValue < _skillRange.Items.Count ? rangeValue : -1;
            _skillMessage.SelectedIndex = ResolveSkillMessageIndex(messagePointer);
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

        // HSP var_407 の先頭空行を含むIDに合わせる。
        int hspMonsterIndex = _selectedMonsterId + 1;
        int recruitOffset = RecruitModifierDataOffset + hspMonsterIndex * 2;
        int recoveryOffset = RecoveryConstantDataOffset + hspMonsterIndex * 2;
        int rangeOffset = SkillRangeDataOffset + hspMonsterIndex * 20;
        int allyOffset = SkillAllyItemDataOffset + hspMonsterIndex * 8;
        int enemyItemOffset = rangeOffset + 18;

        // var_411 の編集値をBIN作業データへ常時保持する。
        BinFileService.CheckRange(_bin, enemyItemOffset, 2);
        if (_enemyThrowItem.SelectedIndex >= 0)
        {
            BinFileService.WriteUInt16(
                _bin, enemyItemOffset, (ushort)_enemyThrowItem.SelectedIndex);
        }

        if (_skillDataReady)
        {
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

        bool modifierArraysMissing =
            _bin[RecoveryConstantDataOffset] == 0x55 &&
            _bin[RecoveryConstantDataOffset + 1] == 0x55;
        int recruitValueToWrite = decimal.ToInt32(_recruitModifier.Value);
        int recoveryValueToWrite = decimal.ToInt32(_recoveryConstant.Value);
        bool recruitChanged = recruitValueToWrite != _loadedRecruitModifier;
        bool recoveryChanged = recoveryValueToWrite != _loadedRecoveryConstant;
        bool modifierChanged = recruitChanged || recoveryChanged;

        if (modifierChanged)
        {
            // 0x5555の未初期化配列だけをゼロ初期化し、既存配列は保持する。
            if (modifierArraysMissing)
            {
                Array.Clear(_bin, RecoveryConstantDataOffset, LegacyModifierArraySize);
                Array.Clear(_bin, RecruitModifierDataOffset, LegacyModifierArraySize);
            }

            BinFileService.WriteUInt16(
                _bin, recruitOffset, unchecked((ushort)(short)recruitValueToWrite));
            BinFileService.WriteUInt16(
                _bin, recoveryOffset, (ushort)recoveryValueToWrite);

            _loadedRecruitModifier = recruitValueToWrite;
            _loadedRecoveryConstant = recoveryValueToWrite;
        }

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
                    CopyRange(offset, MonsterRecordSize - 1),
                    "モンスターレコード"));

                if (modifierChanged)
                {
                    if (modifierArraysMissing)
                    {
                        writes.Add(Tuple.Create(
                            RecoveryConstantDataOffset,
                            CopyRange(RecoveryConstantDataOffset, LegacyModifierArraySize),
                            "回復定数配列（初期化）"));
                        writes.Add(Tuple.Create(
                            RecruitModifierDataOffset,
                            CopyRange(RecruitModifierDataOffset, LegacyModifierArraySize),
                            "勧誘修正値配列（初期化）"));
                    }
                    else
                    {
                        writes.Add(Tuple.Create(
                            recruitOffset,
                            CopyRange(recruitOffset, 2),
                            "勧誘修正値"));
                        writes.Add(Tuple.Create(
                            recoveryOffset,
                            CopyRange(recoveryOffset, 2),
                            "回復定数"));
                    }
                }

                if (_skillDataReady)
                {
                    writes.Add(Tuple.Create(
                        rangeOffset,
                        CopyRange(rangeOffset, 20),
                        "特技範囲データ"));

                    writes.Add(Tuple.Create(
                        allyOffset,
                        CopyRange(allyOffset, 8),
                        "仲間投げアイテムデータ"));
                }
                else
                {
                    // var_411 = var_646 + 20 * monsterId。+18位置の2バイトだけを書く。
                    writes.Add(Tuple.Create(
                        enemyItemOffset,
                        CopyRange(enemyItemOffset, 2),
                        "var_411 敵投擲アイテム"));
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

    private bool IsIonazunActionSelected()
    {
        int index = _actionChange.SelectedIndex - 1;
        return index >= 0 &&
            index < ActionAutoRestoreService.ActionChoices.Length &&
            ActionAutoRestoreService.ActionChoices[index].Id == 0x00BF;
    }

    private static ComboBox CreateActionChoice()
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = true
        };
        combo.Items.Add("（特技を選択）");
        foreach (var action in ActionAutoRestoreService.ActionChoices)
            combo.Items.Add("0x" + action.Id.ToString("X2") + "  " + action.Name);
        combo.SelectedIndex = 0;
        return combo;
    }

    private void ApplySelectedActionChange()
    {
        if (_loadingSelection)
            return;

        _ionazunAlways.Enabled =
            _dataReady &&
            _selectedMonsterId >= 0 &&
            IsIonazunActionSelected();

        if (_selectedMonsterId < 0)
            return;

        // プレースホルダーを選んだ場合は、そのモンスターの設定を解除する。
        if (_actionChange.SelectedIndex == 0)
        {
            Torneko4ActionStore.Remove(_selectedMonsterId);

            _actionRestore.TryApply(
                Torneko4ActionStore.GetCopy(),
                out string clearMessage);

            _status.Text =
                "このモンスターの特技変更設定を解除しました。\r\n" +
                clearMessage;

            return;
        }

        int index = _actionChange.SelectedIndex - 1;

        if (index < 0 ||
            index >= ActionAutoRestoreService.ActionChoices.Length)
        {
            _status.Text = "特技の選択内容が不正です。";
            return;
        }

        var action = ActionAutoRestoreService.ActionChoices[index];

        // PINEへの適用が失敗しても、選択した設定自体は保持する。
        Torneko4ActionStore.Set(_selectedMonsterId, action.Id);

        if (_actionRestore.TryApply(Torneko4ActionStore.GetCopy(), out string message))
        {
            _status.Text = message;
        }
        else
        {
            _status.Text = "特技変更エラー：" + message;
            MessageBox.Show(this, message, "特技変更エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

