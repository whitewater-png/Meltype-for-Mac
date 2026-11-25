---
title: 「それも + て…」のような日本語が、英単語 remote に取られて割れる (soremoteishutsu が そremoteいしゅつ になる)
status: approved   # draft | approved | done。approved にするのは人
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
  - `jp_all.txt` (readings.txt の 20,127 語 × 13 の前置き = 261,651 入力): 英字が残った行 187 行・21 語 (index・karen・opera・omega・taiwan・mason・toronto・domino・nissan・sumac・heroku・gates・moment・init・tonic・initsuite・topic・yahoo・maya・re・shin)。**remote は 0 行** (前置きに「それ・これ・あれ・どれ・かれ」が無いため)。一覧は リポジトリ外の作業フォルダー。
  - 自作 `jp_gen.txt` (頭 sore・kore・are・dore・kare + 助詞 + readings.txt の語。english-readable の語が頭と助詞をまたぐ形に絞った 1,664 + 無作為 11,880、重複を除いて 13,538 入力): 英字が残った 1,613 行のうち、remote 系が 1,610。生データ `jp_gen.base`・`jp_gen_latin.tsv`。
  - `jp_tsu.txt` (つ を含む読み 1,583 語 × 14 の前置き = 22,162 入力): remote が 27 行 (すべて 前置き それも)。

## minimum の要件 (これだけは満たす)
1. 英語の区間が english-readable.txt の語 (remote) で、先頭の れ の前が代名詞の頭の音 (そ・こ・あ・ど・か・だ)、その語の途中の単位が助詞 (も)、助詞の後ろ (て|いしゅつ) が 2 音以上の日本語の語の頭になるなら、その区間は英語にしない (`ReadableWordSplitsIntoJapanese`)。**代名詞 (それ・これ・あれ・どれ・かれ・だれ) は、入力の先頭・空白の後ろ・記号の後ろ (かな の並びの途中でない位置) にあるときだけ。** かな の後ろ (助詞の後ろ・語の後ろ) は対象にしない (経緯は「記録」の R6・R6b・R6c)。
2. 英語の remote は今までどおり: 語の頭 (`remote`・`remoteishutsu`・`remotes`)、空白の後ろ (`sore remotetsudau`)、助詞のすぐ後ろ (`kyouharemotesuru`・`sorenoremotetsudau`・`sorekaremote`)、日本語の後ろ (`kyouremoteishutsu`)、**かな の後ろの代名詞の形 (`kyouhasoremoteishutsu`・`kyoumokoremotetsudau`。直らない形)**、後ろが日本語として読めない (`kyouharemoteworkdesu`)、打ちかけの `soremote`、**日本語の語・人名 + remote** (`momokoremoteishutsu`・`tomokaremotetsudau`・`ikunokaremoteni`・`kanokoremotega`・`todaremoteishutsu`・`nanikaremoteishutsu`・`nantokaremoteishutsu`・`otokoremoteishutsu`・`maniaremoteishutsu`・`hadaremoteishutsu`・`yamadaremoteishutsu`・`tsukuremoteishutsu`・`kaisharemoteishutsu`・`bokuremotetsudau`・`tomoaremoteishutsu`・`wakaremoteishutsu`、後ろが ni・ga・to でも)。
3. 新しい設定項目は作らない。辞書 (english-readable.txt) も変えない。

