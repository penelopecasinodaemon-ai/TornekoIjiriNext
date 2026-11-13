# トルネコいじり Next

2017年版「トルネコいじり」の解析をもとに、最新版PCSX2向けに作り直すためのWindowsデスクトップアプリです。旧版が使っていたPCSX2プロセスの直接走査は使わず、PCSX2のPINE IPCを介してゲーム情報とEEメモリを扱います。

## 現在の実装

- PINEへの接続（既定 `127.0.0.1:28011`）
- PCSX2ビルド、ゲームタイトル、ディスクID/CRC、ゲーム版、実行状態の取得
- EEメモリの16進ダンプ読み込み
- 対象ゲームID `SLPM-62199` に限定したバイト書き込み（書き込み前ID/状態再確認、旧値表示、最終確認、読み戻し照合）
- 解析資料に記載された5つの旧アドレス候補を読み取り、値をログに記録
- 利用者の実機キャプチャで得た16参照先候補から各64 bytesを読み取り、raw hex bytesとlittle-endian DWORDで表示（読み取り専用。意味は未判定）
- 16領域一括ダンプをゲームメタデータ付きJSONで保存（読み取り前後で接続先を照合）
- 旧HSPソースの静的マスターデータ閲覧（ダンジョン、モンスター名、アイテム、合成印、アイテム分類、出現テーブル候補。検索可能・読み取り専用）
- PINE経由の調査キャプチャ保存（JSON）と、同一アドレス・同一長の2キャプチャのBYTE/DWORD差分比較（読み取り専用）
- 旧版出現テーブルの6-byte行データをローカルで編集する実験機能（ID/重み/レベル、平均化、累積値999補正、同一行数のコピー/貼り付け、旧形式bin保存。PCSX2メモリには反映しない）
- 旧解析資料の112-byteフロアデータをローカルで編集する実験機能（既知offsetのフィールド、20罠CDF編集/平均化/999補正、予約byte保持、複数フロアbin保存。ゲームアドレスとの対応は未検証）
- 旧版768-byteアイテム出現テーブルのカテゴリCDFと354個別アイテムCDFをローカル編集（カテゴリ別重み、平均化、999補正、copy/paste、予約byte保持、bin保存。実ゲーム内tableとの対応は未検証）
- 固定部屋データのオフライン編集（旧RLEマップpayload、専用JSON project、別途220×20-byte object tableを読み込みraw field編集。object field semantics/ゲーム内アドレスとは未接続）
- 旧512 KiB memory snapshotのversion marker/extension領域オフライン閲覧、別snapshot byte差分比較、選択領域bin抽出（game memory restoreはなし）
- 旧6-byte monster spawn table複数binのID重複比較（file/row/level/CDFを表示。実floor対応は未確定）
- 旧22/25行INIのCP932 offline編集・保存（ゲームへの設定適用なし）
- 旧データを複製・分類しSHA-256で検証するoffline project bundle（INI/spawn/floor/item/status/growth/fixed-room editorから親asset付き新versionを保存可）

旧版全機能の旧実装箇所・現行状況・必要画面/操作・データ・課題は [FEATURE_PORTING_MATRIX.md](FEATURE_PORTING_MATRIX.md) を参照してください。個別アイテム率編集のテスト結果は [TEST_RESULTS.md](TEST_RESULTS.md) に記録しています。

CodexからChatGPTへ開発を引き継ぐための企画進行表は [PROJECT_PROGRESS_HANDOFF.md](PROJECT_PROGRESS_HANDOFF.md) です。開発の進捗に合わせて更新します。

旧版の全機能を移植した完成版ではありません。今回の追加は、旧HSPの静的名称・候補値を参照できるカタログ画面です。ゲーム内のデータ構造や意味、アドレスとの対応は未検証で、一覧からメモリ編集はできません。機能別の移植状況と次の工程は [FEATURE_PORTING_PLAN.md](FEATURE_PORTING_PLAN.md) を参照してください。

## 起動

Windowsと.NET 8 SDKが必要です。PowerShellでこのフォルダーから実行します。

```powershell
dotnet run
```

PCSX2を起動し、PINEのポート（既定28011）で待ち受けている状態にしてください。アプリは接続後、ゲーム情報を取得して1秒間隔で接続状態を確認します。書き込みはPINEから取得したIDが `SLPM-62199` と完全一致し、ゲーム状態がRunningまたはPausedのときだけ許可します。詳細な安全条件は [CONNECTION_SAFETY.md](CONNECTION_SAFETY.md) を参照してください。

## 旧アドレス候補の扱い

付属資料の `PINE_ADDRESS_VALIDATION.md` では、以下を含むアドレスが未検証と明記されています。アプリの「旧アドレス検証」は読み取りを実施して結果を表示しますが、結果を正しいと自動認定したり、版別アドレスとして保存したりはしません。

- `0x00139D07` モンスター名アンカー候補
- `0x00180000` 静的ポインタテーブル候補
- `0x00148EBC` ダンジョンマスター候補
- `0x00199EC6` モンスター出現テーブル候補
- `0x005DBCA8` 動的ワーク候補

タイトル/村/ダンジョンの各シーンと、無印/廉価版のCRCを記録して照合してから、機能別編集を有効化してください。汎用メモリエディタの書き込みもセーブデータやゲーム状態に影響するため、内容確認ダイアログを必須にしています。

## 実装メモ

- PINEのTCPフレームとコマンド番号はPCSX2公式実装を参照しています。
- メモリ操作はPS2 EEアドレスに対するbyte read/writeです。旧Windows版のPCSX2ホスト仮想アドレスやプロセスIDは使用しません。
- 小さな要求を1つのPINEフレームにまとめて通信します。連続領域のread/writeは最大4096 bytesです。
- `.bin` の旧版メモリ保存形式は、アドレス版差やフィールド境界が確定するまで復元機能を実装しません。

## 参照資料

提供された `ANALYSIS.md`、`CORE_ENGINE_ANALYSIS.md`、`PINE_ADDRESS_VALIDATION.md` と `start.hsp` を仕様調査の入力にしました。資料内の過去の指示文や検証計画は、今回の依頼に対する追加指示としては扱っていません。

旧ソースから抽出した静的カタログは `Data/game-catalog.json` に格納し、ビルド出力にもコピーします。解析資料はモンスター名197種としていますが、`start.hsp` の `var_407` リテラルには空行を除いて199行があり、カタログにはソース行をそのまま保持しています。どちらが実ゲームの有効なID範囲かは未確定です。


### 汎用バイナリ編集（オフライン）

旧データプロジェクトで分類を「その他」にして `.bin` / `.dat` / `.raw` / `.sav` / `.dmp` を取り込むと、汎用バイナリエディタで開けます。最大64 MiB、4096 bytes単位のページ表示、byte単位の16進数編集に対応し、未編集領域は元のまま保持します。プロジェクトから開いた場合は元assetを上書きせず、親SHA-256を記録した新versionとして保存します。ゲーム/PINEへの書き込み経路はありません。

### 既存キャプチャ形式の互換性

現行のFormatVersion 1 single/batch captureに加え、同梱の `live_readonly_verification_*.json` と `pine_readonly_*.json` の旧診断JSONもプロジェクトへ取り込めます。旧診断JSONは、保存されているPointerTable / PointerTargets64Bytes / PreviousProbePoints / Regions / Pointsをraw領域として取り込みます。長さが省略された旧形式は16進byte列の長さから算出し、アドレス・長さ・hex文字列・32-bit範囲を検証します。これは互換取り込みであり、各領域の意味や構造を認定するものではありません。
