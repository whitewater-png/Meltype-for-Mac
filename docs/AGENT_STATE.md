# 現在地 (AI 開発ループの状態)

> 1 枚だけの「今どこか」。ループの終わりと、文脈が圧縮される前に更新する。履歴は git に任せ、ここには最新だけを書く。
> 話題ごとの詳しい引き継ぎは `docs/HANDOFF-*.md`、本家 PR の状態は `docs/UPSTREAM_PR_TRACKER.md`、設計と数字は `docs/IMPROVEMENT_PLAN.md`。

## 基準の数字 (2026-10-24、クラウド Linux、コミット 74b7855 + テストのスキップ 2 件)
| 項目 | 値 | コマンド |
|---|---|---|
| Core のテスト | 938 / 938 (スキップ 6 件は環境依存) | `dotnet run -c Release --project src/Meltype.Core.Tests` |
| 品質テスト | 1269 / 1271 (99.8 %)。外れ: `my name is taro`、`apinoerror` | `-- --eval` |
| 正解表 (4 表) | 278 / 310 (89.7 %)。mac-device 40/41、mac-protected-spans 45/46、mac-terminal 16/20、upstream-quality 177/203 | `tools/eval-compare.sh` |
| 所要 | テスト 約 50 秒、eval-compare 約 13 秒 | |

## 進行中の要件
(無し)

## 次の一手
- 最初の要件定義書を 1 件書き、`/improve` を通して、この表の「1 件目の記録」を埋める。

## 1 件目の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | — |
| 所要時間 | — |
| 周回数 (implementer) | — |
| 役割ごとのモデルと起動回数 | — |
| 手戻りの原因 | — |
