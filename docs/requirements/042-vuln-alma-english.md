---
title: vuln が、打っている間は英字なのに Enter で ゔおん と確定する (alma は入れない理由を固定する)
status: done   # draft | approved | done。approved にするのは人
source: docs/voice/2026-11-24.md (ブランチ voice/2026-11-24) 候補 1 / 本家 #2312 (vuln) / 本家 #1487 (alma。docs/voice/2026-11-03.md 候補 1)
scope: dictionary
---

# 042 vuln が、打っている間は英字なのに Enter で ゔおん と確定する (alma は入れない理由を固定する)

番号について: 定期タスクは 032 (ブランチ `req/032-vuln-alma-english`) で切ったが、main には別の要件 032 (拍数) が入ったので、統合のときに 042 へ付け替えた。

範囲を変えた経緯: 最初は `vuln`・`alma` の 2 語を辞書に足す要件として書いた。査読 1 周目で、`alma` を足すと母音の後の `あおま` の `o` を `l` と打った 1 打鍵の打ち間違い (`maalmae` = まあおまえ) が補正されなくなると分かった。これはこの要件の止める条件に当たるので、`alma` は入れず、入れない理由を正解表とテストで固定する形に絞った (2026-11-25)。`alma` の表示と確定のずれをどう直すか (判定の変更が要る) は人が決める。

## 背景 (誰が、何に困っているか)
- 本家 #2312 (2026-11-24、Windows、1.7.1): ChatGPT の入力欄で `--script vuln` と打つと崩れる (期待 `vuln`)。本家の bot は main で `vuln` になり、再現しなかった。
- 本家 #1487 (2026-11-03、Linux): `alma` + スペースが `あおま` になる (期待 `alma`)。本家の bot は main で再現しなかった。
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

  次の形は、今も英字のまま確定する: `vulns`・`vulnerable`・`cvevulnwotaisaku` (→ `cvevulnをたいさく`)・`AlmaLinux`・`Alma`・`salmon`・`almond`・`calm`・`elm`。

## 今の動き (既に分かっていること)
- 確定の直前に、打ち間違いの補正 (`CompositionText.FixTypos`) が働く。補正は、英単語の一覧 (`dictionaries/english.txt`) に無い英字の区間を、隣のキーの打ち間違いとして読み直す (`l` → `o`)。
- `vuln`・`alma` は一覧に無いので、補正から守られない。要件 016 (`obsidian`) と同じ型。
- `docs/KNOWN_LIMITS.md` に該当の記載なし。
- 本家の扱い: 本家の main では両方とも再現しない (bot の結果)。直し方 (辞書か判定か) は未確認。クラウドから本家に届かないため。

## minimum の要件 (これだけは満たす)
1. `vuln` を英字のまま確定する。次のどの形でもそうなる:
   - 単独
   - 助詞 (`wo`・`de`) が続く形。助詞はかなになる
   - 前に引数がある形 (`--script vuln`)

   表示と確定が同じになる。
2. 次の形は今のまま守る:
   - 英字のまま: `vulns`・`vulnerable`・`AlmaLinux`・`salmon`・`almond`
   - かなのまま: `vu`・`vuittonn`・`aruma`・`arumade`・`karuma`
3. `alma` は辞書に入れない。入れると補正が効かなくなる形を、正解表とテストで固定する:
   - `maalmae` → `まあおまえ`
   - `kinoualmamewoyonda` → `きのうあおまめをよんだ`

## 非目標 (今回はやらない)
- 「表示は英字・確定はかな」のずれ全般を直すこと。判定ロジックは変えない。
  - `alma` (表示 `alma`・確定 `あおま`) はずれたまま残る。直すには判定の変更が要るので、別の要件にするかを人が決める。
