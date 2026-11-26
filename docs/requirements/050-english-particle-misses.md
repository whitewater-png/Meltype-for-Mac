---
title: 英字の語のすぐ後ろの助詞・数字 + 単位・拡張子と版の . の外れを直す (Meltype-Flash の 30 文で見つけた外れ)
status: approved (2026-11-26 リョウさんの指示「お勧めで修正を開始してください」)
source: Meltype-Flash (appipinopi/Meltype-Flash) の rust/core/data/strict-accuracy.json の 30 文を、この fork の Core で打った結果 (14/30)
scope: core
---

# 050 英字の語のすぐ後ろの助詞・数字 + 単位・拡張子と版の . の外れを直す

## 背景 (誰が、何に困っているか)
- Meltype-Flash が自分の精度の物差しに使っている 30 文 (ローマ字 → かなの厳しい一致) を、この fork の Core (`--expect-all`、テスト用キーボードで打って Enter) で打つと 14/30 だった (2026-11-26、main a65ce2b)。外れ 16 件のうち、7 件は期待のほうが合わない (かなの綴りの揺れ・打ち間違いの補正の期待など) と見て対象外にし、残り 9 件を見た。
- 今の挙動 (Core、main a65ce2b):
  - `URLwoshirasetekudasaiPDFwookurimasu` → `URLwoshirasetekudasaiPDFをおくります` (URL から PDF までが 1 語の英字)
  - `ChatGPTdeCSVwo3maitsukurimashita` → `ChatGPTdeCSVを3まいつくりました`
  - `JavaScripttoPythonnoAPIwokakunin` → `JavaScripttoPythonnoAPIをかくにん`
  - `https://example.com/README.mdwobrowserdehiraku` → `…README.mdwobrowserでひらく`
  - `MeltypeTip.dlltoCargo.tomlwoREADME.mdnikirokusuru` → `MeltypeTip.dlltoCargo.tomlwoREADME.mdにきろくする`
  - `1.5kgto100MBnofilewosoufu` → `1.5kg1と00MBのふぃぇをそうふ` (数字の 1 が と の前にずれる・file が ふぃぇ)
  - `versionha1.2.3preview.4desu` → `versionは1.2.3preview。4です`
  - `kwa kwikwekwowotsukau` → `くぁくぃくぇくぉをつかう` (空白が消える)
  - `1000enno5percentwo3ninndeageru` → `1000えんお5percentを3にんであげる` (期待は `5％`)

## 今の動き (既に分かっていること)
- 大文字で始まる語の区切り: `CompositionDetector.CapitalizedWordEnd`。長い頭から「頭 + 小文字のローマ字」を探すので、後ろの略語 (PDF・CSV) までを頭に含めて、最後の助詞の前で区切っていた。次の大文字の前を見る所 (camel) は、最初の大文字の切れ目だけを見るので、URL のように大文字が続く語では効かなかった。
- 拡張子・ドメインの . の後ろ: `CompositionDetector.DotSuffixEnd`。. で続く所まで (dlltoCargo.tomlwoREADME.md) を 1 つの名前にしてから、最後のラベルの助詞で区切っていた。
- 英語の語の後ろの . は、前に日本語の区間があると句点 (。) になる (`FindSpans` の記号の規則。`きょうはgoogle。` のため)。
- 数字 + 単位の後ろの数字: `CompositionText.Append` が、数字を足す前に `SplitUnitAfterNumber(final: true)` をもう一度呼び、単位の後ろの残り (to) を保留に戻したまま数字を単位にしていた。
- file: `IsEnglishSpanCore` の「外来語の途中 (hosu|thin|gu)」の規則で、日本語の後ろの短い語が続きと合わせて読めるなら日本語にしていた。file は小書きの le でしか読めない (ふぃぇ)。
- 空白: Space は変換のキーなので、日本語の文の途中の空白は変換の区切りになる (この fork の設計)。`kyou ha` → `今日は` と同じ。
- percent: 数字の後ろの percent を ％ にする規則は無い (新しい機能)。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドからは本家に届かない)。ACCURACY_PLAN 2-3 の「英字のあいだの と」(本家 `74ca494`・`35beb60`) は移植せず、この fork の規則で直した。Meltype-Flash のコードは見ていない・写していない (打鍵と期待の文字列だけを使った)。

