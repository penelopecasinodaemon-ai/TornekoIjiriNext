# トルネコ3用メモリエディタ「トルネコいじり」ソースコード構造調査レポート (ANALYSIS.md)

## 概要
- **対象ソースコード**: `tr3_ijiri/start.hsp` (全8,232行)
- **形式**: HSP2 / HSP3 用スクリプト（deHSP による逆コンパイルコード）
- **対象エミュレータ/ゲーム**: PCSX2 上で動作する PS2版『ドラゴンクエスト・キャラクターズ トルネコの大冒険3 不思議のダンジョン』
- **調査方針**: コード全体の要約や推測による補完を排除し、ラベル・参照関係・Win32 API・メモリ読み書き・機能範囲を厳密に行番号ベースで整理。

---

## 1. ラベル一覧および関数・モジュール一覧

### 1.1 モジュール関数 (`#deffunc` / `#defcfunc`) 一覧
| 行番号 | 種別 | 関数名 | 引数定義 | 役割・概要 |
|---|---|---|---|---|
| 92 | `#deffunc` | `getstamp` | `array prm_7, str prm_8, int prm_9` | ファイルのタイムスタンプ取得（FindFirstFileA, FileTimeToLocalFileTime, FileTimeToSystemTime） |
| 142 | `#deffunc` | `get_all_exe_file_name` | `var prm_59` | 実行中プロセスのPIDとモジュール名一覧取得（CreateToolhelp32Snapshot, Process32First/Next） |
| 175 | `#deffunc` | `get_all_heap_add_size` | `int, var prm_61, int prm_62` | 全ヒープのアドレスおよびサイズ取得（Heap32ListFirst/Next, Heap32First/Next） |
| 231 | `#deffunc` | `get_all_heap_add` | `int, var prm_64, int prm_65` | 全ヒープのアドレス取得 |
| 267 | `#deffunc` | `get_choice_heap_add_size` | `int, var prm_67, int prm_68, int prm_69` | 指定IDヒープのアドレスおよびサイズ取得 |
| 329 | `#defcfunc` | `bsearch` | `int prm_74, int prm_75, int prm_76, var prm_77, int prm_78` | マシン語バイナリパターン検索関数（VirtualProtectで実行権限付与、callfunc呼出） |
| 346 | `#deffunc` | `transform16_10` | `var prm_79, str prm_80` | 16進文字列から10進数値への変換 |
| 374 | `#deffunc` | `dll_getfunc` | `var prm_81, str prm_82, int prm_83` | DLLから指定関数ポインタを取得（GetProcAddress） |
| 387 | `#deffunc` | `_init_llmod` | なし | llmod互換ライブラリ初期化（LoadLibraryAでkernel32/user32/shell32/comctl32/comdlg32/gdi32読込） |
| 410 | `#deffunc` | `dllproc` | `str prm_84, array prm_85, int prm_86, int prm_87` | DLL関数の動的呼び出し（GetProcAddress + callfunc） |
| 430 | `#deffunc` | `getmjrdll` | `var prm_88, int prm_89` | ロード済み主要DLLハンドル取得 |
| 433 | `#deffunc` | `getmjrfunc` | `var prm_90, int prm_91` | ロード済み主要API関数ポインタ取得 |
| 436 | `#deffunc` | `_get_active_window` | `var prm_92` | アクティブウィンドウハンドル取得 |
| 439 | `#deffunc` | `setwndlong` | `var prm_93, int prm_94` | GetWindowLongA / SetWindowLongA 呼び出し |
| 451 | `#deffunc` | `_null_sep_str` | `var prm_95, int prm_96` | 文字列内の指定区切り文字をヌル文字(0)に置換 |
| 463 | `#deffunc` | `_makewnd` | `array prm_97, str prm_98` | winobj による汎用ウィンドウコントロール生成 |
| 468 | `#deffunc` | `_is_wnd` | `int prm_99` | IsWindow 判定 |
| 472 | `#deffunc` | `_hspobjhandle` | `int prm_100` | HSPオブジェクトのHWND取得 |
| 475 | `#deffunc` | `_hspobjid` | `int prm_101` | HWNDからHSPオブジェクトIDを逆引き |
| 484 | `#deffunc` | `charupper` | `var prm_102` | CharUpperA 呼び出し |
| 488 | `#deffunc` | `charlower` | `var prm_103` | CharLowerA 呼び出し |
| 495 | `#deffunc` | `trackbox` | `int prm_104, int prm_105, int prm_106` | トラックバーコントロール(msctls_trackbar32)生成 |
| 506 | `#deffunc` | `sel_trackbox` | `int prm_107` | 操作対象トラックバーの選択 |
| 510 | `#deffunc` | `trackpos` | `int prm_108, int prm_109` | トラックバー位置の取得/設定 (TBM_GETPOS / TBM_SETPOS) |
| 522 | `#deffunc` | `trackrng` | `int prm_110, int prm_111, int prm_112` | トラックバー範囲の取得/設定 (TBM_SETRANGEMIN / MAX) |
| 539 | `#deffunc` | `trackmrk` | `int prm_113, int prm_114, int prm_115` | トラックバー目盛り設定 (TBM_SETTIC) |
| 561 | `#deffunc` | `tracksel` | `int prm_116, int prm_117, int prm_118` | トラックバー選択範囲設定 (TBM_SETSEL) |
| 601 | `#deffunc` | `keybd_event` | `int prm_119, int prm_120, int prm_121` | keybd_event 呼び出しによるキー入力シミュレート |
| 626 | `#deffunc` | `mouse_event` | `int prm_122, int prm_123, int prm_124, int prm_125` | mouse_event 呼び出しによるマウス操作シミュレート |
| 641 | `#deffunc` | `opnsavedlg` | `var prm_128, str prm_129, str prm_130, int prm_131, int prm_132` | コモンダイアログ（ファイルを開く/保存: GetOpenFileNameA/GetSaveFileNameA） |
| 732 | `#defcfunc` | `sub` | `str prm_133, str prm_134, str prm_135` | 部分文字列置換関数（初出1箇所） |
| 747 | `#defcfunc` | `gsub` | `str prm_136, str prm_137, str prm_138` | 全文字列置換関数（全体） |
| 762 | `#deffunc` | `imgm_opengdiplus` | なし | GdiplusStartup |
| 769 | `#deffunc` | `imgm_closegdiplus` | なし | GdiplusShutdown |
| 775 | `#deffunc` | `imgm_createstream` | `int prm_239` | CreateStreamOnHGlobal |
| 786 | `#deffunc` | `imgm_releasestream` | なし | ReleaseStgMedium |
| 792 | `#defcfunc` | `imgf_getformat` | `str prm_240` | 画像ファイルフォーマット判別 (BMP/JPEG/PNG等) |
| 813 | `#deffunc` | `imgf_getpicsize` | `str prm_241, var prm_242, var prm_243` | 画像サイズ取得 |
| 861 | `#deffunc` | `imgf_picloadex` | `str prm_244, int prm_245, int prm_246, int prm_247` | GDI+による画像ロード・描画 |
| 941 | `#deffunc` | `imgf_jpgsave` | `str prm_248, int prm_249` | JPEG保存 |
| 951 | `#deffunc` | `imgp_calcfitsize` | `var prm_250, var prm_251, int prm_252, int prm_253, int prm_254, int prm_255` | 縦横比保持リサイズ計算 |
| 977 | `#deffunc` | `imgp_gzoom` | `int prm_256, int prm_257, int prm_258, int prm_259, int prm_260, int prm_261, int prm_262` | GDI+による拡大縮小描画 |
| 994 | `#deffunc` | `imgp_rotateflip` | `int prm_263, int prm_264, int prm_265, int prm_266, int prm_267, int prm_268` | 画像回転・反転 |
| 1014 | `#deffunc` | `imgp_memsave` | `var prm_269, int prm_270, int prm_271, int prm_272, int prm_273, int prm_274` | メモリ上への画像エンコード保存 |
| 1065 | `#deffunc` | `imgm_filterlight` | `int prm_275, int prm_276, int prm_277, int prm_278` | 輝度フィルタ |
| 1096 | `#deffunc` | `imgp_filterbrush` | `int prm_279, int prm_280, int prm_281` | ブラシ描画フィルタ |
| 1112 | `#deffunc` | `imgp_gcopysubabs` | `int prm_282, int prm_283, int prm_284, int prm_285, int prm_286` | 差分コピー |
| 1159 | `#deffunc` | `imgp_grotate` | `int prm_287, int prm_288, int prm_289, double prm_290, int prm_291, int prm_292` | GDI+任意角度回転描画 |
| 1185 | `#deffunc` | `imgp_paircopy` | `int prm_293, int prm_294, int prm_295, int prm_296, int prm_297, int prm_298, int prm_299, int prm_300, int prm_301, int prm_302` | ペア画像転送 |
| 1201 | `#deffunc` | `imgp_loopcopy` | `int prm_303, int prm_304, int prm_305, int prm_306, int prm_307, int prm_308, int prm_309, int prm_310, int prm_311` | タイル状ループコピー |
| 1267 | `#deffunc` | `imgp_exchange` | `int prm_312, int prm_313, int prm_314, int prm_315, int prm_316` | 矩形領域入れ替え |
| 1302 | `#deffunc` | `urlpost` | `str prm_337, var prm_338, str prm_339, str prm_340, str prm_341, str prm_342` | hspsockを用いたHTTP POSTリクエスト送信 |
| 1436 | `#defcfunc` | `urldecode` | `str prm_343, local prm_344, local prm_345, local prm_346, local prm_347, local prm_348` | URLデコード (%XX 変換) |
| 1453 | `#defcfunc` | `urlencode` | `str prm_349, local prm_350, local prm_351, local prm_352, local prm_353, local prm_354` | URLエンコード (Shift_JISバイト列の%XX化) |
| 1481 | `#deffunc` | `conv_sjis2euc` | `var prm_355, var prm_356` | Shift_JIS から EUC-JP への文字コード変換 |
| 1510 | `#deffunc` | `conv_euc2sjis` | `var prm_357, var prm_358` | EUC-JP から Shift_JIS への文字コード変換 |
| 7671 | `#deffunc` | `han2zen` | `var prm_367` | 半角カタカナから全角ひらがなへの置換 |
| 7680 | `#deffunc` | `heikinwari` | `array prm_368, int prm_369, int prm_370` | 配列要素の非ゼロ項目を平均配分（均割機能） |
| 7698 | `#deffunc` | `hurinaosi` | `array prm_371, int prm_372, int prm_373` | 合計が指定値(999)になるよう非ゼロ項目に端数を再配分（残全振機能） |
| 7741 | `#deffunc` | `sikaku` | `int prm_374, int prm_375, int prm_376, int prm_377` | 矩形枠線描画 (line × 4) |
| 7808 | `#deffunc` | `rm_init` | なし | クライアント座標計算用オフセット初期化 |
| 7812 | `#defcfunc` | `rmousex` | なし | クライアント相対マウスX座標取得 |
| 7814 | `#defcfunc` | `rmousey` | なし | クライアント相対マウスY座標取得 |

