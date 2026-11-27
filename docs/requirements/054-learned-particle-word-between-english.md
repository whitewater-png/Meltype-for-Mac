---
title: 助詞と同じ 2 文字の語 (to・no・de) を英語と覚えていても、英字の語の間では日本語の助詞にする
status: draft
source: 要件 050 の実機確認 (2026-11-27、Mac の学習データで to が英語と覚えられていた。リョウさん「要件にたてて」)
scope: core
---

# 054 英語と覚えた助詞の語 (to など) を、英字の語の間では助詞にする

## 背景 (誰が、何に困っているか)
- 要件 050 を入れた Mac (main 91fef24) で、`JavaScripttoPythonnoAPIwokakunin` が全部英字に、`MeltypeTip.dlltoCargo.tomlwoREADME.mdnikirokusuru` が `MeltypeTip.dlltoCargo.tomlをREADME.mdに記録する` になった (2026-11-26、自動打鍵)。
- 原因 (2026-11-27 に Mac で調べた): 学習データ (languages.json) に `to` が「はっきり英語」(Explicit。F10・F6 などで英語に直した印) で 1 回あった。「判定を調べる」と同じ処理 (`DictionaryManagement.ExplainDetection`) で、学習あり → `EN:JavaScript | EN:to | EN:PythonnoAPI…`、学習から to を外す → `JavaScript | と | Python | の | API…`。
- リョウさんの指示で to を学習から外した (2026-11-27)。ただ、英字のあとの to をまた英語に直すと、同じように覚え直す。

## 今の動き (既に分かっていること)
- `LanguageMemory.NeedsTwice` (src/Meltype.Core/Composition/LanguageMemory.cs 71〜73 行): 2 文字の読めるローマ字の語 (go・to・no) は 2 回で覚えるが、はっきり直したとき (Explicit) は 1 回で覚える (ふつうの日本語の語 (IsCommonJapanese) は Explicit でも 2 回)。
- 要件 050 の規則 (`CompositionDetector.CapitalizedWordEnd` → `EarliestParticleBeforeCapital`) は学習より先に効いて JavaScript を区切る。ただ、続く `to` の区間を学習 (`CompositionDetector.cs` 898 行付近。前が英語なので短い語の除外が効かない) が英語にし、その後ろの Python は英字の語の途中の扱い (1100 行付近の midWord。`parse|JSONtoObject` の守り) になって `Python|no` で区切られない (Core で再現、2026-11-27。学習に to を Explicit で入れると `JavaScripttoPythonnoAPIをかくにん`・`MeltypeTip.dlltoCargo.tomlをREADME.mdにきろくする`、学習なしなら期待どおり。学習ありでも `I want to go`・`go to school`・`parseJSONtoObject` は英字のまま)。
- macOS のスペルチェッカーは原因ではない (JavaScripttoPython・dlltoCargo を英単語と見ない。2026-11-27 に Mac で NSSpellChecker を呼んで確認)。
- 本家 (yksr-melt/Meltype) の扱い: 未確認。

## minimum の要件 (これだけは満たす)
1. 学習で英語と覚えた語でも、それが助詞と同じ綴り (to・no・de・wo・ni・ga・ha・mo) で、要件 050 の形 (略語か知っている語 + 助詞 + 大文字で始まる語、拡張子 + 助詞 + 大文字で始まる名前) にあるときは、助詞として区切る。
2. 単独の to・英文の中 (`I want to go`・`go to school`・`to do`) では、今までどおり学習どおり英字。
3. 学習データのファイル形式は変えない (CLAUDE.md §5 の止める条件に当たるため)。

## 決めること (人の判断)
- 学習の印 (はっきり英語に直した) を、この形でだけ上書きしてよいか。上書きすると、本当に `JavaScripttoPython` を英字で打ちたい人 (識別子) は F10 で直すことになる。

## 非目標 (今回はやらない)
- 2 文字の語を学習する仕組み (NeedsTwice) そのものを変えること。
- 助詞以外の短い語 (go・me) の扱い。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] テスト: `src/Meltype.Core.Tests/` に `Req054_...` を足す。学習データに to を Explicit で入れた状態で、`JavaScripttoPythonnoAPIwokakunin` → `JavaScriptとPythonのAPIをかくにん`、`MeltypeTip.dlltoCargo.toml…` → `MeltypeTip.dllとCargo.toml…`。実装前は外れ (赤)
- [ ] 守る: 同じ学習の状態で `I want to go`・`go to school`・`parseJSONtoObject` は英字のまま
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] 英字のあとの to を F10 で英語に直したあとでも、TextEdit で `JavaScripttoPythonnoAPIwokakunin` + Enter → `JavaScriptとPythonのAPIを確認` (漢字はライブ変換の設定で変わる。見るのは区切り)
- [ ] 同じ状態で `I want to go` が英字のまま

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 学習データのファイル形式を変えないと直せない。

## 記録 (ループが書く)
- 2026-11-27 要件を立てた (draft)。実装はまだ。
