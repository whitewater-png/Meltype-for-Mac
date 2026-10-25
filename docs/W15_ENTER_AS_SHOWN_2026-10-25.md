# W15: 設定「Enter は見えているとおりに確定」(EnterCommitsAsShown) 2026-10-25

本家 #677 (d87de27・1563241) の Mac 版。既定 OFF。OFF のときは今までと完全に同じ動き。

## 何をしたか
- 設定 `EnterCommitsAsShown` を足した (Settings.cs・MacSettingsCatalog.cs の「入力」グループ、`DoubleSpaceToJapanese` の直後。MeltypeSession → CompositionOptions)。
- 「打ち間違いの自動修正」は確定の直前に `CompositionText.FixTypos()` (RomajiTypoCorrector) が行う。Enter / Shift+Enter の経路で `FixTypos(correct: false)` にして、直しだけを飛ばす
  (数字の後ろの単位を英字のままにする処理・笑いの w の保護は、直しの設定に関係なく今までどおり動く)。
- Space・Shift+Space の変換、Space で英語を確定する経路の直しには触れていない (今までどおり)。
- 本家の 2 つ目の直し (1563241) も取り込み: ライブ変換 ON で `かんたn` と見えているとき、Enter で漢字 (簡単) にせず `かんたん` で確定する。
  Enter の確定の最中だけ効く (`_committingByEnter`)。`kantann` まで打てば漢字のまま。
- 変換中 (候補を出した後) の Enter は、もともと直しをしないので変わらない。

## 実機で確かめる (設定で ON にして)
| 打った | 結果 (ON) | 結果 (OFF) |
|---|---|---|
| `onegaishimsu` → Enter | おねがいしmす (変換ボックスのとおり) | おねがいします |
| `onegaishimsu` → Space | 直した形で変換 (おねがいします) | 同じ |
| `onegaishimasu` → Enter | おねがいします | 同じ |
| ライブ変換 ON: `kantan` → Enter | かんたん | 簡単 |
| ライブ変換 ON: `kantann` → Enter | 簡単 | 簡単 |
| `5min` → Enter | 5min (単位は英字のまま) | 同じ |

## テスト
src/Meltype.Core.Tests/EnterAsShownTests.cs (設定の既定・カタログ、OFF で直す、ON で見えているとおり、ON でも Space は直す、きれいな入力は同じ、かんたn)。
実機未確認。
