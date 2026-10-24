# W12 — 本家 #1029 (3)「前後の英単語を根拠にする」を、正解表で測ってから実装

2026-10-24。worktree `wt-to1029` (ブランチ `feat/to1029`、`main` の `bcebd08` から)。W10 (`docs/W10_SPACE1029_2026-10-24.md`) が「(3) は悪化を測れる正解表が無いので見送り」とした続き。**先に表を作り、候補ごとに fixed / broken を測り、broken 0 の分だけ実装した。**

## 1. 正解表 `eval/cases/short-words-in-english.tsv` (182 件)

期待は「アプリで本来そうなってほしい出力」を作者が考えて書いた。現状の出力には合わせていない (実機の記録ではない)。比べ方はすべて `full`。

- 英語 112 件: `to`・`do`・`go`・`be`・`so`・`no`・`me`・`he`・`we` を含む英文。型は下の表。
- 日本語対照 70 件: 同じ語をローマ字の日本語文で使うもの (`kyou to asobu`・`so ka`・`ima ha dame`)、英単語の直後の日本語 (`Slack de hanasu`)、**今回の変更が狙う形に見える日本語** (`Slack to` Enter・`Zoom to go ji ni au`・`Slack to Zoom`・`Meeting to`)。件数は依頼の 40〜60 を超えた (候補を壊すか見るための対照を足していったため)。
- テスト用の変換エンジンは `kyou` を `今日` にするので、`kyou to asobu`・`kyou no tenki` の 2 件だけ期待を `今日` にしてある (判定ではなくエンジンの辞書の差)。
- 注意: `Mac to iPhone`・`Google Meet to Zoom` は、日本語として `Mac とiPhone` を期待したが、変更前から英語のまま残る (どちらも英語としても読めるので、期待の置き方が分かれる行)。変更前後で同じ結果なので、broken の判定には影響しない。

## 2. 現状 (main `bcebd08`) のパス率

全体 **108/178** (記号つきの 4 件は最後に足したので別掲。足した後の base は 108/182)。

| 型 | 例 | 現状 |
| --- | --- | --- |
| 英語: `to` + 後ろが英語と分かる語 | `How to use it` `Nice to meet you` | 11/12 (`Ready to begin` だけ外れ: `begin` が `Ready とbegin` に) |
| 英語: `to` + 後ろも曖昧な `do`/`go`/`be` | `What to do` `Time to go` `Going to be` | **1/20** |
| 英語: 文末が `to` | `Where to` `Going to` `Used to` | **0/10** |
| 英語: `to` + 大文字始まりの固有名詞 | `Welcome to Tokyo` `Trip to London` | **1/12** |
| 英語: `to` 以外の短い語 | `So what` `No way` `Let me know` `Tell me more` | 25/32 (外れ 7 は下の「残り」) |
| 英語 (後から足した分。候補の語の一覧を作った後) | `Ways to go` `Moving to Berlin` `Likely to` | 2/15 |
| 英語 (`-ing` + to。候補 `ing` 用) | `Learning to do` `Wanting to` | 0/7 |
| 英語: 文末の記号つき | `What to do?` `Where to?` | 0/4 (後から追加) |
| 日本語対照 (全部) | `kyou to asobu` `Slack to` `Zoom to go ji ni au` | 68/70 (外れ 2 は上の注意の 2 件) |

英語だけで見ると **現状 40/112 (35.7%)**、日本語対照は 68/70。

## 3. 候補ごとの測定 (`MELTYPE_W12` で切り替えて、同じ表を回した。base = 変更なし)

fixed = base で外れ、候補で当たる。broken = base で当たり、候補で外れる。表は 178 件の時点 (`eval/cases` の他の表は全候補で変化なし)。

