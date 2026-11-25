---
title: 本家 11-20 の報告の 6 形 (fork ではかなのまま) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-20.md 候補 3)   # 棚卸し 2026-11-25: 実機未確認
source: docs/voice/2026-11-20.md (ブランチ voice/2026-11-20) 候補 3 / 本家 #2072・#2077・#2085・#2091 (#1988)・#2094
scope: core
---

# 022 本家 11-20 の報告の 6 形 (fork ではかなのまま) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (1.6.0 か 1.7.0-rc.1、主に Windows 11) に 2026-11-20 に出た報告 (docs/voice/2026-11-20.md の要約。本文は WebFetch の要約を通したもの):
  - #2077 `sitaidesu` → `死体です` (期待 `したいです`)
  - #2085 `amatte` → `余って` (期待 `あまって`)
  - #2091 話し言葉の語 (`ちょ`・`やっぱ`・`ね` など。例は #1988 の `chomatte` → `苧麻って`)
  - #2072 `kakikomiera-` で候補の区切りを変えられない
  - #2094 数字 + `にちめ` が `2２time` のような形になる
- 本家の報告の中心は漢字の選び方・候補の操作で、Core のテスト (偽の変換エンジン) では測れない。ただしどれも途中に英字として読める並び (`sit`・`mat`・`era`・`time` など) を含む。
- 今の挙動 (Core の `--repro` と `--segments`、最後のキーは Enter、2026-11-21、`origin/main` 20736db): この fork ではどれも英字が混ざらず、かなのまま確定する。
  - `sitaidesu`・`shitaidesu` → `したいです` (`JA:sitaidesu`)
  - `amatte` → `あまって` (`JA:amatte`)、`amattekara` → `あまってから`
  - `chomatte`・`tyomatte` → `ちょまって` (`JA:chomatte`)
  - `yappane` → `やっぱね` (`JA:yappane`)
  - `kakikomiera-` → `かきこみえらー` (`JA:kakikomiera-`)、`kakikomiera-desu` → `かきこみえらーです`
  - `2nitime`・`2nichime` → `2にちめ` (`JA:2nitime`)、`10nitime` → `10にちめ`
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で英字が混ざる形に崩れても、回帰ゲートで気付けない (要件 018・019・020 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`) と Core のテストを `sitaidesu`・`amatte`・`chomatte`・`yappane`・`kakikomi`・`nitime`・`nichime` で探すと 0 件 (2026-11-21)。
- `docs/KNOWN_LIMITS.md` に該当の記載なし (K79 の `time`→ちめ はコード系アプリのコマンド名の話で、別の経路)。
- 本家の扱い: 直し方は未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 上の形を、今の出力のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字の形 (`したいです` か `死体です` か、`あまって` か `余って` か、`書込エラー`。偽の変換エンジンでは測れない)。
- 候補の区切りの変え方 (#2072。Swift と変換エンジン側)、Space を押したときの候補の出方 (#2094)。
- docs/voice/2026-11-20.md の候補 1 (かなの直後の記号の幅。どの幅にするかを人が決める必要がある)・候補 2 (ユーザー辞書の読みのローマ字。fork で起きるかが未確認で、直し方を人が決める必要があり、`userdict.txt` の形式に及ぶ恐れがある)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [x] 正解表: 新しい表 `eval/cases/upstream-guards-1120.tsv` に次の行を足し、今の main でも当たる (新しい表にするのは、開いている PR #24・#25 が `upstream-guards.tsv`・`upstream-guards-1118.tsv` を新しく作っていて、同じファイルを作ると衝突するため。どれも main に入ったら 1 つにまとめてよい)
  ```
  sitaidesu	したいです	full	本家 #2077
  shitaidesu	したいです	full	要件 022 (作者の作例: shi の綴り)
  amatte	あまって	full	本家 #2085
  amattekara	あまってから	full	要件 022 (作者の作例: 後ろに日本語が続く形)
  chomatte	ちょまって	full	本家 #2091・#1988
  tyomatte	ちょまって	full	要件 022 (作者の作例: tyo の綴り)
  yappane	やっぱね	full	本家 #2091
  kakikomiera-	かきこみえらー	full	本家 #2072
  kakikomiera-desu	かきこみえらーです	full	要件 022 (作者の作例: 後ろに日本語が続く形)
  2nitime	2にちめ	full	本家 #2094
  2nichime	2にちめ	full	要件 022 (作者の作例: chi の綴り)
  10nitime	10にちめ	full	要件 022 (作者の作例: 数字が 2 桁)
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req022_Upstream1120Shapes_StayKana` を足す (要点: 上の行を打って Enter の確定を完全一致で比べる。テスト用の判定器で)。
- [x] わざと崩した木で、足した 12 行がどれも少なくとも 1 回は外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0 (両方の木を同じ表で回すので、足した行は base でも当たり added・fixed は 0)、Core のテスト全件通過。
- [x] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。`eval/README.md` の「ファイルの役割」と `docs/requirements/README.md` の一覧には足さない (開いている PR が同じ場所に行を足していて衝突するため。019・020 と同じ扱い。表の役割は表の頭の注釈に書いた)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `sitaidesu` + Enter → `したいです` (Space の 1 番目の候補が `したいです` か `死体です` かも見る)
- [ ] TextEdit: `amatte` + Enter → `あまって`、`chomatte` + Enter → `ちょまって`
- [ ] TextEdit: `kakikomiera-` + Enter → `かきこみえらー` (Space で `書き込みエラー` が候補に出るか)
- [ ] TextEdit: `2nitime` + Space → 空白が入らず変換中のまま (`2日目` が候補に出るか)
- [ ] Safari のフォーム: `yappane` + Enter → `やっぱね`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- わざと崩した木 (作業ツリーで `src/Meltype.Core/Detection/RomajiDetector.cs` の綴りを 1 つずつ変えて、テスト `Req022` と 12 形の `--repro` を回し、`git checkout` で戻した。2026-11-21):
  | 崩し方 | 外れた行 → 確定 |
  |---|---|
  | `("た", ["ta"])` を `taq` に (た の綴りを外す) | `sitaidesu` → `sitあいです`、`shitaidesu` → `しがいです` |
  | `("ま", ["ma"])` を `maq` に | `amatte` → `あmatte`、`amattekara` → `あmatteから`、`chomatte`・`tyomatte` → `ちょmatte` |
  | `("ぱ", ["pa"])` を `paq` に | `yappane` → `やっpane` |
  | `("ら", ["ra"])` を `raq` に | `kakikomiera-` → `かきこみeraー`、`kakikomiera-desu` → `かきこみeraーです`、`amattekara` → `あまってかた` |
  | `("め", ["me"])` を `meq` に | `2nitime` → `2にtime`、`2nichime` → `2にちme`、`10nitime` → `10にtime` |
  - どの崩し方でもテストは赤 (例: `2nitime 期待値: 2にちめ / 実際: 2にtime`)、戻した後は緑。12 行とも少なくとも 1 つの崩し方で外れた。
  - 崩し方は「その行が実際に比べられていること」を確かめるためのもので、本家の原因 (未確認。中心は漢字の選び方) を再現したものではない。
- 周回数: 実装 1 周、査読 2 周 (1 周目は [止める] なし・[直す] 2 件でどちらも手続き: AGENT_STATE の更新、PR の作成。2 周目は指摘なし)。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/27
- 数字: Core 1260/1260、eval-compare (origin/main 20736db と比較) broken 0 / fixed 0 / added 0 (634/740。新しい表は 12/12。両方の木を同じ表で回すので base でも当たる)。`check-terms.mjs`・`check-dictionaries.mjs` エラー 0。辞書・判定ロジックは変えていない (`src/Meltype.Core`・`dictionaries`・`mac/` の差分 0 行)。
- 査読 1 周目 (別エージェント。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 を査読員が再実行。12 行を `--repro`・`--segments` で照合。崩した木は ま・め・ら の 3 つを再現し、出力は上の表と一致。た・ぱ は査読員は再現していない。開いている PR #20・#23・#24・#25・#26 のブランチと `git merge-tree` で合わせて衝突なし):
  - [直す・対応] `docs/AGENT_STATE.md` が未更新 → PR を作った後に 1 行足した。
  - [任意・見送り] テストは最初に外れた 1 形しか報告しない (019・020 と同じ書き方。外れた形の全体は正解表で見える)。
  - 査読員が作った境界例 38 形 (`1nitime`・`2nitimedesu`・`2kaime`・`sitaikedo`・`amattara`・`chomattekudasai`・`yappari`・`era-desu`・`kakikomiera-ga` など) は今の main でどれもかなのまま確定し、外れは見つからなかった。`2time` → `2ちめ` (数字 + 英単語の time とも読める。K79 の `time`→ちめ と同じ傾向) は外れと断定していない。
- 査読 2 周目 (1 周目とは別のエージェント。PR を作った後の文書の更新を確認。12 形と境界例 4 形 (`1nitime`・`era-desu`・`chomattekudasai`・`kakikomiera-ga`)・`2time` を `--repro` で照合、Core 1260/1260 を再実行。eval-compare と崩した木は 2 周目では流していない): [止める]・[直す]・[任意] とも指摘なし。
