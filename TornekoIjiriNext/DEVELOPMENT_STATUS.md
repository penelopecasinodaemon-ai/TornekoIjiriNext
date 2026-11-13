# 開発状況

更新日: 2026-11-13

## 調査対象

既存アプリ `TornekoIjiriNext.csproj` と、そのソース `Program.cs`、`MainForm.cs`、`PineClient.cs`、`app.manifest`、`README.md` を確認した。生成物は `bin/` と `obj/` にあり、ソースではない。ビルド方式は SDK-style .NET 8 (`net8.0-windows`) の WinForms、NuGet外部依存なし。指定資料 `ANALYSIS.md`、`CORE_ENGINE_ANALYSIS.md`、`PINE_ADDRESS_VALIDATION.md` と逆コンパイル元 `start.hsp` の機能区分・該当アドレス定義も参照した。

## 実装済み

| 機能 | 実装位置 | 状態 |
|---|---|---|
| PCSX2 PINE TCP接続 | `PineClient.Connect`、`MainForm.ConnectPineAsync` | 実装済み。既定は `127.0.0.1:28011` |
| PCSX2/ゲーム情報取得 | `PineClient.GetPcsx2Version/GetGameInfo/GetStatus` | PINE opcode 8, 11〜15でタイトル、ID、CRC、ゲーム版、実行状態を表示 |
| EEメモリの読み取り | `PineClient.ReadMemory` | 1〜4096 byteをMsgRead8でまとめて要求、16進表示 |
| EEメモリの書き込み | `PineClient.SafeWriteMemory`、`MainForm.WriteMemoryAsync` | ID `SLPM-62199`・稼働状態を内部再検査し、旧値確認、最終確認、read-back照合。UIと通信層の両方に許可ゲートあり |
| 旧アドレス確認 | `ValidateLegacyAddressesAsync` | 旧資料の候補5件を読み取り。値の意味は未確定 |
| 実測参照先read-only dump | `ReadPointerTargetsAsync` | 利用者実測16アドレスから各64 bytes、領域間差分要約、ゲーム情報確認、JSON保存 |
| 調査キャプチャ | `MemoryCaptureForm` | PINE read-only capture JSONの作成、BYTE/DWORD比較 |
| 旧マスター閲覧 | `CatalogBrowserForm` | dungeon/item/monster/seal等の旧HSP静的値を検索。game memoryとは未結合 |
| モンスター出現テーブル | `MonsterSpawnTableForm` | 6-byte legacy tableのオフライン編集、平均/999補正、copy/paste、bin保存 |
| モンスター重複比較 | `MonsterSpawnCompareForm`, `MonsterSpawnDuplicateScanner` | 複数6-byte binの同一/複数table間ID重複をrow/level/CDF付きで表示 |
| モンスター基本能力/成長 | `MonsterStatusEditorForm`, `MonsterGrowthTableForm` | legacy raw dataのオフライン編集。PINE未接続 |
| フロアデータ | `FloorDataEditorForm` | 112-byte legacy layoutのオフライン編集、罠CDF20処理、reserved byte保持 |
| アイテムカテゴリ率 | `ItemCategoryRateForm` | 768-byte block先頭14 CDFのオフライン編集。残り740 bytes保持 |
| 個別アイテム率 | `ItemRateTableModel`, `ItemIndividualRateForm` | 354 source IDsのカテゴリ内CDF/重みをオフライン編集。offset30+ID×2、reserved bytes保持 |
| 固定部屋 | `FixedRoomMapForm` | RLE mapとraw object recordsのオフライン編集 |
| 旧snapshot | `LegacyMemorySnapshotModel`, `LegacyMemorySnapshotForm` | versioned 512KiB legacy binをオフライン閲覧、比較、領域抽出。memory restoreなし |
| 旧INI設定 | `LegacyIniModel`, `LegacyIniForm` | 22/25行形式をCP932でオフライン取込・編集・保存。数値/flagを検証。ゲーム適用なし |
| capture project workspace | `ProjectCaptureModel`, `ProjectCaptureCompareForm`, `ProjectBundleForm` | PINE capture schema v1 single/batchを検証してgame metadata/region/raw bytes付きでbundleへimportし、同一region配置のcaptureをread-only BYTE比較。GUI操作未確認 |
| 旧データproject | `ProjectBundleModel`, `ProjectBundleForm`, `ProjectBundleEditorRouter`, `ProjectBundleHistoryForm` | INI/spawn/floor/item category+individual/status+growth/fixed-room map+object tableを対応editorで開き、親hash付きnew version保存、履歴一覧とSHA-256検証付きBYTE差分。PINE非接続。WinForms操作は未確認 |
| Windowsアプリの起動 | `Program.Main` | WinFormsエントリーポイント |

