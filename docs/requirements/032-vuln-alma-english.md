---
title: vuln・alma が、打っている間は英字なのに Enter で ゔおん・あおま と確定する
status: approved (定期タスクによる自動承認。元: docs/voice/2026-11-24.md 候補 1)
source: docs/voice/2026-11-24.md (ブランチ voice/2026-11-24) 候補 1 / 本家 #2312 (vuln) / 本家 #1487 (alma。docs/voice/2026-11-03.md 候補 1)
scope: dictionary
---

# 032 vuln・alma が、打っている間は英字なのに Enter で ゔおん・あおま と確定する

番号について: 031 は PR #33 (ブランチ `req/031-upstream-1123-guard`) が使っているので、032 にした。

## 背景 (誰が、何に困っているか)
- 本家 #2312 (2026-11-24、Windows、1.7.1): ChatGPT の入力欄で `--script vuln` と打つと `von` 系に崩れる (期待 `vuln`)。本家の bot は main で `vuln` になり再現せず。
- 本家 #1487 (2026-11-03、Linux): `alma` + スペースが `あおま` になる (期待 `alma`)。本家の bot は main で再現せず。
- この fork では、打っている間の表示は英字なのに、Enter で違う文字 (かな) が確定する。見えている文字と違うものが入るので、気づかずに送ってしまう。
- 今の挙動 (Core の `--repro`、最後のキーは Enter、2026-11-25、`origin/main` 627b77a):

  | 打鍵 | 表示 | 確定 |
  |---|---|---|
  | `vuln` | `vuln` | `ゔおん` |
  | `--script vuln` | `vuln` | `--script ゔおん` |
  | `vulnwo` | `vulnを` | `ゔおんを` |
  | `vulnde` | `vulnで` | `ゔおんで` |
  | `alma` | `alma` | `あおま` |
  | `almaga` | `あlまが` | `あおまが` |
  | `almade` | `あlまで` | `あおまで` |
  | `alma linux` | `linux` | `あおまlinux` (空白も消える) |

  今も英字のまま: `vulns`・`vulnerable`・`cvevulnwotaisaku` (→ `cvevulnをたいさく`)・`AlmaLinux`・`Alma`・`salmon`・`almond`・`calm`・`elm`。

## 今の動き (既に分かっていること)
- 確定の直前の打ち間違いの補正 (`CompositionText.FixTypos`) が、英単語の一覧 (`dictionaries/english.txt`) に無い英字の区間を、隣のキーの打ち間違いとして読み直す (`l` → `o`)。`vuln`・`alma` は一覧に無いので守られない。要件 016 (`obsidian`) と同じ型。
- 試しに `english.txt` に `vuln alma` を足すと、上の 8 形がすべて表示どおり英字 (助詞はかな) で確定した (2026-11-25、作業ツリーで試して戻した)。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 本家の main では両方とも再現しない (bot の結果)。直し方 (辞書か判定か) は未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. `vuln`・`alma` を、単独でも、助詞 (`wo`・`ga`・`de`) が続く形でも、前に英字の引数がある形 (`--script vuln`) でも、英字のまま確定する。助詞はかなになる。表示と確定が同じになる。
2. 今の形を守る: `vulns`・`vulnerable`・`AlmaLinux`・`salmon`・`almond`・`calm`・`elm` は英字のまま。`vu`・`aruma`・`arumade`・`karuma`・`vuittonn` (→ `ゔいっとん`) は今のまま。

## 非目標 (今回はやらない)
- 「表示は英字・確定はかな」のずれ全般を直すこと (判定ロジックは変えない)。辞書に 2 語足すだけ (見出し `本家 #2312・#1487` の行を新しく作る)。
- 候補 2 (`nvm`・`nmap`)・候補 3 (入れ子の括弧の幅) は別の要件。
- `vuln`・`alma` 自体の打ち間違い (`vulnn`・`almaa` など) を直すこと。

## 受け入れる変化 (実装後に base と比べて分かったこと。受け入れてよいかは人が判断する)
Core の `--repro` を base (`origin/main` 627b77a) とこのブランチの両方で打った (2026-11-25、最後のキーは Enter。表示 / 確定)。

