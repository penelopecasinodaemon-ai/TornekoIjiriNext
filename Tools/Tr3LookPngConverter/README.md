# PNG → TR3LOOK 変換ツール

トルネコ3 いじりツールのモデル画像PNGから、TR3LOOK1形式の.tr3look差分ファイルを作るWindows用ツールです。

## 使い方
1. Tr3LookPngConverter.exeを起動します。
2. PNGをウィンドウ中央へドラッグ＆ドロップします。
3. PNGと同じフォルダーに.tr3lookが生成されます。
4. 生成ファイルをモデル編集アプリへ読み込ませます。

複数PNGをまとめてドロップできます。同名の.tr3lookが既にある場合は上書きせず、_converted付きの別名で保存します。

## 初期状態
いしにんぎょうの未塗装テンプレートと元PNGを内蔵しています。初回はPNGだけをドロップすれば動作します。

## 別のモンスターに切り替える
「テンプレート／元PNGを変更」を押し、次の2つを指定します。
- points=0、runs=[]の未塗装.tr3look
- そのモデルを編集アプリから書き出した未編集の元PNG（256×256）

設定は次回起動時にも保持されます。以後は編集済みPNGだけをドロップすれば変換できます。

## 変換仕様と注意
- 元PNGと編集PNGを画素単位で比較し、変更画素だけをrunsに記録します。
- runs[].iは左上から行優先で数えた画素位置、runs[].dはパレット番号列のBase64です。
- pointsは変更画素数です。
- 色はテンプレートの256色パレットからRGB距離が最も近い色へ変換します。同距離なら番号が小さい方を選びます。
- 差分判定はRGBのみです。アルファ値だけの変更は差分にせず、出力透明度はパレットに従います。
- 写真や滑らかなグラデーションではなく、モデル用テクスチャ編集を想定しています。

## ビルド
Windowsと.NET 8 SDKが必要です。
dotnet publish Tools/Tr3LookPngConverter/Tr3LookPngConverter.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

GitHub ActionsでもWindows x64向けの単一EXEをビルドし、成果物として保存します。