## 未移植の主要機能

**ゲーム内memoryとの対応付けが未実装**の機能は、dungeon/floor pointer navigation、floor duplicate scan、monster/item editorと実ゲームblockとの接続、tempo/patch/string editing、ranking upload、bulk memory restoreなど。オフラインUI/legacy data editorが先行実装済みの機能もある。機能ごとの旧版動作・必要画面/操作・データ・課題は [FEATURE_PORTING_MATRIX.md](FEATURE_PORTING_MATRIX.md) を参照。

## 追加の書き込み安全ゲート

- 書き込み許可条件をゲームID単独から、検証済みプロファイル `SLPM-62199` / CRC `e199f75d` / game version `1.04` の3条件一致へ強化した。
- UIの書き込みボタン、確認ダイアログ直前の再確認、`PineClient.SafeWriteMemory` 内部の最終認可で同じ判定関数を使用する。別CRC/版は読み取り専用。
- この変更はGitHub draft PRでレビュー中。現環境では.NET Release buildおよびPCSX2 GUI実機確認は未実施。ユーザーPCでPINEが返すCRC/版を確認し、Release build後に統合する。

## 未解決事項と制約

- オフラインlegacy editorのデータ形式は旧HSP/解析資料に基づく。これらの形式が対象ゲーム `SLPM-62199` 上のどのEEアドレスに対応するかは未検証であり、ゲームメモリへ反映しない。
- PINE read/writeのアドレス候補は根拠を示すが、ゲーム内の意味とscene/CRC依存性が確定していない。旧HSPのホストプロセスアドレス計算を新PCSX2に転用しない。
- 2026-11-13にユーザー起動中のPCSX2 v2.8.2へPINE port 28011でread-only接続し、SLPM-62199/CRC e199f75d/version 1.04を取得。ユーザーの過去測定と一致を確認。アプリGUI自体の接続操作とmemory writeは未確認/未実施。
- 旧資料に異なるゲームIDが記述されている。現在の書き込み許可対象 `SLPM-62199` と一致するCRC/version別profileが実機で再確認されるまで、旧資料のアドレス候補から機能別writeを有効化しない。
- `0x00180020` からの64-byte pointer tableと16参照先×64-byte dumpはlive PINEと以前のユーザー測定が一致。ただし各領域の意味、旧解析のpointer chainとの対応、scene dependenceは未確定。
- モンスター成長表やゲーム内テキストの文字コード、廉価版/版差、EE物理/仮想aliasのデータ一貫性は、対象ゲームで照合が必要。

## 直近の履歴・差分機能

- .tijprojのasset履歴画面で祖先/子孫一覧から比較対象2版を選び、SHA-256検証後にoffset別byte差分を表示する。追加/削除byteも表現し、UI表示は最大2,000件。manifestの循環親子参照はOpen時に拒否する。
- offline model harness: 75 assertions PASS。履歴、差分値、hash不整合拒否、循環履歴拒否を含む。Release build成功、警告0・エラー0。
- 今回は実機PCSX2/PINEアクセスとゲームメモリ書き込みなし。WinForms履歴画面の実操作は未確認。

## PINE capture project workspace

