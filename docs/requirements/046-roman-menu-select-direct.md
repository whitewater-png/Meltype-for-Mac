---
title: 入力メニューで「英数 (Meltype for Mac)」を選んだら、直接入力になる
status: done   # draft | approved | done。approved にするのは人
source: fork Issue #22 (利用者の報告。コードを読んだ推測つき)
scope: swift (実機必須)
---

# 046 入力メニューで「英数 (Meltype for Mac)」を選んだら、直接入力になる

## 背景 (誰が、何に困っているか)
- 報告 (要約): 入力メニューからマウスで「英数 (Meltype for Mac)」を選ぶと、アイコンは英数になるが、`kyouha` と打つと下線が付いて日本語に変換される。英数キー (Karabiner で割り当て) では直接入力になる。メニューから選んだときだけ効かない。TextEdit・Emacs で同じ。macOS 27.2.1 / Meltype 1.2.8。
- 実機での再現は、この作業では行っていない (コードから原因を特定。実機未確認)。

## 今の動き (既に分かっていること)
- Info.plist の英数モードは、`tsInputModeListKey` の辞書のキーが標準名 `com.apple.inputmethod.Roman`、`TISInputSourceID` が `io.github.yksr-melt.inputmethod.Meltype.Roman`。日本語モードはキーと ID が同じ。
- 入力メニューでモードを選ぶと `InputController.setValue(_:forTag:client:)` に `kTextServiceInputModePropertyTag` でモードの文字列が来る。`applyMode` はそれを `romanModeID` (= TISInputSourceID) とだけ比べていた。英数で辞書のキーが来ると、日本語とも英数とも一致せず、`guard` で何もせずに戻る → Core は直接入力にならない。
- 英数キーは `handle` の中で `NativeCore.setDirect` を直接呼ぶので、この比べ方を通らない。
- 本家 (yksr-melt/Meltype) の扱い: 本家に無い (Mac 版の仕組み)。

## 前提: setValue に辞書のキーが来るとする根拠 (2026-11-26 に Web で確認)
- azooKey-Desktop: `setValue` が `value == "com.apple.inputmethod.Roman"` で英語モードを判定し、メニューバーからの切り替えに対応している。plist は辞書のキーが標準名で、`TISInputSourceID` は独自 ID (`dev.ensan.inputmethod.azooKeyMac.Roman`) で、Meltype と同じ構成。
  https://raw.githubusercontent.com/azooKey/azooKey-Desktop/main/azooKeyMac/InputController/azooKeyMacInputController.swift /
  https://raw.githubusercontent.com/azooKey/azooKey-Desktop/main/azooKeyMac/Info.plist
- EmojiIM (mzp): `setValue` で `directMode = value == "com.apple.inputmethod.Roman"`。plist は同じ構成 (キー標準名・`TISInputSourceID` は `jp.mzp.inputmethod.EmojiIM.roman`)。
  https://raw.githubusercontent.com/mzp/EmojiIM/master/Sources/InputMethodKit/EmojiInputController.swift /
  https://raw.githubusercontent.com/mzp/EmojiIM/master/Resources/EmojiIM/Info.plist
- AquaSKK 開発日記 (2010-04-29): 入力メニューから入力モードを変えると `setValue:forTag:client:` が呼ばれ、「tag が kTextServiceInputModePropertyTag の時、value には "com.apple.inputmethod.Japanese.Hiragana" 等の modeIdentifier が渡されてくる」。`selectInputMode:` の modeIdentifier は「Info.plist の ComponentInputModeDict に記述した入力モードのどれかと一致している必要がある」。(ブログの記述であって Apple の仕様ではない。)
  https://t-suwa.hatenadiary.org/entry/20100429
- Apple TN2128: メニューから入力モードが選ばれると `kTextServiceInputModePropertyTag` で「input mode specifier」の文字列が渡される。標準の specifier (`com.apple.inputmethod.Roman` など) を使うよう勧めている。
  http://leopard-adc.pepas.com/technotes/tn2005/tn2128.html
- TextInputSources.h (`kTISPropertyInputSourceID`): 入力モードの ID は「親の入力メソッドの bundle ID + 接尾辞」で、「入力メソッドをまたいで使う一般の入力モード名 (`com.apple.inputmethod.Japanese.Katakana` など) ではない」。つまり TISInputSourceID と標準の入力モード名は別のもの。
  https://github.com/phracker/MacOSX-SDKs/blob/master/MacOSX10.15.sdk/System/Library/Frameworks/Carbon.framework/Versions/A/Frameworks/HIToolbox.framework/Versions/A/Headers/TextInputSources.h
- この Mac の `com.apple.inputsources` の `AppleEnabledThirdPartyInputSources` (読み取りだけ): Meltype の英数は `Input Mode` = `com.apple.inputmethod.Roman` (辞書のキー) で記録され、日本語は `...Meltype.Japanese` (ID = キー)。入力ソースの登録の側がキーで持っていることの傍証で、setValue に来る値そのものの証拠ではない。
- **実機でその文字列を見た証拠はまだ無い。** 利用者の既存の記録 (imk-trace.log) には、英数をメニューから選んだ行が無かった (反証ではない。選んでいないだけ)。

