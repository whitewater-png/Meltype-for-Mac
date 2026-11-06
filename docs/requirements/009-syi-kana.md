---
title: syi が しぃ にならず sい になる
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-11-05.md 候補 4 (voice/2026-11-05 ブランチ)
source: docs/voice/2026-11-05.md 候補 4 / 本家 #1464
scope: core
---

# 009 syi が しぃ にならず sい になる

## 背景 (誰が、何に困っているか)
- 本家 #1464 (Windows 11、1.4.0。本家の main でも再現。good first issue): `syi` と打つと `sい` になる (期待 `しぃ`)。
- 今の挙動 (Core の `--repro`、2026-11-06 クラウド): `syi` → `sい`、`asyi` → `asい`、`syisai` → `sいさい`、`zyi` → `zい`。`tyi`・`cyi` は `ちぃ`、`shi` は `し`、`syu`・`sya` は `しゅ`・`しゃ`。
- 番号は 008 (PR #13、マージ済み) の次の 009。

## 今の動き
- `RomajiDetector.cs` の `CompositionTable` に `ちぃ` (`cyi`・`tyi`) はあるが `しぃ` (`syi`) が無い。そのため `s` が読めず、`yi` だけが `い` になる。

## minimum の要件
1. `syi` と打って Enter で確定すると `しぃ` になる (`syisai` → `しぃさい`、`kyouhasyi` → `きょうはしぃ`)。
2. `shi`・`syu`・`sya`・`tyi`・`cyi` と、英語の語 (`busy`・`easy`) の今の値は変えない。

## 非目標
- `zyi`・`jyi`・`dyi` など `syi` 以外の `子音 + yi` (英語の語の中に出る `*yi` を巻き込む恐れがあり、人が決める)。
- 判定ロジックの変更 (表に 1 行足すだけ)。

## 完成条件
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に `syi`・`syisai`・`kyouhasyi` と守る行 (`shi`・`syu`・`sya`・`tyi`・`busy`・`easy`) を足す。実装前は外れ、実装後は当たる
- [x] テスト: `FeedbackTests.cs` に `Req009_Syi_BecomesSmallI_AndNeighborsDoNotChange`。実装前は赤
- [x] 回帰なし: eval-compare の broken 0、Core 全件通過
- [x] 文書: `docs/KNOWN_LIMITS.md` に `syi` の記載があれば直す

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `syi` + Enter → `しぃ`
- [ ] TextEdit: `shi` → し、`sya` → しゃ (変わらない)
- [ ] ターミナル (zsh): `busy`・`easy` → 英字のまま

## 止める条件
- 表に足して日本語・英語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-06)。