| 候補 | 内容 | fixed | broken |
| --- | --- | --- | --- |
| a1 | 文末の `to`: 直前が英単語 (空白 1 つ) なら**何でも**英語に | 16 | **6** (`Slack to`・`Zoom to`・`Chrome to`・`Gmail to`・`Meeting to`・`Training to` が `to` のまま) → **不採用** |
| a2 | 文末の `to`: 直前の語が**一覧** (`what`/`where`/`going`/`want`/`used`/`back`/`next`/`ready`/`nothing` など疑問詞・動詞・前置詞・副詞。名詞は入れない) にあるときだけ | 11 | 0 |
| b | 直前の `to` が `と` で確定済みで、その前が一覧の語、今の語が `do`/`go`/`be` のとき、`to` と今の語を両方英語に確定し直す | 19 | 0 |
| c | 大文字始まりの語の前の `to` を、前の語が別の一覧 (`welcome`/`back`/`next`/`trip`/`train`/`flight`/`go`/`thanks` など) にあるときだけ直す | 11 | 0 |
| a2+b+c | 採用 | **41** | **0** |
| a1+b+c | | 46 | 6 (a1 と同じ) |
| a2+b+c+`ing` | 一覧に加えて `-ing` で終わる語もすべて対象 | 48 | **5** (`Meeting to`・`Training to`・`Meeting to go ji ni au`・`Hosting to do ko ni aru`・`Testing to be tsu ni`) → **不採用** |

`ing` は英語側 7 件 (`Learning to do`…) を全部直すが、名詞の `Meeting`・`Training` を壊す。例外を一覧にしても名詞は数えきれないので見送り。

### 測定の限界 (重要)
- a2・b・c の一覧は、表の英語側を見たあとに作った。**表の「英語 (to + do/go/be)」「文末」「固有名詞」の fixed は、その分だけ楽観的**。独立に確かめるため、一覧を作った後に、一覧に無い前の語の文を 15 件追加した (`Ways to go`・`Eager to go`・`Moving to Berlin`・`Likely to`…)。結果は **15 件中 3 件だけ**直った (base は 2/15 → 3/15)。つまり一覧にある語の型は直るが、一覧に無い語の型 (`Ways`・`Eager`・`Fun`・`Moving` …) はこの変更では直らない。
- 日本語対照の 70 件で broken 0 は、**今回の一覧の語を前に置いた日本語 (`Slack to` など)** で確かめている。一覧の名詞寄りの語 (`time`・`way`・`place`・`thing`) のあとに日本語の `to` を打つ例は表に無い。

## 4. 採用したもの (最初の版。**§8 で狭めた**。この節の一覧・件数は最初の版のもの)

a2 + b + c (すべて `CompositionController.cs`)。**設定は付けない。** 理由: 70 件の日本語対照 (a1 と `ing` が壊した型を含む) で broken 0、`eval-compare` で broken 0、`--eval` は前後同じ。効く条件が「前の語が一覧にある英語の機能語」と狭く、一覧の外は今までどおりなので、設定で分ける利点 (悪化を避ける) が見つからなかった。誤って効いたときは、確定後の F10 / F6 で直せる (既存)。逆に設定を付けると、既定オフでは誰も使わず本家 issue の (3) が満たされないままになる。

- (a) `Commit()` (Enter・句読点以外の確定) で、変換中でなく、Space で変換を始めたのでもなく、語が `to` 1 語で、直前が「英語で確定した語 + 空白 1 つ」でその語が `WordsBeforeToInEnglish` にあるとき、`と` にせず `to` を英語で確定する。
- (b) `CommitText()` の入口で、今の語が `do`/`go`/`be` (日本語として確定されるところ) のとき、直前の確定が `と` (`to`・Space つき) で、その前の英単語が一覧にあれば、`と` を `to ` に書き換え (`DeleteBackward` + `CommitText`、入力欄の記録と違えば何もしない)、今の語を英語 (`do `) にする。
- (c) 既存の `CorrectPreviousCommit` の「大文字始まりの語では 1〜2 文字の語を直さない」(`Google と Apple` を守る条件) は残し、`to` で、前の語が `WordsBeforeToBeforeProperNoun` にあるときだけ例外にする。

## 5. 見送り・残り

