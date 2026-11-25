# 実機の確認表 (2026-11)

要件 001・003・004・016〜023・025〜027・030 (実機未確認) と、002・005・006・008・010〜014 (主項目のみ確認) の、未確認の実機チェックを 1 枚にまとめたものです。基準は main bc1b603。

## 使い方
- 結果は **「打った > 結果」** の形で、番号つきで貼る (例: `12 pazolite > pazolite`)。期待と違うときだけ、見えたとおりを書けば足ります。
- **必須** だけで約 25〜30 分 (1 行 15〜20 秒に、設定の切り替え・ダウンロード待ちを足した見込み)。打つのが長い 64・65 と、Zenzai を入れる C を除くと約 12〜15 分。**任意** は時間があるときに。
- アプリは TextEdit・ターミナル・LibreOffice・Hermes だけです。文書にあった Safari のフォーム・Slack・Discord・ChatGPT・Obsidian・Word・Pages は使わないので、Hermes か TextEdit で代用するか載せていません (末尾の「載せなかった項目」)。
- JIS 配列のキー: `-` は 0 の右、`^` はその右、`~` は Shift+^、`#` は Shift+3。
- 1 行ごとに、Enter の前の見え方と Enter 後の結果の両方を見て、⌘A → Delete (ターミナルは Ctrl+U) で消してから次へ。
- 各要件文書の未確認 `[ ]` を拾い、今の main の Core (`--repro`。ターミナルの行は表ごとにコード系アプリとして打つ `--expect`) で期待がまだ正しいかを確かめて並べました。ずれたものは「5. 要確認」に分けています。期待の多くは Core の結果と同じです。Space の候補・学習・表示は Core で測れないので実機でだけ確かめます。

## 1. TextEdit (設定は今のまま)

