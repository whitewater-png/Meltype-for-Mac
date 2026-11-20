---
title: pazolite・oeksound がかなと英字の混ざった形に崩れる
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-19.md 候補 1)
source: docs/voice/2026-11-19.md (ブランチ voice/2026-11-19) 候補 1 / 本家 #2037
scope: dictionary
---

# 021 pazolite・oeksound がかなと英字の混ざった形に崩れる

## 背景 (誰が、何に困っているか)
- 本家 #2037 (2026-11-19、Linux の Core テスト。声の文書の要約を通したもの): DTM の製品名・アーティスト名で、語の途中がかなになる。期待はどれも英字のまま。報告の 11 形のうち、大文字で始まる 6 語 (`Omnisphere` など) はこの fork では起きない。小文字の `pazolite`・`oeksound` は fork でも起きる。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-20、`origin/main` 20736db。実機は未確認):
  - `pazolite` → 表示・確定とも `ぱぞlite`
  - `oeksound` → 表示・確定とも `おえksound`
  - `pazolitenokyoku` → `ぱぞぃてのきょく`、`pazolitewo` → `ぱぞぃてを`、`pazolitega` → `ぱぞぃてが`
  - `kyouhapazolitewokiku` → `きょうはぱぞぃてをきく`
  - `oeksoundwo` → `oeksoundwo`、`oeksoundde` → `おえksoundで`、`oeksoundnoplugin` → `oeksoundnoplugin`
  - `kyouhaoeksoundwotukau` → `きょうはおえksoundをつかう`
  - `pazolite no kyoku` → 確定 `ぱぞlite のきょく`、`oeksound wo tsukau` → 確定 `おえksound をつかう`
  - `t+pazolite` → `t＋ぱぞlite`

## 今の動き (既に分かっていること)
- `pazolite` は `pa`・`zo` が読めて、残りの `lite` が英単語の一覧 (`dictionaries/english.txt` の「テスターのチャット」の行) にあるので `ぱぞ` + `lite` に割れる。`oeksound` は `o`・`e` が読めて `k` が残り、`sound` が英単語。どちらも一覧に無い。
- 試しに `english.txt` に `pazolite`・`oeksound` を足すと、上の形は `t+pazolite` の `＋` を除いてすべて英字 + かなの助詞になった (2026-11-20、作業ツリーで試して戻した)。`oeksoundnoplugin` は `oeksoundのplugin` になる。
- 本家の扱い: 未確認 (クラウドから本家に届かない)。
- 同じ型の過去の要件: 014 (`pixiv`)・011 (`colab`)。辞書に足すと打ち間違いの補正が効かなくなって取り込まなかった例: 007 (`pre`)・012 の `undef`・`ifndef`。

## minimum の要件 (これだけは満たす)
1. `pazolite`・`oeksound` を単独でも、助詞 (`no`・`wo`・`ga`・`de`) が続く形でも、文の途中でも英字のまま確定し、助詞と前後の日本語はかなになる。
2. `pazo` (`ぱぞ`)・`kyouhapazo`・`pazuru`・`oekaki`・`oeru`・`oeta` など、同じ綴りで始まる日本語は今のまま。`lite`・`sound` も今のまま。

## 非目標 (今回はやらない)
- `u-he` (`うーへ`)・`YUC'e` (`YUC’え`): 判定で直すか辞書に入れるか、そもそも直すべきかを人が決める (声の文書の候補 1 の注記)。
- `t+pazolite` の `+` が全角の `＋` になること。この要件の後は `t＋pazolite` (base は `t＋ぱぞlite`)。記号の幅は別の話 (PR #24 の査読で見つかった `reibunn:+1:` → `:＋1:` と同じ型)。
- 判定ロジックの変更。辞書 (`english.txt`) に 2 語足すだけ。
- 2 語自体の打ち間違い (`pazolit`・`oeksoun` など) を直すこと。

## 受け入れる変化 (受け入れてよいかはリョウさんの判断)
- 2 語を打ちかけて途中で Enter した形は、表示が英字に変わるが、確定は今までどおりかな (表示と確定のずれ。`kaggl`・PR #20 の `obsidia` と同じ、辞書の語の接頭辞に前からある型)。作業ツリーで見た例: `pazolit` → 表示 `pazolit`・確定 `ぱぞlit`、`oeksoun` → 表示 `oeksoun`・確定 `おえkそうん`、`oeksou` → 表示 `oeksou`・確定 `おえkそう`、`oeks` → 表示 `oeks`・確定 `おえか`。`pazoli` は `ぱぞぃ` のまま。base の表示・確定との差は査読で確かめて記録に書く。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  pazolite	pazolite	full
  oeksound	oeksound	full
  pazolitenokyoku	pazoliteのきょく	full
  pazolitewo	pazoliteを	full
  kyouhapazolitewokiku	きょうはpazoliteをきく	full
  oeksoundwo	oeksoundを	full
  oeksoundde	oeksoundで	full
  oeksoundnoplugin	oeksoundのplugin	full
  kyouhaoeksoundwotukau	きょうはoeksoundをつかう	full
  pazolite no kyoku	pazolite のきょく	full
  ```
  守る行 (今も当たる): `pazo` → `ぱぞ`、`kyouhapazo` → `きょうはぱぞ`、`pazuru` → `ぱずる`、`oekaki` → `おえかき`、`oeru` → `おえる`、`oeta` → `おえた`、`lite` → `lite`、`sound` → `sound`
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req021_PazoliteOeksound_StayEnglish_AndKanaSpellingsDoNot` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を完全一致で比べる)。実装前は赤。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [ ] 査読: 別エージェントが、日本語の語の正しい打ち方と 1 打鍵の打ち間違いを base と比べ、2 語を含まない入力の確定の変化が 0 件 (要件 007・012 の教訓)
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載があれば直す

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] TextEdit: `pazolite` + Enter → `pazolite`
- [ ] TextEdit: `oeksound` + Enter → `oeksound`
- [ ] TextEdit: `pazolitenokyoku` + Enter → `pazoliteのきょく` (Space で `pazoliteの曲` が候補に出るか)
- [ ] Safari のフォーム: `oeksoundwo` + Enter → `oeksoundを`
- [ ] TextEdit: `oekaki` → おえかき、`pazuru` → ぱずる (変わらない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 辞書に足すだけでは直らず、判定ロジックの変更が要るとき。
- 査読の境界例で、正しく打った日本語、または 1 打鍵の打ち間違いの補正が効かなくなる形が見つかったとき (要件 007 の教訓)。その語は外す。

## 記録 (ループが書く)
- (未記入)
