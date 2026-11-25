---
title: 「い + について」が英字に割れる (にほんいについて が にほんinitsuite になる)
status: approved   # draft | approved | done。approved にするのは人
source: 要件 033 の掃引で見つかった別の形 (2026-11-25、レーン L7)
scope: core
---

# 040 「い + について」が英字に割れる (nihonninitsuite → にほんinitsuite)

## 背景 (誰が、何に困っているか)
- 観測 (Core の打鍵): `nihonninitsuite` → にほんinitsuite、`nihonninitsukimashite` → にほんinitすきまして、`nihonninitsumeru` → にほんinitすめる。本家 main (ビルド済み) では にほんいについて・にほんいにつきまして・にほんいにつめる。
- **最初の推測 (「ん と に の境の nn の読み方」) は違った。** `nihonn` + `i` + `ni` + `tsuite` の `nn` は ん で正しく、`ん + い` の い (単位 `i`) のあとの `i|ni|t` が英単語 init になっていた。「日本について」は `nihonnnitsuite` (nn + ni) で、これは変更前から にほんについて と出る。`nihonnitsuite` は にほんいついて で、どの IME でも同じ。
- 実際に外れる範囲: 単位が `i` (い) で終わる語 + について・につき・につめる (母音の後ろの い: せつめい・もんだい・みらい・かい・ぜい・おもい・ごえい…)。readings.txt の 20,177 語の読み (ローマ字) × `nitsuite`・`nitsumeru`・`natsuite`・`notsuite` = 80,708 入力で、変更前に英字が残ったのは 4,020 件。うち 3,780 件がこの形 (語の読みが い で終わる 1,890 語 × `nitsuite`・`nitsumeru`)。残りは英単語そのもの (opera・toronto・omega など) と、単位が `u` の unit (ああいう + について。98 語×2)、語の頭の init。
- 「それについて」「このけんについて」「ぶんしょうについて」「これについて」はもともと当たる (単位が `ni` だけのため)。

