# 実機の確認表 (2026-11)

要件 001・003・004・016〜023・025〜027・030 (実機未確認) と、002・005・006・008・010〜014 (主項目のみ確認) の、未確認の実機チェックを 1 枚にまとめたものです。基準は main bc1b603。

## 使い方
- 結果は **「打った > 結果」** の形で、番号つきで貼る (例: `12 pazolite > pazolite`)。期待と違うときだけ、見えたとおりを書けば足ります。
- **必須** だけで約 25〜30 分 (1 行 15〜20 秒に、設定の切り替え・ダウンロード待ちを足した見込み)。打つのが長い 64・65 と、Zenzai を入れる C を除くと約 12〜15 分。**任意** は時間があるときに。
- アプリは TextEdit・ターミナル・LibreOffice・Hermes だけです。文書にあった Safari のフォーム・Slack・Discord・ChatGPT・Obsidian・Word・Pages は使わないので、Hermes か TextEdit で代用するか載せていません (末尾の「載せなかった項目」)。
- JIS 配列のキー: `-` は 0 の右、`^` はその右、`~` は Shift+^、`#` は Shift+3。
- 1 行ごとに、Enter の前の見え方と Enter 後の結果の両方を見て、⌘A → Delete (ターミナルは Ctrl+U) で消してから次へ。
- **期待は、ライブ変換 ON (既定) の結果で書いてある。** Enter だけで確定すると、ライブ変換の表示 (漢字) のまま確定するのが今の仕様。かなで書いた期待 (OFF の結果) は「ライブ変換 OFF なら」と添えた。漢字の行で見るのは、英字が残らないこと (英字にすべき所は英字のまま残ること) です。
- 各要件文書の未確認 `[ ]` を拾い、今の main の Core (`--repro`。ターミナルの行は表ごとにコード系アプリとして打つ `--expect`) で期待がまだ正しいかを確かめて並べました。ずれたものは「5. 要確認」に分けています。期待の多くは Core の結果と同じです (Core はライブ変換が無く、かなの結果は ライブ変換 OFF と同じ)。Space の候補・学習・表示は Core で測れないので実機でだけ確かめます。

## 1. TextEdit (設定は今のまま)

