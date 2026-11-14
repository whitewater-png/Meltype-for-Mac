---
title: re-toga (レートが) が英字のまま残る (長音の入った語 + 助詞が、接頭辞 + 英単語に読まれる)
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-13.md 候補 1)
source: docs/voice/2026-11-13.md (ブランチ voice/2026-11-13) 候補 1 / 本家 #1841
scope: core
---

# 017 re-toga (レートが) が英字のまま残る

## 背景 (誰が、何に困っているか)
- 本家 #1841 (Windows・Zen ブラウザー、1.5.0): 「最新の為替」に続けて `re-toga` と打つと英字のまま残る (期待 `レートが`)。本家の直し方は未確認 (クラウドから本家に届かない)。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-14、`origin/main` 20736db、同梱の英単語の一覧):
  - `re-toga` → 表示・確定とも `re-toga`
  - `re-sumo` (レースも)・`re-rude` (レールで)・`e-sumo` (エースも)・`e-rude` (エールで) → 英字のまま
  - `re-to` → `れーと`、`re-towo` → `れーとを`、`re-suga` → `れーすが`、`ko-toga` → `こーとが` (崩れない)
  - `kawasere-toga` → `かわせれーとが` (同じ入力の中で日本語に続くと崩れない。崩れるのは入力の先頭だけ)

## 今の動き (既に分かっていること)
- 関係する場所: `src/Meltype.Core/Composition/CompositionDetector.cs` の `IsHyphenatedEnglishWord`。接頭辞 (`HyphenPrefixes`: e・re・co …) + `-` + 3 文字以上の英単語なら英語 (`e-mail`・`re-enter`) とする規則が、`re` + `-` + `toga` (同梱の一覧にある英単語) にも当たる。`--segments re-toga` は `EN:re-toga`。
- `re-towo` が崩れないのは `towo` が英単語でないため。声の文書の推測 (`toga` が英単語のため) は、この規則を読んで確かめた。
- 読みの一覧 (`dictionaries/readings.txt`) に `れーと`・`れーす`・`れーる`・`えーす`・`えーる`・`こーと`・`こーど` などがある。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 未確認。

## minimum の要件 (これだけは満たす)
1. 全部小文字で打った「接頭辞 + `-` + 続き」で、続きが「語の残り + 助詞 1 つ」に分かれ、接頭辞の読み + `ー` + 語の残りの読みが `readings.txt`・`brands.txt` の語と丸ごと同じなら、続きが英単語でも英語にしない (`re-toga` → `れーとが`、`re-sumo` → `れーすも`、`re-rude` → `れーるで`、`e-sumo` → `えーすも`)。
2. `re-enter`・`re-run`・`re-make`・`re-made`・`re-do`・`co-op`・`e-mail`・`e-book` は英字のまま。`re-to`・`re-towo`・`ko-toga`・`kawasere-toga` は今までどおりかな。
3. 大文字を含めて打った形 (`Re-toga`) は今までどおり英字のまま。

## 非目標 (今回はやらない)
- 語の頭の一致 (`re-sa` = レーサー の頭) や、助詞が 2 つ以上・助詞以外が続く形 (`re-todesu`) の扱い。丸ごとの語 + 助詞 1 つだけ。
- `re-saga` (base でも英字のまま。`れーさ` は語でない)。
- 英数状態から打ち始めた形 (`CompositionController.DecideHeld` の経路) の測定。同じ `IsHyphenatedEnglishWord` を通るので同じ向きに変わる見込みだが、Core の `--repro` では測っていない。
- 候補 2 (`propernouns.txt` の形で確定)・候補 3 (表示と確定のずれ) は別の要件。

## 受け入れる変化 (リョウさんの判断をお願いします)
- 同梱の一覧で変わる続きは `toga`・`sumo`・`rude`・`doha`・`chide`・`tide` の 6 語、接頭辞は `re`・`e`・`co` (`co` は `こ` と読める) で、12 形: `re-toga`・`re-sumo`・`re-rude`・`e-toga` (えーとが)・`e-sumo`・`e-rude`・`co-toga` (こーとが)・`co-sumo` (こーすも)・`co-rude` (こーるで)・`co-doha` (こーどは)・`co-chide`/`co-tide` (こーちで)。どれも英語の複合語としては使わない並び。
- かなになるので、後ろの空白は変換のキーになる (`re-toga takai` → base `re-toga たかい`、変更後 `れーとがたかい`)。
- 実機の `NSSpellChecker` が英単語と答える続き (`toto`・`tomo` など。未確認) も、同じ条件ならかなになる。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  re-toga	れーとが	full	本家 #1841
  re-sumo	れーすも	full
  re-rude	れーるで	full
  e-sumo	えーすも	full
  re-togatakai	れーとがたかい	full
  ```
  守る行 (今も当たる): `re-to` → `れーと`、`re-towo` → `れーとを`、`re-suga` → `れーすが`、`ko-toga` → `こーとが`、`kawasere-toga` → `かわせれーとが`、`re-enter`・`re-run`・`re-make`・`re-made`・`re-do`・`co-op`・`e-mail`・`e-book`・`Re-toga` → 打ったまま
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req017_LongVowelWordThenParticle_IsKana_AndHyphenEnglishStays` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を比べる)。実装前は赤。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過
- [ ] 判定ロジックの変更なので、査読員が「接頭辞 + `-` + 英単語」の境界例を自分で作って base と比べる (CLAUDE.md §4)
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] TextEdit: `re-toga` + Enter → `れーとが` (Space で `レートが` が候補に出るか)
- [ ] TextEdit: 「最新の為替」を確定した直後に `re-toga` + Enter → `れーとが`
- [ ] TextEdit: `re-enter`・`e-mail`・`co-op` + Enter → 英字のまま
- [ ] ターミナル (zsh): `re-run` + Enter → `re-run`
- [ ] 英数状態 (英語を打った直後) から `re-toga` を打ったときの見え方

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 査読の境界例で、実際に使う英語の複合語 (`re-enter`・`e-mail` の型) がかなに崩れる形が見つかったとき。
- 入力の先頭以外 (日本語・英語に続く形) で base と結果が変わるとき。

## 記録 (ループが書く)
- (実装後に記入)