---

### 1.2 全ラベル (`*label_xxx`) 一覧
| ラベル名 | 定義行 | 役割・処理概要 |
|---|---|---|
| `*label_001` | 140 | モジュール定義スキップ用ラベル（`goto *label_006`） |
| `*label_006` | 327 | モジュール定義スキップ用ラベル（`goto *label_008`） |
| `*label_008` | 344 | モジュール定義スキップ用ラベル（`goto *label_010`） |
| `*label_010` | 371 | モジュール定義スキップ用ラベル（`goto *label_025`） |
| `*label_025` | 492 | 初期化実行（`_init_llmod`呼出後、`goto *label_032`） |
| `*label_032` | 599 | モジュール定義スキップ用ラベル（`goto *label_035`） |
| `*label_035` | 639 | モジュール定義スキップ用ラベル（`goto *label_037`） |
| `*label_037` | 730 | モジュール定義スキップ用ラベル（`goto *label_039`） |
| `*label_039` | 745 | モジュール定義スキップ用ラベル（`goto *label_041`） |
| `*label_041` | 760 | モジュール定義スキップ用ラベル（`goto *label_061`） |
| `*label_061` | 1300 | モジュール定義スキップ用ラベル（`goto *label_066`） |
| `*label_063` | 1372 | `urlpost` 内のソケット受信ポーリングループ |
| `*label_064` | 1415 | `urlpost` 内の受信完了・ソケット切断・ファイル保存処理 |
| `*label_065` | 1422 | `urlpost` 内のエラー終了処理 |
| `*label_066` | 1434 | モジュール定義スキップ用ラベル（`goto *label_069`） |
| `*label_069` | 1479 | モジュール定義スキップ用ラベル（`goto *label_072`） |
| `*label_072` | 1543 | **メインプログラム開始点**: タイトル設定、PCSX2プロセス検出、OpenProcess、ベースアドレス検索実行 |
| `*label_073` | 1618 | **ベースアドレス確定後のメイン初期化**: ゲームポインタ解決、アドレスオフセット計算、画面0 (メインUI) コントロール構築 |
| `*label_074` | 2358 | **メインウィンドウ (screen 0) メッセージ・イベントループ**: トラックバー、入力ボックス、各ボタン変更検出とメモリ書き込み |
| `*label_075` | 3623 | フロア階数ダウンボタン「▲」押下時処理 |
| `*label_076` | 3632 | フロア階数アップボタン「▼」押下時処理 |
| `*label_077` | 3641 | アイテム出現階の重複チェック処理サブルーチン |
| `*label_078` | 3758 | モンスター出現階の重複チェック処理サブルーチン |
| `*label_079` | 3855 | 指定ダンジョンのモンスター出現テーブルアドレス読み出し |
| `*label_080` | 3876 | アイテム出現テーブルアドレス一覧（1000件分）読み出し |
| `*label_081` | 3891 | 画面中央ヘッダ（床落/店/変化/モノカ/壁、階数、シナリオ/エクストラ）表示更新 |
| `*label_082` | 3964 | アイテム大分類（14カテゴリ）の出現率メモリ読み出し |
| `*label_083` | 3979 | アイテム大分類リストボックス文字列生成・表示更新 |
| `*label_084` | 4067 | アイテム小分類（アイテム個別）の出現率メモリ読み出し・リストボックス文字列生成 |
| `*label_085` | 4178 | アイテム出現率の合計・999からの超過/不足（over表示）更新 |
| `*label_086` | 4209 | モンスター出現テーブル読み出し（出現率、レベル、モンスターID） |
| `*label_087` | 4248 | モンスター情報表示更新（`gosub *label_088`, `gosub *label_089`） |
| `*label_088` | 4252 | モンスター出現リストボックス更新およびHP/攻撃/防御/経験値計算・描画 |
| `*label_089` | 4348 | モンスター出現率over表示更新および選択中モンスターの能力・特技設定読み出し |
| `*label_090` | 4562 | 罠出現率（20種）のメモリ読み出し |
| `*label_091` | 4579 | 罠出現率リストボックス更新およびover表示更新 |
| `*label_092` | 4634 | 「アイテム読出」ボタン処理（外部binからアイテム階段データ読み出し・メモリ展開） |
| `*label_093` | 4707 | トラックバーの範囲（最小〜最大階数）再設定サブルーチン |
| `*label_094` | 4727 | 「コピー」ボタン処理（選択中種別の出現率データをメモリからバッファへコピー） |
| `*label_095` | 4749 | 「貼付」ボタン処理（バッファの出現率データをメモリへ書き込み） |
| `*label_096` | 4767 | 「クリア」ボタン処理（選択中種別の出現率を0クリア、モンスターは初期配置セット） |
| `*label_097` | 4857 | 「均割」ボタン処理（出現率を非ゼロ項目間で均等配分） |
| `*label_098` | 4903 | 「残全振」ボタン処理（出現率の合計を999に合わせる再配分を行いメモリ書き込み） |
| `*label_099` | 5000 | ダンジョンフロア構造体内の各テーブル（部屋数、店率、モンスターハウス率等）アドレスオフセット計算 |
| `*label_100` | 5030 | メモリシグネチャ検索（bsearch）によるPCSX2内ベースアドレス特定サブルーチン |
| `*label_101` | 5092 | VirtualQueryEx によるPCSX2プロセスの有効メモリリージョン列挙サブルーチン |
| `*label_102` | 5129 | 手動ベースアドレス入力ダイアログ（screen 2）の入力完了ボタン処理 |
| `*label_103` | 5135 | `oncmd gosub *label_103, 273` (WM_COMMAND): 特技/水フロア等のリストボックス選択変更ハンドラ |
| `*label_104` | 5196 | WM_COMMAND ハンドラ復帰 |
| `*label_105` | 5199 | テンポ改善設定ウィンドウ（screen 5）の非表示化処理 |
| `*label_106` | 5204 | ダンジョン共通事項編集ウィンドウ（screen 4）の非表示化処理 |
| `*label_107` | 5209 | テンポパッチ設定のメモリ読み出し・コントロール反映サブルーチン |
| `*label_108` | 5216 | 各モーションフレーム数（ポポロ/トルネコ）のReadProcessMemory |
| `*label_109` | 5256 | テンポ設定入力ボックスへの値反映 |
| `*label_110` | 5283 | 「テンポ改善パッチ」ボタン押下時処理（screen 5 表示切替・イベント開始） |
| `*label_111` | 5291 | **テンポ改善パッチ設定ウィンドウ (screen 5) イベントループ**: モーション速度書き込み |
| `*label_112` | 5386 | 最速モーション値（素手、攻撃、アイテム拾い等）のプリセット変数セット |
| `*label_113` | 5411 | 壷入れ等その他アニメーション最速化パッチのメモリ書き込み |
| `*label_114` | 5613 | 「全部 最速設定」ボタン処理 |
| `*label_115` | 5618 | 「設定 SAVE」ボタン処理 |
| `*label_116` | 5621 | 「設定 LOAD」ボタン処理 |
| `*label_117` | 5627 | テンポ改善パッチ設定ウィンドウ（screen 5）構築サブルーチン |
| `*label_118` | 5763 | ダンジョン共通事項編集ウィンドウ（screen 4）構築サブルーチン |
| `*label_119` | 5884 | ダンジョンロゴデータ（VRAM）の読み出し・プレビュー描画 |
| `*label_120` | 5907 | メニュー用・ランク用ダンジョン名文字列のReadProcessMemory |
| `*label_121` | 5930 | **ダンジョン共通事項編集ウィンドウ (screen 4) イベントループ**: 風ターン数、識別率、野良+値、ロゴ画像書き換え |
| `*label_122` | 6155 | **定期監視・パッチ維持サブルーチン**: 旅仲間固定、冒険結果ターン数表示、フェイ問モード維持、敵最速モーション書き換えトリガ |
| `*label_123` | 6329 | 敵モーションテーブル検索・書き換えサブルーチン（敵最速設定） |
| `*label_124` | 6390 | ゲーム内アイテム値段・未識別名（草・壷・指輪）の解説テキスト書き換えサブルーチン |
| `*label_125` | 6413 | ゲーム内アドレスへの文字列WriteProcessMemoryヘルパー |
| `*label_126` | 6419 | ダンジョン一覧リストボックス（Scenario/Extra表記付き）構築サブルーチン |
| `*label_127` | 6435 | 固定部屋エディタウィンドウ（screen 1）構築サブルーチン |
| `*label_128` | 6540 | 固定部屋エディタウィンドウ非表示・メイン画面復帰 |
| `*label_129` | 6555 | **固定部屋エディタウィンドウ (screen 1) イベントループ**: マップタイル描画、タイルパレット、オブジェクト配置 |
| `*label_130` | 6883` | `oncmd gosub *label_130, 276` (WM_HSCROLL): 固定部屋番号トラックバー変更通知ハンドラ |
| `*label_131` | 6898 | WM_HSCROLL ハンドラ復帰 |
| `*label_132` | 6900 | 固定部屋データサイズテーブルの計算・初期化サブルーチン |
| `*label_133` | 6918 | 固定部屋内配置オブジェクト（敵・アイテム・罠等）リストの読み出し・更新 |
| `*label_134` | 7013 | 固定部屋マップタイルのランレングス展開・画面描画サブルーチン |
| `*label_135` | 7132 | 固定部屋タイルパレット描画サブルーチン |
| `*label_136` | 7154 | 起動時パッチ適用・テンポ設定復元サブルーチン |
| `*label_137` | 7221 | モンスター種族名（全角/半角）テーブルの読み出し・ゲーム内ポインタ付け替え |
| `*label_138` | 7285 | 「番付」ボタン処理（したらば掲示板スレッドをブラウザで開く） |
| `*label_139` | 7288 | 「番付登録」ボタン処理（冒険結果・ステータス・アイテム・履歴をHTTP POST送信） |
| `*label_140` | 7503 | **ゲーム内定数・マスターデータ初期化**: ダンジョン名一覧、モンスター一覧、アイテム一覧、BGM一覧、レベル成長テーブルポインタ |
| `*label_145` | 7747 | モンスター名五十音ソート処理サブルーチン |
| `*label_146` | 7748 | ソート処理ラベル |
| `*label_150` | 7817 | 内部停止ラベル (`stop`) |
| `*label_151` | 7818 | 「開く」ボタン処理（メモリダンプbinファイルの選択ダイアログ） |
| `*label_152` | 7832 | メモリダンプbinファイルの読み込み実行部 |
| `*label_153` | 7858 | 読み込んだbinデータをPCSX2メモリへ順次WriteProcessMemoryで復元 |
| `*label_154` | 7940 | 「保存」ボタン処理（メモリデータbinファイルの新規保存ダイアログ） |
| `*label_155` | 7948 | 現在のゲームメモリを読み出してbinファイル形式にパックして保存 |
| `*label_156` | 8055 | 「上書」ボタン処理（現在読み込み中のbinファイルに上書き保存） |
| `*label_157` | 8062 | `tr3_ijiri.ini` 設定読み込み（テンポ設定メニュー内手動ロード時） |
| `*label_158` | 8118 | `tr3_ijiri.ini` 設定読み込み（アプリケーション起動時初期化） |
| `*label_159` | 8180 | `tr3_ijiri.ini` 設定保存サブルーチン |
| `*label_160` | 8212 | **終了処理 (`onexit`)**: ゲーム内テキスト復元、設定保存、プロセスハンドル解放、`end` |

---

## 2. gosub、goto の参照関係

### 2.1 `gosub` 呼び出し関係
| 呼出元行 | 命令 | ジャンプ先 | 呼出元コンテキスト | 処理内容 |
|---|---|---|---|---|
| 1550 | `gosub` | `*label_158` | `*label_072` (起動時) | 初期設定ファイル `tr3_ijiri.ini` の読み込み |
| 1575 | `gosub` | `*label_101` | `*label_072` (起動時) | VirtualQueryEx によるPCSX2メモリ領域の列挙 |
| 1588 | `gosub` | `*label_100` | `*label_072` (起動時) | メモリシグネチャ検索によるベースアドレス特定 |
| 1714 | `gosub` | `*label_140` | `*label_073` (メイン初期化) | ダンジョン・モンスター・アイテム定数データの初期化 |
| 1784 | `gosub` | `*label_118` | `*label_073` (UI構築) | ダンジョン共通事項ウィンドウ (screen 4) 生成 |
| 1785 | `gosub` | `*label_117` | `*label_073` (UI構築) | テンポ改善パッチウィンドウ (screen 5) 生成 |
| 1786 | `gosub` | `*label_127` | `*label_073` (UI構築) | 固定部屋エディタウィンドウ (screen 1) 生成 |
| 2300 | `button gosub` | `*label_139` | `*label_073` (UI構築) | 「番付登録」ボタン押下時 |
| 2304 | `button gosub` | `*label_138` | `*label_073` (UI構築) | 「番付」ボタン押下時 |
| 2340 | `gosub` | `*label_136` | `*label_073` (UI構築) | 起動時パッチ適用・テンポ設定復元 |
| 2351 | `oncmd gosub` | `*label_103, 273` | `*label_073` (UI構築) | WM_COMMAND (コントロール通知) ハンドラ登録 |
| 2406 | `gosub` | `*label_084` | `*label_074` (メインループ) | 階数変更時アイテム詳細読み出し |
| 2526 | `gosub` | `*label_099` | `*label_074` (メインループ) | ダンジョン切替時構造体オフセット再計算 |
| 2527 | `gosub` | `*label_080` | `*label_074` (メインループ) | アイテムテーブルアドレス一覧読み出し |
| 2528 | `gosub` | `*label_079` | `*label_074` (メインループ) | モンスター出現テーブルアドレス読み出し |
| 2532 | `gosub` | `*label_093` | `*label_074` (メインループ) | トラックバー最大値設定 |
| 2570 | `gosub` | `*label_090` | `*label_074` (メインループ) | 罠出現率読み出し |
| 2571 | `gosub` | `*label_091` | `*label_074` (メインループ) | 罠出現率リスト更新 |
| 2676 | `gosub` | `*label_081` | `*label_074` (メインループ) | ヘッダ文字列表示更新 |
| 2683 | `gosub` | `*label_078` | `*label_074` (メインループ) | モンスター出現階重複チェック |
| 2684 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 2735 | `gosub` | `*label_081` | `*label_074` (メインループ) | ヘッダ文字列表示更新 |
| 2736 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 2828 | `gosub` | `*label_090` | `*label_074` (メインループ) | 罠出現率読み出し |
| 2829 | `gosub` | `*label_091` | `*label_074` (メインループ) | 罠出現率リスト更新 |
| 2859 | `gosub` | `*label_082` | `*label_074` (メインループ) | アイテム大分類出現率読み出し |
| 2860 | `gosub` | `*label_083` | `*label_074` (メインループ) | アイテム大分類リスト更新 |
| 2865 | `gosub` | `*label_084` | `*label_074` (メインループ) | アイテム詳細出現率読み出し |
| 2916 | `gosub` | `*label_084` | `*label_074` (メインループ) | アイテム詳細出現率読み出し |
| 2964 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 2965 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3037 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3038 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3069 | `gosub` | `*label_082` | `*label_074` (メインループ) | アイテム大分類出現率読み出し |
| 3070 | `gosub` | `*label_083` | `*label_074` (メインループ) | アイテム大分類リスト更新 |
| 3124 | `gosub` | `*label_084` | `*label_074` (メインループ) | アイテム詳細出現率読み出し |
| 3129 | `gosub` | `*label_085` | `*label_074` (メインループ) | アイテム出現率over表示更新 |
| 3139 | `gosub` | `*label_084` | `*label_074` (メインループ) | アイテム詳細出現率読み出し |
| 3154 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3155 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3201 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3236 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3237 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3258 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 3327 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 3331 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 3335 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 3339 | `gosub` | `*label_077` | `*label_074` (メインループ) | アイテム出現階重複チェック |
| 3348 | `gosub` | `*label_079` | `*label_074` (メインループ) | モンスター出現テーブル読み出し |
| 3349 | `gosub` | `*label_078` | `*label_074` (メインループ) | モンスター出現階重複チェック |
| 3398 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3399 | `gosub` | `*label_088` | `*label_074` (メインループ) | モンスターリスト更新 |
| 3403 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3404 | `gosub` | `*label_088` | `*label_074` (メインループ) | モンスターリスト更新 |
| 3408 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3409 | `gosub` | `*label_088` | `*label_074` (メインループ) | モンスターリスト更新 |
| 3413 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3414 | `gosub` | `*label_088` | `*label_074` (メインループ) | モンスターリスト更新 |
| 3418 | `gosub` | `*label_086` | `*label_074` (メインループ) | モンスター出現データ読み出し |
| 3419 | `gosub` | `*label_088` | `*label_074` (メインループ) | モンスターリスト更新 |
| 3423 | `gosub` | `*label_087` | `*label_074` (メインループ) | モンスター情報表示更新 |
| 3473 | `gosub` | `*label_093` | `*label_074` (メインループ) | トラックバー範囲再設定 |
| 3509 | `gosub` | `*label_122` | `*label_074` (メインループ) | 定期監視・パッチ維持処理 |
| 4249 | `gosub` | `*label_088` | `*label_087` | モンスターリスト更新 |
| 4250 | `gosub` | `*label_089` | `*label_087` | モンスター能力・over表示更新 |
| 4349 | `gosub` | `*label_137` | `*label_089` | モンスター種族名テーブル更新 |
| 4872 | `gosub` | `*label_084` | `*label_097` (均割) | アイテム詳細出現率読み出し |
| 4969 | `gosub` | `*label_084` | `*label_098` (残全振) | アイテム詳細出現率読み出し |
| 4970 | `gosub` | `*label_085` | `*label_098` (残全振) | アイテム出現率over表示更新 |
| 5213 | `gosub` | `*label_108` | `*label_107` | テンポパッチ値ReadProcessMemory |
| 5214 | `gosub` | `*label_109` | `*label_107` | テンポパッチ値UIコントロール反映 |
| 5290 | `gosub` | `*label_107` | `*label_110` (テンポボタン) | テンポパッチ設定読み出し |
| 5356 | `gosub` | `*label_113` | `*label_111` (テンポループ) | 壷入れ等その他最速設定書き込み |
| 5357 | `gosub` | `*label_157` | `*label_111` (テンポループ) | 設定ファイルロード |
| 5359 | `gosub` | `*label_109` | `*label_111` (テンポループ) | コントロール反映 |
| 5362 | `gosub` | `*label_122` | `*label_111` (テンポループ) | 定期監視処理 |
| 5614 | `gosub` | `*label_112` | `*label_114` (最速設定) | 最速値セット |
| 5615 | `gosub` | `*label_109` | `*label_114` (最速設定) | コントロール反映 |
| 5619 | `gosub` | `*label_159` | `*label_115` (設定SAVE) | 設定ファイル保存 |
| 5622 | `gosub` | `*label_112` | `*label_116` (設定LOAD) | 最速値セット |
| 5623 | `gosub` | `*label_157` | `*label_116` (設定LOAD) | 設定ファイルロード |
| 5624 | `gosub` | `*label_109` | `*label_116` (設定LOAD) | コントロール反映 |
| 5646 | `button gosub` | `*label_116` | `*label_117` (テンポUI) | 「設定 LOAD」ボタン |
| 5648 | `button gosub` | `*label_115` | `*label_117` (テンポUI) | 「設定 SAVE」ボタン |
| 5650 | `button gosub` | `*label_114` | `*label_117` (テンポUI) | 「全部 最速設定」ボタン |
| 5826 | `gosub` | `*label_119` | `*label_118` (共通事項UI) | ロゴ描画 |
| 5937 | `gosub` | `*label_119` | `*label_121` (共通事項) | ロゴ描画 |
| 5938 | `gosub` | `*label_120` | `*label_121` (共通事項) | ダンジョン名読み出し |
| 5954 | `gosub` | `*label_126` | `*label_121` (共通事項) | ダンジョン一覧生成 |
| 5961 | `gosub` | `*label_124` | `*label_121` (共通事項) | アイテム解説テキスト書き換え |
| 6077 | `gosub` | `*label_119` | `*label_121` (共通事項ループ) | ロゴ描画 |
| 6125 | `gosub` | `*label_122` | `*label_121` (共通事項ループ) | 定期監視処理 |
| 6323 | `gosub` | `*label_123` | `*label_122` (定期監視) | 敵モーション書き換え実行 |
| 6393 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6396 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6400 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6402 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6405 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6408 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6411 | `gosub` | `*label_125` | `*label_124` | 文字列WriteProcessMemory |
| 6443 | `gosub` | `*label_132` | `*label_127` (固定部屋UI) | 固定部屋サイズ初期化 |
| 6515 | `gosub` | `*label_133` | `*label_127` (固定部屋UI) | 固定部屋オブジェクト読み出し |
| 6519 | `gosub` | `*label_134` | `*label_127` (固定部屋UI) | 固定部屋マップ描画 |
| 6536 | `gosub` | `*label_135` | `*label_127` (固定部屋UI) | タイルパレット描画 |
| 6537 | `oncmd gosub` | `*label_130, 276` | `*label_127` (固定部屋UI) | WM_HSCROLL ハンドラ登録 |
| 6569 | `gosub` | `*label_134` | `*label_129` (固定部屋ループ) | 固定部屋マップ描画 |
| 6602 | `gosub` | `*label_134` | `*label_129` (固定部屋ループ) | 固定部屋マップ描画 |
| 6719 | `gosub` | `*label_134` | `*label_129` (固定部屋ループ) | 固定部屋マップ描画 |
| 6737 | `gosub` | `*label_135` | `*label_129` (固定部屋ループ) | タイルパレット描画 |
| 6798 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6802 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6806 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6810 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6814 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6818 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6826 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6831 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6835 | `gosub` | `*label_133` | `*label_129` (固定部屋ループ) | 固定部屋オブジェクト読み出し |
| 6846 | `gosub` | `*label_134` | `*label_129` (固定部屋ループ) | マップ再描画 |
| 6858 | `gosub` | `*label_134` | `*label_129` (固定部屋ループ) | マップ再描画 |
| 6895 | `gosub` | `*label_134` | `*label_130` (WM_HSCROLL) | マップ再描画 |
| 7155 | `gosub` | `*label_126` | `*label_136` | ダンジョン一覧生成 |
| 7194 | `gosub` | `*label_113` | `*label_136` | その他最速設定パッチ適用 |
| 7218 | `gosub` | `*label_124` | `*label_136` | アイテム解説テキスト書き換え |
| 7524 | `gosub` | `*label_145` | `*label_140` | モンスター名五十音ソート |
| 7838 | `gosub` | `*label_153` | `*label_152` (bin読込) | メモリ初期化データ展開 |
| 7848 | `gosub` | `*label_153` | `*label_152` (bin読込) | 読み込んだbinデータ展開 |
| 7852 | `gosub` | `*label_136` | `*label_152` (bin読込) | 起動時パッチ再適用 |
| 7855 | `gosub` | `*label_124` | `*label_152` (bin読込) | アイテム解説テキスト書き換え |
| 8215 | `gosub` | `*label_125` | `*label_160` (終了時) | ゲーム内テキスト復元 |
| 8218 | `gosub` | `*label_125` | `*label_160` (終了時) | ゲーム内テキスト復元 |
| 8221 | `gosub` | `*label_125` | `*label_160` (終了時) | ゲーム内テキスト復元 |
| 8223 | `gosub` | `*label_159` | `*label_160` (終了時) | 設定ファイル保存 |

### 2.2 `goto` / `button goto` / `onexit` 呼び出し関係
| 呼出元行 | 命令 | ジャンプ先 | 呼出元コンテキスト | 処理内容 |
|---|---|---|---|---|
| 91 | `goto` | `*label_001` | ヘッダ | モジュール関数をスキップ |
| 141 | `goto` | `*label_006` | `*label_001` | モジュール関数をスキップ |
| 328 | `goto` | `*label_008` | `*label_006` | モジュール関数をスキップ |
| 345 | `goto` | `*label_010` | `*label_008` | モジュール関数をスキップ |
| 373 | `goto` | `*label_025` | `*label_010` | モジュール関数をスキップ |
| 494 | `goto` | `*label_032` | `*label_025` | モジュール関数をスキップ |
| 600 | `goto` | `*label_035` | `*label_032` | モジュール関数をスキップ |
| 640 | `goto` | `*label_037` | `*label_035` | モジュール関数をスキップ |
| 731 | `goto` | `*label_039` | `*label_037` | モジュール関数をスキップ |
| 746 | `goto` | `*label_041` | `*label_039` | モジュール関数をスキップ |
| 761 | `goto` | `*label_061` | `*label_041` | モジュール関数をスキップ |
| 1301 | `goto` | `*label_066` | `*label_061` | モジュール関数をスキップ |
| 1335 | `goto` | `*label_065` | `urlpost` | ソケットオープン失敗時エラー処理へ |
| 1385 | `goto` | `*label_064` | `urlpost` | タイムアウト時終了処理へ |
| 1402 | `goto` | `*label_063` | `urlpost` | 再受信ウェイトループ |
| 1409 | `goto` | `*label_064` | `urlpost` | 受信バッファオーバー時終了処理へ |
| 1414 | `goto` | `*label_063` | `urlpost` | 受信継続ループ |
| 1435 | `goto` | `*label_069` | `*label_066` | モジュール関数をスキップ |
| 1480 | `goto` | `*label_072` | `*label_069` | メインプログラム先頭へジャンプ |
| 1549 | `onexit` | `*label_160` | `*label_072` | アプリケーション終了イベントハンドラ登録 |
| 1607 | `button` | `*label_102` | `*label_072` (画面2) | ベースアドレス入力画面「入力終了」ボタン |
| 2264 | `button` | `*label_075` | `*label_073` (画面0) | 階数変更「▲」ボタン |
| 2266 | `button` | `*label_076` | `*label_073` (画面0) | 階数変更「▼」ボタン |
| 2274 | `button` | `*label_121` | `*label_073` (画面0) | 「ﾀﾞﾝｼﾞｮﾝ」ボタン |
| 2283 | `button` | `*label_094` | `*label_073` (画面0) | 「コピー」ボタン |
| 2286 | `button` | `*label_095` | `*label_073` (画面0) | 「貼付」ボタン |
| 2289 | `button` | `*label_096` | `*label_073` (画面0) | 「クﾘｱ」ボタン |
| 2293 | `button` | `*label_098` | `*label_073` (画面0) | 「残全振」ボタン |
| 2296 | `button` | `*label_097` | `*label_073` (画面0) | 「均割」ボタン |
| 2317 | `button` | `*label_151` | `*label_073` (画面0) | 「開く」ボタン |
| 2319 | `button` | `*label_154` | `*label_073` (画面0) | 「保存」ボタン |
| 2321 | `button` | `*label_156` | `*label_073` (画面0) | 「上書」ボタン |
| 2325 | `button` | `*label_092` | `*label_073` (画面0) | 「ｱｲﾃﾑ読出」ボタン |
| 2328 | `button` | `*label_110` | `*label_073` (画面0) | 「テンポ改善パッチ」ボタン |
| 2337 | `button` | `*label_129` | `*label_073` (画面0) | 「定部屋」ボタン |
| 2354 | `goto` | `*label_152` | `*label_073` (起動時) | 起動時既定binのロード処理へ |
| 3631 | `goto` | `*label_074` | `*label_075` | 階数変更後メインループへ戻る |
| 3640 | `goto` | `*label_074` | `*label_076` | 階数変更後メインループへ戻る |
| 4646 | `goto` | `*label_074` | `*label_092` | キャンセル時メインループへ戻る |
| 4667 | `goto` | `*label_074` | `*label_092` | 完了後メインループへ戻る |
| 4676 | `goto` | `*label_074` | `*label_092` | キャンセル時メインループへ戻る |
| 4706 | `goto` | `*label_074` | `*label_092` | 完了後メインループへ戻る |
| 4748 | `goto` | `*label_074` | `*label_094` | コピー後メインループへ戻る |
| 4762 | `goto` | `*label_074` | `*label_095` | キャンセル時メインループへ戻る |
| 4766 | `goto` | `*label_074` | `*label_095` | 貼付後メインループへ戻る |
| 4773 | `goto` | `*label_074` | `*label_096` | キャンセル時メインループへ戻る |
| 4854 | `goto` | `*label_098` | `*label_096` | アイテム右クリア後残全振ルーチンへ |
| 4856 | `goto` | `*label_074` | `*label_096` | クリア後メインループへ戻る |
| 4864 | `goto` | `*label_074` | `*label_097` | キャンセル時メインループへ戻る |
| 4868 | `goto` | `*label_098` | `*label_097` | 罠均割後残全振ルーチンへ |
| 4874 | `goto` | `*label_098` | `*label_097` | アイテム左均割後残全振ルーチンへ |
| 4887 | `goto` | `*label_098` | `*label_097` | アイテム右均割後残全振ルーチンへ |
| 4900 | `goto` | `*label_098` | `*label_097` | モンスター均割後残全振ルーチンへ |
| 4902 | `goto` | `*label_074` | `*label_097` | メインループへ戻る |
| 4999 | `goto` | `*label_074` | `*label_098` | 残全振完了後メインループへ戻る |
| 5134 | `goto` | `*label_073` | `*label_102` | 手動ベースアドレス入力完了後メイン初期化へ |
| 5156 | `goto` | `*label_104` | `*label_103` | WM_COMMAND 処理後復帰ラベルへ |
| 5175 | `goto` | `*label_104` | `*label_103` | WM_COMMAND 処理後復帰ラベルへ |
| 5194 | `goto` | `*label_104` | `*label_103` | WM_COMMAND 処理後復帰ラベルへ |
| 5203 | `goto` | `*label_074` | `*label_105` | テンポウィンドウ非表示後メインループへ |
| 5208 | `goto` | `*label_074` | `*label_106` | ダンジョンウィンドウ非表示後メインループへ |
| 5286 | `goto` | `*label_105` | `*label_110` | テンポパッチウィンドウを閉じる処理へ |
| 5616 | `goto` | `*label_111` | `*label_114` | テンポイベントループへ戻る |
| 5625 | `goto` | `*label_111` | `*label_116` | テンポイベントループへ戻る |
| 5644 | `button` | `*label_105` | `*label_117` (画面5) | テンポウィンドウ「切替」ボタン |
| 5779 | `button` | `*label_106` | `*label_118` (画面4) | ダンジョン共通ウィンドウ「切替」ボタン |
| 5933 | `goto` | `*label_106` | `*label_121` | ダンジョン共通ウィンドウを閉じる処理へ |
| 6514 | `button` | `*label_128` | `*label_127` (画面1) | 固定部屋ウィンドウ「切替」ボタン |
| 6546 | `goto` | `*label_074` | `*label_128` | 固定部屋非表示後メインループへ戻る |
| 6558 | `goto` | `*label_128` | `*label_129` | 固定部屋ウィンドウを閉じる処理へ |
| 6896 | `goto` | `*label_131` | `*label_130` | WM_HSCROLL 処理後復帰ラベルへ |
| 7670 | `goto` | `*label_146` | モジュールスキップ | モジュール関数スキップ |
| 7807 | `goto` | `*label_150` | モジュールスキップ | モジュール関数スキップ |
| 7829 | `goto` | `*label_074` | `*label_151` | キャンセル時メインループへ戻る |
| 7857 | `goto` | `*label_074` | `*label_152` | binロード完了後メインループへ戻る |
| 7944 | `goto` | `*label_074` | `*label_154` | キャンセル時メインループへ戻る |
| 8054 | `goto` | `*label_074` | `*label_155` | bin保存完了後メインループへ戻る |
| 8058 | `goto` | `*label_074` | `*label_156` | キャンセル時メインループへ戻る |
| 8061 | `goto` | `*label_155` | `*label_156` | 確認後bin保存処理へ |

---

## 3. Windows API の宣言と呼び出し箇所

### 3.1 宣言一覧 (`#uselib`, `#func`, `#cfunc`)
| DLL名 | 関数名 | 宣言行 | 呼び出し形式 | 引数定義 |
|---|---|---|---|---|
| `kernel32.dll` | `FindFirstFileA` | 2 | `#cfunc` | `var, var` |
| `kernel32.dll` | `FindClose` | 3 | `#func` | `int` |
| `kernel32.dll` | `FileTimeToLocalFileTime` | 4 | `#func` | `var, var` |
| `kernel32.dll` | `FileTimeToSystemTime` | 5 | `#func` | `var, var` |
| `kernel32.dll` | `VirtualProtect` | 6 | `#func` | `var, int, int, var` |
| `KERNEL32.DLL` | `CloseHandle` | 9 | `#func` | `sptr` |
| `KERNEL32.DLL` | `CreateToolhelp32Snapshot` | 10 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `GetProcAddress` | 11 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `GlobalAlloc` | 12 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `GlobalFree` | 13 | `#func` | `sptr` |
| `KERNEL32.DLL` | `GlobalLock` | 14 | `#func` | `sptr` |
| `KERNEL32.DLL` | `GlobalSize` | 15 | `#func` | `sptr` |
| `KERNEL32.DLL` | `GlobalUnlock` | 16 | `#func` | `sptr` |
| `KERNEL32.DLL` | `Heap32First` | 17 | `#func` | `sptr, sptr, sptr` |
| `KERNEL32.DLL` | `Heap32ListFirst` | 18 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `Heap32ListNext` | 19 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `Heap32Next` | 20 | `#func` | `sptr` |
| `KERNEL32.DLL` | `LoadLibraryA` | 21 | `#func` | `sptr` |
| `KERNEL32.DLL` | `OpenProcess` | 22 | `#func` | `sptr, sptr, sptr` |
| `KERNEL32.DLL` | `Process32First` | 23 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `Process32Next` | 24 | `#func` | `sptr, sptr` |
| `KERNEL32.DLL` | `ReadProcessMemory` | 25 | `#func` | `sptr, sptr, sptr, sptr, sptr` |
| `KERNEL32.DLL` | `VirtualQueryEx` | 26 | `#func` | `sptr, sptr, sptr, sptr` |
| `KERNEL32.DLL` | `WriteProcessMemory` | 27 | `#func` | `sptr, sptr, sptr, sptr, sptr` |
| `psapi.dll` | `EnumProcessModules` | 30 | `#func` | `int, sptr, int, sptr` |
| `psapi.dll` | `GetModuleFileNameExA` | 31 | `#func` | `int, int, sptr, int` |
| `comdlg32.dll` | `GetOpenFileNameA` | 34 | `#cfunc` | `var` |
| `comdlg32.dll` | `GetSaveFileNameA` | 35 | `#cfunc` | `var` |
| `gdiplus` | `GdiplusStartup` | 38 | `#func` | `var, var, nullptr` |
| `gdiplus` | `GdiplusShutdown` | 39 | `#func` | `int` |
| `gdiplus` | `GdipLoadImageFromStream` | 40 | `#func` | `int, var` |
| `gdiplus` | `GdipSaveImageToFile` | 41 | `#func` | `int, wstr, var, var` |
| `gdiplus` | `GdipSaveImageToStream` | 42 | `#func` | `int, int, var, var` |
| `gdiplus` | `GdipCreateBitmapFromGdiDib` | 43 | `#func` | `int, int, var` |
| `gdiplus` | `GdipCloneBitmapAreaI` | 44 | `#func` | `int, int, int, int, int, int, var` |
| `gdiplus` | `GdipGetImageWidth` | 45 | `#func` | `int, var` |
| `gdiplus` | `GdipGetImageHeight` | 46 | `#func` | `int, var` |
| `gdiplus` | `GdipImageRotateFlip` | 47 | `#func` | `int, int` |
| `gdiplus` | `GdipDisposeImage` | 48 | `#func` | `int` |
| `gdiplus` | `GdipCreateImageAttributes` | 49 | `#func` | `var` |
| `gdiplus` | `GdipDisposeImageAttributes` | 50 | `#func` | `int` |
| `gdiplus` | `GdipSetImageAttributesColorMatrix` | 51 | `#func` | `int, int, int, var, nullptr, nullptr` |
| `gdiplus` | `GdipCreateMatrix` | 52 | `#func` | `var` |
| `gdiplus` | `GdipDeleteMatrix` | 53 | `#func` | `int` |
| `gdiplus` | `GdipTranslateMatrix` | 54 | `#func` | `int, float, float, int` |
| `gdiplus` | `GdipRotateMatrix` | 55 | `#func` | `int, float, int` |
| `gdiplus` | `GdipCreateFromHDC` | 56 | `#func` | `int, var` |
| `gdiplus` | `GdipDeleteGraphics` | 57 | `#func` | `int` |
| `gdiplus` | `GdipSetWorldTransform` | 58 | `#func` | `int, int` |
| `gdiplus` | `GdipDrawImageI` | 59 | `#func` | `int, int, int, int` |
| `gdiplus` | `GdipDrawImageRectI` | 60 | `#func` | `int, int, int, int, int, int` |
| `gdiplus` | `GdipDrawImageRectRectI` | 61 | `#func` | `int, int, int, int, int, int, int, int, int, int, int, int, nullptr, nullptr` |
| `Ole32.dll` | `CreateStreamOnHGlobal` | 64 | `#func` | `int, int, var` |
| `Ole32.dll` | `GetHGlobalFromStream` | 65 | `#func` | `int, var` |
| `Ole32.dll` | `ReleaseStgMedium` | 66 | `#func` | `var` |
| `gdi32.dll` | `BitBlt` | 69 | `#func` | `int, int, int, int, int, int, int, int, int` |
| `gdi32.dll` | `SelectObject` | 70 | `#func` | `int, int` |
| `gdi32.dll` | `CreateCompatibleBitmap` | 71 | `#func` | `int, int, int` |
| `gdi32.dll` | `DeleteObject` | 72 | `#func` | `int` |
| `gdi32.dll` | `CreateCompatibleDC` | 73 | `#func` | `int` |
| `gdi32.dll` | `DeleteDC` | 74 | `#func` | `int` |
| `hspsock.dll` | `sockopen` | 77 | `#func` | `int, str, int, int` |
| `hspsock.dll` | `sockclose` | 78 | `#func` | `int, int, int, int` |
| `hspsock.dll` | `sockput` | 79 | `#func` | `bmscr, str, int, int` |
| `hspsock.dll` | `sockgetb` | 80 | `#func` | `var, int, int, int` |
| `hspsock.dll` | `sockcheck` | 81 | `#func` | `int, int, int, int` |
| `GDI32.DLL` | `GetStockObject` | 84 | `#func` | `sptr` |
| `USER32.DLL` | `SetWindowLongA` | 87 | `#func` | `sptr, sptr, sptr` |
| `USER32.DLL` | `GetWindowLongA` | 88 | `#func` | `sptr, sptr` |
| `USER32.DLL` | `SetParent` | 89 | `#func` | `int, int` |

