# File2MD

Office 文書・PDF・テキストを Markdown にするアプリです。
必要な C# ソース・画面・Windows 用 DLL を同梱しています。
ほかのリポジトリは不要です。

## Windows で使う

リポジトリ全体を配置し、`File2MD.vbs` をダブルクリックします。
Windows PowerShell 5.1 が C# ソースをその場でコンパイルし、WPF + WebView2 の画面を開きます。
.NET SDK は不要です。Microsoft Edge WebView2 Runtime が必要です。
ログを表示する場合は `File2MD.cmd` を使います。
ログは `%LOCALAPPDATA%\File2MD\logs` に保存します。

`install.bat` は HKCU のファイル・フォルダ・エクスプローラ背景・デスクトップ背景に
**File2MD** を登録します。管理者権限は使いません。
ファイル／フォルダでは `File2MD > 画面で開く` と `File2MD > Markdown にする` の2項目です。
後者は選んだ対象をコンソールで変換し、進捗と出力先を表示します。背景では直接画面を開きます。
Windows 11 では従来形式のメニュー（「その他のオプションを確認」）に表示されます。
`uninstall.bat` はこの4か所の File2MD キーだけを削除します。
フォルダを移した場合は `install.bat` を再実行してください。

## 変換する

ファイルまたはフォルダを選び、「Markdown にする」を押します。
出力は `output/対象名_YYYYMMDD-HHmm/` の下に保存します。
同名の出力がある場合は別フォルダに保存し、既存ファイルを上書きしません。

- Office: `.docx` / `.docm`、`.xlsx` / `.xlsm`、`.pptx` / `.pptm`
- PDF、Markdown、プレーンテキスト、CSV、ソースコード、拡張子のないテキスト
- UTF-8 / UTF-16 の BOM と、BOM なし UTF-8 / CP932

古い Office バイナリ形式や、
暗号化文書・画像だけの PDF などは変換できない場合があります。
1 MiB を超える選択ファイルは、書き出し前にまとめて「外す」「そのまま」を選びます。
取消・Esc では出力せず選択を保ちます。対話できない CLI と、指定のない API は
大きいファイルの本文を除外して進みます。

未選択・未対応・変換失敗・バイナリ・除外拡張子などは、相対パスと理由を「対象外」に残します。
対象外しかない場合も、名前の一覧を書き出せます。
フォルダの隠しファイルは除外し、明示選択したテキストは変換できます。
`.svg`、`.trx`、`.lock`、ハッシュ一覧、`.metadata`、`.map`、`.bak` とその退避用接尾辞は本文に含めません。

```powershell
.\File2MD.cmd --path ".\input" --mode markdown
.\File2MD.cmd --cli --path ".\input" --mode markdown
.\File2MD.cmd --no-browser
```

`--port` で待受ポートを指定できます。

## Linux・開発

.NET SDK 9 でビルドできます。Linux では HTTP サーバを起動し、表示された localhost URL を
ブラウザで開きます。ファイル選択には `zenity` または `kdialog` が必要です。
`--path` と CLI は選択ダイアログなしでも使えます。

```bash
dotnet build File2MD.csproj
./File2MD.sh --no-browser
./File2MD.sh --cli --path ./input
```

開発用 DLL／exe の出力先を使う場合、`output` はその実行ファイルと同じフォルダの下です。
Windows PowerShell の通常起動では配布フォルダの下です。

[テスト手順](tests/README.md)・[第三者表示](THIRD-PARTY-NOTICES.md)
