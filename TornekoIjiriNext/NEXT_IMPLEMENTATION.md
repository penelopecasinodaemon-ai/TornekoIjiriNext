# 次の実装対象: 読み取り専用データ診断workspace

## 進捗更新 2026-11-13

旧INIの22/25行形式をCP932でimport/edit/exportするoffline画面を追加。旧HSP変数順を維持し、既知の数値範囲とflag値を検証する。意味が曖昧な箇所は注記付きで表示し、PINE適用は行わない。

`.tijproj` manifestと`assets/`に旧データを複製してまとめるoffline project bundleを追加。ファイル分類、サイズ/SHA-256記録、欠落・改変・path traversal検出を実装。旧INI/spawn/floor/item category+individual/monster status+growth/fixed-room map+object table assetを、明示分類と形式検査後に既存editorへ渡し、元を保持した親filename/hash付きedited versionをmanifestへ登録できる。asset履歴とhash検証付きbyte差分画面を追加（WinForms操作は未確認）。

固定部屋エディタは、地図tile描画/編集、旧RLE tile streamのdecode/encode、旧payload容量制限、新規JSON project保存に加え、220×20-byte object tableのraw field offline editorを追加した。object field semanticsとroom index/size pointerを対象EE profileへ結びつける根拠がないため、ゲームには接続しない。

アイテム出現率768-byte blockのうち、先頭14カテゴリCDF (28 bytes) のオフライン編集画面を追加した。旧版相当の平均化/合計999補正を実装し、残り740 bytesは保持する。個別アイテムoffsetの対応関係は旧ソースの根拠が一致しないため保留。

フロア設定の旧112-byte layoutをオフライン編集する画面を追加。field values、罠CDF20の均等化/999補正、複数レコードの選択、予約byte保持、旧形式bin保存に対応した。対象ゲームの構造/アドレスprofileは未確定のため、ゲームメモリとは未接続。

旧HSPの出現テーブル形式（6-byte entry）のオフライン編集画面を追加した。旧版コードに沿ってmonster ID / weight / level編集、有効行の平均化と累積値999補正、legacy bin保存ができる。ただしゲームメモリへの反映は未実装。

また、PINE read-onlyキャプチャのJSON保存・BYTE/DWORD比較機能、利用者実測の16領域各64 bytesの読み取り・共通/差分byte要約・batch JSON保存を追加した。16領域の貼り付け結果では63/64 byte offsetが一致し、先頭byteだけが違う観測だった。アドレス意味の確定、異なるゲーム状態での実機再取得、モンスター一覧化は未完了。

## 提案

capture JSONのschema検証、project bundle取込み、metadata表示、同じ領域配置を持つsingle/batch capture間のread-only比較workspaceを実装した。modelから実提供single captureと16領域batch JSONのschema parseを確認済み。次はそれらのcaptureをbundleへ取り込み、WinForms操作でmetadata/region比較と再openを確認する。旧編集器とcapture workspaceはmodel test済みだが、WinForms実操作は未確認。モンスターのゲーム内一覧はpointer/record/name対応が確定するまで実装済みと扱わない。

## 現在分かっていること

- 旧版はモンスター一覧197件（index 0〜196）を持ち、種族名テーブルとメモリ上の名前アドレス情報を別管理している (`start.hsp` `*label_140`, `*label_137`; CORE_ENGINE_ANALYSIS 4.2)。
- 旧版の `var_149` は `var_134 + 16` からReadProcessMemoryで読み出したポインタをPS2アドレス上位byte補正して得る、と解析資料にある。
- 旧版解析では1体24 byteの `MonsterStatus` として、HP、攻撃、防御、flags、speed、family、勧誘率、経験値、skill、drop、growth typeを記述している。
- 旧版コードは `var_149 + monster_id * 24` からフィールドを読み取る。ただしこれは旧版対象ビルドの構造。`SLPM-62199` の実測版で同じ構造・ポインタ位置とは未確認。
- 利用者報告の `0x00180000 -> 0x00180020` と `0x00180020` からの16値は実測値だが、16値のどれがモンスターマスターポインタか未特定。
- 旧HSP内の種族名文字列配置候補 `0x00139D00` は今回の利用者報告では0。ここを名前tableの根拠にできない。

## 具体的手順

### 1. 再現可能な検証UI

- `SLPM-62199` をPINEから読み、title/ID/CRC/ELF versionを画面とログへ保存できるようにする。
- 現在自動検証ボタンに不足する候補を追加: `0x20180000`、`0x20139D00`、`0x205DBCA8`、`0x00180020` から64 byte。
- 読み取り結果を16進bytesとLE uint16/uint32の両方で表示する。PS2ゲームを一時停止してから読む手順を示し、running/paused stateも記録する。
- 各実測行にゲームID、CRC、ELF版、シーン、PINE version、アドレス、読み取り幅を付与する。元のraw bytesは保持。

### 2. ポインタ候補の特定

- `0x00180000` とaliasからroot値を確認する。
- `0x00180020` の16 dwordを各々EE RAM内ポインタとして有効性・整列・近傍内容を読む。値が16MB/32MB範囲のoffsetか、KSEG形式かなどを記録する。
- その中の候補データを複数の既知モンスターIDで比較し、数値妥当性だけでなくレベル別の値や属性の変化で役割を裏付ける。
- 書き込みはこの診断段階では無効。開始前/後に保存状態を作り、再読み取りで安定性を確認する。
- 現行アプリでは「実測参照先16領域を読み取り」を押すと、報告済みの全16アドレスを各64 bytes読み、raw hex/DWORD表示を開く。出力をテキスト選択してコピーし、ゲーム内シーン・階層・状態も一緒に記録する。

