# 本家 main との比較表 (施策 3-2)

Meltype for Mac を、本家 (Windows 版 `yksr-melt/Meltype` の main) と並べて測り、リリースごとに更新する表です。計画書 [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の柱 A「測って下回らない」を続けるための運用枠で、**この文書は内部用です。README には載せず、push もしていません** (公開は §8 の 8 の決定 (a) にもとづき、リョウさんの許可を得てから)。

## 1. 目的と読み方

- 目的: 本家の判定修正を取り込み続けているか、取り込んだ上で Mac 固有の分を足せているかを、数字で見えるようにする。本家と競うためではなく、「本家の修正を取り込んだ上で Mac 固有分を足した結果」を確かめるための表です (§8 の 8 の協調的な書き方)。
- 現在値は、**実際に回して確かめたものだけ**を入れています。回していないものは「未計測」と書きます。
- 数字はすべて **擬似エンジン上の Core の結果**です (変換エンジンの azooKey も、Mac の入力欄も、macOS のスペルチェッカーも使いません)。実機の体感の代わりにはなりません。
- 「正解表」は `eval/cases/*.tsv` です。うち `upstream-quality.tsv` は本家のテスト (コミット `a4b6f1a` 時点) の写しなので、本家は自分の表では当然 100% です。Mac 側の表 (`mac-device.tsv`・`mac-terminal.tsv`) は Mac 版の側で作った表なので、そこでの Mac 版の高さは「Mac 固有分の確認」であって、本家との優劣ではありません。
- 本家に「勝った/負けた」と読まないでください。外れの内訳 (原因が取り込み待ちか Mac 版固有の回帰か) が肝心です。28 件の対応づけは §5。

## 2. 比較軸と現在値

測定日 2026-10-15。Mac 版 = このブランチ (`docs/p3-3-2`、`669ead5`。Core の版 1.2.0)。本家 = `origin/main` の `45e14f8` (2026-10-14、Core の版 1.2.1)。

| 軸 | 測り方 | 現在値 (Mac 版 / 本家) |
| --- | --- | --- |
| 判定精度 1: 本家の品質テスト | 各ツリーで `dotnet run -c Release --project src/Meltype.Core.Tests -- --eval`。**各ツリー自身のテスト集で測るので、母数が違う** | Mac 版 **1266/1268** (99.8%。外れは `my name is taro`・`apinoerror`) / 本家 **1337/1337** (100%)。**今は Mac 版が下回っている** (母数が 69 件違う。**推測 (未確認)**: 本家が `a4b6f1a` 以降に足した分が Mac 版に入っていない。足された件の中身は確かめていない) |
| 判定精度 2: 同じ正解表を両方で回す | `tools/eval-compare.sh <本家の clone> . eval/cases <出力先>` (正解表は 264 件の判定: `upstream-quality.tsv` 203 + `mac-device.tsv` 41 + `mac-terminal.tsv` 20) | 全体 Mac 版 231/264 (87.5%) / 本家 243/264 (92.0%)。**broken (本家で当たり Mac 版で外れ) 28 件、fixed (逆) 16 件**。表ごと: `upstream-quality` 175/203 (86.2%) / 203/203。`mac-device` 40/41 (97.6%) / 25/41 (61.0%)。`mac-terminal` 16/20 (80.0%) / 15/20 (75.0%) |
| 判定精度 2 の broken 28 件の内訳 | `compare.md` の broken 表 | すべて `upstream-quality.tsv`。`my name is taro`、`apinoerror`、`inviteshimashita`、`yapython` 型 6 件、`devtoprod` 型 5 件、`site PR`、`*pedia` 型 8 件、`i18n`・`l10n`・`h2o` など。対応づけは §5 (2026-10-15 実施)。**28 件すべてが、本家が `a4b6f1a` より前に直したもので、Mac 版に入っていない分** (26 件は P45 で意図して見送った分、2 件 `my name is taro`・`apinoerror` は本家 `4ce7ca8` が候補)。`a4b6f1a` 以降の本家の修正で直ったものは 0 件。Mac 版の後からの回帰と疑える件は見つからなかった (§5) |
| 判定精度 3: Mac 固有コーパスでの上乗せ | 判定精度 2 の `mac-device.tsv` / `mac-terminal.tsv` の行 | 上の表のとおり。`mac-terminal.tsv` の外れ 4 件は「今は外れると分かっている」例を含めた表 (`eval/README.md`)。この外れ 4 件の内訳は、引用元の記載を転記したもので、今回再確認していない |
| 判定精度 4: 実機の体感・かな漢字変換 | リョウさんの実機 (「打った>結果」形式で `mac-device.tsv` に足す)。かな→漢字は `eval/kanji/kanji-cases.tsv` (212 文) を Zenzai の計測で使う | **未計測** (本家との比較としては実機の数字は無い。Zenzai の計測結果は [ZENZAI_EXPERIMENT_2026-10.md](ZENZAI_EXPERIMENT_2026-10.md)) |
| 機能: 本家の変更の取り込み状況 | [UPSTREAM_WATCH.md](UPSTREAM_WATCH.md) の「取り込み」列と [UPSTREAM_PR_TRACKER.md](UPSTREAM_PR_TRACKER.md) の P 項目 | 本家 main は watch が最後に見た `db433da` より 28 コミット先 (未分類)。watch の候補表の 2 件はどちらも「未」。管理表の保留・未着手は P40〜P44。**機能の網羅的な比較表 (本家にあって Mac 版に無いもの/逆) は未作成 = 未計測** |
| 対応ホスト | [HOST_COMPAT.md](HOST_COMPAT.md) の 8 項目の打鍵表 (リョウさんの Mac で手動) | Mac 版: 7 アプリ確認済み (再変換が効かないのは Google ドキュメント・ターミナル。文字は壊れない。この表は引用元の記載を転記したもので、今回再確認していない)、VS Code・Slack は未確認。確認日 2026-10-14、ビルドは `446dd48`。**本家 (Windows 版) のホスト対応は対象外で、同じ打鍵表での比較は未計測** |
| 既知の制限 | [KNOWN_LIMITS.md](KNOWN_LIMITS.md) の「件数」の表 | Mac 版 58 件 (仕様 34 / 直す予定 9 / 直った 5 / 未確認 10)(引用元の記載を転記したもので、今回再確認していない)。本家の制限は同じ基準で数えていないので**未計測** |
| 参考: Mac 版のテスト件数 | `src/Meltype.Core.Tests` の通常実行 | 今回は再実行していない (直近の記録は IMPROVEMENT_PLAN の 1 音確定の節: 659/661、環境由来の 2 件を除き通過。(引用元の記載を転記したもので、今回再確認していない)) |

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
| 2026-10-15 | `669ead5` (docs/p3-3-2)・1.2.0 | `45e14f8`・2026-10-14・1.2.1 | 1266/1268 / 1337/1337 | 231/264 / 243/264、broken 28 / fixed 16 | 最初の枠。今は「下回らない」を満たしていない (原因は §5 のとおり: 28 件は本家の `a4b6f1a` より前の修正のうち Mac 版に入っていない分で、`a4b6f1a` 以降の修正ではない)。実機・ホスト・機能の網羅比較は未計測。内部用で非公開 |
| 2026-10-15 | `54a2002` (main)・1.2.1 の前 | `45e14f8`・2026-10-14・1.2.1 (手元の写し。取り直していない) | 未計測 / 未計測 | 276/310 / 266/310、broken 28 / fixed 38 | 1.2.1 の公開前。正解表に `mac-protected-spans.tsv` (46 件) が加わり母数が 310 になった。表ごと (Mac / 本家): `upstream-quality` 175/203 / 203/203、`mac-device` 40/41 / 25/41、`mac-protected-spans` 45/46 / 23/46、`mac-terminal` 16/20 / 15/20。broken 28 件は §5 の 28 件と同じ打鍵 (新しい回帰 0)。同じ表の合計は Mac 版が上だが、本家の品質テスト (`upstream-quality`) では今も下回っている |

## 5. broken 28 件の対応づけ (2026-10-15)

調べたこと (すべて読み取りのみ。ソースは変更していない):

- **(a) 本家の修正**: 本家の clone (`45e14f8`) で、`git log a4b6f1a..45e14f8` と、ケースの打鍵での `git log -S<打鍵>` (`a4b6f1a..45e14f8` と `a4b6f1a` まで)。
- **(b) Mac 版の側**: `upstream-quality.tsv` は `3dbc7e9` で入った `a4b6f1a` 時点の写しで、28 件はその最初からある。Mac 版の判定ロジックの変更は P45 (7 コミット + 守り 1) だけで、取り込み前に当たっていた例を壊していない (broken 0。[IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) の P45)。
- 追加で確かめたこと: `a4b6f1a` の木は本家の品質テスト 1337/1337 で通る (`git archive` で別の置き場所に出して実行)。`my name is taro`・`apinoerror` の修正を探すため、本家の木を `git archive` で出して `--eval` を回した (下記)。

### 結論

- **`a4b6f1a..45e14f8` の間に、28 件のどれかを足した・直したコミットは無い**。この範囲の判定関連の変更は #603 (bio-break)・#609 (infobesity)・`2b4e641` (いめ→IME)・`1cfb28e` (感嘆符の絵文字) 程度で、`-S` でも 28 件の打鍵は出ない (Core の差分は 12 ファイル、+32/−9 行。`CompositionDetector.cs` 2 行・`Settings`・`MeltypeSession`・辞書数行など。判定の変更は小さい)。**母数の差 69 件 (§2 の判定精度 1) の中身は、今回は 28 件の調査の範囲外で未確認**。
- 28 件の直し方はすべて `a4b6f1a` **以前**の本家コミットにある (全部 `a4b6f1a` の祖先)。つまり「写し以降に本家が直した」ではなく、**「写しの時点で本家は当たり、Mac 版は当たらなかった」**。
- Mac 版固有の回帰 (Mac 版が一度当たっていて後から外れた) と言える件は、**確認できた範囲で 0**。26 件は P45 の見送り (取り込むと別の例が壊れたため) で、2 件は Mac 版の最初の Core から外れていた (外れ 2 件が現在の 2 件と同じかは推測)。ただし、写し (`3dbc7e9` 時点) より後に Mac 版が Core を変えたコミットは 10 件以上あり、そのどれかで一度当たった例が外れた可能性は、26 件については未検証。

### 対応表

| 型 | 件数 | 分類 | 根拠のコミット (本家) | 確度 | 取り込みの見積もり (変更量・リスク) |
| --- | --- | --- | --- | --- | --- |
| `my name is taro` | 1 | 取り込み候補 (本家 `4ce7ca8` に含まれる可能性が高い。**推測**) | `4ce7ca8` (2026-10-07、`Preserve mixed protected input and reduce composition latency`)。本家 `4ce7ca8^` は品質テスト 1241/1243、`4ce7ca8` は 1243/1243、Mac 版の最初の Core (`c3c247f`) は 1241/1243 (外れ 2 件は Mac 版の現在と同じ 2 件と**推測**。`--eval` は外れの名前を出さないので、個別の成否は未確認) | 低 (どの部分が `taro` を直したかは未特定) | 大: Core だけで +198/−39 行 (7 ファイル。`CompositionDetector`・`CompositionText` が主)。Mac 版は両ファイルに独自の改変があるので手移植が前提で、性能最適化 (`CanBeEnglishSpan` ほか) が混ざる。リスク大。部分抜き出しは hunk の特定から |
| `apinoerror` | 1 | 取り込み候補 | `4ce7ca8` の `EnglishBeforeParticleAndEnglishTail` (api + no + error のように辞書の英語見出しを助詞と英単語が挟む。差分を読んで確認)。のちの `2a2bf10` (#376) も触れている | 中〜高 (関数の意図は読んで確認。単独で Mac 版に足して直るかは未実行) | 小〜中: 新規関数 1 つ (約 20 行) と呼び出し 1 か所。ただし `IsListedEnglishWord`・`TrailingParticles` などの前提が Mac 版にあるかは未確認。`sushi + no + error` が壊れないか eval で見る必要 |
| `inviteshimashita`・`invitewookutta` | 2 | 取り込み候補 (P45 で見送り済み) | `c6b281e` (#69、2026-10-08)。Core は +2 行。P45 の n=4 | 高 (P45 が実測) | 小: 2 行。ただし入れると `githubdevote` (期待 `githubでvote`) が壊れて見送った。直すなら `githubdevote` の守りが先 |
| `i18n`・`l10n`・`h2o` | 3 | 取り込み候補 (P45 で見送り済み) | `b4484c4` (2026-10-11、数字挟み略語)。Core は +38 行 (2 ファイル)。P45 の n=6 | 高 (P45 が実測。eval は broken 0 で 3 件直る) | 中: +38 行。入れると既存テスト 2 件 (`video.mp4woMP3ni`・`今日は3かける4を`) が落ちる。本家は `ScoredSegmentation.cs` 側にも守りがあり、Mac 版には無い。条件を絞るか、その 2 件の守りが先 |
| `や` + 英単語 (`githubya`・`yapython`・`yacursor`・`yaslack`・`yastack`・`yaplay`・`yasync`) | 7 | 取り込み候補 (P45 で見送り済み) | `3cb0758` の `や` の部分 (2026-10-08) と `ffd7e6a` (2026-10-08)。P45 の n=8 の残り。`githubya` は `-S` で `27e907c` ・`3cb0758` にも出る | 高 (P45 が実測) | 中: 2 コミットで Core +57/−8 行。全部入れると eval の broken 3 件と既存テスト 4 件が壊れた。`ffd7e6a` を外しても 2 件壊れた。リスク大 (過去に撤退した系統。方針は「同じ種類の回帰が出る修正は撤退」) |
| 英字のあいだの `と` (`devtoprod`・`edittopub`・`dottonet`・`rdstoredshift`・`uikittoswiftui`) | 5 | 取り込み候補 (前提つき) | `35beb60` の `と` の部分 (2026-10-12、Core +62/−5) が `74ca494` (#154、「英字のあいだの『と』を助詞にする」、Core +13 行) を前提にする。Mac 版に `74ca494` の機能が無い | 高 (P45 が実測。`74ca494` が前提という点は P45 の記載で、今回は差分を読んでいない) | 中: 先に `74ca494` (13 行) を別項目として入れ、そのあと `35beb60` の `と` の部分。リスクは中 (`と` 助詞は日本語側に影響しやすい。eval の broken 0 を条件に) |
| `site PR` | 1 | 取り込み候補 (P45 で見送り済み) | `8dfec9d` (2026-10-08、Core +19/−4)。P45 の n=12 | 高 (P45 が実測) | 小〜中: 入れると既存の英文 `make sure you have time` が `まけ すれ よう have time` に壊れた (`sure` が `すれ` と読める)。守りを足さないと入れられない |
| `*pedia` (`pedia`・`protopedia`・`conservapedia`・`wikipedias`・`protopedias`・`Protopedia`・`Protopedias`・`worldpedia`) | 8 | 取り込み候補だが**本家側の修正にも悪化あり** (P45 の査読 R35) | `3cb0758` の pedia の部分・`fd66db0` (-pedias)・`f1a190e` (先頭大文字、#207 系)。P45 の n=8・9 | 高 (P45 が実測。R35 の悪化は P45 の記載) | 中: 3 コミットで Core 約 +45 行。一度入れて外した: `kyouhawikipediadeshirabeta` が前の日本語ごと英字になる悪化があり、**本家の `a4b6f1a` も同じ**。入れ直すなら「前の日本語を巻き込まない条件」を先に決める。本家への報告は判断待ち (P45)。リスク大 |
| 合計 | 28 | 取り込み候補 28 / Mac 版固有の回帰 0 / 不明 0 (ただし `my name is taro` の確度は低) | | | |

### 注意

- 「取り込み候補」は、**取り込むべき**という意味ではありません。26 件は P45 で、入れると別の例が壊れたため見送ったものです。取り込むかどうかはリョウさんの判断です。この節はソースを変えていません。
- P45 の記載 (n=4・6・8・9・11・12) は今回の調査で本家のコミットが実在し `a4b6f1a` の祖先であること、変更行数だけを確かめました。「入れると何が壊れるか」は P45 の記録を写したもので、今回は再実行していません。
- `my name is taro`・`apinoerror` は、P45 の n=13 では「直した修正は見つからなかった (未特定)」でした。今回、本家の木を `4ce7ca8^` と `4ce7ca8` で回して、`4ce7ca8` で 2 件分の差が出ることを見つけました (`apinoerror` は関数まで特定、`my name is taro` はコミットまで)。Mac 版は `c3c247f` (同じ 2026-10-07) の時点から外れているので、写した時期が `4ce7ca8` の直前だった**と推測**します。
- 見積もりの「行数」は `git show --shortstat <hash> -- src/Meltype.Core dictionaries` の数字です。Mac 版への手移植の実際の量とは違います。
