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
- `karen` は英語の固有名詞 (`dictionaries/english.txt`)。`CompositionDetector.IsEnglishSpanCore` の固有名詞の守り (「続きと合わせて日本語の語になるなら固有名詞にしない」。`karenda-` → カレンダー、本家 #920・#965) は、続きが助詞で始まる (`to`) と効かない。そのため `karen` + `to` に分かれる。
- 他の「ローマ字で読める英単語」(`nana`・`sushi`・`kimono` など) + `to` は、すでに全部かなになる。`karen` だけが固有名詞として残る。

## minimum の要件
1. `karento`・`karentodesu`・`kyouhakarentode` が、全部かな (`かれんと`…) になる。
2. `karen` 単独 (英字のまま)・`karenda`・`karendaha`・`karenga` (`karenが`)・`karende` の今の値は変えない。

## 非目標
- 固有名詞 + 助詞 全般の見直し (`appleto` など)。今の値のまま。
- 漢字・カタカナへの変換 (`カレント` は変換エンジンの仕事で、Core では測れない)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に `karento`・`karentodesu`・`kyouhakarentode` と守る行 (`karen`・`karenda`・`karenga`・`appleto`) を足す。実装前は 3 行外れ、実装後は当たる
- [x] テスト: `FeedbackTests.cs` に `Req010_Karento_BecomesKana_AndKarenAndOthersDoNotChange`。実装前は赤
- [x] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [x] 文書: `docs/KNOWN_LIMITS.md` に karen の記載なし (grep で確認)

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `karento` + Enter → `かれんと` (Space で `カレント` が候補に出るかは変換エンジン次第)
- [ ] TextEdit: `karen` + Enter → `karen`、`karenda-` → `かれんだー`
- [ ] ターミナル (zsh): `karen` → `karen`

## 止める条件
- 日本語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-07)。
