---
title: ローマ字の再変換の確定で、選んだ文の中の英単語を「日本語」と覚えてしまう (日英の学習)
status: approved   # 2026-11-23 の人の指示「承認で進めて」(人の判断 1 は 2026-11-22 に決定: 028 より先に進める) / 棚卸し 2026-11-25: 実機未確認
source: 要件 028 (旧 025) の下書きの査読で見つけた (docs/requirements/028-reconvert-romaji-continue-learning.md の「範囲外で見つけたこと」、2026-11-22。028 の人の判断 4 で別の要件に決定)
scope: core
---

# 027 ローマ字の再変換の確定で、選んだ文の中の英単語を「日本語」と覚えてしまう

## 背景 (誰が、何に困っているか)
- 利用者の声からではなく、2026-11-22 に要件 028 (旧 025) の下書きの査読で見つけた。028 の人の判断 4 で、028 とは別の要件にした。
- ローマ字のまま入った文を選んで再変換 (Mac: 選んで Shift+Space。README の「確定後の再変換」) し、確定すると、選んだ文の中の英単語 (README の例 `asita no meeting de review shimasu` の `meeting`・`review`) を、日英の学習に「日本語にする」と覚える。F6 で直したときと同じ「はっきりした選択」として覚えるので、1 回で効く。設定「変換後も続けて入力できる」に関係なく、既定の設定で起きる。
- 覚えたあと、その語を打つと英字のままにならず崩れる (偽の変換エンジンでの表示): `meeting` → `めえちんg`、`review` → `れview`、`kyouhameetingdesu` → `きょうはめえtingです` (学習なしは `meeting`・`review`・`きょうはmeetingです`)。Enter の確定も崩れる (下の I1)。前に英語と覚えさせていた語も、再変換 1 回で日本語に反転する。
- 再変換は「打ち直した文ではないので学習しない」決まり (`CompositionController.Commit` のコメント、`ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`)。ただ、そのテストは変換エンジンと選び直しの記録だけを見ていて、日英の学習は見ていない。
- 今の挙動: 下の「再現の結果」(Core の使い捨てのテスト。実機は未確認)。

## 今の動き (既に分かっていること)

### 再現の結果 (2026-11-22、Core の使い捨てのテスト。コミットしていない)
- やり方: 写しの木 (`git worktree add --detach`) に使い捨てのテストを足して流した。入口は `MeltypeSession.Reconvert` (Mac の Shift+Space が呼ぶ) と `HandleKey`・`CommitPending`。変換エンジンは偽物 (`CompositionTests.FakeConverter`)。日英の学習は `LanguageMemory.Entries()` で見た。覚えたあとの表示は、同じ `LanguageMemory` を判定 (`CompositionTests.Detector.Memory`) に付けて打った。
- 流した木: 下の表の行は (R8 を除いて) 全部、main 5544133 と PR #29 の先頭 d5bbd0f で流し、結果は同じ (R1・R5・C2 と、I1 の `kyouhameetingdesu` の表示は、要件 028 の測定 (L1〜L5) で d210044 でも同じ)。査読員も別に書いたテストで 3 つの木 (5544133・d5bbd0f・d210044) に流し、同じ結果だった (査読員の報告による)。
- 選んだ文は `asita no meeting de review shimasu`。「ON / OFF」は設定「変換後も続けて入力できる」。