- a1・`ing`: 上のとおり broken が出るので不採用。
- **文末の記号つき (`What to do?`・`Where to?`・`Time to go.`)**: 記号が変換ボックスの中に入り (`ど？`)、語が `do?` になるため a・b の条件に当たらず**直らない** (表の 4 件、0/4)。実際の文は記号で終わることが多いので、実用上の穴。直すには「語 + 句読点」を英語側の半角で確定する処理が要る。今回は広げていない。
- 一覧に無い前の語 (`Ways to go`・`Moving to Berlin`・`Likely to`)。名詞か形容詞かを語単位で見分ける根拠が無い。
- `to` 以外の短い語 7 件 (`Tell me more` `Not me` `We do` `Go on` `Let me go` `Say no more` `Why do we go`) と `Ready to begin`: 今回の候補の範囲外。`me`/`no`/`do` の前後の語の根拠に広げるなら、`Let me go` のような型と `me wo tojiru` の日本語を同じ表で測る必要がある。次の候補。
- 文が `to` で終わり、Space で変換を始めた後に Enter (`Where to␣⏎`) の型は (a) の対象外 (コードの条件上。この型は測っていない)。

## 6. 検証

- テスト: 全体 **1041/1041** (もとは 1033。`ShortWordsInEnglishTests` 8 件を追加)。先に実装を外した状態で走らせ、直る型の 4 件が赤 (`Where と`・`What とど`・`Welcome とTokyo`・`Time とごほめ`)、日本語を守る 4 件は緑、その後に実装して 8/8 緑。
- `--eval`: **1292/1294 (99.8%)**。変更前と同じ (外れ 2 件も同じ)。
- `tools/eval-compare.sh` (base = main の `git archive`、cases = `eval/cases`): 合計 386/488 → 427/488、**broken 0 / fixed 41 / added 0 / removed 0**。`short-words-in-english.tsv` は 108/178 → 149/178 (英語側 40/108 → 81/108、日本語対照 68/70 のまま)。記号つき 4 件 (後から追加) を入れた最終の表は 149/182。
- 実機 (NSSpellChecker) の判定は未確認。この測定は同梱の英単語表 (`BuiltInWordChecker`) で、テスト用の変換エンジン。

## 7. 実機で確かめる手順 (未実施)

TextEdit で (設定の変更は不要):
1. `What` Space `to` Space `do` Enter → `What to do` (途中で `と` `ど` が見えても最後に直る)。`Time to go`・`Ready to go` も。
2. `Where` Space `to` Enter → `Where to`。
3. `Welcome` Space `to` Space `Tokyo` Enter → `Welcome to Tokyo`。
4. 対照 (変わらないこと): `Slack` Space `to` Enter → `Slack と`。`Google to Apple` → `Google とApple`。`kyou to asobu` → `今日とあそぶ`。
5. 記号つき (`What to do?`) は**直らない**のが今の仕様。
6. 直った語を BackSpace → F10/F6 で今までどおり直せること。

## 8. 査読 (P1/P2) を受けた測り直しと、一覧の絞り込み (W12-2、2026-10-24)

独立査読の指摘: 最初の版 (a2+b+c) は、一覧の語 (free・good・it・and・not・thing・place・way・up・hard・about・get・Happy …) を前に置いた日本語を壊す。表の日本語対照は前の語が一覧に無い Slack/Zoom/Notion だけだったので、§3 の「日本語対照で broken 0」は**一覧に無い名詞の範囲の話にすぎなかった**と訂正する。

### 8.1 表への追加 (182 → 293 件。英語 142・日本語 151)
- 日本語対照: 一覧の語 + `to do/go/be` + 日本語 24 件 (`free to do ko ni iku` → `free とどこにいく` など)、一覧の語 + 文末の `to` 24 件、一覧の語 + `to` + 大文字始まり 25 件、`What to do` + 助詞 7 件、P3 の記録 1 件 (`Trip to Tokyo ha`、変更前から外れ)。
- 英語: 前の語が一覧に無い英文 30 件 (`Ways to go`・`Eager to go`・`Likely to be`・`Moving to Berlin` …)。変更前 0/30 → 変更後 0/30。一覧の外には効かない (一覧の偏りによる楽観を弱めるための測定)。
- `time`・`nice`・`me`・`you` を前に置く日本語は、変更と関係なく変更前から別の読み (`ちめ`・`にせ`・`め`・`よう`) になるので日本語対照に入れていない。

### 8.2 候補ごとの fixed / broken (同じ表 293 件、一時スイッチで測定。変更前 188/293)

