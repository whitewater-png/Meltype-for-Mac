<!-- seen: ae73a2bd9104a83af6f4147bad9d85e87a7cf6ab -->
# 本家追従の候補表

`tools/upstream_watch.py` が更新します (使い方は docs/DEVELOPMENT.md の「本家追従」)。新しい節が上です。
候補ファイル (`eval/candidates/`) の新しい正解ケースは、人が確かめてから `eval/cases/upstream-quality.tsv` に移します。

## 2026-10-24 (7b7044a..ae73a2b、20 件)

| hash | 日付 | PR | 件名 | 分類 | 取り込み | 当たるか | 新ケース (当たり/件数) |
|---|---|---|---|---|---|---|---|
| 9d0b259 | 2026-10-22 | #1046 | Merge pull request #1046 from yksr-melt/bot/sponsors | 文書 | 未 | - | - |
| 9b7744f | 2026-10-22 | #1000 | Merge pull request #1000 from yksr-melt/ccr-36a5875b-r6ytyv | 文書 | 未 | - | - |
| ecb2ba1 | 2026-10-22 | #642 | Merge pull request #642 from Ginji001/fix/mac-start-running-server | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| ee46590 | 2026-10-23 | #1099 | Merge pull request #1099 from yksr-melt/bot/sponsors | 文書 | 未 | - | - |
| fc2baf1 | 2026-10-23 | #1096 | Merge pull request #1096 from yksr-melt/bot/conflict-notice | Windows (+文書) | 未 | - | - |
| 29ca4d8 | 2026-10-23 | #967 | Merge pull request #967 from yksr-melt/homebrew-cask-1.3.1-pre.1 | Windows (+本家 Mac) (+文書) | 未 | - | - |
| 5849c66 | 2026-10-23 | #662 | Merge pull request #662 from A6P3W/feature/configurable-input-mode-shortcuts | 判定・変換 (+Windows) (+本家 Mac) (+文書) | 未 | 手移植 | - |
| 5dfa048 | 2026-10-23 | #1059 | Merge pull request #1059 from yksr-melt/fix/1040-meltype-brand | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 1c9aa7d | 2026-10-23 | #1060 | Merge pull request #1060 from yksr-melt/fix/968-kansouritsu | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| ade4963 | 2026-10-24 | #1119 | Merge pull request #1119 from yksr-melt/bot/sponsors | 文書 | 未 | - | - |
| 732b82f | 2026-10-24 | #1101 | Merge pull request #1101 from yksr-melt/claude/ci-code-quality | 判定・変換 (+Windows) (+本家 Mac) (+文書) | 未 | 対象ファイルなし | - |
| 8e28b36 | 2026-10-24 | #1108 | Merge pull request #1108 from yksr-melt/claude/codeql-ja | Windows (+文書) | 未 | - | - |
| 5aef5b6 | 2026-10-24 | #1100 | Merge pull request #1100 from yksr-melt/claude/beautiful-clarke-lzhq61 | 判定・変換 (+Windows) (+文書) | 未 | 対象ファイルなし | - |
| c33d8ff | 2026-10-24 | #983 | Merge pull request #983 from Serugu/fix/zenzai-runtime-http | 判定・変換 (+Windows) (+本家 Mac) (+文書) | 未 | 手移植 | - |
| dac365f | 2026-10-24 | #1082 | Merge pull request #1082 from aster-void/fix/f10-raw-after-resize | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 60e4c57 | 2026-10-24 | #1065 | Merge pull request #1065 from yksr-melt/fix/853-series | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 549ccf5 | 2026-10-24 | #1053 | Merge pull request #1053 from yksr-melt/fix/1028-todo-list | 判定・変換 | 未 | 手移植 | - |
| e8836ef | 2026-10-24 | #1127 | Merge pull request #1127 from yksr-melt/claude/wonderful-wozniak-e7h45m | 判定・変換 (+Windows) (+本家 Mac) | 未 | 手移植 | - |
| 7587cda | 2026-10-24 | #1129 | Merge pull request #1129 from yksr-melt/homebrew-cask-1.3.1-pre.2 | その他 | 未 | - | - |
| ae73a2b | 2026-10-24 | #645 | Merge pull request #645 from haru0416-dev/perf/windows-spell-checker | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |

新しい正解ケース (QualityTests.cs の追加行) はありません。

### 分類 (2026-10-24 手作業。65 コミット = PR 単位 20 件 + 枝の中のコミット 45 件)

本家 1.3.1-pre.2 への版上げ (#1127) は本家 Windows / 本家 Mac の版で、**この Mac 版の版 (1.2.3) とは無関係**。取り込まない。

| 区分 | 件数 | PR |
|---|---|---|
| Mac 版に効く修正 (取り込み勧め) | 3 | #1060 (P56)、#1065・#1053 (P57) |
| 判断が要る | 1 | #1082 (P58。土台の #678 が Mac に無い) |
| すでに Mac にある | 1 | #1059 (めるたいぷ→Meltype。Mac は 56e68e0) |
| Windows 専用 | 4 | #662 (入力モード切替キー設定)、#983 (Zenzai の HTTP 部品。Mac は Swift の ZenzaiModel)、#645 (WindowsSpellChecker の速度)、#1096 (conflict bot) |
| 本家 Mac のみ (この版に該当ファイルなし) | 1 | #642 (`mac/start-input-method.sh`。この版に無い) |
| 版上げ・cask | 3 | #1127 (1.3.1-pre.2)、#967・#1129 (Homebrew cask) |
| sponsors 自動更新 | 3 | #1046・#1099・#1119 |
| 文書・CI・bot | 4 | #1000 (AGENTS.md)、#1101 (CI 品質)、#1108 (CodeQL)、#1100 (bot) |

合計 20 件。

## 2026-10-21 (45e14f8..7b7044a、59 件)

| hash | 日付 | PR | 件名 | 分類 | 取り込み | 当たるか | 新ケース (当たり/件数) |
|---|---|---|---|---|---|---|---|
| 0b489dd | 2026-10-18 | #738 | Merge pull request #738 from yksr-melt/refactor/core-cleanup | 判定・変換 (+Windows) | 記録あり (UPSTREAM_PR_TRACKER.md) | 手移植 | - |
| 09345a5 | 2026-10-18 | #737 | Merge pull request #737 from yksr-melt/refactor/ibus-engine-key-handling | 判定・変換 (+Windows) (+Linux) | 未 | 対象ファイルなし | - |
| 03a3758 | 2026-10-18 | #768 | Merge pull request #768 from yksr-melt/ccr-4f85b611-262scl | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 21fb255 | 2026-10-18 | #858 | Merge pull request #858 from yksr-melt/claude/adoring-cerf-vz4e28 | 判定・変換 (+Windows) (+本家 Mac) (+Linux) | 未 | 手移植 | - |
| bc8e487 | 2026-10-18 | #622 | Merge pull request #622 from ayappy-mugicha/fix/424-yahoyaho | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md) | 手移植 | - |
| e1e6e59 | 2026-10-18 | #722 | Merge pull request #722 from harukikubota/codex/issue-594-shatsu | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 記録あり (UPSTREAM_PR_TRACKER.md) | そのまま | - |
| e912061 | 2026-10-18 | #879 | Merge pull request #879 from yksr-melt/ccr-c7f79795-kbps7j | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| af2d1a6 | 2026-10-19 | #871 | Merge pull request #871 from yksr-melt/claude/optimistic-dijkstra-urgb9i | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 98a2933 | 2026-10-19 | #884 | Merge pull request #884 from Serugu/fix/scored-uppercase-japanese | 判定・変換 (+Windows) | 未 | 手移植 | - |
| 537100d | 2026-10-19 | #522 | Merge pull request #522 from sotono-046/codex/fix-mac-input-source-duplicates | 判定・変換 (+Windows) (+本家 Mac) (+文書) | 未 | 対象ファイルなし | - |
| 145277f | 2026-10-19 | #888 | Merge pull request #888 from yksr-melt/claude/determined-franklin-9rcl9f | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 112e8ed | 2026-10-19 | #704 | Merge pull request #704 from Konayukiw/main | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| 19879b7 | 2026-10-19 | #625 | Merge pull request #625 from rankoi/feat/604-portable-edition | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 3ef2a38 | 2026-10-19 | #891 | Merge pull request #891 from yksr-melt/claude/determined-franklin-9rcl9f | 文書 | 未 | - | - |
| 8be537f | 2026-10-19 | #892 | Merge pull request #892 from yksr-melt/claude/determined-franklin-9rcl9f | Windows | 未 | - | - |
| 17c9ce8 | 2026-10-19 | #893 | Merge pull request #893 from yksr-melt/claude/web-without-r2 | Windows (+文書) | 未 | - | - |
| e3a5147 | 2026-10-19 | #894 | Merge pull request #894 from yksr-melt/claude/fix-877 | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| b342f65 | 2026-10-19 | #895 | Merge pull request #895 from yksr-melt/claude/fix-887 | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 386d35f | 2026-10-19 | #896 | Merge pull request #896 from yksr-melt/claude/fix-881 | 判定・変換 (+Windows) (+文書) | 未 | 手移植 | - |
| 7ae20f4 | 2026-10-19 | #897 | Merge pull request #897 from yksr-melt/claude/fix-862 | 判定・変換 (+Windows) (+文書) | 未 | 対象ファイルなし | - |
| 62e4554 | 2026-10-19 | #898 | Merge pull request #898 from yksr-melt/claude/fix-863 | 判定・変換 (+Windows) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| fef4bf3 | 2026-10-19 | #899 | Merge pull request #899 from yksr-melt/claude/fix-890 | 判定・変換 (+Windows) (+Linux) (+文書) | 未 | 手移植 | - |
| 14df3f5 | 2026-10-19 | #901 | Merge pull request #901 from yksr-melt/claude/fix-885 | 判定・変換 (+Windows) (+Linux) (+文書) | 未 | 手移植 | - |
| 34cecde | 2026-10-19 | #900 | Merge pull request #900 from yksr-melt/claude/fix-886 | 判定・変換 | 未 | 手移植 | - |
| 3807c68 | 2026-10-19 | #912 | Merge pull request #912 from asutarisucu/fix/security-review-bundle | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| bf78b27 | 2026-10-19 | #916 | Merge pull request #916 from yksr-melt/claude/determined-franklin-9rcl9f | 文書 | 未 | - | - |
| f5289aa | 2026-10-19 | #919 | Merge pull request #919 from yksr-melt/claude/web-motion | その他 | 未 | - | - |
| cf842e2 | 2026-10-19 | #921 | Merge pull request #921 from yksr-melt/claude/determined-franklin-9rcl9f | その他 | 未 | - | - |
| 9785b17 | 2026-10-19 | #922 | Merge pull request #922 from yksr-melt/claude/web-polish | Windows (+文書) | 未 | - | - |
| 800bec0 | 2026-10-19 | #923 | Merge pull request #923 from yksr-melt/claude/web-polish-2 | その他 | 未 | - | - |
| 767185b | 2026-10-19 | #929 | Merge pull request #929 from yksr-melt/codex/fix-issues-926-928 | 判定・変換 | 未 | 手移植 | - |
| 1910078 | 2026-10-19 | #930 | Merge pull request #930 from yksr-melt/codex/fix-pills-container | その他 | 未 | - | - |
| 461967a | 2026-10-19 | #933 | Merge pull request #933 from yksr-melt/claude/icon-snowflake-m | Windows (+本家 Mac) (+Linux) (+文書) | 未 | - | - |
| 8aee6f8 | 2026-10-19 | #932 | Merge pull request #932 from aster-void/linux-arm64-release | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| 0641d2b | 2026-10-19 | #934 | Merge pull request #934 from yksr-melt/codex/fix-linux-arm64-docs-932 | Linux (+文書) | 未 | - | - |
| c106d6c | 2026-10-19 | #802 | Merge pull request #802 from kuraneko1/fix/ibus-duplicate-commit-486 | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 4ffafe4 | 2026-10-19 | - | docs: Super Thanksにメインメンテナーを追加 | 文書 | 未 | - | - |
| 445976b | 2026-10-19 | - | docs(web): add main maintainer to Super Thanks | その他 | 未 | - | - |
| fbbc3fc | 2026-10-19 | - | Update README.md | 文書 | 未 | - | - |
| d9fe817 | 2026-10-19 | - | docs(web): standardize Super Thanks heading | その他 | 未 | - | - |
| 534c6ad | 2026-10-19 | #927 | Merge pull request #927 from yksr-melt/claude/happy-keller-bmm5nw | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| e8466f0 | 2026-10-19 | #913 | Merge pull request #913 from rankoi/fix/259-tsf-fullwidth-space | 判定・変換 (+Windows) (+本家 Mac) (+Linux) | 未 | 対象ファイルなし | - |
| 13d1241 | 2026-10-19 | #937 | Merge pull request #937 from yksr-melt/codex/apt-cask-support | Windows (+本家 Mac) (+Linux) (+文書) | 未 | - | - |
| 6310c44 | 2026-10-19 | #938 | Merge pull request #938 from yksr-melt/codex/fix-cask-publish-pr | Windows (+文書) | 未 | - | - |
| bc3574b | 2026-10-20 | #942 | Merge pull request #942 from joyamazaki/fix/macos-input-icon | 本家 Mac | 未 | - | - |
| f527248 | 2026-10-20 | #647 | Merge pull request #647 from haru0416-dev/perf/deferred-learning-saves | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 84bc56b | 2026-10-20 | #947 | Merge pull request #947 from eisenferik/perf/dictionary-memory-pr | 判定・変換 (+Windows) (+本家 Mac) | 未 | 手移植 | - |
| 8057f27 | 2026-10-20 | #524 | Merge pull request #524 from asutarisucu/fix/417-slash-middle-dot | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 022e747 | 2026-10-20 | #957 | Merge pull request #957 from yksr-melt/bot/sponsors | 文書 | 未 | - | - |
| 3e50152 | 2026-10-20 | #945 | Merge pull request #945 from aster-void/rpm | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| 9777498 | 2026-10-20 | #849 | Merge pull request #849 from yksr-melt/feat/zenzai-windows | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 手移植 | - |
| 51b02c3 | 2026-10-20 | #720 | Merge pull request #720 from harukikubota/linux-ime-ci-tests | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | そのまま | - |
| c1f3666 | 2026-10-20 | #950 | Merge pull request #950 from nurazon59/fix/homebrew-cask-checksum-update | 判定・変換 (+Windows) (+本家 Mac) (+Linux) (+文書) | 未 | 対象ファイルなし | - |
| e32b763 | 2026-10-20 | #962 | Merge pull request #962 from souta-lab/feat/mozc-ut-dictionary-support | Windows (+文書) | 未 | - | - |
| 5b4a97a | 2026-10-20 | #964 | Merge pull request #964 from yksr-melt/yksr/meltype-version-up | 判定・変換 (+Windows) (+本家 Mac) | 未 | 手移植 | - |
| 33c71c3 | 2026-10-20 | - | fix(mac): create Homebrew cask branch when it does not exist yet | Windows | 未 | - | - |
| ecaf80f | 2026-10-20 | - | fix(bot): dispatch release builds with the job's GITHUB_TOKEN | Windows | 未 | - | - |
| 4b80a7a | 2026-10-20 | #966 | Merge pull request #966 from yksr-melt/homebrew-cask-1.3.1-pre.1 | その他 | 未 | - | - |
| 7b7044a | 2026-10-21 | #998 | Merge pull request #998 from yksr-melt/bot/sponsors | 文書 | 未 | - | - |

新しい正解ケース (QualityTests.cs の追加行) はありません。

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
