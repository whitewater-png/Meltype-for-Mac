---
title: 「それも + て…」のような日本語が、英単語 remote に取られて割れる (soremoteishutsu が そremoteいしゅつ になる)
status: draft   # draft | approved | done。approved にするのは人
source: 要件 033 の掃引 (2026-11-25) で見つかった別の形の割れ。`soremoteishutsu`・`soremotetsudau`・`soremotekisetsu` など (jp_tsu の 26 件)
scope: core
---

# 039 「それも + て…」が英単語 remote に取られて割れる

## 背景 (誰が、何に困っているか)
- 観測 (Core のテストの打鍵、変更前 9ded846): `soremoteishutsu` → そremoteいしゅつ (期待: それも提出)、`soremotetsudau` → そremoteつだう (それも手伝う)、`soremotekisetsu` → そremoteきせつ。「それも手伝う」「それも提出」は普通に打つ文。同じ形は「これも・あれも・どれも」(`koremotetsudau` → こremoteつだう、`aremoteishutsu` → あremoteいしゅつ、`doremotetsudau` → どremoteつだう) と、入力の先頭の「彼も」(`karemoteishutsu`) にもあった。
- 1 文字ずつ伸ばした `showing`: `soremot` → それもt、`soremote` → そremote (ここで英語になる)、以降 `soremotei`・`soremoteishu`・`soremoteishutsu` までずっと そremote…。

## 今の動き (既に分かっていること)
- 033 とは別の規則。t + s の つ の判定 (`CompositionText.cs` の `ReadsAsJapaneseAfterParticle`) ではなく、区間分け (`CompositionDetector.cs` の `Segments` → `IsEnglishSpanCore`) の中の次の 1 行が原因。
  `if (lower.Length >= 4 && ReadableEnglish.Value.ContainsWord(lower) && !ContinuesJapaneseWord(lower, next) && …) return true;`
- `dictionaries/english-readable.txt` (ローマ字として最後まで読めても英語にする語。feature・remote・like …) の語は、前の文脈に関係なく英語になる。例外は 2 つだけ: 続く 1 音と合わせて読みの頭になる (main + do = まいんど)、日本語のすぐ後ろで英語にしない 3 語 (main・domain・data)。
- `so|re|mo|te` で、英語の区間が 2 つ目の単位から始まる (`re|mo|te` = remote)。そ + れ = それ という日本語の語の途中から、英語が始まっている。後ろの `ishutsu` が日本語でも `ContinuesJapaneseWord` は「remote の次の 1 音 (い) と合わせた れもてい」しか見ないので止められない。
- 測った広がり (変更前の Core): 日本語の掃引で英字が残るのは remote (と複数形) の形だけ。他の english-readable.txt の語 (feature・region・rename …) は、助詞を途中に含まないので同じ形にならない。
  - `jp_all.txt` (readings.txt の 20,127 語 × 13 の前置き = 261,651 入力): 英字が残った行 187 行・21 語 (index・karen・opera・omega・taiwan・mason・toronto・domino・nissan・sumac・heroku・gates・moment・init・tonic・initsuite・topic・yahoo・maya・re・shin)。**remote は 0 行** (前置きに「それ・これ・あれ・どれ・かれ」が無いため)。一覧は `lanes/L6-data/latin_words.tsv`。
  - 自作 `jp_gen.txt` (頭 sore・kore・are・dore・kare + 助詞 + readings.txt の語。english-readable の語が頭と助詞をまたぐ形に絞った 1,664 + 無作為 11,880、重複を除いて 13,538 入力): 英字が残った 1,613 行のうち、remote 系が 1,610。生データ `jp_gen.base`・`jp_gen_latin.tsv`。
  - `jp_tsu.txt` (つ を含む読み 1,583 語 × 14 の前置き = 22,162 入力): remote が 27 行 (すべて 前置き それも)。

## minimum の要件 (これだけは満たす)
1. 英語の区間が english-readable.txt の語で、その語の途中の単位が助詞で、前の単位から助詞の前まで (そ|れ) が**代名詞 (それ・これ・あれ・どれ・かれ・だれ)**、助詞の後ろ (て|いしゅつ) が 2 音以上の日本語の語の頭になるなら、その区間は英語にしない (`ReadableWordSplitsIntoJapanese`)。代名詞は入力の先頭・助詞の後ろ (きょうも + それも…) にあるときだけ。前が助詞でない かな (やまだ|れもて の だれ、つく|れもて の くれ) は対象にしない。**助詞の後ろの道 (R6b)**: 助詞の音の単位が語の一部のとき (な|に|か・と|か・お|と|こ・き|の|こ・ま|に|あ・は|だ・と|も|あれ・わ|かれ) は対象にしない。助詞の音の単位より前の かな の並び (入力の先頭・空白・記号まで、伸ばす音 - を含む。最大 16 単位) の、どの位置から始まる語でも、その並びの代名詞の頭の音 (そ・こ・あ・ど・か・だ) までの読み、または れ までの読みが readings.txt・brands.txt の読み (なにか・とか・おとこ・きのこ・まにあ・なんとか・はだ・ともあれ・わかれ) と同じなら、代名詞ではなく日本語の語 + remote として英語のままにする (`EndsInsideJapaneseWord`)。
2. 英語の remote は今までどおり: 語の頭 (`remote`・`remoteishutsu`・`remotes`)、空白の後ろ (`sore remotetsudau`)、助詞のすぐ後ろ (`kyouharemotesuru`・`sorenoremotetsudau`・`sorekaremote`)、日本語の後ろ (`kyouremoteishutsu`)、後ろが日本語として読めない (`kyouharemoteworkdesu`)、打ちかけの `soremote`、**日本語の語 + remote** (`nanikaremoteishutsu`・`nantokaremoteishutsu`・`otokoremoteishutsu`・`maniaremoteishutsu`・`hadaremoteishutsu`・`yamadaremoteishutsu`・`tsukuremoteishutsu`・`kaisharemoteishutsu`・`bokuremotetsudau`・`tomoaremoteishutsu`・`wakaremoteishutsu`、後ろが ni・ga・to でも)。
3. 新しい設定項目は作らない。辞書 (english-readable.txt) も変えない。

