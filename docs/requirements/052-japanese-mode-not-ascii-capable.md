---
title: 日本語モードを「ASCII 対応」でない入力ソースにする (試験。モードごとの文字の指定を外す)
status: approved   # draft | approved | done。approved にするのは人
source: fork Issue #21 (利用者の提案)。調査メモ docs/ISSUE21_ASCII_CAPABLE_2026-11.md
scope: swift (実機必須)
---

# 052 日本語モードを「ASCII 対応」でない入力ソースにする (試験)

2026-11-26 リョウさんが、調査メモの案 (b)「モードごとの `tsInputModeCharacterRepertoireKey` を外す試験 PR」を選んだ (このスレッドの判断カード)。

## 背景 (誰が、何に困っているか)
- 報告 (要約): Meltype の日本語モードは `kTISPropertyInputSourceIsASCIICapable` が true。ことえり・azooKey の日本語モードは false。Emacs の inline パッチ (ns-inline-patch) は、今の入力ソースが ASCII 対応でないときだけ、`C-x` のあとのキーや読み取り専用のバッファ (dired・*Messages*) のキーを日本語入力に通さず Emacs へ渡す。そのため Meltype では `C-x b` の `b` が変換中の文字になり、dired の `n` などが変換待ちになる (`emacs -Q` で azooKey は起きず Meltype は起きる、と報告者が確認)。
- 実機での再現は、この作業では行っていない。

## 今の動き (既に分かっていること)
- 調査メモ `docs/ISSUE21_ASCII_CAPABLE_2026-11.md` (リョウさんの Mac で読み取りだけ): 日本語系で ascii=1 なのは Meltype の日本語モードだけ (ことえり・Google・ATOK は 0)。
- Info.plist で、ほかの日本語入力との違いは 2 つ。(1) 日本語モードにだけモードごとの `tsInputModeCharacterRepertoireKey` (`Hira Kana Hani Latn`) がある。(2) 日本語モードの辞書のキーが標準名でない。入力メソッド全体の `tsInputMethodCharacterRepertoireKey` (`Latn` を含む) はことえり・Google・ATOK にもあるので原因ではない。
- (1) と (2) のどちらが ascii を決めているかは、今の証拠では決まらない。(2) を変えると、更新した人の入力ソースの登録 (`AppleEnabledThirdPartyInputSources` の `Input Mode`) が古いキーを指したままになり、日本語モードが入力メニューから消えるおそれがある。(1) は辞書のキーも `TISInputSourceID` も変えないので、登録は壊れない。
- Mac のコードは `kTISPropertyInputSourceIsASCIICapable` を読んでいない。モードごとの repertoire を読むコードも無い (`git grep` で Info.plist 以外 0 件)。
- 本家 (yksr-melt/Meltype) の扱い: 本家に無い (Mac 版の仕組み)。

## minimum の要件 (これだけは満たす)
1. `mac/Resources/Info.plist` の日本語モードから、モードごとの `tsInputModeCharacterRepertoireKey` を外す。
2. 日本語モードの辞書のキー・`TISInputSourceID`、英数モード (キー・ID・`[Latn]`)、入力メソッド全体の repertoire、モードの並びは変えない。
3. 上の形を Linux でも確かめられる検査 (`tools/test_info_plist_modes.py`) を足し、CI で回す。

