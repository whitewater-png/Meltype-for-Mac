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
- install.sh は変更不要と確認した: `--yes` は非対話 (stdin が端末でなくても動く)、`pkill` は rsync の後・IME だけを止める。切り離した bash は `POSIX_SPAWN_SETSID` の新しいセッションで動くので、IME が止まっても続く。IME が止まるので、結果のダイアログは切り離した側が出す。
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
- アプリのアイコンは無い (汎用のアイコン)。


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
- 比較用のビルド・入力・正解のデータは作業用フォルダー (`~/.hermes/profiles/henrietta/cache/scratch`) にあり、残らない。入力と正解 (約 11 MB) を残すなら、別の置き場所 (リポジトリ外か `docs/` 以外の追跡しないフォルダー) に移す判断が要る。再現の手段は `--repro`・`--segments` と、スキル `meltype-mac-maintenance` の `scripts/compare_repro.py`。

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

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (`cache/scratch`) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

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

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (`cache/scratch`) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

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

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (`cache/scratch`) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

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

- 作業用のコピー・比較表・評価用の文は作業用フォルダー (`cache/scratch`) にあり、24 時間で消える。再現は `--repro`・`--segments`・`--expect` と、この節の入力で行う。

### P30〜P33 の Mac 実機の確認 (2026-10-11、メンテナー、TextEdit、ビルド 21 = 1.1.5)

- 期待どおりに動いたもの: P33 `3×4`・`10÷2` (`電話を3回かける` は変わらない)、P31 `pingをうつ`・`lo-fi`・`gpgが`・候補の `氣`、P30 `しの→紫乃` を登録して `私のことが好き` (紫乃が割り込まない)・`めるたいぷ→Meltype` を登録して `今日はMeltypeを使う`、P32 Ctrl+H (問題なし)。これで P32 の「Ctrl+H が IMK から Core に届く」という未確認の前提も確かめられた。
- 確認していないもの: P31 の `しゃーなし` が先頭候補か・`huihui`・`cwdde`、P30 のライブ変換の遅延、P33 で選んだ `×` が学習に混ざらないか、P32 の変換中 (Space の後) と何も打っていないときの Ctrl+H。

