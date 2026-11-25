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
1. 英語の区間が english-readable.txt の語で、その語の途中の単位が助詞で、前の単位から助詞の前まで (そ|れ) が日本語の語 (readings.txt・brands.txt の読み)、助詞の後ろ (て|いしゅつ) が 2 音以上の日本語の語の頭になるなら、その区間は英語にしない (`ReadableWordSplitsIntoJapanese`)。
2. 英語の remote は今までどおり: 語の頭 (`remote`・`remoteishutsu`・`remotes`)、空白の後ろ (`sore remotetsudau`)、助詞のすぐ後ろ (`kyouharemotesuru`・`sorenoremotetsudau`・`sorekaremote`)、日本語の後ろ (`kyouremoteishutsu`)、後ろが日本語として読めない (`kyouharemoteworkdesu`)、打ちかけの `soremote`。
3. 新しい設定項目は作らない。辞書 (english-readable.txt) も変えない。

## 非目標 (今回はやらない)
- `nihonninitsuite` → にほんinitsuite (L7 の別の要件)。
- `soremote-buru` (それもテーブル) は そremoteーぶる のまま (`てーぶる` が readings.txt に無い。カタカナ語は読みの表に無い)。`soremotetsuzuki` (typo の つづき) も同じ。
- 助詞のすぐ後ろ (きょうは + remote) と、か・や の後ろ (それか + remote。入力の先頭の か は語の頭として見る = `karemote…` は 彼も) の remote は英語のままにする。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `src/Meltype.Core.Tests/RemoteInJapaneseTests.cs` (2 件)。`Japanese_WordParticleWord_IsNotRemote` は変更前の Core で赤 (soremoteishutsu → そremoteいしゅつ)、変更後に緑。`English_Remote_Unchanged` は前後とも緑。
- [x] 回帰なし: Core のテスト全件 1331 → 1333 通過。`tools/eval-compare.sh` (base は origin/main 9ded846) の broken 0 / fixed 0。
- [x] 日本語の掃引 (変更前後の Core を同じ入力で打って比べた。生データ `lanes/L6-data/`): `jp_all.txt` 261,651 入力で変化 0。`jp_tsu.txt` 22,162 入力で 26 件が直り (英字が残らなくなった)、悪化 0 (残る 1 件は上の `soremotetsuzuki`)。`jp_gen.txt` 13,538 入力で 1,565 件が直り (sore・kore・are・dore・kare × 313)、悪化 0。
- [x] 英語の掃引: `en_t.txt` (t で終わる英単語 2,629 語 × 7 の前置き × 5 の後ろ = 92,015 入力、要件 033 と同じ) で変化 0。`en_gen.txt` (english-readable.txt の 298 語 (複数形と remoteness・remotely を含む) × 前置き 19 × 後ろ 13 = 70,756 入力、前置きに きょう・きょうは・きょうを・それ・これ・あれ・どれ・かれ・それの・それで・きみと …) で変化 0。
- [x] 境界例の一括比較 `tools/boundary-sweep.sh` (full、base は origin/main 9ded846): 684,117 入力で日本語の損失 0・英語の損失 0・その他の変化 0。

## 得と損
- 得: 「それも提出・それも手伝う・これも手伝う・あれも提出・どれも手伝う・彼も提出」のような、代名詞 + も + て で始まる語が、remote に割れなくなる (jp_tsu で 26 件、自作で 1,565 件)。
- 損 (測った範囲では 0 件): 英語側の掃引 (162,771 入力) と境界例 684,117 入力で、変わった出力は無い。理屈の上の損は、`これもてつだう` と読める入力を `これremoteつだう` (これ + remote + つだう) の意味で打つ場合だけ。日本語として読める (これ + も + てつだう) ほうを優先した。助詞のすぐ後ろ・空白の後ろ・語の頭・助詞の無い形は影響しない。
- 打ち途中の見え方: `soremote` までは そremote、`soremotei` (て + い の 2 音) から それもてい に変わる (1 音ぶん途中で見え方が変わる)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] `soremoteishutsu` + Enter → 「それも提出」(候補は 提出 を含む)。`soremotetsudau` → 「それも手伝う」。
- [ ] `remote` + Enter → `remote`。`kyouharemotesuru` → 今日はremoteする。`sore remote` (空白あり) は remote のまま。

## 止める条件
- 日本語・英語の掃引で、条件を狭めても消えない悪化が出たとき (今回は出なかった)。

## 人の判断
- なし (悪化 0。設定・公開は伴わない)。

## 記録
- 2026-11-25 henrietta (L6): ブランチ `fix/remote-in-japanese` (origin/main 9ded846 から)。掃引は使い捨ての打鍵の一覧 (コミットしない。入力・出力・道具は `lanes/L6-data/`)。
- 本家 main (ビルド済み up、`--repro`) との比較 (7 件だけ打った): `soremoteishutsu`・`soremotetsudau`・`soremotekisetsu`・`koremotetsudau`・`karemoteishutsu` は本家 main では日本語 (それもていしゅつ …) で、この修正後と同じ。`kyouharemotesuru` は きょうはremoteする で同じ。`remoteishutsu` は本家 main では reもていしゅつ (この修正後と変更前は remoteいしゅつ) で、ここだけ本家と違う (本件の範囲外。語の頭の remote は英語のままにする)。
