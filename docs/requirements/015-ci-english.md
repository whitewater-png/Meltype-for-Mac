---
title: 語の頭の ci (CI) が cい に崩れる
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-11.md 候補 1)
source: docs/voice/2026-11-11.md (ブランチ voice/2026-11-11) 候補 1 / 本家 #1734
scope: core
---

# 015 語の頭の ci (CI) が cい に崩れる

## 背景 (誰が、何に困っているか)
- 本家 #1734 (Linux、1.5.0): `ciochiteru` が `子落ちてる` になる (期待 `ci落ちてる`)。開発の話で「CI が落ちた」を小文字で打つ。この fork では別の形で崩れる。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-12、`origin/main` 20736db。ビルドし直してから実行):
  - `ci` → 表示・確定とも `cい`
  - `cigaochita` → `cいがおちた`、`cinoerror` → `cいのerror`、`ciochiteru` → `cいおちてる`、`cide` → `cいで`
  - `ci/cd` → `cい/cd`
  - `ci ga ochita` → 確定 `cいがおちた`、`kono ci` → 確定 `このcい`、`github no ci` → 確定 `github のcい`、`kyou no ci` → 確定 `今日のcい`
  - `CI`・`Ci` (大文字入り)・`cicd`・`cd` は英字のまま。`ui` → `うい`。

## 今の動き (既に分かっていること)
- `ci` は `c` (かなにできない 1 文字) + `i` (い) に分かれ、同梱の英単語の一覧に無いので英語の区間にならない (`CompositionDetector.FindSpans` → `IsEnglishSpanCore`)。4 文字以上の `cicd` は「読めない英字の語」の決まりで全体が英字になるが、2 文字の `ci` は対象外。
- **`dictionaries/english.txt` に `ci` を足す案は採らない** (2026-11-12 に試して戻した)。上の形は直るが、`chi` の h を落とした打ち間違いの補正が効かなくなる: `docira` → `どちら`、`ocitsuku` → `おちつく`、`shakuhaci` → `しゃくはち` など。`readings.txt` の ち を含む語から作った 940 形のうち、base が正しく補正する 575 形が 36 形に減る (539 形を失う)。要件 007・009・012 と同じ型の悪化 (辞書への追加で日本語が悪化する語は入れない方針)。
- 一方、**`ci` で始まる入力は base でも補正されていない** (`cikai` → `cいかい`、`cizu` → `cいず`。上の 940 形のうち `ci` で始まる 174 形で、正しく補正されるものは 0)。語の頭の `ci` だけを英字にすれば、補正の損失は出ない見込み。
- 「語の頭のときだけ英語」と書ける辞書の形式は無い。判定ロジック (`CompositionDetector.FindSpans`) に守りを 1 つ足す。
- 本家の扱い: 本家 #1734 は open (2026-11-11 時点。直し方は未確認。クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 語の頭 (未確定の文字列の先頭、または空白・半角の記号・`[` の直後。`-` の直後は長音の後ろなので除く) の小文字の `ci` を英字のまま確定する。後ろに助詞・かなの語・記号が続いてもよい (`cigaochita` → `ciがおちた`、`ciochiteru` → `ciおちてる`、`ci/cd` → `ci/cd`)。
2. 語の途中の `ci` は今のまま (`docira` → `どちら`、`ocitsuku` → `おちつく`、`shakuhaci` → `しゃくはち` の補正を止めない)。
3. `ci` で始まる英単語 (`city`・`cinema`・`circle`)、`cicd`・`cd`・`CI`・`Ci`、`chi` で始まる日本語 (`chiisai`・`chotto`)、`ui` は今のまま。
4. 正しく打った日本語と、base で全部かなに補正されていた打ち間違いは 1 件も変えない。