| # | 手順 | 確定 | 日英の学習 |
|---|---|---|---|
| R1 | OFF。再変換 → Enter | `あしたの meeting で review します` | `meeting`・`review` を日本語 (回数 1。はっきりした選択) |
| R2 | OFF。再変換 → Space (選んだままの文) → Enter | `asita no meeting de review shimasu` | R1 と同じ (選んだままの文を確定しても覚える) |
| R3 | OFF。再変換 → 暗黙の確定 (`CommitPending`。フォーカスが外れたときなど) | `あしたの meeting で review します` | R1 と同じ |
| R4 | OFF。再変換 → `yo` (前を暗黙に確定して、新しい入力) → Enter | `あしたの meeting で review しますよ` | R1 と同じ |
| R5 | ON。再変換 → Enter、または Space → Enter (続けて打たない。要件 028 の L2・L3) | R1・R2 と同じ | R1 と同じ |
| R6 | 前に `meeting` を英語とはっきり覚えさせた状態で、OFF。再変換 → Enter | `あしたの meeting で review します` | `meeting` が日本語に反転 (回数 2)、`review` を日本語 |
| R7 | OFF・ON とも。再変換 → Shift+Enter (設定「Shift+Enter で確定して改行」ON) | (見ていない) | R1 と同じ |
| R8 | OFF。再変換 → F6 (または F7) → Enter (再変換中に表示を変えてから確定。`SetMode` が変換中を終える) | `あしたの meeting で review します` | R1 と同じ。出典: 実装の 2 周目でテストに足した (直す前は覚え、直したあとは 0 件。base b59a2b7 と head で確かめた)。上の「流した木」の写しの木 (main 5544133・d5bbd0f) では流していない |
| C1 | OFF。再変換 → Esc (やめて、選んだままに戻す) | `asita no meeting de review shimasu` | なし |
| C2 | ON。再変換 → Space → `yo` → Enter (続けて打つ。要件 028 の L4) | `asita no meeting de review shimasuよ` | なし (選び直しの記録の漏れは要件 028) |

影響 (偽の変換エンジン):

| # | 手順 | 結果 |
|---|---|---|
| I1 | R1 のあとの日英の学習で打つ | 表示: `meeting` → `めえちんg`、`review` → `れview`、`kyouhameetingdesu` → `きょうはめえtingです`。Enter の確定: `めえちんg`・`れview`・`きょうはめえちんがです` (最後は表示と形が違う)。Space の確定: `meeting` は `meeting ` のまま、`review` は `れview `。学習なしは、表示も確定も英字のまま (`きょうはmeetingです`) |

### 直したあとに変わる動き (受け入れる変化。実装の 2 周目で測った)
- 設定に関係なく、再変換の文の文節を固定する前に F6 などを押して、表示のモードが入力全体 (再変換の文 + 続けて打った語) にかかる形では、続けて打った語も覚えなくなった。例: OFF で 再変換 → F6 → `hello` → F6 → Enter、ON で `hello` → Esc (固定を戻す) → F6 → Enter。直す前は `hello`・`meeting`・`review` を日本語と覚えていた。直したあとは何も覚えない。
- 理由: 表示のモードが入力全体にかかるので語を分けられず、全部覚えない方が minimum の要件 1 に合う (査読の判断)。N1〜N3 は変わらない。

変えてはいけない動き (利用者がはっきり直した。今も覚える):

| # | 手順 | 結果 |
|---|---|---|
| N1 | ふつうに `meeting` → F6 → Enter | 確定 `めえちんg`、`meeting` を日本語 (回数 1) |
| N2 | ON。再変換 → Space → `hello` → F6 → Enter (続けて打った分を F6) | `hello` を日本語 (回数 1)。普通の変換 (`hashiwo` Space) のあとに続けて打っても同じ |
| N3 | ON。再変換 → Space → `tanni` → F10 → Enter (続けて打った分を F10) | `tanni` を英語 (回数 1)。普通の変換のあとでも同じ |

### コード (行番号は main 5544133)
- `CompositionController.Commit` (2965 行): `if (converting && !_reconvertRomaji) Learn(); else { if (fixedOnly) Learn(); if (toggle is null) LearnLanguage(); }` (2998〜3003 行)。ローマ字の再変換の確定は、`converting` でも `_reconvertRomaji` なので else 側に入り、`LearnLanguage()` (3002 行) が走る。暗黙の確定 (`Commit(implicitly: true)`) でも同じ。Shift+Enter (1147〜1153 行) も同じ `Commit` を通る (R7)。
- `ReconvertRomaji` (708 行) は、日本語にした文を `_text.AppendKana(c, c)` で 1 文字ずつ入れ、表示を `DisplayMode.Hiragana` にする (736・737 行)。
- `LearnLanguage` (3160 行〜) の Hiragana の分岐 (3175・3176 行) は、表示が F6 などでかなにされたとみなし、区間分けで英語になった区間を `memory.Remember(word, english: false, explicitChoice: true)` (3168 行) で覚える。選んだ文の英単語は英語の区間になるので、日本語として覚える。
- はっきりした選択 (`explicitChoice`) は 1 回で効き、前に英語と覚えていた語も 1 回で反転する (`LanguageMemory.cs` 413 行。R6)。
- 続けて打った経路 (要件 028 の形) では、`EndConversionForNewInput` (1865 行) の `_text.Clear()` が表示のモードを Auto に戻す (`CompositionText.cs` 804 行) ので、再変換の文は日英の学習に入らない (C2)。続けて打った分の F6・F10 は今どおり覚える (N2・N3)。
- Esc・BackSpace は `CancelRomajiReconversion` (966 行〜) で、確定 (`Commit`) を通らないので覚えない (C1)。