ライブ変換 ON・Zenzai OFF・そのほか既定。1 行ごとに打って見える形を確かめ、Enter で確定して結果を見る。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| 1 | 必須 | TextEdit | `apinoerror` Enter | `apiのerror` | 001 |
| 2 | 任意 | TextEdit | `youtubenodouga` Space | 候補に `youtubeの動画`。Enter で確定する形は `youtubeのどうが` (要確認 R2) | 001 |
| 3 | 必須 | TextEdit | `pt-query-digest` Enter | `pt-query-digest` | 004 |
| 4 | 必須 | TextEdit | `ra-menwotaberu` Enter | `らーめんをたべる` | 004 |
| 5 | 必須 | TextEdit | `karen` Enter | `karen` (英字のまま) | 010 |
| 6 | 必須 | TextEdit | `obsidian` Enter | `obsidian` | 016 |
| 7 | 必須 | TextEdit | `obsidianwo` Enter | `obsidianを` (変換ボックスの表示と確定が同じ) | 016 |
| 8 | 必須 | TextEdit | (a) `dainyuu` Enter  (b) 別に `dainyuu` Space | (a) `だいにゅう`  (b) 候補に `代入` | 018 |
| 9 | 任意 | TextEdit | `hensuunidainyuu` Enter | `へんすうにだいにゅう` | 018 |
| 10 | 必須 | TextEdit | `pazolite` Enter | `pazolite` | 021 |
| 11 | 必須 | TextEdit | `oeksound` Enter | `oeksound` | 021 |
| 12 | 必須 | TextEdit | (a) `pazolitenokyoku` Enter  (b) 別に `pazolitenokyoku` Space | (a) `pazoliteのきょく`  (b) 候補に `pazoliteの曲` | 021 |
| 13 | 必須 | TextEdit | (a) `baguhoukoku` Enter  (b) 別に `baguhoukoku` Space | (a) `ばぐほうこく`  (b) 候補に `バグ報告` | 019 |
| 14 | 任意 | TextEdit | `tunagarumademattekara` Enter | `つながるまでまってから` | 019 |
| 15 | 任意 | TextEdit | `CLAUDE.mdwohiraku` Enter | `CLAUDE.mdをひらく` | 019 |
| 16 | 任意 | TextEdit | `,nisuruwo` Enter | `、にするを` | 019 |
| 17 | 任意 | TextEdit | `reibunn:pregnant_man:` Enter | `れいぶん:pregnant_man:` (絵文字の補完の窓は Slack・Discord だけの話。ここでは文字だけ確かめる) | 019 |
| 18 | 必須 | TextEdit | (a) `itotekinagobyuu` Enter  (b) 別に `itotekinagobyuu` Space | (a) `いとてきなごびゅう`  (b) 候補に `意図的な誤謬` | 020 |
| 19 | 任意 | TextEdit | `ronpa` Enter | `ろんぱ` | 020 |
| 20 | 任意 | TextEdit | `uchidome` Enter | `うちどめ` | 020 |
| 21 | 任意 | TextEdit | `Fluorite` Enter | `Fluorite` | 020 |
| 22 | 任意 | TextEdit | (a) `fanntatowirukinnsonn` Enter  (b) 別に `fanntatowirukinnsonn` Space | (a) `ふぁんたとうぃるきんそん`  (b) 候補に `ファンタとウィルキンソン` | 020 |
| 23 | 必須 | TextEdit | (a) `sitaidesu` Enter  (b) 別に `sitaidesu` Space | (a) `したいです`  (b) 1 番目の候補が `したいです` か `死体です` かを記録 | 022 |
| 24 | 任意 | TextEdit | `amatte` Enter | `あまって` | 022 |
| 25 | 任意 | TextEdit | `chomatte` Enter | `ちょまって` | 022 |
| 26 | 任意 | TextEdit | (a) `kakikomiera-` Enter  (b) 別に `kakikomiera-` Space | (a) `かきこみえらー`  (b) 候補に `書き込みエラー` | 022 |
| 27 | 任意 | TextEdit | `2nitime` Space | 空白が入らず変換中のまま (候補に `2日目`) | 022 |
| 28 | 必須 | TextEdit | (a) `atude` Enter  (b) 別に `atude` Space | (a) `あつで`  (b) 候補に `厚手` が出るかを記録 | 023 |
| 29 | 任意 | TextEdit | `tasinamu` Enter | `たしなむ` | 023 |
| 30 | 任意 | TextEdit | `haatu` Enter | `はあつ` | 023 |
| 31 | 任意 | TextEdit | `getu` Enter | `げつ` | 023 |
| 32 | 任意 | TextEdit | `subewomotanai` Enter | `すべをもたない` | 023 |
| 33 | 必須 | TextEdit | (a) `setteisiteirundakedo` Enter  (b) 別に `setteisiteirundakedo` Space | (a) `せっていしているんだけど`  (b) 候補に `設定しているんだけど` | 025 |
| 34 | 必須 | TextEdit | (a) `mainkannsuu` Enter  (b) 別に `mainkannsuu` Space | (a) `mainかんすう`  (b) 候補に `main関数` | 025 |
| 35 | 任意 | TextEdit | `@exampleabc-jh.ed.jpdesu` Enter | `@exampleabc-jh.ed.jpdesu` (全部そのまま) | 025 |
| 36 | 任意 | TextEdit | 先に `kyouhaiitenkidesune,kinoumoiitenkidesitane.` Enter で 21 文字以上を確定し、その直後に `@exampleabc-jh.ed.jpdesu` Enter | 直前の確定があっても、上の行と同じく途中から日本語にならない | 025 |
| 37 | 任意 | TextEdit | `kore` Enter | `これ` (変わらない) | 011 |
| 38 | 任意 | TextEdit | `ima` Enter | `いま` (変わらない) | 012 |
| 39 | 任意 | TextEdit | `ichi` Enter | `いち` (変わらない) | 008 |
| 40 | 任意 | TextEdit | `beeru` Enter | `べえる` (変わらない) | 005 |
| 41 | 任意 | TextEdit | `obake` Enter / `obon` Enter | `おばけ` / `おぼん` (変わらない) | 016 |
| 42 | 任意 | TextEdit | `oekaki` Enter / `pazuru` Enter | `おえかき` / `ぱずる` (変わらない) | 021 |
| 43 | 任意 | TextEdit | `[[nikki]]` Space | 候補に `[[日記]]` (`[` が半角のまま) | 013 |
| 44 | 任意 | TextEdit | `[[nikki` まで打つ (Enter 前) → `]]` を足す | 途中は `「「にっき`、`]]` を足すと `[[にっき]]` に見える。この見え方が気にならないか感想を書く | 013 |
| 45 | 必須 | TextEdit | `nihongo` Space | 変換になる (`日本語` が候補)。ターミナルの同じ行と比べる | 030 |

