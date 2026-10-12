# 引き継ぎ: 本家 PR の取り込み (P30〜P36) と実機の不具合 (Q1〜Q5) の続き

> 2026-10-12 夕の時点 (かな入力・US 配列のマージのあと)。**基準コミット: `a5e1ab6`** (このファイルをコミットすると HEAD は 1 つ進む。必ず `git log --oneline -5` と `git status --short` で現状を確かめてから始める)。
> 別セッションが同じ作業ツリーを使っている。**このファイルを `git add -A` で巻き込まない・他の `docs/HANDOFF-*.md` (exception-recovery、strip-build-paths) には触らない。**
> 状態の正本は `docs/UPSTREAM_PR_TRACKER.md` (項目ごとの状態) と `docs/IMPROVEMENT_PLAN.md` (設計・数字・限界)。この文書はそれを読むための道案内。

## 0. 先にやること
1. `cd /Users/ryo/Meltype && git status --short && git log --oneline -5 && git log origin/main..HEAD --oneline | wc -l` (未 push は 44 件のはず。**push していない**。`/Users/ryo/Meltype` の作業ツリーは、別セッションが `exp/zenzai-eval` などに切り替えていることがある。`git branch --show-current` を見て、`main` でなければそこへコミットしない。`main` へは一時の `git worktree add` で入れ、`git update-ref refs/heads/main <新> <旧>` で更新するとよい)。
2. 入っているアプリ: `~/Library/Input Methods/Meltype.app` は 1.1.5 (build 21)、**2026-10-12 17:20 ビルド** (`main` の `f5de00a` を、切り離した一時の作業ツリーでビルドして入れ替えたもの。P36・S2・かな入力・US 配列を含み、`exp/zenzai-eval` の変更は含まない)。`defaults read io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK` は「存在しない」になっていること (トレースログは切ってある)。
3. テスト: `bash --noprofile --norc -c 'export PATH=$HOME/.dotnet:$PATH; cd src/Meltype.Core.Tests && dotnet run -c Release'` → **559/559** (かな入力・US 配列のマージ後)。
4. かな入力 (JIS) と US 配列対応は、別セッションが `main` に取り込んだ (`afe2ccd`・`11c135f`)。マージ後のテストは 559 件通り、`tools/check-mac-aot-settings.py` は期待値が古くなって失敗していたので直した (`b33e571`。`InputStyle` は Mac の設定に出る)。**実機の確認 (2026-10-12、JIS キーボード、`main` の 17:20 ビルド、TextEdit): 設定の「入力方式」の表示と切り替え、かな入力 (`Shift+E` = ぃ など・英単語のときの英字表示)、ローマ字入力 (`kyouha,.` → `きょうは、。`、`` kono`config`wo `` → `` この`config`を ``。バッククォートは JIS の Shift+@)、`¥` キーは `¥`、Option + `¥` は `\` (半角) で、問題なし。US 配列そのもの (`docs/US_LAYOUT_CHECKLIST.md` の U1〜U13) は US キーボードが無いので未確認。**

## 1. 守ること (ガードレール)
- **`exp/zenzai-eval` は、精度向上の検証用ブランチ (別セッションの作業。リョウさんに確認済み)。`/Users/ryo/Meltype` の作業ツリーがそのブランチに切り替わり、未コミットの変更 (`Package.swift`・`Converter.swift` など) があることがある。そこへはコミットしない・その変更を巻き込まない。`main` にはコミットしない。**
- **push・タグ・版の更新・リリース・外部投稿は、リョウさんの明示の許可があるときだけ。** 実機への入れ替え (`mac/build.sh`) は許可済みだが、何を入れ替えるかを先に言う。入れ替え前に旧版を `~/.hermes/profiles/henrietta/cache/scratch/rollback/Meltype.app` へ退避 (24 時間で消える)。
- 取り消せない操作 (削除・上書き・公開・送信) は、先に懸念と代替案を出して判断を仰ぐ。
- **提出前に、実装者とは別の査読員 (別エージェント) が確認する。** 指摘がなくなってから提出。複数の独立した査読を並行して頼んでよい。
- 実機で確かめていないことは「未確認」と書く。実行していないことを実行したと書かない。
- トレースログ (`MeltypeTraceIMK`) は打った文字が残る。**取る前に毎回リョウさんの同意**を得る。終わったら `defaults delete ... MeltypeTraceIMK` → `pkill -x Meltype` → `~/Library/Application Support/Meltype/imk-trace.log` を削除。**`scratch/trace-backup` は別セッションのもの。触らない。**
- コミットは 1 項目 1 コミットで、変更したファイルを明示して `git add` (`-A` 禁止)。Windows・Linux は「対象外」と書く (未検証にしない)。
- ビルド・テストは `bash --noprofile --norc -c 'export PATH=$HOME/.dotnet:$PATH; ...'` で行う (`~/.zshrc` の対話プロンプトが混ざるため)。
- 作業は `/Users/ryo/Meltype` の**複製** (`rsync -a --exclude bin --exclude obj --exclude mac/build --exclude mac/.build`、そのあと `git checkout -- . && git clean -fdq docs`) で行い、査読済みの差分だけを本体へ当てる。複雑な Python は `write_file` でファイルにして `python3 -c "exec(compile(open(f,encoding='utf-8').read(),'t','exec'))"` で実行 (インラインの heredoc は止められることがある)。

## 2. ここまでの状態 (詳細は管理表)
| 項目 | 内容 | 状態 |
|---|---|---|
| P30〜P34 | ユーザー辞書の割り込み、判定の小修正、Ctrl+H、× ÷、アーティスト名 | 実機確認済み |
| Q1 | `baz` の `z` が消える | 実機確認済み |
| Q2・Q3 | AI モデル名、`chance` など。**`change` は直さない** (ちゃん付けの名前 `yuuchangenki` を優先) | 実機確認済み |
| Q4 | 確定し直しで古い英語が残る (`setMarkedText("")` で取り除く) | TextEdit で確認済み。LibreOffice は限界 (入力欄の中身を返さない)。Hermes・Chrome・Safari などは未確認 |
| Q5 | 2 行目以降の英文 (`Let's go`・`We're here`)。素通しした Enter・Tab・**Shift+Enter** を確定の記録に入れる | TextEdit・Hermes で確認済み |
| P35 | 英文の途中の `go`・`so`・`do`・`to` を確定し直さない | 完了 (実機確認済み。管理表も更新した) |
| **P36** | **選んだローマ字の文を Shift+Space で再変換 (本家 #332 の改良導入)** | **コミット済み。TextEdit で実機確認済み (2026-10-12)。Hermes・LibreOffice などは未 (下の §3)** |
| P37 | Swift の確定し直しを「`original` が無ければ消さない」に (本家 #86 の考え方) | 未着手 |
| P38 | 配布前の自動チェック (検査項目の案は `docs/PKG_REVIEW_2026-10.md` §7) | 未着手 |
| P39 | 候補ごとの注釈 (英訳・全角・半角) | 未着手 |

## 3. P36 の中身と、実機で確かめること
**中身** (計画書の P36 の節に、数字・案の比較・限界がある):
- 選んだ文字がローマ字 (半角の英数字・記号・空白だけで英字を含む) なら、読みに戻さず Meltype の判定で語ごとに英語/日本語に分け、**日本語だと言える所があるときだけ**日本語にした文を候補に出す (Enter で置き換え、Esc 1 回で選んだままに戻る)。
- 日本語だと言える所 = 大文字で始まらない語で、かなが 2 文字以上・3 文字以上・既知の英単語でない、日本語の英字が全体の 1/4 以上 (`IsRomajiEvidence` / `RomajiMinShare`)。大文字始まりの語と単独の敬称 (san・kun・chan・sama) は証拠にしない。
- 文頭と `. ! ?` の次の語が先頭 1 文字だけ大文字なら小文字に直して判定する (アプリの自動大文字対策)。この語自体は証拠に数えない。
- 空白で区切られた複数語の英文 (かな・漢字なし。末尾の改行・全角記号入りを含む) では、従来の OS の読み (でたらめなかな) を使わない。
- Swift (`InputController.handle`、Shift+Space の入口) に 2 つの守りを足した: 読めた長さと選択の長さが違うホストでは何もしない。選んだ文字を読めたのに再変換できなかったときは、キーを飲み込む (アプリに渡すと選択が空白で置き換わって消えるため)。
- 査読: R18b・R19・R20・R21・R22。いずれも P0 なし。

**実機の確認: TextEdit は 2026-10-12 に下の項目 1〜8 が期待どおり (リョウさんの報告)。残りは Hermes → LibreOffice と、行の途中・文書の先頭・候補表示中のクリック / Cmd+Tab。日本語入力 (「あ」) で、最初に捨てキーを 1 回打ってから。**
| # | 操作 | 期待 |
|---|---|---|
| 1 | `asita no meeting de review shimasu` を確定 (文頭は自動で大文字になる) → 選んで Shift+Space | `あしたの meeting で review します` が候補に出る。Enter で 1 回だけ置き換わる |
| 2 | 1 の候補で Esc 1 回 | 選んだ文に戻る |
| 3 | `Kyou wa genki desu. Asita mo ganbarimasu.` を確定 → 選んで Shift+Space | 2 文とも日本語の候補 |
| 4 | `Thanks, Kenji!` を確定 → **ドラッグで選ぶ**・**行の左余白で段落を 3 回クリックして選ぶ** (末尾に改行が付く)、それぞれ Shift+Space | どちらも候補が出ず、文字も消えない |
| 5 | `Hello, my name is Ryo.` を同様に | 同上 |
| 6 | かな・漢字 (`今日は`) を選んで Shift+Space | 従来どおり読みに戻して変換 |
| 7 | 行の途中・文書の先頭・Shift+Enter のあとの行でも 1 を試す | 同じ結果 (置き換え位置がずれない) |
| 8 | 候補を出したまま別の行をクリック / Cmd+Tab | 結果が元の場所に 1 回だけ残る (二重にならない) |
| 9 | Hermes のチャット欄 (改行は Shift+Enter) で 1・4 | 同じ。Enter で送信されない |
| 10 | LibreOffice で 1 | 何も起きない・文書が変わらない (選択を返さないため) |
- 見るトレースの行名 (取る場合は同意を得てから): `handle keyCode=`、`showComposition: setMarkedText`、`apply: insertText`、`commitComposition が呼ばれた`、`再変換できない選択: Shift+Space を飲み込む`。
- **未解明**: 2026-10-12 の最初の TextEdit では `Thanks, Kenji!` が変換され、あとでは変換されなかった。プロセスは新しい版で起動していて、Core の状態でも再現せず、選択に改行・全角記号が混ざると英文でも OS の読みに落ちることは再現した (これは直した)。最初の選び方が段落クリックだったかは**未確認**。再現したら、選び方を聞く。2026-10-12 の確認 (項目 5・6) では、ドラッグで選んだ場合も、行の左余白で 3 回クリックして選んだ場合も、`Thanks, Kenji!` は候補が出なかった。

## 4. 既知の限界 (直していない。数字は計画書)
- 小文字で打った名前の英文 (`thanks, yui!`) は日本語の候補が出る (NS 150 件中 113)。技術語 (`proto`・`inode`) や英語の docstring も出る場合がある。Enter を押さなければ元のまま。
- 空白の無い 1 語の英単語 (`hello` → `へっろ`) は OS のでたらめな読みが残る。
- 複数語の日本語ローマ字で Meltype が証拠不足とする文 (`ii ne`・`Hiroshi san`・全部大文字) は、OS の読みの候補も出なくなった (OS の読みが正しかったのは約 84%)。
- 末尾に改行が付いたローマ字の日本語文は候補が出ない (`IsRomajiText` が ASCII のみ)。
- 全角英数だけの英文・互換漢字・拡張 B 以降の漢字を含む選択は OS の読みに落ちる。
- 辞書に無い製品名が文頭にある日本語文で、その語がかなになる (`Node de deploy shimasu`)。
- 他: Hermes の `We'うぇへれすし` (原因未調査。Q5 の Shift+Enter で説明できる可能性)、TextEdit の `Let  It Go` (空白が二重)、`HI there` (再現せず。Shift を離すのが遅れた打ち方の影響と見ている)、`gemma4-27b`→`gemma4ー27b`、`ollama run` の `run` が `るん`、`gpt-oss`→`gpt-おっs`。