## minimum の要件 (これだけは満たす)
1. 略語 (大文字で終わる 2 文字以上) か知っている語 (3 文字以上) の後ろに、助詞で始まる小文字のローマ字が次の大文字の直前まで続くなら、最初の助詞の前で区切る。頭 + 助詞 + 次の語の一部が 1 つの知っている語なら区切らない (PhotoShop)。小文字の部分か、頭の最後の大文字 + 小文字の部分が 4 文字以上の英単語なら区切らない (AI|note|Book、A|Idea|Lab)。小文字で始まる語と、前の英語の区間に空白なしで続く所 (parseJSON|to|Object) には使わない。
2. 一覧の拡張子・ドメイン + 助詞 + 大文字で始まる次の名前なら、. で続く所まで 1 つの名前にせず、助詞の前で終わる。拡張子 + 助詞の後ろが「4 文字以上の英単語 + 助詞で始まる日本語」でも助詞の前で終わる (README.md|wo|browser|de)。
3. 前に日本語があっても、英語の語のすぐ後ろの . に拡張子・ドメインが続く (Cargo.toml・google.com) か、版の名前の語 (preview・alpha など、英字で残っている語) の後ろの . に数字が続く (preview.4) なら、半角の . のまま。拡張子・ドメインは、記号・数字を含む名前か、一覧の名前でローマ字として最後まで読めないものだけ (Mac.dehanai・Mac.cokode・Mac.goodbye は句点のまま)。ほかの語の後ろの . + 数字 (Mac.3jikara) も今までどおり句点。
4. 数字 + 単位 + 助詞 + 数字 (1.5kg|to|100) で、数字の順番がずれない。単位 + 助詞 (kgto・gto) を、数字の前の略語 (rta2026) と見ない。
5. 同梱の辞書の 4 文字の語で、小書きの l・x でしか読めない綴り (file・hole・mile) の後ろが助詞なら、日本語の後ろでも英字。

## 非目標 (今回はやらない)
- `kwa kwi` の空白: Space は変換のキー (この fork の設計)。直さない。
- `5percent` → `5％`: 新しい機能なので別に決める (2026-11-26 にリョウさんに聞いている)。
- 版の名前の `beta`・`rc` の後ろの . (`beta.4`・`2.0rc.1`): beta はかな (べた) に、rc は英語の区間にならないので、この規則が効かない。既知の外れとして残す (査読で確認)。
- `google.comade` (まで): 前後どちらも外れ (base は `google。こまで`、変更後は `google.comade`)。made を英単語と区別できないため、今回は直さない。
- 前に数字が無い `gto3`・`kgto3`: 変更後は `gと3`・`kgと3` になる (base は英字のまま)。数字の前の略語と見なくなっただけで、実際に打つことは少ないと見て受け入れる (推測)。
- Flash の 30 文のうち、期待のほうが合わないと見た 7 件 (`naiyouokakunin` の を/お、`katazukemasu` の づ/ず、`2kennokoshita` の打ち間違い、`akarukudou` の か/あか、`kekkaobunmyaku`・`kekkaomotomeno` の を/お、`romajinojunbun` の長音など)。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/english-particles-050.tsv` (新しい表、62 行) を足す。直す 24 行は実装前は外れ、実装後は当たる。守る 38 行 (査読で見つけた 11 行を含む) は前後とも当たる。base (main a65ce2b) 38/62 → 62/62。
- [x] テスト: `src/Meltype.Core.Tests/EnglishParticle050Tests.cs` に `Req050_...` 6 件。base に写して流すと 5 件が FAIL (守りの 1 件だけ PASS)、実装後は 6/6 (査読の後に守りの入力を足しても 6/6)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0 (既存の 14 表は 812/913 で変わらず。合計 850/975 → 874/975、fixed 24)、Core のテスト全件通過 (1377/1377)、品質テスト 1295/1296 (base と同じ)。
- [ ] 境界例の一括比較 (`tools/boundary-sweep.sh` full) で、日本語の損失・英語の損失の中身を見て、悪化なら直すか受け入れる理由を書く。
- [x] Flash の 30 文: 14/30 → 21/30 (直した 7 件。残りは非目標の 9 件)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `URLwoshirasetekudasaiPDFwookurimasu` + Enter → `URLをしらせてくださいPDFをおくります`
- [ ] TextEdit: `ChatGPTdeCSVwo3maitsukurimashita` + Enter → `ChatGPTでCSVを3まいつくりました`
- [ ] TextEdit: `JavaScripttoPythonnoAPIwokakunin` + Enter → `JavaScriptとPythonのAPIをかくにん`
- [ ] TextEdit: `MeltypeTip.dlltoCargo.tomlwoREADME.mdnikirokusuru` + Enter → `MeltypeTip.dllとCargo.tomlをREADME.mdにきろくする`
- [ ] TextEdit: `1.5kgto100MBnofilewosoufu` + Enter → `1.5kgと100MBのfileをそうふ` (打っている途中に 1 が と の前に出ないこと)
- [ ] TextEdit: `versionha1.2.3preview.4desu` + Enter → `versionは1.2.3preview.4です`
- [ ] TextEdit: `PhotoShopdehiraku`・`kyouhagoogle.desu`・`kyouhaMac.3jikara` + Enter が今までどおり (`PhotoShopでひらく`・`きょうはgoogle。です`・`きょうはMac。3じから`)
- [ ] Space で変換したときの漢字 (`URLを知らせてください` など) は Core では測れない

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 境界例の一括比較で、日本語の損失が規則の狭め方で消せない形で出る。

## 記録 (ループが書く)
- 1 周目 (上位のモデル)。2026-11-26。クラウドの Core (apt の .NET SDK 10.0.115、global.json は手元だけ 10.0.100 に下げて実行。コミットしない)。
