---
title: ターミナルで、英単語 + 数字のコマンドの名前 (python3・pip3) の後の空白が消える。正解表 mac-terminal をコード系アプリとして測る
status: approved   # 2026-11-23 リョウさんの指示「メルタイプの案件は一旦おすすめで進めてください」(docs/ACCURACY_PLAN_2026-11.md 2-3 の最初の型「パス・コマンドの記号」。人の判断は推奨どおり)
source: docs/ACCURACY_PLAN_2026-11.md 2-3 / docs/KNOWN_LIMITS.md K79 / eval/cases/mac-terminal.tsv の既知の外れ
scope: core
---

# 030 ターミナルで、英単語 + 数字のコマンドの名前の後の空白が消える

## 背景 (誰が、何に困っているか)
- 精度の計画 2-3 の最初の型「パス・コマンドの記号」(正解表の外れ 4 件) を調べたら、4 件の中身は次のとおりだった (2026-11-23、クラウドの Linux、HEAD 508bc3a の Core。テスト用のキーボード (`CompositionTests.Keyboard`) で `CodeApp` を切り替えて打って Enter)。
  - `cd ~/Documents/Meltype` (`mac-terminal.tsv`): ターミナル (コード系アプリ) では当たる (K79 で 2026-10-15 に直っている)。外れているのは、正解表の実行が一般アプリとして打っているから (測り方の問題)。
  - `python3 -m venv .venv` (`mac-terminal.tsv`): ターミナルでも崩れる。一般アプリ `python3ーmvenv .venv`、コード系アプリ `python3-m venv .venv` (`python3` の後の空白が消える)。
  - `cd ~/Downloads` (`mac-protected-spans.tsv`): 一般アプリの外れ。K79 は「一般アプリでは今までどおり」と決めていて、`CommandNameTests.GeneralApp_CommandNames_AreUnchanged` が守っている。
  - `npx create-next-app` (`mac-terminal.tsv`): どちらでも `npx create-next-あっp`。原因が違う (`-` でつないだ語の最後がかなになる)。
- ターミナルで Python を使う人が、`python3 -m venv .venv`・`pip3 install requests` を打つと、`python3`・`pip3` の後の空白が消える (コード系アプリで `python3 venv` → `python3venv`、`pip3 install requests` → `pip3install requests`)。`python -m venv` は崩れない。
- 実機は未確認。

## 今の動き (既に分かっていること)
- K79 (直った、2026-10-15 に Terminal.app の実機で確認): コード系アプリの語の頭で、コマンド名の一覧 (`CompositionDetector.CommandNames`、`src/Meltype.Core/Composition/CompositionDetector.cs` 1105 行付近) と完全一致する語は、確定・Space・Tab・Enter で英字にする。一覧に `python3`・`pip3` は無い。`CommandNameTests.CommandNames_AreLowerCaseWordsOnly` が一覧を小文字の英字だけに縛っている。`python3` の区間は `EN:python` + `JA:3` で、最後の区間が日本語なので、Space は `CompositionController` の Space の最後の分岐 (変換を始める) に落ちる (調査メモ)。
- コード系アプリでも、`python3` + Space・`iphone15` + Space は変換が始まる (表示は `python3`・`iphone15`)。`3ji` + Space → `3じ`、`nihongo` + Space → `日本語`、`ka` + Space → `か` (どちらのアプリでも変換)。
- 正解表の実行 (`src/Meltype.Core.Tests/Repro.cs` の `Checks.ExpectAll`・`Checks.Evaluate`) は、一般アプリとして打つ。表ごとにアプリの種類を変える方法は無い。
- `mac-terminal.tsv` の `full` の行をコード系アプリとして打つと、外れは `python3 -m venv .venv` と `npx create-next-app` の 2 行だけ (ほかは全部当たる)。
- 試作 (使い捨ての写しの木で、一覧に `python3`・`pip3` を足しただけ): コード系アプリで `python3 -m venv .venv`・`python3 -m http.server`・`python3 --version`・`pip3 install requests`・`python3 venv` が打ったとおりになった。一般アプリ、日本語の直後の `python3` + Space、`iphone15`・`3ji`・`nihongo` + Space は今までどおり。落ちたのは `CommandNames_AreLowerCaseWordsOnly` だけ。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドからは本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 正解表の表ごとに「コード系アプリとして打つ」を指定できる。指定は、古い木 (main を含む) では無視されて今までどおりに動く形にする (例: 表の中のコメント行)。`mac-terminal.tsv` に指定を入れる。
2. コード系アプリの語の頭で、英単語 + 数字のコマンドの名前 `python3`・`pip3` は、K79 のコマンド名と同じく、確定・Space・Tab・Enter で英字にする。K79 の一覧 (`CommandNames`) に足し、一覧の形式は「小文字の英字のあとに数字が 0 個以上」に広げる (人の判断 4)。後ろの `-m`・`venv`・`.venv`・`http.server`・`install`・`requests` は打ったとおりに残る。
3. 一般アプリ・日本語の入力は変えない (K79 の決まり)。`3ji` + Space・`nihongo` + Space は、どちらのアプリでも今までどおり変換。

