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
- 013 Obsidian のウィキリンク `[[…]]` が `「「…」」` になる (`CompositionText.HalfWidthWikiLinks`、ブランチ req/013-wikilink-brackets)。docs/voice/2026-11-09.md 候補 1 から自動承認で着手。`[[` と続く `]]` だけ半角 (`[` 1 つ・閉じない `[[` は今までどおり)。Core 1255/1255、eval-compare broken 0 / fixed 4。別エージェントの査読 1 周 (止める指摘なし、直す 1 件対応済み)。`==`・`%%` の幅は別の要件に残す。Mac 実機チェックリストは未確認。
- 007 pre が pれ に崩れる (本家 #1489): `english.txt` に `pre` を足す案 (PR #12) は **取り込まず閉じた** (2026-11-05、リョウさんが判断)。独立査読 (約 198 万入力) で、正しく打った日本語は変わらないが、1 打鍵多い打ち間違いの補正 (`koprekara`→これから、`oprenzi`→おれんじ など) が 89 語・229 形で効かなくなる (PR の記載は 9 形) ため。辞書への追加で日本語が悪化する語は入れない方針。`pre` の別の直し方 (直前が日本語のときは英語にしない等) は未着手の候補。ブランチ req/007-pre-english は残してある。
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
