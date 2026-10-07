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

## 使い方

- ふつうにローマ字で打つと、下線付きの変換中の文字になります。英単語 (google, github …) は英字のまま
- Space で変換 (候補の一覧が出ます)、Enter で確定、← → で文節の選択、Esc で取り消し
- F6 ひらがな / F7 カタカナ / F8 半角カナ / F9 全角英数 / F10 半角英数
- 変換ボックスが出ているときは Ctrl キーでも同じ: Ctrl+J ひらがな / Ctrl+K カタカナ / Ctrl+; 半角カナ / Ctrl+L 全角英数 / Ctrl+' 半角英数 (JIS は Ctrl+:)
- JIS キーボードの「英数」キーで英数 (直接入力)、「かな」キーで日本語に戻ります (入力メニューの表示も「英数 (Meltype)」「Meltype」に変わります)
- Caps Lock で「Meltype」と「英数 (Meltype)」を切り替えられます (効かないときは入力メニューから選んでください)。US 配列では Caps Lock か入力メニューで切り替えます
- 設定・学習データ・ユーザー辞書は `~/Library/Application Support/Meltype` (入力メニューの「Meltype のデータフォルダを開く」)。
  設定は Windows 版と同じ `config.json` です (自動判定の強さ `DetectionLevel` など)

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
