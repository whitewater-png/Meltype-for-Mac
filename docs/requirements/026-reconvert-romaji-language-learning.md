---
title: ローマ字の再変換の確定で、選んだ文の中の英単語を「日本語」と覚えてしまう (日英の学習)
status: draft   # draft | approved | done。approved にするのは人 (人の判断は 2026-11-22 に決定)
source: 要件 025 の下書きの査読で見つけた (docs/requirements/025-reconvert-romaji-continue-learning.md の「範囲外で見つけたこと」、2026-11-22。025 の人の判断 4 で別の要件に決定)
scope: core
---

# 026 ローマ字の再変換の確定で、選んだ文の中の英単語を「日本語」と覚えてしまう

## 背景 (誰が、何に困っているか)
- 利用者の声からではなく、2026-11-22 に要件 025 の下書きの査読で見つけた。025 の人の判断 4 で、025 とは別の要件にした。
- ローマ字のまま入った文を選んで再変換 (Mac: 選んで Shift+Space。README の「確定後の再変換」) し、確定すると、選んだ文の中の英単語 (README の例 `asita no meeting de review shimasu` の `meeting`・`review`) を、日英の学習に「日本語にする」と覚える。F6 で直したときと同じ「はっきりした選択」として覚えるので、1 回で効く。設定「変換後も続けて入力できる」に関係なく、既定の設定で起きる。
- 覚えたあと、その語を打つと英字のままにならず崩れる (偽の変換エンジンでの表示): `meeting` → `めえちんg`、`review` → `れview`、`kyouhameetingdesu` → `きょうはめえtingです` (学習なしは `meeting`・`review`・`きょうはmeetingです`)。Enter の確定も崩れる (下の I1)。前に英語と覚えさせていた語も、再変換 1 回で日本語に反転する。
- 再変換は「打ち直した文ではないので学習しない」決まり (`CompositionController.Commit` のコメント、`ReconvertRomajiTests.Reconvert_RomajiSentence_IsNotLearned`)。ただ、そのテストは変換エンジンと選び直しの記録だけを見ていて、日英の学習は見ていない。
- 今の挙動: 下の「再現の結果」(Core の使い捨てのテスト。実機は未確認)。

## 今の動き (既に分かっていること)

### 再現の結果 (2026-11-22、Core の使い捨てのテスト。コミットしていない)
- やり方: 写しの木 (`git worktree add --detach`) に使い捨てのテストを足して流した。入口は `MeltypeSession.Reconvert` (Mac の Shift+Space が呼ぶ) と `HandleKey`・`CommitPending`。変換エンジンは偽物 (`CompositionTests.FakeConverter`)。日英の学習は `LanguageMemory.Entries()` で見た。覚えたあとの表示は、同じ `LanguageMemory` を判定 (`CompositionTests.Detector.Memory`) に付けて打った。
- 流した木: 下の表の行は全部、main 5544133 と PR #29 の先頭 d5bbd0f で流し、結果は同じ (R1・R5・C2 と、I1 の `kyouhameetingdesu` の表示は、要件 025 の測定 (L1〜L5) で d210044 でも同じ)。査読員も別に書いたテストで 3 つの木 (5544133・d5bbd0f・d210044) に流し、同じ結果だった (査読員の報告による)。
- 選んだ文は `asita no meeting de review shimasu`。「ON / OFF」は設定「変換後も続けて入力できる」。

| # | 手順 | 確定 | 日英の学習 |
|---|---|---|---|
| R1 | OFF。再変換 → Enter | `あしたの meeting で review します` | `meeting`・`review` を日本語 (回数 1。はっきりした選択) |
| R2 | OFF。再変換 → Space (選んだままの文) → Enter | `asita no meeting de review shimasu` | R1 と同じ (選んだままの文を確定しても覚える) |
| R3 | OFF。再変換 → 暗黙の確定 (`CommitPending`。フォーカスが外れたときなど) | `あしたの meeting で review します` | R1 と同じ |
| R4 | OFF。再変換 → `yo` (前を暗黙に確定して、新しい入力) → Enter | `あしたの meeting で review しますよ` | R1 と同じ |
| R5 | ON。再変換 → Enter、または Space → Enter (続けて打たない。要件 025 の L2・L3) | R1・R2 と同じ | R1 と同じ |
| R6 | 前に `meeting` を英語とはっきり覚えさせた状態で、OFF。再変換 → Enter | `あしたの meeting で review します` | `meeting` が日本語に反転 (回数 2)、`review` を日本語 |
| R7 | OFF・ON とも。再変換 → Shift+Enter (設定「Shift+Enter で確定して改行」ON) | (見ていない) | R1 と同じ |
| C1 | OFF。再変換 → Esc (やめて、選んだままに戻す) | `asita no meeting de review shimasu` | なし |
| C2 | ON。再変換 → Space → `yo` → Enter (続けて打つ。要件 025 の L4) | `asita no meeting de review shimasuよ` | なし (選び直しの記録の漏れは要件 025) |

影響 (偽の変換エンジン):