### 3.2 主要 API の呼び出し箇所と目的
1. **プロセス操作**:
   - `OpenProcess`: 
     - 161行目: `get_all_exe_file_name` 内（各プロセス名取得用、アクセス権 `0x0410`）
     - 1573行目: `*label_072` (PCSX2プロセスアタッチ、アクセス権 `4095` = `PROCESS_ALL_ACCESS`)
   - `CloseHandle`:
     - 169, 173行目: プロセス列挙時のハンドル解放
     - 8225, 8227, 8229, 8231行目: 終了時 (`*label_160`) のPCSX2プロセスハンドル解放
   - `CreateToolhelp32Snapshot`, `Process32First`, `Process32Next`:
     - 144, 155, 171行目: `pcsx2` プロセスの検索
   - `VirtualQueryEx`:
     - 5102行目: PCSX2プロセスのメモリマップを走査し、探索対象のメモリリージョンを抽出
   - `VirtualProtect`:
     - 332行目: マシン語検索バイナリ (`var_27`) を実行可能メモリに変更 (`PAGE_EXECUTE_READWRITE` = 64)

2. **ウィンドウ・UI操作**:
   - `SetWindowLongA`:
     - 入力ボックスを数値専用スタイル (`ES_NUMBER` = 0x2000) に変更（1957, 1959, 1969, 1978, 1987, 1991, 2000, 2006, 2014, 2022, 2029, 2037, 2071, 2128, 2130, 2137, 2139, 2159, 2161, 2170, 2195, 2349, 5792, 6492, 6499, 6506行目）
     - サブウィンドウの枠線スタイル変更（5637, 5773, 6440行目）
   - `SetParent`:
     - サブウィンドウ（screen 1, 4, 5）をメインウィンドウ（screen 0）に埋め込み（5639, 5775, 6442行目）
   - `GetStockObject`:
     - フォントオブジェクト(17 = `DEFAULT_GUI_FONT`)を取得してリストボックスへ適用（2080, 2175, 2185行目）

