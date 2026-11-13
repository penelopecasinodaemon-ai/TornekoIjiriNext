# 第2段階テスト結果

更新日: 2026-11-13

## 結果概要

- **Release build**: 成功。`bin/Release/net8.0-windows/TornekoIjiriNext.dll` を生成。警告0、エラー0。
- **偽PINE統合ハーネス**: 23項目PASS。localhost上の模擬サーバーで公式PINEのTCP frame/opcode/LE response形式を使い、`PineClient`の読書きと内部拒否条件を確認。
- **実機PCSX2 v2.8.2 / SLPM-62199**: read-onlyで確認済み（2026-11-13）。PINE status、version、title、ID、CRC、game versionを取得し、EEメモリの測定範囲を読み取った。メモリ書き込みは未実施。
- **GUI画面の自動/手動操作テスト**: 未確認。状態表示とUI制御はコード/ビルド確認。実PINE環境の画面操作は未実施。

## 2026-11-13 実機読み取り接続の再試行

- ユーザー報告: PCSX2 v2.8.2でSLPM-62199「異世界の迷宮」1ターン目。
- 標準PINE TCP port `127.0.0.1:28011` の待受を確認できず、PINE通信でゲームID/CRCやメモリを取得できなかった。
- Windowsアプリ一覧にはPCSX2のウィンドウ情報があったが、画面キャプチャは対象PCSX2ではない画面を返し、対象画面の確認に失敗。実ゲーム画面・実機メモリは未確認のまま。
- メモリ書き込みは一切実行していない。過去のユーザー提供キャプチャ/測定値を今回の実機確認結果として流用していない。

## 2026-11-13 PINE read-only実機確認

- ユーザー報告の稼働中環境にloopback TCP `127.0.0.1:28011` で接続。公式PINE framing/opcodeを使ってstatus、PCSX2 version、title、game ID、UUID/CRC、game versionを取得。
- 取得値: PCSX2 `PCSX2 v2.8.2`、Game ID `SLPM-62199`、CRC `e199f75d`、Game version `1.04`、status before/after `0/0` (running)。一括読み取り前後でID/CRC/version一致。
- `0x00180020` を64 bytes再読込し、同一環境の以前のJSONキャプチャと完全一致。16個の参照先を各64 bytes読み、添付された以前の実機テキストと比較した結果、全16領域で64/64 bytes一致。
- 利用者の既報値も再現: `0x00139D00=0`, `0x20139D00=0`, `0x00148EBC=0xF8035904`, `0x00199EC6=0x02670014`, `0x005DBCA8=150`, `0x205DBCA8=150`。
- 証跡: [`captures/pine_readonly_20261113_171923.json`](captures/pine_readonly_20261113_171923.json) (基点・測定点) と [`captures/pointer_batch_SLPM-62199_20261113_082040.json`](captures/pointer_batch_SLPM-62199_20261113_082040.json) (16×64 bytes)。すべてread-only。値の意味・データ構造・対象ゲーム内の意味は未確定。
- 実ゲーム画面の見た目/現在フロアはアプリ画面から独立に確認していない。ゲーム状態はstatus=runningを確認。TornekoIjiriNext本体のGUI操作確認は未実施で、PINEフレームを使ったread-only検査結果として記録。

## 実行した偽PINEテスト

| テスト | 結果 | 検証内容 |
|---|---|---|
| PINE endpointなし | PASS | 空きloopback portへのConnectがSocketExceptionとなり接続を拒否 |
| PINE status | PASS | Running status 0をLE uint32で取得 |
| PCSX2 version | PASS | opcode 8の文字列取得を保持 |
| 対象IDメタデータ | PASS | opcode 11〜14でタイトル/ID/CRC/ELF版取得 |
| EE read | PASS | MsgRead8を連結し指定byte列を返す |
| 対象ID write/read-back | PASS | SLPM-62199で書き込み、読み戻し一致を確認 |
| 別/非完全一致IDのwrite | PASS | `SLPM-OTHER`と末尾空白のIDで例外拒否、mock memory不変 |
| 空IDのwrite | PASS | 空IDで例外拒否、mock memory不変 |
| Shutdown status write | PASS | status 2で拒否、mock memory不変 |
| 変更前値の競合 | PASS | confirmation時の旧値と実書込直前値が異なると拒否 |
| 書込み前のID再確認 | PASS | old value read後にmock IDを別IDへ切替え、書き込み前の2回目確認で拒否 |
| read-back mismatch | PASS | mock writeを無視すると期待値不一致例外となり、成功を返さない |
| ゲーム終了後status | PASS | PINE socket維持でstatus 2を読み取れる |
| TCP切断 | PASS | server socket close後のstatus要求が例外となり切断を検知 |

