# 評価基盤 (eval/)

同じ「正解表」で、本家 Meltype の Core と Mac 版の Core の判定結果を並べ、回帰 (前回や本家で当たっていたのに今は外れる例) をゼロにするための表と手順です。使い方の全体は [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) の「評価基盤 (eval/)」にあります。

## 正解表の形式 (`eval/cases/*.tsv`)

1 行が 1 件で、Tab 区切りの 4 列です。UTF-8、改行は LF。`#` で始まる行と空行は読みません (カテゴリの区切りやメモに使います)。

```
打鍵<Tab>期待<Tab>比べ方<Tab>出典
```

| 列 | 意味 |
|---|---|
| 打鍵 | 本家の `--expect` と同じです。ローマ字と記号を打ち、Enter で確定した結果を見ます。 |
| 期待 | 確定後の文字列です。 |
| 比べ方 | `full` / `ascii` / `nojp` のどれか (下の表)。 |
| 出典 | 自由文です (例: `本家 QualityTests 2026-10-13 a4b6f1a`、`実機 TextEdit 2026-10-12`、`本家 #316`)。 |

### 比べ方 3 種

| 比べ方 | 合格の条件 | 使いどころ |
|---|---|---|
| `full` | 完全一致です (英字の大小も区別します。本家の `Expect` と同じ)。ただし、期待にカタカナがあるときだけ、カタカナをひらがなに直して比べ (`ホスティング` と `ほすてぃんぐ` が一致)、英字の大小も無視します (`OEM のホスティング` と `oem のほすてぃんぐ` が一致)。 | かなと英字の分かれ方まで確かめたいとき。期待が打鍵そのものの行 (コマンド・英語の名前) も `full` にします (1 文字も崩れないことを見られます) |
| `ascii` | 印字可能 ASCII の連続 (英字・数字・記号。`[!-~]+`) を語とし、小文字にして並びを比べます。`今日は google で検索` と `きょうは google で けんさく` は一致し、`google` が `ごおgle` になれば不一致です。`gemma4-27b` が `gemma4ー27b` になる崩れも見つけます。 | 実機の漢字変換の結果を期待にそのまま書きたいとき。テストには変換エンジンが無く漢字にならないので、日本語と英語の分かれ方だけを測ります。期待に語が 1 つも無い行は、何でも合格になるので、読み込みの時点でエラーにします。 |
| `nojp` | 実際の出力に日本語 (ひらがな・カタカナ・漢字・全角記号・半角カナ。U+3000〜30FF・U+3400〜9FFF・U+FF00〜FFEF) が 1 文字も無ければ合格です。 | 「英文が日本語に崩れないことだけ見たい」行用です。期待が打鍵そのものなら、`nojp` は `ー` 以外の崩れ (綴りの違いなど) を見逃すので `full` を使います。 |

`#` で始まらない空でない行に 4 列 (Tab 3 つ) の行が 1 つも無い古い表 (Pull Request のチェックが使う) は、変更前の `--expect` と同じ挙動です: `#` の行も読み、2 列でない行 (見出し・3 列の行) は黙って捨てる (変更前と同じ)、期待に漢字があれば英字の語 (`[A-Za-z][A-Za-z'’-]*`) の並び、無ければ完全一致 (カタカナの正規化・大小無視なし) で比べます。4 列の行が 1 つでもある表だけ、上の読み方 (`#` の行と空行を無視し、列の数・比べ方の誤りを例外にする) です。`--expect-all` は常に 4 列の表として読みます。かなで打つ行 (候補を見る行) は比べ方を空にします。

## ファイルの役割

| ファイル | 中身 |
|---|---|
| `cases/upstream-quality.tsv` | 本家 `src/Meltype.Core.Tests/QualityTests.cs` の `Typing` 配列 (コミット `a4b6f1a`、GPL-3.0) の写しです。216 件のうち、「直前の文字 (`Before`)」を持つ 13 件は、この形式に直前の文字の列が無いので `#` のコメントで写すだけにして、判定しません。残り 203 件を `full` で比べます。`full` は本家の `Expect` と同じ比べ方です (期待にカタカナがあるときだけ、カタカナ→ひらがなと英字の大小がゆるくなります)。 |
| `cases/mac-device.tsv` | Mac 実機で確かめた「打った → 結果」(`docs/IMPROVEMENT_PLAN.md` と `docs/HANDOFF-upstream-pr-next-session.md` の記録) と、計画書に期待だけが書いてある例です。出典に節の名前を書いています。打鍵が文書に書かれておらず期待から復元した行は、出典に「打鍵は復元」と書いています。 |
| `cases/mac-terminal.tsv` | ターミナル・AI ツール名の形 (`Claude Code de kaku`・`git push origin main`・`ollama run gemma`・`gpt-oss wo tsukau` など) です。期待は今の Mac 版 Core の **望ましい** 出力で、今は外れると分かっているもの (`ollama run` が `るん`、`gemma4-27b` が `gemma4ー27b`、`gpt-oss` が `gpt-おっs`) も入れてあります。このうち `ollama run`→`るん` と `gemma4ー27b` は「実機で既知の外れ」で、Mac 版 Core のテスト用キーボードでは再現しません (`ollama run` は本家の Core でも再現せず、`gemma4ー27b` は本家の Core では再現します)。 |

実機の結果と、Core のテスト用キーボードの結果は別物です。この評価基盤は変換エンジン (azooKey) も Mac の入力欄も使わないので、「実機で確かめた」ことの代わりにはなりません。

