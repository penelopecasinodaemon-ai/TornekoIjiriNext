using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    private const uint EeAddressMask = 0x1FFFFFFF;
    private const uint EeMemoryBaseAddress = 0x00180000;

    private readonly byte[]? _bin;
    private readonly Action? _onChanged;
    private readonly TextBox _search = new TextBox();
    private readonly ListBox _list = new ListBox();
    private readonly Label _count = new Label();
    private readonly Label _selectedTitle = new Label();
    private readonly Label _selectedInfo = new Label();
    private readonly Label _status = new Label();
    private readonly NumericUpDown _hp = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _attack = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _defense = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _experience = CreateNumber(ushort.MaxValue);
    private readonly NumericUpDown _maxLevel = CreateNumber(byte.MaxValue);
    private readonly Button _applyButton = new Button();
    private readonly List<int> _visibleIds = new List<int>();

    private int _recordTableOffset = -1;
    private int _selectedMonsterId = -1;
    private bool _dataReady;

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
        Height = 720;
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
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Font = new Font("Yu Gothic UI", 10, FontStyle.Bold);

        _selectedTitle.Dock = DockStyle.Fill;
        _selectedTitle.TextAlign = ContentAlignment.MiddleLeft;
        _selectedTitle.Font = new Font("Yu Gothic UI", 18, FontStyle.Bold);

        _selectedInfo.Dock = DockStyle.Fill;
        _selectedInfo.TextAlign = ContentAlignment.TopLeft;
        _selectedInfo.Font = new Font("Yu Gothic UI", 9);

        var fieldGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fieldGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        fieldGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        fieldGrid.Controls.Add(CreateField("HP", _hp), 0, 0);
        fieldGrid.Controls.Add(CreateField("攻撃力", _attack), 1, 0);
        fieldGrid.Controls.Add(CreateField("防御力", _defense), 0, 1);
        fieldGrid.Controls.Add(CreateField("経験値", _experience), 1, 1);
        fieldGrid.Controls.Add(CreateField("最大レベル", _maxLevel), 0, 2);

        var explanation = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text =
                "第1段階：旧版24バイト基本レコードのうち、対応を確認できた5項目を編集します。\r\n" +
                "HP(+0)、攻撃力(+2)、防御力(+4)、最大レベル(+11)、経験値(+12)。\r\n" +
                "変更は読み込んだBINの作業データに反映されます。ゲームメモリには直接書き込みません。"
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

        _applyButton.Text = "変更を適用";
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
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(buttons, 0, 1);

        right.Controls.Add(_count, 0, 0);
        right.Controls.Add(_selectedTitle, 0, 1);
        right.Controls.Add(_selectedInfo, 0, 2);
        right.Controls.Add(content, 0, 3);
        right.Controls.Add(bottom, 0, 4);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);

        _dataReady = TryResolveRecordTable(_bin, out _recordTableOffset, out string reason);
        _applyButton.Enabled = _dataReady;

        if (_dataReady)
        {
            _status.Text =
                "BIN読込済み：基本レコード位置 0x" +
                _recordTableOffset.ToString("X") +
                " ／ 199件・24 bytes";
        }
        else
        {
            _status.Text = reason;
            SetEditorEnabled(false);
        }

        RefreshList();
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

    private static Control CreateField(string title, NumericUpDown input)
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

    private void SetEditorEnabled(bool enabled)
    {
        _hp.Enabled = enabled;
        _attack.Enabled = enabled;
        _defense.Enabled = enabled;
        _experience.Enabled = enabled;
        _maxLevel.Enabled = enabled;
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
            "種類　／　表示中：" + _visibleIds.Count + "種類";

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
            _selectedTitle.Text = "モンスター未選択";
            _selectedInfo.Text = "検索条件に一致するモンスターを選択してください。";
            SetEditorEnabled(false);
            return;
        }

        int id = _visibleIds[index];
        _selectedMonsterId = id;
        _selectedTitle.Text = MonsterNames[id];

        if (!_dataReady || _bin == null)
        {
            _selectedInfo.Text =
                "モンスター番号：" + id.ToString("D3") +
                "\r\n一覧登録名：" + MonsterNames[id] +
                "\r\n\r\n" + _status.Text;
            SetEditorEnabled(false);
            return;
        }

        int offset = checked(_recordTableOffset + id * MonsterRecordSize);
        BinFileService.CheckRange(_bin, offset, MonsterRecordSize);

        _hp.Value = BinFileService.ReadUInt16(_bin, offset);
        _attack.Value = BinFileService.ReadUInt16(_bin, offset + 2);
        _defense.Value = BinFileService.ReadUInt16(_bin, offset + 4);
        _maxLevel.Value = _bin[offset + 11];
        _experience.Value = BinFileService.ReadUInt16(_bin, offset + 12);

        _selectedInfo.Text =
            "モンスター番号：" + id.ToString("D3") +
            "　／　BINレコード位置：0x" + offset.ToString("X") +
            "\r\nレコード長：24 bytes　／　未解析領域は変更しません。";

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

        _onChanged?.Invoke();

        _status.Text =
            MonsterNames[_selectedMonsterId] +
            " の変更をBIN作業データへ反映しました。メイン画面から保存してください。";

        _selectedInfo.Text =
            "モンスター番号：" + _selectedMonsterId.ToString("D3") +
            "　／　BINレコード位置：0x" + offset.ToString("X") +
            "\r\nHP・攻撃力・防御力・最大レベル・経験値を更新しました。";
    }
}
