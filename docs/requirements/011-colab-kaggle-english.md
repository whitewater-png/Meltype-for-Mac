---
title: colab・collab・kaggle が英字にならない (こlab・かっがぇ)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-11-07.md 候補 1 と 2 (voice/2026-11-07 ブランチ)
source: docs/voice/2026-11-07.md 候補 1・2 / 本家 #1607・#1609
scope: dictionary
---

# 011 colab・collab・kaggle が英字にならない

## 背景 (誰が、何に困っているか)
- 本家 #1607 (Windows、1.4.1-pre.6): `colab` と打つと `こlab` になる (期待 `colab`)。本家 #1609: `kaggle` が `kaggale` 相当に崩れる (期待 `kaggle`)。
- 今の挙動 (Core の `--repro`、2026-11-08 クラウド): `colab` → `こlab`、`collab` → `こっlab`、`kaggle` → `かっがぇ` (途中は `かっgぇ`)、`colab wo tsukau` → `こlab をつかう`、`kaggle wo tsukau` → `かっがぇをつかう`。
- 守る側の今の値: `korabo` → `こらぼ`、`kore` → `これ`、`kaketa` → `かけた`、`kagu` → `かぐ`、`kagaku` → `かがく`、`kyouhakorabo` → `きょうはこらぼ`。
- 候補 1・2 は同じ型 (辞書 1 語) なので 1 つの要件にまとめた。

## 今の動き
- `gitlab` は `dictionaries/english.txt` の技術用語にあるので崩れない。`colab`・`collab`・`kaggle` は無く、頭の `co`・`ka`・`ga` が読めて残りが英字になる (要件 002・005 の型)。
- 注意 (要件 007 / PR #12): 辞書に足すと「1 打鍵多い打ち間違いの補正」が効かなくなる語がある。足す語ごとに、日本語の打ち間違いが悪化しないかを査読で測る。
- 本家の扱い: 未取り込み (クラウドから本家に届かないので、同じ直し方かは未確認)。

## minimum の要件
1. `colab`・`collab`・`kaggle` と打って Enter で確定すると、そのまま英字になる (`colab wo tsukau` → `colab をつかう`)。
2. `korabo`・`kore`・`kaketa`・`kagu`・`kagaku`・`kyouhakorabo` の今の値は変えない。

## 非目標
- `colabde` (`colabで` にする。今は全体が英字) の助詞の分け方。
- `colab`・`collab`・`kaggle` 以外の語の追加。判定ロジックの変更。

## 完成条件
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に `colab`・`collab`・`kaggle`・`colab wo tsukau`・`kaggle wo tsukau` と守る行 (`korabo`・`kore`・`kaketa`・`kagu`・`kagaku`・`kyouhakorabo`) を足す。実装前は外れ、実装後は当たる
- [ ] テスト: `FeedbackTests.cs` に `Req011_Colab_Collab_Kaggle_StayEnglish_AndJapaneseSpellingsDoNot`。実装前は赤
- [ ] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (grep で確認)

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `colab` + Enter → `colab`、`collab` → `collab`
- [ ] TextEdit: `kaggle` + Enter → `kaggle`
- [ ] TextEdit: `korabo` → こらぼ、`kore` → これ (変わらない)

## 止める条件
- 語を足して日本語の語の正解表の行が 1 件でも崩れる。
- 査読で、日本語の正しい打ち方が崩れる、または打ち間違いの補正が大きく効かなくなる (要件 007 と同じ) と分かった語は、その語を外す。

## 記録
- 定期タスクによる自動実行 (2026-11-08)。
