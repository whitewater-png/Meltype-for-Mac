---
title: 本家 11-22 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-22.md 候補 3)
source: docs/voice/2026-11-22.md (ブランチ voice/2026-11-22) 候補 3 / 本家 #2206・#2208・#2205
scope: core
---

# 025 本家 11-22 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (主に 1.7.1、どれも Windows の報告) に 2026-11-22 に出た報告 (docs/voice/2026-11-22.md の要約。本文は WebFetch の要約を通したもので、細部は原文と違う可能性がある):
  - #2206 `setteisiteirundakedo` → `設定していrunだけど` (途中の `run` が英字で残る)
  - #2208 `mainkannsuu` → `マイン関数` (期待 `main関数`。英単語の `main` がかなになる)
  - #2205 `@exampleabc-jh.ed.jpdesu` → 21 文字目から日本語の変換が始まる (報告者の見立ては、前の文字を 20 文字で切るため `@` が落ちる)
- 今の挙動 (Core の `--repro` と `--segments`、最後のキーは Enter、2026-11-23、`origin/main` 496c90e): この fork ではどれも起きない。
  - `setteisiteirundakedo` → `せっていしているんだけど` (`JA:setteisiteirundakedo`)。同じ形の `taiousiteirundakedo` → `たいおうしているんだけど`、`ryourisiteirundakedo` → `りょうりしているんだけど`
  - `mainkannsuu` → `mainかんすう` (`EN:main | JA:kannsuu`)。`mainkansuu` (n 1 つ) → `mainかんすう`、`mainnokannsuu` → `mainのかんすう`
  - `@exampleabc-jh.ed.jpdesu` → `@exampleabc-jh.ed.jpdesu` (`JA:@ | EN:exampleabc-jh.ed.jpdesu`。全部英字のまま)。前に名前のある `abc@exampleabc-jh.ed.jpdesu` → `abc@exampleabc-jh.ed.jpdesu`
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で英字が混ざる形 (またはかなに崩れる形) になっても、回帰ゲートで気付けない (要件 019・020・022・023 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`)・`docs/KNOWN_LIMITS.md`・`src/Meltype.Core.Tests` を `setteisiteirun`・`siteirundakedo`・`mainkan`・`exampleabc` で探すと 0 件 (2026-11-23)。
- 本家の扱い: #2205 は直す PR (本家 #2215) が open (一覧から。直し方は未確認。クラウドから本家に届かない)。#2205 の「前の文字の長さの制限」は Swift 側 (実機の確定済みの前の文字) の話で、Core の `--repro` は 1 回の入力しか見ないので測れない。
- 開いている PR #29 (ブランチ ccr-0fc8aeab-sra4z7) は正解表の形式に「直前の文字」の列を足す (要件 016 案)。この要件の行は 4 列で、その変更の前後どちらでも読める形にする (push 前に `git merge-tree` で確かめる)。

## minimum の要件 (これだけは満たす)
1. 上の形を、今の出力のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字の形 (`設定しているんだけど`・`main関数` など。偽の変換エンジンでは測れない)。
- #2205 の「確定済みの前の文字の長さ」(Swift 側。実機でしか測れない)。
- `@exampleabc-jh.ed.jpdesu` の末尾の `desu` を `です` にするか (今は全部英字。期待を人が決める必要があるので、今の出力を守るだけにする)。
- docs/voice/2026-11-22.md の候補 1 (`antigravity`・`sideload` が割れる。直し方を調べる必要がある) と候補 2 (日本語を挟んだ引用符の組。記号の幅を人が決める)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [ ] 正解表: 新しい表 `eval/cases/upstream-guards-1122.tsv` に次の行を足し、今の main でも当たる (019〜023 と同じく、日付ごとの別の表にする)
  ```
  setteisiteirundakedo	せっていしているんだけど	full	本家 #2206
  taiousiteirundakedo	たいおうしているんだけど	full	要件 025 (声の文書で打った同じ形)
  ryourisiteirundakedo	りょうりしているんだけど	full	要件 025 (声の文書で打った同じ形)
  mainkannsuu	mainかんすう	full	本家 #2208
  mainkansuu	mainかんすう	full	要件 025 (作者の作例: n 1 つの綴り)
  mainnokannsuu	mainのかんすう	full	要件 025 (作者の作例: 助詞の の を挟む形)
  @exampleabc-jh.ed.jpdesu	@exampleabc-jh.ed.jpdesu	full	本家 #2205
  abc@exampleabc-jh.ed.jpdesu	abc@exampleabc-jh.ed.jpdesu	full	要件 025 (作者の作例: 前に名前のある形)
  ```
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req025_Upstream1122Shapes_StayKanaOrEnglish` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [ ] わざと崩した木で、足した 8 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過。
- [ ] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` と `docs/requirements/README.md` の一覧には足さない (019〜023 と同じ扱い。開いている PR #29 が同じ場所を変えている)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `setteisiteirundakedo` + Enter → `せっていしているんだけど` (Space の候補に `設定しているんだけど` が出るか)
- [ ] TextEdit: `mainkannsuu` + Enter → `mainかんすう` (Space の候補に `main関数` が出るか)
- [ ] TextEdit: `@exampleabc-jh.ed.jpdesu` + Enter → `@exampleabc-jh.ed.jpdesu`
- [ ] TextEdit: 21 文字以上の文字を確定した直後に `@exampleabc-jh.ed.jpdesu` を打ち、途中から日本語にならないか (本家 #2205 の前の文字の長さの話。Core では測れない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- (実装後に書く)