## 非目標 (今回はやらない)
- 辞書のキーを標準名 (`com.apple.inputmethod.Japanese`) に変える案 (c)。効かなかったときに、別の要件で人が決める。
- Swift のコード・Core の変更。新しい設定・通信・ログ。
- Issue への返信・閉じること (リョウさんが決める)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] `python3 tools/test_info_plist_modes.py`: 変更前の Info.plist (origin/main a65ce2b) をパスで渡すと (Registration.swift との照合を飛ばすので 9 件) 「日本語モードに、モードごとの tsInputModeCharacterRepertoireKey が無い」の 1 件が FAIL (終了コード 1)、変更後は 12 件すべて PASS (終了コード 0)。
- [x] `xmllint --noout mac/Resources/Info.plist` が通る (Linux で確認)。
- [x] Core は変えていないので、Core のテスト・回帰ゲートは変わらない (C# の変更 0 件)。
- [x] `mac/build.sh` が通り、~/Library/Input Methods に入る (2026-11-27、りょうさんの Mac で 5510b54。入った中身がビルドと一致することを `cmp` で確認)。`tools/check-mac-aot-settings.py` は未確認
- [ ] 下の Mac 実機チェックリスト (人が確かめる。**この試験の中心**)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
入れ替えたあと、Info.plist の変更を macOS に読み直させるため、いったんログアウトしてログインし直す (実行中に反映されるかは未確認)。
- [x] 日本語モードの `kTISPropertyInputSourceIsASCIICapable` が false になる (2026-11-27: 入れる前 yes → 入れた直後・1 分後とも no。英数モードは yes のまま。ログアウトなしで変わった)
- [x] 入力メニューに「日本語 (Meltype for Mac)」「英数 (Meltype for Mac)」が今までどおり出て、選べる (TIS の enabled・selectable が両方 yes のまま。りょうさんが手で選んで打てた)
- [x] TextEdit: 日本語モードで `kyouha` → きょうは (りょうさん 2026-11-27)。`konnichiwa hello` は hello が へっぉ になったが、1.2.9 (Homebrew) に戻しても同じく へっぉ だったので、この変更とは関係ない前からの動き (別に扱う)
- [x] TextEdit: 英数モードで `kyouha` → `kyouha` のまま。英数キー / かなキーの切り替えが今までどおり (りょうさん 2026-11-27。Caps Lock は未確認)
- [x] Cmd+A・Cmd+C・Cmd+V・Cmd+Z が、日本語モードで今までどおり効く (りょうさん 2026-11-27。配列の違いは未確認)
- [ ] パスワード欄 (Safari のログイン欄など) で、打てる・入力ソースの表示が戸惑う動きにならない (ascii=false にすると、macOS が英字の入力ソースに切り替えることがある。推測)
- [ ] コード系のアプリ (ターミナル・VS Code) で、英数から始まる・前に選んだモードを覚える動きが今までどおり
- [ ] (報告の形) Emacs + inline パッチで、日本語モードのまま `C-x b` が変換されずにコマンドになる・dired の `n` が次の行へ動く (Emacs がある場合)
- [ ] Firefox: 日本語モードで普通に打てる (ascii=false で Firefox は「IME が開いている」と判定するようになる。調査メモ)

## 得と損
- 得: ほかの日本語入力と同じ形になり、Emacs (inline パッチ) などが日本語入力として扱える見込み。辞書のキーも ID も変えないので、更新した人の登録は壊れない。戻すのも Info.plist の数行。
- 損: 効くかどうかは実機で入れるまで分からない (ascii を決めているのが辞書のキーのほうなら、この変更は効かない。害は無い見込み)。効いた場合は、macOS とアプリから「日本語専用の入力」とみなされ、パスワード欄での入力ソースの切り替え・Cmd ショートカットに使う配列 (直前の ASCII 対応の配列) が変わりうる (推測。チェックリストで見る)。

## 戻し方
- 効かなかった、または困る動きがあったときは、この PR を取り込まない (取り込んだあとなら revert する)。Info.plist を戻して入れ直し、ログアウト・ログインで元に戻る見込み。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 実機で日本語モードが入力メニューから消える、または選べなくなったら、すぐ戻す。
- ascii が false にならなかったら、案 (c) に進む前に人が決める。

## 記録 (ループが書く)
- 実機 (2026-11-27): 効いていたのはモードごとの repertoire で、案 (c) は要らなかった。パスワード欄・コード系アプリ・Firefox・Emacs (この Mac に無い) は未確認。
- 番号: 050 は PR #40、051 は別の作業が使っているので 052 にした。
- 1 周目 / 上位のモデル / Info.plist の 1 か所と検査スクリプト 1 本 / PR の URL は PR を作ったあとに AGENT_STATE に書く
