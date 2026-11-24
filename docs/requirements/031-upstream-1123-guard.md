---
title: 本家 11-23 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-23.md 候補 1)
source: docs/voice/2026-11-23.md (ブランチ voice/2026-11-23) 候補 1 / 本家 #2261・#2258・#2254
scope: core
---

# 031 本家 11-23 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (1.7.x、Windows の報告) に 2026-11-23 に出た報告 (docs/voice/2026-11-23.md の要約。本文は WebFetch の要約を通したもので、細部は原文と違う可能性がある):
  - #2261 `nonbirimiroyo` → `のんびりmiroよ` (期待 `のんびり見ろよ`。動詞の命令形 `miro` が英字で残る)
  - #2258 `What are things made you happy?` → `What あれ things まで 用 happy?` (英文の途中が かな・漢字になる。Zenzai ON)
  - #2254 `/usr/bin/php` → 先頭の `/` が `・` になる
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-24、`origin/main` 9f28603): この fork では確定はどれも崩れない。
  - `nonbirimiroyo` → `のんびりみろよ`。同じ形の `kokowomiroyo` → `ここをみろよ`、`asitamiroyo` → `あしたみろよ`
  - `nonbirimiro` → `のんびりみろ`、`kokowomiro` → `ここをみろ`、`asitamiro` → `あしたみろ` (Enter の確定。途中の表示は `のんびりmiro` などで、確定と食い違う。下の非目標)
  - `What are things made you happy?` → `What are things made you happy?`。文頭の `made you happy` → `made you happy`、`it made you happy` → `it made you happy`
  - `/usr/bin/php` → `/usr/bin/php`
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で英字が混ざる形 (またはかなに崩れる形) になっても、回帰ゲートで気付けない (要件 019・020・022・023・025 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`)・`docs/KNOWN_LIMITS.md`・`src/Meltype.Core.Tests` を `miro`・`made you`・`usr/bin/php` で探すと、`miro.com` (`UpstreamP46Tests.cs`) 以外は 0 件 (2026-11-24)。
- 途中の表示 (`showing`) と確定 (`committed`) の食い違い (`のんびりmiro` → `のんびりみろ`、文頭の `made you` が途中で `までよう` になる) は、声の文書の候補 3 で別に調べる話で、ここでは確定だけを守る。

## minimum の要件 (これだけは満たす)
1. 上の形を、今の出力 (Enter の確定) のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は確定が崩れないので直すものが無い)。
- 漢字の形 (`のんびり見ろよ`・`見ろ` など。偽の変換エンジンでは測れない)。
- 打っている途中の表示と確定の食い違い (docs/voice/2026-11-23.md 候補 3。直すかを人が決める)。
- docs/voice/2026-11-23.md の候補 2 (小文字で打った `iphone` を `iPhone` にする。判定と辞書を変える話)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [ ] 正解表: 新しい表 `eval/cases/upstream-guards-1123.tsv` に次の行を足し、今の main でも当たる (019〜025 と同じく、日付ごとの別の表にする)
  ```
  nonbirimiroyo	のんびりみろよ	full	本家 #2261
  kokowomiroyo	ここをみろよ	full	要件 031 (作者の作例: 同じ形)
  asitamiroyo	あしたみろよ	full	要件 031 (作者の作例: 同じ形)
  nonbirimiro	のんびりみろ	full	要件 031 (声の文書で打った同じ形。語の終わりで止める)
  kokowomiro	ここをみろ	full	要件 031 (声の文書で打った同じ形)
  asitamiro	あしたみろ	full	要件 031 (声の文書で打った同じ形)
  What are things made you happy?	What are things made you happy?	full	本家 #2258
  made you happy	made you happy	full	要件 031 (声の文書で打った同じ形。文頭)
  it made you happy	it made you happy	full	要件 031 (声の文書で打った同じ形)
  /usr/bin/php	/usr/bin/php	full	本家 #2254
  ```
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req031_Upstream1123Shapes_StayKanaOrEnglish` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [ ] わざと崩した木で、足した 10 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過。
- [ ] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` と `docs/requirements/README.md` の一覧には足さない (019〜025 と同じ扱い)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `nonbirimiroyo` + Enter → `のんびりみろよ` (Space の候補に `のんびり見ろよ` が出るか)
- [ ] TextEdit: `nonbirimiro` + Enter → `のんびりみろ` (打っている途中の変換ボックスの表示も見る)
- [ ] TextEdit: `What are things made you happy?` + Enter → 英字のまま (文頭で打ち、途中で `まで` が入力欄に入って後で書き直されるかも見る)
- [ ] ターミナル: `/usr/bin/php` + Enter → `/usr/bin/php` (先頭の `/` が `・` にならない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
