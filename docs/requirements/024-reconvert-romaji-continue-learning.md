---
title: 設定「変換後も続けて入力」ON で、ローマ字の再変換の文を学習してしまう (固定した文節の確定)
status: draft   # draft | approved | done。approved にするのは人
source: 要件 018 (ライブ変換の確定の学習。PR #29、ブランチ ccr-0fc8aeab-sra4z7 の docs/requirements/018-live-conversion-learning.md) の記録「範囲外で見つけたこと」(2026-11-22)
scope: core
---

# 024 設定「変換後も続けて入力」ON で、ローマ字の再変換の文を学習してしまう

## 背景 (誰が、何に困っているか)
- 利用者の声からではなく、2026-11-22 に要件 018 の作業中に見つけた (018 の 2 周目の実装者が偽の変換エンジンで再現し、査読員が base d210044 のコードでも同じ形だと確かめた。018 の記録「範囲外で見つけたこと」)。018 より前からあり、018 では直していない。
- ローマ字のまま入った文を選んで再変換 (Mac: 選んで Shift+Space。README の「確定後の再変換」) したものは、打ち直した文ではないので学習しない決まり (`CompositionController.Commit` のコメント、`ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`)。
- ところが設定「変換後も続けて入力できる」(`ContinueAfterConversion`、既定 OFF) が ON だと、再変換 → Space (選んだままの文を選ぶ) → 続けて打つ → Enter で、再変換の文が学習に入る。偽の変換エンジンでは「きょうはいいてんきですね → kyouha iitenki desu ne」の組が、変換エンジン (実機では azooKey) への学習・選び直しの記録・登録提案に入り、次に同じ文を打つと、Space の最初の候補もライブ表示もローマ字の文になる (下の I1・I2)。設定 OFF では何も覚えない。
- 今の挙動: 下の「再現の結果」(Core の使い捨てのテスト。実機は未確認)。

## 今の動き (既に分かっていること)

### 再現の結果 (2026-11-22、Core の使い捨てのテスト。コミットしていない)
- やり方: 写しの木 (`git worktree add --detach`) に使い捨てのテストを足して流した。入口は `MeltypeSession.Reconvert` (Mac の Shift+Space が呼ぶ) と `HandleKey`・`CommitPending`、設定は `CompositionOptions.ContinueAfterConversion`。変換エンジンは偽物 (`CompositionTests.FakeConverter`) に、学習に渡った組 (読み=語) を記録する包みを付けた (`ReconvertRomajiTests` の `RecordingConverter` と同じ形)。選び直しの記録は `ConversionHistory.Entries()`、登録提案は `DictionarySuggestions.Pending(1)` で見た。R7・R8 は PR #29 の `LiveEnterLearningTests` と同じく `CompositionTests.Keyboard` と `Controller.ReconvertRomaji` で流した。
- 流した木: R1〜R6・C1・C2・I0・I1・N1〜N3 は main 5544133・PR #29 の先頭 d5bbd0f・d210044 (018 の前) の 3 つで、結果は同じ。R7・R8 は main と d5bbd0f で同じ。R9・I2 は main だけで流した。
- 選んだ文は `kyouha iitenki desu ne` (R9 だけ `kyouha`)。偽の変換エンジンは日本語にした文を `きょうはいいてんきですね` (かなのまま) にする。続けて打つのは `yo` (よ)。「ON / OFF」は設定「変換後も続けて入力」。

| # | 手順 | 確定 | 変換エンジンへの学習 | 選び直しの記録 | 登録提案 |
|---|---|---|---|---|---|
| R1 | ON。再変換 → Space (選んだままの文) → `yo` → Enter (報告の手順) | `kyouha iitenki desu neよ` | `きょうはいいてんきですね=kyouha iitenki desu ne` | 同じ組 | 同じ組 (1 回) |
| R2 | ON。再変換 → (Space なし) → `yo` → Enter | `きょうはいいてんきですねよ` | `きょうはいいてんきですね=きょうはいいてんきですね` | なし | なし |
| R3 | ON。再変換 → Space 2 回 (日本語にした文に戻る) → `yo` → Enter | `きょうはいいてんきですねよ` | R2 と同じ | なし | なし |
| R4 | ON。再変換 → Space → `hello` → Space (英語だけで終わる Space) | `kyouha iitenki desu nehello ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R5 | ON。再変換 → Space → `kyouhagoogle` → Space (日本語 + 英語で終わる Space) | `kyouha iitenki desu neきょうはgoogle ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R6 | ON。再変換 → Space → `yo` → 暗黙の確定 (`CommitPending`。フォーカスが外れたときなど) | `kyouha iitenki desu neよ` | R1 と同じ | R1 と同じ | R1 と同じ |
| R7 | ON・ライブ変換 ON。再変換 → (Space なし) → `tanniwotoru` (ライブ表示 単位を取る) → Enter (PR #29 の `RomajiReconversion_ContinuedLiveEnter_DoesNotCount` と同じ手順) | `きょうはいいてんきですね単位を取る` | R2 と同じ | 増えない | 測っていない |
| R8 | ON。再変換 → Space → `hashiwo` → Space で変換 → 箸を を選ぶ → Enter | `kyouha iitenki desu ne箸を` | なし | なし | 測っていない |
| R9 | ON。`kyouha` を再変換 (候補 今日は。読みに漢字が入る形) → Space → `yo` → Enter | `kyouhaよ` | `今日は=kyouha` | `今日は=kyouha` | なし |
| C1 | OFF。R1 と同じ手順 | `kyouha iitenki desu neよ` | なし | なし | なし |
| C2 | OFF。R2 と同じ手順 | `きょうはいいてんきですねよ` | なし | なし | なし |

影響 (偽の変換エンジン):

| # | 手順 | 結果 |
|---|---|---|
| I0 | (対照) 学習なしで `kyouhaiitenkidesune` → Space | 最初の候補 `きょうはいいてんきですね` |
| I1 | R1 のあとの選び直しの記録で `kyouhaiitenkidesune` → Space | 最初の候補 `kyouha iitenki desu ne` |
| I2 | R1 のあとの選び直しの記録で、ライブ変換 ON で `kyouhaiitenkidesune` → Enter | ライブ表示も確定も `kyouha iitenki desu ne` (学習なしの対照は `きょうはいいてんきですね`) |

変えてはいけない動き (再変換でない普通の変換。今も学習する):

| # | 手順 | 結果 |
|---|---|---|
| N1 | ON。`hashiwo` → Space で 箸を を選ぶ → `yo` → Enter | `箸をよ`、選び直しの記録 `はしを=箸を` |
| N2 | N1 と同じく 箸を を選ぶ → `google` → Space | `箸をgoogle `、`はしを=箸を` |
| N3 | N1 と同じく 箸を を選ぶ → `kyouhagoogle` → Space (`TakeFixedClauses` の経路) | `箸をきょうはgoogle `、`はしを=箸を` |

### コード (main 5544133 の行。括弧の中は PR #29 の先頭 d5bbd0f の行)
- `CompositionController.Commit` (2965 行 (3003 行)): 変換中の分岐は `if (converting && !_reconvertRomaji) Learn();` (2998 行 (3051 行)) で再変換を除くが、else 側の固定した文節の分岐 `if (fixedOnly) Learn();` (3001 行 (3054 行)) は `_reconvertRomaji` を見ない。Enter (R1〜R3・R7・R9)、英語だけで終わる Space (1204〜1207 行の `Commit(suffix: " ", ...)`。R4)、暗黙の確定 (`CommitPending` → `CommitIfAny` → `Commit(implicitly: true)`。R6) はここを通る。
- `CompositionController.TakeFixedClauses` (1946 行 (1955 行)。`Learn()` は 1951 行 (1960 行)): Space で英語で終わる確定のうち、続けて打った分に日本語がある形 (1211・1212 行 (1220・1221 行)) から呼ばれ、`_reconvertRomaji` を見ずに `Learn()` を呼ぶ (R5)。
- 再変換を除いている所: `Commit` の変換中の分岐 (上。R8 で効いている)、`CommitThroughSelectedClause` (552 行。`_reconvertRomaji` なら `Commit()` に回す。555 行)。
- 状態の流れ: `ReconvertRomaji` (708 行) が `_reconvertRomaji = true` (733 行) にし、文節を 1 つ作る (読み = 日本語にした文、候補 = 日本語にした文・選んだままの文)。続けて打つと `EndConversionForNewInput` (1865 行) がこの文節を固定する (`_headCount` > 0、`_converting` = false)。`_reconvertRomaji` は確定 (`CommitText`) まで true のまま (R8 で変換中の分岐の守りが効くことから分かる)。
- `Learn()` (3805 行) が書く先: 変換エンジンへの学習 (`LearnConversion` 3873 行 → `ILearningConverter.Learn`)、登録提案 (`RecordSuggestions` 3854 行)、予測用の回数 (`history.Touch`)、選び直しの記録 (`history.Remember`)。Space 1 回で選んだままの文を選ぶと「選び直した」扱い (`Changed`) になり、変換エンジン・選び直しの記録・登録提案に入る (R1)。Space 0・2 回は日本語にした文のままなので、変換エンジンへの学習だけ (読み = 語。R2・R3)。
- 登録提案は読みがひらがなだけの組しか数えない (`DictionarySuggestions.IsEligible`、98 行)。読みに漢字が入る形 (R9) では数えない。

### 実機 (azooKey) での出方 (コードを読んだ推定。実機は未確認)
- Core は変換エンジンへの学習を呼ぶが、Swift の `MeltypeConverter.learn` (`mac/Sources/MeltypeIME/Converter.swift` 296 行〜) は、azooKey が直前にその読みで出した候補の中に同じ語がある文節だけを学習し、無ければ飛ばす (300 行)。再変換の文節は、読みが日本語にした文・語が選んだままのローマ字の文なので、ふつうは飛ばされ、azooKey には入らない見込み。
- 実機で日本語にした文は漢字まじりになることが多い見込みで、そのときの選び直しの記録は読みが漢字まじりの組 (R9 の形) になり、ふつうに打ったかなの読みでは引かれない見込み。日本語にした文がひらがなだけになるとき (R1 の形) は、I1・I2 のとおり次に同じ文を打ったときに効いてしまう。
- なので、目に見える害が出るのは「変換後も続けて入力」ON・ローマ字の再変換・日本語にした文がひらがなだけ、が重なったときに限られる見込み。

### 既存のテストと文書
- `ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`: 設定 OFF (既定) の Space 0〜2 回 → Enter だけを見ている。続けて入力の経路は通らない。
- PR #29 の `LiveEnterLearningTests.RomajiReconversion_ContinuedLiveEnter_DoesNotCount`: R7 と同じ手順だが、続けて打った 単位を を数えない・学習しないことだけを見ていて、再変換の文が変換エンジンに渡ること (R7) は見ていない。
- 普通の変換 + 続けて入力の学習を守るテストはある: `ContinueAfterConversionTests.On_EnterWithoutConvertingTail_LearnsFixedClauses` (Enter と英語だけで終わる Space)・`On_EnterCommitsEverything_AndLearnsAllClauses` (変換中の分岐)・PR #29 の `LiveEnterLearningTests.ContinueAfterConversion_CountsFixedClausesAndLiveRestOnceEach`。日本語 + 英語で終わる Space (`TakeFixedClauses` の経路。N3) の学習を守るテストは見つからなかった (`Continue = true` のテストを英語で終わる Space の形で探した)。
- 文書: README・docs/USAGE.md・docs/KNOWN_LIMITS.md に、再変換と続けて入力の学習の記述は無い (「再変換」「続けて入力」で探した)。
- 本家 (yksr-melt/Meltype) の扱い: ローマ字の再変換は本家 #332 の改良導入 (docs/IMPROVEMENT_PLAN.md の P36)、「変換後も続けて入力」は IMPROVEMENT_PLAN の追加項目 P17 (本家に同じ設定があるかは未確認)。本家で同じ形が起きるかは未確認 (クラウドから本家に届かない)。
- 経緯: このクローンは浅く (2026-10-28 の 935583d より前の履歴が無い)、`!_reconvertRomaji` の守りを足した時期は調べていない。

## minimum の要件 (これだけは満たす)
1. ローマ字の再変換で始めた入力は、設定「変換後も続けて入力」で文節を固定したあとの確定でも、再変換の文を学習しない。変換エンジンへの学習・選び直しの記録・予測用の回数・登録提案のどれにも入れない (設定 OFF の C1・C2 と同じ)。対象の確定は Enter (R1〜R3・R7・R9)、英語で終わる Space の 2 つの経路 (R4・R5)、暗黙の確定 (R6)。
2. 再変換でない入力の学習は変えない: 普通の変換 + 続けて入力で固定した文節の学習 (N1〜N3)、変換中の分岐の学習、PR #29 (要件 018) が入っていればライブ表示の数え方 (固定した文節の後ろに続けて打った分を含む)。日英の学習 (`LearnLanguage`) も今の動きのまま。

## 非目標 (今回はやらない)
- R8 (再変換のあとに続けて打って Space で変換し、選び直した分) を学習するようにすること。今は `Commit` の変換中の分岐が、再変換を含む確定をまるごと学習しない。変えるかは別に決める (別の要件の候補)。
- すでに入ってしまった組を探して消すこと (入力メニューの「学習データをすべて消去…」はあるが、ほかの学習も消える)。
- 設定 OFF の動き・ローマ字の再変換の候補の出し方・要件 018 の数え方を変えること。
- 学習データのファイル形式の変更。
- 要件の番号の重なり (下の「人の判断」3) の片付け。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] テスト (赤 → 緑): `src/Meltype.Core.Tests/ReconvertRomajiTests.cs` に `Reconvert_RomajiSentence_ContinueAfterConversion_IsNotLearned` (名前は仮) を足す。要点: 設定 ON の `MeltypeSession` で `Reconvert("kyouha iitenki desu ne")` から R1〜R6 の各経路 (Space 0・1・2 回 → `yo` → Enter、`hello` + Space、`kyouhagoogle` + Space、`yo` → `CommitPending()`) と R9 (`kyouha`) を流し、変換エンジンへの学習 0 回・選び直しの記録 0 件・登録提案 0 件。**実装前は赤** (R1〜R6・R9 の表のとおり入る)、実装後は緑。
- [ ] 待ち方: 「渡していない」を確かめるテストなので、最後に必ず学習する確定 (対照) を流し、その学習が届くまで待ってから数える (PR #29 の `LiveEnterLearningTests` の `Taught()` と同じ考え。固定の `Thread.Sleep` だけにしない)。
- [ ] 守りが効いていること: 足した守りの条件を外す (直す所が 2 か所なら 1 か所ずつ) と、上のテストが落ちることを一度確かめて PR に書く。
- [ ] 守りすぎていないこと: 設定 OFF の同じ手順 (C1・C2) は 0 のまま。既存の `ContinueAfterConversionTests.On_EnterWithoutConvertingTail_LearnsFixedClauses`・`On_EnterCommitsEverything_AndLearnsAllClauses` が通ったまま。N3 (`hashiwo` → 箸を → `kyouhagoogle` + Space で `はしを=箸を` を覚える) のテストを `ContinueAfterConversionTests.cs` に足す (今も緑。再変換でない確定まで学習を止める直し方では落ちることを一度確かめる)。
- [ ] 要件 018 と一緒のとき: PR #29 が先に main に入っていれば、`LiveEnterLearningTests` が全件通ったまま (特に `ContinueAfterConversion_CountsFixedClausesAndLiveRestOnceEach`)。`RomajiReconversion_ContinuedLiveEnter_DoesNotCount` に「変換エンジンへの学習が空 (`Taught()` が空)」の確かめを足し、実装前は赤 (今は `きょうはいいてんきですね` が渡る。R7) になることを確かめる。PR #29 がまだ入っていなければ、この項目は「対象外 (018 のテストが無い)」と書く (PR #29 の側でどうするかは人が決める)。
- [ ] 回帰なし: Core のテストが全件通る (base の数字: main 5544133 で 1266/1266。スキップ 6 件は root の環境で権限を使うテスト。PR #29 の先頭 d5bbd0f は 018 の記録で 1290/1290)。`tools/eval-compare.sh` の broken 0 (判定は変えないので fixed・added・removed も 0 の見込み)。品質テストの基準 (全体 95 %・各カテゴリー 80 %) を満たしたまま。
- [ ] 文書: 直す所は無い見込み (上の「既存のテストと文書」)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
前提: 入力メニューの「変換後も続けて入力できる」を ON。TextEdit で、日本語入力 (「あ」) にして試す。直っていない版で試すと選び直しの記録に誤学習が入る (入力メニューの「学習データをすべて消去…」で消せるが、ほかの学習も消える)。
- [ ] 日本語にした候補がひらがなだけになるローマ字の文を選ぶ (例: `arigatou gozaimasu`。ひらがなだけになるかは azooKey 次第で未確認なので、Shift+Space の最初の候補を見て選ぶ)。選んで Shift+Space → Space (選んだままの文) → 続けて `yo` → Enter。確定は「選んだままの文 + よ」。
- [ ] そのあと同じ文を空白なしで打つ (例: `arigatougozaimasu`)。ライブ表示も、Space の最初の候補も、ローマ字の文にならない (直す前はなる見込み。I1・I2 の形。推定)。
- [ ] 1 つ目の手順の最後を、`hello` Space・`kyouhagoogle` Space・別の欄をクリック (暗黙の確定) に替えても、そのあと 2 つ目を試すとローマ字の文が出てこない。
- [ ] 設定 OFF で同じ手順: 今までどおり、ローマ字の文が出てこない。
- [ ] 普通の変換の続けて入力: `hashiwo` Space で 1 番目でない候補 (例: 箸を) を選ぶ → 続けて `tanni` → Enter。次に `hashiwo` Space で、選んだ候補が 1 番目に出る (今も覚える。変えていないことの確認)。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 学習データ (選び直しの記録・登録提案・回数) のファイル形式を変える必要が出たとき。
- Swift (`mac/`) の変更が要ると分かったとき (学習するかの判断は Core の `Commit`・`TakeFixedClauses` にあるので、Core だけで直る見込み)。
- 直し方が、再変換でない確定の学習 (N1〜N3・変換中の分岐・要件 018 の数え方) まで変えないと成り立たないとき。
- R8 (続けて打って変換した分) も学習させたくなったとき (非目標。人に相談する)。

## 人の判断 (approved にする前に決めること)
1. 範囲: 報告の Enter (R1〜R3) に加えて、英語で終わる Space の 2 つの経路 (R4・R5) と暗黙の確定 (R6) も今回直すか。推奨: 入れる。どれも「ローマ字の再変換は学習しない」の同じ漏れで、R5 は別の呼び出し (`TakeFixedClauses`) なので、`Commit` の分岐だけ直すと残る。
2. R8 (再変換のあとに続けて打って変換し、選び直した分を学習しない) を今のままにしてよいか。推奨: 今回は変えない (非目標)。
3. 番号: 024 にした。main の系列は 023 まで (015 はブランチ req/015-ci-english、017 はブランチ req/017-reto-ga が使っている)、PR #29 の系列は 015〜018 を使っていて、両方で空いている最初の番号が 024。なお、015〜018 は 2 つの系列で同じ番号が別の要件になっている (main の 016・018 と PR #29 の 016・018 など)。PR #29 を今の main に入れると docs/requirements/README.md と docs/LESSONS.md が衝突する (2026-11-22 に `git merge-tree` で確かめた。この要件を足す前から)。どちらの番号を付け直すかは人が決める。

## 記録 (ループが書く)
- 下書き: 2026-11-22。要件 018 の記録「範囲外で見つけたこと」から起こした。「今の動き」の表は、写しの木での使い捨てのテストの出力 (コミットしていない)。
- 周回数 / モデル / 所要時間 / 手戻りの原因 / PR の URL