## 5. リョウさんの判断を待っていること
1. **push・版の更新・リリース** (未 push 44 件)。
2. 空白の無い 1 語の英単語で OS の読みを切るか (でたらめ約 724 件が消える一方、`Suzuki`・`Sakura` など正しい読み約 40 件も消える)。今は「残す」。
3. `docs/HANDOFF-exception-recovery.md` (別セッションのもの。実装済みなのに「未着手」と書いている古い文書) の扱い。
4. `change` を英字にする回避策 (大文字 `Change`・末尾の空白・ユーザー辞書) を試すか。

## 6. 次の作業の候補 (おすすめ順)
1. §3 の P36 の残りの実機確認 (Hermes・LibreOffice・行の途中・候補表示中のクリック)。TextEdit の分は完了で、管理表・計画書に記録済み。
2. **P37** (Swift の確定し直しを fail-closed に)。P36 で触った入口の近くで、LibreOffice の件 (照合できないとき) と関係する。Core に「`deleteBefore>0` の edit は必ず `Original` を持つ」テストを足す。
3. 小さな改善: `IsRomajiText` の前に前後の改行・空白を除く (末尾の改行付きローマ字文を日本語にできる)。複数語でも英語の語が 1 つも無いときだけ Meltype 自前のローマ字→かなを読みに使う案。
4. P38 (配布前チェックのスクリプト)、P39。