上表に列記した条件を含め、個別 assertion 23件がPASS。読み取り/書き込み確認と「不正条件でmock memoryが変更されない」確認はそれぞれ別 assertion として数えた。テストコードは一時的な `work/pine-safety-harness` に置いており、アプリ本体の依存関係には追加していない。

## 実機で未確認のシナリオ

| シナリオ | 状況 |
|---|---|
| ゲーム未起動のPCSX2 | PINE status=2をmockで確認。実PCSX2の挙動は未確認 |
| PINE未接続/PCSX2未起動 | 空きloopback portで接続失敗を確認。実際のアプリGUI上の表示は未確認 |
| SLPM-62199実機接続 | read-only確認済み: v2.8.2 / CRC e199f75d / 1.04 / status 0。ゲームメモリ書き込みは未確認・未実施 |
| 別ゲームのPINE接続 | mock IDで書き込み拒否は確認。実ゲーム接続は未確認 |
| 実ゲーム終了後 | mock status 2とsocket切断を確認。実PCSX2終了時のUI遷移は未確認 |
| 旧アドレス読み取り | PINE実機で0x00180020、16参照先、既報6地点をread-only再読込し、前回測定に一致。各値の意味とwriteは未確認 |
| 書き込み前ID再確認 | mockで遅延中ID切替を確認。実ゲームの急な切替試験は未確認 |

## Offline model tests (2026-11-13)

- 合成データで一時console harnessを実行し、個別率11項目、重複比較6項目、snapshot model 15項目、旧INI model 8項目、project bundle/router 26項目の計66 assertionsがPASS。
- 個別率: 複数768-byte block、カテゴリ内CDF差分、カテゴリ境界のreset、LE書込み、別category/block保持、reserved bytes保持、末尾ID 353 offset、999 normalize、値域/合計超過拒否。
- 重複比較: IDの集約、複数table/同一table区別、file/row/level/CDF位置表示、不完全6-byte entry拒否、同名source拒否。
- snapshot: v1〜v5ごとのextension count、v5 markerと18 extension region、offset/16-byte gap、読み取り/抽出、同一/差分bytes、0x55 sentinel、unknown markerの非推測動作、512KiB境界拒否。
- 旧INI: 22/25行の位置対応、数値編集round-trip、checkboxと3桁値の範囲拒否、CP932日本語文字列のimport/export、25行目追加flagのimport検証、行数不足拒否。
- 実ゲームの個別item-rate semantics、PINEからの対象block特定、旧bin実ファイルのGUI確認、binの実機反映は未確認。ゲームメモリwriteは行っていない。
- Release build成功、警告0、エラー0。

## 旧INI offline editor追加確認

- `LegacyIniModel` と `LegacyIniForm` を追加し、旧HSPの22/25行設定を読み込み、編集、旧形式で保存できるようにした。CP932を維持し、checkboxは0/1、旧3桁入力は0〜999、tempo byteは0〜255に制限する。
- 追加した8件を含むoffline model checksは40 assertions PASS。Release build成功、警告0、エラー0。
- 旧INIの一部項目の意味は未確定。ゲーム内反映やPINE経由での適用はなく、ファイル操作のみ。

## 旧データproject bundle

- offline model checksを更新し66件すべてPASS。bundle作成/import/hash/reopen/tamper検知/path traversal拒否、parent-linked version、履歴lineage、byte差分、hash不整合時の比較拒否、循環履歴拒否、各editorのcategory/extension/length dispatchを検証。
- Project内のINI/spawn/floor/item-rate/status/growth/fixed-room assetを各既存editorへ渡し、編集結果を親asset/hash付き新versionとしてmanifest登録する経路を追加。parent relation再openもmodel test。WinForms画面操作は未確認。
- Project bundleから全offline editorへの明示routerを追加。拡張子/category mismatchと4400-byte object table以外の曖昧routeを拒否する13件のrouter model checksを実行。
- Release build成功、警告0、エラー0。実旧版データ一式での作業再開は未確認。
- テストデータは作業用フォルダー内に作成し、検査後に削除した。ゲームメモリ/PINEへの通信・書き込みなし。

