---
title: obsidian がかなに崩れ、obsidian + 助詞は表示が英字なのに確定で おんしぢあんを になる
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-12.md 候補 1)
source: docs/voice/2026-11-12.md (ブランチ voice/2026-11-12) 候補 1 / 本家 #1763
scope: dictionary
---

# 016 obsidian がかなに崩れ、obsidian + 助詞は表示が英字なのに確定で おんしぢあんを になる

番号について: 015 はブランチ `req/015-ci-english` (査読 3 周で止めた要件。PR なし) が使っているので、016 にした。

## 背景 (誰が、何に困っているか)
- 本家 #1763 (Windows、1.5.0、bot が再現): `obsidian` と打つと `おbしぢあn` になる (期待 `obsidian`)。メモのアプリ Obsidian の名前を小文字で打つ人に毎回起きる。本家の直し方は未確認 (クラウドから本家に届かない)。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-13、`origin/main` 20736db):
  - `obsidian` → 表示 `おbしぢあn`・確定 `おんしぢあん`
  - `obsidianwo` → 表示 `obsidianを`・確定 `おんしぢあんを`
  - `obsidiande` → 表示 `obsidianで`・確定 `おんしぢあんで`
  - `obsidianga` → 表示 `obsidianが`・確定 `おんしぢあんが`
  - `obsidianni` → 表示 `おbしぢあんい`・確定 `おんしぢあんい`
  - `obsidian wo tsukau` → 確定 `おんしぢあんをつかう`
  - `kyouhaobsidianwotukau` → 確定 `きょうはおbしぢあんをつかう`
  - `Obsidian` (大文字始まり) → `Obsidian` (崩れない)

## 今の動き (既に分かっていること)
- `o` が読めて `b` が英字に残る。確定のときは打ち間違いの補正が `b` を `n` と読み (キーが隣)、`おんしぢあん` になる。英単語の一覧 (`dictionaries/english.txt`) に `obsidian` が無い。
- 試しに `english.txt` に `obsidian` を足すと、上の 7 形がすべて期待どおり (表示と確定が同じ) になった (2026-11-13、作業ツリーで試して戻した)。`obsidiannoplugin` → `obsidianのplugin`、`obsidiannimemo` → `obsidianにめも`、`Obsidianwo` → `Obsidianを` も確かめた。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 本家 #1763 は bot が再現した段階。直しは未確認。

## minimum の要件 (これだけは満たす)
1. `obsidian` を単独でも、助詞 (`wo`・`de`・`ga`・`ni`) が続く形でも英字のまま確定し、助詞はかなになる (`obsidianwo` → `obsidianを`)。表示と確定が同じになる。
2. `o` + `b` で始まる日本語 (`obake`・`obon`・`oboeru`・`obi`・`obentou`) は今のまま。`Obsidian`・`obsolete`・`absorb` も今のまま。

## 非目標 (今回はやらない)
- 「表示は英字・確定はかな」のずれ全般を直すこと (`obs` → 表示 `おbs`・確定 `おば` は今のまま)。別の要件にするかは人が決める。
- obsidian 自体の打ち間違い (`obsidiann`・`obsidians` など) を直すこと (base と同じく崩れる)。
- 判定ロジックの変更。辞書 (`english.txt`) に 1 語足すだけ (見出し `本家 #1763` の行を新しく作る)。
- 候補 2 (`bugga`)・候補 3 (前後に半角スペースの記録) は別の要件。

## 受け入れる変化 (査読 1 周目の指摘で追記。受け入れてよいかは人が判断する)
- obsidian を打ちかけて途中で Enter した形のうち `obsid`・`obsidi`・`obsidia` は、表示が英字に変わるが、確定は今までどおりかなになる (表示と確定のずれが新しく生まれる。`kaggl` → 表示 `kaggl`・確定 `かっgl` と同じ、辞書の語の接頭辞に前からある型)。Core の `--repro` を base (`origin/main` 20736db) とこのブランチの両方で打った (2026-11-13、最後のキーは Enter):

  | 打鍵 | base の表示 / 確定 | このブランチの表示 / 確定 |
  |---|---|---|
  | `obsid` | `おbしd` / `おbしd` | `obsid` / `おbしd` |
  | `obsidi` | `おbしぢ` / `おんしぢ` | `obsidi` / `おんしぢ` |
  | `obsidia` | `おbしぢあ` / `おんしぢあ` | `obsidia` / `おんしぢあ` |
  | `kyouhaobsidia` | `きょうはおbしぢあ` / `きょうはおbしぢあ` | `きょうはobsidia` / `きょうはおbしぢあ` |

  `obsi` は base でも表示 `obsi`・確定 `おんし` で、変化なし (前からあるずれ)。
- 大文字始まりの `Obsidian` + 日本語が空白なしで分かれるようになる (良くなる方向): `Obsidiantomari` → 確定 `Obsidianとまり` (base は `Obsidiantomari` のまま。表示は `Obsidianとmari` で確定とずれる)。
- obsidian の前後に余計な 1 文字が付いた意味のない並び (`dobsidian`・`obsidiana` など) は崩れ方が変わるだけ (どちらも正しい語ではない)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  obsidian	obsidian	full	本家 #1763
  obsidianwo	obsidianを	full
  obsidiande	obsidianで	full
  obsidianga	obsidianが	full
  obsidianni	obsidianに	full
  obsidian wo tsukau	obsidian をつかう	full
  kyouhaobsidianwotukau	きょうはobsidianをつかう	full
  ```
  守る行 (今も当たる): `obake` → `おばけ`、`obon` → `おぼん`、`Obsidian` → `Obsidian`、`oboeru` → `おぼえる`、`obi` → `おび`、`obentou` → `おべんとう`、`obsolete` → `obsolete`、`absorb` → `absorb`
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req016_Obsidian_StaysEnglish_AndOSpellingsDoNot` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を比べる)。実装前は赤。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] TextEdit: `obsidian` + Enter → `obsidian`
- [ ] TextEdit: `obsidianwo` + Enter → `obsidianを` (変換ボックスの表示と確定が同じか)
- [ ] Safari のフォーム: `obsidiande` + Enter → `obsidianで`
- [ ] TextEdit: `obake` → おばけ、`obon` → おぼん (変わらない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 辞書に 1 語足すだけでは直らず、判定ロジックの変更が要るとき。
- 査読の境界例で、正しく打った日本語、または 1 打鍵の打ち間違いの補正が効かなくなる形が見つかったとき (要件 007・012 の教訓)。

## 記録 (ループが書く)
- 周回数: 実装 1 周、査読 3 周 (止める指摘なし。1 周目の直す = 非目標の `obsidia` の記述の事実違い、2 周目の直す = 「受け入れる変化」に `obsi` を入れた事実違い。どちらも文書だけ。3 周目は指摘なし)。
- 数字: Core 1260/1260、eval-compare broken 0 / fixed 7 (630→637/743)、`check-dictionaries.mjs`・`check-terms.mjs` エラー 0。
- 査読の境界例: 1,721,540 入力 (`readings.txt` + `japanese.txt` の 31,603 語の正しい打ち方・1 打鍵の打ち間違い・助詞付き・語の境目と n↔b・obsidian まわり) を base と比べ、obsidian を含まない入力の確定の変化は 0 件 (要件 007・012 の型の回帰なし)。obsidian を含む 40,812 件のうち 33,086 件は英字で確定するようになり、残りは意味のない並びの中の変化。比べたのは Enter で確定する形。
- 手戻りの原因: 「今のまま」と書いた形 (`obsidia`・`obsi`) を、base で打たずに (辞書を足した木の結果だけで) 書いた。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/20