- single/batch capture schema v1、必須metadata、32-bit address境界、HEX/length一致を検証する。最大64 MiB JSONをproject assetとして取り込み、categoryにはkind/game ID/CRC/game versionを表示する。
- 比較画面にPCSX2版、title、game ID/CRC/version、UTC、kindを表示。領域構成が同じ場合だけJSON全体ではなく各region raw bytesを比較する。異なるgame identityには注意を表示する。
- offline model checks 75件PASS。Release build成功、警告0・エラー0。実captureのproject import/WinForms操作とPCSX2実機操作は未確認。

## 提示された実測結果（利用者報告、独立再取得なし）

対象: PCSX2 v2.8.2、PINE経由、ゲームID `SLPM-62199`。値の意味・構造体の解釈は未確定。

| アドレス | 報告された値 | 分類と制約 |
|---|---:|---|
| `0x00180000` | `0x00180020` | 実測報告。root pointerらしいが、役割未確定 |
| `0x20180000` | `0x00180020` | 実測報告。同一値はこの地点のalias挙動の証拠。全アドレスへの一般化は未検証 |
| `0x00180020` から16個 | `00181100, 001812C4, 001815D8, 0018195C, 00181C00, 00182064, 00182538, 00182AEC, 00182CB0, 001830A4, 00183578, 00183A4C, 00184070, 00184234, 00184628, 00184B6C` | 実測報告。各値の型/参照先の意味は未確定 |
| `0x00180020` から16 bytes | `00 11 18 00 C4 12 18 00 D8 15 18 00 5C 19 18 00` | 利用者の実機報告。4個のLE DWORDは順に `0x00181100`, `0x001812C4`, `0x001815D8`, `0x0018195C`。16個DWORD測定値の先頭4個と一致。構造的意味は未確定 |
| `0x00139D00` | `0` | 実測報告。期待した文字列アンカーは確認されず |
| `0x20139D00` | `0` | 実測報告。同様に文字列は確認されず |
| `0x00148EBC` | `0xF8035904` | 実測報告。幅/符号/構造体の解釈未確定 |
| `0x00199EC6` | `0x02670014` | 実測報告。read幅/バイト列/entry解釈を再照合する |
| `0x005DBCA8` | `150` | 実測報告。値の幅・シーン依存性未確認 |
| `0x205DBCA8` | `150` | 実測報告。この地点のalias一致のみ確認 |

## 追加の実機キャプチャ（利用者提供JSON）

添付された `capture_SLPM-62199_00180020_20261113_050021.json` の内容を確認した。メタデータは PCSX2 v2.8.2、ゲームID `SLPM-62199`、CRC `e199f75d`、ゲーム版 `1.04`、取得時刻 `2026-11-13T05:00:21.3263074Z`、アドレス `0x00180020`、長さ64 bytes。

raw bytesは以下の16個のlittle-endian DWORDとして読め、以前のユーザー報告の16値すべてと一致する。これは報告された実機測定と添付JSONの整合確認であり、こちらがPCSX2を独立操作して再取得した結果ではない。

`00181100, 001812C4, 001815D8, 0018195C, 00181C00, 00182064, 00182538, 00182AEC, 00182CB0, 001830A4, 00183578, 00183A4C, 00184070, 00184234, 00184628, 00184B6C`

既知の「0x00180020から16 bytes」の報告とも先頭16 bytesが一致する。16個の値が同領域のどのような参照・レコードを表すか、現時点では特定できない。よってこれらをモンスター開始位置やテーブル構造とは断定しない。

新版には利用者実測16参照先候補の各64 bytesを読み取り、領域間の差分を要約し、raw hex bytes/LE DWORDとメタデータをJSON保存する読み取り専用画面を追加した。利用者貼り付け結果との一致は確認したが、こちらが実機から再取得したわけではない。16領域の意味/構造は未判定。
# 全機能移植作業の進捗（2026-11-13）

## 追加進捗: 実測キャプチャ・比較