## 非目標 (今回はやらない)
- `nihonninitsuite` → にほんinitsuite (L7 の別の要件)。
- `soremote-buru` (それもテーブル) は そremoteーぶる のまま (`てーぶる` が readings.txt に無い。カタカナ語は読みの表に無い)。`soremotetsuzuki` (typo の つづき) も同じ。
- 代名詞以外の語の最後の 1 音 + れ (だれ・くれ・しゃれ …) を読みとして使うこと。最初の版 (76aebaa) はこれを使い、`yamadaremoteishutsu` → やまだれもていしゅつ の悪化を出した (査読 R6)。
- 辞書 (readings.txt・brands.txt) の照合で「語の中の助詞の音」を見分けること (R6b の版 0a2c804)。辞書に無い人名・かな語 (ももこ・ともか・ののか・かのこ) で必ず漏れる (R6c)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `src/Meltype.Core.Tests/RemoteInJapaneseTests.cs` (3 件)。`Japanese_WordParticleWord_IsNotRemote` は変更前の Core で赤 (soremoteishutsu → そremoteいしゅつ)、変更後に緑。`English_Remote_Unchanged` は変更前 (9ded846) で緑、R6b の版 (0a2c804) で赤 (`momokoremoteishutsu` → ももこれもていしゅつ)、現行で緑。`KnownLimit_PronounAfterKanaStaysRemote` は 直らない と決めた形 (きょうはそremoteいしゅつ・きょうもそremoteいしゅつ・きょうもこremoteつだう) の今の出力を固定する (0a2c804 で赤 = きょうはそれもていしゅつ、現行で緑)。
- [x] 回帰なし: Core のテスト全件 1331 → 1334 通過。`tools/eval-compare.sh` (base は origin/main 9ded846) の broken 0 / fixed 0。
- [x] 日本語の掃引 (変更前後の Core を同じ入力で打って比べた。生データ リポジトリ外の作業フォルダー): `jp_all.txt` 261,651 入力で変化 0。`jp_tsu.txt` 22,162 入力で 26 件が直り (英字が残らなくなった)、悪化 0 (初版・R6 と同じ 26 件。すべて 入力の先頭の それも)。`jp_gen.txt` 13,538 入力 (前置きは入力の先頭だけ) で 1,565 件が直り (sore・kore・are・dore・kare × 313)、悪化 0 (初版・R6 と同じ件数)。
- [x] 日本語の掃引・かな の後ろの代名詞 (`jp_pron.txt`: 前置き 8 通り (無し・きょうも・きょうは・とても・ここでも・きみと・にほんに・わたしが) × 代名詞 6 語 × も + て で始まる語 313〜400 語 = 15,024 入力): 変更前から変わった入力は、R6b の版 (助詞の後ろの道あり) で 10,065 件、**現行で 1,830 件** (前置き 無し の 6 語 × 約 305 語だけ)。減った 8,235 件は、きょうも・きょうは・とても・ここでも・きみと・わたしが + 代名詞 + も + て… の形で、remote のまま (直らない) になった。にほんに は入力の綴りが にほんい になる (nn が要る) ので測定に使えない。
- [x] 英語の掃引: `en_t.txt` (92,015 入力、要件 033 と同じ) で変化 0。`en_gen.txt` (english-readable.txt の 298 語 × 前置き 19 × 後ろ 13 = 70,756 入力) で変化 0。`en_pre.txt` (R6: 前の語の最後の 1 音 + れ が readings.txt の語になる 93 語 + yamada・tsuku・kaisha・boku など 16 語 = 109 前置き × 後ろ 12 (remoteishutsu・remotetsudau・remotekisetsu・remoteni・remotega・remoteto・remotewo・remotede・remotehaisuru・remotesuru・remoteishiki・remotenotame) = 1,308 入力) は、初版で悪化、R6 の版以降で 変化 0。
  `en_pre2.txt` (R6b: readings.txt・brands.txt から機械で集めた、そ・こ・あ・ど・か・だ・れ で終わる語 1,094 語 × 後ろ 12 + en_pre = 14,400 入力): 変更前から変わった入力は、R6 の版で 546 件 (悪化。なにか・とか・おとこ・まにあ・はだ など)、R6b の版 (辞書で塞いだ版) で 30 件、現行で 30 件。この 30 件は すべて語そのものが代名詞の 1 音 (`so`・`ko`・`do`・`ka`・`da` + remote… の 5 語 × 後ろ 6 通り = `soremoteishutsu` と同じ入力で、入力の先頭にある。意図した直り) で、悪化は 0。
  査読者の入力 (リポジトリ外の作業フォルダーにコピー。R6b: `in.txt` 33,427 行・`in2.txt` 639 行、R6c: `in.txt` 1,726 行・`in3.txt` 6,527・`in4.txt` 415・`in5.txt` 6・`in6.txt` 34,066・`in7.txt` 74): R6c の in・in3・in4・in5・in7 と R6b の in2 は **変化 0** (R6b の版では ももこ・ともか・ののか・いくのか・ひのか・かのこ などで漏れていた入力。現行は変更前と同じ出力)。R6b の `in.txt` と R6c の `in6.txt` は同じ入力で 727 件が変わる: 601 件は英字が残らなくなる直り、126 件は `remotess` のような打ちかけ (aremotess → あれもてっs) で、悪化 (新しく英字が出た入力) は 0。変わるのは すべて 代名詞が入力の先頭・空白・記号 (" ( # 、。「 ,) の後ろ・数字の後ろ (`1aremotega` → 1あれもてが) にある形。
- [x] 速さ (R6c の指摘「300 文字で 1.9 秒 vs 変更前 1.1 秒」): 区間の先頭の単位が re で、その前の単位が代名詞の頭の音 6 つのどれか、という安い条件で先に返し、文字列は english-readable.txt の照合の直前まで作らない。使い捨ての打鍵 (300 文字 × 12 行を 1 文字ずつ打つ。リポジトリ外の作業フォルダー・`time_remote.txt`。後者は remote 系の語を 10% 混ぜた) を、変更前 (9ded846) と現行で 3 回ずつ測った (プロセス起動を含む壁時計): remote を含まない入力は 10.57・10.30・10.55 秒 → 10.82・10.78・10.99 秒 (平均 +3.7%)、remote を含む入力は 10.09・9.71・9.89 秒 → 9.74・9.67・9.95 秒 (平均 -1.1%)。どちらも ±10% に収まる。出力は変更前と同じ。
- [x] 境界例の一括比較 `tools/boundary-sweep.sh` (full、base は origin/main 9ded846): 684,117 入力で日本語の損失 0・英語の損失 0・その他の変化 0。

## 得と損
- 得: 「それも提出・それも手伝う・これも手伝う・あれも提出・どれも手伝う・だれも手伝う・彼も提出」のような、代名詞 + も + て… で始まる語が、remote に割れなくなる (jp_tsu で 26 件、自作 jp_gen で 1,565 件)。
- 損: 「悪化 0」「漏れ 0」は**測った範囲** (上の掃引と、査読者の入力と境界例) での話で、すべての入力ではない。理屈の上の損は、代名詞と同じ綴りで終わる形 (そ・こ・あ・ど・か・だ + remote。例 `soremoteishutsu` = そ + remote + いしゅつ) を、入力の先頭・空白・記号の後ろで空白なしに「そ + remote」の意味で打つ場合だけ。`soreremote` (それ + remote) とは綴りが違い、「そremote」と打つ人はほぼいないと見ている (推測)。日本語として読める (それ + も + ていしゅつ) ほうを優先した。
- 残る外れ (直らない形。安全側): **かな の後ろの代名詞** (`kyouhasoremoteishutsu` → きょうはそremoteいしゅつ、`kyoumosoremoteishutsu` → きょうもそremoteいしゅつ、`kyoumokoremotetsudau` → きょうもこremoteつだう)。きょうも・とても・ここでも・わたしが + それも… は直らない (jp_pron で 10,065 → 1,830 件)。辞書に無い人名・かな語 (ももこ・ともか) と区別できないため、テスト `KnownLimit_PronounAfterKanaStaysRemote` で今の出力を固定した。また `koremotemoteru`・`soremotemoteru` は こremoteもてる・そremoteもてる、`daremotenai`・`doremotenai`・`karemotenai` は だremoteない・どremoteない・かremoteない のまま (どちらも原因は未調査。英字が残る側に倒れる)。
- 打ち途中の見え方が揺れる (実測、`--repro`): `soremote` は そremote、`soremotei` から それもてい (後ろが 2 音以上の日本語の頭になった時点) に変わる。`soremoteni`・`soremotega` は それもてに・それもてが と日本語に変わるが、`soremotewo` (を は readings.txt に頭が無い) と `soremotesuru` は そremoteを・そremoteする のまま。後ろの形によって、日本語にならない打ち方も残る。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] `soremoteishutsu` + Enter → 「それも提出」(候補は 提出 を含む)。`soremotetsudau` → 「それも手伝う」。
- [ ] `remote` + Enter → `remote`。`kyouharemotesuru` → 今日はremoteする。`sore remote` (空白あり) は remote のまま。