## 実機の結果を足すとき

1. 実機 (TextEdit など) で打ち、結果を見ます。メンテナーが貼った `打った>結果` の形は、そのまま 1 行にできます。
   - `打った` を 1 列目、`結果` を 2 列目に、Tab で区切ります。
   - 結果に漢字が入っているときは 3 列目を `ascii` にします (漢字は測れず、印字可能 ASCII の連続 (空白で区切った並び) を比べます)。かな・英字だけなら `full` です。英語がそのまま残ることだけを見たいときは `nojp` です。
   - 4 列目に、いつ・どのアプリで確かめたか、計画書の節の名前を書きます (例: `実機 TextEdit 2026-10-13 Q11`)。
2. 手元で流して、いまの Core でどうなるかを見ます。

   ```bash
   cd src/Meltype.Core.Tests
   dotnet run -c Release -- --expect-all ../../eval/cases /tmp/eval.json
   ```

   外れたものは、直す前の記録として残しておく価値があります (今は外れると分かっている例は、そのまま入れて構いません)。
3. 注意:
   - **私的な文は入れません。** 名前・住所・メールアドレス・パスワード・社内の固有名詞・私信の一文など、公開されて困るものは入れず、似た形の例に書き換えます。
   - このリポジトリを push すれば、この表は公開されます。push しない作業でも、`git add` した時点で履歴に残るので、迷うものは入れないでください。
   - 打鍵を復元した行・実機で確かめていない行は、その旨を出典に書きます。

## 回帰ゲート

`tools/eval-compare.sh <base-tree> <new-tree> <cases-dir> <out-dir>` が、2 つの作業ツリーで同じ表を回します。`broken` (base で当たり、new で外れた) が 1 件でもあれば終了コード 1、dotnet や入力の失敗は 2、問題なしは 0 です。本家のゲートは broken + addedFailing ですが、こちらは broken のみです (同じフォルダーの表なら added は 0)。手順は [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) を見てください。

## かな→漢字の正解表 (`eval/kanji/kanji-cases.tsv`)

Zenzai (ニューラル変換) の計測用の、かな → 漢字の正解表です (212 文。列: category / reading / expected / left_context / note / accepted。accepted は `|` 区切りの別の正解)。上の `cases/` (日英判定用) とは別物で、日英判定の `--expect-all` には読ませません。
計測は `exp/zenzai-1-1` ブランチの `ZenzaiEval` (結果は [docs/ZENZAI_EXPERIMENT_2026-10.md](../docs/ZENZAI_EXPERIMENT_2026-10.md))。製品の変換の経路での配線の確認は `MeltypeIME --zenzai-self-test --weights <gguf> --cases eval/kanji/kanji-cases.tsv` (`mac/README.md`)。

## 前の文字つきの正解表 (`eval/context/`)

実験 2-2 (キャレット前 20 文字の英日比率を判定に使うか) の表です (列: 前の文字 / 打鍵 / 期待 / 種類 / メモ)。`context-cases.tsv` (68 件) は作者が、`review-cases.tsv` (56 件) は査読者が書いた作例で、期待は人の判断です (実機の記録ではありません。件数の配分も実際の利用の頻度ではありません)。上の `cases/` とは別物で、日英判定の `--expect-all` には読ませません。
読むのは実験ブランチ `exp/p2-2-2-on-main` (手元のみで公開していない) の `--ctx-experiment` だけで、main には無いので、このリポジトリだけでは測り直せません。結果は [docs/EXPERIMENT_2-2_RESULT.md](../docs/EXPERIMENT_2-2_RESULT.md) (no-go)。

## 言語モデルの採点の結果 (`eval/lm-judge/`)

実験 2-1 (Zenzai の言語モデルで迷ったときの最終判定) のデータです。`extra/extra-cases.tsv` は補足の 14 件 (`cases/` と同じ 4 列。作者が作った架空の例で、実機で確かめた値ではありません) で、`cases/` には入れていないので `--expect-all ../../eval/cases` には入りません (`--expect-all ../../eval/lm-judge/extra` と直接指せば読めます)。`results/` の jsonl・json は、`cases/` の表と補足の打鍵についての、今のルールの出力と言語モデルの採点の結果です (`main-54a2002/` は main 54a2002 で増えた 46 件の分)。
採点器 `tools/lm-judge/` は実験ブランチ `exp/p2-2-1` (手元のみで公開していない) にあり、main には無いので、このリポジトリだけでは採点し直せません。結果は [docs/EXPERIMENT_2-1_RESULT.md](../docs/EXPERIMENT_2-1_RESULT.md) (no-go)。

## Foundation Models の測定データ (`eval/p2-4/`)

実験 2-4 (Apple の Foundation Models で英語部分の整形・訳ができるか) の単体ツール `fmprobe` の出力 3 回分です (`run1`〜`run3` の `.json` と `.txt`。`run3.lsof.txt` はネットワーク接続を見た回数だけ)。入力 30 文は作者が書いた作例 (個人情報なし) で、正解表ではないので `--expect-all` には読ませません。
`fmprobe` は実験ブランチ `exp/p2-2-4` (手元のみで公開していない) にあり、main には無いので、このリポジトリだけでは測り直せません。結果は [docs/EXPERIMENT_2-4_RESULT.md](../docs/EXPERIMENT_2-4_RESULT.md)。
