---
title: 本家 11-17 の誤判定 5 形 (fork では起きない) を正解表とテストで守る
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-17.md 候補 2)
source: docs/voice/2026-11-17.md (ブランチ voice/2026-11-17) 候補 2 / 本家 #1984・#1985・#1987・#1990・#1991
scope: core
---

# 019 本家 11-17 の誤判定 5 形 (fork では起きない) を正解表とテストで守る

## 背景 (誰が、何に困っているか)
- 本家 (Linux 版 1.6.0) に 2026-11-17 に出た誤判定の報告 (docs/voice/2026-11-17.md の要約。本文は WebFetch の要約を通したもの):
  - #1984 `baguhoukoku` → `bag ウ報告` (英単語 bag を拾う)
  - #1985 `tunagarumademattekara` → `つながるまでmatteから`
  - #1987 `CLAUDE.mdwohiraku` → `CLAUDE.mdwo開く` (大文字だけのファイル名の後ろの助詞が英字のまま)
  - #1990 `,nisuruwo` → `、nisl を` (Zenzai でときどき)
  - #1991 `reibunn:pregnant_man:` → `reibun:pregnant_man:` (絵文字の記法 `:…:` の前まで英語になる)
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-18、`origin/main` 20736db): この fork では 5 つとも**起きない**。
  - `baguhoukoku` → `ばぐほうこく` (`--segments`: `JA:baguhoukoku`)
  - `tunagarumademattekara` → `つながるまでまってから` (`JA:tunagarumademattekara`。`tsunagaru…` も同じ)
  - `CLAUDE.mdwohiraku` → `CLAUDE.mdをひらく` (`EN:CLAUDE | EN:. | EN:md | JA:wohiraku`。`AGENTS.mdwohiraku` も同じ形)
  - `,nisuruwo` → `、にするを` (`JA:,nisuruwo`)
  - `reibunn:pregnant_man:` → `れいぶん:pregnant_man:` (`JA:reibunn: | EN:pregnant_man | JA::`)、`reibun:smile:desu` → `れいぶん:smile:です`
- 正解表に同じ形の行が無いので、後の変更 (辞書への語の追加・判定ロジックの変更・本家への追従) で本家と同じ崩れ方をしても、回帰ゲートで気付けない (要件 018 と同じ考え)。

## 今の動き (既に分かっていること)
- 正解表 (`eval/cases/*.tsv`) を `baguhoukoku`・`mattekara`・`CLAUDE.md`・`nisuruwo`・`pregnant`・`:smile:` で探すと 0 件 (2026-11-18)。近い行は `README.mdwo` (`mac-protected-spans.tsv`)・`chottomatte` (`upstream-quality.tsv`) だけ。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 直し方は未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 上の 5 形を、今の出力のまま正解表に守る行として足す。
2. 同じ行を Core のテストでも守る (Enter の確定を比べる)。