### 3. 名前とIDの対応確認

- 旧HSPの `var_407` 名リスト、`var_549` のゲーム内名アドレス/長さ、`var_137` の読み替え処理を抽出する。
- 既知のゲーム表示名を照合し、Shift-JISか独自文字コードか、NUL/固定長/ポインタtableかを実データで判別する。
- `0x00139D00 == 0` だった値を成功アンカーとして扱わない。別profile/別候補を探索する場合もスキャン結果を証拠として保存する。

### 4.  read-only一覧

ゲームID/版ごとに構造が確定したら、PINE memory backend、版別アドレスprofile、binary parser、viewmodel、一覧UIを分離して追加する。各データ行はraw address/bytesとdecode結果を追えるようにし、未定義フィールドは未知として表示する。モンスターspawn entry（6 bytes: id/cdf/level）は基本status tableと別概念なので混同しない。

### 5. 合格条件

- 対象ID/CRC/ELF versionの実測記録がある。
- 197 IDすべてについて境界内・非重複/重複理由が説明でき、known entriesをゲーム表示または別資料で照合した。
- 別シーン/複数起動でポインタのstatic/dynamic性を確認し、profile選択が機能する。
- read-onlyで再起動可能な繰り返し確認済み。以上を満たすまでは「データ一覧UIの試作」とし、「確定/実装済み」と報告しない。

## 難易度と次の区切り

難易度: 高。メモリread APIは存在するものの、実データ表のpointerとゲーム版が未確定。まず「アドレス候補をrawでまとめて読んで記録する診断機能」を小さく実装し、実測確定後に一覧パーサーとUIを作る。
# 次の移植工程

## 実装済みのオフライン工程（今回）

旧HSP `start.hsp` の `var_149` を基準とする24-byteモンスターステータスrecordについて、旧ソースが参照するoffsetに沿ったローカル閲覧・編集・bin保存画面を追加した。予約byteは保持される。現行PCSX2メモリ上の開始位置、recordと名前/IDの対応、値の意味はゲーム実機との照合が済むまで確定扱いにしない。PINE読み書きとは接続していない。

次は、ユーザー採取の対象ゲームキャプチャ内で候補offsetを読み、ゲーム表示値や操作前後差分と比較する。実機比較の根拠がそろうまでは、ゲームメモリからのモンスター能力一覧・編集は実装しない。

さらに旧HSP `var_622/623/624/626` の成長テーブルについて、12タイプを選び、タイプごとの最大100 slotをraw dumpから表示・編集するオフライン画面を追加した。ソースではタイプ別ベース間隔が200/400 bytes、slot読み出し間隔が2/4 bytes。前版画面のタイプ/slot解釈に誤りがあり本版で修正した。次に必要なのは `var_143` を含む開始アドレス計算と有効slot数を実測で確定し、PCSX2から読んだ領域と旧HSP表示を照合すること。固定アドレスを仮定したPINE読取は行わない。

静的な旧版マスターカタログに加え、PINE read-onlyのキャプチャ保存・比較画面を実装した。次はモンスターのゲーム内データを扱うが、既存のユーザー実測値だけではデータ構造を特定できないため、ゲーム内状態を変えた複数のキャプチャを比較して相関を調べる。

1. 実装済み: PINE read-onlyで指定範囲を読み取り、アドレス・長さ・ゲーム識別情報・取得時刻・raw bytesをJSON保存する。
2. 実装済み: 2つのキャプチャをBYTE単位・DWORD little-endian単位で比較する。
3. 実機でタイトル、村、ダンジョン、異なるフロア/状態ごとに採取したデータを照合する。データの意味はユーザーによるゲーム内変化との相関が取れてから記述する。
4. モンスター領域の開始位置、レコード長、ID範囲、名前テーブルとの対応が再現可能な形で確認できてから、モンスター基本データを読み取り一覧に結びつける。

**完了条件:** アドレスと構造がゲームID/CRCごとの根拠付きで検証され、読取値がゲーム内表示・既知操作と整合し、非対象ゲームでは無効化される。これを満たすまでモンスター一覧を「ゲームメモリから読み取り済み」と表示しない。

その後の順序は [FEATURE_PORTING_PLAN.md](FEATURE_PORTING_PLAN.md) の推奨移植順に従う。未検証の旧版patch、固定部屋、セーブ領域一括復元は、個別の構造確認・バックアップ・書き込み範囲制限が整うまで移植しない。

## 実装優先の進捗

- 全旧版機能の画面/操作、必要データ、実装状況、課題を [FEATURE_PORTING_MATRIX.md](FEATURE_PORTING_MATRIX.md) に整理した。
- `start.hsp` の354 item ID と `offset 30 + 2*ID` を根拠に、個別アイテム率のオフライン編集を実装。category CDF editorとは別画面で、source catalog item names/IDsを表示し、複数768-byte block、category reset CDF、999調整を扱う。
- 旧6-byte spawn tableの同一/複数table間ID重複比較画面を実装。次はcategory/individual item率とfloor dataをひとつのlocal projectに束ね、再読込・diff・編集履歴を扱えるようにする。
- 旧512KiB memory snapshotはoffline viewer/comparer/region extractorを実装。次は旧version marker別container testを実データで確認し、local-only snapshot projectへ進む。ゲームmemory restoreは許可しない。

