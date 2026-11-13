# 旧版全機能の移植計画

## 前提

根拠は `ANALYSIS.md` のBlock 3〜19、Read/Write一覧、`CORE_ENGINE_ANALYSIS.md` のデータ構造/アドレス表、および `start.hsp` の各ラベル。表の「必要なアドレス」は旧版解析からの候補で、新版に適用できる確定profileではない。とくに `var_134` / `var_136` の旧PCSX2 host-memory baseは新PINEでは使用できず、ゲームEEアドレスへの再解決が必要。

難易度: 中=構造・アドレスは比較的限定 / 高=大量の版依存ポインタ・書き込み / 非常に高=独自描画・圧縮形式・動的状態。すべて対象版の検証を含む。

## 機能別

| 機能 | 旧版の実装箇所 | データ構造 | 必要アドレス候補 | 難易度 | 新版状況 |
|---|---|---|---|---|---|
| モンスター一覧・基本能力・成長 | `start.hsp` `*label_086`〜`*label_089`, `*label_137`, `*label_140`; ANALYSIS Block 9/16/18 | 197種リスト、1種24B基本status、種族名/skill strings、12 growth tables × per-level slots | `var_149` は旧rootの `+16` から得る間接ポインタ。成長表は各タイプ基点が200B/400B stride、値は2B/4B strideで読む。旧 `0x00180000`相対式、固定EE開始位置/有効slot数は未検証 | 高 | 24-byte statusのオフライン編集に加え、成長テーブル4系列×12タイプ×最大100 slotのraw dump閲覧・編集・全体bin保存を部分移植。PINE接続・ゲーム内位置・ID/name/level対応は未実装/未検証。 |
| モンスター出現テーブル編集 | `*label_078`, `*label_086`, `*label_088`, `*label_089`, `*label_096`〜`*label_098`; Block 8〜10 | 1 entry=6B (`uint16 id,cdf,level`)、48/60/78B等。最大CDF合計999。表示weightはCDF差分 | 旧一覧 `var_314` の `0x00199EC6` 等876フロア候補、実行時はfloor/header pointerを辿る。利用者報告 `0x02670014` は意味未確定 | 非常に高 | **旧形式bin/単一PINEキャプチャのオフライン編集を部分移植**。ID/weight/level編集、平均化、CDF補正、同一行数のテーブルcopy/paste、bin export。ゲームメモリ連携・フロア候補との対応は未実装/未検証 |
| アイテム基本一覧・分類 | `*label_084`, `*label_140`; Block 9/18 | 354アイテム、14カテゴリ、IDと名前、83合成印のmaster list | 名前/IDの多くは旧HSP内静的定義。出現率table pointerは `var_147` を含む旧pointer群に依存 | 中 | **静的カタログ閲覧を移植**。検索可。ゲーム内ID照合・出現率編集は未実装 |
| アイテム出現率編集 | `*label_082`, `*label_084`, `*label_092`, `*label_094`〜`*label_098`; Block 9/10 | 768B per-table block。14×uint16 category CDF at offset0、354個別item CDF at `offset30+2*itemId`、reserved bytes `28..29` と `738..767` | 各floor+40+kind*2内のtable numberから `var_146`/`var_147` を参照。候補pointer chain未検証 | 非常に高 | **カテゴリCDFと個別item率をオフライン編集移植**。カテゴリ別weight/CDF、均等化/999補正、copy/paste、768B multi-block bin保存。reserved bytes保持。床/店/変化/モノカ/壁tableのPINE連携は未実装/未検証 |
| フロア設定編集 | `*label_099`、メインloop `*label_074`; Block 6/10 | 112B `FloorData`: room, shop/MH rate, trap, enemy count, terrain, BGM, item-table indicesなど | 各dungeon floor pointer (`var_144`) + 112*floor。基点/indirect pointers未確定 | 高 | **112-byte legacy layoutをオフライン編集移植**。既知offsetの値編集、予約bytes保持、複数floor bin入出力、選択floorのローカルcopy/paste。対象ゲームアドレス/値域/ゲーム内効果は未検証 |
| ダンジョン選択・階数・出現重複確認 | `*label_075`〜`*label_080`, `*label_093`, `*label_126`; Block 7〜9 | 32ダンジョン、最大floor表、floor pointer、重複判定 | dungeon header (`var_144`), item tables (`var_146`/`147`), monster table list (`var_314`) | 高 | 旧6-byte candidate tablesのローカル複数bin間ID重複比較を部分移植。dungeon/floor navigation・実メモリpointer scanは未実装 |
| 罠出現率編集 | `*label_090`〜`*label_091`, `*label_096`〜`*label_098`; Block 9/10 | 20種uint16 CDF（floor構造内offset候補 `0x3C`） | `var_513`=floor base+`0x3C`, 112B stride | 高 | 20 CDF値のオフライン閲覧/編集、旧算法相当の均等化/999補正を移植。罠名masterとの対応、ゲーム反映は未実装 |
| ダンジョン共通事項 | `*label_118`〜`*label_125`; Block 14 | wind turns, 32×30 identification data, wild plus, logo bitmap, names, flags | `var_152` old setting base+89224; `var_150` +87592; `var_154` +16; `var_156` +23797200; `var_164` patch. すべて版別EE profileが要る | 非常に高 | 未実装 |
| 固定部屋編集 | `*label_127`〜`*label_135`; Block 15 | dimension bytes + reserved byte + RLE tile stream、220×20B object records、137件room size/pointer table、tile palette | 旧work anchor候補 `0x005DBCA8`。room pointer/size dynamic or scene dependent可能性あり | 非常に高 | **オフラインtile map/RLE編集とraw object record編集を部分移植**。旧room payload import/export、native JSON project。object field semantics、legacy room index/size table、PINE linkageは未実装 |
| ゲームテンポ変更 | `*label_107`〜`*label_117`; Block 12/13 | player animation frame byte群、pot/item wait bytes、config values | Rev0旧 `var_136+offset`; Rev1候補 `0x20C87410`周辺ほか。PINE alias/版別値は未検証 | 高 | 未実装 |
| 各種patch: enemy quick motion | `*label_122`,`*label_123`; Block 14 | enemy motion table pattern search/replace | code/data ranges discovered via `var_700`; old host scanning不能。PINE address-range scan設計要 | 非常に高 | 未実装 |
| 各種patch: enemy hit traps | `*label_121`; Block 14 | 4B patch code (old values 177/36) | 旧候補 EE `0x002CED78` / alias | 高 | 未実装。候補未検証 |
| 各種patch: party lock / Fai question mode / result turns | `*label_122`; Block 14 | flags, character stats, dungeon state and result strings | old `var_170`等および `var_184`〜`var_199`。版差/scene dependent | 非常に高 | 未実装 |
| ゲーム内アイテム説明・名前編集 | `*label_124`,`*label_125`,`*label_137`; Block 14/16 | game character encoding, string buffers, pointer table | old name table `0x00139D00`候補は0報告; extended names buffer/pointer change needs validation | 非常に高 | 未実装 |
| 冒険結果/ランキング送信 | `*label_138`,`*label_139`; Block 17 | adventure result, items/history, EUC-JP HTTP form | old addresses `var_187`〜`var_199`, `var_971` etc; external endpoint | 高 | 未実装。旧掲示板endpointの現存性/通信仕様も調査必要 |
| メモリデータbinの読込・保存 | `*label_151`〜`*label_156`; Block 19 | 524288B dump、marker@355056、version 1..5 extension blocks + 16B gaps | `var_134`,`var_150`〜`var_170`,`var_220`,`var_222`など多数 | 非常に高 | **offline viewer/comparer/region extractorを部分移植**。旧bin game-memory restore/writeは未実装。版/CRC/region mappingとatomic backupを確立するまで禁止 |
| ini設定 | `*label_157`〜`*label_159`; Block 19 | 22/25行の順序固定値。数値/flagと追加textを含む | ローカルファイル。各行は旧HSP変数に対応 | 低 | **offline import/edit/exportを部分移植**。CP932でラウンドトリップし数値範囲/checkboxを検証。一部変数の意味は未確定。ゲーム適用なし |
| 旧データproject/設定bundle | 新アプリの作業管理 | `.tijproj` manifest + `assets/`、asset category/length/SHA-256/parent hash | project新規/再開、旧asset import、category router、各offline editor連携 | 低〜中 | **bundle作成/再開・hash検証・offline editor一式への明示route・parent-linked version保存を実装**。version一覧/diffは未実装 |
| 汎用グラフィック/文字コード/Win32 UI補助 | module functions 91〜1542、main UI | GDI+ image transforms, native controls, SJIS/EUC conversions | 通信/ゲームアドレス外 | 中 | 新版はWinForms基本UIのみ。必要な機能だけ再実装 |
| 旧HSP静的マスター一覧 | `start.hsp` `var_329`,`var_407`,`var_517`,`var_591`,`var_314` | ダンジョン32、モンスター名199、アイテム354、印83、候補テーブル876行 | テーブルの旧版ヒント値を含むが、EEアドレスとして未検証 | 低 | **実装済み**。`Data/game-catalog.json` を検索閲覧。すべて読み取り専用。解析文書のモンスター197種記載とソース199行の差異あり |
| PINE接続/ゲーム識別/汎用メモリエディタ | 新版 `PineClient.cs`, `MainForm.cs` | TCP framed command, raw bytes | PINE EE address | 中 | 実装済み。PINEレビューは [PINE_PROTOCOL_REVIEW.md](PINE_PROTOCOL_REVIEW.md) |

## 推奨移植順

1. **診断基盤**: PINE切断/target ID guard、raw readsのcapture/compare、ゲーム版profile選択を整える。
2. **モンスターread-only list**: 静的名称一覧は実装済み。次は実測キャプチャの保存・比較と、対象版/シーンごとの構造検証。データ構造が検証されるまで実メモリとの結合は禁止。
3. **フロア/ダンジョンのread-only view**: 112B stride、max floor、pointer chainを照合。
4. **モンスター出現率とアイテム出現率編集**: CDF invariant (0〜999/合計) とwrite/read-back導入。
5. **他の編集/patch**: ひとつずつread-before/write-after、保存state、アドレスと版の許可listを必須にする。
6. **固定部屋、tempo、memory bin、ranking**: 高危険/大量の依存があるため最後。外部endpointは現存確認後に扱う。

すべての機能について、旧HSPのホストアドレス計算をそのまま持ち込まない。ゲーム版ごとのPINE EEアドレスprofileに置き換える。
