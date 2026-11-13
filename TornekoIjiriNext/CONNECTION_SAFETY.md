# 接続管理・書き込み安全性

更新日: 2026-11-13

## 書き込み許可条件

書き込み許可プロファイルは `SLPM-62199` / CRC `e199f75d` / game version `1.04` とする。IDとgame versionは `StringComparison.Ordinal`、CRCは16進文字列の大文字小文字を許容して比較する。取得値をtrimして許可することはしない。いずれかが異なる、空、または未取得の場合は書き込みを拒否する。別CRC/版は、別途実機検証して許可プロファイルに追加するまでread-onlyとする。

書き込み可能状態は次のすべてが真の場合だけ。

1. `_pine` が存在し、破棄されていない。
2. 初回/直近のゲーム情報取得が成功し、IDが空でない。
3. 取得したID/CRC/game versionが `SLPM-62199` / `e199f75d` / `1.04` の許可プロファイルと一致。
4. PINEの `MsgStatus` がRunning(0)またはPaused(1)。Shutdown(2)は不可。
5. UIの接続状態が `Connected`。

ボタンの `CanWrite` はUI上の早期ガード。実書き込みの唯一の入口 `PineClient.SafeWriteMemory` も必ずPINEのstatus、タイトル/ID/CRC/ゲーム版取得、ID/CRC/版の許可プロファイル一致、statusを再確認する。書き込み前後の全処理はクライアントのlock下で行い、他のアプリ内PINE要求が検査と書き込みに割り込まないようにする。対象IDであっても確認値の変更があれば中止する。

## 書き込みの流れ

1. UI状態とID/CRC/ゲーム版の許可プロファイル一致に基づきボタンを有効化。
2. クリック時にstatusと全ゲーム情報を再取得し、対象ID/CRC/ゲーム版を再確認。
3. 書き込み前の生byte列を読み取る。
4. 最終確認ダイアログにゲームタイトル/ID、status、アドレス、変更前byte列、書込byte列を表示。
5. Yes後、通信層でstatus→ID/CRC/ゲーム版→statusを再確認。
6. 変更前byte列を再取得して確認時の値と一致しなければ中止。
7. MsgWrite8でbyteを書き込む。
8. MsgRead8で同じ範囲を読み戻し、全byteが期待値と一致した場合だけ成功表示。
9. 不一致時は失敗表示と実際の読み戻し値を表示し、成功扱いにしない。

PINEには「IDを条件にしてwriteを原子的に行う」公式コマンドはない。PCSX2/ゲーム状態はアプリ外で変化するため、最後のstatus/ID確認とwriteの間に起きる切替をプロトコルだけでゼロにすることはできない。直前確認、旧値比較、read-backにより窓を小さくし、結果も検証するが、この制約は残る。

## 接続状態

| 画面表示 | 意味 | read | write |
|---|---|---:|---:|
| 未接続 | TCP/PINE接続なし、または切断検知後 | 無効 | 無効 |
| 接続中 | 接続開始中、またはPINE接続済みで対象ゲームID確認済み | 対象確認後のみ | 対象ID確認後のみ |
| ゲーム情報取得中 | メタデータ確認中、またはPCSX2接続中でゲーム未起動。再ポーリング待ち | 無効 | 無効 |
| 対象外ゲーム | 情報取得成功だがID不一致 | 有効 | 無効 |
| 接続エラー | 初回接続/通信/公式コマンド失敗 | 無効 | 無効 |

PINE `MsgStatus` (opcode `0x0F`) を1秒ごとにポーリングする。status=2ならゲーム情報を無効化し、status 0/1時はゲームIDを再取得して接続状態を更新する。PINEコマンドエラーはTCPが維持される場合があるため監視を続け、次回再取得する。EOF/socket切断ではクライアント破棄・ボタン無効化・未接続表示とする。その他のtimeout/protocol I/O errorは接続エラーにしてクライアントを破棄する。再接続はユーザー操作。

使用コマンドは公式実装の `MsgVersion`, `MsgTitle`, `MsgID`, `MsgUUID`, `MsgGameVersion`, `MsgStatus`, `MsgRead8`, `MsgWrite8` のみ。独自opcodeは追加していない。

## PINE仕様の根拠

Windows版PCSX2はloopback TCP serverを使い、既定slotは28011。現コードのdefault `127.0.0.1:28011` と一致する。コマンド/応答frameは全体長LE uint32、成功応答status byte、値/addressはLE。実装は公式ソースの該当箇所と照合した。

- [PCSX2 PINE.cpp](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.cpp)
- [PCSX2 PINE.h](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.h)

## 運用上の注意

- アプリ内には一度に一つの `PineClient` を保持し、再接続時に既存clientをdisposeする。PCSX2のPINEは接続clientを逐次acceptするので、他の外部PINEクライアントとの同時利用は保証しない。
- 読み込みと旧候補確認は対象IDに限らず、起動中ゲームで利用可能。書き込みだけを対象IDで制限する。
- `MsgRead8/Write8`を連続発行するのでraw byte範囲は1〜4096 byte。ゲームは読み書き要求間も進むため、読み取りは原子的snapshotではない。
- メモリ書き込みはゲーム/セーブ状態を変える。可能ならPCSX2 savestateまたは別のバックアップを用意して使う。
