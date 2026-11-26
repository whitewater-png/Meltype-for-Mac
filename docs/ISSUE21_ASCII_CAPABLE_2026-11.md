# Issue #21「日本語モードを ASCII 対応でない入力ソースとして登録してほしい」判断メモ (2026-11)

基準: origin/main = f558dfa (v1.2.9)。製品コードは変えていない (調査のみ)。macOS 27.2.1 の実機で読み取りだけ行った。

## 推奨 (先に一行)

**(b) まずモード側の `tsInputModeCharacterRepertoireKey` だけを外した試験ビルドで、`kTISPropertyInputSourceIsASCIICapable` が false になるかを実機で確かめる。辞書のキー (`io.github.yksr-melt.inputmethod.Meltype.Japanese`) の改名は、更新する人の「有効な入力ソース」の登録が外れるおそれがあるので、リョウさんの判断なしには入れない。**

## 現状 (証拠)

### 報告者の主張 (issue 本文)
- ことえり・azooKey の日本語モードは ascii=no、Meltype の日本語モードは ascii=yes。
- 動機: Emacs の日本語入力パッチ (ns-inline-patch) が「今の入力ソースが ASCII 対応でないときだけ」キーを入力メソッドに渡さず Emacs に渡す。Meltype だと `C-x b` の `b` が変換中の文字になり、dired の 1 文字コマンドが変換待ちになる (`emacs -Q` で azooKey は起きず Meltype は起きると報告者が確認)。
- 報告者は Info.plist の違い 2 点を挙げ、「どちらが効くかは未確認」としている。

### この Mac で測った値 (リポジトリ外の作業フォルダーの小さな Swift で `TISCreateInputSourceList` を読んだだけ。選択・変更なし)
| 入力ソース | ascii |
|---|---|
| `com.apple.inputmethod.Kotoeri.RomajiTyping.Japanese` (ことえり) | 0 |
| `com.google.inputmethod.Japanese.base` (Google 日本語入力のひらがな) | 0 |
| `com.justsystems.inputmethod.atok36.Japanese` (ATOK) | 0 |
| `io.github.yksr-melt.inputmethod.Meltype.Japanese` | **1** |
| `io.github.yksr-melt.inputmethod.Meltype.Roman` / ことえり・Google の Roman | 1 (英数なので正しい) |

- 日本語系で ascii=1 なのは Meltype の日本語モードだけ (この Mac の ATOK・Google・ことえりの全モードを見た範囲)。
- **azooKey はこの Mac に入っていない**ので自分では測れていない (報告者の値のまま)。

### Info.plist の違い (`mac/Resources/Info.plist`)
| | Meltype | ことえり (JapaneseIM-RomajiTyping の appex) | Google / ATOK |
|---|---|---|---|
| 日本語モードの辞書のキー | `io.github.yksr-melt.inputmethod.Meltype.Japanese` (独自名) | `com.apple.inputmethod.Japanese` (標準名) | 同左 (標準名) |
| 日本語モードの `TISInputSourceID` | キーと同じ | `com.apple.inputmethod.Kotoeri.RomajiTyping.Japanese` (キーと別) | `com.google.inputmethod.Japanese.base` など (キーと別) |
| 日本語モードの `tsInputModeCharacterRepertoireKey` | **あり** (`Hira, Kana, Hani, Latn`、83 行目付近) | なし | なし |
| 入力メソッド全体の `tsInputMethodCharacterRepertoireKey` | `Hira, Kana, Hani, Latn` (52 行目) | `Hira, Kana, Hani, Latn` | `Hira, Kana, Hani, Latn` |
| `TISIntendedLanguage` | ja | ja | ja |

- 全体側の `Latn` はことえり・Google・ATOK にもあり、どれも日本語モードは ascii=0。よって**全体側の `Latn` は原因ではない** (測定から言える)。
- 残る候補は (1) モード側の `tsInputModeCharacterRepertoireKey` に `Latn`、(2) 辞書のキーが標準名でない、の 2 つ。報告者の見立てと同じで、**どちらが効くかは今の手元の証拠では決まらない** (この Mac では試験ビルドを入れていない。入れると利用者の IME を入れ替えるため)。
- 参考: Squirrel (Rime) に「`tsInputModeCharacterRepertoireKey` から `Latn` を外す」変更がある (検索で見つけただけで、中身は読んでいない)。(1) が効く傍証になりうるが未確認。
- Meltype の英数モードは既に標準名キー `com.apple.inputmethod.Roman` で作ってあり、Caps Lock 切り替えのための判断だった (docs/IMPROVEMENT_PLAN.md の P2)。
- Mac のコードは `kTISPropertyInputSourceIsASCIICapable` を読んでいない (`git grep` で 0 件)。`TISSetInputMethodKeyboardLayoutOverride` も使っていない。