ライブ変換 ON・Zenzai OFF・そのほか既定。1 行ごとに打って見える形を確かめ、Enter で確定して結果を見る。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| 1 | 必須 | TextEdit | `apinoerror` Enter | `apiのerror` | 001 |
| 2 | 任意 | TextEdit | `youtubenodouga` Space | 候補に `youtubeの動画`。Enter で確定する形は `youtubeのどうが` (要確認 R2) | 001 |
| 3 | 必須 | TextEdit | `pt-query-digest` Enter | `pt-query-digest` | 004 |
| 4 | 必須 | TextEdit | `ra-menwotaberu` Enter | `ラーメンを食べる` (ライブ変換 OFF なら `らーめんをたべる`)。見るのは英字が残らないこと | 004 |
| 5 | 必須 | TextEdit | `karen` Enter | `karen` (英字のまま) | 010 |
| 6 | 必須 | TextEdit | `obsidian` Enter | `obsidian` | 016 |
| 7 | 必須 | TextEdit | `obsidianwo` Enter | `obsidianを` (変換ボックスの表示と確定が同じ) | 016 |
| 8 | 必須 | TextEdit | (a) `dainyuu` Enter  (b) 別に `dainyuu` Space | (a) `代入` (ライブ変換 OFF なら `だいにゅう`)  (b) 候補に `代入` | 018 |
| 9 | 任意 | TextEdit | `hensuunidainyuu` Enter | `変数に代入` (ライブ変換 OFF なら `へんすうにだいにゅう`)。見るのは英字が残らないこと | 018 |
| 10 | 必須 | TextEdit | `pazolite` Enter | `pazolite` | 021 |
| 11 | 必須 | TextEdit | `oeksound` Enter | `oeksound` | 021 |
| 12 | 必須 | TextEdit | (a) `pazolitenokyoku` Enter  (b) 別に `pazolitenokyoku` Space | (a) `pazoliteの曲` (ライブ変換 OFF なら `pazoliteのきょく`。見るのは `pazolite` が英字のまま)  (b) 候補に `pazoliteの曲` | 021 |
| 13 | 必須 | TextEdit | (a) `baguhoukoku` Enter  (b) 別に `baguhoukoku` Space | (a) `バグ報告` (ライブ変換 OFF なら `ばぐほうこく`。見るのは英字が残らないこと)  (b) 候補に `バグ報告` | 019 |
| 14 | 任意 | TextEdit | `tunagarumademattekara` Enter | `繋がるまで待ってから` (ライブ変換 OFF なら `つながるまでまってから`)。見るのは英字が残らないこと | 019 |
| 15 | 任意 | TextEdit | `CLAUDE.mdwohiraku` Enter | `CLAUDE.mdを開く` (ライブ変換 OFF なら `CLAUDE.mdをひらく`)。見るのは `CLAUDE.md` が英字のまま | 019 |
| 16 | 任意 | TextEdit | `,nisuruwo` Enter | `、にするを` | 019 |
| 17 | 任意 | TextEdit | `reibunn:pregnant_man:` Enter | `例文:pregnant_man:` (ライブ変換 OFF なら `れいぶん:pregnant_man:`。見るのは `:pregnant_man:` が英字のまま。絵文字の補完の窓は Slack・Discord だけの話。ここでは文字だけ確かめる) | 019 |
| 18 | 必須 | TextEdit | (a) `itotekinagobyuu` Enter  (b) 別に `itotekinagobyuu` Space | (a) `意図的な誤謬` (ライブ変換 OFF なら `いとてきなごびゅう`。見るのは英字が残らないこと)  (b) 候補に `意図的な誤謬` | 020 |
| 19 | 任意 | TextEdit | `ronpa` Enter | `ろんぱ` | 020 |
| 20 | 任意 | TextEdit | `uchidome` Enter | `打ち止め` (ライブ変換 OFF なら `うちどめ`)。見るのは英字が残らないこと | 020 |
| 21 | 任意 | TextEdit | `Fluorite` Enter | `Fluorite` | 020 |
| 22 | 任意 | TextEdit | (a) `fanntatowirukinnsonn` Enter  (b) 別に `fanntatowirukinnsonn` Space | (a) `ファンタとウィルキンソン` (ライブ変換 OFF なら `ふぁんたとうぃるきんそん`。見るのは英字が残らないこと)  (b) 候補に `ファンタとウィルキンソン` | 020 |
| 23 | 必須 | TextEdit | (a) `sitaidesu` Enter  (b) 別に `sitaidesu` Space | (a) `したいです`  (b) 1 番目の候補が `したいです` か `死体です` かを記録 | 022 |
| 24 | 任意 | TextEdit | `amatte` Enter | `余って` (ライブ変換 OFF なら `あまって`)。見るのは英字が残らないこと | 022 |
| 25 | 任意 | TextEdit | `chomatte` Enter | `ちょ待って` (ライブ変換 OFF なら `ちょまって`)。見るのは英字が残らないこと | 022 |
| 26 | 任意 | TextEdit | (a) `kakikomiera-` Enter  (b) 別に `kakikomiera-` Space | (a) `書き込みエラー` (ライブ変換 OFF なら `かきこみえらー`。見るのは英字が残らないこと)  (b) 候補に `書き込みエラー` | 022 |
| 27 | 任意 | TextEdit | `2nitime` Space | 空白が入らず変換中のまま (候補に `2日目`) | 022 |
| 28 | 必須 | TextEdit | (a) `atude` Enter  (b) 別に `atude` Space | (a) `あつで`  (b) 候補に `厚手` が出るかを記録 | 023 |
| 29 | 任意 | TextEdit | `tasinamu` Enter | `嗜む` (ライブ変換 OFF なら `たしなむ`)。見るのは英字が残らないこと | 023 |
| 30 | 任意 | TextEdit | `haatu` Enter | `はあつ` | 023 |
| 31 | 任意 | TextEdit | `getu` Enter | `げつ` | 023 |
| 32 | 任意 | TextEdit | `subewomotanai` Enter | `術を持たない` (ライブ変換 OFF なら `すべをもたない`)。見るのは英字が残らないこと | 023 |
| 33 | 必須 | TextEdit | (a) `setteisiteirundakedo` Enter  (b) 別に `setteisiteirundakedo` Space | (a) `設定しているんだけど` (ライブ変換 OFF なら `せっていしているんだけど`。見るのは英字が残らないこと)  (b) 候補に `設定しているんだけど` | 025 |
| 34 | 必須 | TextEdit | (a) `mainkannsuu` Enter  (b) 別に `mainkannsuu` Space | (a) `main関数` (ライブ変換 OFF なら `mainかんすう`。見るのは `main` が英字のまま)  (b) 候補に `main関数` | 025 |
| 35 | 任意 | TextEdit | `@exampleabc-jh.ed.jpdesu` Enter | `@exampleabc-jh.ed.jpdesu` (全部そのまま) | 025 |
| 36 | 任意 | TextEdit | 先に `kyouhaiitenkidesune,kinoumoiitenkidesitane.` Enter で 21 文字以上を確定し、その直後に `@exampleabc-jh.ed.jpdesu` Enter | 直前の確定があっても、上の行と同じく途中から日本語にならない | 025 |
| 37 | 任意 | TextEdit | `kore` Enter | `これ` (変わらない) | 011 |
| 38 | 任意 | TextEdit | `ima` Enter | `いま` (変わらない) | 012 |
| 39 | 任意 | TextEdit | `ichi` Enter | `いち` (変わらない) | 008 |
| 40 | 任意 | TextEdit | `beeru` Enter | `べえる` (変わらない) | 005 |
| 41 | 任意 | TextEdit | `obake` Enter / `obon` Enter | `おばけ` / `おぼん` (変わらない) | 016 |
| 42 | 任意 | TextEdit | `oekaki` Enter / `pazuru` Enter | `お絵描き` / `ぱずる` (ライブ変換 OFF なら `おえかき` / `ぱずる`。`ぱずる` は変わらない) | 021 |
| 43 | 任意 | TextEdit | `[[nikki]]` Space | 候補に `[[日記]]` (`[` が半角のまま) | 013 |
| 44 | 任意 | TextEdit | `[[nikki` まで打つ (Enter 前) → `]]` を足す | 途中は `「「にっき`、`]]` を足すと `[[にっき]]` に見える。この見え方が気にならないか感想を書く | 013 |
| 45 | 必須 | TextEdit | `nihongo` Space | 変換になる (`日本語` が候補)。ターミナルの同じ行と比べる | 030 |

