# File2MD のテスト

.NET SDK 9 の回帰試験は C# 5 構文を使い、14件の文字コード・ソースコード・
対象外一覧・大きいファイルの処理などを確認します。

```bash
dotnet build File2MD.csproj
dotnet run --project tests/File2MD.Regression.csproj -c Release
```

実サーバと Chromium/Chrome を使う試験には Python の Playwright が必要です。
`DOTNET_BIN` と `PLAYWRIGHT_CHROMIUM_EXECUTABLE` で実行ファイルを指定できます。

```bash
python3 -m pip install playwright
python3 tests/app_smoke.py
```

`app_smoke.py` は試験時に生成する無記名の DOCX/XLSX/PPTX/PDF/テキストを実際の UI から変換します。
大きいファイルの取消・一括除外・取込み、対象外だけの出力、削除 API の拒否、
別アプリのプロセスを置換しないことも確認します。出力・試験データはリポジトリ内に置きます。
FileXfer や他リポジトリには依存しません。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-desktop-context.ps1
```

上記は PowerShell 構文と登録・削除する HKCU キーの生成を確認し、レジストリに書き込みません。
Linux の PowerShell 7 でも実行できます。
