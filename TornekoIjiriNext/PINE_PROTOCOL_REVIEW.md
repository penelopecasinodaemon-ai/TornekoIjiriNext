# PINE通信レビュー

更新日: 2026-11-13

## 照合元

PCSX2公式 `pcsx2/PINE.cpp` と `pcsx2/PINE.h` を現行mainから確認した。

- [PINE.cpp (公式実装)](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.cpp)
- [PINE.h (デフォルトslot)](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.h)

PINE.cppはWindowsでloopback TCPを使い、既定slotは28011。PINE.hは `PINE_DEFAULT_SLOT 28011`。このアプリの既定 `127.0.0.1:28011` と一致する。

## ワイヤ形式とコマンド

- 要求先頭4 byte: 要求全体の長さ（little-endian uint32、先頭の4 byteを含む）。後続に1 byte opcodeと各引数。
- 応答先頭4 byte: 応答全体の長さ（little-endian uint32、先頭を含む）。続くbyteが成功 `0x00` / 失敗 `0xFF`。
- 複数の小コマンドを1フレーム内に連結できる。公式Parserはframe bodyを最後まで順次parseする。
- `MsgRead8/16/32/64 = 0/1/2/3`、`MsgWrite8/16/32/64 = 4/5/6/7`、`MsgVersion=8`、`MsgTitle=0x0B`、`MsgID=0x0C`、`MsgUUID=0x0D`、`MsgGameVersion=0x0E`、`MsgStatus=0x0F`。
- メモリアドレス引数と数値応答はlittle-endian。文字列応答は4-byte長＋UTF-8のnull終端文字列。
- MsgRead8はopcode+uint32 addressで1 byte応答。MsgRead16/32/64は各幅でlittle-endian値を応答する。MsgWrite8/16/32/64はopcode+address+幅の値で、成功時は追加データなし。
- PINE.cppの要求最大値は650,000 byte、応答側バッファ上限は450,000 byte。現実装のmemory read/write上限4,096 byteはアプリ側の制限であり、PCSX2上限ではない。

## 現アプリコードとの照合

| 項目 | コードの動作 | 判定 |
|---|---|---|
| TCP接続 | `TcpClient.Connect(host, port)`。既定host/portはUIで `127.0.0.1:28011` | PCSX2 Windows loopbackの既定と一致。非loopback hostを許すUIはサーバーのbind仕様上通常接続できない |
| TCP切断 | FormClosed時と再接続時に `Dispose` | 正常終了経路はある。ConnectPine失敗時もdispose。I/O中の例外時に自動disposeしない点は改善課題 |
| 要求フレーム | 長さを `4+commands.Length` としてLEで書き、全byteをNetworkStream.Write | 公式の全体長形式と一致 |
| 応答フレーム | 4-byte長を読み、指定長までReadExactlyし、body先頭status byteを確認 | 公式の応答形式と一致。1回のsocket readで全て届くと仮定していない |
| opcode | Version 8、Title 11、ID 12、UUID 13、GameVersion 14、Status 15。memory read 0、write 4 | 公式enumと一致 |
| string | size uint32をLEで読む。指定byte数をUTF-8化し末尾NULをtrim | 公式応答形と一致。現行公式のゲームタイトルはUTF-8 byte列。バイトサイズで切るためUTF-16文字数扱いの誤りなし |
| read | 1〜4096 bytesをMsgRead8/addressの連結で取得 | byte列として正しい。16/32-bitの数値表示は利用者側でLE解釈が必要 |
| write | 1〜4096 bytesをMsgWrite8/address/value連結で送る | byte列として正しい。成功statusだけ確認しread-back照合はしない |
| 複数コマンド | 読書きbyteごとの要求を1 frameに連結 | 公式parserは逐次コマンドを処理するためプロトコル上妥当。1回のframe内の各byte読み取りは原子的snapshotではなく、実行中のゲームが更新する可能性がある |
| error | response status != 0、短い/大きいframe、切断、timeoutを例外化 | エラーはUIログへ出る。connectionをstale表示のままにし得る。例外後の自動破棄/reconnectと操作停止は未実装 |
| target game | Title/ID/CRC/ELF versionを取得して表示 | 取得値を許可条件として使わず、別ゲームでもmemory read/writeボタンが有効になる。誤対象書込み防止は未実装 |

## 不一致や未検証の点

1. ユーザー報告の `SLPM-62199` は現アプリで表示できる想定だが、PINE接続実測はこの環境では実施していない。旧解析資料にある `SLPM-65115` / `SLPM-65775` との不一致を解決するまで、ID別のprofileを作らない。
2. `Connect`は同期TCP connectで、明示的接続タイムアウトなし。read/write timeoutは2.5秒。UIスレッド上で行うため、接続/応答中にUIが止まる可能性がある。
3. 読み取りはopcode 0を連打する設計で、転送形式は公式に合うが効率は16/32/64-bit opcodeより低い。Read32・Write16など型指定とbyte orderをUI/APIとして提供すると解釈ミスが減る。
4. `MaxFrame=650000` は公式要求上限と合う一方、response長も同じ650000まで許しており、公式応答上限450000より広い。現実装の単一API要求は最大4096 byteなので実害は限定的。
5. 受信frame長に対する応答内の各field全体の整合性は文字列サイズ以外に限定検査。個々のmemory responseが要求byte数と一致することを明示的に検証していない。
6. PINEが受け取るゲームアドレスがEE physicalかKSEG aliasかはPINE.cppの `vtlb_ramRead` 呼び出しだけから「常に `& 0x01FFFFFF` される」とは結論しない。提示された3地点のphysical/alias一致は利用者報告の局所的実測であり、全領域に一般化しない。

## 改善優先順位

1. target game IDの許可状態を導入し、想定外IDは既定でwriteを無効化。ユーザーが診断目的で解除する場合も明示的にする。
2. socket I/O失敗時はconnectionをdisposeしてUIを未接続へ戻す。非同期接続/要求、キャンセル、明示タイムアウトを追加。
3. 読み込み結果の長さ・status・metadataの妥当性を厳密検証し、write後に任意のread-backを行う。
4. byte/16/32/64-bit read/writeを型別に実装し、unaligned addressも型ごとに検証して生byteと数値を併記。
5. PINE要求/応答を記録する診断ログを追加。ただしゲームメモリ全量の記録は避ける。

## 実機接続結果 (2026-11-13)

ユーザー起動中のPCSX2 v2.8.2に対してloopback port 28011で、上記のLength-prefixed LE framing、応答status byte、公式opcode 0x08/0x0B〜0x0F、opcode 0 MsgRead8を用いたread-only通信が成立した。PCSX2 version/title/ID/UUID/ELF version/statusの応答がコードの期待形で取得でき、SLPM-62199 / CRC e199f75d / version 1.04、status before/after=0を確認。0x00180020と16の64-byte参照先、および既報6地点を読み取った。過去の同一測定との差分は0 byte。詳細は [TEST_RESULTS.md](TEST_RESULTS.md) と `captures/` 内JSONを参照。

この試行はPINE response framingと対象環境での読取を確認したもの。アプリGUIを操作したend-to-end確認ではなく、書き込みopcodeも送信していない。データ構造の意味を確定するものでもない。