- 「調査キャプチャ・比較」画面を追加。PINE read opcode経由の1〜4096 bytes取得、UTC時刻/PCSX2版/ゲームタイトル/ID/CRC/版/EEアドレス/長さ/raw bytesのJSON保存、保存キャプチャ2件の差分表示を実装。
- 比較はBYTE差分と4-byte little-endian DWORD差分を表示する。同一アドレス・同一長のみ受け付け、ゲームID/CRC/版が異なる場合は比較結果に注意を表示する。
- 読み取りの前後でPCSX2 statusとゲームID/CRC/版を再確認し、セッション切断やゲーム変更を検出した場合はキャプチャを破棄する。
- この画面にPINE書き込み経路はない。実機データの意味や構造は推定しない。
- Releaseビルド成功、警告0・エラー0。実機PCSX2でのキャプチャ/比較操作は未確認。

## 追加進捗: 16個の実測参照先

- `0x00180020` の実機キャプチャに含まれる16個のDWORD値すべてを、64-byte read-only dumpの対象に追加した。UIは各候補を `0x00181100` から `0x00184B6C` まで個別に読み、raw bytesとLE DWORDを表示する。
- 以前の4件も対象に含まれている。追加12件も利用者提供キャプチャの値をアドレス候補として採用しただけで、ポインタ先のデータ型・意味は未判定。
- 16領域一括読み取りは最大1024 bytesのスナップショットではなく、逐次PINE要求の集合であり、実行中に内容が更新される可能性がある。

## 利用者貼り付けの16領域測定結果

貼り付けテキストに示されたPCSX2 v2.8.2 / `SLPM-62199` の16領域64-byte結果を照合した。16領域すべてでoffset `+0x01`〜`+0x3F` の63 bytesが同一で、offset `+0x00` の1 byteだけが領域間で異なる。先頭byte列は `03,06,07,05,09,0A,0C,03,08,0A,0A,0D,03,08,0B,05`（各アドレスの列挙順）。

これは貼り付けデータ同士の機械的な一致パターンであり、各領域が同一種類のレコードであることや、先頭byteがID/階層/能力値であることを証明しない。取得時のゲーム内シーン/状態と時刻は貼り付け範囲に含まれない。今後の検証には異なる状態で同一アドレスを再取得した比較が必要。

## 追加進捗: 一括キャプチャの保存性

- 16領域を読む前後にPINEの公式status・ゲームID・CRC・ゲーム版を照合する。切断、ゲーム停止、タイトル切り替わりを検出した場合は一括キャプチャを破棄する。
- 結果ビューから `PointerBatchCapture` JSONを保存できるようにし、取得時刻、PCSX2版、ゲームメタデータ、前後status、全16領域のアドレス/長さ/raw bytesを含めた。
- この検査は同一ゲームセッション内のシーン遷移や各PINE readの間に変化するメモリを原子的に固定しない。厳密比較ではゲームを一時停止して採取する。

## 追加進捗: 出現テーブルのオフライン編集

- 旧HSP `*label_086` は各行を6-byte strideで読み、offset 0/2/4から2-byteずつmonster ID / 累積値 / levelを取得する。表示weightは今回の累積値から前行の累積値を引く形式。
- `MonsterSpawnTableForm` に、6-byte legacy binaryまたは単一メモリキャプチャJSONの読み込み、ID/weight/level編集、旧 `heikinwari` + `hurinaosi` に沿った有効行の平均化、正の重みを行順に補正する999合計調整、旧形式bin exportを実装。
- 旧版の階データcopy/paste操作に相当する、同一行数限定のアプリ内テーブルcopy/pasteを追加。貼り付け前に確認し、適用先もローカル編集中テーブルのみ。
- 上限は旧HSPの配列サイズに基づく512行。編集後に出力するCDFはlittle-endian ushort、weight総和は999以下に制限する。
- これは旧版形式のオフライン移植であり、対象ゲームのアドレス候補への対応付け、PINE書き込み、ゲーム内検証は行わない。旧版のmonster-table clearは表サイズごとの特殊初期化レコードを持つため、この段階では移植していない。

## 追加進捗: フロア設定のオフライン編集

