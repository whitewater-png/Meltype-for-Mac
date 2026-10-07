# Meltype for Mac

Mac の正式な IME の仕組み (Input Method Kit) で動く Meltype です。Windows 版と同じく、日本語を打っている途中の英単語は英字のまま、
英語とも日本語とも読める語は前後の文脈で判定します。Mac では OS が IME としてキーを渡してくれるので、
メニューバーの入力メニューにもふつうの IME として表示されます。

> **テスト版です。** 動かないところやビルドエラーがあれば、エラーの全文とあわせて [Issues](https://github.com/whitewater-png/Meltype-for-Mac/issues) から教えてください。

## しくみ

| 部分 | 中身 |
| --- | --- |
| IME 本体 (`Sources/MeltypeIME`, Swift) | Input Method Kit でキーを受け取り、変換中の文字 (下線付き)・候補の一覧・確定を入力欄に反映する |
| 判定の本体 (`src/Meltype.Mac.Native` → `libMeltypeNative.dylib`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換の流れ・学習・辞書) を NativeAOT で Mac 用のライブラリにしたもの |
| 漢字変換 | [azooKey](https://github.com/azooKey/AzooKeyKanaKanjiConverter) の変換エンジン (MIT License、辞書付き) |
| 英単語の判定 | macOS のスペルチェッカー (英語) |

## 必要なもの

- macOS 13 以降 (Apple シリコン / Intel)
- Xcode、またはコマンドライン ツール (`xcode-select --install`)
- .NET 10 SDK (<https://dotnet.microsoft.com/download>。または `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`)

## ビルドとインストール

```bash
cd mac
./build.sh
```

1. 本体 (C#) を NativeAOT でビルドし、IME (Swift) をビルドして、`build/Meltype.app` を作ります (初回は azooKey の変換エンジンと辞書のダウンロードで時間がかかります)
2. `~/Library/Input Methods/Meltype.app` にインストールします
3. 初めてのときは、入力ソースとして登録します (1 分ほどかかることがあります)。うまくいかなかったときは、いったんログアウトしてログインし直してから、**システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype** を追加します
4. メニューバーの入力メニューで Meltype を選ぶと使えます

作り直したときは `./build.sh` をもう一度実行すれば入れ替わります (動いている Meltype は自動で止め、次にキーを打ったときに新しい Meltype が起動します)。
`~/Library/Input Methods/Meltype.app` を自分で消して入れ直さないでください。macOS が入力ソースの一覧から Meltype を外し、
ログアウトするまで入力メニューに出なくなります (`build.sh` / `install.sh` は Meltype.app を消さずに中身だけを入れ替えます)。
`mac/.build` (Swift のビルド結果) は azooKey の辞書の置き場所として使われることがあるので、消さないでください。

## 新しい版の通知と更新 (通信)

- 既定 ON。入力メニューの「更新を確認する」(チェック付き) でいつでも ON/OFF。設定は `~/Library/Application Support/Meltype/update.json` (`enabled` / `lastCheck` / `notifiedVersion`、0600。`uninstall.sh --remove-data` でデータごと消える)。
- 確認は、IME の起動 5 分後以降に 1 回、その後は前の確認から 24 時間以上たっていれば行う (入力メニューを開いたときも、期限が過ぎていれば確認)。OFF のときは一切通信しない。入力メニューの「今すぐ更新を確認する」は OFF でも手動なら動く。
- 取得先は `https://api.github.com/repos/whitewater-png/Meltype-for-Mac/releases/latest` だけ (GET、15 秒でタイムアウト、`User-Agent: Meltype/<版>`、認証なし)。打った内容は送らない。失敗は黙って次回に回す。
- 判定は Core の `UpdateCheck.Evaluate` (`src/Meltype.Core/Update/UpdateCheck.cs`)。draft / prerelease を無視し、タグ (`v1.0.4-mac` など) の版を数で比べ、資産 `Meltype-mac-<版>.zip` の URL (`github.com/whitewater-png/Meltype-for-Mac/releases/download/`)・サイズ (100 MB 以下)・`digest` (`sha256:` + 64 桁) が全部そろったときだけ有効にする。
- 新しい版が見つかると、入力メニューの先頭に「新しい版があります (v…)…」を出し、通知センターにも 1 度だけ通知する (`notifiedVersion`。許可を断られていれば通知しない)。
- 「更新する」を押したときだけ、zip をダウンロード (リダイレクトは github.com / githubusercontent.com だけ) → SHA-256 を照合 → `ditto -x -k` で展開 → `codesign --verify --deep --strict`・版・バンドル ID の確認 → (展開の前に zip の中身を検査) → 展開したフォルダーの `install.sh --yes` を新しいセッションで切り離して起動する。
  `install.sh` は rsync で入れ替えてから `pkill -x Meltype` で IME を止めるが、切り離した bash は続き、終わると結果のダイアログを出して一時フォルダーを消す (ログは `Application Support/Meltype/update.log`)。ダウンロード・SHA-256・展開前の zip の検査 (エントリ 5000 個以下・展開後 300MB 以下・`..` や絶対パスの名前なし)・展開・署名の検査のどれかで失敗したら、入れてある Meltype には触れずに中止する。
  `install.sh` は入れ替える前に今の Meltype.app を一時フォルダーへ `cp -Rp` で退避し、rsync か入れ替え後の `codesign --verify` が失敗したら退避から書き戻す (成功したら退避を消す。`Meltype.app` フォルダーそのものは消さない)。
- 「更新する」が出るのは、実行中の Meltype が `~/Library/Input Methods/Meltype.app` のときだけ (それ以外 (ビルドの途中の場所など) は「詳細」でリリースページを開くだけ)。
  更新を始めたあとは、切り離した install.sh が終わるまで二重に始まらない (メニューの項目を消し、`Application Support/Meltype/update.lock` で排他ロック)。確認のダイアログは 5 分で時間切れ (「あとで」と同じ)。
- `update.json` は項目ごとに読む (型の違う項目だけ既定値)。ファイルがあるのに壊れていて読めないときは、勝手に通信しないよう OFF にする (ファイルが無いときだけ既定の ON)。更新で残った一時フォルダー (`meltype-update-*`、24 時間以上前) は IME の起動時に消す。
- 電子署名は付けていない。GitHub のアカウントが乗っ取られた場合は防げない (SECURITY.md)。

## 配布物の確認とインストールの影響範囲

テスト版の zip は Apple の公証を受けていません。受け取ったら、まず SHA-256 をリリースページ (または渡した人) の値と見比べてください。

```bash
shasum -a 256 ~/Downloads/Meltype-mac.zip   # ファイル名は実際のものに合わせる
```

`install.sh` (zip の「Install Meltype.command」から呼ばれる) は、次のことをします。これ以外 (管理者権限・ネットワーク通信) はしません (`install.sh` 自身は通信しません。新しい版の確認の通信は IME が行います。上の節)。

1. `codesign --verify --deep --strict` で Meltype.app が壊れていないか確かめる (壊れていたら何も入れずに中止)。入れ直しのときは、入れ替える前に今のものを退避し、入れ替えか入れ替え後の署名の検査に失敗したら元に戻す
2. 実行ファイルと `libMeltypeNative.dylib` の SHA-256 を表示する (リリースページの値と見比べる)
3. `~/Library/Input Methods/Meltype.app` に入れる (入れ直しのときは `rsync -a --delete` で中身だけ入れ替え、動いている Meltype を `pkill -x Meltype` で止める)
4. 隔離属性 `com.apple.quarantine` を外す。**macOS の Gatekeeper の検査をこの Meltype.app について回避する**操作なので、端末では `[y/N]` を聞きます (`--yes` で省略。端末でなく `--yes` も無いときは外さない)
5. 入力ソースとして登録し、`killall imklaunchagent TextInputMenuAgent` で入力メニューと IME の起動役を起動し直す
6. 登録できなかったときだけ `defaults write com.apple.HIToolbox AppleEnabledInputSources -array-add …` で入力ソースの一覧に書き込む
7. 同じバンドル ID の別の Meltype.app を `lsregister -u` で LaunchServices の登録から外す (ファイルは消さない)

`build.sh` は、ビルドした実行ファイルの RPATH からビルドしたマシンの絶対パス (`/Library/Developer/CommandLineTools/...`) を `install_name_tool -delete_rpath` で除いてから署名します。
組み立てたあとの `otool -l build/Meltype.app/Contents/MacOS/Meltype` で、`LC_RPATH` に `/usr/lib/swift`・`@loader_path`・`@executable_path/../Frameworks` だけが残っていることを確かめられます。

## ワンクリックのインストーラー (.pkg)

```bash
cd mac && ./build.sh && ./make-pkg.sh   # dist/Meltype-mac-<version>.pkg ができます
```

ダブルクリックでインストールできる `.pkg` を作ります (「このユーザーのみ」に入れるので、管理者パスワードは不要です)。署名していないので、開くときは右クリック →「開く」が必要です。
**動作確認中です** (実際のインストールは、まだ別の Mac で確認できていません)。

## アンインストール

zip の **`Uninstall Meltype.command`** のダブルクリック、またはターミナルで次を実行します (手順の詳細・消すもの・手で消す方法は、トップの [README.md](../README.md) の「アンインストール」を見てください)。

```bash
bash uninstall.sh                          # 対話式
bash uninstall.sh --yes                    # 確認なしでアプリを削除 (設定・学習データは残す)
bash uninstall.sh --yes --remove-data      # 設定・学習データ・ユーザー辞書も削除 (辞書はデスクトップにバックアップ)
```

入力ソースを外して `~/Library/Input Methods/Meltype.app` を削除します。設定・学習データ・ユーザー辞書 (`~/Library/Application Support/Meltype`) は、聞かれたときに残すか消すかを選べます。
そのあと、いったんログアウトしてログインし直してください。

## 使い方

- ふつうにローマ字で打つと、下線付きの変換中の文字になります。英単語 (google, github …) は英字のまま
- Space で変換 (候補の一覧が出ます)、Enter で確定、← → で文節の選択、Esc で取り消し
- F6 ひらがな / F7 カタカナ / F8 半角カナ / F9 全角英数 / F10 半角英数
- 変換ボックスが出ているときは Ctrl キーでも同じ: Ctrl+J ひらがな / Ctrl+K カタカナ / Ctrl+; 半角カナ / Ctrl+L 全角英数 / Ctrl+' 半角英数 (JIS は Ctrl+:)
- JIS キーボードの「英数」キーで英数 (直接入力)、「かな」キーで日本語に戻ります (入力メニューの表示も「英数 (Meltype)」「Meltype」に変わります)
- Caps Lock で「Meltype」と「英数 (Meltype)」を切り替えられます (効かないときは入力メニューから選んでください)。US 配列では Caps Lock か入力メニューで切り替えます
- 入力メニューの「学習データをすべて消去…」で、変換・予測・登録提案・英語/日本語・英訳・ユーザーモデル・azooKey の学習を消せます (確認あり。ユーザー辞書と設定は消えません)
- 入力メニューの「専門用語集」で、分野 (AI・土木・IT・医療・ネットスラングなど) ごとに専門用語の使用を ON/OFF できます (名称と語数つきのチェック項目。既定はすべて OFF、すぐ全入力欄に反映、`config.json` の `EnabledTermDomains` に保存。読めない `config.json` は上書きしません)。詳しくは [docs/DICTIONARY.md](../docs/DICTIONARY.md)
- パスワード欄など macOS が「秘匿入力」にしている欄 (`IsSecureEventInputEnabled`) では、キーを扱わずアプリに素通しします (アプリが秘匿入力と知らせていない欄は検知できません)
- 設定・学習データ・ユーザー辞書は `~/Library/Application Support/Meltype` (入力メニューの「Meltype のデータフォルダを開く」)。学習データは本人だけが読める権限 (ファイル 0600・フォルダー 0700) で保存します。
  設定は Windows 版と同じ `config.json` です (自動判定の強さ `DetectionLevel` など)

## 開発者向け: AOT 版で設定を保存できるかの確認

managed (dotnet) のテストが通っても、NativeAOT の `libMeltypeNative.dylib` では System.Text.Json の reflection が使えず、列挙型を持つ設定 (`Settings.Mode` など) の保存が例外になることがある (実際に、入力メニューの切り替えが保存失敗になった)。
そのため設定・学習データの JSON はソース生成 (`SettingsJsonContext` ほか) で読み書きしている。JSON の型を変えたり足したりしたときは、AOT 版をビルドして確かめる。

```bash
cd mac && ./build.sh --no-install        # ビルドだけ (インストールしない)
cd .. && python3 tools/check-mac-aot-settings.py
```

`tools/check-mac-aot-settings.py` は、ビルドしたライブラリの関数を直接呼び (Meltype.app は起動しない)、専門用語集の分野と「変換後も続けて入力」を保存して `config.json` の中身を確かめ、別プロセスで読み直しても保たれることを確かめる。
保存場所は環境変数 `MELTYPE_DATA_DIR` で空の一時フォルダーに向ける (.NET は `HOME` ではなくアカウント情報から場所を決めるので、`HOME` の差し替えでは実際の設定に書いてしまう)。一時フォルダーの外を指しているときは、何も書かずに止まる。
`PASS` が出れば成功。

## 困ったとき

- F9 などが効かない: システム設定 → キーボード → キーボードショートカット で F キーの割り当てを外すか、上の Ctrl キーを使う。
- 入力ソースに出てこない: ログアウトしてログインし直す。`~/Library/Input Methods/Meltype.app` があるか確かめる。「+」の一覧に出ないときは、ターミナルで次を実行してから入力メニューを見る (`install.sh` も登録できなかったときに同じことをします、#21)。
  ただし、`defaults read com.apple.HIToolbox AppleEnabledInputSources` と `defaults read com.apple.inputsources AppleEnabledThirdPartyInputSources` のどちらにも Meltype が無いときだけにしてください (両方に入ると、入力ソースの一覧に Meltype が 2 つ出ます)
  ```bash
  defaults write com.apple.HIToolbox AppleEnabledInputSources -array-add \
    '<dict><key>Bundle ID</key><string>io.github.yksr-melt.inputmethod.Meltype</string><key>InputSourceKind</key><string>Keyboard Input Method</string></dict>' \
    '<dict><key>Bundle ID</key><string>io.github.yksr-melt.inputmethod.Meltype</string><key>Input Mode</key><string>io.github.yksr-melt.inputmethod.Meltype.Japanese</string><key>InputSourceKind</key><string>Input Mode</string></dict>' \
    '<dict><key>Bundle ID</key><string>io.github.yksr-melt.inputmethod.Meltype</string><key>Input Mode</key><string>com.apple.inputmethod.Roman</string><key>InputSourceKind</key><string>Input Mode</string></dict>'
  killall TextInputMenuAgent
  ```
- 「英数 (Meltype)」が入力メニューに出ない (モードが増えた版に入れ替えたあと): いったんログアウトしてログインし直してください
- 入力メニューに出ない・選んでも切り替わらない・打っても何も出ない (Meltype.app を消して入れ直したときなど):
  ログアウトしてログインし直すのが確実です。ログアウトしたくないときは、入力ソースとして登録し直し、入力メニューと
  IME を起動する imklaunchagent を起動し直します。それでも打っても何も出ないアプリは、そのアプリを終了して開き直します
  ```bash
  ~/Library/Input\ Methods/Meltype.app/Contents/MacOS/Meltype --register
  killall imklaunchagent TextInputMenuAgent
  ```
  `open` などで Meltype.app を自分で起動しないでください (macOS が起動したものでないと、止まったときに起動し直されません)
- 入力メニューに Meltype がいくつも並ぶ: 同じ Meltype が二重に登録されています。どれを選んでも同じです。ログアウトしてログインし直すと 1 つに戻るはずです
- 動きがおかしい: ログを見る

  ```bash
  log stream --predicate 'process == "Meltype"' --level debug
  ```

- 止まってしまった: `pkill -x Meltype` (次にキーを打つと macOS が起動し直します)

## Windows 版との違い (今のところ)

- 設定画面・トレイ・ユーザー辞書の画面はありません (`config.json` を直接編集)
- アプリの種類 (コード / 一般) の判定は、行ごとの判定はせず、コード系アプリ (Terminal・VS Code・Xcode など) では英数から始めるだけです
- 英数状態でローマ字を検知して日本語に戻す機能は、まだありません
