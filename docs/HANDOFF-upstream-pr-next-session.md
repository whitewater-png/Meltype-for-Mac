# 引き継ぎ: 本家 PR の取り込み (P30〜P36) と実機の不具合 (Q1〜Q5) の続き

> 2026-10-12 夕の時点 (かな入力・US 配列のマージのあと)。**基準コミット: 取り込み後の `main` の先頭 (`git log --oneline -3` で確認)。P36 続き・P39・P38・S3 は `main` に取り込み済み** (このファイルをコミットすると HEAD は 1 つ進む。必ず `git log --oneline -5` と `git status --short` で現状を確かめてから始める)。
> 別セッションが同じ作業ツリーを使っている。**このファイルを `git add -A` で巻き込まない・他の `docs/HANDOFF-*.md` (exception-recovery、strip-build-paths) には触らない。**
> 状態の正本は `docs/UPSTREAM_PR_TRACKER.md` (項目ごとの状態) と `docs/IMPROVEMENT_PLAN.md` (設計・数字・限界)。この文書はそれを読むための道案内。

## 0. 先にやること
1. `cd <リポジトリ> && git status --short && git log --oneline -5 && git log origin/main..HEAD --oneline | wc -l` (未 push の件数は上のコマンドで確認 (別セッションの分も含む)。**push していない**。作者の作業ツリーは、別セッションが `exp/zenzai-eval` などに切り替えていることがある。`git branch --show-current` を見て、`main` でなければそこへコミットしない。`main` へは一時の `git worktree add` で入れ、`git update-ref refs/heads/main <新> <旧>` で更新するとよい)。
2. 入っているアプリ: `~/Library/Input Methods/Meltype.app` は 1.1.5 (build 21)、**2026-10-12 17:20 ビルド** (`main` の `f5de00a` を、切り離した一時の作業ツリーでビルドして入れ替えたもの。P36・S2・かな入力・US 配列を含み、`exp/zenzai-eval` の変更は含まない)。`defaults read io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK` は「存在しない」になっていること (トレースログは切ってある)。
3. テスト: `bash --noprofile --norc -c 'export PATH=$HOME/.dotnet:$PATH; cd src/Meltype.Core.Tests && dotnet run -c Release'` → **559/559** (かな入力・US 配列のマージ後)。
4. かな入力 (JIS) と US 配列対応は、別セッションが `main` に取り込んだ (`afe2ccd`・`11c135f`)。マージ後のテストは 559 件通り、`tools/check-mac-aot-settings.py` は期待値が古くなって失敗していたので直した (`b33e571`。`InputStyle` は Mac の設定に出る)。**実機の確認 (2026-10-12、JIS キーボード、`main` の 17:20 ビルド、TextEdit): 設定の「入力方式」の表示と切り替え、かな入力 (`Shift+E` = ぃ など・英単語のときの英字表示)、ローマ字入力 (`kyouha,.` → `きょうは、。`、`` kono`config`wo `` → `` この`config`を ``。バッククォートは JIS の Shift+@)、`¥` キーは `¥`、Option + `¥` は `\` (半角) で、問題なし。US 配列そのもの (`docs/US_LAYOUT_CHECKLIST.md` の U1〜U13) は US キーボードが無いので未確認。**

## 1. 守ること (ガードレール)
- **`exp/zenzai-eval` は、精度向上の検証用ブランチ (別セッションの作業。リョウさんに確認済み)。作者の作業ツリーがそのブランチに切り替わり、未コミットの変更 (`Package.swift`・`Converter.swift` など) があることがある。そこへはコミットしない・その変更を巻き込まない。`main` にはコミットしない。**
- **push・タグ・版の更新・リリース・外部投稿は、リョウさんの明示の許可があるときだけ。** 実機への入れ替え (`mac/build.sh`) は許可済みだが、何を入れ替えるかを先に言う。入れ替え前に旧版を一時の作業用フォルダー (24 時間で消える) の `rollback/Meltype.app` へ退避。
- 取り消せない操作 (削除・上書き・公開・送信) は、先に懸念と代替案を出して判断を仰ぐ。
- **提出前に、実装者とは別の査読員 (別エージェント) が確認する。** 指摘がなくなってから提出。複数の独立した査読を並行して頼んでよい。
- 実機で確かめていないことは「未確認」と書く。実行していないことを実行したと書かない。
- トレースログ (`MeltypeTraceIMK`) は打った文字が残る。**取る前に毎回リョウさんの同意**を得る。終わったら `defaults delete ... MeltypeTraceIMK` → `pkill -x Meltype` → `~/Library/Application Support/Meltype/imk-trace.log` を削除。**`scratch/trace-backup` は別セッションのもの。触らない。**
- コミットは 1 項目 1 コミットで、変更したファイルを明示して `git add` (`-A` 禁止)。Windows・Linux は「対象外」と書く (未検証にしない)。
- ビルド・テストは `bash --noprofile --norc -c 'export PATH=$HOME/.dotnet:$PATH; ...'` で行う (`~/.zshrc` の対話プロンプトが混ざるため)。
- 作業は作者の作業ツリーの**複製** (`rsync -a --exclude bin --exclude obj --exclude mac/build --exclude mac/.build`、そのあと `git checkout -- . && git clean -fdq docs`) で行い、査読済みの差分だけを本体へ当てる。複雑な Python は `write_file` でファイルにして `python3 -c "exec(compile(open(f,encoding='utf-8').read(),'t','exec'))"` で実行 (インラインの heredoc は止められることがある)。

## 2. ここまでの状態 (詳細は管理表)
| 項目 | 内容 | 状態 |
|---|---|---|
| P30〜P34 | ユーザー辞書の割り込み、判定の小修正、Ctrl+H、× ÷、アーティスト名 | 実機確認済み |
| Q1 | `baz` の `z` が消える | 実機確認済み |
| Q2・Q3 | AI モデル名、`chance` など。**`change` は直さない** (ちゃん付けの名前 `yuuchangenki` を優先) | 実機確認済み |
| Q4 | 確定し直しで古い英語が残る (`setMarkedText("")` で取り除く) | TextEdit で確認済み。LibreOffice は限界 (入力欄の中身を返さない)。Hermes・Chrome・Safari などは未確認 |
| Q5 | 2 行目以降の英文 (`Let's go`・`We're here`)。素通しした Enter・Tab・**Shift+Enter** を確定の記録に入れる | TextEdit・Hermes で確認済み |
| P35 | 英文の途中の `go`・`so`・`do`・`to` を確定し直さない | 完了 (実機確認済み。管理表も更新した) |
| **P36** | **選んだローマ字の文を Shift+Space で再変換 (本家 #332 の改良導入)** | **完了 (TextEdit・Hermes・LibreOffice で実機確認済み 2026-10-12。他のアプリは未確認)** |
| P37 | Swift の確定し直しを「`original` が無ければ消さない」に (本家 #86 の考え方) | 完了 (調査の結果、Core のテストだけ足した。Swift は変えない。`original` が nil になるのは Windows 用の英数状態の判定だけで、Mac では通らない。P11 のときに合わせて行う。計画書 P37) |
| P38 | 配布前の自動チェックのスクリプト `tools/check-release.py` (Mac 専用・読み取り専用。検査項目は `docs/PKG_REVIEW_2026-10.md` §7) | **コミット済み (`main` に取り込み済み)**。査読 R25 の P1 6 件と R29 の P1 を修正。次のリリース前に `python3 tools/check-release.py mac/dist/Meltype-mac-<版>.zip mac/dist/Meltype-mac-<版>.pkg --expect-version <版>` を実行する |
| P39 | 候補ごとの注釈 (英訳・全角・半角) | **コミット済み (`main` に取り込み済み)。Core の JSON の `view.notes` だけ。Swift は保留** (画面には出ない。再開手順は計画書 P39) |
| P36 続き | 選んだローマ字文の前後に改行・タブがあっても候補を出す | **コミット済み (`main` に取り込み済み)。Core のみ。実機未確認** (実機に入っているのは、これらを含まない `main` の 17:20 ビルド) (候補に改行を含めて `setMarkedText` に渡すことの実績が無い。確認手順と退路は計画書 P36 の「査読 R27」) |
| S3 | セキュリティレビューの M5 の見直し (文書だけ) | **コミット済み (`main` に取り込み済み)** |

## 3. P36 の中身と、実機で確かめること
**中身** (計画書の P36 の節に、数字・案の比較・限界がある):
- 選んだ文字がローマ字 (半角の英数字・記号・空白だけで英字を含む) なら、読みに戻さず Meltype の判定で語ごとに英語/日本語に分け、**日本語だと言える所があるときだけ**日本語にした文を候補に出す (Enter で置き換え、Esc 1 回で選んだままに戻る)。
- 日本語だと言える所 = 大文字で始まらない語で、かなが 2 文字以上・3 文字以上・既知の英単語でない、日本語の英字が全体の 1/4 以上 (`IsRomajiEvidence` / `RomajiMinShare`)。大文字始まりの語と単独の敬称 (san・kun・chan・sama) は証拠にしない。
- 文頭と `. ! ?` の次の語が先頭 1 文字だけ大文字なら小文字に直して判定する (アプリの自動大文字対策)。この語自体は証拠に数えない。
- 空白で区切られた複数語の英文 (かな・漢字なし。末尾の改行・全角記号入りを含む) では、従来の OS の読み (でたらめなかな) を使わない。
- Swift (`InputController.handle`、Shift+Space の入口) に 2 つの守りを足した: 読めた長さと選択の長さが違うホストでは何もしない。選んだ文字を読めたのに再変換できなかったときは、キーを飲み込む (アプリに渡すと選択が空白で置き換わって消えるため)。
- 査読: R18b・R19・R20・R21・R22。いずれも P0 なし。

**実機の確認 (2026-10-12、リョウさんの報告): 下の項目 1〜8、位置を変えた場合 (行の途中・文書の先頭・Shift+Enter のあと)、候補を出したままのクリックと Cmd+Tab (TextEdit)、Hermes のチャット欄での候補・置き換え・`Thanks, Kenji!`、LibreOffice (何も起きない) が、すべて期待どおり。残りは Chrome・Safari・Google ドキュメントなど他のアプリだけ (任意)。以下は確認に使った手順 (再確認用)。日本語入力 (「あ」) で、最初に捨てキーを 1 回打ってから。**
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
1. **push・版の更新・リリース** (未 push あり。件数は上の確認コマンド)。
2. 空白の無い 1 語の英単語で OS の読みを切るか (でたらめ約 724 件が消える一方、`Suzuki`・`Sakura` など正しい読み約 40 件も消える)。今は「残す」。
3. `docs/HANDOFF-exception-recovery.md` (別セッションのもの。実装済みなのに「未着手」と書いている古い文書) の扱い。
4. `change` を英字にする回避策 (大文字 `Change`・末尾の空白・ユーザー辞書) を試すか。

## 6. 次の作業の候補 (おすすめ順)
1. **P36 続きの実機確認** (実機を、取り込み後の `main` から入れ替えてから) (計画書 P36 の「査読 R27」の手順。TextEdit で段落を 3 回クリックして選ぶ形が中心)。P36 本体は実機確認まで完了 (§3)。
2. (P37 は完了。Swift の fail-closed は、英数状態の自動判定を Mac に入れる P11 のときに合わせて行う。計画書 P37 を参照。`docs/SECURITY_REVIEW_2026-10.md` の 225 行目は、Mac でも `DirectModeAutoDetect` が効くと書いているが、コード上は効かないので、前提の見直しの余地がある。)
3. 小さな改善: 複数語でも英語の語が 1 つも無いときだけ Meltype 自前のローマ字→かなを読みに使う案 (OS の読みを切ったことで候補が出なくなった `ii ne`・`Hiroshi san` 型を直す)。箇条書きの `- ` が `ー` になる既存の問題 (`- kyou wa ...` → `ーきょうわ…`、`Tel: 03-1234-5678` → `03ー1234ー5678`。計画書 P36 の「査読 R27」の P1 別件)。
4. P39 の Swift 案の再開 (計画書 P39 の再開手順。先に実機で今の「あ」の注釈が見えるかを確かめる)。P38 の P2 (PackageInfo の細かい項目、`HEAD` が読めないときの扱いなど。計画書 P38 の「査読 R29」)。

## 7. 取り込み済みのブランチ `batch/p36c-p38-p39-sec` (2026-10-12 夜)
- `bd8e954` (P36 続き)・`36979e6` (P39)・`f57b031` (P38)・`b0726ff` (S3) を、別セッションが `main` に入れた 4 コミット (丸括弧の設定・pkg の文字化け修正・dmg 配布 (D1)・リリース手順) とマージして、`main` に取り込んだ (マージ後のテスト **575/575**・`build.sh --no-install`・codesign・AOT 検査・`tools/check-release.py` が通る)。計画書の衝突は、両方の節を残して解決した。
- 取り込みの途中で、別セッションが `main` の作業ツリーで未コミットの変更を持っていたため、一時はブランチに積んで待った。**作者の作業ツリーは、別セッションが使っているので、`main` への変更は一時の `git worktree add` かブランチ経由で行い、作業ツリーが綺麗なときだけ `git merge --ff-only` で進める**。
- ブランチ `batch/p36c-p38-p39-sec` は残っている (`main` に含まれているので、不要なら `git branch -d` で消せる。消すのはリョウさんに確認してから)。