3. **DLL動的呼び出し (llmod経由)**:
   - `keybd_event`: 2562, 2705, 2780, 2835, 2908, 2939, 2952, 3076, 3131, 3202, 3244, 3475行目でキャレット移動やコントロール更新後のキー入力シミュレーションに使用。

---

## 4. ゲームメモリへの読み書き箇所

トルネコいじりは、PCSX2内のPS2メインメモリ (32MB) 領域を探索・特定し、直接読み書きして動作します。

### 4.1 アドレス解決の仕組み
- **ベースアドレス検索 (`*label_100`, 5030行目)**:
  - PCSX2のメモリ内から特定シグネチャ `item_equipoff\0` (1583行目) 等をマシン語 `bsearch` で検索。
  - 発見した位置から、仮想PS2アドレスとPCSX2プロセス空間アドレスの差分オフセット `var_134` (ベースポインタ) および `var_136` を計算。
  - 失敗した場合は screen 2 (`*label_072`) を開き、ユーザーに手動でアドレス入力を求める。

### 4.2 主要 ReadProcessMemory 呼び出し一覧
| 呼出行番号 | 読込アドレス計算式 | サイズ (bytes) | 読み取り対象データ |
|---|---|---|---|
| 1622 - 1634 | `var_134 + 0, 4, 8, 12, 16` | 4 × 5 | 各種メインデータテーブルへのポインタ群 |
| 1719 | `var_134` | 524,288 (512KB) | 初期メモリ状態のバックアップ (`ps2tr3_org.bin`) |
| 2517, 2520 | `var_144 + ダンジョンID*4`, `*ptr` | 4, 4 | 選択ダンジョンの階数データ |
| 2524 | `var_144 + cnt*4` | 4 | ダンジョンフロアデータベースポインタ |
| 2573 - 2623 | `var_465〜494 + 112*階数` | 1〜2 | 各種出現フラグ（店率、モンハウ率、罠数、初期敵数、視界、地形、呪い率、祝福率など） |
| 3430 - 3436 | `var_622〜626(種族) + Lv*2/4` | 2〜4 | モンスター成長テーブル（HP、攻撃、防御、経験値） |
| 3654, 3685 | `var_461 + 40 + 種別*2 + 112*階数` | 2 | 各階層の出現アイテムテーブルポインタ |
| 3776, 3781 | `var_715 + cnt*4`, `*ptr + cnt*4 + 4` | 4, 4 | 各階層の出現モンスターテーブルポインタ |
| 3864, 3868 | `var_694 + cnt*4`, `*ptr + cnt*4 + 4` | 4, 4 | モンスター出現テーブル一覧 |
| 3886 | `var_694 + cnt*4` | 4 | アイテム出現テーブルアドレス |
| 3968 | `var_515 + cnt*2` | 2 | アイテム大分類出現率 (14項目) |
| 4091 | `var_515 + 30 + アイテムオフセット` | 2 | 個別アイテム出現率 |
| 4227 - 4235 | `var_543 + cnt*6 + 0/2/4` | 2 × 3 | モンスター出現データ (モンスターID、出現率、出現Lv) |
| 4300 - 4308 | `var_149 + モンスターID*24 + 0/2/4/12/20` | 1〜2 | モンスター基礎ステータス (HP, 攻, 防, 経験値, 成長タイプ) |
| 4370 - 4424 | `var_229〜238 + 24*モンスターID` | 1〜2 | モンスター属性、特技、耐性設定 |
| 4455, 4552 | `var_652 + 20*モンスターID` 等 | 4, 可変 | モンスター特技名・種族名文字列 |
| 4564, 4568 | `var_561 + 112*階数`, `var_513 + cnt*2` | 1, 2 | 罠最大数および各罠出現率 (20種) |
| 4658, 4734-4746 | `var_513`, `var_515`, `var_543` 等 | 可変 | アイテム・罠・モンスターデータのコピー用読み出し |
| 5036, 5071 | `var_700` | リージョン長 | メモリシグネチャ検索用ダンプ読み出し |
| 5211, 5219-5253 | `var_200〜216` 等 | 1 | テンポパッチ（ポポロ/トルネコ各モーションフレーム数） |
| 5421, 5483 | `var_869` | 1 | 壷入れ等その他アニメーション待機時間 |
| 5888 | `var_156` | 8,192 (8KB) | ダンジョンロゴVRAMデータ |
| 5910 - 5924 | `var_158, 160, 162` | 10〜11 | ダンジョン名称文字列 |
| 6157 - 6294 | `var_184, 185, 186, 196, 197` 等 | 1〜4 | 現在の進行状況監視（ターン数、現在階層、HP、レベル、所持金） |
| 6335 | `var_700` | 可変 | 敵モーションテーブル検索用ダンプ |
| 6700, 6706 | `var_1049 + 3 + cnt` | 1 | 固定部屋マップタイル・オブジェクトデータ |
| 6906, 6909 | `var_142, var_500` | 4, 4 | 固定部屋データポインタ（サイズ計算用） |
| 6924 - 6943 | `var_700 + cnt*20` | 1〜2 | 固定部屋内オブジェクト構造体 (敵ID, アイテムID, 座標等) |
| 7100, 7106 | `var_1049 + 3 + cnt` | 1 | 固定部屋ランレングス圧縮タイルデータ |
| 7232, 7242 | `var_700`, `var_798` | 可変, 64 | ゲーム内テキストポインタおよび文字列 |
| 7296 - 7415 | `var_187〜195, 198, 199, 971` 等 | 2〜4, 60 | 冒険結果データ（名前、到達階、勝敗、所持アイテム、履歴） |
| 7964 - 8046 | `var_134`, `var_150〜170` 等 | 524,288等 | メモリ保存 (`.bin`) のための全編集対象領域ダンプ |

