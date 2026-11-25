---
title: 本家では直っていて Mac 版だけ崩れる入力の残りを、1 件ずつ悪化を測って、悪化 0 のものだけ直す
status: draft   # draft | approved | done。approved にするのは人
source: 派生フォーク比較ベンチ (2026-11-22 計測の xlsx、649 件) の「本家では直っていて fork だけ外れる 14 件」のうち、要件 033 (totemoatsui) で直した 1 件を除く 13 行 / リョウさんの方針「悪化するのは良くない」
scope: core
---

# 038 本家では直っていて Mac 版だけ崩れる入力の残り

## 背景 (誰が、何に困っているか)
- 比較ベンチ (649 件、Mac 版の判定と本家 main `23f668c6` の判定を同じテスト用キーボードで比べたもの) で、本家では当たり Mac 版では外れる行が 14 件あった。`totemoatsui` は要件 033 で直した。残り 13 行がこの要件の対象。
- 本家が直した方法は、**どの行も本家 PR #1185 (日英の境界判定のラティス化、`be44eddf`、2026-10-30 に Mac 版では導入しないと決めたもの)** の副産物 (本家の判定の出力に `+1` `+2.5` のような区間ごとの点が出る。`game`・`cache`・`code` は本家の `english.txt` に昔からあるが、ローマ字として読める語なので、本家もラティスの点で英語に決めている)。ラティス以外の小さなコミットで直った行は無かった (`git grep` で本家の辞書に `oss`・`app` は無い)。
- よって Mac 版では、行ごとに「辞書に 1 語」「規則に条件 1 つ」の形で直せるかを測った。日本語の悪化が 1 件でも出たものは採用しない (2026-10-30 の決定と同じ)。