- `FloorDataEditorForm` を追加。解析資料と `*label_099` にある112-byte strideおよび既知フィールドoffsetを使用して、単一/複数floorのlegacy binaryまたは同じ長さのPINE単一領域JSONを閲覧・編集・保存できる。
- 部屋形状、分割、水路、属性、店/MH率、呪い/祝福、敵/罠/床落ち上限、5種のitem table index、固定部屋、壁item数、視界、罠CDF20値、地形、BGM、ひとくい率を扱う。field widthは1/2 bytesとしてlittle-endian解釈。
- 未表示のreserved bytesは元データ配列のまま維持する。`var_482` / `var_485` は意味未確定と明示。
- 対象ゲームで112-byte layoutが使われているか、各値の有効範囲・実アドレス・ゲーム挙動は未検証。PINE writeは行わない。Releaseビルド成功、警告0・エラー0。
- 罠CDF20値について旧 `heikinwari` / `hurinaosi` 相当の平均化と999補正を追加。手動値入力は0〜999の非減少CDFに制限する。すべてオフライン動作。

## 追加進捗: アイテム出現率カテゴリ層

- HSP `*label_082`〜`*label_085` は `var_515` の先頭14個のuint16をカテゴリ累積率として読み、カテゴリ重みを隣接CDF差分で表示する。`*label_092` は768-byte blockを単位として床別データを保存/読込する。
- `ItemCategoryRateForm` は768-byte block群または単一領域capture JSONを開き、14カテゴリの重み編集、旧版相当の平均化/999補正、カテゴリCDF14値のcopy/paste、bin保存を行う。offset28以降の740 bytesは未解釈データとしてそのまま維持。
- `CORE_ENGINE_ANALYSIS.md` のitem ID=0..353、`ANALYSIS.md` の個別率offset `var_515+30+ID*2` により旧形式内のslot mappingを採用。現行ゲーム上での名前/ID・table pointerとの対応は未検証のため、実ゲーム接続はせずオフライン編集のみ。
- ゲームのitem table pointerやfloor/typeとの対応は未検証。PINE writeはない。Releaseビルド成功、警告0・エラー0。

## 追加進捗: 固定部屋マップ/RLE

- `FixedRoomMapForm` は旧HSPのroom payloadレイアウト（width byte, height byte, reserved byte, tile RLE stream）を読み書きし、1〜128の幅/高さのtile mapを描画/編集する。
- RLEは通常tokenをhigh nibble tile / low nibble run-minus-oneとして展開し、`0x0E` tokenは次byteを伴うobject markerとして扱う。tile 0はescape collisionを避け最大14 run、それ以外は最大16 runで再圧縮する。
- 旧payloadを読み込んだ場合、元payloadの容量を超える圧縮データはexportを拒否し、reserved/trailing bytesを保持する。新規/編集案は独自 `.room.json` projectにも保存できる。
- `label_133` にある220×20-byte tableについて、HSPで読み書きするoffset/幅をraw fieldとして編集できる別画面を追加。reserved offsetsは保持し、読み込みは4400-byte binary、JSON project側にもoptional object recordsを格納する。
- タイル番号のゲーム内名称、各raw object fieldの意味、137件の固定部屋選択・サイズpointer、PINEアドレスは未移植/未検証。編集はローカルのみ。Releaseビルド成功、警告0・エラー0。

## 今回の実装

- 旧HSPの静的マスターを `Data/game-catalog.json` に移植し、アプリの「旧版マスター一覧」から検索・閲覧できるようにした。
- 収録数: ダンジョン32、モンスター名199、アイテム354、印83、アイテム分類14、モンスター出現テーブル候補876。
- カタログは読み取り専用。行IDや旧版アドレスヒントを、現行ゲームの実データID・構造・安全な書き込み先としては扱わない。
- `ANALYSIS.md` / `CORE_ENGINE_ANALYSIS.md` はモンスター197種と記載するが、`start.hsp` の `var_407` の非空リテラルは199行。ソースの値を保持し、この差の由来は未解決。
- Release ビルド成功、警告0・エラー0。実機PCSX2を接続したUI確認はこの作業環境では未実施。

