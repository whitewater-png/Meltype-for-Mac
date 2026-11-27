---
title: 数字のすぐ後ろの percent を ％ にする (5percent → 5％)
status: approved
source: 要件 050 の作業で見つけた (Meltype-Flash の 30 文の 1 件。2026-11-27 リョウさん「別要件でたてて」)
scope: core
---

# 053 数字のすぐ後ろの percent を ％ にする

## 背景 (誰が、何に困っているか)
- Meltype-Flash の厳しい一致の 30 文に、`1000enno5percentwo3ninndeageru` → `1000円の5％を3人であげる` (かなの期待は `5％`) がある。
- 今の挙動 (Core、main a65ce2b、要件 050 の調べ): `1000えんお5percentを3にんであげる` (percent は英字のまま)。
- 要件 050 では「外れではなく新しい機能」として外した (2026-11-26 の選択カード。2026-11-27 リョウさんが「別要件でたてて」と答えた)。

## 今の動き (既に分かっていること)
- 数字の後ろの単位の語は `CompositionText.UnitWords` (kg・MB など) で英字のまま残す。percent を記号に置き換える規則は無い。
- 全角の記号を半角に戻す処理 (`CompositionText` の 1774 行付近) はあるが、語を記号に置き換える処理は無い。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドからは本家に届かない)。Meltype-Flash のコードは見ない・写さない (打鍵と期待の文字列だけを使う)。

## minimum の要件 (これだけは満たす)
1. 日本語の文の中で、数字のすぐ後ろの `percent` を `％` (全角か半角かは人が決める。下の「決めること」) にする。
2. 英文の中 (`5 percent`・`I got 5percent`) と、コード系アプリの中では今までどおり英字のまま (案。人が決める)。
3. `percentage`・`percentile` のように percent で始まる長い語は置き換えない。

## 決めること (人の判断)
- 2026-11-27 リョウさんの決定 (選択カード「全角％・日本語の文だけ」): 
  - `％` (全角)。打った `%` と同じ単位にするので、設定「記号を全角にする」が OFF なら半角の `%`。
  - 英文の中 (英文の後ろ) では置き換えない。
  - コード系アプリ (ターミナル・エディター) では置き換えない。

## 非目標 (今回はやらない)
- `pasento` (ぱーせんと) のようなローマ字の読みからの置き換え (変換エンジンの候補の話)。
- `degree`・`yen` などほかの語の記号化。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/` に行を足し、実装前は外れ、実装後は当たる (`1000ennno5percentwo` → `1000えんの5％を` など。今は `1000えんの5percentを`。守る行: `percentage`・英文の `5 percent`)
- [x] テスト: `src/Meltype.Core.Tests/` に `Req053_...` を足す
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`tools/boundary-sweep.sh` の損 0

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `5percentnowaribiki` + Enter → `5％のわりびき` (漢字はライブ変換の設定で変わる。見るのは ％ と区切り)
- [ ] TextEdit: `I got 5 percent` + Enter → 英字のまま
- [ ] TextEdit: `5percentijou` + Enter → `5％いじょう` (漢字は設定で変わる)
- [ ] TextEdit: `5percentrule` + Enter → `5percentrule` (後ろが英字の語なら英字のまま)
- [ ] TextEdit: `5percentno` を打って F10 → `5percentno` (打った綴りに戻る)。Enter で確定した直後の F10 も同じ
- [ ] TextEdit: `5percentno` + BackSpace 2 回 → `5` (％ は 1 文字として丸ごと消える。打った 7 文字を 1 文字ずつは消さない)
- [ ] ターミナル: `5percentnowaribiki` + Enter → 英字のまま (コード系アプリ)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 英文の中で置き換えが起きる形が、規則の狭め方で消せない。

## 記録 (ループが書く)
- 2026-11-27 要件を立てた (draft)。実装はまだ。
- 2026-11-27 リョウさんが選択カードで「全角％・日本語の文だけ」を選び、approved にした。
- 2026-11-27 実装 (ブランチ claude/project-thread-mqhfj7)。`CompositionText.SplitPercentAfterNumber`: 数字の後ろの percent を、打った % と同じ単位 (打った綴りを `CompositionUnit.Typed` に覚える) にする。前が日本語 (同じ入力の中の日本語の区間、または前の確定が かな・漢字・。、で終わる) か、後ろを読んで日本語の区間があるときだけ。打ち足すたびに決め直す (5percent|ru → 5percent|rule)。確定のとき続きが最後までローマ字として読めなければ置き換えない。percentage・percentile・percents (と打ちかけ)・一覧の英単語は英字の語のまま。F6〜F10・Esc で戻す・確定した直後の F10 は打った percent に戻す。コード系アプリ・英文の後ろ・かな入力では置き換えない。
- 数字: Core 1384/1384 (base 1377 + 7)、正解表 `eval/cases/percent-053.tsv` 32 行 (base 17/32 → 32/32)、eval-compare broken 0 / fixed 15、品質テスト base と同じ (外れは `my name is taro` のみ)、境界例の一括比較 full 684,122 入力で変化 0。別エージェントの査読 3 周 (1 周目 直す 3 件、2 周目 直す 1 件、3 周目 指摘なし)。
- ついでに直った形: `5percentijou`・`5percentika` (前は 5perせんちじょう)・`5percentile` (前は 5perせんちぇ)。直していない形: `5percentoff`・`5percentOFF` (前と同じく崩れる。後ろが英字なので ％ にしない決定の外)、`5percentnn` → `5percentん` (前と同じ)。
- Mac 実機は未確認。