### 実機での出方 (コードを読んだ推定。実機は未確認)
- 日英の学習は Core の中 (`languages.json`) だけで決まり、Swift を通らないので、実機でも同じく覚える見込み。
- 覚えた語は「Meltype 辞書」の「学習した語」タブ (⌘3) に「かなにする」として並ぶ見込みで、そこから 1 語ずつ消せる (`mac/README.md` の「学習した語」)。
- 英単語を含むローマ字の文を再変換して確定するたびに、その英単語が日本語に寄る。README の例そのものが当たるので、ローマ字の再変換を使う人には広く効く見込み。

### 既存のテストと文書
- `ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`: 設定 OFF の `kyouha iitenki desu ne` (英単語を含まない文) の Space 0〜2 回 → Enter で、変換エンジンと選び直しの記録だけを見ている。英単語を含む文も、日英の学習も見ていない。
- 文書: README・docs/USAGE.md の再変換の説明は、学習に触れていない。docs/KNOWN_LIMITS.md にも記述は無い (「再変換」で探した)。
- 本家 (yksr-melt/Meltype) の扱い: ローマ字の再変換は本家 #332 の改良導入 (docs/IMPROVEMENT_PLAN.md の P36)。本家で同じ形が起きるかは未確認 (クラウドから本家に届かない)。

## minimum の要件 (これだけは満たす)
1. ローマ字の再変換で始めた確定では、再変換の文 (日本語にした文・選んだままの文のどちらを確定しても) の中の語を、日英の学習に入れない。確定の経路 (Enter・Shift+Enter・暗黙の確定・続けて打って前を確定する形 (設定 OFF)) によらない。前に覚えていた向きも変えない (R6)。
2. 利用者がはっきり直した語の日英の学習は変えない: ふつうに打った語の F6・F10 (N1)、続けて打った分の F6・F10 (N2・N3)。

## 非目標 (今回はやらない)
- すでに覚えてしまった語を探して消すこと (「学習した語」タブで 1 語ずつ消せる。入力メニューの「学習データをすべて消去…」もある)。
- 要件 028 の漏れ (設定 ON で続けて打ったときの、変換エンジン・選び直しの記録・登録提案)。
- ローマ字の再変換の候補の出し方を変えること。
- 学習データのファイル形式の変更。