## 非目標 (今回はやらない)
- `nihonninitsuite` → にほんinitsuite (L7 の別の要件)。
- `soremote-buru` (それもテーブル) は そremoteーぶる のまま (`てーぶる` が readings.txt に無い。カタカナ語は読みの表に無い)。`soremotetsuzuki` (typo の つづき) も同じ。
- 代名詞以外の語の最後の 1 音 + れ (だれ・くれ・しゃれ …) を読みとして使うこと。最初の版 (76aebaa) はこれを使い、`yamadaremoteishutsu` → やまだれもていしゅつ の悪化を出した (査読 R6)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `src/Meltype.Core.Tests/RemoteInJapaneseTests.cs` (2 件)。`Japanese_WordParticleWord_IsNotRemote` は変更前の Core で赤 (soremoteishutsu → そremoteいしゅつ)、変更後に緑。`English_Remote_Unchanged` は前後とも緑。
- [x] 回帰なし: Core のテスト全件 1331 → 1333 (初版と同じ) 通過。`tools/eval-compare.sh` (base は origin/main 9ded846) の broken 0 / fixed 0。
- [x] 日本語の掃引 (変更前後の Core を同じ入力で打って比べた。生データ `lanes/L6-data/`): `jp_all.txt` 261,651 入力で変化 0。`jp_tsu.txt` 22,162 入力で 26 件が直り (英字が残らなくなった)、悪化 0 (初版・R6 と同じ 26 件。すべて 入力の先頭の それも)。`jp_gen.txt` 13,538 入力 (前置きは入力の先頭だけ) で 1,565 件が直り (sore・kore・are・dore・kare × 313)、悪化 0 (初版・R6 と同じ件数)。
- [x] 日本語の掃引・助詞の後ろの道 (R6b で追加。`jp_pron.txt`: 前置き 8 通り × 代名詞 6 語 × も + て で始まる語 313〜400 語 = 15,024 入力。前置きは 無し・きょうも・きょうは・とても・ここでも・きみと・にほんに・わたしが): 変更前に英字が残っていた入力が直った件数は、無し 1,830・きょうも/とても/ここでも/わたしが 各 1,525・きみと 1,220・きょうは 915 (きょうは + こ・だ・か は、はこ・はだ・はか という語があるので 対象外にして英字のまま)。にほんに は入力の綴りが にほんい になる (nn が要る) ので測定に使えない。R6b の版で直る件数は、R6 の版より減った (きょうは の こ・だ・か、きみと の こ など)。
- [x] 英語の掃引: `en_t.txt` (92,015 入力、要件 033 と同じ) で変化 0。`en_gen.txt` (english-readable.txt の 298 語 × 前置き 19 × 後ろ 13 = 70,756 入力) で変化 0。`en_pre.txt` (R6: 前の語の最後の 1 音 + れ が readings.txt の語になる 93 語 + yamada・tsuku・kaisha・boku など 16 語 = 109 前置き × 後ろ 12 (remoteishutsu・remotetsudau・remotekisetsu・remoteni・remotega・remoteto・remotewo・remotede・remotehaisuru・remotesuru・remoteishiki・remotenotame) = 1,308 入力) は、初版で悪化、R6 の版で 変化 0。
  **`en_pre2.txt` (R6b: readings.txt・brands.txt から機械で集めた、そ・こ・あ・ど・か・だ・れ で終わる語 1,094 語 × 後ろ 12 + en_pre = 14,400 入力。助詞の音を含むかに関わらず全部)**: R6 の版 (3ac3dc6) は なにか・なんとか・とか・おとこ・きのこ・まにあ・のだ・なのか・いとこ・たけのこ・ぼすにあ・どうにか・はだ など 546 件が悪化 (査読 R6b の 59 語と同じ型)。助詞の音を見る版 (ー を含めず) でも あーけーど などの - を含む語が漏れ、readings の読みの照合を romaji でなく単位のかなに変えて漏れが 0 になった。現行の変化は 30 件で、**すべて語そのものが代名詞の 1 音 (そ・こ・あ・ど・か・だ) の入力** (`so`・`ko`・`do`・`ka`・`da` + remote… の 5 語 × 後ろ 6 通り = `soremoteishutsu` と同じ入力。意図した直り) で、悪化は 0。
