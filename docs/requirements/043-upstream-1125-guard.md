---
title: 本家 11-24〜25 の誤判定の 3 形 (dai1dan・keihaitowagon・101kenijouattara。fork ではかなのまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-25.md 候補 2)
source: docs/voice/2026-11-25.md (ブランチ voice/2026-11-25) 候補 2 / 本家 #2379・#2365・#2359
scope: core
---

# 043 本家 11-24〜25 の誤判定の 3 形 (dai1dan・keihaitowagon・101kenijouattara。fork ではかなのまま) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (1.7.1、Windows 11) に 2026-11-24〜25 に出た報告 (docs/voice/2026-11-25.md の要約。本文は WebFetch の要約を通したもので、細部は原文と違う可能性がある):
  - #2379 `dai1dan` → `だi1dan` (期待 `第1弾`。数字を挟むと前後が英字に割れる。本家の bot が main でも再現、open)
  - #2365 `keihaitowagon` → Zenzai で `軽ハイとワゴン` (期待 `軽ハイトワゴン`。not planned で close)
  - #2359 `101kenijouattara` → `101家に城あったら` (期待 `101件以上あったら`。打ち間違いの補正の要望で、本家は「ローマ字どおりで正しい」として close)
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-26、`origin/main` f653ce5): この fork では確定はどれも英字に割れない。
  - `dai1dan` → `だい1だん`、`dai3dan` → `だい3だん`、`dai10dan` → `だい10だん`、`1dan` → `1だん`、`dai2kai` → `だい2かい`
  - `keihaitowagon` → `けいはいとわごん`
  - `101kenijouattara` → `101けにじょうあったら`、`101kennijouattara` → `101けんいじょうあったら`