## 完成条件 (先に決める。機械で判定できる形)
- [x] テスト (赤 → 緑): `src/Meltype.Core.Tests/ReconvertRomajiTests.cs` に、英単語を含む文 (`asita no meeting de review shimasu`) の日英の学習を見るテストを足す (名前は仮: `Reconvert_RomajiSentence_DoesNotTeachLanguage`)。要点: `LanguageMemory` を付けた `MeltypeSession` で、R1〜R5 と R7 (Shift+Enter) の各経路で、日英の学習が 0 件。R6 (前に `meeting` を英語と覚えさせた状態) で、`meeting` が英語のまま。**実装前は赤** (表のとおり覚える)、実装後は緑。
- [x] 対照 (今も緑・直したあとも緑): Esc (C1) は 0 件のまま。ふつうに打った `meeting` + F6 (N1) は日本語と覚える。設定 ON で続けて打った分の F6・F10 (N2・N3) は今どおり覚える。
- [x] 守りが効いていること: 足した守りの条件を外すと、上のテストが落ちることを一度確かめて PR に書く。(確認は達成。PR は未作成なので PR への記載は未)
- [x] 回帰なし: Core のテストが全件通る (base の数字: main 5544133 で 1266/1266。スキップ 6 件は、root の環境で権限を使うテスト 5 件と、ファイルシステムの作成時刻の条件のテスト 1 件)。`tools/eval-compare.sh` の broken 0。品質テストの基準 (全体 95 %・各カテゴリー 80 %) を満たしたまま。
- [x] 文書: 直す所は無い見込み (上の「既存のテストと文書」)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
前提: 試す前に「Meltype 辞書」の「学習した語」タブ (⌘3) で、`meeting`・`review` が無いことを見ておく (あれば控えておく)。TextEdit で、日本語入力 (「あ」) にして試す。直っていない版で試すと覚えるので、確かめたら「学習した語」タブで消す。
- [ ] `asita no meeting de review shimasu` を選んで Shift+Space → Enter。そのあと「学習した語」タブに `meeting`・`review` が増えない (直す前は「かなにする」で増える見込み。推定)。 (2026-11-26 自動打鍵 (TextEdit): 表示は `明日の meeting で review します`。学習データのファイル (languages.json) では `meeting`・`review` は増えないことを確認、「学習した語」タブは未確認。[ ] のまま)
- [x] そのあと `kyouhameetingdesu` と打つと、`meeting` が英字のまま (直す前は崩れる見込み。I1 の形。推定)。 (2026-11-26 自動打鍵 (TextEdit) で確認: `今日はmeetingです` (英字のまま))
- [ ] Space で選んだままの文を選んで Enter・別の欄をクリック (暗黙の確定)・Shift+Enter でも、上と同じ。 (2026-11-26 自動打鍵 (TextEdit): 打っていない。未確認)
- [ ] 再変換 → F6 (Ctrl+J。または F7 = Ctrl+K) → Enter でも、「学習した語」タブに `meeting`・`review` が増えない (直す前は増える見込み。R8。推定)。 (2026-11-26 自動打鍵 (TextEdit): 再変換 → Ctrl+J → Enter で表示は変わらず、`meeting`・`review` は増えなかったが、Ctrl+J が再変換中に効いたか不明。F6 が効いた形は未確認)
- [x] 対照: ふつうに `meeting` と打って F6 → Enter では、今どおり「学習した語」タブに増える (確かめたら消す)。 (2026-11-26 自動打鍵 (TextEdit) で確認: `meeting` → Ctrl+J → Enter で `めえちんg` になり、languages.json に `meeting` が増えた (対照どおり。「学習した語」タブではなく学習データのファイルで確認。確認後に学習データは打つ前の状態へ戻した))

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 日英の学習 (`languages.json`) のファイル形式を変える必要が出たとき。
- Swift (`mac/`) の変更が要ると分かったとき (学習するかの判断は Core の `Commit` にあるので、Core だけで直る見込み)。
- 直し方が、利用者がはっきり直した語の学習 (N1〜N3) まで変えないと成り立たないとき。

## 人の判断 (2026-11-22 に決定。指示「OK、両方おすすめで」で推奨どおり)
1. 要件 028 との順番: 028 と 027 は、どちらも `Commit` の同じ分岐 (2998〜3003 行) と `ReconvertRomajiTests.cs` を触る。推奨: 027 を先にして、同時には進めない (027 は既定の設定で起き、当たる人が多い見込み。同時に進めると衝突しやすい。`docs/LESSONS.md` の、要件 023 で開いている PR と衝突した行)。→ **決定**: 推奨どおり。027 を先にして、028 は 027 のあとに進める (同時に進めない)。

