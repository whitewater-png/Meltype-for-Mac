---
title: 本家 11-18 の誤判定 6 形 (fork では起きない) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-18.md 候補 2)   # 棚卸し 2026-11-25: 実機未確認
source: docs/voice/2026-11-18.md (ブランチ voice/2026-11-18) 候補 2 / 本家 #2015・#2016・#2017・#2024・#2029
scope: core
---

# 020 本家 11-18 の誤判定 6 形 (fork では起きない) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (1.6.0。多くは Windows 11、#2029 は Linux) に 2026-11-18 に出た誤判定の報告 (docs/voice/2026-11-18.md の要約。本文は WebFetch の要約を通したもの):
  - #2015 `itotekinagobyuu` → `意図的なgobゆう`
  - #2016 `ronpa` → `ろんpa` (`onpa` も崩れると報告)
  - #2017 `Fluorite` → `Fluおりて`
  - #2024 `uchidome` → `うちdome`
  - #2029 `fanntatowirukinnsonn` → `ファンタとwイルキンソン` (`to` の後の `wi` を英語と見る)
- 今の挙動 (Core の `--repro` と `--segments`、最後のキーは Enter、2026-11-19、`origin/main` 20736db): この fork では 6 つとも**起きない**。
  - `itotekinagobyuu` → `いとてきなごびゅう` (`JA:itotekinagobyuu`)
  - `ronpa` → `ろんぱ` (`JA:ronpa`)、`onpa` → `おんぱ` (`JA:onpa`)、`ronpasuru` → `ろんぱする`
  - `Fluorite` → `Fluorite` (`EN:Fluorite`)
  - `uchidome` → `うちどめ` (`JA:uchidome`。`utidome` も同じ)
  - `fanntatowirukinnsonn` → `ふぁんたとうぃるきんそん` (`JA:fanntatowirukinnsonn`)
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で本家と同じ崩れ方をしても、回帰ゲートで気付けない (要件 018・019 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`) と Core のテストを `gobyuu`・`ronpa`・`onpa`・`fluorite`・`uchidome`・`wirukin`・`towi` で探すと 0 件 (2026-11-19)。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 直し方は未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 上の 6 形を、今の出力のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字・カタカナの形 (`意図的な誤謬`・`論破`・`打ち止め`・`ファンタとウィルキンソン`。偽の変換エンジンでは測れない)。
- 小文字の `fluorite` (今の main で `fluおりて` に崩れる。本家の報告は大文字始まりで、形が違う。直すなら辞書か判定の変更で、別の要件の候補)。`Fluoritewotukau` (丸ごと英字のまま) も同じく扱わない。
- docs/voice/2026-11-18.md の候補 1 (文の途中の `zl`・`zk`。打ち間違いの補正に触れ、`zl` のままにするか矢印にするかを人が決める必要がある)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [x] 正解表: 新しい表 `eval/cases/upstream-guards-1118.tsv` に次の行を足し、今の main でも当たる (新しい表にするのは、開いている PR #24 が `eval/cases/upstream-guards.tsv` を新しく作っていて、同じファイルを作ると衝突するため。両方が main に入ったら 1 つにまとめてよい)
  ```
  itotekinagobyuu	いとてきなごびゅう	full	本家 #2015
  ronpa	ろんぱ	full	本家 #2016
  onpa	おんぱ	full	本家 #2016
  ronpasuru	ろんぱする	full	要件 020 (作者の作例: 後ろに日本語が続く形)
  Fluorite	Fluorite	full	本家 #2017
  uchidome	うちどめ	full	本家 #2024
  utidome	うちどめ	full	要件 020 (作者の作例: ti の綴り)
  fanntatowirukinnsonn	ふぁんたとうぃるきんそん	full	本家 #2029
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req020_Upstream1118Shapes_StayAsTheyAre` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [x] わざと崩した木で、本家の 6 形の行がそれぞれ外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0 (両方の木を同じ表で回すので、足した行は base でも当たり added・fixed は 0)、Core のテスト全件通過。
- [x] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` の「ファイルの役割」には足さない (PR #24 が同じ場所に `upstream-guards.tsv` の行を足していて衝突するため。表の役割は表の頭の注釈に書いた。PR #24 の行は「本家に報告された誤判定のうち、Mac 版の Core では起きないものを守る行」で、まとめた後はこの表にも当てはまる)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `itotekinagobyuu` + Enter → `いとてきなごびゅう` (Space で `意図的な誤謬` が候補に出るか)
- [ ] TextEdit: `ronpa` + Enter → `ろんぱ`、`uchidome` + Enter → `うちどめ`
- [ ] TextEdit: `Fluorite` + Enter → `Fluorite`
- [x] LibreOffice Writer (本家 #2015 の報告のアプリ): `itotekinagobyuu` + Enter → `いとてきなごびゅう` … 2026-11-26 リョウさんの実機 (確認表 57): OK
- [ ] TextEdit: `fanntatowirukinnsonn` + Enter → `ふぁんたとうぃるきんそん` (Space で `ファンタとウィルキンソン` が候補に出るか)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- 周回数: 実装 1 周、査読 2 周 (1 周目は [止める] なし・[直す] 1 件。2 周目は [止める]・[直す] なし)。
- 数字: Core 1260/1260、eval-compare (origin/main 20736db と比較) broken 0 / fixed 0 / added 0 (630/736。新しい表は 8/8。両方の木を同じ表で回すので base でも当たる)。`check-terms.mjs` エラー 0。辞書・判定ロジックは変えていない (`src/Meltype.Core`・`dictionaries`・`mac/` の差分 0 行)。
- 査読 1 周目 (別エージェント。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 を査読員が再実行。8 行と「今の main の外れ」2 形を `--repro`・`--segments` で照合。崩した木は ど・大文字始まり・ぱ の 3 つを再現。開いている PR #20・#23・#24 と `git merge-tree` で合わせて衝突なし):
  - [直す・対応] `docs/AGENT_STATE.md` が未更新 → PR を作った後に 1 行足した。
  - [任意・対応] ぱ を崩した木の「本家とは違う崩れ方」の書き方 → 表示は本家と同じ `ろんpa` になると書き直した (実装者も `--repro` で確かめた)。
  - [任意・見送り] `docs/requirements/README.md` の一覧に 020 の行が無い (開いている PR #20・#23 が同じ末尾に行を足していて衝突するため。019 と同じ扱い)。
  - [任意・見送り] テストも正解表も確定だけを比べるので、表示だけが崩れる回帰は見えない (018・019 と同じ作り)。
- わざと崩した木 (作業ツリーで 1 つずつ変えて、テスト `Req020` と正解表を回し、`git checkout` で戻した。2026-11-19):
  | 崩し方 | 外れた行 → 出力 |
  |---|---|
  | `RomajiDetector.cs` の `("びゅ", ["byu"])` を `byuq` に (びゅ の綴りを外す) | `itotekinagobyuu` → `いとてきなgobゆう` (本家 #2015 のかなの形と同じ) |
  | `RomajiDetector.cs` の `("ぱ", ["pa"])` を `paq` に (ぱ の綴りを外す) | 確定は `ronpa` → `ろのあ`、`onpa` → `おんぺあ`、`ronpasuru` → `ろのあする`。確定の前の表示は `ろんpa`・`おんpa`・`ろんpあする` (表示は本家 #2016 の `ろんpa` と同じ形。テストと正解表が比べるのは確定だけ) |
  | `RomajiDetector.cs` の `("ど", ["do"])` を `doq` に (ど の綴りを外す) | `uchidome`・`utidome` → `うちdome` (本家 #2024 と同じ形) |
  | `RomajiDetector.cs` の `("うぃ", ["wi", "whi"])` の `wi` を `wiq` に | `fanntatowirukinnsonn` → `ふぁんたとwいるきんそん` (本家 #2029 のかなの形と同じ) |
  | `CompositionDetector.cs` の「大文字で始まる語は固有名詞や英文」の行 (`if (char.IsAsciiLetterUpper(span[0]) && (exact \|\| prefix \|\| atEnd \|\| …)) return true;`) を効かなくする | `Fluorite` → `Fluおりて` (本家 #2017 と同じ形) |
  - どの崩し方でもテストは赤 (例: `uchidome 期待値: うちどめ / 実際: うちdome`)、戻した後は緑。
  - 崩し方は「その行が実際に比べられていること」を確かめるためのもので、本家の原因 (未確認) を再現したものではない。
- 見つけた今の main の外れ (この要件では直さない。守る行にもしない。別の要件の候補): 小文字の `fluorite` → `fluおりて` (`EN:flu | JA:orite`)、`Fluoritewotukau` → 丸ごと英字のまま (どちらも実装者が `--repro` で確かめた)。
- 査読 2 周目 (1 周目とは別のエージェント。PR を作った後の文書の更新を確認。`fluorite`・`Fluoritewotukau`・`ronpa` などを `--repro` で照合し、ぱ を崩した木の表示と確定を再現。開いている PR #20・#23・#24 と衝突なし): [止める]・[直す] なし。[任意・対応] AGENT_STATE の「今の外れ」に `Fluoritewotukau` を足した / 周回数の記述を今の状態に合わせた。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/25
- 2026-11-26 リョウさんの実機 (確認表 docs/HANDS_ON_CHECKLIST_2026-11.md の 57): LibreOffice Writer の `itotekinagobyuu` → `いとてきなごびゅう` OK
