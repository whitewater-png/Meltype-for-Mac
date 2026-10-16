# Meltype 改善 実装計画書 (要件定義・設計)

作成: 2026-10-07 (同日、コードとの突き合わせレビューを 1 回反映済み)。対象: Meltype 本体 (`src/Meltype.Core`、3 OS 共通) と Mac 版 (`mac/`)。Windows 固有の UI は、Core の変更が必要な範囲だけ触れる。

この文書は「読めばそのまま実装できる」ことを目的にしている。各項目に、目的 → 受け入れ条件 → 変更するファイルと関数 → 手順 → テスト → 注意、の順で書いた。実装するときは **1 項目ずつ**、項目ごとにビルド・テスト・実機確認をしてから次へ進むこと。行番号は 2026-10-07 時点。ずれていたら関数名で探す。

---

## 0. 前提と共通ルール

### 0.1 方針 (メンテナーの判断)

- 入力モードは **「Meltype (日本語・英語の自動判定)」と「英数 (直接入力)」の 2 つだけ**にする。ひらがな／カタカナ／全角英数／半角英数を別の入力モードにはしない (自動判定が Meltype の価値なので、モードを増やすと価値が薄れる)。
- その代わり、変換ボックスの中で **F6〜F10 (ひらがな／カタカナ／半角カナ／全角英数／半角英数) を全部使える**ようにする。Mac では F キーが OS のショートカットとぶつかるので、Ctrl キーの代替も用意する。
- **予測変換**を入れる。さらに、最新の語 (AI 関連の語など) を**インターネットから自動で取り込む辞書**を持つ。

### 0.2 現状の事実 (実機で確認したもの。2026-10-07、macOS 26.6.2)

| キー | Mac 版の今の動き | 原因 |
| --- | --- | --- |
| F6 | ひらがなになる | 正常 |
| F7 | カタカナになる | 正常 |
| F8 | **半角カナにならず、そのまま確定される** | Core に F8 の処理が無い (`VirtualKeys` に F8 が無い)。Mac は `event.characters` (F8 は U+F70B の 1 文字) を ch として渡すので、文字として扱われて確定に至っている可能性がある。どちらにせよ `case F8` を足せば直る |
| F9 | **反応しない** | この Mac では F9 が macOS のショートカット (Mission Control 系) に取られている。Meltype の問題ではないが、代替キーが無いと使えない |
| F10 | 半角英字になる | 正常 |

- 予測変換は無い。Mac は azooKey の予測を `requireJapanesePrediction: .disabled` で切っている (`mac/Sources/MeltypeIME/Converter.swift`)。Win/Linux の Mozc ヘルパーは `StartConversion` しか呼んでいない (`native/mozc/meltype_mozc_helper.cc:63`)。
- Mac は azooKey の学習が `learningType: .nothing` で OFF で、確定を azooKey に伝える `updateLearningData` も呼んでいない。効くのは Core の `conversions.json` だけ。
- 候補の数字キー選択・PageUp/Down は無い (`CompositionController.HandleConversionKey`)。
- Mac の入力モードは `...Meltype.Japanese` の 1 つだけ。英数への切替は JIS の 英数 キー (`kVK_JIS_Eisu`) のみで、US 配列では切り替える手段が無い。

### 0.3 コードの約束ごと (既存に合わせる)

- コメントは**日本語**。「何をするか」より「なぜそうするか」を書く。既存のコメント量に合わせる (関数ごとに 1〜3 行)。
- `src/Meltype.Core` は NativeAOT で dylib / so になる。`Meltype.Mac.Native.csproj` は `JsonSerializerIsReflectionEnabledByDefault=true` と `TrimmerRootAssembly` を付けているので、既存の `ConversionHistory` のようにリフレクションで JSON を読み書きする型を足してもよい。ただし Swift に渡す結果 (`SessionResult.ToJson`) は**手書き**で足す (既存がそうなっている)。
- テストは xUnit ではなく自作ランナー (`src/Meltype.Core.Tests/TestFramework.cs`)。`[Test]` を付けた `static` メソッドを書き、`Assert.True(条件, メッセージ)` (メッセージ必須) / `Assert.Equal(期待, 実際)` を使う。Mac/Linux の経路は `SessionFacadeTests.cs` の `Type()` ヘルパーの書き方に合わせる。
- 実行: `~/.dotnet/dotnet run --project src/Meltype.Core.Tests -- <フィルタ>` (フィルタは「型名.メソッド名」の部分一致)。**この Mac には .NET SDK 10.0.401 が `~/.dotnet` にある**ので、Core のテストはこの Mac で回せる。
- Mac 版の入れ替えは `cd mac && ./build.sh` (dotnet publish → swift build → 組み立て → 既存の Meltype.app があれば rsync で中身だけ入れ替え)。**`~/Library/Input Methods/Meltype.app` を `rm -rf` で消して入れ直さない** (理由は `mac/README.md`「困ったとき」)。`install.sh` は zip を展開したフォルダー用なので開発中は使わない。
- 設定項目を足すときは `Settings.cs` に `[Category, DisplayName, Description]` 付きで足す (Windows の設定画面が属性から自動生成される)。既定値を変えるときは `SettingsVersion` を上げて `Migrate()` に書く。範囲のある数値は `Normalize()` で収める。
- 辞書ファイルは `dictionaries/*.txt` を埋め込み (`DictionarySource.ReadEmbedded`)。ユーザーが `%LOCALAPPDATA%\Meltype\dictionaries\` (Mac: `~/Library/Application Support/Meltype/dictionaries/`) に同名ファイルを置くと追加される、という仕組みを踏襲する。
- Mac 版の検証の注意: `open` で Meltype.app を手で起動しない。入力ソースの状態は入力メニュー (TextInputMenuAgent) で確かめる。
- SDK ヘッダは `/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk` (Xcode.app は無い)。`IMKTextInput` の定義は HIToolbox の `IMKInputSession.h`。

### 0.4 用語

- **変換ボックス**: 未確定の文字列 (`CompositionText`)。
- **文節**: `CompositionController.Clause`。`Candidates` と `Index` を持つ。
- **表示モード**: `DisplayMode` (`Auto` / `Hiragana` / `Katakana` / `FullWidthAlphanumeric` / `HalfWidthAlphanumeric`)。F キーで切り替える。
- **直接入力 (英数)**: `MeltypeSession.Direct == true`。キーをすべてアプリに渡す。
- **関所**: `CaptureGate`。変換ボックスを開くキーかどうか (`StartsComposition`) を見て、Controller に渡すかアプリに素通しするかを決める。

---

## 1. 優先度と実装順

> 2026-10 の戦略計画 (本家との比較、5 本の柱、Phase 0〜3 の施策、リョウさんの決定 8 件) は `docs/STRATEGY_2026-10.md`。以降の新しい項目は、その Phase 表の ID (0-1 など) を書く。

| # | 項目 | 価値 | 大きさ | 依存 |
| --- | --- | --- | --- | --- |
| P1 | F6〜F10 を完備 (F8 半角カナ追加) + Mac の Ctrl 代替キー | 高 (メンテナー要望) | 小 | なし |
| P2 | Mac に「英数」入力モードを追加 (Caps Lock / 入力メニューで切替) | 高 (メンテナー要望・US 配列で必須) | 小〜中 | なし |
| P3 | 予測変換 (ローカル: azooKey / ユーザー辞書 / 補助辞書) | 高 (メンテナー要望) | 中〜大 | P5 |
| P4 | オンライン新語辞書 (インターネットから自動取得) | 高 (メンテナー要望) | 中 + 配信側 | P3 |
| P5 | Mac で azooKey の学習を ON | 高 (使うほど良くなる) | 小 | なし |
| P6 | 候補を数字キー / PageUp・PageDown で選ぶ | 中 | 小 | なし |
| P7 | 句読点・記号の表記設定 | 中 | 小 | なし |
| P8 | Mac: 選択文字をユーザー辞書に登録するメニュー | 中 | 小 | なし |
| P9 | 確定後の再変換 (選択 + Shift+Space) | 中 | 中 | なし |
| P10 | Mozc 側の予測 (Win/Linux) | 中 | 中 (C++) | P3 |
| P11 | Mac: アプリ種別 (コード／一般) 判定、モード表示 | 低〜中 | 中 | P2 |

推奨順: **P5 → P1 → P6 → P2 → P7 → P3 → P4 → P8 → P9 → P10 → P11**。
P5・P1・P6 は半日ずつで終わり、効果がすぐ出る。P3・P4 はまとまった作業なので、P2 まで入れた版を一度テスターに配ってから着手する。

---

## P5. Mac で azooKey の学習を ON (最初にやる)

**目的**: 使うほど文節の区切り・候補順が良くなるようにする。今は Core の `conversions.json` (選び直した候補を先頭にする) しか効いていない。

**受け入れ条件**
- 「きょうは」で 2 番目の候補を選んで確定したあと、同じ読みを変換すると azooKey 自身もその候補を先頭に返す (Core の学習を切っても効く)。
- 学習データが `~/Library/Application Support/Meltype/azooKey/` に書かれ、Meltype を再起動しても残る。

**仕組み (既存)**: Core には学習の口が既にある。`ILearningConverter.Learn(string? context, IReadOnlyList<ConversionClause> clauses)` (`src/Meltype.Core/Composition/KanjiConversion.cs:25`)。`CompositionController.LearnConversion()` (1437 行) が、確定時に `_converter is ILearningConverter` なら**確定した日本語の文節すべて**を渡す。Windows/Linux は `MozcConverter` がこれを実装している。**呼び出しは `ThreadPool.QueueUserWorkItem` 経由なのでメインスレッド以外から来る**。

**変更**
1. `src/Meltype.Mac.Native/Exports.cs`
   - `meltype_init` にコールバックを 1 つ足す: `delegate* unmanaged<byte*, byte*, void> s_learn` (引数: 文脈 or NULL、「読み\t文字列」を改行でつないだ文節の列)。`InitFunction` の型 (Swift 側 `NativeCore.swift`) も合わせる。
   - `CallbackConverter : IKanjiConverter, ILearningConverter` にし、`Learn(context, clauses)` で `s_learn` を呼ぶ。`Exports.cs:18` の「メインスレッドから呼ぶので排他はしない」というコメントは、「`Learn` だけは裏のスレッドから来る (Swift 側でメインスレッドに移す)」と書き足す。
2. `mac/Sources/MeltypeIME/Converter.swift`
   - `ConvertRequestOptions` の `learningType: .nothing` → `.inputAndOutput`。`memoryDirectoryURL` / `sharedContainerURL` は既にそのディレクトリ。
   - `results(for:)` で得た `[Candidate]` を、読みをキーにして直近数件キャッシュしておく (`[String: [Candidate]]`、10 件程度で古いものから捨てる)。
   - `func learn(context: String?, clauses: [(reading: String, text: String)])`: 各文節について、キャッシュから `text` と一致する `Candidate` を探し、`converter.updateLearningData(candidate)` を呼ぶ。全部終わったら `converter.commitUpdateLearningData()` (**これを呼ぶまで保存されない**)。一致する `Candidate` が無い文節は飛ばす (azooKey の候補以外 = ユーザー辞書などから選んだもの)。
   - `KanaKanjiConverter` は**スレッドセーフではない** (`KanaKanjiConverter.swift:110`)。`learnCallback` は `DispatchQueue.main.async { MeltypeConverter.shared.learn(...) }` でメインスレッドに移してから呼ぶ (コールバックの引数の C 文字列は async の前に `String(cString:)` で Swift の文字列にしておく)。
   - `converter.stopComposition()` は今のまま (変換のたびに呼んでよい。学習されなかった理由は `.nothing` と `updateLearningData` 未呼び出し)。
   - API の正確な名前は `mac/.build/checkouts/AzooKeyKanaKanjiConverter/Sources/KanaKanjiConverterModule/ConverterAPI/KanaKanjiConverter.swift` (固定コミット) の `updateLearningData(_ candidate: Candidate)` (495 行付近)、`commitUpdateLearningData()` (515 行付近)。`setCompletedData` は学習ではないので使わない。
3. `NativeCore.swift`: `ClausesCallback` と同じ形で `LearnCallback` の typealias を足し、`initialize()` で渡す。

**テスト**: Core は `CallbackConverter` の変更だけなので既存テストが通ればよい。実機で「きょうは → 2 番目を選んで Enter → Meltype を `pkill -x Meltype` で再起動 → kyouha + Space」で 1 番目が変わることを確認 (再起動して確かめるのは `commitUpdateLearningData` の保存を見るため)。

**実装済み (2026-10-07)**: `Exports.cs` (`s_learn` / `CallbackConverter : ILearningConverter`)、`NativeCore.swift` (`LearnCallback`、メインスレッドへ移す)、`Converter.swift` (`.inputAndOutput`、直近 10 読みのキャッシュ、`learn`) を変更。Core 183/183 通過、`swift build` と NativeAOT の `dotnet publish` (出力は作業用フォルダー) はコンパイル確認済み。
- 未検証: 実機での学習の効き・再起動後の保存 (Meltype.app は入れ替えていない)。
- 計画との差異・注意: `CallbackConverter` は private のため専用の Core テストは追加していない (計画のテスト節どおり既存テストの通過で確認)。キャッシュは「読み」をキーにするので、学習できるのは `candidates(for:)` / 文節の読みと一致する候補のみ。複数文節の文を最初の変換結果のまま確定した場合、文節ごとの読みのキャッシュは無く、一致しない文節は飛ばされる (計画どおり)。

---

## P1. F6〜F10 の完備と Mac の代替キー

### 要件

- 変換ボックスの中で、次の 5 つの表示モードに切り替えられる。何度押しても同じ (トグルではない)。

| キー | モード | 例 (kyouha) |
| --- | --- | --- |
| F6 | ひらがな | きょうは |
| F7 | カタカナ | キョウハ |
| **F8 (新規)** | **半角カナ** | ｷｮｳﾊ |
| F9 | 全角英数 | ｋｙｏｕｈａ |
| F10 | 半角英数 | kyouha |

- Mac では F キーがぶつかりやすいので、Ctrl キーでも同じことができる (変換ボックスが出ているときだけ。空のときの Ctrl+J などはアプリに渡す)。割り当ては **Apple 日本語入力に合わせ** (macOS 26.6 の `/System/Library/PrivateFrameworks/CoreJapaneseEngine.framework/Versions/A/Resources/KeySetting_Default.plist` で確認済み)、Apple に無い半角カナだけ Meltype 独自に足す:

| Ctrl + | 同じ意味の F キー | 由来 |
| --- | --- | --- |
| Ctrl+J | F6 ひらがな | Apple |
| Ctrl+K | F7 カタカナ | Apple |
| Ctrl+L | F9 全角英数 | Apple |
| Ctrl+' (US) / Ctrl+: (JIS) ※キーコード 39 | F10 半角英数 | Apple |
| Ctrl+; ※キーコード 41 | F8 半角カナ | Meltype 独自 (Apple には半角カナの Ctrl キーが無い。Google は Ctrl+Shift+: だが Shift が要るので避けた) |

- 変換中 (候補を選んでいる最中) に F キーを押したときも効く (`SetMode` は `_converting = false` にしてから切り替えるので、既にそうなっている)。
- ヒント文 (変換ボックスの下の操作案内) に F8 を加える。

### 受け入れ条件

1. `kyouha` → F8 → Enter で `ｷｮｳﾊ` が入る。濁点・半濁点付き (`ga` → `ｶﾞ`、`pa` → `ﾊﾟ`) も正しい。小書き (`xya` → `ｬ`)、長音 (`-` → `ｰ`)、句読点 (`、` → `､`、`。` → `｡`、「」 → `｢｣`) も半角になる。
2. Space での変換候補に `ｷｮｳﾊ` がカタカナの次に出る。
3. Mac で `kyouha` → Ctrl+K → Enter で `キョウハ`。変換ボックスが空のときの Ctrl+K はアプリに渡る (TextEdit で何も起きない／そのアプリのショートカットが動く)。
4. Windows・Linux は今までどおり。Linux は IBus が F キーをそのまま渡すので F8 だけ効くようになる (Ctrl+J/K/L も効くが、`;` `'` は `linux/ibus-engine-meltype` の `SYMBOL_KEYS` (78 行) に無く 0x07 で来るので効かない。Linux は対象外でよい)。
5. 既存テストがすべて通る。

### 設計・手順

**(1) `src/Meltype.Core/Input/KeyEvent.cs`**
- `VirtualKeys` に `F8 = 0x77`、`Oem1 = 0xBA` (`;`)、`Oem7 = 0xDE` (`'`) を追加。

**(2) `src/Meltype.Core/Composition/CompositionText.cs`**
- `DisplayMode` に `HalfWidthKatakana` を追加: `public enum DisplayMode { Auto, Hiragana, Katakana, HalfWidthKatakana, FullWidthAlphanumeric, HalfWidthAlphanumeric }`。
- `public static string ToHalfWidthKatakana(string kana)` を `ToKatakana` (935 行) の隣に追加。実装は「`ToKatakana` → 1 文字ずつ表引き」。表 (`private static readonly Dictionary<char, string> HalfWidthKatakanaMap`) に含めるもの:
  - 清音 ア〜ン、小書き ァィゥェォャュョッ、長音 ー→ｰ、中点 ・→･、句読点 、→､ 。→｡、「→｢ 」→｣。
  - 濁音・半濁音は 2 文字に分解: ガ→ｶﾞ (U+FF76 U+FF9E)、パ→ﾊﾟ (U+FF8A U+FF9F)、ヴ→ｳﾞ。
  - 表に無い文字 (漢字・英数字) はそのまま。`StringBuilder` で組み立てる。
- `Display(bool final, ...)` (587 行) の `switch` に `DisplayMode.HalfWidthKatakana => ToHalfWidthKatakana(AllKana(final))` を足す。
- `IsAlphanumericAt` (417 行) は英数モードだけ `true` なので変更不要。`Mode != DisplayMode.Auto` で分岐している箇所 (515, 632, 689, 746 行) は「Auto 以外」の扱いで正しいので変更不要。

**(3) `src/Meltype.Core/Composition/CompositionController.cs`**
- キー処理は `private void HandleKey(KeyEvent e)` (355〜513 行)。472〜475 行の `switch` に `case VirtualKeys.F8: SetMode(DisplayMode.HalfWidthKatakana); return;` を追加。
- `JapaneseCandidates` (1104 行) の `foreach (var kana in new[] { reading, CompositionText.ToKatakana(reading) })` (1114 行) に `CompositionText.ToHalfWidthKatakana(reading)` を足す (カタカナの次)。
- `Learn()` (1426 行) の「かな・カタカナのまま確定したのは覚えない」の条件に `clause.Text == CompositionText.ToHalfWidthKatakana(clause.Reading)` を足す (半角カナも「その場限り」として `conversions.json` に覚えない)。
- 1338 行の `case DisplayMode.Hiragana or DisplayMode.Katakana when ...` に `or DisplayMode.HalfWidthKatakana` を足す (半角カナにした語も「日本語に直した」として `LanguageMemory` に覚える)。1335 行は英数側なので触らない。
- `UpdateView` のヒント (1584 行) を `"Space 変換　←→ 文節　Enter 確定　F7 カタカナ　F8 半角カナ　F10 英字"` にする。
- 156 行のクラスコメントの `F6 / F7 / F9 / F10` を `F6 / F7 / F8 / F9 / F10` に直す。

**(4) Mac の代替キー: `src/Meltype.Core/Composition/MeltypeSession.cs` の `HandleKey` (166 行)**
- `_host.Begin(...)` (168 行) の**直後、`var down = new KeyEvent(vk, ch ?? 0, ...)` (169 行) より前**に次を足す (169 行より後だと `down` に反映されない):

```csharp
// Apple 日本語入力と同じ Ctrl キーで表示モードを切り替える (変換ボックスが出ているときだけ)。
// Mac では F キーが OS のショートカットに取られることがあるため。
if (control && !alt && !command && !shift && _controller.IsComposing && CtrlShortcutToFunctionKey(vk) is { } functionKey)
{
    vk = functionKey;
    ch = null;
    control = false;
}
```
- `private static int? CtrlShortcutToFunctionKey(int vk) => vk switch { 0x4A /*J*/ => VirtualKeys.F6, 0x4B /*K*/ => VirtualKeys.F7, 0x4C /*L*/ => VirtualKeys.F9, VirtualKeys.Oem7 /*'*/ => VirtualKeys.F10, VirtualKeys.Oem1 /*;*/ => VirtualKeys.F8, _ => null };`
- Windows はこの経路を通らない (`MeltypeEngine` がフックで処理) ので影響なし。

**(5) `mac/Sources/MeltypeIME/KeyMapping.swift`**
- 1 段目の `switch Int(event.keyCode)` に `case kVK_ANSI_Quote: return 0xDE` と `case kVK_ANSI_Semicolon: return 0xBA` を追加 (**文字ではなくキーコードで判定**する。US の Ctrl+' は `charactersIgnoringModifiers` が ":" にならないため。JIS では kVK_ANSI_Quote の位置が ":" キー)。既存の 2 段目 (文字での判定) はそのまま。
- F8 は既に `case kVK_F8: return 0x77` がある。

**(6) ドキュメント**: `README.md`・`mac/README.md`・`docs/USAGE.md`・`mac/INSTALL.txt` の「F6 ひらがな / F7 カタカナ / F9 全角英数 / F10 半角英数」に F8 と、Mac の Ctrl キーを追記。`mac/README.md` に「F9 などが効かないときは システム設定 → キーボード → キーボードショートカット で F キーの割り当てを外すか、Ctrl キーを使う」を書く。

### テスト (`src/Meltype.Core.Tests/CompositionTests.cs` と `SessionFacadeTests.cs` に追加)

- `HalfWidthKatakana_F8`: `kyouha` を打って `VirtualKeys.F8` → 表示が `ｷｮｳﾊ`。Enter で確定文字列が `ｷｮｳﾊ`。
- `HalfWidthKatakana_Dakuten`: `gakkou` → F8 → `ｶﾞｯｺｳ`。
- `ToHalfWidthKatakana_Table`: `CompositionText.ToHalfWidthKatakana("ぱん、。「ー」")` == `"ﾊﾟﾝ､｡｢ｰ｣"`。
- `SessionFacadeTests.CtrlK_IsKatakana`: `Type(session, "kyouha")` のあと `session.HandleKey(0x4B, 'k', false, true, false, false)` → `View.Text == "キョウハ"`。変換ボックスが空のときに同じキーを送ると `Consumed == false`。

**実装済み (2026-10-07)**: 計画どおり。Core テスト 188/188 通過 (追加: HalfWidthKatakana_F8 / HalfWidthKatakana_Dakuten / ToHalfWidthKatakana_Table / CtrlK_IsKatakana / CtrlSemicolon_IsHalfWidthKatakana)。`swift build` と Mac.Native の `dotnet build` は成功。未検証: Mac 実機での Ctrl+J/K/L/;/' と F8 の動作 (US/JIS 配列での `kVK_ANSI_Quote` 判定を含む)、`dotnet publish` (NativeAOT) と build.sh による入れ替えは実行していない。

---

## P2. Mac に「英数」入力モードを追加

### 要件

- 入力メニュー (右上) に **「Meltype」と「英数 (Meltype)」の 2 つ**が並ぶ。
- 「英数 (Meltype)」を選ぶと直接入力 (`MeltypeSession.Direct = true`)。「Meltype」に戻すと自動判定に戻る。
- **Caps Lock** で 2 つを行き来できる (Apple 日本語入力と同じ。macOS の「Caps Lock で入力ソースを切り替え」の設定に従う)。**これは未検証**。下の「確認できていないこと」を参照。
- JIS キーボードの 英数 / かな キーを押したときも、入力メニューの表示が追従する (今は内部状態だけ変えていて、メニューは「Meltype」のまま)。
- Windows 版の `DirectModeAutoDetect` (英数状態でローマ字を打ったら日本語に戻す) は **この項目では入れない** (P11 で検討)。

### 受け入れ条件

1. 入力メニューに 2 項目。「英数 (Meltype)」のアイコンは「A」。
2. 「英数 (Meltype)」で `kyouha` と打つと `kyouha` がそのまま入る (変換ボックスは出ない)。
3. Caps Lock を押すと「Meltype」⇔「英数 (Meltype)」が切り替わり、メニューの表示も変わる (未検証。動かなければ、入力メニューとキーでの切替だけで完了とする)。
4. JIS の 英数 キー → メニューが「英数 (Meltype)」に、かな キー → 「Meltype」に変わる。
5. 初回インストールでも更新インストールでも、2 つのモードが有効になる。

### 設計・手順

**(1) `mac/Resources/Info.plist`** (Google 日本語入力の plist の Roman モードを手本にする。`plutil -p "/Library/Input Methods/GoogleJapaneseInput.app/Contents/Info.plist"` で見られる)
- `ComponentInputModeDict` → `tsInputModeListKey` に、**辞書のキー名は標準名 `com.apple.inputmethod.Roman`** で項目を追加 (Apple・Google ともこの標準名をキーにし、`TISInputSourceID` を別に持つ。Caps Lock 切替は標準名で判定されている可能性がある):
  - `TISInputSourceID` = `io.github.yksr-melt.inputmethod.Meltype.Roman`
  - `TISIntendedLanguage` = `en`
  - `tsInputModeScriptKey` = `smRoman`
  - `tsInputModeMenuIconFileKey` / `tsInputModeAlternateMenuIconFileKey` = `icon-roman.tiff` (新規。黒い「A」の 16×16 テンプレート画像。`icon.tiff` と同じ作り方。`TISIconIsTemplate` = true)
  - `tsInputModePrimaryInScriptKey` = **true** (Apple・Google とも true)、`tsInputModeIsVisibleKey` = true、`tsInputModeDefaultStateKey` = true
  - `tsInputModeCharacterRepertoireKey` = `[Latn]`
- `tsVisibleInputModeOrderedArrayKey` を `[…Meltype.Japanese, com.apple.inputmethod.Roman]` にする (辞書のキー名で並べる)。
- トップレベルに `TICapsLockLanguageSwitchCapable` = true を足す。このキーは Apple の `/System/Library/Input Methods/JapaneseIM-RomajiTyping.app/Contents/PlugIns/JapaneseIM-RomajiTyping.appex/Contents/Info.plist` のトップレベルにある (Google・ATOK の plist には無い)。
- `ja.lproj` / `en.lproj` の `InfoPlist.strings` に Roman モードの表示名を追加 (ja: 「英数 (Meltype)」、en: "Alphanumeric (Meltype)")。キー名は既存の Japanese モードの行と同じ形にする。

**(2) `mac/Sources/MeltypeIME/InputController.swift`**
- `override func setValue(_ value: Any!, forTag tag: Int, client sender: Any!)` を追加 (`IMKInputController.h:147` に実在)。`tag == kTextServiceInputModePropertyTag` (`TextServices.h:1186`。Swift では `Int` のはず。ビルドして型を合わせる) のとき `value as? String` がモード ID。`...Meltype.Roman` なら未確定文字を確定 (`apply(NativeCore.shared.commit(session), to: client)`) してから `NativeCore.shared.setDirect(session, true)`、`...Meltype.Japanese` なら `false`。受け取ったモード ID を `private var currentMode: String?` に覚えておく (`IMKTextInput` に「今のモードを読む」API は無い)。最後に `super.setValue(value, forTag: tag, client: sender)` を呼ぶ。
- JIS キーの処理 (`kVK_JIS_Eisu` / `kVK_JIS_Kana`) で、`setDirect` のあとに `client.selectInputMode("io.github.yksr-melt.inputmethod.Meltype.Roman")` / `(...Japanese)` を呼ぶ (**`selectInputMode:`**。`IMKInputSession.h:199`。`selectMode` という名前ではない)。これでメニューが追従する。`selectInputMode` のあとに `setValue(forTag:)` が呼ばれるかは未確認なので、`setDirect` は JIS キーの処理でも呼んだままにする (二重に呼んでも害は無い)。
- `activateServer(_:)` を override し、`currentMode` が分かっていればそれで `setDirect` を同期する。分からなければ `TISCopyCurrentKeyboardInputSource()` の `kTISPropertyInputSourceID` を読む (`Registration.swift` の `find` と同じ要領)。`super.activateServer(sender)` を呼ぶ。

**(3) 有効化: `mac/Sources/MeltypeIME/Registration.swift` と `build.sh` / `install.sh`**
- 通常の初回インストールで有効にしているのは `Registration.enable()` (Japanese モードを `TISEnableInputSource`) なので、**ここに Roman モードも足す** (`find("io.github.yksr-melt.inputmethod.Meltype.Roman")` → `TISEnableInputSource`)。`build.sh` / `install.sh` の `enable_input_source` は登録失敗時などの予備なので、こちらの `defaults write … -array-add` にも `Input Mode` = `com.apple.inputmethod.Roman` (辞書のキー名。Google の設定もこの形) の dict を 1 つ足す。
- 更新インストール (rsync 経路) ではどちらも走らない。Info.plist にモードが増えたときは TIS が読み直さない可能性があるので、`mac/README.md`「困ったとき」に「モードが増えた版に入れ替えたあとは、一度ログアウトしてログインし直す」と書く。

**(4) `mac/README.md` / `mac/INSTALL.txt`**: 「Caps Lock で英数と切り替え (効かないときはメニューから)」「US 配列はメニューか Caps Lock」を追記。

### テスト

- Core の変更は無い。実機で受け入れ条件 1〜5 を順に確認。
- **確認できていないこと** (実装しながら確かめ、ダメなら受け入れ条件 3 を落とす): `TICapsLockLanguageSwitchCapable` を足すだけで Caps Lock 切替が効くか。HIToolbox に非公開シンボル `kTISPlistKeyCapsLockIsSwitchToIMRomanMode` があり、標準名 `com.apple.inputmethod.Roman` のモードが必要な可能性がある (だから辞書のキーを標準名にしている)。`tsInputModeDefaultStateKey = true` だけで新しいモードが有効になるか (ならなければ `Registration.enable()` で有効にする)。

**実装済み (2026-10-07)**。Info.plist (Roman モード・`TICapsLockLanguageSwitchCapable`)、`icon-roman.tiff` (新規。システムフォントの太字「A」から生成した 32×32 の透過 TIFF)、`InfoPlist.strings` (ja/en)、`InputController.swift` (`setValue(forTag:)` / `activateServer` / JIS キー時の `selectMode`)、`Registration.swift` (`enable()` で Roman も有効化)、`build.sh` (icon-roman.tiff のコピー、予備の `defaults write` に Roman 追加)、`install.sh` (同予備)、`mac/README.md`、`mac/INSTALL.txt` を変更。`swift build` は通過。
- 未検証 (実機確認が必要): 受け入れ条件 1〜5 すべて。`build.sh` による入れ替えは実行していない。Caps Lock 切替、更新インストール時にモードが有効になるか (ならなければログアウト・ログインが必要。README に記載済み)。
- 計画との差異: Swift では `selectInputMode:` が `selectMode(_:)` に改名されている (コンパイラが指摘)。呼び出しは `client.selectMode(...)`。`Registration.enable()` の Roman 有効化は失敗しても無視する (日本語モードの有効化を優先)。

---

## P6. 候補を数字キー / PageUp・PageDown で選ぶ

**目的**: 候補ウィンドウに番号が出ているのに押しても何も起きない、を直す。

**受け入れ条件**
- 変換中に `3` を押すと 3 番目の候補が選ばれ、**そのまま確定**する (Apple 日本語入力・Google と同じ)。候補が 3 個未満なら何もしない (キーは消費する)。
- PageDown / PageUp で候補が 9 個ずつ進む／戻る (末尾／先頭で止まる)。
- 変換前 (ひらがな入力中) の数字は今までどおり文字として入る。

**変更**: `CompositionController.HandleConversionKey` (739 行)
- `VirtualKeys` に `Prior = 0x21 (PageUp)`、`Next = 0x22 (PageDown)` と `public static bool IsDigit(int vk) => vk is >= 0x30 and <= 0x39;` を追加。Mac の `KeyMapping` は数字を `0x30-0x39`、PageUp/Down を `0x21/0x22` で既に渡している (Linux も同じ)。
- `switch` の先頭で `var clause = _clauses[_selectedClause];` を取る。
- `case var digit when VirtualKeys.IsDigit(digit) && !shift:` → `if (!clause.IsEnglish && !clause.Expanded) Expand(clause); var n = digit - 0x30; if (n >= 1 && n <= clause.Candidates.Count) { clause.Index = n - 1; clause.Changed = true; Commit(); } return true;`
- `case VirtualKeys.Next:` → `Expand` してから `clause.Index = Math.Min(clause.Index + 9, clause.Candidates.Count - 1); clause.Changed = true; return true;`。`Prior` は `Math.Max(clause.Index - 9, 0)`。
- Mac の `IMKCandidates` は自分でスクロールするので Swift 側の変更は不要 (青いバーは `InputController.updateCandidates` が `moveDown/moveUp` で合わせる。先頭側に戻るときは `target < shownIndex - target` の条件で `update()` してから進める処理が既にある)。

**テスト**: `Digit_SelectsAndCommits` (`kyouha` → Space → `VirtualKeys` 0x32 → 確定文字列が 2 番目の候補)、`PageDown_MovesNine`、`Digit_OutOfRange_DoesNothing`。

実装済み (2026-10-07): `VirtualKeys` に `Prior`/`Next`/`IsDigit` を追加し、`HandleConversionKey` に数字確定と PageUp/Down を実装。テストは `CompositionTests.cs` に `Digit_SelectsAndCommits`・`Digit_BeforeConversion_StaysACharacter`・`Digit_OutOfRange_DoesNothing`・`PageDown_MovesNine_AndStopsAtEnds` を追加 (計画書のテスト名 `PageDown_MovesNine` は末尾/先頭停止も確認するため改名。`kyouha` ではなく `kawa` + `moreCandidates` で候補数を固定)。Swift 側は無変更のため swift build は未実行。**未検証**: Mac 実機での候補ウィンドウの青いバー追従 (ユーザー確認待ち)。

---

## P7. 句読点・記号の表記設定

**目的**: 技術文書・論文で「，．」を指定される人、チャットで「!?」を半角にしたい人が使えるようにする。

**設定** (`Settings.cs`、カテゴリ「1. 全般」)
- `Punctuation`: enum `PunctuationStyle { Japanese /*、。*/, Comma /*，．*/, CommaJapanese /*，。*/ }`。既定 `Japanese`。
- `FullWidthSymbols`: bool。既定 true。false なら `! ? ~` などを半角のまま入れる。
- 「日本語のあとの Space を全角にする」は**この項目には入れない**。日本語を確定したあとの Space は変換ボックスが空で、関所 (`MeltypeSession.StartsComposition` 214 行、Windows は `MeltypeEngine.StartsComposition` 342 行) が Controller に渡さずアプリへ直行するため、関所と `StartWith` の両方を変える必要があり、別項目にする。

**変更**
- `CompositionText.Symbol(char)` (876 行、`private static`) は 175 行の `Append` からしか呼ばれていないので、インスタンスメソッドにして設定を参照する。`CompositionText` に `Func<PunctuationStyle> Punctuation` と `Func<bool> FullWidthSymbols` のプロパティを足し (既存の `CorrectTypos` などと同じ形)、`','` → `Punctuation() switch { PunctuationStyle.Japanese => '、', _ => '，' }`、`'.'` → `{ Japanese or CommaJapanese => '。', Comma => '．' }`。`'!' '?' '~'` と 901 行の「その他の記号を全角に」は `FullWidthSymbols()` が true のときだけ。
- `CompositionOptions` に同名の `Func` を足し、`MeltypeSession.CreateDefault` (119 行) と Windows の `CompositionService` で `settings` から渡す。`CompositionController` のコンストラクタで `_text` に渡す。
- `SymbolsToHalfWidth` (半角候補を作る側) は変更不要。

**テスト**: `Punctuation_Comma` (`Punctuation = Comma` で `kyouha,` → `きょうは，`)、`HalfWidthExclamation`。`CompositionTests.Keyboard` は `CompositionOptions` をコンストラクタ内で組み立てている (175〜200 行) ので、渡せるように引数を足す。

実装済み (2026-10-07): `PunctuationStyle` / `Settings.Punctuation` / `Settings.FullWidthSymbols`、`CompositionOptions` と `CompositionText` の `Func`、`MeltypeSession.CreateDefault`・Windows の `CompositionService`/`TrayApplicationContext` の配線、`CompositionText.Symbol` のインスタンス化を実装。テストは `CompositionTests.cs` に `Punctuation_Comma`・`Punctuation_CommaJapanese`・`HalfWidthExclamation` を追加 (`Keyboard` に `Punctuation`/`FullWidthSymbols` プロパティを足して渡す形。引数ではなくプロパティにした点が計画との差異)。全 195 件通過、`Meltype.Mac.Native` のビルド成功。**未検証/未ビルド**: Windows 側 (`src/Meltype`) は Mac では未ビルド、Swift・NativeAOT publish は未実行 (Swift 無変更)、Mac 実機・Windows 設定画面での表示は未確認。Mac には設定 UI が無いため、設定ファイルの `Punctuation` / `FullWidthSymbols` を直接書き換えて確認する必要がある。

---

## P3. 予測変換 (ローカル)

### 要件

- ひらがなを **2 文字以上**打つと、変換ボックスの下に予測候補 (最大 8 個) が出る。例: `おせ` → 「お世話になっております」「お世話になります」…
- 予測候補の出どころ (この順に並べ、重複を除く):
  1. ユーザー辞書 (`userdict.txt`) の読みが前方一致する語 (`UserDictionary.Words`。同梱の phrases.txt は含まれない。それでよい)
  2. オンライン新語辞書 (P4) の前方一致
  3. 変換エンジンの予測 (Mac: azooKey `predictionResults`。Win/Linux: P10 で Mozc。それまでは空)
  4. Core の変換履歴 (`conversions.json`) で読みが前方一致するもの
- 操作: **Tab** で予測候補に入る (最初の候補が選ばれる)。↓↑ で移動、**Enter で確定**、Esc か文字キーで予測から抜けて入力に戻る。Space は今までどおり「変換」(予測は無視)。
- **英語と判定した語には予測を出さない**。表示モードが Auto / Hiragana のときだけ。
- 設定 `Prediction` (bool、既定 true)、`PredictionMinLength` (int、既定 2、`Normalize` で 1〜5)。
- Windows の変換ボックス (`CompositionWindow`) で予測を描く部分は **この計画の範囲外** (Core は `View.Predictions` を返すだけにし、描画は作者側に任せる。描画しなくても壊れない)。

### 受け入れ条件

1. Mac で `osewa` と打つと候補ウィンドウに予測が出る。Tab → Enter で「お世話になります」などが確定する。
2. `google` と打っても予測は出ない (英語判定)。
3. Tab で予測に入ったあと文字を打つと、予測から抜けて入力が続く。
4. 予測を OFF にすると出ない。既存テストが通る (`SessionFacadeTests.Json_IsEscaped` は `ToJson` を完全一致で比べているので期待値を更新する)。

### 設計

**(1) Core の口**
- `CompositionOptions` に `Func<string, IReadOnlyList<string>>? Predictions { get; init; }` (読み → 予測候補) と `Func<bool> Prediction = () => true`、`Func<int> PredictionMinLength = () => 2` を追加。
- `CompositionView` (14 行) の末尾に `IReadOnlyList<string>? Predictions = null, int SelectedPrediction = -1` を追加 (既存の呼び出しは位置引数なので壊れない)。
- `SessionResult.ToJson` (21 行) に `"predictions":[…],"selectedPrediction":n` を手書きで追加 (`AppendArray` を使う)。`Json_IsEscaped` テストの期待値を直す。

**(2) `CompositionController`**
- 状態: `private bool _predicting; private int _predictionIndex; private List<string> _predictions = [];`。
- 予測の取得: `UpdateView` (1557 行) の「変換前」の分岐 (1582 行) で、`_options.Prediction() && _text.Mode is DisplayMode.Auto or DisplayMode.Hiragana && !_text.IsAlphanumeric && !EndsWithEnglish(final: false)` のとき、`var reading = _text.AllKana(final: false)` が `PredictionMinLength()` 以上なら `_predictions = CollectPredictions(reading)`。それ以外は空リスト。
- `CollectPredictions(string reading)`: 上の 1〜4 の順。1 は `_options.UserDictionary?.Words.Where(w => w.Reading.StartsWith(reading, StringComparison.Ordinal)).Select(w => w.Word)`。2 は `_options.Online?.Prefix(reading, 8)` (P4)。3 は `_options.Predictions?.Invoke(reading)`。4 は `ConversionHistory` に `IEnumerable<(string Reading, string Text)> StartingWith(string prefix)` を足す (内部の `Dictionary<string, Entry> _entries` を走査。5000 件なので線形で十分)。`Distinct` で重複を除き 8 個まで。**読み自体と同じ文字列 (ひらがなのまま) は除く**。
- キー処理 (`HandleKey` 355 行、`switch (vk)` は 428 行):
  - `case VirtualKeys.Tab when !_converting && _predictions.Count > 0:` → `_predicting = true; _predictionIndex = 0; return;`。既存の `Tab when FindMisspelling()` (464 行) / `Tab when _text.Suggestion()` (468 行) より **後ろ**に置く (もしかして・提案が優先)。
  - `_predicting` のとき (`switch` の前に `if (_predicting && HandlePredictionKey(vk)) return;` を置く): `Down`/`Tab` → index+1 (末尾で止まる)、`Up` → index-1 (0 未満なら `_predicting = false`)、`Return` → 確定: `var text = _predictions[_predictionIndex]; var reading = _text.AllKana(final: false); CommitText(text, english: false, _text.Raw); _options.History?.Remember(reading, text);` (`CommitText` が `_text.Clear()` まで行う)、`Escape` → `_predicting = false` (キーは消費)、それ以外 → `_predicting = false` にして `false` を返し通常処理へ。
  - `Space` は `_predicting` を false にしてから通常の変換へ (上の「それ以外」で済む)。
- リセット: `CommitText` (確定はすべてここを通る)、`Reset()`、Escape の処理 (462 行)、`BeginComposition` で `_predicting = false; _predictions = [];`。
- `UpdateView`: 変換前の `CompositionView` に `Predictions: _predictions, SelectedPrediction: _predicting ? _predictionIndex : -1` を渡す。
- `MeltypeSession.SelectPrediction(int index)` を `SelectCandidate` (199 行) と同じ形で追加 (Controller に `internal void SelectPrediction(int index)` を足し、Enter と同じ確定をする)。

**(3) Mac: `mac/Sources/MeltypeIME/Converter.swift`**
- `ConvertRequestOptions` の `requireJapanesePrediction` を **`.manualMix`** にする (`PredictionMode` は `.autoMix / .manualMix / .disabled` の 3 つ。`.enabled` は無い。`.autoMix` だと `mainResults` に予測が混ざって今の変換結果が変わるので使わない)。`ConvertRequestOptions.swift:12-15` 参照。
- `func predictions(for hiragana: String) -> [String]`: `converter.requestCandidates(composing, options:)` の `predictionResults` から `text` を取り、`hiragana` と同じものを除いて 8 個まで。`results(for:)` と同じ `ComposingText` の作り方。`stopComposition()` も同様に呼ぶ。
- `NativeCore.swift` / `Exports.cs`: `meltype_init` にコールバック `s_predictions` (読み → 改行区切り) を追加 (P5 の `s_learn` と同時に足すと FFI の変更が 1 回で済む)。`Exports.Create` で `CompositionOptions.Predictions` に配線 (`MoreCandidates` と同じ書き方)。`MeltypeSession.CreateDefault` の引数に `predictions` を足す。`meltype_select_prediction(handle, index)` を `meltype_select_candidate` と同じ形で追加。
- `NativeCore.swift` の `CompositionView` に `let predictions: [String]`, `let selectedPrediction: Int` を追加 (`Decodable`)。`selectPrediction(session, index:)` を追加。
- `InputController.updateCandidates`: 変換中でなく `view.predictions` が空でないときも候補ウィンドウを出す。`candidateList = view.predictions`。`selectedPrediction == -1` のときは `window.show()` だけして `moveDown` しない (IMKCandidates に「非選択」は無いので先頭に青いバーが乗る。許容する)。`candidateSelected` (クリック) は `view.converting == false` のとき `NativeCore.shared.selectPrediction(session, index:)` を呼ぶ (直前の `view` を `lastView` として持っておく)。

**(4) 設定**: `Settings.cs` に `Prediction` (「予測変換」) と `PredictionMinLength`。`Normalize()` で 1〜5 に収める。

### テスト (Core)

- `CompositionTests.Keyboard` のコンストラクタに `Func<string, IReadOnlyList<string>>? predictions` を足し、`Predictions = r => r.StartsWith("おせ", StringComparison.Ordinal) ? ["お世話になります"] : []` を渡す。
- `Prediction_TabEnterCommits`: `osewa` → Tab → Enter → 確定 `お世話になります`。
- `Prediction_NotForEnglish`: `google` → `View.Predictions` が空。
- `Prediction_TypingLeaves`: `osewa` → Tab → `n` → `View.SelectedPrediction == -1` (`_predicting` は private なので View で確かめる) かつ `View.Text == "おせわn"` 相当。
- `Prediction_FromUserDictionary`: `UserDictionary` に `きごうとう → 記号等` を入れて `kigou` → 予測に `記号等`。

**実装済み (2026-10-07)**: P4 (オンライン辞書) は実装しない方針 (メンテナー判断) のため、`Online` / `OnlineDictionary` に関わる部分は全て省き、予測の出どころは「ユーザー辞書 → 変換エンジン → 変換履歴 (conversions.json)」の 3 つにした。Core テスト 204/204 通過 (追加 8 件: `Prediction_TabEnterCommits` / `_NotForEnglish` / `_TypingLeaves` / `_FromUserDictionary` / `_FromHistoryAndOrder` / `_CommitRemembersAndSpaceConverts` / `_OffAndMinLength` / `_EscapeReturnsToTyping`。`Json_IsEscaped` の期待値を更新)。`dotnet publish` (Mac.Native, NativeAOT) と `swift build -c release` は通った。Meltype.app の入れ替えはしていない。
- 計画との差異: (1) Swift の `predictions(for:)` は、通常の変換結果と学習に影響しないよう、`options` を既定の `.disabled` のまま残し、予測変換のときだけコピーを `.manualMix` にして要求する (計画は `options` 自体を `.manualMix` にする)。(2) 予測を確定すると `History.Remember(読み全体, 語)` で覚える (計画どおり)。履歴の並びは新しく使った順。(3) Windows の `CompositionService` は予測を描かないので `Prediction = () => false` で予測変換を止めた (見えない候補に Tab で入ってしまうのを防ぐため)。Settings の説明にも「現在は Mac 版だけで表示」と記した。
- 未検証: Mac の実機での表示と操作 (`osewa` → Tab → Enter、候補ウィンドウのクリック、Tab で入る前に先頭に青いバーが乗る見た目、`IMKCandidatesSendServerKeyEventFirst` で Tab・矢印・Enter が本体に届くか、azooKey の予測の中身と速度)。Windows 版 (`src/Meltype`) はこの Mac では restore できずビルド未確認 (変更は `CompositionService.cs` の 1 行のみ)。Linux は予測の配線なし (P10 待ち)。

---

## P4. オンライン新語辞書 (インターネットから自動取得)

### 要件

- 「ChatGPT」「生成 AI」「Claude」のような**新しい語を、ユーザーが登録しなくても変換・予測で出せる**ようにする。
- **打った内容は一切送らない**。やるのは「辞書ファイルを 1 つダウンロードする」だけ (HTTP GET、ETag で変更が無ければ本文を取らない)。これは Meltype の約束 (README「ネットワークに何も送らない」) に関わるので、**既定 OFF (オプトイン)** にし、ON にする設定の説明に「辞書を GitHub から取得する。入力内容は送らない」と明記する。
- 辞書は Meltype の GitHub リポジトリで配信する (作者側の GitHub Actions が週 1 回作る)。URL は設定で変えられる (社内配信用)。
- 取得は **週 1 回**、起動後 5 分以降にバックグラウンドで。失敗しても黙って次回。
- 形式は**ユーザー辞書と同じ「読み\t単語」** (3 列目に任意の注記。例: `ちゃっとじーぴーてぃー\tChatGPT\tAI`)。ユーザー辞書より**後ろ**、変換エンジンの候補より**前**に出す。

### 受け入れ条件

1. 設定 `OnlineDictionaryEnabled = true` にして次回の取得後、`ちゃっとじーぴーてぃー` + Space で `ChatGPT` が候補に出る。予測でも `ちゃっと` で `ChatGPT` が出る。
2. OFF のときは一切ネットワークに出ない (ログに取得の記録が無い)。
3. ファイルが壊れていても (途中で切れた・文字化け) 落ちず、前のファイルを使い続ける。
4. ユーザーが `dictionaries/online/trending.txt` を自分で置き換えてもよい (次の自動取得で上書きされることをコメントに書く)。

### 設計

**名前は次に統一する**: 設定 `OnlineDictionaryEnabled` / `OnlineDictionaryUrl` / `OnlineDictionaryUpdateHours`。クラス `OnlineDictionary` (`Load`, `Lookup`, `Prefix(prefix, limit)`, `Count`, `Reload`)、`OnlineDictionaryUpdater`。

**(1) クライアント側 (Core): `src/Meltype.Core/Composition/OnlineDictionary.cs` (新規)**
- `public sealed class OnlineDictionary`:
  - `static OnlineDictionary Load(string directory)`: `directory/online/trending.txt` を読む。行の読み方は `UserDictionary.Parse` と同じにする。`UserDictionary.Parse` は `private static` (44 行) なので **`internal static` に変えて共用する** (`#` 行は読み飛ばすので 1 行目のヘッダーは問題ない)。
  - 内部: `_byReading: Dictionary<string, List<string>>` と、前方一致用に `_sortedReadings: string[]` (`Array.Sort(…, StringComparer.Ordinal)`。`Array.BinarySearch` で開始位置を探し、前方一致する間だけ進める)。
  - `IReadOnlyList<string> Lookup(string reading)`、`IEnumerable<string> Prefix(string prefix, int limit)`、`int Count`。
  - 読み直しは、新しいインスタンスを作って差し替える (フィールドを `volatile` にして `Interlocked.Exchange`)。変換結果のキャッシュ (`_conversionCache`) は読み直し後に捨てたいが、Core でキャッシュのキーに使えるのは `History.Version` (1279 行) だけなので、`OnlineDictionary` に `Version` を持たせ、`CompositionController` で `_liveCache` / `_conversionCache` を作るときのキーに `Online?.Version` を混ぜる (`History.Version` と同じ使い方)。
- `public sealed class OnlineDictionaryUpdater`:
  - コンストラクタ `(Func<Settings> settings, string directory, Action onUpdated)`。
  - `Start()`: `System.Threading.Timer` で起動 5 分後、その後 6 時間ごとに `Check()`。`OnlineDictionaryEnabled` が false なら何もしない。
  - `Check()`: `directory/online/trending.etag` と `trending.last` (最終取得時刻、`DateTime.UtcNow.ToString("o")`) を読み、`OnlineDictionaryUpdateHours` 未満なら戻る。`HttpClient` (`Timeout = 15 秒`、`User-Agent: Meltype/<ProjectInfo.CoreVersion>`、`If-None-Match: <etag>`) で GET。304 なら `last` だけ更新。200 なら本文を `trending.txt.tmp` に書き、`Validate(tmp)` を通ったら `File.Move(tmp, trending.txt, overwrite: true)`、`etag` と `last` を保存、`onUpdated()` を呼ぶ。例外は `Log.Warn` して握りつぶす。
  - `internal static bool Validate(string path)`: UTF-8 として読める・1 行目が `# Meltype online dictionary` で始まる・2 列以上の行が 1 つ以上・サイズ 5MB 以下。
  - NativeAOT: `HttpClient` はそのまま使える。`Meltype.Core.csproj` に追加の参照は不要。
- `CompositionOptions` に `OnlineDictionary? Online { get; init; }` を追加。`JapaneseCandidates` (1104 行) のユーザー辞書の `foreach` (1108 行) の**直後**に `foreach (var word in _options.Online?.Lookup(reading) ?? []) if (!candidates.Contains(word)) candidates.Add(word);`。P3 の `CollectPredictions` の 2 番目でも使う。
- `MeltypeSession.CreateDefault` と Windows の `CompositionService` で `OnlineDictionary.Load(AppPaths.UserDictionaryDirectory)` を作って渡し、`OnlineDictionaryUpdater` を `Start()` する。`onUpdated` で `Reload()`。
- `AppPaths` に `OnlineDictionaryDirectory => Path.Combine(UserDictionaryDirectory, "online")` を追加。
- **設定** (`Settings.cs`、カテゴリ「1. 全般」の末尾):
  - `OnlineDictionaryEnabled` (bool、既定 **false**)。DisplayName「新しい語の辞書をインターネットから取得」。Description「Meltype の GitHub から週 1 回、新しい語 (AI 関連の用語・新サービス名など) の辞書をダウンロードします。**入力した内容は送りません** (辞書ファイルを受け取るだけ)。取得先は OnlineDictionaryUrl で変えられます。」
  - `OnlineDictionaryUrl` (string、既定 `https://raw.githubusercontent.com/yksr-melt/Meltype/dictionaries/online/trending.txt`。**実装時に作者と配信 URL を決めてから確定する**。それまでの仮の値)。
  - `OnlineDictionaryUpdateHours` (int、既定 168、`Normalize()` で 24〜720)。
- README (「ネットワークに何も送らない」の節) に「オプションの新語辞書だけはダウンロードする (既定 OFF)」を追記。`THIRD-PARTY-NOTICES.md` に Wikipedia/Wikidata (CC BY-SA 4.0 / CC0) を追記。

**(2) 配信側 (作者のリポジトリの GitHub Actions): `tools/make-trending.mjs` (新規) と `.github/workflows/trending.yml`**
- 既存の `tools/make-*.mjs` と同じ Node スクリプトの形 (入力 URL をダウンロード → 整形 → `dictionaries/online/trending.txt` を出力)。
- **データ源** (すべて無料・機械取得可。順に重ねる):
  1. 手書きの種 `dictionaries/online/seed.txt` (読み\t語\t注記)。AI 関連の主要な語をまず 100〜200 語入れておく (ChatGPT、Claude、Gemini、生成AI、LLM、プロンプト、RAG、Copilot、Sora、Midjourney…)。**これがあるだけで要望の大半は満たせる**ので、自動収集が動く前に出す。
  2. Wikipedia 日本語版の**人気記事** (Wikimedia Pageviews API `top/ja.wikipedia/all-access/{年}/{月}/all-days`) 上位 2,000 件と、**新規記事** (`list=recentchanges&rctype=new&rcnamespace=0` 直近 30 日) のタイトル。
  3. 読みは **Wikidata** から: 各記事の Wikidata 項目の `ja` の「かな」エイリアス、無ければ記事本文の先頭にある `（よみ、…）` パターン (`^.*?（([ぁ-んー]+)[、）]`) を Wikipedia の `prop=extracts&exintro` から取る。読みが取れない語は捨てる。
  4. フィルタ: 読みが 2〜20 文字、語が 1〜30 文字。人物 (Wikidata `P31 = Q5`) は最初はすべて除く (存命の一般人を入れないため)。既存辞書・変換エンジンとの重複はビルド環境で判定できないので**除かない** (Core 側の `Distinct` で消える)。
  5. 出力の 1 行目は `# Meltype online dictionary <生成日> <件数>` (クライアントの `Validate` に使う)。
- Actions: 毎週月曜 `cron`、`node tools/make-trending.mjs` → 差分があれば `dictionaries/online/trending.txt` をコミット (配信ブランチ `dictionaries` に push。本体のブランチを汚さない)。失敗したら前の版が残るだけ。
- **これは作者のリポジトリでしか動かない**。メンテナーが試すなら、自分のフォークの raw URL を `OnlineDictionaryUrl` に入れる。

### テスト

- `OnlineDictionaryTests.cs` (新規): 一時ディレクトリに `online/trending.txt` を書いて `Load` → `Lookup("ちゃっとじーぴーてぃー")` が `["ChatGPT"]`、`Prefix("ちゃ", 8)` に含まれる。壊れたファイル (バイナリ) で `Load` しても例外にならず `Count == 0`。
- `Validate`: 1 行目が違う・サイズ超過で false。ネットワークを叩くテストは書かない。
- 統合: `CompositionTests` で `Online` を渡し、`JapaneseCandidates` に語が入ることを確認。

---

## P8. Mac: 選択した文字をユーザー辞書に登録するメニュー

**要件**: 入力メニューに「選択中の文字をユーザー辞書に登録…」。選択範囲を語にし、読みを入力する小さなダイアログ (NSAlert + NSTextField) を出す。読みはひらがなで 2 文字以上 (`UserDictionary.MinReadingLength`)。登録後は次の変換から効く。

**変更**
- `InputController.menu()` に項目追加。`@objc func registerWord`: `client.selectedRange()` と `attributedSubstring(from:)` で語を取る (無ければ空欄で開く)。
- Core: `MeltypeSession` は `CompositionOptions` を保持していないので、コンストラクタで受けた `options` をフィールドに持ち、`public string? AddUserWord(string reading, string word) => _options.UserDictionary?.Add(reading, word)` を足す (`CompositionOptions.UserDictionary` は public)。
- FFI: `Exports.cs` に `meltype_add_user_word(handle, reading, word) -> byte*` (エラー文字列か NULL)。`NativeCore.swift` に対応する関数。

**テスト**: 実機で登録 → 変換に出ることを確認。Core は `UserDictionary` の既存テストで足りる。

**実装済み (2026-10-07)**: `MeltypeSession.AddUserWord`・`meltype_add_user_word`・`NativeCore.addUserWord`・`InputController.registerWord` を追加。計画との差異: Core テスト `SessionFacadeTests.AddUserWord_RegistersOrReturnsReason` を 1 件追加 (205/205 通過)。`swift build` と `dotnet build src/Meltype.Mac.Native` は通過、NativeAOT の `dotnet publish` は未実行。ダイアログの表示 (NSAlert の前面表示・入力欄のフォーカス・選択範囲の取得) と登録後に変換へ出ることは **未検証** (実機確認はメンテナー)。読みのひらがな検査は Core に無いため行っていない (2 文字以上のみ)。

**P8 査読の指摘を修正済み (2026-10-07)**: (1) `CompositionController.LiveConvert` のキャッシュキーに `UserDictionary.Version` を追加 (`_conversionCache` はエンジン出力だけでユーザー辞書に依存しないので変更なし)。登録直後の次の変換に語が出るテスト `UserDictionary_RegisterInvalidatesLiveCache` を追加。(2) `UserDictionary.Shared(path)` でパスごとの共有インスタンスにし、`MeltypeSession.CreateDefault` はこれを使う。登録・削除は lock で直列化。2 つの取得元から登録しても両方残るテスト `UserDictionary_SharedInstance_KeepsBothRegistrations` を追加 (220/220 通過)。未検証: 別プロセス (Windows 版トレイと IME など) 間の共有はしていない (プロセス内のみ)。Windows の `CompositionService` は従来どおり自前のインスタンス。

---

## P9. 確定後の再変換 (選択 + Shift+Space)

**要件**: 確定した文字列を選択して **Shift+Space** を押すと、その文字列を読みに戻して変換を始める (選択範囲を置き換える)。読みに戻せないときは何もしない (キーをアプリに渡す)。

**読みの求め方** (Mac。順に試す):
1. ひらがな・カタカナだけならそのまま (カタカナはひらがなに)。
2. Core の変換履歴 `conversions.json` の逆引き (文字列 → 読み)。自分が直前に確定した誤変換を直す用途ではこれが一番当たる。`ConversionHistory` に `string? ReadingOf(string text)` を足す。
3. `meanings.txt` (書き方\t読み\t意味) の逆引き。読みが空の行は飛ばす。`MeaningDictionary` に `ReadingOf` を足す。**`readings.txt` は「読み\t活用の種類」でかなだけなので逆引きには使えない。**
4. macOS の `CFStringTokenizer` + `kCFStringTokenizerAttributeLatinTranscription` (漢字混じりの日本語のローマ字読みを返す) → ローマ字をひらがなに (`CFStringTransform` の `kCFStringTransformLatinHiragana`)。Swift 側のコールバック `s_reading` として Core に渡す。**精度は未検証** (同音異義語の読み違いはあり得る)。azooKey の固定コミットには逆変換 API が無い。Mozc には `StartReverseConversion` があるので Win/Linux はそれを使える (この計画では Mac だけ)。

**設計**
- Mac: `InputController.handle` で、変換ボックスが空・Shift+Space・`client.selectedRange().length > 0` のとき、選択文字列を取り、`NativeCore.shared.reconvert(session, text)` (新 FFI `meltype_reconvert`) を呼ぶ。結果の `commits` は `deleteBefore` ではなく `replacementRange` に選択範囲を使って入れる (`apply` に置換範囲を渡せるよう引数を足す)。
- Core: `MeltypeSession.Reconvert(string text)` → 読みを求めて `_controller.ReconvertKana(kana)` (新規 `internal`。`BeginComposition` と `StartConversion` は private なので、この中から呼ぶ)。`CompositionText` に `SetKana(string)` を足して読みを入れる。
- **関所**: 再変換で開いた変換ボックスは、関所 (`CaptureGate`) が「開いている」と知らないので、続く Space などが `StartsComposition == false` でアプリへ素通りする。`CaptureGate` に「関所を閉じる (変換中にする)」public メソッドを足し、`Reconvert` から呼ぶ。`CaptureGate.cs` を読んで、`OnKey` が変換中をどう判定しているかに合わせる。
- Windows は #19 の事情 (変換キーを握りつぶしている) があるので、この計画では Mac だけ。

**テスト**: Core に `Reconvert_HiraganaStartsConversion` (`Reconvert("きょうは")` → `View.Converting == true`)、`ConversionHistory_ReadingOf` (Remember したものを逆引き)、`Reconvert_ThenSpace_CyclesCandidates` (関所が閉じていることの確認)。

**実装済み (2026-10-07)**: `MeltypeSession.Reconvert`・`CompositionController.ReconvertKana`・`CompositionText.SetKana`・`CaptureGate.Capture`・`ConversionHistory.ReadingOf`・`MeaningDictionary.ReadingOf`・FFI `meltype_reconvert`・`NativeCore.reconvert`・`InputController.handle` の Shift+Space 分岐 (選択範囲は `apply(replacing:)` で `setMarkedText` の replacementRange に使う) を追加。計画との差異: (1) 読みの 4 番目 (CFStringTokenizer) の Swift コールバックは `meltype_init` の引数を増やさず、別 FFI `meltype_set_reader` で登録し、Core 側は `MeltypeSession.ReadingProvider` (`CreateDefault` の引数 `reader`) で受ける。(2) 再変換の結果は commits が空で、選択範囲の置換は変換中の文字の表示 (`setMarkedText`) 側で行う。(3) Core テストは `SessionFacadeTests` に 8 件追加 (213/213 通過)。`swift build` と `dotnet build src/Meltype.Mac.Native` は通過、NativeAOT の `dotnet publish` と `build.sh` は未実行 (IME の入れ替え回避のため)。**未検証 (実機確認はメンテナー)**: 選択範囲が変換中の文字に置き換わること (アプリにより `replacementRange` の扱いが違う可能性)、Shift+Space が OS 側のショートカットに取られないか、CFStringTokenizer の読みの精度 (同音異義語は読み違えうる)、再変換後に Esc で取り消したとき元の文字が戻るか (下記の修正)。

**修正済み (2026-10-07)**: 再変換して Esc で取り消すと元の文字が消える問題を直した。`CompositionController.ReconvertKana(kana, original)` が再変換した元の文字列を `_reconvertOriginal` に持ち、変換ボックスを空にする Esc (1 回目の Esc は変換を戻して読みに、2 回目で空にして取り消す) のとき、元の文字を commit として返す (学習・文脈の記録はしない)。確定・`Reset`・新しい入力の開始で元の文字は捨てるので、古い文字が後から出ることはない。通常の入力の Esc は変更なし。Swift 側は変更不要 (commit は `replacementRange` なしの `insertText` で、変換中の文字 = 元の選択範囲の位置を置き換える)。テストは `SessionFacadeTests` に 4 件追加 (224/224 通過)。**未検証 (実機確認はメンテナー)**: アプリによって Esc 後の `insertText` が変換中の文字を正しく置き換えるか。

---

## P10. Mozc の予測 (Win/Linux)

- `native/mozc/meltype_mozc_helper.cc` に命令 `P` を追加。入力は `C` と同じ「`P\t文脈\t読み`」(文脈は空でよい)、出力は候補を `\x1f` (kUnitSeparator) で区切った 1 行。
- 実装: `Request()` (139 行) は `request_type = CONVERSION` 固定で、`StartPrediction` には `DCHECK(ValidateConversionRequestForPrediction)` があるので、**`request_type = PREDICTION` の `ConversionRequest` を別に作る** (`PredictionRequest()`)。`Start()` と同じく `composer_.Reset(); composer_.SetPreeditTextForTestOnly(reading);` のあと `converter_->StartPrediction(PredictionRequest(), &segments)` (a069a88 に実在)。`segments.conversion_segment(0)` の候補 `value` を返す。
- `MozcConverter.cs` に `Predict(string reading)` を追加し、`CompositionOptions.Predictions` に配線 (Windows: `CompositionService`、Linux: `Exports.Create`)。
- Mozc のビルドは Windows は作者の環境、Linux は `native/mozc/build-mozc-helper.sh`。C++ の変更は作者に PR で渡す前提。

---

## P11. Mac: アプリ種別判定・モード表示 (後回し)

- `InputController.activateServer` で `client.bundleIdentifier()` を取り、Core の `Settings.ProfileFor(name)` / `KindFor(name)` / `IsAppEnabled(name)` (public。`FindRule` は private) で `Code` プロファイルを選ぶ。`AppRules.Process` に bundle ID を入れる。
- モード表示は、候補ウィンドウの注釈 (`showAnnotation`) に「あ／A」を出すのが最小。

**実装済み (2026-10-07)**: `MeltypeSession.SetApp` / `AppName` / `AppProfile` / `AppEnabled`・`Settings.HasAppRule`・FFI `meltype_set_app`・`NativeCore.setApp` (`AppKind`)・`InputController.activateServer` で `client.bundleIdentifier()` を渡す・候補ウィンドウの注釈にモード表示 (`showModeAnnotation`) を追加。動き: アプリ別設定 (プロセス名の欄に bundle ID を書く) で OFF / 種類「ゲーム」のアプリは `HandleKey`・`Reconvert` とも素通し、種類「コード」(と独自の種類で「最初は英数」) のアプリは `Direct = true` で始める。計画との差異: (1) 既定のアプリ別設定は Windows の exe 名なので、Mac 用に `MeltypeSession.MacCodeApps` (Terminal・iTerm2・VS Code・Cursor・Xcode・JetBrains など) を足し、アプリ別設定に行が無いアプリだけ「コード」にした (行があればそちらが優先)。(2) 「コード」では Windows のような行ごとのコメント／文字列判定はせず、英数から始めるだけ (Mac には行の文脈を取る手段が無いため)。(3) モード表示は候補ウィンドウの注釈に「あ」(コードのアプリでは「あ (コード)」) を出すのみ。「A」は、英数では候補ウィンドウが出ないので出せない。意味の注釈 (1.5 秒後) が出たらそちらに替わる。(4) Core テストは `SessionFacadeTests` に 5 件追加 (218/218 通過)。`swift build` と `dotnet build src/Meltype.Mac.Native` は通過、NativeAOT の `dotnet publish` と `build.sh` は未実行 (IME の入れ替え回避のため)。**未検証 (実機確認はメンテナー)**: `bundleIdentifier()` が各アプリで期待どおり取れるか、`activateServer` が入力欄を移るたびに呼ばれるか、「コード」のアプリで入力メニューの表示が Meltype のまま英数動作になる (見た目と動きがずれる) ことが許容できるか、注釈の表示が出るか (`showAnnotation` は変換候補が 2 つ以上のときだけ)。

**追補 (2026-10-07)**: OFF / ゲームのアプリで JIS の英数/かなキーを Meltype が処理してしまう問題を修正済み。`InputController.handle` の先頭で `appKind == .disabled` なら全キー (英数/かな含む) を `return false` で素通しする (Swift 側のみの変更、Core 変更なし)。未検証 (実機確認はメンテナー): 無効アプリで英数/かなキーが OS 標準の動きになるか。

---

## 追加項目 P12 (辞書提案)・P13 (予測の頻度重み付け)

### P12. ユーザー辞書への登録提案

**要件**
- 確定した「読み → 語」を数える。対象は、ユーザー辞書に未登録で、漢字・カタカナ・英字を含み、変換エンジンの 1 番目の候補ではなく**選び直した語** (前に選び直して学習した語をそのまま確定したときも数える)。除外: ひらがなだけの語、読みが `UserDictionary.MinReadingLength` 未満・ひらがなでないもの、英数 (直接入力) モード、アプリ別設定で OFF のアプリ (どちらも Core にキーが届かない)、16 文字以上の英数字だけの語 (パスワードの疑い)、「登録しない」にした語。
- 同じ組を 3 回 (設定 `DictionarySuggestThreshold`、既定 3、2〜10) 確定したら「提案待ち」。設定 `DictionarySuggest` (既定 ON) で全体を切れる。
- 保存: `suggest.json` (conversions.json と同じ場所)。読み・語・回数・却下したかだけ (前後の文章は保存しない)。上限 500 件で古い順に捨てる。一時ファイル + 置き換えで書く (ConversionHistory と同じ)。
- Mac: 入力メニューに提案待ちを最大 3 件「『語』を辞書に登録 (読み)」で並べ、各提案の下に「『語』は登録しない」を置く。「辞書の登録提案の履歴を消去」も置く。提案待ちができた直後の確定で、入力位置の下に 1 行ヒントを数秒出す (1 日 1 回まで。候補ウィンドウの注釈とは別の小さなパネル)。
- Core: `MeltypeSession.PendingSuggestions / AcceptSuggestion / RejectSuggestion / ClearSuggestions / TakeSuggestionHint`、FFI は `meltype_suggestions / meltype_suggest_accept / meltype_suggest_reject / meltype_suggest_clear / meltype_suggest_hint`。

**変更**: `Composition/DictionarySuggestions.cs` (新規)、`CompositionController` (`RecordSuggestions`、`CompositionOptions.Suggestions` ほか)、`MeltypeSession`、`Settings`、`AppPaths.DictionarySuggestionFile`、`Exports.cs`、`NativeCore.swift`、`InputController.swift`、`SuggestionHint.swift` (新規)。

### P13. 予測変換の使用頻度による重み付け

**要件**
- `ConversionHistory` の項目に `Count` (確定回数) を足し、`Used` (最終使用時刻) と合わせて頻度スコア = 回数 × 0.5^(最後に使ってからの日数 / 30) を出す (30 日で半分。回数だけだと古い語が居座り、新しさだけだと 1 回きりの語が勝つため)。古い `conversions.json` (Count 無し) は 1 回として読む。
- 予測 (`CollectPredictions`) の出どころ順 (ユーザー辞書 → 変換エンジン → 変換履歴) は崩さず、各出どころの中を使用頻度順にする。履歴由来はスコア順 (同点は新しい順)。ユーザー辞書・エンジンの予測は、履歴に使用実績のある語をスコア順で上に引き上げ、実績の無い語は元の順のまま。
- 予測で確定した語は履歴に記録される (P3 で実装済みを確認)。同じ語を選び続けると回数が増え、別の語に変えると数え直し。学習済みの語を選び直さずにそのまま確定したときも `Touch` で回数・時刻を更新する (並びは変わらないのでキャッシュは捨てない)。

**変更**: `ConversionHistory` (`Count`、`Clock`、`Usage()`、`Touch()`、`StartingWith` の並び)、`CompositionController.CollectPredictions` / `Learn`。

**実装済み (2026-10-07)**: 上記すべて。Core テスト 242/242 通過 (追加 18 件: `SuggestionTests.*`)。`dotnet build src/Meltype.Mac.Native` と `swift build` は通過。Meltype.app の入れ替えはしていない。Windows (`src/Meltype`) は変更なし (新しい `CompositionOptions` の項目は既定 null なので提案は動かない。コンパイルへの影響なし、ビルドは未確認)。
- 計画との差異・判断: (1) 英字のまま確定した語 (`English` 判定の文節・打った英字そのまま) は、読みがかなでないので数えない。英字語は「読み → Google」のように変換候補から選んだものだけ数える。(2) 読みがひらがな (長音含む) でないものも除外した (ユーザー辞書の読みはひらがなのため)。(3) 「状態 (待ち/却下)」は、待ちを保存せず「回数 ≥ 閾値かつ却下でない」から導く (閾値を変えても整合する)。ヒントを出した日 (`LastHint`) だけ suggest.json に足した。(4) 「提案の履歴を消去」は却下も消す (その組はまた数え直しになる)。(5) 予測で確定した語・再変換は提案の対象にしない (読みが接頭辞・原文のため)。(6) 登録提案を Windows 版に出す UI は無い。
- 未検証 (実機): 入力メニューの提案項目の表示と登録・「登録しない」の動作、ヒントパネルの位置・見え方・消えるタイミング (`attributes(forCharacterIndex:lineHeightRectangle:)` が取れない場合はマウス位置の近く)、実際の使い込みで提案が出る頻度、予測の並びの体感 (30 日半減が適切か)、別セッション・別プロセス間での回数の共有 (suggest.json はプロセス内共有のみ)。NativeAOT の `dotnet publish` での JSON (リフレクション) 読み書き。

**P12/P13 査読の指摘を修正済み (2026-10-07)**: (1) `ConversionHistory.Shared(path)` でプロセス内共有にし、全操作を lock。`Touch` の保存は 5 秒遅延 (別スレッドのタイマー) にまとめ、選び直し (`Remember`)・`Forget`・`Clear` は即保存、終了時 (`ProcessExit`) と `Flush()` でも書く。`MeltypeSession.CreateDefault` は共有を使う (Windows の `CompositionService` は従来の `new` のまま)。(2) 壊れた conversions.json (値が null・`Text` が null/空) は読み込み時に除外し、`Usage()` も防御。(3) 語・読みに改行・タブを含む組は数えない。`PendingSuggestions` はユーザー辞書に登録済みの組を除く。500 件の上限では「登録しない」の組を後回しにして古い未却下から捨てる。提案ヒントは共有のため別セッションの確定で出ることがあるが、1 日 1 回の制限内で許容 (コメントに明記)。テスト 247/247 (追加 5 件)。未検証: 別プロセス間 (Windows トレイと IME など) の conversions.json の競合は従来どおり。

---

## 2. 作業の進め方 (チェックリスト)

1. 着手前に該当項目を読み、**変更するファイルを全部先に開く** (行番号は 2026-10-07 時点。ずれていたら関数名で探す)。
2. Core を変えたら `src/Meltype.Core.Tests` にテストを足し、`~/.dotnet/dotnet run --project src/Meltype.Core.Tests -- <フィルタ>` を通す。最後にフィルタ無しで全部通す。
3. Mac は `cd mac && ./build.sh` で入れ替え → TextEdit で受け入れ条件を 1 つずつ確認。確認は **キーボードから実際に打つ** (スクリプトで送るときは `System Events` の `key code`。F9 は OS に取られることがある)。
4. 1 項目終わるごとに、この文書の該当項目の末尾に「実装済み (日付)」を追記する。
5. 迷ったら実装せずに、この文書の「受け入れ条件」に照らして質問する。受け入れ条件に無い挙動は増やさない。「未検証」と書いてある箇所は、実装して動かなかったら受け入れ条件を落として報告する。

## 3. 参照 (今回の調査で読んだ場所)

- キー処理: `src/Meltype.Core/Composition/CompositionController.cs` `HandleKey` (355〜513 行。`switch (vk)` は 428 行)、`HandleConversionKey` (739 行)、`SetMode` (805 行)、`JapaneseCandidates` (1104 行)、`Expand` (1187 行)、`Learn` (1426 行)、`LearnConversion` (1437 行)、`UpdateView` (1557 行)。`OnKey` は `CaptureGate.OnKey` という別物
- 表示モードの描画: `CompositionText.Display` (587 行)、記号: `Symbol` (876 行)、`ToKatakana` / `ToFullWidth` (935〜954 行)
- Mac/Linux の入口: `MeltypeSession.HandleKey` (166 行)、`CreateDefault` (119 行)、`SelectCandidate` (199 行)、`StartsComposition` (214 行)、`SessionResult.ToJson` (21 行)
- 学習の口: `ILearningConverter` (`KanjiConversion.cs:25`)
- FFI: `src/Meltype.Mac.Native/Exports.cs`、`mac/Sources/MeltypeIME/NativeCore.swift`
- 変換エンジン (Mac): `mac/Sources/MeltypeIME/Converter.swift`。azooKey の API: `mac/.build/checkouts/AzooKeyKanaKanjiConverter/Sources/KanaKanjiConverterModule/ConverterAPI/` (`ConvertRequestOptions.swift`、`KanaKanjiConverter.swift`、`ConversionResult.swift`)
- 設定: `src/Meltype.Core/Config/Settings.cs` (`Normalize` 504 行、`Migrate` 528 行)
- 辞書: `src/Meltype.Core/Composition/UserDictionary.cs` (`Parse` 44 行)、`CandidateDictionary.cs`、`src/Meltype.Core/Detection/WordList.cs` (`DictionarySource`)
- テスト: `src/Meltype.Core.Tests/TestFramework.cs`、`Program.cs`、`SessionFacadeTests.cs` (`Json_IsEscaped` 101 行)、`CompositionTests.cs` (`Keyboard` 175〜200 行)
- Mozc ヘルパー: `native/mozc/meltype_mozc_helper.cc` (命令の分岐 181 行〜、`Request()` 139 行)
- Apple 日本語入力のキー割り当て: `/System/Library/PrivateFrameworks/CoreJapaneseEngine.framework/Versions/A/Resources/KeySetting_Default.plist`
- InputMethodKit のヘッダ: `/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/System/Library/Frameworks/InputMethodKit.framework/Headers/IMKInputController.h`、`.../Carbon.framework/Frameworks/HIToolbox.framework/Headers/IMKInputSession.h`、`TextServices.h`

---

## 追加項目 P14 (セキュリティ監査の修正)

2026-10-07。セキュリティ監査の指摘のうち、Mac 版の M1・M2・M4 と L1・L2・L3・L4・L5・L7・L8・L9・I3 を直した (M3: Developer ID 署名・公証は対象外)。

### 実装した内容

| 項目 | 内容 | 主な場所 |
| --- | --- | --- |
| M1 秘匿入力 | `handle` の冒頭で `IsSecureEventInputEnabled()` が true なら、未確定があれば先に確定して `return false` (Core・学習・提案・ログに渡さず、`surroundingText` も読まない)。`SECURITY.md` と `Settings.cs` の説明を実装に合わせた | `InputController.swift`、`SECURITY.md`、`Settings.cs` |
| M2(a) 権限 | 保存の共通処理 `SafeFile` を新設。ファイルは作成時 0600・データフォルダーは 0700 (Mac / Linux のみ)。置き換え方式なので既存の 0644 のファイルも保存時に 0600 になる。Windows は何もしない。azooKey の学習フォルダーも 0700 | `Config/SafeFile.cs`、各保存箇所、`Converter.swift` |
| M2(b) 全消去 | 入力メニュー「学習データをすべて消去…」(NSAlert・既定ボタンはキャンセル)。Core `LearningData.ClearAll`、FFI `meltype_clear_learning`、Swift `NativeCore.clearLearning` + `KanaKanjiConverter.resetMemory()`。`LanguageMemory` / `TranslationHistory` も共有インスタンス (`Shared(path)`) にして、メモリ上も消す。ユーザー辞書・設定は消さない | `Composition/LearningData.cs`、`Exports.cs`、`InputController.swift` |
| M4 配布 | `install.sh`: `codesign --verify --deep --strict` に失敗したら中止、実行ファイルと `libMeltypeNative.dylib` の SHA-256 を表示、隔離属性の削除は y/N で確認 (`--yes` で省略。端末でなく `--yes` も無いときは外さない)。`INSTALL.txt` と `mac/README.md` に SHA-256 の確認方法と影響範囲を追記 | `mac/install.sh` ほか |
| L1 | `Exports.Destroy` を try/catch で囲む | `Exports.cs` |
| L2 | `meltype_abi_version` (`Exports.AbiVersion = 2`)。Swift `NativeCore.expectedAbiVersion` と照合し、不一致・関数なしなら NSLog して初期化を中止 (`createSession` は nil、キーはアプリに素通し)。**FFI の引数や意味を変えたら両方を上げる** | `Exports.cs`、`NativeCore.swift` |
| L3 | `build.sh` で署名の前に、`LC_RPATH` のうちビルドマシンの絶対パスを `install_name_tool -delete_rpath` で除く (`/usr/lib/*`・`/System/*`・`@...` は残す) | `mac/build.sh` |
| L4 | `UserDictionary.Validate`: 読み・語の `\r \n \t` を拒否、100 文字まで。`Add` / `AddRange` が使う。「選択中の文字を登録」は選択が 100 文字を超える・改行を含むときエラー表示 | `UserDictionary.cs`、`InputController.swift` |
| L5 | `IsEligible`: 記号を含む語 (`・` は除く) を除外、英数字だけの語は 12 文字以上を除外 (旧 16)。メニュー・ヒントの語は 20 文字超なら「…」で省略 (`DisplayWord`) | `DictionarySuggestions.cs` |
| L7 | 再変換 (Shift+Space) は選択が 200 文字までに制限 (Swift が中身を読む前に判定、Core でも `MaxReconvertLength`) | `InputController.swift`、`MeltypeSession.cs` |
| L8 | 学習データ・ユーザー辞書・設定・ユーザー辞書フォルダーの .txt は、読む前にサイズを見て 20 MB 超なら警告ログのうえ空で続行 (`SafeFile.MaxReadBytes`。テストは小さい値を注入) | `SafeFile.cs`、各読み込み |
| L9 | 保存の一時ファイルを `<名前>.<Guid>.tmp` に変更 (同じフォルダー、`File.Move` は overwrite、失敗時は削除) | `SafeFile.WriteAllBytes` |
| I3 | `THIRD-PARTY-NOTICES.md` の Mac 版の項に、swift-tokenizers・Jinja・swift-collections・swift-algorithms・swift-numerics と azooKey の辞書を追記 (ライセンスは `mac/.build/checkouts` の LICENSE を読んだ)。`.gitignore` に `*.log *.pfx *.p8 *.keychain *.before-restore` | |

テスト: `SecurityTests.cs` を追加 (権限・一時ファイル・読み込み上限・全消去・ユーザー辞書の入力チェック・提案の対象と省略・再変換の長さ)。`SuggestionTests.Suggest_IneligibleAreNotCounted` は新しい基準に更新。

### 計画との差異・判断したこと

- `config.json` も `SafeFile` で保存するので 0600 になる (共通化の結果。Windows は変化なし)。
- 記号の判定は `char.IsPunctuation` / `IsSymbol` (中黒 `・` だけ許す)。`Node.js`・`e-mail` のような語も提案の対象外になる。
- 全消去は `meltype_clear_learning` をセッションなしで呼べる静的な関数にした (共有インスタンスはパスごとにプロセス内で 1 つのため)。Mac には無い `model.json` は作らない。
- 隔離属性は、端末でなく `--yes` も無いとき (自動実行) には外さない (黙って Gatekeeper を回避しないため。手動のコマンドを表示する)。
- ログファイル (`meltype.log`) とバックアップ復元以外の書き出しの権限は今回は触っていない (ログは既定で OFF)。

### 未検証 (実機で確認が必要)

- 秘匿入力欄: Safari / Chrome / Terminal のパスワード欄・ターミナルの sudo で素通しになること、欄を出たあとに通常入力へ戻ること。他のアプリが秘匿入力を付けっぱなしにしたとき全体が素通しになる副作用。
- 全消去: ダイアログの表示 (IME のプロセスから NSAlert が前面に出るか)、消したあと変換の候補順が初期状態に戻ること、azooKey の学習ファイル (`~/Library/Application Support/Meltype/azooKey`) が消えること、ユーザー辞書が残ること。
- `install.sh`: 署名検査の失敗時の中止、SHA-256 の表示、y/N の挙動、`--yes`、Install Meltype.command 経由 (端末) の動作。
- `build.sh` を実行したあとの `otool -l` で RPATH から `/Library/Developer/...` が消え、起動できること (コマンドはスクラッチのコピーでだけ試した)。
- 0600/0700 が実機の `~/Library/Application Support/Meltype` で効くこと (前の版が作ったファイルは、次の保存で 0600 になる)。
- ABI 不一致時にクラッシュせず素通しになること (実機では dylib の版を混ぜて試す)。

### P14 査読後の追加修正

- **L8 の欠陥 (データ消失) を修正**: 上限超過で空のまま続けると、次の保存で元のファイルが上書きされていた。`SafeFile.ReadAllText` が上限超過を検知したら、元を `<名前>.oversize` (あれば `.oversize.1`, `.2` …。同じ大きさの退避が既にあれば増やさない) へ**コピー**してから空で続行する。コピーできなかったパスは `SafeFile` が保存を止める (警告ログのみ、上書きしない)。読み込み側はすべて `SafeFile.ReadAllText` 経由なので、ユーザー辞書・提案・言語・英訳・変換履歴・ユーザーモデル・設定・補助辞書 .txt に一括で効く (設定も `.oversize` で退避。`.broken` ではない)。テスト: `Load_Oversized_IsBackedUpAndNotOverwritten`、`Load_Oversized_BackupFailureBlocksSave`。
- 保存は置き換える前に `Flush(true)` でディスクまで書く。
- `LanguageMemory` / `TranslationHistory` は元々ロックが無かったので、公開メソッド全部に `lock` を付けた (`Clear` を含む)。
- ABI 不一致のとき (`isCompatible == false`)、Swift の `NativeCore` の session を使う入口 (handleKey・commit・select・reconvert・setApp・setDirect・登録/提案系) も早期 return する。全消去は、不一致のとき「azooKey の学習だけを消去しました」と実態どおりに表示する。
- 未検証 (実機): 20MB 超のファイルでの退避の動作、ABI 不一致時のメニュー表示。

---

## 追加項目 P15. 更新通知と更新ボタン (実装済み・未検証)

**目的**: 新しい版が出たことを知らせ、利用者がボタンを押したときだけ、確認してから入れ替える。打った内容は送らない。

### 決定 (リョウ)

既定 ON・入力メニューのチェック項目で切替 / 取得先は GitHub の最新 Release だけ / 自動インストールはしない / 電子署名は入れない (GitHub アカウント乗っ取りは防げないと文書に書く)。

### 変更したファイル

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Update/UpdateCheck.cs` (新規) | `UpdateCheck.Evaluate(json, 現在の版)`。純粋なロジック (System.Net なし)。JSON は 1MB まで・try/catch。draft/prerelease 無視、タグ (`v1.0.4` / `v1.0.4-mac` / `1.0.4`) を数で比較、資産 `Meltype-mac-<版>.zip` の URL 接頭辞・末尾のファイル名・size ≤ 100MB・`digest` = `sha256:` + 64 桁 16 進を全部見る。返すのは版・URL・SHA-256・size・リリースページ |
| `src/Meltype.Mac.Native/Exports.cs` | `meltype_update_evaluate(json, version)` を追加。改行区切り 5 行か NULL。`AbiVersion` 2 → 3 |
| `mac/Sources/MeltypeIME/NativeCore.swift` | `evaluateUpdate`、`expectedAbiVersion` 2 → 3 |
| `mac/Sources/MeltypeIME/Updater.swift` (新規) | `UpdateManager` (update.json・定期確認・通知・メニュー用の状態・osascript のダイアログ) と `Updater` (ダウンロード・SHA-256・展開・検査・切り離した install.sh)。リダイレクト制限のデリゲート |
| `mac/Sources/MeltypeIME/InputController.swift` | `menu()` に「新しい版があります (v…)…」(先頭)・「更新を確認する」(チェック)・「今すぐ更新を確認する」 |
| `mac/Sources/MeltypeIME/main.swift` | 起動時に `UpdateManager.shared.start()` |
| `src/Meltype.Core.Tests/UpdateCheckTests.cs` (新規) | 新しい/同じ/古い・桁の数比較・prerelease/draft・digest 無し/不正・別ホスト/別リポジトリ/http/ファイル名違い・サイズ超過・壊れた JSON・1MB 超・タグ形式 |
| `README.md` / `SECURITY.md` / `mac/INSTALL.txt` / `mac/README.md` | 「通信するコードは入っていない」を、新しい版の確認と更新の説明に書き換え。限界 (電子署名なし) を SECURITY.md に明記 |

### 設計上の判断

- 確認の間隔は、起動 5 分後・その後 1 時間ごとに「前の確認から 24 時間以上か」を見る (IME は頻繁に再起動されるので、起動のたびに 1 回は確認しない)。通信失敗 (オフライン) は lastCheck を進めず、1 時間は再試行しない。HTTP の応答があれば (404・403 でも) 確認したことにする。
- 通知: 通知センター (UNUserNotificationCenter、初回に許可を求める)。**利用者が許可を断っているときは、osascript で代わりに出さない**。エラー (使えない) のときだけ osascript の `display notification` にフォールバック。
- 見つかった版はメモリにだけ持つ (IME を起動し直すと消え、次の確認か「今すぐ更新を確認する」で再び出る)。URL・SHA-256 をファイルに残して信用することを避けた。
- <del>install.sh は変更不要と確認した</del>: `--yes` は非対話 (stdin が端末でなくても動く)、<del>`pkill` は rsync の後・IME だけを止める</del>。切り離した bash は `POSIX_SPAWN_SETSID` の新しいセッションで動くので、IME が止まっても続く。IME が止まるので、結果のダイアログは切り離した側が出す (これは設計の意図。実際は IME が止まらなかった)。[訂正 2026-10-13: 「install.sh は変更不要」「`pkill` は IME だけを止める」は誤り。更新機能は IME が切り離した `bash -c` を起動し、その中で `install.sh` を実行するので、IME は `install.sh` の祖先にあたり、`pkill` は祖先を対象から外す。IME は止まらなかった。`install.sh` の `pkill` を `killall` に変えた。Q13 参照]
- `URLSession` の保存には隔離属性 (quarantine) が付かない (Info.plist に `LSFileQuarantineEnabled` なし)。`install.sh --yes` が、入れた Meltype.app の隔離属性を外す (既存の動き)。

### 未検証 (実機で確認が必要)

- 通知の許可ダイアログ・通知の表示 (背面専用アプリから出るか)。許可を断ったときに出ないこと。
- 実際の Release (digest 付き) での確認と、「更新する」から入れ替え完了までの一連 (切り離した install.sh が IME の停止後も最後まで動き、結果のダイアログが出ること)。
- OFF のときに通信しないこと (確認は `nettop` / Little Snitch など)。
- GitHub の実際の JSON (`digest` の形式・リダイレクト先のホスト) が想定どおりか。

### P15 査読後の修正

- **M1**: ダウンロード URL を、`https://github.com/whitewater-png/Meltype-for-Mac/releases/download/<tag_name>/<Meltype-mac-版.zip>` との完全一致にした (`%`・`\`・空白・制御文字・`..`・ポート・query・fragment・ユーザー情報は拒否)。Swift 側 (`UpdateOffer.hasValidDownloadUrl`) でもダウンロードの直前に再検証。テスト `TrickyUrls_AreRejected` を追加。
- **M2**: 展開前に `zipinfo -t` / `zipinfo -1` でエントリ数 (5000 以下)・展開後の合計 (300MB 以下)・絶対パス・`..`・バックスラッシュの名前を検査して、外れたら中止。
- **M3**: `install.sh` の入れ直しを「`cp -Rp` で一時退避 → rsync → `codesign --verify` → 失敗なら退避から rsync で書き戻し・成功なら退避を消す」にした。この区間は `set -e` に任せず明示的に処理。`Meltype.app` フォルダーは消さない。新規インストールと対話の動きは変更なし。
- **M4**: 実行中の IME が `~/Library/Input Methods/Meltype.app` のときだけ「更新する」を出す。それ以外は「詳細」のみ。
- **M5**: 起動後は `isUpdating` を true のままにし、`offer` を nil にしてメニューの項目を消す。切り離した bash 側も `Application Support/Meltype/update.lock` を `mkdir` で取り (1 時間以上前の古いロックは消す)、取れなければ中止して知らせる。終了時は `trap` で必ず消す。
- **L1**: `update.json` は項目ごとに読み (`UpdateSettings.swift`)、ファイルがあるのに読めない・壊れているときは `enabled=false`、ファイルが無いときだけ ON。ロジックは Foundation だけの小さな型にして、scratchpad で swiftc の単体確認 (7 項目) をした。
- **L2**: `finishCheck` の冒頭で ON を再確認し、OFF で手動でなければ結果を捨てる (通知も出さない)。
- **L5**: IME 起動時に `$TMPDIR` の `meltype-update-*` (自分の接頭辞・ディレクトリ・24 時間以上前) を消す。
- **L8**: 確認ダイアログに `giving up after 300`。時間切れは「あとで」と同じ扱いで `isUpdating` を解除。
- 未検証 (実機): 退避と書き戻し (rsync の失敗・署名の検査の失敗を起こして)・ロックの競合・build の場所から動かしたときに「更新する」が出ないこと・`zipinfo` の出力形式 (macOS 26) での検査。

## 追加項目 P16 (長文入力の高速化)

変換ボックスの文章が長くなると、1 キーごとの反応が重くなる不具合。**先に測ってから**直した (計測は scratchpad の小さなテスト。リポジトリには回帰テスト `LongInputTests.cs` だけを残した)。

### 計測 (修正前 → 後。Release、擬似の変換エンジン、日本語 + 英単語の混じったローマ字を 1 文字ずつ HandleKey)

Core の 1 キーあたり (ms)。「末尾」は最後の 10% のキー (一番長いとき)。

| 文字数 | 修正前 平均 | 修正前 末尾 | 修正後 平均 | 修正後 末尾 |
| --- | --- | --- | --- | --- |
| 10 | 0.15 | 0.17 | 0.4 (JIT) | 0.12 |
| 50 | 0.37 | 0.63 | 0.12 | 0.16 |
| 100 | 0.98 | 2.4 | 0.15 | 0.14 |
| 200 | 4.8 | 14 | 0.21 | 0.38 |
| 400 | 23.9 | 72 | 0.53 | 1.2 |

実機の azooKey も測った (scratchpad の小さな Swift: 読みの長さ 10/50/100/200/400 文字で 5/37/63/128/246 ms。予測も同程度で、**読みの長さに比例**)。修正前は 1 キーごとに「文節の変換 (読み全体)」と「予測 (読み全体)」を呼ぶので、400 文字では 1 キーあたり azooKey だけで 300 ms 前後 (推定。1 文字 0.62 ms で換算)。修正後は区切った末尾だけ: 平均 約 10 ms、末尾は 0 に近い。

### 原因

1. **区間分け (英語 / 日本語の判定) が長さの 3 乗** (`CompositionDetector.FindSpans`)。キーごとに、全部の始点 i から、全部の終点 j の区間 [i, j) を `IsEnglishSpan` で調べる (O(n²) 区間)。しかも区間ごとに単位をつなぎ直す (`Raw` / `Kana` / `HasUnreadable` が O(長さ)) ので O(n³)。400 文字で 1 キー 70 ms 以上。**Core の時間の 99%** (関数ごとの内訳を Stopwatch で測った: UpdateView 内の ConvertWithEngine・LiveConvert・予測・`Usage`・誤字提案はどれも 1% 未満)。
2. **変換エンジンに毎キー読み全体を渡す** (ライブ変換 + 予測)。azooKey は読みの長さに比例するので、長いほど 1 キーごとに重くなる。文脈付き・文脈なしの両方で変換する `ConvertWithContext` は、同じ読みを 2 度渡す。Mac の `clauses(for:context:)` は文脈を使わないので 2 度目は丸ごと無駄。
3. (小) 予測の `ConversionHistory.Usage()` を毎キー全件計算。

### 修正

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Composition/CompositionDetector.cs` | `FindSpans` の区間の判定を絞った。(a) 読み・打った英字・「読めない英字があるか」は、単位列から 1 度だけ作る表 (`SpanTable`) で O(1) / memcpy にした (3 乗 → 2 乗)。(b) 英字 (と `'`) 以外の単位を含む区間は `IsEnglishSpan` が必ず false なので飛ばす。(c) 48 文字 (`MaxEnglishSpanLength`) を超える区間は、英単語・固有名詞・スペルチェッカーの語にならないので飛ばす (大文字で始めて末尾まで打った区間と、`'` を含む区間 = 短縮形は、長さを見ずに英語にする判定なので今までどおり調べる)。(d) `IsEnglishSpan` の結果を (区間・前後・判定の強さ・フラグ) ごとに覚える (学習した語 `Memory` の版とスペルチェッカーが変わったら捨てる)。前のキーで調べた区間を調べ直さない |
| `src/Meltype.Core/Composition/CompositionController.cs` | (a) 50 文字 (`ConvertChunkLength`) を超える読みは区切って変換する (`ConvertWithEngine` → `SplitForConversion`)。区切りは先頭から決まる (文末 。！？ の後ろ → 12 文字以上たまった 、 の後ろ → 変換エンジンの文節の区切り (最後の文節を除く) → 上限)。続きを打っても前の区切りは動かず、前の部分の結果は覚えてある。2 つ目以降は前の部分の変換結果を文脈にする。小さい ゃゅょっー の途中では切らない。(b) 変換エンジンの結果 (読み, 文脈 → 文節) を、変換ボックスが開いている間だけ覚える (`EngineClauses`)。(c) 予測は読みが 40 文字 (`MaxPredictionReadingLength`) を超えたら出さない |
| `src/Meltype.Core/Composition/ConversionPreferences.cs` | `Usage()` を、学習の内容 (`Version`) が同じなら 1 分間使い回す (`Touch` では捨てる) |
| `mac/Sources/MeltypeIME/Converter.swift` | `results(for:)` に直近 16 件の結果の覚えを追加 (学習・学習の消去で捨てる)。同じ読みを文脈付き・なしで 2 度渡されても azooKey を 1 度しか呼ばない |
| `src/Meltype.Core.Tests/LongInputTests.cs` (新規) | 400 文字を打って: 変換エンジンに 50 文字を超える読みを渡さない・渡した文字数の合計が上限以内・予測に 40 文字を超える読みを渡さない・総時間 8 秒以内 (Release で 0.5 秒、直す前は 10 秒超)・表示と確定が一致・長い文の途中の英単語を見落とさない・短縮形が長さの上限で飛ばされない |

### 挙動の変化 (最小限)

- **区間分け (英語 / 日本語の判定)**: 変えていない。直す前後で 4000 通りのランダムな入力 (英単語・助詞・記号・数字・`'`・長い英字の並び、判定の強さ・前後の文脈も変える) を 1 キーずつ打って、6.8 万件の区間分けの結果を比べて**完全一致**を確認した (比較用のテストは scratchpad。リポジトリには残していない)。変わるのは「48 文字を超える英字の並びが、単語でないのに 1 語の英語になる」ごく稀な場合だけだったので、短縮形のものは今までどおりにした。
- **50 文字を超える読みの変換** (ライブ変換・Space の変換・確定): 区切って変換するので、境界の文節の選び方が変わりうる。azooKey で 4 文 (各 80〜90 文字) を全体変換と区切り変換で比べたら、3 文は完全に同じ、1 文は「良かった」が「よかった」になった。50 文字以下は今までと同じ (区切らない)。
- **40 文字を超える読みでは予測を出さない**。
- 他: 予測の `Usage` が 1 分以内は同じスコアを使う (日数による減衰だけなので並びは変わらない)。

### 残っている・直していない点

- Swift の処理は**メインスレッドで同期的に**azooKey を呼ぶ (IMK のキーイベントがメインスレッド、本体からのコールバックが同期のため)。非同期にするには Core の対話を変える必要があり、この件では直していない。区切りで 1 回の読みが 50 文字までになったので、最悪でも 1 キー 30〜40 ms 程度。
- `surroundingText` は変換中 (`hasMarkedText`) は読まない。読むのは変換していない間だけで、前後 20 文字ずつなので重くない。
- 予測と変換が同じ読みなら 2 度 azooKey を呼ぶ (`.manualMix` の予測は別のオプションなので覚えを共有できない)。40 文字以下の予測は 1 キーあたり最大 25 ms 程度。
- Windows / Linux (Mozc ヘルパー) も Core の区切り・覚えの恩恵を受けるが、実機では測っていない。

### 実機で確認してほしい点

- 長い文章 (200〜400 文字) を続けて打って、1 キーの反応が一定か (体感)。
- 50 文字を超えた所で、変換ボックスの表示が一瞬ちらつく・区切りの前後で漢字の選び方が不自然に変わらないか。
- Space で変換したときの文節の区切りと、確定した文字列が、ライブ変換の表示と同じか。
- 40 文字を超えたら予測の候補ウィンドウが消えること (仕様)。

### P16 査読後の修正

- **M1**: 変換エンジンの結果の覚え (`EngineClauses`) を「512 件で全消去」から「いっぱいになったら、使ってから一番長い 64 件 (1/8) を捨てる」(LRU) に変えた。今の区切りは打つたびに使うので残り、全部が一度に変換し直しになる山はできない。1100 キーのテストで、1 キーのエンジン呼び出しは 8 回以内 (全消去のままだと、512 件に達した 1 キーで、残っている区切り 20 余りを全部呼び直す)。
- **M2**: 40 文字の上限は変換エンジンの予測 (`Predictions`) だけにかけた。ユーザー辞書・変換履歴の予測は長い読みでも出る (従来どおり)。README の予測の説明に一言追記。テストあり。
- **M3**: 変換エンジンの失敗 (null) は覚えない (次のキー・Space・Backspace で再試行できる)。1 回目 null・2 回目成功のテストあり。
- 学習した語 (`LanguageMemory`) は、48 文字を超えても今までどおり調べる (飛ばす上限を、覚えている最長の語の長さ `MaxWordLength` と大きい方にした)。
- `_clauseCache` と区間判定の覚え (`ClearSpanCache`) は、変換ボックスが空になったとき・`Reset`・`SuspendInput` (Reset 経由) で捨てる。
- テスト: 飛ばす最適化・判定の覚えを使わない素朴な全探索 (`CompositionDetector.ReferenceMode`、テスト用の内部スイッチ) と、固定の入力 24 通り (英単語・助詞・大文字始まり・短縮形・数字・記号・長い英字列) × 判定の強さ 3 種 × 1 キーずつの途中経過 × 確定前後で照合して完全一致。`Usage`・`EngineClauses`・区間判定の覚えの無効化 (Remember / Forget / Clear / Touch / 学習語の変更 / 変換ボックスを閉じる) のテスト。

### P16 の既知の制限 (仕様として残す)

- 50 文字を超える読みの区切りをまたぐ文脈の手がかり (ContextRules の `surrounding`) は効かない。
- 区間判定の覚えは、OS のスペルチェッカーの内容が変わったこと (ユーザーが単語を登録したなど) を検知しない。変換ボックスを閉じると捨てるので、次の入力からは反映される。
- 区切りの境界を越えるキー (新しい区切りができる 1 キー) は、区切り位置を決めるための変換が 1 回増え、80〜100 ms かかることがある。

---

## 追加項目 P17 (変換後も続けて入力)

**要望**: Space で変換したあと (候補を選んでいる状態) に続けて文字を打つと、その時点で変換結果が確定してしまう。続けて打った文字を含めて、編集・変換を続けたい。

**方針 (決定済み)**: 設定 `ContinueAfterConversion` (bool、カテゴリ「1. 全般」、**既定 OFF = 今までの動作**)。Mac は設定画面が無いので、入力メニューのチェック付き項目「変換後も続けて入力できる」で切り替え、`config.json` に保存する。OFF のときの挙動は完全に従来どおり。

### 変更したファイル

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Config/Settings.cs` | `ContinueAfterConversion` を追加 (既定 false なので Migrate・SettingsVersion は不要。古い config.json に項目が無ければ OFF)。書き換え用に、読めないとき null を返す `LoadForUpdate` を追加 |
| `src/Meltype.Core/Config/ContinueAfterConversionSetting.cs` (新規) | プロセスで 1 つの値 (`IsOn`) と、切り替えて `config.json` に保存する `Set`。読めない config.json は上書きしない |
| `src/Meltype.Core/Composition/CompositionController.cs` | `CompositionOptions.ContinueAfterConversion` (Func)。固定した文節の扱い (下の「設計」) |
| `src/Meltype.Core/Composition/CompositionText.cs` | `PrependReading` (固定した文節を変換前の読みに戻すために、先頭に読みを足す) |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | `CreateDefault` で `ContinueAfterConversion = () => ContinueAfterConversionSetting.IsOn` |
| `src/Meltype.Mac.Native/Exports.cs` | `meltype_get_continue_after_conversion` / `meltype_set_continue_after_conversion`。`AbiVersion` 3 → 4 |
| `mac/Sources/MeltypeIME/NativeCore.swift` | 上の 2 関数の呼び出し。`expectedAbiVersion` 3 → 4 |
| `mac/Sources/MeltypeIME/InputController.swift` | 入力メニューの項目 (チェック付き)。固定した文節 + 未変換の文節の下線表示 |
| `src/Meltype.Core.Tests/ContinueAfterConversionTests.cs` (新規) | 28 件 (OFF は従来どおり・ON の各操作・設定の切り替え・ABI 版の一致) |

### 設計

- `_clauses` の先頭 `_headCount` 個を「固定した文節」(選んだ候補のまま)、続けて打った文字は空にした `_text` (= 未変換の文節) に入れる。`_text` を共有せず分けたのは、`CompositionText` がローマ字の途中・英単語の区切りの判定で直前の単位までさかのぼって書き換える作りで、変換済みの読みと同じ `_text` に入れると、固定した文節が壊れうるため。
- 変換中に「文字として入るキー」(ローマ字・かな・記号。数字は候補の選択のまま) が来たとき、`ContinueAfterConversion` が ON なら固定して続きを未変換の文節にする (OFF は `Commit` + `BeginComposition` で従来どおり)。
- Space = 未変換の文節だけ変換 (最初の文節を選んだ状態)。← → は固定した文節にも戻れる。Shift+←→ は固定した文節と未変換の文節の境目をまたがない。Backspace = 未変換の文節の末尾を消し、空になったら最後の固定した文節の選択に戻る (変換中の Backspace は未変換の文節の変換だけを取り消す)。Enter = 全体を確定 (固定した文節も従来どおり学習: `Learn` / `LearnConversion` / `History`)。Esc = 固定した文節を変換前の読みに戻す (その次の Esc は従来どおり入力を取り消す)。
- F6〜F10 は未変換の文節にだけかかる。英単語は、未変換の文節の中で別に判定する (固定した文節が日本語でも英語でも)。ライブ変換が ON なら、続けて打った部分も漢字で見える。
- 変換の文脈 (`PrecedingForConversion`) には固定した文節の文字列も含める。

### 既知の制限

- 固定した文節があるあいだは、予測変換を出さない (予測を確定すると固定した文節が消えるため)。
- 固定した文節をまたぐ「英語とも日本語とも読める語の確定し直し」(`CorrectPreviousCommit`) はしない。
- 設定は入力メニューか config.json の手編集 (再起動後に反映) だけ。Windows 版は `CompositionOptions` に配線していないので、設定画面に項目は出るが動かない (Windows 側は `TrayApplicationContext` と `CompositionService` に 1 行ずつ足せば動く)。
- かな入力 (JIS) の経路は、ローマ字入力と同じ関数を通すが、専用のテストは無い。

### P17 査読後の修正

- **重大**: 固定した文節の Shift+← で、後ろが英語の文節のとき、新しい文節を固定した側の途中に足すのに `_headCount` を増やしておらず、最後の固定した文節 (で検索) が未変換の側に押し出されて捨てられていた。`Resize` で、足した位置が固定した側なら `_headCount` を増やすようにした (縮めを禁止するより、固定した文節どうしの区切り直しが従来どおりできるほうが自然なため)。再現のキー列 (`kyouhagoogledekensaku` → Space → `ta` → ← ×4 → Shift+←) で、Backspace・Esc・F7・Space のあとも欠けないテストと、固定した文節への Shift+←→ をランダムに繰り返して読みの連結が変わらないことを確かめるテストを追加。
- Esc で戻すとき、固定する前に打っていた単位 (打った英字つき) を戻すようにした。OFF の Space → Esc → F10 と同じく `tanniwotoru` に戻る。
- Esc で、未変換の文節だけに効かせていた表示モード (F7 など) を Auto に戻す。
- 設定の保存に失敗したときのダイアログを専用のタイトル「設定を保存できませんでした」にした。
- `docs/USAGE.md` に説明を追記。

### P17 の既知の制限 (追加。対応しない)

- プロファイルの共通項目 (SharedKeys) には入れていない。プロファイルを切り替えると、プロファイルに保存した値に戻りうる (Mac はプロファイルを使わない)。
- 周辺文脈のコールバックが固定したあとに届くと、その分は文脈に反映されない。
- 保存は設定を読み直して書き直すので、config.json の未知のキー・コメントは残らない。
- 保存はロックの中でファイルを書くので、遅いディスクでは切り替えが一瞬待つ。

---

## 追加項目 P18 (数字の直後の単位)

**不具合**: `50ccgentuki` が「50っc原付」になる (期待は「50cc原付」)。

**原因**: ローマ字の「同じ子音 2 つ = っ + 子音」(`CompositionText.Normalize` / `Romaji.AnalyzeFragment`) が cc に効き、`cc` が「っ」+ `c` の単位になる。英語判定 (`CompositionDetector`) に着く前の段階で決まるため、辞書 (`dictionaries/*.txt` / `WordList`) に cc を登録しても直らない (cc は 2 文字の略語で、数字の直後以外では日本語の列 accno 等と区別できず、辞書は文脈 = 数字の直後を見ない)。**結論: 辞書の登録だけでは足りない。判定規則が要る。**
すでに `UnitsAfterNumbers` (`SplitUnitAfterNumber` / `UnitWords`) があり、mm・min・kg などは数字の直後で英字のままにしていた。cc・pp 系が一覧に無かっただけ。

**修正**: `CompositionText.UnitWords` に `ppm` `ppb` `cal` `pp` `cc` を追加 (最小の変更)。加えて cc・pp は、後ろに母音 (a i u e o) が続くなら今までどおり (50ccaga → 50っかが、5ppu → 5っぷ)。後ろが h・y・w (50ccは・50ccや・50ccを) は単位 + 助詞を優先する。数字は半角のみ (全角数字は今までどおり)。
変更ファイル: `src/Meltype.Core/Composition/CompositionText.cs`、テスト `src/Meltype.Core.Tests/FeedbackTests.cs` (`UnitsAfterNumbers_DoubledConsonant` ほか 2 件)。

**対象の単位の全体**: 既存 (mmol kcal mhz ghz khz kwh mah mol min sec rem dpi ppi fps bpm rpm mph kph mm cm km nm um mg kg ml dl ms ns hz kb mb gb tb px pt em wh) + 追加 (cc pp ppm ppb cal)。l g t m w v kw db ft lb bps など、ローマ字として読めない単位は元から英字のまま。ma ka ki a in oz は日本語の音節と同じ綴りなので入れない。

**既知の制限 (対応しない)**: `5w` + `wo` (5wwo) は ww が笑いの w と同じ扱いで「5っを」になる。`5dpide` は dpi の直後の `de` で英字のまま残る (既存の挙動)。確定前 (打ちかけ) の画面は、単位が打ち終わる前は「50っc」のまま見え、次の文字か確定で「50cc」になる (mm と同じ。次に母音が来る可能性があるため待つ)。

---

## 追加項目 P19 (専門用語集の同梱)

**目的**: 分野ごとの専門用語 (数千〜数万語) を、アプリに同梱して変換・予測変換に使えるようにする。**語そのものは作らない**。仕組み・検査ツール・手引き・空の雛形だけを置いた。配布はアプリの更新のみ。

**使い方**: `dictionaries/terms-<分野>.txt` を置いてビルドするだけ (Core.csproj の `dictionaries/*.txt` のグロブで埋め込まれ、`terms-` で始まるものを全部読む。コード変更なし)。形式は「読み<Tab>語<Tab>注記(任意)」。詳しくは `docs/DICTIONARY.md`。

**設計**
- `Composition/TermDictionary.cs` (新規): 読み込みと索引。読みが `ForcedMinReadingLength = 4` 以上は強制型 (`Dictionary` で読み → 語、先頭の 1 文字ごとの最大長で照合を絞り、`AlternateLookup<ReadOnlySpan<char>>` で文字列を作らずに引く)、3 以下は候補追加型 (読み → 語。助詞付きは `CandidateDictionary.Endings` を共用)。予測用に、読みの昇順に並べた配列 + 二分探索で前方一致 (一致は先頭から最大 64 件だけ数える)。読みはカタカナ → ひらがな、全角英数 → 半角、大文字 → 小文字にそろえる。不正な行 (読み 2 文字未満・100 超、語が空・100 超、タブなし) は飛ばして `Skipped` に数える。不変なので、読む側にロックは要らない。
- `UserDictionary`: `_terms` を持つ (ユーザー辞書の `_builtIn` と同じく保存・表示 (`Words`)・書き出しに出ない)。`Split` は同じ長さならユーザー辞書・組み込み語句が先、より長ければ専門用語が勝つ (最長一致)。`Lookup` はユーザー辞書の後ろに強制型の同じ読みを足す。`LookupTermCandidates` (候補追加型)、`PredictTerms` (予測)、`LoadTerms(IEnumerable<string>)` (テスト用の入口。`Version` を進めてキャッシュを捨てる)。同梱は `TermDictionary.Embedded` (プロセスで 1 つ。複数のユーザー辞書インスタンスで共有)。`builtIn: false` のときは使わない。
- `CompositionController`: `JapaneseCandidates` で候補追加型を (エンジンの単独変換の次に) 足し、`CollectPredictions` を ユーザー辞書 → 専門用語集 → エンジン → 履歴 の順にした。
- `DictionarySource.ReadEmbeddedWithPrefix`: 埋め込みの `terms-*.txt` を名前順に全部読む。
- `tools/check-terms.mjs` (検査。`--self-test` で自己テスト 20 件)。`check-dictionaries.mjs` は `terms-*.txt` を対象外にした。
- FFI は変えていない (`AbiVersion` はそのまま)。語が 0 件のときの結果は旧版と同じ (`Terms_EmptyMeansSameAsBefore`)。

**測定 (合成 5 万語・読み 3〜12 文字、`Terms_FiftyThousandWords_StayFast`)**: 読み込みと索引作成 約 50〜60 ms、メモリ増 約 13 MB、Split 約 5 µs (語 0 件は 1 µs 未満)、予測 約 1 µs、実キー入力 (ライブ変換 + 予測) 1 キー平均 0.108 ms (語 0 件 0.104 ms)。

**変更ファイル**: `src/Meltype.Core/Composition/TermDictionary.cs` (新規)、`UserDictionary.cs`、`CandidateDictionary.cs` (`Endings` を internal に)、`CompositionController.cs`、`Detection/WordList.cs`、`src/Meltype.Core.Tests/TermDictionaryTests.cs` (新規、10 件)、`dictionaries/terms-template.txt` (新規)、`tools/check-terms.mjs` (新規)、`tools/check-dictionaries.mjs`、`docs/DICTIONARY.md` (新規)、`CONTRIBUTING.md`、`THIRD-PARTY-NOTICES.md`。

**既知の制限 (対応しない)**
- 強制型は読みの一致だけで、文脈は見ない (同じ読みの別の語は、候補から選び直す)。同じ読みの専門用語が複数あるとき、先頭はファイルの先に書いた語。
- 候補追加型は候補の先頭にならない (確実に先頭にしたい語は、ユーザー辞書に登録する)。
- 候補追加型の助詞付き (すうを → 数を) は、既存の候補辞書と同じ決まった助詞だけ。
- 語を足したら、アプリの再ビルドが要る (利用者の手元で語を足す仕組みではない)。
- 予測は読みの前方一致の先頭 64 件までを数えるので、短い読み (1〜2 文字) では、読みの昇順で後ろの語は出ない。
- 同じ読み・同じ語は、複数ファイルにまたがっても 1 つにまとめる。

### P19 査読後の修正

- `tools/check-terms.mjs`: パスに空白・日本語があると、直接実行の判定が偽になり何も検査せず exit 0 になる問題を修正 (`fileURLToPath` と、`argv[1]` との実パス比較に変更。root の算出も同様)。空白・日本語を含むフォルダーにコピーして、不正ファイルが exit 1、`--self-test` が出力を出すことを確認。
- 引数で指定したファイルが存在しないときは、エラー (exit 1) にした (自己テストを追加)。
- 読みがちょうど 4 文字 (強制型の最短) の語を、件数と先頭 5 件つきの情報で出す (日常語の巻き込みへの注意)。出典・ライセンスが雛形の「(例: …)」のまま語が 1 つ以上あるときは警告 (自己テスト計 23 件)。
- `docs/DICTIONARY.md`: 4〜5 文字の読みは一般語と重なりやすい旨、強制型は辞書提案の「登録済み」判定に含まれ候補追加型は含まれない旨を追記。

### P19 追記: 分野ごとの有効/無効 (入力メニュー「専門用語集」)

同梱の `terms-*.txt` (ai・civil・it・medical・netslang。雛形 template は除く) を、無条件に全部読むのをやめ、**分野ごとに利用者が ON/OFF** できるようにした。**既定はすべて OFF** (専門語が日常の変換を巻き込まないため)。

- **ファイル**: 各ファイルの先頭に `# 名称: 土木・建設` の行 (入力メニューに出る分野の名前)。無ければ ID が名前になる。`tools/check-terms.mjs` は名称行が無いとエラー (自己テスト 25 件)。`terms-template.txt` にも追加。
- **ID**: ファイル名から `terms-` と `.txt` を除いたもの。有効な ID の一覧を `Settings.EnabledTermDomains` (config.json、プロファイルをまたいで共通) に保存。未知の ID は無視するが、保存し直すときも消さない。読めない config.json は上書きせず、切り替えを断る (`ContinueAfterConversionSetting` と同じ作法)。
- **`TermDomains` (新規、`Composition/TermDomains.cs`)**: 状態をプロセスで 1 つ持つ。`List()` (ID・名称・語数・有効か。語数は最初に 1 回 Parse して数だけ覚える)、`Set(id, on)`、`Current` (有効な分野の全ファイルを `TermDictionary.Parse(IEnumerable<string>)` で 1 つにまとめた辞書。組み合わせが変わるまでキャッシュ)、`Revision`。`TermDictionary.Embedded` (無条件に全部読む) は廃止。
- **全インスタンスへの反映**: `UserDictionary` は共有 (`Shared(path)`) と非共有 (path が null・テスト) があり得るので、インスタンスを数えずに、各インスタンスが `TermDomains.Revision` を見て、変わっていれば `Current` に入れ替えて `Version` を進める (変換キャッシュを捨てる)。`Version` の読み出し時と、専門用語の参照時に確かめる (整数 1 回の比較)。`builtIn: false` と `LoadTerms` で語を直接渡した辞書は、分野の設定に従わない。
- **性能の判断**: 分野ごとの辞書を順に引くのではなく、有効な分野を 1 つの辞書にまとめる方を選んだ。引く辞書が常に 1 つなので、1 キーの時間 (5 万語で約 0.1 ms) は有効な分野の数に依らず、同じ読みの語の順序・重複の整理も分野をまたいで保てる。分野ごとの辞書は持たない (まとめた辞書と 2 重にメモリを使うため)。OFF の分野は語数を数えるときに 1 回読んで捨てる (保持しない)。切り替えのあとの読み直しは、次に辞書を使う入力の時に、その入力を処理したスレッドで行う (実測 9〜13 ms)。
- **Windows / Linux**: 切り替えの画面は無い。config.json の `EnabledTermDomains` を手で編集する (反映は次回の起動から)。
- **ASCII だけの語**: 読みが 4 文字以上でも候補追加型にする (日常語を英単語に置き換えないため。`Terms_AsciiWords_AreCandidatesOnly`)。
- **名称**: 先頭のコメントの並び (空行可) の `# 名称:` から読む。空なら ID。
- **FFI**: `meltype_term_domains` (1 行 1 分野の「ID Tab 名称 Tab 語数 Tab 有効なら 1」を改行でつないだ文字列。`meltype_free` で解放。無ければ NULL)、`meltype_set_term_domain` (ID と on/off。保存できたら 1)。`Exports.AbiVersion` と `NativeCore.expectedAbiVersion` を 4 → 5 に同時に上げた (テストは、両者が同じことと 5 以上であることを確かめる)。
- **Swift**: 入力メニューに「専門用語集」サブメニュー。分野ごとにチェック付きの「名称 (N 語)」項目。保存できなかったときは、既存の「変換後も続けて入力できる」と同じ通知。
- **テスト**: `TermDomainTests.cs` (7 件): 既定 OFF で出ない、ON で共有・非共有・新規の辞書すべてに出る (Version が進む)、OFF に戻すと出ない、複数分野の同時有効、config.json への保存と再読み込み・ほかの設定の保持、読めない config.json を上書きしない、未知 ID の無視と保持、名称・語数の取得、同梱ファイルに名称がある。テストランナー (Program) は、利用者の本物の config.json を読まないよう `TermDomains.ConfigPath` を一時の場所にする。
- **出典・ライセンス**: 同梱の 5 分野は、ネット上の公開情報から収集した語彙。個別の原典・権利者は未特定で、メンテナーの判断で再配布可として同梱 (`THIRD-PARTY-NOTICES.md` に記載。申し出があれば削除・修正する)。

## 追加項目 P20 (辞書の管理画面「Meltype 辞書」)

**目的**: ユーザー辞書と専門用語集の語を、画面で閲覧・追加・編集・削除できるようにする (Mac)。今までは入力メニューの「選択中の文字を登録」(osascript のダイアログ) と、`userdict.txt` の手編集しか無かった。

### 設計の要点

- **採用: 別のふつうのアプリ** (`MeltypeDictionary.app`、AppKit。表示名「Meltype 辞書」)。`Meltype.app/Contents/Helpers/` に同梱し、入力メニューの「辞書を管理…」から `NSWorkspace.openApplication` で開く (すでに開いていれば前に出す。画面の側も `NSRunningApplication` で多重起動を止める)。
  - 理由: IME は `LSBackgroundOnly` で、自分のウインドウがキーボード入力を受けられない (登録のダイアログが打てず osascript にした経緯: `InputController.registerWord`)。別のアプリなら、その画面でも Meltype で日本語を打てる。
  - `Contents/Helpers` にしたのは、codesign の既定の規則 (`rules2`) で入れ子のコードとして扱われる場所だから (`Contents/Library/` 直下は資源ファイル扱い)。`codesign --verify --deep --strict` (install.sh・自動更新の検査) が入れ子の署名まで確かめる。pkg (`pkgbuild --analyze`) では Meltype.app の ChildBundles になり、置き場所の移動 (relocate) は Meltype.app の設定 (false) に従う。
  - 却下: (a) IME のプロセスの中のウインドウ (キーボード入力を受けられない)、(b) osascript のダイアログを重ねる (一覧・検索・複数選択ができない)、(c) SwiftUI (`Table` の Return・Delete・取り消しの細かい制御が macOS 13 では足りず、2 万行の更新の重さを確かめられない)。
- **辞書の読み書きは本体 (libMeltypeNative.dylib) を通す** (FFI。画面は外側の `Contents/Frameworks` のものを dlopen し、Swift の互換ライブラリも rpath `@executable_path/../../../../Frameworks` で共有する)。入力の規則 (`UserDictionary.Validate`)・ファイルの形式・ロック・正規化を C# の 1 か所にするため。Swift で直接ファイルを扱うと、規則とロックの作法が 2 重になって食い違う恐れがある。
  - FFI の追加 (`AbiVersion` 5 → 6。C# `Exports.AbiVersion`・IME の `NativeCore.expectedAbiVersion`・画面の `NativeDictionary.expectedAbiVersion` を同時に。テストが 3 つの一致を確かめる): `meltype_userdict_words` / `_version` / `_problem` / `_check` / `_add` / `_add_many` / `_update` / `_remove` / `_restore` / `_import` / `_export`、`meltype_to_reading`、`meltype_term_words` / `_excluded` / `_set_excluded` / `_edit` / `_revision`。中身は Core の `DictionaryManagement` (managed のテストで確かめる)。文字列は既存と同じく 1 行 1 件・Tab 区切り (JSON を足していない = AOT の reflection の心配が無い)。
- **ファイルを正にする** (IME と画面が別のプロセスなので):
  - 変更は `FileLock` (対象の隣の隠しファイル `.名前.lock` を FileShare.None で開く = Mac / Linux は flock、0600、消さない) の中で、ファイルを読み直してから行い、すぐ保存する (`UserDictionary.Mutate`)。古い内容のインスタンスが保存しても、相手の登録を消さない。読めないファイル (権限・文字コード) には書かず理由を返す (前は読めないと空で始め、次の登録で上書きしていた)。保存できなかったときは、メモリも変えずに理由を返す。
  - 読む側 (変換) は、ファイルの版 (`FileStamp` = 存在・大きさ・更新時刻) を 0.5 秒に 1 回だけ確かめ (`Version` / `Split` / `Lookup` / `Words` の入口。1 キーごとには整数の比較だけ)、変わっていれば読み直して `Version` を進める (ライブ変換のキャッシュの鍵に入っているので捨てられる)。
  - 索引 (読み → 語) は不変のオブジェクトにまとめて参照ごと入れ替える (`Index`。前は 3 つの欄を別々に書き換えていた)。
  - `config.json` も、`TermDomains.Set` と `ContinueAfterConversionSetting.Set` をロックの中の読み直し + 書き込みにした (画面と IME が同時に別の設定を変えても消し合わない)。`TermDomains` は `config.json` と `terms-excluded.txt` の版を 0.5 秒ごとに確かめ、ほかのプロセスが変えた分野・除外を読み直して `Revision` を進める。読めない設定のときは今の状態のまま。
- **専門用語集の「削除」= 除外** (同梱のファイルは読み取り専用): 除外した語を `terms-excluded.txt` (データフォルダ、1 行に「読み[Tab]語」、SafeFile で 0600) に保存し、`TermDomains.Current` を作るときに `TermDictionary.Parse(texts, excluded)` で除く (強制型・候補追加型・予測のどれにも出ない)。`config.json` とは別にした (数千行になりうる・設定の保存のたびに書き直さない)。読みは `TermDictionary.Normalize` でそろえる。**「編集」= 元を除外 + 直した語をユーザー辞書へ** (`DictionaryManagement.EditTerm`。直した語がもうあれば除外だけ。読みも語も同じなら複製だけ)。**「複製」= ユーザー辞書へ登録**。優先順位は従来どおり ユーザー辞書 > 専門用語集 > エンジン。
- **元に戻す**: 画面は `DictionaryChange` (Swift、AppKit を使わない `MeltypeDictionaryKit`) が「実行すると逆の操作を返す」形で、ウインドウの `NSUndoManager` に積む (⌘Z / ⇧⌘Z)。削除は本体が「消した語と元の位置」を返し、`Restore` で同じ位置に戻す (同じ読みの語の中の優先順を保つ)。削除・編集の直前の内容は `userdict.txt.bak` (1 世代、0600) にも残す。削除は確認のダイアログつき。
- **入力チェック**: 入力のたびに `UserDictionary.Check` (Validate と同じ規則 + 重複。編集中は元の語を除く) を呼び、理由を赤字で出して確定のボタンを止める。重複は管理画面の登録では理由にする (`AddNew`)。入力メニューからの登録 (`Add`) は今までどおり黙って成功。
- **画面**: 日本語、AppKit の `NSTableView` (表示する行だけを作るので 2 万語でも軽い。検索・並べ替えの鍵は読み込み時に 1 回だけ作る `RowList`)。キーボード: ⌘F 検索 (↓ で一覧へ)、Delete 削除/除外、Return 編集/直す、Space 分野の ON/OFF、⌘N 登録、⌘Z。VoiceOver: 一覧・検索・ボタン・チェックにラベル、状態とエラーは読み上げの通知。色はシステムの色だけ (ダークモード)。除外は色に加えて取り消し線と「除外中」。

### リスクと対応

| リスク | 対応 |
| --- | --- |
| 同時の書き込みで変更が消える | ロックの中で読み直して書く。テスト: 4 インスタンス × 25 語の同時登録 (managed)、4 プロセス × 25 語 (AOT の確認スクリプト)。ロックを無効にすると (`DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1`) 100 語中 27 語しか残らず、テストが失敗することを確かめた |
| 1 キーごとの stat で遅くなる | 0.5 秒に 1 回だけ (間隔の中は整数の比較だけ) |
| ロックを持ったプロセスが固まる | 3 秒待っても取れなければ、変更せずに理由を返す (flock はプロセスが落ちれば外れる) |
| 更新時刻の分解能が粗いファイルシステム | 大きさも比べる。APFS はナノ秒。置き換えで保存するので、書き換えれば更新時刻が変わる |
| 画面と dylib の版の食い違い | 画面も `meltype_abi_version` を先に確かめ、違えば理由を出して終わる |
| アンインストールで画面が残る | Meltype.app の中にあるので一緒に消える。uninstall.sh は消す前に `pkill -f …/MeltypeDictionary.app/` で閉じる (実行ファイル名が 16 文字を超えて `pkill -x` では合わない) |
| 入れ替え (install.sh・build.sh・pkg・自動更新) で古い画面が残る | 入れ替えたあと同じく閉じる (変更はその都度保存済み) |

### 変更したファイル

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Config/FileLock.cs` (新規) | プロセスをまたぐロック (`FileLock`)、ファイルの版 (`FileStamp`) |
| `src/Meltype.Core/Composition/UserDictionary.cs` | ファイルを正にした変更 (`Mutate`)・版の確認と読み直し (`SyncFile` / `Refresh`)・不変の索引 (`Index`)。`AddNew` / `Update` / `RemoveRange` / `Restore` / `Check` / `AddRange(out error)` を追加 |
| `src/Meltype.Core/Composition/TermDictionary.cs` | `Parse(texts, excluded)`、`Entries` (ファイルの順の語と注記)、`TermEntry`、`TryParseLine` の注記つき |
| `src/Meltype.Core/Composition/TermDomains.cs` | 除外 (`Excluded` / `SetExcluded` / `ExclusionPath`)、分野の語の一覧 (`Words`)、ほかのプロセスの変更の確認 (`CheckExternal`)、`Set` をロックの中に |
| `src/Meltype.Core/Composition/DictionaryManagement.cs` (新規) | 管理画面の操作 (`EditTerm` / `Import` / `Export` / `ToReading`) と FFI の文字列の形式 |
| `src/Meltype.Core/Config/ContinueAfterConversionSetting.cs` | 保存をロックの中に |
| `src/Meltype.Core/Config/AppPaths.cs`、`Backup.cs` | `terms-excluded.txt` (バックアップ・復元の対象にも) |
| `src/Meltype.Mac.Native/Exports.cs` | 上の FFI、`AbiVersion` 6 |
| `mac/Package.swift` | `MeltypeDictionaryKit` (ライブラリ)・`MeltypeDictionary` (実行ファイル) |
| `mac/Sources/MeltypeDictionaryKit/*.swift` (新規) | FFI (`NativeDictionary`)、形式 (`FfiFormat`)、一覧 (`RowList`)、元に戻す (`DictionaryChange`)、`--self-test` (`SelfTest`) |
| `mac/Sources/MeltypeDictionary/*.swift` (新規) | 画面 (AppKit) |
| `mac/Sources/MeltypeIME/DictionaryApp.swift` (新規)、`InputController.swift`、`NativeCore.swift` | 入力メニューの「辞書を管理…」、`expectedAbiVersion` 6 |
| `mac/Resources/Dictionary/Info.plist`・`ja.lproj` (新規) | 画面の Info.plist (版は build.sh が Meltype.app と同じにする) |
| `mac/build.sh`、`install.sh`、`uninstall.sh`、`pkg/scripts/postinstall` | 組み立て・rpath・署名 (中から外へ)・署名の検査、SHA-256 の表示、入れ替え・削除の前後に画面を閉じる |
| `src/Meltype.Core.Tests/DictionaryManagementTests.cs` (新規) | 30 件 (下) |
| `src/Meltype.Core/Config/SafeFile.cs` | `ReadAllTextStrict` (文字コードを厳密に読む。下の「査読後の修正」) |
| `src/Meltype/UI/UserDictionaryForm.cs` (Windows) | 削除を `RemoveRange` 1 回に (`.bak` が最後の 1 語の分だけにならない)、取り込みの保存失敗を理由で出す |
| `src/Meltype.Core.Tests/ContinueAfterConversionTests.cs`・`TermDomainTests.cs`・`Program.cs` | ABI の 3 者の一致と FFI の入口、テストが本物の `terms-excluded.txt` を読まない |
| `tools/check-mac-aot-settings.py` | AOT 版で、辞書の操作・別プロセスでの読み直し・4 プロセスの同時登録・動いている IME のつもりのプロセスへの反映 |

### テスト

- `DictionaryManagementTests` (30 件): ほかのインスタンス (= 別のプロセス) の登録・編集・削除がすぐ見える / 確認の間隔と `Refresh` / ライブ変換のキャッシュが捨てられる / 共有インスタンス (IME のセッション) にも反映 / 古いインスタンスが保存しても相手の登録を消さない / 4 インスタンスの同時登録で 100 語残る (0600・一時ファイルなし) / ロックが取れなければ変更しない / 読めないファイルを上書きしない (ユーザー辞書・除外) / 入力チェックが Validate と同じ + 重複 / 編集で位置を保つ・重なり・元が無い / 削除の位置と元に戻す (重複行・後ろすぎる位置) / 除外が強制型・候補・予測から消え、`terms-excluded.txt` (0600) に保存され、`config.json` に入らず、再起動後も残り、戻せる / 読みの正規化・不正な行 / ほかのプロセスの除外・分野の切り替え・壊れた設定 / 分野の語の一覧と除外の印・FFI の形式 / 専門用語の編集 (登録 + 除外、複製だけ、登録済み、不正) / `config.json` を 2 つの設定が同時に書いても両方残る / 取り込み・書き出し / 形式の往復 / ローマ字の読み / バックアップの対象。
- 画面: `MeltypeDictionary --self-test` (ロジック 33 件 + 本体を通した操作 31 件。`MELTYPE_DATA_DIR` が一時フォルダーの中の空のフォルダーのときだけ本体を通す)。コマンドライン ツールだけの環境では swift-testing が動かない (マクロのプラグインで失敗) ので、実行ファイルに組み込んだ。画面の手動確認の手順は `mac/README.md`「辞書の管理」。

### P20 査読後の修正 (別のエージェントの査読で見つかったもの)

- **重大**: 「読めないファイルには書かない」が文字コードでは守られていなかった。`File.ReadAllText` は不正な UTF-8 (Shift_JIS で保存したファイルなど) を黙って U+FFFD に置き換えるので、次の登録・除外で元のバイト列を失っていた (前からの挙動だが、書く機会が増えた)。
  `SafeFile.ReadAllTextStrict` (BOM があれば UTF-8 / UTF-16 LE・BE、無ければ厳密な UTF-8。不正なら例外) で読み、読めなければ書かずに理由を返す (`userdict.txt`・`terms-excluded.txt`)。テスト: Shift_JIS のファイルが 1 バイトも変わらない・UTF-16 (BOM) は読める (managed と AOT の確認スクリプト)。
- **重大**: タブをまたいで ⌘Z すると、やり直し (⇧⌘Z) が積まれなかった (隠れたタブの `view.window` が nil)。取り消しの中のやり直しは、取り消しが持っている同じ `undoManager` に積み、シート・エラーはウインドウ (`hostWindow`) に出す。
- **重大**: 「ユーザー辞書へ複製」を多くの語でやると、1 語ごとに読み直し・保存 (fsync) で語数の 2 乗の時間がかかり、IME も大きくなるファイルを読み直し続けた。`UserDictionary.AddMany` (`meltype_userdict_add_many`。1 回の読み直し・保存で、新しく登録した語を返す) にし、⌘Z 1 回で新しく登録した語だけを消す。
- 専門用語の「直す」で、ユーザー辞書への登録のあと除外に失敗すると、登録だけが残り取り消せなかった。C# の側で登録を消して戻す (戻せないときだけ `added` を返し、画面がそれを消す取り消しを積む)。取り消し (`revertTermEdit`) は、除外をやめてから消す順にし、消せなければ除外をやり直す。
- 分野を切り替えると、前の分野の選択 (行の番号) が残り、Delete で別の語を除外しえた。切り替えたら選択を外す。
- 読めない・大きすぎる `userdict.txt` を、画面が黙って「0 語」と出していた。`UserDictionary.Problem` (`meltype_userdict_problem`) で理由を返し、一覧の上に注意を出す。
- 取り込みの取り消しを、取り込みの前後の差ではなく、本体が返した「新しく登録した語」で行う (取り込みの間に IME が登録した語を消さない)。
- `FileStamp` に作成時刻も入れた (置き換えで保存するので、更新時刻が 1 秒単位のファイルシステムで同じ大きさに 2 回書き換わっても気づける。テストは、作成時刻を外すと失敗することを確かめた)。前はあったファイルが読み直しのときに無ければ、50 ms 待ってもう一度だけ確かめる (ロックを守らないエディターが消してから作り直す途中で、空として書かないため)。
- Windows のユーザー辞書の画面: 複数の削除を 1 回の `RemoveRange` に、取り込みの保存失敗を理由で出す。

### 既知の制限 (対応しない)

- 画面で除外を変えるたびに、IME は次の打鍵で、有効な分野の専門用語集を作り直す (全分野 ON で数十 ms。前は分野の切り替えのときだけ)。
- 入力メニュー・画面から前に出す (`activate`) が、macOS 14 以降の協調的なアクティベーションで効かない場合があるかは、実機で未確認。
- Windows 版・Linux 版には画面が無い (Core の変更 = ファイルを正にした保存・除外は共通に効く。除外は `terms-excluded.txt` を手で編集する)。
- 取り消し (⌘Z) は画面を開いている間だけ (閉じると消える)。それより前の状態は `userdict.txt.bak` (削除・編集の直前の 1 世代)。
- 画面は 1.5 秒ごとに版を確かめて読み直す (IME の側の登録がすぐには出ないことがある)。編集のシートを開いている間に、ほかのプロセスが元の語を消すと、保存のときに「元の語が見つかりません」と出る。
- 除外は読みと語の組で、すべての分野に効く (分野ごとの除外はしない)。同梱の語が版の更新で無くなっても、除外の行は残る (「除外した語 (すべての分野)」から戻せる)。
- アプリのアイコンは氷の M (`mac/Resources/Meltype.icns`、0-6 で追加。2026-10-14 以前は汎用のアイコンだった)。


---

## 追加項目 P21 (Mac 版の変換の高速化: azooKey の差分変換)

Mac 版で、読みが長くなるほど 1 キーごとの変換が重くなる件の続き (P16 は Core 側の区切り)。**先に測ってから**直した。計測は `MeltypeIME --bench` (リポジトリに残した。`mac/Sources/MeltypeIME/Bench.swift`)。Core (C#) は変えていない。

### 計測 (従来 → 新。Release、実機の azooKey、1 キーあたり ms)

ひらがなの文 30 種をつなげて 1 文字ずつ打つ模擬。1 キーで Core と同じ 3 呼び出し: (1) 予測 (読み全体が 40 文字以下のとき) → (2) ライブ変換 (50 文字ごとに区切った最後) → (3) 変換結果の各文節の読みの候補作成 (前の文節は `resultCache` に当たる)。「遅い 10%」は遅い方から 10% のキーの平均。

最終 (スロット選択の調整後。旧→新の順に測った):

| 文字数 | 従来 平均 | 従来 遅い10% | 新 平均 | 新 遅い10% |
| --- | --- | --- | --- | --- |
| 10 | 11.0 | 26.8 | 5.6 | 22.7 |
| 50 | 35.1 | 70.9 | 15.7 | 48.2 |
| 100 | 29.6 | 58.6 | 13.2 | 42.8 |
| 200 | 24.7 | 53.6 | 11.4 | 38.8 |
| 400 | 20.2 | 45.7 | 10.2 | 34.9 |

新→旧の順に測っても同じ傾向 (平均は新 6.2 / 15.7 / 12.6 / 11.1 / 10.1)。約 2 倍。

調整前 (第 2 ラウンド。複数セッションのみ。旧→新): 新の平均 6.3 / 17.3 / 15.0 / 12.8 / 11.5 (従来 12.3 / 37.4 / 31.2 / 27.0 / 22.5)。

**第 1 ラウンドの数字 (新の平均 4.4 / 5.6 / 4.3 / 3.4 / 3.0 = 5〜6 倍) は採らない**。そのときは 1 キーに「変換だけ」「予測 → 変換」の 2 呼び出ししか模擬しておらず、実アプリの 3 つ目 (文節ごとの候補作成) が前回の入力を上書きする影響を含めていなかった。実際の 3 呼び出しでは約 2 倍。

azooKey の経路ごとの 1 呼び出し (新、20〜50 文字の読み 24 文の平均): 先頭から全計算 (kana2lattice_all) 13.4 ms、1 文字伸ばし (changed) 2.6 ms、同じ読みをもう 1 度 (no_change) 1.2 ms。

### 原因

1. `requestResults` / `predictions` が `requestCandidates` の直後に毎回 `stopComposition()` を呼んでいた。azooKey が持つ前回の入力 (previousInputData) と lattice が捨てられ、差分変換 (`kana2lattice_changed` / `no_change`) が使われず、1 キーごとに読み全体を全計算 (`kana2lattice_all`) していた。
2. 単に `stopComposition` をやめるだけでは足りなかった。1 キーに (1) 予測 (読み全体) → (2) ライブ変換 (読み全体の最後の区切り) → (3) 文節ごとの候補作成 (最後の文節の読み) と読みの違う要求が続き、(3) が previousInputData を上書きするので、次のキーの (1) は共通接頭辞がほぼ無く全計算に戻った。読みの系列ごとに別のセッションが要る。

### 修正

| ファイル | 内容 |
| --- | --- |
| `mac/Sources/MeltypeIME/Converter.swift` | (a) azooKey のセッション 3 本のプール (`Slot`: `lastReading`・最終使用)。要求の読みと共通接頭辞が最長のスロットで `withSession` して変換 (同点は最近使った方。共通接頭辞が 0 なら空のスロット → 最も古いスロット)。直前に使ったスロットの読みの真の接頭辞が来たら (読み全体の変換のあとの先頭の文節) そのスロットは避け、読み全体の系列を上書きしない。(b) `endComposition()` で全セッションをリセット。`learn`・`resetLearning` でも呼ぶ。(c) 従来の動き (毎回 `stopComposition`) は `incremental: false` の変換器として残した (ベンチの比較用)。(d) `init(dictionaryURL:emojiDictionaryURL:memoryDirectory:incremental:)` と `rawMainResults(for:)` (キャッシュを通さない) を追加。本番コードにベンチ用の static・環境変数は置いていない |
| `mac/Sources/MeltypeIME/InputController.swift` | 変換ボックスが閉じるとき (`apply` の確定、`hideComposition`、`commitComposition`) に `endComposition()`。1 キーごとの経路には入れていない |
| `mac/Sources/MeltypeIME/main.swift` / `Bench.swift` (新規) | `MeltypeIME --bench` (速度・一括 vs 1 文字ずつ・ランダム編集・学習を挟んだ比較・予測だけの比較) |
| `mac/README.md` | `--bench` の説明 |

### 挙動の変化と正しさの検査

- **変換結果は新旧で一致**: 30 文の「一括変換 vs 1 文字ずつ」は新旧とも 30 / 30。ランダム編集 (種固定、400 ステップ、1 文字追加・Backspace・読みの切り替え・予測呼び出し、50 文字の区切りをまたぐ文) で、333 要求の上位 10 件の text が毎回キャッシュを通さない従来の変換器と**完全一致** (不一致 0)。学習 (3 つの読みで 2 番目の候補を学習) を挟んで続けて打つ 698 要求も不一致 0。
- **予測の並びが変わる (仕様として受け入れる)**: azooKey の「安定した予測」(前のキーの予測で今の読みにも合うものを先頭に固定する仕組み) は、毎回 `stopComposition` していた従来は働かなかったが、新ではセッションが続くので働く。予測だけを 2〜40 文字打つ模擬 (ライブ変換 OFF 相当) で比べると、30 文中 18 文・668 キー中 26 キーで予測が違った。**並び順だけの違いは 0、いずれも中身の違い** (新は前のキーの予測を引き継いで出し、旧は出さない・別のものが出る)。例: 「おくれてもうしわ」→ 新 [遅れて申し訳, 遅れて申し訳な, 遅れてもうシワシワ] / 旧 [遅れてもうシワシワ, 遅れてもう皺寄せ, 遅れてもう仕分け]。「おくれてもうしわけ」→ 新 [遅れて申し訳な] / 旧 []。「ごかくにんのほどよろし」→ 新 [ご確認の程よろしく, ご確認の程よろしくね, ご確認のほどよろしく] / 旧 [ご確認のほどよろしく, ご確認のほど宜しい, ご確認のほどよろしい]。候補のちらつきが減る方向の azooKey 本来の挙動。
- 変換ボックスが閉じるたびに組版を終えるので、次の入力は全計算から始まる (最初の数キーは従来と同じ速さ)。

### 残っている・直していない点

- 文節ごとの候補作成 (3) は新でも平均 約 4.4〜4.8 ms (新しい文節が出るたびに全計算相当と推定。従来と大差ない)。
- 予測 (P) が、ところどころ 10〜26 ms に跳ねる。lattice の全計算に戻ったのか、予測の候補生成そのものかは時間からは区別できていない (未確認)。azooKey 内部の `kana2lattice_*` の呼び分けは直接見ていない。
- Swift の処理はメインスレッドで同期的に azooKey を呼ぶ (P16 と同じ。据え置き)。
- 実機 (IME として実際に打つ) では未検証。ベンチは 1 文字ずつ前進する模擬で、文節の選択・再変換のような読みの飛び方は、ランダム編集の範囲でしか試していない。学習データは空の一時フォルダーで測った (大きい学習データでの時間は未計測)。

### 実機で確認してほしい点

- 長めの文 (50〜200 文字) を続けて打って、1 キーの反応が以前より軽いか (体感)。
- 変換 → 続けて入力 → 確定、Backspace、再変換、フォーカスを別の入力欄に移す、の前後で、変換結果・予測が不自然に変わらないか (古い入力の状態が残っていないか)。
- 予測の候補が、1 文字ごとに前のキーの候補を引き継いで出る (ちらつきが減る) こと。
- 学習 (候補を選んで確定) したあと、同じ読みで学習した候補が先頭に出ること。

### P21 査読後の修正

- **スロット選択**: 読み全体の変換のあとに先頭の文節の変換が来ると、読み全体のスロットが短い読みで上書きされ、次のキーの予測で後ろ半分を計算し直していた。直前に使ったスロットの読みの真の接頭辞は避けるようにした (平均 100 文字で 15.0 → 13.2 ms)。
- `withSession` の `try?` と `??` の組み合わせ (Optional が戻り値のとき処理が二重に走りうる) を `do` / `catch` に変えた。ベンチ専用の static・環境変数を本番コードから除き、引き数付きの初期化にした。

## 追加項目 P22 (Mac 版: 確定し直しが別の場所の文字を消す)

### 現象

Google ドキュメントで、確定済みの文字 (例: 「v1.1.2】Mel」の「2】Mel」) が、変換中の 1 文字に置き換わる。確定済みの文字を消す処理は Core の「確定し直し」(`CorrectPreviousCommit`) だけ。

### 原因

- (a) Windows 版は Meltype を通らないキー・クリックで `ForgetLastCommit` を呼び、確定の記録 (`_correctable`) を捨てる。Mac 版はマウスクリックを知る手段が無く、記録が残ったまま別の場所で確定し直しが発火する。
- (b) Google ドキュメントなど `selectedRange` や範囲が不正確なアプリでは、消す範囲がずれる。Swift 側は消す前に中身を確かめていなかった。

### 修正 (3 層の防御)

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Composition/CompositionController.cs` | `CorrectPreviousCommit` で、`_precedingText` があり `original` で終わっていなければ、ログに残して記録を捨て、確定し直さない。`original` が 20 文字を超えるときも (比べられないので) 直さない。`IHost.DeleteBackward(int, string expected)` を既定実装つきで追加 (既定は 1 引数版を呼ぶ。Windows の `ICompositionHost` 実装は無変更) |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | host が 2 引数版を実装して消す文字列を保持。`TextEdit` に `Original` を追加し、JSON に `"original"` を出す (null なら出さない)。Linux の `ibus-engine-meltype` は `dict.get` で読むので未知のキーは無視される |
| `mac/Sources/MeltypeIME/NativeCore.swift` | `TextEdit` に `original: String?` |
| `mac/Sources/MeltypeIME/InputController.swift` | `apply` で、キャレットが取れないとき、または `attributedSubstring(from:)` が `original` と違うときは、その edit 全体 (削除も挿入も) を飛ばす。確定済みの文字はそのまま残り、続く新しい語の確定は通常どおり入る |
| `src/Meltype.Core.Tests/CompositionTests.cs` / `SessionFacadeTests.cs` | `AutoCorrect_NotWhenCaretIsElsewhere` (別の場所へ移ったら `bs:` が出ない)、`Json_IncludesOriginalOnlyWhenPresent` |

### 挙動の変化

- クリックなどでキャレットが動いた後は確定し直しが効かなくなる (Windows 版と同じ結果)。
- 自分の確定から 1.5 秒以内 (`OwnCommitTrustMs`) は従来どおり直す (自分の記録と比べるので一致する)。この間にクリックで移ると Core の層では防げず、Swift 側の確認が頼り。

### 未検証

- 原因 (d) は実機で確認済み (トレースで marked 範囲が {0,1} のみ置換されていた)。それ以外 (確定し直しの層 1〜3、クリック検知) は実機未確認。
- Google ドキュメントで `attributedSubstring(from:)` が何を返すか (nil や空を返すなら、確定し直しは常に飛ばされる。その場合は安全側に倒れるだけで、文字は消えない)。

### P22 査読後の追加 (第 2 ラウンド)

- **U+00A0 の正規化**: Chrome・Safari の入力欄は行末の空白を U+00A0 で持つことがある。Core の `EndsWith` 比較と Swift の `attributedSubstring` 比較の前に、両側の U+00A0 を半角空白に揃える (そのままだと「sushi 」のような空白で終わる語は常に不一致になる)。
- **クリック検知 (原因 (a) の根本対策)**: `recognizedEvents` にマウス押下 (左・右・その他) を足し、`handle` の先頭で新 FFI `meltype_forget_last_commit` (`MeltypeSession.ForgetLastCommit` → Controller の同名) を呼んで `return false`。変換中の文字の確定・取消はしない。symbol は optional 解決で ABI 版数は据え置き (古い dylib なら何もしない)。
- **`HandleKey` の早期 return** (直接入力・アプリ OFF・無効) でも記録を捨てる (Windows の「アプリに渡したキーでは捨てる」と同じ)。
- **ログ**: Swift で edit を飛ばしたとき `NSLog` に飛ばした旨と文字数だけ出す (文字そのものは出さない)。
- **整合チェック**: host で、消す文字数と保持した文字列の長さが合わなければ `Original` を null にする。
- **テスト**: 1.5 秒経過後も一致すれば直す / U+00A0 で終わっていても直す / `MeltypeSession` 経由の JSON に `original` / `ForgetLastCommit` の後は直さない。

### P22 の影響範囲と限界

- 影響は Google ドキュメントに限らない。`attributedSubstring` を返さない・返すのが遅れるアプリでは、確定し直しが飛ばされる (文字は消えない安全側。確定し直しが効かないだけ)。
- 自分の確定から 1.5 秒以内のクリックは、Core の層 (層 1) が自分の記録を信じて素通しする。クリック検知が働かなければ、頼りは Swift の層 3 だけになる。

### P22 実機確認の手順 (未検証)

1. TextEdit で「i want 」が「I want 」に、「sushi がすき」が「すしがすき」に直ること。
2. Chrome の Gmail の本文で同様に直ること (U+00A0 の正規化が効くか)。
3. 確定の直後に別の場所をクリックしてから続けて打ったとき、直さず元の文字が残ること。IMK がマウス押下を `handle(_:client:)` に渡すかは実機でしか分からない。
4. Google ドキュメントで FileLog を ON にして再現手順を試し、ログに「確定し直しました」が出るか、`Console.app` に「確定し直しを飛ばしました」が出るかを見る。

### P22 原因 (c): 確定とキーの素通しが同じイベントで起きる (当時の案。誤り。取り消し済み)

- 英数字だけの変換ボックスで矢印を押すと「確定 + キーの素通し」が同じイベントで起き、Google ドキュメントが混乱すると考え、Swift で移動キーを飲み込む案を入れた。
- 後のトレースで原因 (d) が確定し、この仮説は誤りと分かったため、変更 (`hadMarkedText` / `isNavigationKey`) はすべて取り消した。コードには残っていない。

### P22 原因 (d): 機能キーの文字が変換ボックスに入る (トレースで確定)

- **トレースの要点**: 右矢印のたびに `view.text` の長さが 1 → 2 に増え、`setMarkedText` の selectionRange が {2,0}、marked 範囲が {0,1} → {0,2} と広がった (U+F703 は不可視なので表示では分からない)。
- **原因**: `InputController.handle` は `event.characters` の UTF-16 が 1 単位なら `character` として Core に渡す。矢印キーなどの機能キーの `characters` は Unicode 私用領域の文字 (U+F700〜U+F8FF。右矢印 = U+F703 `NSRightArrowFunctionKey`) で、Core には「vk=Right, ch=U+F703」が届く。英数字だけの変換ボックス (「2」) は矢印の処理 (`EnterClauseSelection`、かなのときだけ) を通らず、`CharFromKey` → `_text.Append(ch)` に落ちて U+F703 が変換ボックスに追加される。Google ドキュメントは marked 範囲を 1 文字ずつ広げ、Enter の `insertText` がその範囲を置き換えて既存の文字を消す。Core 単体 (ch = null) では「確定 + 素通し」になるので、原因 (c) の前提は誤りだった。
- **修正**:

| ファイル | 内容 |
| --- | --- |
| `mac/Sources/MeltypeIME/InputController.swift` | `character` を決めるとき U+F700〜U+F8FF は 0 にする。原因 (c) の移動キーを飲み込む変更と `isNavigationKey` は削除 |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | `HandleKey` の入口で ch が U+F700〜U+F8FF なら null に、host の `CharFromKey` も同じ範囲を null にする (Linux 側からも同じ経路で来うるため二重に守る) |
| `src/Meltype.Core.Tests/SessionFacadeTests.cs` | 「2」→ 右矢印 (ch = U+F703) で `consumed:false`・commits は「2」だけ・view が null。「か」→ 同じ右矢印で文節選択に入り view.text が「か」のまま |

- **トレース機能**: `MeltypeTraceIMK` (既定 OFF)、出力は `imk-trace.log`。そのまま残した。
- **実機確認の手順**: Google ドキュメントで「2」→ 右矢印連打 → Enter で後ろの文字が消えないこと。英単語 (google) → 右矢印でキャレットが 1 つ右に動くこと。TextEdit でも同じこと。

### P22 補足 (最終査読の反映)

- **`_precedingText` の照合は Windows にも効く**: 照合は共通 Core (`CorrectPreviousCommit`) にあるため、Windows 版にも効く。UI Automation で読んだ前の文字と食い違うアプリでは、確定し直しが止まりうる (Windows は実機未確認)。ホスト実装は無変更という意味でのみ「無変更」。
- **素通しキーで記録を捨てる**: `MeltypeSession.HandleKey` の通常経路で、アプリに渡したキー (修飾キーを除く) のとき `ForgetLastCommit` を呼ぶ (Windows の `MeltypeEngine` と同じ)。
- **機能キーの文字の範囲**: U+F700〜U+F8FF を落とす。境界はテスト済み (U+F6FF は文字として通り、U+F8FF は落ちる)。
- **マウス押下の検知**: トレースでは「マウス押下」が 0 件 (858 行を確認)。Chrome では IMK がマウスを渡していない可能性が高い。害はないので残す。
- **トレース機能の注意**:
  - ON: `defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool true`。OFF: `defaults delete io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK`。
  - 設定は起動時に 1 回だけ読むので、切り替えたら IME のプロセスを再起動する (`pkill -x Meltype`)。
  - `~/Library/Application Support/Meltype/imk-trace.log` (0600) に、打った文字・キャレットの前後 20 文字・確定/変換中の文字が残る。使い終わったらこのファイルを消す。
  - 秘匿入力 (パスワード欄) とアプリ別 OFF のアプリでは記録しない (その判定より後でログを出す)。

## 追加項目 P23 (Shift+Enter で確定と改行)

- **現象**: Web アプリ (Chrome など) で、文を打ってそのまま Shift+Enter しても改行できない。
- **原因**: 変換ボックスが開いている間、`CompositionController` の Return 処理 (通常・変換中 `HandleConversionKey`・予測中 `HandlePredictionKey`) は Shift の有無にかかわらず「確定して消費」する。確定だけされて、改行のキーがアプリに届かない。
- **修正** (`CompositionController.cs`): 変換ボックスが開いていて Shift+Enter (`_swallowedShift.Count > 0 || _host.IsShiftDown()`) のときは、確定したうえで `ReplayDown(e)` でキーをアプリに渡す。予測候補を選んでいるときはその予測を確定、未変換のときは `FixTypos()` してから確定。Enter 単体は従来どおり確定のみ (チャット欄で送信されないように)。変換ボックスが空のときの Shift+Enter は従来どおり素通し。`docs/USAGE.md` のキー表にも追加。
- **Windows への影響**: 同じ Core を通るので、Windows でも変換中の Shift+Enter は「確定 + 改行」になる (フックが送り直す)。Windows は実機未確認。
- **テスト**: `CompositionTests` に 4 件 (未変換 / Enter 単体 / 変換中 / 予測中)、`SessionFacadeTests` に 2 件 (Shift+Enter は `consumed:false`、Enter 単体は `consumed:true`)。

### P23 実機確認 (2026-10-09)
- Google ドキュメント (Chrome): 下線ありの Shift+Enter で確定 + 改行を確認。
- Discord (Chrome の Web 版): 最初は下線なしでも Shift+Enter が効かなかったが、ページの再読み込みで解消。トレースでは Meltype は Shift+Enter をすべて素通ししていたので、P22 の修正前の版 (矢印キーの機能キー文字が変換ボックスに入っていた) での操作で Discord のページ側の状態が固まっていたと判断。再読み込み後は「下線あり Shift+Enter → 確定 + 改行」「続けて下線なし Shift+Enter → 改行」「Enter 確定 → Shift+Enter → 改行」のすべてが正常で、固まらない。コード変更なし。

## 追加項目 P24 (Shift+Enter の確定と改行をオン・オフ)

- **目的**: P23 の「変換中の Shift+Enter は確定してキーをアプリに渡す」を、設定で切れるようにする。アプリによっては Shift+Enter で確定だけしたい人がいるため。
- **方針**: 設定 `ShiftEnterNewline` (bool、カテゴリ「1. 全般」、**既定 ON**)。既定 ON の理由は、1.1.1 の挙動を変えないため (既存の `config.json` に項目が無ければ ON)。OFF のときは、変換中の Shift+Enter は Enter 単体と同じ (確定のみ、キーはアプリに渡さない)。変換ボックスが空のときの Shift+Enter は ON/OFF にかかわらず素通し。プロファイルをまたいで共通にはしない (`ContinueAfterConversion` と同じ。`SharedKeys` に入れない)。Mac は入力メニューのチェック項目「Shift+Enter で確定して改行」、Windows は設定画面 (属性から自動生成) で切り替える。
- **SettingsVersion**: 既定値を足しただけで、`Migrate` は不要 (項目が無い古い `config.json` を読むと既定の true になる。プロファイルの切り替えも、項目が無ければ今の値のまま)。
- **共通化**: 「プロセスで 1 つの値を持ち、ロックの中で読み直して `config.json` に保存し、読めない `config.json` は上書きしない」作法を `ConfigFlag` に切り出した。`ContinueAfterConversionSetting` は `ConfigFlag` に委譲する薄い入口にして、公開している名前と挙動は変えていない。`ShiftEnterNewlineSetting` が同じ `ConfigFlag` (既定 ON) を使う。
- **ABI**: 版数は上げない。Swift は新しい 2 つの symbol を optional に解決し、古い本体では項目を出さず、ON (従来の動作) 扱いにする。

| ファイル | 内容 |
| --- | --- |
| `src/Meltype.Core/Config/Settings.cs` | `ShiftEnterNewline` (既定 true) |
| `src/Meltype.Core/Config/ConfigFlag.cs` (新規) | 共有の bool 設定の読み書き (旧 `ContinueAfterConversionSetting` の中身) |
| `src/Meltype.Core/Config/ContinueAfterConversionSetting.cs` | `ConfigFlag` への委譲に |
| `src/Meltype.Core/Config/ShiftEnterNewlineSetting.cs` (新規) | `IsOn` / `Set` / `Reset` |
| `src/Meltype.Core/Composition/CompositionController.cs` | `CompositionOptions.ShiftEnterNewline` (Func、既定 true)。Shift+Enter のブロックの条件に追加 |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | `ShiftEnterNewline = () => ShiftEnterNewlineSetting.IsOn` |
| `src/Meltype.Mac.Native/Exports.cs` | `meltype_get_shift_enter_newline` / `meltype_set_shift_enter_newline` |
| `mac/Sources/MeltypeIME/NativeCore.swift`・`InputController.swift` | getter/setter (optional symbol)、入力メニューのチェック項目、保存失敗時のアラート |
| `src/Meltype/UI/TrayApplicationContext.cs`・`src/Meltype/Composition/CompositionService.cs` | Windows のオプションに `ShiftEnterNewline` を渡す |
| `tools/check-mac-aot-settings.py` | AOT 版で OFF の保存と別プロセスでの読み直しを確かめる |
| `docs/USAGE.md`・`mac/README.md` | キー表と説明 |
| `src/Meltype.Core.Tests/ShiftEnterNewlineTests.cs` (新規)・`SessionFacadeTests.cs` | 下記 |

- **テスト**: 設定が OFF のとき、未変換・変換中・予測中の Shift+Enter が確定のみ (`down:0D` なし)。設定の読み書き (項目が無ければ ON、`Set(false)` が保存される、読めない `config.json` は上書きしない)。`SessionFacadeTests` に OFF のとき `consumed:true` の 1 件。既存の Shift+Enter のテストは既定 ON のまま通る。
- **未検証**: Mac 実機 (入力メニューの切り替えと、Chrome・Google ドキュメントでの挙動)、Windows 実機 (設定画面の表示とトレイでの反映)。

## 追加項目 P25〜P28 (本家 open issue を Mac 版 Core で再現して直した分)

本家 (yksr-melt/Meltype) の open issue を、Mac 版の Core (`--repro`) で再現できたものから、直しやすい順に対応した。**Core の変更なので Windows / Linux にも同じく効く** (Windows・Linux は実機未確認)。`--repro` は同梱の単語リストと偽の変換エンジンで動くので、Mac 実機 (NSSpellChecker・azooKey) の結果とは違うことがある。

| 項目 | 本家 issue | 内容 | 変更 |
| --- | --- | --- | --- |
| P25 | #243 | 後ろが英語のとき、バッククォートも半角にする (`` kono`config`wo `` → この`config`を、` ```python ` の 3 つとも半角) | `CompositionText.Openers` に `` ` `` を足す。バッククォートが続くときは、並びの次の文字で英語かを見る |
| P26 | #234 | **変換ボックスの先頭で** `z` + `h/j/k/l` → `← ↓ ↑ →` (Apple の日本語入力と同じ) | `CompositionText.Append`: `z` が単独で待機中で、それまでの単位が無い (または矢印だけ) ときだけ矢印にする。矢印は `FixTypos` の対象外。先頭の矢印 1 つに英字が続いたら元の綴りに戻す (zhang・zlib・zkSync)。`CompositionDetector.Segment` は先頭の矢印の単位を英語判定に回さず、日本語の区間として切り出す (macOS のスペルチェッカーは `zh` を英単語と答える。zhzl = ←→) |
| P27 | #265 | `Are you susi?` で英語の `you` を `よう` に確定し直さない | `CompositionController.CorrectPreviousCommit`: 英語の機能語 (are・do・thank・how …、`WordsBeforePronoun`) のすぐ後ろで単独で確定した `you / are / we / me / she` は日本語に直さない (`EnglishPronounAfterEnglish`)。キャレットの前の文字が分からない・直前が名詞や記号 (iPhone you・Hello, you) のときは従来どおり |
| P28 | #266 | `McAfeeto` → `McAfeeと` (本家 1640142 と同じ変更) | `CompositionDetector.CapitalizedWordEnd`: `EndsWithAcronym` (短い頭 Mc・Le の後ろの大文字は略語にしない) |

- **テスト**: 463 → 470 件。`CompositionTests`: `Backtick_BeforeEnglish_StaysHalfWidth`・`ZArrows_HJKL`・`ZArrows_DoNotBreakEnglishOrTypoCorrection` (文中・記号の後ろ・語中の zl/zk/zh と z の打ち間違いの回帰を完全一致で守る)・`ZArrows_WorkEvenIfSpellCheckerKnowsZh` (Mac 実機の報告: macOS のスペルチェッカーが `zh` を英単語と答えるため `zh` だけ矢印にならなかった。偽のスペルチェッカーで再現)・`AutoCorrect_EnglishPronounAfterEnglishFunctionWord_IsNotRewrittenToJapanese`。`FeedbackTests`: `ShortHeadThenCapital_IsName` (本家の例を移植)・`CamelCaseName_IsNotCutIntoParticle` (下記の取り下げの方針を守る)。`QualityTests`: McAfeeto。判定品質テスト (`--eval`) の外れは変更前と同じ 2 件のまま。バッククォートと矢印の新機能は、修正を外すと赤になることを確認済み。
- **検証**: 独立した査読を 4 回行い、見つかった回帰 (P26 の文中の英語・打ち間違い補正、P27 の「〜用」、P28 の区切りが英語や名前を削る) をすべて直した。変更前のビルドと変更後を、約 1,400 入力、査読者が作った敵対入力 4,917 件、日本人の名前 6,471 件、英語の大文字つなぎ語 1,508 件で比べた。
- **取り下げ: 本家 #268 (`DeVitode` → `DeVitoで`) は直していない。** 「大文字で始まる語が 2 つ以上つながった名前 + 助詞」で区切る処理を 4 通りに作り直して試したが、そのたびに、実在の名前や英語を削る回帰が査読で見つかった (`TanakaMakoto` → `TanakaMako|と`、`TanakaTaiga` → `TanakaTai|が`、`TanakaKanade` → `TanakaKana|で`、`UserGuide` → `UserGui|で`、`GeorgeWashingtonga` → `GeorgeWashing|とんが`)。`DeVito|de` と `Kana|de` は機械的に区別できない。区切らなければ、名前は英字のまま残り (`ClaudeCodede` や `DeVitode` は変更前のまま)、利用者が直せる。名前を壊す損のほうが大きいと判断して、この処理は入れていない。本家の報告者も「実際に使っていて出会ったものではありません」と書いている。入れ直すなら、辞書に載っていない名前だけでなく「利用者がその語を英字として確定した履歴 (Memory)」を条件にするなど、利用者の明示的な手がかりが要る。
- **トレードオフ**:
  - P26: 矢印になるのは変換ボックスの**先頭だけ**。文の途中 (`kyouhazl`)・記号の後ろ・語の途中の `zl` / `zk` / `zh` は矢印にならない (zlib・buzzkill や、`zu` を `zj` と打ち間違えた補正 `mizjwo` → みずを を守るため)。文の途中に矢印を入れたいときは、Space で区切るなどして新しく打ち始める。先頭の `zh` の直後にそのままローマ字を続ける (`zhkonnichiha`) と、矢印にならず英字扱い。矢印が 2 つ以上あとに英字が続く (`zhzlwotsukau`) と戻せない。先頭の `zu` を `zj` と打ち間違えて、すぐ後ろに何も続けず確定すると矢印になる。Space の後に単独で `zh` / `zk` / `zl` を打つ (`hello zh`) と、新しい入力の先頭なので矢印になる。`zh-CN`・`zk.js`・`zh1`・`zh_TW` のように記号や数字が続く形は、矢印 + 記号・数字になる (変更前も `zhーCN` のように崩れていた)。
  - P27: 直前が英語の機能語のときだけ。`iPhone you` (iPhone 用) や `Hello, you` は従来どおり日本語に直す。`help you` (ヘルプ用)・`show you`・`call you`・`love you`・`miss you` のように、機能語の一覧にある動詞の後の `you` も英語のまま (名詞の「〜用」と打ちたい人は少ないとみて、動詞を含めた)。`me` は確定の時点で日本語側に倒れることがあり、守れないことがある。`I love sushi ` の後に `gasuki` で `すしがすき` に直す既存の動き (代名詞以外) は変えていない。
- **未解決 (本家 issue のうち手を付けていないもの)**:
  - #268 (上の取り下げ)。単独の `DeVito` + Enter が `でVito` のままの件も同じ。
  - #265 の後半: `sushi?` を `susi?` と同じく日本語にする (`?` で終わる短い語の判定)。`Are you sushi?` は英語の文としても自然なので、方針を決めてから。
  - #254・#260: `*.dll`・`*.json` など拡張子の形 (`name.ext`) を英字固定にする。本家に Pull Request #267 があるので、取り込むなら本家の形に合わせる。Mac 実機では NSSpellChecker の判定で結果が変わりうる。
  - #263 `lo-fi`、#249 `mozcgamiteirutohaie`・`mocz`、#248 `pingwoutsu`。
  - #259・#246 (Space・Shift+Space を全角にする設定)。関所 (`MeltypeSession.StartsComposition`) と `StartWith` の両方に触れるので別項目。
  - 査読で見つかった小さな点: `McAfee-to` (ハイフン周りで `c` が消える)、閉じ側のコードフェンス ` ``` ` 単独は全角のまま。
- **Mac 実機の確認 (2026-10-10、メンテナー、TextEdit・JIS 配列)**: P25 `` この`config`を確認します ``・`` `git status`で確認 ``・`` `npm install`を実行 `` が期待どおり (バッククォートは JIS では Shift + @)、P26 `zh` が `←` (macOS のスペルチェッカーが `zh` を英単語と答えるため、最初は `zh` だけ矢印にならなかった。直して確認)、`zlib`・`zhang` は英字のまま、P27 `Are you すし？`、P28 `McAfeeと`。
- **未検証**: 矢印を含む読みを Space で azooKey に渡したときの挙動、BackSpace で矢印を消したときの挙動 (コードを読んだ範囲では単位・待機中の文字の整合は保たれる)。Windows・Linux の実機。

## 追加項目 P29 (拡張子・ドメインの形 `英語の語.拡張子` を英字のままにする: 本家 #254・#260)

- **現象**: `MeltypeTip.dll` が `MeltypeTip。dっl`、`Meltype.aab` が `Meltype.ああb`、`Meltype.config` が `Meltype.coんふぃg` のように、`英語の語.拡張子` の拡張子がかなになる。本家 #260 の 580 語のうち 574 語が崩れていた (Mac 版 Core の `--repro`)。
- **対応**: 本家 yksr-melt/Meltype の Pull Request #267 (本家で取り込み済み) を Mac 版の Core に取り込み、**独立した査読 (6 回・のべ 7 体) で見つかった悪化を直した**。Mac 版に本家の `ScoredSegmentation.cs` (区切りを点数で選ぶα版) は無いので、その部分だけ除いた。
- **取り込み (本家と同じ)**: `CompositionDetector.DotSuffixEnd` (新規)・`FileExtensions` (新規)・`FindSpans` の「ドメインの `.` の後ろ」の古い規則の置き換え・`CompositionText.EnglishMask` (`英語の語.` の後ろの英字を、確定の前の打ち間違いの直しの対象にしない)。
- **本家のままだと起きた悪化** (本家にも同じ問題がある可能性が高い。本家の実機では未確認): `.` の後ろの名前に確信が無くても丸ごと英字に決めるため、今まで日本語にできていた入力が崩れる。
  - 拡張子・ドメイン・英単語の後ろの日本語がローマ字のまま残る: `Meltype.mp4wokesu`、`yahoo.co.jpde`、`readme.mdwo`、`Meltype.plistwokesu`、`Meltype.systemwo`、`README.mddayo`、`app.exportttara`
  - 英単語が途中からかなになる: `cloud.storage` → `cloud.sとらげ`、`Meltype.video` → `Meltype.viでお`
  - 数字・略語・三点リーダーの後ろの日本語が英字になる: `No.5kaime`、`A.I.nohanashi`、`Hmm...souka`
  - 日本語の打ち間違いが直らない: `Hello.wakarimashta` (変更前は `Hello。わかりました`)
- **Mac 版での修正** (`DotSuffixEnd`。方針は「確信が無いときは、今までの判定に戻す」):
  - `.` で始まる名前だけを見る (`...` の 3 つ目の `.` を名前に取り込まない)。
  - 分ける位置は最後のラベル (`co.jpde` なら `jpde`) の中で、次の順に探す。「数字・`.` を含む名前は丸ごと英字」はこの後。
    - (0a) 一覧にある拡張子・ドメイン (`Known`・新しい一覧 `SplitStems`) の頭 + 助詞。長い頭から (`com|made|kesu` を `comma|de`・`co|mmade` と切らない。`m|kara|kesu` を `mka|rakesu` としない)。助詞の後ろは、読める語・英単語・大文字の略語や数字 (`mp4|を|MP3に`、`Next.js|の|SSR`) のどれか。
    - (1) 長い英単語の頭 + 助詞、または + 4 文字以上の英単語でない読める語・「っ」で始まる語 (`golden|ga`、`extension|ganai`、`export|ttara`、`documentation|dayo`、`task|kke`)。
    - (0b) 一覧にある拡張子・ドメインの頭 + 助詞以外の日本語 (`README.md|だよ`・`くらい`・`っぽい`、`github.com|じゃない`)。3 文字以上の読める語は何でも、2 文字は短い一覧 (`FollowWordsShort`: `da`・`ka`・`ne`・`jan` など) だけ。頭が 2 文字以下なら `SplitStems` のときだけ (`mka`・`usda` を `m|か`・`us|だ` としない)。頭より長い英単語で始まる名前 (`tar|get`) では使わない。
    - (2) 短い頭から: 数字入りの拡張子 (`mp4`・`7z`)、一覧に無い読めない頭 (`plist`・`ahk`・`db`・`id`) + 助詞、`FollowWordsLong` の長い頭 (`plist|dayo`)、「っ」で始まる語 (`pub|ttara`)。英単語の途中で分けないように、頭 + 助詞が英単語の頭 (`pho|to` = photo) なら分けない、助詞の後ろが 2 文字以下の読める語 (`gitig|no|re`、`car|to|on`) で頭に母音があるなら分けない、「っ」の後ろが助詞なら名前の最後の文字 + 助詞 (`plist|to|kesu`) とする。母音以外の 1 文字の頭 (`s|no`、`v|wo|kesu`、`s|ni|push`) は、助詞 `to` の後ろに語が続くとき (英単語が続くときは `mo` も) は分けない (`s|to|rage`、`s|mo|king`)。13 文字以上の名前は、助詞だけ・助詞 + 3 文字以上の読める語のときだけ。
  - ラベル全体がふつうの英単語 (`video`・`storage`) なら途中で分けない。ラベル全体が読める語 (`desu`) なら、ドメインの後ろの助詞・「っ」で始まる語だけ分ける (`tetr.io|de`、`tetr.io|ttara`)。
  - 数字の後ろの日本語 (`No.5kaime`、`No.1.2bai`、`Rev.1-3pe-ji`、`No.1tte`)・1 文字の略語の連続の後ろの日本語 (`A.I.ha`)・日本語の語の打ち間違い (`TypoDetector` で日本語の語と 1 文字違い)・助詞の `made` だけが続く入力 (ドメインの後ろは英字のまま: `github.commade`) は、英字に決めず今までどおり判定する。
  - 打っている途中の「っ」(`OK.tt` → `OK.っt`) は英字に決めない。「っ」で始まり単位がすべてかなになる語 (`OK.tte`) は読める語とみなす。
  - `FileExtensions` のうち、日本語としてよく打つ綴り (2 文字以下と `rei`: `ii`・`mo`・`ko`・`go`) は、それだけで英字に決めない (`OK.ii yo` → `OK.いいよ`)。実在する拡張子で、末尾が助詞・打ち間違い・「っ」に見えるもの (`lha`・`gcno`・`tga`・`ssa`・`tta`・`mka`・`usda` など) は一覧に足した。`swo` は `.s` + を と区別できないので入れない。
- **テスト**: `CompositionTests.DotSuffix_FileExtensionsStayEnglish` (本家のテストを移植) と `DotSuffix_ParticlesAfterSuffix_AndPlainWordsAndUnchanged` (Mac 版の追加: 上の悪化の再発防止と、変わらないもの約 50 例)。470 → 472 件。
- **変更前後の比較** (`--repro` の Enter で確定した結果。入力は自作 + 査読者が作ったもの、計 41,857 件 + 11,594 件 + 6,844 件。41,857 件のうち 30,551 件は正解を先に決めてある):
  - 正解の数 (30,551 件): 変更前 17,055 件 → 本家のまま 14,438 件 → **修正後 26,937 件**。変更前は正しかったのに修正後に間違えるのは 18 件で、すべて下のトレードオフ (本家のままだと 7,898 件)。
  - 「拡張子・ドメイン + 日本語」2,387 件 (`README.md|だよ`・`くらい`・`じゃん` など): 変更前は正しく修正後に間違えるもの 12 件 (修正の途中の版では 872 件)。
  - 6 回目の査読の正解付き 3,203 件: 変更前 1,251 件 → 修正後 1,879 件。変更前は正しく修正後に間違えるもの 15 件 (すべて下のトレードオフ)。
  - 本家 #260 の 580 語 (`Meltype.<拡張子>`): 566 語が打ったとおりの英字 (変更前は 6 語)。残りは `Meltype.swo` (`Meltype.sを`) と、日本語としてよく打つ短い綴りの 13 語 (`Meltype.go` → `Meltype.ご` など。変更前と同じ)。
  - 正解の無い入力で変更前より悪くなったように見えるものは、目で見たかぎり、変更前も間違い (英単語の打ち間違い・`Hello.demo` の `deも`)、ローマ字の音の単位が境目をまたぐもの、`-`・`/` でつないだ不自然な入力、意味の無い入力 (`amazon.wonipush`)、下のトレードオフのどれか。
  - 速さ: 200 文字の入力で、変更前より遅くならない (6 回目の査読で計測)。
- **トレードオフ**:
  - 1 文字の拡張子 + 助詞 `to` + 語 (`Meltype.stopush`・`vtokesu`)、+ `mo` + 英単語 (`smopush`) は英字のまま (英単語 `storage`・`smoking` と区別できない)。
  - 拡張子と助詞の境目を、ローマ字の音の単位 (`cha`・`sha`・`xn`・`nn`・`si`・`dyo`) がまたぐもの (`Meltype.vimrchadame`、`Meltype.classha`、`report.docxni`、`Node.jsigai`) は分けられない (変更前も多くは崩れていた)。
  - ドメインの後ろが読めて、全体が英単語に見えるもの: `Meltype.depose`・`defame` は崩れる (`de` + 読める語)、`file.code`・`file.memo`・`file.auto` は英単語として英字のまま (変更前は `file.coで`・`file.meも`)、`logo.aide` は `aide` が英単語なので `logo。あいで`、`config.doctoredde` は `doc|to` で切れる。
  - 分けた後ろが英単語として辞書にある日本語 (`photo.jpg|kana`、`Meltype.caf|made`) と、1 文字の `e` (`video.mp4e` → 変更前 `mp4え`) は英字のまま。
  - 入れ替わり型の日本語の打ち間違い (`Hello.yoroshkiu`) は、判定部から `RomajiTypoCorrector` を使えないので直らない (変更前は `よろしく`)。
  - `FileExtensions` の 3 文字以上の読める拡張子 (`ini`・`ace`・`iso`・`ufo`) は、英語の語 + `.` の後ろでは英字になる。
  - 名前と日本語の間に打った空白は残る (`final.mp4 ni commit` → `final.mp4 にcommit`。変更前は `final.mp4にcommit`)。英単語の後ろの空白 (`Python ga suki` → `Python がすき`) と同じ決まりにそろった。
  - 打っている途中、拡張子 + 助詞 + 語の子音を打つたびに後ろがローマ字に戻ることがある (`sub.srtの` → `sub.srtnoj` → `sub.srtのじ`)。確定の結果は正しい。
  - Mac 実機は macOS のスペルチェッカーで英単語の判定が変わるので、`IsKnownEnglishWord` を使う箇所 (英単語の頭・英単語が続く助詞・頭より長い英単語) は `--repro` と違うことがある。英単語と答える語が増えるほど、途中で分けない側に寄る。`IsKnownEnglishWord` は結果を覚えないので、Mac 実機では 1 回の判定で `NSSpellChecker` を数十回呼ぶ (1 回 0.1 ms ほど。実機の体感は未確認)。
- **Mac 側の性能の手当て (`src/Meltype.Mac.Native/Exports.cs` の `CallbackWordChecker`)**: `IsKnownEnglishWord` の結果を Core は覚えないので、`DotSuffixEnd` は 1 キーで `NSSpellChecker` を数十回呼ぶ (候補ごとに頭の英単語を長い方から調べる)。Windows 版の `WindowsSpellChecker` と同じく、Mac 側の `IsWord` に語ごとのキャッシュ (20,000 件で捨てる) を足した。Core は変えていない。答えは入力中ずっと使うので、macOS の辞書に語を学習させても次の起動まで反映されない (Windows 版と同じ割り切り)。Swift 側はスペルチェッカーが使えない間も「正しい綴り」と答えるので、その間に覚えた語は復帰後も次の起動まで英単語扱いのまま (従来も同じ答えだったので、差は固定されることだけ。実害が出たら Swift 側で「判定不能」を別の値にして覚えない対応)。`--repro` は同梱の単語リストを使うので、この変更は一括比較の結果に関係しない。
- **Mac 実機の確認 (2026-10-11、メンテナー、TextEdit・JIS 配列、キャッシュ入りのビルド)**: `setup.exeを実行`・`report.pdfを開く`・`MeltypeTip.dll`・`Meltype.config`・`README.mdだよ`・`github.comにpush`・`Meltype.です`・`final.mp4をけす`・`No.5回目`・`pythonがすき` の 10 件がすべて期待どおり (漢字は azooKey の変換)。打っている途中の引っかかりは感じないとのこと。
- **未検証**: Windows・Linux の実機。Mac 実機での長い入力 (200 文字級) の体感。
## P29 の後に残した候補 (2026-10-11 の再検討。着手は別項目として)

- **P29 の再検討の結論**: 点数で区切りを選ぶ方式 (本家の α `ScoredSegmentation`) への移行はしない。本家 PR #267 だけで「名前だけ」(`Meltype.dll`) は 2,672 → 3,301 / 3,550 と解けていて、`DotSuffixEnd` の約 200 行はほぼすべて「名前 + 日本語」(`setup.exeを`) の 14,383 → 23,634 / 26,644 のためのもの。残るトレードオフは `storage` と `s|to|rage` のような本質的な曖昧さで、方式を変えても負ける側がどこかに出る。
- **候補 1 (仮説・試作から)**: `DotSuffixEnd` の 4 つのループ (0a → 1 → 0b → 2) を「頭の種類 × 後ろの種類 → 分ける/分けない」の表に整理する。挙動は変えない。ただし長さの閾値・母音の有無・`to`/`mo` の特別扱い・`LongerEnglishPrefix` など表に収まらない条件も多いので、まず小さく試作し、all_in / rv3 / rv4 の約 6 万入力で差分ゼロが出るか見てから本採用を決める。ついでに一覧の重複 (`tte` が `FollowWordsShort`・`Long` の両方、`made`・`kara` が `ParticleTails`・`TrailingParticles` の両方) を整理する。
- **候補 2**: Mac 実機と `--repro` の差を測れるようにする。テストハーネスの `IWordChecker` を記録型にして一括比較中に問い合わせた語を集め、Swift スクリプトで `NSSpellChecker` の答えを JSON にし、その表を返す checker で同じ一括比較を回す (P26 の `zh` のように、実機のスペルチェッカーだけが英単語と答える語を事前に見つけるため)。
- **候補 3 (既知の制限)**: `report.docxni` (`docx|に`)・`Node.jsigai` (`js|以外`)・`classha` は、分ける位置が `CompositionUnit` の境目に限られるため分けられない。直すには強制した境目で単位を作り直す変更が `CompositionText` に及ぶ。
- 比較用のビルド・入力・正解のデータは作業用フォルダー (一時、残らない) にある。入力と正解 (約 11 MB) を残すなら、別の置き場所 (リポジトリ外か `docs/` 以外の追跡しないフォルダー) に移す判断が要る。再現の手段は `--repro`・`--segments` と、スキル `meltype-mac-maintenance` の `scripts/compare_repro.py`。

## 追加項目 P31 (判定の小さな修正と辞書の追加: 本家 #381・#320・#312・#384・#382・#383)

- **現象**: 本家の open な PR 6 件ぶん。ベースは HEAD `d2f0206`。
  - #381 (issue #375): 知らない英字の語の最後の文字と助詞の頭が同じだと区切られない。`cwdde` → `cwdde` (`cwdで` にならない)、`gpgga` → `gpgga`、`jwtto` → `jwtto`、`tddde` → `tっっで`、`nvmmade` → `nvmmaで`。
  - #320 (issue #248): `ping`・`pong` が英語の一覧に無い。同梱の英単語表では `ping` → `ぴんg`、`pingwoutsu` → `ぴんがをうつ`。**NSSpellChecker は ping を英単語と答える**ので、実機では入力中の表示 `pingをうつ` と確定 `ぴんがをうつ` が食い違っていた (base の NS 判定器で `pingwo`: showing `pingを` / committed `ぴんがを` を確認)。
  - #312: `lo-fi` → `ぉーふぃ`、`sci-fi` → `scいーふぃ` (NS では `sci-ふぃ`)。`HyphenatedWords` に無い。
  - #384 (issue #368): 変換エンジンが `しゃーなし` を `西なし` にする。
  - #382 (issue #369): `huihui` (AI のモデルを出している huihui-ai) が `ふいふい` になる。
  - #383 (issue #371): `き` の候補に 気 の旧字体 `氣` が無い。
- **対応**: 本家の差分をそのまま取り込んだ (Core は 2 行、辞書は 5 行。変更したファイル: `CompositionDetector.cs`・`dictionaries/{english,phrases,propernouns,candidates}.txt`・テスト 2 ファイル)。
  - #381: `CompositionDetector` の `UnknownWordThenJapanese` (`TrailingParticles` を見る条件) から `stem[^1] == rest[0] ||` を削る。
  - #320: `dictionaries/english.txt` に `ping pong`。
  - #312: `CompositionDetector.HyphenatedWords` に `lo-fi`・`sci-fi`。
  - #384: `dictionaries/phrases.txt` に `しゃーなし`・`しゃあなし` (同梱の語句。ユーザー辞書の後・変換エンジンの前に効く)。
  - #382: `dictionaries/propernouns.txt` に `Huihui`。
  - #383: `dictionaries/candidates.txt` に `き 気 氣` (候補の最後側に足すだけ。変換エンジンが 気 を返せば重ならない)。
- **辞書の同梱手順の確認 (#383 ほか辞書 4 つ)**: **追加の作業は要らない**。`Meltype.Core.csproj` の `<EmbeddedResource Include="..\..\dictionaries\*.txt">` が `dictionaries/*.txt` をまとめて埋め込む。manifest・ハッシュ・コピー先の一覧は無く、`mac/build.sh` は辞書を個別にコピーしない (`dotnet publish` した `libMeltypeNative.dylib` に入る)。`Exports.cs` も辞書を読まない (`CandidateDictionary.Load` / `UserDictionary` / `ProperNouns` が埋め込みを読む)。`tools/check-dictionaries.mjs` はエラー 0・警告 0。**Mac 実機に反映するには `mac/build.sh` で dylib を作り直して入れ直す必要がある** (`mac/dist` の既存の dylib は古い辞書のまま。今回は作り直していない)。
- **本家の差分から変えた点 (テストのみ)**:
  - #381: `TesterReports_20261003` に `gpgga`・`jwtto`・`tddde` を足した (`cwdde` は BuiltIn では赤だが NS では元から通る。`gpgga` などは NS でも赤)。
  - #320: 本家の `SpaceAroundEnglish_WhenTypingInTheCompositionBox` に加え、**`ping`・`pong` を英単語と答える判定器 (`PingIsWordChecker`、実機の NS と同じ答え) を差し込む `Ping_StaysEnglish_WhateverTheSpellCheckerSays`** を足した。BuiltIn と NS 型の両方で、確定結果と入力中の表示 (`pingwoutsu`) が `ping` で一致することを見る。
  - #384: 変換エンジンの偽物が `しゃあなし` も `西なし` にするようにして、`shaanashi` と、文の中 (`sorehasha-nashidayo`) も見る。
- **テスト** (472 → 478 件、全部通る。査読後に Issue375_DoubledLetterBeforeParticle_IsAcceptedTradeOff を 1 件足した):
  - 新規 5 件: `CompositionTests.Ki_OffersOldKanji` (#383)、`Ping_StaysEnglish_WhateverTheSpellCheckerSays` (#320)、`SpaceAroundEnglish_WhenTypingInTheCompositionBox` (#320)、`Shanashi_IsNotConvertedToPlaceName` (#384)、`FeedbackTests.Issue369_Huihui_StaysEnglish` (#382)。
  - 追記 2 件: `CompositionTests.HyphenatedEnglishWords_StayEnglish` (#312: `lo-fi`・`Lo-fi`・`sci-fi`・`lo-fiwokiku`)、`LanguageLearningTests.TesterReports_20261003` (#381)。
  - **赤を先に確認**: テストだけ足した状態で 7 件が落ちた (`HyphenatedEnglishWords_StayEnglish` は `lo-fi` が `ぉーふぃ`、`Ki_OffersOldKanji` は候補 `き キ ｷ ki ｋｉ`、`Ping_...` は `ping` が `ぴんg`、`Shanashi_...` は先頭候補が `西なし`、`SpaceAroundEnglish_...` は `ぴんがをうつ`、`Issue369_...` は `ふいふい`、`TesterReports_20261003` は `cwdde`)。修正後に 477/477。`Shanashi_...` の追加分 (`shaanashi`・文の中) も、`phrases.txt` を戻すと落ちることを確認した。
  - 品質テスト (`--eval-json`): 変更前 1242/1244 → 変更後 1242/1244 (外れる 2 件も同じ `my name is taro`・`apinoerror`、1,244 件の個別の成否は全件同じ)。BuiltIn のみ。
- **前後比較** (`--expect` で Enter 確定した結果。`work/regress.py`): 変更前 (HEAD) と変更後を、BuiltIn と NS の両方で同じ入力に流した。NS は、実際に `IsWord` に来た語を `NSSpellChecker.checkSpelling(language: "en")` (Mac の `isWordCallback` と同じ呼び方) に投げて答えを集め、新しい問い合わせが出なくなるまで繰り返してから `MELTYPE_WORDS_FILE` で差し込む。全件の差分は `P31-diffs.md`。

  | 入力 | 件数 | 差分 BuiltIn | 差分 NS |
  |---|---|---|---|
  | コーパス (review/work/corpus.txt) | 5,040 | 18 | 10 |
  | readings.txt の全語をローマ字にした語 + 助詞 8 種 (ga de to wo ni ha no mo) | 161,392 (20,174 語。かなに戻せない 7 語は除く) | **0** | **0** |
  | 同、助詞なし | 20,174 | **0** | **0** |
  | 語尾と助詞の頭が同じ英字の語 (辞書の英字語 + de/desu/ga/to/tte/wo/ni/no/na/ha/mo/made) | 26,641 | 30 | 29 |
  | 個別に選んだ入力 (cwd・gpg・ping・pong・lo-fi・sci-fi・huihui・しゃーなし・き など) | 179 | 57 | 40 |

  - **実在の日本語を壊したものは 0 件**。差分は全件、目で見て期待どおりの改善 (英字の語 + 助詞が区切られる、`ping`/`lo-fi`/`sci-fi`/`huihui` が英字のまま)。日本語の語 + 助詞 (約 16 万件) は 1 件も変わらない (コードの条件上も、`stem[^1] == rest[0]` の判定に届くのは、頭の読める部分が 2 文字未満の知らない語だけ)。
  - コーパスの差分 (BuiltIn 18): `cwdde`→`cwdで`、`pwdde`→`pwdで`、`gpgga`→`gpgが`、`jwtto`→`jwtと`、`tddde`→`tっっで`→`tddで`、`nvmmade`→`nvmまで`、`nvmmo`→`nvmも`、`rpmmade`→`rpmまで`、`rpmmo`→`rpmも`、`huihui`/`huihuiha`/`huihuino`、`lo-fi`/`lo-fiwokiku`/`sci-fi`、`pingwoutsu`→`pingをうつ`、`pingpong`→`pingpong`、`ponggo`→`pongご`。NS の 10 件は、BuiltIn の 18 件のうち `gpgga`・`jwtto`・`tddde`・`pingwoutsu`・`huihui` 系 3 件・`lo-fi` 系 2 件・`sci-fi` (NS は `sci-ふぃ` → `sci-fi`)。残る 8 件 (`cwdde`・`pwdde`・`nvmmade`・`nvmmo`・`rpmmade`・`rpmmo`・`pingpong`・`ponggo`) は、**NS では変更前から修正後と同じ結果** (`cwdで` など)。理由 (NS がどの途中の語を英単語と答えて区切りが変わるのか) までは調べていない。
  - **BuiltIn と NS の違い** (個別 179 件でも同じ): BuiltIn だけ変わる 17 件 = `cwdde`・`cwddesu`・`pwdde`・`nvmmo`・`nvmmade`・`rpmmo`・`rpmmade`・`ping`・`ping!`・`pong`・`pong.`・`pingga`・`pongga`・`pingno`・`pingpong`・`pingpongu`・`pings`。NS だけ変わる入力は 0 件。両方で変わるのは `gpgga`・`jwtto`・`tddde`・`tdddesu` (#381 で実機に効くもの)、`pingwo`・`pingde`・`pingto`・`pongwo`・`pongde`・`pingfu` (#320。NS の base は `ぴんがを`/`ぴんがで`/`ぴんと` で入力中の表示 `pingを` と食い違い)、`lo-fi`・`sci-fi` 系、`huihui` 系。
- **トレードオフ**:
  - #381: 区切りが増えるぶん、英字の語 + 日本語の判定は「知らない語の最後の文字 + 助詞」になる。上の約 16 万件の日本語では 0 件だったが、読みが 2 文字未満になる知らない英字の語が実は日本語の「っ」の綴り (`kitta` のような) の一部だったときは区切られる。26,641 件の英字の語では、`slotto`→`slotと`・`plotto`→`plotと`・`presetto`→`presetと` のように、`slot`+`to` / `preset`+`to` と読むほうに寄る (`surotto` ではなく `slotto` と打った人は英語の意図の可能性が高い)。
  - #320: `pingu`・`pinga`・`ponge`・`pinpon` (日本語の読み) は変わらない。BuiltIn だけ `pings` が `ぴんが` → `ぴんgs`、`pingpongu` が `ぴんごんぐ` → `pingぽんぐ`、`pingfu` が `ぴんぎふ` → `pingふ` になる (変更前も日本語として意味のある結果ではない。NS は `pings` を英単語と答えるので Mac 実機では `pings` は変わらない)。
  - #312: ハイフン入りの語を 2 つ足しただけ。`ro-fi`・`lo-ru`・`lo-ka` など `lo-` で始まる日本語の打ち方は変わらない。
  - #384: 同梱の語句は文の中の `しゃーなし` にも効く (`sorehasha-nashidayo` で `西` が出ない)。`しゃなし` のように `ー` が無い読みや、`しゃーなしに` のような続きは別の語として変換エンジンに任せる。
  - #382: `huihui` を英字のままにするぶん、`ふいふい` と打ちたい人は `fuifui` を使う (`fuifui` → `ふいふい` は変わらない)。**`huihui-ai` (モデルの正式名) は直らない** (`huihui-あい`。ハイフンの規則 `HyphenatedWords` に `huihui-ai` を足せば直せるが、本家の PR にも無いので入れていない。必要なら別項目)。`huihuiai` は `ふいふいあい` のまま。
  - #383: `氣` は候補の後ろのほうに出るだけで、先頭候補は変わらない。
- **未検証**:
  - Mac 実機・Swift 側は未確認 (今回は Core と辞書のみ。Swift・`Exports.cs` は触っていない。dylib も作り直していない)。
  - #384: azooKey が実際に `しゃーなし` を `西なし` にするかは実機未確認。テストは偽の変換エンジンで「同梱の語句が先頭候補になる」ことを見ただけ。
  - #383: azooKey が `き` の候補に 氣 を返すか、返す順番も実機未確認 (同梱側は後ろに足す)。
  - NS 判定器での比較は、実機と同じ呼び出し (`checkSpelling(language: "en")`) を 1 回ぶんの Mac で集めた答え。ユーザーが macOS の辞書に学習させた語・OS のバージョン違いでは答えが変わりうる。`--eval-json` の品質は BuiltIn のみ (NS の判定器では未実施)。
  - 比較の入力は自作 (コーパス・readings のローマ字化・辞書の英字語)。実際に人が打つ入力の分布ではない。ローマ字化は 7 語を除く全語を機械的に変換した (ん・っ・拗音の規則は単純なもの)。
  - Windows・Linux の実機。

### P31 査読 (独立した査読員 R2) の結果と対応

- **P0 なし**。BuiltIn と NSSpellChecker の両方で、26 万語から作った入力 (二重字 + 助詞の頭 2,438 語)・readings 25,000 件・誤打 1 文字を入れた入力・笑いの繰り返し 1,135 件などを、変更前と比べた。実在の日本語の語や文を壊した例は見つからなかった。`tools/check-dictionaries.mjs` はエラー 0・警告 0。
- **P1 (テスト不足) → 対応済み**: #381 の「区切る」方向のテストしか無く、代償 (`slotto` → `slotと` のように、同梱の単語表が知らない英語の二重字 + 助詞の頭は、語 + 助詞と読む) を固定するテストが無かった。`Issue375_DoubledLetterBeforeParticle_IsAcceptedTradeOff` を足し、現在の挙動と、日本語の二重字の綴り (`kitto`・`zuttoiku`・`matteite`) が変わらないことを固定した。
- **P2**: 一覧に無い実在の英語で、新しく区切られる語がある (BuiltIn のみ: `stretto`・`stiletto`・`larghetto`・`preforbidden` など。NS でも `mlechchha`)。NS が知らない語はほぼ珍語で、実害は小さいと判断した。実装者が「理由を調べていない」とした 8 件 (`cwdde`・`pwdde`・`nvmmade`・`nvmmo`・`rpmmade`・`rpmmo`・`pingpong`・`ponggo`) は、NS が `cwd`・`pwd`・`nvm`・`rpm`・`ping`・`pong` を英単語と答えるため、変更前から別の経路で区切られていたと確認した。`kyouhacwddeninaru` のように語が打ち始めにないと #381 は効かない (本家と同じ仕様)。誤打した日本語は「ゴミ → 別のゴミ」になる (`qattoude` → `qatとうで`)。
- **未検証**: 実機の azooKey が `しゃーなし` を `西なし` にするか・`き` に `氣` を返す順番。NS の答えはこの Mac 1 台ぶん。Windows・Linux の実機。

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

## 追加項目 P32 (入力中の Ctrl+H を BackSpace にする: 本家 #150・PR #308)

- **現象**: Mac で `aiueo` を打っている途中に Ctrl+H を押すと、`consumed:false`・`commits:[あいうえお]`・`view:null` = 全文を確定して Ctrl+H がアプリに渡る (Ctrl+U なども同じ)。Microsoft IME や ATOK のように、ホームポジションのまま 1 音消すことができない。
- **対応**: 本家 PR #308 (`CompositionController.ControlShortcut` に `'H' => VirtualKeys.Back` を足す) は、Mac 版の Core にその関数が無く hunk が当たらない。Mac 版は Ctrl+文字を F キーなどに置き換える場所が `MeltypeSession.CtrlShortcutToFunctionKey` なので、そこに `0x48 => VirtualKeys.Back` を 1 行足した (`MeltypeSession.cs` 6 行、コメント含む)。
  - 置き換えは「Ctrl だけ・Shift/Option/Command なし・変換ボックスが出ているとき」の既存の条件 (Ctrl+J/K/L/'/;) のまま。置き換えたあとは `control=false` で BackSpace と同じ経路に入るので、変換中・予測候補・固定した文節も BackSpace と同じ処理になる (Back 専用の分岐を新しく書いていない)。
  - 本家の TIP 側 (`native/tip/TextService.cpp`) の変更は Windows 専用なので入れない。
- **挙動**:

| 状態 | Ctrl+H | 確認 |
| --- | --- | --- |
| 入力中 (`aiueo`) | 1 音消す (`あいうえ`)、確定しない | テスト |
| 変換中 (`kyouha` + Space) | 変換をやめてかなに戻る (`きょうは`、Converting=false) | テスト (BackSpace と JSON 一致) |
| 変換中で文節を選択 (`tanniwotoru` + Space + →) | かなに戻る (`たんいをとる`) | 同上 |
| ライブ変換 | BackSpace と同じ | 同上 |
| 予測候補を表示中・Tab で選択中 | 1 音消して予測から抜ける (`きょ`) | 同上 |
| ContinueAfterConversion の固定文節 + 未変換 (`単位を\|取る\|た`) | BackSpace と同じ (未変換の末尾を消す → 固定した文節だけ → 変換前の読みに戻る → 1 音ずつ) | 同上 (3 回押しまで) |
| Shift+Enter で確定した直後 (設定 ON/OFF とも) | 入力が空なので何もせずアプリへ (`consumed:false`) | テスト |
| 入力が空 | `consumed:false` でアプリに渡す (Cocoa の削除・各アプリの機能を邪魔しない) | テスト |
| 英数 (Direct)・アプリ別設定 OFF・Meltype 全体 OFF | アプリへ | テスト |
| Ctrl+Shift+H・Option+H・Ctrl+Option+H・Cmd+H (入力中) | BackSpace にしない (従来どおり確定してアプリへ。Cmd+H は「アプリを隠す」が動く) | テスト |

- **Swift 側 (コードを読んだ範囲)**: `KeyMapping.virtualKey` は kVK_ANSI_H を個別に扱わず、`charactersIgnoringModifiers` の `h` → 0x48 になる (Ctrl+J/K/L と同じ経路)。`InputController.handle` は Ctrl+H を横取りせず、`modifiers |= 2` と `character=0x08` (Ctrl+H の `characters`) で `NativeCore.handleKey` → `meltype_handle_key` → `MeltypeSession.HandleKey` に渡す。`mac/Sources` に Ctrl・`doCommand`・`interpretKeyEvents` を扱う箇所は IME 側に無い。**Swift の変更は不要**。
- **テスト** (`SessionFacadeTests`、478 → 483 件。以下の 475/477 などは、単独で当てたときの件数):
  - `CtrlH_IsBackSpaceWhileComposing` (本家の 2 件を Mac のハーネスに合わせて統合。入力中・変換中・空)
  - `CtrlH_SameAsBackSpace_InEveryComposingState` (本家に無い追加: 10 状態で BackSpace と Ctrl+H の結果の JSON と、そのあとの Enter の結果が一致)
  - `CtrlH_AfterShiftEnterCommit_PassesToApp`・`CtrlH_OtherModifiers_AreNotBackSpace`・`CtrlH_DirectAndDisabledApp_PassToApp` (影響を受けないことの確認)
  - **修正前に赤を確認**: 475/477 (失敗は `CtrlH_IsBackSpaceWhileComposing` の「確定しない」と `CtrlH_SameAsBackSpace…` で、`commits:[あいうえお]`・`consumed:false` になっていた)。ほかの 3 件は修正前から通る (変わらないことの確認)。修正後 477/477。
- **ドキュメント**: `docs/USAGE.md` のキー表に Ctrl+H の行、`mac/README.md` の Ctrl キーの説明に 1 行。Windows 版の説明 (`USAGE.md` の Ctrl+U/I/P/O の行は本家側) とは別に「Mac」と明記した。
- **トレードオフ**:
  - 入力中に Ctrl+H で「履歴・検索の置換」などアプリの Ctrl+H を使えなくなる (空のときは今までどおりアプリへ。入力中に押すと今までは確定してアプリへ渡っていた)。
  - 変換ボックスのヒント (`Space 変換　←→ 文節　…`) には Ctrl+H を載せていない。
- **未検証**: **Mac 実機**での Ctrl+H (TextEdit・ブラウザ・ターミナルなど。IMK が Ctrl+H を `handle` に先に渡すか、Cocoa のキーバインディングが先に処理しないかは実機で未確認 — Ctrl+J/K/L が実機で動くなら同じ経路のはず)。JIS/US 配列での差、Windows・Linux。`swift build`・NativeAOT の publish・build.sh による入れ替えは実行していない (Swift は変更なし)。

### P32 査読 (独立した査読員 R3) の結果と対応

- **P0・P1 なし**。Swift のキー経路 (`KeyMapping`・`InputController.handle`・`NativeCore.handleKey`) を読み、Ctrl+H は `charactersIgnoringModifiers` の `h` から 0x48 になり、Ctrl+J/K/L と同じ経路で Core に届くと確認した (Swift は変更不要)。ガード条件を 1 つずつ外すと、`!shift` はテストが検出するが、`!alt`・`!command`・`IsComposing` は外しても結果が変わらない冗長な条件だった (どれも壊れた挙動ではない)。
- **P2 → 対応**: `HandleKeyCore` のコメントが「表示モードの切り替え」だけを述べていたので、H は BackSpace だと分かるように直した。関数名 `CtrlShortcutToFunctionKey` はそのまま (名前の変更は範囲外)。
- **P2 (記録のみ)**: テスト `CtrlH_OtherModifiers_AreNotBackSpace` の Option+H・Cmd+H は Ctrl が無いので置き換えの条件に入らず、実装を壊しても通る (実質の検証は Ctrl+Shift+H と Ctrl+Option+H)。「Apple の日本語入力と同じ」とは書かない (この Mac の Apple 日本語入力の `KeySetting_Default.plist` に `control+'h'` の定義は無く、Cocoa の削除コマンド経由と見られる)。Microsoft IME・本家 #150 と同じ。
- **実機で確かめる点 (推測のまま)**: `charactersIgnoringModifiers` が Ctrl 押下時に `h` を返すこと、IMK が Ctrl+H を Cocoa のキー割り当てより先に `handle` へ渡すこと。`InputController` のトレース出力 (`handle keyCode=…`) で確かめられる。
- 反映には `mac/build.sh` で NativeAOT の dylib を作り直して入れ直す必要がある (Core の変更なので、`mac/dist` の既存の dylib は古いまま)。

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

## 追加項目 P33 (数字の後ろの かける・わる を × ÷ にする: 本家 PR #321・issue #293)


- **現象**: 本家 (Mozc) では `3かける4` が `3掛ける4`、`10わる2` が `10割る2` になる。Mac 版 (azooKey) の実際の変換 (作業コピーで確認):
  - `10わる2` → `10悪2`、`10わる` → `10悪`、`100わる4` → `100悪4`
  - `3かける`・`1かける` (文の終わり) → `3かける` (ひらがなのまま)
  - `今日は3かける4を` → `今日は3書ける4を`
  - `3かける4`・`12かける3かける4`・`20かける5` は azooKey が最初から `×` にする (変わらない)
- **原因**: 候補辞書 (`candidates.txt`) に `かける × 掛ける …`・`わる ÷ 割る 悪` はあるが、変換エンジンの最初の候補が優先で、数字の後ろという文脈を見ていなかった。azooKey は `3|かける4は12` のように **かける の文節が、後ろの数字と助詞まで取り込む** (`わる2`・`かける4を`)。本家の移植のまま「読みが かける/わる だけ(+数字)の文節」だけを見ると、実際の azooKey の区切りでは当たらない。
- **修正** (`CompositionController.cs`。他の場所は触らない):
  - `ConvertWithEngineOnce`: 文節ごとに `AddMathSymbol` を呼び、返った文字列をその文節の最初の候補にする。
    - 読みが「[数字] + かける|わる|たす|ひく|ぷらす|まいなす|いこーる + [何か]」の文節が対象。数字は同じ文節の頭か、前の文節の最後 (文頭なら変換前の確定済みの文字列の最後)。
    - 次の **両方** のときだけ: (1) 前が数字 (半角・全角・小数の最後)。`5分かける`・`3時間かける`・`電話を3回かける`・`10%わる`・漢数字 (`三かける四`) は前が数字でないので何もしない。数字の前が英字 (`mp3`・`A4`) も何もしない。(2) 後ろが数字で始まる、または文の終わり (後ろに言葉が続く `3かける人`・`5かけるなら`・`3かけると12`・`2わるい`・`10わるの`・`3かけた` は動詞なので動かさない)。
    - かける・わる: 記号 (`×`・`÷`) を最初の候補にする。変換エンジンの文字列のうち動詞の部分 (`掛ける`・`書ける`・`悪`) だけを記号にし、前の数字・後ろの数字と助詞 (`4は12`・`4を`) はそのまま。**覚えた変換 (`ConversionHistory`)・文脈の手がかりより優先する** (前に `鍵を掛ける` で かける → 掛ける を覚えていても、数字の間は ×)。
    - たす・ひく・ぷらす・まいなす・いこーる: 記号 (`+`・`-`・`=`。前の数字が全角なら全角) を **2 番目** の候補に足すだけ (足す・引くは動詞のことも多い)。変換エンジンが最初から記号にしていれば足さない。
  - 学習の除外 (2 行): `Learn` の `ConversionHistory.Remember`、`LearnConversion` の変換エンジンへ渡す文節の列。計算の言葉の文節で記号を含む (`IsMathSymbolChoice`) ものは、覚えず、変換エンジンに渡さない (列を切るので、前後の文節が 1 つの学習にまとまることもない)。
- **学習が漏れる経路の洗い出し** (読んだもの: `Learn`・`LearnConversion`・`RecordSuggestions`・`CommitPrediction`・`Exports.cs` の `CallbackConverter.Learn`・`NativeCore.swift` の `learnCallback`・`Converter.swift` の `learn`・`InputController.swift`):
  - `ConversionHistory.Remember` (`Learn`): 記号を選んだ・そのまま確定した文節を除外した (修正)。
  - 変換エンジンへの学習 (`LearnConversion` → `CallbackConverter.Learn` → `learnCallback` → `MeltypeConverter.learn`): 除外した (修正)。**修正前から漏れうる経路だった**: 学習は選び直したかどうかに関係なく確定した全文節を送る。読みに数字が入る文節 (`かける4`) は既存の規則で送らないが、読みが `かける` だけの文節 (`3|かける:×`) は送っていた (テストで、除外を外すと `かける=×` が `Learn` に渡ることを確認)。Swift 側は、直前に作った azooKey の候補に同じ文字列があるときだけ `updateLearningData` する。Core が足す `×` は azooKey の候補に無ければ Swift 側で捨てられるはずだが、azooKey 自身が `×` を出した文節は学習されうる (実機の学習データでは未確認)。
  - `RecordSuggestions` (辞書登録の提案): 変更なし。`DictionarySuggestions.IsEligible` が、読みがひらがなだけでない・語に記号を含むものを除くので、`かける → ×` は数えられない (テストで確認)。
  - `ConversionHistory.Touch`: 覚えていない組には何もしない。変更なし。
  - `CommitPrediction`・`LearnLanguage`・`TranslationHistory`: 計算の言葉の文節には届かない。変更なし。
  - Swift 側 (`NativeCore.swift`・`Converter.swift`・`InputController.swift`)・`Exports.cs`: **変更しない**。学習の呼び出しは `learnCallback` の 1 箇所だけ (`Bench.swift` を除く)。
- **変更したファイル**: `src/Meltype.Core/Composition/CompositionController.cs` (+73 行 / −2 行)、`src/Meltype.Core.Tests/MathSymbolTests.cs` (新規 295 行。共通ファイルの `Program.cs` などは触っていない)。
- **テスト** (483 → 489 件、すべて通る。`MathSymbolTests`):
  - `MathWordsAfterDigits_BecomeSymbols_OnlyWhereExpected`: 41 例を、変換エンジンの区切りの 3 通り (数字は別の文節 `3|かける|4` / 数字と かける が同じ文節 `3かける|4` / かける が後ろの数字を取り込む `3|かける4は12`。最後が実際の azooKey の区切り) で試す。
  - `MathWordsAfterDigits_WholeExpressionInOneClause`: `3かける4` 全体が 1 文節 (`3かける4`) でも動く。
  - `MathWordsAfterDigits_SurroundingText`: 確定済みの数字 (半角・全角) の後ろ、`mp3` の後ろ、変換する語の後ろに文字が続く場合。
  - `TasuHiku_OfferSymbolAsSecondCandidate`: たす・ひく は 2 番目の候補に足す (3 通りの区切りで)。
  - `MathSymbol_BeatsLearnedHistory_AndIsNotLearned`: 覚えた かける→掛ける より記号。`×`・`+` を選んでも `ConversionHistory` に入らない。`掛ける` を選んだら従来どおり覚える (それでも次の `3かける4` は `×`)。辞書登録の提案にも数えない。
  - `MathSymbol_IsNotSentToTheConverterToLearn`: **変換エンジンの学習に渡る文節 (`ILearningConverter.Learn` の引数) に `× ÷ + - ＋ ＝` が入らない**。`単位を取る` のような対照の文節は届くことを確かめてから調べる。3 通りの区切りで。**学習の除外 2 行を外すと失敗する**ことを確認済み (`かける=×`・`わる=÷`・`たす=+` が渡る)。
  - 修正前のコード (d2f0206 にテストだけ足したもの) では、この 6 件がすべて失敗する (0/6)。
- **前後比較**: 41 例。「偽エンジン」は上の 3 通りの区切りのどれでも結果が同じ (3 通りで食い違う例は 0)。「実 azooKey」は作業コピーで azooKey (d59a28e) の `--serve` を Core につないだ結果 (学習なし・文脈なし。Mac 実機の入力欄ではない)。太字が変わったもの。偽エンジンは かける → 掛ける などと返す設定で、本物の Mozc/azooKey の出力ではない。

| # | 入力 (打鍵) | 偽エンジン 前 | 偽エンジン 後 | 実 azooKey 前 | 実 azooKey 後 |
|---|---|---|---|---|---|
| 1 | 3かける4 (`3kakeru4`) | 3掛ける4 | **3×4** | 3×4 | 3×4 |
| 2 | 10わる2 (`10waru2`) | 10割る2 | **10÷2** | 10悪2 | **10÷2** |
| 3 | 12かける3かける4 (`12kakeru3kakeru4`) | 12掛ける3掛ける4 | **12×3×4** | 12×3×4 | 12×3×4 |
| 4 | 3かける4は12 (`3kakeru4ha12`) | 3掛ける4は12 | **3×4は12** | 3×4は12 | 3×4は12 |
| 5 | 3かける (文の終わり) (`3kakeru`) | 3掛ける | **3×** | 3かける | **3×** |
| 6 | 10わる (文の終わり) (`10waru`) | 10割る | **10÷** | 10悪 | **10÷** |
| 7 | 1かける (文の終わり) (`1kakeru`) | 1掛ける | **1×** | 1かける | **1×** |
| 8 | 1わる (文の終わり) (`1waru`) | 1割る | **1÷** | 1悪 | **1÷** |
| 9 | 20かける5 (`20kakeru5`) | 20掛ける5 | **20×5** | 20×5 | 20×5 |
| 10 | 100わる4 (`100waru4`) | 100割る4 | **100÷4** | 100悪4 | **100÷4** |
| 11 | 今日は3かける4を (`kyouha3kakeru4wo`) | 今日は3掛ける4を | **今日は3×4を** | 今日は3書ける4を | **今日は3×4を** |
| 12 | 3かける2たす1 (`3kakeru2tasu1`) | 3掛ける2足す1 | **3×2足す1** | 3×2TAS1 | 3×2TAS1 |
| 13 | 小数 (対象外) (`3.5kakeru2`) | 3.5掛ける2 | 3.5掛ける2 | 3.5×2 | 3.5×2 |
| 14 | 3かける人 (`3kakeruhito`) | 3掛ける人 | 3掛ける人 | 3書ける人 | 3書ける人 |
| 15 | 5かけるなら (`5kakerunara`) | 5掛けるなら | 5掛けるなら | 5書けるなら | 5書けるなら |
| 16 | 3かけるよ (`3kakeruyo`) | 3掛けるよ | 3掛けるよ | 3書けるよ | 3書けるよ |
| 17 | 3かけると12 (`3kakeruto12`) | 3掛けると12 | 3掛けると12 | 3かけると12 | 3かけると12 |
| 18 | 3かけるけど (`3kakerukedo`) | 3掛けるけど | 3掛けるけど | 3書けるけど | 3書けるけど |
| 19 | 3かけること (`3kakerukoto`) | 3掛けること | 3掛けること | 3かけること | 3かけること |
| 20 | 5かけるには (`5kakeruniha`) | 5掛けるには | 5掛けるには | 5書けるには | 5書けるには |
| 21 | 2われる (`2wareru`) | 2割れる | 2割れる | 2割れる | 2割れる |
| 22 | 100かけた (`100kaketa`) | 100掛けた | 100掛けた | 100かけた | 100かけた |
| 23 | 1かけて (`1kakete`) | 1掛けて | 1掛けて | 1かけて | 1かけて |
| 24 | 2わるい (`2warui`) | 2悪い | 2悪い | 2悪い | 2悪い |
| 25 | 10わるの (`10waruno`) | 10割るの | 10割るの | 10悪の | 10悪の |
| 26 | 100わり (`100wari`) | 100割り | 100割り | 100割 | 100割 |
| 27 | 5分かける (`5hunkakeru`) | 5分かける | 5分かける | 5分かける | 5分かける |
| 28 | 3時間かける (`3jikankakeru`) | 3時間かける | 3時間かける | 3時間書ける | 3時間書ける |
| 29 | 2回かけてみる (`2kaikaketemiru`) | 2回掛けて見る | 2回掛けて見る | 2回かけてみる | 2回かけてみる |
| 30 | 電話を3回かける (`denwawo3kaikakeru`) | 電話を3回かける | 電話を3回かける | 電話を3回書ける | 電話を3回書ける |
| 31 | 本3冊かける (`hon3satsukakeru`) | 本3冊かける | 本3冊かける | 本3冊書ける | 本3冊書ける |
| 32 | 2026年かける (`2026nenkakeru`) | 2026年かける | 2026年かける | 2026年書ける | 2026年書ける |
| 33 | かける (`kakeru`) | 掛ける | 掛ける | かける | かける |
| 34 | わるい (`warui`) | 悪い | 悪い | 悪い | 悪い |
| 35 | 鍵をかける (`kagiwokakeru`) | 鍵をかける | 鍵をかける | 鍵をかける | 鍵をかける |
| 36 | 三かける四 (漢数字) (`sankakeruyon`) | 三掛ける四 | 三掛ける四 | 山書けるよん | 山書けるよん |
| 37 | 3たす4 (`3tasu4`) | 3足す4 | 3足す4 | 3たす4 | 3たす4 |
| 38 | 1たす1は2 (`1tasu1ha2`) | 1足す1は2 | 1足す1は2 | 1たす1は2 | 1たす1は2 |
| 39 | 5ひく3 (`5hiku3`) | 5引く3 | 5引く3 | 5−3 | 5−3 |
| 40 | 3たして4 (`3tashite4`) | 3足して4 | 3足して4 | 3足して4 | 3足して4 |
| 41 | 5ひいて3 (`5hiite3`) | 5引いて3 | 5引いて3 | 5引いて3 | 5引いて3 |

  - 変わったのは、偽エンジン 12 例・実 azooKey 7 例で、すべて「前が数字で、後ろが数字か文の終わり」の かける・わる。**悪化(上の表で、意図しない例が変わったもの)は 0 件**。動詞・時間・量・回数・漢数字・%・小数・mp3 などは、偽エンジン・実 azooKey のどちらも変わらない。
  - たす・ひく: 2 番目の候補に `3+4`・`1+1は2`・`5-3` (数字と同じ文節なら `3+`、後ろを取り込む文節なら `+4`) が出る。最初の候補 (3足す4、実 azooKey では `3たす4`・`5−3`) は変わらない。
  - 実 azooKey で、もともと間違っていて今回も直さないもの: `3かける人`→`3書ける人`、`5かけるなら`→`5書けるなら`、`電話を3回かける`→`電話を3回書ける`、`3時間かける`→`3時間書ける`、`2わるい`・`10わるの`→`悪`、`3かける2たす1`→`3×2TAS1` (これは azooKey の `たす1`→`TAS1`。どれも変換エンジンの癖で、前から同じ)。
- **トレードオフ**:
  - `1かける`・`10わる` のように、数字の直後で入力が終わるものは × ÷ になる (動詞のつもりなら 2 番目以降の候補から選ぶ。選んでも覚えないので、次の同じ入力も × ÷)。数字の直後で動詞の かける・わる が文末に来る例は少ないと見ているが、頻度は測っていない。
  - 覚えた変換より記号を優先するので、数字の後ろで 掛ける・割る を使いたい人は毎回選び直す(覚えない)。
  - **小数 (`3.5わる2`)・百分率 (`10%わる`)・分数 (`1/2かける4`)**: 小数点が入ると `3.5` が別の区間(英数)になり、変換エンジンに渡る日本語は `わる2` だけで、前の数字を見られない。**対象外**。実 azooKey で `3.5わる2` は前も後も `3.5悪2` (`3.5かける2` は azooKey が `×` にするので変わらない)。直すなら `StartConversion` で前の区間の最後を `ConvertJapanese` へ渡す必要がある (ライブ変換の表示にも渡さないとスペースで変換したときと食い違うので、今回は入れていない)。
  - 全角・半角: 前の数字に合わせるのは `+ - =` だけ。`× ÷` は 1 種類。
  - 漢数字 (`三かける四`) は動かさない (`山書けるよん` になるのは azooKey 側の区切り)。
  - 記号を選んでも覚えない (本家と同じ)。azooKey が最初から出す `3×4` (`3|かける4:×4`) は読みに数字を含むので、今までも学習の対象外。末尾の `3|かける:×` のように読みが `かける` だけになる場合は、今まで azooKey の学習に渡っていたのを渡さなくした。
  - 本家の「絵文字の辞書」の調整は、Mac 版に絵文字を後ろに回す機能(`MoveEmojiLast`)も日時の選択(`IsDateTimeChoice`)も無いので不要。
- **未検証**:
  - **Mac 実機・TextEdit などの入力欄・JIS 配列・インストールした azooKey での確認は未実施**。
  - 「実 azooKey」の結果は、学習なし・文脈なしの変換 (`MeltypeConverter.clauses(for:context:nil)`) を Core の `Keyboard` テストハーネス (偽のホスト) につないだもの。実機の文脈 (前に確定した文字列)・学習済みの状態・azooKey のユーザー辞書がある状態では区切りが変わりうる。
  - 長い入力 (50 文字を超える区切り変換) の途中の区切りで、前の区切りの最後の数字を見ない (先頭の文節は変換前の確定済みの文字列を見る)。
  - ユーザー辞書の語の直後・直前に かける・わる が来る入力 (`AlignUserWords` が区切りを直す経路は P30 の領域)。
  - ライブ変換の体感。打っている途中、`3kakeru` の時点では文の終わりなので `3×`、`3kakeruhito` と続けると動詞側に変わるはず (ライブ変換も同じ `ConvertWithEngineOnce` を通るのでこの想定だが、実機では見ていない)。
  - Windows・Linux。

### P33 査読 (独立した査読員 R1) の結果と対応

- **P0・P1 なし**。実 azooKey (`MeltypeIME --serve`、学習なし) で 28 例を流し、悪化は 0 件。`答えは3書ける4だよ` → `答えは3×4だよ`、`100悪3` → `100÷3` が直り、動詞の `3かける人`・`5分かける`・漢数字・`電話をかける` は変わらない。学習除外は `Learn` と `LearnConversion` の 2 行で足り、過剰な除外は見つからなかった。
- **P2 (既知の限界として記録。対応しない)**:
  - 数字と かける が別の区間・別の塊に分かれると効かない、または誤って × になる。`mp3kakeru` → `mp3×`、`A4kakeru2` → `A4×2` (同じ入力内の英字区間の `mp`・`A` が見えない)。50 文字ごとの区切りが `3` と `かける` の間に来ると `3掛ける4`。`(3kakeru4)`・`1,000kakeru2` のように数字が記号に接する入力も効かない。後ろの判定は同じ入力の次の区間を見ず、キャレットの後ろの文字だけを見る。
  - P30 との相互作用: かける の直後にユーザー辞書の語が来る `3kakeruyuuya` (`ゆうや`→悠也) は `3×悠也` になる (ルールどおりなら `3掛ける悠也`)。実害は小さい。
  - `terms-it.txt` の `ぷらす`→`+` は IT 分野を有効にすると文節に割り込むが、P33 の変更とは無関係。
- **実機・インストールした azooKey での確認は未実施**。

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

## 追加項目 P30 (ユーザー辞書の語を、変換エンジンの文節の途中には割り込ませない: 本家 #374・PR #385 の改良導入)

P30 を査読 (R1) で見つかった 2 つの問題に合わせて直した版。P30 の目的 (本家の問題例 `わたしのことがすき` が `綿紫乃事が好き` になるのを防ぐ) は変えず、**判定の範囲を狭め (しきい値)**、**変換エンジンが失敗したときは判定しない** ようにした。

- **表記**: 「実 azooKey」= Mac の `MeltypeIME --serve` (azooKey d59a28e・学習なし) を、Meltype の Core とテスト用キーボード (`CompositionTests.Keyboard`) の下に付けて動かした結果。「偽エンジン」= テスト内の自作の変換エンジン。**Mac 実機の IME (Swift・IMKit・入力欄の文脈・学習あり) は未確認**。Swift は一切触っていない。

### 1. 直した問題

**問題 2 (先に書く。バグ): 変換エンジンが失敗すると、先頭以外の登録語が全部消える**
- 原因: `AlignUserWords` が使う `ConvertWithEngine` は、エンジンの失敗 (`ConvertClauses` が null) を「読み全体が 1 文節で未変換」として返す。文節の頭が位置 0 だけになり、先頭以外のユーザー語が全部「文節の途中」と判定されて落ちた。
- 再現 (しの → 紫乃、失敗するエンジン): `kyoushinogakita` 変更前 `きょう|紫乃|がきた` → P30 `きょうしのがきた`。`watashinokotogasuki` 変更前 `わた|紫乃|ことがすき` → P30 `わたしのことがすき` (査読員の報告どおり)。Mac では `NativeCore.swift` の `clausesCallback` が文節 0 件で nil を返す (Swift のコードを読んで確認。実行はしていない) ので現実に起こる。
- 修正: `ConvertWithEngine` / `ConvertWithEngineOnce` に `out bool failed` の版を足し、エンジンが文節に区切れなかった (`EngineClauses` が null) ときに true にする (50 文字超の区切り変換では、どれか 1 つでも失敗したら true)。`AlignUserWords` は `failed` なら **何も変えずに元の語の割り当て (`pieces`) を返す** (本家の `engine is null` なら元に戻す、と同じ)。失敗は `EngineClauses` が覚えない (既存の仕組み) ので、次のキーでエンジンが戻れば判定を使う。
- エンジンが「失敗」する形ごとの確認 (しの → 紫乃、`kyoushinogakita` / `watashinokotogasuki`、**偽エンジン**。しきい値は 変更前相当 / 3 / 現 P30 の 3 通りを流した):

| エンジンの返し方 | 変更前 | P30b (今回) | 備考 |
|---|---|---|---|
| `ConvertClauses` が null・`Convert` も null (Mac の「空 → nil」・Core 側のコールバック未設定 `s_clauses == null` も同じ経路) | `きょう\|紫乃\|がきた` / `わた\|紫乃\|ことがすき` | **同じ** | 修正前の P30 は `きょうしのがきた` に落ちる。**赤→緑のテストあり** |
| `ConvertClauses` が null・`Convert` は文字列を返す | `変換:きょう\|紫乃\|…` | **同じ** | 赤→緑 (上と同じテスト) |
| `ConvertClauses` が空のリスト `[]` | `紫乃` だけ (登録語以外の文字が消える。P30 以前からの挙動) | 同じ | P30 でも P30b でも悪化なし (読みがつながらないので `AlignUserWords` が元に戻す)。テストは「登録語が落ちない」ことだけを見る (消える文字は P30 以前の挙動で、このパッチの範囲外) |
| `ConvertClauses` が例外を投げる | 例外が呼び出し元へ伝わる | 同じ | Core は例外を捕まえない設計 (`MozcConverter`・Mac のコールバックは例外を投げない前提)。`AlignUserWords` が例外の経路を新しく作るのは、**エンジンが例外を投げる + 登録語が全部の読みを覆う (今までエンジンを呼ばない) 読み**だけ。単体テストにはしていない |

**問題 1: 実 azooKey では、助詞の後ろの登録語が使われなくなる範囲が広い**
- 実 azooKey は未知語を助詞とくっつけて区切りがち (`きょう|はめる|たいぷ|をつかう`・`おとうと|のさくらです`・`きょうはゆうや|がくる`)。「語の始まりが文節の頭か」だけで判定する P30 (本家 PR) では、助詞の後ろに置いた登録語が落ちる。
- 一方、本家の問題例は実 azooKey でも `わたし|のことがすき` (しの が文節をまたぐ) と区切られるので、「またがる語だけ使う」案では直らない (本家のノートと同じ結論を、実 azooKey の区切りで確かめた)。
- 対応: 査読員の試案 (読みが一定の長さ以上なら、途中から始まっても使う) を、**しきい値付きで採用**した。しきい値は `CompositionController.InsideClauseMinReadingLength` (定数、**3**)。読みが 3 文字以上のユーザーの語は文節の途中から始まっても使う。2 文字以下の語だけ、従来の P30 の判定 (文節の頭から始まるときだけ使う) を残す。

### 2. 修正の中身 (`CompositionController.AlignUserWords`)

- 「文節の頭を調べるための全体の変換」を始める条件: 読みの先頭以外から始まる、**しきい値より短い**ユーザーの語がある (従来は、長さを問わない)。
- 使う条件 `keep`: ユーザーの語でない (同梱の語句・同梱の専門用語集) / 読みが **しきい値以上** / 文節の頭から始まる。
- 失敗 (`failed`) または読みがつながらない結果なら、判定せずに `pieces` を返す。
- `ConvertJapanese` / `UserDictionary` のコメントを新しい挙動に合わせた。

### 3. 比べ方 (実 azooKey)

- 同じ Core のビルドを、しきい値を切り替えて流した (評価用に `InsideClauseMinReadingLength` を変数にしたコピーで流した。製品のコードは定数。評価用の差分は `impl/P30b/tools/eval-knobs.diff`、実行側は `impl/P30b/tools/P30bProbe.cs.txt`、文・結果・集計スクリプトも同じ `tools/`)。しきい値 1 = 「変更前」(`AlignUserWords` が何もしない = d2f0206 の挙動。査読員の `R1base` の結果と 28 文すべて文字単位で一致を確認)、しきい値 99 = 「現在の P30」(査読員の `R1` の結果と 28 文すべて一致。エンジンの失敗は実エンジンでは 0 回だったので、失敗の修正の有無は影響しない)。
- 評価の文 (すべて「読みを打つ → Space で変換 → Enter」とライブ変換の 1 キーずつ):
  - **意図した登録語** 139 文 = 査読員の 28 文 + 自作 111 文 (日本人の名前・地名・作品名・かな語・英字ブランド名など。読みの長さ 2/3/4/5 文字以上を 20 文ずつ + 追加 31 文)。うち 40 文は、登録しなくても実 azooKey 単独で同じ語が出るので比較に使えず除外。**比較に使えたのは 99 文** (2 文字 25・3 文字 29・4 文字 22・5 文字以上 23)。
  - **意図していない登録語 (誤爆の見張り)** 37 文 = 自作の普通の文 136 文 (日常・仕事の短い文をひらがなで書いたもの) に、名前・語 171 個 (読み 2 文字 33・3 文字 75・4 文字 37・5 文字以上 26) を 1 つずつ登録して、「実 azooKey の区切りで文節の途中に偶然現れた」場所を機械的に拾った 28 件 (2 文字 21・3 文字 7) + 手で足した 10 件 (本家の問題例 `わたしのことがすき` の形を含む)。重複 1 件を除いて 37 件。**拾い方が「当たりだけを集める」ので、誤爆の率ではなく件数として見る**。
  - 136 文 × 171 語の機械的な走査の数字 (率の目安): 2 文字の語 33 個 → 文節の途中に出たのが 21 件 (うち文節をまたぐ 4)。3 文字の語 75 個 → 途中に出たのは **7 件で、語は 2 つだけ** (`りょう` が しりょう・りょうり に 5 件、`れいな` が きれいな に 2 件)。4 文字の語 37 個 → 途中は 0 件 (頭は 6 件)。5 文字以上 26 個 → 0 件。
- 判定: 「語が使われた」= 確定した文字列に登録した語が入っている。誤爆は、意図しない語 (印を付けた 〔読み〕) が出力に入ったこと。

### 4. 結果の表 (実 azooKey。数字はすべて実測)

**4-1. 悪化: 変更前は登録語が使われたのに、その列では使われなくなった文の数** (意図した登録語・比較できた 99 文)

| 読みの長さ | 件数 | しきい値2 | **しきい値3** | しきい値4 | しきい値5 | 現在の P30 (∞) |
|---|---|---|---|---|---|---|
| 2 文字 | 25 | 0 | 10 | 10 | 10 | 10 |
| 3 文字 | 29 | 0 | 0 | 9 | 9 | 9 |
| 4 文字 | 22 | 0 | 0 | 0 | 1 | 1 |
| 5 文字以上 | 23 | 0 | 0 | 0 | 0 | 5 |
| **計** | 99 | **0** | **10** | **19** | **20** | **25** |
| うち 査読員の 28 文 (比較できた 19) | 19 | 0 | 3 | 6 | 6 | 9 |

- 査読員の報告は「変更前は使えて変更後は使えなくなったのが 8 文」だったが、再計算では 9 文 (めるたいぷ ×2・しの ×3・さくら・ゆうや・なんば・あんさんぶるすたーず)。しきい値3 で戻らないのは **しの の 3 文 (2 文字)** だけ。
- 査読員の 28 文を、変更前 / 現在の P30 / しきい値3 で並べたもの: `impl/P30b/tools/r1table.md`。例: `きょうはめるたいぷをつかう` は 変更前 `今日は|Meltype|を使う` → 現 P30 `今日|ハメる|タイプ|を使う` → しきい値3 `今日は|Meltype|を使う`。`おとうとのさくらです` は `弟の|桜良|です` → `弟|の桜です` → `弟の|桜良|です`。

**4-2. 誤爆: 意図していない場所で登録語が使われた件数** (意図しない語の 37 文。使われたら悪)

| 読みの長さ | 件数 | 変更前 | しきい値2 | **しきい値3** | しきい値4 | しきい値5 | 現在の P30 |
|---|---|---|---|---|---|---|---|
| 2 文字 | 23 | 23 | 23 | 0 | 0 | 0 | 0 |
| 3 文字 | 12 | 12 | 12 | 12 | 2 | 2 | 2 |
| 4 文字 | 2 | 2 | 2 | 2 | 2 | 1 | 1 |
| **計** | 37 | 37 | 37 | **14** | 4 | 3 | 3 |

- 本家の問題例 (`わたしのことがすき` に しの → 紫乃): 変更前 `綿|紫乃|ことが…`、**しきい値3 以上は `私|のことが好き`** (しきい値2 は直らない)。2 文字の語の偶然の混入 (23 件) は、しきい値3 で全部なくなる。
- 3 文字の 12 件は **語は 7 種類** (`りょう` ×5: しりょう・りょうり・ざいりょう …、`れいな` ×2: きれいな、`たなか`: あした|なかなか、`さとう`: おさとう、`ちよい`: ここちよい、`かおり`・`あすか` の 2 件は元から文節の頭で、現 P30 でも出る)。これらは**変更前 (d2f0206) でも起きていた**ことで、P30b は悪くしていない。しきい値3 は 3 文字の語についての P30 の改善を捨てる。
- 4 文字の 2 件: `ほしぞら` は文節の頭 (どの列でも出る)、`こうえん` (さい|こうえん|ぜつ) はしきい値4 以下で出る。4 文字以上で実 azooKey の途中に偶然入った例は、136 文の走査では **0 件**。

**4-3. 本家の問題例** (必須条件): しきい値3 以上で直る。実 azooKey の区切り (`わたし|のことがすき`) を記録した単体テスト `ShortUserWord_InsideAnEngineClause_IsNotUsed_WithRecordedAzooKey` で固定した。

### 5. 決めたこと: しきい値 = 3

優先順位は **(1) 本家の問題例を直す (必須) → (2) 変更前に使えていた登録語を落とさない → (3) 偶然の誤爆を減らす**。

- (1) を満たす最小のしきい値は 3 (2 では直らない)。(2) は、しきい値が小さいほど良い。(3) の「誤爆」は、P30 以前から起きていたことで、P30 が減らしていたぶんを戻すだけ。(2) の「悪化」は、**すでに使えていた機能が壊れる後退**なので、(3) より重く見る。よって (1) を満たす最小の **3**。
- 3 を選んだ側の損: 3 文字の語 (`りょう`・`れいな`・`たなか`・`さとう` のような、ほかの語の一部になりやすい名前) で、P30 が減らしていた偶然の混入が元に戻る。3 文字の名前は登録語の主な使い道なので、登録した人は `りょう` → 涼 で 資料 → 子涼 のような誤変換を、変更前と同じ頻度で見る。2 文字の語は、助詞の後ろでも落ちる (4-1 の 10 文。`ひな` → 陽菜 の `今日はひなと…` など)。
- 4 を選んだ側 (採らなかった): 3 文字の混入が減る (12 → 2 件)。損は、3 文字の登録語 (さくら・ひなた・ゆうき・えみり …) の 31% (29 文中 9 文) が助詞の後ろで落ちること。3 文字の名前は登録語の主な使い道なので、落ちる頻度のほうが高いと見た。目安: 136 文 × 3 文字の語 75 個の走査で、偶然の混入は 7 件 (語 2 種類)。一方、意図した 3 文字の語は 29 文中 9 文で落ちる。登録語を打つ文の割合や登録語の数で結論は動く (多数の 3 文字語を登録し、登録語をあまり打たない人は 4 のほうが良い)。**同梱の語句 (`phrases.txt`) の説明には「短い読みはほかの語の中にも現れて巻き込むので 4 文字以上だけを入れる」とあり、Meltype 自身の経験は 4 を支持する**。ただしそれは同梱語 (全員に効く) の話で、ユーザーが自分で登録した語は「意図して打つ」前提が強い。
- 5 以上: 悪化が増えるだけ (20 文) で、誤爆はほとんど減らない (4 → 3)。
- 2 に当たる「変更前」は、本家の問題例が直らない。
- **どちらも完璧ではない**: 2 文字の登録語 (名前の `しの`・`ひな` など) は、助詞の後ろでも落ち、使うには Shift+←→ で区切りを変える。3 文字の語は偶然の混入を許す。

### 6. 検討したが採らなかった案: 「直前が助詞のときは短い語も使う」

2 文字の語が落ちる文は、ほとんどが「助詞 + 登録語」の形 (`今日は|ひな|と`・`彼は|まや|の`)。本家の問題例 (`わた|しの`) は直前が助詞ではない。そこで「しきい値より短い語でも、直前の文字が `はがをにでともへのや` なら使う」を同じ文で試した (評価用のコピーだけ。`tools/eval-knobs.diff`)。

| 案 | 悪化 (99 文) | 誤爆 (37 文) | 呼び出し (ライブ 176 文) |
|---|---|---|---|
| しきい値3 (採用) | 10 | 14 (3 文字 12・4 文字 2) | 3721 |
| しきい値3 + 助詞 | 1 | 19 | 3577 |
| しきい値4 + 助詞 | 1 | 9 (2 文字 5・3 文字 2・4 文字 2) | 3607 |

- 悪化が 10 → 1 に減り、数字だけ見れば「しきい値4 + 助詞」が悪化・誤爆とも少ない。
- 採らなかった理由: (a) 評価の文の多くは「助詞 + 登録語」の形を狙って作ったので、この案に有利な偏りがある。(b) 実 azooKey で、P30 が直していた `ことしのなつ` (こと|しの) が、`と` が助詞に見えて `事|紫乃|夏` に戻る。ほか、`かい` ×2・`ことしの` ×2・`ちあ` の 2 文字の偶然の混入が 5 件増える。(c) 直前の 1 文字だけを見る単純な判定で、独立の査読を受けていない。(d) 依頼の範囲は「しきい値の比較」だった。**次の候補として、P30c で査読にかける価値はある**とだけ書く。

### 7. 性能: 変換エンジンの呼び出し回数 (実 azooKey。呼び出しは Core からエンジンへの `ConvertClauses` の回数)

対象は 4-1・4-2 の文のすべて (意図した 139 文 (除外した 40 文も含む) + 意図しない 37 文 = 176 件、キー数 4311)。

| 列 | Space 変換の合計 | ライブ変換の合計 (1 キーずつ打つ) | 1 キーで最大 |
|---|---|---|---|
| 変更前 | 981 | 3535 | 5 |
| しきい値2 | 981 | 3535 | 5 |
| **しきい値3** | **934** | **3721 (+5%)** | 7 |
| しきい値4 | 930 | 3920 (+11%) | 7 |
| しきい値5 | 979 | 4174 (+18%) | 7 |
| 現在の P30 | 1007 | 4369 (+24%) | 7 |

- 比較できた意図した 99 文だけでは、ライブ変換が変更前 1807 → しきい値3 1941 (+7%) → 現 P30 2346 (+30%)。Space は 522 → 521 → 578。
- **追加の呼び出しは、先頭以外から始まる「しきい値より短い」ユーザーの語があるときだけ**: ① 登録語の読みがしきい値以上の文では、すべてのしきい値で呼び出し回数が変更前と**完全に同じ** (176 件・全列で例外 0 件)。② 短い語でも、語が読みの先頭にある文は、呼び出し回数が変更前と同じ (しきい値3: 1 件・しきい値4: 3 件・現 P30: 7 件、すべて同じ)。③ 短い語が途中にある文だけ変わる (しきい値3: 51 件のうち 23 件増・28 件減。全体変換 1 回で済むぶん、語ごとの変換が減って Space は減ることがある)。
- 単体テストでも確認 (偽エンジン): 先頭の短い語・同梱の語句・同梱の専門用語集の強制語は、全体を変換する呼び出しが無い (`ShortUserWordAtTheHead_…`・`BuiltInPhrases_…` に呼び出しの確認を足した)。長い語だけの文も、全体を変換しない (`LongUserWord_…`)。辞書が空・辞書の語が入らない読みは従来どおり (`NoExtraEngineCalls_WhenNoUserWordIsInsideTheReading` が維持)。
- **未解明**: 1 キーの最大が 5 → 7 になる理由。同じ読みの文脈付き・文脈なしの 2 回の変換 (`ConvertWithContext`) と、使う語で区切った前後の変換が重なるためと考えられるが、内訳は調べていない。**実機のエンジン 1 回あたりの時間は未計測**なので、体感への影響は不明。

### 8. 変更したファイル

- `src/Meltype.Core/Composition/CompositionController.cs` — `InsideClauseMinReadingLength` (定数 3) を新設、`AlignUserWords` の判定にしきい値と `failed` の戻しを追加、`ConvertWithEngine` / `ConvertWithEngineOnce` に `out bool failed` の版を追加 (旧シグネチャは残した)。
- `src/Meltype.Core/Composition/UserDictionary.cs` — クラスのコメントだけ。
- `src/Meltype.Core.Tests/UserWordAlignmentTests.cs` — テスト 8 件を追加、既存の `BuiltInPhrases_…` に呼び出しの確認を追加。

### 9. テスト

- 489 → **505 件、すべて通る** (最初の版で +7、査読後の改訂で +8、再査読後に +1)。
- 追加した 8 件のうち、**修正前のコードで赤になったのは 5 件** (修正前の `CompositionController.cs` に定数だけ足して流した): `FailingEngine_KeepsEveryUserWord_…` (null と Convert のみの 2 種類・ライブ変換)・`FailingEngine_InALongReading_…` (50 文字超)・`FailedEngineResult_IsNotRemembered_…` (失敗中は登録語を使い、エンジンが戻れば判定を使う)・`LongUserWord_AfterAParticle_…` (記録した実 azooKey の区切りで `今日は|Meltype|を使う`・`弟の|桜良|です`)・`MinReadingLength_IsTheBoundaryBetweenUsedAndNotUsed` (しきい値ちょうどの長さは使い、1 文字短い語は使わない)。残り 3 件 (`EmptyEngineResult_…`・`ShortUserWord_…_WithRecordedAzooKey`・`ShortUserWordAtTheHead_…`) は修正前でも通る「変わらないものの見張り」。
- 「記録した実 azooKey」= `RecordedAzooConverter` に、実 azooKey が返した文節を数文ぶん写して入れたもの (記録に無い読みは null)。実エンジンを起動するテストではない (CI で動かせないので)。

### 10. 実エンジンで確認したこと / 偽エンジンだけで確認したこと / 未確認

**実 azooKey (MeltypeIME --serve) で確認した** (Mac 上のコマンドライン、Core + テスト用キーボード):
- 4-1・4-2・7 の表のすべて (意図した登録語 139 文・意図しない 37 文・呼び出し回数)。
- 査読員の 28 文で、変更前 (R1base)・現 P30 (R1) と同じ結果が出る (56 件が文字単位で一致)。
- 本家の問題例 (`わたしのことがすき` + しの) が、しきい値3 で直ること。しきい値3 で、査読員が悪化と報告した 9 文のうち 6 文 (めるたいぷ ×2・さくら・ゆうや・なんば・あんさんぶるすたーず) が戻ること。
- エンジンの失敗は、この評価の間は 0 回 (失敗の修正は、実エンジンでは試していない)。

**偽エンジンだけで確認した**:
- 問題 2 (エンジンが null / 空 / `Convert` のみを返す) の修正とその回帰テスト、失敗してから戻ったとき、50 文字超の読み。例外を投げるエンジンの挙動 (変更前と同じ。評価用プローブで確認、単体テストなし)。
- 呼び出し回数の「先頭の短い語・同梱語・辞書が空は増えない」の単体テスト (偽エンジン)。

**未確認**:
- **Mac 実機の IME** (Swift・IMKit・実際の入力欄)。実機は入力欄の前の文字を文脈としてエンジンに渡すので、区切りが評価と違う可能性がある (評価では文脈なし)。azooKey の学習も無効だった。
- Swift の `NativeCore.swift` は読んだだけ (azooKey が空を返すと nil になる、まで)。実行していない。Windows (Microsoft IME)・Linux (Mozc) の区切り。エンジン 1 回あたりの時間と体感。
- 評価の文は、私 (1 人) が書いた 111 文 + 136 文と、査読員の 28 文。作品名・名前の読みの偏りがある。誤爆の見張りは、偶然に現れた場所を集めた件数であり、実際の利用者の誤爆の率ではない。3 文字の語の誤爆の見張りは実質 5 種類の語。
- しきい値の選択 (3 か 4) は、登録語の使い方 (3 文字の語をどれだけ登録するか、登録語をどれだけ打つか) で変わる。利用者のデータは見ていない。

### 11. 査読の結果と対応

- **1 回目の査読 (R1、実 azooKey)**: P1 が 2 件。(1) 登録語が助詞の後ろで落ちる範囲が広い (28 文のうち 8〜9 文が悪化) → しきい値 3 に直した (上の 1〜5)。(2) 変換エンジンが失敗すると先頭以外の登録語が消える → `failed` の戻しを足した (上の 1)。ほかに P2: 「単語を登録」の説明・`docs/USAGE.md`・`docs/DICTIONARY.md` の「最優先」が不正確 → 2 文字以下の語の例外を書き足した (`UserDictionaryViewController.swift` の説明文は 1 文足した)。
- **2 回目の査読 (R4、実 azooKey、自作の 160 文)**: P0 なし。しきい値 3 の悪化は 93 件中 6 件でどれも 2 文字の語、3 文字以上は 0 件。元の P30 (33 件) から 27 件回復し、誤爆の見張りは変更前と同じか減った。失敗時の戻しは変更前と出力が完全一致。P33 との組み合わせ (12 ケース) も差は「2 文字の語を落とす」ことだけ。
  - **P1 (テスト不足) → 対応済み**: 長さの判定を外しても全テストが通っていた。短い語と長い語が同じ文節の途中にある形のテスト `LongAndShortUserWords_InTheSameClause_OnlyTheLongOneIsUsed` を足し、判定を外す・`>=` を `>` にする変異のどちらでも赤になることを確かめた。
  - **P2 (記録のみ)**: 失敗はキャッシュされないので、エンジンの失敗が続くと全体の変換を毎回やり直す (+50〜70%)。Mac では失敗は即 nil で返るので害は小さい。1 キー最大 5→7 回の理由は、`AlignUserWords` の読み全体の変換 (読みが 1 文字ずつ伸びるのでキャッシュに当たらない) と、その結果の各文節について `JapaneseCandidates` が呼ぶ単独の `Convert` で、文節が多いほど増える (上限が 7 とは限らない)。長い語を使い短い語を落とすと、残りの読みの変換が崩れる例がある (`そのぷるりくえすとはひなのだ` → `その|PR|ハヒなのだ`。変更前も同程度に汚かった)。3 文字でも `しょう`・`りょう`・`じゅん` は実質 2 拍で、語の途中に出やすい (変更前から同じで回帰ではない)。
- **採らなかった案と、その理由**: 「直前が助詞なら短い語も使う」(悪化は 0 に近づくが、`ことしのなつ`・`はなび`・`ともだち`・`とけい`・`このはなし`・`もともと` で変更前の誤爆が戻る)。しきい値 4 (3 文字の登録語の 39% が助詞の後ろで落ちる)。
- **未実装**: 本家は語を使わなかったときにログを残すが、Mac 版は残していない。「辞書が効かない」という報告の原因を追いにくいので、必要になったら足す。
- **未検証**: Mac 実機の IME (入力欄の前の文字を文脈としてエンジンに渡すと区切りが変わる可能性・学習あり)。実機の遅延 (azooKey 単体では 1 回 50〜70 ms)。Windows・Linux。

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

### P30〜P33 の Mac 実機の確認 (2026-10-11、メンテナー、TextEdit、ビルド 21 = 1.1.5)

- 期待どおりに動いたもの: P33 `3×4`・`10÷2` (`電話を3回かける` は変わらない)、P31 `pingをうつ`・`lo-fi`・`gpgが`・候補の `氣`、P30 `しの→紫乃` を登録して `私のことが好き` (紫乃が割り込まない)・`めるたいぷ→Meltype` を登録して `今日はMeltypeを使う`、P32 Ctrl+H (問題なし)。これで P32 の「Ctrl+H が IMK から Core に届く」という未確認の前提も確かめられた。
- 確認していないもの: P31 の `しゃーなし` が先頭候補か・`huihui`・`cwdde`、P30 のライブ変換の遅延、P33 で選んだ `×` が学習に混ざらないか、P32 の変換中 (Space の後) と何も打っていないときの Ctrl+H。


## 追加項目 Q1 (語末に孤立して残った z / q が Enter で消える: 前が 1 音だけのとき、打ち間違いの直しで消さない)

ベースは HEAD 2a5c765。Mac 実機 (インストールした IME)・Swift・Windows/Linux は未確認。

- **現象**: 子音 + 母音 + `z` / `q` で終わる入力を Enter で確定すると、末尾の `z` / `q` が消える。`baz` → `ば`、`kaz` → `か`、`saz` → `さ`、`baq` → `ば`、メモのファイル名 `foo_bar_baz` → `foo_bar_ba`。入力中の表示 (`showing`) は `ばz`・`foo_bar_baz` で正しく、Enter の結果だけが違う。Space で変換を始めた読みも同じく `ば` になる (`foo_bar_baz` + Space は `foo_bar_ba `)。`bak`・`bab`・`bax`・`bal`・`baw`・`bah` は残る。
  - 既存の挙動で、P25〜P29 の回帰ではない。v1.1.4 (00c9a7a) を自分でビルドして `baz`・`baq` が `ば` になることを確認した (`RomajiTypoCorrector.cs` は Mac 版の最初のコミット c3c247f から変わっていない)。
  - **z と q だけの問題ではない** (下の表)。
- **原因**: `CompositionController` の Enter / Space / 記号の処理が先に `CompositionText.FixTypos()` を呼び、その中の `RomajiTypoCorrector.Fix()` が、末尾に残った 1 文字を「打ち間違いの余計な 1 文字」として消している。実行して確かめたもの (`Fix` に標準エラー出力を足した使い捨てのビルド): `baz` → `[Fix] letters=baz final=True first=2 -> Wrong=baz Right=ba Kana=ば`、`baq` → `Wrong=baq Right=ba`、`bak` → `null`。
  - 該当の行 (`src/Meltype.Core/Composition/RomajiTypoCorrector.cs`、修正前):
    - `Fix()` の「語末に子音が 1 つ残っただけ (yoroshikuy) なら、隣のキーを一緒に押したときだけ消す」(`first == letters.Length - 1 && AnalyzeFragment(letters).IsValid` の枝)。`Neighbors` (QWERTY の隣のキー) で、`z` は `asx`、`q` は `wa` なので、`a` の後ろの `z`・`q` は「隣のキーを一緒に押した」と数えられ、`ば` が付属語 (`FunctionWords` に `ば` がある) として `Segment(...).Known` になるため消される。`bak` は `k` の隣 (`iojlm`) に `a` が無いので消えない。
    - `q` は `q` 単独ではローマ字として読めない (`AnalyzeFragment("baq")` は無効) ので上の枝に入らず、後ろの候補探索 (`Consider`: 余計な 1 文字 / 隣のキーに置き換え) で `ba` (削除、費用 0.5 + `ば` 0.8) が選ばれる。`biq` → `びあ` のように `q` が `a` に置き換わる枝もある。
  - 呼び出し側 (`CompositionText.FixTypos`): 「今の表示で英字に見えている部分」「5 文字以上の英単語を含む部分」は直さないが、`baz`・`foo_bar_baz` の `baz` は英単語として数えられない (BuiltIn・NS とも) ので直しの対象になる。
  - `ConvertLenient` (表示用の変換) は関係しない: `Display(final: true)` は `baz` で `ばz` を返す (使い捨てのデバッグ出力で確認)。P26 (`z` + `h/j/k/l` の矢印) も関係しない: 矢印の処理は `CompositionText.Append` の先頭 (`_pending` が `z` 1 文字・単位がすべて矢印のとき) だけで、`FixTypos` は矢印の単位を飛ばすだけ。
- **孤立した末尾の文字の表 (修正前)**: 子音 `b c d f g h j k l m p q r s t v w x y z` を、語頭 / 母音の後 (5 通り) / 子音 + 母音の後 (30 通り) / `desu` の後 / `foo_bar_ba` の後に付けて (計 988 入力、BuiltIn と NS の両方) Enter で確定した。母音と `n` は対象外。

  | 文字 | 修正前に Enter で末尾が落ちた入力 (760 入力中) | 例 |
  | --- | --- | --- |
  | q | 15 | `baq`・`kaq`・`taq`・`haq` → 末尾が消える、`aq`・`biq`・`keq` → `ああ`・`びあ`・`けあ` (q が a に置き換わる) |
  | s | 8 | `bas`・`kas`・`sas`・`tas`・`tes`・`ses`・`hes`、`foo_bar_bas` |
  | z | 6 | `baz`・`kaz`・`saz`・`taz`・`haz`、`foo_bar_baz` |
  | j | 4 | `ij`・`uj`・`sij`、`desuj` |
  | k | 4 | `ik`・`sik`・`tok`・`mok` |
  | h / l / r / y | 各 2 | `uh`・`desuh`、`tol`・`mol`、`ser`・`ter`、`uy`・`desuy` |
  | d | 1 | `hed` |
  | b c f g m p t v w x | 0 | |

  入力中の表示 (`showing`) が落ちた入力は、この 988 入力では 0 件 (BuiltIn・NS とも。英単語と答えられた語 (BuiltIn の `hes`、NS の `uh`・`mol`・`eq`) は表示も英字になるが、末尾は残る)。落ちるのは Enter (と Space・記号の直前の `FixTypos`) だけ。語頭 (`z`・`q` 単独や子音 1 文字 + 末尾) は `first <= 0` で直さない。
  - 規則は「隣のキー」の対応そのもの: 母音の後ろでこの子音が隣のキーになる組 (`a` の隣の `q w s z`、`i` の隣の `u o j k`、`u` の隣の `y i h j`、`o` の隣の `i p k l`、`e` の隣の `w r s d`)。`jazz`・`quiz`・`pizza`・`buzz`・`fizz` などの英単語は英語と判定されるので直しの対象にならない。
- **修正** (`RomajiTypoCorrector.Fix` の先頭、`first <= 0` の次に 5 行): 読めない英字が最後に 1 つ残っただけ (`first == letters.Length - 1`) で、その前が 1 音以下 (`AnalyzeFragment(letters[..first])` の `Tokens.Count` + 入力途中の子音 `n` を 1 音と数えて 2 未満) なら、打ち間違いとは決めず直さない (`null`)。
  - 理由: 1 音 (`ば`・`か`・`さ`) は付属語としても数えられるので、直しの根拠にならない。略語・変数名・ファイル名の末尾を消していた。2 音以上 (`yoroshikuy` の `よろしく`、`desuh` の `です`、`kanb` の `かん`) の後ろの余計な 1 文字は、今までどおり直す。
  - z・q だけを除く案は採らなかった: 原因は文字によらず同じで、`bas`・`tes`・`ik`・`tok` など他の子音も同じ形で落ちていた。逆に z・q だけを除くと、他の子音が同じ形で落ち続けるまま残る。「z / q を他の子音と同じように残す」は、全部の子音で同じ規則にしたほうが満たしやすい。z・q だけに絞る場合は、`Fix` の同じ場所に `extra is 'z' or 'q'` を足すだけ (試していない)。
  - P26 の矢印・`zu` → `zj` の打ち間違いの直し (`mizjwonomu` → `みずをのむ`、`hazkimeru` → `はじめる`、`mazjnoke`、`kozlsite`) は、読めない文字が末尾ではない (または前が 1 音でない) ので変わらない。テストで確認済み。
- **変更したファイル**:
  - `src/Meltype.Core/Composition/RomajiTypoCorrector.cs` (+5 行)
  - `src/Meltype.Core.Tests/FeedbackTests.cs` (+54 行、テスト 2 件)
- **テスト**: 505 → 508 件、すべて通る (査読後に、Space・記号の直前と既知の限界を固定するテストを 1 件足した)。`--eval-json` は 1242/1244 のまま、例ごとの結果も変更前と同じ (外れは `[英語] my name is taro`・`[混在] apinoerror` の 2 件)。
  - `RomajiTypoTests.RomajiTypos_LoneKanaThenStrayConsonant_KeepsTheConsonant`: `baz`→`ばz`・`kaz`・`saz`・`taz`・`haz`・`baq`・`kaq`・`bas`・`kas`・`aq`→`あq`・`foo_bar_baz`・`foo_bar_baq`・`kanz`→`かんz`。打っている間の表示と Enter の結果が同じことも見る。
  - `RomajiTypoTests.RomajiTypos_LoneKanaThenStrayConsonant_DoesNotTouchEnglishOrOtherFixes`: `jazz`・`quiz`・`pizza`・`buzz`・`fizz`・`qr`・`xz`・`zzz`・`qa`・`kazu`・`bax`・`bak`・`bab` が変わらない。2 音以上の直し (`yorosikuy`・`desuh`・`desuy`・`kanb`・`sanh`・`arigatpu`・`onegaishimsu`・`bakz`・`hazkimeru`・`mazjnoke`・`kozlsite`) が今までどおり。打ち間違いの直しを切ると `baz` が `ばz` のまま (原因が直しにあることの固定)。
  - 赤→緑: 修正前に 1 件目が `baz 期待値: ばz / 実際: ば` で赤、修正後に緑 (2 件目は守りのテストで、修正前も緑)。
  - 変異テスト (使い捨てのコピーで 1 つずつ。元に戻して `cmp` で確認): 閾値 `< 2` → `< 1` で 1 件目が赤、`< 3` で `desuh` が赤、`first == letters.Length - 1 &&` を外すと 3 件 (`mizjwonomu`・`shitmeasu`・`bakz`) が赤、規則ごと外すと 1 件目が赤、入力途中の `n` を数えない変更は最初のテストでは見逃されたので `kanb`・`sanh` を足して赤になることを確認した。
- **前後比較** (変更前 = HEAD、変更後 = 同じ + この修正。`--probe` = 打って入力中の表示・Enter の結果・Space の結果を返す使い捨ての道具。BuiltIn と、`NSSpellChecker` が答えた語を表にして流す NS の両方。入力はどれも 1 行 1 入力):

  | 入力 | 件数 | 差分 (BuiltIn / NS) |
  | --- | --- | --- |
  | 既存のコーパス (corpus.txt) | 5,040 | 0 / 0 |
  | readings.txt の全語をローマ字にしたもの | 20,174 | 0 / 0 |
  | 上の語 + 助詞 8 種 (ga de to wo ni ha no mo) | 161,392 | 0 / 0 |
  | 孤立した末尾の文字の表 (上) | 988 | 43 / 43 |
  | z / q を含む自作入力 (実在の英語・略語・コード識別子・変数名・コマンド・日本語のローマ字と打ち間違い・文) | 570 | 126 / 126 |
  | 格子 (左 12 種 × 1 音 10 種 × 末尾の子音 20 種 × 右 8 種) | 19,200 | 1,644 / 1,644 |

  - 差分は全件が「末尾の消されていた文字が残る」方向。内訳: (a) Enter / Space の結果に消えていた文字が足されただけ、(b) `aq`→`ああ`・`biq`→`びあ` のように `a` に置き換わっていた `q` が残る、(c) NS で `bas`・`haz`・`yas`・`das`・`uh`・`eq`・`mol`、BuiltIn で `hes` は、直す前は `ば`・`は` などの日本語に確定していたが、直さなくなったので打ったままの英字で確定する (入力中の表示は元から `hes`・`uh` など英字 / `ばs` で、表示と Enter がそろう)、(d) `Hello ik` → 変更前 `Hello i` / 変更後 `Hello いk` のように、英語・URL・`README.md` の後ろの 1 音 + 子音で、消した後の 1 文字が英語に見えていたものが、日本語の 1 音 + 子音になる (BuiltIn 100 件。`Hello `・`README.md `・`https://` の後ろの `i`・`u`・`ha` + 子音。NS はこれに加えて、NS が英単語と答える `bas`・`haz`・`uh` が、日本語に確定していたものから英字に確定する 7 件 + `.bas.` など 12 件 = 107 件)。(e) 記号の前 (`baz?`・`(baz)`・`"baz"`・`def baz():`・`baz_`) も、同じ `FixTypos` を通るので `ばz？` のように残る (入力中の表示が変わる分も同じ原因)。
  - 手で確かめた上の例外: 差分の中に、英単語 (`jazz`・`quiz`・`pizza`・`buzz`・`fizz`)・`qa`・`qr`・`zzz`・`xz`・`kazu`・2 音以上の直し・日本語の語 (readings)・矢印 (`zh` など) は 1 件も無い。
  - 入力中の表示と Enter の結果が違う入力 (英字だけの 1,162 入力のうち): BuiltIn で 134 → 73 件、NS で 182 → 125 件。残りは、Enter で語全体が英単語と判定される入力 (`sag`・`sam`・`say` など。z・q 以外の子音も同じで、今までどおり) で、末尾が z / q のものは BuiltIn で 0 件、NS で 3 件 (`haz`・`oz`: NS が英単語と答える)。
- **トレードオフ**:
  - 前が 1 音だけの「本物の打ち間違い」の直しが無くなる: `aq` (= `aa` の打ち間違い?) → `ああ`、`ik`・`uh`・`ser`・`tok` のように 1 音 + 隣のキーを一緒に押したもの → 1 音だけ。いずれも 1 音だけの入力で、根拠が弱い (略語・変数名と区別できない)。
  - **前が 2 音以上でも、最後が 1 文字の付属語で終わるものは、z・q が消えるまま**: `kyouhaz` → `きょうは`、`foobaz` → `ふぉおば`、`tabetaz`・`mataz`・`okaz`・`abz`、`kyouhaq` → `きょうは`。`Segment` の「最後の区切りが付属語 (`は`・`ば`・`た`)」を根拠にしているため。`foo_bar_baz` のように `_` などで区切られた最後の 1 音は直った。区切りのない `foobaz` が残るのは、同じ原因で、最後の区切りの長さ (2 音以上の語か) まで見るともっと広く直せる (未実装。実装するなら `Segment` が最後の区切りの長さを返す形にして、直す条件を「最後の区切りが 2 音以上」にする。`tabetaz`・`mataz` の直しも失われる)。
  - `desu` の後ろの `h`・`j`・`y` (`desuh` → `です`) は、直しの対象のまま (2 音。`yoroshikuy` と同じ設計)。
  - 打ち間違いの直しが切れていても (設定)、元から消えない。
  - Space で変換を始めた読みも同じ関数を通るので、`baz` + Space の読みは `ばz` になる (実エンジン azooKey でどう変換されるかは未確認)。
- **未検証**: Mac 実機 (TextEdit など) で `baz`・`kaz`・`foo_bar_baz` を Enter する確認。実機の `NSSpellChecker` は答えが BuiltIn と違うが、`--probe` では NS の答えを表にして流した (`NSSpellChecker` を直接使うのは語の有効性の問い合わせだけで、`CallbackWordChecker` のキャッシュや Swift 側の経路は通っていない)。Swift・`MeltypeSession.HandleKey` の経路 (記号キーが vk `0x07`) は試していない。Windows・Linux。Space 後の azooKey の変換結果。

### Q1 査読 (独立した査読員 R5) の結果と対応

- **P0 なし**。1 音の先頭 135 種 × 末尾 a〜z × 文脈 11 種 × 後ろ 5 種の計 130,910 入力を、修正前と BuiltIn・NSSpellChecker の両方で比べた。差分は各 2,945 件で、(a) 消えていた文字が残る、(b) `q` が `a` に置き換わっていたのが `q` のまま残る、(c) `hes`・`wok`・`uh`・`haz` などが英字のまま確定する、のどれかだった。「修正前は英字のまま確定していたのに修正後は日本語になる」入力は 0 件。readings.txt の全語 20,174 件に `z`・`q` を付けた 40,348 入力 (NS の差分 229 件) とコーパス 5,040 件でも、実在の日本語の誤削除は出なかった。`--eval` は 1242/1244 のまま。P26 の `z`+`hjkl` の矢印と `zj` の直し、`bazh`・`kazl` などは差分 0 件。条件を外す変異 (入力途中の `n` を数える項・閾値 `< 2` を `< 3`) で、どちらもテストが赤になる。
- **P1 → 対応済み (固定)**: 前が 2 音以上で最後が 1 文字の付属語 (`ば`・`は`・`た`・`か`) で終わるもの (`foobaz`→`ふぉおば`、`kyouhaz`→`きょうは`、`tabetaz`→`たべた`、`aaz`→`ああ`、`kkaz`→`っか`) は、`z`・`q` が今も Enter で消える。直すと `tabetaz`→`たべた` のような本物の打ち間違いの補正も失われるので、直していない。この限界が将来うっかり広がる・塞がるときに赤になるよう、現在の答えを固定するテスト `RomajiTypos_Q1_AlsoHoldsBeforeSymbols_AndTheKnownLimitsArePinned` を足した。`foo_baz` は直るのに `foobaz` は直らない、という非対称が残る。
- **P2 → 対応済み**: テストが Enter だけだったので、記号の直前 (`baz?`・`def baz():`) も同じ `FixTypos` を通ることを同じテストで固定した。なお `baz,` は `ばz` ではなく `ば‥` になる。`z,` はローマ字入力の規則 (`z,` → `‥`、`z.` → `…`、`z-` → `〜`) で、打ち間違いの補正ではない (変えていない)。
- **P2 (記録のみ)**: 英語の文脈で `Hello tok` は、入力中の表示は `tok` (英字) なのに Enter は `Hello とk` になる (修正前は `Hello to`)。文字は減らなくなったが、英語の文の中にかなが混ざる形に変わる (BuiltIn 約 100 件・NS 107 件)。入力中の表示と Enter の結果が食い違う形は残る。`baz` 単独は `ばz` で確定する (英語なら `baz` のままが理想だが、これは英語判定の別問題)。
- **未検証**: 実機の Mac と Swift 側の経路、Space 後の azooKey の変換結果。

- 作業用のコピー・比較表・評価用の入力は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

## 追加項目 P34 (アーティスト名・記号入りの名前を英字のままにする: 本家 #303・PR #315 の改良導入)

- **現象** (ベースは HEAD `2a5c765`。Mac 版 Core の `--repro` / テストの Keyboard で確認。本家 issue #303 の例):
  - 辞書にない人名が「大文字の語 + 日本語」やローマ字として読める綴りに区切られる: `Wiz Khalifa` → `Wiz Kはぃふぁ`、`Ariana Grande` → `Ariana Granで`、`Ed Sheeran` → `Ed Sheえらん`、`Marshmello` → `Marshめっぉ`、`beabadoobee` → `べあばどおべえ`、`The Chainsmokers` → `The ちゃいんsmokers`、`iann dior` → `いあんぢor`、`999dobby` → `999どっby`。
  - 記号を混ぜて書く名前・略語が崩れる: `B.o.B` → `B.お。B`、`U.S.A.` → `う.S.A。`、`T.K.O.` → `T.K.お。`、`R.I.P.` → `R.I.P。`、`Chri$tian Gate$` → `Chり＄ちあんGate$`、`Ke$ha` → `け＄は`。
- **原因**:
  - 名前: 辞書 (`propernouns.txt`) に無く、`FindSpans` が「大文字の語 + 助詞で始まるローマ字」(`K|halifa`、`Gran|de`) や「知っている短い語 + 残り」(`She|eran`、`Marsh|mello`) の規則で区切る。`beabadoobee`・`dior`・`dobby` は、ローマ字として最後まで読めて英語の根拠が無いので日本語になる。
  - 記号入り: 英単語に見えないので、`.` は日本語の句読点 (`。`)、`$` は全角 `＄` として扱われ、英字の部分もローマ字に読まれる。P29 の `DotSuffixEnd` は「英語の語 + `.`」の後ろだけを見るので、`B`・`U` のような 1 文字の頭では働かない。
- **本家 PR #315 の中身と、Mac 版にそのまま入らなかった点**: 本家は (1) `propernouns.txt` に 10 語、(2) `IsStylizedName` (正規表現 2 つ) を `Segment` の「構造のあるトークン (`@`・`_`・`://`・キャメルケース 2 回以上)」の条件に足す、の 2 つ。**Mac 版の `Segment` にはこの「構造のあるトークン」の分岐が無く、本家の hunk は当たらない**ため、`IsStylizedName` は Mac 版の `Segment` に自前で足した (調査員の試作と同じ置き場所)。本家の正規表現の形は残して、`$` の規則だけ狭めた (下記)。
- **修正** (最小限。確信が無いときは今までの判定に戻す):
  1. `dictionaries/propernouns.txt` に「音楽」を足す。**8 語**: `Khalifa Grande Sheeran Marshmello Beabadoobee Chainsmokers Dior Dobby`。本家の 10 語のうち `Ariana`・`Iann` は入れない (理由は「辞書の判断」)。他の辞書・既存の語との重複は 0 (`grep -il` で `dictionaries/*.txt` に無いことを確認、`node tools/check-dictionaries.mjs` はエラー 0・警告 0)。形式は既存の行と同じ (空白区切り・正しい大文字小文字・`#` の見出し)。
  2. `CompositionDetector.Segment` の、先頭の矢印 (P26) の切り出しの直後・`FindSpans` の前に、**入力全体 (変換ボックスの先頭から最後まで、空白を含まない) が記号入りの名前の形なら、英語の 1 区間にして返す**。形は次の 2 つだけ (`IsStylizedName`)。
     - 英字 1 文字ずつを `.` でつないだ、大文字を含む略語: `^(英字.)+英字.?\z` (`B.o.B`・`U.S.A.`・`T.K.O.`・`R.I.P.`・`A.B`)。
     - 大文字で始まり、`$` の両側に英字がある名前: `^英字+(\$英字+)+\z` (`Ke$ha`・`Chri$tian`・`A$AP`)。
     - 本家との違い: **語末の `$` (`Kyou$`) は含めない** (本家は `[A-Za-z]*` で含める)。`Kyou$` は日本語 `きょう＄` のままにする。
     - 適用しない場合: かな入力 (`kanaInput`)・判定の強さが手動 (Manual。Shift で打った大文字始まりの語だけを英語にする設定で、変更前から `Marshmello` などは英字。記号入りは従来どおり)・3 文字未満・`.` `$` が無い・先頭が `.`/`$`。
     - 先に返すので、P29 の `DotSuffixEnd` (`FindSpans` の中) や `FixTypos` と順序の競合は起きない。形に当たらない入力は 1 行も通らない (`IndexOfAny` で早く抜けるので、記号の無い入力の 1 キーごとの追加コストは文字列を 1 回つなぐ程度)。`Memory` (利用者が覚えさせた語) は見ない: `Memory` は `[a-z]` だけの語しか覚えない (`LanguageMemory.RememberCore`) ので、記号入りの名前には効かない。
- **変更したファイル**: `dictionaries/propernouns.txt` (+4 行)、`src/Meltype.Core/Composition/CompositionDetector.cs` (+20 行)、`src/Meltype.Core.Tests/FeedbackTests.cs` (+60 行)。Swift・`Exports.cs`・`Meltype.Mac.Native` は触っていない。
- **テスト** (508 → **511 件**、全部通る。査読後に、既知の限界と入力中の表示を固定するテストを 1 件足した。以下の 505 → 507 は単独で当てたときの件数): `FeedbackTests.Issue303_ArtistNames_StayEnglish` (本家のテストを移植 + `T.K.O.`・`R.I.P.`・`A$AP`・`Ke$haga`・`dior`・`Iann Dior`。`Anisiyouka`→`Aにしようか`・`Tokyonisumu`→`Tokyoにすむ` の「大文字の語 + 助詞は今までどおり区切る」を含む)、`Issue303_StylizedNameRule_LeavesJapaneseTypingAlone` (今までの答えを固定する守りのテスト。BuiltIn の英単語表を使う)。
  - **赤を先に確認**: テストだけ足した状態で `Issue303_ArtistNames_StayEnglish` が `Wiz Khalifa` → `Wiz Kはぃふぁ` で落ちた (守りのテストは元から通る。変更前の答えを固定するため)。辞書だけ足すと次は `B.o.B` → `B.お。B` で落ち、規則を足して通った。
  - **ミューテーション確認** (規則の条件を 1 つずつ外して、守りのテストが赤になることを確認): 語末の `$` を許す → `Kyou$` で赤、ドットの「大文字を含む」を外す → `a.b.c` で赤、Manual 除外を外す → 手動の `Ke$ha` で赤、`$` の「大文字で始まる」を外す → `ke$ha` で赤、規則ごと外す → `B.o.B` で赤。辞書の語は、外すと `Wiz Khalifa` で赤。
  - 守りのテストの固定対象: `10.5`・`No.5`・`A.I.`・`3.14`・`$100`・`1$`・`kyou$`・`Kyou$`・`Ke$`・`ke$ha`・`foo$bar`・`Hello.wakarimashita`・`example.com`・`foo.bar`・`A.I.nohanashi`・`U.S.A.no`・`a.b.c`・`x.y.z`・`Mr.Tanaka`・`Meltype.dll` (P29)・辞書に入れなかった `ariana`・`iann`・`kyouhaarianawo`・`kyouhaiannwo` と、辞書の語の頭になる日本語の語 `aria`・`ariawase`・`chai`・`chain`・`chairo`、手動の `Ke$ha`・`B.o.B`。
  - 品質テスト (`--eval-json`): 変更前 1242/1244 → 変更後 **1242/1244** (外れる 2 件も同じ `my name is taro`・`apinoerror`。1,244 件の個別の成否は全件同じ)。BuiltIn のみ。
- **前後比較** (`--expect` で Enter 確定した結果と、`--prefixes` で 1 文字ずつ打った各時点の表示。変更前 HEAD と変更後を、BuiltIn (BI) と NSSpellChecker の答えを差し込んだ判定器 (NS) の両方で同じ入力に流した。道具は P31 の `regress.py` を流用。NS は P31 と同じく、実際に `IsWord` に来た語を `checkSpelling(language: "en")` に投げて集め、新しい問い合わせが出なくなるまで繰り返す)。全件の差分は `P34-diffs.md` と `diffs/*.tsv`。

  | 入力 | 件数 | 差分 BI | 差分 NS |
  |---|---|---|---|
  | コーパス (review/work/corpus.txt) | 5,040 | **0** | **0** |
  | readings.txt の全語をローマ字にした語 | 20,174 | **0** | **0** |
  | 同 + 助詞 8 種 (ga de to wo ni ha no mo) | 161,392 | **0** | **0** |
  | P29 の拡張子・ドメイン系の入力を集めたもの (「.」を含む) | 47,861 | 5 | 5 |
  | 自然な文の自作 ($ . 入り・辞書の語入り) | 130 | 34 | 31 |
  | 辞書の語の周り (語・先頭大文字・全大文字・途中まで・助詞・前置き) | 2,507 | 794 | 537 |
  | $ . の自作 (固定 87 件 + 組み合わせ 6,000 件) | 6,008 | 528 | 512 |
  | 英字 1 文字ずつの . つなぎ (`A.b`・`A.B.`・`A.B.C.` など) | 9,512 | 5,612 | 5,612 |

  - **期待どおりの改善以外の差分は 0 件**: 差分の入力を機械的に (a) 空白で区切った 1 区間が記号入りの名前の形、(b) 辞書に足した 8 語のどれかを含む、に分けると、どの組でも (a)(b) のどちらでもない入力の差分は 0 件。規則の形に当たる入力 (BI・NS とも約 8,000 件) は、変更後に全件が入力どおり (英字のまま)。
  - P29 系 47,861 件の差分 5 件は `U.K.`・`U.S.`・`U.K. dearu`・`U.S. nitsuite`・`U.S.A ni iku` (変更前 `う.K。` など。いずれも英字になった)。P29 の拡張子・ドメイン (`Meltype.dll`・`yahoo.co.jp` など) は 1 件も変わらない。
  - 日本語 20,174 語 (+ 助詞 161,392 件) の確定結果は不変。**辞書追加の語で始まる語 (調査員の言う aria・ariawase・chaimu・china・chairo・chairudo) の確定も不変**。
  - 自然な文の例 (BI): `B.o.B wo kiku` `B.お。B をきく` → `B.o.B をきく`、`Ke$ha no uta` `け＄はのうた` → `Ke$ha のうた`、`Wiz Khalifa wo kiku` → `Wiz Khalifa をきく`、`Ariana Grande wo kiku` `Ariana Granでをきく` → `Ariana Grande をきく`、`Starbucks no Grande` `Starbucks のGranで` → `Starbucks のGrande`、`U.S.A. ni iku` `う.S.A。にいく` → `U.S.A. にいく`。名前の後ろの空白は残る (P29 と同じ決まり。`Ke$ha no` → `Ke$ha の`)。
  - **本家のテストの 14 例**での正解数 (14 件中): 変更前 2 → 規則だけ 6 → 辞書だけ (8 語) 9 → **最終 (規則 + 8 語) 13** → 本家の 10 語 + 規則 14。最終で外れる 1 件は `iann dior` (`いあんdior`)。

- **辞書の判断 (Ariana・Iann を入れなかった理由と、入れない選択肢との比較)**
  - 1 語ずつ外して比べた (BI/NS、`P34-wordtable.md`)。**`Ariana` と `Iann` だけが、日本語の名前としてよく打つ綴りと衝突する**: `ariana` = ありあな (アリアナ)、`iann` = いあん (イアン。語末の ん は `nn` で打つので `iann` が普通の打ち方)。本家の 10 語を入れると、`ariana` + Space / Enter が `ariana ` (英字のまま確定して、アリアナへの変換に進めない)、`iann` + Space が `iann ` になる (実測)。`kyouhaarianawo` → `きょうはarianaを` のように文の途中でも英字になる。`Ariana` は大文字で始めれば辞書なしでも変更前から英字なので、辞書で増えるのは小文字だけ。readings.txt にこの 2 語は無い (上の 16 万件の比較で、10 語を入れた版でも差分が 0 件だったことから)ので、機械的な比較には出てこない。上の実測は個別に打った入力による。
  - 残る 8 語は、ローマ字として読めない (`kh`・`gr`・`rsh`・`dior` の r・`dobby` の bb など) か、日本語の語として自然に打つ綴りではない (`sheeran` = しぇえらん、`beabadoobee`、`chainsmokers`)。1 語ずつ外すと、その語を含む入力だけが変更前の答えに戻り、他は変わらない。
  - 代償: 小文字の `iann dior` は `いあんdior` (dior だけ英字)、小文字の `ariana grande` は `ありあなgrande` (BI では本家の 10 語なら英字)。`Iann Dior`・`Ariana Grande` (大文字始まり) は直る。一語足せば戻せる (本家の行をそのまま使う場合は `Khalifa Ariana Grande Sheeran Marshmello Beabadoobee Chainsmokers Iann Dior Dobby` にする)。

  | 選択肢 | 本家の 14 例の正解 | 日本語の語・名前への影響 |
  |---|---|---|
  | 辞書なし (記号の規則だけ) | 6 | なし。名前 8 語は変更前のまま崩れる |
  | **8 語 (最終)** | **13** | 入力中の表示が 1 時点だけ英字に見える語がある (下記)。確定は不変 |
  | 本家の 10 語 | 14 | 上の 2 語 (`ariana`・`iann`) が英字のまま確定。`aria`・`ariawase` も入力中に英字に見える |

- **入力中の表示 (「表示だけ英字」になる語)**: 辞書の語の頭 4 文字以上が、打っている途中だけ英字になる (`CompositionDetector` の `growing && _proper.HasPrefix(...)` の規則。Space / Enter での確定は変わらない)。readings.txt の全 20,174 語を 1 文字ずつ打って比べると、変わるのは **5 語**: `chaina`・`chaini-zu` (`chai`・`chain` の時点)、`chaimu`・`chairudo`・`chairo` (`chai` の時点)。BI と NS で同じ。コーパスでは `chainsawwa`・`chairudo`・`chairudoga`。例: `chai` と打った時点で `ちゃい` → `chai` に変わり、次の文字で `ちゃいろ` に戻る。**`chai` / `chain` / `shee` で止めて Space を押した場合**、変換候補は `ちゃい`/`ちゃいん`/`しぇえ` のまま (変更前と同じ。表示 `chai` だけが違う)。`aria`・`ariawase` は 8 語では変わらない (`Ariana` を外したため。本家の 10 語では `aria` の時点で英字に見えた)。`shee` は日本語の語に無いので影響なし。`dior` は変更後 `dior` (変更前 `ぢor`)。`china` は変更前も `china` (英語の語)。
  - 入れない選択肢との比較: `Chainsmokers` を外せば `chai`/`chain` の 5 語の見え方は戻るが、`The Chainsmokers` は崩れたまま。見え方だけの差 (確定は不変) と名前が崩れる損を比べて、入れる側にした。
  - 記号入りの規則の入力中の表示: `U.S.A.` などは `U.S.A.` まで打った時点で英字、続けて `no` と打つと変更前と同じ `う.S.A.の` に戻る (`U.S.A.no` の確定は変更前と同じ)。`B.A.` の時点は `B.A。` → `B.A.` になる (変更前は `。` に変わっていた)。

- **トレードオフ・既知の制限**:
  - 名前 + 助詞は区切らない (`#268` の取り下げと同じ方針。名前 + 助詞を区切る処理は再提案しない)。`Ke$haga` → `Ke$haga` (変更前は `け＄はが`。本家も同じ)、`U.S.A.no` → `う.S.A.の` (変更前のまま。`U.S.A.` + 助詞は分けない)。辞書の語でも、`beabadoobee` + `dayo`/`mitai`/`kara`/`made`/`ttara`/`janai`/`nara`、`sheeran` + `ni`/`no`/`nara` (`nn` が ん になる曖昧さ)、`chainsmokersha` (`sha`) は、変更前と同じく日本語のまま (退行ではない)。`khalifa` + `ga`/`de`/`to`/`wo` などは英字 + 助詞に区切られる。
  - 名前が文の途中にあると、規則は効かない: `kyouhaB.o.B`・`kyouhaU.S.A.`・`kyouhaKe$ha` は変更前のまま (`きょうはB。お。B` など)。入力全体がその形のときだけ。Space で区切って打てば効く (`kyouha B.o.B`)。辞書の語は文の途中でも効く (`kyouhakhalifawo` → `きょうはkhalifaを`)。
  - `Kyou$` は変更前のまま `きょう＄` (本家は `Kyou$`)。`$100`・`1$`・`kyou$`・`10.5`・`No.5`・`A.I.`・`3.14` は変更なし。
  - `$` を英字の間に挟んだ、大文字で始まる入力は、英字のままになる: `Hello$dayo` (変更前 `Hello$だよ`)・`Foo$bar` (変更前 `ふぉお＄bar`)・`Python$hwa`。日本語としてこの形を打つ人は想定しにくいが、ゼロとは言えない。小文字始まり (`ke$ha`・`foo$bar`) は変更なし。
  - 英字 1 文字ずつの `.` つなぎで大文字を含むものは、ほぼ何でも英字になる (`A.e`・`I.e` は変更前 `A.え`・`I.え`、`a.B` は `あ。B`、`O.K` は `お.K`)。日本語として使う綴りは見つからなかった (`a.b.c`・`x.y.z`・小文字だけは変更なし)。
  - 辞書の語は、小文字・大文字始まり・全大文字のどれでも英字になる。日本語の語・名前として同じ綴りを打つ人 (例えば `grande` を `ぐらんで` の打ち間違いとして使う) は想定していない。`diorama` は変更前から `ぢおらま` のまま (英単語の判定は別の話)。
  - 手動 (Manual) では記号入りの規則を使わない (`Ke$ha` → `け＄は`、`B.o.B` → `B.お。B`)。大文字で始まる辞書の語 (`Marshmello`・`Khalifa`) は手動でも英字になる (変更後のビルドで確認。手動の判定は変えていない)。
  - `Official髭男dism` は本家 PR も扱っていない (`higedanndism` を 髭男 に変換する話。辞書・変換の候補の問題)。
  - Windows・Linux は対象外 (同じ Core だが、ここでは見ない)。
- **未検証**:
  - Mac 実機 (TextEdit など)・Swift・`Exports.cs`・dylib の作り直し (`mac/build.sh`)・`codesign`・辞書アプリの自己テストは、今回やっていない。Core のテストと `--repro` / `--expect` / `--prefixes` だけ。辞書は `mac/build.sh` で dylib を作り直さないと実機に反映されない。
  - NS 判定器での比較は、実機と同じ呼び出し (`checkSpelling(language: "en")`) の答えを 1 回ぶんの Mac で集めたもの。利用者が macOS の辞書に学習させた語・OS のバージョン違いでは答えが変わりうる。`--eval-json` の品質は BuiltIn のみ。
  - 比較の入力は自作 (コーパス・readings のローマ字化・P29 の入力・自作の `$` `.` 入り)。実際に人が打つ入力の分布ではない。ローマ字化は 7 語を除く全語を機械的に変換した。
  - 実機の azooKey が `chai` / `Khalifa` などの Space 変換の候補をどう出すか。Enter 確定の結果ではなく、入力中の表示 (`chai`) が実際の打ち心地でどう見えるか。
  - 性能: 追加分は 1 キーごとに文字列を 1 回つなぎ、`.` / `$` が無い入力はすぐ抜ける。実測はしていない。
  - 実機で打って確かめる候補 (JIS 配列。`$` は Shift + 4、`.` はそのキー): `Wiz Khalifa` / `Ed Sheeran` / `Marshmello` / `B.o.B` / `U.S.A.` / `Ke$ha` / `Chri$tian Gate$` が英字のまま確定される、`Anisiyouka` → `Aにしようか`、`kyou$` → `きょう＄`、`10.5`・`No.5`・`$100`、`chairo` が `ちゃいろ`。

### P34 査読 (独立した査読員 R6) の結果と対応

- **P0 なし**。コーパス 5,040 件は BuiltIn・NS とも差分 0。自作の敵対入力 (略語 `E.g.`・`I.e.`・`Ph.D.`・`S.O.S`・`M.I.T.`・`N.Y.C.` と `$` を含む名前に助詞や前置きを付けた約 2,900 件、辞書 8 語の大文字小文字・助詞・前置きの組み合わせ約 13,000 件、`/usr/share/dict/words` で 8 語のどれかを含む英単語 51 語) で、日本語の語が英字に変わる退行は見つからなかった。差分はすべて「名前が英字のまま確定される」改善か、辞書の語そのもの・その語 + 助詞・その語を含む綴り間違いだった。判定の強さ 4 段階のうち Manual だけ記号入りの規則が効かない (計画どおり)。
- **P1 → 対応済み (固定。広げる案は未実装)**: 名前の直後に記号が付く形 (`Ke$ha,`・`Ke$ha!`・`(B.o.B)`・`"Ke$ha"`・`B.o.B's`・`U.S.A.,`) は規則が効かず、変更前の崩れた答え (`け＄は、`・`(B.お。B)`・`う.S.A。、`) のまま。規則は空白で区切った 1 語全体が名前の形のときだけ働く (`kyouha B.o.B`→`今日はB.o.B` は効く)。本家 PR #315 の正規表現も入力全体に当てる形で、同じ制限と見られる (本家の挙動そのものは未確認)。末尾の `, . ! ? ; : ) " ' ’` と先頭の `( "` を許す案があるが、「確信が無いときは今までの判定に戻す」方針と、回帰の再査読の手間から、今回は入れていない。使ってみて困るなら別項目にする。
- **P1 (テスト不足) → 対応済み**: 守りのテストが Enter だけだったので、`Issue303_StylizedName_KnownLimits_AreWordByWord_AndShowTheNameWhileTyping` を足し、(1) 空白で区切った文の中 (`kyouha B.o.B`・`kyouha Ke$ha dayo`)、(2) 記号が付く形の現在の答え (限界)、(3) 1 文字ずつ打つ途中の表示 (`B.o.B`・`Ke$ha`・`U.S.A.`) を固定した。
- **P2 (記録のみ)**:
  - BuiltIn のみの退行: 辞書の語で始まる英単語が壊れる (`grandeurs`→`grandeうrs`、`grandeeism`→`grandeえいsm`、`grandesque`→`grandesqうえ`)。NS はこの 3 語を英単語と答えるので実機の Mac では起きない。Windows・Linux のスペルチェッカーでの結果は未確認。
  - `marshmellowwo` (marshmallow の綴り間違い + を) が BuiltIn・NS とも `marshmellowを` から `marshmelloっを` に変わる。単独の `marshmellow` は変わらない。`khalifatwo` は BuiltIn のみ `khalifatを` から `khalifatwo` に変わる。大文字始まりの `Beabadoobeega` は `Beabadoobeeが` に区切られる (khalifa + 助詞と同じ方針)。
  - `chai`・`chain` の時点で、打ちかけの表示は英字になるが、Enter・Space の変換は日本語 (`ちゃい`・`ちゃいん`) のまま (確定は不変)。
  - `Hello$dayo`・`Kyou$ha` のように大文字始まりで `$` を英字の間に挟む入力が英字になる (既知)。
- **未検証**: Mac 実機での入力と、dylib の作り直し (`mac/build.sh`) 後の確認。ルールの条件を 1 つずつ外す変異テストは、実装者が 5 通りで実施済みで、査読員は実行していない。

- 作業用のコピー・比較表・評価用の入力は作業用フォルダー (一時) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

## 追加項目 S1 (セキュリティレビューの対応: ログの仮想キーコード・pkg の署名検査・読み込み失敗の通知ほか)

- **経緯**: 2026-10-11 に Mac 版全体のセキュリティレビューを行った (4 領域: 自動更新・入力データ・FFI 境界・ビルドと配布。統合レポートは独立した査読 3 回)。**重大度「高」は無し**。「中」10 件のうち、作者署名の基盤 (Ed25519/Developer ID)・CI による来歴・新しい設定項目は運用コストのため見送り、残りを最小の変更で入れた。
- **修正** (実装は Sonnet、査読は Opus、軽微修正は Haiku の分担):
  1. **M3** `CompositionController.cs:501`・`InputSession.cs:212`: 英数判定を打ち切ったキーの仮想キーコード (`0x31` など) をログに出さない。`FileLog` だけ ON (打った文字は記録しない設定) でも、数字・記号のコードが並ぶと英字+数字の列 (パスワードに多い) が読めてしまうため。`VirtualKeys.DescribeKind` で「数字 / Enter / Space / 英字以外」の分類だけ残す。守りのテスト `Log_EnglishModeAbort_DoesNotRecordVirtualKeyCodes` を追加 (修正前のコードで落ちることを査読員が確認)。
  2. **M8** `mac/pkg/scripts/postinstall`: 登録の前に `codesign --verify --deep --strict` を行い、失敗なら `exit 1` (Installer が失敗を表示)。ad-hoc 署名なので見つかるのは主に破損。ファイル自体は置かれたまま残る (限界)。
  3. **M7** `NativeCore.swift`: `libMeltypeNative.dylib` を読み込めなかったとき (`privateFrameworksPath` が nil の場合を含む) に、プロセスの寿命で 1 回だけダイアログで知らせる (従来は NSLog のみで、利用者は IME が無効なことに気づけなかった)。素通し (fail-open) の挙動は変えない。`Exports.cs` の `Destroy` のコメントを実態 (不正なハンドルは未定義動作、Swift 側の単一所有が前提) に合わせた。
  4. **M2(b)** `Updater.swift`: 更新ダイアログに、隔離属性 (Gatekeeper の確認) を外して入れ替える旨を明記。
  5. **低** `Updater.swift`: 表示に使う `tag_name` から制御文字を除き 64 文字に切り詰め (URL 検証には影響しない)。`update.log` の open に `O_NOFOLLOW`。
  6. **M9 (軽量部分)** `mac/build.sh`: `swift build` に `--force-resolved-versions` (Package.resolved と食い違えば失敗させる)。存在しない `mac.yml` を指すコメントを修正。`global.json` (SDK 10.0.401、`rollForward: latestFeature`) を追加。
  7. **文書** `SECURITY.md`: 秘匿入力でない欄での言語学習の条件 (英字のみ 2 文字以上・小文字化) と `AppRules` の書き方、Hardened Runtime の無い ad-hoc 署名の限界を明記。`mac/README.md`: pkg の SHA-256 確認と SDK の版。
- **運用**: 実機に残っていた過去のトレースログ 2 本 (`imk-trace.log`・`imk-trace.log.round1`) を作業用フォルダーへ退避 (24 時間で消える)。
- **レビューで分かった訂正**: 周辺テキストの一部を azooKey に context として渡しているという領域レビューの指摘は誤りで、`Converter.swift` の `learn(context:)`・`clauses(for:context:)` は context を受け取るだけで azooKey には渡していない (学習は azooKey 自身の候補だけ)。
- **見送り (理由はコスト・運用負荷)**: Release の作者署名と Updater での検証 (M1)、`install.sh` の検証範囲への取り込み (M2(a))、CI/ビルド来歴 (M9 本体)、コード種別アプリで英字語の学習を OFF にする設定 (M5)、不正ハンドルの ID 間接化 (M7(b))。SECURITY.md の既存の「限界」の記述で足りると判断。
- **検証**: Core テスト 519 件 (518 → 519)、`build.sh --no-install`、`codesign --verify --deep --strict`、`check-mac-aot-settings.py` 4 件、`bash -n`。
- **Mac 実機の確認** (2026-10-11、TextEdit、ビルド 21 を入れ替え): `kyouhaiitennkidesu`→今日はいい天気です、`ko1`→こ1、`Hello world`→Hello world が期待どおり (退行なし)。起動時の自動更新確認も動作 (`update.json` の `lastCheck` 更新)。
- **未検証**: M2(b) の更新ダイアログの文言 (入れた版が公開済み Release より新しく、ダイアログ自体が出ない。次の Release で確認)、M7 の通知 (dylib を壊しての表示)、pkg の postinstall の `exit 1` が Installer でどう見えるか、`DescribeKind` のログ文の Windows 側 (対象外)。
- **後に残した候補**: `docs/RELEASE.md:8,44`・`docs/DEVELOPMENT.md:90` に CI (mac.yml) が動いている前提の古い記述が残っている。ABI の版数不一致のときも通知を出す。

## 追加項目 P35 (英文の途中の go・so・do・to を日本語に確定し直さない: 本家 #317、P27 と併用)

> docs/IMPROVEMENT_PLAN.md への追記用の下書き。基準は HEAD=6c005b0 (`P35.patch` は現在の HEAD c3fcf4d にも `git apply --check` で当たる)。**Mac 実機・Swift 側は未確認**。Windows・Linux は対象外。

- **現象**: 英文の途中で `go` `so` `do` `to` `be` `in` `on` `he` `no` `an` `here` `see` `make` … (英語の単語で、ローマ字としても最後まで読める語) を打ち、そのすぐ後ろに日本語に見える語 (`susi`・`ningen?`・`gasuki`) が続くと、前の英語の語を Enter で日本語に確定し直してしまう。
  - `I want to go susi` → `I want to ごすし` (期待: `I want to go すし`)
  - `It is so susi` → `It is そすし`、`I have to do ningen?` → `I have to どにんげん？`、`I have to be ningen` → `I have to べにんげん`、`Thank you so susi?` → `Thank you そすし？`、`I will go nihon` → `I will ごにほん`、`We can see susi` → `We can せえすし`、`Please take susi` → `Please たけすし`、`Today I go susi` → `Today I ごすし`
  - 現状の再現 (HEAD、同梱の英単語表の `--repro`。Space 確定と入力中の表示は変わらず、**Enter の確定結果だけ**が変わる): 下の表。
  - どの語が壊れるかの実測: 同梱の英単語表から「ローマ字として読める綴りの小文字 1〜8 字の語」888 語を `I want to {語} susi` に入れ、確定し直しの分岐を止めた版と比べると **317 語**が日本語に変わった (`go so do to in on he no be an here see make take name time one made home game …`)。`go・so・do・to` だけではない。
- **原因** (HEAD=6c005b0): 日本語に変わるのは `CompositionController.CorrectPreviousCommit` (L1917) の第 2 分岐 (L1935 `else if (previous.English && !english && !_detector.IsAmbiguousWord(raw) …)`) だけ。`CommitText` (L2084 で呼び、L2086-2090 で `IsAmbiguousWord(raw)` の語だけ `CommitRecord` に積む。`IsAmbiguousWord` は `CompositionDetector` L406) が「英語とも日本語とも読める語」を文脈なしで英語として確定しておき、**次の語が日本語**と分かると、前の語を `ConvertLenient` でかなに直す (`sushi` + `gasuki` → すしがすき、のための仕組み)。P27 (L1900 `EnglishPronounAfterEnglish`、L1938) はこの分岐の入口で `you/are/we/me/she` だけを止めていた。ほかの語は止める条件が無かった。
  - 入力の表示 (入力中) は変わらない: 確定し直しは確定の後で起きる。
  - 別経路 (今回は直さない): `Let me go susi?` → `Let めごすし？`、`Where to go ningen?` → `Where とごにんげん？` は、確定し直し (`bs:` のイベント) を通らず、**最初の確定の時点**で `me`/`to`/`go` が日本語になっている (前が英語 1 語だけで「英文」と見なされない)。`CorrectPreviousCommit` を止めても同じ結果。
- **候補の比較** (同じ入力群、同梱の英単語表 BuiltIn。正解は「前の英語の語を残す」= 確定し直しの分岐を止めた版の出力、「従来の正しい確定し直し」= 変更前の出力):

  | 案 | 内容 | 直る入力 (英文の中: g2 の E 7,080 件のうち直す必要があるのは 5,448) | 従来の確定し直しを壊す入力 (母数 10,608 件 = 日本語の文 J 4,134 + 英語名のあとの助詞 K 1,120 + K2 1,248 + 日本語の文の先頭の英語名 M 1,600 + 日本語由来の語 O 2,096 + 代名詞 P 220 + 文末記号 X 190。内訳に出る数は J・K・K2・M だけで、O・P・X は全案で 0) |
  | --- | --- | --- | --- |
  | A | 本家の条件 (前の語の前が英語 かつ 日本語由来でない) を `CorrectPreviousCommit` に足す。キャレットの前の文字から読む。代名詞は P27 のまま | 5,340 | J 1,764 + K 1,040 + K2 1,040 + M 1,300 = **5,144** |
  | B | 同じ条件を `CommitRecord` に `AfterEnglish` を持たせて実装 (本家と同じ形) | 5,340 | **5,144** (A と全 25,341 行で出力が同じ) |
  | D | B を「直前が英文 (2 語以上)」に絞る | 5,340 | K 1,040 + K2 1,040 + M 1,300 = **3,380** (J は 0) |
  | C | P27 の代名詞の集合を go so do to … に広げるだけ (前の語の一覧は P27 のまま) | 864 | 0 |
  | **採用 (F)** | 代名詞以外の曖昧な語も、**直前が英語の機能語**のとき残す。一覧は P27 のものに代名詞・助動詞・頻度や動作の語を足したもの。日本語由来の語と 1 文字の `i` は今までどおり。to・no・he は代名詞の後ろでは今までどおり | 5,202 | **0** |

  - 全体の数 (採用案、BuiltIn、約 25,000 入力。下の「前後比較」参照): 自然な英語の言い回し 297 件 (go/to/so/do/no/be/in/on/he/an/here/see/make の前に付く言い回し × 日本語の語 3 つ) のうち、A/B は 297、D は 204、**F は 270**、C は 96。
  - **本家の条件 (A/B/D) が壊す型** (Survey の「混成版で通る」は P27 のテストには通るが、この型は比較入力に無かった): ① 英語の固有名詞・名詞句のあとの助詞 `New York no tenki` → `New York no てんき` (期待 `New York のてんき`)、`Google Chrome to ringo wo taberu` → `Google Chrome to りんごをたべる`、`Visual Studio Code no settei`、`dark mode no settei`、`pull request to tsukau`、`Hello World so tsukau` (K・K2 の 2,368 件のうち 2,080 件が崩れる)。② 英語名 1 語のあと `Apple here susi`・`iPhone go susi` (J の 1,764 件)。③ 歌詞のような `I love you no koto` (あなたのこと)・`I miss you to issho ni` (360〜435 件)。名詞のあとの `no/to/he` は日本語の助詞のことが多く、英文のつもりの `to` と綴りで区別できない。区別できる手がかりは「直前の語が機能語か」だけだった。
- **採用案**: F。`CorrectPreviousCommit` の第 2 分岐の入口 (P27 の `EnglishPronounAfterEnglish` を一般化した `StaysEnglishAfterFunctionWord`) で、直前に確定した語 (`previous`) の**すぐ前の英語の語** (キャレットの前の文字から読む。P27 と同じ) を見る。
  - `you/are/we/me/she`: 従来どおり `WordsBeforePronoun` に入っているときだけ (P27 のテストを一切変えない)。
  - それ以外の曖昧な語 (go so do to be in on he no an here see make take name time …): 前の語が `WordsBeforeAmbiguousWord` (= `WordsBeforePronoun` + 代名詞 i you we they he she it me us them、be may might must not never please going gonna wanna also always just really only even still ever oh been go come look stay get got went wait click live feel thanks ready able used) か、`'` を含む縮約形 (I'll・can't・let's) のとき、日本語に直さない。
  - 例外 (今までどおり日本語に直す): ① 日本語由来の語 (`sushi ramen anime sake kimono radio …`。本家 #317 の `IsJapaneseOriginWord` を取り込み。english.txt の「日本語由来」の段落を読む) ② 1 文字の `i` (小文字の `i` のまま残しても `I` にはならず、直す利点が無い) ③ `to・no・he` で、前の語が代名詞 (I love you + no koto) ④ キャレットの前の文字が分からない・前の語を取り出せない (記号・日本語・行頭) とき ⑤ 前が名詞 (York・mode・Tower …)。
  - `CommitRecord` は変えない (B と A の出力は 25,341 行で同じ。P27 が使っている「キャレットの前の文字」で足りるので、記録の形を増やさなかった)。本家の `AfterEnglish` は取り込んでいない。
- **変更したファイル** (`P35.patch`、4 ファイル、+約 150 行 / −8 行。テストを除くと約 70 行):

  | ファイル | 内容 |
  | --- | --- |
  | `src/Meltype.Core/Composition/CompositionController.cs` | `WordsBeforeAmbiguousWord`・`ParticleLikeWords`・`PersonalPronouns` を追加。`EnglishPronounAfterEnglish` を `StaysEnglishAfterFunctionWord` に改名して、代名詞以外の曖昧な語も判定。`CorrectPreviousCommit` の呼び出しを置き換え |
  | `src/Meltype.Core/Composition/CompositionDetector.cs` | `IsJapaneseOriginWord` (本家 #317 から。english.txt の「日本語由来」の段落を読む) |
  | `dictionaries/english.txt` | 「日本語由来」の段落をコードが読むことを示すコメント 1 行 (単語は変えていない) |
  | `src/Meltype.Core.Tests/CompositionTests.cs` | テスト 4 件 (下記) |
- **テスト**: 518 → 522 件、全て通る。`CompositionTests`: `AutoCorrect_ShortEnglishWordInsideEnglishSentence_IsNotRewrittenToJapanese` (本家のテスト `AutoCorrect_KeepsEnglishWordInsideEnglishSentence` を参考に、`I want to go susi`・`It is so susi`・`I have to do ningen?`・`I have to be ningen`・`Thank you so susi?`・`I will go nihon`・`We can see susi`・`Please take susi`・`Today I go susi`・`Please come here susi`・`Maybe I'll go susi`・`You can't see susi` の 12 文と、確定し直しで文字を消さないこと)、`AutoCorrect_ShortEnglishWordAfterNounOrAtStart_StillRewrittenToJapanese` (日本語を守る: `New York no tenki`・`Google Chrome to ringo wo taberu`・`Visual Studio Code no settei`・`dark mode no settei`・`pull request to tsukau`・`Hello World so tsukau`・行頭の `go susi`・`to ringo`・`I want sushi gasuki`・`I like ramen daisuki`・`I like sake daisuki`・1 文字の `I think i ninaru`・代名詞のあとの `I love you no koto`・`I miss you to issho ni`・`Thank you no kokoro`)、`AutoCorrect_ShortEnglishWordInsideEnglishSentence_NeedsKnownCaretText` (自分の記録だけで前の語が分からないときは従来どおり)、`JapaneseOriginWords_AreReadFromTheEnglishDictionarySection` (sushi ramen anime sake kimono gundam は該当、go so do to you python github want は非該当)。
  - **赤の確認**: 12 文は、Core の 2 ファイルを HEAD に戻して全件が赤 (`I want to ごすし` など) になることを 1 件ずつ確認。日本語を守るテストは HEAD でも通る (守るためのテストなので当然)。
  - **変異テスト** (条件を 1 つずつ外す): 日本語由来の判定を外す、`i` の除外を外す、代名詞の一覧を外す、動作の語の一覧を外す、代名詞以外の規則を止める、P27 の代名詞を常に残す、機能語の条件を外して常に残す、to/no/he の代名詞除外を外す、縮約形の規則を外す、の 9 通りすべてで、どれかのテストが赤になった。
  - 判定品質 (`--eval-json`) は変更前と**ファイルが完全に同じ**: 1242/1244 (外れの 2 件も同じ)。
- **前後比較** (変更前 = HEAD と採用案。同梱の英単語表 (BuiltIn) と NSSpellChecker 相当 (`ns.swift` で調べた語を `FileWordChecker` に渡す方式。Mac 実機そのものではない) の両方、各入力で入力中の表示・Enter 確定・Space 確定・Space 後の表示の 4 つを比べた):
  - 入力 29,736 件 (重複を除いた数) = コーパス 5,040 + 自作 約 24,700。自作の内訳 (組ごとの件数。組どうしで重なる分があるので足しても総数と合わない): 英文の頭 20 通り × 曖昧な語 58 語 × 日本語の語 6 通り (文末 `?` を含む) 7,080、前の語が名詞や動詞の英文 1,280、自然な言い回し 924 と自作の文 1,645 (うち 20 文は英語と日本語の混ざった自然な文)、日本語の文の中の英語の短い語 4,134、英語の名前 + 助詞 (K 1,120・K2 1,248、先頭が日本語の文の中 M 1,600)、日本語由来の語 131 語 × 4 通りの前置き × 4 通りの続き 2,096、代名詞 220、文末記号と英語の文の後ろの日本語の語 190、既存の確定し直し (`i want`・`make sure you have`・`I love sushi gasuki` など) 2,264、代名詞 + to/no/he/so/do 540。Space 確定と Enter 確定の違いは、全入力で両方を記録して比べた。
  - 入力中の表示・Space 後の表示: **0 件の差** (BuiltIn・NS とも)。差が出るのは Enter / Space の確定結果だけ (Enter 8,496 件、Space 535 件。BuiltIn)。
  - 期待どおりの改善: 英文の中の曖昧な語が英語のまま残る 8,076 件 (BuiltIn) / 8,073 件 (NS)、`I want といぇちtomorrow` → `I want to いぇちtomorrow` 型 8 件、英語の機能語 (love・want・like) の後の非日本語由来の英単語 (yoga・tea・time・name …) が英語のまま 201 件、`I love you so koto` → `I love you so こと` など代名詞の後の so・do 型 210 件 (BuiltIn)。
  - **退行 (従来正しかった確定し直しが変わった)**: 日本語の文・英語名 + 助詞・先頭が日本語・日本語由来の語・P27 の代名詞・文末記号の 10,608 件 (重複を除くと 10,318 件。BuiltIn・NS とも) で **0 件**。コーパス 5,040 件は元々この分岐を通らず 0 件の差。既存の確定し直し (`i want to go` など 日本語→英語 の向き、`I love sushi gasuki` など 英語→日本語 の向き) は 2,264 件のうち変化があったのは上の 201 + `i want to go susi` の 1 件だけ (どれも英語の機能語の後の英単語が英語のまま残る向き)。
  - 反対側の数: 本家の条件 (A/B) をそのまま入れると、同じ 10,608 件のうち **5,144 件**で従来の正しい確定し直しが壊れる (D は 3,380 件)。
  - BuiltIn と NS で「変わる・変わらない」が食い違うのは 33 件 (`Please do kore wo kudasai` と、`koto` を NS が英語の語と答える `I love you so koto` 系)。どちらも今回の変更が原因ではなく、変更前から BuiltIn と NS で違う判定。
- **トレードオフ** (得と損の両方):
  - 得: 英文の途中の短い英単語 (`go so do to be in on he no an here see make take name time …` の 317 語、日本語の語が後ろに続くとき) が日本語に化けない。自然な英語の言い回し 297 件のうち 270 件 (91%) が直る。P27 の仕組みの延長なので、P27 のテストは一切変えていない。
  - 損 (本家の版より直る数が少ない): 前の語が機能語の一覧に無い英文では直らない。`It is time to nemuru` → `It is time とねむる`、(`Have you been to nihon?` は been が一覧にあるので直る)、`come here` は直るが `over here`・`up here`・`down here`・`right here`・`out here` は直らない、`Please over here susi` (over は一覧に無い)、`I like to make ramen desu` の ramen のように日本語由来の語 (`ramen sake judo karate kimono radio` など。これらは日本語のつもりのことがある)。本家の版 (B) は 297 件すべて直す。
  - 損 (従来は直っていた): 英文の機能語の後に、`yoga・tea・time・name・made・mama・papa・sauna・one・tuna` のような日本語でも使う英単語を打ち、そのあとに日本語を続けると、英語のまま残る (`I want mama ni aitai` → `I want mama にあいたい`。従来は `I want ままにあいたい`)。`to tame ni` (と + ために) のように、機能語 `to` のあとの `tame ni` も英語の tame のまま残る。
  - 損: `you to watashi` (あなたと私)・`I no namae` のような、代名詞のあとの to/no/he は従来どおり日本語に直すが、代名詞のあとの `so`/`do` は英語のまま残る (`I love you so koto` → `I love you so こと`。`そ`/`ど`が日本語の助詞として単独で使われることは少ないとみた)。
  - 損: 機能語の一覧は固定の単語リストで、頻度のデータは無い。一覧に入れた語 (like・for・with・and・but・from・about・that …) は名詞の後で助詞のつもりで打つこともあるが、**直前の語が機能語の英文**の中で、さらに**曖昧な語**と**日本語の語**が続くという 3 つの条件が揃ったときだけなので、実際に当たるのはまれとみた (今回の自作入力では 0 件)。
  - 損: 道具の限界として、一覧に入れる語を増やすほど名詞の後の助詞を壊す危険が増える。名詞の可能性が高い語 (time・back・way・up・down・out・over・right・work・next) は意図的に入れていない。
- **限界・今回は直していないもの**:
  - 最初の確定の時点で日本語になる型 (`Let me go susi?` → `Let めごすし？`、`Where to go ningen?` → `Where とごにんげん？`、`They go nihon`)。前が英語 1 語だけだと英文と見なされない。`CorrectPreviousCommit` では直らない別の経路 (最初の判定) の話で、別の項目にする。
  - 前の語が `me/you/are/she` で、その前が機能語ではない型 (`Can you you susi`・`Let me figure susi` の `me`)。P27 の範囲のまま。
  - 日本語由来の語の一覧は english.txt の「日本語由来」の段落をそのまま読む。`radio` `tea` のような語がこの段落に無い/ある は辞書次第 (`radio` は入っている)。段落の見出しや空行の形を変えると読めなくなる (辞書にコメントを入れ、テストで固定した)。
  - 本家の `CommitRecord.AfterEnglish` は取り込んでいない。本家との差が広がるので、本家を取り込み直すときは `StaysEnglishAfterFunctionWord` との統合が必要。
- **Survey / 管理表の補足** (親セッションが docs を更新するときの材料): `UPSTREAM_PR_SURVEY.md` の #317 の評価「代名詞は P27、それ以外は本家の条件、の混成版で通る」は、**P27 のテストは通るが、名詞のあとの助詞 (`New York no tenki`) や歌詞 (`I love you no koto`) を壊す**。実測で本家の条件のままだと従来の正しい確定し直しが 5,144 件壊れたので、条件を「直前が機能語」に絞った。変更の大きさは約 70 行 (テスト除く)。
- **未検証**: Mac 実機での入力 (TextEdit・Chrome など、キャレットの前の文字を読めない入力欄での挙動は、`PrecedingText = null` のテストで従来どおりを確かめたのみ)、Swift 側 (`NSSpellChecker` を実際に呼ぶ経路。ここでは調べた語を表にして渡す方式で代用)、Windows・Linux は対象外。独立した査読 (別のモデル・別の入力) は未実施: この下書きの数字は実装者自身の入力群での測定で、「実装者の比較入力に無い型」を探して K・K2・PN の型を見つけたが、査読者が別の型を見つける余地がある。

### P35 査読 (R10・R11) の結果と対応

- **R10 の P1 → 対応済み**: 英語の題名・名詞の末尾の語 (Go・Be・Live・where・if・for・and・click・look) の後ろの `no`・`to`・`he` が、従来の の・と・へ から英語のまま残る退行 (`Let It Go no uta`・`YouTube Live no haishin`・`SQL where no baai`・`Python and Go no hikaku`)。`to`・`no`・`he` の直前の語は、文の途中にしか来ない狭い一覧 (`WordsBeforeParticleLikeWord`: not・never・please・also・just・really など 23 語) と縮約形だけに絞った。これで R10 の退行 13 例は BuiltIn・NS とも変更前と同じ答えに戻った。
- **R10 の P2 → 対応済み**: ` を含む名前 (`L'Arc`・`Moody's`・`D'Artagnan`) を縮約形扱いしない (`IsContraction`: n't・'ll・'re・'ve・'m・'d と let's など 9 語だけ)。
- **P2 → 限界として残す**: ① 冠詞 `a` の後ろ (`I have a ringo` → `ありんご`)。② `Only you susi`・`Not you susi` など P27 側 (`you/are/we/me/she`) の一覧が狭い。③ `’` (U+2019) は `see`・`here` 型でしか効かない (`I’ll go susi` は最初の確定で `go` が日本語になり、この分岐に届かない。`IsEnglishSentence` が ASCII だけを見るため。文字数のずれによる誤動作は無し)。④ `Let me go susi?`・`Where to go ningen?`・`They go nihon` (最初の確定で日本語になる別経路)。
- **残る退行 (狭い一覧の語で終わる題名)**: `Never Say Never no uta`・`Best ever no …`・`SQL not no …`・行頭の `Please no …`。c5 の日本語の意図 1,375 件のうち 17 件。トレードオフとして受け入れた。
- **R11 の確認 (BuiltIn・NS)**: R10 の退行例 13 件は 0 件。英語のまま残る差分は 113,014 → 58,428 件に減った (外した型は変更前に戻る)。`go to nihon` は従来どおり (`ごとにほん`)。`--eval-json` は 1242/1244 のままでファイルが完全一致。
- **テスト**: 522 → 523 件 (CompositionTests の 4 件に、R10・R11 の例を足した)。`ParticleLikeWords` から `he` を外す変異で赤になることを確認。代名詞の除外判定は、狭い一覧に代名詞が無いので不要と分かり削除した。


## 追加項目 Q4 (Mac 版: 確定し直しで古い英語が消えず、日本語だけが足される)

- **現象** (Mac 実機、TextEdit と Hermes のデスクトップアプリ、Enter で確定): `I love sushi gasuki` → `I love sushi すしがすき` (期待 `I love すしがすき`)、`New York no tenki` → `New York no のてんき`、`Let It Go no uta` → `Let It Go no のうた`。P35 より前の版でも同じ (Core の `--repro` は期待どおりで、Swift の置き換えだけが効いていなかった)。P22 の実機確認の手順 1 (`sushi がすき` → `すしがすき`) は、未検証のまま残っていた項目。
- **原因** (実機のトレースで確認 2026-10-11、TextEdit): Core が返す edit 列は `{deleteBefore=n, original, text=置き換え後の語}` → `{0, 変換中の文字}` の順 (`CorrectPreviousCommit` は `CommitText` の中で、変換中の文字の確定より前に呼ばれる)。Swift の `apply` は変換中の文字 (marked) が入力欄に残ったまま、1 つ目の edit を `insertText(text, replacementRange: 確定済みの文字の範囲)` で入れる。TextEdit・Hermes は範囲を無視して marked の範囲だけを置き換えたと考えると、報告された 3 例がすべて説明できる (古い英語は残り、marked が日本語に置き換わり、続く edit が足される)。Apple の資料 (IMKTextInput・NSTextInputClient) は、marked があるときの `replacementRange` の扱いを定めていない。
- **修正** (`mac/Sources/MeltypeIME/InputController.swift`、約 20 行): 確定し直しの edit (`deleteBefore > 0`、再変換でない) の前に、入力欄の変換中の範囲が、直前に表示した変換中の文字 (`lastView.text`) と同じと確かめられたときだけ、`setMarkedText("", NSNotFound)` (`hideComposition` と同じ呼び方) で取り除く。最初に `insertText("", replacementRange: marked)` で試したが、実機のトレースで変換中の文字が消えず (直後も marked が残る)、置き換え範囲が無視されたまま。`setMarkedText("")` に変えて直った。違う・読めないときは従来どおり (取り除かない。トレースに残る)。取り除いても変換中の文字は、直後の edit が運ぶので消えない。
- **テスト**: `SessionFacadeTests.AutoCorrect_EditIsAlwaysFollowedByTheEditThatCarriesTheComposingText` (確定し直しの edit の直後に `deleteBefore = 0` の edit が続く約束を固定。確定し直しが 3 回以上出ていることも確かめる)。524 件。Swift 側は自動テストが無い。
- **査読** (R12・R13): P0・P1 なし (R13 は `insertText("")` 版を査読。`setMarkedText("")` への変更は査読後で、R14 で変更部分を査読し指摘なし)。R12 の P1 (marked の長さを確かめない) は `lastView.text` との照合で対応。ホストが `attributedSubstring` を返さない・ずれる場合は取り除かず、従来どおり (直らないだけで、文字は消えない)。
- **実機の確認 (2026-10-11、TextEdit、JIS)**: トレース (`MeltypeTraceIMK`、取得後に削除・設定も戻した) で、3 回の確定し直しすべてで「取り除く → 取り除いた後は `selected={n,0} marked={n,0}` → `insertText` の `replacementRange` どおりに置き換わる」を確認。画面は `I love すしがすき`・`New York のてんき`・`Let It Go のうた` が期待どおり (`Ner York` は打ち間違い)。修正前は `I love sushi すしがすき`・`New York no のてんき`・`Let It Go no のうた`。
- **LibreOffice (`org.libreoffice.script`) では確定し直しが効かない (実機のトレース、2026-10-11)**: `I love sushi gasuki` → `I love sushi がすき` (`sushi` が残り、`すし` は出ない)。LibreOffice は入力欄の中身を IME に返さない。トレースの全体で `selectedRange` が `{0, 0}` のまま、`surroundingText` は `before=nil after=nil`、`attributedSubstring(from: {0, 0})` が nil。Swift の層 3 (P22) が「中身が違う」として確定し直しの edit をまるごと見送った (`確定し直しを飛ばす (中身が違う) actual=nil`)。文字は重複も消去もされず、英語と日本語がそのまま並ぶ安全側の結果。キャレットの位置が読めないので、同じ方法では直せない (Chrome・Google ドキュメントなど、同じように読めないアプリも同じ結果になる見込み。未確認)。直すなら、バックスペースのキー送出など別の手段が要るが、誤って別の場所を消す危険があるので、今は限界として残す。
- **未確認**: Hermes のデスクトップアプリなど TextEdit 以外 (Chrome・Google ドキュメント・Slack など) の挙動。`Let  It` (TextEdit で空白が二重)・Hermes の `Let IT Go` (`It` が `IT`) の原因は不明。`'` を含む英文 (`Let's go susi`・`We're here susi`) は Core のテスト用キーボードと実機のキー経路が違い、別件として調べる。

## 追加項目 Q5 (英文の 2 行目以降で、go・here などを後ろの日本語に合わせて直してしまう)

- **現象** (Mac 実機、TextEdit、2026-10-11): 2 行目以降の `We're here susi` の `here` が `へれ` に確定し直される (`We're へれすし`)。1 行目では起きない。P35 (go・so・do・to など) と P27 (you・are・we・me・she) の「直前の語を読んで英文の続きなら直さない」が、2 行目以降で効かなかった。最初の疑い (`'` が原因) は外れで、`'` は関係なかった。
- **原因** (実機のトレースと、コードの読み取り): 2 つ。(1) 自分の確定の記録 `_lastCommitText` は、確定の直後 1.5 秒 (`OwnCommitTrustMs`) は入力欄から読んだ文字より優先して使う。この記録は、アプリに素通しした Enter (変換中でない Enter の改行) を知らず、2 行目の文頭の語が前の行の最後の文字につながって見える (`…すしWe're here `)。(2) 入力欄から読んだ文字 (1.5 秒を過ぎた後) でも、語の前が「空白か文頭」でないと読まない規則で、改行・タブの直後 (行頭) を読めなかった。テスト用キーボードは文書の先頭から打つので見つからなかった。
- **修正** (`CompositionController.cs`・`MeltypeSession.cs`、約 25 行): (a) `StaysEnglishAfterFunctionWord` で、語の前が改行・タブ (`\n` `\r` `\t`) でも読む。(b) アプリに渡したキー (変換中でない Enter・Tab、修飾キーなし) のとき `NoteBreakKeyPassedToApp` で、`_lastCommitText` (最大 20 文字) に `\n`・`\t` を足す。BackSpace なら、記録の最後が改行・タブのとき 1 文字戻す。
- **テスト**: `SessionFacadeTests.AutoCorrect_EnglishSentenceOnTheSecondLine_IsKept` (Enter 2 回の後の `Please take susi`・Tab の後・BackSpace で消した改行)、`CompositionTests.AutoCorrect_ShortEnglishWordInsideEnglishSentence_AtTheStartOfALine` (入力欄から読む経路: 行頭の `We're`・`We’re`・`Are you`・`\r\n`)。526 件。修正前に赤、BackSpace の巻き戻しを外す変異で赤を確認。`--eval-json` は 1242/1244 のまま。
- **査読** (R15、変更前後を約 4,600 件で比較): P0 なし。改善は約 120 件 (2 行目以降の英文が英語のまま残る、英語の行の後の日本語行が壊れない、英語の前後の空白を入れる設定のとき改行直後の余計な空白が消える、変換エンジンに渡す文脈が前の行でなく空になる)。日本語の行 344 件は、すべて単独で打った結果と一致した。指摘 (BackSpace で改行を戻さない・修飾キー付きの Return/Tab でも足す・テスト不足) は修正・追加した。
- **トレードオフ (受け入れた退行、約 40 件)**: 英語の行頭が、あいさつの一覧に無い語 1 つのあとに do・me・go・to などが続く形 (`When do we go`・`Okay so we are done`・`How do you do`・`Let me go`) は、日本語に直される。変更前は、記録が前の行と語をくっつけた形 (`…goHow `) になっていて、2 語の英文と読めたのは偶然だった。変更後は「行頭 = 新しい文」として、1 行目と同じ判定になった (入力欄から読む経路とも同じ)。英語 80 文のうち、変更前に助かっていて壊れたのは 2 文だけで、その 2 文は単独で打っても変更前から壊れる。
- **限界**: 改行で折り返した英文の続き (`I want to⏎go home`・`I think we⏎should go home`) は、行が変わるので別の文と見なされる (変更前でも 1.5 秒以上待てば同じ)。`Are⏎you susi` は改行をまたいで `Are` を読まない。改行をまたいで読めば折り返しは直るが、`Hi⏎so desune` のような日本語行が壊れるので、現状維持。行頭がタブの英文 (`\tI want to go susi`) は、最初の確定の時点で英文と読めない (`IsEnglishSentence` が空白でしか語を区切らない。変更前から)。空白設定を ON にして `What do you want` を再確定すると `you  want` と空白が二重になる既存の不具合は、直していない。
- **実機の確認 (2026-10-11、JIS)**: TextEdit は 5 行すべて期待どおり (`Hi there`・`We’re here すし`・`Let’s go すし`・`Please take すし`・`I want to go すし`。TextEdit は `'` を `’` にする)。Hermes のデスクトップアプリ (直線の `'` から推定) は、`We're here すし`・`I want to go すし` は期待どおりだが、`Let's ごすし`・`Please take たけすし` が直らず、`Hi there` の `i` が大文字になる (`HI there`)。以前の `Let IT Go` と同じ型で、Hermes 固有の可能性。いずれも原因は未調査 (ログ未取得)。
- **Hermes のログで分かったこと (2026-10-11)**: Hermes のチャット欄は Enter が送信で、改行は Shift+Enter (ログの改行の Enter はすべて `flags=131072`)。初版の修正 (R15 の指摘で `!modifier && !shift` として Shift 付きを除外) が、Shift+Enter の改行を記録に入れず、`take` の確定から 1.5 秒以内に次の語を打つと `Please take susi` が `たけすし` になった (`Let's go susi` は間が空いて入力欄から読む経路になり、直っていた)。修正: Shift+Return も記録に足す (`MeltypeSession.HandleKey` は `!modifier` だけで除外し、Shift+Tab は `NoteBreakKeyPassedToApp` の中で足さない)。テストは Shift+Return の直後の行頭を追加 (旧条件に戻すと赤)。査読 R17: P0・P1 なし。誤って足したとき (Shift+Enter が何もしない・送信だけのアプリ) の害は、改行されないまま 1.5 秒以内に同じ欄へ続けて打ったときの語の誤判定程度で、足さない害 (実機で確認) より小さいと判断した。「Shift+Enter で必ず改行が入る」とは言えないので、確認できたのは Hermes だけ。`HI there`・`Let IT Go` の大文字は、ログでは Shift の押下が `H` にだけあり、再現しなかった (Shift を離すのが遅れた打ち方の影響と見ている。未確認)。
- **Hermes の再確認 (2026-10-11)**: Shift+Enter で改行して打った `Hi there`・`Let's go すし`・`Please take すし` が期待どおり。
- **未確認**: Hermes の上記 3 件の原因。Hermes の `We'うぇへれすし` (最初の報告) の原因。NSSpellChecker での比較は行っていない。

## 追加項目 P36 (ローマ字のまま確定した文を、選んで再変換して日本語にする: 本家 #332 の改良導入)

- **現象**: Meltype が止まっていたとき・ターミナルなどでローマ字のまま確定した文を選んで Shift+Space を押しても、日本語にならない。英語が混ざった文 (`asita no meeting de review shimasu`) は何も起きず、日本語だけの文は OS の読み (CFStringTokenizer) で `きょうはいいてんきですね` に戻るだけで、Meltype の判定 (誤字の直し・英語の語を残す) は通らない。
- **今の挙動 (実測、本家の説明にも無い)**: 英文・URL・メールを選んで Shift+Space を押すと、OS の読みがでたらめなかなに変換されて候補が出る (`this is a pen` → `てひせぃさぺん`。Esc 2 回で戻る)。今回は直していない。
- **本家 #332 の取り込み可否**: `gh pr diff 332` は当たらない (Windows の `GetReconversionSelection` が Mac に無い)。考え方 (逆変換ではなく、空白区切りで語ごとに判定し、日本語の所だけを日本語にして候補に出す。Enter で置き換え、Esc でやめる) を手で移した。本家の条件のまま (自作で再現) だと、英文を選んだ誤操作で壊れた候補が出る (英文 489 件中 403、コード・URL 89、英文+日本人名 175)。
- **採用案**: 「日本語だと言える所があるときだけ」候補を出し、無ければ今までの再変換に戻す。
  - `MeltypeSession.ReconvertCore`: `IsRomajiText(text) && _controller.ReconvertRomaji(text)` なら完了。偽なら従来の経路。
  - `CompositionController.ReconvertRomaji`: 空白区切りで語ごとに英語/日本語に分け (前の語が英語なら手がかりにする)、日本語の区間だけをかなにして変換エンジンへ。英語は打ったとおり残す。打ち間違いは直す (設定が OFF なら直さない)。候補は 日本語にした文 → 選んだままの文。Esc / BackSpace で選んだ文字に戻る (`CancelRomajiReconversion`)。
  - **日本語だと言える所** (`IsRomajiEvidence`): 英字と `-` だけの綴りで、かなが 2 文字以上、3 文字以上、既知の英単語・曖昧な語ではない。**大文字で始まる語と、単独の敬称語 (san・kun・chan・sama) は証拠にしない (v2)**。または小文字の英語の語に助詞が続く (`sdk|to`)。証拠で日本語にする区間は、全体の 1/4 以上であること。
  - コード・URL・パス・メール・`iPhone` 型の語は英語のまま。日本語にした文は学習しない。直前の確定語の「確定し直し」を止める。
  - **Swift の変更は 1 か所**: 読めた文字列の長さ (UTF-16) が選択の長さと違うホストでは何もしない (読んでいない後ろの文字まで置き換えるのを防ぐ。査読 R19 の指摘。既存のかなの再変換にも効く)。
- **v2 の経緯 (査読 R18b)**: 初版は、名前の直後に句読点が付く英文 (`Thanks, Kenji!`・`Hi Tanaka!`・`Dear Kenji,`) で名前が証拠になり、日本語の候補が出ていた (実装者の入力に句読点が無く見えていなかった。NS で 61+21+348 件)。大文字始まりを証拠にしない規則で 0 件になった。比較した案: A 単独 (敬称 `Takeshi san` が残る)、A2 文頭の語を例外 (名前が文頭の英文が 221/520 誤爆)、B ハイフン敬称を例外 (`Thanks, Tanaka-san!` が 718/1280 残り、候補も `たなかーさん`)、C1 ハイフン入りを除く (日本語を落とす損が大きい)。採用は A + 単独の敬称。
  - 文頭だけ大文字の日本語文 (`Kyou wa ...`・`Ohayou gozaimasu`) は、もともと語ごとの判定で英語になり証拠ではないので、落ちる件数は変わらない (査読 R20 でも自作の約 3,900 件で確認)。
- **テスト**: 526 → **544 件、全て通る**。`ReconvertRomajiTests` (新規): 本家の 3 文、英文・URL・パス・メール・コマンド・人名・英文+名前+句読点を「今までどおり」で固定、Enter / Esc / BackSpace / Shift+←→、学習しない、直前の確定語を確定し直さない、割合と文字数の境界 (v2 で追加)。変更前の Core で流すと日本語にする側が赤。変異テスト 25 通り以上すべて赤 (査読 R20 でも 18 通りを独立に確認。緑のまま残るのは等価変異 `Raw.Length>=2` と、数字+大文字語の `2Kenji` 型のみ)。`--eval-json` は 1242/1244 のまま。
- **前後比較** (約 12,000 件に、査読で自作の約 7,500 件。BuiltIn と NSSpellChecker): 日本語のローマ字文 919/920、英語混在文 524/524 で日本語の候補が出る。英文・コード・URL・英文+日本人名は 0 (句読点付きの名前も)。かな・漢字を含む選択は差分 0。従来の再変換が拾っていたものが拾われなくなる退行は 0。
- **得と損**:
  - 得: 英語の `to`・`so`・`on` が日本語にならない。人名を含む英文を壊さない。打ち間違いを直す。日本語にした文を学習しない。
  - 損 (v2 で候補にしなくなった型): 大文字で始まる名前・地名・`Tanaka-san` だけの選択 (`Tanaka-san` → `たなかーさん`、`Shibuya!`、`Sumimasen.` など)。候補自体が壊れているものが多い (査読 R20: 大文字 1 語+句読点の挨拶 19/281 は失う候補が正しい)。今までの再変換 (履歴・OS の読み) に戻る。
- **限界 (直していない)**:
  - 小文字の英語の docstring (`This is a one-time setup:`・`Bind fun to mouse-click ...`): NS で 1,600 件中 13、BuiltIn 34。
  - 語末が `to`・`do`・`o` の技術語 (`proto`・`inode`・`dino`): NS 755 件中 25、BuiltIn 95。`pro|to` と `sdk|to` は文字の並びだけでは区別できない。
  - 辞書に無い英単語・固有名詞を含む長い英文 (BuiltIn で 735 件中 24)。
  - **小文字で打った名前の英文** (`thanks, yui!`・`hi naoki`): NS で 150 件中 113 (査読 R20)。大文字の規則の範囲外。
  - NS では `Tanaka Makoto san ga kimasu` が `Tanaka Makoとさんがきます` になる型 (NS が `mako` を英単語と答える)。
  - 文頭が大文字の日本語文は先頭の語が日本語にならない (`Kyou wa genki desu` → `Kyou わげんきです`)。もともと同じ。
  - 200 文字を超える選択、改行・タブ・全角を含む選択、直接入力 (英数) のときは従来どおり。コード向けアプリは日本語入力 (「あ」) にしてから押す。
  - 以上はすべて、候補が出るだけで Enter を押さなければ元のまま (Esc 1 回で戻る)。
- **Swift・実機の注意点 (査読 R19、読解のみ)**:
  - 入口は `!hasMarkedText` なので、Q4 型 (変換中の文字が残ったまま範囲を渡すと無視される) は入口には当たらない。Enter の確定は範囲なしの `insertText`、Esc は範囲なしの書き戻しで、かなの再変換 (2 回目の Esc) と同じ形。
  - 暗黙の確定 (フォーカス外れ・英数キー・モード切替) では、Esc と違い、日本語にした候補が選択を置き換えて残る。かなの再変換と同じ挙動で、一貫はしているが、誤爆した候補が暗黙に残る損もある。変えていない。
  - 候補を出している間にクリックで別の場所へ移ったときの挙動は、ホストごとに違う可能性があり、読解では確定できない (通常の変換にもある型)。
  - LibreOffice は選択を返さない (`selectedRange` が常に `{0,0}`) ので、再変換が始まらない (何もしない)。
  - 既存のかなの再変換 (P9) の実機確認の記録が計画書に無い。「かなの再変換と同じ経路だから動く」には実績の根拠が無い。
- **未検証**: Mac 実機での選択・候補表示・Enter での置き換え・Esc での復元 (TextEdit・Hermes)。azooKey による漢字化 (比較は偽の変換エンジン)。ユーザー辞書・学習した変換が候補に反映されること。実際に選ぶ文字列の分布 (自作入力のみ)。Windows・Linux は対象外。

### P36 v3 (実機確認での 2 件の修正)

- **実機 (TextEdit) で見つかった 2 件**: (1) アプリは英数で打つと文頭を自動で大文字にするので、ローマ字のまま確定した文は先頭 1 文字だけ大文字になりやすい。選んで Shift+Space すると `Asita no meeting de review shimasu` → `Asita の meeting で review します` (先頭の語だけ英語のまま)。(2) 英文 `Thanks, Kenji!` を選んで Shift+Space すると、ローマ字の再変換が false を返したあと従来の再変換に落ち、OS の読み (CFStringTokenizer) が英文をでたらめなかなにして `手ハンクス感じ` の候補が出る。
- **修正 1 (Core、`CompositionController.SplitRomajiText`)**: 文頭の語 (と、`. ! ?` で終わる語の次の語) が「先頭の 1 文字だけ大文字」(`Asita` `Kenji,` `Tanaka-san`。全部大文字の `PR` と 1 文字の `I` は対象外) のとき、小文字に直して判定・変換する。英語として残る語は打ったままの大文字に戻す (`Hello no meeting …`)。**この語自体は日本語の証拠 (hasEvidence・助詞の証拠) に数えず、日本語の割合の分子にも数えない** (名前が文頭の英文 `Kenji, can you send it?` と、頭が make・run・you の英文を候補にしないため)。`Asita no meeting de review shimasu` → `あしたの meeting で review します`、`Kyou wa genki desu` → `きょうわげんきです`、`Kenji ni onegai shimasu` → `けんじにおねがいします`、`Kyou wa genki desu. Asita mo ganbarimasu.` → `きょうわげんきです。あしたもがんばります。`。
- **修正 2 (Core、`MeltypeSession.ReconvertCore`)**: ASCII のローマ字の選択で、空白を含む (複数語の) ものは OS の読みを使わない (候補が出ず、Swift は通常のキー処理に進む)。履歴・意味辞書の読み、かな・漢字の選択 (漢字の複数語も) は従来どおり。空白の無い 1 語 (`Takeshi` → たけし) は従来どおり OS の読みも使う。Swift は変更なし。
- 比較した案と数字、得と損、残る限界は `P36-v3-notes.md` (作業用フォルダー) にあり、要点は下の通り (v2 = HEAD との前後比較。NSSpellChecker 相当 (NS) / 同梱の英単語表 (BI)。重複を除いて約 4 万件。数字は NS、BI も同じ向き。
  - 英文 (Meltype の候補が出るのは誤爆): 新しく出る 0 件、出なくなる 0 件 (英文型の合計。ZR の docstring は 13→13、ZE・ZL・RE も同じ)。従来の OS の読みのでたらめ候補は 14,803 件 → 370 件 (消えた 14,433 件。残る 370 件は空白の無い 1 語の選択 = `Tanaka!` や 1 語のコード)。
  - 日本語 (JA 群): Meltype の候補が新しく出る 0 件、出なくなる 9 件 (各語が大文字の Title Case だけ。BI は 3 件)、OS の読みの候補から Meltype の候補に変わる 18 件 (`Ashita made iku` など)、候補の中身が変わる (先頭の語が日本語になる) 6,048 件。判断が割れる群 (MX) で Meltype の候補が出なくなる 4 件は `Kobayashi-chan desu` 型 (名前+敬称だけが証拠だった)。
  - **損**: (a) 文頭の語を小文字にすると、辞書に無い英語の固有名詞・製品名が文頭にある日本語文で、その語がかなになる (`Node de deploy shimasu` → `のでで deploy します`、`Homebrew` → `ほめbrew`、`Mercari` → `めかり`)。製品名 678 語 × 4 文で 7 語 28 件 (うち `Honda` `Toyota` `Nikon` `Rakuten` は日本語として妥当)。主な製品名は固有名詞辞書で守られている。(b) OS の読みの候補は、Meltype が日本語と言えない複数語の日本語にも出なくなる: 全部大文字で打った日本語 (`KYOU WA GENKI DESU` → きょうわげんきです)・`Hiroshi san` `Mika-chan ni` のような名前+敬称だけ・`ii ne` `sou ka na` のような短い語。今回の日本語 (JA) 群で 2,996 件、判断が割れる群 (MX) で 4,440 件 (JA のうち全部大文字 919・Title Case 900、MX のうち境界コーパス 3,169・名前+敬称 約 1,100 を含む)。(c) 1 語の英単語の選択は OS のでたらめな読みが残る (`hello` → へっろ)。
  - 却下した案: 文頭の語を証拠にも数える (名前が文頭の英文 348/520、Tanaka-san が文頭の英文 468/600、2 文目が名前の英文 181/780 が誤爆)。複数語でも「全部の語が日本語に見える」ときは OS の読みを残す (`Hi Tanaka!` 型の挨拶+名前 約 600 件が英文なのに残る)。1 語でも既知の英単語なら OS の読みを使わない (1 語の選択 1,709 件のうち英単語 804 語中 724 語のでたらめ候補が消える一方、`Suzuki` `Sakura` `tansu` `futon` など約 30 語の正しい読みも消える)。
- **テスト**: 544 → 549 件。`ReconvertRomajiTests` に 5 件 (文頭大文字の日本語化・英文の文頭が名前や曖昧な語のとき何も起きない・OS の読みを使わない・履歴・意味辞書・かな・漢字は従来どおり・1 語は OS の読みを使う)、既存の英文テスト `Reconvert_EnglishWithCapitalizedName_IsLeftAlone` に 5 文を追加。変更前の Core で赤を確認 (文頭大文字の日本語化と OS の読みの 2 本)。変異テスト 15 通りで赤を確認 (`P36-v3-notes.md`)。
- **未検証**: Mac 実機 (TextEdit・Hermes) での確認。Swift は変更していない (`consumed=false` のとき Shift+Space が通常のキー処理に進むことは `InputController.swift` を読んだだけで、実機では見ていない)。実際に選ぶ文字列の分布 (入力は自作)。Windows・Linux は対象外。
- **v3 の査読 R21 の結果と対応 (2026-10-12)**: P0 なし。文頭の小文字化は、英文約 4,600 件 (BuiltIn・NS) で、候補が新しく出る・出なくなる件数が 0。
  - **P1 (直した)**: 英文を選んで Shift+Space を押し、Core が「読めない」と返すと、キーがアプリへ渡り、アプリが選択を空白で置き換えて選んだ文字が消える (`NSTextView` に直接送って確認。Cmd+Z で戻る)。以前は OS のでたらめな候補が出て Esc で戻せたが、v3 で黙って消える挙動になっていた。Swift で、選んだ文字を読めて長さも合ったのに再変換できなかったときは、キーを飲み込んで何もしない (`InputController.handle`)。トレースに `再変換できない選択: Shift+Space を飲み込む` が出る。**実機 (TextEdit・Hermes) は未確認**。
  - **P1 (直していない)**: 非 ASCII を含む英文 (全角空白・`“”`・`—`・`…`) と、複数行の英文は `IsRomajiText` が偽になり、OS の読みのでたらめ候補が出続ける (`“Hello” world` → `はろおをるるで`)。macOS のスマート引用符で起こりうる。候補が出るだけで Esc で戻せる。
  - **OS の読みを切った損**: 複数語の日本語ローマ字 690 件 (`ii ne`・`ni wo ga`・`kyou desu`・`Hiroshi san`・全部大文字・Title Case) のうち、変更前に OS の読みで候補が出ていた 497 件が、変更後は候補なしになる。出なくなった候補のうち OS の読みが正しかったのは約 84% で、残りは `n` の後に子音が続くと `っ` になる誤読 (`gomen ne` → `ごめっね`)。英文のでたらめ候補 約 14,400 件が消える得と比べて許容した。低コストの代案 (複数語でも英語の語が 1 つも無いときだけ、Meltype 自前のローマ字→かなを読みに使う) は未実装。
  - **P2**: もともと誤爆していた英文の中身が変わる (`Mon あみえ` → `もんあみえ`)。変異 M1 (`,` も文末扱い)・M5 (`first` の条件) がテストで赤にならない (影響は小さい、M5 は等価変異の疑い)。
- **v3 の続き: 英文で OS の読みを使わない条件を広げた (2026-10-12、実機の TextEdit の報告から)**: 最初の TextEdit では `Thanks, Kenji!` が変換され、あとでは変換されなかった。プロセスは入れ替え直後に新しい版で起動していて (古い版が残っていた説は不成立)、Core の状態 (直前の入力・履歴・再変換の順序) では結果が変わらなかった (再現)。一方で、**選択に末尾の改行 (TextEdit で段落を 3 回クリックすると付く)・全角空白・`“ ”`・`—`・日本語の行が混ざると、英文でも `IsRomajiText` (ASCII のみ) が偽になり、OS のでたらめな読みに落ちる**ことを再現した。最初の確認がこの選び方だったかは未確認 (推測)。
  - 修正: `MeltypeSession.IsLatinMultiWordText` (英字を含み、かな・漢字・半角かなを含まず、前後を除いた中に空白・改行・タブがある) のとき OS の読みを使わない。空白の無い 1 語 (`Takeshi`、前後に空白・改行があっても同じ) と、かな・漢字を含む選択は従来どおり。テスト 2 件追加 (551 件)。査読 R22: P0 なし、ロジックは正しい。境界テストの穴 (かな・漢字の範囲の端の変異が赤にならない) を指摘され、範囲の端 10 か所とかな・カタカナ・漢字の単独を足した。範囲の端を 1 つずつずらす変異 10 通りがすべて赤。
  - 限界 (P2): 全角英数だけの英文・互換漢字・拡張 B 以降の漢字を含む選択は OS の読みに落ちる。末尾に改行が付いたローマ字の日本語文 (`Kyouha iitenki desu ne\n`) はローマ字の経路 (`IsRomajiText` は ASCII のみ) に入れず、候補が出ない (以前は OS のでたらめな読み)。将来 `IsRomajiText` の前に前後を除けば直せる。`ー`・`・` が英文に混ざると日本語扱いで OS の読みを使う (変更前と同じ)。

- **Mac 実機の確認 (2026-10-12、JIS、TextEdit、`main` の 17:20 ビルド)**: 英数で打って確定し (文頭は TextEdit が自動で大文字にする)、日本語入力に戻して選び、Shift+Space。`Asita no meeting de review shimasu` → `あしたの meeting で review します` (Enter で 1 回だけ置き換わる・Esc で選んだ文に戻る)、`Kyou wa genki desu. Asita mo ganbarimasu.` → 2 文とも日本語の候補、`Thanks, Kenji!`・`Hello, my name is Ryo.` → 候補が出ず文字も消えない (ドラッグで選んだ場合も、行の左余白で 3 回クリックして選んだ場合 = 末尾に改行が付く も)、`今日は` → 従来どおり読みに戻して変換。すべて期待どおり (リョウさんの報告)。続けて確認した結果もすべて期待どおり: 行の途中・文書の先頭・Shift+Enter のあとの行 (前後の文字が残る)、候補を出したままのクリックと Cmd+Tab (候補が確定して元の場所に 1 回だけ残り、IME も固まらない。TextEdit)、Hermes のチャット欄 (候補・置き換え・`Thanks, Kenji!` で文字が消えない)、LibreOffice (何も起きず文書が変わらない)。**未確認**: Chrome・Safari・Google ドキュメント・Slack などの他のアプリ。
- **v3 の続き: 選んだローマ字の文の前後に改行・タブが付いていても、日本語の候補を出す (2026-10-12、P36c)**: 上の「英文で OS の読みを使わない条件を広げた」の限界 (末尾に改行が付いたローマ字の日本語文 `Kyouha iitenki desu ne\n` は `IsRomajiText` (ASCII のみ) が偽で、候補が出ない) を直した。TextEdit で段落を 3 回クリックして選ぶと末尾に改行が付く。Windows 系の文書は `\r\n`、表計算のセルを選ぶとタブが付く。直前の修正 (77775fc) で英文は OS の読みを使わなくなったので、この選び方の日本語のローマ字は「何も起きない」状態だった。
  - **修正** (Core のみ。Swift は変えない: 選択範囲全体を `setMarkedText` の置き換え範囲にしているので、Core が返す候補に改行が含まれればよい):
    - `CompositionController.TrimLineEdges(text)` を追加: 選択の前後の **空白・`\n`・`\r`・`\t`** の 4 文字だけを (前, 本体, 後) に切り離す。
    - `ReconvertRomaji` は本体だけを判定・変換し、候補の先頭 (日本語にした文) の前後に切り離した文字をそのまま付け直す。2 番目の候補と、Esc / BackSpace で書き戻す元 (`_reconvertOriginal`) は、選んだ文字そのまま (改行を含む全体)。Enter は選んだ範囲全体を置き換えるので改行が残る。空白の付け直しは、以前の `TrimStart(' ')` / `TrimEnd(' ')` と同じ結果。
    - `MeltypeSession.ReconvertCore` は、ASCII のみの門 (`IsRomajiText(text) &&`) を外して `ReconvertRomaji` に任せる (門は本体に対して `ReconvertRomaji` の中で効く)。`IsRomajiText` 自体は変えない。
  - **安全側にした所**: (1) 選択の途中に改行・タブがある (複数行・複数セルの選択) は、`IsRomajiText(本体)` が偽のまま = 今までどおり候補なし。(2) 切り離すのは上の 4 文字だけ。全角空白・垂直タブ・改ページ・NEL・U+2028/2029・NBSP は切り離さず、今までの判定に任せる (広げると候補が出る側に変わるが、実機のどのアプリがどの文字を付けるか未確認で、確信が無いものは今までに戻した)。(3) 前後を切り離しても、判定の門 (日本語だと言える証拠・助詞・日本語の割合 0.25) は本体にそのまま効く。英文 `Thanks, Kenji!\n`・`this is a pen\r\n`・コード・URL・パスは候補なしのまま、OS の読みも使わない。(4) 空白・改行・タブだけの選択 (`\n`、` \r\n\t `) は、本体が空なので今までどおり何もしない。(5) 200 文字の上限は、前後の改行を含めた選択全体で見る (Swift も選択全体の長さで弾く。本体が 200 以下でも改行を足して 201 になると出ない。ちょうど 200 は出る)。
  - **前後比較** (改行付きの入力を足した P36 の比較入力 = 8 グループ 3,775 本体 × 8 通りの付け方 (なし・末尾 `\n`・先頭 `\n`・末尾 `\r\n`・末尾 `\t`・` \n`…` \r\n `・前後 `\t`・前 `\n` 後 `\n\n`) + 途中に改行 / タブを入れた 2 通り = 36,628 件。HEAD (base) と修正後 (new) を、同梱の英単語表 (BI) と NSSpellChecker 相当 (NS) の両方で流した。道具は P36 の `regress_rc.py` を複製して改行を `⏎ ␍ ⇥` で渡せるようにしたもの。数字は件数):

    | グループ (本体の数) | 本体で候補が出る (base = new) | 改行付き: new で候補が出る (BI / NS) | 改行付き: base で候補が出る | 改行付きの結果が「本体の結果に前後を付けたもの」と一致 |
    | --- | --- | --- | --- | --- |
    | A 日本語ローマ字 (920) | 919 | 6,433 / 6,433 (919 × 7) | 0 | 6,440 / 6,440 |
    | C 混在 (524) | 524 (NS 523) | 3,668 / 3,661 | 0 | 3,668 / 3,668 |
    | B 英文 (489) | 0 | **0** / **0** | 0 | 3,423 / 3,423 |
    | I 英文 + 人名 (336) | 0 | **0** / **0** | 0 | 2,352 / 2,352 |
    | D コード・URL・パス (237) | 0 | **0** / **0** | 0 | 1,659 / 1,659 |
    | E かな・漢字 (356) | 0 | **0** / **0** | 0 | 2,492 / 2,492 |
    | M 実際の英文 (281) | 2 (NS 1) | 14 / 7 (= 本体の 2 × 7 / 1 × 7) | 0 | 1,967 / 1,967 |
    | N 人名 (632) | 368 | 2,576 / 2,576 (368 × 7) | 0 | 4,424 / 4,424 |

    - (i) 改行の無い本体 3,775 件と、途中に改行 (3,214 件) / タブ (3,214 件) がある入力は、base と new の **差分 0** (BI・NS とも)。
    - (ii) 改行付きの日本語・混在文 (A・C・N) は、候補の表示・候補の並び・Enter の確定・Esc の書き戻しの全部が、本体の結果に前後を付けたものと一致 (「期待と違う」0 件)。base は改行付きでは 1 件も候補が出ていなかった。
    - (iii) 改行付きの英文・コード・かな (B・I・D・E = 1,418 本体) は、候補が出た 0 件、base と結果が同じ (OS の読みを使わない挙動も含む)。M の 14 件 (BI) / 7 件 (NS) は、改行無しでも出ていた P36 の既知の限界 (`License notice for xoshiro RNGs ---…`、`This is free and unencumbered software.` のように、辞書に無い英単語を含む英文の docstring) で、改行を付けても同じ結果のまま (新しく増えた誤爆ではない)。
  - **テスト**: 560 → **566 件** (全通過)。`ReconvertRomajiTests` に 6 件 (前後の改行・タブ・空白付き 7 文で、候補の表示・候補の並び・Enter が改行を残す・Space の巡回 (選んだままの文 ↔ 日本語にした文)・Esc / BackSpace が元の改行付きの文字を 1 回だけ書き戻す・Shift+←→ が区切りを変えない / 6 種類の付け方 × 10 の本文が、改行無しの結果に前後を付けたものと一致 / 途中の改行・タブ、改行・空白だけ、全角空白・垂直タブ・U+2029 付き、200 文字の上限 / 英文・コードに改行を付けても候補なし (辞書なし・同梱の英単語表) / 空白の無い 1 語は改行を付けても付けなくても同じ / `TrimLineEdges` の境界)。実装前のコードで 4 件が赤 (英文の守りのテストは元から緑 = 守りとして残す)。`--eval-json` は **1242/1244** で、変更前と JSON が完全に一致。
  - **変異テスト 18 通りのうち 17 通りで赤** (作業コピーに 1 つずつ入れ、全テストを流した): `\r`・`\t`・`\n`・空白を切り離さない (各 赤 3〜5 本)、全角空白・U+2029・垂直タブも切り離す (広げる。各 赤 2 本)、後ろだけ / 前だけ切り離さない (6 本 / 4 本)、終端の境目 `end > start` → `end > 0` (赤 1 本)、候補の先頭に前 / 後ろを付けない (各 3 本)、Esc の書き戻しを本体だけにする (1 本)、2 番目の候補を本体だけにする (1 本)、判定 / 変換を切り離す前の全体で行う (4 本 / 3 本)、`MeltypeSession` に ASCII のみの門を残す (4 本)。**生き残り 1**: 変換結果が選んだ文字と同じなら出さない既存の比較 (`converted == text`) を `converted == core` に変える変異。日本語の区間が必ず ASCII でないかなになるので、この比較が真になる入力を作れない (等価変異の疑い。既存の行で、今回の変更では触っていない)。
  - **得と損**: 得: 段落の 3 回クリック・`\r\n`・表計算のセルのように、実際に人が選ぶ形で日本語のローマ字が再変換できる (以前は OS のでたらめな読み、直前の修正以降は何も起きなかった)。置き換えで改行が落ちない。英文・コード・複数行の選択の挙動は変わらない (差分 0)。損: (1) 改行を付けて選んだ英文のうち、改行無しで既に誤爆する形 (辞書に無い英単語を含む docstring 風の英文) は、改行付きでも同じく誤爆する (増えた分は新しい失敗ではないが、誤爆する入力の範囲は「選び方」の分だけ広がる。結果は候補を Enter で確定したときだけ置き換わる)。(2) 全角空白・垂直タブ・U+2029 などが付いた選択は、直さなかった (今までの判定のまま)。(3) 複数行・複数セルの選択は、日本語でも候補が出ない (安全側)。
  - **限界・未確認**: (1) **Mac 実機で確かめていない** (Core のテストとハーネス = 実機と同じ経路の `MeltypeSession.Reconvert` + `HandleKey` まで)。TextEdit の 3 回クリックで付く改行が `\n` であること、`setMarkedText` の置き換え範囲に改行を含む文字列を入れたときの表示 (下線の付き方・候補ウィンドウの見え方)、Enter で改行が 1 回だけ残ること、Esc で元の改行付きの文字に戻ることは **実機未確認**。Chrome・Safari・Slack などで選択に付く文字 (改行の種類・`\u2029` など) も未確認。(2) 本体が 200 文字以下でも、前後の改行を含めて 201 文字以上の選択は Swift が先に弾く (Core も同じ)。(3) Windows / Linux は対象外。
  - **査読 R27 (独立。実装者とは別の査読員)**: P0 なし。自作の入力 6,600 件弱 (段落 3 回クリックの `\n`・`\r\n`・表計算のセル・先頭の空行・チャットの貼り付け・インデント・箇条書き・`$ ` `> ` `// ` `# ` の接頭辞・署名・URL・パス・コード) を変更前後 × BuiltIn・NS で流し、付けた形 3,871 件はすべて「本体の結果に前後を付けたもの」と一致 (不一致 0)、途中に改行・タブがあるものは新しく候補が出た 0 件、改行の無い入力は差分 0 (約 10,000 件)。実装者の 36,628 件も独立に流し直して一致した。変異は、実装者の 18 通りと重ならない 16 通りのうち 15 通りが赤 (生き残りの 1 通りは空本体の守りで、`SplitRomajiText` が証拠なしで false を返すので等価)。実装者の生き残り (`converted == core`) も、43,066 件で差分 0 で実質等価と確認した。
  - **P1 (実機で確かめる)**: 候補 (変換中の文字) に改行を含めて `setMarkedText` に渡すことの実績がコードにも計画書にも無い。Chrome・Electron (Hermes の入力欄) で、変換中の範囲の中の改行が段落区切りになり、下線が崩れる・確定が早まる、候補ウィンドウの 1 行に改行が入って見え方が崩れる、改行が正規化されると `candidateSelected` の文字列の完全一致が崩れてマウスで選んだ候補が無反応になる、などが起こり得る (キー操作は Core 経由なので影響なし)。**退路**: Swift (または FFI) で前後の 4 文字を先に切り離し、本体の範囲だけを `setMarkedText` の置き換え範囲にする案 (変換中の文字に改行が入らず、Esc・挿入も本体だけで済む。Swift の変更が要り、Core のテストでは確かめられない)。実機で失敗したら、この案に切り替える。
  - **P1 (別件、改行の有無に関わらず既存)**: 箇条書きの `- kyou wa ...` は `ーきょうわ…`、`- [ ] …` は `ー [ ] …`、`1. kyou…` は `1.きょう…` (空白が消える)、`kyou wa - ashita` は `きょうわーあしたわ`、`Tel: 03-1234-5678 desu` は `03ー1234ー5678です` になる (ハイフンが長音になる)。箇条書きは 3 回クリックで選ぶと末尾に改行が付くので、今回の変更で当たる機会が増える。1 番目の候補が選択済みで、Enter で確定すると気づかず置き換わる。今回の範囲外で、別項目として記録する (直していない)。
  - **P2**: Chrome・Safari の行末の NBSP は切り離し対象外なので、日本語のローマ字でも候補が出ない (実機で付く文字を確かめてから決める)。かな・漢字 + 末尾の改行 (日本語の段落の 3 回クリック) は、今までどおり再変換されない (未対応の隙間で、退行ではない)。`TrimLineEdges` に NBSP・U+2028・`\f` を足す変異は単体テストで赤だが、セッション経路での固定は無い (必須ではない)。
  - **実機で確かめる手順 (査読員の提案)**: TextEdit (リッチ・プレーンの両方) で、英数入力で `Kyouha iitenki desu ne` + 改行 + `Asita mo ganbarimasu` を打ち、1 行目を左余白で 3 回クリックして選んで Shift+Space。1 行目が `きょうはいいてんきですね` として下線付きで出て、段落の区切りは見えたまま、候補は 2 つ (2 つ目は元の文字)。Space で 2 つ目の候補 → 元の文字、Enter で置き換わり改行はちょうど 1 つ残る。Esc で元に戻る。候補ウィンドウの 1 行目の見え方 (改行で 2 行に見える・欠ける) と、マウスで候補をクリックして確定できるかも見る。Hermes のチャット欄・Chrome の textarea・LibreOffice でも同様 (下線が途切れない・送信が走らない・Enter で 1 回だけ置き換わる・Cmd+Z で戻る)。英文の行 (`Thanks, Kenji!`) は何も起きず文字が消えないこと、`- kyou wa ...` の行は `ー` で始まる候補が出ることも確認する。

## 追加項目 S2 (配布物に残るビルド時の絶対パスを消す: 実行ファイルに `strip -S`)

- **経緯**: pkg の査読 (`docs/PKG_REVIEW_2026-10.md` の §2) で、配布する Swift 側の実行ファイル 2 本 (`Meltype`・`MeltypeDictionary`) に、ビルドしたマシンのホームフォルダー配下の絶対パス (`/Users/<ユーザー名>/Meltype/mac/.build/…` など) がデバッグ用の記録 (stab) として残っていると分かった (1.1.5 の pkg の展開で 1,020 件)。ダウンロードした人はユーザー名と作業フォルダーの場所を取り出せる。リョウさんの判断 (2026-10-12): **次のリリースのビルドから消す** (公開済みの 1.1.5 は変わらない)。引き継ぎ文書 `docs/HANDOFF-strip-build-paths.md` (別セッションの査読担当が作成) に沿って実装した。
- **修正**: `mac/build.sh` の、RPATH を外すループ (`install_name_tool -delete_rpath`) の直後、署名の前に、`strip -S` で 2 本のデバッグ用の記録だけを取り除く 9 行 (コメントを含む) を足した。通常のシンボル (関数名) は残る。strip するのは `.app` の中のコピーで、`.build` の下の元の実行ファイルは変わらない。**署名より前でなければならない** (署名したあとに strip すると署名が壊れる。査読員が複製で確認)。C# 側の `libMeltypeNative.dylib` は `-p:StripSymbols=true` で既に消えている。Core・テスト・Swift は変えていない。
- **実測** (今回の `build.sh --no-install` の成果物。パスの文字列は出さず件数だけ。`grep -a -o -E '/Users/[A-Za-z0-9._-]+/' | wc -l`):

  | | `Meltype` | `MeltypeDictionary` |
  | --- | --- | --- |
  | `/Users/<名前>/` の件数 (変更前 → 後) | 987 → **0** | 33 → **0** |
  | stab | 88,027 → 0 | 8,214 → 0 |
  | 通常のシンボル | 32,145 → 32,144 | 3,840 → 3,839 |
  | 大きさ (バイト) | 8,670,384 → 7,121,344 | 1,081,920 → 946,560 |

  - `libMeltypeNative.dylib`・`libswiftCompatibilitySpan.dylib` は 0 件のまま。`.build` の下の元の実行ファイルは 987・33 のまま (コピーだけ strip)。`mac/build/Meltype.app` 全体 (Info.plist・資源・辞書データを含む) で `/Users/` を探して 0 件 (査読 R23)。
  - 署名: `codesign --verify --deep --strict` 通過、`Signature=adhoc`・`TeamIdentifier=not set` のまま。RPATH は `/usr/lib/swift`・`@loader_path`・`@executable_path/../Frameworks` だけ。
  - strip した `MeltypeDictionary` の `--self-test` は 121 件 PASS (起動できることの確認)、`tools/check-mac-aot-settings.py` PASS。
  - `strings` は、既定でも `-a` でも、この実行ファイルの stab の文字列を拾えない (strip 前でも 0 件)。数えるには `grep -a -o -E` を使う。
- **査読 R23** (独立): P0・P1 なし。strip の位置 (署名の前。配布用署名 `MELTYPE_MAC_IDENTITY` の分岐も、署名は strip のあと) を確認。strip の前後で、全セクションの大きさ、`__text`・`__const`・`__data`・`__swift5_typeref` のハッシュ、`otool -ov` の ObjC クラス情報 (`MeltypeInputController`)、外部に見えるシンボル、LC_UUID が同じ。消えた通常シンボルは、名前がビルド機のパスの絶対シンボル 1 つだけ。クラッシュログの関数名 (通常のシンボル 18,612 個) は読める。P2: 警告の記述と実際の食い違い (下)、文書の更新、変更前の大きさの数字の違い (手元の `.build` は 8,702,808 で、10:15 のビルドとの違いと見ている。件数と stab の数は一致)。
- **引き継ぎ文書との違い**: 文書は「strip の警告 (署名が無効になる) がビルドのログに 2 回出る」と書いていたが、実際のビルドでは **0 回**。`.build` から `.app` にコピーした実行ファイルはリンカーが付けた署名 (linker-signed) の状態で、その状態への `strip -S` は警告を出さず、署名も有効なまま残る。警告が出るのは、署名済みの実行ファイルに strip したとき (査読員が確認)。`build.sh` のコメントを実際の動作に合わせて直した。
- **得と損**: 得: 配布物 (zip・pkg・自動更新で配る zip) から、ビルドしたマシンのユーザー名と作業フォルダーの場所が消える。`Meltype` が約 1.5 MB 小さくなる。損: ビルド手順が 1 か所増え、「署名より前」という順序の制約が付く。デバッグ用の記録が無くなる (クラッシュログの関数名は、通常のシンボルが残るので読める)。
- **未検証**: strip した `Meltype` (IMK として) の実際の起動 (査読員は、実機の Meltype と IMK の接続名が衝突するおそれがあるため、複製を起動せず、strip の前後の静的な比較にとどめた) → **実機の入れ替えと入力で確認する**。`llama.framework` が入るビルドの Frameworks は strip の対象外 (今回のビルドには無い)。次のリリースの zip・pkg で、件数が 0 になっていること (P38 の検査項目 `docs/PKG_REVIEW_2026-10.md` §7 の 6 の期待値を 0 にできる。1.1.5 は 1,020 件のまま)。

## 追加項目 P37 (Swift の確定し直しの照合: 本家 #86 の考え方。調査の結果、Core のテストだけ足した)

- **経緯**: 調査文書 (`UPSTREAM_PR_SURVEY.md` の #86) は「Swift の `apply` は `original` (消す予定の文字列) が nil だと照合せずに消す。nil になるのは長さ不一致のときだけで、数行で最後の穴を塞げる」とした。実装の前に、Core で `deleteBefore>0` の edit が生まれる経路と `original` の有無を調べた (調査員、2026-10-12)。
- **分かったこと**: `DeleteBackward` の呼び出し元は Core に 2 か所だけ。
  - (a) `CompositionController.CorrectPreviousCommit` (確定し直し): 2 引数版 (expected あり)。`original` は必ず付き、長さも `deleteBefore` と一致する。
  - (b) `SwitchHeldToJapanese` (英数状態の判定で、判定を待たずにアプリへ送った英字を消して、変換ボックスに入れ直す): 1 引数版で expected が無い。**`original` が nil になる唯一の出どころはこの (b)** で、調査文書の「長さ不一致のときだけ」は不正確だった。
  - (b) は `DirectMode`・`ClassifyDirect`・`DirectDecided` を配線した Windows (`TrayApplicationContext`) 用。Mac の `MeltypeSession.CreateDefault` はこれらを設定しないので、**Mac では通らない** (Mac の「英数」とコード向けアプリは `session.Direct = true` で全キーを素通しする)。`IMPROVEMENT_PLAN.md` の「Windows 版の `DirectModeAutoDetect` はこの項目では入れない (P11 で検討)」とも合う。
- **実測** (Mac の本番と同じ配線。コーパス 5,040 行と、2,340 行のテンプレート文を、1 行・2 行連結・`before` あり・文頭大文字などで流した): 確定し直しの edit は 3,600 件超、`original` が nil のものは **0**、長さ不一致も **0**。仮に `DirectMode` を Windows と同じに配線すると、(b) の edit がコーパスで 1,883〜2,586 件出て、そのすべてで `original` が nil。
- **案の比較**:

  | 案 | 得 | 損 |
  | --- | --- | --- |
  | A Swift で `original` が nil なら飛ばす | 将来 nil の経路が増えても fail-closed。今の Mac で壊れる経路は 0 件 | 今の Mac では効果が無い。Swift は自動テストが無く、実機確認が要る |
  | B (b) にも expected を添えて、すべての edit に `original` を持たせる | 不変条件が完全になる | Mac で使われない経路のために、消す数が 1 つずれる問題 (現在のキーはまだアプリに入っていない) とアプリの自動大文字化への対処まで直す必要があり、検証手段が無い |
  | C 見送り | 工数ゼロ | 不変条件が文書にしか無い |
  | D 「照合不要」の印を FFI に足す | (b) を Mac で許可できる | JSON・Swift・Core の 3 か所を変える。(b) が Mac で使われるまで不要 |
  | **E Core にテストだけ足す (採用)** | Mac の本番経路で不変条件を固定できる。Swift は変えず、実機確認が要らない | Core の新しい経路が nil を作っても、テストの入力に当たらなければ検出できない |

- **採用 (E)**: `SessionFacadeTests.AutoCorrect_EveryDeletingEditCarriesOriginalOfTheSameLength` を追加した。10 文を流し、確定し直しの edit が 5 件以上出ること、`deleteBefore>0` の edit がすべて `original` を持ち、長さが `deleteBefore` と同じであることを確かめる。変異 2 通り (確定し直しを 1 引数版にする・消す数を +1 にする) でどちらも赤。テストは 559 → 560 件。**Swift は変えていない**。
- **次**: 英数状態の自動判定 (b) を Mac に入れる P11 のときに、(b) の作り直し (`consumed` と、消す数が 1 つ多い問題)、expected の付与 (案 B 相当)、Swift の fail-closed (案 A) を合わせて行う。
- **文書の食い違い (直していない)**: `docs/SECURITY_REVIEW_2026-10.md` の 225 行目は、Mac でも `DirectModeAutoDetect` (既定 ON) により英字が保持され、学習され得る、と書いている。コード上は Mac でこの設定が効かないので、前提を見直す余地がある (`_heldLetters` に保持される経路が Mac にあるかは、このレビューでは未確認)。
- **未検証**: 実機 (Swift を変えていないので不要)。(b) が自動大文字化で照合に失敗するかは、(b) が Mac で使われないので確認していない。Windows は対象外。

## 追加項目 P39 (候補ごとの注釈: 本家 #86 の考え方。Core の JSON に `notes` を足すだけ。Swift は保留)


- **経緯**: 調査文書 (`UPSTREAM_PR_SURVEY.md` の #86) は「Core に `Notes` があるが JSON に出していない。Swift も使っていない」とした。本家 #86 (Windows の TSF IME。`gh pr diff 86` を読んだだけ) は、`SessionResult.ToJson` に `"notes":[…]` (候補と同じ長さ。注釈の無い候補は `null`) を足し、C++ の候補ウィンドウが候補の右に小さく描く。Mac 版の Core には `CompositionView.Notes` と `CandidateNotes` がすでにあり (Windows の `CompositionWindow` が描いている)、Mac だけが JSON に出していなかった。
- **Notes に入るもの** (`CompositionController.CandidateNotes`、変えていない): 「英訳」(英訳の辞書から足した候補)、「全角」「半角」(同じ記号・英数字の半角と全角が両方とも候補にあるとき)。注釈が 1 つも無ければ `Notes` は null。
- **変えたもの (これだけ)**: `src/Meltype.Core/Composition/MeltypeSession.cs` の `AppendNotes` (+20 行) と、テスト `src/Meltype.Core.Tests/CandidateNotesJsonTests.cs` (新規、8 件)。**Swift は変えていない** (`NativeCore.swift`・`InputController.swift` は HEAD と同じ)。
  - `view.notes` を、`view.candidates` と同じ長さ・同じ順で書く。注釈の無い候補は `null`。**候補の範囲に注釈が 1 つも無いとき (Notes が null・空・全部 null、候補が 0 個のとき) は `notes` のキーごと出さない**。キーは view の最後 (`selectedPrediction` の後ろ)。Notes が候補より短ければ null で埋め、長ければ候補の数で切る。
  - **FFI の版数は上げない (8 のまま)**: JSON にキーを足しただけで、関数の引数・戻り値の形は変わらない。`tools/check-mac-aot-settings.py` の期待値も 8 のまま、PASS。
  - **今の Swift への影響は無い**: HEAD の `NativeCore.swift` の `SessionResult` / `TextEdit` / `CompositionView` (`Decodable`) をそのまま写した `swiftc` の投げ捨てスクリプトで、`notes` の入った JSON が読めることを実測した (下の「確認」)。`JSONDecoder` は未知のキーを無視する。
- **Mac の候補ウィンドウ側 (保留)**: `IMKCandidates` の注釈は `showAnnotation` で出す 1 つだけ (候補ごとの欄は無い)。すでに 2 か所が使っている (変換のたびに入力モードの「あ」= `showModeAnnotation`、意味があれば 1.5 秒後に意味 = `scheduleMeaning`)。v1 は `showModeAnnotation` に「選んでいる候補の注釈を `あ ・ 英訳` とつなぐ」数行を足していた。**R26 の指摘で保留にした**:
  1. `showModeAnnotation` は `view.meaning != nil` のとき何も書かない。注釈付きの候補 (学習で 2 番目に来た英訳、末尾の全角) から意味付きの候補 (先頭候補は 3,000 読みの 25% に意味がある) へ動くと、「あ ・ 英訳」が最大 1.5 秒残るおそれがある (IMK が選択の変更で注釈を消すかは未確認)。学習後・折り返しで起きる。
  2. そもそも今の「あ」の注釈が実機で見えるか自体が未検証 (`IMPROVEMENT_PLAN` にも「注釈の表示が出るか」は未検証と記載)。見えなければ Swift の数行は効果が無い。
  3. マウスで候補を選んだときは `candidateSelectionChanged` が番号だけ覚えて注釈を更新しない。
  - 保留した Swift 案は **作者の手元のパッチ (非公開、P39 の Swift 案)** (HEAD 基準、41 行、当てていない。将来の再開用)。再開するときは、先に下の「再開手順」の 1 を実機で確かめる。
- **案の比較** (採用は B):

  | 案 | 得 | 損 |
  | --- | --- | --- |
  | A 見送る | リスク 0 | 記号の `#` と `＃` を見分ける手がかりが Mac に無い。調査文書の改良が残る |
  | **B Core の JSON だけ (採用)** | 版数を上げず、Swift の挙動が変わらない。Linux の ibus も同じ JSON から読める見込み。将来の Swift の変更の足場 | 画面には何も出ない (価値は足場だけ) |
  | C Core の JSON + Swift が既存の注釈につなぐ (v1。保留) | 一覧のうち選んでいる候補の注釈が見える | 上の 3 点。実機で確かめるまで画面にどう出るか不明。意味付きの候補へ動いたとき「あ ・ 英訳」が残るおそれ |
  | D 候補の文字列に注釈を足す (`river　英訳`) | 一覧の全行に出る | `candidateSelected` が文字列で番号を逆引きする作りが壊れる。不採用 |
  | E `NSAttributedString` の候補で行ごとに注釈を描く | 全行に出る | IMKCandidates がどこまで描くか未調査・変更が大きい。不採用 |

- **実測** (v1 の調査。Mac の本番に近い配線。変換エンジンは偽物 = 「読み → 1 文節」で、azooKey ではない。3,000 組、候補の枠は合計 25,158 個): 英訳が付いた候補は 6,723 個 (26.7%)、全角 3,006 個 (11.9%)、半角 2,987 個 (11.9%)。「全角」「半角」はほぼすべての変換で出る (変換の候補の末尾に、打った英字 `kawa` とその全角が必ず足されるため)。この割合は英訳のある語に偏った上限の目安で、実際の azooKey は漢字の候補をもっと並べるので英訳の割合は下がる。
- **独立査読 R26 の結果** (別の査読者が実行。記録は手元のメモ (非公開)): **P0 なし**。
  - JSON と候補の対応: 5,040 行 × 4 設定 (通常 / MoreCandidates 46 個 / 英訳 OFF / 両方)、各約 11 万キーで、notes の長さが候補と合わない 0、全部 null なのにキーがある 0、対があるのに null の欠け 0、意味と注釈の同居 0。再変換 (かな・漢字・複数文節)・学習 4 周・記号も長さ一致。追加キー (Shift+←→ で区切り変更、F6–F10、BackSpace、`SelectCandidate`(マウス)、Esc) 約 8 万キーでも 長さ不一致 0 / 欠け 0 / 余計 0。実データ (3,000 組 × 3 周、学習で順が変わる 3,019 回) も長さ不一致 0。
  - 互換性: 変更前後で同じ入力 43.6 万 JSON を比べ、`notes` を除くと完全一致 (差 0)。Swift のデコード (査読者が `swiftc` で): notes 無し / null 混じり / 全部 null / `[]` / `null` / 未知キーの 6 形がすべて読めた。
  - 性能: 1 キーあたり 通常 5.1 → 5.1 µs、MoreCandidates 46 個 6.5 → 6.5 µs (5 回の中央値、変更前後)。**差なし**。`CandidateNotes` 自体は変更前から毎 `UpdateView` で計算していた。
  - 変異 5 本 (N1 エスケープ無し / N2 長さを 8 で頭打ち / N3 無い所を空文字 / N4 文節番号ぶんずらす / N5 全角判定の参照先): **N1・N3・N5 は赤、N2・N4 は生き残り** (全 569 件でも緑) → テスト不足 (P1)。この修正でテストを 2 本足して、2 本とも赤になった (下)。
  - Swift の読解で、上の保留の理由 1–3 を指摘 → 推奨は「Core の JSON だけ。Swift は実機で『あ』が見えると確認できるまで保留」。
- **テスト**: 560 → **568 件、すべて通る** (`CandidateNotesJsonTests` 新規 8 件。v1 の 6 件 + R26 の P1 で足した 2 件)。
  - v1 の 6 件: JSON の形 (候補と同じ長さ・順、`null` で位置を保つ、短い/長い Notes、エスケープ)、注釈が無いときはキーを出さない (4 通り + 今までの JSON と同じ末尾)、`kawa ` の `river`・`stream` に「英訳」、`@ ` の `@`=半角・`＠`=全角、変換前と英訳の辞書が無いときは出ない。
  - **足した 2 件** (どちらも注釈を位置ではなく候補の文字から決める `Expected` で比べる。位置のずれを検出するため):
    - `Session_NotesStayAlignedWhenThereAreMoreThanEightCandidates`: `MoreCandidates` で候補が 46 個になるとき、notes の長さが候補と一致し、9 個目より後ろの `river`・`stream` が「英訳」、`kawa` が「半角」、`ｋａｗａ` が「全角」で、日本語の候補は null。
    - `Session_NotesFollowTheSelectedClauseNotTheFirstOne`: 4 文節 (川|橋|雨|雲。英訳が 2 個・1 個・3 個・0 個) を → で 1 つずつ選び (`selectedClause` = 0–3、偶数・奇数)、各文節で notes が「この文節の」候補の位置と合う。
  - **変異 13 通りすべて赤** (記録は手元のログ (非公開)、作業コピーで 1 つずつ): 実装者の M1–M8 (`Take(count)` を外す / 長さを notes に合わせる / 全部 null でも出す / null の項目を飛ばす / 呼び出しを消す / 位置を 1 つずらす / Core の「英訳」判定を外す / 「全角」「半角」の入れ替え) と、R26 の N1–N5。**生き残っていた N2 は新しい 2 件が、N4 は文節のテストだけが赤にする**。変異を戻すたびに `touch` し、最後に全体を 1 回通した (568/568)。
- **確認** (作業コピー内。インストールはしていない):
  - `./build.sh --no-install` 成功、`MeltypeNative.dylib` の更新時刻は `MeltypeSession.cs` より後、`codesign --verify --deep --strict` 成功、`python3 tools/check-mac-aot-settings.py` は PASS 4 件。Swift は HEAD のまま (`git diff HEAD -- mac` は空)。
  - **AOT の dylib を直接叩いた確認** (ctypes。IME としての動作ではない): `meltype_abi_version` = 8。`kawa` Space → 候補 `川 かわ カワ ｶﾜ river stream River kawa ｋａｗａ` (9 個)、`notes` = `[null,null,null,null,"英訳","英訳","英訳","半角","全角"]`。`@` Space → 候補 5 個、`notes` = `["半角","全角",null,null,null]`。変換前の `kyou` → `notes` のキーが無い。
  - **HEAD の Swift の型でデコード** (手元の一時的な Swift のスクリプト 2 本 (非公開)。HEAD の `NativeCore.swift` から `SessionResult`・`TextEdit`・`CompositionView` を機械的に抜き出して `swiftc` したもの。v1 の確認は新しい型だった): (1) notes 無し、(2) 候補と同じ長さ・null 混じり、(3) 全部 null、(4) `[]`、(5) `null`、(6) 引用符・改行のエスケープ入り、の 6 形が読めた。さらに上の dylib が実際に返した JSON 3 本 (`kawa `・`@ `・`kyou`) も読めた。
- **得と損**:
  - 得: Core の JSON が #86 と同じ形になる (注釈が無いときは今までと完全に同じ形)。FFI の版数を上げずに済む。Swift は何も変わらないので、実機でのリスクは増えない。将来 Swift が使う足場ができる。
  - 損: 画面には何も出ない (価値は足場だけ)。`notes` が全部 null のときはキーごと出さないので、Swift 側は「無い」と「全部 null」を区別できない (区別が要るものは無い)。
  - 価値の評価: 低。Swift を足すかどうかは実機次第 (下)。
- **未確認 / 対象外**: Mac 実機で注釈が見えるか・今の「あ」の注釈が見えるか (Swift を変えていないので、この変更では確認不要)。変換エンジンは偽物 (azooKey を通していない)。英訳の候補が実際の変換で何割に付くか。**Linux の ibus** は JSON を `dict.get(読み)` で読むので未知のキーは無視されるはずだが (R26 が読んだのみ)、**私は実行していない**。**Windows / Linux は対象外**。
- **再開手順** (将来、Swift の数行を再開するとき。実機: TextEdit。入れ替えは別途承認のうえ。日本語入力は「かな」キー、JIS):
  1. **土台 (先に)**: 今の入れ替え済みの版で、`kawa` を打って Space。候補ウィンドウの横か下に小さく「あ」が出るか。**出なければ**、`showAnnotation` はこの環境で見えない。以降の確認はすべて意味が無いので、Swift 案は取り下げたままにする。
  2. **意味付きの候補へ戻ったとき注釈が残らないか (R26 の指摘。先にここを確かめる)**: 注釈付きの候補 (英訳 `river` や末尾の全角) から、意味が付く候補 (川・橋・箸など。`hashi` Space で 橋・箸) へ ↑↓ で動かす。`あ ・ 英訳` が最大 1.5 秒残らず、「あ」か意味に替わること。学習のあと (英訳を 2 回選んで 2 番目に上がった状態) と、候補の折り返し (一番下から一番上へ) でも試す。**漢字候補だけの確認では足りない**: 漢字候補の `あ` 表示は意味の有無で変わらないので、「あ に戻る」と書いてもあてはまらない (川・橋のような意味付きの候補では `showModeAnnotation` が何も書かない)。残るなら、意味があるときも注釈を書き直す案に直してから再開する。
  3. **英訳**: `kawa` Space のあと ↓ で、`river`・`stream` に着いたとき「あ ・ 英訳」になる。漢字・かなの候補では「あ」のまま。
  4. **全角・半角**: `kawa` Space のあと一番下まで進む → `kawa` で「あ ・ 半角」、`ｋａｗａ` で「あ ・ 全角」。
  5. **記号**: JIS キーボードで `@` のあと Space。`@` は「あ ・ 半角」、`＠` は「あ ・ 全角」。`#`・`!` も同じ。選んで Enter で確定した文字が、注釈の付かない今までと同じ。
  6. **マウス**: 候補をクリックで選んだとき注釈が前の候補のまま残らないか (`candidateSelectionChanged` は番号だけ覚える)。
  7. **退行の確認**: `kyou` Space → 「今日」を Enter、意味が 1.5 秒後に出る語、↓ ↑ の往復、Esc、予測変換 (Tab) の候補ウィンドウに注釈が出ないこと。コード向けアプリ (Terminal・VS Code) では「あ (コード) ・ 英訳」。
  8. **失敗の型**: 注釈が空になる・「あ」が消えて戻らない・注釈が前の候補のまま残る・候補ウィンドウがちらつく/動く・Enter で確定される文字が変わる・入力が止まる。出たらすぐ取り下げる。

## 追加項目 P38 (配布前の自動チェックのスクリプト: 本家 #78 (配布前の Defender 検査)・#87 の考え方の Mac 版)

- **経緯**: pkg の査読 (`docs/PKG_REVIEW_2026-10.md`) で、配布物の問題 (ビルド時の絶対パス 1,020 件・タグ時点の postinstall と HEAD の差など) を、公開のあとに手作業で見つけた。§7 に検査項目の案 10 個を残してあった。本家は配布前にウイルス対策ソフトで検査して誤検知を先に見つける (#78)。Mac 版では「公開する前に、署名・ビルド時のパス・arch・版の一致・pkg の中身を機械で見る」に置き換えた。
- **実装**: `tools/check-release.py` (新規、Python 3.9 互換。既存の `tools/check-mac-aot-settings.py` と同じ書き方で、Core・Swift・`mac/build.sh` は変えていない)。`docs/RELEASE.md` に使い方を 5 行足した。
  - 既定で `mac/build/Meltype.app` を検査。引数に `.zip`・`.pkg`・`.app` を渡すとそれも検査。`--no-app` (既定の app を外す)、`--zip-digest`・`--pkg-digest`・`--digest` (公開済みの Release の digest。**スクリプト自身はネットワークを使わない**)、`--ref` (リポジトリ側の基準。既定 HEAD。公開済みの版は `v<版>-mac`)、`--expect-version`、`--signed` (Developer ID の配布物を想定)、`--compare-installed` (参考表示)、`--help`。
  - **完全に読み取り専用**: zip と pkg は `$TMPDIR` の下の `mktemp` した一時フォルダーへ展開し、終わると (SIGINT・SIGTERM の中断でも) 消す。インストール・postinstall の実行・配布物の実行をしない。`installer` は `-dominfo` だけ (`-target` は渡さない)。`xattr -l`・`mdls` は使わない。リポジトリ側は `git show <ref>:<path>` で読むだけ。
  - 出力は項目ごとの `[PASS]` `[FAIL]` `[SKIP]` `[INFO]` と件数だけ。絶対パス・署名つき URL は出さない (`codesign -dv` の `Executable=` 行は捨て、RPATH も分類の件数だけ。1 行ずつマスクもかける)。FAIL が 1 つでもあれば終了コード 1、使い方の誤りは 2。**SKIP と INFO は失敗にしない**。
- **§7 の 10 項目の取捨**:

  | § | 項目 | 扱い | 理由・やり方 |
  | --- | --- | --- | --- |
  | 1 | digest の突き合わせ | 実装 (任意) | `--zip-digest`・`--pkg-digest` で比べる。渡さなければ SKIP。SHA-256 は INFO で出す。`mac/dist` の同名ファイルとの比較は見送り (検査する対象そのものと比べても何も分からない)。`gh` を呼ぶとネットワークを使うので、digest はユーザーが渡す |
  | 2 | 署名の状態 | 実装 | `pkgutil --check-signature` (署名なしが期待値。`--signed` で逆)、`installer -dominfo` (ホームフォルダー限定)。`spctl` は INFO (拒否が普通)。`installer -pkginfo` は見送り (タイトルを出すだけで判定に使えない) |
  | 3 | 展開 | 実装 | 存在しない一時パスへ `pkgutil --expand-full` が rc 0 |
  | 4 | 構造 | 実装 | Distribution (hostArchitectures・os-version min が Info.plist の `LSMinimumSystemVersion` と同じ・domains)、PackageInfo (install-location・relocatable・auth・identifier・upgrade-bundle・strict-identifier・postinstall の timeout)。期待値はスクリプト冒頭の定数 |
  | 5 | 出所の整合 | 実装 | postinstall を `--ref` のものとバイト比較。Distribution は XML として比べ、productbuild の書き換え (`installKBytes`・`updateKBytes`・`#`・`bundle-version` の塊) を戻してから比べる (`VERSION` の置換は make-pkg.sh の sed と同じ、1 行に 1 回) |
  | 6 | ペイロード | 実装 | symlink (外へ出ないか)・setuid/setgid/sticky/他者書き込み・`.DS_Store`・`._*`、Mach-O の arm64・minos (platform 1 の行だけ)・RPATH (OS 標準と `@` 始まり以外の絶対パスが無い)・依存 (`otool -L`。dylib 自身の install name は除く)・`libMeltypeNative.dylib` の存在・ビルド時の `/Users/<名前>/` が 0 件 (数え方は `grep -a -o -E` と同じ正規表現)。pkg の `._*` は `pkgutil --payload-files` で数えて INFO (1.1.5 は 3,470 件で、無害と見ている)。xattr の名前は見ない (署名つき URL を避ける)。RPATH・依存の「想定どおり」は個別の値の一致ではなく分類にした (値を固定すると、依存の追加のたびに壊れるため) |
  | 7 | 署名 | 実装 | `codesign --verify --deep --strict`、app・管理画面・Frameworks の各部品が ad-hoc で TeamIdentifier なし、エンタイトルメントなし |
  | 8 | 版の一致 | 実装 | app と管理画面の Info.plist、リポジトリの Info.plist・Core の csproj・Converter.swift の versionString・`1-bug.yml` の例、zip・pkg のファイル名、Distribution・PackageInfo の版、`CFBundleVersion`、`--expect-version`。Windows 側の `src/Meltype/Meltype.csproj` は対象外 |
  | 9 | 案内文の整合 | 実装 | `git grep` で「右クリック → 開く」の行を探し、前後 3 行に「このまま開く」があるか (HEAD で 8 行すべて)。これはリポジトリ側の検査で、`--ref` を古いタグにすると正当に FAIL する (併記はタグより後) |
  | 10 | 導入済みとの比較 | 実装 (参考) | `--compare-installed` で版と実行ファイルの SHA-256 を INFO に出すだけ。開発用の Mac では同じ版番号で中身が違うのが普通で、リリースの可否に関係しないため、失敗にしない |

  追加した項目 (§7 に無い): zip の中身 (最上位が `Meltype-mac/` だけ・直下の構成が `make-zip.sh` と同じ・`__MACOSX`/`._*`/`.DS_Store` が 0・名前に `..` や絶対パスが無い・シェルスクリプトの実行権限・`bash -n`・INSTALL.txt に「このまま開く」)、postinstall の `bash -n` と、通信・難読化・再帰削除のコマンドが無いこと (コメント行を除く目安)、zip・pkg・app を同時に渡したときの Mach-O の同一性。
- **実測** (macOS 26.6.2、作業コピーは HEAD `4011e23` と同じ内容。スクリプトの実行時間は app だけで 1.5 秒、zip と pkg で 15.6 秒):
  - **(a) 手元の `mac/build/Meltype.app`** (更新時刻 16:52。`strip -S` 済み): **PASS 22 / FAIL 0 / INFO 1**。ビルド時のパスは `Meltype`・`MeltypeDictionary`・2 本の dylib とも 0 件。codesign 通過、4 部品とも ad-hoc・TeamIdentifier なし・エンタイトルメントなし、arm64、minos 最大 13.0、RPATH は `@` 始まり 4 本・OS 標準 2 本・その他 0、版は 5 か所とも 1.1.5・ビルド番号 21、案内文 8 行すべて併記あり。mac/build に置いた複製への引数なし実行も同じ結果。
  - **(b) `mac/dist` の 1.1.5 の zip と pkg** (読み取り専用で渡した):
    - `--ref v1.1.5-mac`: PASS 68 / FAIL 3 / INFO 9。FAIL は、zip と pkg の中の `Meltype`・`MeltypeDictionary` に**ビルド時のパス 1,020 件** (987 + 33。PKG_REVIEW §2 の数と一致) の 2 か所と、案内文 (タグ時点では併記が無い: 8 行中 6 行) の 1 か所。digest・署名なし・構造・postinstall と Distribution のタグとの一致・版 (13 か所とも 1.1.5、ビルド番号は 5 か所とも 21)・zip と pkg の Mach-O 4 本が同一、は PASS。
    - `--ref HEAD`: 上に加えて、**postinstall が HEAD と違う** (S1 の署名検査がタグに入っていない、PKG_REVIEW §2 のとおり) が FAIL。わざと違う digest を渡すと digest の項目も FAIL。
  - **(c) 変異 20 件** (複製だけを壊す。`mutate.py`。無傷の複製は FAIL 0): すべて、対応する項目が FAIL になった。app: ①署名を壊す (封印された資源を 1 バイト変更) ②実行ファイルの中の文字列をビルド時のパスに見える文字列で上書き ③Info.plist の版 ④Mach-O ヘッダーの cputype を x86_64 に ⑤RPATH にビルド機のパスを足す ⑥minos を 15.0 に ⑦署名を外す ⑧エンタイトルメント付きで署名し直す ⑨`.DS_Store`・bundle の外を指す symlink・他者書き込み権限 ⑩`libMeltypeNative.dylib` を消す ⑪同梱されていない `@rpath` の依存 ⑫`CFBundleVersion` だけ変更。zip: ⑬実行権限を外す ⑭余分なファイルと欠けたスクリプト ⑮構文エラー。pkg (`pkgutil --expand` → 編集 → `--flatten` を scratch で): ⑯hostArchitectures ⑰domains ⑱postinstall に `curl` ⑲install-location ⑳PackageInfo の版。1 つの壊れ方が複数の項目に出るもの (署名を外すと verify も FAIL、など) は、関連する項目の FAIL として記録した。⑫は自分で選んだ文字列が実行ファイルの依存名に当たり、依存の項目も FAIL になった (正当な連鎖)。
  - **読み取り専用の確認**: 検査の前後で `mac/build/Meltype.app` (3,469 ファイル) と `mac/dist` (3,510 項目) の全ファイルの内容・大きさ・更新時刻・権限のハッシュが変わらない (`snap.py`)。`$TMPDIR` に `check-release-*` が残らない (SIGTERM で中断した場合も、子プロセスを止めてから消すように直して 0 件を確認)。全ログに `/Users`・`/private`・`/var/folders` の文字列が 0 件。引数の誤り 4 通りは終了コード 2、存在しない pkg は FAIL、存在しない `--ref` は SKIP。
  - 検査の途中で直した不具合 2 件: dylib 自身の install name (`@rpath/MeltypeNative.dylib`。NativeAOT の出力名のまま) を「同梱されていない依存」と誤って FAIL にしていた、pkg のペイロードの `./._Meltype.app` (AppleDouble) を「Meltype.app の外の項目」と誤って FAIL にしていた。
- **得と損**:
  - 得: 公開のあとに査読で見つけていた種類の問題 (ビルド時のパス・postinstall の差・版の食い違い・arch・署名の状態) を、公開の前に 1 コマンドで見つけられる。手元の 1.1.5 の実物で、既知の問題 (1,020 件・S1 が入っていない) を同じ数字で出し、壊した複製 20 件を検出した。読み取り専用で、何も入れない。
  - 損: (1) 期待値 (`auth=root`・timeout 600・zip の構成・署名方針など) をスクリプトの定数に持つので、方針を変えたときにスクリプトも直さないと偽の FAIL が出る (`--signed` は署名方針の切り替え用)。(2) PASS は「配れる」の保証ではない: ad-hoc 署名なので署名し直した改ざんは見つけられず、postinstall の禁止コマンド検査は目安で回避できる。実インストール・Gatekeeper の許可・実機の動作は見ない。(3) 約 890 行のスクリプトの保守が増える。(4) `otool`・`lipo`・`codesign` など Command Line Tools と、システムの Python 3.9 が要る。(5) 案内文の検査 (§7-9) はリポジトリの文面を見るだけで、配布物の `conclusion.html` などは見ない。
- **未確認・見送り**: `--signed` (Developer ID) の経路は、証明書が無いので実行していない。`llama.framework` などの `.framework` を同梱するビルドは、今回のビルドに無いので試していない (署名の対象には含めてあるが、symlink の扱いも実物では未確認)。Intel Mac・macOS 13/14/15 での実行は未確認 (26.6.2 のみ)。`--compare-installed` は実行して INFO が出ることだけ確認した。CI には組み込んでいない (リョウさんの方針で CI は行わない)。公開済みの Release の digest との突き合わせは、digest を手で渡す形で、実際の `gh release view` の値では今回試していない (手元の `mac/dist` のファイルのハッシュで試した)。次のリリースの zip・pkg で件数が 0 になること (S2) は、このスクリプトで確かめられる。独立した査読は未実施。Windows/Linux は対象外 (Mac 専用のスクリプト)。

## 査読 R25 の結果と対応 (P38 修正版。`P38v2.patch` = HEAD 基準で `tools/check-release.py` と `docs/RELEASE.md` の 2 ファイル)

R25 の結論は P0 なし。読み取り専用・ネットワーク不使用・シェルインジェクションなしは実測で確認済み。P1 が 6 件あり、すべて「先に変異や再現で旧スクリプトが PASS・Traceback・誤 FAIL になるのを確かめ (赤)、直して期待どおりになる (緑)」の順で直した。

| # | R25 の指摘 | 赤 (旧) | 直し方 | 緑 (新) |
| --- | --- | --- | --- | --- |
| P1-1 | preinstall の追加を見逃す | Scripts と PackageInfo に preinstall を足しても PASS (43/0/1) | `Scripts/` のファイル一覧 (`._*` を除く) を `postinstall` の 1 つだけ、PackageInfo の `<scripts>` も `postinstall(./postinstall)` の 1 つだけに固定。pkg の最上位 (Distribution・Resources・Meltype-component.pkg) と component の構成 (Bom・PackageInfo・Payload・Scripts) も固定 | preinstall 追加は FAIL 2 件、Scripts の余分なファイルだけでも FAIL、最上位に余分なファイルでも FAIL |
| P1-2 | Resources を比べない | welcome.html・conclusion.html の改変、余分な `extra.js` が PASS | `git ls-tree -r <ref> mac/pkg/resources` の一覧と、pkg の `Resources/` をファイル名・バイトで突き合わせ (make-pkg.sh の `--resources pkg/resources` がそのまま入る) | 3 変異とも FAIL。1.1.5 の実物は `--ref v1.1.5-mac` で 2 個とも一致 |
| P1-3 | setuid 検査が無効 (`--expand-full` は 04755 → 0755) | Payload の setuid・setgid ディレクトリ・fifo が PASS | `pkgutil --expand` した生の Payload を `bsdtar -tvf` で読み (失敗したら `gzip -dc \| cpio -it -v`)、setuid/setgid/sticky/他者書き込み・デバイス/fifo/ソケット・外へ出る symlink を検査。読めない環境は SKIP (理由を出す) | 3 変異とも FAIL。1.1.5 実物は 6,941 項目で 0 件。bsdtar・gzip・cpio が PATH に無い環境では SKIP と理由 |
| P1-4 | 読めない zip・pkg で Traceback と絶対パス | rc=1、Traceback にパスが出る | `main()` を `guarded_main()` で包み、例外は型名と `strerror` だけの 1 行 `[FAIL]` (パスなし) と終了コード 2。SystemExit・KeyboardInterrupt は握らない | 読めない zip・pkg とも rc=2、1 行、漏れ 0 |
| P1-5 | `--ref typo` が全部 SKIP で rc=0 | rc=0 | `--ref` を明示したときは、最初に `rev-parse` で確かめ、読めなければ stderr に一文を出して終了コード 2 (先頭が `-` の値も拒否)。コミットは読めるがファイルが無いときは FAIL (既定の HEAD のときだけ SKIP) | rc=2 |
| P1-6 | `len(machos) >= 4` が脆い | Swift の互換 dylib が無い app で偽 FAIL | 件数条件を外し、必須 3 本 (Meltype・MeltypeDictionary・libMeltypeNative.dylib) を名前で確認 | 偽 FAIL が消え、`libMeltypeNative.dylib` を消した変異は FAIL |

P2 の対応: 相対 RPATH の文言を「相対・絶対パスの RPATH」に直した。`vtuple` は末尾の 0 を無視する ("13" = "13.0" = "13.0.0"。単体テストで旧 NG 2 → 新 OK)。`docs/RELEASE.md` に「リポジトリ側の検査は HEAD (`--ref`) のコミットの内容と比べる。コミットしてから実行する」と、pkg の追加検査・`--ref` の誤りが rc 2 になることを追記。timeout=600・auth=root は「pkgbuild の既定値で make-pkg.sh に明示は無い」とコメント。追加で見つけて直したもの: エラー文に出た `$HOME` 配下のパスの**後ろの部分** (`/<フォルダー>/...` など) が残る漏れ (マスクを長い順にし、続きも伏せる)。

追加した検査項目は 6 つ (pkg 最上位・component の構成・Scripts・PackageInfo の scripts・Resources・Payload の生のモード)、置き換えた項目は 1 つ (Mach-O の件数 → 必須 3 本)。正常な pkg の件数は 43/0/1 → 49/0/1。

### 実測 (macOS 26.6.2・Python 3.9.6)
- 変異 (新規 13 件 + 既存の検出の確認 2 件 + 制御 2 件。単体テスト 23 件は別): pkg の preinstall 追加・Scripts の余分なファイル・welcome 改変・conclusion 改変・Resources の余分なファイル・最上位の余分なファイル・Payload の setuid・setgid ディレクトリ・fifo (以上 9 件は旧 PASS → 新 FAIL)、読めない zip・読めない pkg・`--ref` の誤り (旧 Traceback / rc=0 → 新 rc=2)、互換 dylib なし (旧 偽 FAIL → 新 FAIL なし)。制御 (無改変の pkg と、Payload を詰め直しただけの pkg) は旧新とも FAIL 0。
- 誤検知の確認 (旧と新で FAIL の項目名が同じ): ① 手元の `mac/build/Meltype.app` は PASS 22 / FAIL 0 のまま。② HEAD の app から make-pkg 相当の手順で scratch に作った zip と pkg、app の 3 点同時は FAIL 0 (旧 89 → 新 95 PASS)。③ `mac/dist` の 1.1.5 の zip と pkg を `--ref v1.1.5-mac`: FAIL 3 (パス 1,020 件 = 987 + 33 を 2 か所、案内文 8 行中 6 行) で旧と同じ。④ 同 `--ref HEAD` は、旧の FAIL (postinstall 不一致) に加えて、HEAD で変わった conclusion.html のため Resources が FAIL (正当)。
- 読み取り専用: app と `mac/dist` の 6,979 項目の内容・大きさ・更新時刻・権限が実行の前後で同一。`$TMPDIR` に `check-release-*` が残らない。ログに `/Users`・`/private`・`/var/folders` 0 件、Traceback 0 件。
- 未確認: Intel Mac・macOS 13〜15。`bsdtar` が無い環境の SKIP は PATH を絞った実行で確認 (実際に無い Mac では未確認)。`--signed` は証明書が無く未実行。R25 の指摘 6 件の再査読は未実施 (**独立した再査読が要る**)。

## 査読 R29 (P38 v2 の再査読) の結果と対応

- **結論**: P0 なし。R25 の P1 6 件は全部直っていることを、R25 の変異を再実行して確認した (preinstall の追加・welcome.html / conclusion.html の改変と削除・setuid の仕込み (cpio を自作)・読めない zip / pkg・`--ref` の誤り・互換 dylib を消した app)。約 50 の新しい変異 (PackageInfo の identifier・install-location・version、Distribution の改変、Payload の絶対パス symlink・余分なトップレベルのファイル、zip の絶対パス・`..`、app 内の外向き symlink、Frameworks の余分な dylib など) のほとんどを検出した。読み取り専用 (app と `mac/dist` の 6,979 項目の内容・大きさ・更新時刻・権限が実行の前後で同一)、一時フォルダーと孤児プロセスなし、ログに Traceback・絶対パス・URL なし、外部コマンドはすべてリスト引数 (`shell=True` なし)、悪意のある cpio は `pkgutil` が拒否。
- **新しい P1 (直した)**: Payload の `./Meltype.app2`・`./Meltype.appEVIL` が、`startswith("./Meltype.app")` の前方一致で PASS になっていた (`/Library/Input Methods` に余分なものが入るのを通す)。`l == "./Meltype.app" or l.startswith("./Meltype.app/")` に直した。**赤→緑**: 直す前は 2 つとも PASS、直したあとは FAIL (対照の無改変の pkg は PASS のまま)。
- **P2 (直した)**: `unzip` に標準入力を渡していなかったので、tty で同名のエントリーが 2 つある zip の上書き確認で止まった → 標準入力を切った。`git ls-tree` が非 ASCII の名前を C クオートして、中身が一致していても偽の FAIL になった → `core.quotePath=false`。
- **確認 (直したあと)**: 手元の `mac/build/Meltype.app` は PASS 22 / FAIL 0 のまま。`mac/dist` の 1.1.5 を `--ref v1.1.5-mac` で検査して PASS 73 / FAIL 3 / SKIP 2 (FAIL は既知のビルド時のパス 1,020 件と、案内文 6 行)。
- **P2 (直していない)**:
  - PackageInfo の `postinstall-action`・`overwrite-permissions`・`<relocate>`・`<atomic-update-bundle>`・`numberOfFiles` を検査していない。`LSMinimumSystemVersion` を app 単体・zip 単体では固定していない (pkg のときだけ Distribution 経由)。helper の実行権限・dylib のモードを見ない (main の実行ファイルだけ)。
  - 既定の `HEAD` が読めないとき (リポジトリの外・git が読めない) は、リポジトリ側の検査が SKIP になり終了コード 0 になる。welcome.html を改変した pkg でも「FAIL なし」と出る (リポジトリの中で実行する前提)。明示した `--ref` に `mac/pkg/resources` が無いときのメッセージが紛らわしい。`--ref ""` は黙って HEAD 扱い。
  - 700MB の非 Mach-O ファイルで最大メモリが約 775MB (ファイルを丸ごと読む。既存)。
- **未確認**: Intel Mac・macOS 13〜15・`--signed`・公証 (stapler)。
- **D1 (dmg 配布) との関係 (マージ後に分かったこと)**: 別セッションの D1 で、リリースの添付が zip と dmg に変わった (pkg は単体で添付しない)。`tools/check-release.py` は zip・pkg・app を検査し、**dmg は検査しない** (dmg を開いて取り出した pkg を渡せば、pkg としては検査できる)。dmg 用の検査 (中身の pkg・図解・開き方の案内) は未対応。リリース前に、zip と dmg の中の pkg を、このスクリプトで検査する手順を RELEASE.md に足すかを、次のリリースの準備のときに決める。

## 追加項目 S3 (セキュリティレビュー 2026-10 の M5 の見直し: 「Mac でも DirectModeAutoDetect で英字が保持・学習され得る」は成り立たない。文書だけを訂正)

- **経緯**: P37 の調査で、Mac では `MeltypeSession.CreateDefault` が `DirectMode`・`ClassifyDirect`・`DirectDecided` を設定せず、Direct のキーは Core に渡る前に素通しされると分かった。`docs/SECURITY_REVIEW_2026-10.md` の M-1 (225 行目) は「秘匿フラグを立てないアプリでも英数モードで `DirectModeAutoDetect` により英字が `_heldLetters` に保持され、学習され得る」と書いていた。調査員が根拠をコードと実測で確かめ、文書の訂正案を作り、独立査読 R28・R30 で文言を詰めた。**コードは変えていない**。
- **確かめたこと** (作業コピー、`MeltypeSession.CreateDefault` (Mac 本番と同じ配線)・FakeConverter・一時フォルダー・ダミーの語):
  - `DirectModeAutoDetect` を読むのは Windows の `MeltypeEngine.cs:357` だけ。`DirectMode` を設定するのは Windows の `TrayApplicationContext.cs:50` だけで、`Hold` (`_heldLetters`) は `DirectMode()` が true のときだけ通る。**Mac では通らない**。
  - Direct (英数・コード向けアプリ。`SetApp` を直接呼んで Terminal・iTerm2 でも確認) は、全キーが素通しで、一時フォルダーに増えたファイルは 0 個。`FileLog` と `LogTypedText` を ON にしても、meltype.log は作られない。
  - **ただし、日本語入力のまま (Direct でない状態で) 学習ファイルに残る経路は、別にある** (M5 の「中」は変えない): (a) 英字のみ 2 文字以上の語を、自動判定と逆の表示モード (F6〜F10) にして確定したとき languages.json に残る (判定の強さが標準 (Balanced) のとき。Manual では基準が変わり、Tab も残る)。(e) 変換で候補を選び直して確定すると、conversions.json・suggest.json・languages.json に書かれる (FakeConverter での実測。数字を含む語でも残る)。(d) `FileLog` と `LogTypedText` が両方 ON のときだけ、meltype.log に語全体が残る (どちらも既定 OFF)。F6〜F10・Tab・候補の選び直しをしない Enter・Space だけの確定では増えない。
- **査読の経緯 (誤りを直した記録)**: 調査員の初版の訂正案は、「F6/F10 をしなければ、Enter・Space で確定しても学習ファイルは増えない」と書いていた。R28 (P0) が、候補の選び直しで 3 つのファイルに書かれることを実測して指摘し、「FakeConverter では候補を選び直せない (未確認)」という記述も誤りだった。v2 で直し、R30 が再実測した。R30 の P1 4 件 (`DetectionLevel` に `Auto` は無い、F6〜F8 のあとの Space だけでは残らない、表 M5 の「問題」「影響」欄と本文の食い違い) を直した。
- **文書の変更** (`docs/SECURITY_REVIEW_2026-10.md` だけ。取り消し線 `<del>` と `[見直し 2026-10-12: …]` の形は、既存の `[訂正]` の前例に沿った): M-1 の 225 行目の主張を取り消し線にして見直しを足した。本文の「影響」の条件を実測に合わせて書き直した。修正案 2 に補足を足した。総括を本文と揃えた。表 M5 の旧行番号に注記を足した。重大度の数字 (中 10 件) は変えていない。
- **見送り (適用していない)**: 調査員が作った任意の M3 の訂正パッチ (M3 は S1 (`9084282`) で対応済み。M-3 の経路は Windows 専用で、HEAD では `DescribeKind` の分類だけをログに出している)。R30 が、同じコミットが M8 (postinstall に `codesign --verify` を足し、失敗なら `exit 1`)・M7・M2(b) も直していることを指摘した。M3 だけ「対応済み」にすると、冒頭の「今すぐ…M8」と表 M8 の「常に `exit 0`」が現状と食い違ったまま残るので、まとめて直すときに一緒に扱う (調査員のパッチは手元の一時ファイル (非公開) で、24 時間で消える)。
- **未確認**: azooKey を通した学習 (`learnCallback`)。Swift・IMK の実機での動作。Windows 実機。2 文字の語で languages.json に残る条件。F7・F8・F9 のすべての組み合わせ (代表だけ測った)。F6〜F8 のあとの Space だけでは残らず、F6 → Space → Esc では残る違いの経路。
- **前提の変化**: Mac で Direct が学習しない・`Hold` が通らないという前提は、英数状態の自動判定を Mac に入れる (P11) と変わる。そのときは、`SwitchHeldToJapanese` の作り直し (P37 の節) と、この M5 の見直しを合わせて見直す。

## 追加項目 D1 (pkg の「開発元が未確認のため開けません」で止まる利用者向けに、開き方の図解つき dmg で配る)

- **きっかけ**: pkg を Chrome で落とした利用者が「“Meltype-mac-1.1.5.pkg” は、開発元が未確認のため開けません」で止まった (macOS 14 以前の形のダイアログ)。この表示は Gatekeeper が pkg を開く前に出すので、pkg の中 (welcome.html など) には案内を置けない。署名・公証は運用コストのため行わない方針 (リョウさんの判断) なので、案内を pkg の外に置く。リョウさんの判断 (2026-10-12): 案 A (dmg) と案 B (README・リリース本文の図解) の両方。
- **実装**: `mac/make-dmg.sh` (新規) が pkg を dmg に包む。dmg を開くと Finder の窓の背景に開き方の図 (macOS 15 以降は「このまま開く」、14 以前は右クリック →「開く」→「開く」) が出て、左に pkg、下に同じ内容の `開けないときは.txt` が並ぶ。背景は `mac/pkg/dmg/make-background.swift` が描き (1x・2x を tiff に)、配置は dmgbuild (`mac/pkg/dmg/settings.py`) で `.DS_Store` を直接書く。既存の dmg は上書きしない。中の pkg は入力とバイト単位で同じ。
- **Finder の AppleScript をやめた理由**: macOS 26.6.2 の Finder は、AppleScript で設定した背景 (`background picture`) を `.DS_Store` に保存しなかった (実測。アイコンの位置と窓の大きさは保存された)。dmgbuild 1.6.7 は macOS 26.2 の背景の不具合に対応済み (dmgbuild #273 / #275)。
- **得と損**: 得 = ダウンロード直後に開き方が目に入る。署名基盤なしで済む。損 = リリース手順が 1 つ増える。dmgbuild を PyPI から初回だけ入れる (Python 3.10 以降とネットワークが必要。版とハッシュは `mac/pkg/dmg/requirements.txt` で固定、wheel のみ)。dmg 自体も署名なしで、pkg の許可の手順は減らない (案内が見えるようになるだけ)。
- **文書**: `README.md` に「配布版 (dmg)」節と窓のスクリーンショット (`docs/images/mac-dmg-window.png`)、`mac/README.md` に作り方、`docs/RELEASE.md` に手順と添付・SHA-256 を足した。アプリ内の更新は zip だけを名前で探す (`Updater.swift:25`) ので、dmg を添付しても影響しない。
- **確認**: 公開済み 1.1.5 の pkg のコピーで dmg を作り、`hdiutil verify`・UDZO/HFS+・中の pkg の SHA-256 が公開値 (`eceb709e…`) と一致・`/Users/` や利用者名が 0 件・再実行で上書きしない (rc 1)・空白や `&` を含むパス名・相対パス、を確認。Finder で開いて背景と配置をスクリーンショットで確認 (macOS 26.6.2)。査読 2 回 (指摘はすべて反映。`com.apple.provenance` の印は外せないので見送り)。
- **未検証 (実機)**: ブラウザで落とした (隔離属性つきの) dmg をダブルクリックしたときに警告が出るか。読み取り専用の dmg の中の pkg に「このまま開く」が効くか。macOS 13/14 の右クリックの経路。`spctl -a -t open` は dmg に対して「rejected / no usable signature」を返すが、隔離属性の有無で結果が同じなので、実際にマウントが止められるかは分からない。

## 追加項目 Q6 (ローマ字の再変換で、箇条書き・電話番号・数字の後ろの `-` が長音 `ー` になる)

- **経緯**: P36 続き (前後に改行が付いた選択の再変換) の査読 R27 で見つかった既存の問題 (上の P36 の節の「P1 (別件、改行の有無に関わらず既存)」)。選んで Shift+Space で再変換すると、`- kyou wa ...` が `ーきょうわ…`、`Tel: 03-1234-5678 desu` が `03ー1234ー5678です`、`kyou wa - ashita wa` が `きょうわーあしたわ` になっていた。段落を 3 回クリックして選ぶ形が P36 続きで増えたので、当たる機会が増える。リョウさんの依頼で急ぎ対応 (2026-10-12)。
- **修正** (Core のみ。`CompositionController` の再変換の経路だけ。打ちながらの変換は変えていない):
  1. `SplitRomajiText`: 空白で区切った語のうち、**英字を含まず `-` を含む語** (`-`・`03-1234-5678`・`2026-10-12`・`10-20`) は、`CompositionText` に通さず、打ったままの語として残す (`RomajiPart` の `Literal`)。英語の語と同じく、前後の空白も残る。後ろの語への手がかりは「英語の後ろ」にしない (`previousEnglish = false`)。
  2. 感動詞の後ろの判定 (`PrecedingEnglishSentence`。`- oh no` の `no` を `の` にしない) に使う文は、新しい `JoinForJudgment` でつなぐ。残した語は、今までどおり長音 `ー` の日本語として数える (査読 R31 の P1 の修正。半角の `-` のままだと、`oh` の手前で「日本語より後ろ」の走査が止まらず、`- oh no, wasureta` が `- oh の、わすれた` になっていた。112 件)。
  3. `DigitHyphens` (リョウさんの判断で追加): 変換結果で、**ASCII の数字のすぐ後ろの `ー` を `-` に**、全角の数字の後ろの `ー` を `－` に戻す。長音はかなの母音の後ろにしか意味が無いので、数字の後ろの `ー` は打った区切りの `-`。変換エンジンには今までどおり `ー` で渡すので、読みと変換結果は変わらない。`10-ji ni kaigi desu` → `10-じにかいぎです`、`denwa wa 03-1234-5678desu` → `でんわわ03-1234-5678です`、`3-4nin desu` → `3-4にんです`。かなの後ろの長音 (`ra-men`・`su-pa-`・`ko-hi-2hai` のこーひー) は変わらない。
- **結果の例** (テストの変換エンジンはかなを返すだけ): `- kyou wa ii tenki desu` → `- きょうわいいてんきです`、`- [ ] …` → `- [ ] …`、`Tel: 03-1234-5678 desu` → `Tel: 03-1234-5678 です`、`kyou wa - ashita wa` → `きょうわ - あしたわ`、`- oh no, wasureta` → `- oh no, わすれた`。
- **テスト**: 575 → **577 件** (全通過)。`ReconvertRomajiTests` に 2 件: `Reconvert_StandaloneHyphenWords_StayHyphen` (箇条書き・末尾の改行・チェックボックス・電話番号・日付・`--`・語の中の長音・数字の後ろの `-`・感動詞・後ろの日本語) と `DigitHyphens_OnlyAfterDigits` (数字の範囲の端 0・9・０・９ と、すぐ外の `/` `:` `／` `：`)。修正前のコードで赤になることを確認した。変換の品質の評価 (`--eval-json`) は変更前と JSON が完全に一致。`./build.sh --no-install`・`codesign --verify --deep --strict`・`tools/check-mac-aot-settings.py` PASS。
- **査読 R31 (独立。2 回)**:
  - 1 回目 (規則 1 だけの版、5,732 件の前後比較): `-` を含まない入力は差分 0。`ー` が `-` になり空白が残るだけの変化が 1,924 件 (改善)。英語だけの文 (`state-of-the-art`・`ls -la`・`a - b` など) に新しく候補が出たのは 0 件 (査読員の報告)。**P1**: `-` の後ろの感動詞 112 件の回帰 (上の 2 で修正)。P2: コメントの誤り (修正)。変異 `previousEnglish = true` が生き残った → テストを足して赤にした。
  - 2 回目 (修正版、19,450 件の前後比較。数字 + `-` + ローマ字・時刻・版番号・`2-` 単独・英語との混在・末尾の改行を足した): **P0・P1 なし**。変更前が正しく変更後が違う行は 0、候補が消えたのは 0、感動詞の回帰は 0。`ー` が `-` になっただけの変化 7,644 件、`> - sushi ga suki` → `> - すしがすき` などの改善 3 件、候補が新しく出る改善 2 件 (`good morning everyone - sushi ga suki`)。変異 17 通りのうち 15 通りが赤。生き残り 2 通りは、`Literal` の語を英語のまま `ー` にする (19,465 件で差分 0、等価) と、英字の後ろの `ー` も `-` にする (差が出るのは `kore wa B- desu` の 1 件だけで、変異のほうが自然とも言える。P2・任意)。
- **得と損**: 得: 箇条書き・チェックボックス・電話番号・日付・範囲・空白で区切った `-` が、選んで再変換しても崩れない。損: (1) 句読点が付いた `-` の語 (`desu -.`・`-!`・`-,`) は、`ですー。` だったものが `です -.` になる (半角の句読点のまま。286 件、入力自体が不自然なので許容)。(2) `1-0 de katta` は `1-0 でかった` と空白が入る (英字を含まない語として残すため)。(3) 数字の後ろでわざと長音を打つ形 (`2-` で「2ー」) は出なくなる (日本語で使う場面は見当たらない)。
- **限界・未確認**: (1) **Mac 実機で確かめていない**。(2) 本物の変換エンジン (azooKey) で確かめていない: 打っていない `ー` が数字のすぐ後ろに出る (変換エンジンが数字 + `ー` に変換する・ユーザー辞書や学習した候補の表記に数字 + `ー` がある) と、`DigitHyphens` がそれも `-` にする。査読の見立てでは、まず起きない。(3) `10-ji` 単独・`2-` 単独は、変更前と同じく候補が出ない (日本語だと言える所が無い)。`10:00-12:00ni kaigi` は、変更前と同じく `10:00-12:00ni かいぎ` になる (`ni` が英字のまま残る)。(4) Windows / Linux は対象外。
- **TODO (次の作業。リョウさんの判断 2026-10-12)**: **打ちながらの変換**でも同じ問題が残っている (`--repro`、変更前と同じ): `10-ji` → `10ーじ`、`10-ji ni kaigi desu` → `10ーじにかいぎです`、`03-1234-5678desu` → `03ー1234ー5678です`、`- kyou wa` → `ー今日わ`。今回は選んで再変換する経路だけを直した。打ちながらの経路 (`CompositionText`) は影響が広いので、別の項目として前後比較と査読をして直す。管理表の Q7。→ 別のセッションのコミット `a490a09` で修正済み・実機未確認。
- **別件 (記録だけ。直していない、原因は推測)**: `denwa wa 03-1234-5678desu` を打つと `でんわﾜー1234ー5678です` になり `03` が消える (`--repro`、変更前から)。Space で変換が始まったあとの数字キーが、候補の番号として扱われているように見える。再現と原因の確認から。管理表の Q8。→ `a490a09` (Q7 と同じコミット) で修正済み・実機未確認。

## 追加項目 Q9 (SNS の X の後ろの日本語が崩れる・英字のまま残る: `Xnotoukouha` → `んお投稿は`)

- **経緯**: リョウさんの実機の報告 (2026-10-12): `Xnotoukouha` → `んお投稿は` (期待は `Xの投稿は`)。`Xは`・`Xへ`・`Xを` は正しいが、`Xni`・`Xnado`・`Xnoyouna` は英字のまま残る。v1.1.5-mac のコードでも同じ (`--repro` で確認。今日の変更による回帰ではない)。X は SNS の名前でよく打つので、急ぎ対応。
- **原因**: (1) `X` に続けて `n` を打つと、ローマ字の `xn` (= ん。小書き文字の x と同じ仲間) としてまとめて読まれ、`X` と後ろの日本語の境目が無くなる (`Xno` → `ん` + `お`)。(2) 大文字 1 文字の後ろを日本語として区切るのは、後ろが 1 音の助詞 (の が を に で と は も わ へ) で始まるときだけだった (`Xnado`・`Xkara` は区切れない)。
- **修正** (Core のみ。打ちながらの変換の経路):
  1. `CompositionText.IsLoneCapitalX`: 語の頭 (入力の先頭・かな・記号・数字の後ろ) で打った大文字の `X` 1 文字は、次の文字と合わせて小書き文字 (`xn` = ん・`xa` = ぁ・`xtu` = っ) にしない。英単語の最後の `l`・`x` を小書き文字にしない既存の規則 (hotel) と同じ 2 か所に足した。小文字の `x` (`xtu` → っ) と、英字の後ろの `X` は今までどおり。
  2. `CompositionDetector.ParticlesAfterX`: 頭がちょうど `X` のときだけ、2 音以上の助詞 (kara・made・nado・nara・nante・yori・dake・shika・sika・bakari・kurai・gurai) でも区切る。`や` は入れない。全体の助詞の表は変えない。
- **結果の例** (打ちながら、Enter で確定。テストの変換エンジンはかなを返すだけ。実機では漢字になる): `Xnotoukouha` → `Xのとうこうは`、`kyouhaXnitoukousita` → `きょうはXにとうこうした`、`Xni` → `Xに`、`Xnoyouna` → `Xのような`、`Xnado` → `Xなど`、`Xnadodehappyousuru` → `Xなどではっぴょうする`、`Xkaradesu` → `Xからです`、`Xmadeni` → `Xまでに`。変わらないもの: `Xbox`・`Xboxnogemu`・`Xperia`・`Xmas`・`xtu` (っ)・`xnotoukou`・`Ishikawa`・`Okara`・`Ayaka`・`Bkara`・`Xyatte`。
- **テスト**: 577 → **578 件** (全通過)。`CompositionTests.LoneCapitalX_IsNotSmallKanaPrefix` (X + 1 音の助詞・X + 2 音以上の助詞 12 個・かなの後ろの X・小文字の x・X で始まる英単語・名前 (`Ishikawa`・`Imadegawa`・`Ashikaga`・`Okara`・`Ayaka`) と `Bkara` が変わらない・`Xya`・`Xyatte`)。修正前のコードで赤になることを確認した。変換の品質の評価 (`--eval-json`) は変更前と JSON が完全に一致。
- **査読 R32 (独立。3 回)**:
  - 1 回目は途中で止まった (前後比較 4,232 件までは流れ、差分は X の後ろが日本語として区切れるようになった改善が中心)。
  - 2 回目 (6,221 件。2 音以上の助詞を大文字 1 文字すべてに広げていた版): 改善 1,874 件。**P1**: ローマ字の名前を区切る回帰 28 件 (`Ishikawa` → `Iしかわ`、`Imadegawa` → `Iまでがわ`、`Ashikaga` → `Aしかが`、`Amadeusha`) と `Okara` → `Oから`・`Onara` → `Oなら`。→ 2 音以上の助詞は **頭が `X` のときだけ** に絞って直した。P2: その部分のテストが無い (足した)、コメントの位置 (直した)。
  - 3 回目 (12,872 件): **P0・P1 なし**。差分 2,450 件はすべて大文字の `X` を含む入力で、`X` を含まない入力・小文字の `x` の入力の差分は 0。変更前が正しく変更後が違う行は 0 (2 回目の回帰は全部戻った)。変異: 助詞 12 個を 1 つずつ削る・`ya` を足す・`X` 以外にも広げる・`unit.Kana == unit.Raw` を外す のうち、`made` を削る・`ya` を足す の 2 つが生き残った → `Xmadedesu`・`Xyatte` をテストに足し、2 つとも赤になることを実装者が確認した (変異を 1 つずつ入れてテストを流した)。
- **得と損**: 得: X の後ろの日本語が、1 音・2 音以上の助詞のどちらでも区切れる。損: (1) 大文字の `X` に日本語を続けて小書き文字を打つ形は使えなくなる (`Xtumo` が `っも` → `Xつも`、`Xatoukouha` が `ぁとうこうは` → `Xあとうこうは`。小文字の `x` は使える。`Xtu`・`Xa` 単独は変更前から英字のまま)。(2) 実際にはまず打たない形で、`Xmadeline` → `Xまでline`・`Xgurain` → `Xぐらいん` になる。
- **限界・未確認**: (1) **Mac 実機で確かめていない** (いま入っている IME は 20:28 のビルドで、Q9 より前)。(2) `X` 以外の大文字 1 文字の後ろの 2 音以上の助詞 (`Bkara`・`Akara`) は今までどおり英字のまま (名前を守るため)。(3) `Xmade` 単体は英字のまま (後ろに何も無いと区切らない既存の規則。`Xmadeni` は区切れる)。(4) 変更前からある別の誤り: `Xcode` → `Xcoで`、`xmas` → `xまs`。(5) Windows / Linux は対象外。
- **Q10 (記録だけ)**: `pushoyobi` → `プ書呼び` (期待は `pushおよび`)。子音で終わる英単語 (push の sh) の後ろに、母音で始まる日本語 (oyobi) を空白なしで続けると、`sho` = しょ と読まれ、全体が日本語のローマ字として読める。v1.1.5-mac のコードでも同じ (`--repro`)。空白を入れる (`push oyobi` → `push および`) と正しい。`pusho` のように日本語としても読める形との区別が要るので、前後比較で損の数を見てから判断する。管理表の Q10。

## 追加項目 Q11 (git のブランチ名 `main` が まいん になる: `mainnima-jisitehosii` → `マイン位マージしてほしい`)

- **経緯**: リョウさんの実機の報告 (2026-10-12): `mainにマージしてほしい` と打ちたくて `mainnima-jisitehosii` と打つと、`マイン位マージしてほしい` になる。リョウさんの希望: **マインドは日本語のまま (名称固定) で、main を英字で出す**。
- **原因**: `main` はローマ字としても ま・い・ん と読めるので、日本語として扱われる。`mainni` は nn がまとめて ん になり、`まいん` + `い` になる。英単語の最後の n + な行の助詞を分ける既存の規則 (python + no) は、ローマ字として読めない英単語だけが対象 (gomen を守るため)。
- **修正** (Core + 辞書。打ちながらの変換の経路):
  1. `dictionaries/english-readable.txt` (ローマ字として読めても英語にする語) に `main`・`domain` を足した。
  2. `CompositionDetector.ContinuesJapaneseWord`: この一覧の語でも、続く 1 音を足した読みが、よく使う日本語の語 (`readings.txt`)・ブランド名 (`brands.txt`) の頭になるときは英語にしない (`main` + `do` = まいんど = マインド、`main` + `ku` = マインクラフト)。readings.txt に無い まいんち (毎日のくだけた形) も足した。一覧の全語に効くが、査読の前後比較で、英字だったものがかなに戻った行は 0 件。
  3. `CompositionDetector.NotAfterJapanese` (`main`・`domain`): 同じ入力の中で日本語のすぐ後ろにあるときは英語にしない (`umaindesu` = うまいんです、`amaindayo`、`kumainda`。査読 R33 の 1 回目の P1)。英語にするのは入力の先頭 (`mainni`) と英語の後ろ (`gitmainni`・`origin/main`) だけ。
  4. `CompositionText.SplitEnglishFinalN`: 一覧の語は、ローマ字として読めても n + な行の助詞を分ける (`main` + `ni` → `mainに`、`lesson` + `ni` → `lessonについて`)。
- **結果の例** (打ちながら、Enter で確定。テストの変換エンジンはかなを返すだけ): `mainnima-jisitehosii` → `mainにまーじしてほしい`、`maindema-ji` → `mainでまーじ`、`mainnokoudo` → `mainのこうど`、`origin/mainnipush` → `origin/mainにpush`、`lessonnituite` → `lessonについて`。変わらないもの: `maindo`・`maindosetto`・`rimaindo` (マインド)、`mainkurafuto` (マインクラフト)、`mainichi`・`mainasu`・`maina-`・`mainen`、`umaindesu`・`amaindayo`・`kumainda`、`mainchi`、`remaindo`。
- **テスト**: 578 → **579 件** (全通過)。`CompositionTests.Main_StaysEnglish_ButMindAndMinecraftStayJapanese`。修正前のコードで赤になることを確認した。変換の品質の評価 (`--eval-json`) は変更前と JSON が完全に一致。
- **査読 R33 (独立。2 回)**:
  - 1 回目 (`main domain remain` を足し、日本語の後ろの守りが無い版。自作の入力 48,275 件の前後比較、差分 1,970 件): 改善 約 537 件 (n で終わる一覧の語 + な行の助詞、main + 助詞、git の文)。**P1**: `〜まい + ん` の日本語が崩れる回帰 562 件 (実在する語に絞ると 178 件: `umaindesu` → `うmainです`、`amaindayo`、`semainjanai`)。軽い回帰: `remaindo` → `remainど`、`mainchi` → `mainち`。どこにも効いていない処理 (子音だけのときの母音の補い) が変異で生き残った。→ 上の 3 を足し、`remain` を一覧から外し、まいんち を足し、効いていない処理を消した。
  - 2 回目 (同じ 48,275 件 + 新しい入力 1,153 件): **P0・P1 なし**。変更前が正しく変更後が違う行は 0 件。1 回目の回帰 562 件はすべて変更前と同じに戻った。48,275 件での差分 886 件 = 改善 399・中立 487 (どちらも誤り・意図があいまい)。変異: 日本語の後ろの守りを外す・`remain` を戻す・まいんち を外す・`before < 0` を `<= 0` にする は赤。`domain` を守りから外す変異が生き残った → `korehadomainde` → `これはどまいんで` をテストに足した。
- **得と損**: 得: 文頭・英語の後ろの `main` + 助詞が英字で残る。同じ一覧の n で終わる語 (lesson・origin・begin・kitchen・region・season …) + な行の助詞も直る。損: (1) 日本語のすぐ後ろの `main` はかなのまま (`kyouhamainnimerge` → `きょうはまいんにmerge`。うまいんです を守るため。日本語を確定してから main を打てば英字)。修正で失った 1 回目の改善は 161 件 (日本語の途中の main/domain 76 件、remain で始まる語 82 件、`push to main` など 3 件)。(2) かな側の記号の後ろ (`` `main` ``・`(main)`・`"main"`) も、かなのまま (変更前と同じ)。(3) `mainmenyu-`・`maintenansu`・`mainsutori-mu` は `main` だけが英字になる (メインは `mein` と打つのがふつうなので影響は小さいと判断)。(4) 標準でない綴りの `nnyi`・`nnye` で、かなが崩れる (`kyouhamainnyidesu` が、査読の検証ドライバでは `きょうはまいんnいです`、`--repro` では `きょうはまinnいです`。変更前は `きょうはまいんいです`。検証ドライバで 8 行。2 つの経路で崩れ方が違う理由は未確認)。
- **限界・未確認**: (1) **Mac 実機で確かめていない**。(2) 空白で区切った `main ni merge` は、Space が変換のキーなので まいん のまま (変更前と同じ。英数入力で打つか、続けて `mainni` と打つ)。(3) 同じ文の 2 回目の main (`mainnima-jisitekaramainwopushsuru` の後ろの main) はかなのまま。(4) Windows / Linux は対象外。
- **Mac 実機の確認と追加修正 (2026-10-12、JIS、TextEdit、`main` の 23:28 ビルド。リョウさんの報告)**: `Xnado`・`Xni`・`Xnoyouna`・`kyouhaXnitoukousita`・`umaindesu`・`mainkurafuto`・`10-ji ni kaigi desu`・`denwa wa 03-1234-5678desu`・`ra-menwotaberu` は期待どおり。期待と違った 3 件の原因 (学習データは `main`・`xnotoukouha` の行だけを、リョウさんの同意を得て読んだ):
  - `Xnotoukouha` → 英字のまま: 修正前の IME で `んお投稿は` になったとき、この語全体を英字として覚えていた (`languages.json` の `xnotoukouha`: English・Explicit、2026-10-12 21:40)。Q9 の修正は正しく働いている (学習なしなら `Xのとうこうは`)。学習した語を消す画面が Mac に無いので、項目 L1 (学習した語の一覧・検索・追加・編集・削除) として実装する。
  - `mainnima-jisitehosii` → `mineにマージしてほしい`: 前の行が日本語のとき、入力の先頭の `main` まで「日本語のすぐ後ろ」(`before < 0`) と判定し、まいんに → 変換エンジンが mine に変えていた。→ `NotAfterJapanese` は同じ入力の中だけで見る (`!startOfInput && before < 0`)。
  - `maindo` → `mainど`: `main` を英字で覚えていた (2026-10-11、Explicit) ので、覚えた英字の規則がマインドの守りより先に効いた。→ english-readable.txt の語は、覚えていても `ContinuesJapaneseWord` (main + do・ku・chi) のときは覚えた英字を使わない。
  - テスト 587 → **588 件** (`Main_AfterJapaneseLine_AndLearnedMain`: 学習あり / なし × 前の行が日本語 / なし。修正前のコードで赤)。`--eval-json` は変更前と一致。
  - **査読 R33 の 3・4 回目**: 3 回目 (1,280 入力 × 6 条件 = 学習あり / なし × 新規・日本語の行の後・英語の行の後): 実機の 2 件は直った。**P1**: 覚えた英字の規則に「日本語のすぐ後ろでは使わない」も足していたため、`main` を覚えている人の記号の後ろ (`` `main`ni ``・`(main)`・`refs/heads/main`) がかなになった (条件ごとに 132 件・132 件・24 件)。→ その条件を外した (覚えている人の `umaindesu` は `うmainです` のまま。Q11 より前と同じ)。4 回目: **P0・P1・P2 なし**。6 条件とも変更前が正しく変更後が違う行は 0、3 回目の P1 の行はすべて変更前と同じに戻った。変異 2 つ (覚えた英字の規則から ContinuesJapaneseWord を外す・読める英語の規則から `!startOfInput` を外す) はどちらも赤。
  - **残り**: 覚えた `main` は、日本語のすぐ後ろでも英字 (`umaindesu` → `うmainです`。Q11 より前と同じ)。`xnotoukouha` の学習は L1 の画面で消す。修正後の実機の確認は、まだ。

## 追加項目 L1 (「Meltype 辞書」に「学習した語」タブ: 一覧・検索・追加・編集・削除)

- **経緯**: Q9・Q11 の実機確認で、`Xnotoukouha` が英字のまま確定された。原因は、修正前の IME で `んお投稿は` になったとき、この語を英字として覚えていたこと (`languages.json`)。Mac には学習を 1 語だけ消す画面が無く (「学習データをすべて消去」だけ)、リョウさんの依頼 (2026-10-12): 学習一覧を GUI で表示し、検索欄・追加・削除・編集をできるようにする。
- **実装** (Core + FFI + Swift。実装はエージェント 2 人の並列 (Core・FFI / Swift の画面)、統合と検証は監督が行った):
  - **Core (`LanguageMemory`)**: IME と辞書アプリは別のプロセスで、IME はメモリに持ったままファイル全体を書き戻すので、そのままでは辞書アプリで消した語が IME の次の保存で戻る。→ ファイルの版 (更新時刻 + 長さ) を覚え、変わっていれば読み直す (`Refresh`。`Get` は 1 秒に 1 回まで、`Entries`・`Check` は毎回)。変更 (Remember・Set・Remove・Clear) は共通の `Edit()` で「ファイルのロック → 必ず読み直す → 変更 → 保存 → 失敗したら元に戻す」。`Check(word, except)`・`Set(word, english, oldWord)` を追加 (足した・直した語は Explicit・回数 2 で、すぐ有効)。壊れた `languages.json` は上書きの前に `.broken` に退避。使用中に大きすぎるファイルを読んでも、今の内容のまま続ける。
  - **ロックの扱い**: 辞書アプリの操作はロックを最大 3 秒待ち、取れなければ「ほかの画面が学習データを書き込み中です。…」を返して保存しない。IME の学習 (Remember) は打鍵を止めないよう 200 ms で諦め、ロック無しで保存して版を「不明」にする (次は必ず読み直す)。
  - **FFI**: `meltype_learned_words` (語・英字/かな・回数・最後に使った日・有効) / `_version` / `_check` / `_set` / `_remove`。**ABI を 8 → 9** (Exports・IME の NativeCore・辞書の NativeDictionary・AOT チェック)。
  - **画面 (`LearnedWordsViewController`)**: 「専門用語集」と「設定」の間に「学習した語」タブ (**⌘3**。「設定」は ⌘3 → **⌘4** に変わる、mac/README.md も直した)。説明文・検索欄 (部分一致、大文字小文字を区別しない)・表 (語 (英字) / 扱い / 回数 / 最後に使った日 / 状態 (有効 / 2 回目を待っています)、見出しで並べ替え、複数選択)・追加… / 編集… (打つたびに Core で確かめて理由を赤字で出し、だめなら OK を押せない) / 削除… (語の数と「元に戻せません」の確認)。1.5 秒ごとに版を見て、IME の変更を選択と検索を保ったまま読み直す。このタブを選んでいる間は ⌘Z がほかのタブの変更を取り消さない (空の UndoManager)。README.md・mac/README.md・mac/INSTALL.txt の学習データの消し方に、1 語ずつ消せることを足した。
- **検証**: Core のテスト 588 → **606 件** (`LearnedWordsTests` 18 件。2 つのインスタンスで同じファイルを使う「IME と辞書アプリ」の再現、ロックが取れないとき、保存の失敗、壊れたファイル、修正前に赤を確認)。`--eval-json` は変更前と一致。`build.sh --no-install`・codesign・`tools/check-mac-aot-settings.py` PASS (新しい項目 7: AOT 版の本物の別プロセスで、動いている IME の判定が消した語で変わり、その IME の次の保存で消した語が戻らない)。辞書アプリの `--self-test` PASS 166。見本データ (一時フォルダー) で開いた画面を画面写真で確認した。`tools/check-release.py` は、変更前からある dmg の README.txt:21 の 1 件だけ FAIL (この項目とは無関係)。
- **査読**: 独立査読 2 本 (Core・FFI のデータの安全性 / Swift の画面) は P0・P1 なし、P2 計 16 件 (ロックが取れないときにロック無しで書く、IME の確定が 3 秒止まる、時刻の粒度が粗いファイルシステムで変更を見落とす、保存の失敗でメモリとファイルが食い違う、壊れたファイルを退避せずに上書き、⌘Z がほかのタブの変更を取り消す、時刻の読み取りが 3,000 語で約 250 ms、同じエラーの読み上げの繰り返し など)。すべて直し、各修正を元に戻す変異でテストが赤になることを確かめた。修正後の最終査読: P0・P1 なし。16 件すべての修正を file:line で確認。Remember (直したときだけ呼ばれる) は 3,000 語で p50 9.2 ms / 最大 12.9 ms (読み直し分は p50 1.0 ms)、ロックを持たれたときは約 210 ms で先に進む。残った P2 (ロック無しの保存の損をコメントに書く) は直した。
- **得と損**: 得: 間違って覚えた 1 語を画面で消せる・直せる・足せる。IME で学習した語がすぐ一覧に出る。損: (1) IME は確定ごとに学習ファイル全体を読み直す (最大 3,000 語)。(2) 辞書アプリがロックを持ち続けると、IME の学習はロック無しで保存され、その間の変更が消えうる (人の操作では起きにくい。打鍵を止めないことを優先)。(3) ⌘3 / ⌘4 のショートカットが変わる。(4) `languages.json` の隣に `.languages.json.lock` (0600) ができる。
- **限界・未確認**: (1) **Mac 実機で確かめていない** (IME と辞書アプリの入れ替え後に、`xnotoukouha` を画面から消して `Xnotoukouha` が `Xの投稿は` になることを確かめる)。(2) 画面の操作 (⌘Z・読み上げ・シート) は画面写真の表示だけで、実際には操作していない。(3) Windows の「学習した語」の画面 (`LearnedWordsForm`) は `Remove` の戻り値の型が変わった (呼び出しは戻り値を使っていないのでコンパイルは通るはず)。Windows / Linux は対象外。

- **Mac 実機の確認 (2026-10-13、JIS、TextEdit と「Meltype 辞書」、`main` の 00:31 ビルド = `b619f34`。リョウさんの報告)**: 「学習した語」タブ (⌘3 で開く・検索で絞る・削除・追加・英字 / かなの変更・⌘4 で設定)・IME の学習の一覧への自動反映は、すべて問題なし。間違って覚えた `xnotoukouha` をタブから消したあと、`Xnotoukouha` → `Xの投稿は` (Q9 も実機で確認できた)。同じビルドで、Q11 の `mainnima-jisitehosii` → `mainにマージしてほしい` (前の行が日本語でも)・`あああmainにマージしてほしい` (`main` を英字で覚えているので、日本語のすぐ後ろでも英字。覚えていなければ `あああまいんに…` が設計どおり)・`maindo` → `マインド`、Q6 の再変換 (`- kyou wa ii tenki desu` を 3 回クリックで選んで Shift+Space、行頭の `- ` が残る) も問題なし。
- **記録した既存の問題 (Q12、直していない)**: `maindo` を打つ途中の表示が `まいんd` → `まindo` になる (確定すると `マインド`)。Q6 より前のコード (`26184cb`) でも同じ `まindo` で、後ろの `indo` を英単語 (indoor など) の打ちかけとして仮に英字で見せる既存の仕組みによる (`--repro … none` で再現)。`main` を打った瞬間に `main` と見えるのは Q11 の修正どおりの新しい表示。英単語の打ちかけの表示は途中表示全体に効くので、直すなら前後比較と査読をして別に行う。

## 追加項目 Q13 (アプリ内の更新で入れ替えたあとも、古い版の IME が動き続ける: 1.1.3 以降は「新しい版があります」も残る)

- **経緯**: リョウさんの報告 (2026-10-13、画面の写真つき): 入力メニューの「新しい版があります (v1.1.6)…」から 1.1.6 に更新したのに、そのあとも同じ表示が出る。
- **確認したこと** (読み取りだけで確かめた。時刻は JST。`update.json` の `lastCheck` だけは UTC で `2026-10-13T00:46:18Z`): 入れ替え後の `~/Library/Input Methods/Meltype.app` は 1.1.6 (ビルド 22)、`update.log` の `install.sh` の出力も「入れ替えました。…」で終わっている。一方、動いている IME のプロセスは、更新 (09:46) より前の 00:38:17 に起動したままで、掴んでいる実行ファイルの inode (133916391) が、今ディスクにあるもの (134005697) と別 (入れ替え前のファイル)。`log show` では、更新の完了ダイアログが閉じた 09:46:18.395 の直後に、同じプロセスが通信を始めている (タイムアウト 15 秒 = `check()`)。
- **表示が戻った仕組み** (コードを読んだ。`log` の文面は `<private>` で読めないので、`detachedInstallDidEnd` という関数名は推定): `performUpdate` は `install.sh` の起動に成功した時点で `offer = nil` にして、メニューの項目をいったん消す。`install.sh` が終わっても IME が止まっていなかったので、`detachedInstallDidEnd` が GitHub へ再確認し (自動の確認が ON のときだけ通信する。この Mac は `update.json` が `enabled: true`)、プロセスがメモリに持つ旧版の版番号 (`currentVersion` = `Bundle.main` の Info.plist の値。再表示されたことから、ディスクの新しい値は読んでいないと分かる) と比べて、同じ v1.1.6 をもう一度 `offer` にした。
- **原因**: `install.sh` の `pkill -x Meltype` が、動いている IME を止めていなかった。macOS の `pkill` は、既定で「自分自身と、その祖先」を対象から外す (`man pkill` の `-a`)。更新機能は、IME が切り離した `bash -c` を起動し、その中で `install.sh` を実行する (IME → bash -c → bash install.sh。`POSIX_SPAWN_SETSID` で新しいセッションにしても、親は変わらない) ので、IME は `install.sh` の祖先にあたる。旧 IME は SIGTERM を無視も捕捉もしていない (カーネルの `p_sigcatch` / `p_sigignore` を、小さな C で `sysctl KERN_PROC_PID` から読んで確認。ブロックの設定は読めない) ので、届いていれば止まっていた。シグナルが届いてブロックされた可能性は、ログからは区別できない。ただし、同じ Mac の 00:31:23 には、00:31 の入れ替え (L1 の実機確認のビルド) の `pkill` と考えられるものが、旧々 IME (pid 15787) を SIGTERM で止めた launchd の記録 (`exited due to SIGTERM | sent by pkill`) があり、更新の 09:45:30〜09:47:00 にはその種の記録がない。ターミナルから手で `install.sh` を実行した形 (祖先に Meltype がいない) は、身代わりの検査 (2) で止まることを確かめた。本物の IME が動いている状態での手での実行は、今回は見ていない。気づけなかった理由は、P15 の節が「`pkill` は IME だけを止める」と書いたまま、「更新する」から入れ替え完了までの一連を実機で未検証としていたこと (P15 の節に訂正を入れた)。
- **症状の出方は版で違う** (コードを読んだだけ): 旧 IME が止まらない原因は、更新機能が使える 1.0.10 以降すべて同じ。「新しい版があります」が再び出るのは、`detachedInstallDidEnd` が入った 1.1.3 以降 (コミット `234ccd3`)。1.0.10〜1.1.2 は、`install.sh` のあとも `isUpdating` が true のままなので、項目が消えたまま旧版が動き続け、以後の確認も止まるはず。実機で見たのは、1 台の 1.1.6 への更新だけ。
- **修正** (`mac/install.sh` の 1 行): `pkill -x Meltype` → `killall Meltype` (名前が完全一致するものだけを止める。祖先も対象)。`install.sh` と `Updater.swift` のコメント、`mac/README.md` の説明、`docs/RELEASE.md` の公開前の検査 (`tools/check-install-stop-ime.py` を 1 行足した) も直した。**更新のときに動く `install.sh` は、ダウンロードした zip の中のもの**なので、この修正を含む版が出れば、それより古い版の IME を使っている人も、その版への更新から効く。
- **テスト**: `tools/check-install-stop-ime.py` (新規)。`install.sh` から Meltype を止める行 (`pkill` / `killall`。行末のバックスラッシュで続く行は、bash と同じく、バックスラッシュのあとに空白がない場合だけまとめて扱う) を取り出し、名前だけを身代わり (`MeltypeD<pid>`) に置き換えて、身代わりの実行ファイル (`xcrun cc` で作る C のプログラム) に対して実行する。「止まった」は、身代わりがシグナル (SIGTERM・SIGKILL など) で終わったこと。(1) 祖先: 身代わりが更新機能と同じ起動のしかた (`POSIX_SPAWN_SETSID | POSIX_SPAWN_CLOEXEC_DEFAULT`・標準入力は `/dev/null`・固定の PATH) で bash を起動し、その bash が止める行を実行 → 身代わりが止まり、止まったあとも bash が最後まで動くこと。(環境変数の HOME・LANG と、標準出力の行き先 (`update.log`) は本物と違う。本物の `bash -c` の中の `bash install.sh` の 1 段は省いている。どちらも、祖先かどうかには関係しない)。(2) 祖先ではない: 手で実行した形で止まること。(3) 名前の完全一致: `MeltypeD<pid>X` を止めないこと。(4) 止める相手がいない: `set -euo pipefail` の下で、止める行のあとに進めること (`|| true` が要る)。(5) 位置 (静的): 入れ直しの分岐の rsync の後・`FIRST_INSTALL=0` の前にあり、rsync の行と同じ字下げ (`if` や関数の中に入れていない) で、間に `exit` / `return` がないこと。取り出す行は、想定した形 (`pkill` / `killall` + オプション (`-a`・`-x`・シグナルの番号か、`HUP`・`INT`・`QUIT`・`KILL`・`TERM` の名前だけ。`pkill -v` は名前が一致しない自分のプロセスすべてに送るので許さない) + 名前 + 任意の `2>/dev/null || true`) だけを実行し、想定外の形・取り出せない書き方は実行せずに FAIL にする。CRLF や UTF-8 でないファイルは検査せずに止める。本物の Meltype と辞書の管理画面には触らない。
- **結果**: 修正前の `install.sh` は (1) で赤 (身代わりが生き残る)。`killall Meltype` と `pkill -a -x Meltype` は緑。検査の判定の部分 (オプションの許可リスト・継続行・字下げ・`exit` の切り出し) は、プロセスを動かさない単体確認 (一時のスクリプト) でも確かめた: 修正前の検査は `pkill -v` と `killall -m` を通し、`\` の後ろに空白がある行を誤って連結していたが、直した検査はどれも弾く。査読者の変異体に通した (本物の Meltype は止めていない): 1 回目の 16 個 (m01〜m16) は赤 14・緑 2 (緑は、正しい書き方の `killall -KILL` と `pkill -a -x '…'`)。2 回目の 67 個は赤 36・緑 31。3 回目の 32 個は赤 19・緑 12・確かめられず 1 (Latin-1 のファイル。意図どおり終了コード 2)。間違った書き方 (`|| true` なし・`command pkill -x Meltype`・`pkill -x 'Meltype'`・何も止めない `killall -s TERM Meltype`・止める行なし・止める行の前後の `exit`・rsync より前・初回インストールの分岐だけ・`if` や関数の中の死んだコード・隠れた `pkill` を別の行に足した版・`\` の後ろに空白がある継続・CRLF) は、すべて赤。正しい書き方でも、取り出せない・想定外の形 (絶対パス・変数経由・関数・`killall -m '^Meltype$'`・`killall -v`・`>/dev/null 2>&1`・同じ行に別のコマンド・行末コメントの `\`) は、安全側で赤。緑は、正しい書き方 (`-KILL`・`-HUP`・`--`・空白の違い・引用符・`|| true` だけ・行の継続・タブ字下げ・`FIRST_INSTALL=0` の字下げやコメントの違いなど) と、見つけられない限界 (`{ exit 0; }` や `true; exit 0` で抜ける書き方、条件つきの `exit`、字下げをそろえたままの `if false`、here-document に包んだ形)。止めたあとに続く操作 (osascript のダイアログ・`mdfind`・一時フォルダーの削除) は、親が止まったあとの bash でも動いた (一回きりの確認)。Core のコードは変更なし。わざと壊した版を流すスクリプトは一時のもので、リポジトリには入れていない。
- **査読 R34 (独立、4 回。各回 2 本: コード・検査の敵対的査読と、この記録の事実確認)**: 1 回目: 原因の説明は独立に再確認された (ほかの説 — `pkill` が名前に一致しなかった・SIGTERM のあとで IME が復活した — は、`pgrep -x` が名前に一致すること・launchd に SIGTERM の記録がないこと・pid と起動時刻の不変で除外。「シグナルが届いてブロックされた」は、ログからは区別できず残る)。P0 なし。記録の P1 4 件 (offer が「消えなかった」は誤り・「1.0.10 以降すべて同じ症状」は 1.1.3 以降だけ・管理表の書きかけの状態・P15 の古い節の訂正) と、検査の P2 は直した。2 回目: P0 なし。P1 2 件 — 検査: `xcrun --find cc` が返したコンパイラの実体を直接呼んでいたので、SDKROOT が無い環境 (Homebrew の python など) でコンパイルできず終了コード 2 になった (`xcrun cc` 経由にして、同じ環境で通ることを確かめた)。記録: 変異体 m03 (`command pkill -x Meltype`) を「正しい書き方」に分類していた (`command` は `pkill` の動きを変えないので、元のバグそのもの。間違った書き方に移した)。P2 は直した (行の継続・想定の形の拡大・身代わりがシグナルで終わったら「止まった」とみなす・止める行の字下げと前の `exit`・CRLF・読めないファイル・`RELEASE.md` の文言・P15 の訂正の範囲と書式・題とコメントの「1.1.3 以降」・得と損・限界の言い回し)。直さなかった P2: `install.sh` の「16 文字を超え `pkill -x` では合わない」というコメントが、今の macOS では古い可能性 (変更対象外。`pkill -f` を使う実装には影響しない)、検査の python が SIGTERM で落ちたときの一時フォルダーの残り (身代わりは 120 秒で自然に終わる)。3 回目: P0 なし。コードに P1 なし。記録の P1 1 件 — 変異体 `killall -s TERM Meltype` を「正しい書き方」に分類していたが、macOS の `killall -s` は、`-v` と同じ表示を出すだけでシグナルは送らない (`-v` は送る) ので何も止めない (間違った書き方に移した)。検査の P2 4 件 — `pkill -v` を実行してよい形として通していた (実行すると、本物を含む自分のプロセスのほとんどに飛ぶので、オプションを許可リストにした)・`\` の後ろに空白がある行を継続として連結していた (bash は継続にしない)・`exit;` やタブ区切りの `exit` を見逃していた・バックスラッシュの数え方 (奇数個だけ継続)。あわせて、P15 の訂正のあとに残る文の注記・テスト (5) の説明・R34 の言い過ぎも直した。4 回目 (差分だけの最終確認): P0・P1 なし。コードの P2 3 件 — here-document に包んだ止める行が検査をすり抜ける (限界の一覧に書いた)・SHAPE が bash の解釈と食い違う形 (Unicode の空白・対でない引用符・`killall -a`) を受理する (実行すると赤になるだけで、本物を巻き込む経路はないので、直さず限界に書いた)・`exit||true` を拾わない (「粗い検査」の限界の内)。記録の P2 2 件 (`killall -s` の言い回し・シグナル名の範囲) と P3 1 件 (検査の冒頭コメントが古い) は直した。この最後の差分は文言とコメントだけで、検査のコードは 4 回目に見てもらった版のまま (構文木の比較で確かめた)。
- **得と損**: 得 = 更新のあと、IME が止まり、次のキー入力で新しい版が起動するはず (設計どおり。実機では未確認)。損 = (1) 更新の瞬間に IME が止まるので、入力中の変換は 1 回途切れる (手で入れ替えるときと同じ。変換中の文字が確定されるか消えるかは未確認)。(2) すでに 1.1.6 へ更新した人と、1.1.6 の `install.sh` を使う更新は直らない。この修正を含む版を出すまで、1.1.6 へのアプリ内の更新は全員が、旧版の IME が動き続ける状態になる (見え方は版で違う。上の「症状の出方は版で違う」を参照。1.1.6 の完了ダイアログは「次にキーを打ったときから新しい版になります」と出るが、実際は旧版のまま)。直し方は、ログアウトか再起動 (推測。未確認)、またはターミナルで `killall Meltype`。(3) `build.sh`・`uninstall.sh`・pkg の `postinstall` の `pkill -x Meltype` は直していない (どれも IME が起動した側ではないので、祖先にならない。`docs/SECURITY_REVIEW_2026-10.md` の `install.sh` の記述は当時のまま)。
- **限界・未確認**: (1) **本物の更新で IME が止まることは、まだ見ていない。** この修正を含む版を公開したあとの最初の更新が、最初の実機確認になる。**1.1.7 として 2026-10-13 に公開した** (タグ `v1.1.7-mac` = `0cd131a`。添付は zip と dmg。GitHub の digest は手元の値と一致、`check-release.py` を digest と `--ref v1.1.7-mac` つきで実行して PASS 97 / FAIL 0)。リリースノートと X の下書きは、別に独立査読を 3 回 (P0・P1 なし。確かめていないことは本文に「確かめていません」と書いた)。公開したあとに、旧版の IME で「更新する」を試した結果は、まだ無い。(2) 本物の IME (名前 `Meltype`) を止める所は、身代わりの名前で確かめた (本物を止めると、その Mac の日本語入力が途切れるため)。(3) macOS 26.6.2 でだけ確かめた。macOS 13〜15 では、`pkill` の祖先の除外も `killall` の動きも見ていない。(4) `Updater.swift` はコメントだけの変更で、`swiftc -parse` の構文検査だけ (`build.sh` は未実行)。(5) 入れ替え後、旧プロセスが新しい版のリソースを読むことで不具合が出ないかは見ていない。(6) 今回の旧 IME の版は、置き換えたファイルが読めず直接は未確認。L1 の実機確認の記述から、main の 00:31 のビルド (`b619f34`。Info.plist は 1.1.5 のビルド 21 で、`detachedInstallDidEnd` を持つ) のはず (推測。再表示が出たことと矛盾しない)。(7) 検査は `install.sh` 全体を動かさない (`lsregister` が本物の登録に触れるため)。止める行の位置は、字下げと `exit` / `return` の有無で見る粗い検査で、字下げをそろえたまま呼ばれない形にしたもの (呼ばれない関数の定義・here-document や複数行の文字列の中・`{ exit 0; }` で抜ける書き方など) は見つからない。検査には Command Line Tools の C のコンパイラが要る。Windows / Linux は対象外。

## 追加項目 0-1 (評価基盤 `eval/` と本家 Core との比較)

- **目的**: 計画書 `docs/STRATEGY_2026-10.md` の柱 A・施策 0-1。同じ正解表で、本家 Meltype の Core と Mac 版の Core の判定結果を並べ、回帰 (本家や前回で当たっていたのに今は外れる例) を 0 件にするゲートを持ちます。公開 (push・README への反映) はこの項目に含めません。
- **作ったもの**:
  - `eval/README.md`、`eval/cases/upstream-quality.tsv`・`mac-device.tsv`・`mac-terminal.tsv` (正解表。1 行 = 打鍵・期待・比べ方 (`full`/`ascii`/`nojp`)・出典)。
  - `src/Meltype.Core.Tests/Repro.cs` (`Checks.Expect` を 4 列形式に対応。`ParseCases`・`Judge`・`ExpectAll` を追加)、`Program.cs` (`--expect-all <dir> <out.json>`)、`EvalFormatTests.cs` (新規。10 件)。
  - `tools/eval-compare.sh`・`tools/eval_compare.py` (key = `file + typed + 何回目か` ごとに `broken`/`fixed`/`added`/`removed`。`broken` があれば終了コード 1、入力・dotnet の失敗は 2)、`tools/eval_legacy_base.py` (`--expect-all` が無い本家の作業ツリー用)。
  - `docs/DEVELOPMENT.md` に「評価基盤 (`eval/`)」の節。Core 本体 (`src/Meltype.Core/`) は変えていません。
- **最初の計測** (Mac 版 = この作業ツリー、base `0556cca`。本家 = `a4b6f1a` の clone のコピー。BuiltIn の英単語表)。査読 1 回目の修正 (full の大小の扱い・ascii の語・nojp の範囲・`nojp` 行の `full` 化) のあとに取り直した数字:

  | ファイル | 本家 | Mac 版 |
  |---|---|---|
  | `upstream-quality.tsv` | 203/203 (100.0%) | 163/203 (80.3%) |
  | `mac-device.tsv` | 24/38 (63.2%) | 37/38 (97.4%) |
  | `mac-terminal.tsv` | 15/20 (75.0%) | 16/20 (80.0%) |
  | 合計 | 242/261 (92.7%) | 216/261 (82.8%) |

  - **`broken` (本家で当たり、Mac 版で外れる) は 40 件**。すべて `upstream-quality.tsv` で、本家の修正のうち Mac 版にまだ入っていないもの (`-pedia` の語 (#207)、助詞「や」+ 英単語 (`yapython`)、英字に挟まれた「と」(`devtoprod`)、メールアドレス (`tanaka@example.com` が `たなか@example.com`)、`my name is taro`・`apinoerror`、`i18n`・`l10n`・`h2o`、`site PR` など)。**回帰ゲートは今は赤**で、本家の判定修正を取り込んでいくと下がります。
  - `fixed` (本家で外れ、Mac 版で当たる) は 14 件 (`mac-device.tsv` の Q4・Q9・Q11・Q6/Q7・P29 の修正 13 件と、`mac-terminal.tsv` の `gemma4-27b` 1 件。Mac 版が先に直したもの)。
  - `mac-terminal.tsv` で Mac 版が外すのは `gpt-oss`→`gpt-おっs`、`python3 -m venv .venv`→`python3ーmvenv`、`npx create-next-app`→`create-next-あっp`、`cd ~/Documents/Meltype`→`cd～/Documents/Meltype` (全角の `～`。本家は空白が落ちて `cd~/…`) の 4 件 (後ろの 3 件は新しく見つかった外れ)。
  - 自前のテストは **616/616 通過** (この作業ツリーの変更前は L1 のあとの 606 件。新規の `EvalFormatTests` は 10 件)。`--expect-all` の結果を `eval_legacy_base.py --rejudge` で判定し直して食い違い 0 件 (261 件)。`tools/check-mac-aot-settings.py` は PASS (5 項目。`mac/build.sh --no-install` で作った dylib を使用)。
- **本家 `Score` と `full` の違い**: 本家の採点 (`QualityTests.cs` の `Run()`、`actual == Expected`) は完全一致です。`full` は本家の `Expect` と同じ形で、期待にカタカナがあるときだけ「カタカナ→ひらがな」と「英字の大小の無視」をし、それ以外は大小も区別します (査読 1 回目の修正)。本家の `--eval-json` は全件当たり (1337/1337)、`upstream-quality.tsv` も本家で 203/203 になり、この差で結果が変わる行はありませんでした。本家の品質テストには「直前の文字 (`Before`)」を持つ 13 件があり、4 列の形式には直前の文字の列が無いので、TSV には `#` のコメントで写しただけで判定していません (216 − 13 = 203 件)。
- **旧 `--expect` の挙動**: `#` で始まらない空でない行に 4 列の行が 1 つも無い表 (Pull Request のチェックが使う) は、変更前の `Expect` と同じです: `#` の行も読み、2 列でない行 (見出し・3 列の行) は黙って捨てる (変更前と同じ)、期待に漢字があれば英字の語の並び、無ければ完全一致 (カタカナの正規化・大小無視なし)。4 列の行が 1 つでもある表は新しい読み方 (`#` と空行を無視、列数・比べ方・かな打鍵行の比べ方を検査) です。`--expect-all` は常に 4 列として読みます。出力 JSON には、これまでの `kind` も残しています。
- **`nojp` と `full`**: `nojp` の「日本語」は U+3000〜30FF・U+3400〜9FFF・U+FF00〜FFEF (全角記号・半角カナを含む)。期待が打鍵そのものの行 (`mac-terminal.tsv` の 12 行、`mac-device.tsv` の P34 の 7 行) は `full` にしました (`nojp` は `ー` 以外の綴りの崩れを見逃すため)。`ascii` の語は印字可能 ASCII の連続 (`[!-~]+`) で、`gemma4-27b` が `gemma4ー27b` になる崩れも見つけます。`ollama run`→`るん`・`gemma4ー27b` は実機で既知の外れで、Mac 版 Core のテスト用キーボードでは再現しません (`ollama run` は本家の Core でも再現せず、`gemma4ー27b` は本家の Core では再現します)。
- **未検証・限界**: 実機は関係しません (Core のテスト用キーボードの結果。変換エンジンも Mac の入力欄も通っていません)。`mac-device.tsv` の期待は文書の記録から写したもので、打鍵が書かれていない行は期待から復元しました (出典に明記)。`mac-terminal.tsv` の期待は「望ましい出力」で、実機で確かめたものではありません。`/tmp` のようにシンボリックリンクを通る絶対パスで `dotnet run --project` を呼ぶとビルドが失敗することがあり、スクリプトは相対パスで呼びます。Windows / Linux は対象外です。

## 追加項目 P45 (本家の判定修正の取り込み: broken 40 件)

- **目的**: 計画書 [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の柱 A・E。`eval/cases/upstream-quality.tsv` のうち、本家 Core で当たり、Mac 版で外れる 40 件を、本家の該当コミットを**手で移植**して直す。1 本家コミット = Mac 版の 1 コミット。Mac 版の `CompositionDetector.cs`・`CompositionText.cs` は独自の改変が入っているので、`git apply` は使わず、本家の差分 (`git show <hash>`) を読んで、意図を Mac 版のコードに合わせて書いた (旧ブランチ batch/p45 から取り直す段階で `cherry-pick` した)。本家にあって Mac 版に無い `ScoredSegmentation.cs` への変更は対象外。
- **進め方 (毎コミット)**: (1) Core のテスト全通過、(2) `--expect-all eval/cases` を回す、(3) 取り込み前の Mac 版の結果 (216/261) と `tools/eval_compare.py` で比べて **broken 0** (取り込み前に当たっていた例を 1 つも壊さない)、(4) 本家の結果との差 (broken の残り) を見る。**broken が 1 件でも出たら、そのコミットは取り消して見送る。条件を足して通そうとしない** (同じ種類の回帰が繰り返し出る修正は撤退する、が方針)。eval に無い既存テストが落ちたときも同じ扱い。
- **査読 R35 (Opus、敵対的) での差し戻し**: 最初の版は本家 9 コミットを Mac 版 9 コミットで入れていた (直った例 20 件)。R35 が 576 行の前後比較をして、**悪化 7 系統**を見つけた。そのうち `-pedia` (n=8・9) は、`kyouhawikipediadeshirabeta` が `きょうはwikipediaでしらべた` でなく `kyouhawikipediaでしらべた` と、**前の日本語ごと英字になる**悪化だった (eval の 261 件には無い形)。**本家の a4b6f1a も同じ出力になる**。本家への報告は判断待ち。そこで n=8・9 の 2 コミット (2a1ccb9・40bcad9) を外し、3dbc7e9 から新ブランチ batch/p45b を作って 7 コミットを取り直し、守りのコミット 1 つ (P45-12) を足した。
- **取り込んだもの** (本家 7 コミット → Mac 版 7 コミット、直った例 12 件):

| n | 本家 hash | 内容 | 直った例 | Mac 版のコミット |
| --- | --- | --- | --- | --- |
| 1 | ac36ced (#406) | `english-readable.txt` に `overcute` (本家の差分は 1 語。仕様の「3 語」とは違う) | 1 (overcute) | fde8902 (P45-1) |
| 2 | b2e33ca (#153) | `thin` などが `てぃ` の綴りで続きと読めるなら、カタカナ語の途中にする | 1 (hosuthingu) | 29872b5 (P45-2) |
| 3 | 82094f0 (#77) | 英単語の最後の t と次の s を、読めない子音が残っていても つ にしない | 1 (reflectsareta) | af19636 (P45-3) |
| 5 | 9dc873d (#104) | 英単語の最後の l + tu/tsu を っ として読み直す (`RereadSmallTsuAfterEnglishL`) | 3 (moraltute・moraltsute・kyouhamoraltute) | f61cae3 (P45-5) |
| 7 | d0dca04 (#59) | メールアドレスの @ より前も英字のままにする (`UserNameEnd`) | 4 (tanaka@・yamada.taro@・taro+news@・yamada-taro@) | 1ba9488 (P45-7) |
| 10 | fb24a53 (#129) | 大文字の略語の後ろのローマ字を日本語に (`IsAcronymWithLowerTail`) | 1 (AIdekiru) | f09e8fb (P45-10) |
| 11 | 35beb60 の ts の部分だけ | 日本語の途中の英単語で `つ` が崩れるのを直す (`ReadsAsJapaneseWithTsu`) | 1 (teishutsushita) | d507d90 (P45-11) |

  - 守りのコミット: 4534aab (P45-12。回帰の守り。下の「テスト」)。
  - n=11 は一部だけ取り込んだ (ts の部分)。取り込んだ部分と残した部分を、コミットメッセージにも書いた。
- **見送ったもの**:

| n | 本家 hash | 見送った理由 (壊れた例) |
| --- | --- | --- |
| 4 | c6b281e (#69) | 直る例は `inviteshimashita`・`invitewookutta` の 2 件だが、`githubdevote` (期待 `githubでvote`) が壊れた (eval の broken 1 件) |
| 6 | b4484c4 | eval は broken 0・3 件直る (`i18n`・`l10n`・`h2o`) が、既存テスト 2 件が落ちた: `video.mp4woMP3ni` が `MP3に` でなく `MP3ni` のまま、`今日は3かける4を` が `今日は3×4を` でなく `きょうは3かけru4wo`。`NumeronymEnd` (英字 1〜2 + 数字 1〜2 + 英字 1〜3) が `MP3|ni`・`3|kakeru|4` に当たる。本家は `ScoredSegmentation.cs` 側にも手を入れていて別の守りがある |
| 8・9 (`-pedia`) | 3cb0758 の pedia の部分・fd66db0 + f1a190e (#207)。Mac 版は 2a1ccb9・40bcad9 | **一度入れたが、査読 R35 で外した**。直る例 8 件 (pedia・protopedia・conservapedia・worldpedia・wikipedias・protopedias・Protopedia・Protopedias) は直らないまま。`kyouhawikipediadeshirabeta` が `きょうはwikipediaでしらべた` でなく `kyouhawikipediaでしらべた` と、前の日本語ごと英字になる悪化があるため。本家の a4b6f1a も同じ。本家への報告は判断待ち。**`-pedia` を入れ直すなら、前の日本語を巻き込まない条件を先に決める** |
| 8 の残り | 3cb0758 の `や` の部分 + ffd7e6a | 直るはずだった `githubya`・`yapython`・`yacursor`・`yaslack`・`yastack`・`yaplay`・`yasync` (7 件) は直らないまま。全部入れると eval の broken 3 件 (`delight`・`devote`・`morally`) と既存テスト 4 件 (`Today I go susi` が `とday`、`Xyatte` が `Xやって`、`kaibunsyorta2026kotosimo`、`Please use TanakaMakoto today` が `とday`) が壊れた。ffd7e6a を外して `や` の部分だけ残しても `Xyatte`・`kaibunsyorta2026kotosimo` が壊れた |
| 11 の残り | 35beb60 の `と` の部分 | `devtoprod`・`edittopub`・`dottonet`・`rdstoredshift`・`uikittoswiftui` (5 件) は、本家 74ca494 (英字のあいだの「と」を助詞にする、#154) が前提。Mac 版にその機能が無い。別項目として 74ca494 から取り込む候補。`delight`・`devote`・`morally` の部分は見送った ffd7e6a の修正 |
| 12 | 8dfec9d | `sitePR`・`site PR` が直る (eval で 2 件) が、既存の英文 `make sure you have time` が `まけ すれ よう have time` に壊れた (eval の broken 1 件)。`sure` が `すれ` (する活用) と読めるため |
| 13 | (未特定) | メール 3 件 (`tanaka@example.com` ほか) は n=7 で直った。`my name is taro`・`apinoerror` は、本家で `git log -S taro`・`-S apinoerror` (Core) を調べたが、直した修正は見つからなかった (当たるのは最初の公開コミット・テスターの報告・n=7 の d0dca04 など)。`taro` は辞書にも無い。**未特定** |

- **計測** (`eval/cases`、Mac 版 Core のテスト用キーボード。実機は通っていない):

| ファイル | 取り込み前 | 取り込み後 |
| --- | --- | --- |
| upstream-quality.tsv | 163/203 | 175/203 |
| mac-device.tsv | 37/38 | 40/41 (R35 の 3 件を追加) |
| mac-terminal.tsv | 16/20 | 16/20 |
| **合計** | **216/261** | **231/264** |

  - 合計の母数が 261 → 264 なのは、P45-12 で足した新規 3 件 (`mac-device.tsv` の R35 のブロック) の分。3 件とも当たっている (`mac-device.tsv`・`mac-terminal.tsv` は、新規 3 件のほかは変わらない)。
  - 取り込み前の Mac 版に対する比較: **broken 0 / fixed 12 / added 3**。
  - 本家に対する比較 (`upstream.json` で当たり、今 Mac 版で外れる): **broken 40 → 28**。残りの 28 件は、見送りの表の例 (`-pedia` 8 件・`i18n`・`l10n`・`h2o`・`inviteshimashita`・`invitewookutta`・`や` 7 件 (`githubya` など)・`と` 5 件・`site PR`) と、`my name is taro`・`apinoerror`。
  - 途中の版の結果は手元の一時ファイル (非公開、版ごとの JSON) に残してある (見送りの n=4・6・12 は壊れた版の結果)。
- **テスト**: Core のテストは 617 → **618 件** (`AcronymThenRomaji_IsJapanese` を追加)、全件通過。品質テスト (`Quality`) の行は、各 n で本家のテストの行をそのまま足した。`FeedbackTests` の辞書の語の形の検査 (`Q3_EnglishReadable_Dictionary_EntriesAreWellFormed`) は、`overcute` だけを例外にした (全体ではローマ字として読めないが、`over|cute` と 2 語に割れて `overくて` になるため)。`FeedbackTests` の `taro@gmail.com` の期待は、本家と同じく `たろ@gmail.com` から `taro@gmail.com` に直した。P45-12 で足した**回帰の守り**:
  - `mac-device.tsv` に R35 のブロック: `kyouhawikipediadeshirabeta` → `きょうはwikipediaでしらべた`、`anowikipedia` → `あのwikipedia`、`ato@hitori` → `ato@hitori` (トレードオフ。本家と同じ)。
  - `QualityTests.cs` に `kyouhawikipediadeshirabeta` (混在) と、本家 35beb60 の「英語」行 6 件 (`delight`・`devote`・`morally`・`chottodehijacked`・`korekarastories`・`mottopython`。6 件とも通る)。
  - `FeedbackTests.cs` に `ato@3nin` → `あと@3nin` (P45-7 の `domainStarts` の条件を `IsAsciiLetterOrDigit` に変えると赤になることを確かめて戻した)。
  - P45-2 の守り: `kyouhathinkdesu` → `きょうはthinkです`、`kyouhathinkpad` → `きょうはthinkpad`。ただし P45-2 の条件 (`AnalyzeFragment(lower + next).IsValid`) を `true` にしても赤にならなかった (`thinly`・`thinner`・`things` も同じ)。**この条件だけで結果が決まる例は見つかっておらず、テストの穴として残る**。なお `kyouhathinkpaddesu` は `きょうはthinkぱっです` になり (本家も同じ)、`thinkpad` は残らない。
- **ほかの項目との干渉の確認**: n=1 は Q11 の `NotAfterJapanese`・`ContinuesJapaneseWord` に当たらない語 (`umaindesu` → `うまいんです` のまま、`korehaovercutedayo`・`overcutenanda` は英字のまま)。n=7 は P29 の `DotSuffixEnd` と別の関数 (`UserNameEnd`) で、`ato@3nin` は `あと@3nin` のまま。**`ato@hitori` は `ato@hitori` になる**: 日本語の語の後ろの `@` は、メールアドレスの一部と読むトレードオフ (本家と同じ)。数字で始まる `ato@3nin` は `あと@3nin` で守っている (上の守りのテスト)。n=10 は Q9 (`X` の後ろ)・P34 (記号入りの名前) の `Xnotoukouha` → `Xのとうこうは`・`Anisiyouka` → `Aにしようか` が `--repro` で変わらない。
- **実機で確かめること** (TextEdit で打って Enter。かなは漢字に変換されてよい。**実機は `NSSpellChecker`、eval と `--repro` は同梱の英単語表なので、`IsKnownEnglishWord` を使う n=3・5・10・11 は答えが違うことがある**):
  1. `overcute` → `overcute` (`overくて` にならない)。あわせて `umaindesu` → `うまいんです` が変わらないこと
  2. `hosuthingu` → `ほすてぃんぐ` (`ほすthinぐ` にならない)
  3. `reflectsareta` → `reflectされた`
  5. `moraltute` → `もらって`、`kyouhamoraltute` → `きょうはもらって`。`hoteltukau` → `hotelつかう` のまま
  7. `tanaka@gmail.com` → `tanaka@gmail.com`、`yamada.taro@example.co.jp` → **そのまま** (`たなか@…` 系にならない)。`ato@3nin` → `あと@3nin` のまま
  10. `AIdekiru` → `AIできる`、`AInituite` → `AIについて`。`Xnotoukouha` → `Xのとうこうは` のまま
  11. `teishutsushita` → `ていしゅつした`。`kyoucommitsuru` → `きょうcommitする`
  - R35 の形: `kyouhawikipediadeshirabeta` → `きょうはwikipediaでしらべた` (前の日本語が英字にならない)、`ato@hitori` → `ato@hitori` (トレードオフの確認)
- **限界**: Mac 版 Core のテスト用キーボードの結果で、変換エンジン・Mac の入力欄は通っていない。同梱の英単語表と `NSSpellChecker` の差は未確認 (上の「実機で確かめること」)。取り込んだ修正は eval の 264 件と Core のテスト 618 件では broken 0 だが、取り込み前後の大規模な比較 (P29 のような数万件) は行っていない (R35 の 576 行の比較が唯一の広い比較)。見送りの判断は「eval か既存テストが 1 件でも壊れたら撤退」で機械的に行い、壊れない条件を探すことはしていない。`-pedia` のように eval にも既存テストにも無い形の悪化は、この方針では見つからず、査読で見つかった。
- **実機確認 (2026-10-14、macOS 26.6.2、JIS、TextEdit、ビルド 08:08 = main 6b0dca9)**: 上の 1〜11・R35 の全項目が期待どおり (`overcute`、`ホスティング`、`reflectされた`、`今日はもらって` / `hotelつかう`、`yamada.taro@example.co.jp` / `あと@3nin`、`AIできる` / `Xの投稿は`、`提出した` / `きょうcommitする`、`今日はwikipediaで調べた` / `ato@hitori`)。`umaindesu` だけ `うmainです` になったが、これはリョウさんの Mac で `main` が英語として学習されているため (Q11 に記録済みの既知のコスト。学習した語から `main` を消せば `うまいんです` に戻る)。P45 の変更とは無関係。→ **完了**。
- **Windows・Linux は対象外** (Mac 版の Core の変更だが、確かめたのは Mac 版の Core だけ)。

## 追加項目 0-2 (本家追従の半自動化: `tools/upstream_watch.py`)

- **目的**: 計画書 [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の柱 E・施策 0-2。本家 Meltype の新しいコミットを毎回手で読み比べる代わりに、PR 単位の候補表 ([UPSTREAM_WATCH.md](UPSTREAM_WATCH.md)) と、本家の新しい正解ケースの候補ファイル (`eval/candidates/upstream-<日付>-<since の短い hash>.tsv`) を、コマンド 1 つで作る。
- **自動にしたこと**: (1) 本家の clone の fetch、(2) first-parent の PR 単位の一覧 (`#NNN` は件名から)、(3) 変更ファイルからの分類 (判定・変換 / Windows / 本家 Mac / Linux / 文書。複数に当たれば判定・変換を主にして併記)、(4) Mac 版に取り込み済みか (Mac 版 (HEAD) のコミットの**件名**に `本家 <7 桁 hash>` (後ろに 16 進が続かない) があれば `取り込み済み`、追跡文書 (`UPSTREAM_PR_TRACKER.md`・`UPSTREAM_PR_SURVEY.md`) に hash か `#NNN` があれば `記録あり`、無ければ `未`)、(5) 判定・変換の差分が Mac 版に `git apply --check` で `そのまま` 当たるか `手移植` か、(6) 本家の `QualityTests.cs` に足された `new("カテゴリ", "打鍵", "期待")` の抽出と、Mac 版での当たり/外れの数え (`--expect`)、(7) `UPSTREAM_WATCH.md` の更新 (先頭の `<!-- seen: <hash> -->` と、新しい順の節)。
- **人が決めること**: どの PR を取り込むか (表は優先順位を付けない)、`手移植` の中身の読み方と移し方 (P45 の方針: 1 コミット = 1 本家コミット、broken が 1 件でも出たら見送る)、候補ファイルの行を正解表 (`eval/cases/upstream-quality.tsv`) に移すか (確かめてから末尾に移す。**自動では足さない**。push すれば公開になる)、`Before:` 付きなど 4 列で表せない行の扱い (件数だけ数える)。
- **初回の実行** (`--since a4b6f1a`、本家の `origin/main` = `db433da`、2026-10-14): 本家の新コミットは **2 件** — #603 (`c2ed84a`、`fix/533-bio-break`) と #609 (`db433da`、`fix/538-infobesity`)。どちらも判定・変換のみ、Mac 版には未取り込み (記録も無い)、`git apply --check` は `手移植`。`QualityTests.cs` の新しい正解ケースは **0 件** (この 2 件は `dictionaries/english.txt` と `CompositionTests.cs` だけを変えているため) で、候補ファイルは作られない。`seen` は `db433da` になった。続けて `--dry-run` で回すと 0 件で、何も書かない。
- **動作の確かめ**: 初回は 2 件で、新ケースの抽出と評価を通らないので、`MELTYPE_UPSTREAM=<clone> python3 tools/upstream_watch.py --dry-run --no-fetch --since df7ab39` でも回した (文書・候補ファイルは書かず、標準出力だけ): 本家 40 件 (判定・変換 33 件、未取り込み 39 件)、新ケース 23 件のうち Mac 版で 14 件当たり、外れ 9 件 (`site PR`・`i18n`・`l10n`・`h2o`・`devtoprod` など。P45 で見送った例と一致)。
- **自己テスト**: `python3 tools/test_upstream_watch.py` (37 件。分類、PR 番号 (Merge → 末尾の `(#N)` → 最初の `#N`)、取り込み状態の判定、`new(...)` 行の抽出 (名前付き引数・エスケープ・`#` 始まり・1 行に 2 つ・`)];`・`/* */`・前後の空白・コメント行・削除行)、文書の節の挿入位置と先頭の `seen` だけを読むこと、表のセルのエスケープ、行ごとの当たりの割り振り)。git・dotnet は使わない。
- **定期実行は Hermes 側** (リポジトリの外)。スクリプトは 1 回走らせて標準出力に要約 (3〜6 行) を出すだけで、スケジュールの仕組みは持たない。Hermes は `MELTYPE_UPSTREAM=<clone> python3 tools/upstream_watch.py` を回し、要約を読む。終了コードは 0 = 正常 (新コミット 0 件も 0)、2 = 引数・環境の誤り (dotnet の失敗も 2。そのときは文書を更新せず、`seen` も進めない)。
- **限界**: (1) `手移植` は「`git apply --check` が通らない」だけの意味で、Mac 版が独自に改変した所や、すでに別の形で取り込み済みの修正でも `手移植` になる。`そのまま` でも、取り込んでよいかは別 (P45 のとおり eval を回して broken を見る)。(2) merge コミットの差分は first-parent との差 (`git diff <hash>^1 <hash>`) で見る (仕様の `format-patch -1` は merge コミットだと空になるため)。(3) `取り込み済み` の判定は、Mac 版のコミットの件名に `本家 <7 桁 hash>` があるかだけ。件名に書かずに取り込んだ修正は `未` と出る。(4) 新ケースの抽出は C# の通常の文字列リテラルの `new("…", "…", "…")` の 1 行だけ。複数行・`@"…"` の行・かなで打つ行・`#` で始まる打鍵・Tab や前後の空白を含む行は「4 列で表せない」として件数だけ数える。(5) 実機は関係しない (Core のテスト用キーボードの結果)。**Windows・Linux は対象外** (本家の Windows・Linux の変更は一覧に出すだけで、取り込み・評価はしない)。

## 追加項目 0-4 (Homebrew cask `meltype-for-mac`)

- **目的**: 計画書 [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の柱 D・施策 0-4。`brew install --cask` で入れ・更新できるようにする。名前はリョウさんの決定 (tap `whitewater-png/homebrew-meltype-for-mac`、cask `meltype-for-mac`)。正本は `mac/homebrew/Casks/meltype-for-mac.rb`、手順は [mac/homebrew/README.md](../mac/homebrew/README.md)、版の書き換えは `tools/update-cask.py`。
- **zip を選んだ理由**: dmg の中は pkg で、Homebrew の `pkg` stanza は `sudo installer -target /` になる。このユーザーだけに入れる pkg と合わず、`brew install` が sudo のパスワードを求める構成にもなるので使わない。zip なら `input_method` stanza が `~/Library/Input Methods/Meltype.app` に置く (zip の展開先は `Meltype-mac/Meltype.app`: `mac/make-zip.sh` で確認)。
- **postflight / uninstall**: `mac/install.sh` の登録部分に合わせた (動いている Meltype と辞書の管理画面を止める → `Meltype --register` (失敗は無視して caveats で案内) → `imklaunchagent` と `TextInputMenuAgent` を止める)。uninstall の前に `--unregister` と、Meltype・辞書の管理画面を止める (`mac/uninstall.sh` と同じ)。この Homebrew (7.0.8) では `postflight` / `uninstall_preflight` が `brew style` で違反になるため、`postflight_steps` / `uninstall_preflight_steps` (`run`・`terminate_process`) で書いている。`zap` は `~/Library/Application Support/Meltype` と `~/Library/Preferences/io.github.yksr-melt.inputmethod.Meltype.plist` (どちらも実機にあることを確認)。
- **実機確認 (2026-10-14)**: (1) ローカル tap から `brew install --cask` すると `~/Library/Input Methods/Meltype.app` と中の実行ファイルに `com.apple.quarantine` が付く。その状態で `Meltype --register-check` は **`Killed: 9`** (Gatekeeper が ad-hoc 署名 + quarantine の実行ファイルを起動させない)。IME は imklaunchagent が裏で起動するので「開発元が未確認」も出ず、入力メニューに出ない・何も起きないだけになる。postflight の `--register` も同じ理由で失敗していた (`must_succeed: false` で黙っていた)。(2) `xattr -dr com.apple.quarantine` で外すと `--register-check` rc=0、`--register` rc=0。(3) 既に `~/Library/Input Methods/Meltype.app` がある (zip の install.sh や build.sh で入れた) と、Homebrew が `It seems there is already an Input Method at ...` で止まる (Homebrew の仕様)。
- **quarantine の扱い (実機確認を受けて変更)**: postflight の最初に `xattr -dr` で外す (`--no-quarantine` の案内はやめた)。`run` の `args` は `base` で解決されない (`base` はコマンドだけ。Homebrew の `install_steps.rb`) うえ、サンドボックスの中では `$HOME` も偽のホーム (`/opt/homebrew/var/homebrew/sandbox/.../home`) を指すので `/bin/sh -c "$HOME/..."` も効かなかった (実機で `xattr: No such file`)。`args` で使える置き換えは `{{user}}` などに限られるため、`/Users/{{user}}/Library/Input Methods/Meltype.app` で本物のホームを指す (ホームが `/Users/<ユーザー名>` でない構成は対象外)。この形で `brew reinstall` 後に quarantine が 0 になることを実機で確認した。得: pkg で入れたときと同じ挙動で、確実に起きる。損: Gatekeeper の確認を飛ばすので、利用者は Release の SHA-256 が cask に書かれていることを信頼する形になる (sha256 は Homebrew が検証する)。既に Meltype.app がある場合の案内は caveats・README・DEVELOPMENT に書いた。**実機 (2026-10-14、macOS 26.6.2) の結果**: `brew reinstall` 直後、入力メニューに Meltype は出たが**入力できず、日本語モードも出なかった** (英数のみ)。同じ日に監督者が確認用に `Meltype --unregister` を手で実行し、その後の postflight の `--register` がサンドボックス内で効かなかった (ログに `com.apple.hiservices-xpcservice` への `Connection Invalid`) ことが重なっている。`brew uninstall` → 元の開発版を戻して手で `--register` → `build.sh` で入れ直し、のどれでも直らず、**ログアウト・ログインで直った**。結論: (1) Homebrew のサンドボックス内では入力ソースの登録ができないので、cask の postflight で登録を済ませることはできない。利用者にはログアウト・ログイン (または `~/Library/Input Methods/Meltype.app/Contents/MacOS/Meltype --register` を手で実行) が要る。(2) 入力ソースの登録状態が崩れると、正しいファイルを置き直しても `build.sh` でも戻らない。`--register-check` は `--unregister` の後も 0 を返すので、登録の有無の確認には使えない。
- **確かめたこと (ローカル tap `whitewater-png/meltype-for-mac`)**: `brew audit --cask --strict --online` は指摘なし、`brew style` は指摘なし、`brew fetch --cask` は sha256 が一致。`python3 tools/test_update_cask.py` は OK。
- **確認済み (上の実機の結果)**: quarantine を外す postflight での再インストール (quarantine 0、codesign OK、版 1.1.7)、`brew uninstall` (`~/Library/Input Methods/Meltype.app` が消え、`~/Library/Application Support/Meltype` は残る)。**未確認**: cask で入れた直後にログアウト・ログインすれば使えるようになるか (今回は開発版に戻してからログアウトしたので、cask 版そのものでは確かめていない)、きれいな Mac (Gatekeeper の判定が記憶されていない) での初回。
- **決定: (a)**。caveats に「インストール後にログアウト・ログイン」を明記して公開 tap へ進んだ。公開 tap を作成済み (2026-10-14): https://github.com/whitewater-png/homebrew-meltype-for-mac (public、`Casks/meltype-for-mac.rb` と README、commit 29b083a)。`brew tap whitewater-png/meltype-for-mac` → `brew audit --cask --strict --online`・`brew fetch` が通る。入れ方は `brew install --cask whitewater-png/meltype-for-mac/meltype-for-mac`。
- **実機 (2026-10-14、公開 tap から)**: cask だけで 1.1.7 を入れ (quarantine 0、codesign OK)、**ログアウト・ログイン後に入力メニューに Meltype が出て入力できた**。ログアウト前は、入力メニューに出ても入力できない (上の記録どおり)。未確認: きれいな Mac での初回 (Gatekeeper の判定が記憶されていない)。
- **その後の開発機の事情**: 開発版 (main 446dd48) を `build.sh` で同じ場所に上書きして戻した (brew の記録上は cask が入ったまま)。開発機だけの事情で、利用者には関係ない。
- **tap の履歴**: 最初の `brew tap-new` の自動コミットに個人のメールアドレスが入っていたので、履歴を書き換えて noreply にして force push した (新規リポジトリで、取得者はいない)。今後 tap で commit するときは `git config user.email` を本体リポジトリと同じ noreply にする。
- **README・リリース手順**: README の「インストール」の先頭に Homebrew の節を足した。リリースごとの cask の更新 (`tools/update-cask.py` → tap へ写して push。push は Release の公開と同時) は `docs/RELEASE.md`。`tools/check-release.py` が cask の `version` (built app と) と `sha256` (`mac/dist` の zip と) を照合する。
- **Windows・Linux は対象外** (Mac 版だけ。配布 zip は Apple シリコン・macOS 13 以降のみ)。

## 追加項目 0-5 (既知の制限の仕分けと README の制限節)

- **目的**: [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の施策 0-5。計画書の各節に散らばった「既知の制限」を 1 か所に集め、「仕様 / 直す予定 / 直った / 未確認」に仕分けて、利用者向けの README に短い「制限」節として載せる (「翌日アンインストール」型の失望を減らす)。
- **作ったもの**: [KNOWN_LIMITS.md](KNOWN_LIMITS.md) (開発者向けの正本。50 行)。README に「## 制限 (知っておいてほしいこと)」(8 行) と、「よくある質問」に 3 問 (ライブ変換との違い・かな入力・明示的な切り替え)。Core・Swift のコードは変えていない。
- **仕分けの件数** (査読 R37 の反映後): 仕様 28 / 直す予定 10 / 直った 4 / 未確認 8 (計 50)。「直った」の hash は、作業中に `git log` が承認待ちで止まったため、`a490a09` (Q7・Q8) 以外は未取得。
- **番号待ちの `候補`**: K02・K03 小文字の人名・名字 (専門用語集に「人名」分野を足すか。Phase 1-1 Zenzai・本家 #487 の経過を見て)、K16 本家の判定修正の残り 28 件 (`-pedia` は前の日本語を巻き込まない条件が先)、K20 空白の無い 1 語の英単語の再変換 (リョウさんの判断待ち、HANDOFF:76)、K33 英単語の前後の空白設定を ON にしたときの二重空白、K40 全角スペースを打つ手段。
- **全角スペース (K40) の確認**: 入力中でないときの Space は `StartWith` → `ReplayDown` でアプリに渡り、半角スペースが入る。Shift+Space は、選択があれば再変換、無ければ同じく素通し (読み直せない選択では飲み込んで何も起きない)。全角スペースを出す設定・候補は無い。本家 #310 (Shift+Space の全角スペース) はリョウさんが却下済みなので、Shift+Space には割り当てない。**実機では確かめていない** (コードを読んだ範囲)。
- **半角スペースの再変換 (K41・K42)**: スペースを含むローマ字の文の再変換は P36 で直っている (TextEdit で実機確認済み、2026-10-12)。日本語の確定済みの文字の途中に半角スペースがある場合は、コードを読むと読みに戻せない可能性があるが、再現は取っていない (未確認)。
- **`--repro` で確かめた打鍵と結果** (2026-10-14、変換エンジンは偽物。Enter で確定した結果):
  | 打鍵 | 結果 |
  | --- | --- |
  | `thanks, yui!` | `thanks, ゆい！` |
  | `Kyou wa genki desu` | `Kyou わげんきです` |
  | `tanaka san ni aimasu` | `たなかさんにあいます` |
  | `Yamada san to hanashimashita` / `yamada san to hanashimashita` | `Yamada さんとはなしました` / `やまださんとはなしました` |
  | `Node de deploy shimasu` / `Frobnitz de deploy shimasu` | `Node でdeploy します` / `Frobnitz でdeploy します` (HANDOFF:71 の `Node` は再現しない) |
  | `kyou wa Frobnitz wo tsukatta` / `kyou wa frobnitz wo tsukatta` | `今日わFrobnitz をつかった` / `今日わfrobにtzをつかった` |
  | `kyou wa mp3 kakeru 5` | `今日わmp3駆ける` |
  | `ollama run llama3` / `gpt-oss` / `gemma4-27b` | `ollama るんllama3` / `gpt-おっs` / `gemma4-27b` (HANDOFF:72 のうち `gemma4-27b` は直っている) |
  | `10-ji ni kaigi desu` / `- kyou wa` | `10-じにかいぎです` / `- 今日わ` (Q7 で直っている) |
  | `maindo` / `pushoyobi` | `まいんど` (途中は `まindo`、Q12) / `ぷしょよび` |
  | 100 文字超のかな文 | 判定は崩れない (外れるのは 50 文字の区切りをまたぐ文脈だけ) |
- **README の文言について**: 「語の頭を大文字で打つと英字のまま」は、上の `Yamada`・`Frobnitz`・`Node` で確かめた範囲だけを書いた。本家 USAGE の「Shift を押しながら打てば英語」がそのまま成り立つかは、Shift を押したまま続けた場合を確かめていないので、README には Shift のことを書いていない。
- **実機について**: 実機・brew・作者の作業ツリーには触っていない。
- **査読 R37 の反映** (2026-10-14): 事実の誤り 4 件を直した。(1) 学習の条件: 語だけを打って直すと次から直した方になる。日本語としてよく使う語を英字にするときは 2 回 (`LanguageMemory.NeedsTwice`)。(2) 「50 文字」は漢字変換の区切り (`ConvertChunkLength`) で、日英判定ではない。またげないのは `ContextRules` の同音語の選び分け。(3) 例を `thanks, yui` に替え、名前のすぐ後ろに `!` `,` が付くと大文字でも日本語になることを足した。(4) ライブ変換 (下記)。推奨・任意: LibreOffice・Google ドキュメントの文、Homebrew「入力できないことがある」、Intel (ソースからビルド)、Shift+Space「など」、FAQ の US 配列の切り替えキーとかな入力、K33 を `you  want` と `Let  It Go` (K37、未確認) に分割、K40 をリョウさんが決める事項と明記、K26 を K25 にまとめた。ユーザー辞書に全角スペースを登録する手段は、`UserDictionary` が語を `Trim()` するので使えない (コードを読んだ範囲。K40)。
- **ライブ変換は既にあった (実機確認) → 戦略計画 2-3 を訂正**: 2026-10-14、TextEdit で `kyouhaiitenkidesune` を Space なしで打つと漢字で表示された。`Settings.LiveConversion` は既定 ON で、設定タブで OFF にできる。0-5 の最初の版は「ライブ変換が無い」と書いていたので、README の FAQ・K28・K51 を直し、[STRATEGY_2026-10.md](STRATEGY_2026-10.md) の Phase 2 の 2-3 と §1.3 を訂正した (2-3 は「ライブ変換の精度向上 (Zenzai 前提) と、ライブ変換の存在を README で前面に出す」に読み替え)。
- **Windows・Linux は対象外** (Mac 版だけ)。

## 追加項目 0-3 (ホストアプリ互換マトリクス)

- **目的**: [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の施策 0-3。どのアプリの入力欄で何が効くかを、実機で同じ 8 項目 (ライブ変換・日英の混在・確定し直し・再変換・F10 と学習・二重空白・アポストロフィ・英数/かなの切り替え) を打って確かめ、[HOST_COMPAT.md](HOST_COMPAT.md) に一覧にした。コードは変えていない。
- **実機の結果** (2026-10-14、リョウさん、macOS 26.6.2、JIS、main 446dd48 のビルド): TextEdit・Hermes のチャット欄 (Electron)・LibreOffice Writer・Chrome の Google 検索欄・Discord は 8 項目すべて OK。Google ドキュメントは再変換 (4) だけ NG (Shift+Space で何も起きない。文字は壊れない)。ターミナル (Terminal.app) は再変換 (4) だけ NG (変換ボックスが出ず、選択の先頭に半角スペースが入った)。VS Code・Slack は未確認。英数/かなの切り替え (8) は全アプリで期待どおり。
- **K30 の食い違い**: 2026-10-12 の記録 (計画書 :1814「LibreOffice は `selectedRange` が `{0,0}` で確定し直しが効かない」) と違い、2026-10-14 の実機では LibreOffice で確定し直し (3) が効いた。当時の確認はローマ字の文の再変換 (P36) で、確定し直しは別経路。条件によるのかもしれないので、K30 は「仕様」から「未確認」に移した (以前の記録は要再確認)。K31 (Google ドキュメントで確定し直しが飛ばされる) も今回は出ず、確定し直しは OK、再変換が NG に更新した。K32 (Hermes の `We'うぇへれすし`) は再現せず、未確認のまま。K34 (表示と動きのずれ) はターミナルで出ず、VS Code が残る。
- **新規 K66 と候補**: ターミナルで再変換が効かず、Shift+Space の半角スペースが入る。アプリが選択範囲を IME に返さない (`selectedRange` 経由) ためと推測 (確かめていない。Google ドキュメントも同種と推測)。再変換できないこと自体は仕様。**候補**: 「選択が取れないときは Shift+Space を素通ししない」。得: 選択の先頭に勝手にスペースが入らない。損: 選択が無いふつうの Shift+Space の半角スペースも打てなくなるおそれがある。選択の有無を区別する手段があるかを先に調べる。着手はリョウさんの判断待ち。
- **README**: 制限節の LibreOffice の行を、「再変換 (選んで Shift + Space) は、Google ドキュメントとターミナルでは効きません (何も起きないか、スペースが入ります)。アプリごとの一覧は docs/HOST_COMPAT.md」に書き換えた。確定し直しが効かないという記述は外した。
- **件数** (KNOWN_LIMITS.md): 仕様 27 / 直す予定 11 / 直った 4 / 未確認 9 (計 51)。
- **実機について**: 実機の確認はリョウさんが行った。この作業では実機・作者の作業ツリーには触っていない。
- **Windows・Linux は対象外** (Mac 版だけ)。

## 追加項目 0-6 (アイコン)

- 入力メニューの氷の M・氷の A は `1188c89` で既に入っていた (16/32px の tiff)。残っていたのは辞書アプリ「Meltype 辞書」と Meltype.app 本体の Finder・Dock・⌘Tab 用のアイコンだけだったので、それを足した。
- 元絵は氷の M の横長画像 (1536×1024、リョウさんが用意)。`tools/make_icon.py` で正方形に整え、macOS 風の角丸 (半径 22.37%) にして `mac/Resources/icon-1024.png` と `Meltype.icns` を作る。`build.sh` が Meltype.app と MeltypeDictionary.app の両方の Resources に写し、両方の Info.plist に `CFBundleIconFile` = `Meltype` を入れた。
- 実機 (2026-10-14): Finder・Dock・⌘Tab で氷の M が出ることをリョウさんが確認。Finder はアイコンのキャッシュが残ることがある (ウインドウを開き直す)。
- 注意: `plutil -insert` は Info.plist のコメントを消して並べ替えるので、plist は手で編集する。
- 残り: 入力メニュー用の 16/32px tiff は別の元絵なので、絵柄を揃えるなら同じ元絵から tiff も作り直す (今は保留。小さいサイズでは今の tiff のほうが見やすい)。

## 追加項目 1-1 (Zenzai)

- **設計・実験**: 設計は [ZENZAI_DESIGN_1-1.md](ZENZAI_DESIGN_1-1.md) (§3 が製品の形)、実験の結果は [ZENZAI_EXPERIMENT_2026-10.md](ZENZAI_EXPERIMENT_2026-10.md) (212 文で第一候補の一致 +17〜22 ポイント、1 キーの p95 61 ms、メモリ +30〜100 MB。CPU 版)。決定: go。既定 OFF、推論の上限 3、左文脈あり、CPU 版、重みは ON にしたときだけ取得。
- **実装の要点**:
  - `mac/Package.swift` を swift-tools-version 6.1、azooKey に trait `ZenzaiCPU`、`.interoperabilityMode(.Cxx)` に。GPU 版 (`Zenzai`) は llama.cpp b4846 の Metal 初期化で落ちるので使わない。
  - B1: `build.sh` が `otool -L` に出るフレームワークだけを同梱し (`llama.framework` は `lipo -thin` で自分の CPU の分だけに)、`tools/check-release.py` が `Contents/Frameworks` と一致するかを検査する。ビルド機の絶対パスが文字列に残らないよう、`swift build` に `-ffile-prefix-map` を渡す。
  - 設定 `Settings.Zenzai` (既定 false)・設定タブの項目・IME 用の getter `meltype_get_zenzai` (ABI 版数は上げない)。
  - `ZenzaiModel.swift`: モデル (73,871,968 バイト、SHA-256 固定) を、設定が ON のときだけ IME が取得する (Updater と同じ作法。転送先は `huggingface.co` と `*.cdn.hf.co`)。状態は `zenzai.json` で辞書アプリの設定タブに出す。失敗したら設定を OFF に戻す。
  - `Converter.swift`: 要求ごとに `zenzaiMode` (`.on(… .v3(.init(profile: "", leftSideContext: context)))`) を決め、結果キャッシュの鍵に左文脈を足す。モデルを読めない・1 キーが 300 ms 超を 3 キー続けて、はそのプロセスだけ OFF。
- **実機で確かめること** (リョウさん): ① 設定タブで ON → 状態が「ダウンロード中 (xx%)」→「準備できました」になり、そのあと変換が変わる (同音異義語・文脈で漢字が選ばれる) ② OFF に戻すと今までの変換に戻る ③ 機内モード (ネットワーク無し) で ON → 「失敗: …」が出て、スイッチが OFF に戻る ④ パスワード欄 (秘匿入力) では Meltype がキーを触らないので変換は起きない (左文脈も渡らない) ⑤ M1 級の Mac が有れば、打ち心地 (K67) ⑥ `~/Library/Application Support/Meltype/models/` を消してから ON → 取り直す。
- **未確認 (この作業では確かめていない)**: 実機の IME での操作 (①〜⑥)。macOS 13.0〜13.2 での起動 (llama.framework の minos が 13.3 で、`LSMinimumSystemVersion` (13.0) より新しい。`tools/check-release.py` の minos の検査が FAIL する。13.3 に上げるかはリョウさんが決める)。llama.framework に残る上流の作者のパス (`/Users/miwa/` 55 件、`tools/check-release.py` のビルド機のパスの検査が FAIL する)。llama.cpp の MIT ライセンス全文を Meltype.app の `Licenses/` に入れること (今は THIRD-PARTY-NOTICES に表記だけ)。

### 1-1 の実機確認 (2026-10-14、macOS 26.6.2、JIS、feat/zenzai 51767c3)
- OFF のままの変換は従来どおり。設定タブで ON → 「ダウンロード中」→「準備できました」。左文脈で「講演」「公園」が切り替わり、日英混在 (`kyouhameetingdesu`) も従来どおり。OFF に戻すと状態文が OFF になり変換が元に戻る。リョウさん「きちんと動作しました」。
- 通信を切った状態での失敗表示は実機では未確認 (自己テスト `--zenzai-self-test` の取得元なし・SHA 不一致・転送先拒否の経路で代替)。
- 査読: R39 (実験の計測と結果)、R40 (製品実装、セキュリティ重視: 必須 0、推奨 9 を反映)。

## 追加項目 A1 (ATOK の 1 音確定: 入力中の Shift+↓ で、先頭の 1 音だけ確定する)

- **経緯**: リョウさんの依頼 (2026-10-14): ATOK スタイルの操作の追加として、1 音確定 (Shift+↓) を入れる。ATOK のキー設定の書き出し (第三者の Gist、https://gist.github.com/takatakata/c31ac544e09283d777fa1e82c8c248fb) で、入力中・変換中・次候補表示中・文節区切り直し中の各状態に「1音確定 [Shift+↓]」が割り当てられていることを確かめた。JustSystems の「ATOK標準のキー操作」(FAQ 040816) は文字化けで該当の行を読めなかった。**1 音確定の動き (先頭の 1 音だけを確定して残りを入力のまま残す) を説明した公式の文章は見つけておらず、推定**。変換中の動きは根拠が無いので入れていない (リョウさんの判断: 入力中だけ・英字は 1 文字ずつ・Windows も同じ動き)。
- **動き** (`CompositionController.CommitFirstSound`・`CompositionText.TakeFirst`): 入力中 (変換前) の Shift+↓ で、先頭の 1 音を見えている形 (F7 ならカタカナ、F8 なら半角カナ、ライブ変換で漢字に見えていても読みの 1 音) で確定し、残りは入力のまま続ける。最後の 1 音で変換ボックスを閉じる。英字に見えている部分 (F9 / F10・自動で英語と判定した語・数字) は 1 文字ずつ。小書きのかなが別の単位になっているとき (再変換の き|ょ、かな入力) も きょ で 1 音。入力途中の子音だけが残ったら、ふつうの確定と同じ (語末の n → ん、英字に見えていた n は n)。変換中・Tab で予測候補を選んでいるとき・変換後も続けて入力して固定した文節があるときは、今までどおり。Shift なしの ↓ も今までどおり。
- **見え方の固定**: 1 音確定の時点で、残りの単位と入力途中の子音の英語 / 日本語を見えていたまま固定する (`_frozen`・`_frozenPending`。単位の英字が合う所まで使い、後ろに新しく打った文字は、固定した言語を前の文脈として今までどおり判定する)。固定しないと、英語の語の頭を確定したあとの残り (Hello の ello) を判定し直して かな にし、押し続けると画面に出ていない文字 (Hえっぉ！) を確定していた。確定する文字は「取り出す前の見え方 − 残りの見え方」で決める (英語の前で半角に見せている開きの記号を、全角で確定しないため)。Enter・Space の前の打ち間違いの直しは、見え方を固定した文字を含む並びを直さない (10 を確定したあとの mm は、数字の後ろの単位と分からなくなるため、英字・かなを問わず)。BackSpace で消したら、消した所から後ろの固定を捨てる (消したあとに打った文字は、固定した英字と綴りが同じでも判定し直す)。
- **学習と文脈**: 1 音確定をした入力では、そのあとの確定 (Enter・変換) でも変換と英語 / 日本語の学習をしない (残りは語の途中から始まるので、kanpai の npai を F10 の英字として覚えてしまうため)。1 音だけの確定も、前の確定の確定し直し (`_correctable`) の対象にもしない。確定した 1 音は、残りの入力の前の文字 (`_precedingText`・`PrecedingEnglish`) と、次の入力の文脈 (`_lastCommitText`) に足す。再変換の途中で 1 音確定したら、Esc で元の文字を書き戻さない (二重になるため)。ローマ字の再変換の「学習しない」印はそのまま。
- **Mac / Windows**: キー処理は Core なので両方に効く。Mac は ↓ を `KeyMapping` ですでに Core に渡していて、Swift の変更は無い。確定と残りの表示を同じ結果で返すのは、変換中に次の文字を打ったときと同じ流れ (`insertText` → `setMarkedText`)。
- **検証**: Core のテスト 606 → **624 件** (`CommitFirstSoundTests` 18 件。BackSpace の確認は 8 通り。変更前から落ちる環境由来の 2 件 = `DictionaryManagementTests.UserDict_SameSizeReplacement_IsDetectedByCreationTime`・`LearnedWordsTests.SaveFailure_RestoresMemory` を除き通過)。英語の語・記号・数字・カッコ・n で終わる語など 21 通りの入力で、Shift+↓ を押し続けて毎回「見えていた文字 = 確定した文字 + 残り」と最後の文書を確かめる。1 回押したあとの Enter・Space も確かめる。どの修正も、修正前のコードで赤になることを確かめた。品質テスト (`--eval`) は 1242/1244 で変わらず。`Meltype.Mac.Native` のビルドは通過 (Linux。NativeAOT・Swift のビルドはしていない)。
- **査読**: 独立査読 4 回。1 回目: P0・P1 4 件 (英語の残りが かな になる・開きの記号の幅・再変換の Esc で二重・再変換の き だけ確定) → すべて直した (`962547c`)。2 回目: P0 2 件・P1 1 件・P2 2 件 (語末の n が ん になる・打ち間違いの直しが固定した英字を書き換える・Space が変換に入る・SegmentsKey の数え直し) → 開きの記号の P2 以外を直した (`9489bde`)。3 回目: P0 1 件・P1 4 件・P2 4 件 (10mm の単位を数字の確定後に 10っまで に直す・BackSpace のあとに固定が残る (3 通り)・F6〜F10 で残りの断片を学習する) → P1・P0 はすべて直した (修正を外すとテストが赤になることを確かめた。入力途中の子音の固定に「単位の数が固定したときと同じ」を足した条件は、BackSpace で子音の固定を捨てる処理と重なる二重の守りで、外しても赤にならない)。P2 は限界に書いた。4 回目 (3 回目の修正だけ、約 2 万通りの入力で修正前と比べた): P1 1 件 (BackSpace で語末の子音だけを消して打ち直すと、英字に見えていた n が確定で ん になる: `Japan` → Shift+↓ → BS → `n` → `Japaん`。3 回目の修正で入れた回帰) → 子音の固定を捨てるのを、単位を消したときだけにして直した (修正前に赤を確認)。P2 3 件は限界に書いた。この最後の修正 (1 行) は査読していない。
- **得と損**: 得: ホームポジションから手を離さずに、読みの頭から少しずつ確定できる (ATOK に慣れた人の操作)。損: (1) 設定「ATOK スタイルのキー操作」が ON のとき、Shift+↓ は今まで ↓ と同じ (文節の選択に入る) だったのが、入力中は 1 音確定に変わる (OFF では変わらない)。(2) 1 音確定のあとは、残りの英語 / 日本語の判定が、確定前の見え方のまま固定される (残りを打ち足しても、固定した部分は判定し直さない)。
- **1.2.0 への対応 (2026-10-14、リョウさんの依頼「新しいバージョンにも対応できるように」)**: main の 1.2.0 (`a0747b5`) には、別の作業で「ATOK スタイルのキー操作」(設定 `TokMode`、既定 OFF。変換中の ← → で区切り・↓ で部分確定。`docs/ATOK_STYLE_KEYS_PLAN.md`) が入っていて、その計画書は 1 音確定を「未実装」としていた。ATOK スタイルの操作は設定で選ぶ形にそろえ、**1 音確定も `TokMode` が ON のときだけ**にした (OFF の Shift+↓ は、A1 より前と同じく ↓ と同じ)。main をこのブランチに merge し (`8c40777`。衝突は記録の追記どうしだけ)、次を足した: 入力中のヒントの最後に「Shift+↓ 1音確定」(ON のとき)、設定の説明文 (`Settings.TokMode`・`MacSettingsCatalog`) に 1 音確定、部分確定 (`RemoveLeading`) で 1 音確定の固定を捨てる (念のための守り。部分確定で先頭の単位が消えると固定の照合はもともと外れるので、外してもテストは赤にならない)。変換中の Shift+↓ は ON でも今までどおり (↓ だけが部分確定)。テスト 18 → 21 件 (OFF は ↓ と同じ・ヒント・ON の変換中の Shift+↓ と ↓・1 音確定のあとの部分確定)、全体 659/661 (環境由来の 2 件を除き通過)、品質テスト 1266/1268 (main と同じ)。OFF の確認は、設定での切り替えを外すと赤になることを確かめた。この対応は独立査読をしていない。
- **限界・未確認**: (1) **Mac・Windows の実機で確かめていない** (Mac で Shift+↓ が IME に届くか、`kyouha` → Shift+↓ で きょ が確定して うは が残るか、Google ドキュメントなど範囲が不正確なアプリ)。(2) 1 音確定のときに半角で見えていた開きの記号 (" ' ` { と、全角の設定の ( ) は、後ろの英語を消しても半角のまま (見えていたまま固定する方針どおり。2 回目の査読の P2、直していない)。(3) F6 で かな にしたときは、固定した英字も かな で見せる (F6 の設計どおり)。(4) 変換中・次候補表示中・文節区切り直し中の 1 音確定 (ATOK にはある) は入れていない。(5) 3 回目の査読の P2 (直していない。どれも確定する文字は見えていたとおり): 1 音確定の前に打ちかけていた語を、あとから打ち足しても判定し直さない (`Japan` → Shift+↓ → `ese` が `Japaねせ`、`kyouhagoo` → Shift+↓ → `gle` が `ごおgぇ`)。空白を入れる設定で `iPhone15de` を 6 回以上 1 音確定すると、数字と で の間に空白が入らない。打っている途中の仮の見え方 (`kitaww` の `きたっw`、`10min` の `10みn`、`ok` の `おk`) を、そのまま確定する (Enter なら確定のときに見え方を作り直すので結果が違う)。笑いの w の残りで Space を押すと変換に入る (1 音確定なしの `ww` + Space と同じ)。(6) 4 回目の査読の P2 (直していない。確定する文字は見えていたとおり): 固定した英語の語の途中の 1 文字を消して打ち直すと、打ち直した所は判定し直すので かな になりうる (`Hello` → Shift+↓ → BS → `o` が `Helぉ`、`Google` → … → `e` が `Googぇ`。3 回目の査読の、消したあとに打った文字が英字のまま残る問題との引き換え)。固定した日本語のすぐ後ろに続けて打った語は、打ち間違いの直しを受けない (`kyouha` → Shift+↓ → `onegaishimsu` が `おねがいしmす`)。変換後も続けて入力する設定では、1 音確定のあとに変換して続けて打った語も学習しない (入力全体で学習を止めるため)。

## 追加項目 1-2 / 1-6 (確定後の 1 キー日英トグル + 学習の強度)

- **設計**: 設計は手元の設計メモ (1-2・1-3 の設計。非公開) の §2・§4 (R41)、根拠は手元の事実確認のメモ (非公開) の §B。決定: キーは F10 = 英字 / F6 = かな (変換中と同じ。Ctrl+' / Ctrl+J は設定で ON、既定 OFF)、強度は「手動 1 回・暗黙は 2 回続くまで反転しない」、前後語の学習は今回やらない (段階 2。本家 #491 の本文を読んでから)。
- **実装の要点**:
  - 直前の確定 1 回分を 1 件だけ覚える (`CompositionController._lastWord` = 表示した文字・打った英字・英語の区間があるか `HasEnglish`・日本語の区間があるか `HasJapanese`・時刻)。単位は語ではなく**確定 1 回分まるごと** (変換中の F10 / F6 と同じ。R43-1): F6 は英語の区間があるとき、F10 は日本語の区間があるときに効く (「きょうはSushiです」→ F6 → 「きょうはすしです」、「今日は寿司です」→ F10 → `kyouhasushidesu`)。自動の確定し直しの `_correctable` とは別 (R41-1)。固定した文節を含む確定・再変換・ローマ字再変換・暗黙の確定 (フォーカス外れ・英数キー)・打った英字の無い確定は記録しない。消える条件は確定し直しと同じ (キーの素通し・マウス押下・英数・キャレットの前の文字が記録と違う・取消のあと) と、有効時間 `LastWordTrustMs` (**3 秒**。`OwnCommitTrustMs` = 1.5 秒とは分けた。R43-2)。F10 / F6 を押したときの経過ミリ秒を診断ログ (`Log.Info`) に出す (実機で 3〜5 秒の間で決めるため)。
  - `TryToggleLastWord(toEnglish, precedingText)`: 変換ボックスが無い・記録がある・`precedingText` が読めて末尾が記録と一致、のときだけ直す (それ以外は `false` = キーを素通し。R41-4)。F10 = 入力欄の語を消して打った英字を確定 + `Remember(raw, english: true, explicit: true)`。2 回ルールで 1 回目が効かないときは「もう一度英字にすると覚えます」を、確定のヒント (`meltype_suggest_hint`、キャレットの近く) に 1 回出す。F6 = 入力欄の語を消して、打った英字から作ったかなの変換ボックスを開く (再変換 `ReconvertKana` と同じ作り。Raw は元の英字を `ToggleBox` で持つ)。Enter・Space・番号で確定したときだけ `Remember(raw, english: false, explicit: true)` (フォーカス移動・クリック・英数キー・Ctrl ショートカット・続けて打ち始めての確定は、文字は確定するが学習しない。R43-3)、Esc は 1 回で、学習せず元の語を書き戻して記録も戻す (R43-8)。`ContinueAfterConversion` が ON で続けて打ったときも、末尾の空白は (続けて打った文字の後ろに) 戻り、Enter で学習する (R43-4)。F6 の変換ボックスで F10 は打った英字に戻す (往復。学習は動かさない)。語の前後の半角スペース (Space での確定・英単語の前後の空白の設定) は直さず残す。トグルした時点で `_correctable` を空にし、トグルの確定は自動の確定し直しの対象にしない (R41-2)。
  - 取消: `SessionResult.Toggle` (JSON `"toggle":true`、トグル由来のときだけ) と `meltype_cancel_toggle` (**ABI 11**。別ブランチ feat/judge-tab の 1-3 は ABI 10。統合後は 11 に統一)。Swift の `apply` が、トグル由来の edit を確かめられず飛ばしたら、変換ボックスを出さず `meltype_cancel_toggle` を呼び、キーはアプリに渡す。Core は変換ボックス・記録・学習 (`LanguageMemory.Undo`)・自分の確定の記録・`_correctable` を直前に戻す (R41-3)。
  - 設定 `ToggleLastWordCtrlKeys` (既定 OFF。設定タブの「入力」)。ON のときだけ、変換ボックスが無いときの Ctrl+' = F10・Ctrl+J = F6 を、素通し判定より前に読み替える (R41-9)。
  - 1-6 の強度: `LanguageMemory.Entry.Reverse` (逆向きの回数。旧ファイルは 0)。`Remember`: 同じ向きは `Count++`・`Reverse = 0`、違う向きは explicit なら即反転 (`Count = 2`。ただし英語向きでよく使う日本語 `IsCommonJapanese` の語は `Count = 1` = 2 回ルールが効く。R43-5)、暗黙なら `Reverse++` で 2 回に達したら反転 (`Count = 2` で作る。2 回ルールと重なって 3 回にならない。R41-7)。「続けて」は前の逆向き (`Reverse` の時刻 `ReverseAt`) から 24 時間以内で、超えたら 1 から数え直す (R43-6)。`Reverse` はソース生成の JSON に入り、AOT で保存・読み直しできる。
  - 既存テストの変更: `FeedbackTests.ShiftSpace_ConvertsEnglishWordAsRomaji` は、explicit で覚えた英語の語を Shift+Space の日本語候補で 1 回確定すると日本語になる、という旧仕様を見ていた。新しい強度では 1 回では変わらず、続けて 2 回で変わるので、2 回目を足した。
- **R41 で塞いだ穴**: 1 (記録は別持ち) / 2 (トグルで `_correctable` を空に) / 3 (Swift が飛ばしたら変換ボックスも出さず取消) / 4 (前の文字が読めて末尾が一致するときだけ飲み込む) / 5 (変換ボックスがある間は届かない・有効時間) / 6 (F6 は Enter で学習・Esc は学習しない・暗黙学習と二重にしない) / 7 (2 回ルールを残し、反転は `Count = 2`) / 8 (往復は raw に戻す) / 9 (Ctrl は設定で ON) / 10 (強度)。
- **実機で確かめること** (リョウさん): TextEdit / Hermes / LibreOffice / Google ドキュメント / ターミナル で、① 確定直後の F10 (`nihongo` → `にほんご` → F10 → `nihongo`)・F6 (`hello api` の `api` → あぴ) ② 往復 (F10 → F6 → F10) ③ F6 の変換ボックスで Esc (元の語に戻る・学習しない) ④ 別の語を打った後は効かない (キーはアプリに渡る) ⑤ キャレットを矢印キー・クリックで動かした後は素通し ⑥ よく使う日本語 (`kyouha`・`sushi`) を F10 で英字にした 1 回目の案内と、2 回目で覚えること ⑦ 設定で Ctrl+' / Ctrl+J を ON / OFF (JIS で Ctrl+' が押せるか。ターミナルの Ctrl+J) ⑧ 確かめられないアプリで、文字が増えも消えもしないこと (取消) ⑨ **置き換えたあとの中身が二重になっていないか** (LibreOffice・Java 系など `insertText(replacementRange:)` の範囲を無視するアプリで「日本語nihongo」のようにならないこと。既存の確定し直しと同じ危険。R43-10) ⑩ 「きょうはSushiです」(英語と日本語が混ざった確定) で F6 / F10 が確定 1 回分まるごとに効くこと ⑪ F6 の変換ボックスのまま別の場所をクリックしたとき、文字は確定し、学習はしないこと。Mac のノート機では F キーに Fn が要る。
- **未確認 (この作業では確かめていない)**: 実機の IME での操作 (上の ①〜⑪)。有効時間 3 秒が実際の操作として短すぎないか・長すぎないか (確定を見てから F10 を押すまで。診断ログの「確定から N ミリ秒」を見て 3〜5 秒の間で決め、`LastWordTrustMs` を変える)。Swift 側の取消 (`apply` の `skippedToggle`) はビルドだけで、実機・テストでは動かしていない (Core の `CancelToggle` は Core のテストで確かめた)。

## 追加項目 1-3 (「判定を調べる」タブ)

- **設計の前提のずれ** (1-2・1-3 の設計メモの調査。計画は [STRATEGY_2026-10.md](STRATEGY_2026-10.md)): 計画書の「Core の判定理由は既にある」は誤りで、理由と点数を持つのは Windows の `ScoreEngine` だけ。Mac の判定 `CompositionDetector` は bool を返すだけで、分岐は約 35 か所に散らばる。
- **切り分け方式にした理由**: 理由を付ける本格版 (中〜高、1 週間超) は避け、同じ入力を条件を 1 つずつ変えて判定し直し、結果が変わる条件を並べる。「どうすれば直るか」に直接答えられ、嘘の理由を出さない。損: 「なぜそうなったか」の文は出ない。得: 変更が小さい。
- **実装**: Core `DictionaryManagement.ExplainDetection` (TSV。条件は memory-off / memory-without:語 / before-* / after-* / level:*、2 回目待ちの語は memory-pending)。学習した語を 1 つ外す試しは、`LanguageMemory` が封印・非仮想なので包みではなく、ファイルに結び付かない写し (`CopyWithout`) で行う (languages.json は読むだけ)。判定器は `CreateDefault` + 共有の学習 + `IsCommonJapanese` で IME と揃え、1 回だけ作って使い回す (初回 36 ms、2 回目以降 0.4〜3 ms。AOT 版で実測)。
- **英単語の判定の差**: 画面から `meltype_set_word_checker` で NSSpellChecker を登録し、IME と同じ答えにする (未登録なら同梱の表)。`isWordCallback` は IME 側と辞書アプリ側 (`WordChecker.swift`) に同じものを置き、互いに「同じにしておく」と書いた (IME は AppKit を持ち込まない Kit に移せなかったため複製)。
- **ABI 9 → 10** (`Exports.AbiVersion`、`NativeDictionary`、`NativeCore` の 3 か所)。`tools/check-mac-aot-settings.py` の版数の期待値も 10 に (1-3 単独の時点)。1-2 (11) との統合後は 11 に統一した (10 = 判定を調べる、11 = 確定後の F10/F6 の取消)。
- **english.txt の読み直し**: 辞書ファイルは `MeltypeSession.CreateDefault` (= 入力欄ごとの入力のセッションの作成、IME の `InputController.init`) で 1 回だけ読む。この画面の判定器も最初に調べるときに 1 回。案内文はこのとおりに書いた。
- **実機で確かめること** (リョウさん): ① 「設定・辞書…」の ⌘5 のタブで `kyouhasushidesu` を打つと区間と「条件を変えた場合」が出る ② 「学習した語」に sushi (英字) を足すと「学習した語「sushi」を外す → …」と「この語を消す」が出て、確認のあと消せる (languages.json は調べるだけでは変わらない) ③ 判定の強さ・かな入力の既定が設定タブの今の値になる ④ IME での実際の判定と、画面の区間が一致する (NSSpellChecker 登録の効果。特に `zh` のような語) ⑤ english.txt に語を足したあとの反映の時期が案内文のとおり。
- **未確認**: 画面 (AppKit) の見た目と操作は自動テストが無く、実機で見ていない。IME の判定との一致 (④) は実機未確認。


## 3-2 運用枠 (本家 main との比較表の定期更新)

- **やったこと** (文書だけ。コードは触っていない): `docs/COMPARISON_UPSTREAM.md` を新設 (目的と読み方・比較軸の表・リリースごとの更新手順・更新履歴)。`docs/RELEASE.md` の「Mac 版: 公開前の検査」の先頭に「リリースの前に比較表を更新する」を 1 行足した。README・push・本家への連絡はしていない (§8 の 6・8)。
- **最初の枠で実測した値** (2026-10-15、本家 `45e14f8` と Mac 版 `669ead5`): 同じ正解表 264 件で Mac 版 231 / 本家 243、本家で当たり Mac 版で外れる例 (broken) 28 件・逆 (fixed) 16 件。各ツリー自身の品質テストは Mac 版 1266/1268、本家 1337/1337。**今は柱 A の「下回らない」を満たしていない。** broken 28 件はすべて `upstream-quality.tsv`。原因は未分類 (取り込み待ちか Mac 版固有の回帰かは、本家 PR との対応づけ後に決める。対応づけは未実施)。母数が 69 件違う理由も「本家が `a4b6f1a` 以降に足した分」という推測 (未確認)。mac-terminal の外れ 4 件の内訳・既知の制限 58 件の内訳・ホスト表 7 アプリ・659/661 は、引用元の記載を転記したもので今回再確認していない。
- **未計測**: 実機の体感・本家との機能の網羅比較・本家側のホスト対応と制限の件数。
- **得と損**: 得 = 取り込みの遅れが数字で見える。損 = リリースごとに本家の clone を更新して `tools/eval-compare.sh` を回す手間 (clone の更新を含めて数分)、数字が下がったとき正直に書く判断が要る。

## 追加項目 1-4b / 1-4c (コード系アプリの追加・アプリごとに最後のモードを覚える)

- **1-4b**: `MeltypeSession.MacCodeApps` に WezTerm `com.github.wez.wezterm`・Rio `com.raphaelamorim.rio`・Hyper `co.zeit.hyper`・PhpStorm `com.jetbrains.PhpStorm`・RubyMine `com.jetbrains.rubymine`・DataGrip `com.jetbrains.datagrip`・Android Studio `com.google.android.studio`・IntelliJ IDEA CE `com.jetbrains.intellij.ce`・PyCharm CE `com.jetbrains.pycharm.ce`・VSCodium `com.vscodium` を足した。出典は Homebrew cask (`Casks/{w/wezterm,r/rio,h/hyper,p/phpstorm,r/rubymine,d/datagrip,a/android-studio,i/intellij-idea-ce,p/pycharm-ce,v/vscodium}.rb`) の `uninstall quit:` / `zap` の記述。JetBrains の ID は大文字小文字が製品ごとに違う (今回足した中では PhpStorm だけ大文字。既存の WebStorm・CLion も大文字を含む) ので cask のとおりに書いた (比較は大文字小文字を区別しない)。IntelliJ IDEA CE・PyCharm CE の cask は 2025-12-08 に廃止された (統合版 `com.jetbrains.intellij`・`com.jetbrains.pycharm` は既に一覧にある) ので、CE の 2 件は古い版を入れている人だけに効く。テスト `SetApp_AddedMacCodeAppsAreCode`。
- **1-4c**: Swift の `InputController` に static の `rememberedModes` (bundle ID → モード ID) を置いた。この入力欄でキーを打ったあとに、入力メニュー・Caps Lock (`setValue`) でモードが実際に変わったとき、または英数・かなキーのときに `rememberMode` で覚え、`activateServer` の `setApp` のあとで、覚えたモードがあれば `applyMode` と `selectMode` で上書きする。保存はしない。覚えた記録の無いコード系アプリでは、`currentMode` も英数にする。
- **Swift に置いた理由**: Core の `Direct` の setter は `meltype_set_direct` を通り、`activateServer` が入力メニューに合わせる `applyMode` も同じ経路なので、Core では「利用者の操作」と「起動時の合わせ直し」を区別できない (しかもその時点の `AppName` は前のアプリ)。区別するには FFI を足して ABI を上げるしかなく、Swift 側なら ABI は 11 のまま。損: Core のテストが書けない (`swift build` と実機確認のみ)。
- **範囲**: `setApp` が `.code` を返すアプリだけ覚える・適用する。`.disabled` (アプリ別 OFF・ゲーム) と秘匿入力は今までどおり素通し (`handle` の前半で返る。ただしターミナルのパスワード入力は秘匿入力とは限らない。KNOWN_LIMITS K74)。一般アプリは今までどおり。独自の種類の「最初は英数」(`StartInEnglish`) は Swift から見分けられないので覚えない (損)。
- **検証**: Core のテスト 720/720、`swift build -c release` 成功、eval-compare は broken 0 (231/264 で変化なし)。
- **実機で確かめること** (リョウさん): ① Terminal でかなキー → 別のアプリ → Terminal に戻って日本語のまま ② 覚えていないコード系アプリは英数から ③ 英数に戻して別のアプリ→戻ると英数 ④ 入力メニューの表示が食い違わない ⑤ Meltype を再起動すると英数から ⑥ 一般アプリは変わらない ⑦ 覚えたコード系アプリから一般アプリへ戻ったとき、入力メニューの表示と動きが合うか ⑧ macOS の「書類ごとに入力ソースを自動的に切り替える」が ON のとき ⑨ Terminal.app の sudo のパスワード欄が秘匿入力になるか。 ⑩ トレース (`MeltypeTraceIMK`) で、利用者が何もしていないのに `setValue … changed=true acted=true` が出ないか (出ると、覚えた記録の無いコード系アプリで `currentMode` を英数にしたぶん、日本語と覚えてしまう。R58 任意 1) ⑪ 入力メニューで今と同じ「Meltype」を選び直したとき、`setValue` が来るか (来ないと、表示は Meltype・動きは英数のずれを選び直しで直せない。R58 任意 2)。
- **未確認**: 上の実機の動き。自動の setValue は、印 (この入力欄でキーを打ったか。入力欄を離れると戻す) とモードが実際に変わったかで防いだ。残る前提は、この入力欄でキーを打ったあとに来る自動の setValue が無いこと (実機のトレースで順番を確かめる: setValue / activateServer / deactivateServer を記録するようにした)。

## 追加項目 1-7 (候補の意味に macOS の辞書を補いとして使う)

- **経緯**: 変換中に約 1.5 秒止まると、選んでいる候補の意味を候補ウィンドウの注釈に出す (`ShowCandidateMeanings`、既定 ON)。意味は Core の `meanings.txt` (ウィクショナリー)、無ければ JMdict の英訳だが、英字の候補 (`meeting` など) には何も出なかった。リョウさんの決定 5 (2026-10-15): macOS の辞書を意味の補いに使う (綴りの警告は入れない)。
- **実装の要点** (Swift のみ。Core・FFI は変えない):
  - `mac/Sources/MeltypeIME/CandidateDictionary.swift` を新設。選んでいる候補が ASCII の英字で始まり、英字・`-`・`'` だけの 2 文字以上で、Core の `view.meaning` が空のとき、`DCSCopyTextDefinition` で引く。
  - 呼び方: 専用の直列キュー (`qos: .utility`) で引き、結果をメインに戻して、候補が変わっていない (`meaningKey` を書くたびに進める番号が同じ) ときだけ `showAnnotation`。確定したら `meaningKey` を空にして、待っている意味を捨てる。メインスレッドでは引かない。
  - 引く前に全角英字を NFKC で半角に、`’` を `'` に。語ごとのキャッシュ (プロセス内・200 件・整えた語そのものがキー (`IT` と `it` は辞書が別の項目を返すので分ける)・古いものから捨てる・「定義なし」も覚える)。
  - 表示は、定義が次のどちらかの形のときだけ。(A) 国語辞典の外来語 `見出し␣` + 数字 + 語 → `見出し：本文` (例 `meeting` →「ミーティング：打ち合わせや連絡のための会合」、`apple` →「アップル：りんご」)。(A′) (A) の本文が発音 `| … |` で始まる英英の地名など → `見出し：発音の後ろ` (例 `Kyoto` →「Kyoto：an industrial city…」)。(B) 英和・英英 `語 | 発音 | 本文` → 本文 (例 `Tokyo` →「名詞東京.」)。略語の項目 (定義側ですべて大文字の 2 文字以上) は、選んだ語もすべて大文字のときだけ当てる (`aids` に AIDS を、`it` に IT を出さない。損: 小文字で選んだ略語 (`pc`・`tv`・`usb` など) には、正しい項目があっても意味は出ない)。最初の改行・`。`・`②` の手前で切り、空白をつめて 50 文字ほどで切る。`river` (R. = 比)・`api` (API = 石油協会)・`Einstein` (人名) は形が合わないので出さない。
  - 辞書の意味を出したあと、同じ候補のまま `showModeAnnotation` が「あ」で上書きするので、出した意味を `(meaningKey, 文)` で覚え、同じ `meaningKey` のあいだは出し直す (候補が変われば捨てる)。`ShowCandidateMeanings` は辞書のキューの中で 2 秒ほどキャッシュする (毎回 config.json を読まない。Core は変えない)。
  - `InputController.scheduleMeaning` の今の 1.5 秒の中だけで動く (新しいタイマー・設定は無し)。`ShowCandidateMeanings` が OFF だと Core の意味が空になるだけで Swift からは見えないので、既存の `meltype_settings_get` を `NativeCore.showCandidateMeanings` で読み、OFF なら引かない (config.json を読むので辞書のキューの中で読む。読めなければ既定の ON)。
  - 守り: 辞書が無い・定義が無い・空文字は何も出さない (`isLookupWord` が 2 文字未満・対象外の文字を先に落とし、`DCSCopyTextDefinition` の nil は「定義なし」)。
- **経緯 (続き)**: 以前の調査 (feat/p1-1-7、未統合) は river→R などの誤表示を理由に実装しない結論だった。リョウさんの決定 5 で使う方針に変え、見出しの形 (A)(B) に合うものだけ出す確認で誤表示を抑えた (査読 R52)。
- **プライバシー**: DCS は端末内の辞書を引く。このMacで、引くあいだのネットワークの接続を `lsof -nP -i` (システム全体、引く前との差) で見て、新しい接続は 0 (42 回 + 13 回。`Wikipedia` という語を引いても英和の項目が返り、辞書アプリの Wikipedia の項目は返らなかった。辞書アプリの Wikipedia の ON/OFF は確かめていない)。`Self.trace` / NSLog には語も定義も書いていない。
- **検証**: `cd mac && swift build -c release` が通った。
- **得と損**: 得: 英単語の意味 (`meeting` など) が分かり、別の語の項目 (`river` → R.・`api` → アメリカ石油協会など) は出さない。損: 辞書の並び順で出る辞書が変わる。辞書の最初の項目だけを出すので、ふだんの意味と違うことがある (`it` → イット：性的魅力。`IT` と `it` は辞書が別の項目を返すので、キャッシュも分けた)。形が合わない辞書 (英英を先頭にした環境の一部など) では何も出ない。初めての語は引くのに少し時間がかかる (別のキューなので入力は止まらない)。
- **未確認**: 実機の注釈の見え方。`kyouhameetingdesu` → 変換 → `meeting` の文節を選んで 1.5 秒 → 辞書の 1 文 (ミーティング：打ち合わせや連絡のための会合)。`ringo` → 変換 → `apple` を選んで 1.5 秒 → 辞書の 1 文 (アップル：りんご)。同じ候補のままでも「あ」に戻らないこと。Core の意味の確認は漢字の候補 (`hashi` → 橋) で行い、そのときは辞書を引かない。設定の「候補の意味を表示」を OFF にすると両方出ない。辞書アプリで英語の辞書を外しても落ちないこと。

## 追加項目 1-8 (Tab で変換。P41)

- **経緯**: 本家 1.2.1 の設定「Tab で変換」(`TabConversion`、本家の PR 一覧では #299、コードのコメントでは issue #219。2 つの関係は未確認) を Mac 版に入れた。リョウさんの決定 4: 本家と同じ動きで、**Mac は既定 OFF** (本家は ON。フォームで日本語を打って Tab で次の欄へ移る操作を変えないため)。
- **実装の要点** (`CompositionController.cs`): 入力中の Tab の優先順は 本家と同じ — もしかして → 手動の提案 (英字にする) → 予測候補 → (設定 ON なら) 変換を始める。`TabStartsConversion()` は Space が変換を始める場面だけ true (打ち間違いを直してから、英語で終わっていれば false = 確定して Tab を渡す。箇条書きの - も Space と同じ)。変換中の Tab は `HandleConversionKey` に枝を足し、**Tab で始めた変換の中だけ** Tab = 次の候補・Shift+Tab = 前の候補 (`_tabStartedConversion`)。Space で始めた変換の中・Shift+Tab での開始は、今までどおり確定して Tab を渡す。Esc で戻ったあとの Tab はもう一度変換を始める。旗は `StartConversion`・`CommitText`・ローマ字の再変換・`Reset`・`BeginComposition`・変換中の Back / Esc (本家と同じ。査読 R54) で下ろす。
- **本家との差**: 保護区間は 1-4a で Mac 版にも入ったので、本家と同じく保護区間を含む入力 (後ろに日本語が続く `./build.shde` も) では Tab で変換を始めず、確定して Tab を渡す (`Segment.Protected` を `final: true` で見る。統合の査読 R64 必須 1、テスト `WithProtectedSpan_TabCommitsAndPassesTab_LikeUpstream`)。ただし判定の強さ「手動」では Mac 版は保護区間を作らない (1-4a の設計) ので、手動ではこの判定も働かない (本家は強さを見ない)。`PreviewCandidates` は Mac 版に無いので入れていない (それ以外は Space と同じ条件)。予測候補の Tab (`HandlePredictionKey`) は本家と同じにした (実機確認 D-14): Tab で次、Shift+Tab で前、最後の次・先頭の前は「選ばない状態」に戻って一巡し、選ばない状態からの Shift+Tab は最後の候補。入力中の Shift+Tab も予測に入り、最後の候補を選ぶ。設定「Tab で変換」の ON / OFF どちらでも同じ (テスト `PredictionTab_NextPreviousAndWrap_SameForTabConversionOnOff`・`ShiftTabWithPrediction_EntersAtLast`)。↓ ↑ は今のまま (↓ は最後で止まる)。
- **設定の配線**: `Settings.TabConversion` (既定 false)、`MacSettingsCatalog` の「入力」に項目 (説明は本家と同じ文 + Mac の既定 OFF の注)、`MeltypeSession.CreateDefault` で `shared.Current.TabConversion` (config.json の変更が 0.5 秒で効く)。**FFI は変えていない** (ABI 11 のまま。設定タブの JSON の仕組みで渡る)。`tools/check-mac-aot-settings.py` に、一覧に出る・既定 OFF・保存できる・既定に戻る、を足した。
- **1 音確定・確定後の F6 ボックスとの組み合わせ**: 1 音確定 (TokMode の Shift+↓) の残りも Tab で変換を始められ、Tab で次の候補になる。確定後の F6 で開いた変換ボックスは最初から変換中なので Space で始めた変換と同じ扱いで、設定 ON でも Tab は今までどおり (確定して Tab を渡す)。Esc 1 回で元の語に戻る動きも変わらない (どちらもテストあり)。
- **検証**: Core 733/733 (Tab で変換のテスト 14 件: 本家の `TabConversion_*` 9 件を写したもの + 既定 OFF + OFF のとき今までどおり + 1 音確定との組み合わせ + F6 ボックス + BackSpace で戻したあとの Tab)、eval-compare broken 0 (231/264 同じ)、`swift build -c release` 通過、`build.sh --no-install` と `check-mac-aot-settings.py` 全 PASS。 統合 (trial/merge-p1b、1-4a・1-4b/c・1-7・1-8) の後: Core 753/753 (Tab で変換のテストは保護区間との組み合わせを足して 15 件)、eval broken 0 / fixed 25、build.sh・AOT 6 PASS・辞書アプリの self-test 212 PASS。
- **得と損**: 得: Microsoft IME と同じ「Tab で変換」が使える (設定 ON)。損: ON にすると、フォームで日本語を打ってから Tab で次の欄へ移る操作が、確定ではなく変換開始になる (もう一度 Tab は次の候補、Enter で確定してから Tab)。そのため既定は OFF。
- **実機で確かめること** (リョウさん): 設定「Tab で変換」を ON にして、① Tab で始めた変換 (候補が 2 つ以上) で、Tab / Shift+Tab に候補ウィンドウの選択がついてくるか (候補ウィンドウは `IMKCandidatesSendServerKeyEventFirst` が変換中で候補が 2 つ以上のときだけ ON になる (`InputController.swift:697` 付近)) ② Space で始めた変換の中の Tab で、Tab がアプリに届いて次の欄へ移るか ③ 既定 OFF で今までどおり ④ `hello` → Tab で確定して Tab が届く。
- **未確認**: 実機の IME での操作 (上の ①〜④)。Windows 側の配線 (`TrayApplicationContext.cs`・`CompositionService.cs` に TokMode と同じ形で 1 行ずつ) はビルド未確認。候補ウィンドウと Tab の関係 (Swift 側は変えていない)。

## 追加項目 1-4a (保護区間 P42)

- **経緯**: ターミナルのコマンドの入力で、パス・オプション・変数が崩れていた (`./src/main.ts` → `。/src/main.ts`、`--no-install` → `ーーのーinstall`、`$HOME` → `＄HOME`、`/tmp/fooni` → `/tmp/ふぉおに`、`tar -xzvf a.tar.gz` → `tar -xzvfあ。tar。gz`)。本家 1.2.1 の `ProtectedSpanScanner` (メンション・URL・メール・パス) のうち、Mac 版に無かった「パス」を、本家の `TryPosixPath` と同じ規則で入れ (GPL-3.0、出典は `CompositionDetector.ProtectedEnd` のコメント)、オプションと変数は Mac 独自に足した。メンション・URL・メールは Mac 版に別の規則 (`UserNameEnd` ほか) が既にあるので触らない。
- **実装の要点** (Core のみ。ABI・Swift は変えない):
  - `CompositionDetector.FindSpans` の先頭で `ProtectedEnd` を呼び、語の頭から空白・入力の終わりまでを英語の区間にする。ほかの判定 (記号・DotSuffixEnd・UserNameEnd ほか) より先に決め、あとの「少し後ろから長い英単語」の取り直しも効かせない。判定の強さが「手動」(Mac 版の既存の規則とそろえた。本家は強さに関係なく守る) とかな入力 (`KanaInput`。JIS かなの `/` は `め`) では使わない。
  - **語の頭** (査読 R50 必須 1): 変換ボックスの先頭が語の頭かを `Segment` / `FindSpans` / `ProtectedEnd` に `startsWord` で渡す。一番外の呼び出しでは、キャレットの前の文字が「無い・分からない・空白・開きの記号」で終わるときだけ true (`CompositionText.PrecedingWordStart`。Controller が `_precedingText` から決める。自分の確定を信じる間は `_lastCommitText` の末尾。`PrecedingEnglish` と同じ場所で更新)。途中から呼び直す所 (1 音確定の固定のあとの続き・`zh` の矢印の後) は、直前の単位が空白・開きの記号のときだけ true。変換ボックスの中では、直前の単位が空白・開きの記号なら語の頭。これで、日本語を確定した直後の `/fuka`・`/iie`・`/oosaka`・`/2kai` は守らない。
  - **開きの記号の後** (推奨 3): `` ` ``・`「`・`『`・`(`・`（`・`"`・`'`・`[` の直後も語の頭 (本家 `IsTokenBoundary` を参考)。範囲は対応する閉じの記号まで (`` `./build.sh` wo `` → `` `./build.sh` を``)。閉じの記号は半角のまま範囲に入れる。ただし `[` `]` は日本語の入力では `「」` なので、`]` は範囲に入れず日本語のまま (`[~/Documents]wo` → `「~/Documents」を`)。閉じの記号が無ければ空白まで。
  - パス: `./` `../` `~/` のあと、または `/` のあとに要素 (英数字・`.` `_` `~`) が続く形 (`a/b`・`1/2`・`///` は対象外)。オプション: `-` + 英字は、ローマ字として読めない (`-xzvf`・`-rn`)・大文字を含む (`-Wall`)・1〜2 文字で終わる (`-la`・`-m`)・直前が英語 (`find . -name`) のどれか。`-kyou` は今までどおり `ーきょう`。`--` + 英字 (小説・台詞の「ーー」を `--` で打つ書き方があるので、`-` と同じく絞る・査読 R50 推奨 2): `=` を含む (`--color=auto`)・`-` で区切った部分のどれかがローマ字として最後まで読めない (`{ IsValid, Partial が "" か n }` でない。`--no-install`・`--dry-run`・`--no-edit`・`--ff-only`・`--all`・`--amend`。長音の `-` の `--ko-hi-wo`・`--ra-men` は全部読めるので守らない。R57 推奨 C・R62 推奨 1。`-` の前の部分はいつもこの条件、最後の部分は打ち終わり (確定・後ろが空白) のときだけ。打っている途中は `IsValid` だけ)・大文字を含む (`--no-Sushi`)・最初の英字がローマ字として読めない (`--help`・`--force`・`--version`)・直前が英語のときだけ。`--sorekara`・`--sate`・`--ko-hi-wo`・`--ra-men`・`--no-sushi` は `ーーそれから`・`ーーさて`・`ーーこーひーを`・`ーーらーめん`・`ーーのーすし` のまま。変数: `$` のあとが英字か `{`。
  - 範囲は空白まで。ただしパス・変数の最後の要素が拡張子付きなら `DotSuffixEnd` の終わりで止め、後ろは今までどおり判定する (`./build.shde` → `./build.shで`)。拡張子が無ければ空白まで英字のまま (`/tmp/fooni`。リョウさんの決定 1)。
  - `CompositionSegment` に `Protected` (既定 false) を足し、`EnglishMask` で保護した区間を「表示で英字」とみなす (ローマ字の打ち間違いの直しが、保護区間の中の `./onegaishimsu` を `onegaishimasu` に直さない)。学習は、`LanguageMemory.Remember` が英小文字だけの語しか受けない (保護区間は記号を含む) ので入らない。もしかして・自動の確定し直しは、英語の区間・英小文字だけの語しか対象にしないので入らない。学習 (確定後の F6 → Enter・入力中の F6 → Enter)・もしかして (`/tmp/buresureddo` → Tab)・確定し直し (守った区間の後ろで次の語を確定) は、`Protected_IsNotLearnedOrCorrected` (入力中の F6)・`Protected_Tab_DoesNotSuggestACorrection`・`Protected_IsNotRewrittenByTheNextCommit` で確かめる。確定後の F6 → Enter は `CompositionTests.Keyboard` では経路 (`MeltypeSession` の `TryToggleLastWord`) に届かないので、`LastWordToggleTests.ProtectedSpan_AfterCommit_F6_DoesNotLearn` で `MeltypeSession` を通し、F6 が `Consumed`・`Toggle` になった (経路に届いた) ことを確かめてから、学習しないことを確かめる (`--Sushi`・`~/Sushi`・`$SUSHI`。R57 推奨 B)。`FixEnglishTypo` は今の Mac 版のスペルチェッカーに打ち間違いの自動修正が無いので何もしない (将来足すと `/tmp/teh` も直すので、そのときは保護区間を除く。コード中にコメントを残した)。
  - 「英語の前後に空白を入れる」(`SpaceAroundEnglish`) が ON のとき、保護区間を含む確定も設定どおり空白を入れる (`./build.shwo` → `./build.sh を`。R57 推奨 A2 で (a) を採った)。本家は末尾が保護区間のとき空白を入れないが、Mac 版は設定どおり入れる。パスそのものは崩れない (`AddSpacesAroundEnglish` の語の文字に `/` `~` `$` `=` が入らず、保護区間の左は空白・開きの記号・入力欄の先頭のどれかなので、空白が入るのは保護区間の終わりと後ろの日本語の間だけ)。`SpaceAroundEnglish_KeepsProtectedSpansIntact` で確かめる。
  - **アプリに渡した Space の記録** (R57 必須 A): 日本語を確定してから、入力していない状態で Space (アプリに渡る) → `/tmp/foo` と打つとき、`NoteBreakKeyPassedToApp` が Enter・Tab と同じく Space も (Shift+Space も。R62 任意 1)自分の確定の記録 (`_lastCommitText`) に足す (BackSpace で記録の最後が空白なら戻す)。足さないと、確定から 1.5 秒の間は入力欄から読んだ文字を使わないので、空白の後なのに語の頭と読めず、`ファイルは /tmp/foo` の `/tmp/foo` が `/tmp/ふぉお` に崩れた。`LastWordToggleTests.ProtectedSpan_AfterASpaceThePassedToTheApp_IsStillProtected` で、`MeltypeSession` を通して (Space・Enter・BackSpace を入力欄にも反映する `Editor`) 確かめる (直す前は落ちることを確かめた)。
  - **確定済みの開きの記号** (R57 任意 D・E): 入力欄の側にある開きの記号も、閉じを決める。`CompositionText.PrecedingTail` (キャレットの前の文字列) から `PrecedingWordStart`・`PrecedingOpener` を決め、`IsWordStartAfter` と `OpenerBefore` を使う。確定済みの `「` に対応する `]` は日本語の `」` のまま (`ファイルは「` + `~/Documents]wo` → `~/Documents」を`)、確定済みの半角の `[`・`(` に対応する閉じは半角のまま範囲に入れる。確定済みの全角の `（` は、打った `)` を閉じと読み替える (`「` → `]` と同じ。R62 任意 2)。打った `[` `]` は、英語の後ろでも日本語の入力では `「」` (`IsAsciiSymbol` が除く。bot「Thinking」) なので、打った `[` に対する `]` は日本語のまま対にそろう (D は、`[` が半角になる確定済みの側でだけ起きる)。`SquareBracket_FollowsTheWidthOfTheOpener` で確かめる。
- **検証**: Core のテスト全件 737/737 (r4 で `DoubleDash_WhileTyping_StaysJapaneseUntilTheEnd` を 1 件足した)。`ProtectedSpanTests` は 15 件 (語の頭・`--` の絞り込み・開きの記号・確定済みの開きの記号・手動・`SpaceAroundEnglish`・学習・もしかして・確定し直しほか)、`LastWordToggleTests` に 2 件 (確定後の F6 は学習しない・アプリに渡した Space)。`eval/cases/mac-protected-spans.tsv` を足し (R50 推奨 7 の行・開きの記号・`--`・R57 の `--dry-run`・`--ko-hi-wo` ほか)、`tools/eval-compare.sh <比べる元の作業ツリー> "$PWD" eval/cases` で **broken 0 / fixed 22** (合計 251/307 → 273/307)。
- **得と損**: 得: ターミナルのコマンド・パス・オプション・変数を、変換ボックスの中で崩さない。直前に日本語を確定した直後の `/fuka` は守らない。損: (1) `-` + 英字の 1〜2 文字 (`-ka`) と、直前が英語のときの `-xxx` は、日本語の箇条書きのつもりでもオプションとして英字のまま。(2) `-ky` まではオプション、`-kyo` で日本語に戻る (打っている途中で見え方が変わる)。(3) 空白まで 1 語なので、拡張子の無いパスの直後の助詞は空白で区切る必要がある (KNOWN_LIMITS K77)。(4) `--no-edit` は、打っている間は日本語に見え (`--no-edi`)、確定・変換 (Space・Enter・Shift+Space・←→) で英字になる (打っている途中に子音ごとに英字と日本語が入れ替わらないため。R62 推奨 1)。(5) `--name`・`--no-sushi` のように、`-` で区切った部分が全部ローマ字として読めるオプションは、英語の後ろでないと守らない (`--sorekara`・`--ko-hi-wo` の「ーー」と長音を守るため。F10 で英字に。K78)。(6) 判定の強さ「手動」では守らない (Mac 版の既存の規則とそろえた。本家は守る)。(7) 「英語の前後に空白を入れる」が ON なら、保護区間の後ろにも空白が入る (`./build.sh を`。本家は入れない。パスそのものは崩れない)。(8) 入力欄の先頭が語の頭かは、キャレットの前の文字が読めるアプリでだけ正しく決まる。読めないアプリでは、自分の確定の記録 (アプリに渡した Space・Enter・Tab も含む) があればそれを使い、無ければ (入力欄を移った直後など) 「分からない」→ 語の頭とみなす。Space で文字が入らない画面でも空白を記録するが、次の入力は別の入力欄になることが多いので害は小さい。 (9) 日本語の中の `"` `'` は `”` `’` で確定されるので、その後ろは語の頭にならない (`“~/foo” を` の `~/foo` は守らない)。
- **得と損 (追記・実機確認 A-5)**: 「直前が英語」は同じ行の英語だけ。改行のあと (キャレットの前が `\n`・`\r`) は前の行が英語でも直前とみなさず、`--sorekara` は「ーーそれから」。損: Terminal のシェル行 (プロンプト `… % ` の後ろ) は同じ行の英語の後ろなので、`--sorekara` も英字のまま (K78)。`-la` のような 1〜2 文字と、`--no-install` のような読めない部分があるものは、改行のあとも守る。
- **得と損 (追記・査読 R66)**: 前の行が英語でも、改行のあとの行頭の `->`・`-` だけ・`-10%` は、入力欄の先頭と同じく全角 (`ー＞`・`ー`・`ー10％`) になる (以前は半角)。改行のあとの字下げ (空白・タブ・全角空白だけ) も行頭とみなす。字下げが 20 文字以上で改行が見えない (前の文字は 20 文字までしか読まない) ときは同じ行とみなし、最後の確定が英語なら英字のまま守る (安全側)。損: エディタで字下げした続きの行 (シェルの `\` の次の行・YAML・Dockerfile) の `--name`・`--save` は「ーーなめ」になる (以前は字下げのおかげで英字。F10 で英字に戻せる。ターミナルの続きの行は前に `> ` があるので今までどおり守る。査読 R67)。
- **未確認**: 実機 (Mac の IME としての動き) は未確認。今回は直していない別件: `cd`・`ls` のような 2 文字の英字は英語と判定されず、Space で変換が始まって空白が消える (`cd ~/Downloads` → `cd~/Downloads`、`ls -la` → `ls-la`)。`mac-terminal.tsv` の既知の外れ (`cd ~/Documents/Meltype`) と同じ原因で、保護区間とは別。


## 追加項目 1-7 の続き (候補の意味を自前の小さなパネルで出す。K47)

- **経緯・実機で分かったこと** (2026-10-15、macOS 26.6.2、TextEdit、試験用ビルドのトレース): (1) `IMKCandidates.showAnnotation` は画面に何も出さない。公式の呼び元 (`candidateSelectionChanged`) で固定の文字を出しても、1 周遅らせて文字色・背景色を付けても見えない。(2) 自プロセスの窓は候補ウィンドウ (`IMKUIPanel`、level 20) だけで、注釈用の窓は作られていない。(3) `candidatesWindow.candidateFrame()` は大きさだけ正しく位置は (0, 0)。`NSApp.windows` の可視の `IMKUIPanel` の `frame` (画面座標) は正しい。
- **実装の要点** (Swift のみ):
  - `mac/Sources/MeltypeIME/AnnotationPanel.swift` を新設。枠なし・`.nonactivatingPanel`・クリック透過・影あり・角丸の `NSPanel` (key/main にならない)。背景は `NSVisualEffectView` (`.popover`)、文字は `labelColor` の折り返すラベル (12pt・最大幅 300pt・最大 3 行・末尾 `…`)。level は候補ウィンドウの 1 つ上。
  - 位置は表示のたびに `NSApp.windows` から可視の `IMKUIPanel` を探し、右隣 (x = maxX + 4、上端をそろえる)。画面の `visibleFrame` をはみ出すなら左隣、上下も収める。見つからなければ出さない。
  - `InputController`: 出す所を `presentMeaning` に集め (秘匿入力の間は出さない。`ShowCandidateMeanings` OFF は今までどおり意味が空になる)、IMK の `showAnnotation` の呼び出しは全部やめた (入力モードの「あ」「あ (コード)」の表示も無くなり、`showModeAnnotation`・`shownDictionaryMeaning` は削除)。隠す所: `meaningKey` の didSet、`updateCandidates` の else、`hideComposition`、`deactivateServer`、利用者が候補ウィンドウの選択を動かしたとき (`candidateSelectionChanged` で `isSelectingProgrammatically` が false)。語・意味はログに書かない。
- **得と損**: 得: 意味が実際に見える。損: 標準の見た目と少し違う。候補ウィンドウの位置を IMK の非公開のクラス名 `IMKUIPanel` で探すので、macOS の更新で見つからなくなったら出ないだけ (安全側)。入力モードの「あ」の表示は無くなった。候補ごとの注釈 (`notes`) は出さず、意味だけ。
- **査読 R68 の反映 (r2)**: (1) 必須: `candidateSelectionChanged` で、利用者が動かした選択が Core の選択 (`lastView?.selectedIndex`) と違うとき `meaningKey = nil` (世代が進み、隠れ、待っているタイマー・辞書の completion も止まる)。同じなら何もしない。(2) 見張り: パネルが見えている間だけ 0.3 秒ごとのタイマー (`.common`) で候補ウィンドウを探し直し、見つからない・見えない・`frame` が出したときと違うなら隠す。`hide()` で止め、出し直すたびに古いタイマーを止める。候補ウィンドウを出し直す枝でも `meaningKey = nil`。(3) 任意: 高さを行の高さ × 3 で切る・角丸は `maskImage`・どちらの隣にも入らない画面では出さない・見つからないときはトレースに「候補ウィンドウが見つからない」だけ・秘匿入力の確認は `presentMeaning` に 1 か所・古いコメントを直した・SPDX 行を足した。(4) 文書: K47 を「直った (実機未確認)」に、候補ごとの注釈 (`notes`) は新しい K80 (直す予定) に分け、K46 の書き方を直した。
- **査読 R69 の反映 (r3)**: 3 行の上限は、同じラベル・同じ幅 (300pt) で「あ」3 行を init で 1 回だけ `sizeThatFits` で測った高さ (r2 の「行の高さ × 3」は日本語の代替フォントでずれるため)。利用者が選択を動かしたときのトレースに番号 (数字だけ) を、出せなかった理由ごとの文言 (候補ウィンドウが見つからない / 置く場所が無い / 文字が空) を足した。マスクは init で 1 回、見張りのタイマーに tolerance 0.1。損: 絵文字・発音記号など「あ」より高い行が 3 行あると、3 行目の下が数 pt 切れうる (実機の ⑦ で見る。切れていたら上限を外す。査読 R70 任意 1)。
- **検証 (r3)**: swift build・build.sh --no-install 通過。
- **実機 (2026-10-15、リョウさん、TextEdit・ライトモード)**: `hashi` の「橋」で 1.5 秒後に候補ウィンドウの右横に意味が出た (スクリーンショットで確認)。ただし 1 行で「…」に切れていた (`.byTruncatingTail` だと NSTextField が 1 行になる) ので、`.byWordWrapping` + `truncatesLastVisibleLine` にして 3 行まで折り返すように直した (査読 R71)。実機で 3 行に折り返し、最後の行の末尾に「…」が付くことを確かめた (14:48、`hashi` の「橋」、スクリーンショット)。ダークモードは未確認。
- **検証 (r4)**: swift build・build.sh 通過。実機に入れ直した。
- **検証** (r2): `swift build -c release` 通過、`./build.sh --no-install` 通過、Core 758/758。
- **実機で確かめること** (リョウさん): ① `hashi` を Space で変換して 1.5 秒 → 候補ウィンドウの右隣に意味 ② Esc・Enter・候補のクリック・Cmd+Tab・入力ソースの切り替えで消える ③ マウスで選択を動かすと消え、また出てこない ④ 画面の右端では左隣に出る ⑤ ダーク・ライト・フルスクリーン・別のディスプレイ ⑥ 設定「候補の意味を表示」を OFF にすると出ない ⑦ `kyouhameetingdesu` → `meeting` の文節で辞書の意味が出る ⑧ 意味が出たまま 5 秒ほど置いても消えない ⑨ 候補が 10 個以上ある語で次のページに送り、1.5 秒待つと出て、すぐには消えない ⑩ 候補ウィンドウが閉じたら、0.3 秒ほどで意味も消える。
- **未確認**: ダークモード・複数ディスプレイ・フルスクリーン。絵文字・発音記号など高い行があると「2 行 + … + 下に空き」になるかもしれない (査読 R71 任意 2)。右隣に出ること・ライトモード・3 行への折り返しは確認済み。



## 追加項目 1-5 の代わり (コード系アプリのコマンド名。K79)

- **経緯** (リョウさんの決定、2026-10-15): 1-5「英数モードのままローマ字を見つけて日本語にする」は作らない。実機のトレース (Terminal.app) で分かった 3 点: (1) 変換中の文字が無いとき、Enter・Ctrl+C・F10 は IME に回ってこない。(2) 送った文字も、入力側から置き換えられない。(3) 確定後の F10 / F6 はそもそも効かない (変換中の F10 は届く。K69 を訂正した)。代わりに、1-4c (アプリごとに日本語モードを覚える) と 1-4a (保護区間) で「ターミナルを日本語モードのまま使う」形を良くする。残る大きな穴が K79 (`cd ~/Downloads` で `cd` が英語と判定されず、Space で変換が始まって空白が消える・`make` → まけ)。
- **実装の要点** (Core のみ。Swift・設定・FFI は変えない):
  - `CompositionOptions.CodeApp` (`Func<bool>`、既定 false) を足し、`MeltypeSession.CreateDefault` で `AppProfile == AppProfile.Code` を返す (`TokMode`・`TabConversion` と同じ形。設定ではなくアプリの種類)。Windows 側は渡さない (既定 false)。
  - `CompositionDetector.CommandNames` (小文字・完全一致の `HashSet<string>`)。`CompositionText.SegmentUnits` の先頭で、コード系アプリ・打ち終わり (final: 確定・Space・Tab・Enter)・語の頭 (`PrecedingWordStart`)・かな入力でない・判定の強さが「手動」でない・打った英字まるごと (単位の Raw + 入力途中の子音) が一覧の語、のすべてが揃ったとき、全体を 1 つの英語の区間にする。`SegmentsKey` に `CodeApp` も入れた。コード系アプリでは語の頭の条件をゆるめた (`IsWordStartAfter(…, codeApp)`: キャレットの前の、末尾の ASCII 数字の並びをとばした文字が ASCII 以外の文字 (日本語。全角空白は空白) でなければ語の頭。`sleep 10`・`v2`・`10` は語の頭、`予算は10`・`第3` は語の頭ではない。Terminal.app は変換中の文字が無いときの Enter を IME に渡さないので、`ls`⏎ のあとも自分の確定の記録の末尾が `ls` のままで、続けて打つ `cd` が語の頭にならなかった。コマンド名の判定と 1-4a の保護区間の両方に効く)。一覧の判定で英字にした確定は、確定し直しの対象 (`_correctable`) に入れない (`CompositionText.IsCommandName`。`open` Space → `shiryou` で おぺん に書き戻されない)。予測候補が出ていても、一覧の語の打ち終わりの Tab は予測に入らず、確定して Tab を渡す。打っている間 (final でない) は今までの見え方で、確定で英字になる (1-4a の `--no-edit` と同じ考え方)。
- **一覧の語と理由** (`--repro` で語 + Space・語 + Enter を試した結果。同梱の英単語の一覧を使った今の判定):

  | 語 | 今の判定 (Space / Enter) |
  | --- | --- |
  | `cd` `ls` `rm` `cp` `df` `sh` `gh` `rg` `fd` `jq` `wc` | 英語と判定されず、Space で変換が始まり空白が消える (Enter は英字のまま) |
  | `ln` `du` `vi` `su` `env` `uv` | lん・づ・ゔぃ・す・えんv・うv (Space で変換が始まり、Enter で日本語) |
  | `man` `make` `sudo` `open` `node` `go` `bun` `nano` `more` `eza` `unalias` | まん・まけ・すど・おぺん・ので・ご・ぶん・なの・もれ・えざ・うなぃas (Space で変換が始まり、Enter で日本語) |
  | `bg` `fg` `tr` `bc` | 英語と判定されず、Space で変換が始まる (Enter は英字のまま) |
  | `dd` `nc` `uname` `nohup` `time` `date` `tee` | っd・んc・うなめ・のふp・ちめ・だて・てえ (Space で変換が始まり、Enter で日本語) |
  | `echo` `code` | 指示書で崩れると挙げられた。同梱の一覧では崩れない。原因は未確認 |
  | 入れなかった (日本語の「店」としてよく使う) | `mise` (ツールとしての利用者は少ない) |
  | 入れなかった (今の判定でも崩れない) | `codex` `vim` `hugo` `mv` `ps` `ip` `deno` `less` `kill` `top` `cat` `tar` `zip` `pip` `npm` `npx` `git` `bat` `brew` `curl` `ssh` `scp` `sed` `awk` `head` `tail` `sort` `find` `grep` `diff` `touch` `chmod` `chown` `which` `where` `export` `source` `exit` `clear` `history` `alias` |

- **検証**: `CommandNameTests` は今 19 件 (最初は 14 件。r3 までで 18 件、r4 で `CodeApp_SecondLineAfterCommandWithTrailingDigits_IsAtWordStart` を 1 件足し、`CodeApp_AfterDigit_DecidedByCharBeforeDigits`・`CodeApp_AfterF6_TabChoosesPrediction` は確かめを足した) を足した (一覧を空にした状態で数え直すと 8 件落ちる: `CodeApp_CommandThenSpace_CommitsEnglishWithSpace`・`CodeApp_CommandThenEnter_CommitsEnglish`・`CodeApp_CommandThenTab_CommitsEnglish`・`CodeApp_TypingCommands_FullLine`・`CodeApp_NotAtWordStart_IsUnchanged`・`CodeApp_SecondLineAfterEnterThatNeverReachedTheIme_IsStillAtWordStart`・`CodeApp_CommandCommit_IsNotCorrectedLater`・`CodeApp_CommandWithPredictions_TabCommitsEnglishAndPassesTab`。`CommandNames_AreLowerCaseWordsOnly`・`CodeApp_EveryCommandName_CommitsEnglishOnSpaceEnterAndTab` は一覧が空だと回すものが無く通る。ほかは一般アプリ・打っている途中・一覧と完全一致しない (`makeru` → まける)・手動で今までどおり)。査読 R72 の反映 (r2) では、語の頭の条件をゆるめる・確定し直しの対象から除く・予測があっても Tab で英字に確定する・`SegmentsKey` に `CodeApp` を足す、の 4 つを直し、一覧に 12 語 (`bg` `fg` `tr` `bc` `dd` `nc` `uname` `mise` `nohup` `time` `date` `tee`) を足した。Core 全件 772/772。査読 R73 の反映 (r3) では、`SegmentsKey` に `CodeApp` を**実際に**足した (r2 の報告は「入れた」と書いたが入っていなかった)・`mise` を一覧から外した (r2 で足した 12 語は 11 語になった)・数字のあとは語の頭にしない (r4 で直した)・F6 で「まけ」にしたあとは予測を Tab で選べる (`IsCommandName` は Mode が Auto のときだけ)・予測つき Tab のテストで Tab がアプリに渡ることを確かめる・自分の確定の記録の経路 (`kyou`⏎ のあとの `/fuka`) のテストを足した。Core 全件は r3 で 776/776。査読 R74 の反映 (r4) では、r3 の「数字のあとは語の頭にしない」が `sleep 10`⏎ のあとの `cd` (自分の確定の記録の末尾が数字) を落としていたので、末尾の ASCII 数字の並びをとばして、その前の文字で語の頭を決める形にした (`sleep 10`・`v2`・`10` は語の頭、`予算は10`・`第3` は語の頭ではない)。追加のテストは直す前のコードで落ちることを確かめた。Core 全件 777/777。`eval-compare` は broken 0 / fixed 0 (eval は CodeApp を渡せないので、`mac-terminal.tsv` の `cd ~/Documents/Meltype`・`mac-protected-spans.tsv` の `cd ~/Downloads` は既知の外れのまま。コード系アプリでは `CodeApp_TypingCommands_FullLine` で直っている)。
- **得と損**: 得: ターミナルを日本語モードのままコマンドが打てる (`cd`・`ls`・`make`・`sudo` が英字で確定し、Space は空白つきの英字で確定)。損: 打っている間は日本語に見えるが、Space・Enter・Tab で英字で確定する。日本語にしたいときは確定の前に Shift+Space か F6 (Terminal.app では確定後の F6 は届かない)。効くのはコード系アプリ全部 (ターミナル・エディター・利用者が種類を『コード』にしたアプリ)。VS Code などで Markdown を書くとき、語の頭で一覧の語を単独で打つ場合にも効く (`bun`→文・`man`→万・`more`→漏れ・`make`→負け・`go`→語・`su`→酢・`node`→ので・`date`→伊達・`time`→ちめ)。一般アプリでは何も変わらない。コード系アプリで F6 → Enter で日本語にすると、その語を日本語として学習し全部のアプリに効く。コード系アプリでは一覧の判定が学習より先に効くので次も英字。
- **実機で確かめること** (リョウさん): Terminal.app で日本語モードのまま `cd ~/Downloads`・`ls -la`・`make`・`sudo ls`・`git status`・`kono bagu wo naoshite`。
  - **まだ直らない場面**: 日本語で終わる行の次の行 (`mkdir 資料`⏎ のあとの `cd 資料`、Claude Code に日本語を送ったあとシェルに戻った直後) は、Terminal.app が入力欄の中身を返さないので、まだ直らない (日本語の後は語の頭にしない)。そのときは Space の前に F10 で英字に。時間で語の頭を決める案は、AI の入力欄で日本語を壊すので採らなかった。数字のあとは、数字をとばしたその前の文字で決める (`予算は10` の `man` は語の頭にしない。`sleep 10`⏎ や `kill 1234`⏎ のあとの `cd` は語の頭)。小数・桁区切り (`予算は1.5`・`予算は1,000`) のあとは `.`・`,` で決まるので語の頭になる (査読 R75 の任意。まれなので文書だけにした)。
- **未確認**: 実機 (Terminal.app・iTerm2・VS Code のターミナル)。macOS のスペルチェッカーでの `echo`・`code` ほか一覧外の語の判定。
- **実機確認 (2026-10-15、4712b1f)**: Terminal.app で `cd ~/Downloads`・続けて `ls -la`・`sleep 1`⏎ のあとの `cd ~`・`make`・`date`・`git status` が英字のまま実行された。`make` + Shift+Space で日本語に変換できる。Claude Code の入力欄で `kono bagu wo naoshite` が日本語になる。TextEdit の `make` Space は今までどおり日本語の変換。

## 追加項目 Phase 2 (判定の壁を越える実験: 2-1〜2-5)

- **経緯**: [STRATEGY_2026-10.md](STRATEGY_2026-10.md) の Phase 2 の 5 項目を、項目ごとの実験ブランチ (`exp/p2-2-1`〜`exp/p2-2-5`。手元のみで公開していない) で測り・設計し、別レーンの査読 (R-P2-1〜R-P2-5、非公開) を受けた。リョウさんの決定 (2026-10-15): **文書と表だけを main に入れ、コード (実験フラグ・測定ツール・使い捨て IME) は入れない**。文書の行番号は 54a2002 時点。
- **結論と文書**:
  - 2-1 Zenzai の言語モデルで「迷ったときの最終判定」: **no-go**。門付き (P-合計・τ=8) でも直るのは `pedia` 系に偏り、τ の選択に使っていない 46 件で直 0 / 壊 2 (310 件で直 8 / 壊 3)。遅延の p95 が 40 ms を超える測定がある。[EXPERIMENT_2-1_RESULT.md](EXPERIMENT_2-1_RESULT.md)、データ [eval/lm-judge/](../eval/lm-judge/) (コードは `exp/p2-2-1` の `f923a02`)。
  - 2-2 キャレット前 20 文字の英日比率: **no-go**。R3 型 (英語の名詞句で始まる日本語の文) を 12 件中 6 件壊し、閾値 0.8・0.9・片側だけでも直らない。[EXPERIMENT_2-2_RESULT.md](EXPERIMENT_2-2_RESULT.md)・[EXPERIMENT_2-2_HOST_CONTEXT.md](EXPERIMENT_2-2_HOST_CONTEXT.md) (実験 A の手順書)、表 [eval/context/](../eval/context/) (コードは `exp/p2-2-2-on-main` の `3672d5f`)。
  - 2-3 ライブ変換: **README の前面化だけ go**。実機確認 1 は 2026-10-15 に合格 (`kyouhagoogledekensaku` のライブ表示が「今日はgoogleで検索」。macOS 26.6.2・JIS) し、README の「できること」にライブ変換の 1 行を足し、Zenzai の行に「既定 OFF」を足した (`exp/p2-2-3` の `a30d35a` と同じ差分)。Zenzai の既定 ON・4 文字のしきい値の引き下げはしない。[EXPERIMENT_2-3_LIVE_CONVERSION.md](EXPERIMENT_2-3_LIVE_CONVERSION.md)。
  - 2-4 Foundation Models: **自動整形は no-go** (20 文中 6 文で破壊)、**選択範囲の訳は保留** (意味の誤り 3/10)、IME の中で確かめる**実験 0b は今は見送り**。[EXPERIMENT_2-4_RESULT.md](EXPERIMENT_2-4_RESULT.md)、測定データ [eval/p2-4/](../eval/p2-4/) (`fmprobe`・0b の手順書は `exp/p2-2-4` の `416819c`)。
  - 2-5 打鍵の間隔 (本家 #286): **条件 0 と段階 0 だけ go** (Meltype のコード変更なし)。段階 1 (計測コード) はその結果を見てから。[EXPERIMENT_2-5_DESIGN.md](EXPERIMENT_2-5_DESIGN.md)。
- **検証**: Core 777/777 (統合ブランチ `docs/phase2-results`、2026-10-15)。`--expect-all eval/cases` は 310 件・ルール正解 276 で、2-1 の文書の新 main の値と同じ。`src/`・`mac/` は cad2336 から変えていない。足した表は `eval/cases` の外 (`eval/context/`・`eval/lm-judge/`・`eval/p2-4/`) で、`--expect-all eval/cases` は今までどおり 4 つの表だけを読む。
- **得と損**: 得: 5 項目の go/no-go と根拠の数字が公開の文書で辿れる。前文脈つきの表 (`eval/context/`) は別の案を測るときに使える。損: 文書が指す測定ツール・実験フラグは公開していないので、公開リポジトリだけでは再現できない (再現には手元の実験ブランチが要る)。
- **未確認**: 2-2 の実験 A (実機のホスト別の取得率)。2-3 の「表示と確定の食い違い」(Space で英語の語で終わる確定だけ `Convert` を通る) の実機確認と、FAQ・K51 の Apple の日本語入力との比較の断定 (直していない) [追記 2026-10-15: 直した。FAQ・K51・STRATEGY は Meltype の動きだけを書き、Mac 標準との比較は実機で確かめてから書く]。実機確認 1 はアプリ名が報告に無い (手順は TextEdit の指定)。2-4 で IME のプロセスから呼べるか (0b)。2-5 の 0a (変換中にブラウザーが `keydown` を時刻つきで出すか)。
- **リョウさんの判断待ち**: 2-5 の §5.6 の 3 つ (記録を自動で消すか・記録の上限・エージェントの読み取りを OS の層で止めるか = 生ファイルを読むスクリプトを誰が動かすか) を条件 0 の前に。2-4 の実験 0b は見送り中 (行うときは承認が要る)。

## 追加項目 3-2 の続き (本家の小さい 2 型の取り込み。2026-10-15)

- **結果**: 本家 `c6b281e` (#69、`inviteshimashita`・`invitewookutta`) を、`githubdevote` を守る条件つきで取り込んだ。本家 `8dfec9d` (`site PR`) は、独立した査読で `kono site URL` → `このして URL` などの悪化が見つかったので見送った (リョウさんの判断)。詳細と数字は [COMPARISON_UPSTREAM.md](COMPARISON_UPSTREAM.md) の小節「小さい 2 型の取り込み実験」。
- **確認**: テスト 778/778、品質テスト 1269/1271、正解表 278/310 (`main` に対して broken 0 / fixed 2)。独立査読の自作入力 79 件で悪化 0。
- **実機で確かめること** (リョウさん): `inviteshimashita`・`invitewookutta`・`githubdevote` (`githubでvote`)・`devote` や `derive` を含む文。
- **実機確認 (2026-10-16 午前、リョウさん。打ったアプリが TextEdit かは未確認)**: 入れてあった本体の更新時刻は 2026-10-16 05:48 (727e486 より後。どのコミットから作ったかは未確認)。報告は `打鍵>結果` の 5 行で、`inviteshimashita>inviteしました`・`invitewookutta>inviteを送った`・`githubdevote>githubでvote`・`devotewo>デヴォテを`・`derive や devote>でリヴェヤでvote`。最後の行の打鍵は `deriveyadevote` と推測した (報告の文面からの推測で、確かめていない)。前の 3 行は狙いどおり、`devotewo` は取り込みのときに既知の限界として固定したとおり。`derive` は直っていなかった。
- **原因**: #69 の守り `StartsWithParticleThenEnglishWord` (助詞で始まり、残り 3 文字以上が英単語の語には新しい規則を使わない) が、残りを `IsKnownEnglishWord` で見ていた。これは 4 文字以上ならスペルチェッカーにも聞く。実機の NSSpellChecker は `rive` を英単語と答える (下の Swift の表で確認) ので、`derive` が `de|rive` として守り側に入り、v 行の規則で日本語に戻っていた。テストと `--repro` が使う同梱の一覧 (`english-words.txt`) には `rive` が無いので、そこでは `deriveやでvote` になり、取り込みのときの査読の「`derive` も直った」はテストでだけ成り立っていた (COMPARISON_UPSTREAM の該当の行に [訂正 2026-10-16] を付けた)。
- **今回の変更** (`feat/p3-3-2-derive` の `809548c`。Core のみ): 守りの残りの判定を、覚えた語 → `english.txt`・固有名詞 → 4 文字以上なら `english-words.txt` (`BuiltInWordChecker.Shared`) だけにして、スペルチェッカーには聞かない。`_english.Words` が読むのは `english.txt` (+ 固有名詞・ユーザー辞書。217 行) で、`english-words.txt` ではない。`vote` は `english.txt` に無いので、`english-words.txt` を見ないと `githubdevote` が `githubdevote` に壊れる。そのため `english-words.txt` も見る。`vote`・`vise`・`vice` は `english-words.txt` にあり、`rive` は無いことを確かめた。
  - 同梱の一覧を使うテスト・`--repro`・`--eval` では、式の上で変更前と答えが変わらない (スペルチェッカーが同梱の一覧のときは前と同じ式になる。スペルチェッカーが無いときは、外側の `IsSpellWord` が false なので守りまで来ない)。
  - 実機でも、新しい辞書は足していない (`english-words.txt` は、`CallbackWordChecker` が「分からない」を返したときの代わりに使う同梱の表。今回の変更で、守りを見るときにも最初に使う時点で読み込まれる。約 3.9 万語)。
- **実機に近い比較**: Mac 版と同じ呼び方 (`NSSpellChecker.shared.checkSpelling(of:startingAt:0,language:"en",…)`、2 文字未満は語でない) の答えを Swift のスクリプトで JSON の表に書き出し、それを読む使い捨ての入口 (テストのプロジェクトの `--nsprobe`。コミットしていない) で、変更前 (60534b0) と変更後を同じ `Keyboard` に打って比べた。表に無い語を聞かれたら書き足して打ち直したが、最後の回は表に無い語は 0 件。実機が 4 行を同じ形で出しているので、この表で実機の判定が再現できていると見た (`devotewo`・`deriveyadevote` は漢字・カタカナを除いて一致。変換エンジンは偽物なのでかなまで)。
  - 入力 (重なりを除いて 814 件): 指定の 14 件 + `devote`・`yadevote`・`deriveyadevise`。`english-words.txt` から機械的に取り出した「v を含み、Core の `StartsWithParticle` の助詞 (no ga wo ni de to ha mo wa he) で始まり、残りが 3 文字以上の語」206 語の、単独・`github` + 語・語 + `wo`。`/usr/share/dict/words` も合わせて、守りの答えが変更前後で変わる語 (NSSpellChecker が語と答え、l・x を含まない 5 文字以上) を数えると 26 語あり、その単独・`github` + 語・語 + `wo`・`kyouha` + 語 + `desu`。`derive`・`devote`・`devise`・`novice`・`invite` ほか 9 語に `suru`・`shita`・助詞・`kyouha`・`kono`・`yadevote` などを付けたもの。
  - 結果: **変更前が正しく変更後が違うもの 0 件**。変わったのは 24 件 (Enter・Space・入力中の表示のどれか)。そのうち 23 件は、語が英語のまま残るようになった (`derive` を含む 15 件と、`github` + 語・語 + `wo` の `degenerative`・`denotative`・`novena`・`novitiate` 8 件)。残る 1 件の `derivedyadevote` は、`でりゔぇぢゃでvote` → `deriveぢゃでvote` で、前後とも外れ。守りが新しく効くようになる側の語 (`devised`・`devising`: `vised`・`vising` は同梱の一覧にあり、NSSpellChecker は語でないと答える) も、答えは変わらなかった (前後とも英語のまま)。同梱の一覧での結果は、全入力で前後が同じ。

  | 打鍵 | 実機 (報告) | NS の表・変更前 | NS の表・変更後 | 同梱の一覧 (`--repro` と同じ。前後同じ) |
  | --- | --- | --- | --- | --- |
  | `deriveyadevote` | でリヴェヤでvote (打鍵は推測) | でりゔぇやでvote | **deriveやでvote** | deriveやでvote |
  | `deriveya` | — | でりゔぇや | **deriveや** | deriveや |
  | `derive` | — | derive | derive | derive |
  | `derivewo` | — | でりゔぇを | **deriveを** | deriveを |
  | `githubdevote` | githubでvote | githubでvote | githubでvote | githubでvote |
  | `devotewo` | デヴォテを | でゔぉてを | でゔぉてを | でゔぉてを |
  | `devise` | — | devise | devise | devise |
  | `devisewo` | — | でゔぃせを | でゔぃせを | でゔぃせを |
  | `githubdevise` | — | githubでゔぃせ | githubでゔぃせ | githubでゔぃせ |
  | `inviteshimashita` | inviteしました | inviteしました | inviteしました | inviteしました |
  | `invitewookutta` | inviteを送った | inviteをおくった | inviteをおくった | inviteをおくった |
  | `kyouhanovice` | — | きょうはのゔぃせ | きょうはのゔぃせ | きょうはのゔぃせ |
  | `novice` | — | novice | novice | novice |
  | `vanpaia` | — | ゔぁんぱいあ | ゔぁんぱいあ | ゔぁんぱいあ |
  | `yadevote` | — | やでvote | やでvote | やでvote |
  | `kyouhaderive` | — | きょうはでりゔぇ | きょうはでりゔぇ | きょうはderive |

  - 機械的に取り出した 206 語 (NS の表、Enter): 単独で英語のまま 203 → 203、語 + `wo` で `語を` 200 → 201、`github` + 語が 1 語の英字 200 → 201。変更後も外れる語 + `wo` は `devote`・`devise`・`deaves`・`heave`・`waive` の 5 語。`heave`・`waive` は単独でも `へあゔぇ`・`わいゔぇ` で、同梱の一覧でも同じ (この守りとは別の経路。原因は調べていない)。
- **確認**: テスト 778 → 779 (足したテスト `Upstream69_ParticlePlusWordGuard_DoesNotAskTheSpellChecker`: `rive` を英単語と答えるスペルチェッカーで `deriveyadevote` → `deriveやでvote`・`githubdevote` → `githubでvote`・`derivewo` → `deriveを` ほか。変更前のコードでは `deriveyadevote` が `でりゔぇやでvote` で落ちることを確かめた)。品質テスト 1269/1271 → 1269/1271 (外れの 2 件は前と同じ)。`tools/eval-compare.sh` で main (`/Users/ryo/Meltype`、60534b0) と比べて 278/310 → 278/310、broken 0 / fixed 0。既存のテストの期待値は変えていない。
- **go** (新しい外れ 0 件、実機に近い前後比較で変更前が正しいものを壊していない)。push はしていない。
- **残る限界** (KNOWN_LIMITS の K81): `devote`・`devise` のように「助詞 + 同梱の英単語」とも読める v を含む英単語は、続けて打つと日本語側になる (`devotewo` → `でゔぉてを`、`devisewo` → `でゔぃせを`、`deriveyadevise` → `deriveやでゔぃせ`)。`ya devote` を空白なしで続けた `yadevote` も `やでvote`。日本語の後ろの `derive` (`kyouhaderive` → `きょうはでりゔぇ`、`konoderive` → `このでりゔぇ`) は、実機に近い表では前後とも日本語 (同梱の一覧では `きょうはderive`。別の経路でもスペルチェッカーの `rive` が効いていると推測。調べていない)。
- **未確認**: 実機 (変更後のビルドは入れていない。`build.sh --no-install` も回していない)。実機の 5 行目の打鍵 (`deriveyadevote` は推測)。打ったアプリ (TextEdit かどうか)。
- **実機確認 (2026-10-16 06:11 のビルド = main `1052f6c`、リョウさん)**: `deriveyadevote` → deriveやでvote、`derivewo` → deriveを、`githubdevote` → githubでvote、`inviteshimashita` → inviteしました、`kyouhaderive` → きょうはでりゔぇ (K82 のとおり) の 5 件すべて期待どおりと報告 (「期待通りの結果でした」。行ごとの出力の貼り付けは無い)。打ったアプリは未確認。上の「未確認」のうち、実機と 5 行目の打鍵の件はこれで済み。

## K82 の原因調査 (2026-10-16)

- **目的**: 実機で `kyouhaderive` → `きょうはでりゔぇ` (`konoderive` → `このでりゔぇ`) になる K82 の経路を突き止める。調査のみで、コードは変えていない (調べるための入口とトレースはコミットしていない)。対象は main `bab5b26`。
- **原因** (`src/Meltype.Core/Composition/CompositionDetector.cs:668-673`、`IsEnglishSpanCore` の中): 「日本語のすぐ後ろで、助詞 + 英単語 (`dochira|mo|user`) は 1 語の英語にしない」守り。条件は `before < 0` (区間の直前が日本語。`BeforeScore`・`PrecededByEnglish` (:308-310)) かつ 区間が助詞 (no ga wo ni de to ha mo wa he) で始まり残りが 3 文字以上 かつ 区間全体が `english.txt` に無い かつ `IsKnownEnglishWord(残り)`。`IsKnownEnglishWord` は 4 文字以上ならスペルチェッカーにも聞く (:594-600)。実機の NSSpellChecker は `rive` を語と答えるので、`derive` の区間は「英語でない」と返る。そのあと `rive` だけの区間は 4 文字で v を含み `りゔぇ` と読めるので、v 行の規則 (:729-736、スペルチェッカーの語として英語に戻すのは 5 文字以上) で日本語になり、全体が `でりゔぇ`。残りがローマ字で読めない語 (`livery`) なら `で|livery` と英字が残る。
  - この守りは Mac 版の最初のコミット (`c3c247f`) からあり、#69 の取り込み (`809548c` が直したのは :567-574 の `StartsWithParticleThenEnglishWord`) とは別の場所。同じ型の問題 (守りがスペルチェッカーに聞くので、実機だけ守りが効きすぎる) が残っていた。
  - `EndsWithParticleAfterUnknownWord` (:1417) も `rive` を聞くが、結果には効いていない (下の実験 A は :670-671 だけを変えて直る)。
- **単独の `derive`・`derivewo`・`deriveyadevote` が英字のままの理由** (問い 4): 入力の先頭で、前の確定済みの文字も無ければ `before` は 0 (前の文脈が分からない) で、この守りは `before < 0` のときだけ効く。`deriveyadevote` の `derive` も先頭。逆に、日本語を確定した後 (`kyouha`⏎ のあと) に `derive` だけを打つと、前の文脈が日本語 (`before < 0`) なので `でりゔぇ` になる (表での比較。実機は未確認)。`kyouha derive` (空白あり) も `今日はでりゔぇ`。
- **測り方**: Mac 版と同じ呼び方の NSSpellChecker の答えを Swift で JSON の表にし (`p32e` の `ns` をそのまま使い、表に無い語は書き足して打ち直す。最後の回は表に無い語 0 件)、それを読む使い捨ての入口 `--nsprobe` (3-2 の続きと同じもの) を、聞かれた語の答えの差し替え (環境変数で `rive=false` など) と、指定した語を聞かれたときのスタックトレースの書き出しを足して使った。
- **因果の確かめ方と結果**:
  - 再現: NS の表で `kyouhaderive` → `きょうはでりゔぇ`、`konoderive` → `このでりゔぇ`、`kyouhaderivewo` → `きょうはでりゔぇを`。同梱の一覧ではそれぞれ `きょうはderive`・`このderive`・`きょうはderiveを`。単独の `derive`・`derivewo`・`deriveyadevote` は両方で英字。
  - `rive` の答えだけを変える: NS の表で `rive` だけ「語でない」にすると `きょうはderive`・`このderive` に戻る。同梱の一覧で `rive` だけ「語」にすると `きょうはでりゔぇ`・`このでりゔぇ` になる (実機と同じ)。単独の `derive` はどちらでも `derive`。
  - 誰が聞いたか: `kyouhaderive` で `rive` を聞くのは `IsKnownEnglishWord` ← `IsEnglishSpanCore:670` と `EndsWithParticleAfterUnknownWord:1417` の 2 か所だけ (どちらも `FindSpans` → `Segment` から)。単独の `derive` では `rive` は一度も聞かれない。
  - 実験 A (:670-671 の `IsKnownEnglishWord(残り)` を、#69 の守りと同じ `StartsWithParticleThenEnglishWord(lower)` (同梱の表だけ) に差し替え): NS の表で `kyouhaderive` → `きょうはderive`、`konoderive` → `このderive`、ほか上の 7 入力すべて同梱の一覧と同じになる。テスト 779/779、品質テスト 1269/1271、正解表 278/310 で、どれも変更前と同じ (同梱の一覧では式の上で答えが変わらない)。
- **影響範囲** (Enter の結果。`english-words.txt` にあり、助詞で始まり残りが 3 文字以上、5 文字以上で NSSpellChecker も語と答える `english.txt` 外の語 419 語。`/usr/share/dict/words` から足した語も含めた 776 語を、単独・語 + `wo`・`kyouha` + 語・`kono` + 語 で打った):
  | `kyouha` + 語 が `きょうは` + 語 になる数 (419 語中) | NS の表 (実機に近い) | 同梱の一覧 (`--repro`) | NS の表 + 案 A | NS の表 + 案 A + 案 B |
  | --- | --- | --- | --- | --- |
  | 正しい | 7 | 104 | 104 | 285 |
  - `kono` + 語 も同じ数。単独・語 + `wo` は 4 つとも同じ (419 語中 401・399。外れは `demote` → `でもて` などローマ字で読める語で、この守りとは別)。
  - **実機だけで外れ、案 A で直る** (この調査の K82 の本体): 97 語 (そのうち v を含むのは 14 語。v は「残りがかなになるか英字で残るか」を分けるだけで、外れるかどうかには関係しない)。例: `derive` (でりゔぇ)・`movers` (もゔぇれ)・`delivery` (でlivery)・`delegate`・`definitely`・`described`・`designers`・`deposits`・`decoder`・`moments` (もments)・`motors`・`mobiles`・`topics` (とpics)・`toaster`・`heavens` (へavens)・`hearty`・`galactic`・`warehouse` (われほうせ)・`nothingness`。`kyounotopics` → `きょうのとpics` も同じ。
  - **同梱の一覧でも外れる** (守りの仕様の側。K81 と同じ型で、K82 の外): 残りが `english-words.txt` にある語。`deliver` → でliver、`depart` → でpart、`deserve` → でserve、`details` → でtails、`despite` → でspite、`designed` → でsigned、`decrease` → でcrease、`delight` → でlight、`totally` → とtally、`towards` → とwards、`nowhere` → のwhere、`notable` → のtable、`movies` → もvies、`novice` → のゔぃせ (テストで固定) など。
  - 依頼の例のうち `demand`・`desk`・`notice`・`nimble`・`nodes`・`deviate` は残りが語でない (または 3 文字で `english.txt` に無い) ので、実機でも英字のまま。`deliver`・`depart`・`deserve`・`devise`・`devote` は同梱の一覧でも外れる側。
- **直し方の案** (実装していない):
  - **案 A (勧める)**: :670-671 の `IsKnownEnglishWord(lower[leading.Length..])` を `StartsWithParticleThenEnglishWord(lower)` に替える (残りを 覚えた語 → `english.txt`・固有名詞 → 4 文字以上なら `english-words.txt` だけで見る)。得: 上の 97 語が実機でも同梱の一覧と同じになる。テスト・`--repro`・品質テスト・正解表の答えは変わらない (今回の測りで差 0)。NS の表で、変更前が正しく変更後が外れる入力は 0 件 (776 語 × 4 形)。損: 実機で、`english-words.txt` に無いがスペルチェッカーが知っている語を「日本語 + 助詞 + その語」で打つ場合 (`dochira|mo|<珍しい語>`) は 1 語の英語として扱われうる (例は測っていない。`dochiramouserga` → `どちらもuserが` は `user` が表にあるので変わらない)。影響は Core の 1 か所で Windows にも届く (Windows のスペルチェッカーの答えでの確認はしていない)。
  - **案 B (案 A に足す。勧めない)**: 区間全体が `english-words.txt` の語なら守りを効かせない。得: 同梱の一覧でも外れる側 (`kyouhadetails`・`kyouhatotally`・`kyouhamovies`・`kyouhadeliver`) も英字になり、419 語中の正しい数が 104 → 285。損: 「日本語 + 助詞 + 英単語」が実際にそう読みたい入力を壊す: `kokodetest` → `ここdetest` (今は `ここでtest`)、`nihongonotable` → `にほんごnotable` (今は `にほんごのtable`)、`kyouhanovice` → `きょうはnovice` でテスト `Upstream69_SpellWordWithV_IsEnglish_ButParticlePlusWordKeepsSplit` が落ちる。全体を NSSpellChecker で見る形にすると、`mouser` も語と答えるので `dochiramouserga` も壊れる (推測ではなく `mouser` の答えは確認済み、打鍵の結果は未測定)。文字だけでは区別できない型 (K81 と同じ) なので、仕様のままにするのがよいと考える。
  - **案 C (広い。勧めない)**: `IsKnownEnglishWord` 自体を、短い語 (例: 4〜5 文字) だけ同梱の表で見るようにする。得: 同じ型の「スペルチェッカーの断片の語」(`rive`・`pics`) の問題を 31 か所の呼び元でまとめて止める。損: 呼び元すべての実機の答えが変わり、`python + no` の n のような本来の使い道 (同梱の表に無い語をスペルチェッカーで拾う) を弱める。影響を測っていない。
- **未確認**: 実機での案 A の確認 (ビルドしていない)。実機で、日本語を確定した後に `derive` だけを打つ場合・`kyouha derive` (空白あり) の結果。実機の出力の数は表での比較から推した数で、実機で 1 語ずつは打っていない。Windows のスペルチェッカーでの同じ型の外れ。案 A の損の側の具体例 (`english-words.txt` に無く NSSpellChecker が知っている語を `日本語 + 助詞 + 語` で打つ場合) は探していない。

### 案 A の実装と前後比較 (2026-10-16): **no-go** (取り込まない)

- **実装** (ブランチ `inv/k82-plan-a` の `342f722`。`inv/k82` には入れていない): `IsEnglishSpanCore` の守りの条件 `StartsWithParticle(lower) is { } leading && lower.Length - leading.Length >= 3 && … && IsKnownEnglishWord(lower[leading.Length..])` を `StartsWithParticleThenEnglishWord(lower)` に置き換え、`before < 0` と `!_english.Words.ContainsWord(lower)` は残した。
  - 2 つの条件の比較 (コードを読んで): 助詞の取り方・残り 3 文字以上・覚えた語 (`Memory.Get(残り)`) を先に見る・`english.txt`・固有名詞は同じ。違いは 4 文字以上の残りで、前はスペルチェッカー (`IsWord` か `AutoCorrection`)、後は `english-words.txt` (`BuiltInWordChecker.Shared`)。スペルチェッカーが同梱の一覧 (`--repro`・`--eval`・正解表・品質テスト) なら `AutoCorrection` は常に null なので式の上で等価。スペルチェッカー無し (多くの単体テスト) では、後のほうが `english-words.txt` の分だけ守りが効く側に変わる (テストの結果は変わらなかった)。
  - テスト `K82_ParticlePlusWordGuardAfterJapanese_DoesNotAskTheSpellChecker` を追加 (`rive`・`livery`・`pics`・`ments`・`mouser` を語と答えるスペルチェッカーで `kyouhaderive` → `きょうはderive`、`konoderive`・`kyouhadelivery`・`kyounotopics`・`kyouhamoments` が英字、`dochiramouserga` → `どちらもuserが`・`kyouhanovice` → `きょうはのゔぃせ`・`githubdevote` → `githubでvote`・`derive`・`derivewo` は今までどおり)。変更前のコードでは `kyouhaderive` (`きょうはでりゔぇ`) で落ちることを確かめた。
- **テスト・正解表** (変更前 → 後): テスト 779/779 → 780/780 (追加 1、既存の期待値は変えていない)。品質テスト (`--eval`) 1269/1271 → 1269/1271 (出力の全文が同じ)。正解表 (`tools/eval-compare.sh`、base は main `bab5b26` を detach した作業ツリー) 278/310 → 278/310、broken 0 / fixed 0。同梱の一覧での `--nsprobe builtin` の出力も、下の 3 組の入力すべてで変更前と同じ (差 0)。
- **NS の表での前後比較** (実機に近い。NSSpellChecker の答えの表は今回の入力で聞かれた語をすべて書き足し、最後の回は表に無い語 0 件。Enter の結果で比べた):
  - **正しいの判定基準**: (1) 調査の 776 語 × 4 形: 単独 = 語そのまま、語 + `wo` = 語 + を、`kyouha` + 語 = きょうは + 語、`kono` + 語 = この + 語。(2) 損の側を探す入力: `日本語 + 助詞 + X` は、日本語と助詞がかな・X が英字のまま (例: `kokode` + X = ここで + X) を正しいとする (依頼どおり「助詞 + 語と読む」)。日本語は 助詞ごとに 1 つ (no: nihongo、ga・wo: kore、ni・de: koko、to: anata、ha・wa: kyou、mo: watashi、he: gakkou)。
  - 損の側の入力の作り方: (A) `/usr/share/dict/words` の小文字の語 W のうち、助詞で始まり、残り X が 4 文字以上で `english-words.txt` に無く NSSpellChecker が語と答えるもの 545 語 (`detest`・`notable` の型で、X が同梱の表に無いもの。表にある `test`・`table` は案 A でも守りが効くので変わらない) を `日本語 + W` で。(B) 同じ辞書から無作為に選んだ (seed 82) 4 文字以上で `english-words.txt` に無く NSSpellChecker が語と答える X 722 語を `kokode`・`nihongono`・`korega` + X で (2161 入力)。
  - 結果 (正しい数、変更前 → 案 A):
    | 入力 | 数 | 変更前 | 案 A | 変更前が正しく案 A が違う | 変更前が違い案 A が正しい |
    | --- | --- | --- | --- | --- | --- |
    | 776 語 × 4 形 | 3104 | 1464 | 1982 | 0 | 518 |
    | うち `kyouha` + 語 (`english-words.txt` の 419 語) | 419 | 7 | 104 | 0 | 97 |
    | うち `kyouha` + 語 (776 語) | 776 | 7 | 266 | 0 | 259 |
    | (A) 日本語 + 助詞 + X (助詞 + X が辞書の別の語) | 545 | 423 | 214 | **209** | 0 |
    | (B) 日本語 + 助詞 + X (ふつうの語) | 2161 | 1931 | 1943 | 0 | 12 |
    | 依頼・調査の小さい例 (`kyouha derive` など) | 32 | — | — | 0 | 12 (変わったのは K82 の 12 入力だけで、どれも同梱の一覧と同じ答えになる。確定後の `kyouha`⏎`derive`・空白入りの `kyouha derive` も `derive` が英字。`kyouhadetails`・`kyouhatotally`・`kyouhamovies`・`kaishadeliver` は変わらない) |
  - **壊れる 209 件** (すべて (A)。変更前は 助詞 + X、案 A は区間全体を 1 語の英語にする。全体の W は 185 件で NSSpellChecker の語): `de` 180・`he` 9・`ga` 5・`wo` 4・`mo` 3・`no` 3・`wa` 2・`ha`・`ni`・`to` 各 1。例: `kokodecarbonize` ここでcarbonize → ここdecarbonize、`kokodeforestation` ここでforestation → ここdeforestation、`kokodecaffeinate`、`kokodemilitarization`、`korewomanlike` これをmanlike → これwomanlike、`nihongonovitiate`、`gakkouheaver`、`kokodelegatee` ここでlegatee → ここdelegateえ (どちらの読みでも外れ)。一覧は作業用の `loss_broken_planA.tsv` (コミットしていない)。
  - ただし 209 件のうち 30 件は、W が `english-words.txt` にあり X は無い (`kokodelivery` ここでlivery → ここdelivery、`kokodebris` → ここdebris、`kokodecree`・`kokodefinitely`・`kokodepict`・`kokoderived`・`koregalactic`・`kyouhabitable`・`gakkouhearty`・`gakkouheavens`・`watashimourning`・`kokonitwit`・`nihongonothingness`・`anatatoaster`・`kyouwashroom` など)。これは K82 の本体 (`kyouhadelivery` → `きょうはdelivery` を正しいとする) と同じ形で、W の読みのほうが自然。この 30 件を「直る側」と数え直しても、残り **179 件** (W も X も同梱の表に無い、どちらとも読める語。`decarbonize` と `carbonize` のように、文字だけでは分けられない K81 と同じ型) が変更前の答えから変わる。
- **go 判定: no-go**。テスト・正解表の新しい外れは 0 だが、前後比較で変更前が正しい (助詞 + 語と読む) 入力を 209 件 (W が同梱の一般語の 30 件を除いても 179 件) 壊す。`inv/k82` には入れていない。
  - 実際に困る度合い (推測): 壊れる 179 件はどれも `english-words.txt` (SCOWL のよく使う語) に無い珍しい語どうしの組み合わせ (`de` + 化学・専門用語が多い) で、得の側 (`derive`・`delivery`・`topics`・`moments` など 97 語はよく使う語) と比べると打つ機会は少ないと考えるが、測っていない。
- **参考: 案 A' (実験。ブランチ `inv/k82-plan-a2` の `591c149`)**: 守りを「同梱の表で見て残りが語」または「区間全体が `english-words.txt` に無く、残りが `IsKnownEnglishWord` (前の条件)」にする。区間全体がよく使う英単語なら、残りをスペルチェッカーに聞かない。結果: テスト 780/780。`kyouha` + 語 (419 語) 7 → 104 (案 A と同じ。776 語では 104 で、案 A の 266 より少ない)。776 語 × 4 形で壊れる 0。(A) で変わるのは 31 件で、30 件は上の「W が同梱の一般語」の側 (直る側)、1 件は `kokodelegatee` → ここdelegateえ (`delegate` の区間が守られなくなる。どちらの読みでも外れ)。(B) は変わらない。同梱の一覧での出力は変更前と差 0。品質テスト・正解表・`eval-compare` は回していない (この時点)。採るかどうかはリョウさんの判断待ち (この時点)。
- **未確認**: 実機での確認 (案 A・案 A' ともビルドしていない)。数は NSSpellChecker の答えの表での比較で、実機で 1 語ずつは打っていない。(A)・(B) の「正しい」は上の機械的な基準で、どちらの読みが利用者の意図に近いかは確かめていない。Windows のスペルチェッカーでの同じ比較。案 A' の `--eval`・正解表。
- **採用: 案 A' (2026-10-16、リョウさんの判断)**: `a216429` (実験の `591c149` を `inv/k82` に取り込み、コメントとコミットの説明だけ直した)。テスト 780/780、品質テスト 1269/1271 (変わらず)、正解表 278/310 → 278/310 (main `bab5b26` に対して broken 0 / fixed 0)。KNOWN_LIMITS の K82 は「直った (実機未確認)」へ。案 A は上の no-go のまま採らない。
- **実機で確かめること** (リョウさん): `kyouhaderive` → きょうはderive、`kyouhadelivery` → きょうはdelivery、`kyounotopics` → きょうのtopics、`kyouhamoments` → きょうはmoments、今までどおり `githubdevote` → githubでvote、`deriveyadevote` → deriveやでvote。

### 査読での案 A' の悪化と案 A'' (2026-10-16)

- **査読で見つかった悪化**: 案 A' の守り `!english.txt(W) && (StartsWithParticleThenEnglishWord(W) || (!BuiltIn(W) && 前の条件))` は、左の枝が前の条件で守らなかった語も守る。語全体 W も残り X も `english-words.txt` にあり、X を NSSpellChecker が語と答えない 6 語 (`devised`・`devising`・`nomads`・`hashes`・`washes`・`gashes`。X は `vised`・`mads`・`shes`) で 14 入力が変更前より悪くなっていた: `kyouhadevised` きょうはdevised → きょうはでゔぃsed、`kyouhanomads` → きょうはのまds、`kyouhahasheswo` → きょうははしぇあを、`kyouhawasheswo` → きょうはわしぇあを、`kyouhagasheswo`、`kyouhadevisingwo`、`konodevised`・`koregadevised` など (6 語 × kyouha/kono/korega/kyouha…wo のうちの 14)。
- **案 A'' (`9e5b509`)**: 守りを「変更前 (`bab5b26`) の条件 G かつ でない (W が `english-words.txt` の語 かつ `StartsWithParticleThenEnglishWord(W)` が偽)」にした。
  - 式: 変更前 G = `before < 0 ∧ 助詞 + 残り 3 文字以上 ∧ ¬english.txt(W) ∧ IsKnownEnglishWord(X)`。案 A'' = G ∧ ¬(BuiltIn(W) ∧ ¬S(W)) (S = `StartsWithParticleThenEnglishWord`)。G に AND を足しただけなので 案 A'' ⇒ G、つまり守りが効く (区間を 1 語の英語にしない) 入力は変更前の部分集合で、守りが外れる向き (英字のまま) にしか変わらない。BuiltIn(W) が偽なら G と同じ、真なら G ∧ S。案 A' との違いは「案 A' は守るが G は守らない」(S が真で `IsKnownEnglishWord(X)` が偽 = X が `english-words.txt` にあり NSSpellChecker が語と言わない) 場合だけで、そこを守らなくした。
  - テスト `K82_ParticlePlusWordGuardAfterJapanese_DoesNotAskTheSpellChecker` の使うスペルチェッカーを、`rive`・`livery`・`pics`・`ments`・`mouser` を語と答え、`vised`・`mads`・`shes` を語と答えないもの (実機と同じ) にし、`kyouhadevised` → きょうはdevised、`konodevised` → このdevised、`kyouhanomads` → きょうはnomads、`kyouhahasheswo` → きょうはhashesを を足した (既存の期待値は変えていない)。`a216429` のコードでは `kyouhadevised` (きょうはでゔぃsed) で落ち、新しいコードで通る。
- **テスト・正解表**: テスト 780/780。品質テスト (`--eval`) 1269/1271 (出力の全文が変更前・案 A' と同じ)。正解表 (`tools/eval-compare.sh`、base は main `bab5b26` を detach した作業ツリー) 278/310 → 278/310、broken 0 / fixed 0。同梱の一覧での `--nsprobe builtin` の出力は、下のすべての入力で変更前・案 A' と差 0。
- **NS の表での前後比較** (base = `bab5b26`。表は今回の入力で聞かれた語をすべて書き足し、最後の回は表に無い語 0 件。Enter の結果。「正しい」の基準は上の案 A の節と同じ):
  | 入力 | 数 | 変更前 | 案 A' | 案 A'' | 変更前と違う | 案 A' と違う |
  | --- | --- | --- | --- | --- | --- | --- |
  | 調査の 776 語 × 4 形 | 3104 | 1464 | 1658 | 1658 | 196 | 0 |
  | うち `kyouha` + 語 (`english-words.txt` の 419 語) | 419 | 7 | 104 | 104 | 98 | 0 |
  | 損の側 (A) 日本語 + 助詞 + X (助詞 + X が辞書の別の語) | 545 | 423 | 392 | 392 | 36 | 0 |
  | 損の側 (B) 日本語 + 助詞 + X (ふつうの語) | 2161 | 1931 | 1931 | 1931 | 0 | 0 |
  | `english-words.txt` の助詞で始まる全語 (残り 3 文字以上) × `kyouha`・`kono` | 5752 | 3966 | 4156 | 4162 | 198 | 6 |
  | 査読の `korega` + 同じ全語 | 2876 | 1982 | 2077 | 2080 | 99 | 3 |
  | 査読の 6 語の形 (`kyouhadevised` など) | 36 | — | — | — | 0 | 14 |
  | 査読の自然文 / 損の側の例 / 小さい例 | 96 / 288 / 32 | — | — | — | 6 / 1 / 12 | 0 |
  - Enter の結果が変更前と違う入力は全部で 548 行 (表の重なりを除いて 346 通り)。どれも変更前より英字が長く残る向き (区間を分けない。`kokodelegatee` 以外は変更前の英字の部分をそのまま含む)。ほかに空白で確定した結果の区間の分け方だけ違う 1 件 (`今日は|derive`) も同じ向き。kyouha 419 語の 98 件は直る 97 件と `kyouhawarehouses` (きょうはわれほうせ → きょうはwarehouse。どちらも外れ。`kono` も同じ)。
  - 案 A' と違う入力は査読の 14 入力 (表の重なりを含め 23 行) だけで、どれも変更前の答えに戻る (`kyouhadevised` → きょうはdevised など)。案 A' より悪くなる入力は 0。
  - **変更前が正しく案 A'' が違う入力**: (A) の 31 件と査読の損の側の例 1 件。(A) の 30 件は W が `english-words.txt` にある K82 の本体と同じ形 (`kokodelivery` ここでlivery → ここdelivery、`kokodebris`・`kokodefinitely`・`koregalactic`・`gakkouhearty`・`anatatoaster` など。W の読みのほうが自然なので直る側と数える)。残る 2 件は `kokodelegatee` ここでlegatee → ここdelegateえ と `kokodebrisks` ここでbrisks → ここdebrisks: 区間全体 (`delegatee`・`debrisks`) は変更前どおり守るが、短い `delegate`・`debris` の区間 (`english-words.txt` の語で、残り `legate`・`bris` は同梱の表に無い) が守られなくなり、そちらが選ばれる。derive を直すのと同じ働きで、案 A' でも同じ答え。式の上での「守りは変更前の部分集合」は保たれるが、結果は変更前の読みにも語全体の読みにもならない。
- **go 判定**: テスト・正解表の新しい外れ 0。前後比較で変更前が正しく壊れるのは上の 2 件 (`kokodelegatee`・`kokodebrisks`。どちらも珍しい語で、案 A' から引き継いだもの)。依頼の基準 (変更前が正しいものを壊さない) どおりなら no-go。案 A' (今の `inv/k82`) と比べると悪化 0 で査読の 14 件を消すので、ブランチには `9e5b509` として入れた。採るか戻すかはリョウさんの判断待ち。
- **未確認**: 実機 (ビルドしていない)。数は NSSpellChecker の答えの表での比較。Windows のスペルチェッカーでの比較。`kokodelegatee`・`kokodebrisks` の型 (W の頭の短い区間が `english-words.txt` の語) をほかの語でどれだけ起こすかは探していない。
- **独立査読 (2026-10-16、案 A'')**: 数字はすべて再現 (テスト 780/780、`--eval` 全文一致、eval-compare broken 0、表での前後比較の全行一致)。上の型を `english-words.txt` と `/usr/share/dict/words` から機械的に探し (4176 語 × `koko`/`kyou` = 8352 入力、NS の表は IME と同じ `checkSpelling` で取り足し)、結果が変わったのは 38 件: 良くなった 12 (`kokowadeduces` → ここわdeduces など)、前後とも外れ 16、助詞で分ける読みが正しく変更後が外れる 5 語 10 入力 (`debrisks`・`delegatee` と、語全体が辞書にあり変更後のほうが自然とも読める `decreement`・`derelictness`・`derivedness`)。**よく使う語での悪化は 0**。英字が短くなる向きの例 `kokowarehouseful` (ここわれhouseful → ここwarehouseふl、前後とも外れ) も見つかった。
- **採用 (2026-10-16、リョウさんの判断)**: 珍しい語の悪化 (`delegatee`・`debrisks` など) は許容して、案 A'' (`9e5b509`) を main に入れる。
- **実機確認 (2026-10-16 08:59 のビルド = main `a5ffa4a`、リョウさん)**: `kyouhaderive` → 今日はderive、`kyouhadelivery` → 今日はdelivery、`kyounotopics` → 今日のtopics、`kyouhamoments` → 今日はmoments、`kyouhadevised` → 今日はdevised、`githubdevote` → githubでvote、`deriveyadevote` → deriveやでvote。7 件とも期待どおり。最初の報告の `deriveyadevote` は デルヴェヤでvote だったが、`deruveyadevote` (i と u の打ち間違い) の結果と一致し (NS の表で再現)、打ち直して期待どおりと確認した。
