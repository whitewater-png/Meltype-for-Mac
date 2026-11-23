# 現在地 (AI 開発ループの状態)

> 1 枚だけの「今どこか」。ループの終わりと、文脈が圧縮される前に更新する。履歴は git に任せ、ここには最新だけを書く。
> 話題ごとの詳しい引き継ぎは `docs/HANDOFF-*.md`、本家 PR の状態は `docs/UPSTREAM_PR_TRACKER.md`、設計と数字は `docs/IMPROVEMENT_PLAN.md`。

## 基準の数字 (2026-11-23、クラウド Linux 4 コア、ブランチ ccr-0fc8aeab-sra4z7、HEAD 8da7bac = origin/main 496c90e (PR #30 まで) を取り込んだ後)
| 項目 | 値 | コマンド |
|---|---|---|
| Core のテスト | 1297 / 1297 (main の取り込み後。スキップ件数は未確認)。Python のテスト: test_eval_compare.py 8 件、test_eval_legacy_base.py 5 件、test_boundary_sweep.py 21 件、test_upstream_watch.py 37 件、test_update_cask.py OK | `dotnet run -c Release --project src/Meltype.Core.Tests` |
| 品質テスト | 1295 / 1296 (99.9 %)。外れ: `my name is taro` のみ | `-- --eval` |
| 正解表 (10 表) | 717 / 823。mac-device 40/41、mac-protected-spans 52/53、mac-terminal 33/37、paren-space 10/12、short-words-in-english 343/424、upstream-guards 8/8、upstream-guards-1118 8/8、upstream-guards-1120 12/12、upstream-guards-1121 12/12、upstream-quality 199/216。eval-compare (base origin/main 496c90e、704/810) は broken 0 / fixed 0 / added 13 / removed 0 (added は要件 025 (旧 016) の前の文字つきの 13 行で、すべて ok。PR #29 の CI も 8da7bac で成功) | `tools/eval-compare.sh` |
| 境界例の一括比較 | full (既定) 684,102 入力 (j1 203,350 / j2 270,110 / j3 196,735 / e 13,907)、1 本の木あたり約 16 分。light 35,862 入力、約 50〜70 秒 (026 (旧 018) では light 35,862 入力で違い 0。main を取り込んだ後も、origin/main 496c90e と比べて light で違い 0)。ローマ字にできず除いた読み 0。検算 1 (english.txt に undef を足した写し) は full で日本語の損失 289・英語の損失 0・その他 14・終了コード 1、light で日本語の損失 17・その他 1・終了コード 1。検算 2 (同じ木) は full・light とも違い 0・終了コード 0 | `tools/boundary-sweep.sh` (使い方は `docs/DEVELOPMENT.md`) |
| 規模 | 1 キー 0.086 ms (`[規模]`)。2026-10-24 の値。今回は測っていない | |

## 進行中の要件
- 015 判定の悪化を大量の境界例で測る道具 (`tools/boundary_sweep.py`・`boundary_compare.py`・`boundary-sweep.sh`。ブランチ ccr-0fc8aeab-sra4z7、PR #29)。docs/ACCURACY_PLAN_2026-11.md の 0-3、リョウさんの指示「おすすめで進めてください」(2026-11-22) で着手。status は approved のまま (done は人がマージした後)。Core 1259/1259、eval-compare broken 0 / fixed 0。単体テスト 21 件。別エージェントの査読 1 周 (止める・直す指摘なし。任意 1 件: `--expect-all` の無い木を base にする経路は動かしていない)。未確認: Mac で回せるか (`bash --noprofile --norc`)、かな → ローマ字の表 (Jht.cs の手写し) と Jht の出力の機械比較 (代表例のテストのみ)。判定ロジック・辞書は変えていない。
- 025 (旧 016) 正解表に「前の文字」の列を足す (本家の Before つきのケースを測る。PR #29 に積んだ。015 と同じ PR・同じブランチ)。status は approved のまま (done は人がマージした後)。docs/ACCURACY_PLAN_2026-11.md の 0-2 の下準備。Core 1263/1263、eval-compare broken 0 / fixed 0 / added 13。完成条件はすべて達成。別エージェントの査読 3 周 (止める・直す指摘なし。任意 2 件を入れた)。未確認: 本家の木 (`tools/eval_legacy_base.py` の実際の経路) では回していない (単体テストのみ)。前の文字そのものが `"…"` の形は今の形式では書けない (`eval/README.md` に記載)。Mac 実機は要件どおりなし。判定ロジック・辞書は変えていない。
- 017 前の文を Zenzai に渡す (`CompositionOptions.WideConversionContext`、既定 OFF。Mac の経路で Zenzai ON かつ Mozc でないとき ON。PR #29 に積んだ。015・016 と同じ PR・同じブランチ)。2026-11-22 リョウさんの指示「OK、おすすめで対応して」(案 A・区切りの規則は残す・Mac は実機チェックリスト)。status は approved のまま (done は人がマージした後)。Core 1273/1273 (base 136d90c は 1263)、eval-compare broken 0 / fixed 0 / added 0 / removed 0、品質テスト 1295/1296 (base と同じ)、境界例の一括比較 light 35,862 入力で違い 0。機械で測れる完成条件は達成。別エージェントの査読 2 周 (1 周目 opus で直す 2 件と任意 7 件、2 周目 sonnet は指摘なし)。**Mac 実機チェックリストは未確認。** 測れていないこと: 実エンジン (azooKey・Zenzai) での効果 (正解表・一括比較は OFF の経路しか通らない。ON の経路は WideContextTests だけ)、規則を残した形での前の文で決まる 32 行の効果 (14 → 24 は規則なしの値)、ON での文頭の打鍵の変換エンジンの呼び出し回数 (OFF 8 回 → ON 16 回。1 回の変換あたりは最大 2 回のまま。Zenzai の推論は約 2 倍の見込み、推測)、Mac の入力欄が U+2028 などを渡すか。Swift・設定ファイルの形式・入力欄から読む範囲は変えていない。
- 026 (旧 018) ライブ変換の表示のまま Enter で確定したときの学習 (案 A。予測用の回数 `history.Touch` と登録提案の回数 `RecordSuggestions` だけ数える。azooKey の学習はしない。PR #29 に積んだ。015〜017 と同じ PR・同じブランチ)。2026-11-22 リョウさんの指示「OK、おすすめで対応して」。status は approved のまま (done は人がマージした後)。Core 1290/1290 (base d210044 は 1273)、eval-compare broken 0 / fixed 0 / added 0 / removed 0、品質テスト 1295/1296 (base と同じ)、境界例 light 35,862 入力で違い 0。別エージェントの査読 2 周 (2 周目は指摘なし)。Mac 実機チェックリストは未確認。
- 精度の計画 `docs/ACCURACY_PLAN_2026-11.md` が今の方向 (§7 の 6 件はリョウさんの指示で決定済み)。
- 002 ost が おst に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/002-short-english-words、PR #7 で main に統合済み)。定期タスクが docs/voice/2026-10-26.md の候補 1 から自動承認で着手。Core 1146/1146、eval-compare broken 0 / fixed 2。Mac 実機チェックリストはリョウさんが確認済み (2026-10-28。項目ごとの記録なし)。`gooddane` (本家 #1192) は判定ロジックの変更が要るため別の要件に残す。
- 003 (長いローマ字文が全部かなになることを Core で守るテスト): PR #8 で main に統合済み・Mac 実機チェックリストは未確認。守りのテストで判定ロジックは未変更。外れは出なかった (400 文字超でも全部かな)。
- 004 母音の無い英字 + ハイフンを長音にしない (ブランチ req/004-hyphen-english、PR 作成後に番号を記す)。docs/voice/2026-10-30.md 候補 2 から自動承認で着手。Core 1228/1228、eval-compare broken 0 / fixed 6。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機チェックリストは未確認。日本語の直後の `git-log` (候補 1) は別の要件に残す。
- 005 beep が べえp に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/005-beep-english)。docs/voice/2026-10-31.md 候補 1 から自動承認で着手。Core 1229/1229、eval-compare broken 0 / fixed 3。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機は `beep` + Enter だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。`beepon` は `べえぽん` のまま (`beep音` は漢字なので Core では測れない)。候補 2 (未確定中の機能キー、Swift) は人が実機で扱う要件に残す。
- 006 計算の候補で `^` (べき乗) を読む (ブランチ claude/wizardly-cori-v99xo5)。docs/voice/2026-11-01.md 追記の候補 2 から、リョウさんの指示 (2026-11-01) で着手。Core 1231/1231、eval-compare broken 0 / fixed 0 (555/661)、品質テスト 1294/1295 (base と同じ)。別エージェントの査読 1 周 (止める指摘なし)。Mac 実機は `2^10` + Space の候補だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 008 iCloud・iTunes の頭の i がかなになる (`dictionaries/propernouns.txt` に 2 語。ブランチ req/008-icloud-english)。docs/voice/2026-11-04.md 候補 1 から自動承認で着手。Core 1229/1229、eval-compare broken 0 / fixed 4。別エージェントの査読 1 周 (止める指摘なし)。`english.txt` に小文字で足しても大文字入りは直らず、`propernouns.txt` に足した。Mac 実機は `iCloud` + Enter だけリョウさんが確認済み (2026-11-05、「1～3＞OK」の一括報告で項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 010 karento が karenと になる (`japanese.txt` に `karento` + `CompositionDetector` の固有名詞 + 助詞の守りを 2 行。ブランチ req/010-karento、PR #15 で main に統合済み 2026-11-11)。docs/voice/2026-11-06.md 候補 2 から自動承認で着手。Core 全件通過、eval-compare broken 0 / fixed 3。別エージェントの査読 1 周 (止める指摘なし)。受け入れる変化: 小文字の人名 Karen + と もかなになる、`karent` + 子音・記号が `かrent` に崩れる (要件に記載。2026-11-11 リョウさんが受け入れた。独立査読で、新しい条件が効くのは同梱の辞書では karen + to の 1 組だけ、日本語の入力 80,554 件の変化 0 件。変わるのは karen を含む入力と、karent を含まない karento 系の打ち間違い 57 形)。Mac 実機は 2026-11-11 にリョウさんが確認 (`karento`・`Karento`・`karenda-` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 011 colab・collab・kaggle が英字にならない (`dictionaries/english.txt` に 3 語。ブランチ req/011-colab-english、PR #16 で main に統合済み 2026-11-11)。docs/voice/2026-11-07.md 候補 1・2 から自動承認で着手。Core 1255/1255、eval-compare broken 0 / fixed 5。別エージェントの査読 1 周 (止める指摘なし。約 372 万入力で打ち間違い補正の変化 0 件。独立査読 2026-11-11 の約 49 万入力でも日本語の変化 0 件)。Mac 実機は 2026-11-11 にリョウさんが確認 (`colab`・`collab`・`kaggle`・`korabo` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 012 プリプロセッサの指令名 (endif・elifndef・pragma) が崩れる (`dictionaries/english.txt` に 3 語。ブランチ req/012-preprocessor-english、PR #17 で main に統合済み 2026-11-11)。docs/voice/2026-11-08.md 候補 3 から自動承認で着手。最初は 5 語で、別エージェントの査読 2 周では指摘なしだったが、独立査読 (2026-11-11、約 49 万入力) で `undef` (〜んで の 179 語・18,136 形)・`ifndef` (1 語・24 形) が打ち間違いの補正を止めると分かり、リョウさんの判断で 2 語を外して作り直した (3 語の形は日本語の損失 0)。作り直した版は別の査読者が確認 (止める指摘なし)。origin/main を合流させた木で Core 1259/1259、eval-compare broken 0 / fixed 2。Mac 実機は 2026-11-11 にリョウさんが確認 (`#endif`・`#pragma`・`endifwo`・`undef`・`jibundeftotemo` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。`define` (全部かなに読める `でふぃね`) は判定ロジックの変更が要るので別の要件に残す。
- 013 Obsidian のウィキリンク `[[…]]` が `「「…」」` になる (`CompositionText.HalfWidthWikiLinks`、ブランチ req/013-wikilink-brackets、PR #18 で main に統合済み 2026-11-11)。docs/voice/2026-11-09.md 候補 1 から自動承認で着手。`[[` と続く `]]` だけ半角 (`[` 1 つ・閉じない `[[` は今までどおり)。Core 1255/1255、eval-compare broken 0 / fixed 4。別エージェントの査読 3 周 (止める指摘なし、直す 2 件対応済み。3 周目は指摘なし)。独立査読 (2026-11-11、約 4.7 万入力で `[[ ]]` を含まない入力の変化 0 件) で見つかったテストの穴を `"[[link]]"` の行で塞いだ (main への統合後 Core 1257/1257)。`==`・`%%` の幅は別の要件に残す。Mac 実機は 2026-11-11 にリョウさんが確認 (`[[link]]`・`kyouha[[nikki]]desu`・`"[[link]]"`・`[kagi]` を TextEdit で Enter、`[[Hello World]]` は確定のキーの記録なし。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。
- 014 pixiv が ぴぃv に崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/014-pixiv-english、PR #19 で main に統合済み 2026-11-11)。docs/voice/2026-11-10.md 候補 2 から自動承認で着手。Core 1255/1255、eval-compare broken 0 / fixed 5 (576→581/687)。別エージェントの査読 1 周 (止める指摘なし。境界例 25,159 入力で、pixiv を含まない入力の変化は 0 件。独立査読 2026-11-11 の約 49 万入力でも日本語の変化 0 件)。Mac 実機は 2026-11-11 にリョウさんが確認 (`pixiv`・`pixivnitoukou`・`pinku` を TextEdit で Enter。試し版 Q41・Q43 と合わせた手元の版で、「全て期待結果通り」の一括報告。項目ごとの記録は無い)。ほかのチェックリストは未確認。候補 1 (`alt`・`esc` などキーの名前) と候補 3 (`onvif` の `ゔぃ`) は別の要件に残す。
- 016 obsidian がかなに崩れる (`dictionaries/english.txt` に 1 語。ブランチ req/016-obsidian-english、PR #20 は open)。docs/voice/2026-11-12.md 候補 1 (本家 #1763) から自動承認で着手。Core 1260/1260、eval-compare broken 0 / fixed 7 (630→637/743)。別エージェントの査読 3 周 (止める指摘なし。直す 2 件は要件定義書の「受け入れる変化」の事実違いで対応済み、3 周目は指摘なし)。境界例 1,721,540 入力で、obsidian を含まない入力の確定の変化は 0 件。受け入れる変化 (人の判断待ち): 打ちかけの `obsid`・`obsidi`・`obsidia` で Enter すると表示は英字・確定はかな (`kaggl` と同じ型)。Mac 実機チェックリストは未確認。候補 2 (`bugga`)・候補 3 (前後に半角スペースの記録) は別の要件に残す。番号 015 はブランチ req/015-ci-english (査読 3 周で止めた。PR なし) が使っている。
- 018 dainyuu など -inyuu の形が英単語 in で崩れないことを守る (正解表 9 行 + テスト `Req018_...`。判定ロジック・辞書は未変更。ブランチ req/018-inyuu-guard、PR #23)。docs/voice/2026-11-16.md 候補 3 から自動承認で着手 (本家 #1946 の `だinゆう` はこの fork では起きない)。守りなので、`にゅ` の綴りを一時的に外した木で 9 行とテストが外れることを確かめた。Core 1260/1260、eval-compare broken 0 / fixed 0 (631/737)。別エージェントの査読 3 周 (止める指摘なし、3 周目は指摘なし)。Mac 実機チェックリストは未確認。候補 1・2 (Issue #22・#21) は Swift・入力ソースの登録で実機が要るため人の判断に残す。
- 021 pazolite・oeksound がかなと英字の混ざった形に崩れる (`dictionaries/english.txt` に 2 語。ブランチ req/021-pazolite-oeksound-english、PR #26)。docs/voice/2026-11-19.md 候補 1 から自動承認で着手。Core 1260/1260、eval-compare broken 0 / fixed 10 (630→640/746)。別エージェントの査読 3 周で、日本語の語から作った 915,400 入力は確定・表示とも変化 0 件。**人の判断待ち**: `pazolite` 自体の打ち間違い 227 件の確定が `pazolite` に寄る (完成条件の「2 語を含まない入力の変化 0 件」を文字どおりには満たさない。要件の「受け入れる変化」)。`u-he`・`YUC'e`・`t+pazolite` の `＋` は非目標。Mac 実機チェックリストは未確認。
- 007 pre が pれ に崩れる (本家 #1489): `english.txt` に `pre` を足す案 (PR #12) は **取り込まず閉じた** (2026-11-05、リョウさんが判断)。独立査読 (約 198 万入力) で、正しく打った日本語は変わらないが、1 打鍵多い打ち間違いの補正 (`koprekara`→これから、`oprenzi`→おれんじ など) が 89 語・229 形で効かなくなる (PR の記載は 9 形) ため。辞書への追加で日本語が悪化する語は入れない方針。`pre` の別の直し方 (直前が日本語のときは英語にしない等) は未着手の候補。ブランチ req/007-pre-english は残してある。
- 020 本家 11-18 の誤判定 6 形 (`itotekinagobyuu`・`ronpa`・`onpa`・`Fluorite`・`uchidome`・`fanntatowirukinnsonn`。fork では起きない) を守る (新しい正解表 `eval/cases/upstream-guards-1118.tsv` に 8 行 + テスト `Req020_...`。判定ロジック・辞書は未変更。ブランチ req/020-upstream-1118-guard、PR #25)。docs/voice/2026-11-18.md 候補 2 から自動承認で着手。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 (630/736)。わざと崩した木で 6 形とも外れることを確認。別エージェントの査読 2 周 (2 周目は止める・直す指摘なし)。PR #24 (019) の `upstream-guards.tsv` と衝突しないよう別の表にした (両方が main に入ったら 1 つにまとめてよい)。見つけた今の外れ: 小文字の `fluorite` → `fluおりて`、`Fluoritewotukau` → 丸ごと英字のまま (別の要件の候補)。Mac 実機チェックリストは未確認。候補 1 (文の途中の `zl`・`zk`) は別の要件に残す。
- 022 本家 11-20 の報告の 6 形 (`sitaidesu`・`amatte`・`chomatte`・`yappane`・`kakikomiera-`・`2nitime`。fork ではかなのまま確定する) を守る (新しい正解表 `eval/cases/upstream-guards-1120.tsv` に 12 行 + テスト `Req022_...`。判定ロジック・辞書は未変更。ブランチ req/022-upstream-1120-guard、PR #27)。docs/voice/2026-11-20.md 候補 3 から自動承認で着手。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 (634/740)。わざと崩した木で 12 行とも外れることを確認。別エージェントの査読 1 周目は止める指摘なし (直す 2 件は手続きで対応済み。境界例 38 形で今の外れは見つからず)。PR #24 (019)・#25 (020) の表と衝突しないよう別の表にした (どれも main に入ったら 1 つにまとめてよい)。本家の報告の中心 (漢字の選び方・候補の区切り) は測れず、直していない。Mac 実機チェックリストは未確認。候補 1 (かなの直後の記号の幅)・候補 2 (ユーザー辞書の読みのローマ字) は人の判断が要るので別の要件に残す。
- 009 syi が sい になる (本家 #1464): CompositionTable に `しぃ` を足す案 (PR #14) は **取り込まず閉じた** (2026-11-11、リョウさんが判断)。独立査読で、し の打ち間違いの補正 (`desyita`→でした、`wakarimasyita`→わかりました、`sukosyi`→すこし など) が 4,296 語・16,625 形で効かなくなる (PR の記載は 3 例) ため。`しぃ` は今でも `sixi` で打てる。補正の側で直す案 (syi を shi と読めば語になるときは補正を優先する) は未着手の候補。ブランチ req/009-syi-kana は残してある。
- 019 本家 11-17 の誤判定 5 形 (`baguhoukoku`・`tunagarumademattekara`・`CLAUDE.mdwohiraku`・`,nisuruwo`・`reibunn:pregnant_man:`。fork では起きない) を守る (新しい正解表 `eval/cases/upstream-guards.tsv` に 8 行 + テスト `Req019_...`。判定ロジック・辞書は未変更。ブランチ req/019-upstream-1117-guard、PR #24)。docs/voice/2026-11-17.md 候補 2 から自動承認で着手。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 (630/736)。わざと崩した木で 5 形とも外れることを確認。別エージェントの査読 3 周 (3 周目は止める・直す指摘なし)。査読で見つかった今の外れ `reibunn:+1:` → `れいぶん:＋1:` は別の要件の候補。Mac 実機チェックリストは未確認。候補 1 (数字の後の `,`) と候補 3 (ターミナルの Claude Code、Swift) は別の要件に残す。
- (無し)。001 は PR #5 として base ブランチ `ccr-27ad1b0c-qzazzd` にマージ済み (2026-10-24、人の判断)。main へは PR #4 に含めて出す。Mac 実機チェックリスト (PR #5 本文) は未確認。
- 実装した範囲: 英字 1 文字ずつ読む略語 (`loanwords.txt` の大文字語) + の + 英字の残る英単語。新しく英字になるのは `api`・`ide`・`uri`・`gui`・`who` の 5 語。
- 023 本家 11-21 の誤判定 6 形 (`atude`・`tasinamu`・`haatu`・`getu`・`subewomotanai`・`Yukishiro`) を守る行と作例 6 形 (ブランチ req/023-upstream-1121-guard、PR #28)。docs/voice/2026-11-21.md 候補 2 から自動承認で着手。fork では今どれも起きないので、判定ロジック・辞書は変えず `eval/cases/upstream-guards-1121.tsv` (12 行) とテスト `Req023` だけ。わざと崩した木で 12 行とも外れることを確かめた。Core 1260/1260、eval-compare broken 0 / fixed 0 / added 0 (634/740)。別エージェントの査読 1 周 (止める指摘なし、直す 1 件は この行の追加、任意 2 件は 1 件 (テスト名) を対応・1 件 (`getup`・`at` の今の挙動) は範囲外として残す)。Mac 実機チェックリストは未確認。候補 1 (`verdeha` の `r` が確定で消える。原因は打ち間違いの直し) と候補 3 (括弧の幅) は人の判断が要るので別に残す。
- 024 サロゲートペアの漢字 (𠮷・𩸽) だけの文節を記号とみなし、確定がひらがなになる (`IsSymbolOnly` が UTF-16 の 1 単位ずつ見る。変換エンジンに渡す文脈の切り目も)。PR #29 の要件 017 の作業中に見つけた。2026-11-22 の人の指示で approved (案 A: `IsSymbolOnly` をコードポイントで見る + `ContextOf` の文脈の先頭の下位サロゲート単独を、切ったかどうかに関係なく落とす)。要件定義書だけの PR を作った (PR #30、ブランチ claude/keen-noether-kicstk)。偽の変換エンジンでの再現と、写しの木の試しの直し (コミットしていない) で完成条件の形を確かめた。別エージェントの査読 4 周 (止める指摘なし。直す 6 件はすべて対応)。次の一手: AI は `/improve 024` で実装する (未着手)。人は 024 の教訓 (開いている PR の前と後の両方で効き目を書く) を `docs/requirements/TEMPLATE.md` にも書くかを判断する。Mac 実機チェックリストは未確認 (azooKey が 𠮷 だけの文節を返すかも未確認)。「次の一手」の節は PR #29 と衝突するので、この行に書いた。

## 次の一手
- 人: Mac の Claude デスクトップアプリで、このリポジトリと本家の clone のフォルダーを共有する (本家追従 0-1 のため。2026-11-22 時点でクラウドから使える Mac のフォルダーは 0 件)。
- 人: 017・026 (旧 018) の Mac 実機チェックリスト (要件定義書の「Mac 実機チェックリスト」) を確かめる。
- 人: PR #29 (計画・015・017・025 (旧 016)・026 (旧 018)) の確認とマージの判断。
- AI: Mac のフォルダーが共有されたら 0-1 (本家追従) を回す。
- AI: Zenzai の初回の案内 (計画の 1-3 の (c)。Swift なので Mac で作る) の要件の下書き。
- AI: 計画の 0-5 (Core を通す漢字の計測)。
- 人: `uri` (売り・瓜と読める) を英字にしてよいか判断する。
- 人: 001 の Mac 実機チェックリスト (TextEdit・ターミナル) を確かめて、マージ・要件の `done` を判断する。
- 別の要件にする候補: `my name is taro` (品質テストの最後の外れ)。
- 別の要件にする候補 (026 (旧 018) の範囲外で見つけたこと。026 より前から): 設定「変換後も続けて入力」ON で、ローマ字の再変換 → Space → 続けて打つ → Enter とすると、`Commit` の fixedOnly の分岐が `!_reconvertRomaji` を見ずに `Learn()` を呼ぶので、azooKey と履歴に「きょうはいいてんきですね → kyouha iitenki desu ne」のような誤学習が入る (偽の変換エンジンで再現。base のコードでも同じと査読員が確認)。
- (済) 017 の査読の範囲外で見つけた `IsSymbolOnly` のサロゲートペアの件は、要件 024 (PR #30 で main に統合) になった。
- 辞書の英単語 + 助詞 全般 (`sorenoerror` など) は今回の範囲外。広げるときは境界例を別モデルの査読員に作らせる (`docs/LESSONS.md`)。道具は `tools/boundary-sweep.sh`。

## 1 件目の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 001 apinoerror → apiのerror |
| 所要時間 | 約 33 分 (2026-10-24 09:24:58Z〜09:57:45Z)。人の介入は要件の事実訂正 1 回のみ |
| 周回数 (implementer) | 2 |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、3.0 分) / implementer 2 回 (1 周目 sonnet 5.9 分、2 周目 opus 9.1 分。昇格理由: `Composition/` に触れる + 査読の「止める」指摘) / reviewer 2 回 (opus、6.6 分・4.9 分) / eval-guard 2 回 (sonnet、1.5 分・1.3 分) / doc-keeper 1 回。サブエージェント合計 8 回 |
| 手戻りの原因 | (1) 要件の背景を `--explain` だけで書いて「全体が英語」と誤認し、完成条件に既に通る行 (`urlnoerror`) を入れた。(2) 1 周目の実装が「辞書の英単語 + 助詞」全般に効き、`sorenoerror` → `soreのerror` などの回帰が出た。正解表にその形が無く eval-guard は通り、reviewer が境界例を自分で作って発見。2 周目で `loanwords.txt` の大文字略語に絞って解消。(3) 2 つのエージェントが同じブランチを worktree に取って衝突 (`--detach` で回避) |

## 015 の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 015 判定の悪化を大量の境界例で測る道具 (境界例の一括比較) |
| 所要時間 | 約 53 分 (2026-11-22T04:28:27Z〜05:21:40Z)。人の介入なし (要件の承認はループの前) |
| 周回数 (implementer) | 1 |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、約 3.5 分) / implementer 1 回 (sonnet、約 42.9 分。full の検算 2 本を含む) / reviewer 1 回 (sonnet、約 3.8 分。定義は opus だが、利用者の指定で sonnet にし、オーケストレーター (opus) が要所を読み直した) / eval-guard 1 回 (sonnet、約 1.9 分) / doc-keeper 1 回。サブエージェント合計 5 回 |
| 手戻りの原因 | なし。設計の調整が 1 つ: J3 (語の連結) を最初は約 267 万入力で作ったが、1 入力 約 2.8 ms で 1 本の木 23 分超の見込みになり (止める条件は 30 分)、ん で終わる語 + で に右の語 6 語、ほかの語は 25 語に 1 語などに絞った (196,735 入力) |

## 025 (旧 016) の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 025 (旧 016) 正解表に「前の文字」の列を足す |
| 所要時間 | 約 12 分 (開始 2026-11-22T05:23:39Z、終了 05:36:05Z)。人の介入なし (要件の承認はループの前) |
| 周回数 (implementer) | 2 (ほかに 3 周目をオーケストレーターが自分でテスト 4 行だけ直した) |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、約 1.6 分) / implementer 2 回 (sonnet、約 3.2 分・約 0.4 分) / reviewer 3 回 (sonnet、約 2.3 分・約 0.3 分・約 0.2 分。定義は opus だが、利用者の指定で sonnet にし、オーケストレーター (opus) が要所を読み直した) / eval-guard 2 回 (sonnet、約 2.5 分・約 1.4 分) / doc-keeper 1 回。サブエージェント合計 9 回 |
| 手戻りの原因 | 止める・直すの指摘による手戻りはなし。任意の指摘 2 件を入れた。(1) 2 周目: `tools/eval_compare.py` の鍵の単体テスト `tools/test_eval_compare.py` と README の 1 文。(2) 3 周目: 2 周目のテスト `test_repeated_same_key_is_numbered` が、鍵から何回目かの番号を外す改造をしても通っていた (査読員が写しで改造して見つけた)。同じ鍵が 3 回と 2 回の表で removed 1・added 1 を足し、番号を外した写しで落ちることを確かめた (`docs/LESSONS.md` に記録)。1 周目の AGENT_STATE の更新漏れは記録係の段で直した |

## 017 の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 017 前の文を Zenzai に渡す |
| 所要時間 | 約 46 分 (開始 2026-11-22T08:17:18Z、終了 09:02:50Z)。人の介入なし (要件の承認はループの前) |
| 周回数 (implementer) | 2 |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、約 2.4 分) / implementer 2 回 (1 周目 sonnet 約 3.9 分、2 周目 opus 約 17.9 分。昇格理由: `Composition/` に触れる + 1 周目の査読の指摘) / reviewer 2 回 (1 周目 opus 約 15.2 分、2 周目 sonnet 約 1.7 分。1 周目は実装者と別のモデルにした (CLAUDE.md §4 の境界例の決まり)) / eval-guard 2 回 (sonnet、約 3.2 分・約 2.3 分) / doc-keeper 1 回 |
| 手戻りの原因 | (1) 1 周目で新しいメソッドを、別のメソッドの要約コメントと本体の間に入れた (査読で発見)。(2) 任意の指摘のうち入れたもの: 広い文脈の先頭の下位サロゲート単独を「20 単位を超えて切ったときだけ」落としていた (自分の確定の記録はちょうど 20 単位で切られ、下位サロゲートから始まりうる)、長い読みの区切りの文脈が先に効くことを守るテストが無かった (改造が通った)、U+2028・U+2029・垂直タブを改行として扱っていなかった、要約コメント 2 件。(3) 2 周目の実装者が、Edit ツールやヒアドキュメントでエスケープの形 (バックスラッシュ + u2028) を書くとファイルに生の U+2028 が入り、C# のビルドが「Newline in constant」で落ちた (Python で書き直して解決。生の文字が 0 件であることはオーケストレーターと査読員も確かめた。`docs/LESSONS.md` に記録) |

## 026 (旧 018) の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 026 (旧 018) ライブ変換の表示のまま Enter で確定したときの学習 |
| 所要時間 | 約 55 分 (開始 2026-11-22T09:04:11Z、終了 09:58:47Z)。人の介入なし (要件の承認はループの前) |
| 周回数 (implementer) | 2 |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、約 1.8 分) / implementer 2 回 (1 周目 sonnet 約 4.3 分、2 周目 opus 約 23.6 分。昇格理由: `Composition/` に触れる + 1 周目の査読の指摘) / reviewer 2 回 (1 周目 opus 約 18.0 分、2 周目 sonnet 約 2.3 分。1 周目は実装者と別のモデル) / eval-guard 2 回 (sonnet、約 2.3 分・約 2.4 分) / doc-keeper 1 回 |
| 手戻りの原因 | (1) 1 周目の「語末の n をかなのまま確定したら数えない」テストが、偽の変換エンジンでは学習済みの語が入らない入力だったので、守り (`shownAsLive = false`) を外しても通った。査読員が守りを外して、見えてもいない語 (簡単) を数えてしまう例を見つけた。入力を「かんたん→簡単 を学習済み・EnterCommitsAsShown ON・kantan Enter」に替え、守りを外すと落ちることを確かめた。(2) 1 周目の実装者は fixedOnly (変換後も続けて入力) を「二重に数えないため」数えないとしたが、査読で二重にならないと分かり、2 周目で数えるようにした。(3) 任意の指摘のうち入れたもの: 表示と確定の形が違う Enter でも、確定した学習済みの語を数える動きをテストで固定、数えない条件のテスト (1 音確定・確定後の F6 の変換ボックス)、学習の待ち方を対照が届くまで待つ形に、K95 の理由の訂正。(4) PR 本文と AGENT_STATE が古かった (1 周目の査読) |
