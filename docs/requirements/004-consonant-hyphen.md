---
title: 母音の無い英字 + ハイフン (pt-query-digest・ls-files) が長音 ー になる
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-10-30.md 候補 2
source: docs/voice/2026-10-30.md 候補 2 / 本家 #1349
scope: core
---

# 004 母音の無い英字 + ハイフンを長音にしない

## 背景
- 本家 #1349: `pt-query-digest` が `ptーquery-digest` になる。
- 今の挙動 (Core の `--repro`、2026-10-30): `pt-query-digest` → `ptーquery-digest`、`ls-files` → `lsーfiles`、`cd-rom` → `cdーrom`、`rm-rf` → `rmーrf`、`kyouhapt-query` → `きょうはptーquery`。

## 今の動き
- 関係する場所: `src/Meltype.Core/Composition/CompositionText.cs` の `IsPlainHyphenPosition` (数字・半角 - の後ろだけ半角のまま)。
- `git-log`・`docker-compose` など母音のある語は単独なら英字のまま (今回の範囲外)。

## minimum の要件
1. かなにならなかった英字が 2 文字以上続き、母音 (aiueo) を含まないとき、その直後の `-` は長音にせず半角のまま残す。
2. 母音を含むかなの長音 (`ra-men`・`ko-hi-`・`ssu-pa-`) は変えない。

## 非目標
- 日本語の直後の `git-log` → `きょうはgitーlog` (候補 1。判定ロジックの別の所を触る)。

## 完成条件
- [x] 正解表 `eval/cases/mac-terminal.tsv` に 7 行 (実装前は 6 行が外れ、実装後は当たる)
- [x] テスト `Req004_ConsonantOnlyLettersThenHyphen_StayHalfWidth_AndLongVowelsDoNot` (実装前は赤)
- [x] 回帰なし: eval-compare broken 0、Core 全件通過

## Mac 実機チェックリスト (未確認)
- [ ] TextEdit: `pt-query-digest` + Enter → `pt-query-digest`
- [ ] ターミナル: `ls-files`・`cd-rom` → 英字のまま
- [ ] TextEdit: `ra-menwotaberu` → らーめんをたべる

## 止める条件
- 母音を含むかなの長音が変わるなら止める。

## 記録
- 周回数: 実装 1 周 / 査読は PR 本文
