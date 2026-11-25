---
title: 助詞の後ろの「あつ・おつ・うつ・いつ・ぶつ」が英字に割れる (とてもあつい が とてもatすい になる)
status: draft   # draft | approved | done。approved にするのは人
source: 派生フォーク比較ベンチ (2026-11-22 計測の xlsx、649 件) の「本家では直っていて fork だけ外れる」行 / リョウさんの実機 (2026-11-25、`totemoatsui` → `toてもatすい`)
scope: core
---

# 033 助詞の後ろの「あつ・おつ・うつ・いつ・ぶつ」が英字に割れる

## 背景 (誰が、何に困っているか)
- 観測: 比較ベンチの `kyouhatotemoatsui` (期待 今日はとても暑い) が、Core のテストの打鍵で `きょうはとてもatすい` になる。本家 main (01672c87) では `きょうはとてもあつい`。
- 実機 (リョウさん、2026-11-25、Hermes の入力欄): `totemoatsui` → `toてもatすい` (Enter 前の表示)。候補は「とてもあt水を」など。実機では先頭の `to` も英字に残る (テストの打鍵では と)。この差の原因は未確認 (この要件の範囲外。下の非目標)。
- 範囲 (Core のテストの打鍵で確かめた): 「かな + 助詞 (も・は・が・を・に・で・と・の・へ・や) + 母音 1 つ or 1 音 + tsu」の形すべて。`kyoumoatsui` → きょうもatすい、`kokodemoatsui`、`soremootsukaresama` → それもおtすかれさま、`kimitoutsukushii` → きみとうtすくしい、`watashigaitsumodoori` → わたしがitすもどおり、`kokodebutsuri` → ここでbutすり。