- 正解表に数字を挟んだ `第1弾` 型の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で英字に割れる形になっても、回帰ゲートで気付けない (要件 019・020・022・023・025・041 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`)・`src/Meltype.Core.Tests`・`docs` を `dai1dan`・`keihaitowagon`・`kenijou` で探すと 0 件 (2026-11-26)。
- 打っている途中の表示 (`showing`) は `だい1だn`・`けいはいとわごn` で、最後の `n` だけ英字。確定 (Enter) では `ん` になる (ふつうの `n` の扱い)。ここでは確定だけを守る。

## minimum の要件 (これだけは満たす)
1. 上の形を、今の出力 (Enter の確定) のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は確定が崩れないので直すものが無い)。
- 漢字・カタカナの形 (`第1弾`・`軽ハイトワゴン`・`101件以上`。偽の変換エンジンでは測れない)。
- 打ち間違いの補正 (`kenijou` → `件以上`)。本家と同じく要件の外。
- docs/voice/2026-11-25.md の候補 1 (`check-release.py` の cask の検査。scope: swift) と候補 3 (変換中の ⌘S など。実機でしか確かめられない)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる (要件 041 と同じ)。
- [x] 正解表: 新しい表 `eval/cases/upstream-guards-1125.tsv` に次の行を足し、今の main でも当たる (019〜041 と同じく、日付ごとの別の表にする)
  ```
  dai1dan	だい1だん	full	本家 #2379
  dai3dan	だい3だん	full	要件 043 (声の文書で打った同じ形)
  dai10dan	だい10だん	full	要件 043 (声の文書で打った同じ形。2 桁)
  1dan	1だん	full	要件 043 (声の文書で打った同じ形。数字が先頭)
  dai2kai	だい2かい	full	要件 043 (要件の書き手の作例: 同じ形)
  keihaitowagon	けいはいとわごん	full	本家 #2365
  101kenijouattara	101けにじょうあったら	full	本家 #2359
  101kennijouattara	101けんいじょうあったら	full	要件 043 (声の文書で打った同じ形。打ち間違いの無い形)
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req043_Upstream1125Shapes_StayKana` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [x] わざと崩した木で、足した 8 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過。
- [x] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` と `docs/requirements/README.md` の一覧には足さない (019〜041 と同じ扱い)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `dai1dan` + Enter → `だい1だん` (英字に割れない。Space の候補に `第1弾` が出るか)
- [ ] TextEdit: `keihaitowagon` + Enter → `けいはいとわごん` (Space の候補に `軽ハイトワゴン` が出るか)
- [ ] TextEdit: `101kenijouattara` + Enter → `101けにじょうあったら` (英字に割れない)
- [ ] TextEdit: 上の 3 つを打っている途中の変換ボックスで、数字の前後が英字のまま入力欄に入らないか

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- わざと崩した木 (作業ツリーで一時的に変え、テスト `Req043` と新しい表だけを `--expect-all` で回し、`git checkout` で戻した。2026-11-26):
  | 崩し方 | 外れた行 → 確定 |
  |---|---|
  | `src/Meltype.Core/Detection/RomajiDetector.cs` の `("だ", ["da"])` を `daq` に | `dai1dan` → `dあい1dan` (本家 #2379 の `だi1dan` に近い、数字の前後が英字の形)、`dai3dan` → `dあい3dan`、`dai10dan` → `dあい10dan`、`1dan` → `1dan`、`dai2kai` → `dあい2かい` |
  | 同じファイルの `("け", ["ke"])` を `keq` に | `keihaitowagon` → `kえいはいとわごん`、`101kenijouattara` → `101kえにじょうあったら`、`101kennijouattara` → `101kえんいじょうあったら` |
  - どちらの崩し方でもテストは赤 (例: `dai1dan 期待値: だい1だん / 実際: dあい1dan`)、戻した後は緑。8 行とも少なくとも 1 つの崩し方で外れた。
  - 外れなかった崩し方 (行が守る範囲の注): `dictionaries/english.txt` に `dan`・`dai`・`wagon` を足す (8 行とも当たったまま)。英単語の一覧に語が増えるだけでは、この形は英字に割れない。
  - 崩し方は「その行が実際に比べられていること」を確かめるためのもので、本家の原因 (未確認) を再現したものではない。
- 数字: Core 1359/1359 (スキップ 8 件: 権限で失敗させるテスト 5・ファイルシステムの条件 1・環境変数が無いもの 2 (`MELTYPE_TEST_CONVERSIONS_COPY`・`MELTYPE_TEST_USERDICT_COPY`))、eval-compare (origin/main f653ce5 と比較) broken 0 / fixed 0 / added 0 / removed 0 (812/913。新しい表は 8/8。両方の木を同じ表で回すので base でも当たる)。`src/Meltype.Core`・`dictionaries`・`mac/` の差分 0 行 (辞書は変えていないので `check-dictionaries.mjs`・`check-terms.mjs` は対象外)。
- `docs/KNOWN_LIMITS.md` に `dai1dan`・`keihaitowagon`・`kenijou` の記載なし (確認のみ)。
- 査読 1 周目 (実装を見ていない別のエージェント。Core 1359/1359・eval-compare broken 0 / fixed 0 / added 0 / removed 0 (812/913) を再実行、8 行を HEAD と origin/main の `--repro` で打ち直して一致、崩した木 (da→daq・ke→keq・english.txt に 3 語) を別の worktree で再現し記録と一致、近い境界例 40 入力 (`dai1kai`・`dai100dan`・`2dan`・`dai1dan desu`・`dai1dan2dan` など) で HEAD と origin/main の違い 0): [止める] なし。
  - [直す・対応] スキップの件数が違っていた (出力の末尾だけを見て 4 件と書いた。実際は 8 件) → 内訳つきで書き直した。`docs/LESSONS.md` に 1 行足した。
  - [直す・対応] `docs/AGENT_STATE.md` が未更新 → 進行中の要件に 043 の行を足した。
  - [任意・対応] `101kenijouattara` の行は、将来打ち間違いの補正を入れると改善でも broken に出る → 表の頭の注に「補正を入れるときは、この行の期待を書き換える」と書いた。
