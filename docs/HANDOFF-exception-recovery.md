# 引き継ぎ: Mac の入力経路で例外が起きたときの状態の復旧 (#9)

- 作成: 2026-10-10
- 対象の版: v1.1.3-mac (コミット `700889d`)。行番号はこの版のもの
- 出どころ: 2026-10-10 の総点検 (ミラ・ヴェラ・クイナの 3 名が独立に指摘。検証の判定は PLAUSIBLE、重大度は Medium)
- 状態: **未着手**。この文書だけで着手できるように書いている

## 1. 何が問題か (一言で)

Windows 版は、変換の処理で例外が起きると状態を捨てて立て直す。**Mac 版にはその立て直しが無い**。
そのため、例外が一度でも起きると、未確定の表示・先読みしたキュー・確定しかけた文字が中途半端に残る。
同じ例外が毎回起きる場合は、その入力欄で Meltype が使えなくなる。

## 2. 今の動き (コードで確認済み)

### Mac の経路

1. Swift の `InputController.handle` → `NativeCore.handleKey` → FFI `meltype_handle_key` → `Exports.Run` → `MeltypeSession.HandleKey`。
2. `src/Meltype.Mac.Native/Exports.cs:900-913` の `Run` は、例外をつかまえると、**ログを出して `null` を返すだけ**。
   ```csharp
   catch (Exception ex)
   {
       // 例外を Swift 側へ投げると IME ごと落ちるので、ここで止めてキーはアプリに渡す (NULL)。
       Diagnostics.Log.Error($"Mac: 入力の処理で例外: {ex}");
       return null;
   }
   ```
3. Swift の `mac/Sources/MeltypeIME/InputController.swift:185-188` は、`nil` を受けると `return false` する。キーはアプリにそのまま渡る。`apply` は呼ばれないので、**画面の未確定の文字 (marked text) は前のまま残る**。
4. `src/Meltype.Core/Composition/MeltypeSession.cs` (Mac で使う窓口) には、`try/catch`・`_controller.Reset()`・`_gate.Abort()` の呼び出しが 1 つも無い。

### Windows の経路 (比較用。こちらは対処済み)

`src/Meltype/Composition/CompositionService.cs:155-177` の `Safely`:

```csharp
catch (Exception ex)
{
    Diagnostics.Log.Error($"変換ボックスで例外が起きたので入力をリセットしました: {ex}");
    try { Controller.Reset(); } catch { }
    ReplayAll(Gate.Abort());
    Hide();
}
```

- `CompositionController.Reset()` (`CompositionController.cs:413-414`)。コメントは「例外からの復旧用。未確定の内容と追跡中の状態をすべて捨てる (次の入力で同じ例外を繰り返さないように)」。**この復旧用の関数は Core に用意済みで、Mac からは呼ばれていない**。
- `CaptureGate.Abort()` (`CaptureGate.cs:86-95`) は、先読みで捕まえていたキーのキューを返して空にし、捕捉を解く。Windows はそれをアプリへ送り直す (ReplayAll)。

## 3. 起きること (シナリオ)

原因は特定していない (後述) が、`Pump` の途中で例外が起きたとすると、次のことが起きる。

1. **表示が残る**: 画面の marked text が前のまま残り、押したキーはアプリに入る。表示と中身が食い違う。
2. **キーが二重になる**: `CaptureGate` が捕捉中 (`_captured=true`) だと、今のキーはキューに積まれたまま `null` で返り、アプリにも渡る。例外が一度きりで済むと、次の `Pump` でそのキーをもう一度処理する。文字の重複や順番の入れ替わりが起きる。
3. **打った文章が消える**: `Host.Begin` (`MeltypeSession.cs:433-435`) は `_commits` を空にし、結果は `Host.Result` (`446-451`) で取り出す。確定の処理 (`CompositionController.CommitText`) が状態を消したあと、`_host.CommitText` に渡す前に例外が起きると、打った文章は Core からも画面からも消え、Enter だけがアプリに渡る。
4. **抜け出せない**: 例外が入力に対して決定的なら、キーを打つたびに同じ例外になる。セッションを作り直すのは Swift の `init`/`deinit` だけ。`deactivateServer` → `commitComposition` も同じ `Run` を通るので、その `InputController` が生きている間は Meltype がその入力欄で止まったままになる。

## 4. なぜ PLAUSIBLE (確定でない) か

- **確定していること**: 復旧の処理が欠けていること (上の 2)。
- **確定していないこと**: 今のコードで例外を起こす入力。監査で挙がった候補 (`CompositionText` の `Raw[0]`/`Raw[^1]`、`_clauses[_selectedClause]`、`_predictions[_predictionIndex]`) は、原文を追うとどれも範囲外にならなかった。
- それでも直す価値がある理由は 3 つ。共通の Core で例外が起きる前提で Windows はすでに対処している。起きたときの被害 (入力欄が使えない・文章が消える) が大きい。修正は小さく、テストで確かめられる。

## 5. 直し方 (提案)

### 5-1. Core: `MeltypeSession` に復旧を置く (本体)

置き場所は `MeltypeSession` にする。`Meltype.Core.Tests` は `Meltype.Mac.Native` を参照していないので、Core に置けばテストできる。

- 公開している次の 5 つの本体を、共通の `Safely(Func<SessionResult> body)` で包む。
  - `HandleKey` (`MeltypeSession.cs:328-`)
  - `CommitPending` (`387-`)
  - `SelectCandidate` (`396-`)
  - `SelectPrediction` (`379-`)
  - `Reconvert` (`269-`)