| 打鍵 | base | このブランチ | 向き |
|---|---|---|---|
| `vulnn`・`vulne`・`vulnu` | `vulnn` / `ゔおん`・`vulne` / `ゔおね`・`vulnu` / `ゔんぅ` | 英字のまま (表示と確定が同じ) | ずれが消える |
| `vulnni`・`vulnno`・`vulnha`・`vulnto` | 確定 `ゔおんい`・`ゔおんお`・`ゔおんは`・`ゔおんと` | `vulnに`・`vulnの`・`vulnは`・`vulnと` | 良くなる |
| `almani`・`almano`・`almaha`・`almadesu`・`almaa` | 確定 `あおまに`・`あおまの`・`あおまは`・`あおまです`・`あおまあ` | `almaに`・`almaの`・`almaは`・`almaです`・`almaa` | 良くなる |
| `vuln_` | `vuon_` / `vuon_` | `vuln_` / `vuln_` | 良くなる |
| `cve vuln` | `vuln` / `cゔぇゔおん` | `vuln` / `cゔぇvuln` | 後ろは直る。`cve` がかなになり空白が消えるのは base と同じ (この要件では直さない) |
| `alma mater` | `まてr` / `あおままて` | `mater` / `alma mate` | 前は直る。最後の `r` が落ちるのは base と同じ (この要件では直さない) |
| `almame`・`almae`・`almari`・`almaeda` | 確定 `あおまめ`・`あおまえ`・`あおまり`・`あおまえだ` | `almaめ`・`almaえ`・`almaり`・`almaえだ` | **悪くなる**: 入力の先頭の `あおま〜` の `o` を隣の `l` と打ち間違えた形は、補正が効かなくなる (`aomame` = 青豆 の打ち間違いなど) |

- 悪くなる形は「入力の先頭が `alma` で、`あおま` で始まる語を `o` → `l` と 1 文字打ち間違えたとき」だけ。リポジトリの日本語の辞書 (`readings.txt`・`japanese.txt`) に `あおま` を含む語は無い。語の途中・境目の形 (`kimihalmae` → `きみはおまえ`、`hanalmatsuri` → `はなおまつり`、`kalmawari` → `かおまわり`、`nalmasa` → `なおまさ`) は base と同じに補正される。
- `vuln` を足して悪くなる形は見つからなかった (`vuon`・`vulon`・`ivulnn` は base と同じ)。
- 悪くなる形を受け入れないなら、`alma` を外して `vuln` だけにする (その場合、正解表の `alma`・`almaga`・`almade`・`alma linux` は外れのまま残す)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  vuln	vuln	full	本家 #2312
  --script vuln	--script vuln	full	本家 #2312
  vulnwo	vulnを	full
  vulnde	vulnで	full
  alma	alma	full	本家 #1487
  almaga	almaが	full
  almade	almaで	full
  alma linux	alma linux	full
  ```
  守る行 (今も当たる): `vulns` → `vulns`、`vulnerable` → `vulnerable`、`AlmaLinux` → `AlmaLinux`、`salmon` → `salmon`、`almond` → `almond`、`vu` → `ゔ`、`aruma` → `あるま`、`arumade` → `あるまで`、`karuma` → `かるま`
- [ ] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req032_VulnAlma_CommitAsShown` を足す (要点: 上の行を `BuiltInWordChecker` で打って Enter の確定を比べる。`vuln`・`--script vuln`・`alma` は、Enter の直前の表示と確定が同じことも比べる)。実装前は赤。
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過、`node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [ ] 境界例: `tools/boundary-sweep.sh` (full) で、日本語の損失・英語の損失が 0 件
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] ターミナル (zsh): `--script vuln` + Enter → `--script vuln`
- [ ] TextEdit: `vuln` + Enter → `vuln` (変換ボックスの表示と確定が同じか)
- [ ] Safari のフォーム: `alma` + Enter → `alma`、`almade` + Enter → `almaで`
- [ ] TextEdit: `aruma` → あるま、`karuma` → かるま (変わらない)
- [ ] 実機では macOS のスペルチェッカーが `vuln`・`alma` をどう扱うかで結果が変わり得る (Core は同梱の一覧で測っている)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 辞書に 2 語足すだけでは直らず、判定ロジックの変更が要るとき。
- 境界例の一括比較で、日本語の損失 (正しく打った日本語、または 1 打鍵の打ち間違いの補正が効かなくなる形) が見つかったとき (要件 007・012 の教訓)。

## 記録 (ループが書く)
- 周回数 / 上位・下位のどちらのモデルか (モデルの名前は書かない) / 所要時間 / 手戻りの原因 / PR の URL
