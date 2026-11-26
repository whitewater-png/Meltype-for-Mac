---
title: 設定「変換後も続けて入力できる」ON で、ローマ字の再変換の文を学習してしまう (固定した文節の確定)
status: approved   # 2026-11-22 の人の指示「OK、両方おすすめで」(人の判断 1〜4 は、その前の指示「おすすめでお願い」で決定。案 A)。実装は要件 027 のあと (027 の人の判断 1) / 2026-11-25 実装 (fix/continue-after-conversion-learning)。実機は未確認 (done にするのは実機のあと)
source: 要件 026 (旧 018。ライブ変換の確定の学習。PR #29、ブランチ ccr-0fc8aeab-sra4z7 の docs/requirements/026-live-conversion-learning.md) の記録「範囲外で見つけたこと」(2026-11-22)
scope: core
---

# 028 設定「変換後も続けて入力できる」ON で、ローマ字の再変換の文を学習してしまう

## 背景 (誰が、何に困っているか)
- 利用者の声からではなく、2026-11-22 に要件 026 (旧 018。PR #29) の作業中に見つけた (026 の記録「範囲外で見つけたこと」: 偽の変換エンジンで再現、base のコードでも同じ。026 の記録の base は d210044)。026 より前からあり、026 では直していない。
- ローマ字のまま入った文を選んで再変換 (Mac: 選んで Shift+Space。README の「確定後の再変換」) したものは、打ち直した文ではないので学習しない決まり (`CompositionController.Commit` のコメント、`ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`)。
- ところが設定「変換後も続けて入力できる」(`ContinueAfterConversion`、既定 OFF。以下「設定」) が ON だと、再変換 → Space (選んだままの文を選ぶ) → 続けて打つ → Enter で、再変換の文が学習に入る。偽の変換エンジンでは「きょうはいいてんきですね → kyouha iitenki desu ne」の組が、変換エンジン (実機では azooKey) への学習・選び直しの記録・登録提案に入り、次に同じ文を打つと、Space の最初の候補・ライブ表示・予測候補にローマ字の文が出て、同じ文をもう一度選んで再変換すると最初の候補がローマ字の文になる (下の I1〜I4)。
- 設定 OFF では、変換エンジン・選び直しの記録・登録提案には何も入れない。ただし日英の学習には、設定に関係ない別の漏れがある (下の「範囲外で見つけたこと」)。
- 今の挙動: 下の「再現の結果」(Core の使い捨てのテスト。実機は未確認)。

## 今の動き (既に分かっていること)

### 再現の結果 (2026-11-22、Core の使い捨てのテスト。コミットしていない)
- やり方: 写しの木 (`git worktree add --detach`) に使い捨てのテストを足して流した。入口は `MeltypeSession.Reconvert` (Mac の Shift+Space が呼ぶ) と `HandleKey`・`CommitPending`、設定は `CompositionOptions.ContinueAfterConversion`。変換エンジンは偽物 (`CompositionTests.FakeConverter`) に、学習に渡った組 (読み=語) を記録する包みを付けた (`ReconvertRomajiTests` の `RecordingConverter` と同じ形)。選び直しの記録は `ConversionHistory.Entries()`、登録提案は `DictionarySuggestions.Pending(1)`、日英の学習は `LanguageMemory.Entries()` で見た。R7・R8 は PR #29 の `LiveEnterLearningTests` と同じく `CompositionTests.Keyboard` と `Controller.ReconvertRomaji` で流した。
- 流した木: 下の表の行は全部、main 5544133・PR #29 の先頭 d5bbd0f・d210044 (026 の前) の 3 つで流し、結果は同じ。査読員も別に書いたテストで、表の行を全部 3 つの木に流し、同じ結果だった (査読 1〜4 周目。査読員の報告による)。
- 選んだ文は `kyouha iitenki desu ne` (R9 だけ `kyouha`)。偽の変換エンジンは日本語にした文を `きょうはいいてんきですね` (かなのまま) にする。続けて打つのは `yo` (よ)。「ON / OFF」は設定。

| # | 手順 | 確定 | 変換エンジンへの学習 | 選び直しの記録 | 登録提案 |
|---|---|---|---|---|---|
| R1 | ON。再変換 → Space (選んだままの文) → `yo` → Enter (報告の手順) | `kyouha iitenki desu neよ` | `きょうはいいてんきですね=kyouha iitenki desu ne` | 同じ組 | 同じ組 (1 回) |
| R2 | ON。再変換 → (Space なし) → `yo` → Enter | `きょうはいいてんきですねよ` | `きょうはいいてんきですね=きょうはいいてんきですね` | なし | なし |
| R3 | ON。再変換 → Space 2 回 (日本語にした文に戻る) → `yo` → Enter | `きょうはいいてんきですねよ` | R2 と同じ | なし | なし |
| R4 | ON。再変換 → Space → `hello` → Space (英語だけで終わる Space) | `kyouha iitenki desu nehello ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R5 | ON。再変換 → Space → `kyouhagoogle` → Space (日本語 + 英語で終わる Space) | `kyouha iitenki desu neきょうはgoogle ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R6 | ON。再変換 → Space → `yo` → 暗黙の確定 (`CommitPending`。フォーカスが外れたときなど) | `kyouha iitenki desu neよ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R7 | ON・ライブ変換 ON。再変換 → (Space なし) → `tanniwotoru` (ライブ表示 単位を取る) → Enter (PR #29 の `RomajiReconversion_ContinuedLiveEnter_DoesNotCount` と同じ手順) | `きょうはいいてんきですね単位を取る` | R2 と同じ | 増えない | なし |
| R8 | ON。再変換 → Space → `hashiwo` → Space で変換 → 箸を を選ぶ → Enter (変換中のまま確定) | `kyouha iitenki desu ne箸を` | なし | なし | なし |
| R9 | ON。`kyouha` を再変換 (候補 今日は。読みに漢字が入る形) → Space → `yo` → Enter | `kyouhaよ` | `今日は=kyouha` | `今日は=kyouha` | なし |
| R10 | ON。再変換 → Space → `yo` → Shift+Enter (設定「Shift+Enter で確定して改行」ON) | `kyouha iitenki desu neよ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R11 | ON。再変換 → Space → `yo` → Tab (暗黙の確定) | `kyouha iitenki desu neよ` | R1 と同じ | R1 と同じ | R1 と同じ |
| S1 | ON。再変換 → Space → `hashiwo` → Space で変換 (橋を) → さらに `yo` → Enter | `kyouha iitenki desu ne橋をよ` | R1 の組と `はしを=橋を` (1 回の学習に 2 文節) | R1 の組 | R1 の組 |
| S2 | OFF。S1 と同じ手順 | S1 と同じ | `はしを=橋を` | なし | なし |
| S3 | ON。S1 で 橋を の代わりに 箸を を選ぶ | `kyouha iitenki desu ne箸をよ` | R1 の組と `はしを=箸を` (1 回の学習に 2 文節) | R1 の組・`はしを=箸を` | R1 の組・`はしを=箸を` |
| S4 | OFF。S3 と同じ手順 | S3 と同じ | `はしを=箸を` | `はしを=箸を` | `はしを=箸を` |
| C1 | OFF。R1 と同じ手順 | `kyouha iitenki desu neよ` | なし | なし | なし |
| C2 | OFF。R2 と同じ手順 | `きょうはいいてんきですねよ` | なし | なし | なし |

- S1・S3 の最後を `kyouhagoogle` + Space (`TakeFixedClauses` の経路) にしても、学習は S1・S3 と同じ (確定は `kyouha iitenki desu ne橋をきょうはgoogle ` など。S3 の形は査読員の測定)。

影響 (I7 のほかは偽の変換エンジン。I1・I2 と I4〜I6 の文の形は、偽の変換エンジンが知らない読みを文節に分けず、文全体を 1 文節にするから出る。I7 は、文を文節に分けるテスト用の変換エンジンで測った。実機は下の「実機 (azooKey) での出方」):

| # | 手順 | 結果 |
|---|---|---|
| I0 | (対照) 学習なしで `kyouhaiitenkidesune` → Space | 最初の候補 `きょうはいいてんきですね` |
| I1 | R1 のあとの選び直しの記録で `kyouhaiitenkidesune` → Space | 最初の候補 `kyouha iitenki desu ne` |
| I2 | R1 のあとの選び直しの記録で、ライブ変換 ON で `kyouhaiitenkidesune` → Enter | ライブ表示も確定も `kyouha iitenki desu ne` (学習なしの対照は `きょうはいいてんきですね`) |
| I3 | R1 のあとの選び直しの記録で `kyouhai` まで打つ | 予測候補に `kyouha iitenki desu ne` (学習なしの対照は予測候補なし) |
| I4 | R1 のあと、同じ文をもう一度選んで再変換 (`arigatou` を R1 と同じ手順で誤学習させたあとの `arigatou` も同じ形) | ローマ字の再変換を通らない (日本語にした結果が選んだ文字と同じになり、727 行で止まる)。記録の読みに戻す今までの再変換 (MeltypeSession.cs 367 行の `ReadingOf` → `ReconvertKana`) になり、候補は `kyouha iitenki desu ne`・`きょうはいいてんきですね`・カタカナ・半角カナの 4 つで、1 番目がローマ字 (学習なしなら `きょうはいいてんきですね`・選んだままの文の 2 つ)。続けて Space → `yo` → Enter と打つと、確定は `きょうはいいてんきですねよ` |
| I5 | 誤学習のあと、ふつうに打って Space → Enter | 1 語 (`ありがとう=arigatou` を覚えたあとの `arigatou`): 表示も確定も `arigatou`。登録提案は、確定した語が打った英字と同じなので数えない (3859 行の `c.Text != c.Raw`)。文 (`kyouhaiitenkidesune`): 表示も確定も `kyouha iitenki desu ne` で、確定ごとに登録提案を数え、R1 の 1 回と合わせて 3 回で提案待ちになる |
| I6 | 誤学習のあと、同じ語・文をもう一度選んで再変換し、最初の候補 (ローマ字) のまま Enter (または `yo` → Enter) を 2 回 | `arigatou`・文とも、1 回ごとに登録提案を数え、R1 の 1 回と合わせて 3 回で提案待ちになる。記録の読みに戻す今までの再変換の文節には打った英字が無いので 3859 行の除外に当たらず、3862 行の「記録と同じ語」で数える (I4 のように Space で日本語の候補を選ぶと数えない) |
| I7 | 文節に分けるテスト用の変換エンジン (`きょうはいいてんきですね` をひらがなの 3 文節で返す。azooKey が文を文節に分ける形の代わり) で、R1 の手順を 3 回 | 毎回ローマ字の再変換が開き (候補は `きょうはいいてんきですね`・`kyouha iitenki desu ne`)、登録提案を 1 回ずつ数えて 3 回で提案待ちになる。そのあと `kyouhaiitenkidesune` の Space の最初の候補とライブ表示は `きょうはいいてんきですね` のまま (記録は文節ごとに引くので当たらない)。`kyouhai` の予測候補には `kyouha iitenki desu ne` が出る |

変えてはいけない動き (再変換でない普通の変換。今も学習する):

| # | 手順 | 結果 |
|---|---|---|
| N1 | ON。`hashiwo` → Space で 箸を を選ぶ → `yo` → Enter | `箸をよ`、選び直しの記録 `はしを=箸を` |
| N2 | N1 と同じく 箸を を選ぶ → `google` → Space | `箸をgoogle `、`はしを=箸を` |
| N3 | N1 と同じく 箸を を選ぶ → `kyouhagoogle` → Space (`TakeFixedClauses` の経路) | `箸をきょうはgoogle `、`はしを=箸を` |

### 範囲外で見つけたこと (028 では直さない。要件 027 にした)
- 日英の学習の漏れ: ローマ字の再変換の確定では、設定に関係なく `Commit` の else 側の `if (toggle is null) LearnLanguage();` (3002 行) が走る。`ReconvertRomaji` は表示を `DisplayMode.Hiragana` にする (737 行) ので、`LearnLanguage` の Hiragana の分岐 (3175・3176 行) が、選んだ文の中の英語の区間を「日本語にする」と覚える (explicitChoice)。査読で見つかり、下の表は自分のテストでも測り直した (3 つの木で同じ)。

| # | 手順 | 日英の学習 |
|---|---|---|
| L1 | OFF。`asita no meeting de review shimasu` (README の例) を再変換 → Enter | `meeting`・`review` を日本語 |
| L2 | ON。同じ文を再変換 → Enter (続けて打たない) | L1 と同じ |
| L3 | ON。同じ文を再変換 → Space → Enter (続けて打たない) | L1 と同じ |
| L4 | ON。同じ文を再変換 → Space → `yo` → Enter (続けて打つ。028 の形) | なし (日英の学習は続けて打った分だけを見る)。選び直しの記録には `あしたの meeting で review します=asita no meeting de review shimasu` が入る (028 の漏れ。読みに英字があるので変換エンジンには渡さず、登録提案にも数えない) |
| L5 | L1 のあと `kyouhameetingdesu` を打つ | 表示 `きょうはめえtingです` (学習なしの対照は `きょうはmeetingです`) |

- `ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned` は変換エンジンと選び直しの記録だけを見ていて、日英の学習は見ていない。

### コード (行番号は main 5544133。PR #29 の先頭 d5bbd0f の行は、主な所 (`Commit` と `TakeFixedClauses` の `Learn()` の呼び出し) だけ「d5bbd0f: N 行」と添える。d5bbd0f では、ほかの行も PR #29 の変更でずれている)
- `CompositionController.Commit` (2965 行。d5bbd0f: 3003 行): 変換中の分岐は `if (converting && !_reconvertRomaji) Learn();` (2998 行。d5bbd0f: 3051 行) で再変換を除くが、else 側の固定した文節の分岐 `if (fixedOnly) Learn();` (3001 行。d5bbd0f: 3054 行) は `_reconvertRomaji` を見ない。ここを通る確定は、Enter (R1〜R3・R7・R9・S1・S3)、Shift+Enter (1147〜1153 行。R10)、英語だけで終わる Space (1204〜1207 行の `Commit(suffix: " ", ...)`。R4)、暗黙の確定 (`Commit(implicitly: true)`: フォーカスが外れたときの `CommitPending` → `CommitIfAny` (R6)、Tab・Delete・(続けて打った分が英字だけのときの) 矢印など (1320 行。R11)、Ctrl などの修飾キーの前 (1129・1135 行。テストは流していない))。
- `CompositionController.TakeFixedClauses` (1946 行。`Learn()` は 1951 行。d5bbd0f: 1955・1960 行): Space で英語で終わる確定のうち、続けて打った分に日本語がある形 (1211・1212 行。d5bbd0f: 1220・1221 行) から呼ばれ、`_reconvertRomaji` を見ずに `Learn()` を呼ぶ (R5)。
- 再変換を除いている所: `Commit` の変換中の分岐 (上。R8 で効いている)、`CommitThroughSelectedClause` (552 行。`_reconvertRomaji` なら `Commit()` に回す。555 行)。
- 状態の流れ: `ReconvertRomaji` (708 行) が `_reconvertRomaji = true` (733 行) にし、文節を 1 つ作る (読み = 日本語にした文、候補 = 日本語にした文・選んだままの文)。続けて打つと `EndConversionForNewInput` (1865 行) がこの文節を固定する (`_headCount` > 0、`_converting` = false)。`_reconvertRomaji` は確定 (`CommitText`) まで true のまま (R8 で変換中の分岐の守りが効くことから分かる)。
- `Learn()` (3805 行) が書く先のうち、再変換の文節に当たるのは 3 つ: 変換エンジンへの学習 (`LearnConversion` 3873 行 → `ILearningConverter.Learn`。読みに英字・数字があれば渡さない)、登録提案 (`RecordSuggestions` 3854 行)、選び直しの記録 (`history.Remember`)。Space 1 回で選んだままの文を選ぶと「選び直した」扱い (`Changed`) になり、3 つ全部に入る (R1)。Space 0・2 回は日本語にした文のままなので、変換エンジンへの学習だけ (読み = 語。R2・R3)。ほかの書き先 (日英の学習 3811・3816 行、英訳の記録 3821 行、予測用の回数 `history.Touch`) は、再変換の文節ではもともと当たらない (打った英字が無い・英語の文節でない・英訳でない・`Touch` は記録に同じ組があるときだけ増やす)。
- 登録提案は読みがひらがなだけの組しか数えない (`DictionarySuggestions.IsEligible`、98 行)。読みに漢字・英字が入る形 (R9・L4) では数えない。語に句読点・記号 (`・` は除く) があるとき (103 行) と、空白の無い 12 文字以上の英数字だけのとき (101 行) も数えない。

### 実機 (azooKey) での出方 (コードを読んだ推定。実機は未確認)
- 変換エンジン: Core は学習を呼ぶが、Swift の `MeltypeConverter.learn` (`mac/Sources/MeltypeIME/Converter.swift` 296 行〜) は、azooKey が直前にその読みで出した候補の中に同じ語がある文節だけを学習し、無ければ飛ばす (300 行)。Space 1 回の形 (語が選んだままのローマ字の文) は、ふつうは飛ばされ、azooKey には入らない見込み。Space 0・2 回の形 (読み = 語 = 日本語にした文) は、日本語にした文がひらがなだけで、azooKey がその読みで同じ語を出していれば学習される見込み (最初の候補を強めるだけで、害は小さい見込み)。
- 選び直しの記録 (Space の候補・ライブ表示): 記録は文節の読みごとに引く (2349 行の `History?.Get(clauses[i].Reading)`)。I1・I2 で効いたのは、偽の変換エンジンが文全体を 1 文節にした (2335 行) から。azooKey は文を文節に分けて返す (Converter.swift 326 行〜) ので、記録が Space の候補・ライブ表示に出るのは、azooKey がその読みを 1 文節で返すとき (短い語など) だけの見込み (文節に分けるテスト用の変換エンジンでは出なかった。I7)。
- 選び直しの記録 (予測候補): 予測候補 (既定で ON、2 文字から。177・180 行) は記録を読みの前方一致で引く (4158 行の `history.StartingWith`) ので、変換エンジンの区切りに関係なく出る (I3・I7)。実機の記録の読みは日本語にした文 (漢字まじりのことが多い見込み) なので、出るのは打った読みが記録の読みの先頭部分と一致するとき (日本語にした文がかなで始まるとき) の見込み。
- 同じ語・文の 2 回目の再変換: 記録が引かれる形 (短い語など) では、I4 のとおりローマ字の再変換を通らず、最初の候補がローマ字になる見込み。文節に分かれる長い文では記録が引かれないので、ローマ字の再変換がふつうに開く見込み (I7)。
- 登録提案: 読みがひらがなだけなら、1 回目の誤学習で 1 回数える。そのあと同じ語・文をもう一度選んで再変換し、最初の候補 (ローマ字) のまま確定すると、1 回ずつ数える (I6。Space で日本語の候補を選べば数えない。I4)。ふつうに打って学習したローマ字をそのまま確定したときは、確定した語が打った英字と同じ 1 語 (`arigatou`) なら数えず、違う文 (空白を含む) なら数える (I5)。どちらの形でも、登録提案の対象 (読みがひらがなだけで、句読点・記号などで除かれない語。上の「コード」) なら既定の 3 回に届いて、入力メニューに「『語』を辞書に登録」が出る見込み: 1 文節で返る短い語は、2 回目から I4 の形になり、選び直してローマ字のまま 2 回確定すれば 3 回 (I6)。azooKey が文節に分ける長い文は、記録が引かれないので 2 回目以降もローマ字の再変換がふつうに開き、R1 の手順を 3 回くり返せば 3 回 (I7)。
- 読みが漢字で始まる記録は、ふつうに打ったかなの読みでは引かれない見込み (目に見える害は出ないが、記録には残る)。

### 既存のテストと文書
- `ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`: 設定 OFF の Space 0〜2 回 → Enter だけ。変換エンジンと選び直しの記録を見ていて、登録提案と日英の学習は見ていない。続けて入力の経路は通らない。
- PR #29 の `LiveEnterLearningTests.RomajiReconversion_ContinuedLiveEnter_DoesNotCount`: R7 と同じ手順だが、続けて打った 単位を を数えない・学習しないことだけを見ていて、再変換の文が変換エンジンに渡ること (R7) は見ていない。
- 普通の変換 + 続けて入力の学習を守るテストはある: `ContinueAfterConversionTests.On_EnterWithoutConvertingTail_LearnsFixedClauses` (Enter と英語だけで終わる Space)・`On_EnterCommitsEverything_AndLearnsAllClauses` (変換中の分岐)・PR #29 の `LiveEnterLearningTests.ContinueAfterConversion_CountsFixedClausesAndLiveRestOnceEach`。日本語 + 英語で終わる Space (`TakeFixedClauses` の経路。N3) の学習を守るテストは無い (1951 行の `Learn()` を外しても、main の Core のテストは 1266/1266 のまま。2026-11-22 に写しの木で確かめた)。
- 文書: README・docs/USAGE.md・docs/KNOWN_LIMITS.md に、再変換と続けて入力の学習の記述は無い (「再変換」「続けて入力」で探した)。
- 本家 (yksr-melt/Meltype) の扱い: ローマ字の再変換は本家 #332 の改良導入 (docs/IMPROVEMENT_PLAN.md の P36)、設定「変換後も続けて入力できる」は IMPROVEMENT_PLAN の追加項目 P17 (本家に同じ設定があるかは未確認)。本家で同じ形が起きるかは未確認 (クラウドから本家に届かない)。
- 経緯: このクローンは浅く (2026-10-28 の 935583d より前の履歴が無い)、`!_reconvertRomaji` の守りを足した時期は調べていない。

## minimum の要件 (これだけは満たす)
1. ローマ字の再変換で始めた入力は、設定 ON で文節を固定したあとの確定でも、再変換の文を学習しない: 変換エンジンへの学習・選び直しの記録・登録提案のどれにも入れない (設定 OFF の C1・C2 と同じ)。対象の確定は Enter (R1〜R3・R7・R9)、Shift+Enter (R10)、英語で終わる Space の 2 つの経路 (R4・R5)、暗黙の確定 (R6・R11。修飾キーの前を含む)。
2. 再変換のあとに続けて変換した文節 (S1・S3) も学習しない (人の判断 2 で案 A に決定: 再変換を含む確定は、まるごと学習しない)。
3. 再変換でない入力の学習は変えない: 普通の変換 + 続けて入力で固定した文節の学習 (N1〜N3)、変換中の分岐の学習、PR #29 (要件 026) が入っていればライブ表示の数え方 (固定した文節の後ろに続けて打った分を含む)。続けて打った経路の日英の学習 (続けて打った分だけを見る。L4) も今のまま。

## 非目標 (今回はやらない)
- R8 (再変換のあとに続けて打って Space で変換し、変換中のまま確定) を学習するようにすること。今は `Commit` の変換中の分岐が、再変換を含む確定をまるごと学習しない。変えるかは別に決める (別の要件の候補)。
- 範囲外で見つけたこと (日英の学習の漏れ) を直すこと (人の判断 4 で別の要件に決定。要件 027)。
- すでに入ってしまった組を探して消すこと (入力メニューの「学習データをすべて消去…」はあるが、ほかの学習も消える。登録提案だけなら「辞書の登録提案の履歴を消去」で消せる)。
- 設定 OFF の動き・ローマ字の再変換の候補の出し方・要件 026 の数え方を変えること。
- 学習データのファイル形式の変更。
- 要件の番号の重なり (人の判断 3) の片付け。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト (赤 → 緑。名前は仮のまま採用): `src/Meltype.Core.Tests/ReconvertRomajiTests.cs` に `Reconvert_RomajiSentence_ContinueAfterConversion_IsNotLearned` を足した (実装前は R1〜R6・R9〜R11・S1・S3 が全部赤、R2・R3 は変換エンジンへの学習だけ入る。C1・C2 は 0)。要点: 設定 ON の `MeltypeSession` で `Reconvert("kyouha iitenki desu ne")` から、R1〜R6・R10・R11 の各経路 (Space 0・1・2 回 → `yo` → Enter、`hello` + Space、`kyouhagoogle` + Space、`yo` → `CommitPending()`、`yo` → Shift+Enter、`yo` → Tab) と R9 (`kyouha`) を流し、変換エンジンへの学習 0 回・選び直しの記録 0 件・登録提案 0 件。**実装前は赤** (表のとおり入る)、実装後は緑。
- [x] テスト (赤 → 緑): R7 の形 (`LiveEnterLearningTests.RomajiReconversion_ContinuedLiveEnter_TeachesNothingToTheConverter`。既存の `RomajiReconversion_ContinuedLiveEnter_DoesNotCount` は、026 のテストなので 028 の確かめを別テストに分けた) (ライブ変換 ON・`CompositionTests.Keyboard` と `Controller.ReconvertRomaji`) を main のテストにも足し、変換エンジンへの学習が空になることを見る (PR #29 のマージの順に関係なく測れる)。実装前は赤 (今は `きょうはいいてんきですね=きょうはいいてんきですね` が渡る)。
- [x] テスト (赤 → 緑。上のテストの S1・S3 の行): S1・S3 (再変換のあとに続けて変換した文節) で、変換エンジンへの学習・選び直しの記録・登録提案がどれも 0 (人の判断 2 の案 A)。実装前は赤 (表のとおり入る)。設定 OFF の S2・S4 は今のまま (緑のまま)。
- [x] 待ち方 (対照の `hashiwo` Space Enter の学習が届くまで 10 ms ずつ最大 2 秒待つ): 「渡していない」を確かめるテストなので、最後に必ず学習する確定 (対照) を流し、その学習が届くまで待ってから数える (PR #29 の `LiveEnterLearningTests` の `Taught()` と同じ考え。固定の `Thread.Sleep` だけにしない)。
- [x] 守りが効いていること (2026-11-25 実測。`Commit` の守りを外すと上の 2 テストが落ち、`TakeFixedClauses` の守りを外すと R4・R5 の行で `ReconvertRomajiTests` の 1 テストが落ちる): 足した守りの条件を外す (直す所が 2 か所なら 1 か所ずつ) と、上のテストが落ちることを一度確かめて PR に書く。
- [x] 守りすぎていないこと (C1・C2 は 0 のまま。既存の 2 テストは緑。N3 は `ContinueAfterConversionTests.On_JapaneseThenEnglishSpace_LearnsFixedClauses`。`TakeFixedClauses` の `Learn()` を外すと落ちることを確かめた。N1・N2 は既存の `On_EnterWithoutConvertingTail_LearnsFixedClauses` が守る): 設定 OFF の同じ手順 (C1・C2) は 0 のまま。既存の `ContinueAfterConversionTests.On_EnterWithoutConvertingTail_LearnsFixedClauses`・`On_EnterCommitsEverything_AndLearnsAllClauses` が通ったまま。N3 (`hashiwo` → 箸を → `kyouhagoogle` + Space で `はしを=箸を` を覚える) のテストを `ContinueAfterConversionTests.cs` に足す (今も緑。今は守るテストが無いので、1951 行の `Learn()` を外すと落ちることを一度確かめる)。
- [x] 要件 026 と一緒のとき (026 の `LiveEnterLearningTests` は main に入っていて全件緑。`ContinueAfterConversion_CountsFixedClausesAndLiveRestOnceEach` も緑): PR #29 が先に main に入っていれば、`LiveEnterLearningTests` が全件通ったまま (特に `ContinueAfterConversion_CountsFixedClausesAndLiveRestOnceEach`)。`RomajiReconversion_ContinuedLiveEnter_DoesNotCount` に「変換エンジンへの学習が空 (`Taught()` が空)」の確かめを足し、実装前は赤 (R7) になることを確かめる。PR #29 がまだ入っていなければ、この項目は「対象外 (026 のテストが無い)」と書く (PR #29 の側でどうするかは人が決める)。
- [x] 回帰なし (2026-11-25: Core 1348/1348 → 1351/1351 (+3 テスト)。`eval-compare` base bc1b603: broken 0 / fixed 0 / added 0 / removed 0。判定 (英語・日本語の振り分け) は変えていないので boundary-sweep は流していない。Swift は変えていないので swift build も不要): Core のテストが全件通る (base の数字: main 5544133 で 1266/1266。スキップ 6 件は、root の環境で権限を使うテスト 5 件と、ファイルシステムの作成時刻の条件のテスト 1 件。PR #29 の先頭 d5bbd0f は 026 の記録で 1290/1290)。`tools/eval-compare.sh` の broken 0 (判定は変えないので fixed・added・removed も 0 の見込み)。品質テストの基準 (全体 95 %・各カテゴリー 80 %) を満たしたまま。
- [x] 文書: 直す所は無い見込み (上の「既存のテストと文書」)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
実機で試す手順は、下の各行が「打つ > 期待」。学習の有無は、次に同じ語を打ったときの Space の最初の候補・予測候補・もう一度の再変換の候補で目で見る。まだ実機では試していない (2026-11-25)。
前提: 入力メニューの「設定・辞書…」→「設定」タブの「変換後も続けて入力できる」を ON。TextEdit で、日本語入力 (「あ」) にして試す。直っていない版で試すと、選び直しの記録と登録提案に誤学習が入る (入力メニューの「学習データをすべて消去…」で消せるが、ほかの学習も消える。登録提案だけなら「辞書の登録提案の履歴を消去」)。長い文は azooKey が文節に分けるので、Space の候補・ライブ表示では直ったかを見分けにくい見込み (推定)。1 文節で済む短い語で試す。直す前の版と比べるときは、1 回ごとに学習データを消すか、別の語で試す (同じ語で続けると、2 回目からはローマ字の再変換を通らない。I4)。
- [ ] 1 文節で済む短いローマ字の語を選ぶ (例: `arigatou`)。選んで Shift+Space で、候補が「日本語にした語 (ひらがなだけ。例: ありがとう)」と「選んだままの `arigatou`」の 2 つになることを確かめる (ならなければ別の語で。どうなるかは azooKey 次第で未確認)。Space (選んだままの `arigatou`) → 続けて `yo` → Enter。確定は `arigatouよ`。
- [ ] そのあと `ariga` まで打ったときの予測候補に `arigatou` が出ない。`arigatou` を打ったときのライブ表示も、Space の最初の候補も、`arigatou` (ローマ字) にならない (直す前はなる見込み。I1〜I3・I5 の形。推定)。見るだけで、確定せずに Esc (Space で変換したあとは 2 回) で取り消す (確定すると記録が変わり、次の確かめが崩れることがある)。
- [ ] 同じ `arigatou` をもう一度選んで Shift+Space で、候補が 1 つ目と同じ 2 つのまま (直す前は、最初の候補が `arigatou` で、カタカナなどが並ぶ見込み。I4 の形。推定)。
- [ ] 1 つ目の手順の最後を、`hello` Space・`kyouhagoogle` Space・Shift+Enter・Tab・別の欄をクリック (暗黙の確定) に替えても、2 つ目・3 つ目と同じ。
- [ ] 長い文でも試す: ふつうに打って Space で変換すると 2 つ以上の文節に分かれ、Shift+Space の最初の候補がひらがなだけになり、句読点・記号を含まないローマ字の文を選ぶ (例: `ohayou gozaimasu`。そうなるかは azooKey 次第で未確認。句読点・記号があると、直す前でも登録提案に数えない)。1 つ目の手順を 3 回しても、入力メニューに登録の提案が出ない。そのあと `ohayou` まで打ったときの予測候補に、選んだ文 (ローマ字) が出ない (直す前はどちらも出る見込み。I3・I7 の形。推定)。
- [ ] 設定 OFF で同じ手順: 今までどおり、ローマ字の語が出てこない。
- [ ] 普通の変換の続けて入力: `hashiwo` Space で 1 番目でない候補 (例: 箸を) を選ぶ → 続けて `tanni` → Enter。次に `hashiwo` Space で、選んだ候補が 1 番目に出る (今も覚える。変えていないことの確認)。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 学習データ (選び直しの記録・登録提案・回数) のファイル形式を変える必要が出たとき。
- Swift (`mac/`) の変更が要ると分かったとき (学習するかの判断は Core の `Commit`・`TakeFixedClauses` にあるので、Core だけで直る見込み)。
- 直し方が、再変換でない確定の学習 (N1〜N3・変換中の分岐・要件 026 の数え方) まで変えないと成り立たないとき。
- R8 (変換中のまま確定) も学習させたくなったとき、または日英の学習の漏れ (範囲外で見つけたこと。要件 027) も一緒に直したくなったとき (どちらも非目標。人に相談する)。

## 人の判断 (2026-11-22 に決定。指示「おすすめでお願い」で、1・2・4 は推奨どおり。3 は推奨を書いていなかったので、今の形 (025 のまま) にした)
1. 範囲: 報告の Enter (R1〜R3) に加えて、Shift+Enter (R10)・英語で終わる Space の 2 つの経路 (R4・R5)・暗黙の確定 (R6・R11) も今回直すか。推奨: 入れる。どれも「ローマ字の再変換は学習しない」の同じ漏れで、R5 は別の呼び出し (`TakeFixedClauses`) なので、`Commit` の分岐だけ直すと残る。→ **決定**: 入れる。
2. 再変換のあとに続けて変換した文節 (S1・S3) をどうするか。
   - 案 A (推奨): 再変換を含む確定は、まるごと学習しない。得: R8 (変換中のまま確定) と同じ規則にそろい、直し方が単純。損: 続けて変換して選び直した語 (S3 の 箸を) を覚えない (設定 OFF の S4 なら覚える)。
   - 案 B: 再変換の文節だけを学習から外し、続けて変換した文節は学習する。得: 設定 OFF (S2・S4) と同じ結果。損: 文節ごとに「再変換の文節か」を持たせる変更が要る。R8 は何も学習しないままなので、続けて打ったかどうかで食い違う (そろえるなら R8 も変える = 非目標の見直し)。
   - → **決定**: 案 A。
3. 番号: 025 にした。最初は、main の系列 (023 まで。015 はブランチ req/015-ci-english、017 はブランチ req/017-reto-ga が使っている) と PR #29 の系列 (015〜018) の両方で空いている最初の番号の 024 で push した (d6f8227、2026-11-22 11:15:39 UTC)。約 2 分後に、別のセッションのブランチ claude/keen-noether-kicstk が別の要件 (`024-surrogate-kanji-clause.md`、deb0ff4、11:17:26 UTC) に 024 を使ったので、`/improve` の番号がぶつからないよう、こちらを 025 に付け直した (向こうのブランチには触っていない。どちらに 024 を残すかは人が決めてよい)。なお、015〜018 は main の系列と PR #29 の系列で同じ番号が別の要件になっている (main の 016・018 と PR #29 の 016・018 など)。PR #29 を今の main に入れると docs/requirements/README.md と docs/LESSONS.md が衝突する (2026-11-22 に `git merge-tree` で確かめた。この要件を足す前から。足したあとも衝突するファイルは増えないが、README の衝突の塊に 025 の行も入る)。claude/keen-noether-kicstk (PR #30) とは一覧の同じ場所に行を足していて README が衝突したが、PR #30 が main に入ったあと、このブランチに main を合わせて、一覧を 024・025・026 の順に並べた (2026-11-22)。どちらの番号を付け直すか、一覧をどうまとめるかは人が決める。→ **決定**: 025 のまま (claude/keen-noether-kicstk の 024 には触らない。向こうの 024 は 2026-11-22 に approved になり、PR #30 で main に入った)。 → 2026-11-23 に、PR #29 が自分の 016・018 を 025・026 に付け直し、PR #31 も 025 を使ったので、人の指示でこちらを 028 に付け直した (このブランチの 026 (日英の学習) は 027 に)。
4. 範囲外で見つけたこと (日英の学習の漏れ) を 028 に入れるか、別の要件にするか。推奨: 別の要件。既定の設定で起き、直す所 (`LearnLanguage` を呼ぶ条件) も 028 と違うので、分けたほうが完成条件を判定しやすい。→ **決定**: 別の要件 (要件 027。`docs/requirements/027-reconvert-romaji-language-learning.md`)。
- 実装の順番 (要件 027 の人の判断 1 で、2026-11-22 に決定): 027 を先にして、028 は 027 のあとに進める (どちらも `Commit` の同じ分岐と `ReconvertRomajiTests.cs` を触るので、同時に進めない)。

## 記録 (ループが書く)
- 実装 (2026-11-25、レーン R028): 学習するかの判断は Core で、Swift は変えていない。`CompositionController.Commit` の固定した文節の分岐を `if (fixedOnly && !_reconvertRomaji) Learn();` に、`TakeFixedClauses` の `Learn()` を `if (!_reconvertRomaji) Learn();` に変えた (2 行)。得: 再変換の文が変換エンジン・選び直しの記録・登録提案に入らない (案 A なので S1・S3 も)。損: 再変換のあとに続けて変換して選び直した語 (S3 の 箸を) も覚えない (設定 OFF の S4 は覚える。人の判断 2 で受け入れ済み)。日英の学習 (`LearnLanguage`) は触っていない (要件 027)。
- 下書き: 2026-11-22。要件 026 の記録「範囲外で見つけたこと」から起こした。「今の動き」の表は、写しの木での使い捨てのテストの出力 (コミットしていない)。
- 下書きの査読 1 周目 (2026-11-22): 止める 0・直す 4・任意 12。直す 4 件 (設定 OFF の言い過ぎと日英の学習の漏れ、続けて変換した文節 (S1〜S4) の期待、実機での出方の推定とチェックリスト、設定の場所) と任意 12 件を、自分のテストで測り直してから入れた。
- 下書きの査読 2 周目 (2026-11-22): 止める 0・直す 3・任意 5。番号 024 が別のセッションのブランチと重なったので 025 に付け直し (人の判断 3)、チェックリストの「3 回で登録提案が出ない」の確かめを、2 回目の再変換の確かめ (I4) に替え、行番号の書き方をそろえた。任意 5 件も入れた。査読員の推測「ふつうに打って 3 回で登録提案が出る」は、1 語では数えないこと (I5) を測ってから書いた。
- 下書きの査読 3 周目 (2026-11-22): 止める 0・直す 1・任意 3。2 周目に書いた「同じ語をもう一度再変換しても登録提案を数え直さない」は、最初の候補 (ローマ字) のまま確定すると数えるので誤り (I6 を測って足し、実機での出方を直した)。任意 3 件 (影響の表の見出し、査読員の範囲、チェックリストの 2 つ目を Esc で取り消す) も入れた。
- 下書きの査読 4 周目 (2026-11-22): 止める 0・直す 1・任意 1。3 周目の言い回し「長い文は登録提案が出ることは少ない」は逆 (文節に分かれる長い文は、2 回目以降もローマ字の再変換が開き、R1 の手順をくり返せば 3 回で出る)。文を文節に分けるテスト用の変換エンジンで測って I7 を足し、実機での出方を直し、チェックリストに長い文の 3 回の確かめを足した。任意 1 件 (Esc は Space のあと 2 回) も入れた。
- 下書きの査読 5 周目 (2026-11-22): 止める 0・直す 0・任意 1。任意 1 件 (登録提案に数えない語の条件: 句読点・記号、空白の無い 12 文字以上の英数字だけ) も入れた。
- 下書きの査読 6 周目 (2026-11-22): 指摘なし。
- 人の判断 (2026-11-22): 指示「おすすめでお願い」で、1・2・4 を推奨どおりに、3 を今の形 (025 のまま) に決め、本文 (minimum の要件 2・非目標・完成条件・止める条件) に入れた。status は draft のまま (approved にするのは人)。
- 承認 (2026-11-22): 人の指示「OK、両方おすすめで」で status を approved にした。
- main を合わせた (2026-11-22): 要件 024 (PR #30) が main に入ったので、このブランチに origin/main (496c90e) を合わせ、一覧の衝突を 024・025・026 の順に並べて解いた。Core のコードは変わっていない (PR #30 は文書だけ) ので、base の数字 (1266/1266) はそのまま使える。
- 番号の付け直し (2026-11-23): PR #29 (自分の 016・018 を 025・026 に付け直した) と PR #31 (025) と番号が重なったので、人の指示で 025 → 028 に付け直した (日英の学習の要件は 026 → 027)。PR #29 の要件 018 (ライブ変換の確定の学習) は、PR #29 の付け直しに合わせて本文では 026 と書いた。
- 周回数 / モデル / 所要時間 / 手戻りの原因 / PR の URL
- 2026-11-26 自動打鍵 (リョウさんの依頼。人の手ではない。TextEdit。手順と結果は docs/HANDS_ON_CHECKLIST_2026-11.md の追加分 87〜93): 87〜93 は全部 OK。90 の「別の欄をクリックして確定する」形だけ未実施 (hello Space・kyouhagoogle Space・Shift+Enter・Tab は OK)。再変換の選び方は、単語 1 つは Shift+← で選ぶ必要があった (自動打鍵側の事情)。status は変えない。