## 非目標 (今回はやらない)
- 一般アプリで、コマンドの名前の後の空白が消える件 (`cd ~/Downloads` → `cd～/Downloads` など)。K79 の「一般アプリでは今までどおり」を変えることになる (人の判断 1)。調査メモの案 (Space の分岐に「変換しても見た目が打鍵と同じ ASCII の英数字なら、英語で確定して空白」を足す) は、2〜3 文字の全組み合わせ + Space で、かなに読める出力の変化は 0 件だった。変わるのは、ローマ字として読めない子音の並び (`kd`・`ts` など) + Space が変換でなく空白つきの確定になることと、`iphone15 wo` の空白が残ること。別の要件の候補として人に出す。
- `-` でつないだ語の最後がかなになる形 (`npx create-next-app` → `npx create-next-あっp`)。別の要件にする。
- コマンドの引数の、ローマ字として読める短い語 (`grep -n foo` → `grep -n ふぉお`、`mv a b` → `mv あb`)。
- `python3`・`pip3` 以外の、数字つきの名前を広く拾う規則 (`iphone15` など)。一覧に足す形にとどめる。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表の実行: 表の中の指定で、その表の行をコード系アプリとして打つ。指定の無い表は今までどおり。古い木 (base の main) で `tools/eval-compare.sh` が今までどおり回る (終了コード 2 にならない)。
- [ ] 正解表: `mac-terminal.tsv` をコード系アプリとして測ると、`cd ~/Documents/Meltype` が当たる。`python3 -m venv .venv` は実装前は外れ (`python3-m venv .venv`)、実装後は当たる。
- [ ] 正解表: `mac-terminal.tsv` に次の行を足す。`python3`・`pip3` の 3 行は実装前は外れ、実装後は当たる。`cd`・`ls`・`rm` の 5 行は、コード系アプリでは今も当たる (K79 の守り)
  ```
  python3 -m http.server	python3 -m http.server	full	要件 030
  python3 --version	python3 --version	full	要件 030
  pip3 install requests	pip3 install requests	full	要件 030
  cd ..	cd ..	full	要件 030 (K79 の守り)
  cd /tmp	cd /tmp	full	要件 030 (K79 の守り)
  ls -la	ls -la	full	要件 030 (K79 の守り)
  ls ~/Library	ls ~/Library	full	要件 030 (K79 の守り)
  rm -rf node_modules	rm -rf node_modules	full	要件 030 (K79 の守り)
  ```