## 7. 作業の道具と進め方 (scratch は 24 時間で消える。残す価値のあるものはスキルと計画書に入れてある)
- スキル `meltype-mac-maintenance` (Henrietta プロファイル。`references/core-fix-recipes.md`、`upstream-issue-triage.md`、`device-vs-harness-lessons.md`、`shared-worktree-commits.md`) に、前後比較・赤→緑・変異テスト・実機の確認手順・共有作業ツリーでのコミットの要点がある。使えない環境なら、上のガードレールと次を守る。
- 調査用: `cd src/Meltype.Core.Tests && dotnet bin/Release/net10.0/Meltype.Core.Tests.dll --repro "<keys>" enter|space|none` (`--segments`、`--eval`、`--eval-json` (1242/1244)も)。実機の判定は NSSpellChecker、`--repro` は同梱の英単語表 (BuiltIn) なので、**両方で比べる**。
- 進め方: 実装者 (複製) → 比較 (BuiltIn・NS) → 変異テストで赤を確認 → 独立した査読 → 本体へ当てる → 全テスト → コミット → 入れ替え → 実機確認 → 管理表と計画書に記録。実装者の比較入力に無い型 (句読点・文頭の大文字・改行・全角記号) を査読で必ず足す。
- 実機の結果は、リョウさんが「打った>結果」の形で貼る。キーボードは JIS (`'` は Shift+7)。TextEdit・Hermes・LibreOffice で確認する。
- 却下済み (新しい根拠が無ければ再提案しない): Space / Shift+Space の全角スペース化、Shift 入力の CamelCase 名と助詞の分割、`change` の辞書での英字化。
- 署名基盤・CI・Developer ID・新しい設定は、運用コストのため行わない方針。