## 2. ターミナル (zsh)

Enter は IME の確定だけにする。確定のあとに 2 回目の Enter は押さない (コマンドが実行される)。確かめたら Ctrl+C か Ctrl+U で行を消す。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| 46 | 必須 | ターミナル | `apinoerror` Enter | `apiのerror` | 001 |
| 47 | 必須 | ターミナル | `error` Enter / `api` Enter | `error` (英字のまま) / `api` (実機のターミナルでは英字のまま。Core の `--expect` では `あぴ`。差の原因は未確認: 学習データの英語の記憶か、コード系アプリの判定か) | 001 |
| 48 | 必須 | ターミナル | `ls-files` Enter / `cd-rom` Enter | `ls-files` / `cd-rom` (英字のまま) | 004 |
| 49 | 必須 | ターミナル | `beep sound` (間は Space) Enter | `beep sound` | 005 |
| 50 | 必須 | ターミナル | `iTunes` Enter | `iTunes` | 008 |
| 51 | 必須 | ターミナル | `#endif` Enter | `#endif` (JIS は Shift+3 で `#`) | 012 |
| 52 | 任意 | ターミナル | `karen` Enter | `karen` | 010 |
| 53 | 必須 | ターミナル | `python3 -m venv .venv` (Space 区切り) Enter | `python3 -m venv .venv` (`python3` の後の空白が消えない) | 030 |
| 54 | 必須 | ターミナル | `pip3 install requests` (Space 区切り) Enter | `pip3 install requests` (空白が消えない) | 030 |
| 55 | 任意 | ターミナル | `cd ~/Downloads` (`~` は JIS で Shift+^) Enter | `cd ~/Downloads` (今までどおり) | 030 |
| 56 | 必須 | ターミナル | `nihongo` Space | 変換になる (`日本語` が候補)。TextEdit と同じ | 030 |

## 3. LibreOffice Writer・Hermes