- [x] 境界例の一括比較 `tools/boundary-sweep.sh` (full、base は origin/main 9ded846): 684,117 入力で日本語の損失 0・英語の損失 0・その他の変化 0。

## 得と損
- 得: 「それも提出・それも手伝う・これも手伝う・あれも提出・どれも手伝う・だれも手伝う・彼も提出」のような、代名詞 + も + て… で始まる語が、remote に割れなくなる (jp_tsu で 26 件、自作 jp_gen で 1,565 件)。
- 損: 「悪化 0」は**測った範囲** (上の掃引と境界例) での話で、すべての入力ではない。理屈の上の損は、代名詞と同じ綴りで終わる形 (そ・こ・あ・ど・か・だ + remote。例 `soremoteishutsu` = そ + remote + いしゅつ) を、空白なしで「そ + remote」の意味で打つ場合だけ。`soreremote` (それ + remote) とは綴りが違い、「そremote」と打つ人はほぼいないと見ている (推測)。日本語として読める (それ + も + ていしゅつ) ほうを優先した。助詞のすぐ後ろ・空白の後ろ・語の頭・助詞の無い形・日本語の語 + remote は影響しない。
- 残る外れ (安全側): `koremotemoteru`・`soremotemoteru` は こremoteもてる・そremoteもてる のまま、`daremotenai`・`doremotenai`・`karemotenai` も だremoteない・どremoteない・かremoteない のまま (どちらも原因は未調査)。どちらも英字が残る側に倒れる (悪化ではない)。
- 打ち途中の見え方が揺れる (実測、`--repro`): `soremote` は そremote、`soremotei` から それもてい (後ろが 2 音以上の日本語の頭になった時点) に変わる。`soremoteni`・`soremotega` は それもてに・それもてが と日本語に変わるが、`soremotewo` (を は readings.txt に頭が無い) と `soremotesuru` は そremoteを・そremoteする のまま。後ろの形によって、日本語にならない打ち方も残る。
- 初版からの変更の損: 代名詞 + も + て… の対象を、readings.txt の読みになる すべての語 (くれ・しゃれ …) から代名詞 6 語に狭めた。測った範囲で、直る件数は減っていない (jp_tsu 26・jp_gen 1,565 のまま)。代名詞以外の れ で終わる語 + も + て… (`yamadaremotetsudau` = やまだ + れも + てつだう ではなく やまだ + remote + つだう) は remote のまま。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] `soremoteishutsu` + Enter → 「それも提出」(候補は 提出 を含む)。`soremotetsudau` → 「それも手伝う」。
- [ ] `remote` + Enter → `remote`。`kyouharemotesuru` → 今日はremoteする。`sore remote` (空白あり) は remote のまま。

## 止める条件
- 日本語・英語の掃引で、条件を狭めても消えない悪化が出たとき (今回は出なかった)。

## 人の判断
- なし (悪化 0。設定・公開は伴わない)。

## 記録
- 2026-11-25 henrietta (L6): 査読 R6b の指摘 1 (中: 語の中の と・の・に・は・で・も・わ・を の音が 助詞 として通り、なにかremote… が なにかれもて… になる) を、「助詞の後ろ」の道を残したまま 助詞の音の前の かなの並び全体で語の一部かを見る形に直した (道をやめない理由: きょうも・とても・ここでも・わたしが + それも… が直る件数が 1,525〜1,830 (jp_pron) あり、判定は readings.txt の読みの照合 1 つで済むため。複雑にはなったが、漏れは en_pre2 の 14,400 入力で 0)。指摘 2 は残る外れの欄に書いた。
- 2026-11-25 henrietta (L6): 査読 R6 の指摘 1 (中: 日本語の語 + remote が日本語に取られる) を直し、指摘 2・3 (低: 打ち途中の見え方・損の書き方) を文書に反映。初版 76aebaa は前の語の最後の 1 音 + れ を読みとして使っていたため悪化した (`yamadaremoteishutsu`)。ブランチ `fix/remote-in-japanese` (origin/main 9ded846 から)。掃引は使い捨ての打鍵の一覧 (コミットしない。入力・出力・道具は `lanes/L6-data/`)。
- 本家 main (ビルド済み up、`--repro`) との比較 (7 件だけ打った): `soremoteishutsu`・`soremotetsudau`・`soremotekisetsu`・`koremotetsudau`・`karemoteishutsu` は本家 main では日本語 (それもていしゅつ …) で、この修正後と同じ。`kyouharemotesuru` は きょうはremoteする で同じ。`remoteishutsu` は本家 main では reもていしゅつ (この修正後と変更前は remoteいしゅつ) で、ここだけ本家と違う (本件の範囲外。語の頭の remote は英語のままにする)。