## 止める条件
- 日本語・英語の掃引で、条件を狭めても消えない悪化が出たとき (今回は出なかった)。

## 人の判断
- **決定 (2026-11-25 リョウさん「おすすめで対応して」): 直す範囲は、代名詞が入力の先頭・空白の後ろ・記号の後ろにあるときだけ (助詞の後ろ「きょうもそれもて…」は直さない)。**
- なし (悪化 0。設定・公開は伴わない)。

## 記録
- 2026-11-25 リョウさんの実機 (要件 034〜040 をまとめた版 int/accuracy-1125 を入れて確認): `soremoteishutsu` → それも提出。`commitsuru` → commitする も今までどおり。
- 2026-11-25 henrietta (L6): 経緯 (3 回の査読で、日本語の語 + remote が日本語に取られる悪化が 3 回出た。直し方を変えるたびに漏れの形が変わった)。
  - R6 (76aebaa → 3ac3dc6): 初版は 直前の 1 音 + れ を読みにしていて `yamadaremoteishutsu` → やまだれもていしゅつ。対象を代名詞 6 語に狭めた。
  - R6b (3ac3dc6 → 0a2c804): 代名詞の前が助詞 (も・と・に…) のとき、助詞の音が語の中の音 (なにか・おとこ・まにあ・はだ) でも通る (546 件)。助詞の音の前の かな の並びを readings.txt・brands.txt の読みで照合して塞いだ (EndsInsideJapaneseWord)。
  - R6c (0a2c804 → 現行): 辞書に無い人名・かな語 (ももこ・ともか・ともこ・ののか・ももか・とだ・いくのか・ひのか・かのこ) で漏れが残る (査読者の入力 1,726 + 415 件)。辞書で塞ぐ方法は、辞書に無い語で必ず漏れるので打ち切り、「助詞の後ろの道」をやめて 代名詞が 入力の先頭・空白の後ろ・記号の後ろ にあるときだけにした (親の決定。リョウさんの方針「悪化するのは良くない」に合わせ、直る件数が減っても安全側)。EndsInsideJapaneseWord と readings の照合 (IsJapaneseReading) は消した。直る件数の減り方は jp_pron 10,065 → 1,830。
  - 遅さの指摘 (R6c): 判定を安い条件で先に返すようにした (上の「速さ」)。
- ブランチ `fix/remote-in-japanese` (origin/main 9ded846 から)。掃引は使い捨ての打鍵の一覧 (コミットしない。入力・出力・道具は リポジトリ外の作業フォルダー)。
- 本家 main (ビルド済み up、`--repro`) との比較 (7 件だけ打った): `soremoteishutsu`・`soremotetsudau`・`soremotekisetsu`・`koremotetsudau`・`karemoteishutsu` は本家 main では日本語 (それもていしゅつ …) で、この修正後と同じ。`kyouharemotesuru` は きょうはremoteする で同じ。`remoteishutsu` は本家 main では reもていしゅつ (この修正後と変更前は remoteいしゅつ) で、ここだけ本家と違う (本件の範囲外。語の頭の remote は英語のままにする)。