### 今回の機能実装: 旧INI設定のoffline editor

- 旧HSP `*label_157`〜`*label_159` の22/25行の順序固定INIを読み込み、行番号・対応HSP変数・暫定説明付きで編集し、CP932形式で保存できる画面を追加。
- tempo byteは0〜255、checkboxは0/1、3桁設定は0〜999に制限。25行形式の追加flagもimport時に検証し、日本語Shift-JIS文字列はラウンドトリップを維持する。
- 一部行の意味は未確定。PCSX2/PINEに適用する機能はなく、設定ファイルのローカル編集のみ。
- offline model checks 40件PASS、Release build成功、警告0・エラー0。実ファイルを使った画面操作は未確認。

### 今回の機能実装: 旧データproject bundle

- `ProjectBundleModel` / `ProjectBundleForm` を追加し、`.tijproj` manifestと`assets/`フォルダーから成るローカル作業bundleを作成・再開できるようにした。
- `.bin`/`.json`/`.ini`等を分類付きでbundleへコピーし、元ファイルは変更しない。manifestはサイズ・SHA-256・取り込み時刻を保存し、再起動後の検査で欠落/変更を検出する。manifest内のパストラバーサルを拒否する。
- bundleからINI、spawn、floor、item category/individual rates、monster status/growth、fixed room map/object tableを対応する既存offline editorで開ける。category・拡張子・object tableの正確な4400-byte長をrouterで検査し、editor側も各データ長を検証する。
- 保存は元assetを上書きせず新versionを追加し、元ファイル名とSHA-256をmanifestに記録する。manifest再open時に親assetの存在/hashも検証する。
- 編集version履歴一覧・hash検証付きdiffを実装。model検証済み、GUI操作未確認。旧形式データのゲーム内意味/address applicabilityは別途検証が必要。ゲーム/PCSX2へ接続しない。
- offline model checks 66件PASS、Release build成功、警告0・エラー0。GUI操作は未確認。

## 全体状況

PINE接続/ゲーム情報/raw read-write安全ゲート、旧アドレス候補read、静的カタログ、読み取りcapture/compare、出現table/重複候補比較/floor/item category+individual rates/monster status+growth/fixed room/legacy snapshot viewer/旧INI editor/project bundleのオフライン機能を実装済み。bundleから既存offline editor全系統へのrouteを実装。dungeon/floor pointer navigation、実ゲームとの編集block対応、tempo/patch/string/ranking、bulk restore等は未実装。

これら未移植項目のデータ形式やアドレスは候補・旧実装に基づくものを含み、SLPM-62199 / PCSX2 2.8.2での意味・構造の実機検証が済んでいません。カタログが増えたことは、編集機能やアドレスprofileが確定したことを意味しません。

### 今回の進捗: モンスター基本能力のオフライン編集

- `MonsterStatusEditorForm` を追加。旧HSP/CORE解析資料の24-byte strideとoffset表に沿い、読み込んだ連続レコードをローカルで表示・編集して旧形式binへ保存できる。
- 旧ソースに記されたHP/攻撃/防御/特性/視界/速度/系統/勧誘率/経験値/特技ID/ドロップ/成長型を列として表示。予約byteは保存時に保持し、一部は表示上も編集可能。
- レコード番号と種族名の一致、現行ゲーム内のデータ配置、値が実際に意味する内容は未照合。PINE読取・書込とは未接続で、ゲーム機能として実装済みとは扱わない。
- Releaseビルド成功（0警告、0エラー）。初回はMSBuild server timeout後に停止したため、build server無効化で再実行して確認。

### 今回の進捗: フロアrecordのローカル複製

- 112-byteフロアデータ画面に選択recordのcopy/pasteを追加。paste前に112 bytes全体を置換することを確認し、元のreservation bytesを含めて複製する。
- 操作対象は読み込んだbin/キャプチャ由来のアプリ内バッファのみ。PCSX2/PINEへの書き込み経路はない。ゲームにおけるfloor間コピーの有効性は未検証。
- Releaseビルド成功（0警告、0エラー）。実機PCSX2接続・UI操作の確認は未実施。

