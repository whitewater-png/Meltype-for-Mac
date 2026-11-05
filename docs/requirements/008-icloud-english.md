---
title: iCloud・iTunes の頭の i がかなになる (いCloud)
status: approved   # 定期タスクによる自動承認。元: docs/voice/2026-11-04.md 候補 1 (voice/2026-11-04 ブランチ)
source: docs/voice/2026-11-04.md 候補 1 / 本家 #1530
scope: dictionary
---

# 008 iCloud・iTunes の頭の i がかなになる

## 背景 (誰が、何に困っているか)
- 本家 #1530 (Windows 11、1.4.0): `iCloud` と打つと `いCloud` になる (期待 `iCloud`)。
- 今の挙動 (Core の `--repro`、2026-11-05 クラウド): `iCloud` → `いCloud`、`icloud` → `いcloud`、`iTunes` → `いTunes`、`itunes` → `いつね`。`iPhone`・`iPad` は英字のまま、`ichi`・`ikou` はかなのまま、`iCloudnihozon` は `iCloudにほぞん`。
- 番号は 005 (PR #10)・006 (PR #11)・007 (PR #12) が open のため 008 にした。

## 今の動き
- `iPhone`・`iPad` は `dictionaries/propernouns.txt` (小文字の形は英語判定にも使われる) にあるので崩れない。`iCloud`・`iTunes` が無いため、頭の `i` だけが読めて `いCloud` になる (要件 002・005・007 の辞書 1 語と同じ型。ただし `english.txt` に小文字で足しても大文字入りの `iCloud` は直らなかったため、足す先は `propernouns.txt`)。
- 本家の扱い: 未取り込み (クラウドから本家には届かないので、同じ直し方かは未確認)。

## minimum の要件
1. `iCloud`・`iTunes` と打って Enter で確定すると、そのまま `iCloud`・`iTunes` になる。
2. `iPhone`・`ichi`・`ikou`・`iCloudnihozon` の今の値は変えない。

## 非目標
- `iCloud`・`iTunes` 以外の語の追加 (`iMessage` など)。
- 判定ロジックの変更。

## 完成条件
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に `iCloud`・`iTunes`・`iCloud wo tsukau`・`iCloudnoerror` と守る行 (`ichi`・`ikou`・`iPhone`・`iCloudnihozon`) を足す。実装前は外れ、実装後は当たる
- [x] テスト: `FeedbackTests.cs` に `Req008_ICloud_ITunes_StayEnglish_AndJapaneseISpellingsDoNot`。実装前は赤
- [x] 回帰なし: eval-compare の broken 0、Core 全件通過、`check-dictionaries.mjs`・`check-terms.mjs` が通る
- [x] 文書: `docs/KNOWN_LIMITS.md` に iCloud・iTunes の記載なし (grep で確認)。直すものなし

## Mac 実機チェックリスト (人が確かめる。すべて未確認)
- [x] TextEdit: `iCloud` + Enter → `iCloud` … リョウさんの実機 (2026-11-05。「1～3＞OK」の一括報告で、項目ごとの記録は無い)
- [ ] ターミナル (zsh): `iTunes` + Enter → `iTunes`
- [ ] TextEdit: `ichi` → いち (変わらない)

## 止める条件
- 語を足して日本語の語の正解表の行が 1 件でも崩れる。

## 記録
- 定期タスクによる自動実行 (2026-11-05)。
- 査読 1 周 (止める指摘なし)。境界例 100 件を base と比較。日本語側で変わったのは「…tunes / …icloud で打ち終えてすぐ Enter」の 8 件だけ (`kitunes` きつね → きつねs、`haitunes` はいつね → はitunes、`haicloud` はいcloud → はicloud など)。後ろに 1 文字でも続けば base と同じ。受け入れる。