LibreOffice は本家 #2015 の報告のアプリ。Hermes の入力欄は、要件文書にある Safari のフォーム・Slack・ChatGPT の代わり (Safari 固有の挙動は確かめたことにならない)。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| 57 | 必須 | LibreOffice | `itotekinagobyuu` Enter | `いとてきなごびゅう` | 020 |
| 58 | 任意 | Hermes | `kinyuu` Enter | `きにゅう` | 018 |
| 59 | 任意 | Hermes | `obsidiande` Enter | `obsidianで` | 016 |
| 60 | 任意 | Hermes | `oeksoundwo` Enter | `oeksoundを` | 021 |
| 61 | 任意 | Hermes | `pixivde` Enter | `pixivで` | 014 |
| 62 | 任意 | Hermes | `yappane` Enter | `やっぱね` | 022 |
| 63 | 任意 | Hermes | `Yukishiro` Enter | `Yukishiro` | 023 |

## 4. 設定の切り替えが要る項目 (後ろにまとめた)

切り替えは見出し A〜F ごとに 1 回だけ。設定は入力メニュー → 「設定・辞書…」→ 右端の「設定」タブ (⌘5)。変えるとすぐ保存される。終わったら元に戻す (ライブ変換は ON、計算式と Zenzai は OFF、「変換後も続けて入力できる」は元の状態)。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| | | | **A. ライブ変換を OFF にして打つ** | | |
| 64 | 必須 | TextEdit | ライブ変換 OFF で `kyouhaiitenkidesune` を 22 回続けて打つ (418 文字。確定しない)。Enter | 途中も全部かなのまま (英字が混ざらない)。Enter で確定 | 003 |
| 65 | 必須 | TextEdit | ライブ変換を ON に戻して、同じ 418 文字を打つ。Enter | 全部かなのまま。Enter で確定 | 003 |
| | | | **B. 設定「計算式を変換すると結果を候補に出す」を ON** | | |
| 66 | 必須 | TextEdit | `5^25` Space | 計算の候補が出ない。固まらない (JIS は ^ キー) | 006 |
| 67 | 必須 | TextEdit | `5^20` Space | 候補に `95367431640625` が出る | 006 |
| 68 | 必須 | TextEdit | `2^10` Space → Enter | 半角の `2^10` で確定 (全角 `2＾10` にならない) | 006 |
| 69 | 任意 | TextEdit | 設定を OFF に戻して `2^10` Space → Enter | 計算の候補が出ない。Enter で全角 `2＾10` (今までどおり) | 006 |
| | | | **C. Zenzai を ON** (設定タブの「Zenzai (ニューラル変換、実験的)」。初回は約 74 MB をダウンロードする。終わるまで待つ) | | |
| 70 | 必須 | TextEdit | `daigakunosenseinohanashiwokiita.` Enter で確定したあと `kouenha` Space | 先頭の候補が `講演は` (前の文が効く) | 017 |
| 71 | 任意 | TextEdit | 新しい書類 (前の文なし) で `kouenha` Space | 先頭の候補を記録 (今までと同じ結果のはず) | 017 |
| 72 | 任意 | TextEdit | `daigakunosenseinohanashiwokiita.konohonha` のあとに `atsui` Space | `厚い` が先頭 | 017 |
| 73 | 任意 | TextEdit | `maenobundesu.` Enter で確定したあと `ninatteshimau` Space | 先頭の候補を記録。Zenzai を OFF にして同じ手順の先頭も記録して比べる | 017 |
| 74 | 任意 | TextEdit | 確定のあと Return で改行してから `kouenha` Space | Zenzai OFF の結果と同じ (前の段落は渡さない) | 017 |
| 75 | 任意 | TextEdit | Zenzai ON で何文か打つ | 1 キーごとの遅さが気にならない (感想) | 017 |
| | | | **D. 設定「変換後も続けて入力できる」を ON** (設定タブの「変換後も続けて入力できる」。入力メニューには出ない) | | |
| 76 | 任意 | TextEdit | **前提**: 4 かな以上の語 (`kikaiwo`。きかいを) で、Space で **1 番目でない候補** (`機会を`) を選び直して確定する (変換エンジンの既定 1 位をそのまま確定しても学習されず、数えない)。設定「変換後も続けて入力できる」ON で、`kikaiwo` を Space で変換して 1 番目でない候補を選び、続けて入力して `kikaiwo` を Enter、と 2 回目の Enter まで進める | 最初の選び直しが 1 回目に数えられるので、**2 回目の Enter の直後** (既定のしきい値 3) に、入力位置の下へ 4 秒だけ 1 行のヒント (`『機会を』を辞書に登録できます (入力メニューから)`) が出る (1 日 1 回)。以後は入力メニューに「『機会を (機械を)』を辞書に登録」が残る。しきい値ちょうどで 1 回だけなので、**77 など別の項目で同じ語を使い回さない** (すでに数えた語は出ない) | 026 |
| | | | **E. 設定は変えずに、ライブ変換 ON (既定) のまま学習を見る** | | |
| 77 | 必須 | TextEdit | **前提**: 4 かな以上の語 (`kouenwo`。こうえんを) で、Space で **1 番目でない候補** (`講演を`) を選び直して確定する (これが 1 回目。既定 1 位をそのまま確定しても学習されない。ライブ変換は 4 かな以上でしか働かず、3 かなの語 `箸を` はかなのまま確定されて数えない)。続けて `kouenwo` Enter を打つ (ライブ変換の表示が `講演を` になってから確定。Space は押さない) | 2 回目の Enter の直後 (既定のしきい値 3) に、入力位置の下へ 4 秒だけ 1 行のヒントが出て (1 日 1 回)、以後は入力メニューに「『講演を』を辞書に登録」が残る。しきい値ちょうどで 1 回だけなので、**76 の語 (`機会を`) や、すでに数えた語を使い回さない** | 026 |
| 78 | 任意 | TextEdit | 上のあと、同じ読みの予測 | 前より上に出る | 026 |
| 79 | 任意 | TextEdit | `kouen` Enter (語末の n で表示と確定が違う形) を 3 回 | `kouen` は実機では `公園` に確定する (`講演` にならない)。3 回確定すると、確定した語の登録提案が出る | 026 |
| 80 | 任意 | TextEdit | 確定せずにほかのアプリをクリックして確定する (暗黙の確定) | 回数は増えない (その分の提案が早く出ない) | 026 |
| 81 | 任意 | TextEdit | 設定「ATOK スタイルのキー操作」ON で Shift+↓ のあとの Enter。設定「Enter は見えているとおりに確定」ON で `kantan` Enter | どちらも回数は増えない | 026 |
| | | | **F. 再変換 (設定は変えない。学習データを見る)** | | |
| 82 | 必須 | TextEdit | 先に入力メニュー →「設定・辞書…」→「Meltype 辞書」の「学習した語」タブで `meeting`・`review` が無いことを確かめる (あれば増えたか分からないので、この行は飛ばす)。次に英数入力で `asita no meeting de review shimasu` と打ち、その文を選択して、「あ」にして Shift+Space → Enter | 「学習した語」タブに `meeting`・`review` が増えない | 027 |
| 83 | 必須 | TextEdit | そのあと日本語入力で `kyouhameetingdesu` Enter | `きょうはmeetingです` (meeting が英字のまま) | 027 |
| 84 | 任意 | TextEdit | 再変換 → Space で選んだままの文 → Enter。別に、再変換 → 別の欄をクリック。別に、Shift+Enter | いずれも `meeting`・`review` が増えない | 027 |
| 85 | 任意 | TextEdit | 再変換 → F6 (または F7) → Enter | `meeting`・`review` が増えない | 027 |
| 86 | 任意 | TextEdit | 対照: ふつうに `meeting` と打って F6 → Enter。終わったら「学習した語」タブで `meeting` を削除する | こちらは「学習した語」タブに増える (今どおり)。削除を忘れると以後 meeting が崩れる | 027 |

