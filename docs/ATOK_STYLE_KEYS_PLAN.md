# ATOK スタイルのキー操作 (TokMode) 要件定義・設計計画書 (第 2 版)

## 0. 第 1 版からの変更 (なぜ作り直したか)

第 1 版は参考資料の「← で候補を広げ、→ で候補を縮める (候補一覧の表示数 9 個 ⇔ 全部)」をそのまま実装した。
しかしこれは **ATOK の実際の操作ではない**。ATOK 公式の「キーの機能一覧：ATOKスタイル」(Mac 版ヘルプ) では、変換中の ← → は
「文節の区切りを前へ / うしろへ移動」= **文節を縮める / 伸ばす** 操作で、↓ は「部分確定」である。
ユーザーから「目的が達成できていない」「↓ で確定 (部分確定) が含まれていない」と指摘を受け、ATOK の表に合わせて作り直した。
第 1 版の「候補一覧を 9 個に縮める」機能 (`Clause.ShowAll`・`VisibleCandidates`) は削除した。

参照: https://atok.com/other/support/howtouse/mac/ap/pgs/ap_key.htm (文節操作・確定操作の表)

## 1. 要件定義

| 項目 | 内容 |
|---|---|
| 目的 | 変換中の文節操作・部分確定を ATOK と同じキーでできるようにする (オプション) |
| 設定 | `Settings.TokMode` (bool、既定 `false`)。「1. 全般」。Mac の設定画面 (MacSettingsCatalog) にも出す |
| OFF | 今までどおり (← → 文節の移動、↓ 次の候補、Shift+← → 区切りの変更) |

ON のときの変換中のキー (ATOK の表との対応):

| キー | 動作 | ATOK の機能名 |
|---|---|---|
| ← | 選んでいる文節を 1 文字縮める (区切りを前へ) | 文節の区切りを前へ移動 |
| → | 選んでいる文節を 1 文字伸ばす (区切りをうしろへ) | 文節の区切りをうしろへ移動 |
| ↓ | 先頭から選んでいる文節までを確定。残りは変換中のまま、その先頭を選ぶ | 部分確定 |
| Shift+← / Shift+→ | 前 / うしろの文節を選ぶ | 注目文節を前へ / うしろへ移動 |
| Ctrl+← / Ctrl+→ | 先頭 / 最後の文節を選ぶ | 注目文節を先頭へ / 最後へ移動 |
| Space / ↑ | 次 / 前の候補 (今までどおり) | 変換・次候補 / 前候補 |
| Enter / Esc / 数字 | 今までどおり (全文確定 / 取り消し / 番号で選んで確定) | 全文確定 / 変換取消 |

ON のときの入力中 (変換前) のキー (1.2.0 のあとに追加。`docs/IMPROVEMENT_PLAN.md` の A1):

| キー | 動作 | ATOK の機能名 |
|---|---|---|
| Shift+↓ | 先頭の 1 音 (きょ・っ など。英字に見えている部分は 1 文字) だけを、見えている形で確定し、残りは入力のまま | 1 音確定 |

対象外 (今までどおり): 変換に入る前の矢印 (文節の選択に入る)、ローマ字の再変換中の ← → (区切りを変えられないので消費するだけ)、
変換中・次候補表示中の Shift+↓ (ATOK では 1 音確定だが、変換中の動きの根拠が見つからないので入れていない。今までどおり次の候補)。

## 2. 設計

- `CompositionOptions.TokMode`: `Func<bool>`。毎回設定を見るので、切り替えはすぐ効く。
  配線: `MeltypeSession` (Mac/Linux)、`TrayApplicationContext` と `CompositionService` (Windows)、テストの `Keyboard`。
- `CompositionController.HandleConversionKey`:
  - `Left/Right when TokMode`: Shift なし → 既存の `Resize(±1)` (Shift+← → の区切り変更と同じ処理)。Shift あり → `MoveClauseBy(±1)`。
  - `Down when TokMode && !shift`: `CommitThroughSelectedClause()`。
