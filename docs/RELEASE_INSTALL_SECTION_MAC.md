<!-- Mac 版のリリースノートの「## インストール」節の雛形 (docs/RELEASE.md から参照)。<ver> を版に置き換える。添付は zip と dmg の 2 つ (pkg は単体で添付しない)。画像は main に push 済みであること -->
## インストール
### dmg (動作確認中)
1. `Meltype-mac-<ver>.dmg` をダブルクリックして開く (開いたウインドウの背景に、開き方の図が出ます)
2. 中の `Meltype-mac-<ver>.pkg` をダブルクリック (「開発元が未確認のため開けません」と出たら、下の「開けないとき」の手順で開きます)
3. インストーラーの案内に従って進める (「このユーザーのみ」に入るので管理者パスワードは不要)
4. 入力メニューで Meltype for Mac を選ぶ。出ていないときは、いったんログアウトしてログインし直す

![dmg を開いたときのウインドウ](https://raw.githubusercontent.com/whitewater-png/Meltype-for-Mac/main/docs/images/mac-dmg-window.png)

同じ案内は、dmg の中の「開けないときは.txt」にも書いてあります。

### zip
1. `Meltype-mac-<ver>.zip` を展開
2. 「Install Meltype.command」をダブルクリック (「開発元が未確認のため開けません」と出たら、下の「開けないとき」の手順で開きます)
3. 入力メニューで Meltype for Mac を選ぶ

### Homebrew
`brew install --cask whitewater-png/meltype-for-mac/meltype-for-mac` (更新は `brew upgrade --cask meltype-for-mac`)。**入れたあとはログアウトしてログインし直す**。
`~/Library/Input Methods/Meltype.app` が既にあるときは、先に外してから入れる (Homebrew は上書きしない)。

アンインストールは「Uninstall Meltype.command」です。詳しくは同梱の INSTALL.txt とリポジトリの README を見てください。

### 開けないとき (dmg・zip 共通)
Apple の署名・公証を受けていないため、最初に 1 回だけ許可が必要です。「開発元が未確認のため開けません」「開いていません」などと出たら、表示を閉じてから (「ゴミ箱に入れる」は押さない) 次のようにします。
- **macOS 15 以降**: 一度ダブルクリックして開こうとしてから、システム設定 → プライバシーとセキュリティ → 下のほうの「このまま開く」を押す (ログインパスワードを聞かれることがあります。「このまま開く」は、開こうとしてから約 1 時間だけ押せます)。確認の表示が出たら、開くほうを選びます
- **macOS 14 以前**: 右クリック (control + クリック) →「開く」→「開く」

心配なときは、下の SHA-256 と `shasum -a 256 <ダウンロードしたファイル>` の結果を比べてから開いてください。

<!-- 動作確認の状況に合わせて、各見出しに「(動作確認済み)」「(動作確認中)」を付ける。dmg が未確認の版では、次の 1 文を dmg 節の末尾に入れる:
**まだ実機での確認が十分ではありません。** うまくいかないときは zip を使うか、Issue で教えてください。 -->
<!-- SHA-256 の節には 3 行を並べる (pkg は添付しないが、dmg の中の案内が pkg の値と比べるよう書いているので載せる):
<ZIP_SHA256>  Meltype-mac-<ver>.zip
<DMG_SHA256>  Meltype-mac-<ver>.dmg
<PKG_SHA256>  Meltype-mac-<ver>.pkg (dmg の中の pkg) -->
