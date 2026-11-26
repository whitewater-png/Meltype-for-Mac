---
title: Zenzai の答えが読みと合うかを確かめて、合わない答えを捨てる (本家の IsPlausible の移植案。1.6.0 から)
status: done   # draft | approved | done。調べた結果は「見送りを推奨」。2026-11-26 リョウさんが「見送る」を選んだ
source: Meltype-Flash との比較 (2026-11-26、案 D) / 本家 `src/Meltype.Core/Engine/ZenzaiConverter.cs` の `IsPlausible`
scope: swift
---

# 048 Zenzai の答えが読みと合うかを確かめる (見送り)

## 背景 (誰が、何に困っているか)
- Meltype-Flash との比較で、本家 (1.6.0 から) にある「AI の答えが読みと合わなければ捨てて普通の変換に戻す」判定 (`ZenzaiConverter.IsPlausible`) を Mac 版にも入れる案が出た (案 D)。
- 本家で防いでいる失敗の例: `はーどうぇ` → `ハードウェアウェア` (本家 issue #1675、読みに無いものが続く)、`きょうはで` → `今日はでで` (本家 issue #1511、直前の文節が混ざる)。

## 今の動き (既に分かっていること)
- **本家**: Zenzai は llama-server でモデルに**自由に文字列を生成させる** (`ZenzaiConverter.Complete`)。答えが Mozc の文節ごとの候補をつないで作れないときだけ、`KeepsSymbols` と `IsPlausible` (かなと記号が読みの位置と同じか。漢字 1 文字・英数字の並びは読みの 1 文字以上に当てる) を通った答えを使い、通らなければ Mozc の結果に戻す (`src/Meltype.Core/Engine/HybridConverter.cs` の `Choose`)。本家は GPL-3.0-or-later なので、写すこと自体はライセンス上できる。この説明は本家 main (2026-11-26 に読んだ版、1.7.1 より後) のもの。`IsPlausible` は 1.6.0 から (当時は `src/Meltype.Core/Composition/ZenzaiConverter.cs` で、判定は `IsPlausible` だけ)。`KeepsSymbols` と `Engine/` の場所は main だけにある。
- **Mac 版 (この fork)**: Zenzai は azooKey の中で動く (`mac/Sources/MeltypeIME/Converter.swift` の `requestCandidates` が `zenzaiMode` を渡すだけ)。azooKey (`mac/Package.resolved` で固定した `d59a28e4c7ca049aef04f29a91eae9677a7753f2`) の `Sources/KanaKanjiConverterModule/ConversionAlgorithms/Zenzai/zenzai.swift` を読むと、候補は `kana2lattice_all` (制約なし) と `kana2lattice_all_with_prefix_constraint` (制約つき) で**ラティスから作られる**。モデル (`candidateEvaluate`) の答えは次の制約になるだけで、制約を満たす経路が無ければ使われない。そのときは、それまでにできた候補だけを返して制約を捨てる (同ファイルの `best was not found!` の分岐)。早く返るほかの経路 (推論の上限・評価の先送り・キャッシュ) も、返すのはラティスのノードだけ。`kana2lattice_all_with_prefix_constraint` の関数の本体は別のファイルで、読んでいない。
- したがって、Mac 版の Zenzai の答えはどれも、辞書・学習の語 (ラティスのノード) で読み (カーソルが途中ならカーソルまで) を埋めた形になる。本家が防いでいる「読みに無い文字が続く」「前の文節が混ざる」は、**この構造では起きない** (コードを読んだ範囲。実機で全数を確かめたわけではない)。
- 実測との突き合わせ: AJIMEE-Bench 200 件の Zenzai ON の外れ 35 件は、同音異義語 27 件・区切りなど 8 件 (`docs/AJIMEE_BENCH_2026-11-24.md`)。リポジトリにあるのは部類ごとの件数だけで、35 件の本文 (`candidates/misses.tsv`) はリポジトリの外にある。このときは 1 件ずつ見ていないので、繰り返し・混入が 0 件かは未確認。

## 判断
- 入れても効き目はほぼ 0 の見込みで、変更先は変換の経路 (`Converter.swift`。CLAUDE.md §5 の止める条件にかかる) になる。**見送りを推奨**し、2026-11-26 にリョウさんが「見送る」を選んだ。
- コード・正解表・テストは変えていない。

## 見直す条件
- azooKey の Zenzai が自由生成の経路を使うようになったとき (`requestRichCandidates` を ON にする、azooKey を上げて生成のしかたが変わる、など)。
- azooKey のラティスを通らないモデルの生成 (`Zenz.swift` の `predictNextInputText`・`pureGreedyDecoding`・`generateTypoCandidates`。タイポ補正や次の文字の予測) を使うようになったとき。今の fork は使っていない。
- Mac 版が llama-server など、azooKey の外でモデルを動かすようになったとき。
- 実機やベンチで、読みと合わない Zenzai の答え (繰り返し・混入) が 1 件でも見つかったとき。

## 記録 (ループが書く)
- 番号: 047 は別の作業 (Zenzai のモデルを v3.2 に替える案。別のセッションのブランチ claude/project-thread-r7ku6m で、2026-11-26 時点ではリポジトリの main に未反映) が使っている。
- 周回数 0 (調査のみ) / 上位のモデル / 約 25 分 / 手戻り: 査読 1 周目の直す 2 件 (azooKey の候補の作り方の書き方・本家の版の取り違え) と任意 4 件を直した / PR は本文書のみ
- 本家のコードは raw.githubusercontent.com から読んだ (クラウドから git clone と API は 403)。Mac の `~/Documents/GitHub/Meltype` は fork で、本家の clone ではない。