## 非目標 (今回はやらない)
- 判定ロジック・辞書の変更 (今は起きないので直すものが無い)。
- 漢字の形 (`バグ報告`・`繋がるまで待ってから`・`例文`。偽の変換エンジンでは測れない)。#1988 `chomatte` → `苧麻って` は漢字の選び方だけの話なので行にしない。
- 絵文字の記法 `:…:` の中を補う・絵文字に変える機能 (#1991 の提案の後半)。
- docs/voice/2026-11-17.md の候補 1 (数字の後の `,`。判定の変更で、数字だけのときの扱いを人が決める必要がある) と候補 3 (ターミナルの Claude Code。scope: swift で実機必須)。

## 完成条件 (先に決める。機械で判定できる形)
守りの行なので、今の main で最初から当たる。「先に赤」の代わりに、**わざと崩した木 (作業ツリーで一時的に変えて戻す) で、足した行とテストが外れること**を一度確かめる。
- [x] 正解表: 新しい表 `eval/cases/upstream-guards.tsv` に次の行を足し、今の main でも当たる (新しい表にするのは、開いている PR #20・#23 が `short-words-in-english.tsv` の末尾に行を足していて、同じ場所に足すと衝突するため)
  ```
  baguhoukoku	ばぐほうこく	full	本家 #1984
  tunagarumademattekara	つながるまでまってから	full	本家 #1985
  tsunagarumademattekara	つながるまでまってから	full	要件 019 (作者の作例: tsu の綴り)
  CLAUDE.mdwohiraku	CLAUDE.mdをひらく	full	本家 #1987
  AGENTS.mdwohiraku	AGENTS.mdをひらく	full	要件 019 (作者の作例: 大文字だけの別のファイル名)
  ,nisuruwo	、にするを	full	本家 #1990
  reibunn:pregnant_man:	れいぶん:pregnant_man:	full	本家 #1991
  reibun:smile:desu	れいぶん:smile:です	full	要件 019 (作者の作例: 記法の後ろに日本語)
  ```
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req019_Upstream1117Shapes_StayAsTheyAre` を足す (要点: 上の行を打って Enter の確定を比べる。テスト用の判定器 (`BuiltInWordChecker`) で)。
- [x] わざと崩した木で、本家の 5 形の行がそれぞれ外れ、テストが赤になる (崩し方と結果を記録欄に書く)。
- [x] 回帰なし: `tools/eval-compare.sh` の broken 0 (両方の木を同じ表で回すので、足した行は base でも当たり added・fixed は 0)、Core のテスト全件通過。
- [x] 文書: `eval/README.md` の「ファイルの役割」に新しい表の行を足す。`docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `baguhoukoku` + Enter → `ばぐほうこく` (Space で `バグ報告` が候補に出るか)
- [ ] TextEdit: `tunagarumademattekara` + Enter → `つながるまでまってから`
- [ ] TextEdit: `CLAUDE.mdwohiraku` + Enter → `CLAUDE.mdをひらく`
- [ ] Slack か Discord の入力欄: `reibunn:pregnant_man:` + Enter → `れいぶん:pregnant_man:` (アプリ側の絵文字の補完の窓と重ならないか)
- [ ] TextEdit: `,nisuruwo` + Enter → `、にするを`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 足した行のどれかが今の main で外れる (守りではなく直す要件になるので、判定ロジックの変更として別に扱う)。

## 記録 (ループが書く)
- 周回数: 実装 1 周、査読 2 周 (1 周目は [止める]・[直す] なし、[任意] 5 件。2 周目は文書の直しの確認)。
- わざと崩した木 (作業ツリーで 1 つずつ変えて、テスト `Req019` と正解表を回し、戻した。2026-11-18):
  | 崩し方 | 外れた行 → 出力 |
  |---|---|
  | `RomajiDetector.cs` の `("ぐ", ["gu"])` を `guq` に (ぐ の綴りを外す) | `baguhoukoku` → `bagうほうこく` |
  | `RomajiDetector.cs` の促音 (同じ子音の連続) の条件を外す | `tunagarumademattekara`・`tsunagarumademattekara` → `つながるまでmatteから` (本家 #1985 と同じ形) |
  | `CompositionDetector.cs` の「拡張子・ドメイン・英単語の後ろが助詞で始まる日本語なら、そこまで」の決まり全体を外す | `CLAUDE.mdwohiraku`・`AGENTS.mdwohiraku` → 打ったまま (`CLAUDE.mdwohiraku`) |
  | `CompositionText.cs` の `',' => … '、' : '，',` の行を `',' => ',',` に変える (`,` を半角のまま返す。行ごと消すと全角の `，にするを` になる: 査読員の再現) | `,nisuruwo` → `,にするを` |
  | `CompositionDetector.cs` の「名前に `_` があれば丸ごと英字」を外す | `reibunn:pregnant_man:` → `れいぶん:pregnant＿まん:` |
  - どの崩し方でもテストは赤 (例: `baguhoukoku 期待値: ばぐほうこく / 実際: bagうほうこく`)、戻した後は緑。
  - 崩し方は「その行が実際に比べられていること」を確かめるためのもので、本家の原因 (未確認) を再現したものではない。`,nisuruwo` (本家は Zenzai でときどき `、nisl を`) と `reibunn:pregnant_man:` (本家は前の日本語まで英語になる) は、本家と違う崩れ方で確かめた。
  - 崩れなかった試し: `SplitStems` から `md` を外すだけ・0a の助詞の行を外すだけでは `CLAUDE.mdwohiraku` は当たったまま (別の行が同じ分け方をする)。`reibun:smile:desu` は上のどの崩し方でも当たったまま (この行を外す崩し方は確かめていない)。
- 数字: Core 1260/1260、eval-compare (origin/main 20736db と比較) broken 0 / fixed 0 / added 0 (630/736。新しい表は 8/8。両方の木を同じ表で回すので base でも当たる)。`check-terms.mjs` エラー 0。辞書・判定ロジックは変えていない。
- 査読 1 周目 (別エージェント。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 を査読員が再実行。崩した木は ぐ・`_`・`,` の 3 つを再現): [止める] なし。[直す] なし。
  - [任意・対応] `,` の崩し方の書き方が具体的でない → 上の表に実際の編集を書いた。
  - [任意・対応] 正解表の出典の `docs/voice/2026-11-17.md` は main に無い → 表の頭の注釈に「ブランチ voice/2026-11-17」と書いた。
  - [任意・見送り] `docs/requirements/README.md` の一覧に 019 が無い (開いている PR #20・#23 が同じ末尾に行を足していて衝突するため。一覧は main でも 003・008・011〜013 が抜けている)。`docs/DEVELOPMENT.md`・`docs/AGENT_STATE.md` の「4 表」は main で既に古い (今回の差分で壊れたものではない)。
  - [任意・見送り] テストは最初に外れた行しか報告しない (守る力には影響しない)。正解表の `full` はカタカナをひらがなに直して比べるので `バグほうこく` のような崩れは正解表では見えず、完全一致のテストの方が捕まえる。
  - [任意・見送り] 足せる境界例 (今も望ましい出力: `baguwonaosu` → `ばぐをなおす`、`CLAUDE.mdnikaku` → `CLAUDE.mdにかく`、`SKILL.mdwohiraku`、`Makefilewohiraku`、`nisuruwo,` → `にするを、`、`reibunn:thumbsup:`、`mattete`) は同じ型なので足していない。
  - 査読員が見つけた今の main の外れ (この要件では直さない。守る行にもしない。別の要件の候補): `:pregnant_man:desu` → `:pregnant_まん:です` (行の先頭の記法が崩れる)、`reibunn:+1:` → `れいぶん:＋1:`。
