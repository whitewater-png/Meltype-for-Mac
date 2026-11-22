---
title: 本家 11-21 の誤判定の 6 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-21.md 候補 2)
source: docs/voice/2026-11-21.md (ブランチ voice/2026-11-21) 候補 2 / 本家 #2174・#2157・#2147・#2143・#2141・#2168
scope: core
---

# 023 本家 11-21 の誤判定の 6 形 (fork ではかなのまま・英字のまま) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (主に 1.7.0) に 2026-11-21 に出た報告 (docs/voice/2026-11-21.md の要約。#2174・#2168 以外は一覧の題だけで、個別のページは読んでいない):
  - #2174 `atude` → `at腕` (期待 `厚手`)
  - #2157 `tasinamu` → `足しなmu`
  - #2147 `haatu` → `はatう`
  - #2143 `getu` → `getう`
  - #2141 `subewomotanai` → `すbeを持たない`
  - #2168 `Yukishiro` → `Yukiしろ`
- どれも途中や頭に英字として読める並び (`at`・`mu`・`get`・`be`・`Yuki`) を含み、本家では英字が残った形になった。
- 今の挙動 (Core の `--repro` と `--segments`、最後のキーは Enter、2026-11-22、`origin/main` 20736db): この fork ではどれも起きない。
  - `atude`・`atsude` → `あつで` (`JA:atude`)、`atudenoko-to` → `あつでのこーと`
  - `tasinamu`・`tashinamu` → `たしなむ` (`JA:tasinamu`)
  - `haatu` → `はあつ` (`JA:haatu`)、`haatudesu` → `はあつです`
  - `getu` → `げつ` (`JA:getu`)、`getuyoubi` → `げつようび`
  - `subewomotanai` → `すべをもたない` (`JA:subewomotanai`)
  - `Yukishiro` → `Yukishiro` (`EN:Yukishiro`。大文字で始まる名前は英字のまま)、`yukishiro` → `ゆきしろ`
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で英字が混ざる形に崩れても、回帰ゲートで気付けない (要件 019・020・022 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`) と `docs/KNOWN_LIMITS.md` を `atude`・`tasinam`・`haatu`・`getu`・`subewo`・`Yukishiro` で探すと 0 件 (2026-11-22)。開いている PR (#20・#23〜#27) のブランチの `eval/cases/` にも同じ打鍵の行は無い。
- 本家の扱い: #2157・#2143・#2141・#2168 は本家で修正済みとして閉じた (一覧の題から。直し方は未確認。クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 上の形を、今の出力のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字の形 (`厚手`・`嗜む`・`月` など。偽の変換エンジンでは測れない)。
- 大文字で始まる名前に日本語が続く形 (`Yukishirodesu` は今 `Yukishirodesu` と全部英字。期待を人が決める必要があるので守らない)。
- docs/voice/2026-11-21.md の候補 1 (`verdeha` の `r` が確定で消える。打ち間違いの直し (`RomajiTypoCorrector`) が `r` を余計な 1 文字として消していて、表示と確定を合わせると打ち間違いの直しが効かなくなる形が出る。どちらを優先するかを人が決める必要がある) と候補 3 (括弧の幅。どの幅にするかを人が決める)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [ ] 正解表: 新しい表 `eval/cases/upstream-guards-1121.tsv` に次の行を足し、今の main でも当たる (新しい表にするのは、開いている PR #24・#25・#27 が別の名前の守りの表を新しく作っていて、同じファイルを作ると衝突するため。どれも main に入ったら 1 つにまとめてよい)
  ```
  atude	あつで	full	本家 #2174
  atsude	あつで	full	要件 023 (作者の作例: tsu の綴り)
  atudenoko-to	あつでのこーと	full	要件 023 (作者の作例: 後ろに日本語が続く形)
  tasinamu	たしなむ	full	本家 #2157
  tashinamu	たしなむ	full	要件 023 (作者の作例: shi の綴り)
  haatu	はあつ	full	本家 #2147
  haatudesu	はあつです	full	要件 023 (作者の作例: 後ろに日本語が続く形)
  getu	げつ	full	本家 #2143
  getuyoubi	げつようび	full	要件 023 (作者の作例: 後ろに日本語が続く形)
  subewomotanai	すべをもたない	full	本家 #2141
  Yukishiro	Yukishiro	full	本家 #2168
  yukishiro	ゆきしろ	full	要件 023 (作者の作例: 小文字で打った形)
  ```
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req023_Upstream1121Shapes_StayAsTyped` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [ ] わざと崩した木で、足した 12 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過。
- [ ] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` と `docs/requirements/README.md` の一覧には足さない (開いている PR が同じ場所に行を足していて衝突するため。019・020・022 と同じ扱い)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `atude` + Enter → `あつで` (Space の候補に `厚手` が出るか)
- [ ] TextEdit: `tasinamu` + Enter → `たしなむ`、`haatu` + Enter → `はあつ`
- [ ] TextEdit: `getu` + Enter → `げつ`、`subewomotanai` + Enter → `すべをもたない`
- [ ] Safari のフォーム: `Yukishiro` + Enter → `Yukishiro`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- (未記入)
