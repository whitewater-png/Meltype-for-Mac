---
title: ん の後ろに打った子音 (b・m・h・j) が Enter の確定で消えるのを、表示どおりに確定する (anb → あんb)
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-27.md 候補 1)
source: docs/voice/2026-11-27.md 候補 1 (本家 yksr-melt/Meltype #2461)
scope: core
---

# 056 ん の後ろの子音が Enter で消える

## 背景 (誰が、何に困っているか)
- 本家 #2461 (本家 main・v1.8.0、Core の不具合): `anb` と打つと変換ボックスは `あんb` なのに、Enter で `あん` が入る。最後に打った子音が黙って消える。本家の作者は「表示どおりに確定する」方針 (docs/voice/2026-11-27.md。本家のコードは読んでいない)。
- 今の挙動 (この fork の origin/main `cd791a2`、Core の `--repro`、最後のキーは Enter、2026-11-28 に打った):

| 打鍵 | 表示 | 確定 |
|---|---|---|
| `anb` | `あんb` | `あん` |
| `kanb` | `かんb` | `かん` |
| `anm` | `あんm` | `あん` |
| `anh` | `あんh` | `あん` |
| `anj` | `あんj` | `あん` |
| `sanm` | `さんm` | `さん` |
| `honb` | `ほんb` | `ほん` |
| `honnb` | `ほんb` | `ほん` |
| `gohanm` | `ごはんm` | `ごはん` |
| `nihonj` | `にほんj` | `にほん` |
| `kyouhaanb` | `きょうはあんb` | `きょうはあん` |
| `anp` (対照) | `あんp` | `あんp` |
| `kanz` (対照) | `かんz` | `かんz` |
| `a-nb` (対照) | `あーんb` | `あーんb` |
| `kanbu` (対照) | `かんぶ` | `かんぶ` |
| `yoroshikuy` (対照) | `よろしくy` | `よろしく` |
| `desuh` (対照) | `ですh` | `です` |
| `anbdesu` (対照、語の途中) | `あんbです` | `あんです` |
| `honbdesu` (対照、語の途中) | `ほんbです` | `ほんbです` |

## 今の動き (既に分かっていること)
- 原因 (コードを読み、上の表で確かめた): `src/Meltype.Core/Composition/RomajiTypoCorrector.cs` の `Fix` の「語末に子音が 1 つ残っただけ (yoroshikuy)」の枝。最後の子音が、その前の文字の隣のキーか同じキーなら「一緒に押した余計な 1 文字」として消す。`n` の隣のキーは `b h j m` (逆に `b`・`h`・`j`・`m` の隣にも `n` がある) なので、`ん` (語末の `n`) の後ろにこの 4 つを打つと、いつも余計な 1 文字と見なされる。`p`・`z` は `n` の隣でないので消えない。
- `b h j m` は ば・は・じゃ・ま 行の頭でもあり、`n` の後ろに打つのは「次の音を打ちかけた」ことが多い。表示も `あんb` のままなので、消すと利用者には何が起きたか見えない。
- **前の決定の再検討**: 要件化の前の改善 (docs/IMPROVEMENT_PLAN.md の「1 音 + 余計な子音」の項、`FeedbackTests.RomajiTypos_LoneKanaThenStrayConsonant_DoesNotTouchEnglishOrOtherFixes`) で、`kanb` → `かん`・`sanh` → `さん` を「2 音以上の後ろの余計な 1 文字は今までどおり直す」例としてテストに固定していた。固定の目的は「入力途中の n を 1 音と数える」規則を変える改造で赤になること (変異テストの記録)。この要件はその 2 例の期待を `かんb`・`さんh` に変え、同じ改造で赤になる別の例 (`kanq` → `かな`。候補探索の枝で、今も同じ答え) に置き換える。`yoroshikuy`・`desuh` など、`n` 以外の後ろの余計な 1 文字の直しは変えない。
- 本家 (yksr-melt/Meltype) の扱い: 本家は「表示どおりに確定する」方針で修正案 (PR #2492) があるらしい (docs/voice/2026-11-27.md。本家のコードは読んでいない・写さない)。この要件は fork のコードの 1 行の条件で直す。

## minimum の要件 (これだけは満たす)
1. 語末が `n` (ん) + `b`・`h`・`j`・`m` の 1 文字で終わる入力を Enter で確定すると、表示どおり (`あんb`) に確定する。
2. `n` 以外の後ろの余計な 1 文字の直し (`yoroshikuy` → `よろしく`、`desuh` → `です`、`desuy` → `です`) と、ほかの打ち間違いの直し (`onegaishimsu`・`arigatpu`・`hazkimeru` など) は今までどおり。

## 非目標 (今回はやらない)
- `yoroshikuy` → `よろしく` のような、表示と確定が食い違う直し全般をやめるか (本家はやめる方針)。得 (表示どおり) と損 (よく使う打ち間違いの直しが無くなる) があるので人が決める。
- 語の途中 (`anbdesu` → `あんです`) と、候補探索の枝の置き換え (`kanq` → `かな`)。同じく表示と確定が食い違うが、別の枝 (候補探索) なので別の項目にする。
- Space・記号の直前の確定 (同じ `FixTypos` を通るので一緒に直る見込み。テストでは Space の 1 形だけ見る)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/trailing-consonant-after-n-056.tsv` を足し、直す行 (`anb`・`kanb`・`anm`・`anh`・`anj`・`sanm`・`honb`・`honnb`・`gohanm`・`nihonj`・`kyouhaanb`) は実装前は外れ、実装後は当たる。守る行 (`anp`・`kanz`・`a-nb`・`kanbu`・`honnda`・`yoroshikuy`・`desuh`・`desuy`・`onegaishimsu`・`arigatpu`) は前後とも当たる。
- [ ] テスト: `src/Meltype.Core.Tests/TrailingConsonantAfterN056Tests.cs` に `Req056_...` を足す (直す形の表示と確定が同じ、Space の後ろでも消えない、守る形)。`FeedbackTests` の `kanb`・`sanh` の期待を直し、`kanq` → `かな` で「入力途中の n を 1 音と数える」規則を守る。
- [ ] 回帰なし: `tools/eval-compare.sh` (base origin/main) の broken 0、Core のテスト全件通過。
- [ ] 境界例: 査読員が `n` + 子音で終わる入力を自分で作り、base と比べて変わるのが「ん + b/h/j/m で終わる」形だけであることを確かめる。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `anb` + Enter → `あんb` (変換ボックスの表示と同じ)
- [ ] TextEdit: `kyouhaanb` + Enter → `きょうはあんb`
- [ ] TextEdit: `yoroshikuy` + Enter → `よろしく` (今までどおり)
- [ ] TextEdit: `honbdesu` + Enter → `本bです` などの英字の b が残る形 (今までどおり。漢字は azooKey 次第)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- `n` の後ろだけに絞った条件で、`n` 以外の直し (`yoroshikuy` など) や辞書の語の判定が変わる。

## 記録 (ループが書く)
- 2026-11-28 定期タスク (改善担当) で要件を立てた。