## 変えたらどうなるか

### macOS 側の扱い (Apple の文書より)
- 入力メソッド・モードが選ばれているとき、キー変換には**直近に使った ASCII 対応のキーボード配列**が使われ、キーボードビューアにもそれが出る。別の配列にしたいときは `TISSetInputMethodKeyboardLayoutOverride` を呼ぶ ([Text Input Source Services Reference](https://web.archive.org/web/20140604054226/https:/developer.apple.com/library/Mac/documentation/TextFonts/Reference/TextInputSourcesReference/Reference/reference.html))。
- `TISCopyCurrentASCIICapableKeyboardInputSource` は「最後に使った ASCII 対応の入力ソース」を返す。ASCII 対応でない IME の間に、アプリが「英字に戻す先」として使う ([同上](https://web.archive.org/web/20140604054226/https:/developer.apple.com/library/Mac/documentation/TextFonts/Reference/TextInputSourcesReference/Reference/reference.html))。
- アプリ側の例: Firefox は「今の入力ソースが IME のモードで、かつ ASCII 対応でない」を「IME が開いている」と判定し、キーイベントの文字を ASCII 対応の配列で計算する ([TextInputHandler.mm](https://raw.githubusercontent.com/mozilla-firefox/firefox/main/widget/cocoa/TextInputHandler.mm))。Deskflow も同様に IME 使用中は ASCII 対応の配列で変換する ([deskflow #10039](https://github.com/deskflow/deskflow/pull/10039/files/b5f126d0724352f04e3e6ba16e37f717d0fbe9df))。つまり ascii=0 にすると、**アプリは Meltype の日本語モードを「かな入力中の IME」として扱い、他の日本語 IME と同じ分岐に入る**。報告の Emacs はこの型。
- パスワード欄 (セキュア入力) で ASCII 対応の入力ソースに切り替わる動作は、macOS の既知の挙動だが、**今回は一次資料を引けていない** (未確認)。今は ascii=1 なので切り替えが起きず、Meltype が日本語モードのまま素通しに頼っている可能性がある (コードは `IsSecureEventInputEnabled()` で素通しにしている。`InputController.swift` 150〜155 行付近)。ascii=0 にすると、ことえりと同じく別の ASCII 対応ソース (「英数 (Meltype)」や ABC) に切り替わる可能性がある。実機で未確認。
- Cmd ショートカットなどのキー変換: Meltype はキーボード配列の指定を持たないので、ascii=0 にすると「直近の ASCII 対応の配列」が使われる (Dvorak・AZERTY などを使う人は、直近に使った配列が基準になる)。今は ascii=1 の自分自身が基準になっている可能性があるが、未確認。Spotlight・Terminal での違いも実機では見ていない。

## 得と損

| | 得 | 損 |
|---|---|---|
| ascii=0 にする | Emacs (inline パッチ)・Firefox など、ことえり/azooKey と同じ分岐に入る。`C-x b`・dired が日本語モードで普通に動く。他の日本語 IME と挙動が揃う | 「英語も自動で打てる」設計の Meltype が、アプリから「日本語専用の IME」とみなされる。パスワード欄などで自動で別ソースに切り替わる動作が増える可能性。Meltype のモード自動記憶 (アプリ別に英数を覚える) と干渉するかは未確認 |
| 辞書のキーを標準名にする | 標準名に揃うので、Caps Lock・システムの日本語キー対応と相性がよい可能性 | **下の「更新する人への影響」の登録が外れるおそれ** |
| 変えない | 利用者への影響なし | 報告者の Emacs の操作性は直らない |

## 安全面
- パスワード欄: コードは秘匿入力のとき素通しするので、**ascii を変えても入力内容を Meltype が見る経路は増えない** (学習・提案・ログに渡さない設計。読んだだけで実機は未確認)。macOS が ASCII 対応ソースに切り替える挙動が加わるのは、むしろ安全側 (ことえりと同じ)。
- 入力メソッド全体の属性ではなく、モードの属性を変えるだけなので、権限・署名・entitlements・ネットワークには影響しない (Info.plist のキーの変更のみ)。
- 変えた後に起きうる害は「英数のつもりでキーが日本語として処理される」ではなく、「別ソースに切り替わって戸惑う」「登録が外れて入力メニューから消える」で、後者の方が重い (下記)。

## 更新する人への影響
- 今の `build.sh`/`install.sh` は Meltype.app を**消さずに中身だけを入れ替える**ので、macOS の入力ソースの登録は残る (mac/README.md 40 行目付近)。Info.plist の変更が反映されるには、IME の再起動と TIS の再読み込み (ログアウト・ログインが確実) が必要になる可能性が高い (`ComponentInputModeDict` の変更が実行中に反映されるかは未確認)。「英数」モードを足した版 (README 304 行目) は「ログアウトしてログインし直してください」と案内している前例がある。
- **有効な入力ソースの一覧 (`AppleEnabledThirdPartyInputSources`) は、モードを `Input Mode` に辞書のキー名で記録している。** この Mac では実際に次のとおり (読み取りのみ):
  - `Input Mode` = `io.github.yksr-melt.inputmethod.Meltype.Japanese` ← 日本語 (辞書のキーと `TISInputSourceID` が同じ)
  - `Input Mode` = `com.apple.inputmethod.Roman` ← 英数 (**辞書のキー**。`TISInputSourceID` は `...Meltype.Roman` で別)
  - Google も `Input Mode` が標準名のキー (`com.apple.inputmethod.Japanese` など) で、`TISInputSourceID` (`com.google.inputmethod.Japanese.base`) ではない。
  - したがって**日本語モードの辞書のキーを標準名に改名すると、既存の登録 (`...Meltype.Japanese`) がどのモードにも当たらなくなり、更新した人の日本語モードが無効化される (入力メニューから消える) おそれがある**。`build.sh`/`install.sh` の予備登録 (`defaults write`) と `Registration.swift` の `modeID` も書き換えが要る。英数のときのような「新しいモードが足された」場合と違い、既存のモードの名前が変わる点が危険。
- 一方、モード側の `tsInputModeCharacterRepertoireKey` を外すだけなら、キー名も `TISInputSourceID` も変わらないので、登録は壊れない。
- **モード ID (`TISInputSourceID`) は変えない。** `Registration.swift`・`InputController.swift` の `selectMode`・アプリ別設定・利用者の保存設定がこの ID を使うため。

## 推奨 (選択肢)

- **(a) 何もしない**: 影響ゼロ。報告者には「Emacs 側で例外にする」(ns-inline-patch 側の設定) を案内する。Emacs だけの問題で、他のアプリへの波及が分からないうちは妥当。
- **(b) 小さい変更から試す (推奨)**: 日本語モードの `tsInputModeCharacterRepertoireKey` (Latn を含む) を**外す**だけの試験ビルドを作り、`--no-install` でビルドしたものを、リョウさんが入れ替えてよい日に実機で `tis` 相当の読み取りと Emacs で確かめる。ascii=0 になれば、辞書のキーは触らず終われる (登録も壊れない)。ならなければ (c) を検討する。モード側の `Latn` は「英語も打てる」宣言として入れた (`c3c247f`) が、実際の英字入力は Meltype 自身が処理しているので、外しても日本語・英数の動作は変わらないはず (未確認。外したときに英数モードが影響を受けないことも確認すること)。
- **(c) 辞書のキーを標準名 (`com.apple.inputmethod.Japanese`) にする**: 効く可能性は高いが、上記のとおり既存の登録が外れるおそれがある。やるなら、(i) 実機で「更新前に入れておいた登録」が残るか、(ii) 外れたとき自動で再登録できるか (`--register-enable` 相当)、(iii) `build.sh`/`install.sh`/`Registration.swift` の置き換え、をそろえてから。リョウさんの判断事項。

## 未確認
- 実際にどちらの違い (モード側 `Latn` / 辞書のキー) が ascii を決めているか (試験ビルドを入れていない)。
- azooKey の値 (この Mac に無い。報告者の値のみ)。
- ascii=0 にしたときの、パスワード欄での切り替え・Spotlight・Terminal・Cmd ショートカット・Caps Lock 切り替え・アプリ別のモード記憶 (`InputController`) への影響。
- Info.plist 変更が、ログアウトなしで反映されるか。更新した人の登録が残るか (特に (c))。
- 報告の Emacs 32.0.50 + inline パッチでの再現 (Emacs は入れていない)。
- README にある「#21」(入力ソース登録の件) は別件の可能性がある (この fork の issue #21 との対応は読んでいない)。
- 調べ方: リポジトリ外の作業フォルダーの Swift (一覧を読むだけ) と `plutil`・`defaults read`。どれも利用者の設定は書き換えていない。