| # | 手順 | 結果 |
|---|---|---|
| I1 | R1 のあとの日英の学習で打つ | 表示: `meeting` → `めえちんg`、`review` → `れview`、`kyouhameetingdesu` → `きょうはめえtingです`。Enter の確定: `めえちんg`・`れview`・`きょうはめえちんがです` (最後は表示と形が違う)。Space の確定: `meeting` は `meeting ` のまま、`review` は `れview `。学習なしは、表示も確定も英字のまま (`きょうはmeetingです`) |

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
- 続けて打った経路 (要件 025 の形) では、`EndConversionForNewInput` (1865 行) の `_text.Clear()` が表示のモードを Auto に戻す (`CompositionText.cs` 804 行) ので、再変換の文は日英の学習に入らない (C2)。続けて打った分の F6・F10 は今どおり覚える (N2・N3)。
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
- 要件 025 の漏れ (設定 ON で続けて打ったときの、変換エンジン・選び直しの記録・登録提案)。
- ローマ字の再変換の候補の出し方を変えること。
- 学習データのファイル形式の変更。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] テスト (赤 → 緑): `src/Meltype.Core.Tests/ReconvertRomajiTests.cs` に、英単語を含む文 (`asita no meeting de review shimasu`) の日英の学習を見るテストを足す (名前は仮: `Reconvert_RomajiSentence_DoesNotTeachLanguage`)。要点: `LanguageMemory` を付けた `MeltypeSession` で、R1〜R5 と R7 (Shift+Enter) の各経路で、日英の学習が 0 件。R6 (前に `meeting` を英語と覚えさせた状態) で、`meeting` が英語のまま。**実装前は赤** (表のとおり覚える)、実装後は緑。
- [ ] 対照 (今も緑・直したあとも緑): Esc (C1) は 0 件のまま。ふつうに打った `meeting` + F6 (N1) は日本語と覚える。設定 ON で続けて打った分の F6・F10 (N2・N3) は今どおり覚える。
- [ ] 守りが効いていること: 足した守りの条件を外すと、上のテストが落ちることを一度確かめて PR に書く。
- [ ] 回帰なし: Core のテストが全件通る (base の数字: main 5544133 で 1266/1266。スキップ 6 件は、root の環境で権限を使うテスト 5 件と、ファイルシステムの作成時刻の条件のテスト 1 件)。`tools/eval-compare.sh` の broken 0。品質テストの基準 (全体 95 %・各カテゴリー 80 %) を満たしたまま。
- [ ] 文書: 直す所は無い見込み (上の「既存のテストと文書」)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
前提: 試す前に「Meltype 辞書」の「学習した語」タブ (⌘3) で、`meeting`・`review` が無いことを見ておく (あれば控えておく)。TextEdit で、日本語入力 (「あ」) にして試す。直っていない版で試すと覚えるので、確かめたら「学習した語」タブで消す。
- [ ] `asita no meeting de review shimasu` を選んで Shift+Space → Enter。そのあと「学習した語」タブに `meeting`・`review` が増えない (直す前は「かなにする」で増える見込み。推定)。
- [ ] そのあと `kyouhameetingdesu` と打つと、`meeting` が英字のまま (直す前は崩れる見込み。I1 の形。推定)。
- [ ] Space で選んだままの文を選んで Enter・別の欄をクリック (暗黙の確定)・Shift+Enter でも、上と同じ。
- [ ] 対照: ふつうに `meeting` と打って F6 → Enter では、今どおり「学習した語」タブに増える (確かめたら消す)。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 日英の学習 (`languages.json`) のファイル形式を変える必要が出たとき。
- Swift (`mac/`) の変更が要ると分かったとき (学習するかの判断は Core の `Commit` にあるので、Core だけで直る見込み)。
- 直し方が、利用者がはっきり直した語の学習 (N1〜N3) まで変えないと成り立たないとき。

## 人の判断 (2026-11-22 に決定。指示「OK、両方おすすめで」で推奨どおり)
1. 要件 025 との順番: 025 と 026 は、どちらも `Commit` の同じ分岐 (2998〜3003 行) と `ReconvertRomajiTests.cs` を触る。推奨: 026 を先にして、同時には進めない (026 は既定の設定で起き、当たる人が多い見込み。同時に進めると衝突しやすい。`docs/LESSONS.md` の、要件 023 で開いている PR と衝突した行)。→ **決定**: 推奨どおり。026 を先にして、025 は 026 のあとに進める (同時に進めない)。

## 記録 (ループが書く)
- 下書き: 2026-11-22。要件 025 の「範囲外で見つけたこと」から起こした。「今の動き」の表は、写しの木での使い捨てのテストの出力 (コミットしていない)。
- 下書きの査読 (2026-11-22): 1 周目は止める 0・直す 0・任意 5 (全部入れた。確定の形と Shift+Enter は、写す前に自分で測った)。2 周目 (最後の確認) は止める 0・直す 0・任意 2 (025 の PR #30 の書き方は直した。026 の R7 の確定の欄は、自分で測っていないので「(見ていない)」のまま)。
- 人の判断 (2026-11-22): 指示「OK、両方おすすめで」で 1 を推奨どおりに決めた。status は draft のまま (approved にするのは人)。
- 周回数 / モデル / 所要時間 / 手戻りの原因 / PR の URL