- `catch` では次の順に処理する。
  1. `Diagnostics.Log.Error($"... 入力をリセットしました: {ex}")`
  2. `try { _controller.Reset(); } catch { }`
  3. `_gate.Abort()` で、先読みしたキューを捨てて捕捉を解く (下の「決めること 1」)
  4. `Host` に `ResultAfterFailure()` を足す。中身は `new SessionResult(consumed: false, _commits.ToList(), view: null)` で、その回の中ですでに積んだ確定 (`_commits`) を失わずに返す。`view: null` なので、Swift の `apply` が `hideComposition` (`InputController.swift:582-`) を呼び、marked text が消える。
- `Exports.Run` の `catch` は、最後の砦としてそのまま残す。

### 5-2. Swift: 結果が無いときも表示を消す (保険)

`InputController.swift:185-188` で、`handleKey` が `nil` のとき、`hasMarkedText` なら `hideComposition(client:)` を呼んでから `return false` にする。
Core 側で 5-1 をしても、FFI の層 (`Run`) で例外になった場合に表示が残らないようにする。

### 5-3. テスト (`src/Meltype.Core.Tests`)

既存の偽の変換器 (`CompositionTests.cs:15` の `FakeConverter`、`1158` の `LearningConverter`) にならって、`Convert` で例外を投げる偽の `IKanjiConverter` を作り、`MeltypeSession` に渡す。テストは `SessionFacadeTests.cs` に足すのが自然。

次のことを確かめる。

- 例外が起きた回の結果は `consumed: false`・`view: null` で、例外にならない。
- 例外の前にその回で確定した文字列が `commits` に残る。
- **例外の次のキーで、普通に入力できる** (偽の変換器を「1 回だけ例外」にして確かめる)。
- 「毎回例外」の偽の変換器でも、入力が止まらない (変換は失敗しても、かなのまま確定できるなど。期待する振る舞いは実装時に決める)。
- 修正を外すと、これらのテストが落ちることも確かめる (前回までの修正と同じ確かめ方)。

### 5-4. 任意の防御 (今は到達しないので必須ではない)

- `_clauses[_selectedClause]` の前に `_selectedClause < _clauses.Count` を確かめる。
- `Raw[0]` / `Raw[^1]` を `Raw is [var c, ..]` の形にする。

## 6. 実装の前に決めること

1. **先読みしたキーの扱い**: Windows は `Gate.Abort()` で戻ったキーをアプリへ送り直す。Mac の IME には、キーをアプリへ送り直す手段が無い。どれかを選ぶ。
   - (a) 捨てる (いちばん単純。数文字が消えうる)
   - (b) その文字を確定として `commits` に足して返す (文字は残る。ただし変換されない)

   **おすすめは (b)**。打った文字を失わない方を優先する。
2. **例外が続くとき**: 同じセッションで例外が N 回続いたら、そのセッションでは Meltype を素通し (直接入力) にするか。今の「キーはアプリに渡る」動きでも入力自体はできるので、まずは入れずに、ログで頻度を見てから決めてもよい。

## 7. 進め方と確かめ方 (この環境での決まり)

- **Core.Tests**: `cd src/Meltype.Core.Tests && dotnet run -c Release`。今は 452/452。
- **Mac アプリのビルド**: `cd mac && ./build.sh --no-install`。Core の変更は、NativeAOT の dylib を作り直すまで Mac アプリに反映されない。
- **辞書アプリの自己テスト**:
  ```bash
  T=$(getconf DARWIN_USER_TEMP_DIR); D=$(mktemp -d "${T}meltype-selftest.XXXX")
  MELTYPE_DATA_DIR="$D" mac/build/Meltype.app/Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary --self-test
  ```
  今は PASS 121 件。一時フォルダーは NSTemporaryDirectory の下である必要がある。`/private/tmp` の下だと「本体を通した操作」が飛ばされる。
- **AOT の検査**: `python3 tools/check-mac-aot-settings.py`。今は 4 項目とも PASS。`TMPDIR` を作業用の場所に向けて実行する。
- **実機での確認**: 例外を人為的に起こす入口は今は無い。テストで担保し、実機では普段どおり入力できること (退行が無いこと) を確かめる。
- **査読**: 実装者とは別のレーン (別の subagent か別のセッション) で、差分を査読してから完了とする。
- **リリース**: 版を上げる 4 か所は次のとおり。手順は v1.1.3 と同じで、`mac/make-zip.sh`・`mac/make-pkg.sh` → タグ `v<版>-mac` → `gh release create`。更新の判定はアセットの SHA-256 の digest が必須で、GitHub が自動で付ける。
  - `mac/Resources/Info.plist` の 2 つ (`CFBundleShortVersionString`・`CFBundleVersion`)
  - `mac/Sources/MeltypeIME/Converter.swift`
  - `src/Meltype.Core/Meltype.Core.csproj`
  - `.github/ISSUE_TEMPLATE/1-bug.yml` の例
- **作業の注意**
  - この環境の Bash の `grep`・`find`・`head`・`diff` は、rtk のフックで出力が要約・改変されることがある。行番号は Read で確かめる。git は `/usr/bin/git` を使う。
  - Write・Edit・Bash の入力の「\u＋16 進 4 桁」は、実行前に実体の文字に変わることがある。
- **対象外**: Windows 版 (`src/Meltype/`) と Linux 版は対象外 (リョウの方針)。共通の Core (`src/Meltype.Core`) の変更は Windows 版にも入るが、Windows のビルド・確認はしない。

## 8. 関連 (同じ総点検で見送ったもの)

- コード用アプリで、入力メニューの表示が日本語のまま残る件。入力モードを全体に効かせると、別のアプリも英数で始まる副作用があり、実機での確認が要る。
- 性能の指摘 (PLAUSIBLE)。自作分野を編集した直後の全分野の作り直し、学習データの同期保存、`NSAlert.runModal` を IME のメインスレッドで回す件。