## 受け入れる変化 (人の判断が要る。実装して分かったこと、2026-11-12)
- **空白で区切った `no`・`to` + `ci` は、`no`・`to` が英字に戻る**: `kyou no ci` → `今日no ci` (base は `今日のcい`)、`github no ci` → `github no ci` (base は `github のcい`)、`main no ci ga akai` → `main no ci があかい`、`no ci` → `no ci`、`to ci` → `to ci`。
  - 同梱の英単語は base でも同じ動き (`kyou no cli` → `今日no cli`、`kyou no git` → `今日no git`、`kyou no test` → `今日no test`、`github no cli` → `github no cli`)。`ci` が英語の区間になったので、後ろの語を見て前の `no`・`to` を英字に確定し直す既存の決まり (英文の `no`・`to` の扱い、`docs/W12_SHORTWORDS_2026-10-24.md`) に `ci` も乗るようになった。
  - 最初の完成条件に入れていた `github no ci` → `github のci` はこのため届かない。行を `kono ci` → `このci` に差し替えた (実装前は `このcい` で赤)。`no`・`to` + 英単語 全般の直し方は別の要件。
  - 空白を入れない `kyounoci` は変わらない (`きょうのcい`。非目標)。`ci no kekka` → `ci のけっか`、`ci to cd` → `ci とcd`、`slack de ci wo mawasu` → `slack でci をまわす` は良くなる。
- `ago ci ago` のように、英単語にも読める日本語 (`ago`) を空白で区切って `ci` の前後に置くと、全体が英字のままになる (base は `あごcいあご`)。
- `ci` で始まる崩れた入力の見た目が変わる: `cikai` → `ciかい` (base `cいかい`)、`cia` → `cia` (base `cいあ`)、`ci-zu` → `ci-ず` (base `cいーず`)、`cihkai` → `cihかい` (base `cihkai`)。どれも base で既に崩れている形。
- `city` などを打つ途中の表示が、`ci` まで打った時点で `ci` になる (base は `cい`)。

## 非目標 (今回はやらない)
- 空白で区切った `no`・`to` + 英単語 (`kyou no cli`・`kyou no ci`) の `no`・`to` を かなにすること (上の「受け入れる変化」)。
- 日本語のすぐ後ろの `ci` (`kyouhacigaochita` → 今は `きょうはcいがおちた`)。語の途中の h 落ちの補正と見分けられないので扱わない (空白を挟めば直る)。
- `ci` で始まる h 落ちの打ち間違い (`cikai` = ちかい、`cigau` = ちがう) を かなに直すこと。base は `cいかい`・`cいがう`、変更後は `ciかい`・`ciがう` (どちらも崩れたまま。見た目だけ変わる)。
- `qi`・`qa` など、ほかの「読めない 1 文字 + 母音」。
- 辞書の変更、Swift 側の変更、手動モード (判定レベル Manual)・かな入力の挙動。
- 実機の `NSSpellChecker` が `ci` を英単語と答える場合の挙動 (Core では測れない)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  ci	ci	full	本家 #1734
  cigaochita	ciがおちた	full
  cinoerror	ciのerror	full
  ciochiteru	ciおちてる	full
  ci/cd	ci/cd	full
  ci ga ochita	ci がおちた	full
  kono ci	このci	full
  ```
  守る行 (今も当たる。実装後も当たる): `docira` → `どちら`、`ocitsuku` → `おちつく`、`shakuhaci` → `しゃくはち`、`chiisai` → `ちいさい`、`chotto` → `ちょっと`、`ui` → `うい`、`CI` → `CI`、`city` → `city`、`cinema` → `cinema`、`cicd` → `cicd`
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req015_Ci_AtWordHead_StaysEnglish_AndChiTyposStillCorrected` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を比べる)。実装前は赤。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、品質テスト (`--eval`) の数字が base と同じ
- [ ] 査読: 判定ロジックの変更なので、査読員が境界例を自分で作って base と比べ、「base で全部かなだった入力が変わる」形が 0 件
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] TextEdit: `ci` + Enter → `ci`
- [ ] TextEdit: `cigaochita` + Enter → `ciがおちた` (Space で `ciが落ちた` が候補に出るかは未確認)
- [ ] ターミナル (zsh): `ci/cd` + Enter → `ci/cd`
- [ ] TextEdit: `docira` + Enter → `どちら`、`ocitsuku` + Enter → `おちつく` (変わらない)
- [ ] TextEdit: `city` + Enter → `city`、`chiisai` → `ちいさい` (変わらない)
- [ ] TextEdit: `city` を打つ途中 (`ci` まで打った時点) の表示が `ci` になる (今は `cい`)。打ちにくくないか

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 境界例で、正しく打った日本語、または base で全部かなに補正されていた打ち間違いが 1 件でも変わるとき (要件 007・009 の教訓)。
- 守りを `FindSpans` の 1 か所に置くだけでは直らず、打ち間違いの補正 (`RomajiTypoCorrector`・`FixTypos`) の変更が要るとき。

## 記録 (ループが書く)
- (実装後に書く)