### 4.3 主要 WriteProcessMemory 呼び出し一覧
| 呼出行番号 | 書込アドレス計算式 | サイズ (bytes) | 書き込み内容・機能 |
|---|---|---|---|
| 2826 | `var_513 + cnt*2 + 112*階数` | 2 | 罠出現率の変更 |
| 3067 | `var_515 + cnt*2` | 2 | アイテム大分類出現率の変更 |
| 3122 | `var_515 + 30 + オフセット` | 2 | アイテム個別出現率の変更 |
| 3153, 3199 | `var_543 + モンスターインデックス*6` | 1, 2 | 出現モンスターIDおよび出現レベルの変更 |
| 3191 | `var_550` | 可変 | モンスター種族名の変更 |
| 3223, 3234 | `var_543 + 2 + cnt*6` | 2 | モンスター出現率の変更 |
| 3250 - 3326 | `var_465〜494 + 112*階数` | 1〜2 | 店率、モンハウ率、罠数、初期敵数、視界、地形、呪い率等の変更 |
| 3347 | `var_585 + 階数*4 + 4` | 4 | フロアのモンスターテーブルポインタ付け替え |
| 3352, 3388 | `var_242, var_250` 等 | 2, 4 | モンスター個別パラメータ変更 |
| 3397 - 3422 | `var_228〜238 + 24*モンスターID` | 1〜2 | モンスターHP、攻撃、防御、経験値、成長タイプの変更 |
| 3442 - 3463 | `var_166, 168, 227, 234〜237` | 1〜2 | モンスター属性、特技確率、行動速度等の変更 |
| 3469 | `var_129` (階数ポインタ) | 4 | ダンジョン最大階数の変更 |
| 3480 - 3507 | `var_640〜653 + 20*モンスターID` | 1〜4 | モンスター姿、逆色フラグ、特技メッセージポインタ等の変更 |
| 4665 | `var_774` | 768 | 「アイテム読出」による全階層アイテムテーブルの展開 |
| 4751 - 4757 | `var_513, var_515, var_543` | 可変 | 「貼付」による罠・アイテム・モンスター出現率の復元 |
| 4780 - 4842 | `var_513, var_515, var_543` | 可変 | 「クリア」による出現率0化およびデフォルト配置書き込み |
| 4909, 4917, 4963, 4993 | `var_513, var_515, var_543` | 2 | 「残全振」による再配分後出現率の書き込み |
| 5301 - 5352 | `var_200〜216` | 1 | テンポ改善パッチ（プレイヤー各種モーションフレーム数） |
| 5448 - 5607 | `var_869` | 1 | テンポ改善パッチ（壷入れ・各種ウェイト短縮） |
| 6031 | `var_152 + ダンジョン*2` | 2 | 風までのターン数設定 |
| 6035, 6040, 6044 | `var_150 + ダンジョン*30` 等 | 2 | 道具識別率、呪い祝意識済フラグ |
| 6055 | `var_929 + cnt*2` | 2 | 野良アイテム+値テーブルの変更 |
| 6076, 6098 | `var_156` | 8,192 | 編集したダンジョンロゴ画像（VRAM）の書き込み |
| 6101 - 6107 | `var_158, 160, 162` | 可変 | ダンジョン名称文字列（メニュー用/ランク用）の書き込み |
| 6123 | `var_164` | 4 | 「敵も罠にかかる」パッチコード書き込み |
| 6159 - 6195 | `var_170〜183` | 2, 4 | 「旅仲間固定」時の仲間初期ステータス維持 |
| 6212 - 6224 | `var_700, var_160, var_162` | 可変 | 冒険結果画面への経過ターン数・タイム文字列書き込み |
| 6235 - 6281 | `var_187〜199` | 2, 4 | 「フェイ問題モード」時の各階初期化（Lv1, 所持品クリア） |
| 6361, 6388 | `var_981, var_700` | 32, 可変 | 「敵最速設定」時の敵モーションデータ一括置換 |
| 6415, 6417 | `var_700` | 可変 | アイテム値段・草/壷/指輪未識別解説テキストの書き換え |
| 6682 - 6708 | `var_1049 + 3 + cnt` | 1 | 固定部屋マップタイルのランレングス圧縮書き込み |
| 6797 - 6834 | `var_1064 + オブジェクト*20` | 1〜2 | 固定部屋内配置オブジェクト（敵・アイテム・罠）のパラメータ変更 |
| 6845, 6857 | `var_1049, var_1049 + 1` | 1 | 固定部屋の幅(X)・高さ(Y)の変更 |
| 7277, 7278 | `var_700, var_799` | 4, 32 | モンスター種族名テーブルのポインタ・文字列置換 |
| 7859 - 7936 | `var_134`, `var_150〜170, 220, 222` | 可変 | メモリファイル (`.bin`) からの全データ一括復元 |
| 8215 - 8221 | 各テキストアドレス | 可変 | 終了時テキスト復元（「今回ﾉ冒険ﾉ結果」等） |

