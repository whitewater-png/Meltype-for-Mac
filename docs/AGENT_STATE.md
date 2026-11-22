# 現在地 (AI 開発ループの状態)

> 1 枚だけの「今どこか」。ループの終わりと、文脈が圧縮される前に更新する。履歴は git に任せ、ここには最新だけを書く。
> 話題ごとの詳しい引き継ぎは `docs/HANDOFF-*.md`、本家 PR の状態は `docs/UPSTREAM_PR_TRACKER.md`、設計と数字は `docs/IMPROVEMENT_PLAN.md`。

## 基準の数字 (2026-11-22、クラウド Linux 4 コア、ブランチ ccr-0fc8aeab-sra4z7、HEAD 1d80438)
| 項目 | 値 | コマンド |
|---|---|---|
| Core のテスト | 1263 / 1263 (base 9a38f39 は 1259 / 1259。+4 件。スキップ件数は未確認)。Python のテスト: test_eval_compare.py 8 件、test_eval_legacy_base.py 5 件、test_boundary_sweep.py 21 件、test_upstream_watch.py 37 件、test_update_cask.py OK | `dotnet run -c Release --project src/Meltype.Core.Tests` |
| 品質テスト | 1295 / 1296 (99.9 %)。外れ: `my name is taro` のみ | `-- --eval` |
| 正解表 (6 表) | 635 / 741。mac-device 40/41、mac-protected-spans 52/53、mac-terminal 33/37、paren-space 10/12、short-words-in-english 301/382、upstream-quality 199/216。eval-compare (base 9a38f39) は broken 0 / fixed 0 / added 13 / removed 0、終了コード 0 (added の 13 件は upstream-quality の前の文字つきの行で、すべて ok) | `tools/eval-compare.sh` |
| 境界例の一括比較 | full (既定) 684,102 入力 (j1 203,350 / j2 270,110 / j3 196,735 / e 13,907)、1 本の木あたり約 16 分。light 35,862 入力、約 50〜70 秒。ローマ字にできず除いた読み 0。検算 1 (english.txt に undef を足した写し) は full で日本語の損失 289・英語の損失 0・その他 14・終了コード 1、light で日本語の損失 17・その他 1・終了コード 1。検算 2 (同じ木) は full・light とも違い 0・終了コード 0 | `tools/boundary-sweep.sh` (使い方は `docs/DEVELOPMENT.md`) |
| 規模 | 1 キー 0.086 ms (`[規模]`)。2026-10-24 の値。今回は測っていない | |