`PINE 未接続` は `PCSX2` の表記誤りを含まず、local mock接続だけがテスト対象。socket connection failureは初回接続時に「接続エラー」と表示する設計である。既存の汎用メモリリーダーと旧候補読み取りコードは維持し、接続中のread byte response長検査を加えた。

## ビルド記録

コマンド: `dotnet build .\TornekoIjiriNext.csproj -c Release -p:UseSharedCompilation=false`

結果: 成功、警告0、エラー0。環境でMSBuild server接続がtimeoutし、CLIがin-process buildへfallbackしたログはあるが、Release build自体は正常終了した。

## 2026-11-13 PINE read-only実機再確認 (18:14 JST)

- ユーザーが起動中のPCSX2 v2.8.2のゲーム画面を確認し、公式PINE `127.0.0.1:28011` へ既存 `PineClient` でread-only接続した。PCSX2 version/title/game ID/CRC/game version取得に成功。
- 取得値: `PCSX2 v2.8.2` / トルネコの大冒険3 / `SLPM-62199` / CRC `e199f75d` / game version `1.04` / status before/after `0/0` (Running)。
- `0x00180020` 64 bytesから16個のtarget addressを得た。以前報告された16値に全一致し、16個のtarget各64 bytesも保存済み前回captureと16/16領域でaddress+raw bytes完全一致。
- 既報probe 6か所は値をすべて再現: `0x00139D00=0`, `0x20139D00=0`, `0x00148EBC=0xF8035904`, `0x00199EC6=0x02670014`, `0x005DBCA8=150`, `0x205DBCA8=150`。基点64-byte raw captureも保存済みcaptureと完全一致。
- 証跡: [`captures/live_readonly_verification_20261113_091453.json`](captures/live_readonly_verification_20261113_091453.json)。status/ID/CRC/versionをread前後で確認。PINE write opcodeは発行していない。
- これは実機PCSX2へPINE protocolを使ったread-only確認であり、TornekoIjiriNext GUIから接続/操作した試験ではない。アプリGUIでの実機接続操作は未確認。取得値の構造/意味は依然未確定。


## PINE capture project workspace (2026-11-13)

- 単体captureとbatch capture schema v1、必須metadata、32-bit address境界、HEXとlength一致をmodelで検証。project bundle画面からJSONを一括取込みし、game ID/CRC/version/PCSX2版/時刻を保持する。
- 比較画面は共通のregion address/length配置を確認してから、各regionのraw bytesを比較する。JSON文字列差分を測定byte差分と誤認しない。
- 合成single/batch capture、不正byte長拒否を含む75 assertions PASS。Release build成功、警告0・エラー0。実captureのWinForms操作と実機比較は未確認。modelで提供single JSONと16 region batch JSONのID/CRC/領域数/長さを検証できた。今回PINE/ゲームメモリ通信なし。

## Project asset履歴・差分 (2026-11-13)

- ProjectBundleHistoryFormを追加し、assetの祖先/子孫履歴から2版を選択してoffset別BYTE差分を閲覧できる。長さ、変更byte総数、変更前後値、追加/削除byteを表示し、先頭2,000件への切り詰めを明示する。
- 比較前に双方のmanifest長/SHA-256を検証し、循環parent graphはmanifest Open時に拒否する。
- 追加5 assertionsを含む計75 assertions PASS。Release build成功、警告0、エラー0。WinForms操作とPCSX2実機確認は未実施。今回PINE/ゲームメモリ通信なし。


## Capture region comparison model (2026-11-13)

- 領域対応判定、ゲームidentity一致、regionごとの変更byte数/offset/前後値/表示上限をUI非依存modelへ分離した。領域配置不一致時は値を比較しない。
- 実際のユーザー提供single capture JSONと16×64-byte batch capture JSONをmodelから開き、schema、CRC、address、region個数/長さを検証。合成single-vs-batch、CRC/layout mismatch、truncate動作も確認。
- 合計75 offline model checks PASS。Release build成功、警告0・エラー0。PCSX2実機確認とGUI操作は今回未実施。


## Capture project UI launch attempt (2026-11-13)

- Built Release app launch was attempted to verify the project capture import/compare controls. Computer-use did not expose a TornekoIjiriNext window; UI operation could not be performed. The temporary dotnet-hosted inspection process was stopped. No PCSX2/PINE interaction occurred.
- Model tests and Release build remain the verified checks; the WinForms capture import/compare workflow is still unconfirmed until the app window can be opened through the desktop session.