fixed = 変更前で外れ候補で当たる。broken = 変更前で当たり候補で外れる (すべて日本語対照)。

| 候補 | 内容 | fixed | broken |
| --- | --- | --- | --- |
| 最初の版 (a2+b+c) | 広い一覧 (名詞・代名詞・接続詞・副詞を含む約 100 語) | 41 | **70** |
| 広い一覧 a+c | (b) をやめる | 22 | 40 |
| 広い一覧 b+c | (a) をやめる | 30 | 47 |
| 広い一覧 + 取り消し | (b) の後ろが日本語なら戻す | 41 | 44 |
| 狭い一覧 a+b+c | 疑問詞・to 不定詞を取る動詞・前置詞だけ | 21 | 11 |
| 狭い一覧 a+c | (b) をやめる | 9 | 2 |
| 狭い一覧 a+c、(a) から what/try を除く | (b) をやめる | 9 | 0 |
| **採用: 狭い一覧 a+b+c + (b) の取り消し + (a) から what/try を除く** | | **21** | **0** |
| (b) だけ広い一覧 + 同上 | (b) に not/about/plan/something などを戻す | 28 | 4 (`not to do u zo` など。次の語が `u` で戻せない) |

- (iii) (b) をやめる = 狭い一覧 a+c (what/try 除く) は fixed 9 / broken 0。採用案より fixed が 12 少ない。
- (ii) 「書き換えを遅らせる」: `do` を確定する時点で後ろは分からない。確定を保留するのは入力の流れ全体の変更になるので**見送り**。代わりに「いったん英語にして、次の語が日本語と分かったら戻す」を足した。広い一覧のままでは (a)(c) が名詞を拾うので broken 44 までしか減らず、一覧の絞り込みと両方が要る。

### 8.3 採用した内容 (CompositionController.cs)
- `WordsBeforeToInEnglish` を約 40 語に絞る。疑問詞 (what/where/how/who/whom/which/when/why)、to 不定詞を取る動詞・形容詞 (going/gonna/want 系/need 系/have 系/used/able/unable/ready/supposed/ought/try 系/hoping/planning/got/glad/sorry)、前置詞 (due/according/thanks/forward)、listening/talking。名詞・代名詞・接続詞・副詞・一般の形容詞は入れない (コードのコメントと一致させた)。
- (a) 文末の to: 同じ一覧から what / try を除く。日本語対照の表に `What と`・`try と` を置いたため (実際の頻度は低いが、表で broken 0 を優先した)。`What to do` は (b) が直す。
- (b) の取り消し: `to do `・`to go `・`to be ` (語尾が空白つき) に書き換えた直後の確定が日本語の語・助詞なら `とど`・`とご`・`とべ` に戻す。自分の確定の記録とキャレット前の文字が一致するときだけ。間に Enter・Tab・Space・Backspace を通した、確定し直しの記録を捨てた (`ForgetLastCommit` など)、のどれでも取り消しの記録も捨てる (古い記録のまま戻すと、間に打った文字を消すため。`What tとどこにいく` が出た)。Enter で確定した `to do` (空白なし) は戻さない。ホストがキャレット前の文字を返さない (`_precedingText` が null) ときは**何もしない**。
- (c) 大文字始まりの前の to: 前の語を welcome / thanks / due / according / going だけに。Up・Back・Next・Trip・Way・Train・Flight などは外した。
- 英語側の直る数は 41 → 21 件。外れたままの型: `Time to go`・`Nothing to do`・`Good to go`・`Place to go`・`Back to`・`Up to`・`Trip to London`・`Flight to Osaka`・`Back to Tokyo`・`Next to Google`。英語として正しくても、同じ並びの日本語を壊すため見送り。
- **既知の穴 (表には入れていない。入れると採用案の broken になる)**: 戻す条件は、次の語が日本語と判定される、または助詞 (ga・wo・ni・de・mo・ka・ne・wa・na・ze・zo) のとき。`yo` は英語の語でもあり、戻すのに `yo` 自体が英語で確定済みで半分だけ戻る (`what to do yo` → `what とどyo`) ので助詞から**外した** (日本語対照 broken 0 のまま)。対象は no/to/so/u/ha の 5 語だけではなく、**英語にも見える語全般**: `What to do no`・`to`・`so ka`・`u zo`・`ha` のほか sushi・dame・sensei・home など、`how to do sudo` のように英語として打った後続と、日本語として打った語を区別できないため、戻らず英語のまま (変更前は日本語だった型)。実機での頻度は不明。
- P3 (`Trip to Tokyo ha` の `ha` が `は` にならない): 変更前の main でも `ha` のまま (表の外れ、変更前後同じ)。記録のみ。

