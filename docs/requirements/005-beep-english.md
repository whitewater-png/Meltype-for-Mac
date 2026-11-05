---
title: beep が べえp に崩れる (be + e が読めて p が残る英語)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-10-31.md 候補 1 (voice/2026-10-31 ブランチ)
source: docs/voice/2026-10-31.md 候補 1 / 本家 #1377
scope: dictionary
---

# 005 beep が べえp に崩れる

## 背景 (誰が、何に困っているか)
- 本家 #1377 (Mac、1.4.1-pre.1、open): ChatGPT で `beepon` + Enter が `beeぽん` になる (期待 `beep音`)。
- 今の挙動 (Core の `--repro`、2026-11-01 クラウド): `beep` → `べえp`、`beep sound` → `べえpsound`、`beep wo narasu` → `べえpをならす`、`beepon` → `べえぽん`。
- `beep` は `dictionaries/` のどの表にも無い (`beeper` は `english-words.txt` にある)。要件 002 (`ost`) と同じ形で、辞書に 1 語足すと直る見込み (`--repro` で確かめてから完成条件にした)。

## minimum の要件
1. `beep` と打って Enter で確定すると `beep` になる。後ろに英単語・助詞が続く形 (`beep sound`・`beep wo narasu`) も `beep` が英字で残る。
2. `bee` 始まりの日本語の読み (`beeru`)・`bee` 単独・辞書にある `beeper` は変わらない。

## 非目標
- `beepon` → `beep音` (漢字は Core のテストで測れない。`beep` を足した後の `beepon` の形は記録欄に書くだけで、完成条件にしない)。
- `beep` 以外の語の追加。

## 完成条件
- [x] 正解表 `eval/cases/short-words-in-english.tsv` に `beep`・`beep sound`・`beep wo narasu` と守る 3 行。実装前は `beep` 系 3 行が外れ、実装後は当たる
- [x] テスト `FeedbackTests.cs` の `Req005_Beep_StaysEnglish_AndBeeWordsDoNot`。実装前は赤
- [x] 回帰なし: eval-compare broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` エラー 0

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [ ] TextEdit: `beep` + Enter → `beep`
- [ ] ターミナル (zsh): `beep sound` → `beep sound`
- [ ] TextEdit: `beeru` → べえる (変わらない)
- [ ] ChatGPT (ブラウザー): `beepon` + Enter の見え方 (期待は `beep音`。Core では測れない)

## 止める条件
- `beep` を足して日本語の語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-01)。
- 周回数 1 (査読 1 回、止める指摘なし)。査読員が境界例 50 件を base と比べ、変わったのは `beep` をそのまま含む打鍵 (`kyouhabeepga`・`nobeep`・`beep.` など) だけで、どれも base では `べえp`・`べえぺ` に崩れていた。`beepu`・`beppu`・`abeepo` など日本語として読める形は変わらない。
- 結果: Core 1229/1229、eval-compare broken 0 / fixed 3、品質テストは base と同一。
- `beepon` は `beep` を足した後も `べえぽん` のまま (本家の期待 `beep音` とは別。`beepu` と同じく日本語として読める形を優先)。`beeps`・`beeping`・`beeped` は崩れる (非目標。次の候補)。
