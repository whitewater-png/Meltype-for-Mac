<!-- seen: 45e14f86558aa6ea34f2c2ecd038ba95ca8c3c46 -->
# 本家追従の候補表

`tools/upstream_watch.py` が更新します (使い方は docs/DEVELOPMENT.md の「本家追従」)。新しい節が上です。
候補ファイル (`eval/candidates/`) の新しい正解ケースは、人が確かめてから `eval/cases/upstream-quality.tsv` に移します。

## 2026-10-16 (db433da..45e14f8、12 件)

| hash | 日付 | PR | 件名 | 分類 | 取り込み | 当たるか | 新ケース (当たり/件数) |
|---|---|---|---|---|---|---|---|
| 9a25e7c | 2026-10-14 | #651 | Merge pull request #651 from yksr-melt/bot/sponsors | 文書 | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | - | - |
| 300dc3c | 2026-10-14 | #649 | Merge pull request #649 from reyalka/main | Windows (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | - | - |
| 6389b2b | 2026-10-14 | #652 | Merge pull request #652 from yksr-melt/yksr-melt-patch-1 | 文書 | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | - | - |
| f881053 | 2026-10-14 | #551 | Merge pull request #551 from asutarisucu/fix/broken-data-file-backup | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 取り込み済み (P46-17、511839d。#551 の枝の 1 つ目 (`942e4a8`、読めない中身を `.broken` に残す) は 10-13 に統合 PR #560 の中で先に main に入り、この merge で入ったのは 2 つ目 (`67ee7a1`、残せなかったときの警告。6 ファイル) だけ。P46-17 は、この 2 つを `conversions.json`・`translations.json` に手で移植した (`config.json` の警告は取り込んでいない。追従表の P46-17 の行)) | 手移植 | - |
| 1b60c06 | 2026-10-14 | #631 | Merge pull request #631 from ayappy-mugicha/fix/545-exclamation-emoji | 判定・変換 (+Windows) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。見送り) | 手移植 | - |
| 0bf338d | 2026-10-14 | #518 | Merge pull request #518 from ryokky3/fix/486-fcitx5-duplicate-commit | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | 対象ファイルなし | - |
| ba27157 | 2026-10-14 | #534 | Merge pull request #534 from ayappy-mugicha/fix/231-ime | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 取り込み済み (P46-3、4420099) | 手移植 | - |
| 1a98928 | 2026-10-14 | #480 | Merge pull request #480 from harukikubota/codex/issue-432-mac-mode-shortcuts | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。見送り) | 対象ファイルなし | - |
| bee078c | 2026-10-14 | #636 | Merge pull request #636 from citrussoda-com/fix/617-shift-stuck-two-keyboards | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | そのまま | - |
| 38344bf | 2026-10-14 | #595 | Merge pull request #595 from asutarisucu/feat/ime-typed-keys | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。見送り) | 手移植 | - |
| 54100aa | 2026-10-14 | #658 | Merge pull request #658 from yksr-melt/fix/mac-boundary-tests-mode-sources | その他 | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | - | - |
| 45e14f8 | 2026-10-14 | #527 | Merge pull request #527 from asutarisucu/fix/511-private-window | 判定・変換 (+Windows) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md。対象外) | 手移植 | - |

「分類」の列は、`tools/upstream_watch.py` が merge の第 2 親との差も数えるため、実際の PR の変更ファイルより広く出ます (重複を除いたファイル数で、ツールが数えた数 → 第 1 親との差で数えた実際: #534 116 → 2・#518 118 → 1・#551 112 → 6・#636 27 → 2・#595 32 → 6・#480 26 → 11・#631 13 → 2・#649 2 → 1。残りの 4 行は同じ)。「当たるか」の値は第 1 親との差 (`git diff <merge>^1 <merge>`) で出しますが、この列を埋めるかどうかは「分類」が「判定・変換」かで決まるので、#518 と #480 の「対象ファイルなし」は、広く出た分類のせいで出ています (第 1 親との差だけで分類すると、#518 は Linux、#480 は本家 Mac で、この列は「-」)。ほかの 10 行の値は、第 1 親との差だけで分類しても同じです。分類の数え方 (`changed_files`) の直しは別の項目にしました。

新しい正解ケース (QualityTests.cs の追加行) はありません。

## 2026-10-14 (a4b6f1a..db433da、2 件)

| hash | 日付 | PR | 件名 | 分類 | 取り込み | 当たるか | 新ケース (当たり/件数) |
|---|---|---|---|---|---|---|---|
| c2ed84a | 2026-10-14 | #603 | Merge pull request #603 from ayappy-mugicha/fix/533-bio-break | 判定・変換 | 取り込み済み (P46-2、23d948c) | 手移植 | - |
| db433da | 2026-10-14 | #609 | Merge pull request #609 from ayappy-mugicha/fix/538-infobesity | 判定・変換 | 取り込み済み (P46-1、5ce1f23) | 手移植 | - |

新しい正解ケース (QualityTests.cs の追加行) はありません。