## 2. ターミナル (zsh)

Enter は IME の確定だけにする。確定のあとに 2 回目の Enter は押さない (コマンドが実行される)。確かめたら Ctrl+C か Ctrl+U で行を消す。

| 番号 | 優先 | アプリ | 打つもの | 期待 | 要件 |
|---|---|---|---|---|---|
| 46 | 必須 | ターミナル | `apinoerror` Enter | `apiのerror` | 001 |
| 47 | 必須 | ターミナル | `error` Enter / `api` Enter | `error` (英字のまま) / `あぴ` (単体はかなになる。単体で英字に残すのは 001 の範囲外) | 001 |
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
| 76 | 任意 | TextEdit | Space で変換して確定し、続けて学習済みの語を打って Enter、を 3 回 | 3 回目で、続けて打った語の登録提案が出る | 026 |
| | | | **E. 設定は変えずに、ライブ変換 ON (既定) のまま学習を見る** | | |
| 77 | 必須 | TextEdit | `kouenwo` Enter を 3 回 (毎回、ライブ変換の表示が `講演を` になってから確定する。Space は押さない) | 3 回目で入力メニューに「『講演を』を辞書に登録」の提案が出る (別の学習済みの語でもよい) | 026 |
| 78 | 任意 | TextEdit | 上のあと、同じ読みの予測 | 前より上に出る | 026 |
| 79 | 任意 | TextEdit | `kouen` Enter (語末の n で表示と確定が違う形) を 3 回 | `講演` で 3 回確定すると提案が出る | 026 |
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
| R1 | 実機 | `beepon` Enter (Hermes・TextEdit) | 要件 005 の実機チェックは `beep音` (本家 #1377 の期待)。**Core では今 `べえぽん`** (005 の記録欄に「`beep` を足した後も `べえぽん` のまま」と既にある。目標ではない)。期待を `べえぽん` に直すか | 005 |
| R2 | 実機 | `youtubenodouga` Enter | 要件 001 の期待は `youtubeの動画`。Enter 単独の確定は漢字にならず、**Core では今 `youtubeのどうが`**。漢字は Space の候補。表では Space の行に直した | 001 |

## 載せなかった項目 (理由)
- Obsidian・Safari のフォーム・Slack・Discord・ChatGPT・Word・Pages の項目: 使うアプリでない。Safari のフォームは Hermes の入力欄で代用した (Safari 固有の挙動は未確認のまま)。
- 要件 017 の「𠮷野家 を確定した直後」: 打つ手段がない (要件 024 の範囲)。
- 要件 013 の `[[Hello World]]`・`[[nikki` Space → `]]`・Shift+↓ で候補を回す形: 「限界」として記録済みの動き。
- 要件 005 の ChatGPT での `beepon`: 要確認の R1 に移した。
- 確認済みの項目 (`ost`・`beep`・`iCloud`・`pixiv`・`karento` など。各要件の `[x]`) は再掲しない。

## 集計
- 番号つきの項目: 必須 39 / 任意 57 (計 96。追加分を含む)
- 要確認: 2 件 (R1・R2)
- 直したこと: 要件 001 の `api` 単体の期待 (旧 R1。`あぴ` が正しい)

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