## 5. 要確認 (Core の今の結果が、文書の期待と違う)

「Core では今 X」は、今の main の `--repro` (一般アプリの打ち方) の結果。実機でも打って、どちらが正しいか (期待を直すか、直す対象か) をリョウさんが決める。

| 番号 | アプリ | 打つもの | 期待と、Core の今 | 要件 |
|---|---|---|---|---|
| R1 | 実機 | `beepon` Enter (Hermes・TextEdit) | 要件 005 の実機チェックは `beep音` (本家 #1377 の期待)。**実機 (2026-11-26 自動打鍵) は、ライブ変換 ON で `べえ本`、OFF で `べえぽん`** (Core の `--repro` は `べえぽん`。005 の記録欄に「`beep` を足した後も `べえぽん` のまま」と既にある。どちらも `beep音` にはならない。目標ではない)。期待を直すか | 005 |
| R2 | 実機 | `youtubenodouga` Enter | 要件 001 の期待は `youtubeの動画`。**実機 (2026-11-26 自動打鍵) は、ライブ変換 ON で `YouTubeの動画`、OFF で `YouTubeのどうが`** (大文字の Y は IME の表記)。Core の `--repro` は `youtubeのどうが`。漢字は ON の表示か Space の候補。表では Space の行に直した | 001 |

## 載せなかった項目 (理由)
- Obsidian・Safari のフォーム・Slack・Discord・ChatGPT・Word・Pages の項目: 使うアプリでない。Safari のフォームは Hermes の入力欄で代用した (Safari 固有の挙動は未確認のまま)。
- 要件 017 の「𠮷野家 を確定した直後」: 打つ手段がない (要件 024 の範囲)。
- 要件 013 の `[[Hello World]]`・`[[nikki` Space → `]]`・Shift+↓ で候補を回す形: 「限界」として記録済みの動き。
- 要件 005 の ChatGPT での `beepon`: 要確認の R1 に移した。
- 確認済みの項目 (`ost`・`beep`・`iCloud`・`pixiv`・`karento` など。各要件の `[x]`) は再掲しない。

## 集計
- 番号つきの項目: 必須 39 / 任意 57 (計 96。追加分を含む)
- 要確認: 2 件 (R1・R2)
- 直したこと: 要件 001 の `api` 単体の期待 (旧 R1。Core では `あぴ`。2026-11-26 にリョウさんが実機のターミナルで打つと `api` の英字のままだったので、確認表 47 は実機の結果に直した)

## 追加分

要件 028・024 の実機チェックリスト (それぞれのブランチの文書から写した)。番号は上の続き。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| | | | **要件 028 (再変換の文を学習しない)**。前提: 設定タブの「変換後も続けて入力できる」を ON。TextEdit で日本語入力 (「あ」) にして試す。**028 の修正が入った版で打つ** (入っていない版では誤学習が入り、消すには入力メニューの「辞書の登録提案の履歴を消去」か「学習データをすべて消去…」が要る)。1 文節で済む短い語で打つ。 | | |
| 87 | 必須 | TextEdit | 英数入力で `arigatou` と打って選択 → 「あ」にして Shift+Space。候補を見る。次に Space で選んだままの `arigatou` にして、続けて `yo` → Enter | 候補が `ありがとう` (ひらがなだけ) と選んだままの `arigatou` の 2 つ (ならなければ別の語で記録)。確定は `arigatouよ` | 028 |
| 88 | 必須 | TextEdit | 上のあと `ariga` まで打つ (予測を見る)。続けて `arigatou` を打ち、ライブ表示と Space の最初の候補を見る。見るだけ。Esc で取り消す (Space で変換したあとは Esc 2 回) | 予測に `arigatou` が出ない。ライブ表示も Space の最初の候補も `arigatou` (ローマ字) にならない | 028 |
| 89 | 必須 | TextEdit | 同じ `arigatou` をもう一度選んで Shift+Space | 候補が 1 回目と同じ 2 つのまま (最初の候補が `arigatou` にならない) | 028 |
| 90 | 任意 | TextEdit | 1 回目の最後を `hello` Space・`kyouhagoogle` Space・Shift+Enter・Tab・別の欄をクリック (暗黙の確定) に替える | いずれも 88・89 と同じ (ローマ字が予測・候補に出ない) | 028 |
| 91 | 任意 | TextEdit | 長い文: 「あ」で `ohayou gozaimasu` を打って選択 → Shift+Space → Enter を 3 回。そのあと `ohayou` まで打つ | 入力メニューに登録の提案が出ない。予測にローマ字の文が出ない (ひらがなだけになる語を選ぶ。ならなければ別の語で記録) | 028 |
| 92 | 任意 | TextEdit | 設定「変換後も続けて入力できる」OFF で、87 と同じ手順 | 今までどおり、ローマ字の語が出てこない | 028 |
| 93 | 任意 | TextEdit | `hashiwo` Space で 1 番目でない候補 (例: `箸を`) を選び → `tanni` → Enter。次に `hashiwo` Space | 選んだ候補が 1 番目に出る (普通の変換の続けて入力は今も学習する。変えていないことの確認) | 028 |
| | | | **要件 024 (サロゲートペアの漢字)**。今の変換エンジンは `𠮷` を返さないので実機では差が見えない。「表示が変わらない・壊れていない」だけを見る。 | | |
| 94 | 任意 | TextEdit | `kara` Space | `から` が先頭で、`～` は後ろ (今と同じ) | 024 |
| 95 | 任意 | TextEdit | `yoshi`・`hokke`・`yoshisan` を 1 つずつ Space | 候補の並びと区切りを記録 (BMP の外の漢字が出るか・何番目か。出なければ要件 024 の不具合は実機では起きない)。落ちない・表示が崩れない | 024 |
| 96 | 任意 | TextEdit | Zenzai ON (4 章 C の設定) で、空の書類に `𠮷あいうえおかきくけ` を貼り付け、その後ろで `kawa` Space | 落ちない。表示も変わらない | 024 |

## 2026-11-26 自動打鍵の結果

リョウさんの依頼で、TextEdit への自動打鍵で確かめた結果です (人の手で打ったものではありません)。判定は、上の表の (直したあとの) 期待に、実機の実際の値を当てたものです。

### 件数 (番号単位。(a)(b) は悪い方)
| 優先 | OK | NG | 未実施 | 計 |
|---|---|---|---|---|
| 必須 | 28 | 0 | 11 | 39 |
| 任意 | 44 | 0 | 13 | 57 |

### リョウさんの実機の結果と合わせた件数 (2026-11-26)
自動打鍵で未実施だった 24 項目 (必須 11・任意 13) は、リョウさんが自分の手で打って確かめた (下の「2026-11-26 リョウさんの実機確認」の節)。自動打鍵の結果と手の結果は混ぜず、番号ごとに分けて数えると次のとおり。

| 優先 | 自動打鍵 OK | リョウさんの手 OK | 合わせて OK | NG | 未実施 | 計 |
|---|---|---|---|---|---|---|
| 必須 | 28 | 11 | 39 | 0 | 0 | 39 |
| 任意 | 44 | 13 | 57 | 0 | 0 | 57 |

- 96 項目すべて確認済み、NG 0。
- ただし、自動打鍵で一部だけ省いた形は、リョウさんの手でも打っていないので、まだ確かめていない: 82 (「学習した語」タブは見ず、学習データのファイルで確認)、84・90 (別の欄・ほかのアプリをクリックして確定する形。90 の「別の欄をクリック (暗黙の確定)」)。
- 81 はリョウさんが「多分 OK」(回数は画面で見えないので、目視での OK)。
- 上の「未実施」の表の数は、自動打鍵だけの時点のもの (そのまま残す)。

- 自動打鍵の報告 (期待を直す前) では、必須は OK 21・NG 8・未実施 10、任意は OK 32・NG 13・未実施 12 だった。
- **N1** (ライブ変換 ON で Enter だけだと漢字のまま確定する): 不具合ではない。ライブ変換は既定 ON で、表示のまま Enter で確定するのが今の仕様。かなの期待は、漢字の変換エンジンが無い Core の `--repro` から書いたためのずれだった。期待を直したので、該当の 19 行 (必須 7: 4・8・12・13・18・33・34、任意 12: 9・14・15・17・20・22・24・25・26・29・32・42) は NG から OK に数え直した。実機の値は表のとおりで、英字が残らない (英字にすべき所は英字のまま) ことを見て判定した。
- **N2** (76・77 で『講演を』の登録提案が出ない): 不具合ではない。登録提案に数えるのは、選び直した文節か、学習済みの語を最初の候補のまま確定したときだけで、学習していない `講演を` は数えない。79 で提案が出たので、要件 026 の動き (ライブ表示のまま Enter でも数える) は確かめられている。76・77 は前提の手順を足したので、**NG から未実施に数え直した** (前提を足した手順での打ち直し待ち)。上の未実施の必須 11 と任意 13 に含む。

### 未実施の理由
- TextEdit 以外のアプリ: ターミナル 46〜56、LibreOffice 57、Hermes 58〜63 (必須 10・任意 8)。TextEdit での参考値は取ってあるが、アプリが違うので判定に使わない。
- 75 (遅さの感想)・78 (77 の学習が前提)・80 (ほかのアプリのクリックを避けた)・81 (ATOK スタイルの設定キーが設定に無く、「Enter は見えているとおりに確定」の半分だけ OK): 任意 4。
- 76・77: 上の N2。
- 一部だけ未実施の項目 (OK に数えた): 82 (「学習した語」タブは見ず、学習データのファイルで確かめた)・84・90 (別の欄・ほかのアプリをクリックして確定する形)。

### TextEdit 由来の変化 (IME の不具合ではない)
- 自動修正: `asita`→`Akita`、`kyou`→`You`、`sorede`→`Soured` など。82・84・85 は語を変えても同じ結論だった。
- 行頭の大文字化: `Beep sound`・`Pip3install requests`・`Cd ~/Downloads`。
- `python3`・`pip3` の後の空白が消える (`python3ーmvenv`): TextEdit は一般アプリなので、要件 030 (コード系アプリ = ターミナル) の範囲外。ターミナルの 53・54 で確かめる。
- 再変換 (Shift+Space): ⌘A で選んだ単語 1 つでは効かず、Shift+← で選ぶと効く (87〜93)。
- 𠮷 は画面のフォントでは 吉 に見えるが、文字は 𠮷 のまま (96)。

## 2026-11-26 リョウさんの実機確認 (自動打鍵で未実施だった項目)

リョウさんが自分の手で打って確かめた結果です (自動打鍵ではありません)。24 項目 (必須 11・任意 13)、結果は すべて OK、NG 0。

| 番号 | 結果 | 備考 |
|---|---|---|
| 46 | OK | `apinoerror` → `apiのerror` |
| 47 | OK | `error` は英字のまま。`api` 単体 Enter は **`api` (英字のまま)**。確認表の期待 (Core の `--expect` 由来の `あぴ`) と違うが、英字のままのほうが自然。差の原因は未確認 (学習データの英語の記憶か、コード系アプリの判定か)。期待を実機の結果に直した |
| 48〜52 | OK | `ls-files`・`cd-rom`・`beep sound`・`iTunes`・`#endif`・`karen` |
| 53 | OK | `python3 -m venv .venv` の `python3` の後の空白が消えない (要件 030 の中心) |
| 54 | OK | `pip3 install requests` の空白が消えない (要件 030 の中心) |
| 55・56 | OK | `cd ~/Downloads` は今までどおり。`nihongo` Space は変換になる |
| 57 | OK | LibreOffice: `itotekinagobyuu` |
| 58 | OK | Hermes: `kinyuu` → `記入` |
| 59 | OK | Hermes: `obsidiande` → `obsidianで` |
| 60 | OK | Hermes: `oeksoundwo` → `oeksoundを` |
| 61 | OK | Hermes: `pixivde` → `pixivで` |
| 62 | OK | Hermes: `yappane` → `やっぱね` |
| 63 | OK | Hermes: `Yukishiro` |
| 75 | OK | Zenzai ON の速さは問題ない (感想) |
| 76 | OK | 手順を直して OK (下の経緯) |
| 77 | OK | 手順を直して OK (下の経緯) |
| 78 | OK | 同じ読みの予測が前より上に出る |
| 80 | OK | 暗黙の確定で回数は増えない |
| 81 | OK (多分) | 回数は画面で見えないので、目視での OK |

### 76・77 の経緯 (手順を直した理由)
- 最初の手順の `講演を` は、77 の時点で既にしきい値に達していたので、提案 (ヒント) が出なかった (数えるのはしきい値ちょうどの 1 回だけ)。
- `箸を` (はしを。かな 3 字) は、ライブ変換が 4 かな以上 (`LiveConversionMinLength = 4`) でしか働かず、かなのまま確定されて数えられなかった。
- `単位を` は、変換エンジンの既定 1 位で選び直しにならず、学習されなかった。
- `kikaiwo` で 1 番目でない候補 (`機会を`) を選び直して確定 → 「変換後も続けて入力できる」ON → 1 語を Space で変換し続けて `kikaiwo` を Enter を 2 回、で、入力メニューに「『機会を (機械を)』を辞書に登録」が出た。
- コードで確かめたこと: 登録提案に数えるのは、選び直した文節 (`clause.Changed`) と、学習済みの語を最初の候補のまま確定した文節だけ (`CompositionController.RecordSuggestions`)。回数がしきい値 (既定 3。設定で 2〜10) に**ちょうど**なったときだけヒントを覚える (`DictionarySuggestions.Record`)。ヒントは `mac/Sources/MeltypeIME/InputController.swift` の確定直後に、入力位置の下へ 4 秒 (`SuggestionHint.show` の既定)。1 日 1 回 (`TakeHint` の `LastHint`)。
