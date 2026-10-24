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
- 001 短い英単語の直後の「の」が英語に吸われる (`docs/requirements/001-api-no-error.md`、status は approved のまま)。ループは終了。Pull Request はこのあと人が作る。Mac 実機チェックリストは未確認 (人が確かめる)。
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
