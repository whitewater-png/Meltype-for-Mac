---
title: 本家 11-23 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-23.md 候補 1)
source: docs/voice/2026-11-23.md (ブランチ voice/2026-11-23) 候補 1 / 本家 #2261・#2258・#2254
scope: core
---

# 041 本家 11-23 の誤判定の 3 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る

番号について: 定期タスクは 031 (ブランチ `req/031-upstream-1123-guard`) で切ったが、main には別の要件 031 (変換中の Delete で学習を忘れる) が入ったので、統合のときに 041 へ付け替えた。

## 背景 (誰が、何に困っているか)
- 本家 (1.7.x。#2261・#2258 は Windows、#2254 は OS 不明) に 2026-11-23 に出た報告 (docs/voice/2026-11-23.md の要約。本文は WebFetch の要約を通したもので、細部は原文と違う可能性がある):
  - #2261 `nonbirimiroyo` → `のんびりmiroよ` (期待 `のんびり見ろよ`。動詞の命令形 `miro` が英字で残る)
  - #2258 `What are things made you happy?` → `What あれ things まで 用 happy?` (英文の途中が かな・漢字になる。Zenzai ON。本家の bot の main・1.7.1 では再現せず)
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
- [x] 正解表: 新しい表 `eval/cases/upstream-guards-1123.tsv` に次の行を足し、今の main でも当たる (019〜025 と同じく、日付ごとの別の表にする)
  ```
  nonbirimiroyo	のんびりみろよ	full	本家 #2261
  kokowomiroyo	ここをみろよ	full	要件 041 (要件の書き手の作例: 同じ形)
  asitamiroyo	あしたみろよ	full	要件 041 (要件の書き手の作例: 同じ形)
  nonbirimiro	のんびりみろ	full	要件 041 (声の文書で打った同じ形。語の終わりで止める)
  kokowomiro	ここをみろ	full	要件 041 (声の文書で打った同じ形)
  asitamiro	あしたみろ	full	要件 041 (声の文書で打った同じ形)
  What are things made you happy?	What are things made you happy?	full	本家 #2258
  made you happy	made you happy	full	要件 041 (声の文書で打った同じ形。文頭)
  it made you happy	it made you happy	full	要件 041 (声の文書で打った同じ形)
  /usr/bin/php	/usr/bin/php	full	本家 #2254
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req041_Upstream1123Shapes_StayKanaOrEnglish` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [x] わざと崩した木で、足した 10 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過。
- [x] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` と `docs/requirements/README.md` の一覧には足さない (019〜025 と同じ扱い)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `nonbirimiroyo` + Enter → `のんびりみろよ` (Space の候補に `のんびり見ろよ` が出るか)
- [ ] TextEdit: `nonbirimiro` + Enter → `のんびりみろ` (打っている途中の変換ボックスの表示も見る)
- [ ] TextEdit: `What are things made you happy?` + Enter → 英字のまま (文頭で打ち、途中で `まで` が入力欄に入って後で書き直されるかも見る)
- [ ] ターミナル: `/usr/bin/php` + Enter → `/usr/bin/php` (先頭の `/` が `・` にならない)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- わざと崩した木 (作業ツリーで一時的に変え、テスト `Req041` と新しい表だけを `--expect-all` で回し、`git checkout` で戻した。2026-11-24):
  | 崩し方 | 外れた行 → 確定 |
  |---|---|
  | `src/Meltype.Core/Detection/RomajiDetector.cs` の `("み", ["mi"])` を `miq` に | `kokowomiroyo` → `ここをmiroよ`・`asitamiroyo` → `あしたmiroよ` (本家 #2261 と同じ形)、`nonbirimiroyo` → `のんびrimいろよ`、`kokowomiro` → `ここをmiro`、`asitamiro` → `あしたmiro`、`nonbirimiro` → `のんびrimいろ` |
  | `dictionaries/english.txt` から `made`・`happy`・`are`・`what` を消す | `What are things made you happy?` → `What あれthings までようhappy?` (本家 #2258 に近い形)、`made you happy` → `までようはっpy`、`it made you happy` → `it までようはっpy` |
  | `src/Meltype.Core/Composition/CompositionDetector.cs` の `ProtectedEnd` で `isPath` を常に false に | `/usr/bin/php` → `/usr/びん/php` |
  - どの崩し方でもテストは赤 (例: `nonbirimiroyo 期待値: のんびりみろよ / 実際: のんびrimいろよ`)、戻した後は緑。10 行とも少なくとも 1 つの崩し方で外れた。
  - 外れなかった崩し方 (行が守る範囲の注): `english-words.txt` に `miro` を足す・`english-words.txt` から `made`・`you` を消す・`english.txt` から `made`・`happy` だけを消す (このときは `What are things made you happy?` は英字のまま)・`CompositionText.cs` の先頭の `/` を中黒にする条件を「設定と前の文字に関係なく」にする (パスの保護区間が英字の区間にして `/` を打ったまま出すので、`/usr/bin/php` は崩れない)。本家 #2254 の `・` の形そのものは、この fork の崩し方では再現できていない。
  - 崩し方は「その行が実際に比べられていること」を確かめるためのもので、本家の原因 (未確認) を再現したものではない。
- 数字: Core 1305/1305 (権限で失敗させるテストはスキップ)、eval-compare (origin/main 9f28603 と比較) broken 0 / fixed 0 / added 0 / removed 0 (745/849。新しい表は 10/10。両方の木を同じ表で回すので base でも当たる)。`check-dictionaries.mjs`・`check-terms.mjs` エラー 0 (警告は main と同じ。辞書は変えていない)。`src/Meltype.Core`・`dictionaries`・`mac/` の差分 0 行。
- `docs/KNOWN_LIMITS.md` に `miro`・`made you`・`/usr/bin/php` の記載なし (確認のみ)。
- `/usr/bin/php` は表とテストでは一般アプリとして打つ。査読員が `# app: code` の表で確かめ、コード系アプリでも `/usr/bin/php`・`cd /usr/bin/php`・`ls /usr/local/bin/node`・`cd ~/bin` は当たった (表には足していない)。
- 査読 1 周目 (実装を見ていない別のエージェント。Core 1305/1305・eval-compare broken 0 / fixed 0 / added 0 / removed 0 (745/849) を再実行、10 行の `--repro` を照合、崩した木は `miq`・`isPath`・`english.txt` の 4 語を別の worktree で再現し記録と一致。境界例 (`tabero`・`okiroyo`・`What made you happy?`・`/usr/local/bin/node` など) で要件の記述との矛盾なし): [止める] なし。
  - [直す・対応] `docs/AGENT_STATE.md` が未更新 → 進行中の要件に 041 の行を足した。
  - [直す・対応] 背景の「Windows の報告」が #2254 (OS 不明) にもかかっていた → 件ごとに書き分け、#2258 に「bot の main・1.7.1 では再現せず」を足した。
  - [任意・対応] 出典の「作者の作例」が本家の作者と紛らわしい → 「要件の書き手の作例」に直した (表も)。
  - [任意・対応] 実機チェックリストの `/usr/bin/php` はターミナルだが表は一般アプリ → 上の 1 行を記録に足した。
  - [任意・範囲外] 近い形で今の main が崩れるもの (main と同じ。一般アプリの `--repro`): 日本語と空白の後のパス `sore ha /usr/bin/php desu` → `それは/usr/びん/php です`、`sore ha /tmp/foo desu` → `それは/tmp/ふぉおです`、`kore ha ./build.sh desu` → `これは。/build.sh です`、`phpha/usr/bin/phpniaru` → `phpは/usr/びん/phpにある`、一般アプリの `cd /usr/bin/php` → `cd/usr/びん/php`。本家 #2254 の形 (先頭の `/` が `・`) ではないので、この要件では扱わない (次の声の文書の候補にするかは人が決める)。
- 査読 2 周目 (1 周目とは別のエージェント。Core 1305/1305・eval-compare broken 0 / fixed 0 / added 0 / removed 0 (745/849) を再実行、崩した木は `miq` + `isPath` と `english.txt` の 4 語を別の worktree で再現し記録と一致、範囲外の例 5 つを `--repro` で打ち直して一致): [止める]・[直す] なし。
  - [任意・対応] `docs/AGENT_STATE.md` の 041 の行に査読の周回数が無い → 2 周と書いた。
  - [任意・範囲外] `docs/AGENT_STATE.md` の基準の数字の Core 件数が 1301 (030 の後) のまま → 基準の数字は測った HEAD と組で書く表なので、この要件では変えない (次に基準を測り直すときに直す)。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/33
