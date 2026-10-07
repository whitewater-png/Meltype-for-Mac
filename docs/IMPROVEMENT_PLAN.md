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
