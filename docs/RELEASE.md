# リリースの手順

## 版を出す (テスト版・公開版共通)

1. 版を上げる: `src/Meltype/Meltype.csproj`・`src/Meltype.Core/Meltype.Core.csproj` の `<Version>`、
   `mac/Resources/Info.plist` の `CFBundleShortVersionString`・`CFBundleVersion`、`mac/Sources/MeltypeIME/Converter.swift` の版。
2. コミットして push し、タグを付けて push: `git tag v0.3.0 && git push origin v0.3.0`
3. GitHub Actions が Windows (build.yml)・Mac (mac.yml)・Linux (linux.yml) の zip を作り、リリースに添付する。
4. 公開版 (1.0.0 以降) なら、利用者の Meltype が自動で更新する (Windows)。

## コード署名 (任意。1.0.0 の後でよい)

署名しなくても配布できます (README に「詳細情報」→「実行」の案内と、zip の SHA-256 の確かめ方を書いている)。
費用をかけない方法として、オープンソース向けに無料で署名する SignPath Foundation (要申し込み・審査、GitHub Actions でのビルドが前提) がある。
ウイルス対策ソフトに誤検知されたら、Microsoft のサイトから誤検知として報告する (無料)。


署名の無い実行ファイルは、Windows の SmartScreen が「発行元が不明」と警告し、キーボードを扱うソフトなのでウイルス対策ソフトに誤検知されやすい。
Mac は署名と公証 (notarization) が無いと、Gatekeeper が開かせない。

### Windows

コード署名の証明書 (OV か EV。年 1〜数万円、個人でも取れる。または Azure Trusted Signing) を用意し、リポジトリの Settings → Secrets and variables → Actions に登録する。

| 秘密 | 中身 |
|---|---|
| `SIGN_PFX_BASE64` | 証明書の .pfx を Base64 にしたもの (PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`) |
| `SIGN_PASSWORD` | .pfx のパスワード |

登録すると、`Build-Package.ps1` が Meltype.exe・dll・Mozc のヘルパーにタイムスタンプ付きで署名する。手元では環境変数 `MELTYPE_SIGN_PFX` と `MELTYPE_SIGN_PASSWORD` で同じことができる。
(EV 証明書はハードウェアトークンに入っていて CI では使えないことが多い。その場合は Azure Trusted Signing を検討する)

### Mac

Apple Developer Program (年 99 ドル) に入り、「Developer ID Application」の証明書を作って次を登録する。

| 秘密 | 中身 |
|---|---|
| `MAC_CERT_P12_BASE64` | 証明書と秘密鍵を書き出した .p12 を Base64 にしたもの |
| `MAC_CERT_PASSWORD` | .p12 のパスワード |
| `MAC_IDENTITY` | `Developer ID Application: 名前 (チーム ID)` |
| `NOTARY_APPLE_ID` / `NOTARY_TEAM_ID` / `NOTARY_PASSWORD` | 公証に使う Apple ID・チーム ID・App 用パスワード |

登録すると、mac.yml が配布用に署名し (Hardened Runtime)、公証してから zip にする。

## GitHub の設定 (公開するとき)

- リポジトリを Public にする (自動更新・Issue・bot が外から使えるようになる)。
- Settings → Security → **Private vulnerability reporting** を ON (SECURITY.md の報告先)。
- Settings → Branches → main のブランチ保護: 必須のチェックに **CLA**・**build**・**精度の比較**・**辞書の形式** を入れる。
- (任意) bot の名前を変える: GitHub App を作り、変数 `BOT_APP_ID` と秘密 `BOT_APP_PRIVATE_KEY` を登録 (CONTRIBUTING.md の bot の項)。
- 不具合報告のフォーム: [tools/report-form/README.md](../tools/report-form/README.md) の手順で作り、`src/Meltype.Core/Config/ProjectInfo.cs` の `ReportForm` に URL を書く。

## パッケージマネージャー (公開版)

リリースの zip から、winget・Scoop・Homebrew のマニフェストを作る。

```
node tools/make-manifests.mjs 1.0.0 dist/Meltype-1.0.0-windows.zip dist/Meltype-1.0.0-mac.zip
```

`dist/manifests/` にできたものを出す。

- **winget**: `winget/manifests/...` を [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) に Pull Request で出す (`winget validate` で確かめてから)。
  審査があり、署名の無い実行ファイルは止められることがある。
- **Scoop**: 自分のバケット (例: `yksr-melt/scoop-bucket`) を作り、`scoop/meltype.json` を `bucket/` に置く。利用者は `scoop bucket add yksr-melt https://github.com/yksr-melt/scoop-bucket` → `scoop install meltype`。
- **Homebrew**: 自分の tap (例: `yksr-melt/homebrew-tap`) を作り、`homebrew/Casks/meltype.rb` を置く。利用者は `brew install --cask yksr-melt/tap/meltype`。

winget・Scoop で入れた場合は Install.cmd を使わないので、Windows の起動時に起動するには、トレイの「Windows の起動時に起動」を ON にしてもらう。

## 公開版 (1.0.0) の前の確認

- [ ] 判定・変換の精度 (v0.3.1〜v0.3.3 で変換のバグ修正・辞書の拡張)
- [x] 協力者のお名前を載せる (README の「協力してくださった方々」)
- [ ] リポジトリを Public にし、上の GitHub の設定をする
- [x] Mac 版・Linux 版を「プレビュー版」と明記 (README。リリースノートにも書く)
- [x] リリースの zip の名前を `Meltype-<版>-windows.zip`・`-mac.zip`・`-linux.zip` に (タグのビルドだけ。テスト版は `Meltype-test-<日時>.zip` のまま。自動更新は両方の名前を探す)
- [x] Discord の bot (tools/discord-bot) を消す (テスター用だったため)
- [ ] リリースノートに zip の SHA-256 を載せる (GitHub のリリースのページにも出る)
- [x] README を一般の人向けにし、詳しい使い方を docs/USAGE.md、開発の話を docs/DEVELOPMENT.md に分ける

1.0.0 の後でよいもの: コード署名 (Windows・Mac。上の「コード署名」)、不具合報告のフォーム (無い間は「不具合の報告・提案...」が GitHub の Issue の画面を開く)、winget・Scoop・Homebrew への登録。
