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
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、今の main でも当たる
  ```
  dainyuu	だいにゅう	full	本家 #1946
  kinyuu	きにゅう	full
  kanyuu	かにゅう	full
  yunyuu	ゆにゅう	full
  hensuunidainyuu	へんすうにだいにゅう	full
  dainyuusuru	だいにゅうする	full
  kinyuusuru	きにゅうする	full
  ```
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req018_Inyuu_DoesNotSplitEnglishIn` を足す (要点: 上の行を打って Enter の確定を比べる。既定の判定器と `BuiltInWordChecker` の両方)。
- [ ] わざと崩した木で、足した行の少なくとも 1 つとテストが外れる (崩し方と結果を記録欄に書く)。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0 (足した行は added として ok)、Core のテスト全件通過。
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `dainyuu` + Enter → `だいにゅう` (Space で `代入` が候補に出るか)
- [ ] TextEdit: `hensuunidainyuu` + Enter → `へんすうにだいにゅう`
- [ ] Safari のフォーム: `kinyuu` + Enter → `きにゅう`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- (未記入)
