# 旧版全機能の移植マトリクス

根拠: 旧 `start.hsp` のBlock/labelと、`ANALYSIS.md` / `CORE_ENGINE_ANALYSIS.md`。旧メモリ位置の記述は旧PCSX2プロセス向け候補であり、新版PINEの書込許可アドレスとはみなさない。**オフライン編集**はローカルファイルだけを扱い、ゲームへ反映しない。

| 機能 | 旧版での機能・実装箇所 | 新アプリの状況 | 必要な画面・操作 | 必要データ | 実装上の課題 |
|---|---|---|---|---|---|
| 接続/ゲーム識別/汎用memory | `*label_003`〜`*label_006`, `*label_100`〜`*label_102`; Win32 process attachment | 公式PINE接続、status/game metadata、raw read/write、安全なID制限を実装。v2.8.2のread-only実機通信とSLPM-62199/CRC/版を確認 | 接続先・状態表示、メモリread、書込前後確認とread-back | PINE framing/opcode、ゲームID/CRC、EE addresses | 書込先は個別に版/CRC/構造検証が必要。GUIでの実機接続操作は未確認 |
| 静的マスター | `*label_140`〜`*label_150`; `var_329/407/517/591/314` | 32 dungeons、monster/item/seal、876 spawn hintsを検索表示 | タブ、検索、ID/カテゴリ表示 | HSP literal catalog | 197 vs 199 monstersの差、source IDと実ゲームIDの照合 |
| ダンジョン/floor選択・移動 | `*label_074`〜`*label_080`; Block 6〜8 | 旧ダンジョン一覧とfloor編集器は個別実装。メモリ連携、重複チェック未実装 | dungeon/floor選択、上下移動、重複一覧 | dungeon-floors map、pointer arrays、112B FloorData | dynamic pointers、ダンジョン×階対応、アドレス未検証 |
| アイテムカテゴリ出現率 | `*label_082`,`*label_084`,`*label_092`,`*label_094`〜`*label_098`; 旧offset 0..27 | オフライン編集実装。均等化/999補正/コピー/貼付/bin保存 | block選択、14カテゴリ重み/CDF編集 | 768B block先頭14×uint16 LE | PINEの実block pointerと店/床/変化/モノカ等の種別対応未検証 |
| 個別アイテム出現率 | `*label_084`; `var_517`, item id 0..353; `*label_092`,`*label_094`〜`*label_098`; read/write at `block+30+id*2` | **今回オフライン編集を実装**。カテゴリ別重み/CDF、均等化/999補正、copy/paste、768B複数blockのbin入出力 | blockとカテゴリ選択、354アイテム名/ID一覧、重み編集、合計表示 | 14カテゴリ、354 source-ordered item IDs、2-byte CDF at offset30+2*ID | source-order names vs game ID、CDF reset behaviorを実機で未検証。PINE連携未実装 |
| モンスター出現率 | `*label_078`,`*label_086`,`*label_088`,`*label_089`,`*label_096`〜`*label_098` | オフライン6B entry editor実装 | table選択、monster ID/weight/level編集、平均/999補正、copy/paste | 6B `(uint16 ID,CDF,level)` entries | 876候補tableと実floorのpointer関係未検証 |
| モンスター基本能力 | `*label_086`〜`*label_089`; `var_149`, stride24 | 24B raw recordのオフライン表示/編集/bin保存 | レコード一覧、各offset field編集 | 197/199 source-order names、24B row | names/ID対応と実ゲームtable位置未確認 |
| モンスター成長 | `*label_089`,`*label_140`; `var_622/623/624/626` | 12タイプ×最大100 slotのraw dumpオフライン表示/編集 | type selector、slotごとのHP/攻/防/EXP編集 | HSP derived base/strides、最低14,408B dump | valid slots、EE start address (`var_143`)、type/name対応未確認 |
| 罠出現率 | `*label_090`,`*label_091`,`*label_096`〜`*label_098` | floor editor内20 CDFのオフライン編集、平均/999補正を実装 | 112B floor selector、罠CDF行編集 | FloorData +0x3Cの20×uint16 | 罠ID/name対応と実floor address未検証 |
| フロア設定/その他出現率 | `*label_099`; `*label_074`,`*label_092`〜`*label_098` | 112B FloorDataオフライン編集、複数floor、copy/paste | field grid、floor選択、rate編集、コピー/保存 | 112B layout、既知offsets、reserved bytes | 一部field意味/enum未確定、ゲーム内アドレス未照合 |
| 他floorとの出現重複チェック | `*label_077`,`*label_078`; Block 8 | **オフラインcandidate 6-byte table複数比較を実装**。同一/複数tableのmonster ID出現を行位置付きで表示。実dungeon/floorのscanは未実装 | 複数bin選択、重複IDとtable/file/row/level/CDF表示 | 6-byte `(uint16 ID,CDF,level)` files | file名は利用者指定table名に限定。dungeon/floor pointer map、ゲーム名寄せ未検証 |
| 固定部屋/部屋オブジェクト | `*label_127`〜`*label_135`; Block 15 | オフラインRLE map editor、220×20B raw object record editor実装 | tile map描画/編集、RLE import/export、object field grid | room payload、RLE tokens、object rows、palette | object意味、room index/pointer/size table、実機対応未検証 |
| ゲームテンポ/アニメーション | `*label_105`〜`*label_117`; Block 12/13 | **オフライン設定プロファイル画面を実装**。パッチ有効/その他最速/モンスター最速/冒険結果ターン表示/自動セーブの意図をJSON保存・読込。全最速プリセットあり | 設定フラグ編集、JSON LOAD/SAVE、メモ欄 | 旧UIから確認できる5設定フラグ、formatVersion=1 | 個別待機値・Rev0/Rev1 byte group・版別address mappingは未実装。設定はゲームへ適用されず、PINE接続/書込なし |
| ダンジョン共通設定 | `*label_118`〜`*label_125`; Block 14 | 未実装 | 風ターン、識別率、野良補正、ロゴ/名前、flag editor | settings table、logo bitmap、strings、flags | 旧版host offsets、文字コード/容量/ポインタ更新 |
| enemy motion / trap patches / party flags / Fai問 | `*label_121`〜`*label_123`; Block 14 | 未実装 | patch toggles、進行状態表示、motion pattern editor | code signatures、byte patterns、party/result state | コード領域書込リスク、signature false positive、CRC毎の検証 |
| item description / monster names / dungeon names | `*label_120`,`*label_124`,`*label_125`,`*label_137` | 未実装 | string editor、文字数/encoding preview | game character encoding、string buffers、pointer table | EUC/SJIS/独自文字コード、容量超過、pointer relocation |
| game progress/status | `*label_122`; turn/floor/HP/level/gold reads | PINE metadata and raw read only; semantic status view未実装 | live status panel、refresh/polling | turn/floor/HP/level/gold fields and valid addresses | address/session/scene changes; currently no verified offsets |
| adventure score/ranking upload | `*label_138`,`*label_139`; Block 17 | 未実装 | preview, explicit opt-in send, errors | result fields, EUC-JP encoding, old HTTP endpoint | endpoint/privacy/current protocol/consent. External send remains out of scope |
| full memory backup/restore | `*label_151`〜`*label_156`; Block 19 | **512 KiB snapshotのoffline viewer/comparer/extractorを実装**。game memory restoreは未実装 | version markerとextension一覧、別snapshot比較、選択region抽出 | 512KiB container、marker offset355056、offset355072からversion1..5 segment/16B gaps | container restoreは最大リスク。CRC/版/region mapping/backup/rollback未確立のためPINE write経路に接続しない |
| app preferences/legacy INI | `*label_003`,`*label_157`〜`*label_159` | **22/25行の旧INIをCP932で取り込み・編集・保存するoffline画面を実装**。HSP変数順・値域を保持。PCSX2/ゲームへの適用は未実装 | ローカル設定画面、旧INIのopen/edit/export | 22/25 positional lines; HSP vars 807..846/224/433/353/431 | 一部変数の画面上の意味は未確定。設定反映・実ゲーム連携は未実装 |
| 旧データ作業管理/実測capture | 旧HSPの各editor、`*label_151`〜`*label_159` | **offline project bundleを実装**。INI/spawn/floor/item category+individual/monster status+growth/fixed room map+object tableを明示分類で既存editorへ渡し、元assetを保持して親hash付きnew version/履歴/hash検証付きBYTE差分を表示し、PINE single/batch capture JSONもmetadata付き取込み・read-only比較 | project新規/再開、カテゴリ付きimport、対応editor起動、format validation、version integrity検証 | `.tijproj` JSON manifest + `assets/`; CP932 INI、6B spawn、112B floor、768B item table、24B status、growth dump、room RLE/object table | model検証済み、WinForms操作は未確認。captureのgame ID/CRC/version/時刻/address/length/raw bytesを保持する。各旧形式とゲーム内意味/address applicabilityの検証は別課題 |
| utility functions/UI/graphics/encoding | HSP `#deffunc`/`#defcfunc`, Block 1/2, 91..1542 | WinForms foundation and core tools implemented; visual utilities only as needed | native windows, tables, draw canvas, text codec helpers | event logic, bitmap/encoding routines | preserve usability/accessibility; avoid porting process-memory-specific helpers |

## 実装順

1. **実装済み:** 個別編集器・旧INIに続き、旧データproject bundleを追加。次はmonster/floor/item captureを安全に整理するread-only diagnostic workspaceへ進む。
2. 現在の実機PINE read-only測定をシーン違いで採取し、monster/floor/item pointer dataを比較。意味が確定するまでアプリ内編集はoffline modelに限定。
3. dungeon/floor navigation、duplicate result view、growth/profile captureをread-onlyで追加。
4. tempo/common settings/patch UIはデータモデルと候補address profileを先行し、対象CRCごとに個別レビューできるまでmemory writeを実装しない。
5. bulk restore/ranking uploadは互換/endpoint/バックアップ/プライバシー条件が揃うまで実装しない。

## 追加: 汎用raw asset編集基盤

`その他`分類で取り込んだ `.bin` / `.dat` / `.raw` / `.sav` / `.dmp` を扱うページ式の16進編集器を追加。64 MiB上限、4096-byte page、byte単位hex validation、未変更領域保持、未保存変更確認、project bundleへの親hash付き新version保存に対応。未知形式の解析用・補助編集用であり、旧版の各データ構造を理解した専用editorの代替ではない。ゲーム/PINEへは接続しない。
