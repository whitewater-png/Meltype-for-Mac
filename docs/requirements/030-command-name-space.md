---
title: コマンドの名前の後の空白が消え、後ろのパスやオプションが崩れる (cd・ls・rm・python3 など)
status: approved   # 2026-11-23 リョウさんの指示「メルタイプの案件は一旦おすすめで進めてください」(docs/ACCURACY_PLAN_2026-11.md 2-3 の最初の型「パス・コマンドの記号」。人の判断は推奨どおり)
source: docs/ACCURACY_PLAN_2026-11.md 2-3 / docs/KNOWN_LIMITS.md K79 / eval/cases/mac-terminal.tsv・mac-protected-spans.tsv の既知の外れ
scope: core
---

# 030 コマンドの名前の後の空白が消え、後ろのパスやオプションが崩れる

## 背景 (誰が、何に困っているか)
- ターミナルでよく打つ短いコマンドで、コマンドの名前の後の空白が消え、後ろのパスやオプションが全角や長音になる。Mac のターミナルや AI のチャットにコマンドを打つ人が困る (精度の計画 2-3 で、打つ頻度が高そうな型の 1 番目)。
- 今の挙動 (Core。2026-11-23、クラウドの Linux、HEAD 508bc3a。打って Enter):
  - `cd ~/Downloads` → `cd～/Downloads`、`cd ~/Documents/Meltype` → `cd～/Documents/Meltype`
  - `cd ..` → `cd。。`、`cd /tmp` → `cd/tmp`、`cd Documents` → `cdDocuments`、`cd x` → `cdx`
  - `ls -la` → `lsーぁ`、`ls ~/Library` → `ls～/Library`
  - `rm -rf node_modules` → `rmーrfnode_modules`
  - `python3 -m venv .venv` → `python3ーmvenv .venv`、`python3 venv` → `python3venv`
- 同じような形でも崩れないもの: `mkdir -p src`・`ssh -i key`・`tail -f log`・`git status`・`docker compose up -d`・`python -m venv`・`pip install -r requirements.txt`。`mv a b` も `mv` の後の空白は残る (`a` がかなになるのは別の件)。
- 実機は未確認 (K79 は実機の記録から書かれた行)。

## 今の動き (既に分かっていること)
- `docs/KNOWN_LIMITS.md` K79: 「`cd`・`ls` が英語と判定されず Space で変換が始まって空白が消える」(保護区間とは別の原因)。ただし `--explain cd` では 2 文字目で English (日本語の入力として成立しない) と出る。どこで Space が変換になるかは未確認 (調査で確かめる)。
- 関係しそうな場所: `src/Meltype.Core/Composition/CompositionController.cs` (Space の扱い)、`CompositionDetector.cs` (判定)、保護区間 (`~/` で始まるパス、`-` で始まるオプション。K78: 英語の後ろなら守る)。
- 正解表: `eval/cases/mac-protected-spans.tsv` の `cd ~/Downloads`、`eval/cases/mac-terminal.tsv` の `cd ~/Documents/Meltype`・`python3 -m venv .venv` (どれも既知の外れ)。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドからは本家に届かない)。

## minimum の要件 (これだけは満たす)
1. ローマ字として読めない英字のコマンドの名前 (`cd`・`ls`・`rm` など) と、英単語 + 数字の名前 (`python3`) の後で Space を押すと、空白が入る (変換を始めない)。
2. その後ろに続けて打つパス・オプション (`~/…`・`/tmp`・`..`・`-la`・`-rf`・`-m`) と語は、打ったとおりの半角で残る (「今の挙動」の行が、打ったとおりになる)。
3. 日本語の入力は変えない: ローマ字として読める打鍵の後の Space の変換、日本語の中の `~` → `～`・`.` → `。`・`-` → `ー`、打ち間違いの補正。

