# 本家 main との比較表 (施策 3-2)

Meltype for Mac を、本家 (Windows 版 `yksr-melt/Meltype` の main) と並べて測り、リリースごとに更新する表です。計画書 [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の柱 A「測って下回らない」を続けるための運用枠で、**この文書は内部用です。README には載せず、push もしていません** (公開は §8 の 8 の決定 (a) にもとづき、リョウさんの許可を得てから)。

## 1. 目的と読み方

- 目的: 本家の判定修正を取り込み続けているか、取り込んだ上で Mac 固有の分を足せているかを、数字で見えるようにする。本家と競うためではなく、「本家の修正を取り込んだ上で Mac 固有分を足した結果」を確かめるための表です (§8 の 8 の協調的な書き方)。
- 現在値は、**実際に回して確かめたものだけ**を入れています。回していないものは「未計測」と書きます。
- 数字はすべて **擬似エンジン上の Core の結果**です (変換エンジンの azooKey も、Mac の入力欄も、macOS のスペルチェッカーも使いません)。実機の体感の代わりにはなりません。
- 「正解表」は `eval/cases/*.tsv` です。うち `upstream-quality.tsv` は本家のテスト (コミット `a4b6f1a` 時点) の写しなので、本家は自分の表では当然 100% です。Mac 側の表 (`mac-device.tsv`・`mac-terminal.tsv`) は Mac 版の側で作った表なので、そこでの Mac 版の高さは「Mac 固有分の確認」であって、本家との優劣ではありません。
- 本家に「勝った/負けた」と読まないでください。外れの内訳 (どれが本家の修正の取り込み待ちか) が肝心です。

## 2. 比較軸と現在値

測定日 2026-10-15。Mac 版 = このブランチ (`docs/p3-3-2`、`669ead5`。Core の版 1.2.0)。本家 = `origin/main` の `45e14f8` (2026-10-14、Core の版 1.2.1)。

| 軸 | 測り方 | 現在値 (Mac 版 / 本家) |
| --- | --- | --- |
| 判定精度 1: 本家の品質テスト | 各ツリーで `dotnet run -c Release --project src/Meltype.Core.Tests -- --eval`。**各ツリー自身のテスト集で測るので、母数が違う** | Mac 版 **1266/1268** (99.8%。外れは `my name is taro`・`apinoerror`) / 本家 **1337/1337** (100%)。**今は Mac 版が下回っている** (母数が 69 件違う。本家が `a4b6f1a` 以降に足した分が Mac 版に入っていない) |
| 判定精度 2: 同じ正解表を両方で回す | `tools/eval-compare.sh <本家の clone> . eval/cases <出力先>` (正解表は 264 件の判定: `upstream-quality.tsv` 203 + `mac-device.tsv` 41 + `mac-terminal.tsv` 20) | 全体 Mac 版 231/264 (87.5%) / 本家 243/264 (92.0%)。**broken (本家で当たり Mac 版で外れ) 28 件、fixed (逆) 16 件**。表ごと: `upstream-quality` 175/203 (86.2%) / 203/203。`mac-device` 40/41 (97.6%) / 25/41 (61.0%)。`mac-terminal` 16/20 (80.0%) / 15/20 (75.0%) |
| 判定精度 2 の broken 28 件の内訳 | `compare.md` の broken 表 | すべて `upstream-quality.tsv`。`my name is taro`、`apinoerror`、`inviteshimashita`、`yapython` 型 6 件、`devtoprod` 型 5 件、`site PR`、`*pedia` 型 8 件、`i18n`・`l10n`・`h2o` など。**どの本家 PR に当たるかの対応づけは未実施**で、取り込み待ちか意図して取り込まないかの仕分けは未計測 |
| 判定精度 3: Mac 固有コーパスでの上乗せ | 判定精度 2 の `mac-device.tsv` / `mac-terminal.tsv` の行 | 上の表のとおり。`mac-terminal.tsv` の外れ 4 件は「今は外れると分かっている」例を含めた表 (`eval/README.md`) |
| 判定精度 4: 実機の体感・かな漢字変換 | リョウさんの実機 (「打った>結果」形式で `mac-device.tsv` に足す)。かな→漢字は `eval/kanji/kanji-cases.tsv` (212 文) を Zenzai の計測で使う | **未計測** (本家との比較としては実機の数字は無い。Zenzai の計測結果は [ZENZAI_EXPERIMENT_2026-10.md](ZENZAI_EXPERIMENT_2026-10.md)) |
| 機能: 本家の変更の取り込み状況 | [UPSTREAM_WATCH.md](UPSTREAM_WATCH.md) の「取り込み」列と [UPSTREAM_PR_TRACKER.md](UPSTREAM_PR_TRACKER.md) の P 項目 | 本家 main は watch が最後に見た `db433da` より 28 コミット先 (未分類)。watch の候補表の 2 件はどちらも「未」。管理表の保留・未着手は P40〜P44。**機能の網羅的な比較表 (本家にあって Mac 版に無いもの/逆) は未作成 = 未計測** |
| 対応ホスト | [HOST_COMPAT.md](HOST_COMPAT.md) の 8 項目の打鍵表 (リョウさんの Mac で手動) | Mac 版: 7 アプリ確認済み (再変換が効かないのは Google ドキュメント・ターミナル。文字は壊れない)、VS Code・Slack は未確認。確認日 2026-10-14、ビルドは `446dd48`。**本家 (Windows 版) のホスト対応は対象外で、同じ打鍵表での比較は未計測** |
| 既知の制限 | [KNOWN_LIMITS.md](KNOWN_LIMITS.md) の「件数」の表 | Mac 版 58 件 (仕様 34 / 直す予定 9 / 直った 5 / 未確認 10)。本家の制限は同じ基準で数えていないので**未計測** |
| 参考: Mac 版のテスト件数 | `src/Meltype.Core.Tests` の通常実行 | 今回は再実行していない (直近の記録は IMPROVEMENT_PLAN の 1 音確定の節: 659/661、環境由来の 2 件を除き通過) |

### 数字の注意

- 判定精度 1 と 2 は、母数と中身が違います (1 は各ツリー自身のテスト集、2 は同じ正解表)。並べて「どちらが上」と言うのは 2 だけです。
- 判定精度 2 の `upstream-quality.tsv` は、本家の `Before` つきの 13 件を判定していません (`eval/README.md`)。
- 本家 main の側は `--expect-all` を持つので、`tools/eval_legacy_base.py` は使われていません (2026-10-15 時点)。
- NSSpellChecker が絡む答えは Mac の環境で変わりうるのに対し、この表は同梱の英単語表で測っています。

## 3. リリースごとの更新手順

リリースの前に、次を順に行います。表の更新は、この文書の §2 と §4 を書き換えます。

1. [ ] 本家 main を取る。
   - 初回: `git clone https://github.com/yksr-melt/Meltype.git <置き場所>` (完全な履歴が要るので `--depth 1` は不可。置き場所は Mac 版のツリーの外)。
   - 2 回目以降: `git -C <置き場所> fetch origin` → `git -C <置き場所> merge --ff-only origin/main` (ローカルの clone は読むだけで、本家には何も送らない)。
   - `git -C <置き場所> log -1 --format='%H %ad %s' --date=short origin/main` を控える。
2. [ ] 本家の新しい修正を候補にする (取り込み作業の前段): `export MELTYPE_UPSTREAM=<置き場所>` → `python3 tools/upstream_watch.py` ([DEVELOPMENT.md](DEVELOPMENT.md) の「本家追従」)。`docs/UPSTREAM_WATCH.md` の「取り込み」が「未」の行を確かめ、取り込むものは今までの取り込み手順 (1 項目ずつ・独立査読) で行う。**取り込み前に比較表だけ更新して「下回っている」と記録するのは構いません** (むしろ現状を正直に書く)。
3. [ ] 同じ正解表で判定を回す (どちらも `bash --noprofile --norc -c '...'` の中で):
   - 母数が同じ比較: `tools/eval-compare.sh <置き場所> <Mac 版のツリー> eval/cases <出力先>`。終了コード 0 = broken なし、1 = broken あり、2 = 実行の失敗。`<出力先>/compare.md` に表ごとの数字と broken/fixed の一覧が出る。dotnet が各ツリーに `bin/obj` を作る (git の対象外)。
   - 各ツリー自身の品質テスト: `cd <ツリー> && dotnet run -c Release --project src/Meltype.Core.Tests -- --eval` を本家と Mac 版の両方で。先頭の「品質テスト: n/m」を控える。
   - 本家が `a4b6f1a` 以降に足した正解ケースは、`upstream_watch.py` が `eval/candidates/` に書く。**人が確かめてから** `eval/cases/upstream-quality.tsv` の末尾に移す ([DEVELOPMENT.md](DEVELOPMENT.md))。正解表を足したら、同じ表で本家側も回し直す (表が変われば §2 の母数も変わる)。
4. [ ] 実機の結果を足す (任意・リョウさんの作業): TextEdit などで打った結果を `eval/cases/mac-device.tsv` に足す ([eval/README.md](../eval/README.md))。私的な文は入れない。
5. [ ] §2 の表を更新する。本家と Mac 版の版・コミット、各数字、broken の件数と内訳。**数字が下がったら、そのまま書く**。確かめていない軸は「未計測」のままにする。
6. [ ] ホストの表 ([HOST_COMPAT.md](HOST_COMPAT.md)) と制限の表 ([KNOWN_LIMITS.md](KNOWN_LIMITS.md)) に今回の変更があれば、そちらを先に直して件数を写す。
7. [ ] §4 に 1 行足す: 測定日、Mac 版のコミットと版、本家のコミットと日付、品質テスト、同じ表の数字、broken/fixed の件数、メモ。
8. [ ] README への掲載・push・本家への連絡は、別にリョウさんの許可を得てから (この手順には含めない。§8 の 6・8)。

## 4. 更新履歴

| 測定日 | Mac 版 (コミット・版) | 本家 main (コミット・日付・版) | 品質テスト (Mac / 本家) | 同じ表 (Mac / 本家)、broken / fixed | メモ |
| --- | --- | --- | --- | --- | --- |
| 2026-10-15 | `669ead5` (docs/p3-3-2)・1.2.0 | `45e14f8`・2026-10-14・1.2.1 | 1266/1268 / 1337/1337 | 231/264 / 243/264、broken 28 / fixed 16 | 最初の枠。今は「下回らない」を満たしていない (本家の `a4b6f1a` 以降の修正の取り込み待ち。対応づけは未実施)。実機・ホスト・機能の網羅比較は未計測。内部用で非公開 |
