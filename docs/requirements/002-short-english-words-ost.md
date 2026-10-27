---
title: ost が おst に崩れる (3 文字で o + 子音 2 つの英語)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-10-26.md 候補 1 (voice/2026-10-26 ブランチ)
source: docs/voice/2026-10-26.md 候補 1 / 本家 #1123
scope: dictionary
---

# 002 ost が おst に崩れる

## 背景 (誰が、何に困っているか)
- 本家 #1123 (closed、本家 PR #1148 で辞書に追加): `ost` と打つと `おst` になる (期待 `ost`)。
- 今の挙動 (Core の `--repro`、2026-10-27 クラウド): `ost` → `おst`、`ost wo tsukau` → `おstをつかう`。`--segments ost` は `units: o=お s=s +t`、`segments: JA:ost`。
- 同じ候補にあった `gooddane` → `ごおっだね` (本家 #1192) は、`good` + `dane` が `go|o|d|da|ne` と最後まで読める形で、区間の分け方 (Composition) を変える必要がある。回帰の恐れが大きいので **この要件では扱わない** (非目標)。

## 今の動き (既に分かっていること)
- `ost` は `dictionaries/english-words.txt` にも `english.txt` にも無い。`o` が読めて `st` が残るため、日本語とも英語とも決まらない。
- `ssd`・`os` のような IT 用語は `dictionaries/english.txt` の「技術用語」の行にある。そこに足すと直ることを `--repro` で確かめた。
- 本家の扱い: PR #1148 で辞書に追加 (未取り込み。クラウドから本家には届かないので、同じ内容かは未確認)。

## minimum の要件
1. `ost` と打って Enter で確定すると `ost` になる。
2. 日本語の語 (`kosuto`・`osutoraria`) と、`ost` を含まない既存の判定は変わらない。

## 非目標
- `gooddane` → `goodだね` (本家 #1192)。判定ロジックの変更が要るため別の要件にする。
- `ost` 以外の語の追加。

## 完成条件
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に `ost` → `ost`、`ost wo tsukau` → `ost をつかう`、守る 4 行を足す。実装前は `ost` 系 2 行が外れ、実装後は当たる
- [x] テスト: `FeedbackTests.cs` の `Req002_Ost_StaysEnglish_AndJapaneseSpellingsAroundItDoNot` (実装前は赤を確認)
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs` が通る
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載が無いことを確かめる

## Mac 実機チェックリスト (人が確かめる)
- [ ] TextEdit: `ost` + Enter → `ost`
- [ ] ターミナル (zsh): `ost` + Enter → `ost`、`kosuto` → こすと
- [ ] TextEdit: `osutoraria` → おすとらりあ (変わらない)

## 止める条件
- `ost` を足して日本語の語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-10-27)。