- 候補 2 (`nvm`・`nmap`)・候補 3 (入れ子の括弧の幅) は別の要件。
- `vuln` の前の語 (`cve vuln` の `cve` がかなになり空白が消える) を直すこと。
- 前に日本語がつながった `vuln` の表示と確定のずれ (`avuln` → 表示 `avuln`・確定 `あゔおん`、`kavulnwo` → 確定 `かゔおんを`)。base と同じで、この要件では直さない (査読 2 周目の任意指摘)。

## 受け入れる変化 (base と比べて分かったこと。受け入れてよいかは人が判断する)
Core の `--repro` を、base (`origin/main` 627b77a) とこのブランチの両方で打った (2026-11-25、最後のキーは Enter。表示 / 確定)。

| 打鍵 | base | このブランチ | 向き |
|---|---|---|---|
| `vulnn`・`vulne`・`vulnu` | `vulnn` / `ゔおん`・`vulne` / `ゔおね`・`vulnu` / `ゔんぅ` | 英字のまま (表示と確定が同じ) | ずれが消える |
| `vulnni`・`vulnno`・`vulnha`・`vulnto` | 確定 `ゔおんい`・`ゔおんお`・`ゔおんは`・`ゔおんと` | `vulnに`・`vulnの`・`vulnは`・`vulnと` | 良くなる |
| `vuln_`・`vuln.` | `vuon_` / `vuon_`・`ゔおん。` / `ゔおん。` | `vuln_`・`vuln.` | 良くなる |
| `use vuln` | `vuln` / `うせゔおん` | `vuln` / `use vuln` | 良くなる |
| `cve vuln` | `vuln` / `cゔぇゔおん` | `vuln` / `cゔぇvuln` | 後ろは直る。`cve` がかなになり空白が消えるのは base と同じ |

- 悪くなる形は見つからなかった。次の形は base と同じ: `vul`・`vuon`・`vulon`・`ivulnn`・`vulnerab`・`vulnalma`・`alma`・`almade`・`maalmae`。
- 境界例の一括比較 (`tools/boundary-sweep.sh` の full、base 627b77a と最終の形 (`vuln` だけ) を比べた。査読 2 周目の査読員が回した):
  - 684,122 入力で、日本語の損失 0・英語の損失 0・その他の変化 5。
  - 変化した 5 件は、どれも `vuln` が英字で確定するようになった形: `vuln`・`vulnwo`・`vulnga`・`vulnno`・`vulndesu` (base は `ゔおん`・`ゔおんを`・`ゔおんが`・`ゔおんお`・`ゔおんです`)。
  - 実装者が先に回した 1 回目 (`vuln alma` の 2 語の版、684,117 入力で変化 0) は、入力の一覧を変更前の辞書から作っていた。そのため `vuln`・`alma` の入力を 1 件も打っておらず、記録に使わない。
  - `alma` の損失 (`maalmae`) は、この道具が作らない形 (1 文字の置き換え) なので、一括比較には出ない。査読員が手で見つけた。
- 査読 2 周目の自作の境界例: 19,703 入力 (日本語の綴りに隣のキーへの 1 文字の置き換え・前後に語) を base と比べた。変化 651 件は、すべて `vuln` を含む入力で `vuln` が英字で残る形。`vuln` を含まない入力の変化は 0 件。

## 完成条件 (先に決める。機械で判定できる形)
- [x] 正解表: `eval/cases/short-words-in-english.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  vuln	vuln	full	本家 #2312
  --script vuln	--script vuln	full	本家 #2312
  vulnwo	vulnを	full
  vulnde	vulnで	full
  ```
  - 守る行 (今も当たる): `vulns` → `vulns`、`vulnerable` → `vulnerable`、`AlmaLinux` → `AlmaLinux`、`salmon` → `salmon`、`almond` → `almond`、`vu` → `ゔ`、`vuittonn` → `ゔいっとん`、`aruma` → `あるま`、`arumade` → `あるまで`、`karuma` → `かるま`
  - 入れない理由の固定 (今も当たる。`alma` を辞書に入れると外れる): `maalmae` → `まあおまえ`、`kinoualmamewoyonda` → `きのうあおまめをよんだ`
