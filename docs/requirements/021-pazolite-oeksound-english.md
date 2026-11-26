---
title: pazolite・oeksound がかなと英字の混ざった形に崩れる
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-19.md 候補 1)   # 棚卸し 2026-11-25: 実機未確認
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
査読員が base (`origin/main` 20736db) と比べた結果 (Core、Enter で確定。実機は未確認)。
1. 2 語を打ちかけて途中で Enter した形は、表示だけが英字に変わり、確定は base と同じ (表示と確定のずれ。`kaggl`・PR #20 の `obsidia` と同じ、辞書の語の接頭辞に前からある型)。

   | 打鍵 | base の表示 / 確定 | この要件の後の表示 / 確定 |
   |---|---|---|
   | `pazol` | ぱぞl / ぱぞ | pazol / ぱぞ |
   | `pazolit` | ぱぞlit / ぱぞlit | pazolit / ぱぞlit |
   | `oeksou` | おえkそう / おえkそう | oeksou / おえkそう |
   | `oeksoun` | おえksoun / おえkそうん | oeksoun / おえkそうん |
   | `kyouhapazol` | きょうはぱぞl / きょうはぱぞ | きょうはpazol / きょうはぱぞ |
   | `kyouhaoeks` | きょうはおえks / きょうはおえか | きょうはoeks / きょうはおえか |

   `paz`・`pazo`・`pazoli`・`oek`・`oeks`・`oekso` は、単独で打った場合は表示も確定も base と同じ (日本語の後に続けると、表の `kyouhaoeks` のように表示だけ変わる。`oeks` は base でも 表示 `oeks`・確定 `おえか`。`pazol` の確定で `l` が消えるのも base から)。
2. **`pazolite` 自体の 1 打鍵の打ち間違いが、確定で `pazolite` に寄る** (打ち間違いの補正が新しい語にも効く)。2 語の打ち間違い 19,673 入力のうち、綴りをそのまま含まない 227 件 (`pazolitde` を含む)。227 件はこの 19,673 入力の中での数で、上限ではない: `pazolit` (e の抜けた形) の前後に日本語の語や助詞が続く形でも起きる (査読 2 周目の別の 424,669 入力で 34 件。`pazolitde` を含み、227 件と 1 件重なる。`pazolitwatashi` ぱぞぃてわたし → pazoliteわたし、`pazolitdentou` ぱぞぃてんとう → pazoliteんとう)。base の確定はどれも意味のないかな・混ざった形で、日本語の語が壊れた例は無い。`oeksound` の打ち間違いでは 0 件。
   - 良くなる方向の例: `pazolitd` ぱぞlite → pazolite、`pazklite` ぱぞlite → pazolite、`paqzolitewo` ぱぞぃてを → pazoliteを、`kyouhapazlolitenokyoku` きょうはぱぞぃてのきょく → きょうはpazoliteのきょく
   - どちらも望ましくない例: `pazolitxeno` ぱぞぃてんお → pazoliteんお、`kyouhapapzoliteno` きょうはっぱぞぃての → きょうはっpazoliteの、`pazolitde` ぱぞlite → pazolite (で は base でも消えている)
3. `oeksoundesu` (で + す の d が 1 つ) は おけそうんです → おえkそうんです。どちらも望ましい形 (`oeksoundです`) ではない。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
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
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req021_PazoliteOeksound_StayEnglish_AndKanaSpellingsDoNot` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を完全一致で比べる)。実装前は赤。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [ ] 査読: 別エージェントが、日本語の語の正しい打ち方と 1 打鍵の打ち間違いを base と比べ、2 語を含まない入力の確定の変化が 0 件 (要件 007・012 の教訓)
  - **文字どおりには未達 (人の判断待ち)。** 日本語の語から作った 915,400 入力は確定・表示とも変化 0 件で、007・012 の型の回帰は無い。ただし `pazolite` 自体の打ち間違い 227 件 (綴りをそのまま含まない入力) の確定が変わった (「受け入れる変化」の 2)。条件を書いた時に「2 語の打ち間違い」を除くと書かなかったので、条件は書き換えずに未達のまま残し、受け入れるかをリョウさんに聞く。
- [x] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ。K06 は一般的な書き方で 2 語を名指ししていない)

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
- 周回数: 実装 1 周 (辞書に 2 語。判定ロジックは未変更)。査読は別エージェントで 1 周目に [止める] 1 件 (完成条件の「2 語を含まない入力の確定の変化が 0 件」に対し、`pazolite` 自体の打ち間違い 227 件が変わった。辞書・コードは直さず、「受け入れる変化」と完成条件に事実を書いて人の判断に回した)、[直す] 1 件 (この記録と AGENT_STATE)、[任意] 2 件 (接頭辞の表の `oeks`・`pazol` の誤り、`oeksoundesu` → どちらも反映)。2 周目は [止める] なし、[直す] 1 件 (件数を 228 と二重に数えていた → 227 に直した)、[任意] 2 件 (接頭辞は単独で打った場合の話、227 件は上限でない → どちらも反映)。2 周目の査読員は別の綴り (`si`・`tu`・`n'` など) と断片を語の間に置いた 424,669 入力も比べ、`pazolit` を含まない入力の確定の変化は 0 件 (表示は 2 語の断片を含む 13,912 件で変わる。「受け入れる変化」の 1 の型)。3 周目は [止める]・[直す] なし、[任意] 3 件 (測れていないことの書き方、34 件の内訳、表示の変化の件数 → 反映)。
- 先に赤: 2 つ目のコミットの木で `Req021_...` は 0/1 (`pazolite` 期待 pazolite / 実際 ぱぞlite)。正解表の 10 行は base で外れる。
- 数字 (2026-11-20、クラウド Linux): Core 1260/1260、eval-compare (origin/main 20736db と比較) broken 0 / fixed 10 / added 0 (630→640/746)、品質テスト (`--eval`) は base と同じ 1295/1296、`check-dictionaries.mjs`・`check-terms.mjs` エラー 0。
- 査読の境界例 (Core、Enter で確定、同梱の英単語の一覧): `readings.txt` の約 2 万語 (ヘボン式 1 通り) + `japanese.txt` 930 語から、正しい打ち方 20,441、+ 助詞 11 種 224,808、1 打鍵の打ち間違いのうち 2 語の断片 (`paz`・`pazol`・`oek`・`oeks`・`eks`・`kso`・`zol` など) を含むもの全件 16,874、それ以外の打ち間違い約 841 万件の 4 % の標本 332,414、語の境目に断片が出る複合語とその打ち間違い 326,829。重複を除いて 915,400 入力で、確定・表示とも変化 0 件。
- 測れていないこと: 断片を含まない打ち間違いの残り 96 %、別の綴り (`si`・`tu`・`n'` など) の打ち間違いの残り 97 % (2 周目に正しい形・助詞付きは全件、打ち間違いは 3 % の標本を比べた)、Space で変換する形、実機の `NSSpellChecker`、azooKey の漢字変換、実機の入力欄。本家の直し方 (クラウドから届かない)。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/26
- 2026-11-26 リョウさんの実機 (確認表 docs/HANDS_ON_CHECKLIST_2026-11.md の 60: Hermes の入力欄で `oeksoundwo` → `oeksoundを` OK (Safari のフォームの代わり。Safari 固有の挙動は未確認のまま)