## 今の動き (既に分かっていること)
- 関係する場所: `src/Meltype.Core/Composition/CompositionText.cs` の `ReadsAsJapaneseAfterParticle` (要件 033 で足した)。英単語の最後の t の次に s を打つと `EndsWithEnglishWordFromUnit(..., tsuFollows: true)` が英単語 (i|ni|t = init、4 文字以上の知っている語) を見つけ、t と s を つ にしない。`ReadsAsJapaneseWithTsu` の日本語の例外は、英単語の頭から読んだ「いにつ」(語でも頭でもない) とそれより前からの読みしか見ず、に + つ (につ…) を見ない。`--repro` で 1 文字ずつ: `nihonni` → にほんい、`nihonnit` → にほんいt、`nihonninit` → にほんinit (n・i・t で init が確定)。
- 本家 main は にほんいについて (ラティス化 #1185 で英語の決まりごと消えている。要件 033 と同じ。Mac 版では導入しない)。

## minimum の要件 (これだけは満たす)
1. `ReadsAsJapaneseAfterParticle` に 1 つ足す: 英単語が `i` (い) + `ni` (に) + 最後の t の 3 単位で、その前の単位が かな (を 以外) のとき、`につ` が日本語の語の頭なら t と s を つ にまとめる。
2. 語の頭の init (`init`・`initsuite`・`npminit`)、空白の後ろ、`kyouhainitshita` (tsu が続かない形)、`korewoinitsuru`、unit は今までどおり。
3. 新しい設定項目は作らない。

## 非目標 (今回はやらない)
- `u|ni|t` (unit。ああいう + について 98 語) は直さない。きょう|について (o・u の段の後ろの u) は `IsLongVowelU` で既に当たっており、残りの形 (い|う + について) は稀なのに、直すと `kyouhaunitsuru` (きょうはunitする) などの英語の損が増えるため。
- 語の頭の `initsuite` (→ いについて) は、本家と違うが、init の入力を守るため直さない。
- 英単語そのものの例 (opera・toronto + について) は別の規則。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト: `src/Meltype.Core.Tests/NiTsuiteTests.cs` (3 件)。`Japanese_VowelNiTsu_ReadsKana` は変更前の Core で赤 (にほんinitsuite など)、`KnownLoss_InitAfterKana` も変更前で赤、`English_Init_Unchanged` は前後とも緑。
- [x] 回帰なし: Core のテスト全件 1331 → 1334 通過 (新しい 3 件)、`tools/eval-compare.sh` の broken 0 / fixed 0。
- [x] 日本語の大量掃引: jp_all.txt (readings.txt の 20,127 語 × 13 の前置き = 261,651 入力) で変わったのは `nihonninitsuite` 系の 3 件 (英字が かな だけになった。悪化 0)。常用語 29 語 × 7 の語尾 (203 入力) で 25 件が直り (もんだい・せつめい・あい・かい・みらい + について・につき・につめる・につきまして・につつ)、悪化 0。20,177 語 × 4 の語尾 (80,708 入力) で 3,780 件が直り (英字 → かな だけ)、悪化 0。
- [x] 英語の掃引 (en_t.txt の 92,015 入力): 変わったのは 5 件、すべて悪化 (下の損。助詞 + init の形だけを測った数で、損の範囲全体ではない)。
- [x] 境界例の一括比較 `tools/boundary-sweep.sh` (full、base は origin/main 9ded846): 684,117 入力で日本語の損失 0・英語の損失 0・その他の変化 19。19 件はすべて、英字の残りが かな だけになったもの (`kanaeruinitsukau`: かなえるinitすかう → かなえるいにつかう、`shuuryouinitsukau`: しゅうりょうinitすかう → しゅうりょういにつかう など。い + に使う)。

## 得と損
- 得: 「〜い + について・につき・につめる」(せつめいについて・もんだいについて・みらいについて・おもいについて・ぜいについて・にほんいについて) が英字に割れない。い で終わる語は日本語に非常に多い (readings.txt の 1,890 語)。
- 損: **かなで終わる語に空白なしで続けた `init` / `reinit` + `tsu…` の形全般** (助詞に限らない。条件は「直前がかな (を 以外) + i + ni + t」で、助詞かどうかは見ていない)。後ろが す・する・すると・すれば・すいて・すか・すめる のとき、`つ` にまとまる。例: `kyouhainitsuru` → きょうはinitする が きょうはいにつる、`nihonninitsuru` → にほんいにつる (変更前 にほんいinitする)、`kyouhareinitsuru` → きょうはれいにつる、`reinitsuru` → れいにつる (変更前 れinitする)。数: 査読 (R7) の 31,372 入力 (31 前置き × 44 語 × 23 後ろ) で変わったのは 354 件、すべて init / reinit + tsu… で、前置きは 59 通り。自分の掃引 (en_t.txt) では 5 件 (助詞 + init の形だけ)。`shita`・`sareta`・`shite`・`site` の後ろ、空白あり、大文字 (`Init`)、`initialize`、-it で終わる他の語、`korewoinitsuru` (を の後ろ)、語の頭の `init` は変わらない。t と s を打つ時点では後ろが `uru` か `uite` か見えないので区別できない (033 と同じ限界)。テスト `KnownLoss_InitAfterKana` で今の出力を固定してある (望ましい出力ではない)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] `nihonninitsuite` + Enter → 「にほんいについて」(にほんinitsuite にならない)。
- [ ] `setsumeinitsuite` + Enter → 「せつめいについて」。
- [ ] `commitsuru` + Enter → `commitする` (今までどおり)。`init` + Enter → `init`。

## 止める条件
- 日本語の掃引で悪化が出て、条件を狭めても消えないとき。
- 英語の悪化が `<かな>init / reinit + tsu…` 以外 (`initshita`・`git init`・`npm init` など) に出たとき。

## 人の判断
- **決定 (2026-11-25 リョウさん「おすすめで対応して」): 損 (かなで終わる語に空白なしで続けた init / reinit + tsu… の形。31,372 入力中 354 件) は受け入れる。u|ni|t (unit) は今回は直さない。**
1. 上の損 (かなで終わる語 + init / reinit + tsu… の形全般、354 件規模) を受け入れるか。
2. `u|ni|t` (unit) まで直すか (非目標。直すと英語の損が 6 件増える)。

## 記録
- 2026-11-25 リョウさんの実機 (要件 034〜040 をまとめた版 int/accuracy-1125 を入れて確認): `setsumeinitsuite` → 説明について。`commitsuru` → commitする も今までどおり。
- 2026-11-25 henrietta (L7): 実装・テスト・掃引。ブランチ `fix/ni-tsuite` (origin/main 9ded846 から)。掃引は使い捨ての打鍵の一覧 (コミットしない) を変更前後のビルドで打って比べた。生データはリポジトリ外の作業フォルダーにある。
