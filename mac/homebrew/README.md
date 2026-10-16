# Homebrew cask (自前 tap)

`Casks/meltype-for-mac.rb` が cask の正本です。tap リポジトリ (`whitewater-png/homebrew-meltype-for-mac`) へは、ここからコピーして使います。
公開 tap は作成済みです (2026-10-14。<https://github.com/whitewater-png/homebrew-meltype-for-mac>)。利用者は `brew install --cask whitewater-png/meltype-for-mac/meltype-for-mac` で入れられます。tap への push は公開行為なので、Release の公開と同時に、許可を取って行います (下の「公開 tap」の節)。

配布物は zip (`Meltype-mac-<version>.zip`) です。dmg の中身は pkg で、Homebrew の `pkg` stanza は `sudo installer -target /` になり、「このユーザーのみ」の pkg と合わないため使いません。Apple シリコン・macOS 13.3 以降だけが対象です (cask の `depends_on macos:` は Ventura = 13 までしか書けないので、13.0〜13.2 の人には caveats で案内します)。

## ローカル tap で確かめる

```bash
brew tap-new whitewater-png/meltype-for-mac
cp mac/homebrew/Casks/meltype-for-mac.rb "$(brew --repository whitewater-png/meltype-for-mac)/Casks/"   # Casks/ が無ければ先に mkdir
brew audit --cask --strict --online whitewater-png/meltype-for-mac/meltype-for-mac
brew style whitewater-png/meltype-for-mac/meltype-for-mac
brew fetch --cask whitewater-png/meltype-for-mac/meltype-for-mac    # sha256 の一致
brew install --cask whitewater-png/meltype-for-mac/meltype-for-mac
```

`brew install` は `~/Library/Input Methods/Meltype.app` を置き、quarantine を外して入力ソースの登録 (`Meltype --register`) まで行います。既に Meltype.app があると Homebrew は入れないので、先に外してください (下の「注意」)。
`brew tap-new` は Homebrew の開発者モード (`brew developer on`) を自動で有効にします。不要なら `brew developer off`。

## 版を上げる

version と sha256 の 2 行だけを変えます。

```bash
python3 tools/update-cask.py <version> <sha256>      # 例: 1.1.8 と、zip の shasum -a 256
```

そのあと tap 側へコピーし、上の audit / style を通します。`livecheck` は GitHub の Release のタグ (`v<version>-mac`) を見ます。

## 公開 tap (作成済み)

`whitewater-png/homebrew-meltype-for-mac` は 2026-10-14 に作成済みです。版を上げたあとの反映 (tap の `Casks/` に同じファイルを写してコミットし、push する) は、Release の公開と同時に行います。手順は [docs/RELEASE.md](../../docs/RELEASE.md) の「Mac 版: cask の更新 (Homebrew)」にあります。**push は公開行為なので、事前に許可を取ります。** 入れ方は本体の README.md の入れ方の節にも書いてあります。

## 注意

- **quarantine 属性は postflight で外します。** cask で入れたファイルには `com.apple.quarantine` が付き、ad-hoc 署名の実行ファイルは Gatekeeper に止められます (`Killed: 9`)。IME は imklaunchagent が裏で起動するので「開発元が未確認」の表示も出ず、入力メニューに出ないだけになり、`--register` も失敗します。pkg で入れたときは付かないので、それと同じ結果にするための処理です。得: 確実に起きる。損: Gatekeeper の確認を飛ばすので、Release の SHA-256 が cask に書かれていること (Homebrew が検証) を信頼する形になります。
- **`~/Library/Input Methods/Meltype.app` が既にあると入りません** (zip の install.sh や build.sh で入れた場合。`It seems there is already an Input Method at ...` で Homebrew が止まる仕様)。先に `mac/uninstall.sh` か Finder で外してください。
- `brew uninstall` は設定・学習データ (`~/Library/Application Support/Meltype`) を残します。`--zap` で消えるので、ユーザー辞書は先に控えてください。
- アプリ内の更新通知は Homebrew の版と食い違うことがあります。
- Windows / Linux は対象外です。
