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

    private readonly TextBox _search = new TextBox();
    private readonly ListBox _list = new ListBox();
    private readonly Label _count = new Label();
    private readonly Label _selectedTitle = new Label();
    private readonly Label _selectedInfo = new Label();
    private readonly List<int> _visibleIds = new List<int>();

    public MonsterCreatorForm()
    {
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
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
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Font = new Font("Yu Gothic UI", 10, FontStyle.Bold);

        _selectedTitle.Dock = DockStyle.Fill;
        _selectedTitle.TextAlign = ContentAlignment.MiddleLeft;
        _selectedTitle.Font = new Font("Yu Gothic UI", 18, FontStyle.Bold);

        _selectedInfo.Dock = DockStyle.Fill;
        _selectedInfo.TextAlign = ContentAlignment.TopLeft;
        _selectedInfo.Font = new Font("Yu Gothic UI", 10);
        _selectedInfo.Text = "左の一覧からモンスターを選択してください。";

        var notice = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
            Text = "この画面はモンスター一覧の第1段階です。\\r\\n次の段階で、旧版HSPのデータ定義と照合した編集項目を追加します。\\r\\n未確認のデータ領域には書き込みません。"
        };

        var closeButton = new Button
        {
            Text = "閉じる",
            Dock = DockStyle.Right,
            Width = 120,
            Height = 36
        };
        closeButton.Click += (_, _) => Close();

        right.Controls.Add(_count, 0, 0);
        right.Controls.Add(_selectedTitle, 0, 1);
        right.Controls.Add(_selectedInfo, 0, 2);
        right.Controls.Add(notice, 0, 3);
        right.Controls.Add(closeButton, 0, 4);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);

        RefreshList();
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
            bool matches = query.Length == 0 ||
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
        _count.Text = "登録モンスター：" + MonsterNames.Length +
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
            _selectedTitle.Text = "モンスター未選択";
            _selectedInfo.Text = "検索条件に一致するモンスターを選択してください。";
            return;
        }

        int id = _visibleIds[index];
        _selectedTitle.Text = MonsterNames[id];
        _selectedInfo.Text =
            "モンスター番号： " + id + "\\r\\n" +
            "一覧登録名： " + MonsterNames[id] + "\\r\\n\\r\\n" +
            "この番号を基準に、次の段階で基本能力・行動・耐性などのデータを紐付けます。";
    }
}