- [ ] テスト: `CommandNameTests` に、コード系アプリの `python3 -m venv .venv` と `pip3 install requests` (Enter で確定) が打ったとおりになることを足す。実装前は赤。
- [ ] テスト: `CommandNames_AreLowerCaseWordsOnly` を「小文字の英字のあとに数字が 0 個以上」に変える (テストの名前と説明も合わせる)。英字の無い語・数字で始まる語・数字のあとに英字が続く語は、今までどおり落ちる形にする。
- [ ] 守り (テスト): 一般アプリの `python3` + Space は今までどおり (変換が始まる)。コード系アプリでも、語の頭でない `python3` (日本語の直後) と、`3ji` + Space・`nihongo` + Space は今までどおり。守りを外す改造 (一般アプリにも効かせる・語の頭を見ない) で落ちることを確かめる。
- [ ] 回帰なし: Core のテストが全件通る。`tools/eval-compare.sh` (base は main) の broken 0。
- [ ] 境界例: 判定を変えるので、`tools/boundary-sweep.sh` light で base と比べて違い 0 (一般アプリで打つ道具なので、変わらないことの確認)。加えて、実装者とは別のモデルの査読員が、コード系アプリと一般アプリの両方で、数字つきの語 + Space・Enter (`python3`・`pip3`・`iphone15`・`3ji`・`kyouha3jini`・`python3wo` など) を自分で作って base と比べる。変わった形は全部、受け入れるかどうかを一覧にする。
- [ ] 文書: `docs/KNOWN_LIMITS.md` の K79 の直った範囲に `python3`・`pip3` を足す (実機は未確認と書く)。`eval/README.md` (または正解表の書き方の説明がある所) に、表ごとの指定の書き方を書く。`eval/cases/mac-protected-spans.tsv` の `cd ~/Downloads` の行の出典の注を「一般アプリでは K79 の決まりで今までどおり (要件 030 の非目標)」の趣旨に直す (今の「mac-terminal.tsv の cd ~/Documents/Meltype と同じ原因」は、あちらが測り方の問題だったので不正確)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] ターミナル (zsh): `python3 -m venv .venv` → `python3 -m venv .venv` (表示だけ確かめる。実行は任意)
- [ ] ターミナル: `pip3 install requests` → `pip3 install requests` (表示だけ確かめる。**Enter で実行しない**。確かめたら Ctrl+C で消す)
- [ ] ターミナル: `cd ~/Downloads` → `cd ~/Downloads` (今までどおり)
- [ ] ターミナル: `nihongo` + Space が今までどおり変換になる
- [ ] TextEdit (一般アプリ): `nihongo` + Space が今までどおり変換になる

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 一般アプリの動きを変えないと成り立たないとき (K79 の決まりを変えるのは人の判断)。
- Swift (`mac/`) の変更や、コード系アプリの見分け方 (`MeltypeSession.MacCodeApps`) の変更が要るとき。
- 正解表の指定が、古い木での `tools/eval-compare.sh` を壊す (終了コード 2) 形でしか作れないとき。

## 人の判断 (2026-11-23 に推奨どおり決定。リョウさんの指示「メルタイプの案件は一旦おすすめで進めてください」)
1. 一般アプリでも、コマンドの名前 (`cd`・`ls` など) の後の Space で空白を入れるか。→ **決定**: この要件ではやらない (推奨。K79 で「一般アプリでは今までどおり」と決めていて、守りのテストもある。コード系アプリにしたいアプリは、利用者がアプリの種類を「コード」にできる)。
2. 正解表 `mac-terminal.tsv` を、コード系アプリとして測るように変えるか。→ **決定**: 変える (推奨。表の中身はターミナルで打つ行で、今は一般アプリとして測っているので、外れの数が実際と合わない)。
3. `npx create-next-app` (精度の計画で同じ型に数えた 4 件のうちの 1 件) を別の要件にするか。→ **決定**: 別にする (推奨。原因が違う。精度の計画の「1 型 1 要件」)。
4. `python3`・`pip3` を、K79 の一覧に足して形式を広げるか、数字つきの名前の別の一覧を作るか。→ **決定**: 一覧に足して、形式を「小文字の英字のあとに数字が 0 個以上」に広げる (推奨。完全一致の仕組みは同じで、一覧が 1 つのほうが読みやすい。一覧に無い `iphone15` などは今までどおりで、守りのテストで固定する)。

## 記録 (ループが書く)
- 周回数 / 上位・下位のどちらのモデルか (モデルの名前は書かない) / 所要時間 / 手戻りの原因 / PR の URL
