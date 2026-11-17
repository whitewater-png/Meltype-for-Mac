---
title: dainyuu など -inyuu の形が英単語 in で崩れないことを正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-16.md 候補 3)
source: docs/voice/2026-11-16.md (ブランチ voice/2026-11-16) 候補 3 / 本家 #1946
scope: core
---

# 018 dainyuu など -inyuu の形が英単語 in で崩れないことを正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 #1946 (Linux 版 1.6.0、Zenzai 不使用): `dainyuu` が `だinゆう` になる (期待: 代入)。英単語 `in` を拾った形。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-17、`origin/main` 20736db): この fork では**起きない**。
  - `dainyuu` → `だいにゅう`、`kinyuu` → `きにゅう`、`kanyuu` → `かにゅう`、`yunyuu` → `ゆにゅう`
  - `hensuunidainyuu` → `へんすうにだいにゅう`、`dainyuusuru` → `だいにゅうする`、`kinyuusuru` → `きにゅうする`
  - `--segments dainyuu` → `JA:dainyuu` (区間は 1 つ)
- 正解表に `-inyuu` の形の行が 1 つも無いので、後の変更 (辞書への語の追加・判定ロジックの変更) で本家と同じ崩れ方をしても、回帰ゲートで気付けない。

## 今の動き (既に分かっていること)
- `in` は `dictionaries/english.txt` の基本語の行にある。`dainyuu` は `JA` の 1 区間で、`in` を英語の区間として切り出していない。
- 正解表 (`eval/cases/*.tsv`) を `inyuu` で探すと 0 件 (2026-11-17)。
- 本家の扱い: 本家の bot が main と 1.6.0 で再現 (docs/voice/2026-11-16.md の要約)。直し方は未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. `dainyuu` → `だいにゅう`、`kinyuu` → `きにゅう`、`kanyuu` → `かにゅう`、`yunyuu` → `ゆにゅう`、`hensuunidainyuu` → `へんすうにだいにゅう` を正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字の `代入` になるか (偽の変換エンジンでは測れない)。
- `tennyuu` → `てんゆう`・`shinnyuu` → `しんゆう` (ローマ字の規則どおり `nn` で `ん`。誤りとはみなさない)。
- 英語の後ろの形 (`x ni dainyuu` の空白の扱い、`xnidainyuu` → `んいだいにゅう`) は別の話で今回は触らない。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、今の main でも当たる
  ```
  dainyuu	だいにゅう	full	本家 #1946
  kinyuu	きにゅう	full
  kanyuu	かにゅう	full
  yunyuu	ゆにゅう	full
  hensuunidainyuu	へんすうにだいにゅう	full
  dainyuusuru	だいにゅうする	full
  kinyuusuru	きにゅうする	full
  shinyuu	しにゅう	full	要件 018 (査読の例: 前に英単語 shin が来る形)
  ginyuu	ぎにゅう	full	要件 018 (査読の例: 前に英単語 gin が来る形)
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req018_Inyuu_DoesNotSplitEnglishIn` を足す (要点: 上の行を打って Enter の確定を比べる。テスト用の判定器 (`BuiltInWordChecker`) とスペルチェッカー無しの両方)。
- [x] わざと崩した木で、足した行の少なくとも 1 つとテストが外れる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0 (両方の木を同じ表で回すので、足した行は base でも当たり added・fixed は 0)、Core のテスト全件通過。
- [x] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `dainyuu` + Enter → `だいにゅう` (Space で `代入` が候補に出るか)
- [ ] TextEdit: `hensuunidainyuu` + Enter → `へんすうにだいにゅう`
- [ ] Safari のフォーム: `kinyuu` + Enter → `きにゅう`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- 周回数: 実装 1 周 + 査読の指摘への対応 2 回、査読 3 周 (3 周目は指摘なし)。
- わざと崩した木: `src/Meltype.Core/Detection/RomajiDetector.cs` の `("にゅ", ["nyu"])` を一時的に `nyuq` にした (にゅ の綴りを外す崩し方。`in` を英語として拾う経路そのものに絞った崩し方ではない)。結果は `dainyuu` → `だinゆう` (本家 #1946 と同じ形)、`kinyuu` → `kinゆう`、`kanyuu` → `かnゆう`、`yunyuu` → `ゆじゅう`、`hensuunidainyuu` → `へんすうにだinゆう`、`dainyuusuru` → `だinゆうする`、`kinyuusuru` → `kinゆうする`、`shinyuu` → `shinゆう`、`ginyuu` → `ginゆう` で、足した 9 行はすべて外れ、テストは赤 (`dainyuu 期待値: だいにゅう / 実際: だinゆう`)。戻した後に緑。査読員も同じ崩し方で再現した。
- 数字: Core 1260/1260、eval-compare (origin/main 20736db と比較) broken 0 / fixed 0 / added 0。辞書は変えていない。
- 査読 2 周目: [止める] なし。[直す] 記録欄の AGENT_STATE の記述 → 直した。[任意] 完成条件の出典の書き方を tsv とそろえた。
- 査読 1 周目: [止める] なし。[直す] テストの 2 回目のループが 1 回目と同じ判定器だった → スペルチェッカー無しに変えた / 記録欄 → 記入 (AGENT_STATE は PR 作成後に PR 番号付きで更新)。[任意] 「added として ok」の記述 → 直した、崩し方の範囲 → 上に明記、`shinyuu`・`ginyuu` (前に英単語が来る形) → 足した。`sounyuu`・`kounyuu`・`ninyuu`・`chuunyuu` などは今も当たるが、同じ型なので足していない。