### 今回の進捗: モンスター成長テーブルのオフライン編集

- 初版画面の配列解釈に誤りが判明したため修正。旧HSPは12タイプごとに別基点を持ち、各タイプ内でHP/攻撃/防御を2-byte stride、経験値基準を4-byte strideで読む。
- 修正版は12タイプを選択し、各タイプ内最大100 slotを表示・編集する。旧アドレス定数差分に基づきraw dump最小長を14,408 bytesとし、変更slotだけ該当offsetに反映して全体bin保存する。capture JSONのBytesHexも対応。
- スロットのレベル意味、上限100のゲーム内妥当性、raw dump開始位置と実ゲーム対応は未検証。固定EEアドレスを仮定せず、PINEへの読み書きはない。
- 修正版のReleaseビルド結果は下記を参照（先の初版ビルド成功は修正版UIの検証には当たらない）。

## 2026-11-13 実機PINE読み取り確認

ユーザーが起動したPCSX2 v2.8.2へPINE `127.0.0.1:28011` でread-only接続し、SLPM-62199 / CRC `e199f75d` / game version `1.04` / status `0` を確認。`0x00180020` の64 bytesは保存済みの以前のcaptureと完全一致し、16個の参照先×64 bytesはユーザー添付済みの前回測定テキストと全bytes一致。既報のポイント値も一致した。詳細とcapture JSONは [TEST_RESULTS.md](TEST_RESULTS.md) に記録。

この再現性により、現在のPINE read pathとユーザーの以前の測定内容が同一値を返すことを確認できた。ただし、アドレスに格納された値の意味、ポインタ先とモンスター/フロア/出現テーブルの対応、成長表の開始位置はまだ未確定。今回メモリ書き込みは行わず、実ゲーム画面の内容との相関も未確認。

## 2026-11-13 実機PINE読み取り再確認 (18:14 JST)

ユーザー起動中のPCSX2 v2.8.2のゲームウィンドウを確認し、PINE `127.0.0.1:28011` でread-only再確認した。`SLPM-62199` / CRC `e199f75d` / game version `1.04` / status `0 -> 0`。`0x00180020` からの16個の参照先、各64 bytes、既報probe 6か所、以前の基点captureのraw bytesが保存済みの以前の実機測定とすべて一致。証跡は [TEST_RESULTS.md](TEST_RESULTS.md) と [`live_readonly_verification_20261113_091453.json`](captures/live_readonly_verification_20261113_091453.json)。PINE書き込みは行っていない。TornekoIjiriNext GUI自体の接続操作とアドレス意味の確定は未確認。

## 今回の機能実装: 個別アイテム出現率

- 旧 `start.hsp` の `var_517` 354件・ID 0..353と、`ANALYSIS.md` の `var_515+30+ID*2`、`CORE_ENGINE_ANALYSIS.md` のsource-order item IDを照合。
- `ItemRateTableModel` と `ItemIndividualRateForm` を追加。768-byte blockの個別item CDFからカテゴリ内重みを復元し、354件のsource catalog名/IDで編集、カテゴリ別均等化/999補正、copy/paste、multi-block bin保存を実装。28..29と738..767、非選択category/blockのbyteは保持。
- 新版画面はローカルbin/単一capture JSONのみ。PINEやPCSX2への接続・書き込み経路はなし。レコードのゲーム内意味/出現カテゴリとtable kindの対応は実機未検証。
- `ItemRateTableModel` の一時console harnessで11項目PASS (CDF delta、category reset、LE encode、予約byte/別category/block保持、末尾ID、999 normalize、範囲拒否)。結果は[TEST_RESULTS.md](TEST_RESULTS.md)。
- Release build成功（警告0、エラー0）。実PCSX2へ個別率binを反映する操作は未実装・未実施。

## 今回の機能実装: 旧版512 KiB snapshot viewer

