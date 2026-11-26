---
title: Zenzai に右文脈 (カーソルの後ろの文字) を渡すと精度が上がるかを測る
status: approved   # draft | approved | done。approved にするのは人
source: 要件 047 の判断事項 4 (2026-11-26、リョウさんの指示「おすすめで」: 別の要件にして先に測る)
scope: swift (実機必須)
---

# 049 Zenzai に右文脈を渡すと精度が上がるかを測る

## 背景 (誰が、何に困っているか)
- 文の途中を書き直すとき (カーソルの後ろに文字があるとき)、今の Zenzai は前の文字しか見ない。後ろの文字で決まる同音異義語 (例: 「〜に乗って帰った」の前の `kisha` → 汽車 / 記者 / 帰社) を取り違えうる (推測。測っていない)。
- zenz-v3.2 は右文脈を使える。azooKey の `Docs/zenzai.md` (d59a28e) に「zenz-v3.2: zenz-v3の機能に加えて、``に続けて入力された右文脈を考慮できます」とある。
- 文の最後に打ち足す場面 (いちばん多い) では後ろに文字が無いので、変わらない。

## 今の動き (既に分かっていること)
- 入力欄の後ろの文字は**もう読んでいる**: `mac/Sources/MeltypeIME/InputController.swift` の `surroundingText(of:)` が、キャレットの前後 20 文字 (`before` / `after`) を読み、`NativeCore.shared.handleKey(..., before:, after:)` で Core に渡している (未確定の文字があるときは読まない)。Core は `MeltypeSession.HandleKey` で受け取る。
- 変換エンジンへは左文脈だけ: `mac/Sources/MeltypeIME/Converter.swift` の `zenzaiMode(weight:context:)` は `.v3(.init(profile: "", leftSideContext: context ?? ""))` で、`rightSideContext` を渡していない。`clauses(for:context:)` も左文脈 1 つだけ。
- azooKey の受け口: `ConvertRequestOptions.ZenzaiV3DependentMode.rightSideContext` (azooKey 6c2a224、2026-05-10、#340。d59a28e に含まれる)。
- 計測の道具: `mac/Sources/MeltypeIME/ConvertEval.swift` (`--convert-eval`) と `tools/ajimee-eval.py`。入力の表は 4 列 (id・読み・許容解・左文脈) で、右文脈の列は無い。AJIMEE の正解表にも右文脈は無い。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドから届かない)。

## minimum の要件 (これだけは満たす)
1. **先に測る (製品の振る舞いは変えない)**: 計測の道具に、任意の 5 列目「右文脈」を足す (`ConvertEval.swift` と `tools/ajimee-eval.py`)。列が無い・空の行は今と同じに動く。
2. 右文脈つきの正解表を作る: 既存の `eval/kanji/kanji-cases.tsv` の文を、文節の切れ目で「左・真ん中・右」に機械的に分け、真ん中を読み・許容解に、左右を文脈にする。新しく文を書かない (作り手の癖を新しく入れないため)。分け方のスクリプトと表をリポジトリに置く。`kanji-cases.tsv` は 6 列 (category・reading・expected・left_context・note・accepted) で、計測の道具の列の順 (id・読み・許容解・左文脈) と違うので、スクリプトで並べ替える。
   - 注意: `kanji-cases.tsv` の 212 文のうち 142 文は LLM が作った文 (`docs/ZENZAI_EXPERIMENT_2026-10.md` の信頼度の注記)。機械的に分けても元の文の癖は残るので、元の出どころ (LLM 作 / それ以外) を列で分けて、結果も分けて出す。第三者の表 `eval/kanji/bench-homophones.tsv` (Serugu/Meltype-Zenzai-Vulkan 由来、GPL-3.0) も同じ方法で分けて使えるなら使う。
   - 短い文が多く、右文脈が空でない行が少なくなりうる。McNemar の検出力が足りないかもしれないので、行数を先に数えて文書に書き、少なすぎるときは人に相談する。
3. Mac で、zenz-v3.2-small を使って「右文脈なし / あり」を同じビルドで 1 回ずつ流し、McNemar で比べる。
4. 結果を見て、製品に入れるかを人が決める。入れるなら別の要件にする (Core → FFI → `Converter.swift` に右文脈を通す変更)。

## 非目標 (今回はやらない)
- 製品の変換に右文脈を渡すこと (測った後に、別の要件で人が決める)
- 入力欄から読む文字数 (20 文字) を変えること
- モデルの切り替え (要件 047)

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 計測の道具: 5 列目が無い今の表 (AJIMEE・Meltype-Flash 40 件) で、変更前と同じ結果になる (Mac。同じ重み・同じビルドで acc@1 が一致)
- [ ] 正解表: 右文脈つきの表と、作るスクリプトがある。行数と、右文脈が空でない行数を文書に書く
- [ ] 計測: 右文脈なし / ありの acc@1 と McNemar の p 値、悪化した行・良くなった行の一覧が文書 (`docs/` の新しい日付の文書) にある
- [ ] 回帰なし: Core のテスト全件通過、`tools/eval-compare.sh` の broken 0 (Core は変えない想定)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] 計測の道具の変更だけで、IME を入れ直しても打った結果が変わらない (TextEdit で数文)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 計測のために `InputController.swift` (IMK の入力経路) を変えたくなったとき
- 製品の変換に右文脈を渡したくなったとき (測った後に人が決める)
- トレースログ (`MeltypeTraceIMK`) が要りそうになったとき (取る前に同意を得る)

## 右文脈つきの表の行数 (2026-11-26、`tools/make-right-context-items.py`)
- 元の文 332 (kanji-cases 212・bench-homophones 120) → 表に入れた 306 行。**右文脈が空でない行は 306 行すべて** (分け方で、右が空の行は作らない)。飛ばした 26 行 (読みとの対応が 1 通りに決まらない 15・漢字を含む文節の後ろに文節が無い 11)。
- 出どころ別: bench-homophones (第三者) 112 / kanji-cases の人の 70 文 62 / kanji-cases の LLM 作 132。
  - 注: `note` 列が「前回の70文」の行を人の文とした。その行は 68 行で、文書の「142 文が LLM」(残り 70) と 2 行ずれる (どの 2 行かは記録が無く未確認。LLM 側に数えている)。
- 左文脈もある行 54 (元の表の左文脈、または真ん中の前の文字)。
- 検出力の見込み (推測): 306 行なら、なし / ありで 10 行以上の差が片側に寄れば McNemar で p < 0.05 になりうる。人に相談するほど少なくはないと判断した。
- 真ん中は文の最初の漢字の文節 (例: 「しかくを」+ 右「取るために勉強する」→ 資格を)。短い読みが多く、実際の書き直し (長い語・句) とは性質が違う。語の途中で切れる行もある (例: K117 「かい → 買い」+ 右「物に行く…」。漢字の連なりで文節を切るため)。

## 記録 (ループが書く)
- 2026-11-26: 計測の道具に右文脈の列を足し、表を作った (行数は上)。製品の経路 (IME) は右文脈を渡さない (`MeltypeConverter.measurementRightContext` は `--convert-eval` だけが入れる)。
- 2026-11-26: リョウさんが承認 (「承認するので測定に進んでください」)。status を approved にした。
- 2026-11-26: 下書き。
