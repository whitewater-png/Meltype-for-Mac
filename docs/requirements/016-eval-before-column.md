---
title: 正解表に「前の文字」の列を足す (本家の Before つきのケースを測る)
status: approved   # 2026-11-22 リョウさんの指示「おすすめで進めてください」(docs/ACCURACY_PLAN_2026-11.md §8 の 1 週目、0-2 の下準備)
source: docs/ACCURACY_PLAN_2026-11.md 0-2
scope: core
---

# 016 正解表に「前の文字」の列を足す

## 背景 (誰が、何に困っているか)
- 本家の品質テストには、直前の文字 (`Before`) を入れてから打つケースがある (例: 前が `I like ` なら `sushi` は英字のまま、前が `今日は` なら すし)。
- `eval/cases/upstream-quality.tsv` は本家 `a4b6f1a` の写しで、`Before` つきの 13 件は、正解表の形式に列が無いため `#` で写すだけにして判定していない ([eval/README.md](../../eval/README.md))。
- 精度の計画の 0-2 で、本家の新しい正解ケース (0-1 で `eval/candidates/` に出る予定) を取り込む。そこにも `Before` つきがあるはず (推測) なので、先に正解表で読めるようにしておく。

## 今の動き (既に分かっていること)
- 関係する場所:
  - `src/Meltype.Core.Tests/Repro.cs` の `Checks.ParseCases` (4 列でない行は例外)・`Checks.Evaluate` (`new CompositionTests.Keyboard()` で打って `k.Host.Document` を取る)・`EvalCase`・`EvalResult`
  - `src/Meltype.Core.Tests/QualityTests.cs` は `Before` を `k.Host.PrecedingText` に入れてから打つ (374 行付近)
  - `tools/eval-compare.sh` (同じ正解表のフォルダーを両方の木で回す)、`tools/eval_compare.py` (base と new の行の突き合わせ)、`tools/eval_legacy_base.py` (`--expect-all` が無い木、主に本家の木)
  - `src/Meltype.Core.Tests/EvalFormatTests.cs` (正解表の形式のテスト)
- 今の Mac 版の品質テスト (`--eval`) では、`Before` つきの 13 件はすべて当たっている (外れは `my name is taro` だけ)。
- 本家の扱い: 本家の `--expect` が前の文字を読めるかは未確認 (本家に `--expect-all` は無い。COMPARISON_UPSTREAM.md の数字の注意)。本家の木は `tools/eval_legacy_base.py` で回す。

## minimum の要件 (これだけは満たす)
1. 正解表の行に、任意の 5 列目「前の文字」を書ける (打鍵・期待・比べ方・出典・前の文字)。5 列目がある行は、前の文字をキャレットの前の文字として入れてから打つ (品質テストの `Before` と同じ)。4 列の行は今までどおり。
2. 結果の JSON に前の文字を入れ、`tools/eval_compare.py` の突き合わせで、同じ打鍵で前の文字だけが違う行を別の行として扱う。
3. 5 列目を読めない木 (この変更より前の Mac 版、本家の木) と比べても `tools/eval-compare.sh` が実行の失敗 (終了コード 2) にならない。読めない木では 5 列目のある行を回さず、比較では「new だけにある行 (added)」として扱う。
4. `upstream-quality.tsv` の `#` で写した 13 件を、5 列の行にして判定に入れる。
5. `eval/README.md` の形式の説明を直す。

## 非目標 (今回はやらない)
- 本家の新しい正解ケースの取り込み (0-1 の後に別に行う)。
- 品質テスト (`QualityTests.cs`) の変更、判定の変更。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `EvalFormatTests.cs` に、5 列の行の読み込み (前の文字が入る・4 列の行は変わらない・6 列以上や 3 列以下は今までどおり例外) と、前の文字を入れて打った結果 (前の文字 `I like ` + `sushi` → `sushi`、前の文字 `今日は` + `sushi` → `すし`) のテストを足す。実装前は赤を確かめる。
- [x] 正解表: `upstream-quality.tsv` の 13 件が 5 列の行として判定され、`--expect-all` の結果で 13 件とも ok。
- [x] 互換: `tools/eval-compare.sh <この変更より前の木> . eval/cases <出力>` が終了コード 0 で終わる (13 件は added、broken 0)。
- [x] 互換 2: `tools/eval_legacy_base.py` も 5 列目のある行を飛ばして動く (Python の単体テストで確かめる。本家の木はクラウドに無い)。
- [x] 回帰なし: Core のテストが全件通る。
- [x] 文書: `eval/README.md` の形式・比べ方の節と、`upstream-quality.tsv` の行の説明。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- なし (開発用の評価の形式で、IME の動きを変えない)。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 前の文字を入れて打つために、`src/Meltype.Core/` (判定・入力の本体) を変える必要が出たとき。

## 記録 (ループが書く)
- 周回数: implementer 2 周 (1 周目で完成条件をすべて達成、2 周目は査読の任意指摘)。3 周目はオーケストレーターがテスト 4 行を直した
- モデル: researcher 1 回 (sonnet)・implementer 2 回 (sonnet)・reviewer 3 回 (sonnet。定義は opus だが利用者の指定。オーケストレーターの opus が要所を読み直した)・eval-guard 2 回 (sonnet)・doc-keeper 1 回
- 所要時間: 約 24 分 (2026-11-22T05:23:39Z〜05:47Z ごろ)。人の介入なし
- 手戻りの原因: 止める・直すの指摘による手戻りはなし。任意の指摘 2 件 (eval_compare.py の鍵の単体テストと README の 1 文 / 同じ鍵の何回目かの番号を固定できていなかったテストの強化。後者は docs/LESSONS.md)
- 数字: Core 1263/1263 (base は 1259)、正解表 635/741、eval-compare broken 0 / fixed 0 / added 13 / removed 0
- 測れていないこと: 本家の木 (`tools/eval_legacy_base.py` の実際の経路) では回していない (単体テストのみ)。前の文字そのものが `"…"` の形は今の形式では書けない (`eval/README.md` に記載)
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/29 (要件 015 と同じ PR。同じブランチに積んだ)