- `start.hsp` `*label_151`〜`*label_156` にある512 KiB container、version marker offset355056、extension start355072、version 1〜5 segment lengths、segmentごとの16-byte stride gapをモデル化。
- オフラインUIでsegment一覧/offset/lengthを確認し、異なるsnapshotとbyte差分比較、選択領域をbin抽出できる。snapshotファイルには変更を加えず、PINE/PCSX2へは接続せず、memory restore/write機能もない。
- synthetic snapshot testでv1..v5ごとの領域数、v5 18領域、16-byte gap、sentinel 0x55、unknown marker、短い入力拒否、diff/extractを確認。旧形式実ファイルでのUI確認は未実施。
- Offline model testの最新結果は[TEST_RESULTS.md](TEST_RESULTS.md)に記録。

## ChatGPT作業コピーでの追加実装（2026-11-13）

- `LegacyBinaryEditorForm.cs` を追加。未知/未分類のraw assetをページ単位（4096 bytes）で開き、byte単位のhex編集、ページ移動、未保存変更確認、最大64 MiBの読み込み/保存に対応。未変更byteを保持し、PINE/PCSX2への書き込み経路は持たない。
- project bundleの「その他」分類に `.bin/.dat/.raw/.sav/.dmp` を明示ルーティングし、編集結果を元assetの上書きではなく親SHA-256付き新versionとして追加する。
- `ProjectCaptureModel` を拡張し、FormatVersion 1のsingle/batch captureだけでなく、同梱されている旧診断JSON（PointerTable/PointerTargets64Bytes/PreviousProbePoints、Regions/Points形式）をread-only captureとして取り込めるようにした。旧形式の数値/hex文字列アドレス、Length省略、raw bytes長、32-bit address overflowを検査する。
- 検証: 同梱JSON 6ファイルのJSON構文OK。capture 5ファイルを互換ルールで検査し、1/16/23/23/11領域すべてのhex長・byte長・アドレス範囲が一致。26個のC#ソースの括弧/文字列境界の簡易静的検査OK。
- 制約: この作業環境に `dotnet` / MSBuild / C# compilerが存在しないため、Windows FormsのReleaseビルド・実画面操作テストは未実施。簡易静的検査はC#コンパイルの代替ではない。次のWindows側工程で `dotnet build -c Release` を必ず実行する。


## 2026-11-13 UI安全性改善: 読み込み前の書き込み操作を防止

- メモリ欄の初期値が空の状態で書き込みボタンを押すと、16進数byte parserの入力形式エラーが表示されるUX上の問題を修正。
- 書き込みボタンは、現在の接続先/許可ゲーム条件に加えて、現在のアドレスと長さで読み込みが成功している場合のみ有効化する。
- 読み込み後にアドレスまたは長さを変更した場合は読み込み済み状態を破棄し、再読み込みを要求する。編集値は読み込み時と同じbyte数でなければ書き込めない。
- 書き込み後は読み戻し値を新しい読み込み済みbufferとして扱う。書き込み失敗時は読み込み済み状態を無効化する。
- UI文言を「編集値を書き込み」に変更し、読み込み→編集→書き込みの順序を明示。
- この変更はPINEの書き込み許可アドレスを増やさず、ゲームデータの意味が未確定なアドレスへの書き込みを安全と認定するものではない。
- Windows上のRelease buildとWinForms操作確認が必要。


### 2026-11-13: project bundle snapshot routing

- Project bundleの分類 `snapshot` で、拡張子 `.bin` / `.dat` かつ正確に512 KiBのassetを専用ルートへ渡すようにした。
- `LegacyMemorySnapshotForm` に既存asset pathを受け取る起動経路を追加。プロジェクト側のSHA-256整合性確認後、旧snapshotのregion一覧・比較・抽出を既存read-only viewerで利用できる。
- 形式不一致のファイルはrouterで拒否し、汎用binary editorへ誤って回さない。PCSX2/PINE接続・memory restore/writeは追加していない。
- Windows Release buildとGUI操作確認は未実施。次のレビュー環境で確認する。