## minimum の要件 (これだけは満たす)
1. setValue で受け取ったモードの文字列が英数の辞書のキー (`com.apple.inputmethod.Roman`) でも英数の ID と同じに扱い、直接入力にする (`InputSourceRegistration.canonicalModeID`)。
2. 日本語の ID・英数の ID・知らない文字列の扱いは変えない (知らない文字列は今までどおり何もしない)。

## 非目標 (今回はやらない)
- Info.plist の辞書のキーを変えること (Caps Lock の切り替えが標準名で判定されている可能性があるため。plist のコメントのとおり)。
- 新しい設定・通信・ログ。打った文字は触らない。

## 完成条件
- [x] `Meltype --self-test` の「入力モードの ID」群 (5 件): 標準名のキー → 英数の ID・英数の ID・日本語の ID・知らない文字列・空はそのまま。
- [x] ビルド (`./build.sh --no-install`)・codesign 検証・`check-mac-aot-settings` が通る。Core は変えていないので Core のテストは変わらない。
- [x] 下の Mac 実機チェックリスト (人が確かめる。2026-11-27 までにトレース以外の 5 項目を確認。トレースの項目は AI が要らないと提案して外した。リョウさんの明示の了承は無い。記録を参照)

## 得と損
- 得: 辞書のキーでも ID でも、メニューの英数が効く。直す場所が 1 つの純粋な関数で、self-test で確かめられる。
- 損: 実際に setValue へ来る文字列が辞書のキーだという点は、報告者の推測とコードの読みから (IMKit の説明: モードの識別子は辞書のキーと一致させる) で、実機のトレースでは未確認。もし違う文字列が来ていたら、この直しは効かない (害は無い)。確かめるには `MeltypeTraceIMK` を ON にしてメニューで英数を選び、`setValue mode=` の行を見る。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [x] TextEdit: 入力メニューで「英数 (Meltype for Mac)」をマウスで選ぶ → `kyouha` → `kyouha` のまま入る (下線なし)
- [x] 続けて入力メニューで「日本語 (Meltype for Mac)」を選ぶ → `kyouha` → きょうは と変換中になる (日本語に戻る)
- [x] 英数キー (または Karabiner で割り当てたキー) → 直接入力。かなキー → 日本語。今までどおり
- [x] 変換中 (未確定の文字がある) に入力メニューで英数を選ぶ → 未確定の文字が確定されてから直接入力になる (今までの英数キーと同じ)
- [x] Caps Lock での切り替えが今までどおり
- [ ] (原因の確認) `MeltypeTraceIMK` を ON にして英数を選び、`imk-trace.log` の `setValue mode=` が `com.apple.inputmethod.Roman` か確かめる → **やらない** (AI の提案。リョウさんの明示の了承は無い。記録の 2026-11-27 を参照。今のトレースは canonicalModeID でそろえた後の値を書くので、この確かめ方では常に `...Meltype.Roman` が出る)

## 止める条件
- 実機で直らない (setValue の文字列が別のものだった) 場合は、トレースの文字列を見て直す。この直しを入れたままで害は無い。

## 記録
- 周回数 1 / 実機確認はトレース以外の全項目 (下記 2026-11-26・2026-11-27) / PR の URL: なし (直しは main に直接入った。この記録の更新は作業ブランチの PR)

- 2026-11-26 リョウさんの実機 (main 579a708 を入れた版) で確認: 入力メニューから英数をマウスで選んで `kyouha` がそのまま入る・日本語を選び直して変換に戻る・英数キー / かなキーの切り替えが今までどおり、の 3 点を「動作に問題ありません」と報告。未確認: 変換中に英数を選んだときの確定、Caps Lock での切り替え、トレースで `setValue mode=` の実際の文字列 (トレースは読んでいない)。
- 2026-11-27 リョウさんの実機 (入れた版は聞いていない) で残りを確認: 変換中 (`kyouha` に下線) に入力メニューで英数を選ぶと確定されてから直接入力になる (「OK」)。Caps Lock を押すと英数になって変換されず、もう一度押すと日本語に戻る (「戻るよ」)。
- トレースでの原因確認は、AI が要らないと提案し、2026-11-27 にリョウさんへ「トレースの確認はやらなくていい」と理由つきで伝えた。リョウさんは異論なく残り 2 項目の確認に進んだが、明示の了承の言葉は無い。違っていれば項目を戻す。理由: (1) 今のトレースは `canonicalModeID` でそろえた後の値を書く (`InputController.swift` の `setValue`) ので、書いてある確かめ方では元の文字列が見えない。(2) 直す前に効かなかったのは Issue #22 の報告者の環境 (リョウさんの Mac では、直す前は試していない)。直した後はリョウさんの Mac で効く。直しが変えたのは `com.apple.inputmethod.Roman` が来たときだけなので、その文字列が来ている見込みが高い (推論。トレースで見たわけではない)。(3) トレースは打った文字が残る。
- status を done にしたのは、リョウさんが残りの確認項目 (変換中の切り替え・Caps Lock) を済ませ、どちらも問題なしと答えたため (2026-11-27)。「承認」という言葉での返事は無い。違っていれば draft に戻す。
