---
title: C/C++ のプリプロセッサの指令名 (endif・elifndef・pragma) がかなに崩れる
status: approved   # 定期タスクによる自動承認 (リョウさんの設定)。元: docs/voice/2026-11-08.md 候補 3 (voice/2026-11-08 ブランチ)
source: docs/voice/2026-11-08.md 候補 3 / 本家 #1625
scope: dictionary
---

# 012 プリプロセッサの指令名が英字のまま残る

## 背景 (誰が、何に困っているか)
- 本家 #1625 (辞書の提案): `#ifndef`・`#undef`・`#pragma` など、コードを書くとき指令名を英字のまま残したい。
- 今の挙動 (Core の `--repro`、2026-11-09 クラウド): `endif` → `えんぢf`、`ifndef` → `ifんdef`、`elifndef` → `elifんdef`、`undef` → `うんdef`、`#pragma` → `#pragま` (`pragma` 単独は英字のまま)、`#endif` → `#えんぢf`。`include`・`elif` は英字のまま。
- `define` は `dictionaries/english.txt` に既にあるのに `でふぃね` (`#define` → `#でふぃね`)。全部ローマ字として読める語なので、辞書では直らない (判定ロジックの変更が要る)。

## minimum の要件
1. `endif`・`elifndef`・`pragma` を Enter で確定すると英字のまま (`#` 付きも)。`undef`・`ifndef` は入れない (下の「入れない語」)。
2. `ima`・`unde`・`enda`・`ifu`・`dekiru`・`denwa` の今の値は変えない。

## 入れない語 (独立査読 2026-11-11、リョウさんの判断で作り直し)
- `undef`: 辞書に入れると、〜んで (自分で・休んで・新聞で…) + 余計な `f` / 次の ふ… の打ち間違いの補正が効かなくなる。179 語・18,136 形 (`jibundeftotemo` じぶんでとても → じぶんでfとても)。PR #12 (`pre`) と同じ仕組み。
- `ifndef`: シフォンで + ふ… の o 抜け (`shifndefure` しふぉんでふれ → しfんでふれ) が直らなくなる。1 語・24 形。
- 2 語は今までどおり `うんdef`・`ifんdef` のまま。直すなら辞書ではなく判定の側 (直前が日本語のときは英語にしない等) で、別の要件にする。
- 3 語 (`endif`・`elifndef`・`pragma`) だけの形は、約 49 万入力 × base との比較で日本語の損失 0。英語として打った形の損失は `pragma` + 英語として読める語尾の 4 形 (`pragmamade` pragmaまで → pragmamade、`pragmanode`) と大文字始まり 2 形で、記録だけ (`kyouhapragmawo` きょうはpragまを → きょうはpragmaを などの改善が上回る)。

## 非目標
- `define` の修正 (判定ロジックの変更が要る。別の要件にする)。
- 判定ロジックの変更、ほかの語の追加。

## 完成条件
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に 2 語 (`endif`・`elifndef`) と守る行、入れない理由の打ち間違い 4 行を足す。実装前は外れ (eval-compare で fixed 2)。`#` 付きは表で書けない (`#` で始まる行は注釈) ので C# のテストだけで守る
- [x] テスト: `FeedbackTests.cs` に `Req012_PreprocessorDirectives_StayEnglish_AndKanaWordsDoNot`。実装前は赤 (`endif` → `えんぢf`) を確認
- [x] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [x] 査読: 別エージェント 2 周 (指摘なし) → 独立査読 (2026-11-11) の指摘で `undef`・`ifndef` を外して作り直し、作り直した版を別の査読者が確認 (止める指摘なし)

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `#endif` + Enter → `#endif`、`#pragma` + Enter → `#pragma`
- [ ] ターミナル (zsh): `#endif` + Enter → `#endif`
- [ ] TextEdit: `undef` + Enter → `うんdef` (入れない語。今までどおり)
- [ ] TextEdit: `ima` → いま (変わらない)

## 止める条件
- 語を足して日本語の語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-09)。番号は 009〜011 が PR #14〜#16 で open のため 012 にした。
- 査読 2 周。1 周目の指摘: 正解表の `#` 始まりの 4 行が注釈として読み飛ばされ測れていなかった → 表から外してテストで守る形に直した。境界例 82 件を base と比較し、日本語として正しく出ていた入力で崩れたものは 0 件 (助詞が続く形 `undefde` → `undefで` などは良くなった。undef を外した今は成り立たず、`undefde` は うんでfで のまま)。
- 測れていないこと: `define` (`でふぃね`、判定ロジックの変更が要るので別の要件)、実機の入力欄・実機の NSSpellChecker。
- 独立査読 (2026-11-11、約 49 万入力 × 7 本の木): `undef` で 179 語・18,136 形、`ifndef` で 1 語・24 形の打ち間違いの補正が効かなくなると分かり、リョウさんの判断で 2 語を外して 3 語に作り直した。外した形は日本語の損失 0。境界例 82 件で 0 件だったのは、語の連結 (`jibunde` + `f` + `totemo`) を見ていなかったため。