## 非目標 (今回はやらない)
- ローマ字として読めるコマンドの名前 (`node -v` → `のでーv`、`open ~/Desktop` → `おぺん～/Desktop`)。英語と日本語のどちらでも読めるので、別の要件にする。
- `-` でつないだ語の最後がかなになる形 (`npx create-next-app` → `npx create-next-あっp`)。原因が違うので別の要件にする (精度の計画で「パス・コマンドの記号」に数えた 4 件のうちの 1 件)。
- コマンドの引数の、ローマ字として読める短い語 (`grep -n foo` → `grep -n ふぉお`、`mv a b` → `mv あb`)。
- 文の中の略語 + 助詞 (`cdwokatta` は今は全部英字のまま)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: 既にある 3 行 (`cd ~/Downloads`・`cd ~/Documents/Meltype`・`python3 -m venv .venv`) が、実装後に当たる。
- [ ] 正解表: `eval/cases/mac-terminal.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  cd ..	cd ..	full	要件 030
  cd /tmp	cd /tmp	full	要件 030
  cd Documents	cd Documents	full	要件 030
  ls -la	ls -la	full	要件 030
  ls ~/Library	ls ~/Library	full	要件 030
  rm -rf node_modules	rm -rf node_modules	full	要件 030
  python3 -m http.server	python3 -m http.server	full	要件 030
  ```
- [ ] テスト: Core のテストに、`cd` + Space で空白が入り変換が始まらないこと、`python3` + Space で同じこと、を足す。実装前は赤。
- [ ] 守り: ローマ字として読める打鍵の後の Space は変換のまま (例: `ka` + Space、`nihongo` + Space)。日本語の中の `~`・`-` は今のまま (偽の変換エンジンで `10ji~12ji` → `10じ～12じ`、`ra-menwotabeta` → `らーめんをたべた`)。テストか正解表の行で固定し、守りを外す改造で落ちることを確かめる。
- [ ] 回帰なし: Core のテストが全件通る。`tools/eval-compare.sh` の broken 0。
- [ ] 境界例: 判定と Space の扱いを変えるので、`tools/boundary-sweep.sh` (light 以上) で base と比べて日本語の損失 0・英語の損失 0。加えて、実装者とは別のモデルの査読員が、Space を含む境界例 (ローマ字として読めない短い打鍵 + Space、子音の打ち間違い + Space、英単語 + 数字 + Space、日本語の文の途中の Space など) を自分で作って base と比べる。変わった形は全部、受け入れるかどうかを一覧にする。
- [ ] 文書: `docs/KNOWN_LIMITS.md` の K79 を直った側に移す (実機は未確認と書く)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] ターミナル (zsh): `cd ~/Downloads` → `cd ~/Downloads` と出て、Enter で移動できる
- [ ] ターミナル: `ls -la` → `ls -la`
- [ ] ターミナル: `python3 -m venv .venv` → `python3 -m venv .venv` (表示だけ確かめる。実行は任意)
- [ ] ターミナル: `rm -rf node_modules` → 表示だけ確かめる (**Enter で実行しない**。確かめたら Ctrl+C で消す)
- [ ] TextEdit: `cd ~/Downloads` → `cd ~/Downloads`
- [ ] 日本語: `nihongo` + Space が今までどおり変換になる。`10ji~12ji` の `~` が今までどおり `～` になる

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- Space の扱いを変えるのに、Swift (`mac/`) の変更が要ると分かったとき。
- 日本語の入力で Space の動きや確定の結果が変わる形が出て、守りの条件で消せないとき (一覧にして人に出す。精度の計画 §7-2 の決定: ふだんの要件は broken 0)。
- 設定 (`CompositionOptions`) の既定の動きを変える必要があるとき。

## 人の判断 (2026-11-23 に推奨どおり決定。リョウさんの指示「メルタイプの案件は一旦おすすめで進めてください」)
1. 範囲を、ローマ字として読めない名前と、英単語 + 数字の名前に絞り、`node`・`open` のようにローマ字としても読める名前は別の要件にするか。→ **決定**: 絞る (推奨。読める名前は日本語の語とぶつかるので、守りの作り方が別になる)。
2. `npx create-next-app` (精度の計画で同じ型に数えた 4 件のうちの 1 件) を別の要件にするか。→ **決定**: 別にする (推奨。原因が違う。精度の計画の「1 型 1 要件」)。

## 記録 (ループが書く)
- 周回数 / 上位・下位のどちらのモデルか (モデルの名前は書かない) / 所要時間 / 手戻りの原因 / PR の URL
