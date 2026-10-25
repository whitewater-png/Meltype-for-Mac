# W15: Esc で一段ずつ戻す (EscStepsBack、本家 #676)

## 何をしたか

設定「Esc で一段ずつ戻す」(`Settings.EscStepsBack`、既定 OFF、入力グループ) を足した。ON のとき、入力中 (変換前) の Esc は、打った内容を全部消さずに一段ずつ戻す。

- 漢字 → (変換中の Esc は今までどおり読みに戻る) → ひらがな → 打ったローマ字 → 消す
- 見た目が変わらない段は飛ばす (ひらがな表示のまま打っているときは、ひらがなの段を飛ばして直接ローマ字へ)
- 英字のまま見えている入力 (hello) は、かなに変えても「戻す」にならないので、今までどおり 1 回で消す
- 戻した入力は、英語 / 日本語の判定として覚えない (`_escReverted`。確定・全部消し・BackSpace で空にしたとき・Reset で下ろす)。F7 でカタカナにして Esc で戻して確定しても、merge を日本語として覚えない
- OFF のときは今までと完全に同じ (Esc 1 回で全部消す)

## 他の Esc との衝突を避けた点

- 変換中の Esc (読みに戻す)、固定した文節を戻す Esc、ローマ字再変換の取り消し、確定後 F6 の変換ボックスの Esc (元の語を書き戻す)、予測選択中の Esc は、いずれも StepBack より前の分岐で処理され、変更していない
- 再変換 (`_reconvertOriginal`) と確定後 F6 の箱 (`_toggleBox`) の最中は StepBack をしない。元の文字を書き戻す Esc が優先される (一段ずつ戻して元の文字が失われるのを防ぐ)
- DoubleSpaceToJapanese の「Esc で元の英語に戻る」は `_toggleBox` 経由なので、そのまま

## 実装

- `CompositionController.StepBack()` と `_escReverted` を追加。Esc の分岐は `when EscStepsBack() && _reconvertOriginal is null && _toggleBox is null && StepBack()`
- `LearnLanguage()` の先頭で `_escReverted` なら何も覚えない
- `Settings.cs` / `MacSettingsCatalog.cs` は DoubleSpaceToJapanese の直後。`MeltypeSession.cs` に配線
- テスト: `EscStepsBackTests.cs` (設定の既定・カタログ、OFF は全部消す、カタカナ → ひらがな → ローマ字 → 消す、変換中、ひらがな表示の段飛ばし、英字は消す、再変換の Esc は元に戻す、学習しない + 対照 + 次の入力は学習する)

## 実機で確かめる打鍵 (設定を ON にして)

| 打った > | 期待する結果 |
|---|---|
| `kyou` `Space` `Esc` | きょう (変換前に戻る) |
| 上の続きで `Esc` | kyou (打ったローマ字) |
| 上の続きで `Esc` | 消える |
| `kyou` `Esc` | kyou (ひらがなの段は見た目が同じなので飛ばす) |
| `kyou` `F7` `Esc` | きょう |
| 上の続きで `Esc` `Esc` | kyou → 消える |
| `hello` `Esc` | 1 回で消える (かなにならない) |
| `kyouhamergedesu` `F7` `Esc` `Enter` | ひらがなで確定。次に `kyouhamergedesu` を打っても merge の判定が変わっていない |
| 文字を選択して再変換 `Esc` `Esc` | 読み → 元の文字に戻る (一段ずつで失われない) |
| 設定 OFF で `kyou` `F7` `Esc` | 1 回で全部消える (今までどおり) |

実機未確認 (mac/build.sh は実行していない)。