## 今の動き (既に分かっていること)
- 関係する場所: `src/Meltype.Core/Composition/CompositionText.cs`。英単語の最後の t の次に打った s を、t と合わせて つ にしない決まり (`commitsuru` → commitする。本家 #77、P45-3) が、`EndsWithEnglishWordFromUnit(..., tsuFollows: true)` で英単語を探す。
- 原因: `to|te|mo|a|t` を後ろからさかのぼると、`mo|a|t` = `moat` が知っている英単語になる (heat・moot・toot・tout・gait・debut も同じ形)。日本語なら例外にする `ReadsAsJapaneseWithTsu` (本家 35beb60、P45-11) は、英単語の頭 (も) から読んだ「もあつ」と、さらに前から読んだ「てもあつ」…しか見ないので、助詞の次から読んだ「あつ」が見えない。
- いつから: Mac 版の最初のコミット (c3c247f) から。本家もフォークした時点 (dffcdb05) では同じで、本家は `7794629b` (PR #1185 のラティス化。2026-10-30 に Mac 版では導入しないと決めたもの) でこの決まりごと消して直った (どちらも git bisect で確かめた)。

## minimum の要件 (これだけは満たす)
1. `ReadsAsJapaneseWithTsu` の例外に 1 つ足す: 英単語の頭の単位が助詞 (も・は・が・を・に・で・と・の・へ・や) で、その前の単位が かな で、助詞の次の単位が 1 つだけ (英単語 = 助詞 + 1 音 + t) で、その 1 音 + つ が日本語の語かその頭 (`IsJapaneseWordOrPrefix`。あつ・おつ・うつ・いつ・ぶつ) なら、t と s を つ にまとめる。
2. 語の頭の英単語 (`heatsuru`・`debutsuru`・`commitsuru`)、助詞の次が 2 音以上の英単語 (`kyouhamomentsuru`・`korewodeceitsuru`) は今までどおり。
3. 新しい設定項目は作らない。ラティス化 (#1185) は入れない。

## 非目標 (今回はやらない)
- 実機で先頭の `to` が英字に残ること (`toてもatすい`)。実機の macOS のスペルチェッカーが関わると推測しているが未確認。切り分けにはトレースが要る (取る前に同意を取る)。
- 同じベンチの他の「本家だけ直っている」行 (game・cache・image・me・do・cd ~/ など)。多くは 2026-10-30 に見送り・不採用と決めたもの。
- 別の形の割れ (`soremoteishutsu` → そremoteいしゅつ、`nihonninitsuite` → にほんinitsuite)。今回の掃引で見つけたが、規則が違う (前者は t+s ではない)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `src/Meltype.Core.Tests/ParticleBeforeTsuTests.cs` (3 件)。`Japanese_AfterParticle_ReadsTsu` は変更前の Core で赤 (totemoatsui → とてもatすい)、変更後に緑。`English_TsuRule_Unchanged` は前後とも緑 (commitする・heatする・debutする・moatする・きょうはcommitする・きょうはmomentする・これをdeceitする・shutした・reflectされた・heats)。`KnownLoss_PluralRightAfterParticle` は受け入れた損 (きょうはへあts・きょうはもあて) の今の出力を固定する (変更前の Core では赤。変更前は きょうはheats・きょうはmoats)。
- [x] 回帰なし: Core のテスト全件通過 (1328 → 1331)、`tools/eval-compare.sh` (base は origin/main cec762c) の broken 0 / fixed 0、`--eval` の出力は前後で同一 (md5 一致)、比較ベンチ 649 件で変わったのは `kyouhatotemoatsui` 1 件だけ (外れ → 当たり)。
- [x] 日本語の大量掃引: readings.txt の 20,127 語 × 13 の前置き (無し・きょうも・とても・ここでも・きょうは・わたしが・これを・にほんに・ここで・きみと・きょうの・にほんへ・いぬや) = 261,651 入力で、変わったのは 103 件、すべて英字が残っていたものが かな だけになった (悪化 0)。`つ` を含む読み 1,583 語 × 14 の前置き = 22,162 入力では 149 件が直り、悪化 0。
- [x] 英語の掃引: 辞書の t で終わる英単語 2,629 語 × 7 の前置き × 5 の後ろ (suru・shita・s・sareta・site) = 92,015 入力で、変わったのは 78 件。うち 48 件は変更前も英単語が割れていた (`kyouhaheatsuru`: きょうはへatする → きょうはへあつる など。どちらも外れ)。**悪化は 30 件**: かな + 助詞のすぐ後ろに続けて打った複数形 `heats`・`moats`・`moots`・`toots`・`touts` + Enter (`kyouhaheats`: きょうはheats → きょうはへあts)。語の頭や空白の後ろの複数形は今までどおり。
- [x] 境界例の一括比較 `tools/boundary-sweep.sh` (full、base は origin/main cec762c): 684,117 入力で日本語の損失 0・英語の損失 0 (この道具の英語の入力の集合での数。上の英語の掃引の 30 件とは別の入力)・その他の変化 16。16 件はすべて英字の残りが かな だけになったもの (`hitootsu`: ひとおtす → ひとおつ、`kumootsu`: くもおtす → くもおつ など)。

## 得と損
- 得: 「今日も暑い」「とても暑い」「ここでも暑い」「それもお疲れさま」「君と美しい」のような、助詞の後ろの あつ・おつ・うつ・いつ・ぶつ で始まる日本語が英字に割れなくなる。
- 損: かな + 助詞のすぐ後ろに空白なしで打った、助詞の音で始まる 4 文字の英単語の複数形 (heats・moats・moots・toots・touts) が、Enter で割れる (上の 30 件。`kyouhaheats` → きょうはへあts、`kyouhatouts` → きょうはとうts、`kyouhamoats` → きょうはもあて、`kyouhamoots` → きょうはもおつ、`kyouhatoots` → きょうはとおつ)。テスト `KnownLoss_PluralRightAfterParticle` で今の出力を固定してある (変わったら気づけるように)。変更前も `heatsuru` の形はこの位置で割れていた。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
**未確認。今入っているアプリには、この修正は入っていない。**
- [ ] TextEdit: `kyoumoatsui` + Enter → 「今日も暑い」(英字が混ざらない)。
- [ ] TextEdit: `totemoatsui` + Enter → 「とても暑い」。先頭の `to` が英字に残るかは別の問題として記録する (非目標)。
- [ ] TextEdit: `commitsuru` + Enter → `commitする`、`kyouhacommitsuru` + Enter → `きょうはcommitする` (今までどおり)。

## 止める条件
- 日本語の掃引で悪化が出て、例外の条件を狭めても消えないとき。
- 英語の悪化が複数形以外 (`suru`・`shita` の形で、変更前は当たっていたもの) に出たとき。

## 人の判断
1. **上の損 (助詞のすぐ後ろの heats・moats などの複数形 30 件) を受け入れて入れるか。** おすすめは受け入れる: 日本語側は 261,651 入力で悪化 0・直る 103 件で、損の形 (日本語に空白なしで続けた英語の複数形 + Enter) は少ない。決めないと、この修正は main に入らない。

## 記録
- 2026-11-25 henrietta: 実装・テスト・掃引。ブランチ `fix/particle-atsu` (origin/main cec762c から)。掃引は使い捨ての打鍵の一覧 (コミットしない) を変更前後のビルドで打って比べた。