- `CommitThroughSelectedClause` (部分確定):
  1. 最後の文節を選んでいる・固定した文節がある (変換後も続けて入力)・選んだ文の再変換中なら、全部を確定する (`Commit`)。
  2. 確定する文節だけで `Learn()` (学習)。
  3. `CompositionText.RemoveLeading` で、確定した分の単位 (読み / 英語の文節は打った英字) を先頭から取り除く。
     残りは打った英字を覚えたままなので、Esc で読みに戻したあと F10 で打ったままの英字にできる。合わなければ残りの読みだけにする。
  4. `CommitPart` で確定した文字を入力欄へ (確定し直しの対象にはしない)。
  5. 残りの文節を変換中のまま残し、先頭を選ぶ。確定した文字は残りの文脈 (`_precedingText`) に足す。
- `MeltypeSession.HandleKeyCore`: ON かつ変換中の Ctrl+← / Ctrl+→ を `SelectEdgeClause` に渡して消費する
  (ふだんの Ctrl+キーは「確定してアプリへ」なので、変換ボックスに入る前に受ける)。
- `UpdateView`: ON のときのヒント「←→ 区切り　Shift+←→ 文節　Space 候補　↓ 部分確定　Enter 確定　Esc 戻る」。
  入力中 (固定した文節が無いとき) のヒントの最後に「Shift+↓ 1音確定」を足す。
- 1 音確定 (A1): 入力中の `Down when TokMode && Shift` で `CommitFirstSound()`。部分確定 (`RemoveLeading`) は、1 音確定で固定した見え方を捨てる。

## 3. 検証

- `src/Meltype.Core.Tests/TokModeTests.cs` (21 テスト): OFF の従来動作、← → の区切り、伸ばし切り / 1 文字で止まる、
  Shift+← → の文節移動、↓ の部分確定 (残りの変換・Enter・Esc・F10・最後の文節・区切り変更後)、Space / ↑、
  MeltypeSession 経由の Ctrl+← →・Shift+← →・↓、OFF の Ctrl+←、設定カタログ。
- 全体 638/638 パス。
- Mac 実機: `mac/build.sh` で入れ替え、**期待どおりに動くことを確認済み** (← → の区切り、Shift / Ctrl+← → の文節移動、↓ の部分確定)。
  - 途中で一度、実機だけ効かなかった。原因は `MeltypeSession.CreateDefault` (Mac の本番の配線) から
    `TokMode = () => shared.Current.TokMode` が抜けていたこと (作業中の `git checkout` で消えた)。
    テストは `TokMode` を直接渡すので気づけなかった。トレース (`MeltypeTraceIMK`) でキーは届いているのに文節が変わらないことから特定した。
- Windows 版は macOS ではビルドできないため未検証 (配線は 1 行ずつ)。

### 実機の確認手順

設定「ATOK スタイルのキー操作」を ON にして、`kyouhaiitenki` (今日はいい天気) を Space で変換する。

1. ← を押す → 先頭の文節が 1 文字縮み、区切りが前へ動く。→ で伸びる。
2. Shift+→ → 次の文節が選ばれる。Ctrl+→ で最後、Ctrl+← で先頭。
3. ↓ → 先頭から選んでいる文節までが入力欄に確定し、残りは変換中のまま。Enter で残りも確定。

## 4. 既知の制限

- 部分確定は、「変換後も続けて入力」で固定した文節があるとき・選んだ文の再変換中は全部を確定する。
- ATOK の「１音確定 (Shift+↓)」は入力中だけ (A1)。変換中の 1 音確定・「ローマ字単位区切り (Ctrl+Shift+← →)」などは未実装。
- macOS の Mission Control に Ctrl+← → を割り当てていると、Ctrl+← → は IME に届かない。