## 進行中の要件
- 015 判定の悪化を大量の境界例で測る道具 (`tools/boundary_sweep.py`・`boundary_compare.py`・`boundary-sweep.sh`。ブランチ ccr-0fc8aeab-sra4z7、PR #29)。docs/ACCURACY_PLAN_2026-11.md の 0-3、リョウさんの指示「おすすめで進めてください」(2026-11-22) で着手。status は approved のまま (done は人がマージした後)。Core 1259/1259、eval-compare broken 0 / fixed 0。単体テスト 21 件。別エージェントの査読 1 周 (止める・直す指摘なし。任意 1 件: `--expect-all` の無い木を base にする経路は動かしていない)。未確認: Mac で回せるか (`bash --noprofile --norc`)、かな → ローマ字の表 (Jht.cs の手写し) と Jht の出力の機械比較 (代表例のテストのみ)。判定ロジック・辞書は変えていない。
- 016 正解表に「前の文字」の列を足す (本家の Before つきのケースを測る。PR #29 に積んだ。015 と同じ PR・同じブランチ)。status は approved のまま (done は人がマージした後)。docs/ACCURACY_PLAN_2026-11.md の 0-2 の下準備。Core 1263/1263、eval-compare broken 0 / fixed 0 / added 13。完成条件はすべて達成。別エージェントの査読 3 周 (止める・直す指摘なし。任意 2 件を入れた)。未確認: 本家の木 (`tools/eval_legacy_base.py` の実際の経路) では回していない (単体テストのみ)。前の文字そのものが `"…"` の形は今の形式では書けない (`eval/README.md` に記載)。Mac 実機は要件どおりなし。判定ロジック・辞書は変えていない。
- 017・018 (draft、人の判断待ち)。017 は案 A で進めてよいか・区切りの規則・Mac での測り方、018 は案 A・B・C の選択。
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
- 007 pre が pれ に崩れる (本家 #1489): `english.txt` に `pre` を足す案 (PR #12) は **取り込まず閉じた** (2026-11-05、リョウさんが判断)。独立査読 (約 198 万入力) で、正しく打った日本語は変わらないが、1 打鍵多い打ち間違いの補正 (`koprekara`→これから、`oprenzi`→おれんじ など) が 89 語・229 形で効かなくなる (PR の記載は 9 形) ため。辞書への追加で日本語が悪化する語は入れない方針。`pre` の別の直し方 (直前が日本語のときは英語にしない等) は未着手の候補。ブランチ req/007-pre-english は残してある。
- 009 syi が sい になる (本家 #1464): CompositionTable に `しぃ` を足す案 (PR #14) は **取り込まず閉じた** (2026-11-11、リョウさんが判断)。独立査読で、し の打ち間違いの補正 (`desyita`→でした、`wakarimasyita`→わかりました、`sukosyi`→すこし など) が 4,296 語・16,625 形で効かなくなる (PR の記載は 3 例) ため。`しぃ` は今でも `sixi` で打てる。補正の側で直す案 (syi を shi と読めば語になるときは補正を優先する) は未着手の候補。ブランチ req/009-syi-kana は残してある。
- (無し)。001 は PR #5 として base ブランチ `ccr-27ad1b0c-qzazzd` にマージ済み (2026-10-24、人の判断)。main へは PR #4 に含めて出す。Mac 実機チェックリスト (PR #5 本文) は未確認。
- 実装した範囲: 英字 1 文字ずつ読む略語 (`loanwords.txt` の大文字語) + の + 英字の残る英単語。新しく英字になるのは `api`・`ide`・`uri`・`gui`・`who` の 5 語。

## 次の一手
- 人: Mac の Claude デスクトップアプリで、このリポジトリと本家の clone のフォルダーを共有する (本家追従 0-1 のため。2026-11-22 時点でクラウドから使える Mac のフォルダーは 0 件)。
- 人: 要件 017・018 の判断 (017: 案 A で進めてよいか・区切りの規則・Mac での測り方 / 018: 案 A・B・C の選択)。
- 人: PR #29 (015・016 と計画・要件定義書) の確認とマージの判断。
- AI: 要件 017・018 が approved になったら回す。
- AI: Mac のフォルダーが共有されたら 0-1 (本家追従) を回す。
- 人: `uri` (売り・瓜と読める) を英字にしてよいか判断する。
- 人: 001 の Mac 実機チェックリスト (TextEdit・ターミナル) を確かめて、マージ・要件の `done` を判断する。
- 別の要件にする候補: `my name is taro` (品質テストの最後の外れ)。
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

## 016 の記録 (ループの実測。目標値は置かない)
| 項目 | 値 |
|---|---|
| 要件 | 016 正解表に「前の文字」の列を足す |
| 所要時間 | 約 12 分 (開始 2026-11-22T05:23:39Z、終了 05:36:05Z)。人の介入なし (要件の承認はループの前) |
| 周回数 (implementer) | 2 (ほかに 3 周目をオーケストレーターが自分でテスト 4 行だけ直した) |
| 役割ごとのモデルと起動回数 | researcher 1 回 (sonnet、約 1.6 分) / implementer 2 回 (sonnet、約 3.2 分・約 0.4 分) / reviewer 3 回 (sonnet、約 2.3 分・約 0.3 分・約 0.2 分。定義は opus だが、利用者の指定で sonnet にし、オーケストレーター (opus) が要所を読み直した) / eval-guard 2 回 (sonnet、約 2.5 分・約 1.4 分) / doc-keeper 1 回。サブエージェント合計 9 回 |
| 手戻りの原因 | 止める・直すの指摘による手戻りはなし。任意の指摘 2 件を入れた。(1) 2 周目: `tools/eval_compare.py` の鍵の単体テスト `tools/test_eval_compare.py` と README の 1 文。(2) 3 周目: 2 周目のテスト `test_repeated_same_key_is_numbered` が、鍵から何回目かの番号を外す改造をしても通っていた (査読員が写しで改造して見つけた)。同じ鍵が 3 回と 2 回の表で removed 1・added 1 を足し、番号を外した写しで落ちることを確かめた (`docs/LESSONS.md` に記録)。1 周目の AGENT_STATE の更新漏れは記録係の段で直した |