### 8.4 検証
- テスト: 1041 → **1045/1045** (`ShortWordsInEnglishTests` に 4 件追加、既存 2 件の例を新しい一覧に合わせて差し替え)。実装を HEAD に戻した状態で新しいテストは赤 (`free to do こにいく`・`free to`・`Up to Zoom`・`What to do こにいく`)、実装後に 12/12 緑。
- `--eval`: 1292/1294 (変更前と同じ)。
- `tools/eval-compare.sh` (base = main の `git archive`、cases = `eval/cases`): 466/603 → 487/603、**broken 0 / fixed 21 / added 0 / removed 0**。
- `short-words-in-english.tsv`: 188/293 → 209/293 (英語 40/142 → 61/142、日本語 148/151 のまま。日本語の外れ 3 件は変更前から)。
- 実機 (NSSpellChecker) は未確認。

### 8.5 再査読の修正 (取り消しの安全化、W12-3)
- 取り消しの記録 `_pairedRevert` を、確定し直しの記録を捨てる所すべて (`ClearCorrectable`) と、Enter・Tab・Space・Backspace をアプリに通したとき (`NoteBreakKeyPassedToApp`) で捨てる。Backspace は確定し直しの記録も捨てる。取り消しは語尾が `to do `/`to go `/`to be ` (空白つき) のときだけ。ホストがキャレット前の文字を返さなければ何もしない。助詞から `yo` を外した。
- テスト 1045 → **1047/1047** (2 件追加)。修正を外すと赤: `What to do⏎⏎ko ni iku` が `What とどこにいく` (期待 `What to doこにいく`)、`what to do yo` が `what とどyo`。
- `--eval` 1292/1294 (同じ)、`eval-compare` broken 0 / fixed 21。
- 未確認: 実機の入力欄で改行・Backspace を挟んだ場合 (試験用ホストは Enter で改行を入れないため、実機の改行入りの記録では未実測)。


## 9. 語末の記号 (W13-2)

### 9.1 問題
`What to do?`・`Where to?` のように、語末に記号がつくと (a)・(b)・(c) の判定に乗らず、`と` が残っていた。実際の英文は記号で終わることが多いため、実用上の穴だった。

### 9.2 実装
- `CompositionController.WordBeforeTrailingPunctuation` を追加し、語末の句読点・閉じ記号（半角・全角）を判定から外す。記号そのものは確定文字列に残す。
- (a) 文末の `to`、(b) `to` + `do`/`go`/`be`、(c) 大文字始まりの固有名詞の前の `to` の各判定で、記号を除いた語を使う。
- 取り消し (`_pairedRevert`) は、空白つきの語尾 (`to do ` など) という条件を保ったまま、語と空白の間の記号を許す。Enter 確定 (空白なし) は従来どおり戻さない。
- `do-`・`do/path` のような語は記号扱いにしない (ハイフンとスラッシュは除外しない)。

### 9.3 検証
- 新しい正解表を `eval/cases/short-words-in-english.tsv` に 25 件追加 (英語 12・日本語対照 13)。
- Core 全テスト **1072/1072** (1068 + 4)。記号を除く処理を無効にすると、記号つきの 3 テストが赤になることを確認。
- `--eval`: **1292/1294** (変更前と同じ)。
- `tools/eval-compare.sh` (base = main `78b9af5`): 500/626 → 513/626、**broken 0 / fixed 13 / added 0 / removed 0**。
- 日本語の疑問形 (`masaka?`・`hontou?`・`nani?`) と、記号つきの組の後ろに日本語が続く例 (`what to do? ko ni iku`) を対照に入れ、日本語側の broken は 0。
- 実機 (NSSpellChecker) は未確認。
