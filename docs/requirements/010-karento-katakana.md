---
title: karento が karenと になる (karen + to の分け方)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-11-06.md 候補 2 (voice/2026-11-06 ブランチ)
source: docs/voice/2026-11-06.md 候補 2 / 本家 #1596
scope: core
---

# 010 karento が karenと になる

## 背景 (誰が、何に困っているか)
- 本家 #1596 (Windows、1.4.0): `karento` と打つと `karenと` になる (期待 `カレント`)。本家 main では直っている (直し方は未確認。クラウドから本家に届かない)。
- 今の挙動 (Core の `--repro`、2026-11-07 クラウド): `karento` → `karenと`、`karentodesu` → `karenとです`、`kyouhakarentode` → `きょうはkarenとで`。`karen` 単独は `karen`、`karenda` は `かれんだ`、`karenga` は `karenが`。
- 番号は 009 (PR #14) の次の 010。

## 今の動き
- `karen` は英語の固有名詞 (`dictionaries/propernouns.txt` の `Karen`。この守りが見るのはこちら)。`CompositionDetector.IsEnglishSpanCore` の固有名詞の守り (「続きと合わせて日本語の語になるなら固有名詞にしない」。`karenda-` → カレンダー、本家 #920・#965) は、続きが助詞で始まる (`to`) と効かない。そのため `karen` + `to` に分かれる。
- 他の「ローマ字で読める英単語」(`nana`・`sushi`・`kimono` など) + `to` は、すでに全部かなになる。`karen` だけが固有名詞として残る。

## minimum の要件
1. `karento`・`karentodesu`・`kyouhakarentode` が、全部かな (`かれんと`…) になる。
2. `karen` 単独 (英字のまま)・`karenda`・`karendaha`・`karenga` (`karenが`)・`karende` の今の値は変えない。

## 受け入れる変化 (査読で判明。2026-11-11 リョウさんが受け入れた)
- 得: `karento` → かれんと (カレント)。
- 損: 小文字で打った人名の Karen + と も かな になる (`kinouhakarentoaimashita` → きのうはかれんとあいました、`karentohanashita` → かれんとはなした)。大文字の `Karento` は `Karenと` のまま。綴りでは カレント と Karen と を区別できない。正解表 2 行で固定した。
- 広がる崩れ: `karent` + 子音・記号 (`karentdesu` → かrentです、`karent.` → かrent。)。base でも `karend.` → かrend。 と同じ崩れ方がある (既存の動きの広がり)。直すなら別の要件。

## 非目標
- 固有名詞 + 助詞 全般の見直し (`appleto` など)。今の値のまま。
- 漢字・カタカナへの変換 (`カレント` は変換エンジンの仕事で、Core では測れない)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に `karento`・`karentodesu`・`kyouhakarentode` と守る行 (`karen`・`karenda`・`karenga`・`appleto`) を足す。実装前は 3 行外れ、実装後は当たる
- [x] テスト: `FeedbackTests.cs` に `Req010_Karento_BecomesKana_AndKarenAndOthersDoNotChange`。実装前は赤
- [x] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [x] 文書: `docs/KNOWN_LIMITS.md` に karen の記載なし (grep で確認)

## Mac 実機チェックリスト (人が確かめる。[x] は 2026-11-11 にリョウさんが確認、ほかは未確認)
- [x] TextEdit: `karento` + Enter → `かれんと` (Space で `カレント` が候補に出るかは変換エンジン次第。こちらは記録だけの項目で、結果の詳細は無い)
- [ ] TextEdit: `karen` + Enter → `karen`、`karenda-` → `かれんだー` (`karenda-` → かれんだー と `Karento` → Karenと は確認済み。`karen` 単独は未確認)
- [ ] ターミナル (zsh): `karen` → `karen`

## 止める条件
- 日本語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-07)。
- 2026-11-11 リョウさんの Mac 実機 (TextEdit、Enter で確定。試し版 int/q41-q43 に main を合わせた手元の版 verify/q41-q43-plus-1111 を入れて確認。19 行の確認表と記録だけの 4 行への「全て期待結果通り」の一括報告で、項目ごとの記録は無い): `karento` → かれんと、`Karento` → Karenと、`karenda-` → かれんだー。
