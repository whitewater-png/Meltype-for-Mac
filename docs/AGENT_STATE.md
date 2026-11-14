# 現在地 (AI 開発ループの状態)

> 1 枚だけの「今どこか」。ループの終わりと、文脈が圧縮される前に更新する。履歴は git に任せ、ここには最新だけを書く。
> 話題ごとの詳しい引き継ぎは `docs/HANDOFF-*.md`、本家 PR の状態は `docs/UPSTREAM_PR_TRACKER.md`、設計と数字は `docs/IMPROVEMENT_PLAN.md`。

## 基準の数字 (2026-10-24、クラウド Linux、ブランチ feat/001-api-no-error)
| 項目 | 値 | コマンド |
|---|---|---|
| Core のテスト | 940 / 940 (スキップ件数は未確認) | `dotnet run -c Release --project src/Meltype.Core.Tests` |
| 品質テスト | 1270 / 1271 (99.9 %)。混在 39/39、英語 38/39。外れ: `my name is taro` のみ | `-- --eval` |
| 正解表 (4 表) | 289 / 320 (90.3 %)。mac-device 40/41、mac-protected-spans 45/46、mac-terminal 26/30、upstream-quality 178/203。eval-compare は broken 0 / fixed 3 | `tools/eval-compare.sh` |
| 規模 | 1 キー 0.086 ms (`[規模]`) | |

