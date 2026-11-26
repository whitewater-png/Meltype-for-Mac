---
title: 入力メニューで「英数 (Meltype for Mac)」を選んだら、直接入力になる
status: draft   # draft | approved | done。approved にするのは人
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

## minimum の要件 (これだけは満たす)
1. setValue で受け取ったモードの文字列が英数の辞書のキー (`com.apple.inputmethod.Roman`) でも英数の ID と同じに扱い、直接入力にする (`InputSourceRegistration.canonicalModeID`)。
2. 日本語の ID・英数の ID・知らない文字列の扱いは変えない (知らない文字列は今までどおり何もしない)。

## 非目標 (今回はやらない)
- Info.plist の辞書のキーを変えること (Caps Lock の切り替えが標準名で判定されている可能性があるため。plist のコメントのとおり)。
- 新しい設定・通信・ログ。打った文字は触らない。

## 完成条件
- [x] `Meltype --self-test` の「入力モードの ID」群 (5 件): 標準名のキー → 英数の ID・英数の ID・日本語の ID・知らない文字列・空はそのまま。
- [x] ビルド (`./build.sh --no-install`)・codesign 検証・`check-mac-aot-settings` が通る。Core は変えていないので Core のテストは変わらない。
- [ ] 下の Mac 実機チェックリスト (人が確かめる)

## 得と損
- 得: 辞書のキーでも ID でも、メニューの英数が効く。直す場所が 1 つの純粋な関数で、self-test で確かめられる。
- 損: 実際に setValue へ来る文字列が辞書のキーだという点は、報告者の推測とコードの読みから (IMKit の説明: モードの識別子は辞書のキーと一致させる) で、実機のトレースでは未確認。もし違う文字列が来ていたら、この直しは効かない (害は無い)。確かめるには `MeltypeTraceIMK` を ON にしてメニューで英数を選び、`setValue mode=` の行を見る。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: 入力メニューで「英数 (Meltype for Mac)」をマウスで選ぶ → `kyouha` → `kyouha` のまま入る (下線なし)
- [ ] 続けて入力メニューで「日本語 (Meltype for Mac)」を選ぶ → `kyouha` → きょうは と変換中になる (日本語に戻る)
- [ ] 英数キー (または Karabiner で割り当てたキー) → 直接入力。かなキー → 日本語。今までどおり
- [ ] 変換中 (未確定の文字がある) に入力メニューで英数を選ぶ → 未確定の文字が確定されてから直接入力になる (今までの英数キーと同じ)
- [ ] Caps Lock での切り替えが今までどおり
- [ ] (原因の確認) `MeltypeTraceIMK` を ON にして英数を選び、`imk-trace.log` の `setValue mode=` が `com.apple.inputmethod.Roman` か確かめる

## 止める条件
- 実機で直らない (setValue の文字列が別のものだった) 場合は、トレースの文字列を見て直す。この直しを入れたままで害は無い。

## 記録
- 周回数 1 / 実機未確認 / PR の URL: なし