## 記録 (ループが書く)
- 下書き: 2026-11-22。要件 028 の「範囲外で見つけたこと」から起こした。「今の動き」の表は、写しの木での使い捨てのテストの出力 (コミットしていない)。
- 下書きの査読 (2026-11-22): 1 周目は止める 0・直す 0・任意 5 (全部入れた。確定の形と Shift+Enter は、写す前に自分で測った)。2 周目 (最後の確認) は止める 0・直す 0・任意 2 (028 の PR #30 の書き方は直した。027 の R7 の確定の欄は、自分で測っていないので「(見ていない)」のまま)。
- 人の判断 (2026-11-22): 指示「OK、両方おすすめで」で 1 を推奨どおりに決めた。status は draft のまま (approved にするのは人)。
- 承認 (2026-11-23): 人の指示「承認で進めて」で status を approved にした。
- 番号の付け直し (2026-11-23): PR #29 (自分の 016・018 を 025・026 に付け直した) と PR #31 (025) と番号が重なったので、人の指示で 026 → 027 に付け直した (続けて入力の要件は 025 → 028)。実装のコミット (569e28a・fbd9b19) は付け直す前なので「026:」で始まる。
- 実装のループ: 開始 2026-11-23T06:09:59Z、終了 06:52:28Z (約 42 分)。人の介入: 番号の付け直しの判断 1 回 (下)。
- 周回数: 実装 2 周。役割ごとの起動回数: 調査 1 回・実装 2 回 (2 周目は `Composition/` に触れるので上位のモデルに上げた)・査読 2 回・回帰ゲート 2 回・記録 1 回。
- コミット: 569e28a (テスト。実装前は赤)・fbd9b19 (直し: `LearnLanguage()` の先頭に `if (_reconvertRomaji && _headCount == 0) return;`)・443cdf1 (2 周目: R8 の経路と対照の確かめを足した)。569e28a と fbd9b19 の見出しの「026:」は、付け直す前の番号 (要件 027 のこと。PR #29 の 026 (ライブ変換の確定の学習) とは別)。
- 数字: Core のテスト 1268/1268 (base b59a2b7 は 1266/1266。スキップ 6 件は root の環境で権限を使うテスト 5 件と作成時刻のテスト 1 件)。品質テスト 1295/1296 (99.9 %、base と同じ。最低のカテゴリーは英語 39/40)。正解表 704/810 で base と同じ (eval-compare broken 0 / fixed 0 / added 0 / removed 0)。PR #29 (cb76c3c) と合わせた写しで 1299/1299。
- 赤と守りの確かめ: 直す前は `DoesNotTeachLanguage` の全経路 (R1〜R5・R7・R8) が赤、R6 も単独で赤 (査読員が確かめた)。改造で落ちること: 守りを外す → 全経路、`if (_reconvertRomaji) return;` → 対照の N2、守りを `Commit` 側の「変換中のときだけ」に移す → R8、守りをひらがなのときだけ・`_converting` のときだけにする → R8、続けて打った経路で文の語が漏れる形 → N2/N3 の件数。
- 境界例 (査読員): 数字キー・↓↑・PageDown・←→・Tab・Delete・F6〜F8・TokMode・ライブ変換などの経路で、直したあとは全部 0 件。ほかの 10 文で直す前に日本語と覚えた 13 語も 0 件。N1〜N3 と、かなの再変換 (`ReconvertKana`) は変わらない。
- 手戻りの原因: 1 周目のテストは変換中のまま確定する経路だけで、守りを `Commit` 側の「変換中のときだけ」に移す改造でも通った (査読 1 周目の直す 1)。2 周目に R8 (F6・F7 → Enter) を足した。
- 手順: 実装のコミット (fbd9b19 は 06:19:12Z、443cdf1 は 06:46:03Z) は、止めのフックに従い、査読の前に作業ブランチへ push された (このブランチに PR は無いので公開行為ではない。improve.md の順は 査読 → 記録 → push)。
- 番号の付け直し: 実装中の 2026-11-23 に、人の指示で 026 → 027 に付け直した (既に記録の行がある。重ねない)。
- 測れていないこと: 実機の入力欄・azooKey は未確認。Mac 実機チェックリストは未確認。PR は作っていない (このセッションでは、PR は人が頼んだときだけ作る)。
- 2026-11-26 自動打鍵 (TextEdit、入っている版 1.2.9 = main f558dfa。リョウさんの今の設定: ライブ変換 ON・Zenzai ON・変換後も続けて入力 ON。OFF の行は一時的にライブ変換を切った。人の手ではない): 1 行目 (`asita no meeting de review shimasu` を貼って選び Shift+Space → Enter) は `明日の meeting で review します`、languages.json に `meeting`・`review` は増えない (タブは未確認)。2 行目 (`kyouhameetingdesu`) は `今日はmeetingです`。対照 (`meeting` → Ctrl+J → Enter) は `めえちんg` で languages.json に `meeting` が増えた。2 行目と対照の行を [x] にし、1 行目は「学習した語」タブを見ていないので [ ] のまま。4 行目 (再変換 → F6) は Ctrl+J を送ったが表示が変わらず、Ctrl+J が再変換中に効いたか不明なので **F6 が効いた形は未確認** ([ ] のまま)。3 行目 (Space で選んだまま・別の欄をクリック・Shift+Enter) は打っていない。確認は学習データのファイルで、「学習した語」タブは見ていない。リョウさんの手での確認ではない。status は変えない。
