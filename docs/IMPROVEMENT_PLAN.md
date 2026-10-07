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
