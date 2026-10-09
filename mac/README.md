# Meltype for Mac

Mac の正式な IME の仕組み (Input Method Kit) で動く Meltype です。Windows 版と同じく、日本語を打っている途中の英単語は英字のまま、
英語とも日本語とも読める語は前後の文脈で判定します。Mac では OS が IME としてキーを渡してくれるので、
メニューバーの入力メニューにもふつうの IME として表示されます。

> **テスト版です。** 動かないところやビルドエラーがあれば、エラーの全文とあわせて [Issues](https://github.com/whitewater-png/Meltype-for-Mac/issues) から教えてください。

## しくみ

| 部分 | 中身 |
| --- | --- |
| IME 本体 (`Sources/MeltypeIME`, Swift) | Input Method Kit でキーを受け取り、変換中の文字 (下線付き)・候補の一覧・確定を入力欄に反映する |
| 辞書の管理画面「Meltype 辞書」(`Sources/MeltypeDictionary` + `Sources/MeltypeDictionaryKit`, Swift・AppKit) | ユーザー辞書と専門用語集の一覧・登録・編集・削除。IME とは別のふつうのアプリで、`Meltype.app/Contents/Helpers/MeltypeDictionary.app` に入っている (下の「辞書の管理」) |
| 判定の本体 (`src/Meltype.Mac.Native` → `libMeltypeNative.dylib`) | Windows 版と共通の C# の部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換の流れ・学習・辞書) を NativeAOT で Mac 用のライブラリにしたもの |
| 漢字変換 | [azooKey](https://github.com/azooKey/AzooKeyKanaKanjiConverter) の変換エンジン (MIT License、辞書付き) |
| 英単語の判定 | macOS のスペルチェッカー (英語) |

## 必要なもの

- macOS 13 以降。配布している zip は Apple シリコン (M1 以降) 用です。ソースからのビルドは、ビルドした Mac の CPU 用になります (Apple シリコンの Mac なら Apple シリコン用、Intel の Mac なら Intel 用。Intel 用のビルドは配布していません)
- Xcode、またはコマンドライン ツール (`xcode-select --install`)
- .NET 10 SDK (<https://dotnet.microsoft.com/download>。または `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`)

## ビルドとインストール

```bash
cd mac
./build.sh
```

1. 本体 (C#) を NativeAOT でビルドし、IME と辞書の管理画面 (Swift) をビルドして、`build/Meltype.app` を作ります (初回は azooKey の変換エンジンと辞書のダウンロードで時間がかかります)。
   辞書の管理画面は `build/Meltype.app/Contents/Helpers/MeltypeDictionary.app` に入れ、中から外へ署名して、最後に `codesign --verify --deep --strict` で確かめます
2. `~/Library/Input Methods/Meltype.app` にインストールします
3. 初めてのときは、入力ソースとして登録します (1 分ほどかかることがあります)。うまくいかなかったときは、いったんログアウトしてログインし直してから、**システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype** を追加します
4. メニューバーの入力メニューで Meltype を選ぶと使えます

作り直したときは `./build.sh` をもう一度実行すれば入れ替わります (動いている Meltype は自動で止め、次にキーを打ったときに新しい Meltype が起動します)。
`~/Library/Input Methods/Meltype.app` を自分で消して入れ直さないでください。macOS が入力ソースの一覧から Meltype を外し、
ログアウトするまで入力メニューに出なくなります (`build.sh` / `install.sh` は Meltype.app を消さずに中身だけを入れ替えます)。
`mac/.build` (Swift のビルド結果) は azooKey の辞書の置き場所として使われることがあるので、消さないでください。

## 設定 (「Meltype 辞書」の「設定」タブ)

- 入力メニューの「設定・辞書…」で開く画面の「設定」タブ (⌘3) で、Windows 版の詳細設定のうち Mac で効く項目 (自動判定の強さ・ライブ変換・句読点の表記・予測変換・登録提案・ログなど) を変えられる。変えるとすぐ `config.json` に保存され、開いているすべての入力欄に反映される。「既定値に戻す…」もある。
- 「変換後も続けて入力できる」(既定 OFF) と「Shift+Enter で確定して改行」(既定 ON) もここにある。「Shift+Enter で確定して改行」を OFF にすると、変換中の Shift+Enter は Enter と同じく確定だけになる (変換ボックスが空のときは、どちらでもアプリにそのまま渡す)。

## 新しい版の通知と更新 (通信)

- 既定 ON。入力メニューの「更新を確認する」(チェック付き) でいつでも ON/OFF。設定は `~/Library/Application Support/Meltype/update.json` (`enabled` / `lastCheck` / `notifiedVersion`、0600。`uninstall.sh --remove-data` でデータごと消える)。
- 確認は、IME の起動 5 分後以降に 1 回、その後は前の確認から 24 時間以上たっていれば行う (入力メニューを開いたときも、期限が過ぎていれば確認)。OFF のときは一切通信しない。入力メニューの「今すぐ更新を確認する」は OFF でも手動なら動く。
- 取得先は `https://api.github.com/repos/whitewater-png/Meltype-for-Mac/releases/latest` だけ (GET、15 秒でタイムアウト、`User-Agent: Meltype/<版>`、認証なし)。打った内容は送らない。失敗は黙って次回に回す。
- 判定は Core の `UpdateCheck.Evaluate` (`src/Meltype.Core/Update/UpdateCheck.cs`)。draft / prerelease を無視し、タグ (`v1.0.4-mac` など) の版を数で比べ、資産 `Meltype-mac-<版>.zip` の URL (`github.com/whitewater-png/Meltype-for-Mac/releases/download/`)・サイズ (100 MB 以下)・`digest` (`sha256:` + 64 桁) が全部そろったときだけ有効にする。
- 新しい版が見つかると、入力メニューの先頭に「新しい版があります (v…)…」を出し、通知センターにも 1 度だけ通知する (`notifiedVersion`。許可を断られていれば通知しない)。
- 「更新する」を押したときだけ、zip をダウンロード (リダイレクトは github.com / githubusercontent.com だけ) → SHA-256 を照合 → `ditto -x -k` で展開 → `codesign --verify --deep --strict`・版・バンドル ID の確認 → (展開の前に zip の中身を検査) → 展開したフォルダーの `install.sh --yes` を新しいセッションで切り離して起動する。
  `install.sh` は rsync で入れ替えてから `pkill -x Meltype` で IME を止めるが、切り離した bash は続き、終わると結果のダイアログを出して一時フォルダーを消す (ログは `Application Support/Meltype/update.log`)。ダウンロード・SHA-256・展開前の zip の検査 (エントリ 5000 個以下・展開後 300MB 以下・`..` や絶対パスの名前なし)・展開・署名の検査のどれかで失敗したら、入れてある Meltype には触れずに中止する。
  `install.sh` は入れ替える前に今の Meltype.app を一時フォルダーへ `cp -Rp` で退避し、rsync か入れ替え後の `codesign --verify` が失敗したら退避から書き戻す (成功したら退避を消す。`Meltype.app` フォルダーそのものは消さない)。
- 1.0.4〜1.0.9 をお使いの場合は、入力メニューの更新が失敗するので、リリースページから zip をダウンロードして、「Install Meltype.command」で手で入れ替えてください (設定と辞書はそのまま残ります。1.0.10 以降は更新が使えます)。
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
2. 実行ファイル (IME と辞書の管理画面) と `libMeltypeNative.dylib` の SHA-256 を表示する (リリースページの値と見比べる)
3. `~/Library/Input Methods/Meltype.app` に入れる (入れ直しのときは `rsync -a --delete` で中身だけ入れ替え、動いている Meltype を `pkill -x Meltype` で止める。
   開いている辞書の管理画面も `pkill -f …/Meltype.app/Contents/Helpers/MeltypeDictionary.app/` で閉じる。辞書の変更はその都度保存済みなので失われない)
4. 隔離属性 `com.apple.quarantine` を外す。**macOS の Gatekeeper の検査をこの Meltype.app について回避する**操作なので、端末では `[y/N]` を聞きます (`--yes` で省略。端末でなく `--yes` も無いときは外さない)
5. 入力ソースとして登録し、`killall imklaunchagent TextInputMenuAgent` で入力メニューと IME の起動役を起動し直す
6. 登録できなかったときだけ `defaults write com.apple.HIToolbox AppleEnabledInputSources -array-add …` で入力ソースの一覧に書き込む
7. 同じバンドル ID の別の Meltype.app (展開したフォルダーのものなど) を Spotlight (`mdfind`) で探し、`lsregister -u` で LaunchServices の登録から外す (ファイルは消さない)。そのあと入れた方を `lsregister -f` で登録し直す

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
bash uninstall.sh --yes                    # 確認なしでアプリを削除 (データを消すかは聞く。端末でなければ残す)
bash uninstall.sh --yes --keep-data        # 確認なしでアプリを削除し、データは聞かずに残す
bash uninstall.sh --yes --remove-data      # 設定・学習データ・ユーザー辞書・自作の専門用語集も削除 (辞書と専門用語集はデスクトップの Meltype-backup-<日時> にバックアップ)
```

入力ソースを外して `~/Library/Input Methods/Meltype.app` を削除します。辞書の管理画面「Meltype 辞書」は Meltype.app の中にあるので一緒に消えます (開いていれば先に閉じます)。
設定・学習データ・ユーザー辞書・自作の専門用語集 (`~/Library/Application Support/Meltype`。自作の専門用語集の `terms/`・除外した専門用語の `terms-excluded.txt`・`userdict.txt.bak`・ロック用の隠しファイルも含む) は、聞かれたときに残すか消すかを選べます。消すときは、`userdict.txt`・`terms/`・`terms-excluded.txt` を先にデスクトップの `Meltype-backup-<日時>` フォルダーへ写します。
そのあと、いったんログアウトしてログインし直してください。

## 使い方

- ふつうにローマ字で打つと、下線付きの変換中の文字になります。英単語 (google, github …) は英字のまま
- Space で変換 (候補の一覧が出ます)、Enter で確定、← → で文節の選択、Esc で取り消し
- F6 ひらがな / F7 カタカナ / F8 半角カナ / F9 全角英数 / F10 半角英数
- 変換ボックスが出ているときは Ctrl キーでも同じ: Ctrl+J ひらがな / Ctrl+K カタカナ / Ctrl+; 半角カナ / Ctrl+L 全角英数 / Ctrl+' 半角英数 (JIS は Ctrl+:)
- JIS キーボードの「英数」キーで英数 (直接入力)、「かな」キーで日本語に戻ります (入力メニューの表示も「英数 (Meltype)」「Meltype」に変わります)
- Caps Lock で「Meltype」と「英数 (Meltype)」を切り替えられます (効かないときは入力メニューから選んでください)。US 配列では Caps Lock か入力メニューで切り替えます
- 入力メニューの「学習データをすべて消去…」で、変換・予測・登録提案・英語/日本語・英訳・ユーザーモデル・azooKey の学習を消せます (確認あり。ユーザー辞書と設定は消えません)
- 「設定・辞書…」の画面の「専門用語集(サンプル)」タブで、分野 (AI・土木・IT・医療・ネットスラング・映像など) ごとに専門用語の使用を ON/OFF できます (既定はすべて OFF、すぐ全入力欄に反映、`config.json` の `EnabledTermDomains` に保存。読めない `config.json` は上書きしません)。詳しくは [docs/DICTIONARY.md](../docs/DICTIONARY.md)
- 入力メニューの「設定・辞書…」で、辞書の管理画面「Meltype 辞書」が開きます (下の「辞書の管理」)
- パスワード欄など macOS が「秘匿入力」にしている欄 (`IsSecureEventInputEnabled`) では、キーを扱わずアプリに素通しします (アプリが秘匿入力と知らせていない欄は検知できません)
- 設定・学習データ・ユーザー辞書は `~/Library/Application Support/Meltype` (「Meltype 辞書」の「ファイル」メニューの「データフォルダを開く」)。学習データは本人だけが読める権限 (ファイル 0600・フォルダー 0700) で保存します。
  設定は Windows 版と同じ `config.json` です (自動判定の強さ `DetectionLevel` など)

## 辞書の管理 (「Meltype 辞書」)

入力メニューの **「設定・辞書…」** で開きます。ふつうのアプリなので、ウインドウでそのまま日本語を打てます (Meltype も使えます)。
すでに開いていれば、新しく起動せずに前に出ます。ウインドウを閉じると終わります。

**ユーザー辞書** のタブ

- 一覧: 読み・単語・登録順 (列の見出しで並べ替え。既定は新しい順)。検索の欄 (⌘F) は読み・単語のどこかに含まれる語を出します (カタカナ・全角英字・大文字で探しても見つかる)。
- 登録 (⌘N / 「登録…」)・編集 (Return / ダブルクリック / 「編集…」)・削除 (Delete / 「削除…」。複数選択可)。
  入力のたびに確かめ、だめな理由を赤字で出します (読み 2 文字以上・100 文字まで、単語が空でない、改行・タブ不可 = 入力メニューの登録と同じ規則。同じ読みと単語の重複も)。
  読みの欄の「ひらがなにする」は、ローマ字・カタカナで打った読みをひらがなにします (押したときだけ。黙って変えません)。
- 削除は確認してから行い、**⌘Z (編集 → 取り消す) で元の位置に戻せます** (⇧⌘Z でやり直し)。登録・編集・取り込みも取り消せます。
  削除・編集の直前の内容は、データフォルダの `userdict.txt.bak` にも残ります (1 世代)。
- 取り込み (⇧⌘I): Microsoft IME・Google 日本語入力で書き出したファイル、Meltype の `userdict.txt` (UTF-8 / UTF-16)。⌘Z で、取り込みで新しく登録した語だけをまとめて消せます。書き出し (⇧⌘E): Microsoft IME の形式 (UTF-16、0600)。
- `userdict.txt` を読めない (UTF-8 / UTF-16 でない文字コード・権限) ときや、大きすぎて (20 MB 超) 読み込んでいないときは、一覧の上に注意を出します (0 語に見えても、ファイルは消えていません。読めないファイルには書きません)。

**専門用語集(サンプル)** のタブ

**同梱の専門用語集はサンプルです。** 利用者が自分の用途に合わせて育てていくための出発点で、完成した辞書ではありません。不要な語は「除外」、直したい語は「直す」、使いたい語は「複製」、足したい語はユーザー辞書への登録で、自分の辞書にしていってください。

- 左に分野 (AI・土木・IT・医療・ネットスラング・映像)。チェック (または選んで Space) で ON/OFF (`config.json` の `EnabledTermDomains`)。最後の行は「除外した語 (すべての分野)」。
- 右に選んだ分野の語 (IT は約 1.5 万語。裏で読み込み、一覧は表示する行だけを作るので軽い)。検索 (読み・語・注記)・並べ替え・「すべて / 使う語だけ / 除外した語だけ」の絞り込み。
- 同梱の語は書き換えられないので、次の意味になります。
  - **除外する** (Delete): その語を変換に使わない (強制型・候補・予測のどれにも出ない)。除外中の語は取り消し線と「除外中」で一覧に残り、「除外をやめる」・⌘Z で戻せます。保存先は `terms-excluded.txt` (`config.json` とは別、1 行に「読み[Tab]語」、0600)
  - **直してユーザー辞書へ** (Return): 元の語を除外して、直した語をユーザー辞書に登録 (ユーザー辞書 > 専門用語集 > 変換エンジン の順なので、直した語が先に出る)。⌘Z で両方元に戻る
  - **ユーザー辞書へ複製**: そのままユーザー辞書にコピー (複数選択可。何千語でも 1 回の保存で済ませ、⌘Z 1 回で新しく登録した語だけを消せる)

**自作の専門用語集** (「専門用語集(サンプル)」タブの、同梱の分野の下。「自作」の印つき)

ユーザー辞書の語を分野ごとに整理したいときに、自分で専門用語集を作れます。保存先は `~/Library/Application Support/Meltype/terms/terms-user-xxxxxxxx.txt` (1 分野 1 ファイル、同梱と同じ形式、ファイル 0600・フォルダー 0700。ID は `user-` + 16 進 8 桁で、同梱の分野の ID とは重なりません)。

- **新しく作る** (⌥⌘N / 左下の「自作の専門用語集」メニュー): 名前 (50 文字まで。同梱・自作のほかの分野とかぶる名前は不可) を付けます。**作るとすぐ有効**になります (有効にしないと、移した語が黙って変換に使われなくなるため)。名前の変更・削除・書き出し・取り込みも同じメニュー (と右クリック) から。
- **ユーザー辞書から移す** (ユーザー辞書のタブで語を選び、「専門用語集へ移す…」/ ⇧⌘M / 右クリック): 移し先 (自作の専門用語集か「新しい専門用語集…」) を選びます。語は**移る** (ユーザー辞書からは消える) ので、変換結果は変わりません。専門用語集に入れられない語は、ユーザー辞書に残して一覧で知らせます。**⌘Z で両方元に戻ります** (専門用語集から消え、ユーザー辞書の元の位置に戻る)。
  先に専門用語集のファイルへ書き、そのあとでユーザー辞書から消します。2 つ目に失敗したときは、語は両方に残ったまま理由を出します (語は消えません)。
- **自作の分野の語**: 追加・編集・削除ができます (同梱の分野は除外・直す・複製のまま)。削除は確認してから行い、⌘Z で戻せます。分野の削除も同じで、語ごと戻ります (有効だったかも)。
- **書き出し・取り込み**: 書き出しは、同梱と同じ形式 (先頭に「# 名称:」「# 出典: 自作」) の UTF-8 テキスト。取り込みは、そのファイルを**新しい**自作の分野にします (名前は「# 名称:」、無ければファイル名。同じ名前があれば「名前 (2)」。1 行ずつ検めて、不正な行は飛ばして数を知らせます。20 MB・20 万語まで)。
- **変換での扱い**: 自作の分野の語は、**ユーザー辞書の語と同じ**です (読みが短くても・日常語と同じ読みでも・英単語でも強制。同じ読みでは、あとから足した語が先)。同梱の専門用語集のような「読みが 4 文字以上・英単語は候補だけ」という振り分けは、自作の分野にはありません。ユーザー辞書の語を持っている語と読みが同じときは、ユーザー辞書の語が先に出ます (優先順位は ユーザー辞書 > 専門用語集 > 変換エンジン)。
- 別のプロセス (IME) の変更は、ファイルの版を 0.5 秒ごとに確かめて拾います。読めない分野のファイルには書きません (ほかの分野は使えます)。

**検索の使い方** (両方のタブ共通。検索の欄は ⌘F で呼び出し、Esc で消去)

- 打つたびに絞り込みます (Enter は不要。5000 語を超える一覧では 0.12 秒だけ間引くので、2 万語でも軽い)。
- 空白 (半角・全角) で区切ると AND です。例: `ほけん 保険` は両方を含む語だけ。読み・語 (専門用語集は注記も) のどこかに含まれていれば一致で、カタカナ・全角英数・大文字小文字は区別しません。
- 並び: 検索中は、(1) 読みか語が完全一致 → (2) 読みの前方一致 → (3) 語の前方一致 → (4) 途中に含む → (5) 注記だけ、の順に上へ出します (同じ段階は登録順)。列の見出しで並べ替えを選んでいる間は、その指定を優先します。
- 件数は「3 / 全 120 語」(絞り込み中)・「全 120 語」。1 件もなければ一覧の上に「見つかりません」を出します。
- ユーザー辞書で登録・編集した直後は、その語が選ばれて見える位置までスクロールします。検索で絞り込み中で、その語が条件に合わないときは、絞り込みを解除せず「登録しました (検索条件に合わないため表示されていません)」と出します。

**動いている IME への反映と、同時の変更**

- 画面での変更はすぐファイルに保存し、動いている IME は、ファイルの版 (更新時刻・大きさ) を 0.5 秒ごとに確かめて読み直します (次にキーを打ったときに反映。アプリを開き直す必要はありません)。
  逆に、IME の側の登録 (入力メニューの「選択中の文字を登録」・登録提案) や分野の切り替えも、画面に 1.5 秒以内に反映されます。
- 変更 (登録・編集・削除・取り込み・除外・分野の切り替え) は、プロセスをまたぐロック (データフォルダの隠しファイル `.userdict.txt.lock` など。flock、0600) の中で、ファイルを読み直してから書きます。
  IME と画面が同時に書いても、片方の変更が消えることはありません。読めないファイル (権限・文字コード) には書かず、理由を出します。

**しくみ**: 画面は IME と同じ `libMeltypeNative.dylib` (外側の `Meltype.app/Contents/Frameworks`) を dlopen して、C# の `DictionaryManagement` / `UserDictionary` / `TermDomains` を呼びます (入力の規則・ファイルの形式・ロックを 1 か所にするため。FFI の版数は `Exports.AbiVersion` を参照)。
IME が背面専用のアプリ (`LSBackgroundOnly`) で、自分のウインドウがキーボード入力を受けられない (登録のダイアログが打てず、osascript のダイアログにした経緯がある) ので、画面は別のアプリにしています。

### 実機での確認の手順 (画面の自動テストは無い)

実際のデータに触らないよう、空の一時フォルダーを保存場所にして、ビルドした画面を直接起動します (IME は起動しません)。

```bash
cd mac && ./build.sh --no-install
D="$(mktemp -d)/data"
MELTYPE_DATA_DIR="$D" build/Meltype.app/Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary --self-test   # ロジック + 本体を通した操作 (PASS が出る)
MELTYPE_DATA_DIR="$D" build/Meltype.app/Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary              # 画面を出す (ウインドウの副題が $D になっていること)
```

1. ⌘N で「きごうとう / 記号等」を登録 → 一覧に出る。もう一度同じものを入れると「すでに登録されています」が赤字で出て、登録ボタンが押せない。読み「き」でも理由が出る
2. 行を選んで Return → 単語を「記号党」に直して保存 → 登録順はそのまま
3. 2 語を ⌘ クリックで選んで Delete → 確認 → 削除。⌘Z で元の位置に戻る。⇧⌘Z でもう一度消える。`$D/userdict.txt.bak` がある
4. 取り込み・書き出し: 書き出したファイルを取り込むと「0 語を登録しました / 登録済み 2」
5. 専門用語集: IT を選ぶ (1.5 万語がすぐ出る・スクロールが重くない)。「土木」のチェックを入れる。語を選んで Delete → 取り消し線・「除外中」。Return で直す → ユーザー辞書のタブに直した語が出る。⌘Z で両方戻る
6. 検索の欄で ↓ を押すと一覧に移る。VoiceOver (⌘F5) で、一覧・ボタン・チェック・赤字の理由・状態の表示が読み上げられる
7. システム設定 → 外観 をダークにして、文字・取り消し線・赤字が読めること
8. もう一度同じコマンドで起動すると、新しく起動せずに最初のウインドウが前に出る
9. 入れたあと (自分の Mac で ./build.sh): 入力メニューの「設定・辞書…」で前に出ること。画面で登録した語が、ほかのアプリでの変換にすぐ出ること。入力メニューから登録した語が、画面の一覧に出ること

## 開発者向け: 変換の速度と正しさの計測 (`MeltypeIME --bench`)

azooKey の変換まわり (`Converter.swift`) を変えたときの計測用です。IMKServer は立てず、標準出力に表を出して終わります (入力ソースには影響せず、`~/Library` にも書きません。学習データは一時フォルダーです)。

```bash
cd mac && swift build -c release
.build/release/MeltypeIME --bench   # 1 分ほどかかる。辞書は同じフォルダーの *.bundle から読む
```

毎キーごとに止める従来の変換器と、差分変換の新しい変換器を並べて、(1) 10〜400 文字を 1 文字ずつ打つ速度 (Core と同じ「予測 → 変換 → 文節ごとの候補」の 3 呼び出し)、(2) 一括変換と 1 文字ずつ打った結果の一致、(3) ランダムな編集・学習を挟んだときの新旧の一致、(4) 予測だけが続くときの並びの違い、を出します。

## 開発者向け: AOT 版で設定を保存できるかの確認

managed (dotnet) のテストが通っても、NativeAOT の `libMeltypeNative.dylib` では System.Text.Json の reflection が使えず、列挙型を持つ設定 (`Settings.Mode` など) の保存が例外になることがある (実際に、入力メニューの切り替えが保存失敗になった)。
そのため設定・学習データの JSON はソース生成 (`SettingsJsonContext` ほか) で読み書きしている。JSON の型を変えたり足したりしたときは、AOT 版をビルドして確かめる。

```bash
cd mac && ./build.sh --no-install        # ビルドだけ (インストールしない)
cd .. && python3 tools/check-mac-aot-settings.py
```

`tools/check-mac-aot-settings.py` は、ビルドしたライブラリの関数を直接呼び (Meltype.app は起動しない)、専門用語集の分野と「変換後も続けて入力」「Shift+Enter で確定して改行」(設定タブの項目。後者は既定 ON) を保存して `config.json` の中身を確かめ、別プロセスで読み直しても保たれることを確かめる。
辞書の管理画面の関数 (`meltype_userdict_*` / `meltype_term_*`) も、登録・重複・編集・削除と復元・取り込み・書き出し・除外・専門用語の編集をして、ファイル (0600) を別プロセスで読み直す。
さらに、4 つのプロセスが同時に 25 語ずつ登録しても 100 語すべて残ること、IME のつもりのプロセス (`meltype_create` のセッション) が動いたまま、別のプロセスの登録が変換の結果に出る・除外と分野の切り替えで版が進むこと、IME の側の登録が画面の登録を消さないことを確かめる。
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
- 「設定・辞書…」で何も開かない・「見つかりません」と出る: `~/Library/Input Methods/Meltype.app/Contents/Helpers/MeltypeDictionary.app` があるか確かめ、無ければ入れ直す。
  「本体の版が合いません」と出るときは、Meltype.app の中身が古い版と混ざっているので入れ直す
- 画面で「ほかの画面 (またはプロセス) が … を保存中」と出る: IME などが同じファイルを書いている途中 (3 秒待っても終わらなかった)。少し待ってからやり直す (変更はしていない)

## Windows 版との違い (今のところ)

- トレイはありません。設定・ユーザー辞書・専門用語集は「Meltype 辞書」(入力メニューの「設定・辞書…」) で変えます。設定タブにあるのは Mac で効く項目だけです (アプリ別設定などは `config.json` を直接編集) (Windows 版のユーザー辞書の画面にある「読みから候補を出す」はありません)
- アプリの種類 (コード / 一般) の判定は、行ごとの判定はせず、コード系アプリ (Terminal・VS Code・Xcode など) では英数から始めるだけです
- 英数状態でローマ字を検知して日本語に戻す機能は、まだありません