---

## 5. 各機能ブロックの開始位置と終了位置の整理

ソースコード全体の構造を 19 の機能ブロックに分割して整理します。

| ブロック番号 | 行範囲 | 機能ブロック名 | 主な処理内容 |
|---|---|---|---|
| **Block 1** | 1 - 90 | **Windows API 外部宣言部** | kernel32, psapi, comdlg32, gdiplus, ole32, gdi32, hspsock, user32 の `#uselib`, `#func`, `#cfunc` 宣言 |
| **Block 2** | 91 - 1542 | **汎用モジュール関数群** | ファイル日時、プロセス列挙、マシン語bsearch、16進変換、llmod互換API呼出、Trackbar制御、ファイルダイアログ、GDI+画像処理、hspsock HTTP POST通信、EUC/SJIS相互変換 |
| **Block 3** | 1543 - 1617 | **初期化 & プロセスアタッチ** | タイトル設定、`tr3_ijiri.ini` ロード、PCSX2プロセス検索・`OpenProcess`、ベースアドレス検索実行、手動入力画面(screen 2) |
| **Block 4** | 1618 - 1762 | **ベースポインタ解決 & メモリオフセット計算** | 各種テーブル（フロア、罠、アイテム、モンスター、設定フラグ等）の実メモリポインタ計算、定数データ初期化呼び出し |
| **Block 5** | 1763 - 2357 | **メイン画面 (screen 0) UI構築** | トラックバー、各出現率リストボックス、各種パラメータ入力ボックス、操作ボタン類の配置、フォント・ウィンドウスタイル適用 |
| **Block 6** | 2358 - 3622 | **メインイベントループ (screen 0)** | 階数変更、ダンジョン変更、大分類/個別アイテム変更、モンスター変更、罠変更等のUIイベント検知および対応メモリの読み書き |
| **Block 7** | 3623 - 3640 | **階数切替処理 (`*label_075`, `*label_076`)** | ボタン「▲」「▼」による階数加減算とトラックバー連動 |
| **Block 8** | 3641 - 3854 | **重複チェック処理 (`*label_077`, `*label_078`)** | 同一ダンジョン・他フロアにおけるアイテムおよびモンスターの出現重複スキャン・結果表示 |
| **Block 9** | 3855 - 4633 | **メイン画面データ取得・描画サブルーチン群** | アイテム出現率読み出し(`*label_082`, `*label_084`)、モンスター情報・ステータス計算描画(`*label_086`〜`*label_089`)、罠出現率更新(`*label_090`, `*label_091`) |
| **Block 10** | 4634 - 5029 | **データ一括編集補助サブルーチン群** | 「アイテム読出」(`*label_092`)、「コピー」(`*label_094`)、「貼付」(`*label_095`)、「クリア」(`*label_096`)、「均割」(`*label_097`)、「残全振」(`*label_098`)、構造体オフセット定義(`*label_099`) |
| **Block 11** | 5030 - 5134 | **ベースアドレス特定サブルーチン群** | シグネチャ検索(`*label_100`)、VirtualQueryExメモリ探索(`*label_101`)、手動アドレス確定(`*label_102`) |
| **Block 12** | 5135 - 5282 | **メッセージハンドラ & サブ画面切替** | WM_COMMAND通知処理(`*label_103`)、テンポ画面切替(`*label_105`)、ダンジョン画面切替(`*label_106`)、テンポ設定読み出し(`*label_107`〜`*label_109`) |
| **Block 13** | 5283 - 5762 | **テンポ改善パッチ機能 (screen 5)** | テンポ改善画面構築(`*label_117`)、イベントループ(`*label_111`)、最速設定プリセット(`*label_112`)、壷入れ等高速化(`*label_113`)、全最速設定(`*label_114`) |
| **Block 14** | 5763 - 6434 | **ダンジョン共通事項編集機能 (screen 4)** | ウィンドウ構築(`*label_118`)、VRAMロゴ描画(`*label_119`)、ダンジョン名取得(`*label_120`)、風ターン数・識別率・敵罠有効・フェイ問モード編集ループ(`*label_121`)、敵最速モーション書換(`*label_123`)、アイテム未識別解説テキスト書換(`*label_124`, `*label_125`) |
| **Block 15** | 6435 - 7220 | **固定部屋エディタ機能 (screen 1)** | ウィンドウ構築(`*label_127`)、画面非表示(`*label_128`)、エディタループ(`*label_129`)、WM_HSCROLL通知(`*label_130`)、部屋サイズテーブル(`*label_132`)、配置オブジェクト読出(`*label_133`)、ランレングスマップ描画・圧縮書込(`*label_134`)、タイルパレット(`*label_135`) |
| **Block 16** | 7221 - 7284 | **モンスター種族名テーブル置換 (`*label_137`)** | 全モンスター種族名文字列を走査し、全角化および拡張領域へのポインタ付け替えを実行 |
| **Block 17** | 7285 - 7502 | **したらば掲示板「番付登録」機能 (`*label_138`, `*label_139`)** | スレッドURL起動、ゲームメモリから冒険結果（回数、ターン数、得点、名前、階数、死因、Lv、経験値、所持品、履歴）を抽出しEUC-JPに変換してHTTP POST送信 |
| **Block 18** | 7503 - 7816 | **ゲームマスターデータ・定数定義 (`*label_140`〜`*label_150`)** | 全32ダンジョン定義、全モンスター一覧(197体)、全アイテム一覧(354種)、モンスター成長テーブル、BGM一覧(64曲)、ソート関数(`*label_145`)、均割・振直関数 |
| **Block 19** | 7818 - 8232 | **ファイル入出力 & 終了処理** | メモリデータbin読み込み(`*label_151`〜`*label_153`)、bin保存/上書き(`*label_154`〜`*label_156`)、`tr3_ijiri.ini` 読込/保存(`*label_157`〜`*label_159`)、終了処理(`*label_160`) |

---

## 結論と次のステップ
本調査により、`start.hsp` の構造、全ラベルの参照関係、Win32 API依存関係、PCSX2プロセスメモリに対する全読み書き箇所、および各機能ブロックの境界が網羅的に判明しました。

次段階では、ユーザーの指示に基づき、上記で分類した機能ブロック（例: 「ベースアドレス特定・プロセスアタッチ機能」「出現率編集機能」「テンポ改善パッチ機能」「ダンジョン共通・識別率機能」「固定部屋エディタ機能」「番付登録機能」等）ごとに順次詳細な仕様解析および再開発設計を進めます。
