---
title: pre が pれ に崩れる (3 文字で p + r + e の英語)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-11-03.md 候補 2 (voice/2026-11-03 ブランチ)
source: docs/voice/2026-11-03.md 候補 2 / 本家 #1489
scope: dictionary
---

# 007 pre が pれ に崩れる

## 背景 (誰が、何に困っているか)
- 本家 #1489 (Windows、Discord、1.4.1-pre.2): `pre` と打つと `pれ` になる (期待 `pre`)。本家の bot は main では直っている見込みと答えた (どう直したかは未確認)。
- 今の挙動 (Core の `--repro`、2026-11-04 クラウド): `pre` → `pれ`、`prekana` → `pれかな`、`prenoerror` → `pれのerror`。`preview`・`pre-release`・`prefix` は英字のまま、`pure` → `ぷれ`。
- 番号は 005 (PR #10)・006 (PR #11) が open のため 007 にした。

## 今の動き
- `pre` は `p` が単独で残り `re` が読めるため、日本語とも英語とも決まらず `pれ` になる。`dictionaries/english.txt` の技術用語の行 (`ost` と同じ行) に足すと直る見込み (要件 002・005 と同じ型)。
- 本家の扱い: 未取り込み (クラウドから本家には届かないので、同じ直し方かは未確認)。

## minimum の要件
1. `pre` と打って Enter で確定すると `pre` になる。
2. `pure`・`purege`・`pretty`・`preview`・`pre-release` など、`pre` を単独で含まない打鍵の判定は変わらない。

## 非目標
- `pre` 以外の語の追加。
- 打っている途中の表示 (候補 3 の `dissatisfactio` の形)。

## 完成条件
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に `pre` → `pre`、`pre wo` 系と守る行を足す。実装前は外れ、実装後は当たる
- [ ] テスト: `FeedbackTests.cs` に `Req007_Pre_StaysEnglish_AndJapaneseSpellingsAroundItDoNot`。実装前は赤
- [ ] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載があれば直す

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `pre` + Enter → `pre`
- [ ] ターミナル (zsh): `pre` + Enter → `pre`
- [ ] TextEdit: `pure` → ぷれ (変わらない)

## 止める条件
- `pre` を足して日本語の語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-04)。