- [x] テスト: `src/Meltype.Core.Tests/FeedbackTests.cs` に `Req042_Vuln_CommitsAsShown_AndAlmaTypoStillCorrected` を足す。
  - 要点: 上の行を `BuiltInWordChecker` で打ち、Enter の確定を比べる。`vuln`・`--script vuln` は、Enter の直前の表示と確定が同じことも比べる。
  - 実装前は赤。`alma` を辞書に足しても赤になる (`maalmae`)。
- [x] 回帰なし: 次をすべて満たす。
  - `tools/eval-compare.sh` の broken 0
  - Core のテスト全件通過
  - `node tools/check-dictionaries.mjs`・`node tools/check-terms.mjs` がエラー 0
- [x] 境界例: `tools/boundary-sweep.sh` (full) で、日本語の損失・英語の損失が 0 件
- [x] 文書: `docs/KNOWN_LIMITS.md` に該当の記載なし (確認のみ)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す。すべて未確認)
- [ ] ターミナル (zsh): `--script vuln` + Enter → `--script vuln`
- [ ] TextEdit: `vuln` + Enter → `vuln` (変換ボックスの表示と確定が同じか)
- [ ] Safari のフォーム: `vulnwo` + Enter → `vulnを`
- [ ] TextEdit: `aruma` → あるま、`karuma` → かるま、`maalmae` → まあおまえ (変わらない)
- [ ] スペルチェッカー: 実機では macOS のスペルチェッカーが `vuln` をどう扱うかで、結果が変わり得る。Core は同梱の一覧で測っている。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 辞書に語を足すだけでは直らず、判定ロジックの変更が要るとき。
- 境界例で、日本語の損失が見つかったとき (要件 007・012 の教訓)。日本語の損失とは、正しく打った日本語の形が崩れる、または 1 打鍵の打ち間違いの補正が効かなくなること。
  - → 査読 1 周目で `alma` に当たったので、`alma` を外した (上の「範囲を変えた経緯」)。

## 記録 (ループが書く)
- 周回数:
  - 実装は 2 周。1 周目は `vuln alma`、2 周目は `alma` を外した。
  - 査読: 1 周目の止める指摘は無し。直す指摘は 2 件。
    - 直す 1: `alma` で、母音の後の `あおま` の打ち間違いの補正が効かなくなる → `alma` を外した。
    - 直す 2: `docs/AGENT_STATE.md` の更新 → PR の前に更新した。
  - 査読 2 周目: 止める指摘は無し。直す 1 件・任意 2 件。
    - 直す: 境界例の一括比較の記録が、`vuln` を打っていない 1 回目の値だった → 最終の形で回し直した値に差し替えた。
    - 任意: 前に日本語がつながった `avuln` のずれを非目標に足した。`vuittonn` を守る行に足した。
  - 査読 3 周目: 止める・直す指摘なし。任意 2 件 (完成条件の印・この記録) を入れた。
- 数字: Core のテスト・eval-compare・境界例の値は、`docs/AGENT_STATE.md` と PR 本文に書く。
- 手戻りの原因: 境界例の一括比較は、1 文字の置き換えの打ち間違いを作らない。そのため、語の境目の `あ + おまえ` の打ち間違いを見落とした。査読員が手で作った境界例で見つかった。
- PR: https://github.com/whitewater-png/Meltype-for-Mac/pull/34

## 人の判断
- **決定 (2026-11-25 リョウさん「おすすめで」): main に取り込む (PR #34、旧 032)。要件番号は main の 031・032 と重なるので付け替えた**

## 記録
- 2026-11-25 リョウさんの実機 (PR #33・#34 と状態の棚卸しをまとめた版): `vuln` → vuln、`vulnwo` → vulnを (ゔおん にならない)。問題なし。