## 今の動き (既に分かっていること)
- 過去の決定 (読んだもの): `docs/UPSTREAM_PR_TRACKER.md` 2026-10-30 の行 (#1185 は入れない、B 群 26 件は見送り)、`docs/UPSTREAM_1185_CLASSIFICATION_2026-10-30.tsv`、`docs/IMPROVEMENT_PLAN.md` の W18-1 (shat・oss・app は打ち間違いの補正を壊すので足さない。image・garage・tone は悪化あり)、`docs/W12_SHORTWORDS_2026-10-24.md` (me/do/to の短い語。「広い一覧は日本語対照 63 件を壊した」)、`docs/KNOWN_LIMITS.md` K79 (`cd` などは一般アプリでは英語と判定しない)。
- 測り方: 変更前 (origin/main `9ded846`) と変更後の Core を、同じ入力の一覧で打って出力を比べた (使い捨ての `[Test]`。コミットしていない)。入力は `jp_all.txt` (日本語 261,651 行)、`en_t.txt` (英語 + 日本語の語尾 92,015 行)、ベンチ 649 行、語ごとに作った近所の入力 (その語を含む `jp_all` の行 + 日本語の行を前後に置いた約 7,500 入力)。生データはリポジトリ外の作業フォルダー。

## 13 行の結果

fix = その行を含め、変更で直った (Mac 版の出力が本家と同じになった) 入力の数。悪化 = 変更前は日本語として正しく、変更後に崩れた入力の数。

| 入力 | Mac (今まで) | 本家 | 原因 (規則・辞書) | 試した直し方 | fix | 悪化 | 採否 | 過去の決定との関係 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `gamewohajimeta` | がめをはじめた | gameをはじめた | `game` は `english.txt` にあるが、ローマ字として最後まで読める (がめ) ので日本語側が勝つ | (a) `english-readable.txt` に `game` | 1 行 + 日本語の頭が game の `kyouhagamewo` など | **242 / 270** (`jp_all` の game を含む 270 行のうち 242 行が英字化: `togameru` → とgameる、`nagame` → なgame、`watashigameate` ほか) | **不採用** | `english-readable.txt` 冒頭の方針 (日本語の語と衝突する語は入れない) と同じ結論 |
| | | | | (b) (a) + 日本語の直後では英語にしない (`NotAfterJapanese` に `game`。`main`・`data` と同じ扱い) | 1 行 (`gamewohajimeta`) | **jp_all の 270 行は 0** だが、頭が game の日本語で悪化: `gamenwomiru` → gameんをみる (画面を見る)、`gametsui` → gameつい、`gameru` → gameる、`gamen` + 助詞 (wo・no・ha・ga …) 52 / 140 入力 | **不採用** | `gamen` (画面) の後ろに助詞が付く形は毎日の語。`readings.txt` に がめつい・がめる を足して塞ぐ案は、辞書 3 か所の編集で 1 行を直すことになり、塞いだ後に別の語が出ないことを測り切れない |
| `cachewokeshitemite` | かちぇをけしてみて | cacheをけしてみて | 同上 (cache = かちぇ) | (a) `english-readable.txt` に `cache` | 1 行 + 近所 7,503 入力 (cache の部分だけが英字になる) | **0** (`jp_all` に cache を含む行は 0。boundary-sweep full の日本語の損失 0 のとき `tacache` 型は無し) | 下の (b) で採用 | `english-readable.txt` の方針 (日本語の語にならない綴り) に合う |
| | | | | (b) (a) + 日本語のすぐ後ろでは英語にしない (`NotAfterJapanese` に `cache`。`main`・`data` と同じ扱い。code と型をそろえるため) | 1 行 (入力の先頭・英語の後ろの cache。`cachewo`・`cachedesu`・`redisnocache`・`gitcache`) | **0** (`jp_all` 261,651 行・近所 7,504 入力で、cache の外側が変わったのは「cache + 日本語の語」の 9 入力 (`cachebonsai`・`karencache` など) だけで、もとも かちぇぼんさい で日本語の語ではない) | **採用** | 損: `kyouhacachewo` (日本語の後ろ) は かちぇ のまま (直らない) |
| `dockernoimagewotsukurinaosu` | dockerのいまげをつくりなおす | dockerのimageをつくりなおす | 同上 (image = いまげ) | `english-readable.txt` に `image` | 1 行 (+ `imagewohiraku`・`kyouhaimagewo`) | **2 以上**: `tadaimagenzai` → ただimageんざい (ただいま現在)、`nihonnimageru` → にほんimageる | **不採用** | W1・W18-1 の決定どおり (tadaimagenzai を守るため意図して残した。QualityTests に いまげを の期待)。測り直しても同じ |
| `codereviewwoonegaishimasu` | こでreviewをおねがいします | codereviewをおねがいします | 同上 (code = こで。`review` は英語で当たるが code が かな) | (a) `english-readable.txt` に `code` | 1 行 + `kyouhacodewokaita`・`codewoharu` など | **3** (boundary-sweep full が見つけた): `tacodesu` (タコです) → たcodeす、`chocodesu` (チョコです) → ちょcodeす、`sa-bisugyoucdemou` (サービス業 + c の打ち間違い) → さーびすぎょうcodeもう。`jp_all` は 0 行 (c 綴りの日本語が無いため見えなかった) | 条件を狭めて (b) へ | 初回の測定 (jp_all 261,651 行 + 近所 7,500 入力で悪化 0) では見えず、境界例の一括比較で見つかった。**c 綴りの日本語 (taco・choco + で・です) は実在する** |
| | | | | (b) (a) + 日本語のすぐ後ろでは英語にしない (`NotAfterJapanese` に `code`) | 1 行 (`codereviewwoonegaishimasu`)、`codewo`・`codega`・`codedesu`・`vscodedecodewokaku`・`codegemma` | **0** (上の 3 入力は かな のまま。boundary-sweep full: 日本語の損失 0・英語の損失 0・その他の変化 8 (すべて cache・code の直り)。`jp_all` 変化 0) | **採用** | `english-readable.txt` 冒頭は code を「入れない」例に挙げていた。その理由 (日本語の語と区別できない) は tacodesu で当たっていた。**日本語のすぐ後ろでは かな のまま**にして悪化を消した。損: `kyouhacodewokaita`・`korehacodewokaku` (日本語の後ろ) は こで のまま (既存テストの「限界」の期待はそのまま。`codegemma` だけ codegemma に直ったので期待を更新) |
| `Tell me more` | Tell めもれ | Tell me more | 英語 + 空白 + `me` の判定が「前後の両方が英語」(2 文字語の needed = 2) を要求し、末尾・次が曖昧な語では足りない | `CompositionDetector.IsEnglishSpanCore` で、語が `me`・`do`・`we` で前が英語 (before ≥ 1) のとき needed = 1 | 6 (下の 5 行 + `How do we do`・`I do`) | **0** (下記) | **採用** | W12 §5 の「次の候補」(`Tell me more` `Not me` `We do` `Go on` `Let me go` `Say no more` `Why do we go`)。W12 §8 の「広い一覧は壊した」は、前の語の一覧を使う案だった。今回は語を 3 つに限り、前が日本語なら今までどおり。`Go on`・`Say no more` は直らない (on・no は対象外) |
| `Not me` | Not め | Not me | 同上 | 同上 | (同上) | (同上) | **採用** | 同上 |
| `We do` | We ど | We do | 同上 | 同上 | (同上) | (同上) | **採用** | 同上 |
| `Let me go` | Let めご | Let me go | 同上 | 同上 | (同上) | (同上) | **採用** | 同上 |
| `Why do we go` | Why どうぇご | Why do we go | 同上 | 同上 | (同上) | (同上) | **採用** | 同上 |
| `cd ~/Downloads` (一般アプリ) | cd～/Downloads | cd ~/Downloads | `cd` が一般アプリでは英語と判定されない (K79 の仕様) ので、`cd ` が日本語の区間になり、**空白も消える**。`~` が全角になるのは同じ原因の副産物 | `~` を「次が `/` なら半角」にする (分類表の案) | **0** (直しても `cd~/Downloads` で空白が戻らず、本家の `cd ~/Downloads` にならない) | — (測っていない) | **見送り** | K79 は「コード系アプリ (ターミナル・VS Code など) の語の頭だけ直す。一般アプリでは今までどおり」と決めている。実機のターミナルでは `cd ~/Downloads` は直っていて、ベンチの一般アプリ扱いの測り方の差 (コード系アプリとして打つと Mac 版も `cd ~/Downloads`) |
| `cd ~/Documents/Meltype` (一般アプリ) | cd～/Documents/Meltype | cd ~/Documents/Meltype | 同上 | 同上 | 0 | — | **見送り** | 同上 |
| `gpt-oss wo tsukau` | gpt-おっsをつかう | gpt-oss をつかう | `-` の後ろの `oss` が日本語の区間になる (`my-app`・`next-oss` も同じ) | (a) `english.txt` に `oss` | 1 行 | **2 以上**: `ossna` → ossな (打ち間違いの補正が効かない)、`kyouhaossnaodesu` → きょうはossなおです | **不採用** | W18-1 (`ossna` → おっさん を守るため足さない) と同じ。測り直しても同じ入力で壊れる |
| | | | | (b) `HyphenatedWords` (ハイフン入りの語の一覧) に `gpt-oss` を 1 語 | 1 行 (`gpt-oss`・`gpt-oss ha tsuyoi` なども) | **0** (`jp_all` 261,651 行・`en_t` 92,015 行で変化 0。`ossna`・`gpt-ossna` は今までどおり) | **採用** | W18-1 が外した理由 (`oss` 単独を足す) には当たらない。語単位の一覧 (`wi-fi`・`k-pop` と同じ) なので `oss` の打ち間違いの補正に触れない |
| `npx create-next-app` | npx create-next-あっp | npx create-next-app | 同上 (`-` の後ろの `app`。`create-react-app`・`my-app`・`web-app` も `-あっp`) | (a) `english.txt` に `app` | 1 行 + `tsukaiyasuiapp`・`apptoapp` | **4 以上**: `appwo` → appを (あっぷを)、`kyouhaappwo`、`appwoiretai`、`appli` → appぃ | **不採用** | W18-1 (`appwo` → あっぷを を守るため足さない) と同じ |
| | | | | (b) `HyphenatedWords` に `create-next-app`・`create-react-app` | 1 行 (`npx create-next-app`・`create-react-app`・`npx create-react-app`) | **0** (同上の全件で変化 0。`appwo`・`kappa`・`appurodo` は今まで通り) | **採用** (2 語) | 同上。`my-app`・`web-app` などの一般形は直さない (一般の「ハイフン + app」を英語にする規則は、`ossna` と同じ型の打ち間違いと区別するため、前後の測定が要る。次の候補) |

### 「前が英語なら me・do・we は 1 語でも英語」の悪化の測定
- ベンチ 649 件: 変わったのは 9 行 (すべて上の直った行)。
- `jp_all.txt` 261,651 行: **変化 0**。`en_t.txt` 92,015 行: **変化 0**。`me`・`do`・`we` を含む行だけに絞った 20,852 行・10,640 行も 0。
- 英単語 22 語 (Slack・Zoom・Google・iPhone・Notion・Chrome・Excel・GitHub・Python・Windows・LINE・Mac・the・I・Tell・Not・Let・Why・We・You・And) + 空白 + 頭が me・do・we の日本語 (`jp_all` から各 300 行)、同じ 22 語 + 空白 + `me`・`do`・`we` + 助詞 14 通り、`kyouha` + 英単語 + `me`・`do` + 日本語の行: 8,942 入力で変わったのは 46。**46 件はすべて「英単語 + 空白 + 単独の me または do (入力の末尾)」**: 前が機能語 (the・I・Tell・Not・Let・Why・We・You・And) の 20 件は直る型、前が名詞 (Slack・Zoom・Google・iPhone・Notion・Chrome・Excel・GitHub・Python・Windows・LINE・Mac) の 24 件は `Slack me` → Slack め が `Slack me` に、`Zoom do` → Zoom ど が `Zoom do` になる。さらに `kyouha iPhone me son` など 2 件 (でたらめな続き)。後ろに日本語が続く形 (`Slack me wo`・`Slack dokusho`) は 0 件。
- 判断が分かれるのは、**英単語 + 空白 + 単独の `me`・`do` が、末尾でも、直後が `.` `,` `!` `?` でも英語になる**こと (上の 24 件は末尾だけを数えた入力集合での数で、実際の範囲はもっと広い)。変更前→変更後: `Slack me.` → Slack め。 が `Slack me.`、`Zoom do.` → Zoom ど。 が `Zoom do.`、`Teams do?` → Teams ど？ が `Teams do?`、`kyouha Slack me,` → 今日はSlack め、 が `今日はSlack me,`、`Wi-Fi me` → Wi-Fi め が `Wi-Fi me`、`Slack me me` → Slack めめ が `Slack me me`、`the me`・`and do` も英語。**数字の後ろの `do` (〜度) も英語になる**: `iPhone 15 do` → iPhone 15 ど が `iPhone 15 do`、`Python 3 do` → Python 3 ど が `Python 3 do`。後ろに日本語を続けた形は変わらない (`Slack me wo` → Slack めを、`Slack doko` → Slack どこ、`Slack ni do` → Slack にど、`Slack ichi do` → Slack いちど)。ただし前の語で扱いが割れる: `Tell me wo` は 変更前 Tell めを → `Tell me を` に変わるが、`Slack me wo` は Slack めを のまま。日本語として め・ど を打つ入力に意味のあるものは見つからなかったが、`〜 15 do` (15 度) のような数字の後ろは日本語の読みもありうる。**ここは人が見る点** (下の「人の判断」)。

## minimum の要件 (これだけは満たす)
1. 上の「採用」の 5 件 (cache・code (日本語の後ろ以外)・gpt-oss・create-next-app / create-react-app・me/do/we) で、直る入力が直り、日本語の隣人 (`tacodesu`・`chocodesu`・`kokodemo`・`ossna`・`appwo`・`tadaimagenzai`・`gamen` ほか) が変わらない。
2. `jp_all` 261,651 行・`en_t` 92,015 行・ベンチ 649 行で、直る行以外が変わらない。
3. 不採用の語 (game・image・oss・app) は辞書に足さない。その「今のまま」をテストで固定する。

## 非目標 (今回はやらない)
- 本家のラティス (#1185) の導入。
- `game` を「画面」「がめつい」と両立させる直し (readings.txt に語を足す案は、測り切れないので見送り)。
- 一般アプリで `cd`・`ls` を英語にすること (K79 の決定)。
- `Go on`・`Say no more` (on・no は対象外。`no`・`on` は助詞と同じ綴りで、W12 §8 の broken が出た型)。
- `my-app`・`web-app` など、`-` の後ろの `app`・`oss` の一般形。
- 実機の NSSpellChecker の判定 (テストは同梱の英単語の一覧で測っている。実機は先にスペルチェッカーが判定する)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/upstream-only-misses.tsv` (40 行: 直した 10・守る日本語 26・直さなかった 4)。変更前 26/40 (守る 26 がすべて当たり、直した 10 がすべて外れ) → 変更後 36/40。外れの 4 件は直さなかった `gamewohajimeta`・`dockernoimagewotsukurinaosu` と、日本語の後ろの `kyouhacodewokaita`・`kyouhacachewo` (既知の外れ)。
- [x] テスト: `src/Meltype.Core.Tests/UpstreamOnlyMissesTests.cs` (7 件)。直る入力と、日本語の隣人 (`kokodemo`・`sokode`・`kodomo`・`gamen`・`gametsui`・`ossna`・`appwo`・`tadaimagenzai`・`me wo tojiru`・`Slack me wo` など)、直さなかった語の「今のまま」を固定。
- [x] 回帰なし: Core のテスト全件通過 (1331 → 1338)、`tools/eval-compare.sh` (base = origin/main `9ded846`) の broken 0 / fixed 17 (新しい表の 10 + 既存の表の 7: mac-terminal の gpt-oss・create-next-app、short-words-in-english の 5 行)、`tools/boundary-sweep.sh full` の日本語の損失・英語の損失 0 (結果は下の「記録」)。
- [ ] 文書: `docs/KNOWN_LIMITS.md`・`docs/UPSTREAM_PR_TRACKER.md` に載せるか (人の判断)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `cachewokeshitemite` Enter → `cacheをけしてみて`、`codereviewwoonegaishimasu` Enter → `codereviewをおねがいします`
- [ ] TextEdit: `Tell me more`・`Not me`・`We do`・`Let me go`・`Why do we go` + Enter → 打ったとおり (英語のまま)
- [ ] TextEdit: `gpt-oss wo tsukau` Enter → `gpt-oss をつかう`、`npx create-next-app` Enter → そのまま
- [ ] TextEdit (守り): `kokodemo` → ここでも、`gamenwomiru` → がめんをみる、`ossna` → おっさん、`appwo` → あっぷを、`tadaimagenzai` → ただいまげんざい、`kyouha me` → 今日はめ
- [ ] Safari のフォーム: 上と同じ数件

## 止める条件 (この要件に固有のもの)
- 採用した 5 件のどれかで、日本語の入力に悪化が見つかり、条件を狭めても消えないとき → その件だけ外す (辞書の 1 語・一覧の 1 項目なのでいつでも外せる)。

## 得と損
- 得: `cache`・`code` (英語の開発の文で毎日出る語)、`gpt-oss`・`create-next-app`、英文の末尾・途中の `me`・`do`・`we` (`Tell me more`・`Not me`・`We do`・`How do we do`・`I do`) が、日本語の かな に化けなくなる。日本語の入力 261,651 行・英語 92,015 行・ベンチ 649 行で他は変わらない。
- 損: (1) 英単語 (数字をはさんでもよい) + 空白 + 単独の `me`・`do` が、末尾・`. , ! ?` の直前でも英字で確定する (`Zoom do.`・`iPhone 15 do`。上の「判断が分かれる」)。後ろに日本語が続く形 (`Slack me wo`) は変わらないので、`Tell me wo` (→ Tell me を) と `Slack me wo` (→ Slack めを) で扱いが割れる。(2) `codegen` → codeげん (変更前 こでげん)・`codebasewomiru` → codeばせをみる のように、`code` だけ英字になり残りが かな の形が出る (もとは こでばせをみる で、どちらも外れ。悪化ではない)。(3) 日本語のすぐ後ろの `code`・`cache` (`kyouhacodewokaita`) は かな のまま (tacodesu = たこです を守るための損。F10 で直す)。(4) 直らなかった行 (game・image・cd ~/ 2 行) と一般形 (oss・app 単独・Go on・my-app) は、本家との差として残る。 (5) 限界: `code`・`cache` は、日本語のすぐ後ろだけでなく、かな側の記号 (`:`・`"`・`-`・`#`・`(`) の後ろでも こで・かちぇ のまま (`:code` → :こで、`-code` → ーこで、`#code`・`(code`)。`.code`・`/code`・`_code`・`@code` は英語。

## 人の判断
1. **英単語 + 空白 + 単独の me・do (末尾・`. , ! ?` の直前・数字の後ろの do を含む) を英語にしてよいか** (上の「判断が分かれる」。後ろに日本語が続く形は変わらず、前の語で `Tell me を` と `Slack めを` に割れる)。→ 未判断。おすすめ: 受け入れる (`Slack me.`・`Google me` は英語として自然、日本語で め・ど が来る入力は見つからなかった。数字の後ろの do (15 度) だけは日本語の読みもありうる)。受け入れないなら、前の語を一覧 (Tell・Not・Let・Why・I・We・You・And・the …) に限る案があるが、検出器の判定は前の語を持たず点数だけなので、前の語の判定を足す作業になる。
2. **直さなかった 4 行 (game・image・`cd ~/…` 2 行)** と、一般形 (`my-app`・`Go on`) を、このまま「見送り」にしてよいか。→ 未判断。おすすめ: 見送り (数字は上の表)。

## 記録
- 最終の測定 (採用した変更を全部入れたビルド、変更前は origin/main `9ded846`): Core のテスト 1331 → 1338 (新規 7 件)。実装を外した状態で新しいテストは 7 件中 3 件が赤 (`cachewokeshitemite` → かちぇをけしてみて、`gpt-oss` → gpt-おっs、`Tell me more` → Tell めもれ)、残り 4 件 (日本語の守り) は前後とも緑。`eval-compare` broken 0 / fixed 17。`boundary-sweep full` 684,117 入力で日本語の損失 0・英語の損失 0・その他の変化 8 (`codewo`・`cachewo` など、すべて直り)。`jp_all` 261,651 行・`en_t` 92,015 行は変化 0、ベンチ 649 行は 9 行だけ変化 (すべて直り)。
- 途中で見つかった悪化: `code` を日本語の後ろでも英語にした最初の版は、`jp_all`・近所の入力では悪化 0 だったが、boundary-sweep full が `tacodesu`・`chocodesu`・`sa-bisugyoucdemou` の 3 件を見つけた。`NotAfterJapanese` に `code`・`cache` を足して 0 にし、その 3 入力をテストと正解表で固定した。
- 2026-11-25 henrietta (レーン L5): 測定・実装・テスト。ブランチ `fix/upstream-only-misses` (origin/main `9ded846` から)。本家が直した方法は、どの行も #1185 (ラティス) の副産物で、小さなコミットは無かった。
- 測定の限界: (a) 日本語の入力は Hepburn のローマ字の読み (`jp_all`) だけで、c 綴りや打ち間違いの日本語は少ない (打ち間違いの補正は個別の入力で確認)。(b) 実機は未確認。(c) 辞書の語 (cache・code) の悪化は、語を含む入力だけが変わりうる (検出器は綴りで引く) ので、`jp_all` に cache・code を含む行が 0 であることと、前後に日本語を置いた合成の入力 (各 7,500) で見た。
