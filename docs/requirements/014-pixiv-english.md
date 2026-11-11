---
title: pixiv が ぴぃv に崩れ、pixiv + 助詞は丸ごと英字のまま残る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-10.md 候補 2)
source: docs/voice/2026-11-10.md (ブランチ voice/2026-11-10) 候補 2 / 本家 #1696
scope: dictionary
---

# 014 pixiv が ぴぃv に崩れ、pixiv + 助詞は丸ごと英字のまま残る

## 背景 (誰が、何に困っているか)
- 本家 #1696 (Windows・Vivaldi、1.4.0): `pixiv` と打つと `ぴぃv` になる (期待 `pixiv`)。本家は PR #1701 で閉じた (直し方は未確認。クラウドから本家に届かない)。絵の投稿の話で日常的に出る語。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-11、`origin/main` 48fd746):
  - `pixiv` → 表示・確定とも `ぴぃv`
  - `pixivnitoukou` → `pixivnitoukou` (後ろの `にとうこう` までかなにならない)
  - `pixivde` → `pixivde`、`pixivwomiru` → `pixivwomiru`
  - `pixivga` → 表示 `pixivga`・確定 `ぴぃが`
  - `pixiv wo miru` → 確定 `ぴぃvをみる`

## 今の動き (既に分かっていること)
- `pi` + `xi` (ぃ) が読めて、`v` だけが英字に残る。英単語の一覧 (`dictionaries/english.txt`) に `pixiv` が無い。
- 同じ投稿サイトの語 `fanbox`・`skeb` は `english.txt` の「テスターのチャット」の行にあり、英字のまま残る。
- 試しに `english.txt` に `pixiv` を足すと、上の 6 形 (正解表に入れたのは `pixivwomiru` を除く 5 形) がすべて期待どおりになった (2026-11-11、作業ツリーで試して戻した)。
- 本家の扱い: 本家 PR #1701 で対応済み (中身は未確認)。

## minimum の要件 (これだけは満たす)
1. `pixiv` を単独でも、助詞 (`de`・`ni`・`ga`・`wo`) が続く形でも英字のまま確定し、助詞はかなになる (`pixivnitoukou` → `pixivにとうこう`)。
2. `pixi` (`ぴぃ`) など語でない形、`pinku`・`piza`・`pittari` など `pi` で始まる日本語は今のまま。`pixel`・`booth`・`fanbox` も今のまま。

## 非目標 (今回はやらない)
- pixiv 自体の打ち間違い (`pxiv`・`pixxiv` など) を直すこと (base と同じく崩れる)。
- 候補 1 (`alt`・`esc` などキーの名前)、候補 3 (`onvif` の `ゔぃ`) は別の要件。
- 判定ロジックの変更。辞書 (`english.txt`) に 1 語足すだけ (見出し `本家 #1696` の行を新しく作る)。
- 大文字入りの `Pixiv`・`PIXIV` 単独は今も英字のまま (変えない)。`Pixivnitoukou` は `Pixivにとうこう` に変わる (良くなる方向。査読で確認)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  pixiv	pixiv	full	本家 #1696
  pixivnitoukou	pixivにとうこう	full
  pixivde	pixivで	full
  pixivga	pixivが	full
  pixiv wo miru	pixiv をみる	full
  ```
  守る行 (今も当たる): `pixi` → `ぴぃ`、`pixel` → `pixel`、`booth` → `booth`、`fanbox` → `fanbox`、`pinku` → `ぴんく`、`piza` → `ぴざ`、`pittari` → `ぴったり`
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req014_Pixiv_StaysEnglish_AndPiSpellingsDoNot` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を比べる)。実装前は赤。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [x] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。[x] は 2026-11-11 にリョウさんが確認、ほかは未確認)
- [x] TextEdit: `pixiv` + Enter → `pixiv`
- [x] TextEdit: `pixivnitoukou` + Enter → `pixivにとうこう` (Space で `pixivに投稿` が候補に出るかは未確認)
- [ ] Safari のフォーム: `pixivde` + Enter → `pixivで`
- [x] TextEdit: `pinku` → ぴんく (変わらない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 辞書に 1 語足すだけでは直らず、判定ロジックの変更が要るとき。
- 査読の境界例で、正しく打った日本語、または 1 打鍵の打ち間違いの補正が効かなくなる形が見つかったとき (要件 007 の教訓)。

## 記録 (ループが書く)
- 周回数: 実装 1 周、査読 1 周 (止める指摘なし。直す 1 件 = AGENT_STATE、任意 3 件のうち辞書の見出し・本文の件数・`Pixiv` の記録を反映)。
- 数字: Core 1255/1255、eval-compare broken 0 / fixed 5 (576→581/687)、`check-dictionaries.mjs`・`check-terms.mjs` エラー 0 (警告の数は base と同じ)。
- 査読の境界例: 25,159 入力 (readings.txt の ぴ・ぃ・ゔ を含む 410 語 + 手作りの例と、それぞれの 1 打鍵の打ち間違い) を base と比べ、変わったのは `pixiv` を含む 1,157 件だけ。pixiv を含まない入力の変化は 0 件 (要件 007 の型の回帰なし)。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/19
- 2026-11-11 リョウさんの Mac 実機 (TextEdit、Enter で確定。試し版 int/q41-q43 に main を合わせた手元の版 verify/q41-q43-plus-1111 を入れて確認。19 行の確認表への「全て期待結果通り」の一括報告で、項目ごとの記録は無い): `pixiv` → pixiv、`pixivnitoukou` → pixivにとうこう、`pinku` → ぴんく。Safari の `pixivde` は未確認。