## 進行中の要件
- 002 ost が おst に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/002-short-english-words、PR #7 で main に統合済み)。定期タスクが docs/voice/2026-10-26.md の候補 1 から自動承認で着手。Core 1146/1146、eval-compare broken 0 / fixed 2。Mac 実機チェックリストはリョウさんが確認済み (2026-10-28。項目ごとの記録なし)。`gooddane` (本家 #1192) は判定ロジックの変更が要るため別の要件に残す。
- 003 (長いローマ字文が全部かなになることを Core で守るテスト): PR #8 で main に統合済み・Mac 実機チェックリストは未確認。守りのテストで判定ロジックは未変更。外れは出なかった (400 文字超でも全部かな)。
- 004 母音の無い英字 + ハイフンを長音にしない (ブランチ req/004-hyphen-english、PR 作成後に番号を記す)。docs/voice/2026-10-30.md 候補 2 から自動承認で着手。Core 1228/1228、eval-compare broken 0 / fixed 6。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機チェックリストは未確認。日本語の直後の `git-log` (候補 1) は別の要件に残す。
- 005 beep が べえp に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/005-beep-english)。docs/voice/2026-10-31.md 候補 1 から自動承認で着手。Core 1229/1229、eval-compare broken 0 / fixed 3。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機は `beep` + Enter だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。`beepon` は `べえぽん` のまま (`beep音` は漢字なので Core では測れない)。候補 2 (未確定中の機能キー、Swift) は人が実機で扱う要件に残す。
- 006 計算の候補で `^` (べき乗) を読む (ブランチ claude/wizardly-cori-v99xo5)。docs/voice/2026-11-01.md 追記の候補 2 から、リョウさんの指示 (2026-11-01) で着手。Core 1231/1231、eval-compare broken 0 / fixed 0 (555/661)、品質テスト 1294/1295 (base と同じ)。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機は `2^10` + Space の候補だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 008 iCloud・iTunes の頭の i がかなになる (`dictionaries/propernouns.txt` に 2 語。ブランチ req/008-icloud-english)。docs/voice/2026-11-04.md 候補 1 から自動承認で着手。Core 1229/1229、eval-compare broken 0 / fixed 4。別エージェントの査読 1 周 (止める指摘なし)。`english.txt` に小文字で足しても大文字入りは直らず、`propernouns.txt` に足した。Mac 実機は `iCloud` + Enter だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 010 karento が karenと になる (`japanese.txt` に `karento` + `CompositionDetector` の固有名詞 + 助詞の守りを 2 行。ブランチ req/010-karento、PR #15 で main に統合済み 2026-11-11)。docs/voice/2026-11-06.md 候補 2 から自動承認で着手。Core 全件通過、eval-compare broken 0 / fixed 3。別エージェントの査読 1 周 (止める指摘なし)。受け入れる変化: 小文字の人名 Karen + と もかなになる、`karent` + 子音・記号が `かrent` に崩れる (要件に記載。2026-11-11 リョウさんが受け入れた。独立査読で、新しい条件が効くのは同梱の辞書では karen + to の 1 組だけ、日本語の入力 80,554 件の変化 0 件。変わるのは karen を含む入力と、karent を含まない karento 系の打ち間違い 57 形)。Mac 実機は 2026-11-11 にリョウさんが確認 (`karento`・`Karento`・`karenda-` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 011 colab・collab・kaggle が英字にならない (`dictionaries/english.txt` に 3 語。ブランチ req/011-colab-english、PR #16 で main に統合済み 2026-11-11)。docs/voice/2026-11-07.md 候補 1・2 から自動承認で着手。Core 1255/1255、eval-compare broken 0 / fixed 5。別エージェントの査読 1 周 (止める指摘なし。約 372 万入力で打ち間違い補正の変化 0 件。独立査読 2026-11-11 の約 49 万入力でも日本語の変化 0 件)。Mac 実機は 2026-11-11 にリョウさんが確認 (`colab`・`collab`・`kaggle`・`korabo` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 012 プリプロセッサの指令名 (endif・elifndef・pragma) が崩れる (`dictionaries/english.txt` に 3 語。ブランチ req/012-preprocessor-english、PR #17 で main に統合済み 2026-11-11)。docs/voice/2026-11-08.md 候補 3 から自動承認で着手。最初は 5 語で、別エージェントの査読 2 周では指摘なしだったが、独立査読 (2026-11-11、約 49 万入力) で `undef` (〜んで の 179 語・18,136 形)・`ifndef` (1 語・24 形) が打ち間違いの補正を止めると分かり、リョウさんの判断で 2 語を外して作り直した (3 語の形は日本語の損失 0)。作り直した版は別の査読者が確認 (止める指摘なし)。origin/main を合流させた木で Core 1259/1259、eval-compare broken 0 / fixed 2。Mac 実機は 2026-11-11 にリョウさんが確認 (`#endif`・`#pragma`・`endifwo`・`undef`・`jibundeftotemo` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。`define` (全部かなに読める `でふぃね`) は判定ロジックの変更が要るので別の要件に残す。
- 013 Obsidian のウィキリンク `[[…]]` が `「「…」」` になる (`CompositionText.HalfWidthWikiLinks`、ブランチ req/013-wikilink-brackets、PR #18 で main に統合済み 2026-11-11)。docs/voice/2026-11-09.md 候補 1 から自動承認で着手。`[[` と続く `]]` だけ半角 (`[` 1 つ・閉じない `[[` は今までどおり)。Core 1255/1255、eval-compare broken 0 / fixed 4。別エージェントの査読 3 周 (止める指摘なし、直す 2 件対応済み。3 周目は指摘なし)。独立査読 (2026-11-11、約 4.7 万入力で `[[ ]]` を含まない入力の変化 0 件) で見つかったテストの穴を `"[[link]]"` の行で塞いだ (main への統合後 Core 1257/1257)。`==`・`%%` の幅は別の要件に残す。Mac 実機は 2026-11-11 にリョウさんが確認 (`[[link]]`・`kyouha[[nikki]]desu`・`"[[link]]"`・`[kagi]` を TextEdit で Enter、`[[Hello World]]` は確定のキーの記録なし。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 014 pixiv が ぴぃv に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/014-pixiv-english、PR #19 で main に統合済み 2026-11-11)。docs/voice/2026-11-10.md 候補 2 から自動承認で着手。Core 1255/1255、eval-compare broken 0 / fixed 5 (576→581/687)。別エージェントの査読 1 周 (止める指摘なし。境界例 25,159 入力で、pixiv を含まない入力の変化は 0 件。独立査読 2026-11-11 の約 49 万入力でも日本語の変化 0 件)。Mac 実機は 2026-11-11 にリョウさんが確認 (`pixiv`・`pixivnitoukou`・`pinku` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。候補 1 (`alt`・`esc` などキーの名前) と候補 3 (`onvif` の `ゔぃ`) は別の要件に残す。
- 017 re-toga (レートが) が英字のまま残る (本家 #1841): **査読 3 周で指摘が 0 にならず、PR は作らず止めた** (2026-11-14。ブランチ req/017-reto-ga に push 済み)。docs/voice/2026-11-13.md 候補 1 から自動承認で着手。`CompositionDetector.IsHyphenatedEnglishWord` で、入力の先頭 (直前が英語のときを除く) に全部小文字で打った 接頭辞 (co を除く) + `-` + 続き が「readings.txt の語 + 助詞 1 つ」と読めるなら英語にしない。同梱の辞書で変わるのは re・e × toga・sumo・rude の 6 形。Core 1260/1260、eval-compare broken 0 / fixed 5 (640→645/751)、品質テスト base と同じ。査読 1 周目 [止める] (英語の直後でも変わる → 絞った。要件の止める条件の文を後から書き直した)、2 周目 [直す] (`co-doha` の表示と確定のずれ → co を外した)、3 周目 [直す] 1 件 (要件の文の食い違い。反映済みだが、反映後の版は査読を受けていない)。Mac 実機チェックリストは未確認。続けるかはリョウさんの判断 (続けるなら、もう 1 度査読してから PR)。
- 007 pre が pれ に崩れる (本家 #1489): `english.txt` に `pre` を足す案 (PR #12) は **取り込まず閉じた** (2026-11-05、リョウさんが判断)。独立査読 (約 198 万入力) で、正しく打った日本語は変わらないが、1 打鍵多い打ち間違いの補正 (`koprekara`→これから、`oprenzi`→おれんじ など) が 89 語・229 形で効かなくなる (PR の記載は 9 形) ため。辞書への追加で日本語が悪化する語は入れない方針。`pre` の別の直し方 (直前が日本語のときは英語にしない等) は未着手の候補。ブランチ req/007-pre-english は残してある。
- 009 syi が sい になる (本家 #1464): CompositionTable に `しぃ` を足す案 (PR #14) は **取り込まず閉じた** (2026-11-11、リョウさんが判断)。独立査読で、し の打ち間違いの補正 (`desyita`→でした、`wakarimasyita`→わかりました、`sukosyi`→すこし など) が 4,296 語・16,625 形で効かなくなる (PR の記載は 3 例) ため。`しぃ` は今でも `sixi` で打てる。補正の側で直す案 (syi を shi と読めば語になるときは補正を優先する) は未着手の候補。ブランチ req/009-syi-kana は残してある。
- (無し)。001 は PR #5 として base ブランチ `ccr-27ad1b0c-qzazzd` にマージ済み (2026-10-24、人の判断)。main へは PR #4 に含めて出す。Mac 実機チェックリスト (PR #5 本文) は未確認。
- 実装した範囲: 英字 1 文字ずつ読む略語 (`loanwords.txt` の大文字語) + の + 英字の残る英単語。新しく英字になるのは `api`・`ide`・`uri`・`gui`・`who` の 5 語。

## 次の一手
- 人: `uri` (売り・瓜と読める) を英字にしてよいか判断する。
- 人: PR を作り、Mac 実機チェックリスト (TextEdit・ターミナル) を確かめて、マージ・要件の `done` を判断する。
- 別の要件にする候補: `my name is taro` (品質テストの最後の外れ)。
- 辞書の英単語 + 助詞 全般 (`sorenoerror` など) は今回の範囲外。広げるときは境界例を別モデルの査読員に作らせる (`docs/LESSONS.md`)。

## 1 件目の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 001 apinoerror → apiのerror |
| 所要時間 | 約 33 分 (2026-10-24 09:24:58Z〜09:57:45Z)。人の介入は要件の事実訂正 1 回のみ |
| 周回数 (implementer) | 2 |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、3.0 分) / implementer 2 回 (1 周目 sonnet 5.9 分、2 周目 opus 9.1 分。昇格理由: `Composition/` に触れる + 査読の「止める」指摘) / reviewer 2 回 (opus、6.6 分・4.9 分) / eval-guard 2 回 (sonnet、1.5 分・1.3 分) / doc-keeper 1 回。サブエージェント合計 8 回 |
| 手戻りの原因 | (1) 要件の背景を `--explain` だけで書いて「全体が英語」と誤認し、完成条件に既に通る行 (`urlnoerror`) を入れた。(2) 1 周目の実装が「辞書の英単語 + 助詞」全般に効き、`sorenoerror` → `soreのerror` などの回帰が出た。正解表にその形が無く eval-guard は通り、reviewer が境界例を自分で作って発見。2 周目で `loanwords.txt` の大文字略語に絞って解消。(3) 2 つのエージェントが同じブランチを worktree に取って衝突 (`--detach` で回避) |
