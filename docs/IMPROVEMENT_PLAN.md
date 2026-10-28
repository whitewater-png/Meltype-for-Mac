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
- 操作: **Tab** で予測候補に入る (最初の候補が選ばれる)。↓↑ で移動、**Enter で確定**、Esc か文字キーで予測から抜けて入力に戻る。Space は今までどおり「変換」(予測は無視)。[追記 2026-10-28: W16-2 で、予測を選んでいる間の文字キーは「選んだ語で確定してから打つ」に変えた (本家 issue #1187 #1214)]
- **英語と判定した語には予測を出さない**。表示モードが Auto / Hiragana のときだけ。
- 設定 `Prediction` (bool、既定 true)、`PredictionMinLength` (int、既定 2、`Normalize` で 1〜5)。
- Windows の変換ボックス (`CompositionWindow`) で予測を描く部分は **この計画の範囲外** (Core は `View.Predictions` を返すだけにし、描画は作者側に任せる。描画しなくても壊れない)。

### 受け入れ条件

1. Mac で `osewa` と打つと候補ウィンドウに予測が出る。Tab → Enter で「お世話になります」などが確定する。
2. `google` と打っても予測は出ない (英語判定)。
3. Tab で予測に入ったあと文字を打つと、予測から抜けて入力が続く。[追記 2026-10-28: W16-2 で「選んだ語で確定してから、その文字で入力が続く」に変えた]
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
  - 本家の「絵文字の辞書」の調整は、Mac 版に絵文字を後ろに回す機能(`MoveEmojiLast`)が無いので不要。日時の選択(`IsDateTimeChoice`)は、この行を書いた時点 (2026-10-11) では無かったが、P47 (2026-10-16) で入った。Mac の絵文字の辞書 (`emoji.txt`・`emoji-cldr.txt`) には、日時の候補の読み (いま・なう・きょう) と同じ読みの行が無く、日時の選択と絵文字がぶつかる場面は今のところ無い。本家の調整の中身そのものは、P47 のあとに突き合わせ直していない。
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
- **未確認 (この作業では確かめていない)**: 実機の IME での操作 (①〜⑥)。macOS 13.0〜13.2 での起動 (llama.framework の minos が 13.3 で、`LSMinimumSystemVersion` (13.0) より新しい。`tools/check-release.py` の minos の検査が FAIL する。13.3 に上げるかはリョウさんが決める)。llama.framework に残る上流の作者のパス (`/Users/miwa/` 55 件、`tools/check-release.py` のビルド機のパスの検査が FAIL する)。llama.cpp の MIT ライセンス全文を Meltype.app の `Licenses/` に入れること (今は THIRD-PARTY-NOTICES に表記だけ)。 [追記 2026-10-16: 後ろの 3 件はコードでは済んでいる (同じ節の査読 R40 の反映 `51767c3` の説明に「ライセンス全文の同梱、最低 OS 13.3、check-release の第三者パスは INFO」とある)。(1) 最低 OS: `mac/Resources/Info.plist` と `mac/Resources/Dictionary/Info.plist` の `LSMinimumSystemVersion` は 13.3 で、`tools/check-release.py` は minos をこの値と、pkg の `os-version` もこの値と比べる。13.0〜13.2 の Mac にはそもそも入らない形になった。13.3〜15 の Mac での起動は未確認のまま (K62)。(2) 上流の作者のパス: `tools/check-release.py` は `llama.framework` の中の上流の作者のビルド機のパスだけを例外として数え、FAIL でなく参考 (INFO) として出す。自前の Mach-O と、それ以外のパスは今までどおり FAIL。(3) MIT の全文: `mac/build.sh` が `mac/Resources/Licenses/llama.cpp-LICENSE.txt` を Meltype.app の `Contents/Resources/Licenses/` に写し、無ければビルドを止める。THIRD-PARTY-NOTICES.md にも書いてある。どれもコードを読んで確かめたもので、今回ビルドして `check-release.py` を回してはいない]

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
- **未確認**: 実機 (Mac の IME としての動き) は未確認。今回は直していない別件: `cd`・`ls` のような 2 文字の英字は英語と判定されず、Space で変換が始まって空白が消える (`cd ~/Downloads` → `cd~/Downloads`、`ls -la` → `ls-la`)。`mac-terminal.tsv` の既知の外れ (`cd ~/Documents/Meltype`) と同じ原因で、保護区間とは別。 [追記 2026-10-16: この `cd`・`ls` の件は K79 (「追加項目 1-5 の代わり」、`78d90b6`〜) で、コード系アプリの語の頭に限って直った。2026-10-15 に Terminal.app の実機で `cd ~/Downloads`・`ls -la` が英字のまま実行されることを確認済み (同じ節の「実機確認」)。一般アプリ (TextEdit など) では今までどおり。この節の保護区間そのものの実機確認は、この節には記録が無い]


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
- **未確認**: 2-2 の実験 A (実機のホスト別の取得率)。2-3 の「表示と確定の食い違い」(Space で英語の語で終わる確定だけ `Convert` を通る) の実機確認と、FAQ・K51 の Apple の日本語入力との比較の断定 (直していない) [追記 2026-10-15: 直した。FAQ・K51・STRATEGY は Meltype の動きだけを書き、Mac 標準との比較は実機で確かめてから書く]。実機確認 1 はアプリ名が報告に無い (手順は TextEdit の指定)。2-4 で IME のプロセスから呼べるか (0b)。2-5 の 0a (変換中にブラウザーが `keydown` を時刻つきで出すか)。 [追記 2026-10-16: 2-2 の実験 A と 2-4 の 0b は「やらない」に決めた (リョウさんの決定。2-2 は no-go、2-4 の訳は保留で、手間に見合わない。方針を変えるときに必要なら再検討する)。確かめないまま閉じる]
- **リョウさんの判断待ち**: 2-5 の §5.6 の 3 つ (記録を自動で消すか・記録の上限・エージェントの読み取りを OS の層で止めるか = 生ファイルを読むスクリプトを誰が動かすか) を条件 0 の前に。2-4 の実験 0b は見送り中 (行うときは承認が要る)。 [追記 2026-10-16: 2-4 の「選択範囲の英訳/和訳」(保留) と実験 0b は「やらない」に決めた (リョウさんの決定。理由は上の未確認の追記と同じ)。2-5 の 3 つは条件 0 に進むときにまとめて決めるまま]

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
- **確認**: テスト 778 → 779 (足したテスト `Upstream69_ParticlePlusWordGuard_DoesNotAskTheSpellChecker`: `rive` を英単語と答えるスペルチェッカーで `deriveyadevote` → `deriveやでvote`・`githubdevote` → `githubでvote`・`derivewo` → `deriveを` ほか。変更前のコードでは `deriveyadevote` が `でりゔぇやでvote` で落ちることを確かめた)。品質テスト 1269/1271 → 1269/1271 (外れの 2 件は前と同じ)。`tools/eval-compare.sh` で main (作者の作業ツリー、60534b0) と比べて 278/310 → 278/310、broken 0 / fixed 0。既存のテストの期待値は変えていない。
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
- **測り方**: Mac 版と同じ呼び方の NSSpellChecker の答えを Swift で JSON の表にし (3-2 の続きで作った表 (作業用フォルダー、一時) をそのまま使い、表に無い語は書き足して打ち直す。最後の回は表に無い語 0 件)、それを読む使い捨ての入口 `--nsprobe` (3-2 の続きと同じもの) を、聞かれた語の答えの差し替え (環境変数で `rive=false` など) と、指定した語を聞かれたときのスタックトレースの書き出しを足して使った。
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
  - **壊れる 209 件** (すべて (A)。変更前は 助詞 + X、案 A は区間全体を 1 語の英語にする。全体の W は 185 件で NSSpellChecker の語): `de` 180・`he` 9・`ga` 5・`wo` 4・`mo` 3・`no` 3・`wa` 2・`ha`・`ni`・`to` 各 1。例: `kokodecarbonize` ここでcarbonize → ここdecarbonize、`kokodeforestation` ここでforestation → ここdeforestation、`kokodecaffeinate`、`kokodemilitarization`、`korewomanlike` これをmanlike → これwomanlike、`nihongonovitiate`、`gakkouheaver`、`kokodelegatee` ここでlegatee → ここdelegateえ (どちらの読みでも外れ)。一覧は手元の一時ファイル (非公開、コミットしていない)。
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

## Phase 1・2 の見落としの整理 (2026-10-16)

読み取りの調査で見つかった、文書どうしの食い違いと push 前の手元の情報を、文書だけで直した (ソース・README.md・mac/README.md は変えていない。push はしていない)。

- **直したもの**:
  - 計画書の 3-2 の続き・K82 の節に残っていた作者の手元のパス・作業用フォルダーとファイルの名前を一般的な表現に (`60565c1`)。`docs/` 全体で `/Users/`・作業用フォルダーの名前を探し、残りは一般的な例 (`/Users/<名前>/` など) と、`tools/check-release.py` にも書いてある上流の作者のビルド機のパス (1-1 の節) だけ。
  - 管理表 (UPSTREAM_PR_TRACKER): 1-2〜1-8 の行を足し、P41・P42 を main への統合 (実機未確認) に、重なっていた Q2 の 1 つを D1 に、列数の合わない行を更新の記録へ、-pedia の本家への報告を「現時点では行わない (STRATEGY §8)」に。更新の記録に Phase 1 の統合・3-2 の続き・K82 を足した (`c555f01`)。
  - KNOWN_LIMITS: K65 (アイコン) を直った (`ac3ff1c`) に、K16 を 26 件に、件数の欄の K82 を案 A'' に。`git log --grep` で確かめられた hash を足した。件数表は行を数えて検算 (仕様 40 / 直す予定 8 / 直った 9 / 未確認 10 / 合計 67) (`9e8432c`)。
  - COMPARISON_UPSTREAM: 既知の制限を 67 件に、小さい 2 型の「未確認: 実機」に実機確認を追記、更新履歴に K82 の後の測定 (278/310 / 266/310、broken 26 / fixed 38) (`aca3b58`)。
  - 計画書の 1-1 の未確認 (最低 OS 13.3・上流の作者のパス・MIT の全文) はコードで済んでいること、1-4a の `cd`・`ls` は K79 で直ったこと、2-3 の文書の FAQ の比較の文は `e77ca98` で直したことを、元の文を残して追記 (`8ac3955`)。
  - Zenzai の 2 文書: 実験ブランチ `exp/zenzai-1-1` が手元のみで公開していないこと、正解表の今の場所 `eval/kanji/kanji-cases.tsv` を追記 (`932dda0`)。
- **直していないもの** (リョウさんの判断・確認待ち):
  - README の「できること」に Phase 1 の機能 (Tab で変換・保護区間・候補の意味のパネル・アプリごとのモード・コード系アプリのコマンド名・判定を調べるタブ) が載っていない。確定後の F10 / F6 は制限節と FAQ にだけある。README は公開文書なので、書くかどうかと書き方はリョウさんが決める。
  - 実機未確認の一覧: 1-2 / 1-6 (確定後の F10 / F6 と学習の強度)、1-3 (判定を調べるタブ)、1-4a (保護区間)、1-4b / 1-4c (アプリごとのモード)、1-7 (辞書から引く英字の候補の意味。パネルはライトモードだけ確認済み)、1-8 (Tab で変換)。どれも v1.2.1-mac のタグに含まれる。各節の「実機で確かめること」のとおり、リョウさんの確認待ち。
  - COMPARISON_UPSTREAM の §2 (現在値) の判定精度 2 の行は、最初の枠の数字 (231/264、broken 28) のまま。最新は §4 の更新履歴の最後の行。

## スペルチェッカー起因の実機だけの外れ (2026-10-16)

実機 (Mac) は英単語の判定に NSSpellChecker を使い、テストと `--repro` は同梱の一覧 (`BuiltInWordChecker` = `english-words.txt`) を使う。NSSpellChecker は語の途中から始まる断片 (`verde`・`umol`・`rive`) も英単語と答えるので、テストでは当たり実機でだけ外れる入力がある。K82 (`9e5b509`) と #69 の守り (`809548c`) はその 2 か所を直したもの。ここでは Core がスペルチェッカーに聞く箇所を洗い出して外れを分類し、そのうち 2 か所を K82 と同じ「変更前の条件に AND を 1 つ足す」形で直した。push はしていない。実機での確認はまだ。

- **調査の要約** (main `0dee8de` で測った。行番号はそのときのもの):
  - スペルチェッカーに語の一部 (頭・残り・途中から始まる区間) を聞く箇所は、`CompositionDetector.cs` では (`FindSpans`:379 の長い語、`IsEnglishSpanCore`:676 の K82 の守り・:691 の英語区間の判定の本体、`CapitalizedWordEnd`、`IsHyphenatedEnglishWord`、`DotSuffixEnd` の頭・残りの判定など)、`CompositionText.cs` に 5 か所 (`SplitEnglishFinalN`:251、`RereadSmallTsuAfterEnglishL`:288、`EndsWithEnglishWord`:323、`EndsWithEnglishWordFromUnit`:369、`KeepLaughterAt`:1228 と `ContainsEnglishWord`:1276 は既に同梱の一覧だけで見る形)。Mac の `CallbackWordChecker` は `AutoCorrection` を返さないので、`FixEnglishTypo` (英語の打ち間違い直し) は実機では効かない (Windows だけ)。
  - 測り方: google の頻度表の上位で `english-words.txt` にある 5000 語を 50 の型 (単独・語 + 助詞・`kyouha` + 語 + `desu` など。計 249,992 入力) で打ち、Enter の結果を「かな + 語 + かな」の正解と比べた。NSSpellChecker の答えは Swift で表にし (Mac 版と同じ `checkSpelling(of:startingAt:language:"en"...)`)、表に無い語は書き足して打ち直した (最後の回は表に無い語 0 件)。ずれた入力ごとに、聞いた箇所 (スタックの最初の判定コード) ごと・語ごとに答えを同梱の一覧に差し替えて、どこで直るかを記録した。
  - NS の表で外れ、同梱の一覧で当たる入力は 349 件 (逆向き、テストでだけ外れるのは 96 件。両方外れ 186 件):
    | 分類 | 件数 | 単独で直る箇所 | 原因の語 (NSSpellChecker だけが語と言う) | 例 (NS の表での結果) |
    | --- | --- | --- | --- | --- |
    | H1 日本語の音の途中の母音 1 つの単位 (`kyo\|u`・`sense\|i`) + 助詞 + 語頭 | 231 | `IsEnglishSpanCore`:691 (222)。そのうち 153 は `EndsWithEnglishWordFromUnit`:369 だけでも直る | `umol` 153、`inis`、`ides`、`unicity` など | `kyoumolike` → きょumolいけ、`kyounicity` → きょunicity、`senseidestar` → せんせidestar |
    | H2 助詞 + 語頭 | 43 | :691 | `haver`・`hasta`・`noter`・`noms` など | `kyouhaverydesu` → きょうhaveryです、`konotermwo` → こnotermを |
    | T1 語尾 + 後ろの日本語 | 49 | `FindSpans`:379 (49 全部) | `verde` 28、`thermo` 5、`ndidatede`、`steno`、`mailto` など | `overdesu` → overdesu、`emailto` → えmailto、`candidatedesu` → かndidatedeす |
    | T2 語尾 + 後ろの日本語の頭 | 9 | :691 | `usda`・`markka`・`barde` | `usdayo` → usdaよ |
    | W1 入力全体が NSSpellChecker だけの英単語 | 4 | — | `promo`・`mailto` など | NSSpellChecker のほうが正しいと思われる (正解表の決め打ちの側) |
    | W2 日本語を含む入力全体を NSSpellChecker が語と言う | 13 | :691 (10) | `residentsga`・`konosugarwo` | 理由は不明 |
- **今回直したもの** (`fix/sc-t1-h1`。それぞれ go):
  - **T1 (`d36ef48`)**: `FindSpans` の「見つけた区間 (`teal`) の途中から始まって後ろへ続く長い語 (`alcoholic`) があれば、見つけた区間を捨てる」(sometealcoholic 用)。長い語は `IsLongEnglishWord` でスペルチェッカーに聞くので、`o|verde|su` の `verde` で `over` を捨てていた。
    - 式: 変更前の捨てる条件 D = `IsLongEnglishWord(W)`。変更後 = D ∧ ¬(B(X) ∧ ¬B(W) ∧ P(W の X より後ろの部分))。X = 見つけた区間、W = 長い語、B = 同梱の一覧の語 (`IsBundledEnglishWord`: `IsListedEnglishWord` か、4 文字以上なら `english-words.txt`)、P = ちょうど助詞 1 つ (`DictionaryDetector.StartsWithParticle` の表: no ga wo ni de to ha mo wa he)。D に AND を足しただけなので変更後 ⇒ D、つまり区間を捨てるのは変更前に捨てていた場合の一部だけ。同梱の一覧 (テスト) では、`IsLongEnglishWord(W)` が真なら W は同梱の一覧の語なので ¬B(W) が偽になり、答えは変わらない。
    - P (助詞 1 つ) を足したのは、無い形では W が打ちたい語のことが多い入力 (`hiringtone` = ひ + ringtone、`dealpine` = で + alpine。W が `english-words.txt` に無いよく使う語) を壊したため。下の「採らなかった形」。
  - **H1 の一部 (`a093319`)**: `EndsWithEnglishWordFromUnit` (l・x・t を小書き・つ にしない守り) は、最後の単位からさかのぼった英字の語を `IsKnownEnglishWord` (4 文字以上はスペルチェッカー) で見る。`kyo|u|mo|l` の `umol` で、きょうも + like の l を英字に残していた。
    - 式: 変更前の守り G = `letters.Length >= 4 ∧ IsKnownEnglishWord(letters)`。変更後 = G ∧ (¬V ∨ B(letters))。V = 語が「英字の並びの途中 (前の単位も英字) にある、母音 1 文字の単位」から始まる。G に AND を足しただけなので、l・x・t を英字に残すのは変更前に残していた場合の一部だけ。同梱の一覧では、`IsKnownEnglishWord` が真なら B も真なので答えは変わらない。
    - V (母音 1 つの単位に限る) を足したのは、無い形 (途中の単位から始まる語はすべて同梱の一覧だけ) では `kyouha|vi|ll` の `vill` を見なくなり、`villa`・`viola` が ゔぃっぁ になったため (よく使う語の外れ)。
- **テスト・正解表**: テスト 780 → 782 (`SpellCheckerFragmentAfterListedWord_DoesNotDropTheListedWord`・`SpellCheckerWordFromMidSound_DoesNotKeepTheLetterL`。どちらも NSSpellChecker のように断片を語と答える `IWordChecker` を使う。変更前のコードではそれぞれ `overdesu`・`kyoumolike` で落ちることを確かめた。既存テストの期待値は変えていない)。条件を 1 つずつ外すと落ちる行: T1 の P → `hiringtone`、¬B(W) → `untilde`、H1 の V → `kyouhavilla`、「前の単位も英字」→ `kyou,umolike`。外しても落ちない条件が 2 つある: T1 の B(X) (今回のどの入力でも結果を変えなかった。メインの 249,992 入力で差 0。変更を狭くするために残した) と H1 の B(letters) (母音 1 つの単位から始まる同梱の語は、ほかの単位から始まる語でも守られる)。`--eval` 1269/1271 (出力の全文が変更前と同じ)。正解表 (`tools/eval-compare.sh`、base は `1a11bf3` を detach した作業ツリー) 278/310 → 278/310、broken 0 / fixed 0 (T1 だけ・両方の 2 回)。
- **NS の表での前後比較** (base = `1a11bf3`。Enter の結果。正解は入力の作り方から決めた「かな + 語 + かな」。表は今回聞かれた語をすべて書き足し、最後の回は表に無い語 0 件):
  | 入力 | 数 | 変更前 | T1 だけ | H1 だけ | 両方 | 変更前と違う (両方) | 変更前が正しく外れる (両方) |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | 調査の 50 型 (5000 語) | 249,992 | 215,089 | 215,132 | 215,242 | 215,285 | 200 | 0 |
  | T1 の得の側: `english-words.txt` の全語 (2 文字以上) × 助詞・`desu` など 13 種 + `kyouha` + 語 + `desu` | 547,328 | 481,945 | 482,065 | — | 482,065 | 121 | 0 |
  | T1 の損の側: 1 音 (68 種) + `/usr/share/dict/words` の語 (5 文字以上・同梱の一覧に無い・NSSpellChecker が語と言う 46,606 語)。1 音 + 語の頭 が同梱の一覧の語になるものだけ | 213,004 | 159,628 | 159,616 | — | 159,616 | 39 | 12 |
  | H1 の得の側: `english-words.txt` の l・x・ts を含む語 × `kyoumo`・`doumo`・`kinoumo`・`kyouha`・`kono`・`korewo` (+ `kyoumo` + 語 + `desu`) | 103,348 | 101,648 | — | 102,084 | 102,087 | 536 | 0 |
  | H1 の損の側: `/usr/share/dict/words` の l・x・ts を含む語 (4 文字以上・同梱の一覧に無い・NSSpellChecker が語と言う) × 日本語 6 種 (+ `desu`) | 288,024 | 284,322 | — | 284,408 | 284,408 | 246 | 4 |
  | 日本語のローマ字 (`readings.txt` の 2 万語 × 5 形 + 2 語の連結 6 万) | 160,598 | 正解なし | 差 0 | 差 3 | 差 3 | 3 | — |
  - 調査の 349 件のうち直ったのは 196 件 (T1 43 / 49、H1 153 / 231)。メインの 50 型で変更前と違う 200 件は、直る 196 件と、前後とも外れの 4 件 (`kyoumolee` きょumolええ → きょうもぇえ など `lee` 3 件、`kyouhadeliverdesu` きょうはでぃverdeす → きょうはでliverです)。
  - **変更前が正しく変更後が外れる入力** (よく使う語 = `english-words.txt` か google の頻度表 1 万語にある語):
    - T1 の損の側の 12 件。よく使う語は `emailto` だけで、これは T1 で直したい入力そのもの (損の側の正解を「1 音 + 語」と決め打ちしたので外れと数えた。メインの正解表では `emailと` が正しい)。`wasteno` (wasteの) も同じ。残る 10 件は珍しい語: `apronto` あpronto → apronと、`baconga` ばconga → baconが、`bogusto` ぼgusto → bogusと、`pecanto` ぺcanto → pecanと、`basteno`・`hasteno`・`pasteno`・`tasteno` (steno) → baste の など、`karathe` かrathe → karatへ、`nowhereto` のwhereto → nowhereと。多くは変更後の読み (bacon が・nowhere と) も自然。
    - H1 の損の側の 4 件。どれも珍しい語 `lemma`: `kyoumolemma`・`doumolemma` (+ `desu`) きょうもlemma → きょうもぇっま。
    - よく使う語の悪化は 0 件。
  - 日本語のローマ字の 3 件は H1 による日本語側の改善: `ro-shonnanatsu` ろーしょんanatす → ろーしょんあなつ、`shuuintsuduru` しゅuintすづる → しゅういんつづる、`supeingotsuyuiri` すぺingotすゆいり → すぺいんごつゆいり。
- **採らなかった形** (同じ入力での数。実験のコミットは手元の実験ブランチに残した。push していない):
  | 形 | メインの直る数 | 損の側で変更前が正しく外れる (うちよく使う語) | 得の側の外れ |
  | --- | --- | --- | --- |
  | T1 で P なし (B(X) ∧ ¬B(W) だけ) | 49 | 26,508 (262: `chichristian` ちchristian → chichりsちあん、`haringtone`、`midwest`、`playlist`) | 1 (`sparmade` sparまで → sparmade) |
  | T1 で P の代わりに「後ろの部分がローマ字で読める」 | 49 | 7,801 (118) | — |
  | T1 で P の代わりに「後ろの部分が助詞で始まり読める」 | 44 | 1,013 (31: `haringtone`・`deadware`) | — |
  | **T1 の採った形 (ちょうど助詞 1 つ)** | 43 | 12 (`emailto` だけ。上) | 0 |
  | H1 で V なし (途中の単位から始まる語はすべて同梱の一覧だけ) | 153 | 6 | メイン 32 件・得の側 13 件 (`kyouhavilla` → きょうはゔぃっぁ、`konoviola`、`kononets`) |
  | **H1 の採った形 (母音 1 つの単位だけ)** | 153 | 4 (`lemma`) | 0 |
- **直していないもの**:
  - T1 の残り 6 件: 長い語の後ろの部分が助詞 1 つでないもの (`visitkara` の `sitka`、`accidentdayo`・`accidentdake` の `ccidentda`、`outletdake`、`arisingshita`、`symptomsdeshita`)。P を広げると上の表のとおり よく使う語 で外れが出る。
  - :691 の本体 (H1 の残り 78 件、H2 43 件、T2 9 件): `IsEnglishSpanCore` の英語区間の判定そのものがスペルチェッカーに聞くので、広く変えると損が大きい (助詞で始まり同梱に無い NSSpellChecker だけの語が `/usr/share/dict/words` に 3,908 語)。狭い案 (日本語のすぐ後ろの、同梱に無い区間は、区間の途中から始まって区間の後ろまで続く同梱の語があれば英語にしない) は新しい枝なので変更前の部分集合にならず、別の項目として前後比較が要る。例: `kyounicity` → きょunicity、`kyouhaverydesu` → きょうhaveryです、`konotermwo` → こnotermを、`usdayo` → usdaよ。
  - W2 (13 件): NSSpellChecker が日本語込みの連結 (`residentsga`・`konosugarwo`) を語と答える理由が分からない。Core では見分けにくい。
  - W1 (4 件): NSSpellChecker の答えのほうが正しいと思われるので直さない。
  - 測っていない箇所: `DotSuffixEnd`・`CapitalizedWordEnd`・`SplitEnglishFinalN`・`IsHyphenatedEnglishWord` (`.`・大文字・`nn`・`-` を含む型が今回の入力に無い)。
  - 残る外れは KNOWN_LIMITS の K83・K84。
- **実機で確かめる入力** (期待): `overdesu` → overです、`neverde` → neverで、`othermo` → otherも、`emailto` → emailと、`candidatedesu` → candidateです、`wasteno` → wasteの、`kyoumolike` → 今日もlike、`doumolist` → どうもlist、`kinoumolast` → 昨日もlast。変わらないこと: `sometealcoholic` → 染めてalcoholic (かな漢字変換は azooKey 次第)、`hiringtone` → ひringtone、`kyouhavilla` → 今日はvilla、`kyouhaderive` → 今日はderive (K82)。
- **未確認**: 実機 (ビルド・インストールしていない)。数はすべて NSSpellChecker の答えの表 + Core のテスト用キーボードでの比較で、かな漢字変換はしていない。NSSpellChecker の答えはこの Mac (言語の自動判定 on) のもので、ほかの Mac で同じかは未確認。Windows のスペルチェッカーでの比較。損の側の入力は機械的に作った形 (1 音 + 語、日本語 6 種 + 語) で、日本人がよく打つ形の割合は分からない。頻度は google の頻度表で、日本人がよく打つ英単語の順ではない。
- **独立査読 (2026-10-16)**: 数字はすべて再現 (テスト 782/782、変更前で新しいテストが落ちる、`--eval` 全文同じ、eval-compare broken 0)。自作の入力 (日本人が打ちそうな文 247 件、よく使う語 2,546 語 × 前 4 種 × 助詞・です 8 種 = 81,472 件ほか) で、よく使う語の悪化は 0、直ったのは 328 件。損の側で悪化 10 件: `emailto` 系 4 件は直したい入力そのもの (えmailto → emailと)。残る 6 件は `verde`・`mailto` (google の 1 万語の 8,000 位台。この定義では「よく使う語」に入るので、上の「よく使う語の悪化 0」は english-words.txt の上位 3,000 語での話): `kyoumoverde` きょうもverde → きょうmoverで、`sonouemailto` そのうえmailto → そのうemailと など。変更後の出力は同梱の一覧で動くテストのときの出力と同じ (前の日本語の途中から始まる同梱の語 mover・ever・email を残すため)。日本語の利用者が打つことはまれと見て、許容する。
- **査読の軽微な指摘で直したもの**: 文書の hash を作り直し前のもの (`a83afad`・`29fa4f3`) から `d36ef48`・`a093319` に。テストの説明 (`zoverde` は条件 B(X) だけを守るテストになっていない、`untilde`・`kyouhaultrade`・`kyou,umolike` は外れの答えを固定しているだけ)。`IsBundledEnglishWord` の説明に学習した語も見ることを足した (動きは変えていない)。
- **実機確認 (2026-10-16 13:25 のビルド = main `5fdb413`、リョウさん)**: `overdesu` → overです、`emailto` → emailと、`candidatedesu` → candidateです、`kyoumolike` → 今日もlike、`kyouhavilla` → 今日はvilla、`kyouhaderive` → 今日はderive は期待どおり。`neverde` の行は neverです と報告 (`neverdesu` を打った結果と推測。どちらも英字は残っていて、表での比較の答えと合う)。`kyounicity` → きょunicity は直っていない: これは H1 の残り (`IsEnglishSpanCore`:691 の本体、K83) で今回の対象外。確かめる入力の表に誤って入れていた (表での比較でも変更前後とも きょunicity)。

## P47 日時の候補 (いま・なう → 時刻、きょう → 日付。本家 #208。2026-10-16)

- **経緯**: 古いブランチ `fix/input-fixes-1.1.3` のコミット `e8fbc64` (「入力まわりの不具合修正と「いま」「きょう」の日時候補を追加」) に、本家 #208 の日時の候補 (`DateTimeCandidates`、49 行 + `CompositionController` 約 10 行) が入っていたが、main に取り込まれていなかった (取りこぼし)。同じコミットのほかの直し (tabde・そうについて・サロゲートペア) は今回は扱わない。本家の #322 (#294 の打った日付 `10がつ9にち` → `10/9`) は調査のとき「日時候補の土台が先に要る」として見送っていた (UPSTREAM_PR_SURVEY §3.4)。リョウさんが取り込みを承認。
- **実装** (`1c84c84`): `e8fbc64` の形ではなく、本家の今の実装 (c211d24・aef35bc・1745a92) に合わせた (今後の本家の変更 (#322 など) を移しやすくするため)。
  - `CompositionOptions.Now` (今の時刻。テストで固定する)、`CompositionController.TimeCandidates`・`IsDateTimeChoice` (名前も本家と同じ)。本家の `TypedDateCandidates` (#294) は含めない。
  - 文節の読みがまるごと いま・なう・きょう のときだけ、`JapaneseCandidates` の最後 (ひらがな・カタカナ・半角カタカナの後ろ) に足す。最初の候補と、Space 2 回目 (ひらがな) は変わらない。変換エンジンの候補を広げたとき (`Expand`、azooKey の 今・居間 …) も、その後ろに残る。打ったままの英字 (`ima`・`ｉｍａ`) はさらにその後ろ (本家と同じ並び)。
  - いま・なう → `17:22` / `17時22分` / `午後5時22分` / `2026年10月9日(金) 17時22分` / `10月9日(金) 17時22分` / `2026年10月9日(金) 17:22` / `2026/10/09 17:22`。
  - きょう → `2026年10月9日` / `2026年10月9日(金)` / `10月9日` / `10月9日(金)` / `2026/10/09` / `2026-10-09` / `金曜日`。
  - `/` と `:` は数字から組み立てる (地域の設定に左右されない。本家 1745a92 の直し)。
  - 選んで確定しても覚えない: 変換の記録 (`ConversionHistory`)・変換エンジンへの学習 (`ILearningConverter`。azooKey) は本家と同じ。加えて Mac 版にある登録の提案 (`DictionarySuggestions`) にも数えない (数えると きょう → 金曜日 を 3 回選んだときに登録を勧めていた。`2026年10月9日`・`17時22分` も提案の対象の形)。日時でない候補 (きょう → 京) は今までどおり覚える。
  - `e8fbc64` との違い: 候補が 4 つから 7 つに。ちょうどの時刻も `0分` と書く (`午後11時0分`。`e8fbc64` は `午後11時`)。時刻の時は 2 桁 (`09:05`。`e8fbc64` は `9:05`)。どちらも本家の形をそのまま採った。0 分を省く形にしたければ別に直す。
  - 設定は足さない (本家にも無い。候補の後ろに出るだけで、確定は変わらないため)。
  - Swift の変更は無い: 候補ウインドウは Core の `view.candidates` (JSON) をそのまま出す。注釈 (`view.notes`) は日時の候補には付かない。
- **数字**:
  - テスト 782 → 790 (`DateTimeCandidatesTests` 8 件。`e8fbc64` のテストを本家の書式に合わせて移し、本家の学習しないテストと、登録の提案・広げた候補・日時でない候補は覚えるテストを足した)。変更前 (テスト用の `Now` だけ足した状態) では 8 件中 6 件が落ちる。学習しない 3 か所 (変換の記録・変換エンジン・登録の提案) は、それぞれを外すと `ChosenDateTime_IsNotLearned` が落ちることを確かめた。既存のテストの期待値は変えていない。
  - `--eval` 1269/1271 のまま (変更前と全文同じ)。`tools/eval-compare.sh` (base = main `5fdb413`): 278/310 → 278/310、broken 0 / fixed 0。
  - `--repro` の前後比較 (`ima`・`nau`・`kyou`・`kyouha`・`imakara`・`imasugu`・`imada`・`imano`・`kyouno`・`kyoumo`・`ashita`・`imaha` を Enter と Space で 24 通り): 出力は全文同じ (確定と最初の候補は変わらない)。
  - 候補の一覧の前後比較 (同じ 12 入力、テスト用の変換エンジン): 変わったのは `ima`・`nau` (7 つ増える)・`kyou` (7 つ増える) だけ。もとの候補の順は同じ。
  - `cd mac && ./build.sh --no-install` は通る (`git archive` のコピーで。`codesign --verify --deep --strict` も通る。`tools/check-mac-aot-settings.py` は PASS 6 / FAIL 0 (設定は足していないが回した))。
- **実機で確かめる入力** (未確認):
  - `ima` / `nau` + Space を何回か (または候補ウインドウ) で、今の時刻 7 種が かな・カタカナの後ろに出る。azooKey の 今・居間 の後ろか。
  - `kyou` + Space で 今日 が最初のまま、日付 7 種と曜日が後ろに出る。
  - `17:22` などを選んで Enter → 次に `ima` + Space で最初の候補が 今 のまま (覚えていない)。きょう → 金曜日 を 3 回選んでも登録の提案が出ない。
  - `imakara`・`kyouha` など: azooKey が いま|から のように文節を分けると、いま の文節には時刻が出る (文節の読みがまるごと いま のため。本家も同じ)。テスト用の変換エンジンでは 1 文節なので出ない。
- **未確認**: 実機 (インストールしていない)、azooKey の文節の切れ方と候補の数 (時刻が候補ウインドウの何ページ目に出るか)、Windows・Linux (Core の変更なので届くが確かめていない)。
- **次に取り込めるもの**: 本家 #322 (#294、打った日付・時刻の `/`・`:` の形)。土台 (`TimeCandidates`・`IsDateTimeChoice`) が入ったので、本家の差分に近い形で移せる見込み (未着手)。
- **P47 の実機確認 (2026-10-16 14:42 のビルド = main `4ef0c2e`、リョウさん)**: `ima`・`nau` で今の時刻、`kyou` で今日の日付が候補に出る。`kyouha` には日時が出ない。日時を選んで確定したあとも `kyou` の 1 番目は今までどおり。すべて期待どおり (「期待通りの結果が表示されておりました」)。

## 古いブランチの入力の修正 2 つの取り込み実験 (2026-10-16): A・B とも **no-go** [追記 2026-10-16: B は絞った形を、リョウさんの判断で採用した (この節の末尾)]

古いブランチ `fix/input-fixes-1.1.3` (`e8fbc64`・`8a0e708`) に、main に入っていない修正が 2 つあった。リョウさんの判断 (「実験してから取り込むか決める」) で、main `5fdb413` に移して前後比較した。push はしていない。実機では確かめていない。

- **A**: `tabde` が確定で `たべ` になる (変換ボックスでは `tabで` と見えている)。古いブランチの直し方: `CompositionText.EnglishMask` に「3 文字以上で、ローマ字として読めない子音 (n 以外) で終わる英字の区間は、確定の打ち間違いの直しで書き換えない」を足す (1 条件 2 行)。本家にも同じ問題の直しがあり (`42e5dc0`、本家 #220)、こちらは「ちょうど 3 文字・小文字・スペルチェッカーの語」に絞った形。
- **B**: `sounitsuite` → `そunitsuite`、`hontounitsukareta` → `ほんとunitすかれた` (`そう|u|ni|t` の `unit` を英単語と見て、t と s を つ にしない守りが効く)。古いブランチの直し方: `CompositionDetector` で辞書の英単語に続きを足すと日本語の語になるなら英語にしない (約 40 行) + `CompositionText.RejoinSplitTs` (分けた t と s を、続きで日本語の語になるとき つ に戻す。約 40 行)。本家の直し方 (`7a20aff`) は `EndsWithEnglishWordFromUnit` で「伸ばす音の う (前の かな が o・u の段) から始まる語は見ない」(7 行)。
- **測り方**: 同梱の一覧 (BuiltIn = テストと `--repro`) と、NSSpellChecker の答えの表 (実機と同じ `checkSpelling(language: "en")` で取った答え。今回の入力で聞かれた語をすべて書き足し、最後の回は表に無い語 0 件) の両方で、Core のテスト用キーボードで Enter の結果を比べた。下の数は NS の表 (BuiltIn も同じ向き)。正解は入力の作り方から決めた (かな + 語 + かな)。「よく使う語」= `english-words.txt` の上位 3,000 語か google の頻度表 1 万語。入力:
  - 共通: 「スペルチェッカー起因の実機だけの外れ」の 50 型 (249,992 件。正解あり) と日本語のローマ字 (`readings.txt` の 2 万語 × 5 形 + 2 語の連結、160,598 件。正解なし、差だけ見る)。
  - A の得と損: 子音 (n・y 以外) で終わる英字 (`english-words.txt` の 9 文字以下・頻度表・`/usr/share/dict/words` の 3 文字の語。25,560 語) × 前 (なし・`kyouha`) × 助詞・です 5 種 + 単独 = 281,160 件。損の側として、`readings.txt` の語から子音の後ろの母音を 1 つ落とした打ち間違い (`onegaishimsu`・`afurka` の形。1 語 3 か所まで) × (なし・`desu`・`wo`) = 89,951 件 (正解は元の語)。
  - B の得と損: 日本語の語 (`readings.txt` から 3,000 語と、`ou`・`uu` で終わる 1,859 語) + `ni` + つ で始まる語 (ついて・つかれた・つく・つける などと `readings.txt` から 40 語。`tsu` と `tu` の両方) = 176,999 件。損の側として、t で終わる英単語 (2,738 語。`unit`・`units`・`united`・`until`・`unite`・`unity`・`commit`・`edit` などを含む) × 前 8 種 (なし・`kyouha`・`kono`・`sou`・`hontou`・`kyou`・`mou`・`korewo`) × する・した・された・させる・すぎ・助詞など 12 種、`unit` の仲間 × 前 8 種 × 助詞・です 11 種 = 263,100 件。P45-11 (t と s を つ にまとめる規則) の例は、日本語のローマ字の 16 万件に含まれる (`teishutsushita` など)。
- **A の結果** (変更前が正しく変更後が外れる = 悪化):
  | 形 | 50 型の正解 (249,992) | 子音で終わる英字 + 助詞 (281,160) | 打ち間違いの直し (89,951) | 日本語のローマ字の差 |
  | --- | --- | --- | --- | --- |
  | 変更前 | 215,285 | 266,213 | 25,900 | — |
  | 古いブランチの形 | 215,765 (悪化 0) | 269,202 (悪化 0) | 20,561 (**悪化 5,339**) | 0 |
  | 本家の形 | 215,572 (悪化 0) | 267,782 (悪化 0) | 22,295 (**悪化 3,605**) | 0 |
  - BuiltIn では、古いブランチの形の悪化 2,377、本家の形 1,727 (打ち間違いの直し)。
  - 悪化はどれも、打ち間違いの直しで正しい日本語にしていた入力が、英字の区間 (NSSpellChecker や同梱の一覧の短い語) を残すようになるもの: `afurka` あふりか → あfurか、`abisru` あびせる → あbisる、`amerka` あめりか → あmerか、`aidru` あいどる → aidる、`aidgara` あいだがら → aidがら、`appku` あっぱく → appく、`onegaishimsu` おねがいします → おねがいshimす (古いブランチの形だけ。`shim` は 4 文字)。残る英字の多い語: `ish`・`fur`・`yak`・`tor`・`tar`・`nam`・`bur`。よく使う語 (上位 3,000 語) の区間が残るものも 20 件 (本家の形。`aid`・`aim`・`ass`)。
  - 得 (変更前が外れ変更後が正しい) は `tabde` → tabで と同じ型: `aidwo` あいだを → aidを、`atomdeshita` あとむでした → atomでした、`bangwo` ばんがを → bangを。ただし `aidga` (あいだが / aidが) のように、得の入力そのものが「英単語 + 助詞」と「打ち間違いの日本語」のどちらとも読める。区間の位置 (入力の先頭・助詞の後ろ・かなの後ろ) で分けても、損は先頭 2,036・助詞の後ろ 204・かなの後ろ 1,365 (本家の形) で、得と同じ位置に出る。
  - テスト・正解表 (古いブランチの形): テスト 783/783 (新しいテストは変更前で `tabde` が落ちる)、`--eval` 1269/1271 で出力の全文が変更前と同じ、`tools/eval-compare.sh` (base は main `5fdb413` を detach した作業ツリー) 278/310 → 278/310、broken 0 / fixed 0。
  - **go 判定: no-go** (古いブランチの形・本家の形とも)。テスト・正解表では外れは増えないが、打ち間違いの直しを数千件壊す。文字だけでは `tab` + `de` と `ta(b)e` の打ち間違いを区別できない。実験のコミットは `exp/inp-tabde-nogo` (`c6aa2a7`。古いブランチの形。手元のみで公開していない)。KNOWN_LIMITS の K85。
- **B の結果**:
  | 形 | 50 型の正解 (249,992) | 〜に + つ… の日本語 (176,999) | t で終わる英単語 + する など (263,100) | 日本語のローマ字の差 |
  | --- | --- | --- | --- | --- |
  | 変更前 | 215,285 | 80,699 | 250,879 | — |
  | 古いブランチの形 | 215,268 (**悪化 17**) | 176,683 (悪化 0) | 244,528 (**悪化 6,351**、うちよく使う語 2,865) | 249 (235 件は直る: `ashibabuntsuu` あしばbuntすう → あしばぶんつう。14 件は前後とも外れ) |
  | 本家の形 | 215,267 (悪化 18) | 171,721 (悪化 0) | 250,483 (悪化 396、うちよく使う語 66) | 2 (どちらも直る) |
  | 本家の形を t + s をつなぐ判定だけに絞った形 | 215,267 (**悪化 18**) | 171,721 (悪化 0) | 250,645 (**悪化 234**、うちよく使う語 36) | 2 (どちらも直る) |
  - BuiltIn も同じ向き (絞った形: 悪化 18・0・288 (よく使う語 54)。古いブランチの形: 17・0・4,155)。
  - 古いブランチの形は、テスト 781/783 (既存の `sukilltute` すkillつて → すきっlつて と、新しいテストの守りの `kyouhacommitsuru` → きょうはこっみtする が落ちる)、`--eval` 1268/1271 (`kyoucommitsuru` → きょうこっみtする)。前後比較の悪化: `cementsugi` → せめんつぎ、`konomanagementsurunara` → このまなげめんつるなら、`kyouhafinddesu` → きょうはふぃんっです、`kyouhaanddesu` → きょうはあんっです。続きの読みが日本語の語の頭になるか (`ContinuesAsJapanese`) を広く見るので、英単語 + する・です を壊す。
  - 本家の形は、`until` の l の守りや P45-11 の分け直し (t + s の後ろが u 以外の母音なら分ける) まで止めるので、`konountilkara` このuntilから → このうんちゕら、`konounitsareta` このunitされた → このうにつぁれた が出た。そこで「伸ばす う から始まる語を見ない」を t + s をつなぐかの判定 (`tsuFollows`) だけに絞った形を作った (1 回だけ。得は同じ数のまま、悪化 396 → 234)。
  - 絞った形で残る悪化 (よく使う語、NS の表): 50 型の `units` 18 件 (`konounits` このunits → このうにて、`kyoumounits` → きょうもうにて、`watashinounits`。の・も・と の後ろの u を伸ばす音と見る) と、`unit`・`upset` + する・すぎ・された 36 件 (`konounitsuru` このunitする → このうにつる、`hontouupsetsuru` ほんとうupsetする → ほんとうupせつる、`souunitsugi` → そううにつぎ)。珍しい語は `unset`・`uncut`・`unfit`・`undercut`・`unkempt` など 198 件。どれも前が `sou`・`hontou`・`kyou`・`mou`・`kono`・`korewo` (o・u で終わる語) で、`kyouha` の後ろは変わらない。
  - 得は大きい: 〜う + に + つ… の日本語 91,022 件が直る (`jikkounitsukareta` じっこunitすかれた → じっこうにつかれた、`shokugyounitsuita` → しょくぎょうについた、`zenkounitsuinn` → ぜんこうについん、日本語のローマ字の 16 万件では `jinjounitsukimashite` → じんじょうにつきまして・`juunitsupparu` → じゅうにつっぱる)。前後とも外れ 49 件 (`buntsuunitsuburu` buntすunitすぶる → buntすうにつぶる。前の `bunt` は別の型)。
  - テスト・正解表 (絞った形): テスト 783/783 (新しいテストは変更前で `sounitsuite` が落ちる)、`--eval` 1269/1271 で出力の全文が変更前と同じ、`tools/eval-compare.sh` (base は main `5fdb413` を detach した作業ツリー) 278/310 → 278/310、broken 0 / fixed 0。
  - **go 判定: no-go** (どの形も) [追記 2026-10-16: 絞った形はリョウさんの判断で採用した。この節の末尾]。依頼の基準 (よく使う語・自然な日本語での悪化 0) に対して、絞った形でも `units`・`unit`・`upset` の悪化が 54 件残る。文字だけでは `kono` + `unit` + `suru` と `この` + `う` + `につ…` を区別できない (どちらも o・u の段の かな + u + ni + t + s)。ただし得 (〜うにつ… の日本語 9 万件) と損 (o・u で終わる日本語のすぐ後ろに続けて打った u で始まる英単語 + s で始まる続き、よく使う語は 3 語) の大きさは大きく違うので、絞った形を採るかはリョウさんの判断に残す。実験のコミットは `exp/inp-sounitsuite-nogo` (`0bac8a2` = 古いブランチの形、`b6f25bf` = 絞った形。手元のみで公開していない)。KNOWN_LIMITS の K86。
- **テスト** (どちらも実験のブランチにだけある): `LettersShownAsEnglish_AreNotRewrittenOnCommit` (`tabde`・`tabga`・`kyouhatabdeyaru` と、変わらないこと `tabete`・`onegaishimsu`・`tabde` + Space)、`LongVowelUThenNi_TsuStaysJapanese` (`sounitsuite`・`hontounitsukareta`・`kaijounitsukuto` と、変わらないこと `kyouhacommitsuru`・`editsuru`・`unitsuite`・`unitsuushin`・`unit test`)。古いブランチと本家のテストから移した。既存テストの期待値は変えていない。A のテストは、スペルチェッカーを入れないと変更後でも `tabde` が `たべ` のまま (`tab` が英字の区間にならない) なので、どちらのテストもほかのテストと同じく `BuiltInWordChecker` を入れて動かす。
- **未確認**: 実機 (ビルド・インストールしていない)。数はすべて Core のテスト用キーボードでの比較で、かな漢字変換はしていない。NSSpellChecker の答えはこの Mac のもの。損の側の入力は機械的に作った形 (母音を 1 つ落とした打ち間違い、前 8 種 + 英単語 + 続き) で、日本人がよく打つ形の割合は分からない。B の悪化は、前の語と英単語の間に空白を入れた 5 例 (`kono units`・`kono unitsuru`・`kono unit suru`・`sou unitsuru`・`hontou upsetsuru`) では起きなかった (絞った形・NS の表) が、空白付きの入力を大量には数えていない。打ち間違いの直しの当たりは変更前でも 25,900 / 89,951 件で、直しの仕組みそのものの当たりの低さは別の話。
- **B の採用 (2026-10-16、リョウさんの判断「一旦おすすめで。そうについて という言い回しはあまり使わないので大丈夫な気がする」)**: 絞った形 (`b6f25bf` のコード・テスト) を `feat/sounitsuite` に 1 コミットで入れ直した。基準 (よく使う語の悪化 0) には届かないが、得 (〜う + に + つ… の日本語 91,022 件) と損 (よく使う語 54 件、o・u で終わる日本語のすぐ後ろに空白なしで `unit`・`up` などを続けた形) の差が大きいので採る。残る損は KNOWN_LIMITS の K86 (仕様) に書き直した。テスト 790 → 791 (main の日時の候補の後)、`--eval` 1269/1271。A (`tabde`) は no-go のまま (K85)。
- **独立査読と、を の除外 (2026-10-16)**: 査読 (自作の入力 1,747 件、同梱の一覧と NSSpellChecker の答えの表の両方) で、直ったのは 〜う + に + つ… の日本語 253 件、日本語の側の悪化 0。開発でよく使う語 (`update`・`upload`・`user`・`until`・`utils`・`unix`・`upgrade`・`uninstall` など) は、`きょうupdateする` を含め変わらない (t の直後に s が来ないので影響しない)。悪化 49 件はどれも「o・u の段で終わる かな + u で始まる語 + t + s」で、そのうち開発でありそうな `これをunsetする` → これをうんせつる は、助詞の を の後ろの う を伸ばす音と見ないようにして戻した (を を伸ばす語は無い)。`このunitする` → このうにつる・`このunitテスト` → このうにつてすと は残る (の + う は 脳・農 などの語があり区別できない)。K86 の「空白を入れれば崩れない」は「英単語の前に空白」に直した (後ろだけの空白では崩れる)。テスト 791/791 (`korewounsetsuru`・`sorewounitsuika` を足した)、`--eval` 1269/1271、eval-compare broken 0。
- **B の実機確認 (2026-10-16 16:05 のビルド = main `9796671`、リョウさん)**: `sounitsuite` → そうについて、`hontounitsukareta` → 本当に疲れた、`kyounitsuite` → 今日について、`korewounsetsuru` → これをunsetする、`kyouupdatesuru` → きょうupdateする、`kyouhacommitsuru` → 今日はcommitする。6 件とも期待どおり。

## Phase 1 の実機確認 (2026-10-16)

- リョウさんの報告: Phase 1 (1-1〜1-8、同時期の ATOK スタイルの 1 音確定を含む) は実機で確認済み。各節の「実機で確かめること」の項目ごとの結果は記録されていないので、ここでは「確認済み」とだけ書く。管理表の 1-1〜1-8・P41・P42 の行に同じ注記を足した。各節の「実機未確認」の文は、当時の状態として残す。

## 追加項目 P46 (本家 2026-10-14 までの追従、2026-10-16)

- **目的**: 本家 `yksr-melt/Meltype` の main `45e14f8` (2026-10-14。追従表の `db433da` より 28 コミット先) までを精査し、取り込める修正・直せる誤判定を Mac 版に反映する (Mac の PR の調査 (10-11) とウォッチの起点 `a4b6f1a` のあいだに本家に入った PR と、調査の前 (Mac の最初の Core に最も近い本家の版のあと。最も近い版は 6 つあり、どの版のあとからかで #37 (再変換) が入るかが変わる) に入って調査が選ばなかった PR は、まとめて調べていない。下の「P46 で触れなかった既知の穴」(9))。精査の結果は [UPSTREAM_REVIEW_2026-10-16.md](UPSTREAM_REVIEW_2026-10-16.md) (調査員 10 人 + 調査の査読 6 回 (RV1〜RV6。下の P46 の独立査読とは別))、リョウさんの決定 (4 件) は同じ文書の §0。
- **やり方**: ブランチ `adopt/upstream-2026-10-16` (基準は main `0dee8de`。枝を作っている間に main が進んだので、最後に merge で main に統合した = 下の「main への統合」) を作業用の別フォルダー (git worktree) で作り、**1 項目 1 コミット**。各項目で、先にテストを足して**変更前に赤**を確かめ、変更を入れて**緑**と、**全テストの件数が「直前の件数 + 足した本数」と一致**することを確かめてからコミットした (手順を自動で流す小さなスクリプトで。失敗したら止まる)。足したテストは `src/Meltype.Core.Tests/UpstreamP46Tests.cs` に 61 本 (枝の先端 P46-46 までで 59 本。P46-41 と P46-47 の 1 本ずつは統合した木だけ)。44 本は変更前に赤を確認、17 本は守りや今の動きを固定するテストで前後とも緑 (`P46_2_JapaneseLongVowels_AreUnchanged`・`P46_17_ValidOrMissingFiles_DoNotCreateBroken`・P46-23 の 2 本・`P46_24_LiveConversion_SymbolsStillDoNotCount`・P46-26 の `ExistingLooseBroken` と `CorruptAgain`・P46-29 の 2 本・`P46_34_ProtectUnreadable_ClassifiesByException`・P46-38 の 2 本・P46-43 の 3 本・P46-46 の 1 本・P46-41 の 1 本。P46-38 の 2 本と P46-43 の 3 本と P46-46 の 1 本は、コードを 1 か所壊した木で赤になることを確かめた)。実機の NSSpellChecker にだけ現れる挙動 (P46-18・P46-20・P46-23) は、その答えを真似た偽のスペルチェッカーでテストした。
- **やったこと** (コミット件名の本家の番号は、その修正が直す本家の Issue/PR。P46-1〜22 が取り込み、P46-23〜64 が独立査読の指摘への対応と統合の直し: 1 回目の対応が P46-23〜29、2 回目が P46-30〜34、3 回目が P46-36〜40、4 回目が P46-42〜44、5 回目が P46-45〜48 (P46-47 は探索員が見つけた統合でだけ起きる崩れの直し、P46-48 は P46-47 のテストを強める)、6 回目が P46-49 (P46-47 のテストに、先頭の単位から始まる語の例を足し、変異の注を直す)、7 回目が P46-50 (P46-47 のテストに、大文字で始まる語と、長い語の頭まで 8 単位さかのぼる語の例を足し、変異の注を直す)、8 回目が P46-51 (P46-47 のテストに、16 文字の語と、語の途中に大文字をはさむ語の例を足し、模擬の判定器の語の数の書き方と変異の注を直す)、9 回目が P46-52 (P46-47 のテストに、大文字の略語の直後に頭が大文字の長い語が来る例を足し、変異の注を直す)、10 回目が P46-53 (P46-47 のテストに、3 文字の略語・略語の直後の頭が小文字の語の例と、大文字を含む英字の直後の断片の守りの例を足し、変異の注を直す)、11 回目が P46-54 (P46-47 のテストに、略語の長さ (2〜6 文字) ごとの例を足し、変異の注を直す)、12 回目が P46-55 (P46-47 のテストに、伸ばす音の う の直後の断片の守りの例と、7 文字・13 文字の大文字だけの語の例を足し、変異の注を直す。本文の INTERNATIONAL のくだりの誤りは、13 回目の箇条の RW2-02 の行に正しい事実を書いた)、13 回目が P46-56 (P46-47 のテストに、伸ばす音の う の直後の守りの例 (u の段の伸ばす音の後ろの断片と、伸ばす音の後ろの英単語) を足し、変異の注を直す)、14 回目が P46-57 (P46-47 のテストに、u の段の伸ばす音の判定の守りの例と、7 文字以上の辞書に無い大文字だけの並び (略語) の例 (8 文字と 9 文字の 2 つだけ) を足し、変異の注を直す)、15 回目が P46-58 (P46-47 のテストに、7 文字と 10〜13 文字の辞書に無い大文字だけの並びの例を足し、変異の注と、P46-57 の注の書き方を直す)、16 回目が P46-59 (P46-47 のテストに、略語の並びの位置 3 × 語の頭 2 の形と、辞書に無い 3〜5 文字・辞書の語の 6・8〜12 文字の並びの例を足し、P46-58 の注の書き方を直す)、17 回目が P46-60 (P46-47 のテストに、語の頭が大文字の子音の語の形と、頭が A 以外の大文字の母音の語の例を足し、P46-59 の注の書き方を直す)、18 回目が P46-61 (P46-47 のテストの、頭が A 以外の大文字の母音の語の例を 7 つの並び × 位置 3 にし、同梱の一覧に無い、頭が e の語の形を足し、P46-60 の注の書き方を直す) (19 回目の注 (19 回目の査読 SC2-01): 7 つの並び × 位置 3 にしたのは Element の例で、Underachievement の例は 1 つのまま)、19 回目が P46-62 (P46-47 のテストの Element の例を 23 の並びのすべて × 位置 3 にし、sa1_lvow の数の母数を直す)、20 回目が P46-63 (P46-47 のテストに、1 文字の並び × 位置 3 の例 (Ament・Cement・Element の 18 例) を足し、捕まえない写し方の一覧をそろえる)、21 回目が P46-64 (P46-47 のテストに、1 文字の並び 26 字 × 語の形 18 (語 6 × 位置 3) の例 (459 例) を足し、頭が小文字の語と sc1_luo の注を直す) (22 回目の注 (22 回目の査読 SF1-04): 459 例は、468 入力から、模擬の判定器と NS で答えが違う 9 入力を除いた数)。P46-35 は欠番 (統合の直しに使う予定だった番号が P46-41 になった)。P46-41 (3b6af06)・P46-47 (485cb80)・P46-48 (e8f06d9)・P46-49 (c210017)・P46-50 (c5737c3)・P46-51 (aed8e66)・P46-52 (0d2897e)・P46-53 (43b6ec9)・P46-54 (19ad2ac)・P46-55 (f653fec)・P46-56 (bedf9f7)・P46-57 (cf80890)・P46-58 (6acfe6d)・P46-59 (364841f)・P46-60 (9102d1f)・P46-61 (289a3e0)・P46-62 (90290e7)・P46-63 (bf5c6ed)・P46-64 (5ddc046) は main への統合の後ろに載せたコミットで (P46-41 は統合で必要になった直し、P46-47 は統合でだけ起きる崩れの直し、P46-48〜P46-64 は P46-47 のテストを強める)、統合のコミットの後ろにある (P46-42〜46 は、統合の前の枝のコミット)):

| P46 | 内容 (コミット件名) | コミット |
|---|---|---|
| P46-1 | infobesity が いんふぉべしty にならないようにする (本家 #609, #538) | 5ce1f23 |
| P46-2 | bio-break と Blu-ray を英字のままにする (本家 #603, #533, #435) | 23d948c |
| P46-3 | 「いめ」で IME を候補に出す (本家 #534) | 4420099 |
| P46-4 | vibecoding が ゔぃべcoding にならないようにする (本家 #412, 0738e11) | 4127bec |
| P46-5 | oem3 が おえm3 にならないようにする (本家 #34, 4a5a62b) | da16dc8 |
| P46-6 | Xcode・DeepSeek・Mozc を英字のままにする (本家 #34, #347, #249) | c00638d |
| P46-7 | 稀な英単語 8 語と固有名詞 2 語を英字のままにする (本家 #683 #686 #689 #690 #692 #725 #726 #730 #731 #733) | 1e0512a |
| P46-8 | あおにしき を 安青錦 に変換できるようにする (本家 #706) | 090a531 |
| P46-9 | japanese が じゃぱねせ にならないようにする (本家 #712) | 6b129b9 |
| P46-10 | やほやほ を 谷保谷保 にせず、ぶいあーるちゃっと を VRChat にする (本家 #424, #365) | 6d94329 |
| P46-11 | うちかた・かいかた の候補に 打ち方/撃ち方・買い方/飼い方 を足す (本家 PR #615 の辞書の部分だけ) | 5a0e503 |
| P46-12 | fyu を ふゅ と読む (ふゃ・ふゅ・ふょ) (本家 #674) | cef1639 |
| P46-13 | 小文字で打った siri-zu の siri を英字の語として数えず、しりーず にする (本家 #656) | c676862 |
| P46-14 | 入力の先頭が . のドットファイル (.exe・.gitignore・.env) を英字のままにする (本家 #34, 803719c) | ffae88a |
| P46-15 | data が だた にならないようにする (日本語の後ろでは今までどおり) (本家 #34) | b2f3251 |
| P46-16 | cye を ちぇ と読む (cyen = ちぇん) (本家 #34, 05a1bb5) | a68dd06 |
| P46-17 | 壊れた conversions.json・translations.json を .broken に残す (本家 #551) | 511839d |
| P46-18 | 長いローマ字文 (73 文字以上) が途中から英字扱いになるのを直す (本家 PR #709) | 6a697ef |
| P46-19 | ライブ変換で、伸ばし記号だけで 4 文字に届いた短い読み (な～～～) を漢字にしない (本家 PR #632, #505) | 831a10b |
| P46-20 | 英単語の頭の t + s を、よく使う語の頭になる つ にまとめる (shatsu → しゃつ) (本家 #594) | 431d5f5 |
| P46-21 | docs/USAGE.md: 「/ → ・」は実際と違うので直す (/ は半角のまま、z/ で ・) | 190f8fa |
| P46-22 | mac/homebrew/README.md: 公開 tap は作成済みなので書き直す | e1ca403 |
| P46-23 | 辞書系テストに「実機の判定器に近い模擬」の経路を足し、テスト後の判定器を元の値に戻す | 80b0ef8 |
| P46-24 | ライブ変換を始める長さに数字・英字も数える (P46-19 の見落とし) | 0075050 |
| P46-25 | 小文字の固有名詞を英字の語に数えない変更を、長音の - の前だけにする (P46-13 の見落とし) | 13f3b4d |
| P46-26 | 壊れた学習データを .broken に残せないときと、読めなかっただけのときは、元のファイルを上書きしない (P46-17 の穴) | e19853c |
| P46-27 | 読みの前方一致の表を、使うときまで作らない (P46-20 の起動コスト) | 8707c8f |
| P46-28 | 辞書の注釈を実際に合わせて直す (datamation の出どころ、じゃぱねせ) | b756468 |
| P46-29 | data の境界のテストを足す (mydata・database・dataset ほか。日本語の語は変えない) | 55e9ad9 |
| P46-30 | 「学習データをすべて消去」が、保存を止めたファイルを消せていなかったのを直す (P46-26 の副作用) | 9b80ff7 |
| P46-31 | languages.json と model.json の読み込み失敗の警告から、打った語を外す (P46-26 の残り 2 か所) | 0ef95e2 |
| P46-32 | 保存を止めているファイルの警告を、保存のたびではなく止めてから 1 回だけ出す | af6271f |
| P46-33 | NUL・UTF-16 の BOM だけのファイルも、前に残した .broken を潰さない | f6fca66 |
| P46-34 | P46-23〜29 のテストを強める (渡した読みと英字を見る・. が残るのを見る・例外の分け方を固定する・root で黙らない) | d3bd946 |
| P46-36 | 「学習データをすべて消去」が、ファイルに書けなかったときに成功と答えないようにする (変換・辞書の提案・英訳) | 3558cf8 |
| P46-37 | ClearAll() の本体を ClearAllAt() に分ける (保存場所を引数で受け取る。動きは変えない) | e876b68 |
| P46-38 | 全消去が、学習データのどのファイルが止まっていても削除して再開すること・止めたものだけを消すことを、テストで固定する | 9007cad |
| P46-39 | 退避できなかった警告に HResult を出し、languages.json の「保存しません」の警告を止めてから 1 回だけにする | 4276be1 |
| P46-40 | 「学習データをすべて消去」の確認ダイアログに、読めなくて保存を止めているファイルも中身ごと消すことを書く | 3f7ec64 |
| P46-42 | 先頭が . の入力で、打った英字が確定のときに消える・変わるのを直す (4 回目の査読 RJ-01) | 762626d |
| P46-43 | 全消去の「ログの削除・戻り値の合成」と、止めていない失敗の警告をテストで固定し、ログを先に消すようにする | 0b8957b |
| P46-44 | 「学習データをすべて消去」の確認ダイアログの文面を、3 つの段落に分けて内部の言葉をやめ、先にコピーを取る方法を書く | 2e4a2f8 |
| P46-45 | P46-42 の性質テストを、表示で英字に見えている入力は確定してもその表示のまま、に強める (5 回目の査読 RL-02) | 7f4e6d6 |
| P46-46 | 全消去が渡すログ 3 つの一覧をテストで固定し、コメントの言い切りを限り、ダイアログの文面の細部を直す (5 回目の査読 RL-04・05・06) | bd89649 |
| P46-41 | main の T1 と統合して直った adtech + の のテストの期待値を変え、残る 3 形を限界として固定する | 3b6af06 |
| P46-47 | 日本語 + 英単語 + する の t と s が統合の木でだけ つ にまとまるのを、長い英単語の中の断片を読みの頭として飛ばさないことで直す | 485cb80 |
| P46-48 | P46-47 のテストに、短い長い語 (ament) の例を足す (5 回目の査読 RM-01) | e8f06d9 |
| P46-49 | P46-47 のテストに、先頭の単位から始まる語の例 (cadent) を足し、変異の注を直す (6 回目の査読 RN-01・03・04) | c210017 |
| P46-50 | P46-47 のテストに、大文字で始まる語と、長い語の頭まで 8 単位さかのぼる語の例を足し、変異の注を直す (7 回目の査読 RP1-01・02・03) | c5737c3 |
| P46-51 | P46-47 のテストに、16 文字の語と、語の途中に大文字をはさむ語の例を足し、模擬の判定器の語の数の書き方と変異の注を直す (8 回目の査読 RQ1-01・02、RQ2-02・05) | aed8e66 |
| P46-52 | P46-47 のテストに、大文字の略語の直後に頭が大文字の長い語が来る例 (PCUnderachievement・PCAment) を足し、変異の注を直す (9 回目の査読 RR1-02・RR2-05) | 0d2897e |
| P46-53 | P46-47 のテストに、3 文字の略語・略語の直後の頭が小文字の語の例と、大文字を含む英字の直後の断片 (ぱんつ・でんつ) の守りの例を足し、変異の注を直す (10 回目の査読 RS1-01・RS1-02) | 43b6ec9 |
| P46-54 | P46-47 のテストに、略語の長さ (2〜6 文字) ごとの例 (略語の直後に頭が大文字の語・頭が小文字の語と、読みの頭になる断片の守り) を足し、変異の注を直す (11 回目の査読 RT1-01・RT1-02) | 19ad2ac |
| P46-55 | P46-47 のテストに、伸ばす音の う の直後の断片の守りと、7・13 文字の大文字だけの語の例を足し、変異の注を直す (12 回目の査読 RU1-01・02・03) | f653fec |
| P46-56 | P46-47 のテストに、伸ばす音の う の直後の守りの例 (u の段の伸ばす音の後ろの断片と、伸ばす音の後ろの英単語) を足す (13 回目の査読 RW1-01・02) | bedf9f7 |
| P46-57 | P46-47 のテストに、u の段の伸ばす音の直後に u で始まる語の頭が来る例と、7 文字以上の辞書に無い大文字だけの並びの例を足す (14 回目の査読 RX1-01・02) | cf80890 |
| P46-58 | P46-47 のテストに、7・10〜13 文字の辞書に無い大文字だけの並びの例を足し、P46-57 の注の書き方を直す (15 回目の査読 RY1-01・02) | 6acfe6d |
| P46-59 | P46-47 のテストの略語の並びに、位置 3 × 語の頭 2 の形と、辞書に無い 3〜5 文字・辞書の語の 6・8〜12 文字の並びを足し、P46-58 の注の書き方を直す (16 回目の査読 RZ1-01・02) | 364841f |
| P46-60 | P46-47 のテストの略語の並びに、語の頭が大文字の子音の語の形 (Cement) と、頭が A 以外の大文字の母音の語の例 (Element) を足し、P46-59 の注の「条件にできる次元」と「捕まえていない写し方」の書き方を直す (17 回目の査読 SA1-01〜03) | 9102d1f |
| P46-61 | P46-47 のテストの、頭が A 以外の大文字の母音の語 (Element) の例を 7 つの並び × 位置 3 にし、同梱の一覧に無い頭が e の語 (emolument) の形を足し、P46-60 の注の書き方を直す (18 回目の査読 SB1-01〜03) | 289a3e0 |
| P46-62 | P46-47 のテストの、頭が A 以外の大文字の母音の語 (Element) の例を 23 の並びのすべて × 位置 3 にし、sa1_lvow の数の母数を直す (19 回目の査読 SC1-01・02) | 90290e7 |
| P46-63 | P46-47 のテストに、1 文字の並び (X・A) × 位置 3 の、頭が大文字の語 (Ament・Cement・Element) の例を足し、捕まえない写し方の一覧をそろえる (20 回目の査読 SD1-01・04) | bf5c6ed |
| P46-64 | P46-47 のテストに、1 文字の並び A〜Z の 26 字 × 語の形 18 (語 6 × 位置 3) の例を足し、頭が小文字の語と sc1_luo の注を直す (21 回目の査読 SE1-01・02・03、SE5-01・02、実装者 IM21-01) | 5ddc046 |

- **前の項目とのつながり** (14 回目の査読 RX6-01): P46-6 で、計画書の Q9 の「限界・未確認」の (4) の `Xcode` → `Xcoで` と、P25〜P28 の「未解決」の #249 の `mozcgamiteirutohaie` は、統合した木で直る (BI・NS とも、main では `Xcode` → `Xcoで`・`mozcgamiteirutohaie` → `mozcgamiteirutohaie`、統合では `Xcode` → `Xcode`・`mozcgamiteirutohaie` → `mozcがみているとはいえ`。表示も確定と同じ)。同じ 2 か所に並ぶ `xmas` と `mocz` は、統合した木でも変わらない (Q9 の (4) の `xmas` → `xまs` は BI の答えで、5 つの木とも同じ。NS では 5 つの木とも `xmas` → `xmas`。#249 の `mocz` は、BI・NS とも 5 つの木で、確定が `mocz` → `もか`、表示が `mocz` → `mocz`。15 回目の査読 RY4-05)。Q9 と P25〜P28 の行は、その時の記録として変えない。
- **確認** (枝の先端は P46-46 (bd89649)。P46-34 の後でコードを変えたのは P46-36・P46-39・P46-42・P46-43、動きを変えない書き換えが P46-37・P46-46 (一覧の取り出しとコメント)、ダイアログの文面だけが P46-40・P46-44 (P46-46 でも細部を直した)、テストだけが P46-38・P46-45。取り込み 22 コミットのコードが出そろったのは P46-20 (431d5f5)。以下の比較のうち「先端 d3bd946」と書いたものは、P46-34 の木で測った。P46-36 以降の枝のコミットのうち、判定を変えたのは P46-42 (先頭が `.` の入力の確定。変換ボックスの表示は変えない) だけで、ほかは保存・全消去・警告・文面・テストだけの変更。統合した木にだけ載せた P46-47 (長い英単語の中の断片を、読みの頭として飛ばさない) も判定を変える (P46-48〜P46-64 はテストだけ)。統合した木 (先端は P46-64 = 5ddc046。コードは P46-47 = 485cb80 と同じ) での確認は下の「main への統合」):
  - 全テスト 780 → **839 件** (枝の先端 bd89649。全部通る)。main (`657d042`) に統合した木 (先端 P46-64 = 5ddc046) では **852 件** (main 側の 791 + P46 の 61。全部通る。P46-47 の時点でも 852 件で、P46-48〜P46-64 は例を足しただけで本数は変わらない)。
  - 回帰ゲート (`tools/eval-compare.sh`、取り込み前 `0dee8de` 対 先端 d3bd946): **278/310 → 278/310** (`mac-device` 40/41、`mac-protected-spans` 45/46、`mac-terminal` 16/20、`upstream-quality` 177/203)、broken 0 / fixed 0 / added 0 / removed 0。固定の表に今回の入力は入っていないので、取り込みの効果はこの表には出ない (悪化が無いことの確認)。統合した木 (P46-47 = 485cb80) 対 main でも同じ (**278/310 → 278/310**、broken 0 / fixed 0 / added 0 / removed 0)。
  - 取り込み前後の比較 (BI = 同梱の英単語表、NS = 実機と同じ NSSpellChecker を差し込んだ再現。取り込み前 `0dee8de` 対 431d5f5):
    - 狙いの 40 入力: 38 入力が直った (BI・NS の少なくとも一方で。`Blu-ray` は NS では元から正しく、`shatsu` 系 5 入力は BI では元から日本語)。残る 2 入力 (単独の `deepseek` `DeepSeek`) は元から正しい。日本語の後ろ (`kyouhadeepseekwotukau` → `きょうはdeepseekをつかう`) は BI・NS とも直った。
    - 守りの 30 入力 (`e-to` `su-pa-` `tsunami` `natsu` `matsuri` `outsource` `cats` `main` `domain` `database` ほか): 29 入力は前後で同じ。変わったのは `dataset` だけ (BI のみ。`だたset` → `dataset`、改善)。
    - 先端 d3bd946 で、この 80 入力 (狙い 40 + 限界 10 + 守り 30) の結果は 431d5f5 と完全に同じ。
  - 先端 d3bd946 での大きな比較 (取り込み前 `0dee8de` 対 先端):
    - 日本語側 15,000 入力 (読みの辞書から 3,000 語 × 5 形): BI で 3 入力、NS で 19 入力が変わった。NS の 19 入力は #594 の 16 入力 (`shatsu` 系、`kyouhautsuru` → `きょうはうつる`、`hautsu-` → `はうつー` など) と、BI と共通の `peru-wo` `peru-desu` `peru-ga` (`ぺる-を` などが `ぺるーを` などに。P46-13 と同じ仕組みで長音が正しくなる向き)。**英字が増えた入力は 0**。
    - 英語側 15,000 入力 (同梱の英単語表から 5,000 語 × 3 形): BI・NS とも 2 入力 (`data` → `data`、`datawo` → `dataを`。どちらも `だた`・`だたを` から) だけが変わった。
    - `data` のガード (P46-15): ガードを外した木では `hedatari` (へだたり) が `へdataり` になる (「へだたり」「へだたる」を含む 3 語 15 入力のうち 10 入力、BI・NS とも)。ガードありは 0 入力。
    - P46-18 (#709): 73 文字の文 + 日本語の語 3,000 入力 (NS) で、実機の判定器と同梱の表の答えが違った 55 入力がすべて同梱の表と同じになり、ほかに変わる入力は 0 (P46-18 だけを入れた木で実測)。
    - 文書に書いた打鍵の例の照合は、下の「main への統合」に書いた (統合した木と取り込み前の木に流した結果)。
  - Mac ビルド (統合した木 (P46-47 = 485cb80) の複製で `mac/build.sh --no-install`): 成功 (216 秒)。`codesign --verify --deep --strict` OK、`tools/check-mac-aot-settings.py` は PASS 6・FAIL 0 (終了コード 0)、辞書アプリの自己テスト 212 件 PASS (ロジック 129 件 + 本体を通した操作 83 件)。`libMeltypeNative.dylib` に P46-30・P46-36・P46-39 の警告の文字列 (UTF-16) が、`Meltype` の実行体に P46-36 の失敗表示と P46-44・P46-46 のダイアログの文面 (旧文面は残っていない) が入っていることをバイト列で確認 (13 項目すべて一致)。 **実機にはまだ入れていない**。
  - 秘密情報・ローカルの絶対パスの走査: P46 の全コミット 64 件 (件名が `P46-` か `Merge: 本家追従` のもの) の本文と追加行、および文書の差分と新規ファイルを走査した (`scan_land.py`)。探した形: GitHub・AWS・Slack・Google のキー、`sk-` のキー、秘密鍵、Bearer/Authorization、メールアドレス、ローカルの絶対パス (ホームフォルダー・一時フォルダーの形)。**該当は 0 件**。テストの中の偽のパス (P46-31 のテストが、パスが出力に入らないことを確かめるために書いた偽の例) だけは除いた。
- **独立査読と対応** (査読員は実装とは別。複製の上で再実行し、指摘だけを書く。修正は実装側):
  - **1 回目** (取り込み 22 コミット。辞書 RA、規則 RB、`.broken` と全体の整合 RC の 3 人): **P0 1・P1 8・P2 21**。P0 と P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P0 (RB-01) | P46-19 のライブ変換: 本文は「記号を数えない」だが、実装は数字・英字も数えず、`12gatsu` `100en` `iPhone15wokau` などの変換が止まる | **直した** (0075050)。偽エンジンで 12,019 入力を比べると、P46-19 の時点で止まった 2,894 入力のうち、数字を含む 1,404 (手選びの 5 入力を含む) が戻り、止まるのは記号を含む 1,490 入力だけ (1,404 + 1,490 = 2,894) |
    | P1 (RA-1) | `adtech` を足すと、実機の判定器 (NS) で `adtech` + の が崩れる (`adtechnoyouna`) | **辞書に残す**。限界は K88、模擬の判定器でテスト固定 (80b0ef8)。2 回目の査読 (RD・RF) で測り直した: 読みの辞書から無作為の 1,200 語 + 助詞・語尾 18 = 1,218 形を文頭に置くと、P46 の木だけでは NS で 17 形 (1.4%) が戻り (`adtechno…` 14 形と `adtechmade` `adtechhanami` `adtechhaha`)、909 形 (75%) が直る。BI は 3 形が戻り、912 形が直る。その後、main に先に入っていた別セッションの T1 (d36ef48) と統合した木で測り直すと、`adtechno…` の 14 形は直り、戻るのは NS・BI とも同じ 3 形 (0.25%) になった (3b6af06)。最初に書いた「34 形のうち 15 形が直り 4 形が悪化」は、並べた続きの数で、頻度ではなかった |
    | P1 (RA-2) | 辞書系テストが同梱の表固定で、NS だけの差を見ない | **直した** (80b0ef8) |
    | P1 (RB-02) | P46-13 が `.` の前まで及び、`miro.com` の `.` が `。` になる | **直した** (13f3b4d)。固有名詞 706 語 × 4 形 (BI・NS) で、`.com` `.jp` の `。` を含む出力は 39 → 0 |
    | P1 (RB-03) | P46-12: `…fy` で終わる英単語の連結が かな になる、u 脱落の補正 2 件が消える | 規則は**変えない** (Microsoft IME・Mozc・本家と同じ綴り)。限界を測り直して書いた (K88) |
    | P1 (RB-04) | P46-14: 規模が本文と違う、日本語側の悪化、2 文字ドメイン | **書いた** (K88。先頭が `.` の日本語入力は稀) |
    | P1 (RC-1) | P46-17: `.broken` を残せないと、次の保存が元のファイルを上書き | **直した** (e19853c。失敗したら保存を止める。読めなかっただけのときも止める) |
    | P1 (RC-2) | P46-17: テストが正常系だけ | **直した** (e19853c でテスト 7 本) |
    | P1 (RC-9) | P46-12 の本文の「英単語 33,299 語中 0」の分母が同梱の表と合わない | **訂正**: 33,299 は 4〜10 文字の英単語の数 (調査の c3 コーパス) で、本文は絞り込みを書き漏らしていた。同梱の英単語表は 39,095 語で、全体でも 4〜10 文字でも `fy[auo]` を含む語は 0 (2 回目の査読 RF が再現) |

  - P2 21 件: **直した** 9 (RB-05 前方一致の表を使うときまで作らない = 8707c8f、RB-08・RC-10 テスト後に判定器を元の値へ = 80b0ef8 (P46 のテストファイルだけ。ほかのテストの `SpellChecker = null` 50 か所は P46 の前から)、RC-3 `.broken` がリンクのとき (conversions・translations の 2 か所。残りは K89)・RC-4 空ファイルで `.broken` を潰す (さらに f6fca66)・RC-5 警告に打った読みが入る (e19853c で 2 か所、0ef95e2 で残りの 2 か所) = e19853c、RA-6 `data` の境界テスト = 55e9ad9、RA-8・RC-7 辞書の注釈とテストの冒頭コメントの参照先 = b756468 と本節)、**書いた** 7 (RA-3 `Blu-ray` は NS では元から通る・RA-5 `kyouhaime` は IME にならない・RA-7 phrases の根拠は偽エンジンだけ・RA-9 `oemga` の補正が消える・RB-06 `P46-18` の閾値と長い文字列・RB-07 分母・RB-09 `icyen` が いくえん → いちぇん)、**直さない・対応なし** 5 (RA-4 `coding` は本家 `0738e11` と同じ行にそろえるため残す・RA-10 どちらも誤りの入力で見た目が変わるだけ・RC-6 他の `.broken` の権限と `suggest.json` は P46 の前から・RC-8 Windows の学習データのリセット・RC-11 並行実行の警告は害なし。RC-6・RC-8 は K89 に記録)。
  - **P46-18 の追加探索** (査読 RB が時間切れで未完了としたもの): 実機と同じ NSSpellChecker で、73〜100 文字の長い文字列を取り込み前後で比べた。英単語の連結・camelCase の識別子・16 進のハッシュ 各 80 本は、英字のままだったものが崩れた入力 0。意味のない小文字の列は、別々の標本で 100 本中 5〜8 本 (80 本の標本では 5 本) で 1〜2 文字が変わる (`xe` → `ぇ`、`de` → `で` など。72 文字以下の列は元から 100 本中 24 本が同じように変わるので、72 文字以下と同じ扱いになっただけ)。
  - **2 回目** (1 回目の対応 7 コミット 80b0ef8〜55e9ad9 の確認。コード RD、`.broken` と保存の停止の安全 RE、指摘の網羅と全体の整合 RF の 3 人。7 コミットを個別にビルドして全テストが 805 / 807 / 808 / 815 / 815 / 815 / 817 と本文どおりになることも確かめた): **P0 0・P1 5 (RE-1 と RF-1 は同じ指摘なので 4 件)・P2 26 (重複を除くと 25 件)**。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RE-1・RF-1) | P46-26 で保存を止めたファイルが「学習データをすべて消去」で消えないのに、全消去は成功を返し、Mac は「学習データを消去しました」と出す (P46-26 より前は消えていたので、消去が効かなくなった悪化) | **直した** (9b80ff7)。全消去は止めていたファイルを削除して、止めた状態を解く (消去は利用者が確認して選んだ操作なので)。削除できなければ失敗を返し、Mac は「消去できなかったものがあります」と出す。保存の途中で残った一時ファイル (`.tmp`) も消す。代償: 読めなかった中身は、全消去を選ぶと消える |
    | P1 (RE-2) | 保存の停止はプロセスが終わるまで外れない。Windows では、起動時の 1 回の共有違反でも、再起動まで学習が保存されない (推測。Windows は未確認) | **書いた** (K89)。以前の「空で動いて次の保存で全履歴を上書き」よりは安全。読み直しで自動復帰する仕組みは足さない (止めた側のメモリは空に近く、復帰して保存すると、読み直した中身を上書きするおそれがある。設計が要るので、必要になったら別の項目で)。Mac では IME の再起動か、全消去 (9b80ff7) で戻る |
    | P1 (RE-3) | `IOException`・`FileNotFound` の分岐と BOM・CR・TAB の判定に、赤になるテストがない (変異 3 本が緑のまま) | **直した**: 例外ごとの分け方を固定 (d3bd946)、BOM・CR・TAB を含む空の形 (f6fca66)、起動時に他が開いていた `IOException` の実フロー (9b80ff7) |
    | P1 (RD-01) | K88 の「戻るのは、文頭の `adtech` + の だけ」は事実と違う | **直した** (K88)。NS で戻る 17 形のうち 14 形が `adtechno…`、残り 3 形が `adtechmade` `adtechhanami` `adtechhaha` (BI でもこの 3 形が戻る)。辞書に残す判断は変えない (直る形のほうが多い。上の RA-1 の行)。なお、main の T1 と統合した木では `adtechno…` の 14 形は直り、3 形が残る (3b6af06) |

  - P2 26 件の対応:
    - **直した** (コード・テスト): RE-5 `languages.json`・`model.json` の警告に打った語が入る (0ef95e2)。RE-6 保存を止めた警告が保存のたびに出る・文面が原因を取り違える (af6271f)。RE-7 NUL・UTF-16 の BOM・VT だけのファイルが前の `.broken` を潰す (f6fca66)。RE-9 全消去が保存の途中の一時ファイルを消さない (9b80ff7)。RE-10 `Describe` が位置の無い例外で空欄になる (0ef95e2、文面は K89 を書き直した)。RD-04・RD-06・RD-07・RE-8 (= RF-10) はテストの強化 (d3bd946): ライブ変換のテストが渡された読みを見て、英字が決め手になる 3 入力 (`kyouqxz` `kyouq` `soukq`) を足した・固有名詞の `.` が残ることを BI と模擬の両方で見る・`metadata` の限界テストに「直ったら期待値を変える」を書いた・root で権限のテストが黙って通らないようスキップを表示する。
    - **書いた** (文書): RD-02 模擬の判定器は 7 語だけで、無作為の 577 入力では NS と 5.5% 食い違う (テストのコメントを実際に合わせた = d3bd946)。RD-03 `data` + 英語は一部が直らない (K87。このときは 4 標本で NS 9〜12%・BI 3〜6% と書いたが、3 回目の査読 RH-03 を受けて全数 33,299 語を測り直し、NS 12.8〜12.9%・BI 5.5% に書き直した)。RD-08 判定器を元の値に戻す直しは P46 のテストファイルだけ。RE-4 `.broken` のコピー 5 か所のうち 3 か所はリンクを辿る (K89)。RE-11 P46-27 の「1 回だけ」は `RomajiTypoCorrector` のインスタンスごと (IME のセッションごとに 1 回ずつ約 3.5〜7 ms と 4.6 MB。Mac の IMK がセッションをいくつ作るかは未確認。表を静的に共有する案は実害が小さいので見送り)。RF-2 `adtech` の数え方 (上の RA-1 の行)。RF-3 33,299 の出どころ (上の RC-9 の行)。RF-4・RF-8 K88 の数字を標本の幅で書いた。RF-5 `Blu-ray` が NS では元から正しいことの書き場所はこの節の「確認」。RF-7 数字の食い違い (文書の例は 42 件。ライブ変換で戻った 1,399 は数字を含む入力で、手選び 5 を足すと 1,404)。RF-9 回帰ゲートの 4 コーパスは読みと英単語表の語だけで、ライブ変換・固有名詞 + `.`/`-`・`.broken`・遅延生成は通らない (P46-24〜27 の裏付けは各項目の専用測定)。RF-11 Windows の一過性の共有違反 (K89)。RF-6 表に行のなかった項目: `english.txt` に本家由来のコメントが 2 行並ぶのは害なし。`Datamation`・`OpenStreetMap` を打つ途中の表示は 42 接頭辞 (BI・NS) で害なし。調査時の測定で再現しなかった数字 (58,174 入力・近似 240 入力の 93 件・736 入力の 112 件) は、P46-13・19 では測り直した数字に置き換え済み。
    - **直さない・対応なし**: RD-05 `|| c == 'ー'` は冗長 (`char.IsLetterOrDigit('ー')` が true) だが害はなく、意図を残す。RD-09 既存の `LearnedWordsTests.TwoInstances_ConcurrentEdits_LoseNothing` が、全テストを 8 本同時に流したときに 1 回赤になった (時間に依存するテストで P46 と無関係。単独では 5 回とも緑)。
  - **3 回目** (2 回目の対応 5 コミット 9b80ff7〜d3bd946 のコードと、そのときの文書。コード RG、文書 RH の 2 人。5 コミットを個別にビルドして全テストが 821 / 823 / 824 / 825 / 826 と本文どおりになることも確かめた): **RG: P0 0・P1 3・P2 8、RH: P0 0・P1 1・P2 16** (合わせて P0 0・P1 4・P2 24)。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RG-11) | 2 回目の対応で書いた「削除できなかったときは false」は、止めているファイルにしか当たらない。止めていない普通のファイルは、書けないフォルダー (権限・読み取り専用) で「成功」と出て、メモリだけ空になり、打った読みがファイルに残る (2 回目の RE-1 の一般形。P46-26 より前からの弱点) | **直した** (3558cf8)。変換・辞書の提案・英訳の 3 つに `TryClear()` を足し、ファイルに書けなかったとき (保存を止めたままのときも) は全消去が false を返す。Mac は「消去できなかったものがあります」と出す。`Clear()` は今までどおり例外を投げない (Windows 版の呼び出しは変わらない)。代償: 書けないとき、メモリは空になるがファイルは残る (書けるようにしてもう一度消去すれば消える) |
    | P1 (RG-1) | 公開の `ClearAll()` (Mac が呼ぶ唯一の入口) が、止めたファイルの一覧を渡す行を消しても、どのテストも赤にならない (変異で全部緑) | **直した**: テストから呼べるよう本体を `ClearAllAt()` に分け (動きは変えない = e876b68)、学習データ 5 ファイルのどれが止まっていても削除して再開すること・止まっているユーザー辞書には触らないことを固定した (9007cad) |
    | P1 (RG-2) | `ReleaseBlocked` の「止めているものだけ削除する」行を消しても緑 (消すべきでないものを消さないための唯一の砦) | **直した** (9007cad)。止めていないファイルは中身ごと残り、止めていても渡した一覧に無いものは触らないことを固定した |
    | P1 (RH-01) | 統合レポートの §0 の「調査時の数字のうち訂正されたのは次の 2 つ」は足りない (悪化の規模を小さく読ませる数字が他にもある) | **直した**。UPSTREAM_REVIEW の §0 に、調査時の数字と測り直した結果を表で並べた (7 行) |

  - P2 24 件 (RG 8・RH 16) の対応:
    - **直した** (コード・テスト): RG-4 `languages.json` の警告が保存のたびに出る (4276be1。「保存しません」の警告は止めてから 1 回だけにし、保存できなかったので学習を取り消したという警告も、止めているあいだは出さない)。RG-9 `.broken` に残せなかった警告に HResult が出ない (4276be1)。RG-7 `IsBlank` の 0x0C にテストが無い (9007cad)。RG-5 全消去の確認ダイアログが、読めなくて保存を止めているファイル本体も消えることを書いていない (3f7ec64。ダイアログの表示は実機で未確認)。
    - **直した** (文書): RH-02 `adtech` の分母と判断の記録 (K88 (2) を統合した木の数字で書き直した。辞書に残すか外すかはリョウさんの判断待ち、おすすめは残す)。RH-03・RH-04 K87 の `data` + 英語を、無作為の標本ではなく 4〜10 文字の英単語の全数で書き直し、例ごとに BI と NS を書き分けた。RH-05・RH-17 K88 (4) の長い文字列の数字を、別の標本を含めて書き直した。RH-06 ライブ変換の足し算 (1,404)。RH-07 文書の例は「44 件 (28 入力を BI 22・NS 22)」と数え方を書いた。RH-08 COMPARISON の他の行を今の件数に。RH-09 README・INSTALL・ダイアログの「退避したコピー」は学習データの分だけ。RH-10 K89 を読みやすく書き直した。RH-11 「実機と同じ NSSpellChecker を差し込んだ表 (NS)」と書き、K87〜K89 に「実機は未確認」を足した。RH-12 P2 の数え方 (重複を除くと 25)。RH-13 追従表の `#649` の根拠 (差分は本家の GitHub bot のスクリプト 1 ファイルだけ)。RH-14 IMPROVEMENT_PLAN の見出しが二重になったので、P46 の節の見出しを変えた。RH-16 K89 (5) の警告の範囲。
    - **書いた** (限界): RG-3 `languages.json` が読めない (権限・所有者違い) ときは、止めた状態ではないので全消去は削除せず、権限を直すまで「消去できなかったものがあります」が続く (偽の成功ではなく失敗を返すので安全側)。RG-8 `conversions.json` がリンクで、リンク先が読めないときは、全消去はリンクを置き換えるだけで、リンク先の中身は残る (P46 の前から同じ)。どちらも K89 (3)。
    - **直さない・対応なし**: RG-6 `名前.*.tmp` の削除は学習データ 5 ファイルの名前の続きに限られ、ユーザー辞書・設定には当たらない (巻き込むのは、利用者が手で付けた `conversions.json.backup.tmp` のような名前だけで、実害は考えにくい)。RG-10 root・Windows では権限のテストをスキップして表示だけ出す (テストランナーに「スキップ」の状態が無い。書き方そのものは正しい)。RH-15 K66 の分類が「仕様」と「直す予定」の二重 (P46 の前から。別の項目で)。
  - **4 回目** (3 回目の対応 5 コミット 3558cf8〜3f7ec64 のコード RI、main との統合 (merge と P46-41) の RJ、そのときの文書 (docs コミット `a145e38`) の RK の 3 人。RI は 5 コミットを個別にビルドして全テストが 828 / 828 / 830 / 832 / 832 と本文どおりになることも確かめた): **RI: P0 0・P1 1・P2 8、RJ: P0 1・P1 3・P2 3、RK: P0 0・P1 2・P2 11** (合わせて P0 1・P1 6・P2 22)。P0 と P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | **P0 (RJ-01)** | P46-14 (先頭が `.` のドットファイル) の後、先頭が `.` で後ろに日本語が続く入力は、確定のとき打った英字が消える・別の字に変わる (`.zoverdesu` → `.zoveです`、`.zoverdetsu` → `.zoveretsu`、`.kyoumolinks` → `.kyoumolinka`)。変換ボックスの表示は `.zoverです` で合っているのに、確定が違う。BI でも NS でも起き、取り込み前と main では起きない | **直した** (762626d)。確定の直前の打ち間違いの直しが、表示で英字の部分を守る範囲に、P46-14 の「先頭の `.` で始まる英字の区間」を入れていなかった。入れた。`.` + 語幹 44 × 語尾 33 = 1,452 通りで、打っていない英字が出る入力は、P46-42 の前は BI 111・NS 100、後は BI・NS とも 0 (取り込み前と main は BI で 4 通り (`.revers` → `。revere` など) だったが、これも 0)。RJ の NS 7,545 入力を流し直すと、打っていない英字が出る入力は 5 → 0 で、答えが変わるのは先頭が `.` の 30 入力だけ。表示が日本語になる入力の打ち間違いの直しは従来どおり効く (`.zovermade` → `。ぞゔぇまで`) |
    | P1 (RI-1) | 全消去の本体 `ClearAllAt` の「ログを消す」配線と、ログ・退避コピーを消せなかったときの戻り値が、どのテストでも固定されていない (ログを消さなくしても、戻り値からログの失敗を外しても全部緑)。確認ダイアログは「ログも消します」と約束している | **直した** (0b8957b)。ログ・退避コピー・一時ファイルが消え、ユーザー辞書・設定は残ること、ログを消せなかったときは false を返すことをテストで固定した。コードを 1 か所壊した 6 通りの木で、足した 4 本が赤になることを確かめた |
    | P1 (RJ-02) | main の H1 (`a093319`) だけを入れた木で、`doumolistaa` が `どうもlisaた` になる (`ta` `a` が `a` `た` の順に出る。NS の取り込み前は `どumolisたあ`。BI は取り込み前も `どうもlisaた` なので、BI については H1 で変わったのではない。12 回目の査読 RU3-05) (15 回目の注 (15 回目の査読 RY5-03): 4 回目の報告がこの答えを見た木は、H1 だけを入れた木ではなく、main のコード (P47 まで) の木 (NS)。H1 は原因の見当) (16 回目の注 (16 回目の査読 RZ4-03): この P47 は、Mac の項目 P47 (日時の候補、`4970657`) のことで、P46-47 ではない。4 回目の時点では P46-47 はまだ無い) | **直さない** (別セッションの main の変更で、P46 の変更ではない。統合した木にも入る)。別セッションに伝える。実機での頻度は低い見込み (作為的な入力。推測) |
    | P1 (RJ-03) | T1・H1 は BI の同梱の表では動かない (BI で main だけが変えた入力は、査読員の 15 万入力でも 0 件) ので、統合の 3 者比較の BI の「0 件」は、相互作用の根拠にならない | **言い方を直した**。統合の悪化 0 の根拠は NS の入力数で書いた (下の「main への統合」) |
    | P1 (RJ-04) | 着地の手順の穴: main が動いたとき、主ツリーに別セッションの未追跡ファイルがあること、主ツリーのファイルを動かす許可 | **直した**。着地のスクリプトが、主ツリーのブランチ・main の hash・docs 枝の先端・tracked の変更なし・入ってくるファイルが主ツリーの未追跡ファイルと重ならないことを確かめ、違えば止まる (`merge --ff-only`、push しない)。main が動いたら統合を作り直す (今回も何度か作り直した。文書だけが進んだ分は、コードの木が同じことを確かめて、ゲートの結果を使った)。主ツリーを動かす許可は、リョウさんに最終報告で確認する |
    | P1 (RK-01) | `UPSTREAM_WATCH.md` の #527 が「取り込み済み (aca3b58)」になっている。`aca3b58` は main の文書のコミットで、ツールの誤検出 (追従表は「対象外」) | **直した**。#527 は「記録あり (対象外)」に、取り込んだのに「記録あり」のままだった #551 (P46-17)・#534 (P46-3) は「取り込み済み」に。ツール (`tools/upstream_watch.py`) の誤検出そのものは直していない (文書のコミットの件名の「本家 ＜hash＞」を取り込み済みと読む) |
    | P1 (RK-02) | #649 の根拠「1 ファイルだけの変更」が、WATCH の分類「Windows (+文書)」と食い違う | **確認した** (本家の clone で): `git diff --stat 300dc3c^1 300dc3c` は `.github/bot/meltype-bot.mjs` の 1 ファイルだけで、2 行追加・4 行削除 (出力は 1 file changed, 2 insertions(+), 4 deletions(-))。WATCH の「(+文書)」は、ツールが merge コミットの第 2 親との差の `README.md` も数えるため (`git diff --name-only 300dc3c^2 300dc3c` が `README.md`)。追従表の記述は正しい |

  - P2 22 件 (RI 8・RJ 3・RK 11) の対応:
    - **直した** (コード・テスト) 4: RI-2 止めていない保存の失敗でも「今回の学習は取り消しました」の警告が出ることを固定 (0b8957b。「いつも出さない」に変えても緑だった)。RI-7 全消去の確認ダイアログの文面 (2e4a2f8。内部の言葉の「保存を止めている」を「壊れている・読めないために Meltype が上書きを避けているファイル」に直し、先にデータフォルダーのコピーを取りたいときはキャンセルする、ログは古い分も消える、を足し、3 つの段落に分けた。折り返しの見え方は実機で未確認)。RI-8 `ClearAll` のコメントの「Windows 版が使う」を実態に合わせた (0b8957b。呼ぶのはテストと Mac の本体だけ)。RK-08 全消去が失敗したとき、案内する `meltype.log` を同じ操作が消す件 (0b8957b。ログを先に消す順にした。それまでは、失敗の警告を書いたあとでログを消していたので、ログへの書き出しが重なったときに、まれに警告ごと消えた。順序のテストは、書き出しを休まず繰り返す別のスレッドで、その最悪の場合を作る)。
    - **直した** (文書) 11: RJ-05 「判定は変わっていない」の範囲 (下の「main への統合」に、判定のファイルごとに書いた)。RJ-06 先頭が `.` だと全体が英字のまま確定する (`.kyoumolike`。K88 (3) に例を足した)。RK-03 K88 (2) の「3 形」は標本で 1〜3 形 (乱数の種が違うと入れ替わる) なので幅で書き、NS の名指しの例は絞った。RK-04 K87 の NS の幅を、これまでの測定が入る幅に (4 回目の対応のときは 4,275〜4,295 語。5 回目のゲートで 4,274 語が出たので、今は 4,274〜4,295 語)。RK-05 `adtech` を外すかの判断待ちを、UPSTREAM_REVIEW の §0 と追従表にも書いた。RK-06 KNOWN_LIMITS の冒頭に BI・NS の定義を足した (K88 は長いが (1)〜(5) に分けてある。行を分けるとテストの注や文書の参照 (K88 (2) など) が全部変わるので分けなかった)。RK-07 Mac ビルドの文字列の帰属 (ダイアログの文面は実行体の中、警告の文面は dylib の中。文字列があることは、動くことの確認ではない)。RK-09 `EXPERIMENT_2-5_DESIGN.md` の `LearningData.cs:57` の行番号参照を、関数名の参照にした (同じ文書の他の行番号参照は点検していない)。RK-10 UPSTREAM_REVIEW §4 の「K83 の新設案」に、調査時の仮番号で今の K83 とは別、の注。RK-11 P46-35 は欠番、の 1 行を表の上に。RK-12 内部の言葉 (「scratch の worktree」「専用のドライバー」、K87・K88 の根拠欄の ID の指し先)。
    - **書いた** (限界) 3: RI-3 書けないフォルダー (権限・読み取り専用・容量不足) では、保存のたびに警告が増える (`Remember` 20 回で 60 行。ログは 1000 件で古い方から消えるので、続くと原因の行が押し出される。P46 の前から)。RI-4 保存の失敗の既存の警告には、保存先のパス (ユーザー名入り) が出る (P46-36 のコミット本文の「パスも打った語も入らない」は、新しく足した 1 行についてだけ正しい。ダイアログは「不具合を報告するなら先にログを保存」と案内するので、共有するときは注意)。RI-5 ユーザーモデルだけは、書けなくても `ClearAll` が成功を返す (Mac は作らない。Windows 版は `ClearAll` を呼ばない)。どれも K89 (5)。
    - **直さない・対応なし** 4: RI-6 「書けた」を保存を止めているかの再確認で間接に判定する細部 (別スレッドが止めたり止めを解いたりする間に挟まる経路は、モーダルのダイアログから 1 回ずつ呼ばれる実際の使い方ではほぼ無い)。RI-9 全テストを並列で流すと `TwoInstances_ConcurrentEdits_LoseNothing` が偶発的に赤になる (単独では緑。ゲートを並列で回すときの注意)。RJ-07 P47 の候補と P46 の組み合わせを画面で確かめていない (未確認に書いた)。RK-13 計画書の P46 の節が P47 の節の後ろにある (末尾に足した結果で、害はない)。
  - **5 回目** (4 回目の対応 762626d〜2e4a2f8 のコード RL、5 回目の対応 7f4e6d6〜e8f06d9 と作り直した統合の RM、統合した木でだけ起きる崩れを探した探索員 4 人 (LV・TSU・LETTERS・P46WORDS) と、P46-47 のあとに同じ入力の作り方で流し直した 4 人): **RL: P0 0・P1 2・P2 7、RM: P0 0・P1 2・P2 4、探索: 統合でだけ起きる崩れ 1 件** (探索員は重さを付けない決まりなので、この 1 件は P1 として数えた。合わせて P0 0・P1 5・P2 11)。P0 と P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | **P1 (探索: 形 A・B)** | 統合した木でだけ、日本語 + 英単語 + する の t と s が つ にまとまる。形 A: `souunemploymentsuru` が、取り込み前・main・P46 だけでは `そうunemploymentする` なのに、統合だけ `そううnemployめんつる`。形 B (A より広い): 同梱の英単語表にない母音始まりの語 (`empowerment`・`entitlement` など。実機の判定器は単語と答える) + する (`kyouhaempowermentsuru` → `きょうはempowerめんつる`)。同梱の表にない母音始まり・t 終わりの語 2,256 語のうち 119 語。形 A は三者比較 (ゲート) で、形 B は探索員 3 人 (LV・TSU・P46WORDS) が見つけた (LETTERS の性質 P は、英字が日本語に化ける型を見ないので 0 と報告した)。BI では、形 B の語を同梱の表が英単語と見ないので統合でだけの崩れは起きない (形 A は BI では main の時点で崩れる = K87。7 回目の査読 RP2-09) | **直した** (485cb80。統合した木にだけ載せる)。P46-20 の「`ment` などの断片は日本語の読みの頭」と、main の「伸ばす音の う から始まる語・同梱の一覧にない母音 1 つの単位から始まる語は見ない」が、単独では互いの取りこぼしを救い合っていたのに、両方入ると消し合う。断片が知っている英単語で、さらに前へ伸ばした並びも知っている英単語のときは、断片を読みの頭として飛ばさないことにした。探索員が見つけた崩れ 659 入力は、直した後すべて取り込み前の答えに戻った (戻らなかった入力と、直した後に悪くなった入力は 0)。F3 (main の `IsLongVowelU` を狭める。9/12 語幹しか直らず、別セッションの守りも動く)・F5 (飛ばしを絞る。形 B が直らない) は採らなかった。詳しくは下の「main への統合」 |
    | P1 (RL-01) | 先頭が `_` `@` の入力にも、RJ-01 と同じ型の症状 (表示と確定が食い違う。表示は `_zoverdesu` と全部英字なのに、確定のとき打った英字が消えて `_zovedesu` になる)。基準 `0dee8de` から同じ (P46-42 の悪化ではない) | **書いた** (K88 (3))。取り込み前・main・P46 の木で同じ答え。`_`・`@` のそれぞれ 1,452 通りのうち BI 158 通り (NS では `_` の先頭 400 通りのうち 73 通り)。直す案 (確定の直前の打ち間違いの直しの対象を、表示で日本語になる区間に限る) は影響が広いので、全比較をしたうえで別の項目にする |
    | P1 (RL-02) | P46-42 の性質テストが、筆頭例 `.zoverdesu → .zoveです` を見逃す。表示と確定が食い違った 140 通りのうち 42 通り (30%) を捉えない (「出力の英字は打った文字の一部」だけを見るので、`r` が消えただけの崩れは見えない) | **直した** (7f4e6d6。テストだけ)。表示が `.` で始まる (英字に見えている) 1,225 通りは、確定してもその表示のまま、をあわせて見る。P46-42 の前のコードでは 140 通りで赤 (この数は RM が時間切れで照合できなかったので、`3f7ec64` のコードの木に流し直して、1,225・140・旧の性質が捉える 98 と見逃す 42・227 (表示が日本語) がコミット本文と一致することを確かめた) |
    | P1 (RM-01) | P46-47 のテストの穴: `IsPartOfLongerEnglishWord` に変異を 6 つ入れると、4 つ (a・b・d・f) がテスト全部を緑のまま通る | **直した** (e8f06d9。テストだけ)。入力を流して分けた (P46-47 で答えが変わる入力と、語頭から始まる語・記号や数字をはさむ形の自作入力、あわせて 2,848 入力を BI と NS で): 動きが変わるのは **f** (ループの中の長い語の下限 4 を 6 にする) だけで、NS の 208 入力で変わる (`imaamentsuru` → `いまあめんつる` など)。`ament` を模擬の判定器に足し、`imaamentsuru`・`kyouhaamentsuru` の例を足したら、f の木で赤になった (851/852)。**a** (最初の `letters.Length < 4` を外す)・**d** (ループの「英字だけの単位」の条件を外す) は 1 入力も変わらず、読みでも等価なので足さない (a は同じ単位の後ろの本判定が 4 文字以上を要るので判定器を呼ぶ回数が増えるだけ。d は、`IsKnownEnglishWord` が小文字にしてから a-z だけの並びかを見るので、数字・記号をはさんだ並びは以降ずっと知らない語になり、元のコードがそこでループを終えるのと同じ (大文字は元の条件 (`IsAsciiLetter`) も通す。小文字にすると a-z になる U+212A (ケルビン記号) だけは例外で、6 回目の RN の実測では差なし)。d を読みで確かめたのは 6 回目の査読 RN-04 を受けて、理由の書き方を直したのは 7 回目の査読 RP1-03 を受けて)。**b** (`j >= 0` を `j > 0` にする) も、この 2,848 入力では 1 入力も変わらなかったので、P46-48 では等価変異とみなして足さなかったが、等価ではなかった (6 回目の査読 RN-01。c210017 で直した。下の 6 回目の行)。RM が例に挙げた `kyouha2empowermentsuru`・`ima-empowermentsuru` のうち、前者は取り込み前の木でも `きょうは2empowerめんつる` (数字の後ろは P46 の前から同じ)、後者は NS では 5 つの木とも `いまーempowermentする` で崩れない (BI では 5 つの木とも `いまーempowerめんつる` で、木による差が無いのは同じ。7 回目の査読 RP1-11)。どちらも P46-47 の例にならない (P46-48 のコミット本文は後者を `2empowermentsuru` と書いたが、書き換えず、c210017 の本文で訂正した) |
    | P1 (RM-02) | 新しい関数は、長い語 L (`unemployment` など) を判定器の答えだけで受け入れ、main の守り (伸ばす音の う・同梱の一覧にない母音 1 つの単位から始まる語・大文字の略語の境目) を L に掛けない。判定器が偶然に語と答える並び L の後ろに、日本語のつもりの `…ment` `…dent` + su が来ると、t と s が英字のまま残りうる (理論上) | **測って書いた** (コードは変えない)。L に main の守りを掛けると、直したい形 B (その守りが長い語を飛ばすこと自体) をまた飛ばして直らない。実測では悪化 0: 日本語の読み (製品の読みの辞書 `dictionaries/readings.txt` から作った評価用の入力 20,127 語のうち `tsu` を含む 1,567 語) を 4 通りの文脈で流した 6,267 入力 (重複 1 件を除く。NS) で、直す前と直した後の答えが 1 入力も変わらない。RM の 43,606 入力と探索員 4 人の流し直しで答えが変わった入力は、すべて「取り込み前・main・P46 だけの 3 つの木と同じ答えに戻る」直しだった。理論上の穴として「P46 で触れなかった既知の穴」の (7) に書いた |

  - 探索員 4 人の流し直し (P46-47 の前の統合と後の統合に、最初の探索と同じ入力の作り方で。元の入力の一部で、範囲は各報告の通り): **LV** BI 75,795・NS 7,978 入力、崩れ 504 → 0、違いは NS の 504 入力でどれも直し (426 入力は理想の答えとも一致、残り 78 入力は 4 つの木とも元から誤っていた形が他の木と同じ誤りに戻っただけ)。**TSU** BI 124,608 入力 (語幹 4 つ)・NS のべ 8,867 入力、崩れ 180 → 0 (最初の報告の 65 入力のほか、子音で始まる語 `kyouhaprocidentsuru`・短い語 `kyouhaamentsuru` も崩れていた)、違いは NS の 180 入力でどれも直し。**LETTERS** 出力の英字の連続が入力の部分文字列でなくなる入力 (性質 P) は、直す前も直した後も同じ 402 入力 (BI 387・NS 15) で、統合でだけ・P46 からの崩れは 0 (全件が、取り込み前か main の木でも破る前からの崩れ)。BI 52,763 入力は差 0、NS は目標入力 3,240 のうち 326 入力で答えが違い、どれも直し (英字が増えるだけ。減る入力は 0)。**P46WORDS** BI 74,274・NS 12,118 入力、崩れ 119 + 36 → 0、違いは NS の 262 入力 (重複を除く) でどれも直し、表示と確定が食い違う入力 (`kyouhaimplicantsuru`) も直った。**どれも悪化・意図しない変化は 0 件**。実機の判定器 (IME の中の NSSpellChecker) は誰も通していない。

  - P2 11 件 (RL 7・RM 4) の対応:
    - **直した** 4: RL-04 公開 `ClearAll()` が 3 つのログを `ClearAllAt` に渡す配線が、どのテストにも固定されていない (ログを `meltype.log` だけにしても全テストが緑) (bd89649。一覧を `LearningData.DefaultLogFiles` に取り出してテストで固定。ログを 1 つにした変異で赤)。RL-05 「消去の途中の警告は、あとから新しいログに必ず残る」は言い過ぎ (`FlushFile` と削除の間に別のスレッドが積んだ記録は、新しいログに出ることがある) (bd89649。コメントを「この時点で積んである分は」と限った。P46-43 の本文の同じ言い方もこの限定つきで読む)。RL-06 確認ダイアログの文面の細部 (bd89649。内部の言葉の「ユーザーモデル」を外した・「それぞれの古い分」を「回転して残った .old も」にした・Finder で隠れている `~/Library/Application Support/Meltype` を「移動」→「フォルダへ移動…」で開く方法と、保存元が `meltype.log` であることを足した。折り返し・見え方は実機未確認)。RM-06 着地の手順: `gated_commit.txt` が古い (41aeb49) と第 4 引数を省いたとき止まる (安全側) → 全テストを通した先端 (最後は 5ddc046。この値は、回ごとに書き換える) に更新した。ゲートを実際に通した木 (485cb80) は別の記録にして、着地のスクリプトの表示も「ゲートを通した木」と「全テストを通した先端」を分けるようにした (6 回目の査読 RN-02・RO1-14)。今は、全テストを通した先端から着地する枝までの差が文書だけ、ゲートを通した木からの差がテストと文書だけであることを確かめてから進み、既定では確かめるだけで merge しない (7 回目の査読 RP1-07・08)。
    - **確かめた** 2: RL-03 条件の `s == 0` を外した変異がテスト全部を緑のまま通る → 外した木で、先頭以外に `.` を置く 27 形の接頭辞 × 語幹 25 × 語尾 33 ほか BI 24,867 入力・NS 2,880 入力を流して、答えの差が 0。先頭以外に `.` で始まる英字の区間ができる入力は見つからなかったので、等価変異とみなす (コードは足さない)。RM-05 P46-45・P46-46 のコミット本文の数字 (1,225・140・227・42) を RM が時間切れで照合できなかった → `3f7ec64` のコードの木に流し直して一致を確かめた (上の RL-02 の行)。
    - **書いた** 2: RL-07 P46-42 の本文に `.afurka` の確定が変わったことが書かれていない → K88 (3) に書いた (表示は前から `.afurka` の英字、確定は P46-42 の前は `。あふりか`、後は `.afurka`。表示と確定を一致させる設計どおり。コミット本文は書き換えない)。RM-04 P46-47 の本文の例 `そううnemployめんつる` は実機の判定器の答え、テストの模擬の判定器では `そううねmployめんつる` (赤の表示) で、どちらも崩れの例として正しいが同じ文字列ではない → 文書の例は実機の判定器の答え (NS) の側で書き、BI の答えは `そううねmployめんつる` と区別した。
    - **直さない・対応なし** 3: RL-08 順序のテストの強度は Windows では落ちる (開いているログを消せないので、最悪の場合を作らない。本文が明記している意図)。RL-09 `ClearLeftovers` の `Directory.EnumerateFiles` が try の外 (P46 の前から。P46-43 の新しい順序ではログ削除と学習データの消去のあとなので、悪化ではない)。RM-03 長い語を前へ伸ばすループが毎回判定器を呼ぶ (入るのは t + s の打鍵で、断片が知っている語かつ「かな + つ」が読みの頭のときだけ。1 打鍵あたり多くて数十回。コメントの提案のみ)。
  - **6 回目** (5 回目の対応のあとに増えた P46-48 と着地の手順 (RN)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の数字と主張 (RO1)、KNOWN_LIMITS・追従表・WATCH・README/INSTALL・EXPERIMENT_2-5・コミット本文 (RO2)): **RN: P0 0・P1 1・P2 3、RO1: P0 1・P1 2・P2 14、RO2: P0 1・P1 3・P2 10** (合わせて P0 2・P1 6・P2 27。ただし RO1-01 の P0 は確かめると誤検知だった (下の表)。それを除くと P0 1)。P0 と P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | **P0 (RO2-01)** | K88 (3) が、先頭が `_` の入力の変換ボックスの表示を `_zoverです` と書いた。実測は `_zoverdesu` (全部英字) で、確定のとき打った英字 r が消えて `_zovedesu` になる (`.zoverdesu` の表示が `.zoverです` なのを、`_` の例にそのまま写していた) | **直した** (K88 (3) と 5 回目の表の RL-01 の行)。取り込み前・main・P46 だけ・P46-42 の前・統合の 5 つの木で、BI・NS とも表示は打ったまま (`_zoverdesu` → `_zoverdesu`・`@zoverdesu` → `@zoverdesu`・`_kyoumolinks` → `_kyoumolinks`。確定は `_zovedesu`・`@zovedesu`・`_kyoumolinka`。14 回目の注 (14 回目の査読 RX4-01): 6 回目に書いた文は「表示は `_zoverdesu`・`@zoverdesu`・`_kyoumolinks` のまま」で、13 回目に、値を書いた今の形に書き換えた (言っていることは同じ。13 回目の箇条の「実装者が自分で見つけたもの」の (1)))。症状の本質 (表示と確定が食い違う) は変わらない。`verify_docs_examples.py` に表示の確認 (8 行) を足して、書き方が実測とずれたら気づけるようにした (7 回目の査読 RP2-08 を受けて、5 つの木 × BI/NS に広げた) |
    | **P0 (RO1-01)** | 「文書の例 67 件 = 44 入力を BI 31・NS 36」がどのログとも合わない (ログの OK 行は 59 件、74 件) | **誤検知** (数は合っていた)。文書の例の確認 (`verify_docs_examples.py`) は、ゲートの実行 (18:33) では OK 59 行 (文書の例 54 件と補助 5 行)。そのあと例を 13 件・補助を 2 行足して、同じコードの木で流し直した版 (19:24) が OK 74 行で、そのうち 67 行が例 (`先端=` の行。BI 31・NS 36、44 入力。うち 4 件 (3 入力) は文書に書いていない確認用の入力)、残る 7 行は P46-42 の前の木での答えを確かめる補助の行 (最初はゲートの実行の行数と取り違えて書いていた。7 回目の査読 RP2-02・03 で直した)。ただしログに集計が出ていなかったので追えなかった。`verify_docs_examples.py` が「集計:」行を出すようにし、文書の件数はその行から機械で取るようにした。文書には補助の 7 行のことも書いた |
    | P1 (RN-01) | P46-48 が「等価変異」とした b (`IsPartOfLongerEnglishWord` のループの `j >= 0` を `j > 0` にする) は等価ではなかった。`ReadsAsJapaneseWithTsu(0, …)` は先頭の単位 (i == 0) でも掛かるので、全体も「かな + つ」の読みの頭に見える語が先頭の単位から始まるときは、先頭の単位まで伸ばさないと全体が日本語に読まれる。判定器が `cadent` (ca + dent) を単語と答えると、`cadentsuru` が元のコードで `cadentする`、b の木で `かでんつる` (対照の `kyouhacadentsuru` は b でも緑) | **直した** (c210017。テストと注だけ)。`cadent` を単語と答える試験用の判定器 (論理の確認用。実機のまねではない) の例を P46-47 のテストに足し、b の木で赤 (851/852)、現在のコードで緑 (852/852) を確かめた。今の判定器で起きるか: 開発機で NSSpellChecker を直接呼んだ答え (`tools/nsd`) も同梱の英単語表も `cadent` を単語と答えない。全体も「かな + つ」の読みの頭になる t 終わりの語を、同梱の英単語表と `/usr/share/dict/web2` で探すと、「先頭の単位 + 4 文字以上の断片」の形になるのは `cadent` だけだった (RN の調べ。この 2 つの一覧の外は探していない)。利用者が `cadent` のような語を英字の語として学習した場合は、変異 b の木で同じ崩れが起こりうる (推測)。IME の中の NSSpellChecker での動きは実機未確認 (言い方は 7 回目の査読 RP2-01 で直した)。P46-48 の 2,848 入力で b が 0 だったのは、変わる形がその入力の集合に無かっただけで、変えないことの証明ではなかった (RN の計装では、b が戻り値を変える呼び出しは BI 8 種・NS 26 種あったが、日本語側の条件も真の呼び出しは 0)。a・d は読みでも等価 (d の理由は 5 回目の表の RM-01 の行。書き方は 7 回目の査読 RP1-03 で直した。P46-48 は「測っただけ」と書いた)。RN の再実行 (BI 6,825 入力 × a・b・d・f の 4 つの木、NS 1,848 入力 × a・b・d の 3 つの木) でも、いずれも現在のコードと差 0。P46-48 のコミット本文は書き換えず、c210017 の本文で訂正した |
    | P1 (RO1-02) | P33 の訂正の一文が、日時の候補の読みを 8 つ (いま・なう・きょう・あした・あす・きのう・ことし・いまなんじ) と書いた。実装の読みは 3 つ (`NowReadings` = いま・なう と、きょう) | **直した** (3 つに。結論 (ぶつかる場面は今のところ無い) は 3 つでも成り立つ。絵文字の辞書側は、3 つとも行頭一致の行が 0 のまま) |
    | P1 (RO1-03) | 「のべ約 44 万入力を流して全数に近く探し」は過大。各報告は「流し切っていない」「時間切れ」と書いている (LV は BI の全組み合わせ 287,625 のうち 75,795、TSU は NS の同梱語の全数と BI の語幹 8 つが時間切れ、LETTERS は乱数の入力で「全数ではない」、P46WORDS は N3a の 6,000 入力が時間切れで、総当たりは a/e/i/o 始まりの語 + suru に限った) | **直した** (「全数ではない。流した範囲は下の「未確認」(11)」。(11) に各報告の打ち切りを書いたのは 7 回目の査読 RP2-04 を受けて)。のべの入力数は、重複もあるので網羅の根拠にならない |
    | P1 (RO2-02) | K88 (3) が LETTERS の探索 (約 13.6 万入力、BI 418・NS 320) を「`_` `@` `,` を英単語の前後に打つ入力の探索」「同じ型の崩れ」と書いた。実際の探索は区切り記号・大文字・長い入力・日本語 + 英語・l x t で終わる語などを含み、数は「出力の英字の連続が入力の部分文字列でなくなる入力」。LETTERS は原因を未確認と書いている | **直した** (探索の範囲・数の意味・`_zoverdesu` と同じ型かは未確認であることを書いた。共通点が「区切り記号 `_` `@` `,` が入ること」なのは LETTERS の見立て) |
    | P1 (RO2-03) | 追従表の P46-47 の行が「取り込み前・main・P46 だけでは起きず、統合した木だけで起きる」と書いたが、これは NS だけ。BI では形 A が main の時点で崩れる (`souunemploymentsuru` → `そううねmployめんつる`。K87) | **直した** (NS の限定と、BI の形 A のことを書いた) |
    | P1 (RO2-04) | K88 (3) の `.` + 英単語 800 入力「別の 2 標本では 771 と 766」は統合の前の木の値。統合した木 (ゲートの測定) は、乱数の種 9 が 773、種 10 が 768 (BI・NS とも同じ) | **直した** (773 入力 (96.6%) と 768 入力 (96.0%)。悪化 0)。BI は揺れないので、これは木の違いによる実際の差 |

  - P2 27 件 (RN 3・RO1 14・RO2 10) の対応:
    - **直した** 26:
      - RN-02 着地のスクリプトが、全テストを通した先端を「ゲートを通した」と表示していた → ゲートを実際に通した木 (485cb80) と全テストを通した先端 (c210017) を分けて持ち、両者でテスト以外のコードの木が同じことをスクリプトが確かめて表示するようにした (手順。リポジトリの外)。RN-03 `NsLikeChecker` の注の「577 入力で 5.5%」は 7 語のときの値 → 注に「7 語のときに測った値。語を足したあとは測り直していない」と書いた (c210017)。RN-04 d は読みでも等価 → テストの注と本文に書いた (c210017)。
      - RO1-04 形 B を見つけたのは 3 人 (LV・TSU・P46WORDS)。RO1-05 崩れる語尾は `su` で始まるもの (`su`・`suki`・`suru`・`surudake`)。RO1-06 形 A の前は o・u で終わる日本語 (`kono` も)。RO1-07 RM の例は `kyouha2empowermentsuru`・`ima-empowermentsuru` で、後者は崩れない。RO1-08 `torpedoes` の NS の食い違いは流し直しで再現しなかったことと、同じ実行でもう 1 入力の分類が変わったことを書いた。RO1-09 LV の 78 入力は元の誤りに戻っただけで、正しくなったのではない。RO1-10 「読みの辞書 20,127 語」の呼び名と、6,267 が重複 1 件を除いた数であることを書いた (このときの呼び名は誤りで、7 回目の査読 RP2-05 で直した: 製品の読みの辞書 `dictionaries/readings.txt` から作った評価用の入力)。RO1-11 先頭が `_`・`@` の 1,452 通りは「それぞれ」。RO1-12 崩れの数 119・36 と、無害な差 2。RO1-13 流し直しは元の入力の一部。RO1-14 着地の比較対象の説明 (上の RN-02 と同じ)。RO1-15 「調査の査読 6 回 (RV1〜RV6)」。RO1-16・RO1-17 UPSTREAM_REVIEW に、791 が別の数であること・赤にならない守りのテストがあることを一言足した。
      - RO2-05 NS の幅を広げた (下端は、7 回目の査読 RP3-04 で、残っている記録に合わせて 2,996 にした)。RO2-06 追従表に 5 回目の P1 5 の内訳。RO2-07 追従表に RL-01・RL-07 を直さず K88 (3) に書いたこと。RO2-08 #636 の対象外の理由を、Mac の IME が受けるイベントから書いた (入口の説明が不完全だったので、7 回目の査読 RP3-02 で直した。今の理由は追従表の対象外の行)。RO2-09 #551 の中身 (本家の差分は `.broken` のコピー失敗を警告するだけの変更。本家との関係の書き方は、7 回目の査読 RP3-06 で直した)。RO2-11 README の消去の一覧から、確認ダイアログにない「ユーザーモデル」を外した (同じ理由で「予測」も、7 回目の査読 RP3-09 で外した)。RO2-12 EXPERIMENT_2-5 の参照を `LearningData.DefaultLogFiles` と `clearLearningData` の名前に。RO2-13 K88 (5) の `ほめる？` を偽の変換エンジンで測り直した (`homeru?`・`homeru??`・`homeru!` は変換エンジンに 0 回、`homeruyo`・`homeru?yo` は読み (`ほめるよ`・`ほめる？よ`) が渡る。一時のテストで、コミットしていない)。RO2-14 追従表に #622 を足した (#622 は #424 を直す PR。7 回目の査読 RP3-05 で、GitHub の画面で確かめて書き直した) (19 回目の注 (19 回目の査読 SC4-05): 7 回目の箇条の RP3-05 の行への 19 回目の注のとおり、画面で確かめた記録は無く、2026-10-17 に GitHub の API で確かめた) (20 回目の注 (20 回目の査読 SD4-01): 7 回目の箇条の RP3-05 の行への 20 回目の注のとおり、2026-10-16 の朝の精査にも、gh と GitHub の API で取った一覧がある)。
    - **書いた** 1: RO2-10 UPSTREAM_WATCH の「分類」の列は、`tools/upstream_watch.py` の `changed_files` が merge の第 2 親との差も数えるので、実際の PR より広く出る (本家の clone で数えると、#534 は実際 2 ファイルなのに 116、#518 は 1 ファイルなのに 118)。ツールの直しは別の項目にして、WATCH の表の下に注を足した。注の「当たるか」の列の説明と、指摘の後半 (「取り込み」の列の「記録あり」の書き方の揃え) は、7 回目の査読 RP3-01・RP2-06 で直した。
  - **7 回目** (6 回目の対応のあとに増えた P46-49・着地の手順・件数を数える道具 (RP1)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の 6 回目の対応 (RP2)、KNOWN_LIMITS・追従表・WATCH・COMPARISON・EXPERIMENT_2-5・README/INSTALL・コミット本文の 6 回目の対応 (RP3)): **RP1: P0 0・P1 1・P2 10、RP2: P0 0・P1 1・P2 8、RP3: P0 0・P1 2・P2 10** (合わせて P0 0・P1 4・P2 28)。RP1 は、長い語 (BI 3,360・NS 500) と、実装者・6 回目の査読員とは別の作り方の入力 (BI は大文字・数字や記号をはさむ形・短い断片・非常に長い列・深い語の 5 種で 4,292、NS は大文字 500・深い語 168 の 668) の、合わせて BI 7,652 入力・NS 1,168 入力と、自分で考えた変異 25 個を流した (入力の内訳の書き方は 8 回目の査読 RQ2-02 と 9 回目の RR2-05 で直した)。a・d の等価は崩れず (BI 7,652 入力・NS 1,000 入力で差 0)、緑のまま答えが変わったのは、下の up1・dep6・dep7 の 3 つだけだった (どれも c5737c3 で赤にした)。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RP1-01) | P46-47 のテストに大文字で始まる語の例が無く、`IsPartOfLongerEnglishWord` のループの「英字だけの単位」の条件を小文字だけ (`char.IsAsciiLetterLower`) にする変異 (up1) が、全テストを緑のまま通る。NS では査読員の 500 入力中 39 入力の答えが変わる (`kyouhaEntitlementsuru` → `きょうはEntitleめんつる`)。BI では 584 入力中 0 | **直した** (c5737c3。テストだけ)。`kyouhaEntitlementsuru` → `きょうはEntitlementする`・`imaEntitlementsu` → `いまEntitlementす` の例を足し、up1 の木で赤 (851/852)、現在のコードで緑 (852/852) を確かめた |
    | P1 (RP2-01) | 6 回目の表の RN-01 の行の「実機では起きない形」が、RN の報告より強い言い切りだった (確かめた範囲・利用者が学習した語の経路・IME の中は実機未確認、が落ちていた) | **直した** (6 回目の表の RN-01 の行)。開発機で NSSpellChecker を直接呼んだ答え (`tools/nsd`) と同梱の英単語表の話であること、「`cadent` だけ」は同梱の英単語表と `/usr/share/dict/web2` の範囲で探した結果であること、利用者が学習した語の経路 (推測)、IME の中は実機未確認であることを書いた。テストの注も c5737c3 で同じ言い方に直した |
    | P1 (RP3-01) | WATCH の注の「『当たるか』の列は第 1 親との差で見るので影響を受けない」は、#518・#480 の 2 行で偽。ツールは、分類が「判定・変換」のときだけ「当たるか」を埋めるので、広く出た分類のせいで「対象ファイルなし」が出ている | **直した** (WATCH の注と、6 回目の表の RO2-10 の行)。第 1 親との差だけで分類すると、#518 は Linux、#480 は本家 Mac で、この列は「-」になる。ほかの 10 行は値が同じ。`tools/upstream_watch.py` の `watch()` が、分類が「判定・変換」のときだけ `fits_as_is` を呼ぶことを読み、12 行を両方の分類で数え直して確かめた (査読員の道具を流し直した) |
    | P1 (RP3-02) | 追従表の #636 (対象外) の理由「Shift 単独の押下・リピートは Core に届かない」は入口の説明が不完全。Core には、変換中に Shift + キーを打ったとき `MeltypeSession` が作る LShift の押下と解放が届き、#636 が変えた行を通る | **直した** (追従表の対象外の行と、6 回目の表の RO2-08)。結論 (Mac では動きが変わらない) は同じ: その押下と解放は 1 回の呼び出しの中で作られ、リピートは起きない (変換の始まりと終わりでは片方だけになることは、8 回目の査読 RQ3-06 で書き足した)。#636 を当てると、解放はアプリへの送り直し (`Replay`) に回るが、Mac の `Replay` は修飾キーやキーを離したことには何もしない。`MeltypeSession.cs` (LShift を送る所と `Replay`)、`CompositionController.cs` の `_capturedDown.Add`、本家の差分 (`bee078c`) を読んで確かめた。#636 を当てた木は動かしていない。実機は未確認 |

  - P2 28 件 (RP1 10・RP2 8・RP3 10) の対応 (すべて直した。うち RP1-04〜10 の 7 件は、リポジトリの外の手順の直し):
    - **テストと注** (c5737c3): RP1-02 前へ伸ばす深さに上限 (6・7 単位) を入れる変異 (dep6・dep7) が緑のまま通る → 長い語の頭まで 8 単位さかのぼる語の例 (`imaunderemploymentsuru` → `いまunderemploymentする`。模擬の判定器に `underemployment` を足した。開発機の NSSpellChecker は単語と答え、同梱の英単語表には無い) を足し、dep6・dep7 の木で赤 (851/852) を確かめた。RP1-03 変異 d が等価な理由の書き方 (`IsKnownEnglishWord` は小文字にしてから a-z だけの並びかを見る。大文字は元の条件も通す。小文字にすると a-z になる U+212A だけは例外) を、テストの注と、5 回目の表の RM-01 の行・6 回目の表の RN-01 の行で直した。RP1-11 「`ima-empowermentsuru` は 5 つの木とも `いまーempowermentする`」は NS のとき (BI は 5 つの木とも `いまーempowerめんつる`) → 5 回目の表を直し、P46-49 の本文は書き換えずに c5737c3 の本文で訂正した。
    - **着地と作り直しの手順** (リポジトリの外): RP1-04 統合を作り直す手順が、テストの成否を FAIL の行の数だけで見ていた → 最後の行が「N/N passed」で 2 つの N が同じこと・FAIL の行が 0・コンパイル警告が 0 を見るようにした。RP1-05 並行して流すと main 由来のテスト `LearnedWordsTests.TwoInstances_ConcurrentEdits_LoseNothing` が負荷で赤になることがある (8 並列で 1/8) → 統合のコミットだけの木のテストも含めて全テストを 1 本ずつ流し、そのテストだけが赤のときは 1 回だけ流し直すようにした (テスト自体は main 由来で、P46 のコードとは関係ない)。RP1-06 作り直す手順の冒頭の説明が古い → 段の一覧・作り直すとコミットの hash が変わること・今の枝の残し方・試しに流す方法を書いた。試しに流して、今の統合した木と同じ木 (コミット本文も同じ) になることを確かめた。RP1-07 着地のスクリプトの既定が「merge する」 → 既定を確かめるだけにし、`LAND=1` を付けたときだけ merge するようにした。RP1-08 確かめるパスの範囲が狭い → 全テストを通した先端から着地する枝までの差が文書 (`docs/`・`mac/README.md`・`mac/INSTALL.txt`) だけ、ゲートを通した木からの差がテストと文書だけであることを、許可する一覧で確かめるようにした。RP1-09 止まるときの文言が原因と違う所を指す → 2 つの確かめ方で文言を分けた。想定と違う引数を 5 通り (main・枝の先端・第 4 引数 2 通り・ゲートの木) 渡して、どれも止まることと、正しい引数でも既定では merge しないことを確かめた。RP1-10 件数を数える道具が、出所の分からないログを読んでいた → 統合した木の複製の Core と辞書が final/p46 と同じことを確かめ、検証を流し直してから数えるようにした (検証の既定の木も統合した木にした)。
    - **IMPROVEMENT_PLAN と UPSTREAM_REVIEW**: RP2-02 6 回目の表の RO1-01 の行の「ゲートで流した … OK 行 74」は誤り (ゲートの実行は OK 59 行で、74 行は、そのあと例を 13 件足して同じコードの木で流し直した版) → 直した。「ゲートを通した木」の段落にも、文書の例はゲートのあと流し直したことを書いた。RP2-03 「文書の例 67 件」のうち 4 件 (3 入力) は文書に書いていない確認用の入力 → 「文書の例と確認用の入力 67 件。うち 4 件 (3 入力) は文書に書いていない確認用」と書き、この数も機械で数えて、作り直した文書で確かめ直すようにした。RP2-04 RO1-03 の対応が指す「未確認」(11) に、探索員の打ち切りが書かれていない → 4 人の打ち切り (LV は BI の全組み合わせ 287,625 のうち 75,795、TSU は NS の同梱の t で終わる 2,596 語の全数と BI の語幹 8 つが時間切れ、LETTERS は乱数の入力、P46WORDS は N3a の 6,000 入力が時間切れ) を (11) に足した。RP2-05 「製品の辞書ファイルではない」は誤り (評価用の入力は、製品の読みの辞書 `dictionaries/readings.txt` (データ 20,181 行) の読みのうち 2 文字以上の 20,127 語 (重複を除く) をローマ字にしたもの。`tsu` を含むのは 1,567 語) → 呼び名を直した (流して数を確かめた)。RP2-06 RO2-10 の指摘の後半 (WATCH の「取り込み」の列で、#527 だけに「。対象外」が付いている) への対応が無い → 「記録あり」の 10 行すべてに、追従表での扱い (対象外 7 行・見送り 3 行) を付けて揃えた。RP2-07 UPSTREAM_REVIEW の P46-49 の行が RN-01 だけ → RN-01・03・04 にし、P46-50 の行を足した。RP2-08 6 回目の RO2-01 の「5 つの木」の測定の出力が残っていない → 検証スクリプトの表示の確認を 5 つの木 (取り込み前・main・P46 だけ・P46-42 の前・統合) × BI/NS に広げ、件数を数えるたびに流すようにした。RP2-09 5 回目の表の形 A・B の行の「BI では起きない」は、形 A について誤り (BI では main の時点で崩れる) → 直した。
    - **KNOWN_LIMITS・追従表・WATCH・README/INSTALL・コミット本文**: RP3-03 K88 (3) の LETTERS の「共通点は区切り記号 `_` `@` `,`」は全部には当たらない (入るのは BI 418 件中 370 件・NS 320 件中 315 件) → 「多くは」とし、入らないもの (大文字を含む入力など) もあること、BI 324 件・NS 287 件は上の 1,452 通りと同じ語幹と語尾に先頭の記号を付けた入力 (上の 158 通りに足し合わせる別の件数ではない) であることを書いた (査読員の数え方の道具を流し直して同じ数を得た)。RP3-04 K88 (2) の NS の幅の下端 2,995 を裏づける記録が無い → 残っている記録 (`logs/` の 3 回。NS で 2,996・2,998・2,998) に合わせて 2,996〜2,998 にし、「残っている記録の範囲」と添えた。RP3-05 「#424 (#622 と同じ作業)」は本家の clone では確かめられない → GitHub の PR の画面 (2026-10-16) で、#622 は #424 を直す PR で本家では merge されていないことを確かめ、そう書いた (19 回目の注 (19 回目の査読 SC4-05): この日に #622 を確かめた記録は、作業のファイルに無い。#622 が #424 を直す PR で、本家で merge されていない (開いたまま) ことは、2026-10-17 に GitHub の API で確かめた) (20 回目の注 (20 回目の査読 SD4-01): 取り込みの作業のフォルダーの外の、2026-10-16 の朝の精査のフォルダーに、gh と GitHub の API で取った PR の一覧があり、#622 は開いたまま・merge なし・本文に Fixes #424 だった。無いのは、GitHub の PR の画面で確かめた記録)。RP3-06 #551 の説明 (本家との関係) → 追従表と WATCH の #551 の行を、`.broken` に残す分と、残せなかったときに警告を出す分に分けて書いた (このとき「残す分は #551 の前に #560 で本家に入っていた」と書いたのは帰属の誤りで、8 回目の査読 RQ3-01 で直した: その `942e4a8` は #551 の枝の 1 つ目のコミット。#560 のほかの PR が Mac のどの調べにも入っていないことは、8 回目の RQ3-02 で書いた)。RP3-07 #636 の「重ねて数えない直し」は不正確 (`_capturedDown` は集合で、重ねて数えることは起きない) → RP3-02 と一緒に書き直した (このときの `_capturedDown` の呼び方は、8 回目の査読 RQ3-07 で、コードのコメントの言い方に直した)。RP3-08 WATCH の注の「実際は」の列挙が 4 行だけ → 広く出ている 8 行すべての数 (重複を除いたファイル数で、ツールの数 → 第 1 親との差) を書いた。RP3-09 README の消去の一覧の「予測」は確認ダイアログに無い → ダイアログに合わせて外した (INSTALL の「変換・予測の学習など」も「変換の学習など」に。予測が azooKey の学習に含まれるかは確かめていない)。RP3-10 文書のコミット本文の「ユーザーモデル」を外した直しは README だけ (INSTALL には元から無い) → 新しい文書のコミット本文で書き分けた。RP3-11 K88 (5) の「4 文字目 (のかな)」は狭い (数字・英字も数える)。「本家 #632 と同じ」は本家の main の動きに読めるが、PR #632 は閉じられていて本家の main に入っておらず、本家の main (`45e14f8`) は記号も数える (17 回目の注 (17 回目の査読 SA5-01): #632 は閉じておらず、開いたまま。2026-10-17 に GitHub の API で確かめた (merge されていない)) → 「読みの 4 文字目 (かな・ー・数字・英字。記号は数えない)」「本家の PR #632 の案と同じ動き」に直し、本家の main との違いを書いた (PR の状態は 2026-10-16 に GitHub で、コードは本家の clone で確かめた (18 回目の注 (18 回目の査読 SB4-01・SB2-07・SB5-01): この日に PR の状態を確かめた記録は無い。状態は 2026-10-17 に GitHub の API で確かめた (開いたまま、merge されていない)) (20 回目の注 (20 回目の査読 SD4-01): 2026-10-16 の朝の精査の一覧 (gh と GitHub の API) にも、#632 は開いたまま (merge なし) とある)。#632 の案はかなだけを数え、Mac の P46-19 は数字・英字も数える。理由の列の直し漏れは、8 回目の査読 RQ2-01・RQ3-04 で直した。「同じ動き」は、8 回目の査読 RQ3-05 で「記号を数えない点は同じ」に直した)。RP3-12 K88 (3) の「`.zoverdesu` の表示は `.zoverです`」は、P46 の木の P46-42 の前の話 (取り込み前と main では表示も `。` で始まる日本語寄り) → 5 つの木 × BI/NS で表示と確定を測り直し、そう添えた (BI では取り込み前と main でも表示と確定が食い違っていたことは、8 回目の査読 RQ3-08 で書き足した)。
  - **8 回目** (7 回目の対応のあとに増えた P46-50・着地と作り直しの手順・件数を数える道具 (RQ1)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の 7 回目の対応 (RQ2)、KNOWN_LIMITS・追従表・WATCH・README/INSTALL・コミット本文の 7 回目の対応 (RQ3)): **RQ1: P0 0・P1 0・P2 9、RQ2: P0 0・P1 1・P2 4、RQ3: P0 0・P1 4・P2 6** (合わせて P0 0・P1 5・P2 19。RQ2-01 と RQ3-04 は同じ指摘で、重複を除くと P1 4)。RQ1 は、変異 up1・dep6・dep7 が c5737c3 のテストで赤になることを自分の複製で確かめたうえで、自分で考えた変異を流し、5 つが緑のまま通ること (うち 3 つは NS で答えが変わる。下の RQ1-01・02) を見つけた。RQ2 は、7 回目の 32 件が対応表で 1 件ずつ過不足なく扱われていることを確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RQ2-01・RQ3-04) | K88 の理由の列に「(5) は本家と同じ」が残り、同じ行の (5) の説明 (本家の main は記号も数える) と食い違う。7 回目の RP3-11 の直し漏れ | **直した** (K88 の理由の列)。「(5) は、記号を数えない点で本家の PR #632 の案と同じ (本家の main とは違う)」にした。作り直した文書で古い言い方を数える確かめ (`docs_check.py`) の一覧にも、この言い方を足した |
    | P1 (RQ3-01) | 追従表の P46-17 の行と WATCH の #551 の行の「本家は #551 の前 (#560 で入った `942e4a8`) から `.broken` を残していた」は帰属の誤り。`942e4a8` は #551 の枝の 1 つ目のコミットで、10-13 に統合 PR #560 の中の `Merge #551` (`b173edf`) で先に main に入った。10-14 の #551 の merge (`f881053`) で入ったのは 2 つ目 (`67ee7a1`、残せなかったときの警告) だけ | **直した** (追従表の P46-17 の行、WATCH の #551 の行、7 回目の対応の RP3-06 の注)。本家の clone で、`b173edf` の親が `efa34e9` と `942e4a8` であること、`942e4a8` が `67ee7a1` の祖先であること、`f881053` の第 2 親の側のコミットが `67ee7a1` だけで、第 1 親との差が 6 ファイルであることを確かめた。P46-17 は、この 2 つを `conversions.json`・`translations.json` に手で移植したもの |
    | P1 (RQ3-02) | 本家 #560 (10-13) は、#547〜#584 をまとめて main に入れた統合 PR で、#551 以外の PR は Mac の文書のどこにも記録が無い。追従表の「(Mac の文書に #560 の記録は見当たらない)」は、残りの PR を誰も見ていないことを伝えない | **書いた** (計画書の「P46 で触れなかった既知の穴」(9)、追従表の P46-17 の行、UPSTREAM_REVIEW の §0)。本家の clone で数え直した: #560 (`9038438`) は 38 回の merge で、PR は 37 (#575 が 2 回)。#551 を除く 36 の PR が未精査で、Core (テスト以外) に触るのは 15。中のコミットはどれも 10-13 の 0 時台〜1 時台のもので、Mac の PR の調査 (10-11) より後、ウォッチの起点 (`a4b6f1a`、10-13 13:01) より前なので、どの調べにも入らなかった。main の全ファイルを番号で探して、#547〜#584 の記録は 0。精査は別の項目にする (リョウさんの判断待ち。9 回目の査読 RR2-01 で、#560 だけでなく、この時期に入った PR の全体を対象に書き直した) |
    | P1 (RQ3-03) | #551 は「取り込み済み」とあるが、本家 #551 の 6 ファイルのうち、Mac が使う `config.json` (`Settings.cs`) の「`.broken` に残せなかったときの警告」は取り込んでいない。それがどこにも書かれていない | **書いた** (追従表の P46-17 の行、K89 (4)、計画書の「P46 で触れなかった既知の穴」(8))。統合した木で 6 ファイルを 1 つずつ確かめた: `conversions.json`・`translations.json` は P46-17 で警告まで出す、`languages.json` は元から「残せなければ保存しない」(保存の失敗として警告が出る)、定型文 (Snippets) は Mac に無い、`model.json` (UserModel) は Mac では作らない、`config.json` は `.broken` へのコピーの失敗を何も出さずに捨てる (P46 の前から)。移植するかは別の項目にする (リョウさんの判断待ち) |

  - P2 19 件 (RQ1 9・RQ2 4・RQ3 6) の対応 (すべて直した。うち RQ1-03〜09 と RQ2-04 の 8 件は、リポジトリの外の手順の直し):
    - **テストと注** (aed8e66): RQ1-02 前へ伸ばす長さに文字数の上限 (15 文字) を入れる変異 (len15) が緑のまま通る → 16 文字の語の例 (`imaunderachievementsuru` → `いまunderachievementする`・`kyouhaunderachievementsu` → `きょうはunderachievementす`。模擬の判定器に `underachievement` を足した。開発機の NSSpellChecker は単語と答え、同梱の英単語表には無い) を足した。RQ1-01 「英字だけの単位」の条件を、小文字だけか頭だけ大文字の単位にする変異 (up2)・大文字で始まる単位で止める変異 (up3) が緑のまま通る (語の途中に大文字をはさむ語) → `kyouhaempOwermentsuru`・`imaemPowermentsuru` の例を足した。この 2 つは、今の答え (`きょうはえmpOwermentする` など) の前半 (え) が別の規則の答えで、取り込み前の木から同じなので、期待値に入れず、確定が `mentする` で終わることだけを確かめる補助 (`ExpectCommittedEndsWith`) で見る。4 つの例とも、開発機の NSSpellChecker で 5 つの木に流し、今のコードの答えが模擬の判定器と同じことを確かめた (NS では、取り込み前・main・P46 だけの木も今の統合と同じ答えで、P46-47 の前の統合だけが `…めんつる`・`…めんつ` に崩れる (`su` で終わる `kyouhaunderachievementsu` は `…めんつ`。10 回目に、実装者が文書の例を照合して直した)。統合でだけ起きる崩れの仲間)。変異の木 11 本 (up1・up2・up3・len15・dep6・dep7・dep8・b・f・a・d。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、up1・up2・up3・len15・dep6・dep7・b・f は赤 (どれも 851/852 で、赤は P46-47 のテストだけ。新しく足した例では、up2 が `きょうはえmpOwerめんつる`、up3 が `いまえmPowerめんつる`、len15 が `いまうんでらちえゔぇめんつる`)、a・d は緑 (852/852。等価)、dep8 (深さ 8 単位の上限) は緑 (852/852。9 単位以上さかのぼらないと届かない語は見つけていないので、テストで捕まえていない。注に書いた)。変異なしの木は 852/852・警告 0。RQ2-05 テストの注の「10 語だけまねた判定器」 → 語の数を書かない言い方に直した (語を足すたびにずれるため)。RQ2-02 テストの注の入力の列挙に「長い語」が無い → 長い語 (BI 3,360・NS 500) と別の作り方の入力 (BI 4,292・NS 668) に分けて書いた (7 回目の節の冒頭も同じに直した。査読員の入力の作り方を流し直して数えた。NS の 668 が 5 種の入力に読める書き方は、9 回目の査読 RR2-05 で直した)。
    - **着地と作り直しの手順** (リポジトリの外): RQ1-03 作り直す手順が、テストが 0 本でも本数が減っても緑とみなす → 段ごとに本数を確かめる (P46-41・P46-47 は前の段より 1 本多い、P46-48〜P46-51 は同じ、0 本は止める) ようにした。RQ1-04 統合のコミットだけの木のビルドが失敗すると、前に流したときの古いログを読む → 流す前にログを消し、ビルドの失敗で止まるようにした。RQ1-05 流し直す条件がテストのクラス全体で、流し直したことが残らない → 負荷で落ちると分かっている 1 本 (`TwoInstances_ConcurrentEdits_LoseNothing`) だけが赤のときに限り、流し直した段を最後に表示するようにした。RQ1-06 消す場所 (作業ツリー・統合のコミットだけの木の複製) を確かめない・枝の名前の大文字小文字・冒頭の説明 → 守りを許可の一覧にした: 作り直してよい枝は `final/p46` と `tmp/p46-…` だけ (大文字小文字まで同じもの)、作業ツリーは作業フォルダーの直下の `wt-…` の 1 段で、すでにあるなら作り直す枝の作業ツリーのときだけ、複製は作業フォルダーの直下の 1 段で名前に mergeonly を含む場所だけを消す。`final/p46` は消す前に `backup/` に自動で残す。冒頭の説明も中身に合わせた。main の hash をわざと違えて流し (守りを抜けた指定も、そのあとの main の確かめで止まるので、何も消さない)、想定と違う指定 36 通り (枝 17・作業ツリー 11・複製 8) がどれも守りで止まり、正しい 2 通り (既定・試し) だけが守りを通ることを確かめた (前と後で枝と worktree の一覧が同じ)。はじめに直した形 (禁止の一覧) は、同じ流し方で、複製の `…/mergeonly/../..` (消す先が作業フォルダーの親になる)・未 push の `exp/…` の枝・作業ツリーの `wt-final/../wt` と `wt-final/` の 4 通りが守りを抜けていた (8 回目の対応のあと、自分で気づいて書き直した)。RQ1-07 着地のスクリプトがテストとして許す範囲に、ゲートの評価を流す `Program.cs` まで入る → `src/Meltype.Core.Tests/` の直下の `*Tests.cs` だけにした。RQ1-08 件数を数える道具の確かめが、比べるフォルダー 3 つと表示の行の合計だけ → 6 つのフォルダー (`src/Meltype.Core`・`src/Meltype.Mac.Native`・`mac`・`tools`・`dictionaries`・`eval`) を比べ、測る複製のビルドが源より新しいことと、表示の行が木ごと・BI/NS ごとに 3 入力ずつあることも確かめるようにした。RQ1-09 古い言い方を全体の回数で数えていた → ファイルごとに数え、引用してよいのは、その言い方を直した回の対応の箇条の中だけにした。RQ2-04 文書の機械検査は、コミットしたあとの作業ツリーで流すと何も検査せず、hash の検査は落ちない作りだった → 検査する文書を基準のコミットとの差から選び (空なら止まる)、足した行の hash (数字と a〜f を両方含む 7 桁) が Mac か本家の clone にあることを確かめるようにした (76cd573 の文書で試して、9 ファイル・hash 269 か所で NG 0。数字だけの hash なども確かめるように広げたのは、9 回目の査読 RR2-06)。
    - **IMPROVEMENT_PLAN と UPSTREAM_REVIEW**: RQ2-02 7 回目の節の冒頭の入力の列挙 (上と同じ)。RQ2-03 7 回目の節の見出しで、RP3 の範囲から COMPARISON・EXPERIMENT_2-5 が抜けている → 足した。
    - **KNOWN_LIMITS・追従表・WATCH・コミット本文**: RQ3-05 K88 (5) の「#632 の案と同じ動き」は `ほめる？` の例に限る (#632 の案はかなだけを数え、Mac は数字・英字も数える) → 「記号を数えない点は同じ」とし、数え方の違いを書いた。RQ3-06 #636 の「押下と解放の対だけ」は、変換の始まりと終わりでは片方だけになる (`MeltypeSession` は、キーの前に変換中なら Shift の押下を、キーのあとに変換中なら解放を送る) → 書き直した (片方だけのときも、Mac の `Replay` は何もしないので結論は同じ)。RQ3-07 #636 の「『飲み込んだ』印」は、Shift を握りつぶす別の記録 (`_swallowedShift`) と取り違えやすい → コードのコメントの言い方 (関所を閉じたあとに押下を受けたキーの記録。離したことはアプリに伝えない) にし、本家の直しの理由 (#617: 2 台のキーボードで Shift を押したまま打つと、確定のあとも Shift が押されたままになる) を足した。RQ3-08 K88 (3) の `.zoverdesu` の括弧が、取り込み前と main には食い違いが無いように読める → BI では取り込み前と main でも表示 `。ぞゔぇrです`・確定 `。ぞゔぇです` と食い違っていたこと (NS は表示も確定も `。ぞverdeす`) を書いた (7 回目に 5 つの木 × BI/NS で測った記録と、査読員の流し直しが同じ)。RQ3-09 追従表の P46-47〜50 の行に、P46-50 の RP1-03 (変異の注を直した) が抜けている → 足し、P46-51 も足した。RQ3-10 文書のコミット本文の「44 入力 (うち 4 件は…確認用)」が 4 入力に読める → 新しい文書のコミット本文では「67 件 = 44 入力。そのうち 4 件 (3 入力) は文書に書いていない確認用」と書いた。
  - **9 回目** (8 回目の対応のあとに増えた P46-51・作り直しと着地の手順・件数と文書の道具 (RR1)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の 8 回目の対応 (RR2)、KNOWN_LIMITS・追従表・WATCH・README/INSTALL・コミット本文の 8 回目の対応と、本家の事実 (#551・#560・#617・#632・#636) との照合 (RR3)): **RR1: P0 0・P1 1・P2 6、RR2: P0 0・P1 1・P2 6、RR3: P0 0・P1 0・P2 5** (合わせて P0 0・P1 2・P2 17)。RR1 は、変異 len15・up2 が aed8e66 のテストで赤になり dep8 が緑のままであることを自分の複製で確かめたうえで、自分で考えた変異 4 つを流し、1 つ (abbr) が緑のまま通って NS で答えが変わることを見つけた (下の RR1-02)。RR2 は、8 回目の 24 件が対応表で 1 件ずつ過不足なく扱われていることを確かめた。RR3 は、#551 の 2 つのコミットの入り方と P46-17 の新しい説明を、本家の clone・GitHub の画面・Mac のコードで確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RR1-01) | 作り直す手順 (`final_merge.sh`) の守りは大文字・小文字を区別するが、macOS のファイルシステムと git (`core.ignorecase=true`) は区別しないので、`wt-FINAL`・`wt-Docs8`・`wt-DOCS7`・`wt-Merge` が守りを通る (main の hash が正しければ、`wt-final` などの作業ツリーを消しうる。RR1 は git の実装からの推測として書き、実際には消していない) | **直した** (リポジトリの外の手順)。名前を ASCII の小文字・数字・_・- だけにし (`LC_ALL=C` で照らす。改行を含む値も止める)、登録された作業ツリーとの照合も大文字・小文字を区別しない形にした。本番 (`final/p46`) は既定の場所だけで作り直し、試し (`tmp/p46-…`) は本番の場所・ログの名前と重ならない指定だけを通す (下の RR1-03 も一緒に直した)。main の hash をわざと違えて流し (守りを抜けた指定も、そのあとの main の確かめで止まるので、何も消さない)、想定と違う指定 68 通り (本番に既定でない場所を付ける 5・試しで FM_TAG と FM_OUT を省く 1・枝 18・作業ツリー 18・複製 12・本文の置き場所 6・ログの名前 8。RR1 が守りを抜けた `wt-FINAL`・`wt-Docs8`・`wt-DOCS7`・`wt-Merge` と、改行・ASCII でない文字を含む名前も入れた) が、どれも狙った守りの文言で止まり、正しい 2 通り (既定・試し) だけが守りを通ることを確かめた (前と後で枝と worktree の一覧が同じ) |
    | P1 (RR2-01) | (9) は #560 が抜けた理由を「Mac の PR の調査 (10-11) より後、ウォッチの起点 (`a4b6f1a`) より前」と書くが、同じ時期には #560 のほかにも本家 main への merge があり、穴を「#560 の 36 の PR」だけに見せている (過小評価) | **書き直した** (計画書の「P46 で触れなかった既知の穴」(9) と目的、8 回目の表の RQ3-02 の行、UPSTREAM_REVIEW の §0 の範囲の外の段落と冒頭の訂正の表、追従表の P46-17 の行・§3 の判断待ち・更新の記録の行。窓の前にも同じ穴があることと、窓の中にも個別に見た PR があることは、10 回目の査読 RS2-02・RS3-01 で書き足した)。数え直した: 本家 main の first-parent で、10-11 以降・a4b6f1a までの merge は、PR の番号を問わず 52 (#560 を含む。10-11 の調査は番号 390 以下も全部は見ていないので、390 以下の 7 も入れた)、Core (テスト以外) に触るのは 23、そのうち 13 は Mac の記録に番号も hash も Issue の番号も出てこず、残る 10 も跡があるだけ (数え方と内訳は (9))。査読員の数 (10-12 の 0 時から、#560 を除いて 37 の merge・Core 15・Mac の main に記録が無い 10) とは、数え始めと、記録を探した範囲 (P46 の調査の本文も入れた。#540・#588 はそこにだけ出てくる) が違うだけで、10-12 からに絞ると 37・15 で同じ。統合 PR は #560 と #402 の 2 つ。8 回目の対応は #560 しか書いていなかった。精査の項目も、#560 ではなく、この時期の全体を対象にした (判断待ち)。数える途中で、P46 の調査の §6 が「PR を通らない本家の直接コミット」と書いた 8 つが、どれも PR の中のコミットだったことも分かったので、UPSTREAM_REVIEW の冒頭の訂正の表に足した |

  - P2 17 件 (RR1 6・RR2 6・RR3 5) の対応 (すべて直した。うち RR1-03〜07 の 5 件は、リポジトリの外の手順と道具の直し):
    - **テストと注** (0d2897e): RR1-02 大文字の略語の直後に頭が大文字の長い語が来る形で、「大文字の略語の途中から始まる語は見ない」規則を前へ伸ばすループにも当てる変異 (abbr) が緑のまま通り、NS で 168 入力中 56 の答えが変わる (`kyouhaPCUnderachievementsuru` が `きょうはPCUnderachievementする` から `きょうはPCUnderachieveめんつる` に) → `kyouhaPCUnderachievementsuru` → `きょうはPCUnderachievementする`・`kyouhaPCAmentsuru` → `きょうはPCAmentする`・`PCAmentsu` → `PCAmentす` の例を足した (模擬の判定器が知っている語の形)。3 つとも、開発機の NSSpellChecker で 5 つの木に流した: 今の統合と、取り込み前・main は、テストの期待と同じ答え。P46 だけの木と P46-47 の前の統合は `…めんつる`・`…めんつ` に崩れる (P46-51 の例と違って P46 だけの木でも崩れる形で、P46-47 で直った)。同梱の表では、どの木でも `…めんつる`・`…めんつ` で、取り込み前から同じ。変異の木 15 本 (up1・up2・up3・len15・dep6・dep7・dep8・b・f・a・d と、9 回目の査読員の abbr・len16・caps2・mixchk。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、abbr は赤 (851/852。実際 `きょうはPCUんでらちえゔぇめんつる`)、ほかの up1・up2・up3・len15・dep6・dep7・b・f・mixchk も赤 (どれも 851/852 で、赤は P46-47 のテストだけ)、a・d は緑 (852/852。等価)、dep8・len16・caps2 は緑 (852/852。答えが変わる入力が見つかっていないので捕まえていない。テストの注に書いた)。変異なしの木は 852/852・警告 0。 RR2-05 テストの注 (と 7 回目の節の冒頭) の「別の作り方の入力 (…深い語。BI 4,292・NS 668)」は、NS 668 が 5 種の入力に読める → BI は 5 種の 4,292、NS は大文字と深い語の 668 と書き分けた。
    - **着地と作り直しの手順・道具** (リポジトリの外): RR1-03 作り直す手順の試しで `FM_TAG`・`FM_OUT` を省くと、本番のログ (`logs/final*.out`) と本文 (`fixmsg/*.txt`) を上書きする・`FM_TAG` に `../` を入れられる・複製と作業ツリーを同じ場所にできる → 上の RR1-01 と一緒に直した (試しは本番と重ならない名前だけ、`FM_TAG` は小文字・数字・- だけで final で始めない、複製の名前は wt- で始めない)。RR1-04 着地の手順は、第 4 引数 (全テストを通したコミット) と `GATE_TREE` (ゲートを通した木) をそのまま信じ、文書の枝の先端を渡すと「全テストは … で通し」と本当でない文言を出す → どちらも記録 (`gated_commit.txt`・`gate_tree_commit.txt`) と同じでなければ止まり、全テストを通したコミットから文書の変更が 1 つも無い枝も止まるようにした (記録と違う 4 通りと文書の無い枝で、どれも原因を指す文言で止まり、正しい引数だけが通ることを確かめた)。RR1-05 本文の型紙を書く道具の説明「数字は手で打たない」は、変異の結果のほかの数字を記録から手で写していた → 説明を、ログから入れる数字と手で写した数字に分けて書いた (P46-52 の型紙を書く道具は、NS と BI の答えと、査読員の入力の数・答えが変わった数も、ログと表から入れて assert する)。RR1-06 8 回目に直した言い方のうち 2 句 (「対になり、リピートは起きない」「コピーが失敗したとき警告を出すだけ」) が、古い言い方の一覧に無かった → 9 回目の一覧に足した。RR1-07 古い言い方を数える道具の 3 番目の確かめは表示するだけ・文書の機械検査は基準のコミットが無いと traceback で止まる・文書を作り直す手順は置き場所の記号の残りを表示するだけで止めない → 引用として数えない箇条の切り方と、7・8 回目の箇条にその回の句があることを assert にし、基準のコミットが無いときは NG で止め、記号が残っていれば止めるようにした。
    - **IMPROVEMENT_PLAN と UPSTREAM_REVIEW**: RR2-02 計画書の P46 の目的の「`45e14f8` までを精査」は、全部見たように読める (8 回目の RQ3-02 の後半) → 目的に、精査していない時期があること ((9)) を書いた。RR2-03 UPSTREAM_REVIEW の §0 の「#560 にまとめて入った 36 の PR」は、入った数 (37) ではない → この段落を、上の RR2-01 の時期の全体の書き方にした (#551 を除いた 36 は、計画書の (9) に書いた)。RR2-04 (9) の「15 のうち 8 は 1 行も無い」は、照らした条件と、ほかの多くもほとんど無いことが伝わらない → 25 文字以上の追加行 (注を除く) で照らしたことと、15 の PR ごとの行数を書いた。RR2-06 8 回目の対応の「足した行の hash が Mac か本家の clone にあることを確かめる」は、数字と英字を両方含む 7 桁だけだった → 8 回目の行に「数字と a〜f を両方含む 7 桁」と書き足し、道具も広げた (7〜40 桁で、`` ` `` で囲んだものは数字だけ・英字だけでも確かめる)。RR2-07 7 回目の RP3-11 の行に、8 回目の RQ3-05 の直し (「同じ動き」→「記号を数えない点は同じ」) への注が無い → 足した。
    - **KNOWN_LIMITS・追従表・コミット本文**: RR3-01 P46-17 の行の「`languages.json` は元から同じ」は、何と同じかが 2 通りに読める (本家とは、残す時と、残せないときの動きが違う) → 「元から、上書きの前に `.broken` に残す (残せなければ保存しない。警告は保存の失敗として出る)」にした。RR3-02 判断待ちの 2 件が、追従表 §3 の「判断待ち」の一覧に無い → 一覧と、更新の記録の行に足した (`config.json` の警告は、P46-17 の行と K89 (4) にも「判断待ち」と書いた)。RR3-03 K88 (3) の `.zoverdesu` の括弧が 3 段の入れ子で、NS の形まで「日本語寄り」とまとめている → `_` の不具合の文と、`.zoverdesu` を比べる文に分けた。RR3-04 文書のコミット本文で、件数の重複の注を 8 回目にだけ付け、2 回目には付けていない → 2 回目にも付けた。RR3-05 #636 の理由で「変換中」と「変換ボックスが開いている間」が混ざっている → 同じ欄の前の方に合わせて「変換ボックスが開いている」にそろえ、「変換の始まりと終わり」も「変換ボックスが開くときと閉じるとき」にした (送る条件は `MeltypeSession` の `IsComposing`)。査読員の付記どおり、`_capturedDown` の説明も、コード (`CompositionController` の `HandleKey`) に合わせて「ここにあって押下をアプリに送り直さなかったキーは、離したこともアプリに伝えない」にした。
  - **10 回目** (9 回目の対応のあとに増えた P46-52・変異・作り直しと着地の手順・文書の道具 (RS1)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の 9 回目の対応と、窓の数え直し (RS2)、KNOWN_LIMITS・追従表・WATCH・README/INSTALL・コミット本文の 9 回目の対応と、本家の事実との照合 (RS3)): **RS1: P0 0・P1 0・P2 6、RS2: P0 1・P1 1・P2 5、RS3: P0 0・P1 1・P2 1** (合わせて P0 1・P1 2・P2 12)。RS1 は、変異 abbr が 0d2897e のテストで赤になることを自分の複製で確かめたうえで、自分で考えた変異 5 つを流し、3 つ (abbr3・lowabbr・midcap) が緑のまま通って答えが変わることを見つけた (下の RS1-01・02。lumirror は赤、abbrdig は答えの変わる入力が見つからなかった)。RS2 は、9 回目の 19 件が対応表で過不足なく扱われていることと、窓の数 (52 回の merge・Core に触る 23・記録の無い 13 など) を自分のスクリプトで確かめた。RS3 は、追従表・KNOWN_LIMITS・コミット本文の 9 回目の直しを、本家の clone (どの merge で main に入ったか) と Mac のコードで確かめた。P0・P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P0 (RS2-01) | (9) の「#402 (…中の 49 の PR のうち 40 は番号が Mac の記録に出てきて、9 … は出てこない)」は誤り。中の PR は 47 で、番号が出てくるのは 38 (どれも 10-11 の調査)。数えた道具が、中の merge の件名すべてから番号を拾い、main を取り込む merge の件名にある #319・#299 (どちらも別の merge で main に入った PR) まで中の PR に数えていた (記録がある側を多く見せる向き) | **直した**。中の merge のうち件名が PR の merge の形のものだけを数える道具で数え直し、(9) を「中の merge 65 のうち PR の merge は 47 で、47 の PR のうち 38 は番号が 10-11 の調査に出てきて、9 は出てこない」に直した。数え直した道具 (`gap4.py`) は、本家 main の first-parent の merge を、日付 (committer、JST) で窓と窓の前に分けて数え、統合 PR の中の PR は、件名が PR の merge の形 (`Merge #N`・`Merge pull request #N`) の merge だけを数える。窓 (52 回・Core 23・記録なし 13) は 9 回目と同じで、#402 の中は PR の merge 47・番号が 10-11 の調査に出てくるもの 38。窓の前は merge 62 回・直接のコミット 6 (Core に触る merge 36・そのうち記録なし 19、Core に触る直接のコミット 4)。査読員 RS2 の数 (10-07 の 0 時から merge 63 回・Core 36・記録なし 19) とは、数え始めが違うだけ (`b546845` 自身の #91 の 1 回。Core に触らない) で、記録の無い 19 は同じ PR |
    | P1 (RS2-02) | 穴を「10-11 の調査 → `a4b6f1a`」の窓の中だけに見せている。10-11 の調査は、渡された一覧の 6 件と #268 以降を選んで見たもので、それより前に入った PR (UPSTREAM_REVIEW の冒頭の訂正の表の #167・#184 (10-08) など) も、まとめて調べたものが無い (9 回目の RR2-01 と同じ形の過小評価) | **書き直した** (計画書の (9) と目的、UPSTREAM_REVIEW の §0 の範囲の外の段落と冒頭の訂正の表、追従表の P46-17 の行・§3 の判断待ち・更新の記録の行。9 回目の表の RR2-01 の行に注)。窓の前は、Mac の最初の Core と中身が同じファイルが最も多い本家 main の版 (40 ファイルのうち 29。10-06 12:58〜10-07 06:25 の 6 つの版が同じ数で、その最後の `b546845`) のあとから数えた (Mac の最初の版に入っている分は引いていないので、上限の目安。11 回目の査読 RT2-01 のあとで、同じ中身で入っているものは無いことを確かめ、(9) の例の書き方を直した)。精査の対象を窓だけにするか、Mac の最初の版のあとからにするかも、判断待ちに足した (範囲の言い方は、11 回目に「Mac の最初の Core に最も近い本家の版 (`b546845`) のあとから」にそろえ、12 回目に、最も近い版は同じ数の 6 つで `b546845` はその最後だと書き足した。上限の目安とする理由は、12 回目に (9) に書いた。12 回目の査読 RU4-04) |
    | P1 (RS3-01) | 追従表の P46-17 の行と更新の記録の行、文書のコミット本文 (`5223acf`) が、窓に入った PR を全部「どの調べにも入っていない」「未精査」と書く。同じ表の §3 は、窓の中の #355・#362・#370・#380・#319 を対象外か済にしていて、P46-4・5・14 は窓の PR (#531・#464・#484) の中のコミットを移植している (9 回目の RR2-01 を直すときの言い過ぎ) | **直した**。計画書・UPSTREAM_REVIEW の「まとめて調べたものが無い」にそろえ、一部は 10-11 の調査や P46 で個別に見たことを書いた (11 回目の査読 RT3-06 で、P45 と「PR 全体を見たとは限らない」を足した。追従表の 2 か所と、文書のコミット本文を作る道具の文。窓の前も同じ書き方にした) |

  - P2 12 件 (RS1 6・RS2 5・RS3 1) の対応 (すべて直した。うち RS1-03〜06 の 4 件は、リポジトリの外の手順と道具の直し):
    - **テストと注** (43b6ec9): RS1-01 略語の規則の別の写し方 (3 文字以上の略語の直後だけに当てる abbr3、略語の直後の頭が小文字の語を見ない lowabbr) が緑のまま通り、NS で答えが変わる → 例を足した (`kyouhaAPIAmentsuru` → `きょうはAPIAmentする`、`imaUSBAmentsu` → `いまUSBAmentす`、`AIamentsuru` → `AIamentする`、`kyouhaPCamentsu` → `きょうはPCamentす`。NsLikeChecker の例)。開発機の NSSpellChecker で 5 つの木に流すと、取り込み前・main・今の統合はこの答えで、3 文字の略語の例は P46 だけの木と P46-47 の前の統合で、頭が小文字の語の例は P46-47 の前の統合だけで `…めんつる`・`…めんつ` に崩れる (同梱の表 (BI) では、どの木でも崩れる。取り込み前から同じ)。RS1-02 救いすぎの変異 (2 文字目以降に大文字を含む並びを、判定器に聞かずに長い語とみなす midcap) が緑のまま通り、BI でも答えが変わる → 守りの例を足した (`kyouhaPCpantsuwokau` → `きょうはPCぱんつをかう`、`PCdentsu` → `PCでんつ`。同梱の表と NsLikeChecker の両方。BI でも NS でも、取り込み前と main は `きょうはPCpantすをかう`・`PCdentす` で、P46 だけの木から今の答え)。あわせて、P46-52 で略語の項目を差し込んだために、深さの上限 (8・10 単位) の注が略語の項目の続きに見えていたのを、独立した項目にした (注だけ)。変異の木 20 本 (P46-52 の 15 本と、10 回目の査読員の abbr3・lowabbr・midcap・lumirror・abbrdig。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、abbr3・lowabbr・midcap は足した例で赤 (851/852。実際 `きょうはAPIAめんつる`・`AIあめんつる`・`きょうはPCpantすをかう`)、lumirror は前からある長音の u の例で赤 (実際 `そううねmployめんつる`)、ほかの up1・up2・up3・len15・dep6・dep7・b・f・abbr・mixchk も赤 (どれも 851/852 で、赤は P46-47 のテストだけ)、a・d は緑 (852/852。等価)、dep8・len16・caps2・abbrdig は緑 (852/852。答えが変わる入力が見つかっていないので捕まえていない。テストの注に書いた)。変異なしの木は 852/852・警告 0。
    - **着地と作り直しの手順・道具** (リポジトリの外): RS1-03 `docs_lint.py` が、バッククォートで囲まない数字だけの hash (表の欄や括弧の中の 0075050・4420099) を確かめていない → 7 桁以上の数字だけの並びは、囲まなくても確かめるようにした (land6 の文書で、バッククォートで囲まない数字だけの hash 5 回を含む hash 332 回を確かめて NG 0。そのうち 1 つを存在しない hash に変えた試しの木では NG 1)。RS1-04 9 回目の古い言い方の一覧の句「#560 にまとめて入った 36 の PR」は、直す前の文書の言い方 (#560 の後ろに「(10-13 の統合 PR)」が入る) に当たらない → 句を直す前の文書の言い方に合わせた。直すときに、この句が # で始まるために一覧の読み込みで注として飛ばされ、一度も確かめられていなかったことも分かったので、注は「# 」(# と空白) で始まる行だけにし、どの回の句も、その回に査読された文書 (直す前) に 1 回以上あることを `docs_check.py` で確かめるようにした (前の回に直した句は直した回の文書で見る。断片にしか無い言い方は除く)。RS1-05 文書のコミット本文を作る道具の説明「件数は … (手で打たない)」は言い過ぎで、本文の「#560 の 36 の PR」には #551 を除いた数だという書き分けが無い → 説明を、道具が取る数 (例・実機確認・テストの件数と査読の件数) と手で書いた数を分けて書く形にし、本文を「#560 の、#551 を除く 36 の PR」にした (`docsmsg11.py`)。RS1-06 作り直す手順 (`final_merge.sh`) の試しの守りに、本文の置き場所 (`FM_OUT`) が作業ツリー・logs でも、ログの名前 (`FM_TAG`) が mac・mergeonly でも通り、記号リンクの作業ツリーも通る抜けがある → 試しの本文の置き場所は `$W/fm<小文字・数字・_・->-msg`、ログの名前は fmtest で始まる名前だけを通し、作業ツリー・複製・本文の置き場所が記号リンクなら止めるようにした。守りの試し (`fmguard10.sh`。main の hash を、存在しない値 deadbee にして流すので、守りを抜けても main の確かめで止まる) は、9 回目の 70 通りに 28 通りを足した 98 通りで、どれも想定どおりの所で止まった (足したのは、本文の置き場所に作業ツリー・logs・rl03 などを渡す 9 通り、ログの名前に mac・mergeonly・main3 などを渡す 8 通り、記号リンクの作業ツリー・複製・本文の置き場所 3 通り、本文の置き場所と複製が同じもの、査読員が試した指定など。許される名前は main の確かめで止まった)。試しのために作った記号リンクは最後に消し、リンクの先が残っていることと、枝と作業ツリーの一覧が前と後で同じことを確かめた。P46-53 の段を足した作り直しの試し (`FM_BRANCH=tmp/p46-fmtest`) では、段ごとの全テストが 851/851・852/852 (警告 0) で、9 つのコミット (統合の merge と P46-41・47〜53) の木と本文が今の final/p46 と同じ (先端の木 `0860a5b`。試しの枝と作業ツリーは比べたあとに消した)。
    - **IMPROVEMENT_PLAN と UPSTREAM_REVIEW**: RS2-03 (9) の残る 10 の内訳で、#402・#426 を「別の話の中に出てくるだけ」とするのは記録と合わない → #402 は中の PR の多く (47 のうち 38) の番号が 10-11 の調査に出てくる、#426 は直す Issue #54 を 10-11 の調査が「P26 で解決済み」とした (PR の中身は見ていない)、と書き分けた (#477 は「別の話」のまま。これは誤りで、11 回目の査読 RT2-04・RT3-01 で直した)。RS2-04 日付の境目のタイムゾーンが無い → 「日付 (committer、JST)」にした。RS2-05 記録の無い 13 に入る #476 は、merge 自身の hash `a4b6f1a` がウォッチの起点として記録に出てくる → 13 の後ろに注を足した (書いた基準 (番号・中のコミットの hash・枝の Issue) では 13 のまま)。RS2-06 10-11 の調査の説明「`gh pr list` の 100 件から、6 件と…を選んで見た」は、6 件の出どころが違う → 「依頼で渡された一覧の 6 件と、`gh pr list` で取った 100 件のうち…」にした (`UPSTREAM_PR_SURVEY.md` の 8 行目で確かめた)。RS2-07 UPSTREAM_REVIEW の §0 の段落が、数え始め (10-11 の 0 時) を書いていない → 「10-11 の 0 時 (JST) から起点までの merge は 52 回 (調査の前に入ったものも数えた。…)」にした。
    - **追従表**: RS3-02 #636 の理由の主文「変換ボックスが開いているときに Shift + キーを打つと」は、変換ボックスが開く場合 (何も打っていないときの Shift + A など。Core は解放だけを受ける) を含まない → 主文を「Shift + キーを打ったときに」にした (括弧の中と結論は変えない)。
  - 実装者が自分で見つけたもの (10 回目の査読の待ち時間に。同じ波で直した): (1) 文書の例の照合の穴: 計画書の「確認」は、文書に書いた打鍵の例を統合した木で流して照合したと書くが、照合の道具 (`verify_docs_examples.py`) の一覧に、途中の回から文書に足した例 (テストだけのコミットの例・3 者比較の例・途中の木で測った例など) が入っていなかった (land6 (`5223acf`) で P46 が文書に足した行から拾った「打つ → 結果」の例 71 入力のうち、前の照合の一覧にも実機確認リストにも無いものが 38 入力あり、そのうち 24 入力は木の答え (今の木か、5 つの木の答え) を書いた例だった (残る 14 は、調査時点のまま残した UPSTREAM_REVIEW の §1 以降の 9 と、記号の説明・長い列の一部などで外してよい 5))。9 回の査読でも見つかっていなかった → 照合の一覧に、確定の例 19 件と、文書が 5 つの木の答えを書いた例の確定と表示を足した (例の一覧は `docs_examples_data.py` に分けた)。`docs_check.py` が、P46 で文書に足した行の例が、照合の一覧・実機確認リスト・理由つきで外す一覧・調査時点の本文のどれかにあることを確かめる (網)。(2) 照合を広げて見つかった食い違い: 8 回目の P46-51 の段落の「P46-47 の前の統合だけが `…めんつる` に崩れる」は、`su` で終わる `kyouhaunderachievementsu` では `…めんつ` → 「`…めんつる`・`…めんつ`」に直した。(3) 3 者比較の `certificatesgagakui-repleted` の「main だけが `せrちふぃcatesgagakuiーrepleted` に崩し」は、5 回目の査読員の LETTERS (NS) の 1 回の答えで、同じ木で流し直すと 3 回とも `certificatesががくいーrepleted` (ほかの木も BI・NS とも同じ) → その実行での答えと流し直した結果を両方書き、NS の揺れとみられる (原因は推測) と直した。結論 (統合は取り込み前と同じで、P46-47 と関係ない) は変わらない。
  - **11 回目** (10 回目の対応のあとに増えた P46-53・変異・作り直しと着地の手順・文書の道具 (RT1)、IMPROVEMENT_PLAN の P46 の節と UPSTREAM_REVIEW の 10 回目の対応と、窓と窓の前の数え直し (RT2)、追従表・変わっていないはずの文書・コミット本文と、新しい文に入った本家の事実 (RT3)): **RT1: P0 0・P1 2・P2 2、RT2: P0 0・P1 2・P2 4、RT3: P0 0・P1 1・P2 6** (合わせて P0 0・P1 5・P2 12。RT2 の報告の見出しは P2 5 だが、P2 の項目は RT2-03〜05 の 3 つで、RT2-05 の (a)・(b) を分けて 4 と数えた。11 回目の対応では見出しの 5 を写して、合わせて 13 としていた (12 回目の査読 RU4-01)。RT2-04 と RT3-01 は同じ #477 の指摘) (15 回目の注 (15 回目の査読 RY5-01): RT3-04 と RT2-05 (a) も、どちらも P2 で同じ指摘。下の「コミット本文を作る道具」の箇条)。RT1 は、変異 abbr3・lowabbr・midcap が 43b6ec9 のテストで赤になることを自分の複製で確かめたうえで、自分で考えた変異 4 つを流し、2 つ (lowabbr3・abbr3ov) が緑のまま通って答えが変わることを見つけた (下の RT1-01・02。lvpart・ovret は答えの変わる入力が見つからなかった)。作り直す手順の守りの試し (98 通り) と、着地の手順の試し (10 通り) も流した。RT2 は、窓と窓の前の数 (merge 52・62、Core に触る 23・36、記録の無い 13・19、#402 の中の PR 47・38・9、窓の前の直接のコミット 6・そのうち Core に触る 4) を自分のスクリプトで数え直し、文書の数がすべて合うことを確かめた (指摘は数の意味づけと書き方)。10 回目の 15 件が対応表で過不足なく扱われていることも確かめた。RT3 は、新しい文に入った本家の事実 (#319・#299 の入り方、#402 の中の merge、#426 と Issue #54、§6 の 8 つの入り方、#212 の中の `b2e33ca` など) を本家の clone と GitHub の画面で確かめ、変わっていないはずの 6 つの文書が land6 と 1 字も違わないことも確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RT1-01) | 3 文字以上の略語の直後の、頭が小文字の語だけを見ない変異 (lowabbr3。10 回目に捕まえた abbr3 と lowabbr を組み合わせた写し方) が、全テストを緑のまま通る。NS では、査読員の 420 入力のうち 60 入力の答えが変わる (`kyouhaAPIamentsuru` が `きょうはAPIあめんつる` に) | **直した** (テストだけ。19ad2ac)。規則を「n 文字以上の略語」に写す変異は n を変えるたびに別の変異になるので、略語の長さ (2〜6 文字: PC・API・HTTP・HTTPS・NASDAQ) ごとに、略語の直後に頭が大文字の語・頭が小文字の語を置いた例を足した (`kyouhaAPIamentsuru` → `きょうはAPIamentする` など。NsLikeChecker の例)。開発機の NSSpellChecker で 5 つの木に流すと、`kyouhaAPIamentsuru` は取り込み前・main・P46 だけの木・今の統合でこの答えで、崩れるのは P46-47 の前の統合だけ (`…めんつる`。同梱の表 (BI) では、どの木でも崩れる。表示も確定と同じ)。変異の木 38 本 (P46-53 の 20 本と、11 回目の査読員の lowabbr3・abbr3ov・lvpart・ovret、略語の文字数を 4〜7 にした abbr4〜7・lowabbr4〜7・abbr4ov〜7ov と、文字数がちょうどのときだけ当てる abbreq4・lowabbreq5。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、lowabbr3・abbr3ov は足した例で赤 (851/852。実際 `きょうはAPIあめんつる`・`きょうはAPIpantすをかう`)、abbr4〜6・lowabbr4〜6・abbr4ov〜6ov・abbreq4・lowabbreq5 も足した例で赤、P46-53 までの up1・up2・up3・len15・dep6・dep7・b・f・abbr・mixchk・abbr3・lowabbr・midcap・lumirror も赤 (どれも 851/852 で、赤は P46-47 のテストだけ)、a・d は緑 (852/852。等価)、dep8・len16・caps2・abbrdig・lvpart・ovret は緑 (852/852。答えが変わる入力が見つかっていないので捕まえていない)、abbr7・lowabbr7・abbr7ov は緑 (852/852。7 文字以上の略語の例が無いので捕まえていない。どれもテストの注に書いた)。変異なしの木は 852/852・警告 0。 (緑の abbr7・lowabbr7・abbr7ov は、12 回目の査読 RU1-03 で、略語に限らず 7 文字以上の大文字だけの語の直後で答えが変わると分かり、f653fec で例を足して赤にした) |
    | P1 (RT1-02) | 3 文字以上の略語の中まで伸ばしたら、判定器に聞かずに長い語とみなす変異 (abbr3ov。救いすぎの形) が、全テストを緑のまま通る。同じ 420 入力のうち BI で 45・NS で 117 入力の答えが変わる | **直した** (同じコミット)。略語の長さごとに、読みの頭になる断片の守りの例を足した (`kyouhaAPIpantsuwokau` → `きょうはAPIぱんつをかう` など。同梱の表と NsLikeChecker の両方)。BI でも NS でも、取り込み前と main は `きょうはAPIpantすをかう` で、P46 だけの木から今の答え (表示も確定と同じ)。略語の最後の字と断片で知っている語になる形 (`APIdentsu`。API + dent = Ident で、開発機の NSSpellChecker は ident を単語と答える) は、`PCAmentsu` (PC + Ament を英字に残す) と同じ形で見分けられないので、例に入れていない: NS では、取り込み前・main・今の統合が `APIdentす`、P46 だけの木と P46-47 の前の統合が `APIでんつ` (BI では取り込み前と main だけが `APIdentす`)。テストの注に書いた |
    | P1 (RT2-01) | (9) と 10 回目の表 (RS2-02 の行) の「上限の目安 (数え始めの 2 分後の #90 (10-07 06:27) などは、Mac の最初の版に入っているかもしれない)」は、データと合わない (Mac の最初の Core の該当のファイルは #90 の前の中身。窓の前の Core の変更で、Mac の最初の版に同じ中身で入っているものは無い) | **直した**。#90 の例を外し、照合を足して書いた: 窓の前で Core に触る merge 36 と直接のコミット 4 が変えた Core のファイル (のべ 103) は、変えた後の中身がどれも Mac の最初の Core (`c3c247f`) の同じファイルと違う (ファイルごとの中身 (blob) の照合。#90 は `RomajiDetector.cs` だけを変え、Mac の最初の Core のそのファイルは #90 の前の中身)。手を入れた形で入っているかは確かめていないので、数は上限の目安のまま (10 回目の表の RS2-02 の行に注。12 回目の査読 RU2-04 で内訳を足して (9) に書いた: 窓の前で Core に触る merge 36 と直接のコミット 4 が変えた Core のファイルは、のべ 103 (異なるファイル 28)。変えた後の中身が、Mac の最初の Core (`c3c247f`) の同じパスのファイルと同じものは 0 (ファイルごとの中身 (blob) の照合): 11 は Mac の最初の Core に無いファイル (8 種類)、13 は Mac の最初の Core が変える前の中身のまま (#10 の 3・#32 の 2・#212 の 3 と、#87・#90・#179・#202・`947ff64` の 1 ずつ。#90 は Core では `RomajiDetector.cs` だけを変える)、残る 79 は両方にあって中身が違う。上限の目安とする理由は、12 回目の査読 RU4-02 で、時刻の事実を使って (9) に書き直した。11 回目の文を残して 12 回目の直しを注にする形には、13 回目の査読 RW4-03 で戻した) |
    | P1 (RT2-02) | 窓の前の穴の書き方で、10-11 の調査の前提 (`UPSTREAM_PR_SURVEY.md` の §2「本家の #267 までは取り込み済み」) を訂正していない。Mac の Core に最も近い本家の版は 10-07 06:25 の `b546845` で、窓の前の記録の無い 19 はどれも #211 以下。調査が #268 以降を選んだのは、この前提によると読める | **書き足した** ((9)・UPSTREAM_REVIEW の §0 の範囲の外の段落と冒頭の訂正の表)。#267 は P29 で個別に取り込んだものであることも書いた。調査の範囲の言い方も「渡された一覧の 6 件と、#268 以降で Mac に関係しそうな約 45 件」にそろえた。調査の文書そのものは、調査時点の記録として変えていない (最も近い本家の版は、12 回目の査読 RU2-01 で、中身が同じファイルの数が同じ 6 つの版で、`b546845` はその最後だと分かり、(9) などに書き足した) |
    | P1 (RT3-01) | (9) の「#477 は、番号が別の話の中に出てくるだけ」は誤り。`STRATEGY_2026-10.md` は #477 を、Mac にまだ無い機能 (英数状態でローマ字を検知して日本語に戻す。Mac は P11 待ち) を本家が IME 方式 (TSF) に入れた PR として挙げていて、#477 は Mac も使う Core の `MeltypeSession` を変える (10 回目の RS2-03 の残り。RT2-04 (P2) も同じ指摘) | **直した**。「STRATEGY が、Mac にまだ無い機能を本家が IME 方式に入れた PR として番号を挙げている。PR の中身 (Core の `MeltypeSession`・`CompositionController`・`Settings` を変える) は見ていない」にした (10 回目の表の RS2-03 の箇条に注) |

  - P2 12 件 (RT1 2・RT2 4・RT3 6) の対応 (すべて直した。RT3-07 は、査読済みのコミット本文を書き換えないので、ここに書いて補う。RT1-03・04 と RT3-05 は、リポジトリの外の道具の直し):
    - **文書の例の網と照合** (リポジトリの外): RT1-03 網 (`docs_net.py`) が、大文字で始まる打つ入力 (`AIamentsuru`・`PCdentsu`) を拾わない → 英字で始まる ASCII の並びを拾うようにした。RT1-04 網が、1 つのコード片の中の矢印 (`.zoverdesu → .zoveです`) と括弧の結果を拾わず、KNOWN_LIMITS の K87 の `kyouhaime` (きょうはいめ) がどの一覧にも無い。候補に IME が出ないという主張も、どの道具も照合していない → 網に 2 つの形を足し (land7 (`1138062`) の文書で、広げた網は 79 入力を拾い (広げる前は 75)、新しく拾った 4 入力のうち 3 (`AIamentsuru`・`PCAmentsu`・`PCdentsu`) は照合の一覧にあり、`kyouhaime` だけがどれにも無かった)、`kyouhaime` を照合の一覧に足した。候補の主張は、照合の道具に新しい表 (入力のあとに Space を押したときの候補に、ある語が入る・入らない) を足して確かめる (`kyouhaime` の候補に IME が無く、`ime` の候補に IME があることを、BI と NS で)。
    - **IMPROVEMENT_PLAN と UPSTREAM_REVIEW**: RT2-03 窓の前の #212 が、v1.1.0 をまとめた統合 PR であることを書いていない → 「v1.1.0 のまとめ (中のコミット 51、うち merge 28。PR #141 と、Issue の枝 17 本を取り込む)」と書いた。RT2-04 は RT3-01 と同じ (上の表)。RT2-05 (a) 窓の前の範囲の言い方が文書によってずれる (UPSTREAM_REVIEW の段落の「Mac の最初の版 (10-07) のあとに本家に入った PR」、文書のコミット本文 (`1138062`) の「Mac の最初の版のあとから 10-11 までの merge 62 回」と、RS2-07 で直させた形の「10-11 の調査とウォッチの起点のあいだの merge 52 回」) → UPSTREAM_REVIEW の段落を「Mac の最初の Core に最も近い本家の版 (`b546845`、10-07 06:25) のあとに」にし、文書のコミット本文は新しい文書のコミットで「10-11 の 0 時 (JST) から起点まで」「`b546845` のあとから 10-11 の 0 時まで」にした (査読済みの `1138062` の本文は書き換えない)。(9) と追従表の §3 の判断待ちの「Mac の最初の版のあとからにするか」も「Mac の最初の Core に最も近い本家の版 (`b546845`) のあとからにするか」にそろえた (12 回目の査読 RU2-01・RU5-02 で、最も近い版は同じ数の 6 つで、`b546845` はその最後だと書き足した)。RT2-05 (b) #476 の「`a4b6f1a` がウォッチの起点として出てくるだけ」は、ほかの出てくる所 (正解表 `upstream-quality.tsv` の写しの時点と、P45 の調べの起点 `a4b6f1a..45e14f8`) を落としている → 評価基盤 0-1 で比べた本家の版・正解表の写しの時点・ウォッチ 0-2 の起点の 3 つを書いた (報告の挙げた P45 の調べの起点が抜けていた。12 回目の査読 RU4-03・RU5-01 で、本家の修正を探した範囲の起点と、P45 で同じ出力になるかを比べた本家の版を足して 5 つにした)。RT3-02 「#167・#184 の Mac の記録は §6 のこの一言だけ」は、#167 の `96d8228` が §3 の D2 の注 (同じ `case F9/F10` の行を触る `8af9d19` と順番を決める) にも出てくる → 「#167 は §6 の一言と §3 の D2 の注だけ、#184 は §6 の一言だけ」にした ((9) と訂正の表)。RT3-03 窓の前の「Core に触る直接のコミット 4」のうち、`e95c07e` はテストの `669543f` と一緒に `45711a0` で戻されて差し引き 0、`55c8a29` は Core の版の番号だけ → 中身を変えるのは `947ff64` の 1 つだけであることを書いた ((9) と UPSTREAM_REVIEW の段落)。RT3-07 P46-53 の本文 (43b6ec9) の 2 つ目の「168 入力」(lowabbr) は、1 つ目 (前置き 6 × 頭を大文字にした 14 語 × 尾 2) とは別の組で、略語の直後に頭が小文字の 14 語を置いた 168 入力 (前置き 6 × 14 語 × 尾 2。ここで補う)。
    - **追従表**: RT3-06 「一部は 10-11 の調査や P46 で個別に見た」に、計画書の「PR 全体を見たとは限らない」が無く、P45 も抜けている → 「一部は、10-11 の調査で番号を見たか、P45・P46 で中のコミットを個別に見た (PR 全体を見たとは限らない)」にした (P46-17 の行と更新の記録の行)。
    - **コミット本文を作る道具** (リポジトリの外): RT3-04 は上の RT2-05 (a) と同じ。RT3-05 文書のコミット本文を作る道具 (`docsmsg11.py`) の説明の「手で書いた数」の一覧に、10 回目に足した数 (merge 52 回・62 回など) が入っていない → 新しい道具 (`docsmsg12.py`) では、本文に手で書いた数が作り直した文書にも同じ言い方で書いてあることを assert で確かめ、確かめられない数は説明に一覧で書いた。
  - 実装者が自分で見つけたもの (11 回目の対応の中で。同じ波で直したか、記録した): (1) 略語の長さごとの例を決めるために、略語 7 つ (2〜6 文字) × 前置き 3 × 語 8 (頭が大文字・小文字) × 尾 2 の 336 入力と、略語 7 × 前置き 3 × 断片 6 の 126 入力を、5 つの木で BI と NS に流した。今の統合の答えが期待と違う入力は、どれも取り込み前・main と同じ答えで、統合でだけ崩れる入力は無かった。P46 だけの木の答えが統合で失われるのは、NS の `APIdentsu` の形の 3 入力だけ (上の RT1-02 の行)。(2) その中で、P46 と関係ない、取り込み前からある崩れを 2 つ見つけた (`kyouhaLamentsu` → `きょうはLameんtす` は BI・NS とも 5 つの木で同じ、`APIUnderachievementsuru` → `あPIUnderachievementする` は NS で、取り込み前・main・今の統合が同じ) → 追従表の §2 に Q14 (記録だけ) として書いた。
  - **12 回目** (11 回目の対応のあとに増えた P46-54・変異・略語の格子と Q14 (RU1)、埋める文を作る道具の assert・作り直しと着地の手順・記録 (RU2)、文書の例の道具 (網・照合・確かめ・本文の道具。RU3)、IMPROVEMENT_PLAN の 11 回目の対応と UPSTREAM_REVIEW・窓の前の照合の数え直し (RU4)、追従表・変わっていないはずの文書と木・コミット本文と、新しい文に入った本家の事実 (RU5)): **RU1: P0 0・P1 1・P2 2、RU2: P0 0・P1 2・P2 12、RU3: P0 0・P1 3・P2 5、RU4: P0 0・P1 1・P2 4、RU5: P0 0・P1 0・P2 4** (合わせて P0 0・P1 7・P2 27。どの報告も、見出しの数と項目の数が合う)。RU1 は、11 回目の変異 lowabbr3・abbr3ov が 19ad2ac のテストで赤になることを自分の複製で確かめたうえで、自分で考えた変異 4 つを流し、1 つ (lvfrag) が緑のまま通って答えが変わることを見つけた (下の RU1-01。oabbrlow・iplnoext は赤、lvunotsu は答えの変わる入力が見つからなかった)。略語の格子の流し直しの食い違いが 0 なことと、Q14 の 2 入力も確かめた。RU2 は、埋める文を作る道具の assert を文の中の数ごとに表にし (assert の無い数・弱い assert を P2 に挙げた)、作り直す手順の守りの試し (98 通りと、自分で考えた 17 通り)、着地の手順の試し (10 通り)、記録の最後の 3 段落 (抜き取り 16 項目) を確かめた。RU3 は、網・照合・確かめ・本文の道具を流し直し、足した行を自分の道具で広く走査し、照合の一覧の期待値 376 個を機械で文書と照らした。RU4 は、11 回目の対応表を報告と照らし、窓の前の数 (first-parent の merge 62 と直接のコミット 6、Core に触る 36 と 4、Core のファイルのべ 103、#90・#212・#267 など) を自分のスクリプトで数え直して、文書の数がすべて合うことを確かめた (指摘は 11 回目の P2 の数え方と、理由の書き方)。RU5 は、追従表、変わっていないはずの 6 つの文書 (land7 と 1 字も違わない) と 6 つのフォルダーの木 (ゲートを通した 485cb80 の木と同じ)、2 つのコミット本文 (19ad2ac の本文の 30 入力の主張は、答えのログと食い違い 0)、本家の事実 (本家の clone と GitHub の画面 6 ページ) を確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RU1-01) | 伸ばす音の う の直後の断片 (きょう + pant・そう + dent) を、読みの頭として飛ばさない変異 (lvfrag。救いすぎの形) が、全テストを緑のまま通る。査読員の 143 入力 (伸ばす音の う で終わる前置き 11 と対照 2 × 断片と語 11) で、同梱の表 (BI) で 33・NS で 88 入力の答えが変わり、どれも取り込み前・main の答えに戻る。守りの例に、伸ばす音の う の直後のものが無かった (入力の頭・「は」の後ろ・略語の後ろにはあった) | **直した** (テストだけ。f653fec)。伸ばす音の う の直後の守りの例を足した: `kyoupantsuwokau` → `きょうぱんつをかう`・`soudentsu` → `そうでんつ`・`kinoupantsu` → `きのうぱんつ` (同梱の表と NsLikeChecker の両方)、`kyoumentsuyu` → `きょうめんつゆ`・`kyoushatsu` → `きょうしゃつ` (NsLikeChecker だけ。同梱の表では、どの木もこの答え)。BI と NS で 5 つの木に流すと、取り込み前と main は `きょうpantすをかう`・`そうでんtす`・`きのうpantす` (NS では `きょうmentすゆ`・`きょうshatす` も) で、P46 だけの木から今の答え (表示も確定と同じ)。変異の木 63 本 (P46-54 の 38 本と、12 回目の査読員の oabbrlow・iplnoext・lvfrag・lvunotsu、大文字だけの語の文字数を 8〜14 にした abbr8〜14・lowabbr8〜14・abbr8ov〜14ov。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、lvfrag は足した守りの例で赤 (851/852。実際 `きょうpantすをかう`)、P46-54 で緑だった abbr7・lowabbr7・abbr7ov は足した WARNING の例で赤 (実際 `きょうはWARNINGAめんつる`・`きょうはWARNINGあめんつる`・`きょうはWARNINGpantすをかう`)、abbr8〜13・lowabbr8〜13・abbr8ov〜13ov は UNDERSTANDING の例で赤、oabbrlow・iplnoext と、P46-54 までに赤だった 27 本も赤 (どれも 851/852 で、赤は P46-47 のテストだけ)、a・d は緑 (852/852。等価)、dep8・len16・caps2・abbrdig・lvpart・ovret・lvunotsu は緑 (852/852。答えが変わる入力が見つかっていないので捕まえていない)、abbr14・lowabbr14・abbr14ov は緑 (852/852。14 文字以上の大文字だけの語の例が無いので捕まえていない。どれもテストの注に書いた)。変異なしの木は 852/852・警告 0。 |
    | P1 (RU2-01) | (9) と UPSTREAM_REVIEW の 2 か所が「Mac の最初の Core に最も近い本家の版は 10-07 06:25 の `b546845`」と 1 つに言い切っている。中身が同じファイルの数が同じ (40 のうち 29) 版は 6 つ (10-06 12:58〜10-07 06:25) あり、`b546845` はその最後。あいだの #37 は Core の `CompositionController.cs` を変えるのに、Mac の記録に無く、窓の前の数にも入っていないので、その数は上限にならない | **直した** (文書)。(9) に書き足した: この 6 つの版のあいだに入った merge 5 (#33・#37・#49・#50・#91) のうち、Core (テスト以外) に触るのは #37 (`97a9640`、10-06 15:43、再変換) だけで、これは下の数に入っていない: Mac の最初の Core の `CompositionController.cs` は #37 の前とも後とも違い、#37 が足した再変換の形 (`GetReconversionSelection`・`TryReplaceSelection`) は無い (Mac の最初の Core の再変換の入口は、別の名前の `ReconvertKana`)。#37 が足した 25 字以上の行 23 のうち Mac にある 3 行は、#37 の前のファイルにもある行。Mac の記録に #37 の番号も hash も枝の名前も出てこない (12 回目の査読 RU2-01) (行の数え方と再変換の入り方の言い方は、13 回目の査読 RW4-01・02 で (9) を直した)。UPSTREAM_REVIEW の §0 の範囲の外の段落と訂正の表、追従表の §3 の判断待ちも、同じ数の 6 つの版 (最後が `b546845`) と書き、判断待ちの選び方 (6 つの最初の版のあとからなら #37 も入る) を書いた |
    | P1 (RU2-02) | 文書を作る手順 (`build_docs12.sh`) の置き場所の記号の守りが、新しい文書 (untracked の UPSTREAM_REVIEW) を intent-to-add にする前に流れていて、いちばん大きい新しい文書を見ていなかった (今の文書に記号は無く、実害は無い) | **直した** (リポジトリの外の道具)。新しい手順 (`build_docs13.sh`) では、intent-to-add のあとに探す (探した新しい文書の名前も出す) |
    | P1 (RU3-01) | 文書の例の網 (`docs_net.py`) が、コード片の組を重ならない一致で作るので、前のコード片が次の入力を結果として飲み込み、その例を拾わない (`peru-wo`・`peru-desu`・`peru-ga`、`doumolistaa` など) | **直した** (リポジトリの外の道具)。1 行のコード片を順に取り出して、隣り合う組を作る (前の組の結果のコード片も、次の組の入力にする)。land8 (`4ab9f6b`) の文書で、直した網は 106 入力を拾い (直す前は 83。前に拾った入力はどれも拾う)、新しく拾った 23 入力のうち 13 はどの一覧にも無かった (4 は照合の一覧に足し (`.afurka desu`・`cye`・`doumolistaa`・`peru-ga`)、9 は理由を付けて外した (記号・道具や関数の名前・並びの説明・結果の並び、試験用の判定器の `cadentsuru`、ライブ変換と変換エンジンが要る `na~~~`・`12gatsu`))。照合の一覧には、新しく拾った例と、査読員が挙げた照合していなかった主張のために CASES 25 行・PRE 2 行・COMMIT5 14 行・SHOW5C 2 行を足し、木の並びに P46-42 の前の木を足した。期待値 (入力, モード, 値) は、land8 の文書とこの直しのあとの一覧で 258 組 (文書のどこかに字として出てくる (ほかの語の一部でもよい) のは 222、出てこない 36 は、どれも木の答えを写したものの一覧 (`FROM_TREE`。37 組で、1 組はほかの語の一部として出てくる) にある)。このあと、P46-55 の例の行 (CASES 8・COMMIT5 10・SHOW5C 10) を足し、BI の取り込み前の答え 2 つ (RU3-05) も照合するようにしたので、作り直した文書では 277 組 = 文書のどこかに字として出てくる 241・出てこない 36 (13 回目の査読 RW2-01・RW3-01 で、数の名前と探し方を書き足した。探し方は 13 回目に強めた) (14 回目の注 (14 回目の査読 RX4-01): 上の文の「書き足した」は、正しくは置き換えた。12 回目に書いた文は「258 組 (文書に字として出てくるのは 222、木の答えを写したもの (`FROM_TREE`) は 36)」「277 組 = 文書に字として 241・`FROM_TREE` 36」で、外した入力の理由の分類に「結果の並び」は無かった (13 回目の査読 RW2-01・RW2-03 で、今の文に置き換えた)) (15 回目の注 (15 回目の査読 RY4-03): この置き換えの指摘は RW2-01・RW2-03・RW3-01 の 3 つで、上の 13 回目の注は RW2-01・RW3-01 を、この 14 回目の注は RW2-01・RW2-03 を挙げていた) |
    | P1 (RU3-02) | 網が、矢印の後ろがバッククォートなしの結果 (`cye` → ちぇ)・空白を含む入力 (`.afurka desu`)・数字で始まる入力 (`12gatsu`) を読めない | **直した** (同じ道具)。3 つの形も拾う (数字で始まる入力は英字を含むものだけで、7〜40 字の 16 進だけの並びは hash として除く)。網が見る範囲 (P46 が変えた・足した docs と mac の .md・.txt。コードの中の文は見ない) を説明に書いた (RU3-08) |
    | P1 (RU3-03) | 照合済みかを入力の単位で見るので、文が言う木・モード・確定か表示かのうち、照合していない主張が残っていた (`.zoverdesu` の表示、`_kyoumolinks` の確定、`datawo` の BI、`adtechnoon` など) | **足した** (照合の一覧)。査読員が挙げた主張を、文書の文を読んで、文が言う木・モード・確定か表示かのとおりに一覧に足した (どれも木の答えと合った。数は上の RU3-01 の行)。文が片方のモードでしか成り立たない 2 つは、文を直した (下の RU3-05)。照合済みかを主張の単位で見る仕組みは作っていない (入力の単位のまま。計画書の検証の段落に書いた) |
    | P1 (RU4-01) | 11 回目の P2 の数「13」(RT2 の「5」) が、報告の項目の数と合わない (RT2 の P2 の項目は RT2-03〜05 の 3 つで、RT2-05 の (a)・(b) を分けても 4。合わせて 12) | **直した** (11 回目の見出しと箇条、UPSTREAM_REVIEW の冒頭、追従表の更新の記録)。項目で数えて 12 とし、見出しの 5 の意味を 11 回目の見出しに書いた |

  - P2 27 件 (RU1 2・RU2 12・RU3 5・RU4 4・RU5 4) の対応 (すべて直した。RU1-02 は、査読済みの 19ad2ac の本文を書き換えないので、テストの注と f653fec の本文で補う。RU2・RU3 の多くは、リポジトリの外の道具の直し):
    - **テストと変異** (f653fec): RU1-02 lvpart と ovret の注の「査読員の入力 (56 入力と 420 入力。BI・NS) で答えが変わらず」は、両方の変異を両方の入力で流したように読める (流したのは lvpart が 56 入力、ovret が 420 入力) → テストの注を「前者は 56 入力、後者は 420 入力」と分けた (19ad2ac の本文の同じ言い方は、査読済みなので書き換えない)。RU1-03 規則を 7 文字以上の略語の直後だけに写す変異 (abbr7・lowabbr7・abbr7ov) は、略語に限らず 7 文字以上の大文字だけの語の直後で答えが変わる (査読員の 80 入力で、abbr7 は NS 24、lowabbr7 は NS 16、abbr7ov は BI・NS 24) → 7 文字の WARNING と 13 文字の UNDERSTANDING を、略語の例と同じ 6 つの形で足した (BI と NS で 5 つの木に流した答えも、略語の例と同じ形)。文字数を 14 にした写し方は捕まえていないことをテストの注に書いた。
    - **埋める文を作る道具の assert** (リポジトリの外。`fill12.py` が、前の回の数を出どころと照らし直す): RU2-03 (9) の「のべ 103」を assert していない → 数と内訳を assert した。RU2-04 「変えた後の中身がどれも Mac の最初の Core の同じファイルと違う」は、Mac に無いファイル 11 を含む → 内訳 (Mac に無い 11・変える前の中身のまま 13・違う 79) を書いた。RU2-05 NET11 の 3 入力が照合の一覧にあることを assert していない → 11 回目の網と一覧の版で流し直して assert した。RU2-06 「10-07 06:25」を手で書いている → 本家の clone の時刻で assert した。RU2-07 `947ff64` は、規則のほかに、ファイルの頭の注 2 行と空行も消す → (9) に書いた。RU2-08 変異の本数 (P46-52 の 15 本・P46-53 の 20 本) の assert が定数どうし → 変異の検査のログの名前の集合で assert した。RU2-09 査読員 RS2 の数との照らし方が弱い → RS2 の出力の 3 つの一覧と、数え直した一覧を集合で比べた。RU2-10 手で書いた数 (依頼で渡された一覧の 6 件・ウォッチに出た 14 件・#560 の 1 回に 37 の PR・`b2e33ca` (#153)) を assert していない → 調査の文書・UPSTREAM_REVIEW・本家の clone と照らした。RU2-11 NET10 の「24 入力は木の答えを書いた例だった」は、数の式だけを確かめている → 10 回目の網と一覧で land6 に流し直し、前の網の 38 入力が、照合に足した 24 と、外した・調査時点の 14 に分かれることを集合で確かめた (「木の答えを書いた例」という性質は手の分類のまま)。
    - **作り直しと着地の手順** (リポジトリの外): RU2-12 着地の手順 (`land.sh`) は、引数が足りないと文言を出さずに終わり、第 3 引数に hash を渡しても通る → 引数の数と、第 3 引数が枝であることを確かめる (試し 15 通り: 前の 10 通りと、引数 0・1・5 個、第 3 引数に hash、無い枝)。RU2-13 文書を作る手順が、作業ツリーの場所が記号リンクかを見ず、作業ツリーの登録を大文字小文字を区別して照らす → 記号リンクなら止め、照らし方を `final_merge.sh` とそろえた。RU2-14 記録の「試しの先端」は、試しの枝を消すとたどれない → 比べた木の hash を記録に書いた (どれも final/p46 の同じ順のコミットの木なので、final/p46 からたどれる)。
    - **文書の例の道具** (リポジトリの外): RU3-04 照合の一覧の期待値のうち 34 個は、文書に字として無い (木の答えを写したもの) → 期待値が文書に字として出てくるか、木の答えを写したものの一覧 (`FROM_TREE`) にあることを、文書を作るたびに確かめる (数は上の RU3-01 の行)。RU3-05 モードを書いていない「取り込み前は …」の答えが NS でしか成り立たない 2 つ (`doumolistaa` (5 回目の表の RJ-02 の行。正しくは 4 回目の表。13 回目の査読 RW5-02) と K88 (2) の `kyouhaadtechwo`) → どちらも NS の答えだと書き、BI の答えを書いた。RU3-06 候補の確かめは、変換エンジンを通さない試験用の画面の候補だけを見る → 計画書の検証の段落にそう書き、文書に書いていない確認用の入力を、CASES のほかの一覧 (PRE・SHOWN5・COMMIT5・SHOW5C・CANDS) でも数えるようにした (前後が英数字でない所に出てくるかで見る。部分の一致だと、短い入力が別の語の中に当たる)。RU3-07 文書のコミット本文を作る道具の、assert していない手書きの数 (4 つ・2 件・1 件・P46-1〜54 など) と、件数の assert が `or` でつないだ形 (どちらか一方に当たれば通る) なこと → 新しい道具 (`docsmsg13.py`) では、本文の数を作り直した文書の同じ言い方と照らし、件数はその回の行で照らす。RU3-08 網の範囲が、査読の依頼の「docs と mac」より狭い → 範囲を、P46 が変えた・足した docs と mac の .md・.txt にそろえ、説明に書いた。
    - **計画書と UPSTREAM_REVIEW** (文書): RU4-02 「上限の目安」の理由が時刻の事実を使っていない → (9) に、40 の変更のうち Mac の最初の Core より前に入ったのは #90 だけで、中の Core のコミットがそれより前のものも 5 つ (#90 と、調査に番号のある #10・#32・#86・#87) だけ、記録の無いもので最初の版に入りえたのは #90 だけ (そのファイルは #90 の前の中身) と書き、上限の目安とする理由を「Mac がのちに記録なしで同じ直しを入れた場合を除いていないため」にした。RU4-03・RU5-01 #476 の `a4b6f1a` の出てくる所に、本家の修正を探した範囲 (`a4b6f1a..45e14f8`) の起点と、P45 で同じ出力になるかを比べた本家の版が抜けている (11 回目の箇条の要約も報告とずれる) → 5 つの役割を書き (Mac の記録の 22 か所が、どれかに当たることを `fill12.py` が確かめる)、11 回目の箇条の要約を報告に合わせた。RU4-04 10 回目の表の RS2-02 の行に、古い範囲の言い方が注なしで残る → 注を足した。RU4-05 目的と追従表の 2 行 (と §3) の「調査の前に入って調査が選ばなかった PR」は、下の端を書かない → 「調査の前 (Mac の最初の Core に最も近い本家の版のあと) に入って」にした (6 つの版のどれのあとかは、13 回目の査読 RW4-05 で書き足した)。RU5-02 「最も近い本家の版 (`b546845`)」と縮めた言い方では、同じ数の版が 6 つあってその最後だと分からない → 上の RU2-01 と一緒に直した。RU5-03 #477 の「Mac は P11 待ち」は、10-15 の決定 (1-5 は作らない。代わりは K79) を落としている → (9) に書いた。RU5-04 D2 の注の「`8af9d19` と順番を決める」は、本家では順番が決まっている (`96d8228` は `8af9d19` の祖先で、Mac の Core は `96d8228` の前の形) ことを書いていない → (9) と訂正の表に書いた。
  - 実装者が自分で見つけたもの (12 回目の対応の中で。同じ波で直したか、記録した): (1) 13 文字の大文字だけの語は、はじめ INTERNATIONAL で試したが、頭が小文字の語の写し方 (lowabbr8〜14) は 8 文字から先で、略語の写し方の abbr13 も、赤にならなかった (最後の L と続く ament が、同梱の英単語の一覧 `english-words.txt` にある lament になるので、写し方で消した規則とは別の守りが効いたとみられる。確かめていない。13 回目の査読 RW1-03 で、別の守りではなく、同じ規則 (長い語の一部か) が 1 単位先で lament を見つけていたと分かった。13 回目の表の RW2-02 の行)。最後の字と ament で一覧の語にならない UNDERSTANDING (gament は一覧に無い) にして、どの写し方も 13 文字まで赤、14 文字で緑になった。(2) RU2-01 の #37 を確かめる中で、Mac の最初の Core には #37 とは別の名前の再変換の入口 (`ReconvertKana`) があると分かった ((9) に書いた。入口は `MeltypeSession.Reconvert` で、`ReconvertKana` はそこから呼ばれる中のメソッド。13 回目の査読 RW4-02 で (9) を直した)。(3) 作り直しの試しは、11 のコミット (統合の merge と P46-41・47〜55) の木と本文が今の final/p46 と同じで、P46-54 までの 10 の木は 11 回目の試しと同じ。
  - **13 回目** (12 回目の対応のあとに増えた P46-55・変異・足した例の表示 (RW1)、埋める文を作る道具の assert・作り直しと着地の手順・記録 (RW2)、文書の例の道具 (網・照合・確かめ・本文の道具。RW3)、IMPROVEMENT_PLAN の 12 回目の対応と前の回の箇条の直し・(9) の事実 (RW4)、追従表・UPSTREAM_REVIEW・KNOWN_LIMITS・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (RW5)): **RW1: P0 0・P1 2・P2 1、RW2: P0 0・P1 2・P2 5、RW3: P0 0・P1 2・P2 6、RW4: P0 0・P1 0・P2 5、RW5: P0 0・P1 1・P2 5** (合わせて P0 0・P1 7・P2 22。どの報告も、見出しの数と項目の数が合う。RW2-02 (P1) と RW5-03 (P2) は同じ f653fec の本文の指摘)。RW1 は、12 回目の変異 lvfrag が f653fec のテストで赤 (851/852) になることを自分の複製で確かめたうえで、自分で考えた変異 3 つを流し、2 つ (lvfraguu・lvnext) が緑のまま通って答えが変わることを見つけた (下の RW1-01・02。もう 1 つの lvfragi は赤)。表示を確かめる道具も流し直し (170 行)、確定と表示の食い違いが 0 なことを確かめた。RW2 は、埋める文を作る道具の assert を文の中の事実ごとに照らし (assert の無い事実を P2 に挙げた)、窓の前の数え直しと網の数を自分のスクリプトで出し直し、着地の手順の試し (15 通り)・作り直す手順の守りの試し (98 通り)・記録の最後の 2 段落 (20 項目) を確かめた。RW3 は、網・照合・確かめの道具を流し直し、壊した入力の試し 18 通りで止まらないものを見つけ (下の RW3-01・02)、期待値の抜き取り 22 組が、文が言う木・モードの答えと合うことを確かめた。RW4 は、12 回目の 34 件が対応表で漏れなく扱われていることと、「直した」の抜き取り 18 件を確かめ、(9) の事実を本家の clone と Mac から数え直して、どれも合うことを確かめた。RW5 は、追従表、変わっていないはずの 5 つの文書と 6 つのフォルダーの木、2 つのコミット本文、本家の事実 (本家の clone と GitHub の画面 5 ページ)、K88 (2) と RJ-02 の BI の答えを確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RW1-01) | 伸ばす音の う の直後の守りの例が o の段 (きょう・そう・きのう) だけで、u の段 (ゆう・くう・つう) の直後に限って断片を読みの頭として飛ばさない変異 (lvfraguu) が、全テストを緑のまま通る。12 回目の査読員の 143 入力で BI 9・NS 24 入力の答えが変わり、どれも取り込み前・main の答えに戻る (P46 の改善の一部が消える形) | **直した** (テストだけ。bedf9f7)。u の段の伸ばす音の直後の守りの例を足した: `yuupantsu` → `ゆうぱんつ`・`kuudentsu` → `くうでんつ` (同梱の表と NsLikeChecker の両方)、`yuushatsu` → `ゆうしゃつ` (NsLikeChecker だけ。同梱の表では、どの木もこの答え)。BI と NS で 5 つの木 (取り込み前・main・P46 だけ・P46-47 の前の統合・統合) に流すと、取り込み前と main は `ゆうpantす`・`くうでんtす` (NS では `ゆうshatす` も) で、P46 だけの木から今の答え (表示も確定と同じ)。変異の木 69 本 (P46-55 の 63 本と、13 回目の査読員の lvfraguu・lvfragi・lvnext と、同じ形を o の段・u の段の伸ばす音の片方だけに当てる lvfragou・lvnextuu・lvnextou。`CompositionText.cs` を 1 か所だけ変えた複製) で全テストを流し、足した 6 本はどれも赤 (851/852。変異の木の答えで、lvfraguu は `yuupantsu` が `ゆうpantす`、lvnext と lvnextou は `kyoucommitsuru` が `きょうこっみつる`、lvnextuu は `yuuchatsuru` が `ゆうちゃつる`、lvfragou は `kyoupantsuwokau`、lvfragi は P46-20 のテストの `waishatsu`)、P46-55 までの 63 本は P46-55 のテストのときと同じ (赤 51・緑 12)。P46-55 のテストでは、lvfraguu・lvnext・lvnextuu・lvnextou は緑 (852/852) だった。変異なしの木は 852/852・警告 0。 |
    | P1 (RW1-02) | 伸ばす音の う の直後に英単語が来る例 (`kyoucommitsuru` → `きょうcommitする`) が、どのテストにも無い。伸ばす音の う の直後を見る規則を 1 単位ずらした変異 (lvnext) が、全テストを緑のまま通り、査読員の 224 入力で BI 88・NS 88 入力を崩す (崩れる前の答えは 5 つの木で同じなので、P46 で生じた穴ではない) | **直した** (テストだけ。bedf9f7)。伸ばす音の う の直後の英単語の例を足した: `kyoucommitsuru` → `きょうcommitする`・`yuuchatsuru` → `ゆうchatする` (同梱の表と NsLikeChecker の両方。5 つの木で同じ答えで、表示も確定と同じ)。P46 の改善を守る例ではなく、P46-55 で足した守りの例の反対側 (英字に残す側) を見る例。変異の結果は上の RW1-01 の行 |
    | P1 (RW2-01) | 12 回目の表の RU3-01 の行 (網の数の文)・文書の確かめの道具・記録が「`FROM_TREE` 36」と書くが、`FROM_TREE` は 37 組で、36 は文書に字として無い期待値の数 (1 組 (`mentsu` の NS) は、ほかの語の一部として文書にある) | **直した** (文書と、リポジトリの外の道具)。12 回目の表の文に数の名前を書き足し (文を作る道具 `fill12b.py` が、37 組と、そのうちほかの語の一部として出てくる 1 組を assert する)、記録に訂正を書いた。12 回目の数 (277 組 = 字として 241 と 36) を求めていた確かめは外し、13 回目の規則 (下の RW3-01 の行) の数を確かめる |
    | P1 (RW2-02) | f653fec の本文の INTERNATIONAL のくだりは、緑のまま通った変異のうち abbr13 を落とし、確かめていなかった原因 (lament) を言い切りの並びで書いている (RW5-03 も同じ) | **記録を直した** (本文は査読済みなので書き換えない。正しい事実をここに書く): はじめの INTERNATIONAL の版のテストでは、頭が小文字の語の写し方 lowabbr8〜13 と、略語の写し方の abbr13 (頭が大文字の `Ament` の形) が緑のまま通った (本文は abbr13 を落としている。UNDERSTANDING の版では、どれも赤。14 文字の写し方は、どちらの版でも緑)。変異は `ament` (`Ament`) から始まる単位だけを飛ばし、長い語の一部かを見るループ (`IsPartOfLongerEnglishWord`) は、そのまま 1 つ前の L の単位まで伸びて、`Lament`・`LAment` を同梱の英単語の一覧 (`english-words.txt`) の lament として受け入れていた (別の守りではなく、同じ規則が 1 単位先で別の語を見つけた。13 回目の査読員 RW1 がコードを読んで確かめた)。変異 lowabbr8・abbr13 を、INTERNATIONAL と、最後の字だけ G にした INTERNATIONAG の同じ 5 つの形 (10 入力) に流すと、NS で答えが変わるのは INTERNATIONAG の形だけ (lowabbr8 は 3 入力、abbr13 は 2 入力。BI はどちらも 0)。12 回目の箇条の「実装者が自分で見つけたもの」の (1) にも、この行を指す注を足した |
    | P1 (RW3-01) | 期待値の出どころの確かめ (文書の確かめの 5) は、値が文書のどこかにほかの語の一部としてあれば通り、入力・モード・木と結び付かない (写しで「`cye` → ちぇ」を「ちぇえ」に変えても止まらない。値が入力の名前そのものに含まれる 38 組は必ず通る) | **直した** (リポジトリの外の道具と、書き方)。期待値は、文書のその入力を書いた行に、区切りのある字として書いてある (値が入力と同じなら、入力とは別にもう 1 回書く) か、木の答えを写したものの一覧 (`FROM_TREE`) にあるかの、どちらか一方 (重なり 0) であることを確かめる (前は、文書のどこかにほかの語の一部としてあれば通った)。逆向きに、網が拾った組の結果が、その入力の照合の一覧の値のどれかと同じか、理由つきで外す一覧 (`PAIR_EXEMPT`。理由の頭は、変異の木の答え・残していない木の答え・再現しなかった答えのどれか) にあることも確かめる (ある行の結果を書き誤ると、同じ値が別の行にあっても止まる)。網は、並べた入力と並べた結果を順に 1 対 1 で組み、数が合わない並びは組まない (前は並びの隣同士も組み、追従表の `mentsu`・`kyouhashatsu` の 2 つの結果を取り違えるなどの組み違えがあった)。land9 (`c32bb1a`) の文書では、網は 112 入力を拾い (照合の一覧にあるもの 79)、逆向きの組 116 は、一覧の値と同じ 111・`PAIR_EXEMPT` 5 (4 組) で、止まる組は 0。文が残してある木の答えを言っている 1 組は照合の一覧に足し (`kyouhaempowermentsuru` の NS の P46-47 の前の統合)、変異の木の答えなどの 4 組は `PAIR_EXEMPT` に理由つきで足した。値を書いていなかった 15 組は書き方を直し (下の「実装者が自分で見つけたもの」の (1))、文が答えを書いていない 3 組は注を付けて `FROM_TREE` に足した。写しの文書と一覧を 1 か所ずつ壊して確かめを流す試しでは、RW3 の試しで止まらなかった 6 通りと、ある行の結果だけを書き誤る試し 5 通り (追従表の「`cye` → ちぇ」を「ちぇえ」に、など) が止まり、正しい文書は通る。行の単位で見るので、同じ行に入力が 2 回あると、値が入力と同じ組は書いたものとして通る (残る限界)。作り直した文書の分け方: 期待値 295 組 = 入力と同じ行に区切りのある字として書いたもの 257 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0) (14 回目の注 (14 回目の査読 RX3-01・RX4-02): 逆向きの確かめで止まるのは、網が組にした結果だけ (入力のコード片と隣り合わない結果は組まないので、同じ値がほかの行にあれば通る)。網は、並べた入力のあとの結果が 1 つなら、並べた入力のどれにも組む。検証の段落を直した) (15 回目の注 (15 回目の査読 RY3-03・RY4-02): 上の 14 回目の注の、ほかの行に同じ値があれば通るという言い方は逆で、入力の無いほかの行に同じ値があっても止まる。通るのは、入力を書いた行に、同じ値が別の主張 (変異の木の答えなど) として出てくるとき。網が組まない結果には、隣り合うコード片でも、あいだが 24 字を超えるものもある。検証の段落を直した) |
    | P1 (RW3-02) | 網は、入力のコード片のあとの括弧の中に「かな → かな」と書いた形を拾わない (K88 (1) の `mafyu`・`ayafya` がこの形で、照合の一覧にたまたまあったので照合済みだった) | **直した** (リポジトリの外の道具)。括弧の中の矢印の右の結果、並べた入力に続く矢印、漢字だけの結果、ASCII で書いた矢印も拾う。land9 (`c32bb1a`) の文書で新しく拾った入力は、`mafyu`・`ayafya` (照合の一覧にある) と、計画書の件名の頭の `P46-` (理由つきで外す一覧に足した)。拾わない形 (コードブロックの中・表の隣の欄の裸の結果・括弧の中の漢字だけの結果) は検証の段落に書き、例はその形で書かない (14 回目の注 (14 回目の査読 RX4-03): 上の入力は手伝い NET13 の直しのときのもの。NET13b が並びの組み方を直したあとの網では、land9 の文書で拾う入力は 112 (12 回目の網は 111) で、ほかに `homeruyo` (理由つきで外す一覧にある) と `new` (調査時点のまま残した本文の範囲にある) も新しく拾い、4 つの入力を拾わなくなった (`a`・`candidates.txt` と、照合の一覧に残る `imaaldeamentsuru`・`peru-ga`。後ろの 2 つは、網が組まないので、その行に書いた結果は逆向きの確かめに入らない)) (15 回目の注 (15 回目の査読 RY2-05): 13 回目の網は、括弧の中の ASCII の矢印を拾っていなかった (14 回目の査読 RX4-02 (b) で拾うようにした)。1 つのコード片の中の ASCII の矢印も拾っていなかった (15 回目の査読 RY3-01 で拾うようにした)。拾わない形の「コードブロックの中」は言い過ぎで、網はコードブロックの中と外を見分けず、コードブロックの中でもバッククォートで囲んだ例は拾う (15 回目に実装者が見つけた)) (16 回目の注 (16 回目の査読 RZ3-02・RZ4-01): この行の拾わない形の並びは足りず、網は、括弧の中に漢字・空白・「、」が 1 字でもある結果と、矢印の無いつなぎの後ろの裸の結果も拾わない。検証の段落は、拾う形を並べる書き方に直した) |
    | P1 (RW5-01) | 追従表 (P46-47〜55 の行) と UPSTREAM_REVIEW (P46-55 の行) は、P46-55 を「7 文字以上の大文字だけの語の例を足し」「(… 7 文字以上の大文字だけの語) 埋め」と書くが、足したのは 7 文字 (WARNING) と 13 文字 (UNDERSTANDING) だけで、14 文字以上は例が無く、その写し方の変異 (abbr14・lowabbr14・abbr14ov) は緑のまま (計画書の表と件名は「7・13 文字」と正確) | **直した** (文書)。2 か所とも「7 文字・13 文字の大文字だけの語」と書き、14 文字以上の語の例は無いことを書いた |

  - P2 22 件 (RW1 1・RW2 5・RW3 6・RW4 5・RW5 5) の対応 (すべて直した。RW5-03 は上の RW2-02 の行で直した。RW2・RW3 の多くは、リポジトリの外の道具の直し):
    - **テストと変異の記録**: RW1-03 12 回目の箇条の (1) の「写し方で消した規則とは別の守りが効いたとみられる。確かめていない」は、確かめると、別の守りではなく、同じ規則 (長い語の一部か) が 1 単位先で lament を見つけていた → 事実を上の RW2-02 の行に書き、12 回目の箇条の (1) に注を足した。
    - **埋める文を作る道具・手順** (リポジトリの外): RW2-03 `fill12b.py` は、外した入力の理由を EXEMPT の理由の一覧に当てて確かめると書くが assert が無く、文の分類に「結果の並び」が抜けていた → 外した理由を 6 つの分類 (記号・道具や関数の名前・並びの説明・結果の並び・試験用の判定器・ライブ変換と変換エンジン) に当てて assert し、文に「結果の並び」を足した。RW2-04 `p55fill.py` の事実 (P46-54 までの赤と緑・CAPS7 の例・1 か所だけ変えた複製・8 語 × 形 10) に assert が無い → 変異のログ・測りの json・変異の木と元の差 (差の塊 1 つ)・P46-54 のコミットのテストで assert した (書く文はバイトで同じ)。RW2-05 `fill12.py` の事実 (#37 が下の数に入らないこと・1-5 を決めた日・記録の無い 19 と #37 が #211 以下なこと・`a4b6f1a` が #476 の merge なこと・10-11 の調査の番号・P46 の先端との照合) に assert が無い、または弱い → それぞれ assert した (書く文は同じ)。RW2-06 `docs_frag_wave12.py` が押さえる 12 回目の表の数は 163 のうち 59 で、照らすログの行の一部は値に関係なく出る決まった文 → 説明をその数に直し、計算した値の行と、結果のログ (着地の手順の試しの 15 通りが OK・NG 0) を見るようにした。この 13 回目の箇条の数は、番号・hash・置き場所を除いて、どれも `docs_frag_wave13.py` が査読の報告・ログ・同梱の一覧と照らす (照らしていない数があれば止まる。14 回目の注 (14 回目の査読 RX2-03・RX4-05): 止まるのは、数が道具に登録した句の外にあるときだけで、登録した句の中の事実を報告・ログと照らしていない句があった (RX2 の数え方で 25 個の数。多くは (9)・(1) のような項目の番号))。RW2-07 作り直す手順の記号リンクの守りが、パスの最後の部分しか見ない・使い方の注の道具の列に `fill12b.py` が無い・着地の手順が空の第 3 引数を既定の枝として通す → 新しい作り直す手順 (`build_docs14.sh`) は、パスのどこかに記号リンクがあれば止め (実体のパスと比べる)、注の列を足した。着地の手順は、空の第 3・第 4 引数で止まる。
    - **文書の例の道具** (リポジトリの外): RW3-03 網が拾わないほかの形 (括弧の中の矢印・漢字の結果・ASCII で書いた矢印・コードブロック・表の欄など。今の足した行に実例は無かった) → 拾う形を広げ (上の RW3-02 の行)、拾わない形は検証の段落に書いた。RW3-04 「文書」の範囲が道具ごとに 3 通りで、P46-22 が変えた `mac/homebrew/README.md` が古い言い方の確かめの外にあった → 範囲を 1 つの関数 (`docs_scope.py`) にまとめ、網・文書の確かめ (古い言い方と期待値)・数を出す道具がそれを使う。RW3-05 照合の一覧の行を消しても、入力と期待値の組が変わらなければ止まらない → 一覧の行の数を、数を出す道具の数と比べて止める。RW3-06 確かめの 1 の止まる文が、一覧の入力の数を出さない → 両方の数を出す。RW3-07 本文の道具 (`docsmsg13.py`) の説明の「確かめていない手書きの数」の一覧が足りない → この文書のコミットの本文の道具 (`docsmsg14.py`) は、本文に書く文書の番号 ((8)・(9)・K88 (5)・K89 (4)・追従表の §2 と §3 など) が文書にあることを assert し、assert しない手書きの数は説明の一覧に並べる。RW3-08 追従表の「`mentsu`・`kyouhashatsu` は日本語のまま」は、NS の取り込み前と main の答え (`mentす`・`きょうはshatす`) とは違うのに、`FROM_TREE` が注なしで受け入れていた → 追従表の文に、P46 だけの木・P46-47 の前の統合と同じことと、NS の取り込み前と main の答えを書いた (`FROM_TREE` から外し、文書に書いた答えとして照合する)。
    - **計画書** (文書): RW4-01 (9) の「#37 が足した 25 字以上の行 23」は、数え方を書いておらず、同じ (9) の #560 の照合 (注を除く) と違う数え方 (空白を全部除いた字数で、注の行も入れる)。「Mac にある 3 行」の中身は 2 種類 → #560 と同じ数え方 (前後の空白を除いて 25 字以上・注の行を除く) で数え直し、数え方と中身を (9) に書いた (Mac にある 3 行は変わらない)。RW4-02 (9) の「Mac の最初の Core の再変換の入口は、別の名前の `ReconvertKana`」は、入口の言い方が不正確 (入口は `MeltypeSession.Reconvert` で、`ReconvertKana` はそこから呼ばれる `CompositionController` の中のメソッド) → (9) を直し、12 回目の箇条の (2) に注を足した。RW4-03 11 回目の表の RT2-01 の行は、11 回目に書いた文を 12 回目の内訳で置き換えていて、その回に何と書いたかが読めない (ほかの行は元の文を残して注を足す形) → 11 回目の文に戻し、12 回目の内訳は注にした。RW4-04 検証の段落の中で「5 つの木」が 2 つの意味になる (表示を確かめる行の 5 つは P46-42 の前を含み、P46-47 の前の統合を含まない) → 表示の行の 5 つの木の名前を書いた。RW4-05 目的と追従表の 3 か所の「最も近い本家の版のあと」は、最も近い版が 6 つあるのに、どの版のあとかが分からない → 最も近い版は 6 つあり、どの版のあとからかで #37 が入るかが変わることを書いた。
    - **追従表・UPSTREAM_REVIEW・記録の行** (文書): RW5-02 12 回目の箇条の「5 回目の表の RJ-02 の行」は、4 回目の表の誤り → 注で直した。RW5-04 11 回目の「P2 12」は、報告の見出しの和 (13) と違う数え方なのに、追従表と UPSTREAM_REVIEW に注が無い → 2 か所に注を足した (land9 のコミットの本文は書き換えないので、この文書のコミットの本文に書く)。RW5-05 UPSTREAM_REVIEW の訂正の表で最初に出る「同じ数の 6 つの版」は、何の数かを書いていない → 「中身が同じファイルの数が同じ 6 つの版」と書いた。RW5-06 `doumolistaa` の確定で、打った `ta` `a` が `a` `た` の順に入れ替わる (BI は取り込み前から) のに、Q の行にも K の行にも記録が無く、表示が打った順のままなことも書いていない → 追従表の §2 に Q15 (記録だけ) を足し、5 つの木の確定と表示を照合の一覧に足した。
  - 実装者が自分で見つけたもの (13 回目の対応の中で。同じ波で直したか、記録した): (1) 13 回目の規則で期待値の出どころを確かめ直すと、文が答えを言葉 (「のまま」「も同じ」「のように戻っていた」「直る」) で決めていて値を書いていない組が 15 あった → 書き方だけを矢印の形に直した (6 回目の表の RO2-01 の行と、K87・K88 (1)・(2)。言っていることは同じ)。文が答えを書いていない 3 組は、注を付けて `FROM_TREE` に足した。(2) 変異は、査読員の 3 つに加えて、o の段・u の段の伸ばす音の片方だけに当てる形 (lvfragou・lvnextuu・lvnextou) も流した (上の RW1-01 の行。そこに書いた変異の木の答えは、文書の確かめの逆向きでは `PAIR_EXEMPT` に理由つきで入れた。14 回目の注 (14 回目の査読 RX3-04): 入れたのは 2 組 (lvnext・lvnextou の `kyoucommitsuru` と、lvnextuu の `yuuchatsuru`) で、lvfraguu の `yuupantsu` の組は、取り込み前・main の答えと同じ字なので、一覧の値と同じとして通る (どの木の答えかは見ない))。(3) 作り直しの試しは、12 のコミット (統合の merge と P46-41・47〜56) の木と本文が今の final/p46 と同じで、全テストは 852/852 (警告 0。14 回目の注 (14 回目の査読 RX4-04): 852/852 は P46-47〜56 の 10 の木で、統合の merge だけの木は 849/850、P46-41 の木は 851/851)。
  - **14 回目** (13 回目の対応のあとに増えた P46-56・変異・足した例の表示・INTERNATIONAL の事実 (RX1)、埋める文を作る道具の assert・作り直しと着地の手順・記録・断片の道具と本文 (RX2)、文書の例の道具 (網・範囲・確かめ・照合の一覧・照合) と壊した入力 (RX3)、IMPROVEMENT_PLAN の 13 回目の対応・前の回の箇条の直し・(9) と本家の事実・検証の段落と本文 (RX4)、追従表・UPSTREAM_REVIEW・KNOWN_LIMITS・変わっていないはずの文書と木・2 つのコミット本文・本家の事実と GitHub の画面 (RX5)、P46 で変えていない行 (main からある行) の例が統合した木でも正しいか (RX6。網の範囲の外で、13 回目までは誰も見ていなかった)): **RX1: P0 0・P1 2・P2 0、RX2: P0 0・P1 0・P2 6、RX3: P0 0・P1 1・P2 5、RX4: P0 0・P1 0・P2 5、RX5: P0 0・P1 0・P2 5、RX6: P0 0・P1 0・P2 1** (合わせて P0 0・P1 3・P2 22。どの報告も、見出しの数と項目の数が合う。RX2-03 と RX4-05 は、同じ道具の同じ言い過ぎの指摘)。RX1 は、自分の複製で変異なしの全テスト (852/852) と、13 回目の査読員の変異 lvfraguu・lvnext が bedf9f7 のテストで赤 (851/852) になることを確かめたうえで、自分で考えた変異 4 つを流し、3 つ (lvo・capsnw7・capsnw7u) が緑のまま通って答えが変わることを見つけた (下の RX1-01・02。もう 1 つの lvnextlone は緑だが、192 入力で答えの変わるものは無かった)。足した例の表示 (5 つの木 × NS・BI で、確定との食い違いは 0/50) と、lvnext で NS の答えが変わる 88 入力の途中の表示 (789 形) も見た。RX2 は、埋める文を作る道具の assert を文の中の事実ごとに照らし、着地の手順の試し (17 通り)・作り直す手順の守りの試し (98 通り)・本家の `a4b6f1a` が Mac の記録に出てくる 22 か所・変異の木 12 本を確かめた。RX3 は、網・照合・確かめの道具を流し直し、壊した入力の試し 41 通りと、壊した一覧での照合 5 通りで、止まらないものを見つけた (下の RX3-01 と、P2 の RX3-02・03)。RX4 は、13 回目の 29 件が対応表で過不足なく扱われていることと、箇条の数 28 個以上・「直した」の抜き取り 17 件を確かめ、(9) の #37 を本家の clone と Mac から数え直した。RX5 は、変わっていないはずの 5 つの文書と 6 つのフォルダーの木、2 つのコミット本文、本家の事実 (本家の clone と GitHub の画面 5 ページ)、5 つの木 × NS/BI × 確定/表示の答えを確かめた。RX6 は、範囲の文書のうち main にもある 36 の文書の、main からある行の 11,122 入力を main と統合した木に流し、答えが違う入力 (BI 99・NS 101) の出どころを読んだ (打つ例として書いた行のうち、統合した木で成り立たなくなったのは、下の RX6-01 の 2 か所だけ)。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RX1-01) | u の段の伸ばす音 (ゆう・くう・じゅう) を伸ばす音と見る分岐 (`IsLongVowelU`) を、どのテストも見ていない。この分岐を消した変異 (lvo) が全テストを緑のまま通り、査読員の 84 入力で BI 72・NS 72 入力の答えが変わる (どれも u の段の前置きの入力で、取り込み前・P46 だけの木の答えに戻る)。穴は main の `58ec05d` (本家 `7a20aff` を t + s の判定に絞った移植) からで、P46 で生じたものではないが、bedf9f7 の注と本文は u の段を名指ししている | **直した** (テストだけ。cf80890)。u の段の伸ばす音の直後に u で始まる語の頭が来る例を足した: `yuunitsuite` → `ゆうについて`・`juunitsuite` → `じゅうについて` (同梱の表と NsLikeChecker の両方)。BI と NS で 5 つの木 (取り込み前・main・P46 だけ・P46-47 の前の統合・統合) に流すと、取り込み前と P46 だけの木は `ゆunitsuite`・`じゅunitsuite` で、main・P46-47 の前の統合・統合は今の答え (表示も確定と同じ)。変異の木 72 本 (P46-56 の 69 本と、14 回目の査読員の lvo・capsnw7・capsnw7u。`CompositionText.cs` を 1 か所だけ変えた複製) を流し、足した 3 本はどれも赤 (全テストで 851/852。変異の木の答えで、lvo は `yuunitsuite` が `ゆunitsuite`、capsnw7 は `kyouhaHTTPSAPIamentsuru` が `きょうはHTTPSAPIあめんつる`、capsnw7u は `kyouhaHTTPSAPIAmentsuru` が `きょうはHTTPSAPIAめんつる`)、P46-56 までの 69 本は、P46-56 のテストのときと同じ (赤 57・緑 12。赤の 1 本目の例も同じ)。全テストで流したのは、変異なし (852/852・警告 0) と、足した 3 本と、P46-56 までの緑の 12 本 (852/852) で、赤の 57 本は、P46-47 のテストのあるクラス (61 本) だけを流した (P46-56 のテストのときの全テストでは、57 本とも赤はこのクラスの 1 本だけで、P46-57 が変えるのはこのクラスのファイルだけ)。P46-56 のテストでは、lvo・capsnw7・capsnw7u は緑 (852/852) だった。 |
    | P1 (RX1-02) | 7 文字以上の大文字だけの語の例が、辞書の語 (WARNING・UNDERSTANDING) だけ。規則を辞書に無い並び (略語) の直後だけに当てる変異 (capsnw7・capsnw7u) が全テストを緑のまま通り、NS で答えが変わる (査読員の 88 入力で NS 16、66 入力で NS 16。BI は 0) | **直した** (テストだけ。cf80890)。略語の長さごとの例の並びの最後に、辞書に無い 8 文字の HTTPSAPI と 9 文字の GITHUBAPI を足した (頭が大文字の語と頭が小文字の語の 4 つの形は NsLikeChecker、読みの頭になる断片の守りの 2 つの形は同梱の表と NsLikeChecker)。NS で 5 つの木に流すと、`kyouhaHTTPSAPIamentsuru` → `きょうはHTTPSAPIamentする` は、P46-47 の前の統合だけで `きょうはHTTPSAPIあめんつる` に崩れ、`kyouhaHTTPSAPIAmentsuru` → `きょうはHTTPSAPIAmentする` は、P46 だけの木と P46-47 の前の統合で `きょうはHTTPSAPIAめんつる` に崩れる (今の並びの略語の例と同じ形で、GITHUBAPI も同じ。同梱の表では、この 2 つはどの木でも崩れた答え)。変異の結果は上の RX1-01 の行 (15 回目の注 (15 回目の査読 RY1-01): P46-57 で捕まえた写し方 (capsnw7・capsnw7u) の文字数を 10・13 にしたもの (capsnw10・capsnw10u・capsnw13) は、辞書に無い並びの例が 8・9 文字だけなので、P46-57 の全テストで緑だった。この行と P46-57 のテストの注・本文は、13 文字まで捕まえると読めた。P46-58 で 7・10〜13 文字の並びを 1 つずつ足し、文字数を 2〜13 にした写し方はどれも赤になった (14 文字以上の並びだけに当てる写し方は、今も捕まえていない)) (16 回目の注 (16 回目の査読 RZ1-02): 上の 15 回目の注の括弧の中は、捕まえていないのが 14 文字以上だけと読めたが、そのときは、ちょうど 3〜5 文字の辞書に無い並びだけに当てる写し方と、並びが入力の頭から始まるときだけに当てる写し方も捕まえていなかった (P46-59 で捕まえた。今も捕まえていないのは、14 文字以上の並びだけ・kyouha と ima 以外の前置きだけ・ament と pant 以外の断片だけに当てる写し方)) (17 回目の注 (17 回目の査読 SA1-01): 上の 16 回目の注の、捕まえていない写し方の並びは、そのとき見つかっていたものだけで、全部ではなかった (語の頭が大文字の子音のときだけに当てる写し方と、A 以外の大文字の母音の語を、並びが 3 文字以上のときだけ見ない写し方なども、捕まえていなかった。P46-60 で捕まえた)。P46-60 のあとで見つかっている、捕まえていない写し方は、14 文字以上の並びだけ・kyouha と ima 以外の前置きだけ・断片が ment と pant 以外のときだけ・語がテストの語 (ament・Ament・Cement・Element・Underachievement) 以外のときだけに当てる写し方で、これで全部とは言えない) (18 回目の注 (18 回目の査読 SB1-01・02): この 17 回目の注の、捕まえていない写し方の並びも全部ではなかった (頭が A 以外の大文字の母音の語に、並びの長さや位置で条件を付けた写し方と、同梱の一覧に無い、頭が a 以外の小文字の母音 1 つの単位の語だけに当てる写し方も、捕まえていなかった。P46-61 で例を足して捕まえた)。P46-61 のテストの注は、置いた例の答えをどれも変えない写し方は捕まえない、と書く) |
    | P1 (RX3-01) | 逆向きの確かめの効き目の言い方 (13 回目の表の RW3-01 の行の「ある行の結果を書き誤ると、同じ値が別の行にあっても止まる」) が広すぎる。止まるのは網が組にした結果だけで、追従表の Q15 の「確定も表示も」のあとの値は、1 字変えても止まらなかった (網はこの値を `doumolistaa` と組まず、同じ値が計画書の 4 回目の表にあるので、期待値の出どころの確かめも通る)。検証の段落の、網が拾わない形にも、この形が無い | **直した** (文書)。13 回目の表の RW3-01 の行に注を足し (その回の文は残す)、検証の段落の逆向きの説明を網が組にした結果に絞って、拾わない形に「入力から離れた結果 (入力のコード片と隣り合わない結果)」を足した。Q15 の値は、どれも入力のあとの矢印の形 (`doumolistaa` → `どumolisたあ` など) で書き、網が組にして逆向きの確かめに入るようにした。作り直した文書の分け方: 期待値 317 組 = 入力と同じ行に区切りのある字として書いたもの 279 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0) |

  - P2 22 件 (RX2 6・RX3 5・RX4 5・RX5 5・RX6 1) の対応 (すべて直した。RX2-03 と RX4-05 は同じ直し):
    - **埋める文を作る道具・手順・記録** (リポジトリの外): RX2-01 本文の道具 (`docsmsg14.py`) の (9) の照らしが、計画書のほかの (9) でも通る (6 か所に当たる)。K88 (5)・K89 (4) も同じ形 → この文書のコミットの本文の道具 (`docsmsg15.py`) は、既知の穴の (9) の頭の句がちょうど 1 回あることと、K88 (5)・K89 (4) の項目の中に番号があることを照らす。RX2-02 本文の手書きの番号 (#636・#617・§6 と、その 2 つのコミットの日付) に assert が無く、確かめていない手書きの数の一覧にも無い → assert を足した。RX2-03・RX4-05 断片の道具の「13 回目の箇条の数 146 個をすべて照らした」は言い過ぎで、止まるのは数が登録した句の外にあるときだけ。句の中の事実を報告・ログと照らしていない句があった (RX2 の数え方で 25 個の数。多くは (9)・(1) のような項目の番号) → 13 回目の箇条の RW2-06 の行に注を足し、14 回目の道具 (`docs_frag_wave14.py`) は、句ごとに照らした先 (報告・ログ・一覧・リポジトリ) を持たせ、照らさない句は理由と一緒に一覧で出し、項目の番号は数から分けて数える。RX2-04 13 回目の RW2-07 の直しのうち「`final_merge.sh` とそろえて」の分が、直しにも直さないものにも無い → `final_merge.sh` にも、作り直す手順と同じ記号リンクの守り (パスのどこかに記号リンクがあれば止める) を入れた。RX2-05 記録 (`gated_evidence.txt`) の P46-56 の段落の「= 変異を流した複製の版」は、注の行が違う (埋めた 4 か所のほかに、lvfragi の注 1 行) → P46-57 の段落に訂正を書いた。RX2-06 12 回目の表の RU2-08 の行の直しの assert が、MUT53 の文の数と結ばれていない → `fill12.py` に足した (書く文は同じ)。
    - **文書の例の道具** (リポジトリの外): RX3-02 記録の許しは、許した回の箇条の全部で句を数えず、回数も見ない → 許した回の箇条での回数が、その句を直した回に査読された文書の同じ回の箇条での回数を超えたら止める。RX3-03 照合の一覧の行の数の確かめは、確定 (`COMMIT5`) と表示 (`SHOW5C`) の和しか見ない → それぞれを、数を出す道具の数と比べる (同じ値の行を確定と表示のどちらで確かめるかは、文書の文と結んでいない。検証の段落に書いた)。RX3-05 文書の確かめの道具の注の「7〜12 回目」 → 14 回目までに直した。RX4-02 (b) 括弧の中の ASCII で書いた矢印を拾わない → 拾う (今の文書に、この形は無い)。
    - **計画書** (文書): RX3-04 13 回目の箇条の「実装者が自分で見つけたもの」の (2) の、変異の木の答えを `PAIR_EXEMPT` に入れたという文は、3 組のうち 2 組にしか当てはまらない → 注を足した (lvfraguu の組は、取り込み前・main の答えと同じ字なので、一覧の値と同じとして通る)。RX3-06 追従表の `mentsu`・`kyouhashatsu` の木ごとの主張 (main・P46 だけ・統合の前) が、照合の一覧に無い → 5 つの木の確定の行を足した (NS と BI) (15 回目の注 (15 回目の査読 RY4-04): 直したのはリポジトリの外の照合の一覧で、この見出し (計画書) の下に置いたのは誤り)。RX4-01 記録の行の扱い (その回の文を残して、直しは注にする。13 回目の査読 RW4-03 で決めた) が、6 回目の表の RO2-01 の行と 12 回目の表の RU3-01 の行で守られていない (その回の文を、注なしで書き換えた) → どちらの行にも、その回に書いた文を引いた注を足した (RU3-01 の行の「書き足した」は、置き換えたの誤り) (15 回目の注 (15 回目の査読 RY2-06): 2 つの行は、本文を 13 回目に書き換えた文のままにし、その回に書いた文を注で引いた。その回の文を本文に残す決まりの例外)。5 回目の箇条の「最後は」は、回ごとに書き換える値だと書いた。RX4-02 (a) 網の説明の「1 対 1 で組む」は、並べた入力のあとの結果が 1 つのときに合わない (並べた入力のどれにも組む) → 13 回目の表の RW3-01 の行に注を足し、検証の段落を直した。RX4-03 13 回目の表の RW3-02 の行は、手伝い NET13b が組み方を直したあとの網で、land9 の文書で拾わなくなった入力を書いていない → 注を足した。RX4-04 13 回目の箇条の (3) の「全テストは 852/852」は、12 のコミットの全部の木の数と読める → 注を足した (852/852 は P46-47〜56 の 10 の木)。
    - **追従表・UPSTREAM_REVIEW・既知の穴** (文書): RX5-01 11 回目の注の「項目で数え直した」は、項目の番号で数えると 11 で、12 にならない (12 は RT2-05 の (a)・(b) を 2 つに数えた数) → 追従表と UPSTREAM_REVIEW の注に、その数え方を書いた (13 回目の文書のコミットの本文は書き換えないので、この文書のコミットの本文に書く)。RX5-02 11 回目の重なり (P1 の RT3-01 と P2 の RT2-04 は同じ #477 の指摘) の注が無い → 足した。RX5-03 Q15 の「P46 の 13 回目の査読で見つけた」は誤りで、計画書の 4 回目の表の RJ-02 の行に NS の main の形として記録があり、BI では取り込み前からあることは 12 回目の査読 RU3-05 が書いた → Q15 の出どころを直した (13 回目の文書のコミットの本文の同じ言い方は、この文書のコミットの本文で直す)。RX5-04 追従表の §3 の 1 つの欄に、最も近い版が 6 つあることが 2 回出る → 前の 1 つを短くした。RX5-05 (9) の「そのうち Mac にある 3 行」は Mac の最初の Core の数で、今の main と P46 では、Mac の P36 (本家 #332 の改良導入) が足した 1 行も #37 の行と同じ形で 4 行 → (9) を直した。
    - **main からある行** (文書): RX6-01 計画書の P46 より前の 2 か所 (Q9 の「限界・未確認」の (4) と、P25〜P28 の「未解決」の #249) が、P46-6 で直った形を、誤り・未解決と書いたままで、P46 の行とつながっていない → P46 の表のあとに、つながりの文を足した (古い 2 か所は、その時の記録として変えない)。
  - 実装者が自分で見つけたもの (14 回目の対応の中で): (1) RX5 が出し方を見つけられなかった、12 回目の表の RU2-01 の行の #37 の長い行の数 23 は、12 回目の数え方 (空白を全部除いた字数で、注の行も入れる) で出る (13 回目の箇条の RW4-01 の行に書いた数え方。`fill13.py` が assert している)。
  - **15 回目** (14 回目の対応のあとに増えた P46-57・変異・足した例と、14 回目の表の P46-57 の行の事実・作り直しの試し (RY1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具の数の照らし (RY2)、文書の例の道具 (網・確かめ・照合の一覧に足した行) と壊した入力・網の数 (RY3)、IMPROVEMENT_PLAN の 14 回目の表と箇条・前の回の箇条への注・(9)・検証の段落・P46 の本文と P46 より前の記録とのつながり (RY4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実と GitHub の画面 (RY5)。14 回目の査読員が確かめられなかったものは、どれかの分担に名前を付けて入れた): **RY1: P0 0・P1 1・P2 2、RY2: P0 0・P1 0・P2 6、RY3: P0 0・P1 0・P2 3、RY4: P0 0・P1 0・P2 5、RY5: P0 0・P1 0・P2 4** (合わせて P0 0・P1 1・P2 20。どの報告も、見出しの数と項目の数が合う。RY2-01 と RY4-01 は、同じ道具の同じ照らしの指摘)。RY1 は、自分の複製で変異なしの全テスト (852/852) と、P46-57 で捕まえた 3 本 (lvo・capsnw7・capsnw7u) と P46-56 までの赤から選んだ 3 本 (up1・lowabbr7・lvnext) が cf80890 のテストで赤 (851/852) になることを確かめたうえで、capsnw7 の文字数を変えた変異を流し、10 文字以上にしたもの (capsnw10・capsnw10u・capsnw13) が全テストを緑のまま通ることを見つけた (下の RY1-01)。足した 14 入力の答えは、5 つの木 × BI・NS × 確定・表示の 140 通りで手伝いの測りと同じで、作り直しの試しの記録 (13 のコミットの木と本文が同じ) もログと合った (16 回目の注 (16 回目の査読 RZ2-02): 上の 140 は、14 入力 × 5 つの木 × BI・NS の数で、確定と表示は、どの組でも両方を比べた)。RY2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直してログと同じことを確かめ、#37 の行の数を別の差の取り方で数え直し (Mac の最初の Core で 3 行、今の木で 4 行)、断片の道具の数の網 (数 98 個・項目の番号 19 個) を、道具を流さずに再現した (下の RY2-01)。RY3 は、網の数 (128 入力・期待値 317 組・逆向き 150 組) と land10 の数との差を、14 回目に足した 6 入力と追従表の Q15 の 4 組で全部説明し、照合の一覧に足した 24 行を 5 つの木で流し直して全部同じことを確かめ、壊した入力の試し 20 通りと、断片の道具の写しでの試し 4 通りを流した (止まらないものは下の RY3-01・03)。RY4 は、14 回目の 25 件が対応表で過不足なく扱われていることと、「直した」の抜き取り 19 件・箇条の数の句 51 個を確かめ、(9) の #37 を本家の clone と Mac から数え直し、つながりの文の 5 つの木の答えを流し直し、14 回目に誰も見ていなかった大文字だけの語の単位 (1 字が 1 つの単位) を、統合した木の Core を使う小さなプログラムで確かめた。RY5 は、変わっていないはずの 6 つの文書と 6 つのフォルダーの木、2 つのコミット本文、14 回分の件数の並び (3 か所で同じ)、Q15・P46-57 の例・つながりの文の答え (5 つの木 × BI/NS × 確定と表示)、本家の事実 (本家の clone と GitHub の画面 7 ページ) を確かめた。P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (RY1-01) | 辞書に無い大文字だけの並びの例が 8 文字 (HTTPSAPI) と 9 文字 (GITHUBAPI) だけ。P46-57 で捕まえた写し方 (capsnw7・capsnw7u) の文字数を 10・13 にした変異 (capsnw10・capsnw10u・capsnw13) が全テストを緑のまま通り、capsnw10 は査読員の 60 入力で NS 16 入力の答えを変える (BI は 0)。P46-57 のテストの注と本文、14 回目の表の RX1-02 の行は、13 文字まで捕まえると読める | **直した** (テストだけ。6acfe6d)。P46-47 のテストの、略語の長さごとの例の並びの最後に、辞書に無い大文字だけの並びを 7 文字 (JSONAPI)・10 文字 (HTTPSAPIJP)・11 文字 (NASDAQJPAPI)・12 文字 (HTTPSAPIJSON)・13 文字 (GITHUBAPIJSON) で 1 つずつ足した (今の 8・9 文字と合わせて 7〜13 文字が 1 つずつ。14 文字以上は足していない。テストの本数は 852 のまま)。6 つの形の答えは、5 つの木 × BI・NS × 確定・表示で、今の並びの略語と同じ形 (手伝い P58 が、使わなかった候補を含む 24 語 × 6 つの形 = 144 入力を流した)。変異の木 110 本 (P46-57 までの 72 本と、新しい族 38 本) を流し、写し方の文字数を K にしたもの (capsnwK・capsnwKu) は K = 2〜13 がどれも赤 (この査読員が P46-57 の全テストで緑と言った capsnw10・capsnw10u・capsnw13 は、capsnw13u とともに、P46-58 の全テストで 851/852)、K = 14 は緑 (14 文字以上の例は置いていない)、直前の大文字の並びがちょうど K 文字のときだけに当てる写し方 (capsnwKx・capsnwKux) は K = 7〜13 がどれも、ちょうど K 文字の例で赤。P46-57 までの 72 本は、P46-57 のテストのときと同じ (赤 60・緑 12。赤の 1 本目の例も同じ)。この査読員の数 (capsnw10 は 60 入力で NS 16・BI 0) は、変わった入力と答えまで再現した。テストの注の、13 文字まで捕まえると読める書き方は P46-58 で直し、P46-57 の本文は書き換えないので P46-58 の本文に書いた (14 回目の表の RX1-02 の行には注を足した) (16 回目の注 (16 回目の査読 RZ1-01・02): P46-58 のテストは、3 文字以上の並びでは頭が小文字の語の形にどれも前置きが付いていて (前置きの無い形は 2 文字の AIamentsuru だけ)、3〜5 文字の並びの例は辞書の語だけだったので、並びが入力の頭から始まるときだけに当てる写し方と、ちょうど 3〜5 文字の辞書に無い並びだけに当てる写し方を捕まえていなかった。P46-59 で、位置 3 × 語の頭 2 の形と、辞書に無い 3〜5 文字の並びの例を足し、どちらも赤になった) |

  - P2 20 件 (RY1 2・RY2 6・RY3 3・RY4 5・RY5 4) の対応 (すべて直した。RY2-01 と RY4-01 は同じ直し):
    - **テストとコミット本文** (6acfe6d): RY1-02 P46-57 のテストの注と本文の「7a20aff のテストの例は o の段の sounitsuite・hontounitsukareta・kaijounitsukuto だけ」は、本家 7a20aff のテストの 5 つの例のうち、伸ばす音の う の 3 つ (ほかの 2 つは kyouhacommitsuru・editsuru)。u の段の分岐も本家 7a20aff の `IsLongVowelU` にすでにあり、Mac には main の `58ec05d` で入った → P46-58 でテストの注を直した (P46-57 の本文は書き換えないので、P46-58 の本文に書いた)。
    - **埋める文を作る道具・手順・記録** (リポジトリの外): RY1-03 記録 (`gated_evidence.txt`) の P46-57 の段落は、変異をどれも、埋める所 4 か所が仮のままの複製で流したと書くが、全テストで流した緑の 12 本は、埋めたあとの複製で流した (注の行だけの違いで、答えには効かない) → P46-58 の段落に訂正を書いた。RY2-01・RY4-01 14 回目の断片の道具 (`docs_frag_wave14.py`) の「98 個は、句ごとに照らした」のうち 3 句は、照らす先が、木の並びが 5 つ以上あれば必ず真になる式か、報告ではなく照合の一覧の木の名前だった (句の事実は正しい) → 3 句の照らす先を報告の行と照合の一覧の行にし、関数だけで照らした句を一覧で出すようにした (15 回目の道具 `docs_frag_wave15.py` も同じ) (16 回目の注 (16 回目の査読 RZ2-03): 関数だけで照らした句のうち、(9) の行の数の句は、文書の文を、同じ文を作った埋める文と比べていただけだった (句の事実は、15 回目の (9) の直しで数え直した)。この句は、git で数え直す関数で照らすようにした)。RY2-03 本文の道具 (`docsmsg15.py`) は、本文が言う (9) の中身を、(9) の項目の中ではなく計画書の全体で照らしていた → この文書のコミットの本文の道具 (`docsmsg16.py`) は、(9) の項目の中で照らす。RY2-04 作り直す手順の守りの試し (`fmguard14.sh`) は、読むだけの場所に試しの写しを書いてから消すことを、注に書いていない → 15 回目の試し (`fmguard15.sh`) は、写しを自分の場所に書き、注にもそう書いた (16 回目の注 (16 回目の査読 RZ4-02): この「自分の場所」は、この回の実装者の場所 (`$W/rl10/`) のことで、試しを流す人の場所ではない。試しは、記号リンクも `$W` と `$W` の親に作って消す。道具の頭の注には、どちらも書いてある)。
    - **文書の例の道具** (リポジトリの外): RY3-01 1 つのコード片の中に ASCII の矢印で書いた組を、網が拾わない (検証の段落と道具の注は、ASCII の矢印も読むと書いている) → 拾うようにした (手伝い NET15 が直した。直す前と後で、land11 (`e5b38a0`) と land10 (`fe0a377`) の文書の網の入力・組・逆向きの対象は同じ (足した行で、1 つのコード片の中に ASCII の矢印があるのは計画書の 1 か所だけで、矢印の左がかなを含むので打つ入力に見えない)。壊した入力 33 通りの試しで、1 つのコード片の中の ASCII の矢印の結果を 1 字変えると、直した道具は止まり、直す前の道具は通ること、正しい結果は通ることを確かめた) (16 回目の注 (16 回目の査読 RZ2-01): 1 つのコード片の中に ASCII の矢印がある 1 か所の、矢印の左にあるのは漢字と記号だけで、かなは無い。打つ入力に見えないのは、左に打てない字 (漢字) があるため) (17 回目の注 (17 回目の査読 SA2-01・SA4-01): この 16 回目の注の、矢印の左の字の言い方も誤り。左には ASCII の英字と記号もあり、ASCII 以外の字が漢字と `・` だけ)。RY3-02 14 回目の表の RX1-02 の行の「GITHUBAPI も同じ」が、照合の一覧に無い → GITHUBAPI の 2 つの形の確定を、照合の一覧に足した (NS では、`kyouhaGITHUBAPIamentsuru` → `きょうはGITHUBAPIamentする` が P46-47 の前の統合だけで `きょうはGITHUBAPIあめんつる` に、`kyouhaGITHUBAPIAmentsuru` → `きょうはGITHUBAPIAmentする` が P46 だけの木と P46-47 の前の統合で `きょうはGITHUBAPIAめんつる` に崩れる。BI では、どの木も崩れた答え)。作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
    - **計画書** (文書): RY2-02 (9) の「4 行」の 4 つ目 (Mac の P36 が足した行) が、#37 の前のファイルには無いことを書いていない → (9) に書いた。RY2-05 13 回目の表の RW3-02 の行に、13 回目の網が括弧の中の ASCII の矢印を拾っていなかったこと (14 回目の査読 RX4-02 (b)) の注が無い → 注を足した (1 つのコード片の中の ASCII の矢印も、15 回目まで拾っていなかった)。RY2-06 14 回目の RX4-01 の直しは、6 回目と 12 回目の行の本文を 13 回目に書き換えた文のままにして、その回の文を注で引いた形で、決まり (その回の文を残す) の例外なのに、そう書いていない → 14 回目の箇条の RX4-01 に注を足した。RY3-03 13 回目の表の RW3-01 の行の 14 回目の注の「同じ値がほかの行にあれば通る」は、実際の動きと逆 (入力の無いほかの行に同じ値があっても止まる。通るのは、入力を書いた行に、同じ値が別の主張 (変異の木の答えなど) として出てくるとき) → RW3-01 の行に注を足し、検証の段落を直した。RY4-02 検証の段落の、網が拾わない「入力から離れた結果 (入力のコード片と隣り合わない結果)」は狭い (隣り合うコード片でも、あいだが 24 字を超えると組まない) → 検証の段落を直し、RW3-01 の行の注にも書いた。RY4-03 12 回目の表の RU3-01 の行の 14 回目の注 (RW2-01・RW2-03) と 13 回目の注 (RW2-01・RW3-01) が、同じ書き換えについて別の指摘を挙げる → 14 回目の注のあとに、その書き換えの指摘は RW2-01・RW2-03・RW3-01 の 3 つだと注を足した。RY4-04 14 回目の箇条の RX3-06 (照合の一覧に行を足した) が「計画書 (文書)」の見出しの下にある → 注を足した (直したのは、リポジトリの外の照合の一覧)。RY4-05 つながりの文が、同じ 2 か所に並ぶ `xmas` と `mocz` は統合した木でも変わらないことを書いていない → 書いた (5 つの木 × BI・NS の答えを照合の一覧に足した)。
    - **追従表・UPSTREAM_REVIEW** (文書): RY5-01 11 回目の重なりの注が 1 組 (P1 の RT3-01 と P2 の RT2-04) だけで、計画書の 11 回目の箇条にある 2 組目 (P2 の RT3-04 と RT2-05 (a)) が無い → 追従表と UPSTREAM_REVIEW に足し、計画書の 11 回目の見出しに注を足した (14 回目の文書のコミットの本文は書き換えないので、この文書のコミットの本文に書く)。RY5-02 Q15 の「出どころの言い方は 14 回目の査読 RX5-03・RX3-01 で直した」の RX3-01 は、出どころではなく値の書き方 (矢印の形) の直し → 書き分けた。RY5-03 Q15 が出どころに挙げた計画書の 4 回目の表の RJ-02 の行は「main の H1 (`a093319`) だけを入れた木」と書くが、4 回目の報告が流した木は main 全体 (NS) → RJ-02 の行に注を足した (その回の文は残す)。RY5-04 追従表の §3 の「どの版かは、この欄の後ろに書いた」は、版が 1 つに決まっているように読める (候補は 6 つで、どれにするかは判断待ち) → 言い方を直した。
  - 実装者が自分で見つけたもの (15 回目の対応の中で。手伝い NET15 の試し): (1) 検証の段落の、網が拾わない形の「コードブロックの中」は言い過ぎで、網はコードブロックの中と外を見分けず、コードブロックの中でもバッククォートで囲んだ例は拾う → 「コードブロックの中のバッククォートで囲まない例」に直し、13 回目の表の RW3-02 の行に注を足した。(2) 文書の確かめの最後 (作り直した文書の期待値の分け方の文がちょうど 1 回あること) は計画書の全体で数えていたので、最後の回の箇条に文が無くても、前の回の箇条に同じ数の文があれば通った → 最後の回の箇条の中で数えるようにした。(3) 文書の確かめの、回の箇条の切り方は、終わりの側を確かめていなかった (最後の回の箇条が計画書の次の節まで伸びても、ほかの確かめでは止まらなかった) → 箇条が計画書の次の節を含まないことも確かめるようにした。
  - **16 回目** (15 回目の対応のあとに増えた P46-58・変異の族・足した例と、15 回目の表の P46-58 の行と 14 回目の表への 15 回目の注の事実・作り直しの試し (RZ1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具 (RZ2)、文書の例の道具 (網・確かめ・検証の段落・照合の一覧に足した行) と壊した入力・網の数 (RZ3)、IMPROVEMENT_PLAN の 15 回目の表と箇条・前の回の箇条への 15 回目の注・(9)・検証の段落・P46 の本文と P46 より前の記録とのつながり (RZ4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実と GitHub の画面 (RZ5)。15 回目の査読員が確かめられなかったものは、どれかの分担に名前を付けて入れた): **RZ1: P0 0・P1 2・P2 0、RZ2: P0 1・P1 0・P2 10、RZ3: P0 0・P1 0・P2 3、RZ4: P0 0・P1 0・P2 3、RZ5: P0 0・P1 0・P2 2** (合わせて P0 1・P1 2・P2 18。どの報告も、見出しの数と項目の数が合う。RZ3-02 と RZ4-01 は、検証の段落の同じ並びの指摘)。RZ1 は、P46-58 の変更がテストのファイル 1 つだけであることと、15 回目の RY1-01・02 への対応を確かめ、自分の複製で、写し方の文字数を 10〜13 にした変異が全テストで赤 (851/852)、14 にした変異が緑 (852/852) であることを流し直し、手伝いが流した 144 入力を 5 つの木で流し直して全部同じ (1440/1440) ことを確かめたうえで、並びが入力の頭から始まるときだけに当てる写し方と、ちょうど 3〜5 文字の並びだけに当てる写し方が緑のまま通ることを見つけた (下の RZ1-01・02)。RZ2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直してログと同じことを確かめ、15 回目の断片の道具の写しで 9 通り (壊さないものを含む) を試した (下の RZ2-01〜11)。RZ3 は、壊した入力の試し 40 通り (壊さないものを含む) と、照合の一覧の全部での NS の照合 (NG 0) を流し、網の数の land11 との差を、15 回目に照合の一覧に足した行だけで説明した (下の RZ3-01〜03)。RZ4 は、15 回目の 21 件がどれも対応表にあり、「直した」が実際に直っていることを 21 件すべてで確かめ、(9) の #37 の行を数え直し、つながりの文の答えを 5 つの木で流し直した (下の RZ4-01〜03)。RZ5 は、15 回目に変えた追従表と UPSTREAM_REVIEW の文・2 つのコミット本文の数と本家の事実を、ログ・測り・本家の clone・自分で流した答えと照らし、変わっていないはずの 6 つの文書と 6 つのフォルダーの木と、GitHub の画面 4 ページを確かめた (下の RZ5-01・02)。P0・P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P0 (RZ2-01) | 15 回目の箇条の RY3-01 の行の、手伝い NET15 の結果の文は、足した行で 1 つのコード片の中に ASCII の矢印がある 1 か所について「矢印の左がかなを含むので打つ入力に見えない」と書くが、左にあるのは漢字と記号 (`・` など) だけで、かなは無い (打つ入力に見えないという結論は合う)。埋める文を作る道具の照らしが、漢字も受ける正規表現で、文より弱かった | **直した**。15 回目の箇条の RY3-01 の行に 16 回目の注を足した (打つ入力に見えないのは、左に打てない字 (漢字) があるため)。照らしは、かなと漢字を分けて数える形にし、land11 (`e5b38a0`) と land10 (`fe0a377`) の文書の両方で確かめた (この回の道具 `fill16.py`)。網の道具 (`docs_net.py`) の説明にも同じ言い方が残っていたので、直した (実装者が見つけた) (17 回目の注 (17 回目の査読 SA2-01・SA4-01): この行の、矢印の左の字の言い方は誤り。左には ASCII の英字 (`W`・`f`・`m`) と記号もあり、ASCII 以外の字が漢字 5 字と `・` 3 つだけで、かなは無い。網の道具の説明も直した) |
    | P1 (RZ1-01) | 3 文字以上の大文字の並びでは、頭が小文字の語の例にどれも前置き (kyouha・ima) が付いていて、並びが入力の頭から始まるときだけに当てる写し方 (capsnw7s・lowabbr3s) が全テストを緑のまま通り、NS の答えを変える | **直した** (テストだけ。364841f)。P46-47 のテストの略語の並びの語の形を、位置 3 (kyouha の後・ima の後・入力の頭) × 語の頭 2 (大文字・小文字) の 6 形にそろえた (入力の頭で頭が小文字の形と、ima の後で頭が大文字の形を足し、守りの形も ima の後に足して位置 3 つにした。テストの本数は 852 のまま)。足した形の答えは、5 つの木 × BI・NS × 確定・表示で、今の並びの略語の形と同じ (手伝い P59 が、使わなかった候補を含む 47 語 × 9 形 = 423 入力を流した。統合の NS は 2 回流して同じ)。この査読員の 12 本 (capsnw7s・lowabbr3s と、ちょうど 2〜6 文字の並びだけに当てる capsnw2x〜6x・capsnw2ux〜6ux) は、どれも P46-59 のテストで赤。この査読員の数 (256 入力で、capsnw7s は NS 26・lowabbr3s は NS 58・capsnw4x は NS 8、BI はどれも 0) は、手伝い P59 が流し直して同じ (17 回目の注 (17 回目の査読 SA2-02・SA4-04): 括弧の中の 47 語のうち JAXA は、NS の 2 つの木 (P46 だけ・統合の前) で、語の形の答えが今の形と違ったので、テストに使わなかった。テストに置いた 23 語 (今の 14 語と足した 9 語) は、5 つの木で今の形と同じ) |
    | P1 (RZ1-02) | テストの注の「長さごとに 1 つずつ置くのは、ちょうど n 文字の並びだけに当てる写し方も捕まえるため」と「14 文字以上の並びだけに当てる写し方は…捕まえていない」は、捕まえていないのが 14 文字以上だけと読めるが、辞書に無い 3〜5 文字の並びの例が無いので、ちょうど 3〜5 文字の並びだけに当てる写し方 (capsnw3x〜5x・capsnw3ux〜5ux) も緑。14 回目の表への 15 回目の注の括弧の中も同じ読み | **直した** (テストだけ。364841f)。並びに、辞書に無い 3・4・5 文字の JPN・QWER・JETRO と、大文字だけの辞書の語の 6・8〜12 文字の SYSTEM・PASSWORD・DIRECTORY・TECHNOLOGY・INFORMATION・RELATIONSHIP を足した (辞書に無い並びは 2〜13 文字、辞書の語の並びは 3〜13 文字が 1 つずつになった。2 文字の並びは、どの判定器でも辞書の語にならない。IsKnownEnglishWord は 3 文字未満の語を辞書の語としない)。写し方が差し込み先で条件にできる次元 (並びの長さ・辞書の語か・位置・語の頭) の組ごとに、ちょうど K 文字の並びだけに当てる写し方 (格子の族 150 本) を流し、置いた組と長さの 138 本はどれも赤、緑は 14 文字以上に当てる 6 本と、2 文字の辞書の語の並びだけに当てる 6 本 (条件が真にならないので、答えを変えない)。語の後ろで条件を付けた写し方は、開発機の NSSpellChecker と同梱の表で 423 入力の答えを変えない (後ろは差し込み先から見えないので、次元に入れていない)。テストの注は、捕まえていない写し方を、14 文字以上の並びだけ・kyouha と ima 以外の前置きだけ・ament と pant 以外の断片だけに当てるもの、と書き直した。開発機の NSSpellChecker は NASDAQ を辞書の語と答える (テストの NsLikeChecker では辞書に無い側。答えの形は同じ) (17 回目の注 (17 回目の査読 SA1-01・02): 上の行が写し方の次元として挙げた 4 つ (並びの長さ・辞書の語か・位置・語の頭) は、テストで選んだ次元で、写し方が見られる次元の全部ではなかった (語の頭は大文字か小文字かだけで、語の形の頭は a・A だけ)。語の頭が大文字の子音のときだけに当てる写し方は全テストを緑のまま通り、NS で答えが変わった (17 回目の査読員の 180 入力で NS 30)。テストの注の、捕まえていない写し方の断片の書き方も、語 (ament) と断片 (ment・pant) を分けていなかった。P46-60 で、頭が大文字の子音の語の形と、頭が A 以外の大文字の母音の語の例を足し、注を直した) |

  - P2 18 件 (RZ2 10・RZ3 3・RZ4 3・RZ5 2) の対応 (すべて直した。RZ3-02 と RZ4-01 は同じ直し):
    - **埋める文を作る道具・手順・記録** (リポジトリの外): RZ2-02 (道具の側) 15 回目の断片の道具の数の照らしに、必ず真になる式が 3 つあった (掛け算・足し算・語の文字数の式。どれもほかの照らす先があり、関数だけの句には入らない) → 外した (文の側は下の計画書の箇条)。RZ2-03 14 回目の断片の道具の、関数だけで照らした句のうち (9) の行の数の句は、文書の文を、同じ文を作った埋める文と比べていただけで、出力は「句ごとに照らした」のままだった (句の事実は、15 回目の (9) の直しで数え直した) → git で数え直す関数で照らすようにし、出力の「句ごとに照らした」数を、照らす先がファイルの句の中の数と、関数だけの句の中の数に分けた (14 回目・15 回目・16 回目の道具)。15 回目の箇条の RY2-01・RY4-01 の行に注を足した。RZ2-04 文書を作り直す手順 (`build_docs16.sh`) の、埋める文が無いときの止まる文言が、15 回目の埋める文の道具の名前を挙げていない → この回の手順 (`build_docs17.sh`) で挙げた。RZ2-05 記録 (`gated_evidence.txt`) の P46-58 の段落の「変異は、埋める所が仮のままの複製で流した」を確かめる時刻の照らし (埋める文の道具と記録の道具) は、埋めたテストのファイルではなく、本文の下書きの時刻と比べていた (結論は同じ) → 埋めたテストのファイルの時刻と比べて流し直した (この回の道具も同じ) (17 回目の注 (17 回目の査読 SA2-08): この括弧は違っていて、16 回目 (P46-59) の記録の道具は、切り替え表の木を作り直した時刻で照らした。埋める前と後のテストの差が注の行だけなことは、17 回目の道具で確かめた)。RZ2-06 期待値の分け方の文を書く道具 (`split15fill.py`) の「前の回の文は変えない」の確かめが、2 つの書き先の片方だけで、land10 の記録の文を land10 の文書でなく land11 の文書で照らしていた → この回の道具 (`split16fill.py`) は、2 つの書き先の両方で照らし、land10 の記録の文は land10 の文書 (`fe0a377`) で照らす。RZ2-07 記録 (`gated_evidence.txt`) の P46-58 の段落の、UpstreamP46Tests だけで流したテストの本数は、手で書いた数だった (値は合う) → 変異の測りの json と照らし、この回の段落にそう書いた。RZ2-08 前の回の文書のコミットの本文の道具 (`docsmsg16.py`) は、本文の「P45・P46 で中のコミットを個別に見た」の P46 の分を照らしていない → この回の道具 (`docsmsg17.py`) で照らす。RZ2-09 断片の道具の注が、道具の中に無い名前を引いていた → 見出しの名前に直した。RZ2-11 記録の land12 の段落の「1 回目の作り直しは、文書の確かめで止まった」は、残ったログも assert も無い (ログは 2 回目の作り直しで上書きされた)。直した 15 回目の注 (13 回目の表の RW3-01 の行) は、古い言い方の語順を入れ替えた形だった (中身は打ち消す文で、誤りではない) → この回の段落に、1 回目の出力を残していないことを書き、作り直しの手順は回ごとのログを残すようにした。語順を入れ替えた形も古い言い方の一覧に足した (許すのは、13 回目の表の、その注だけ)。
    - **文書の例の道具** (リポジトリの外): RZ3-01 網の説明と検証の段落は、ASCII の矢印 (`->`) も `→` と同じに読むと書くが、隣り合うコード片のあいだの 24 字の数え方では、`->` と後ろの空白を数えていた → `→` と同じに外すようにした (手伝い NET16 が直した。land12 (`baf1b40`) と land11 (`e5b38a0`) の文書の足した行には、あいだが ASCII の矢印で始まる隣り合うコード片は無く、直す前と後で網の入力・組・逆向きの確かめは同じ (land12 で 132 入力・組 195・逆向き 156、land11 で 128 入力・組 189・逆向き 150)。写しの文書を壊す試し 66 通りで、ASCII の矢印のあいだで書き誤った結果を、直す前の道具は通し、直した道具は止めること、同じ形の正しい結果はどちらも通ることを確かめた)。RZ3-02・RZ4-01 検証の段落の、網が拾わない形の並び (コードブロックの中のバッククォートで囲まない例・表の隣の欄の裸の結果・括弧の中の漢字だけの結果と、入力から離れた結果) が足りない (矢印の無いつなぎの後ろの裸の結果と、括弧の中に漢字・空白・「、」が 1 字でもある結果も拾わない。網の説明の「記号」は `.`・`@`・`_`・`-` だけ) → 検証の段落と網の説明を、拾う形を並べる書き方に直し、13 回目の表の RW3-02 の行に注を足した。RZ3-03 文書の確かめ (`docs_check.py`) は、15 回目の 2 つのファイル (直した古い言い方・記録の許し) を、無ければ黙って読まなかった → 無ければ止めるようにした (あるときだけ読むのは 16 回目の分から)。作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
    - **計画書** (文書): RZ2-02 (文の側) 15 回目の箇条の「5 つの木 × BI・NS × 確定・表示の 140 通り」は、書いた数を掛けると 280 になる (140 は 14 入力 × 5 つの木 × BI・NS の数で、確定と表示は、どの組でも両方を比べた) → 15 回目の箇条に注を足した。RZ2-10 (9) の #37 の「3 行」「4 行」は、#37 の足した行の側で数えた数で、Mac のファイルの中の行の数とは中身が違う (`StartConversion(preferJapanese: true);` の「2 回」は #37 の中の回数で、Mac の最初の Core のファイルの中では 1 回。今の木のファイルの中では `_spaceStartedConversion = false;` が 2 回) → (9) に、数えた側とファイルの中の回数を書いた。RZ4-02 15 回目の箇条の、作り直す手順の守りの試しの「自分の場所」は、試しを流す人の場所と読める → 注を足した (書くのはこの回の実装者の場所で、記号リンクは作って消す)。RZ4-03 4 回目の表の RJ-02 の行の 15 回目の注の P47 は、Mac の項目 P47 (日時の候補) で、P46 の節の中では P46-47 と取り違えやすい → 注を足した。
    - **追従表・UPSTREAM_REVIEW** (文書): RZ5-01 追従表と UPSTREAM_REVIEW の P46-57 の句は、7 文字以上の辞書に無い大文字だけの並びの例を足した (UPSTREAM_REVIEW は穴を埋めた) と読めるが、例は 8 文字と 9 文字の 2 つだけ → 限りを書き足した (計画書の本文の、何回目がどのコミットかを並べた文の同じ句も直した)。RZ5-02 UPSTREAM_REVIEW と文書のコミットの本文の 11 回目の注は、重なりの 2 組が、4 件で 1 つの指摘とも読める → 2 組だと読める言い方にした。
  - **17 回目** (16 回目の対応のあとに増えた P46-59・変異の族・足した例と作り直しの試し・記録 (SA1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具 (SA2)、文書の例の道具 (網・確かめ・検証の段落の文と網の動き・照合の一覧) と壊した入力・網の範囲 (SA3)、IMPROVEMENT_PLAN の 16 回目の表と箇条・前の回の箇条への 16 回目の注・(9)・検証の段落・P46 の本文と追従の文 (SA4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実と GitHub の画面 (SA5)。16 回目の査読員が確かめられなかったものは、どれかの分担に名前を付けて入れた): **SA1: P0 0・P1 1・P2 2、SA2: P0 1・P1 1・P2 6、SA3: P0 0・P1 1・P2 4、SA4: P0 1・P1 0・P2 3、SA5: P0 1・P1 0・P2 0** (合わせて P0 3・P1 3・P2 15。SA2 の報告は見出しの P2 が 7 で、項目は 6。ほかの報告は、見出しの数と項目の数が合う。SA2-01 と SA4-01 は同じ指摘 (P0) で、SA2-02 (P1) と SA4-04 (P2) も同じ指摘)。SA1 は、P46-59 の変更がテストのファイル 1 つだけであることと、16 回目の RZ1-01・02 への対応を確かめ、自分の複製で、16 回目に全テストで流していなかった写し方 8 本 (capsnw3x・3ux・4ux・5x・5ux・13u・7s・lowabbr3s) がどれも全テストで赤 (851/852) であることと、足した例の 108 入力を 5 つの木で流し直して全部同じ (1080/1080) ことを確かめたうえで、語の頭が大文字の子音のときだけに当てる写し方が全テストを緑のまま通ることを見つけた (下の SA1-01〜03)。SA2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直してログと同じことを確かめ、13 回目のあとの断片の写しで、14 回目の断片の道具を壊す試し 10 通り (壊さないものを含む) と、14〜16 回目の断片の道具を続けて流す試しをした (下の SA2-01〜08)。SA3 は、壊した入力の試し 31 通りと、照合の一覧の全部での NS の照合 (NG 0) を流し、網の数が land12 と同じことを、照合の一覧に足した行が無いことと、網の直しが網の入力と組を変えないことで説明した (下の SA3-01〜05)。SA4 は、16 回目の 21 件がどれも対応表にあり、「直した」が実際に直っていることを 21 件すべてで確かめ、(9) の #37 の行を数え直し、P46-6 の前後の木をビルドして、`Xcode` と #249 の入力を直したのが P46-6 であることを確かめた (下の SA4-01〜04)。SA5 は、16 回目に変えた追従表と UPSTREAM_REVIEW の文・2 つのコミット本文の数と本家の事実を、ログ・測り・本家の clone・GitHub の画面 10 ページと照らし、変わっていないはずの 6 つの文書と 8 つのフォルダーの木を確かめた (下の SA5-01)。P0・P1 の対応:

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P0 (SA2-01・SA4-01) | 16 回目の表の P0 (RZ2-01) の行と、15 回目の箇条の RY3-01 の行に足した 16 回目の注は、1 つのコード片の中に ASCII の矢印がある 1 か所について「矢印の左にあるのは漢字と記号だけ」と書くが、左には ASCII の英字 (`W`・`f`・`m`) もある (ASCII 以外の字が漢字 5 字と `・` 3 つだけで、かなは無い。打つ入力に見えないという結論は合う)。埋める文を作る道具の照らしは、かなが無いことと漢字があることだけを数え、「だけ」を照らしていなかった (16 回目の P0 と同じ形) | **直した**。2 つの行に 17 回目の注を足した。照らしは、ASCII の英字・ASCII の数字・ASCII の記号・かな・漢字・そのほかの字を全部数えて、文の分け方と同じことを確かめる形にし、land11 (`e5b38a0`) と land10 (`fe0a377`) の文書の両方で確かめた (この回の道具 `fill17.py`)。網の道具 (`docs_net.py`) の説明も同じに直した (手伝い NET17) |
    | P0 (SA5-01) | KNOWN_LIMITS の K88 の行の (5) と、7 回目の箇条の RP3-11 の行は、本家の PR について「#632 は閉じられていて」と書くが、#632 は開いたまま (閉じた記録も無い)。K88 の行の「PR の状態は 2026-10-16 に GitHub で確かめた」も、確かめた記録が無い (7 回目の元の査読は、merge は clone から確認できない、とだけ書いた。本家の main に入っていないことと、本家の main が記号も数えることは合う) (20 回目の注 (20 回目の査読 SD4-01 と同じ形。実装者): 2026-10-16 の朝の精査に、gh と GitHub の API で取った PR の一覧があり、#632 は開いたまま (閉じていない) だった。無いのは画面で確かめた記録で、K88 の行の、閉じているという句は、その一覧とも合わない) | **直した**。KNOWN_LIMITS の K88 の行を、開いたまま (merge されていない) と、2026-10-17 に GitHub の API で確かめたことに直し、7 回目の箇条の RP3-11 の行に 17 回目の注を足した (API の答えは、状態が open で、merge されておらず、閉じた・開き直した出来事は無い)。文書のほかの、本家の PR の状態の句 (#622 は merge されていない・#623 と #244 は閉じた) は、API の答えと合った (18 回目の注 (18 回目の査読 SB4-01・SB2-07・SB5-01): 7 回目の箇条の RP3-11 の行の、PR の状態をこの日に GitHub で確かめたという句にも、18 回目の注を足した) |
    | P1 (SA1-01) | テストの注・P46-59 の本文・16 回目の表の P1 (RZ1-02) の行は、写し方が「差し込み先で条件にできる次元」を (並びの長さ・辞書の語か・位置・語の頭) と書くが、これは選んだ次元で、語の頭は大文字か小文字かだけだった (語の形の頭は `a`・`A` だけ)。語の頭が大文字の子音のときだけに当てる写し方が、全テストを緑のまま通り、NS の答えを変える。14 回目の表への 16 回目の注の「今も捕まえていないのは…だけ」も、全部と読める | **直した** (テストだけ。9102d1f)。語の頭の字の種類ごとに、外側のループと差し込み先のどちらが救うかをコードから読み、写し方を作って測った (手伝い P60。頭の種類ごとの語 23 × 並び 4 × 位置 3 = 276 入力)。差し込み先だけが救うのは、頭が大文字の語 (A・A 以外の母音・子音) と、頭が小文字の a 1 つの単位の語で、そこだけに当てる写し方は NS で 12〜48 入力の答えを変える (同梱の表では 0)。頭が小文字の子音・頭の単位が 2 字以上・同梱の一覧にある a 以外の小文字の母音 1 つの単位の語は外側のループが救い、そこだけに当てる写し方は、NS でも同梱の表でも 276 入力の答えを変えない。P46-47 のテストの語の形に、頭が大文字の子音の 3 形 (語は Cement、断片は ment) を足し、並びのループの後ろに、頭が A 以外の大文字の母音の語 (Element) の例を位置 3 つに 1 つずつ置いた (Element の答えは並びによって見え方が違うので形には入れず、5 つの木で今の頭が大文字の語の形と同じ木で崩れる並びだけに置いた)。この査読員の写し方 (sa1_consU) は P46-60 のテストで赤 (査読員の 180 入力で NS 30・同梱の表で 0 は、手伝い P60 が流し直して同じ)。格子の族の語の頭に大文字の子音を足した 75 本は、14 文字以上の並びと 2 文字の辞書の語の並びだけに当てる 6 本のほか赤。A 以外の大文字の母音の語を、並びが 3 文字以上のときだけ見ない写し方と、kyouha の後でないときだけ見ない写し方 (NS で 276 入力のうち 15・13 入力の答えが変わる) は、手伝いの版のテストでは緑で、実装者が Element の例を足して赤にした。テストの注は、写し方の次元を選んだ次元と書き、捕まえていない写し方の一覧を、見つかっているもの (これで全部とは言えない) と書いた (18 回目の注 (18 回目の査読 SB1-01・02・SB4-03): 頭が小文字の母音 1 つの単位の語を外側のループが救うかの分かれ目は、頭が a か否かではなく同梱の一覧にあるかで、同梱の一覧に無い語 (emolument) では、a 以外の小文字の母音 1 つの単位だけに当てる写し方も答えを変える。頭が A 以外の大文字の母音の語の例 (3 つ) では、並びの長さや位置で条件を付けた写し方を捕まえていなかった。どちらも P46-61 で例を足して捕まえた。また、この行の対応は、16 回目の表の RZ1-02 の行と 14 回目の表の RX1-02 の行に 17 回目の注を足したことと、P46-59 の本文の誤りを P46-60 の本文に書いたことを書いていなかった) (19 回目の注 (19 回目の査読 SC2-01): 上の 18 回目の注の例の数は、P46-60 で足した Element の例の数で、頭が A 以外の大文字の母音の語の例は、Underachievement の 1 つを入れて 4 つ) (20 回目の注 (20 回目の査読 SD5-01): 4 つは並び (2 字以上の大文字) の直後の例の数。かな (kyouha・ima) の直後にも、P46-50 で足した Entitlement の例が 2 つあり、場所を限らなければ 6 つ) |
    | P1 (SA2-02) | 16 回目の表の P1 (RZ1-01) の行の「使わなかった候補を含む 47 語 × 9 形 = 423 入力を流した」は、47 語どれも今の形と同じだったと読めるが、JAXA は NS で形が違った。埋める文を作る道具は、この「同じ」を照らしていなかった (SA4-04 も同じ指摘) | **直した**。その行に 17 回目の注を足した (JAXA は NS の 2 つの木で形が違ったので使わず、テストに置いた 23 語は 5 つの木で今の形と同じ)。この回の道具 (`fill17.py`) は、手伝いの測りの json の語ごとの答えから、違う語と木を出して確かめる |
    | P1 (SA3-01) | 検証の段落と網の説明は、拾うのは「打つ入力 (ASCII だけの並び)」を書いた例だけと書くが、網が打つ入力とみなす並びはもっと狭い (`/x`・`#x`・`'n`・数字だけの並び・数字で始まる 16 進の並びは、矢印で書いても拾わない)。拾う形を並べる書き方に変えたので、この定義の違いが、そのまま拾う範囲の誤りになる。網の説明の「記号で始まる」も、許す記号は 4 つ (`.`・`@`・`_`・`-`) だけ | **直した**。手伝い NET17 が、網の道具 (`docs_net.py`) の説明と、検証の段落の網の文を、網のコードの字のとおりに書き直した (網のコードは変えていない。説明の文字列を除いた構文木は直す前と同じ)。打つ入力に見える並びは、ASCII の字だけで、空白が無ければ頭が英字か「.」「@」「_」「-」のもの、または頭が数字で英字を含むもの (数字だけのものと、頭が数字の 7〜40 字の 16 進だけのものは拾わない)、空白があれば頭と尻が空白でなく英字を含むもの。句ごとの写しの試し 67 通りで、拾うと書いた形は拾い、拾わないと書いた形は拾わないことを確かめた。land13 (`50ed15b`) の文書の足した行で、打つ入力に見えない ASCII だけのコード片は 140 で、結果と組になる形で書かれているのは「/」の 3 か所 (USAGE.md の「/」で ・ になるという説明 (実際は「z/」) を直した項目の文) と hash の 2 か所 (打つ入力ではない) だけだった。記号で始まる打つ入力も拾うように網を広げる案 (land13 の文書で 132 入力が 133 入力になり、増えるのは「/」の 1 入力) は入れなかった: その入力は実機確認リストにすでにあり、広げると記号で始まるパスなども組になりうるため (18 回目の注 (18 回目の査読 SB3-02): 網が外すのは、頭が数字の 7〜40 字の、数字と小文字の a〜f だけの並びで、大文字を含む 16 進の並び (`0ABCDEF` など) は拾う) |

  - P2 15 件 (SA1 2・SA2 6・SA3 4・SA4 3) の対応 (SA4-02 のほかは直した。SA4-04 は SA2-02 と同じ直し):
    - **テストと変異の記録** (P46-60): SA1-02 テストの注の「ament・pant 以外の断片」は、語 (ament) と断片 (ment・pant) が混ざっている → P46-60 のテストの注で、語と断片を分けて書いた。SA1-03 切り替え表で全テストに流したのは緑の写し方だけで、全テストで切り替えが効くことの記録が無い (査読員が赤の写し方を全テストで流すと 851/852) → P46-60 の本文に、切り替え表の全テストで、変異なし (852/852) と、赤の sa1_consU (851/852。切り替えが全テストで効く) と、緑の写し方 (Element の例を足す前のテストで 14 本、足したテストで緑のままの 12 本。どれも 852/852) を流した数を書いた。P46-59 までの写し方 275 本も、P46-60 のテスト (Element の例を足す前) で流し直した: P46-59 のテストで赤だった 247 本はどれも赤のままで、落ちるテストも 1 本目の赤の例も同じ。緑だった 28 本は、Element の例を足したテストでも緑のまま
    - **埋める文を作る道具・手順・記録** (リポジトリの外): SA2-03 16 回目の断片の道具の数の照らしは、照らす先のファイルの句に句の中の数があることを求めず、関数だけで裏付けた数も「照らす先がファイル」に数えていた (#37 の中の 2 回は、埋める文の道具が assert しただけで、断片の道具はどちらでも照らしていなかった) → この回の断片の道具 (`docs_frag_wave17.py`) は、句の中の数が照らす先のファイルの句に無いとき、その数を関数だけで照らした数に分けて数える。#37 の中の回数は、git で数え直した事実 (`r17/r17facts.json`) で照らした。SA2-04 16 回目の埋める文の道具は、(9) の #37 の行を、統合した木の先端を入れずに `6acfe6d` を 2 回数えていた (`6acfe6d` と `364841f` で CompositionController.cs の中身は同じなので、文の事実は合う) → この回の道具 (`fill17.py`) は、今の先端で数え直した。SA2-05 文書を作り直す手順 (`build_docs17.sh`) の、埋める文が無いときの止まる文言が、期待値の分け方の文の道具 (`split13fill.py`〜`split15fill.py`) を挙げていない → この回の手順 (`build_docs18.sh`) で挙げた (`split16fill.py` も)。SA2-06 前の回の文書のコミットの道具 (`rl12/commit_land13.sh`) の頭の注の写し元が、無いファイルの名前だった → 直した。SA2-07 16 回目の埋める文のうち、P46-58・P46-59 のテストの中身の句 (並びの語の長さ・辞書の語か・前置きの無い形) は、埋める文の道具が照らしていない手書きだった (事実は合う) → この回の道具 (`fill17.py`) が、テストの文字列と格子の族の結果から確かめる (18 回目の注 (18 回目の査読 SB2-01): この道具が照らしたのは、P46-59 の並びの長さと格子の族の赤と、P46-58 の前置きの無い形だけだった。P46-58 の 3〜5 文字の並び (API・HTTP・HTTPS) が辞書の語 (同梱の英語の辞書の語) であることと、開発機の NSSpellChecker が NASDAQ を語と答えることは、18 回目の道具 (`tools18/sa207_check.py`) で照らした) (19 回目の注 (19 回目の査読 SC4-01・SC2-02): 上の 18 回目の注の辞書は、英語の辞書 (english.txt。IsKnownEnglishWord が見る) のことで、同梱の英単語表 (english-words.txt) には 3 つとも無い。18 回目の道具は english.txt を空白で区切って読む参考の読み方で、C# (`WordList.ParseWords`) と同じ読み方での確かめは、19 回目の道具 (`tools19/english_parsewords.py`) で記録に残した)。SA2-08 16 回目の箇条の RZ2-05 の行の「(この回の道具も同じ)」は、16 回目 (P46-59) の照らし方と違う (16 回目の記録の道具は、切り替え表の木を作り直した時刻で照らした) → その行に 17 回目の注を足し、P46-59 の埋める前と後のテストの差が注の行だけなことを、この回の道具 (`fill17.py`) で確かめた。
    - **文書の例の道具** (リポジトリの外): SA3-02 網が「かなか漢字を含む」と数える字には「・」「ー」も入るが、網の説明と検証の段落の「かな」は、それを含むと読めなかった → 手伝い NET17 が、網の説明と検証の段落に、「・」「ー」も数えることと、漢字に「々」と CJK の拡張 A も数えること、(3) の括弧は丸括弧で、中は「・」「ー」でないかなを含み、かなと半角の英数字と「.」「@」「_」「-」だけであることを書いた。SA4-03 検証の段落の (5) の「閉じ括弧の前まで」は網より広い → 結果の終わりで止まる字は 7 つ (空白・バッククォート・「、」・「。」・閉じ丸括弧 (半角と全角)・「」」) だけで、ほかの閉じ括弧 (「】」「』」など) は結果に入ることを書いた。あわせて、(2) のあいだが矢印だけのときは、かなか漢字が無くてもよいこと (網はどちらの有無も見ない) を書いた。SA3-03 確かめの道具 (`docs_check.py`) は、16 回目の古い言い方と記録の許しの一覧を、あるときだけ読んでいた (15 回目の一覧のときと同じ形で、1 回ずれて残った) → 16 回目までは必ず読み、17 回目からは、計画書にその回の見出しがある回は必ず読む (無ければ止まる) 形にした。見出しの無い回の一覧は、あれば読む (読まない形にすると、作り直す前の文書でも通るが、見出しを書き誤った箇条を捕まえられなくなる)。SA3-04 箇条にその回の古い言い方が 1 つ以上あるかの数え方は、部分の一致のままだった → 回数の確かめと同じ数え方 (数字で始まる句は、すぐ前が数字の所を数えない) にそろえ、句の終わりの側は見ないことを道具の説明に書いた (land13 の文書で、数字で終わる 38 句のすぐ後ろに数字が続く所は 0)。SA3-05 作った文書の中の埋める文を 1 字変えても、どの確かめも止まらなかった → 断片の埋める所から決まる数と、作った文書の中の埋める文の数を、鍵ごとに照らす確かめを足した (land13 の文書で 63 鍵とも同じ。埋める文の中の hash を別の hash に変えたときと、文の置き場所を変えたとき (別の文書へ移したときも。数は文書の全部で足して比べる) は止まらない)。写しの文書と道具を壊す試し 23 通りで、直した道具が止めること、壊さない写しは通ることを確かめた (写しの道具が共有のファイルを開いたのは 0 回)。 (18 回目の注 (18 回目の査読 SB3-01・SB2-04): 止まらないのは、文書を組むときにコミットの番号から埋める hash (今は 1 つの鍵だけ) を変えたときで、文に字のまま書いた hash を変えると止まる (18 回目の査読員が写しで確かめた)。結果の終わりで止まる空白には、全角の空白とタブも入る)。(19 回目の注 (19 回目の査読 SC4-03): 上の 18 回目の注は、この行の SA4-03 の「空白」の句と SA3-05 の文への注で、行の末尾にまとめて置いた。注の後ろに句点が無かったので、19 回目に足した)。 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
    - **KNOWN_LIMITS** (文書。main の行): SA4-02 K83 の行の 8 例のうち `accidentdayo` は、今の 5 つの木のどの NS でも行の値にならない (`accidentだよ`。実装者も流し直して同じ。ほかの 7 例は行の値と同じ) → main の行で、P46 は変えていないので、この取り込みでは直さず、別のセッションに伝える。
  - **18 回目** (17 回目の対応のあとに増えた P46-60・変異の族・足した例と作り直しの試し・記録 (SB1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具 (SB2)、文書の例の道具 (網・確かめ・検証の段落の文)・照合の一覧・壊した入力・網の範囲 (SB3)、IMPROVEMENT_PLAN の 17 回目の表と箇条・前の回の箇条への 17 回目の注・(9)・検証の段落・P46 の本文と KNOWN_LIMITS (SB4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (SB5)): **SB1: P0 0・P1 2・P2 1、SB2: P0 0・P1 1・P2 7、SB3: P0 0・P1 1・P2 3、SB4: P0 0・P1 1・P2 4、SB5: P0 0・P1 0・P2 3** (合わせて P0 0・P1 5・P2 18。どの報告も、見出しの数と項目の数が合う。SB4-01 (P1) と SB2-07・SB5-01 (P2) は同じ指摘)。SB1 は、足した例の答えを 5 つの木 × BI・NS × 確定・表示で流し直して全部同じ (270/270) で、作り直しの試しの一致 (16/16) も確かめた。SB2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直し、記録の許しの一覧を壊す試し (12 通り) をした。SB3 は、照合の一覧の全部での NS の照合 (NG 0) と、検証の段落の句ごとの試し (101 通り、予想と違うもの 0) をした。SB4 は、17 回目の項目がどれも表と箇条にあること (過不足なし) と、「直した」の抜き取り 18 件を確かめた。SB5 は、本家の PR の状態を GitHub の API (11 回) で取り直し、#632 は開いたままと確かめた。着地の決め方 (2026-10-17 に決めた): この回の指摘を全部直したら、直した所だけを別の査読員 1 人が確かめて着地する。ただし、製品のコード・テストの判定・利用者向けの文書に P0 か P1 が出たときは、もう 1 回全体の査読をする。この回は SB1-01・SB1-02 (テストの判定の P1) があったので、もう 1 回全体の査読をする。

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (SB1-01) | P46-60 で足した、頭が A 以外の大文字の母音の語 (Element) の例は 3 つ (並び 2〜3 文字) だけで、並びの長さや位置で条件を付けた写し方 (査読員の自作 4 本) が、全テストを緑のまま通り、NS の答えを変える。テストの注・P46-60 の本文・14 回目の表への 17 回目の注の「見つかっている捕まえていない写し方」に、この形が無い。UPSTREAM_REVIEW の P46-60 の行の「埋め」は書き過ぎ | **直した** (テストだけ。289a3e0)。P46-47 のテストに 2 組の例を足した。頭が A 以外の大文字の母音の語 (Element) の例を、統合の木で英字のまま残る 7 つの並び (2〜6 文字の PC・JPN・HTTP・QWER・HTTPS・JETRO・SYSTEM) × 位置 3 の 21 例にした (P46-60 の 3 例はこの中にある。7 文字以上の並びの後ろでは、取り込み前の木から …Eぇment… になる別の規則があるので置いていない)。同梱の一覧に無い、頭が a 以外の小文字の母音 1 つの単位の語 (emolument) の 3 形を、略語の並びのループ (23 の並び) に足した (開発機の NSSpellChecker が語と答える語として、テストの模擬の判定器にも足した)。期待値は開発機の NSSpellChecker で流した統合の木の答えで、模擬の判定器でも同じ。この査読員の写し方 6 本のうち 5 本と sa1_lvow は、P46-61 のテストで赤になる (頭が I・O の語だけに当てる 1 本は、I・O の例を置いていないので緑のまま)。テストの注は、語の頭の字の種類の分け方を、同梱の一覧にあるかに直し、捕まえていない写し方を、置いた例の答えをどれも変えない写し方と書いて、見つかっているものはその例とした (例を置いていない形だけに当てる写し方は、変異では確かめていないと書き分けた) (19 回目の注 (19 回目の査読 SC1-01・SC2-01): Element の例を置かなかった 2〜6 文字の API・NASDAQ は、統合の木で並びの頭が崩れる (あPI・んASDAQ) ので外したが、その理由を書いていなかった。そこだけに当てる写し方が全テストを緑のまま通ったので、P46-62 で API・NASDAQ と 7 文字以上の 14 の並びにも Element の例を置いた。21 例は Element の例の数で、頭が A 以外の大文字の母音の語の例は、Underachievement の 1 つを入れて 22) (20 回目の注 (20 回目の査読 SD2-01・SD4-02・SD5-01): 上の 19 回目の注の並びの頭の崩れは、API は 3 つの位置のどれも (あPI)、NASDAQ は kyouha・ima の後だけ (んASDAQ) で、入力の頭の NASDAQElementsu は崩れない。22 は並びの直後の例の数で、かなの直後の Entitlement の 2 つを入れると 24) |
    | P1 (SB1-02) | テストの注・P46-60 の本文・17 回目の表の SA1-01 の行は、同梱の一覧にある、頭が a 以外の小文字の母音 1 つの単位の語は外側のループが救い、そこだけに当てる写し方 (sa1_lvow) は答えを変えないと書くが、外側のループが救うかの分かれ目は同梱の一覧にあるかで、確かめた a 以外の語は同梱の一覧にある element だけだった。同梱の一覧に無い語 (emolument) では、sa1_lvow が NS の答えを変え、全テストは緑 | **直した** (テストだけ。289a3e0)。上の行の対応のとおり。17 回目の表の SA1-01 の行と、14 回目の表への 17 回目の注に、18 回目の注を足した。UPSTREAM_REVIEW の P46-60 の行にも 18 回目の注を足した |
    | P1 (SB2-01) | 17 回目の箇条の SA2-07 の行は、この回の道具 (`fill17.py`) がテストの中身の句 (並びの語の長さ・辞書の語か・前置きの無い形) を確かめると書くが、照らしたのは一部で、P46-58 の 3〜5 文字の並びが辞書の語かと、開発機の NSSpellChecker が NASDAQ を辞書の語と答えることは、どの道具も照らしていなかった (事実は合う) | **直した**。その行に 18 回目の注を足し、2 つの句をこの回の道具で照らした (P46-58 のテストの並びを同梱の表で引き、NASDAQ を開発機の NSSpellChecker に聞いた) (19 回目の注 (19 回目の査読 SC2-02・SC4-01): 引いたのは英語の辞書 (english.txt。IsKnownEnglishWord が見る) で、3 つとも同梱の表 (english-words.txt) には無い。17 回目の表の SA2-07 の行への 19 回目の注のとおり、C# と同じ読み方での確かめは 19 回目の道具で記録に残した) |
    | P1 (SB3-01) | 17 回目の箇条の SA3-05 の行は、埋める文の中の hash を別の hash に変えても確かめが止まらないと書くが、止まらないのは、文書を組むときにコミットの番号から埋める hash (今は 1 つの鍵だけ) で、文に字のまま書いた hash を変えると止まる (査読員が写しで確かめた)。確かめの力を実際より小さく書いていた (19 回目の注 (19 回目の査読 SC3-06): この行の「(今は 1 つの鍵だけ)」の後ろに「を変えたとき」が抜けていた。17 回目の箇条への 18 回目の注は、その句を書いている) | **直した**。その行に 18 回目の注を足した |
    | P1 (SB4-01・SB2-07・SB5-01) | 7 回目の箇条の RP3-11 の行の「PR の状態は 2026-10-16 に GitHub で、コードは本家の clone で確かめた」に注が無い。17 回目の注は同じ行の「閉じられていて」の後ろだけで、17 回目の表の SA5-01 の行は同じ句を「確かめた記録が無い」と書くので、記録の行どうしで読み分けられない | **直した**。その句の後ろに 18 回目の注を足し (この日に状態を確かめた記録は無い。状態は 2026-10-17 に GitHub の API で確かめた) (20 回目の注 (20 回目の査読 SD4-01): 2026-10-16 の朝の精査に、gh と GitHub の API で取った一覧がある。7 回目の箇条の RP3-05 の行への 20 回目の注のとおり)、17 回目の表の SA5-01 の行にも 18 回目の注を足した |

  - P2 18 件 (SB1 1・SB2 7・SB3 3・SB4 4・SB5 3) の対応 (SB4-05 のほかは直した。SB2-07・SB5-01 は SB4-01 と同じ直し):
    - **テストと変異の記録** (P46-61): SB1-03 Cement の形の期待値の「Cえ」は別の規則の答えで、その規則を直すと期待値も変わることを、テストの注に書いていない → P46-61 のテストの注に書いた。変異は、P46-60 までの 368 本と、この回の査読員の 6 本を、P46-61 のテストの UpstreamP46Tests で流した (切り替え表 334 本・1 行だけ変えた本物の木 40 本)。P46-60 のテストで赤だった 328 本は、どれも赤のままで、落ちるテストも 1 本目の赤の例も同じ。緑だった 40 本のうち、赤になったのは sa1_lvow だけ。全テストでは、変異なし (852/852) と、赤になった査読員の 5 本 (どれも 851/852) と、緑の 40 本 (どれも 852/852) を流した (切り替え表 37 回・本物の木 9 回。変異なしは切り替え表で流した)。実装者が見つけたもの: テストの注の「辞書の語か否かは、NsLikeChecker (同梱の表) の答え」は、辞書の語の判定 (IsKnownEnglishWord) が英語の辞書と固有名詞も見ることを書いていなかった (P46-58 の 3〜5 文字の並びは英語の辞書の語で、同梱の表には無い。手伝い TOOLS18 の照らし) → P46-61 のテストの注で直した。
    - **埋める文を作る道具・手順・記録** (リポジトリの外): SB2-02 17 回目の埋める文の道具 (`fill17b.py`) は、P60A の「6 本」の中身を照らしていない → 緑の写し方の名前の集合を照らすようにした。SB2-03 同じ道具は、P60R の「kyouha の後の 2 文字の並びの 1 つだけ」を 3 つの語の有無だけで照らしていた → テストの文字列から、並びの直後の大文字の母音で始まる語を全部拾って照らすようにした (書く文は変わらない)。SB2-05 記録 (`gated_evidence.txt`) の P46-60 と land14 の段落は、作り直しの試しの複製と本文が残っていることと、手順のログの写しの終わりの 2 行の違いを書いていない → 2 つの段落に 18 回目の注を足した。SB2-06 17 回目の主張の照らし (`rl14/check_claims17.py`) の頭の説明が、埋める文の照らし方と合わず、古い書き方を足した行だけで数えていた → 説明を直し、テストのファイル全体でも数えるようにした。SB2-08 17 回目の断片の道具の数の照らしに、道具の中に書いた字の定数を数える必ず真の式が 2 つあった → 報告の字から名前を拾って数える式にした (作る断片は同じ)。
    - **計画書の記録の行** (文書): SB3-02 17 回目の箇条の NET17 の行の「16 進だけ」は、網のコードより広い (網が外すのは、数字と小文字の a〜f だけの並び) → その行に 18 回目の注を足した。SB4-03 17 回目の表の SA1-01 の行の対応に、足した 2 つの注 (16 回目の表と 14 回目の表) と、P46-59 の本文の誤りを P46-60 の本文に書いたことが無い → その行に 18 回目の注を足した。SB4-05 17 回目の箇条の「文書の例の道具」の行に余分な空白がある → 前の回の行の字は変えない決まりなので、そのままにした (主張は変わらない)。
    - **文書の例の道具と検査の道具** (リポジトリの外と検証の段落): SB3-03・SB4-02・SB2-04 検証の段落の網の文の (3)・(4)・(5) に、網のコードより狭い・広い言い方が残っていた ((3)・(4) の括弧の前に空白を挟んでよいことが無い、(4) の「括弧」は網では丸括弧 (半角か全角) だけ、(5) の止まる「空白」に全角の空白とタブも入ることが無い)。網の道具 (`docs_net.py`) の説明は、(4) だけが「括弧」だった → 網の動きは変えずに、検証の段落の (3)・(4)・(5) と網の道具の説明の (4) を、網のコードのとおりに直した (網の道具の説明の文字列を除いた構文木は直す前と同じ)。17 回目の箇条の SA4-03 の直しの「空白」には、18 回目の注を足した。SB4-04 検証の段落の 16 回目と 17 回目の文のあいだに「。」が無い → 足した。SB3-04 文書の検査の道具 (`docs_lint.py`) は、KNOWN_LIMITS の件数表を出すだけで止めなかった → K 番号の重複と、件数表の各行の数・合計が K の行を数えた数と合わないときに止めるようにした (手伝い TOOLS18。写しを壊す試し 6 通りで、壊した 5 通りがどれも止まり、壊さない写しは通ることを確かめた。直す前の道具は 6 通りとも通った)。 (19 回目の注 (19 回目の査読 SC4-03): 17 回目の箇条の「文書の例の道具」の行への 18 回目の注は、SA4-03 の句の後ろではなく、行の末尾にまとめて足した) (20 回目の注 (20 回目の査読 SD4-03): すぐ前の 19 回目の注は、この行の中ほどの「17 回目の箇条の SA4-03 の直しの「空白」には、18 回目の注を足した。」への注)
    - **追従表と UPSTREAM_REVIEW** (文書): SB5-02 17 回目の P2 の数 (項目で数えた数) が報告の見出しの和と違うことの注が、追従表と UPSTREAM_REVIEW に無い (計画書にはある) → 追従表と UPSTREAM_REVIEW の 17 回目の件数に、報告の見出しの和を書き足した。SB5-03 #632 が本家で merge されたときに KNOWN_LIMITS の K88 の (5) を見直すことが、文書の判断待ちに無い → 追従表の判断待ちに足した。
    - 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
  - **19 回目** (18 回目の対応のあとに増えた P46-61・変異・足した例の答えと文・作り直しの試しと記録 (SC1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具 (SC2)、18 回目の直し・照合の一覧・壊した入力・網の範囲 (SC3)、IMPROVEMENT_PLAN の 18 回目の表と箇条・前の回への注・(9) と検証の段落・P46 の本文と利用者向けの文書 (SC4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (SC5)): **SC1: P0 0・P1 1・P2 3、SC2: P0 1・P1 1・P2 4、SC3: P0 0・P1 0・P2 6、SC4: P0 0・P1 1・P2 4、SC5: P0 0・P1 2・P2 0** (合わせて P0 1・P1 5・P2 17。どの報告も、見出しの数と項目の数が合う。SC2-02 と SC4-01 (P1) は同じ指摘で、SC2-03 と SC3-03 (P2) も同じ指摘。SC2-01 (P0) と SC5-02 (P1)、SC3-01 と SC4-02 (P2) は一部が同じ指摘)。SC1 は、変異の表を数え直して主張どおりと確かめ、足した例の答えを 5 つの木 × BI・NS × 確定・表示で流し直して全部同じ (480/480) だった。SC2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直し、記録の許しの一覧を壊す試しをした。SC3 は、照合の一覧の全部での NS の照合 (NG 0)、検証の段落の句ごとの試し (66 通り。文の読みと違う形は SC3-01・02)、写しを壊す試し (45 通り、予想と違うもの 0) をした。SC4 は、18 回目の項目がどれも表と箇条にあること (過不足なし) と、「直した」の抜き取り 21 件を確かめた。SC5 は、本家の PR の状態を GitHub の API (46 回) で取り直して #632 は開いたままと確かめ、#560 の変更ファイルを 37 の PR ごとに clone と照らした。着地の決め方 (2026-10-17 に決めた) のとおり、この回は SC1-01 (テストの判定の P1) があったので、もう 1 回全体の査読をする。

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P0 (SC2-01) | UPSTREAM_REVIEW の P46-61 の行・P46-60 の行への 18 回目の注・17 回目の表の SA1-01 の行への 18 回目の注は、頭が A 以外の大文字の母音の語の例の数を、P46-60 で足した Element の例の 3 つのまま、限らずに書いていた (テストの例は Underachievement を入れて 4 つ) (20 回目の注 (20 回目の査読 SD5-01): 4 つは並びの直後の例の数で、かなの直後の Entitlement の 2 つを入れると 6 つ)。計画書の本文の P46-61 の説明も同じ形 | **直した**。3 つの注と本文の説明の後ろに 19 回目の注を足した。注の数は、断片の道具がテストの文字列から並びの直後の例を拾って照らす |
    | P1 (SC1-01) | P46-61 は Element の例を、英字のまま残る 7 つの並びの後ろにだけ置き、2〜6 文字の残りの API・NASDAQ を外した理由を書いていない。直前の並びが 3 文字の辞書の語のときだけに当てる写し方 (査読員の自作) は、全テストを緑のまま通り、NS の答えを変える | **直した** (テストだけ。90290e7)。P46-47 のテストの Element の例を、23 の並びのすべて × 位置 3 の 69 例にした (P46-61 の 21 例に、2〜6 文字の残りの API・NASDAQ の 6 例と、7 文字以上の 14 の並びの 42 例を足した)。統合の木で、API・NASDAQ の後ろでは Element は英字のまま残るが並びの頭が崩れ (あPI・んASDAQ)、7 文字以上の並びの後ろでは Element の頭が Eぇ に崩れる。どちらも取り込み前と main も同じ答えで、テストの注に、期待値にその答えが入ることを書いた。期待値は開発機の NSSpellChecker で流した統合の木の答えで、テストの模擬の判定器も 69 入力とも同じ答えを返す (模擬の判定器は NASDAQ を語と答えないが、答えは同じ)。外した理由を書いていなかった API・NASDAQ と、例を置いていない形として書いていた 7 文字以上の並びにも例を置いたので、テストの注の、捕まえない写し方の一般の言い方から、7 文字以上の並びの後ろの形を外した (20 回目の注 (20 回目の査読 SD1-02・SD2-01・SD4-02): 並びの頭が崩れるのは、API は 3 つの位置のどれも、NASDAQ は kyouha・ima の後だけで、入力の頭の NASDAQElementsu は崩れない) |
    | P1 (SC2-02・SC4-01) | 18 回目の表の SB2-01 の行は、P46-58 のテストの並びを同梱の表で引いたと書くが、3〜5 文字の並びは同梱の表 (english-words.txt) には無く、照らしたのは英語の辞書 (english.txt) の列だった。その列の読み方は C# と同じでない参考の読み方で、C# と同じ読み方での確かめは記録に無かった | **直した**。その行と、17 回目の表の SA2-07 の行への 18 回目の注に、19 回目の注を足した。english.txt を C# (`WordList.ParseWords`) と同じ読み方で読む道具を作り、api・http・https は語で、nasdaq・emolument は語でないことを記録に残した |
    | P1 (SC5-01) | 追従表の判断待ちに足した #632 の見直しが、範囲の外の 2 件の括弧の中に入り、3 つ目に読める | **直した**。範囲の外の 2 件の括弧を閉じてから足した |
    | P1 (SC5-02) | UPSTREAM_REVIEW の P46-61 の行と P46-60 の行への 18 回目の注は、テストの穴を限りの句 (P46-60 で足した・Element・英字の直後の・1 つの単位) なしに書き、テストの中身と合わない (SC2-01 と一部が同じ。SC4-04 の「1 つの単位」の抜けも同じ行) | **直した**。その 2 つの行に 19 回目の注を足した |

  - P2 17 件 (SC1 3・SC2 4・SC3 6・SC4 4) の対応 (どれも直した。SC3-03 は SC2-03 と、SC4-02 は SC3-01 と同じ直し):
    - **テストと変異の記録** (P46-62): SC1-02 sa1_lvow の NS の数の母数が、P46-61 の本文・テストの注・査読員の測りで違う → P46-62 のテストの注を、査読員の 48 入力 (4 つの語) のうち emolument の 12 入力のすべて、に直した (P46-61 の本文の数は emolument の入力だけの数)。SC1-03 記録の P46-61 の段落と手順の注の、変異を流したあとに注を直したという句は、時刻の順と合わない (注の最後の版は変異の全テストの途中。流した木のテストは前の版のままで、結果には効かない) → 段落の字は変えずに、すぐ後ろに 19 回目の注の段落を置き、手順の注にも 19 回目の注を足した。SC1-04 P46-61 の記録の道具 (`rl15/evidence61.py`) は、段落を足したあとの DRY で止まり、段落を出し直せない → DRY のときは、作った段落と記録の段落を比べる形にした。変異は、P46-61 までの 374 本と、19 回目の査読員の 7 本を、P46-62 のテストの UpstreamP46Tests で流した (切り替え表 341 本・1 行だけ変えた本物の木 40 本)。P46-61 のテストで赤だった 334 本は、どれも赤のままで、落ちるテストも 1 本目の赤の例も同じ。緑だった 40 本も、どれも緑のまま。査読員の 7 本のうち 6 本は赤 (1 本目の赤の例は、3 本が足した例、3 本が前からある例) で、同梱の一覧に無い、頭が小文字の u・o の語だけに当てる 1 本は緑 (その語の例は置いていない)。全テストでは、変異なし (852/852。切り替え表と本物の木で 1 回ずつ) と、赤の査読員の 6 本 (どれも 851/852) と、緑の 41 本 (どれも 852/852) を流した (切り替え表 39 回・本物の木 10 回)。 (20 回目の注 (20 回目の査読 SD1-03): 査読員の 7 本のうち赤の 6 本に入る sc1_Uv6 は、開発機の NSSpellChecker では答えを変えない (査読員の 48 入力で 0。NASDAQ を語と答えるので条件が真にならない) が、模擬の判定器は NASDAQ を語と答えないので、テストでは赤になる)
    - **埋める文を作る道具・手順・検査の道具** (リポジトリの外): SC2-04 18 回目の埋める文の道具 (`fill18.py`) の emolument の assert は、期待値の字を照らしていない → 23 の並びの 3 つの形を、テストの期待値の字と照らすようにした。SC2-05 同じ道具の、P46-60 の 3 例は 21 例の中にあるという句の assert は 1 例だけを見る → 3 例とも 21 例に入ることを照らすようにした (書く文は変わらない)。SC2-06 land15 の記録の道具 (`rl15/evidence_land15.py`) も DRY で止まる → SC1-04 と同じ形にした。SC3-04 文書の確かめの道具 (`docs_check.py`) の説明に、18 回目に合わせていない句が 2 つ残る → 今の動きに合わせた。SC3-05 文書の検査の道具 (`docs_lint.py`) の件数表の照らしは、空白の入れ方が違う K の行を数えない → 形の違う K の行があれば止めるようにした (20 回目の注 (20 回目の査読 SD3-01): 19 回目の道具が止めたのは、行の頭か | のまわりの空白の入れ方が違う K の行で、行の頭に | の無い行や、番号を太字にした行などは止めなかった。20 回目に、それらも止めるようにした (21 回目の注 (21 回目の査読 SE3-02): 20 回目に止めるようにしたのは、表の中の、行の頭に | の無い行と、番号を ** で太字にした行など。番号を HTML で太字にした行 (`<b>K90</b>`)・| の無い表・表に続かない K の行・番号をリンクにした行は止めない))。
    - **計画書・追従表の記録の行** (文書): SC3-06 18 回目の表の SB3-01 の行の文に、埋める hash を「変えたとき」が抜けている → その行に 19 回目の注を足した。SC4-03 17 回目の箇条の「文書の例の道具」の行への 18 回目の注が行の末尾にあり、句点なしで次の文に続く → 注の後ろに句点と 19 回目の注を足し、18 回目の箇条の網の行にも 19 回目の注を足した。SC4-04 UPSTREAM_REVIEW の P46-61 の行の「母音の語」に「1 つの単位」が抜けている → SC5-02 の 19 回目の注で書いた。SC4-05 7 回目の箇条の RP3-05 の句と追従表の #622 の句の、2026-10-16 に GitHub の PR の画面で確かめたという句にも、確かめた記録が無い → 7 回目の RP3-05 の句と 6 回目の RO2-14 の句に 19 回目の注を足し (作業のファイルにこの日の記録は無い。2026-10-17 に GitHub の API で確かめた) (20 回目の注 (20 回目の査読 SD4-01): 取り込みの作業のフォルダーの外の、2026-10-16 の朝の精査のフォルダーに、gh と GitHub の API で取った一覧がある。無いのは画面で確かめた記録)、追従表の句は日付と確かめ方を API のものに直した。
    - **文書の例の道具** (リポジトリの外と検証の段落): SC3-01・SC4-02 検証の段落と網の道具 (`docs_net.py`) の説明の (5) の「打つ入力の後ろの矢印」は、網のコードより広く読める (網が拾うのは、打つ入力のすぐ後ろに空白だけを挟んで置いた矢印) → 2 か所とも「打つ入力のすぐ後ろ (空白だけを挟んでよい) の矢印」に直した。SC4-02 が挙げた (3)・(4)・(5) の「空白」の書き分けは、(3)〜(5) の空白 (打つ入力と括弧・矢印のあいだと、括弧の中・結果の終わり) には全角の空白とタブも入る、という文を 2 か所に足してそろえた。SC3-02 網の (4) は、括弧の中にバッククォートや別の丸括弧があると拾わないことを書いていない → 2 か所に書き足した。SC2-03・SC3-03 網の道具の説明の (5) は、止まる空白にタブを書いていない (18 回目に検証の段落だけを直していた) → 網の道具の説明の (5) にもタブを書いた。どれも網の動きは変えていない (網の道具と検証の段落の道具は、文字列を除いた構文木が直す前と同じ。手伝い TOOLS19)。検証の段落の回の並びに、19 回目の 1 文を足した。
    - **実装者が見つけたもの**: 19 回目の依頼文の訂正 (18 回目に照らした辞書は英語の辞書) は手伝い TOOLS18 に送ったが、手伝いが終わったあとに着き、届いていなかった (結果は同じ。照らしの json が、同梱の表と英語の辞書の両方の列を持っていた)。査読員 SC1 が確かめた、7 文字以上の 14 の並びの後ろだけに当てる写し方 (自作) は、緑のまま NS の答えを 48 入力のうち 42 で崩れる向きに変える (テストの注は、例を置いていない形として書いていた。指摘ではない) → P46-62 で、その並びにも Element の例を置いた (上の P1 の行)。
    - 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
  - **20 回目** (19 回目の対応のあとに増えた P46-62・変異・足した例の答えと文・作り直しの試しと記録 (SD1)、埋める文を作る道具・本文の道具・作り直しと着地の手順・記録・断片の道具 (SD2)、19 回目の直し・照合の一覧・壊した入力・網の範囲 (SD3)、IMPROVEMENT_PLAN の 19 回目の表と箇条・前の回への注・(9) と検証の段落・P46 の本文と利用者向けの文書 (SD4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (SD5)): **SD1: P0 0・P1 1・P2 3、SD2: P0 0・P1 0・P2 5、SD3: P0 0・P1 0・P2 2、SD4: P0 0・P1 1・P2 2、SD5: P0 0・P1 1・P2 1** (合わせて P0 0・P1 3・P2 13。どの報告も、見出しの数と項目の数が合う。SD1-02・SD2-01・SD4-02 (P2) は同じ指摘で、ほかに SD5-01 (P1) と SD2-02 (P2) は一部が同じ指摘)。SD1 は、Element の 69 例の期待値が NS・統合の確定と 69/69 同じことを確かめ、変異の表 (381 本) を数え直し、自作の写し方 10 本を流した。SD2 は、作り直す手順の守りの試し (99 通り) と着地の手順の試し (18 通り) を流し直し、作り直した文書の分け方の確かめ (329 = 291 + 38) を流した。SD3 は、19 回目に書き足した検証の段落の句の試し (22 通り、文の読みと違うもの 0) と、照合の一覧の網 (入力 132 の集合と組 195 の多重集合が前の文書と同じ) を流した。SD4 は、19 回目の 23 項目がどれも表と箇条にあること (過不足なし) と、「直した」の抜き取り 21 件を確かめ、10-16 の記録を取り込みの作業のフォルダーの外 (5,126 ファイル) まで探した。SD5 は、本家の PR の状態を GitHub の API で取り直して #622 は開いたままと確かめ、変わっていないはずの文書の blob が前の文書と同じことを確かめた。着地の決め方のとおり、この回は SD1-01 (テストの判定の P1) があったので、もう 1 回全体の査読をする。

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (SD1-01) | P46-62 の頭が大文字の語の例は、2〜13 文字の 23 の並びの後ろだけにあり (21 回目の注 (21 回目の査読 SE5-01): 2〜13 文字の並びの後ろにだけあるのは、大文字の並びの直後の、頭が大文字の語の例。かな (kyouha・ima) の直後にも、P46-50 で足した Entitlement の例が 2 つある)、直前の並びが 1 文字のときだけに当てる写し方 (査読員の自作) は、全テストを緑のまま通り、NS の答えを変える。テストの注と本文の「見つかっているもの」は、並びの長さの上の端 (14 文字以上) だけを挙げていた | **直した** (テストだけ。bf5c6ed)。P46-47 のテストに、1 文字の並び (子音の字の X と、母音の字の A) × 位置 3 の例を 18 足した (並びのループの後ろに Ament・Cement の 12 例、Element の例の 4 つ目の組に 6 例)。期待値は開発機の NSSpellChecker で流した統合の木の答えで、取り込み前と main も同じ答え (P46 だけの木と P46-47 の前の統合は、語の ment が めんつ に崩れる)。テストの模擬の判定器も同じ答えを返す。語はどれも 2 文字以上の並びの後ろと同じ形 (Ament・Cえment・Element) で残り、並びの X は英字のまま、A は Element の前では あ になる (取り込み前と main も同じ答えで、テストの注に、期待値にその答えが入ることを書いた)。テストの注の「見つかっているもの」に、並びの長さの下の端の結果と、20 回目の査読員の写し方の結果を足した |
    | P1 (SD4-01) | 6 回目・7 回目・18 回目・19 回目の記録の行への注の、2026-10-16 に #622・#632 の状態を確かめた記録は無いという句は、10-16 の精査のフォルダーを探していなかった (そこに gh と GitHub の API で取った一覧がある) | **直した**。その 5 か所に 20 回目の注を足し (2026-10-16 の朝の精査の一覧では、#622 は開いたまま・merge なし・本文に Fixes #424、#632 も開いたまま。無いのは画面で確かめた記録)、追従表の #622 の句に、2026-10-16 の確かめを足した |
    | P1 (SD5-01) | UPSTREAM_REVIEW の P46-60・61 の行と、計画書の 17 回目・18 回目・19 回目の 3 か所の、頭が A 以外の大文字の母音の語の例の数 (4 つ・22) に場所の限りが無く、テストの中身 (かなの直後にも Entitlement の例が 2 つある) と合わない (SD2-02 と一部が同じ) | **直した**。その 5 か所に 20 回目の注を足した (4 つと 22 は並びの直後の例の数で、場所を限らなければ 6 つと 24)。注の数は、断片の道具がテストの文字列から並びの直後とかなの直後の例を拾って照らす |

  - P2 13 件 (SD1 3・SD2 5・SD3 2・SD4 2・SD5 1) の対応 (どれも直した。SD2-01・SD4-02 は SD1-02 と同じ直し):
    - **テストと変異の記録** (P46-63): SD1-02 19 回目の表の SC1-01 の行の「並びの頭が崩れ (あPI・んASDAQ)」は、入力の頭の NASDAQElementsu が崩れないことを書いていない (18 回目の表の SB1-01 の行への 19 回目の注も同じ) → その 2 か所に 20 回目の注を足した (API は 3 つの位置のどれも、NASDAQ は kyouha・ima の後だけ)。SD1-03 19 回目の箇条の「査読員の 7 本のうち 6 本は赤」は、そのうちの sc1_Uv6 が、開発機の NSSpellChecker では答えを変えず、模擬の判定器との違いで赤になることを書いていない → 20 回目の注を足した。SD1-04 テストの注の「見つかっている捕まえていない写し方」の一覧に、P46-62 の本文が挙げた sc1_luo が無い → P46-63 のテストの注に足した (20 回目の査読員の写し方の結果も足した)。SD2-04 記録の P46-62 の段落の族の内訳が、母数の 374 本の内訳として足りない (18 回目の査読員の 6 本が無い) → 段落の字は変えずに、すぐ後ろに 20 回目の注の段落を置き、P46-63 の段落には内訳を全部書いた。変異は、P46-62 までの 381 本と、20 回目の査読員の 10 本と、実装者の 7 本 (並びが 1 文字のときだけに当てる写し方) を、P46-63 のテストの UpstreamP46Tests で流した (切り替え表 358 本・1 行だけ変えた本物の木 40 本)。P46-62 のテストで赤だった 340 本は、どれも赤のままで、落ちるテストも 1 本目の赤の例も同じ。緑だった 41 本も、どれも緑のまま。査読員の 10 本のうち 5 本は赤 (1 本目の赤の例は、並びが 1 文字のときだけに当てる 1 本が足した例、ほかの 4 本が前からある例) で、5 本は緑 (頭が U の語のうち例の形 (kyouha の後の 2 文字の並び) でないものだけ・同梱の一覧に無い頭が大文字の E の語だけ・14 文字以上の並びだけ・kyouha と ima 以外の前置きだけに当てるもの。テストの注の「見つかっているもの」に書いた)。実装者の 7 本のうち、頭が大文字の語に当てる 4 本は足した例で赤、頭が小文字の語に当てる 3 本は緑 (開発機の NSSpellChecker の答えを変えない)。全テストでは、変異なし (852/852。切り替え表と本物の木で 1 回ずつ) と、赤の新しい 9 本 (どれも 851/852) と、緑の 49 本 (どれも 852/852) を流した (切り替え表 50 回・本物の木 10 回)。
    - **埋める文を作る道具・手順・検査の道具** (リポジトリの外): SD2-02 断片の道具の数の網は、その回の箇条だけを見ていて、前の回の箇条に足した注の数は網の外 → 20 回目の注の数も網に入れ、19 回目の注の数 (4 つ・22・3 つとも) を、20 回目の道具で数え直した (どれも合う。4 つと 22 は並びの直後の数で、SD5-01 の注を足した)。SD2-03 P46-62 のコミットの手順 (`rl16/commit62.sh`) の最後の確かめは、同じ sed の出力どうしを比べるだけで、必ず真になる → P46-63 の手順では、作った本文を git のコミットの本文と比べるようにした (P46-62 の本文が git の本文と同じことは、記録の道具が照らしていた)。SD2-05 20 回目の依頼文の、わざと直していないものの説明の句が、2 つの道具の片方に当たらない → 21 回目の依頼文で、2 つの道具の先端の取り方を分けて書いた (文書と記録には、この句は無い)。SD3-01 19 回目の箇条の「形の違う K の行があれば止めるようにした」は、文書の検査の道具 (`docs_lint.py`) の照らしより広い → その句に 20 回目の注を足し、道具の照らしも広げた。SD3-02 同じ道具の表の列数の照らしは、行の頭に空白のある表の行を表の行と見ない → 行の頭の空白が 3 つまでの行も、表の行と見るようにした (21 回目の注 (21 回目の査読 SE3-01): 箇条の中の、行の頭の空白 4 つの表 (各回の査読の表) は見ていなかった。21 回目に、それも見るようにした)。
    - **計画書・追従表の記録の行** (文書): SD4-03 18 回目の箇条の網の行への 19 回目の注が、注す句から離れて行の末尾にある → すぐ後ろに、どの句への注かを書いた 20 回目の注を足した。SD5-02 追従表と UPSTREAM_REVIEW の 19 回目の件数の注の「P0 の 1 件と P1 の 1 件、P2 の 2 件どうしは一部が同じ指摘」は、2 つの P2 の組が別だと読めない → 今の文なので、「ほかに」と「別の」で書き分けた。
    - **実装者が見つけたもの**: SD4-01 と同じ形の句は、17 回目の表の SA5-01 の行にもあった (K88 の行の、PR の状態を 2026-10-16 に GitHub で確かめたという句に、確かめた記録が無いと書いた) → その行にも 20 回目の注を足した。SD1-01 と同じ穴 (並びの長さの下の端) は、頭が A の語 (Ament) と頭が大文字の子音の語 (Cement) にもあった (並びが 1 文字のときだけ、その語に当てる写し方は、P46-62 のテストで緑のまま、開発機の NSSpellChecker の答えを 1 文字の並びの 15 入力で変える) → P46-63 で、1 文字の並びの後ろの Ament・Cement の例も足した (上の P1 の行)。頭が小文字の語は、1 文字の並びの後ろでは 5 つの木のどれも同じ答えで、並びが 1 文字のときだけ当てる写し方 3 本は NS の答えを変えないので、例を置いていない (21 回目の注 (21 回目の実装者 IM21-01): 頭が小文字の語の句は、測った 1 文字の並び X・Q・A・I・O の後ろでは正しかったが、26 字では正しくない。26 字で測ると、並び C・D・F・G・H・M・N・R・S・T の後ろの 36 入力で、NS の答えが取り込み前・main と違い (P46 だけの木から同じ答え)、P46-64 で、その形も例に置いた。写し方 3 本が NS の答えを変えないのは、26 字の 468 入力でも同じ)。
    - 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
  - **21 回目** (20 回目の対応のあとに増えた P46-63・変異・足した例の答えと文・作り直しの試しと記録 (SE1)、新しい埋める道具・コミットの本文の道具・手順と記録・断片の道具 (SE2)、20 回目の直し・照合の一覧・壊した入力・網の範囲 (SE3)、IMPROVEMENT_PLAN の 20 回目の表と箇条・前の回の箇条への注・(9) と検証の段落・P46 の本文と利用者向けの文書 (SE4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (SE5)): **SE1: P0 0・P1 1・P2 2、SE2: P0 0・P1 0・P2 2、SE3: P0 0・P1 1・P2 1、SE4: P0 0・P1 0・P2 2、SE5: P0 0・P1 1・P2 2** (合わせて P0 0・P1 3・P2 9。どの報告も、見出しの数と項目の数が合う。SE1-02・SE5-02 (P2) は同じ指摘)。SE2 は、はじめの査読員が認証の誤りで止まったので、同じ依頼文で別の査読員が査読した。SE1 は、足した 18 例の答えを 5 つの木で流して測りの json と 108/108 同じことを確かめ、自作の写し方を流した。SE3 は、壊した入力 (照合の道具の写しで 61 通り、予想と違う 0) を流した。SE4 は、20 回目の 16 項目が表と箇条に過不足なくあることと、「直した」の抜き取り 14 件を確かめた。SE5 は、本家の PR の状態を GitHub の API と PR の画面で確かめた (#622・#632・#424 は開いたまま)。着地の決め方のとおり、P1 の SE1-01 がテストの判定 (確かめ方) に当たるので、直したあとに、もう 1 回全体の査読をする。

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (SE1-01) | P46-63 で足した 1 文字の並びの例は X・A だけで、並びが X・A 以外の 1 文字のときだけに当てる写し方 (査読員の自作) は、全テストを緑のまま通り、NS の答えを変える (並び X・Q・A・I・O の 90 入力で、それぞれ 9)。テストの注と本文は、この形を、見つかっているものにも、例を置いていない形にも書いていなかった | **直した** (テストだけ。5ddc046)。P46-47 のテストの関数の最後に、1 文字の並び A〜Z の 26 字 × 語の形 18 (頭が大文字の語 Ament・Cement・Element と、頭が小文字の語 ament・emolument・pantsu の 6 語 × 位置 3) の例を 459 足した (pantsu の 78 例は同梱の表でも確かめる)。期待値は開発機の NSSpellChecker で流した統合の木の答えで、テストの模擬の判定器も同じ答えを返す (模擬の判定器と答えが違う 9 入力 (並び H の後ろの ament と、並び D・M の後ろの emolument) は置かず、注に書いた)。頭が大文字の語の 234 入力は、どの並びの後ろでも取り込み前と main も同じ答え (P46 だけの木と P46-47 の前の統合は 231 入力で崩れる) |
    | P1 (SE3-01) | 文書の検査の道具 (`docs_lint.py`) の表の列数の照らしは、箇条の中の、行の頭の空白 4 つの表 (計画書の各回の査読の表) を見ない。道具の注の「Markdown ではコードの塊」は、箇条の中では当たらない | **直した** (リポジトリの外の道具)。表の列数の照らしを、コードの囲み (``` か ~~~) の外で、行の頭の空白のあとが縦棒 (表の区切りの字) の行を表の行とし、表の最初の行と頭の空白の幅の差が 3 までの行を同じ表と数える形にした (箇条の中の、空白 4 つの表も見る)。空白 4 つの表の列を足す・減らす査読員の 2 通りは止まるようになり、ほかの通りの結果は直す前と同じ。今の文書では NG 0 で、計画書の空白 4 つの表の行 (見出しと区切りを除いて 114 行) も数えている。閉じていないコードの囲みも止め、囲みの中の行は列数を見ないことにした。道具の注の、空白 4 つ以上の行をコードの塊とする句は、箇条の外に限った (22 回目の注 (22 回目の査読 SF2-01・SF3-01・SF4-01・SF3-02): 空白 4 つの表の行の数は、手伝い TOOLS21 が直したときの文書 (20 回目の対応の文書) で数えた数で、この表を載せた 21 回目の対応の文書では、この表の 3 行を足して 117 行。「ほかの通り」は、前の回の査読員と、手伝い TOOLS20・TOOLS19 の通りのことで、TOOLS21 自身の通りには、結果が変わったものがある (囲みの中の行・タブの行・箇条の外の空白 4 つの行)) |
    | P1 (SE5-01) | P46-63 の、頭が大文字の語の例は 2〜13 文字の並びの後ろにだけあるという書き方 (UPSTREAM_REVIEW の P46-63 の行・計画書の 20 回目の表の SD1-01 の行・P46-63 の本文) に場所の限りが無く、テストの中身 (かなの直後にも Entitlement の例が 2 つある) と合わない (20 回目の SD5-01 と同じ形) | **直した**。UPSTREAM_REVIEW の行と計画書の行に 21 回目の注を足し (大文字の並びの直後の例のこと)、P46-64 のテストの注と本文にも場所の限りを書いた (P46-63 の本文は書き換えない) |

  - P2 9 件 (SE1 2・SE2 2・SE3 1・SE4 2・SE5 2) の対応 (どれも直した。SE5-02 は SE1-02 と同じ直し):
    - **テストと変異の記録** (P46-64): SE1-02・SE5-02 P46-63 の本文 19 行の、語の後ろの答えの言い方 (suru・su まで) は、同じ本文の 24〜25 行とテストの注 (suru・su・wokau まで) と合わない → P46-64 の本文に、正しい言い方を書いた (P46-63 は書き換えない)。SE1-03 テストの注の sc1_luo の「答えを変えるかは測っていない」は、測った記録 (20 回目の査読員の 36 入力・21 回目の査読員の 54 入力で 0) と合わない → P46-64 のテストの注を、その測りの結果に直した。変異は、P46-63 までの 398 本と、21 回目の査読員の 4 本と、実装者の 112 本 (並びの字ごとに分けた 78 本・頭が小文字の語の 26 本・並びの中身で条件を付けた 8 本) を、P46-64 のテスト (注の 5 か所を直す前の版。注の行を除いたコードは同じ) の UpstreamP46Tests で流した (切り替え表 474 本・1 行だけ変えた本物の木 40 本)。P46-63 のテストで赤だった 349 本は、どれも赤のままで、落ちるテストも 1 本目の赤の例も同じ。緑だった 49 本も、どれも緑のまま。査読員の 4 本のうち 3 本 (並びが X・A 以外の 1 文字のときだけ) は足した例で赤、1 本 (頭が小文字の語に当てるもの) は緑 (開発機の NSSpellChecker の答えを変えない)。実装者の 112 本のうち 79 本は赤、33 本は緑 (答えを変えない 27 本 (並び L の後ろの Ament だけに当てる 1 本と、頭が小文字の語に当てる 26 本) と、例の並びでない 2 文字以上の並びの後ろだけに当てる 6 本。6 本は答えを変えるので、テストの注の「見つかっているもの」に書いた)。全テストでは、変異なし (852/852。切り替え表と本物の木で 1 回ずつ) と、新しい写し方のうち 15 本 (赤の 5 本はどれも 851/852、緑の 10 本はどれも 852/852) を流した (切り替え表 16 回・本物の木 1 回)。P46-63 のテストで緑だった 49 本の全テストは、P46-63 のときの全テスト (どれも 852/852) と、P46-64 が変えたのは UpstreamP46Tests.cs だけであることから導いた。
    - **埋める文を作る道具・断片の道具** (リポジトリの外): SE2-01 20 回目の埋める文 (P63A・P63R・P63C) の、手で書いた事実に assert が無い → 21 回目の埋める道具で、その事実 (並びの長さ、注の句、緑の 2 本の条件) を照らすようにした (文は変えない)。SE2-02 20 回目の断片の道具の、注の句の照らし 2 つが、句の中の数を照らしていない (1 つは必ず真) → その 2 つを、句の数を照らす式に直した (作り直す手順は、毎回この道具を流す)。SE3-02 19 回目の箇条の SC3-05 の行への 20 回目の注の、それらも止めるようにしたという句は、道具より広く読める → その注に 21 回目の注を足し、止める形と止めない形を書いた。
    - **計画書・追従表・利用者向けの文書** (文書): SE4-01 KNOWN_LIMITS の K88 の (2) の `medtechnoyouna` の例が、NS の答えだと書いていない → 書き分けた (BI では英字のまま)。SE4-02 計画書の (9) の #484 の跡に、P46 の調査が §2 の B2 で見た `ed1ba78` が無い → 足した。SE5-03 追従表の P46-20 の行の「実機で `shatす` になるのを直す」は、実機で見た記録が無い → 開発機の NSSpellChecker を差し込んだ再現でのことと書き直した (本家 #594 は Windows の報告)。
    - **実装者が見つけたもの**: IM21-01 20 回目の箇条の「頭が小文字の語は、1 文字の並びの後ろでは 5 つの木のどれも同じ答えで」(P46-63 の注と本文も同じ言い方) は、測った 5 字 (X・Q・A・I・O) の後ろでは正しかったが、26 字では正しくない (26 字で測ると、並び C・D・F・G・H・M・N・R・S・T の後ろの 36 入力で、NS の答えが取り込み前・main と違う。P46 だけの木から同じ答え) → 20 回目の箇条のその句に 21 回目の注を足し、P46-64 のテストの注を直して、その形も例に置いた (模擬の判定器と NS で答えが違う 9 入力は置かない)。36 入力は語として通らない英字の並び (例: kyouhaRamentsuru が、取り込み前・main の きょうはRamentする から きょうはRamenつる に。打鍵の例の形 (コード片) では書かない。答えは実装者の測り rl18/p64ns.json と、P46-64 のテストの期待値) で、利用者向けの KNOWN_LIMITS に書くかは、まだ決めていない (要判断)。並びの中身 (字) で条件を付けた写し方 (例の並びでない 2 文字以上の並びの後ろだけに当てるもの) は、テストで捕まえず、NS の答えを変える → テストの注の「見つかっているもの」に書いた (1 文字の並びは 26 字すべてに例を置いたので、この形は 2 文字以上の並びのもの)。
    - 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
  - **22 回目** (21 回目の対応のあとに増えた P46-64・変異・足した例の答えと文・作り直しの試しと記録 (SF1)、埋める道具・コミットの本文の道具・手順と記録・断片の道具 (SF2)、21 回目の直し・照合の一覧・壊した入力・網の範囲 (SF3)、IMPROVEMENT_PLAN の 21 回目の表と箇条・前の回の箇条への注・(9) と検証の段落・P46 の本文と利用者向けの文書 (SF4)、追従表・UPSTREAM_REVIEW・変わっていないはずの文書と木・2 つのコミット本文・本家の事実 (SF5)): **SF1: P0 0・P1 0・P2 4、SF2: P0 0・P1 1・P2 3、SF3: P0 0・P1 1・P2 2、SF4: P0 0・P1 1・P2 1、SF5: P0 0・P1 1・P2 1** (合わせて P0 0・P1 4・P2 11。どの報告も、見出しの数と項目の数が合う。SF2-01・SF3-01・SF4-01 (P1) は同じ指摘で、SF1-01・SF2-04 (P2) と SF4-02・SF5-02 (P2) も、それぞれ同じ指摘。SF1-02 は SF2-03 の一部と、SF1-04 は SF5-01 の一部と同じ)。SF1 は、足した例のうち 130 入力の答えを 5 つの木で流し直して測りの json と違い 0 を確かめ、自作の写し方を 3 本流した。SF3 は、全部の一覧の verify (NG 0) と壊した入力 (docs_check の写しで 47 通り・docs_lint の写しで 22 通り) を流した。SF4 は、21 回目の 12 項目が表と箇条に過不足なくあることと、「直した」の抜き取り 10 件を確かめた。SF5 は、本家の PR の状態を GitHub の API で確かめた (#622・#632・#424 は開いたまま)。着地の決め方のとおり、P1 はどれも製品のコード・テストの判定・利用者向けの文書ではない (計画書の表の文と UPSTREAM_REVIEW の行) ので、直したあとは、直した所だけを別の査読員 1 人が確かめて着地する。22 回目の対応は、文書とリポジトリの外の道具だけ (テストとコードは P46-64 のまま) (22 回目の確かめの注: 直した所は、別の査読員 1 人 (SG1) が確かめた。結果は P0 0・P1 0・P2 5 で、着地の決め方のとおり、この箇条を載せた文書を着地した。P2 のうち、この箇条の言い方の 3 件 (SG1-03〜05) には、着地のあとの文書のコミットで、その句のすぐ後ろに注を足した。リポジトリの外の 2 件 (道具の注と記録の段落の時刻の書き方。SG1-01・02) も直した)。

    | 重さ | 指摘 | 対応 |
    |---|---|---|
    | P1 (SF2-01・SF3-01・SF4-01) | 21 回目の表の SE3-01 の行 (埋める文 T21LINT) の「今の文書では NG 0 で、計画書の空白 4 つの表の行 (見出しと区切りを除いて 114 行) も数えている」の 114 は、手伝い TOOLS21 が直したときの文書 (20 回目の対応の文書) で数えた数で、この表を載せた 21 回目の対応の文書の数と合わない。埋める道具は、直したときの文書で流した出力だけを照らしていた | **直した**。21 回目の表のその行に 22 回目の注を足した (114 行は、手伝い TOOLS21 が直したときの文書で数えた数で、この表を載せた 21 回目の対応の文書では 117 行)。22 回目の埋める道具は、注の 2 つの数を、TOOLS21 が直したときの文書で流した docs_lint の出力と、21 回目の対応の文書を作り直したときの docs_lint の出力から取り、差の 3 行が 21 回目の表の P1 の行の数と同じことも照らした。表を足す回は数が変わるので、22 回目の注には、数えた文書を書いた |
    | P1 (SF5-01) | UPSTREAM_REVIEW の P46-64 の行 (埋める文 P64R) の「459 例で塞ぎ」は書き過ぎ。テストの注と本文は、置かなかった 9 入力だけに当てる写し方は捕まえないと書き、18 回目の SB1-01 は「埋め」を書き過ぎとした。459 が、468 入力から 9 入力を除いた数であることも書いていない | **直した**。UPSTREAM_REVIEW の P46-64 の行に 22 回目の注を足した (459 例は 468 入力から 9 入力を除いた数で、その 9 入力だけに当てる写し方は捕まえないので、穴は狭めたのであって、塞いではいない)。計画書の P46 のやったことの P46-64 の句にも、459 例の出どころの注を足した |

  - P2 11 件 (SF1 4・SF2 3・SF3 2・SF4 1・SF5 1) の対応 (どれも直した。同じ指摘は 1 つの直しにまとめた):
    - **テストと変異の記録** (P46-64 は書き換えない): SF1-01・SF2-04 P46-64 の本文・記録の段落・手順の注の、変異を流したあとに注を直したという句は、時刻の順と合わない → P46-64 の本文と記録の段落の字は変えずに、記録の段落のすぐ後ろに 22 回目の注の段落を置き、手順の道具の注も直した (注の 5 か所のうち 3 か所を直したのは、変異の流し (23:43〜00:20) の途中の 00:16〜00:17 で、残りの 2 か所は最初のコミットのあと。流した木のテストは 23:42 の版のままで、本物の木もその木を写すので、結果には効かない)。 SF1-04 計画書の P46 のやったことの P46-64 の句 (P64D) と UPSTREAM_REVIEW の行 (P64R) は、459 例が 468 入力から 9 入力を除いた数だと書いていない → どちらにも 22 回目の注を足した。SF1-03 記録の段落の道具 (`rl18/evidence64.py`) は、DRY でも、当て直しの確かめのための一時のフォルダーを作って消す → 道具の説明に書き、確かめの依頼文にも書いた。
    - **埋める文を作る道具・手順の道具** (リポジトリの外): SF2-02 21 回目の埋める道具の、模擬の判定器と NS の答えが違う 9 入力の照らしは、必ず真になる式だった → 468 入力の答えから数え直し、9 入力の中身も照らす形にした (文は変えない)。SF2-03 (SF1-02 を含む) 道具の注の 3 か所 (変異の道具の元の木の注・コミットの手順の注の数・21 回目の埋める道具の説明の緑の本数) が、古いか、照らす範囲より広い → 直した。
    - **検査の道具** (リポジトリの外): SF3-03 文書の検査の道具 (`docs_lint.py`) は、表の見出しの行と区切りの行のセルの数が違う形 (GFM では表にならず、段落になる) を止めなかった → 手伝い TOOLS22 が、表の 2 行目が区切りの行なら、見出しの行と区切りの行のセルの数を比べ、違えば止める照らしを足した (止めるのは、本文の行の照らしと同じく、見出しの行か区切りの行が足した行のときだけ)。SF3-02 21 回目に直した道具は、箇条の外の、行の頭の空白 4 つ以上の囲みの字の行も囲みとみなすので、その間の本物の表を見落とす。21 回目の表の「ほかの通り」の句も、手伝い TOOLS21 自身の通りには当たらない → 囲みの読み方は変えずに、見落とす 2 つの形 (箇条の外の、行の頭の空白 4 つ以上の囲みの字の行と、箇条の中の囲みが箇条の終わりで閉じる形) を道具の注に書き、21 回目の表のその行に 22 回目の注を足した (査読員の試しの囲みの字の行は箇条の中にあり、CommonMark では箇条の中の囲みになるので、止めるには箇条の中の囲みの閉じ方も変える必要がある) (22 回目の確かめの注 (SG1-04): 箇条の中にあるのは、試しの 2 つの囲みの字の行のうち最初の行で、2 行目は、そのあいだの表で箇条が終わったあとの、箇条の外の、行の頭の空白 4 つ以上の囲みの字の行)。22 回目に査読した文書 (21 回目の対応の文書) では、行の頭の空白 4 つ以上の囲みの字の行は 0 行で、182 個の表のどれも、見出しの行と区切りの行のセルの数が同じ (22 回目の確かめの注 (SG1-03): 182 個は、検査の道具の対象の 9 文書に docs の下のほかの文書を足した 32 文書で数えた表の数で、対象の 9 文書だけなら 112 個、そのうち計画書は 74 個)。TOOLS22 が、直す前と直したあとの道具で壊した入力を流し直すと、止まるようになったのは、査読員 SF3 の 2 通り・TOOLS21 の 1 通り・TOOLS22 が足した 6 通りで、TOOLS21 の 2 通りは止まったまま NG が 1 つ増えた。ほかの通り (SF3 の 20 通り・21 回目の査読員 SE3 の 16 通り・TOOLS21 の 12 通り・TOOLS20 の 62 通り・TOOLS19 の 14 通り・TOOLS22 の 5 通り) と、作り直しの手順と同じ流し方の出力 (NG 0) は、直す前と同じ
    - **追従表・UPSTREAM_REVIEW** (文書): SF4-02・SF5-02 P46-64 の行の出どころが、件名の番号と合わない → 追従表の P46-64 の句を件名と同じ番号にし (今の文なので直接直した) (22 回目の確かめの注 (SG1-05): 番号のほかに、中身の句にも、件名のとおり sc1_luo の注を直したことを足した。この箇条を載せた文書のコミットの本文には、その句の直しを書いていない)、UPSTREAM_REVIEW の行には 22 回目の注を足して、sc1_luo の注の直し (SE1-03) も書いた。
    - 作り直した文書の分け方: 期待値 329 組 = 入力と同じ行に区切りのある字として書いたもの 291 + 木の答えを写したもの (`FROM_TREE`) 38 (重なり 0)
- **main への統合** (枝を作っている間に main が進んだため。merge = d3709bc、P46-41 = 3b6af06):
  - main は `0dee8de` から `657d042` まで進んだ。別セッションの変更は、T1 (`d36ef48`。見つけた英語の区間を、後ろの長い語で捨てる条件に AND を足す)・H1 (`a093319`。l・x・t の守り)・日時の候補 P47 (`4970657`)・伸ばす音の う から始まる語 (`unit`) を t + s の つ にまとめる守りで見ない `そうについて` (`58ec05d`・`9796671`) と文書。統合を作り直している間にも main は進んだが (文書だけの変更)、コードの木 (`src` `mac` `tools` `dictionaries` `eval`) が同じことを `git` で確かめて、ゲートの結果をそのまま使った。**rebase でなく `--no-ff` の merge** にした (査読で確かめたコミットの hash と、コミットごとにビルドして全テストが通った結果をそのまま保つため。枝は 44 コミットで、P46-35 は欠番)。両方が変えたファイルは `CompositionDetector.cs`・`CompositionText.cs`・`CompositionController.cs` で、変更した場所が別々なので自動マージは衝突なし。
  - merge のコミットだけの木で全テスト 849/850。赤の 1 本は、P46-23 が「既知の限界」として固定していた `adtechnoyouna` (T1 で `adtechのような` に直ったので、テストに書いた「直ったら期待値を変える」の合図どおり)。P46-41 で期待値を変えて 851/851 (main 側の 791 + P46 の 60)。残る 3 形 (`adtechmade` `adtechhanami` `adtechhaha`) を限界として固定した。コードは変えていない。
  - **統合でだけ起きる崩れ (P46-47)**: 5 回目の査読のあいだに、4 つの木の三者比較 (下) で、統合した木だけが main とも P46 だけとも違う答えを出す入力が見つかった。探索員 4 人 (LV・TSU・LETTERS・P46WORDS) が、のべ約 44 万入力 (BI 約 37.6 万・NS 約 6.5 万。入力の作り方が重なるものは重複して数えた。P46-47 のコミット本文は「約 20 万入力」と書いたが、4 本の報告の入力数を足すとこの数になる。コミット本文は書き換えない) を流して探し (全数ではない。流した範囲は下の「未確認」(11))、2 つの形に絞れた。いずれも NS (実機と同じ NSSpellChecker を差し込んだ再現) での崩れで、BI は同梱の表だけで形 B の語を英単語と見ないため、統合でだけの崩れは起きない。**形 A**: 前が o・u で終わる日本語 (う が長音になる並び。`sou`・`kono` など) + `unemployment` + する (`souunemploymentsuru` が、取り込み前・main・P46 だけでは `そうunemploymentする` なのに、統合だけ `そううnemployめんつる`。読み 12 × 語尾 2 の 24 入力)。**形 B** (A より広い): 同梱の英単語表にない、母音で始まる語 (`empowerment`・`entitlement`・`enablement`・`impeachment`・`encroachment` ほか。NS は英単語と答える) + する (`kyouhaempowermentsuru` が、3 つの木では `きょうはempowermentする` なのに、統合だけ `きょうはempowerめんつる`)。前の日本語は何でもよく、語尾は `su` で始まるもの (`su`・`suki`・`suru`・`surudake`) だけ。同梱の表にない母音始まり・t 終わりの語 2,256 語 (`ima` + `suru`) のうち 119 語 (`-ment` 110・`-dent` 5・`-cant` 2・`-pant` 1・`-tint` 1)。同梱の表にある語 550 語は崩れない。原因は、P46-20 の「かな + つ が読みの頭になる断片 (`ment` = めんつ) は日本語として飛ばす」規則と、main の「伸ばす音の う から始まる語 (形 A)・同梱の一覧にない母音 1 つの単位から始まる語 (形 B) は見ない」規則が、単独では互いの取りこぼしを救い合っていたのに、両方入ると救いが消えること (単独の main では断片 `ment` を NS が単語と答えて救い、単独の P46 では長い語が救う。両方入ると、長い語も断片も見られず、t と s が つ にまとまる)。**直し** (P46-47。統合した木にだけ載せる): 飛ばす断片が、知っている英単語で、さらに前へ伸ばした並びも知っている英単語の中にあるとき (`unemployment` の中の `ment`) は、読みの頭として飛ばさない。断片の前に長い語がない `mentsu`・`shatsu`・`doitsu`・`gattsu`・`mahoutsukai` は P46-20 のとおり日本語のまま。探索員が見つけた崩れ 659 入力 (形 A の 24 入力と、形 B などの BAD 行) は、直した後に NS で 659 入力すべて取り込み前の答えに戻り、戻らなかった入力と、統合では合っていたのに直した後に違う入力は 0 (`c_check6.py`)。BI では形 A が main の時点で崩れている (main の `IsLongVowelU` の変更による。取り込み前と P46 だけは正しい) ので、P46-47 では直らない。P46 とは無関係で、「P46 で触れなかった既知の穴」の (6) に書いた。
  - **P46-47 の影響の測り直し** (コミット本文で「文書に載せる」と書いたもの): 直す前の統合 (P46-41 = 3b6af06) と直した後 (P46-47 = 485cb80) に、探索員の入力の作り方をそのまま使って同じ入力を流し、答えが違う入力をすべて集めた。違う入力は BI で 0 件 (BI は `ment` を単語と答えないので、この直しは効かない)。NS では、LV 504 入力・TSU 180 入力・LETTERS 326 入力・P46WORDS 262 入力 (重複を除く)・RM 930 入力 (RM が流した 43,606 入力 = 断片 64 種 × その断片で終わる web2 の語 × 前 8 通り × 語尾 2 通りの 42,976 入力と、前の 1〜2 単位 + 断片で語になる 105 組 × 前 3 通り × 語尾 2 通りの 630 入力の中で、922 + 8 件) が違い、**すべて直し** (取り込み前・main・P46 だけの 3 つの木と同じ答えに戻る。LV の 504 入力のうち 78 入力は、4 つの木とも元から誤っていた形が他の木と同じ誤りに戻っただけで、正しくなったのではない)。悪化・意図しない変化は 0 件。崩れも消えた: 直す前の統合で崩れていた入力 (LV 504・TSU 180・P46WORDS 119 + 36) は、直した後は 0 件。`kyouhaimplicantsuru` のように変換ボックスの表示 (`…impぃかんつる`) と確定 (`…いもぃかんつる`) が食い違っていた入力も、表示も確定も `きょうはimplicantする` になった (表示≠確定の入力の集合は、直した後は直す前の部分集合。直した後だけに出る食い違いは 0)。日本語の読み (製品の読みの辞書 `dictionaries/readings.txt` (JMdict 由来、データ 20,181 行) の読みのうち 2 文字以上の 20,127 語 (重複を除く) をローマ字にした評価用の入力のうち、`tsu` を含む 1,567 語) を、そのまま・`kyouha` の後ろ・`wo` の前・`ima` の後ろの 4 通りで流した 6,267 入力 (1,567 × 4 = 6,268 から重複 1 件 (`imahitotsu`) を除いた。NS) では、直す前と直した後で答えが 1 入力も変わらない (`mentsu`・`dentsu`・`pantsu` など日本語として読む形は今までどおり)。短い語になる並び (`a` + `ment` = `ament`、`i` + `dent` = `ident`。NS は単語と答える) の後ろの `su` は、日本語の めんつ・でんつ ではなく英字のまま残る (`imaamentsuru` → `いまamentする`、`kyouhaidentsuru` → `きょうはidentする`。取り込み前・main・P46 だけの 3 つの木と同じ答え)。LETTERS の 3 者比較で、直した後の統合が BAD と出た 1 入力 (`certificatesgagakui-repleted`) は、その実行 (5 回目の査読員の NS) では main の木だけが `せrちふぃcatesgagakuiーrepleted` と答え、統合は取り込み前と同じ `certificatesががくいーrepleted` だった。10 回目に同じ木 (main と Core・辞書が同じ複製) で NS を 3 回流し直すと 3 回とも `certificatesががくいーrepleted` で、ほかの木も BI・NS とも同じ答えなので、main の答えは NS の揺れとみられる (原因は推測)。どちらにしても統合は取り込み前と同じで、直す前も同じ答えなので、P46-47 と関係ない。表示だけが変わる入力も 2 件あった (`kyouhaaldeamentsuru`・`imaaldeamentsuru`。確定は 5 つの木とも同じ `…あおであめんつる` で、表示だけ、取り込み前・main・P46 だけと同じになった)。
  - **ゲートを通した木**: 回帰ゲート (eval)・4 つの木の比較・文書の例・手動確認のリスト・Mac ビルドは P46-47 の木 (485cb80) で通した (文書の例は、そのあと例を足して、同じコードの木で流し直している。7 回目の査読 RP2-02)。その後の P46-48〜P46-64 はテストだけで、`src/Meltype.Core`・`src/Meltype.Mac.Native`・`mac`・`tools`・`dictionaries`・`eval` の木の hash は P46-47 と同じ (`git rev-parse` で比べた)。全テストは P46-64 の木でやり直して 852/852 (警告 0)。着地の手順のスクリプトは、全テストを通した先端 (P46-64) から着地する枝までの差が文書 (`docs/`・`mac/README.md`・`mac/INSTALL.txt`) だけで、ゲートを通した木 (P46-47) からの差がテストと文書だけであることを確かめてから進み、既定では確かめるだけで merge しない (6 回目の査読 RN-02、7 回目の査読 RP1-07・08。最初の版は、全テストを通した先端をそのまま「ゲートを通した木」と表示していた)。
  - K 番号: main で K83〜K86 が別セッションに使われていたので、P46 の限界は K87〜K89 に振り直した (枝を作った時点の番号は K83〜K85 で、これより前のコミット本文に出てくる K83〜K85 はその番号。テストの中の注は P46-41 で K87・K88 に直した)。
  - **4 つの木 (取り込み前・main・P46 だけ・統合) に同じ入力を流して、効き方が食い違わないか見た**。統合した木 (P46-47 = 485cb80) で、日本語の読み 15,000 (乱数の種 11)・英語 15,000・T1/H1 の l・x・t に当たる語を狙った英語 32,000 (入力の作り方 2 通り × 16,000)・P46 で足した語の前後 684・伸ばす音で終わる読み + 英語 32,400 の計 95,084 入力を、BI と NS で流した。main と P46 が別々の答えに変えた入力は 0。統合の答えがどちらの変え方にも当てはまらなかったのは NS の 4 入力 (BI は 0) で、いずれも統合のほうが正しい: `adtechno` → `adtechの`・`kyouhaadtechno` → `きょうはadtechの` (P46 の `adtech` と T1 の組み合わせで直る。前回のゲートでも同じ 2 入力)、`torpedoeswo` → `torpedoesを`・`kyouhatorpedoeswo` → `きょうはtorpedoesを` (今回のゲートで初めて出た 2 入力。取り込み前と P46 だけの木では今は `torpedoeswo`・`きょうはとrpedoeswo` で、main と統合の木で `torpedoesを` になる。比較を流したときだけ、取り込み前の木の答えが `torpedoesを` と出て「P46 だけが変える」に分類されたもので、NS の揺れと見ている。流し直すと取り込み前の木は 3 回とも `torpedoeswo`、分類は「main だけが変える」で統合の答えと合う。揺れそのものは流し直しでは再現しなかったので、原因は分からない (その実行のときだけ判定器の答えが違った、という推測)。同じ実行では、BAD にならなかった入力 1 つの分類も変わっていた (l・x・t を狙った英語の NS で、「P46 だけが変える」が前回のゲートの 2 入力から 5 入力に増えた。BAD の 2 入力のほかの 1 入力は、記録に残っていないので特定できない。統合の答えは P46 の答えと一致している)。伸ばす音で終わる読み + 英語の 32,400 入力 (形 A を狙った入力) は、P46-47 の前の統合では NS で食い違いが 24 入力あったが、P46-47 の後は 0。main だけが答えを変える入力は BI 132・NS 102 あり、統合の答えはどれも main と同じ。
  - **`adtech` の測り直し**: 統合した木では `adtechno…` の 14 形が直り、残るのは 3 形 (K88 (2))。
  - **NS の答えは時刻でわずかに揺れる**: 同じ木・同じ入力でも、測るたびに数入力変わる (`adtech` の 1,218 形で 910 → 911 形、`adtechno` + 読み 3,000 語で 4 → 5 形 (`adtechnomonseki` が `adtechのもんせき` から `adtechnoもんせき` に変わった)、`data` + 英語の全数で 4,274〜4,295 語)。BI は変わらない。文書の NS の数字は、この揺れで結論が変わらない幅で書いた。
  - **統合した木 (P46-47 = 485cb80) と main (`657d042`) の比較** (`final_regress.py`。日本語 15,000・英語 15,000 入力、BI と NS): 変わるのは日本語側で BI 3 入力・NS 19 入力、英語側で BI・NS とも 2 入力 (英字が増えた入力は日本語側で 0。英語側の 2 入力は `data` → `data`・`datawo` → `dataを` の改善)。**変わった入力の一覧は、試し統合 (P46-34 + main `5fdb413`) の一覧と 4 ファイルともバイト単位で同じ**。判定のファイルの変わり方は、試し統合との `git diff` で確かめた (4 回目の査読 RJ-05 を受けて範囲を書き分けた): `Detection/` 以下・`CompositionDetector.cs`・`RomajiTypoCorrector.cs` は変わっていない。`CompositionText.cs` は 3 つの変更が増えた (P46-42 の、先頭が `.` の英字区間を確定の直前の打ち間違いの直しから守る分と、main の `そうについて` の守りと、P46-47 の、長い英単語の中の断片を読みの頭として飛ばさない分)。`CompositionController.cs` は P47 の日時の候補 (候補の末尾に足すだけ) が増えた。P46-36〜40・43〜46 は保存・全消去・警告・文面・テストだけで、判定に触れない。
  - `data` + 英語 (K87) の全数測定: 4〜10 文字の英小文字の語 33,299 語すべてに `data` を前置して流すと、英字のままにならないのは BI で 1,842 語 (5.5%)、NS の表で 4,274〜4,295 語 (12.8〜12.9%。NS の答えは時刻でわずかに揺れるので幅で書いた)。悪化は 0。
  - 文書に書いた打鍵の例と、実機で確かめる入力のリスト (追従表に書いたもの) を、統合した木で流して照合した: `handson_check.py` (実機確認リストの 42 入力) は 42 入力すべて期待どおり。`verify_docs_examples.py` (文書の例と確認用の入力 130 件 = 78 入力を BI 58・NS 72。照合の一覧 (6 つ) の入力 116 のうち、文書に書いていない確認用は 3 入力 (どれも CASES にあり、CASES で 4 件。そのうち 1 入力は PRE にもある)。ほかに、P46-42 の前の木の答えを確かめる補助 9 行と、変換ボックスの表示を確かめる 32 行 (うち 30 行は 5 つの木 (取り込み前・main・P46 だけ・P46-42 の前・統合) × BI/NS、2 行は統合した木だけ)、文書が木ごとの答え (取り込み前・main・P46 だけ・P46-47 の前の統合・統合の 5 つと、文が書いていれば P46-42 の前) を書いた例の確定と表示 677 行 (確定 475・表示 202。うち P46-42 の前の木 10 行)、変換の候補に語が入る・入らないという主張を、入力のあとに Space を押したときに Meltype が出す候補 (変換エンジンを通さない、試験用の画面の候補。変換エンジン azooKey の候補は入らない) で確かめる 4 行) は全部一致 (NG 0)。件数は、検証スクリプトを統合した木で流し直した出力の「集計:」行から機械で取った。P46 で文書に足した行の「打つ → 結果」の例は、この照合か実機確認リストに入れるか、理由を付けて外す (調査時点のまま残した UPSTREAM_REVIEW の §1 以降・記号の説明・長い列の一部など)。文書を作るたびに `docs_check.py` がこれを確かめる (10 回目に、照合に入っていない例が見つかったので足した。11 回目に、大文字で始まる入力・1 つのコード片の中の矢印・括弧の結果の形も拾うようにした。12 回目に、隣り合うコード片を順に組にし (前の組の結果のコード片も次の組の入力にする)、矢印の後ろのバッククォートの無い結果・空白を含む入力・数字で始まる入力も拾い、照合の一覧の期待値が文書に字として出てくるか、木の答えを写したもの (`FROM_TREE` の一覧) かも確かめるようにした。13 回目に、期待値は、文書のその入力を書いた行に区切りのある字として書いてあるか (12 回目の確かめは、文書のどこかにほかの語の一部として出てくれば通り、入力と結び付いていなかった)、`FROM_TREE` にあるかのどちらか一方 (重なり 0) であることを確かめ、網が組にした結果が、その入力の一覧の値のどれかと同じこと (逆向き。網が組にしない結果は見ない) も確かめ、括弧の中の矢印・並べた入力・漢字だけの結果・ASCII で書いた矢印の形も拾うようにした。14 回目に、括弧の中の ASCII で書いた矢印も拾い、照合の一覧の確定と表示の行の数を、それぞれ検証スクリプトの出力の数と比べるようにした。15 回目に、1 つのコード片の中の ASCII で書いた矢印も拾うようにした (13 回目と 14 回目の文は ASCII の矢印をどこでも拾うと読めたが、1 つのコード片の中は → だけだった)。16 回目に、隣り合うコード片のあいだの字の数え方でも、ASCII で書いた矢印を → と同じにつなぎとして数えないようにした (15 回目までは、矢印と後ろの空白の 3 字を数えた)。17 回目に、網の動きは変えずに、拾う形の言い方 (打つ入力に見える並び・かなか漢字・括弧の中の字・結果の終わり) を網のコードのとおりに直し、作った文書の中の埋める文の数が、断片の埋める所から決まる数と同じことも確かめるようにした。18 回目に、網の動きは変えずに、(3)・(4) の括弧の前に空白を挟んでよいことと、(4) の括弧が丸括弧 (半角か全角) であることと、(5) で止まる空白に全角の空白とタブも入ることを、網のコードのとおりに書き足した。19 回目に、網の動きは変えずに、(4) の括弧の中にバッククォートか別の丸括弧があると拾わないことと、(5) の矢印が打つ入力のすぐ後ろ (空白だけを挟んでよい) にあることと、(3)〜(5) のどの空白にも全角の空白とタブが入ることを、網のコードのとおりに書き足した)。網が見るのは、P46 が変えた・足した docs と mac の .md・.txt (コードの中の文は見ない) で、拾うのは、打つ入力 (ASCII だけの並びで、空白が無ければ、頭が英字か `.`・`@`・`_`・`-` のもの (「/」「#」「'」など、ほかの記号で始まるものは拾わない) か、頭が数字で英字を含むもの (数字だけのものと、頭が数字の 7〜40 字の、数字と a〜f だけのものは拾わない)。空白があれば、頭と尻が空白でなく英字を含むもの) をバッククォートで囲んだコード片と、その結果を次のどれかの形で書いた例だけで、ほかの形は拾わない (例はこの形のどれかで書く): (1) 1 つのコード片の中の `打つ → 結果` (`->` も。矢印の左は、コード片の頭からの空白の無い 1 語)、(2) 隣り合う 2 つのコード片の後ろのほう (かなか漢字を含むもの。あいだが矢印だけなら、かなか漢字が無くてもよい) で、あいだが「・」「、」空白だけでなく、24 字以内のもの (頭の空白と、つなぎの 1 つ (→・->・が・は・で・を) とその後ろの空白は数えない)、(3) 打つ入力のすぐ後ろ (空白だけを挟んでよい) の丸括弧 (半角か全角) の中の、「・」「ー」でないかなを含み、かな (「・」「ー」も)・半角の英数字・`.`・`@`・`_`・`-` だけの並び (漢字・空白・「、」・ほかの記号・全角の英数字が 1 字でも入ると拾わない)、(4) 打つ入力のすぐ後ろ (空白だけを挟んでよい) の丸括弧 (半角か全角) の中に矢印があるときの、矢印の右 (かなか漢字を含むもの。括弧の中にバッククォートか別の丸括弧があると拾わない)、(5) 打つ入力のすぐ後ろ (空白だけを挟んでよい) の矢印の、さらに後ろのバッククォートで囲まない、かなか漢字を含む並び (空白 (全角の空白とタブも)・バッククォート・「、」・「。」・閉じ丸括弧 (半角と全角)・「」」の前まで。止まるのはこれだけで、頭の「「」と、ほかの閉じ括弧 (「】」「』」など) も結果に入る)、(6) 並べた入力 (「・」「、」空白だけでつないだコード片) の後ろの結果 (下の組み方)。(3)〜(5) の空白 (打つ入力と括弧・矢印のあいだに挟んでよい空白と、括弧の中・結果の終わりの空白) には、全角の空白とタブも入る。ここで「かなか漢字を含む」の「かな」は「・」「ー」も数え (「・」や「ー」と英字だけの結果も拾う)、漢字は「々」と CJK の拡張 A も数える。矢印の無いつなぎの後ろの裸の結果・表の隣の欄の裸の結果・隣り合わない結果などは拾わない。網はコードブロックの中と外を見分けないので、コードブロックの中でもバッククォートで囲んだ例は拾い、囲まない例は拾わない。網が組にしない結果は逆向きの確かめに入らない。その結果を書き誤ると、照合の一覧の期待値が、その入力を書いたどの行にも無くなったときだけ、期待値の出どころの確かめで止まる (入力を書いた行に、同じ値が別の主張 (変異の木の答えなど) として残っていると通り、入力の無いほかの行に残っているだけなら止まる)。一覧に無い主張と、木の答えを写した期待値 (`FROM_TREE`) の入力は、組にしない形で書き誤っても止まらない。並べた入力は、矢印のあとの結果が 1 つなら並べた入力のどれにもその結果を、入力と同じ数なら順に 1 つずつ組み (矢印の無いつなぎは、結果がどれもかなか漢字を含むときだけ)、数が合わない並びは組まない。照合済みかは入力ごとに見る (文が言う木・モード・確定か表示かごとには見ない。12 回目・14 回目・15 回目の査読が挙げた、照合していなかった主張は一覧に足した)。
- **悪化・限界** (KNOWN_LIMITS の K87〜K89): 効かない形が K87 (日本語の直後の `vibecoding`・`bio-break`・`data`、複数形、`kyouhaime`、`metadata`、`data` + 英語 ほか)。**新しく悪化する形が K88**: `…fy` で終わる英単語を空白なしで続けた形 (`selfyou` → `せlふょう`。682 入力中 186 入力が英字 → かな)、u を落とした打ち間違いの補正 2 件 (`mafyu`・`ayafya`)、文頭の `adtech` + 英語の語に読める続き (`adtechmade` `adtechhanami` `adtechhaha`。無作為の 1,218 形のうち 3 形)、先頭が `.` の日本語入力 (1000 入力中 15〜31 入力。確定のとき打った英字が消える・変わる不具合は、4 回目の査読で見つかり P46-42 で直した。先頭が `_` `@` などの入力には同じ不具合が P46 の前から残る = 5 回目の査読 RL-01)、73 文字以上の意味のない小文字列 (100 本中 3〜8 本で結果が変わる。おもに 1〜2 文字)、3 文字の語 + 記号のライブ変換。`.broken` と保存の停止の限界が K89。P46-3 は、カタカナの `イメ` が 3 番目に下がる (調査時)。
- **取り込まなかったもの**: B7 (#498)・B8 (#716)・B9 (#127)、C3〜C5、D2〜D4・D6〜D8 は判断・確認待ち。#705 と #499 は測定で悪化が出たので入れない。#480 は見送り。§4 の見送り推奨 31 件も取り込まない。理由は [UPSTREAM_REVIEW_2026-10-16.md](UPSTREAM_REVIEW_2026-10-16.md) の §0・§2〜§4。
- **P46 で触れなかった既知の穴** (査読で見つかったが P46 の前からのもの。必要になったら別の項目で): (1) `config.json.broken`・`model.json.broken` は元のファイルの権限をそのまま引き継ぎ (`SECURITY_REVIEW_2026-10.md` の「`.broken` は 0600」は、元が 0600 のときだけ正しい)、`suggest.json` には `.broken` が無い (RC-6)。(2) Windows 版の「学習データをリセット」は `.broken` を消さない (RC-8。Mac 版は消える)。(3) 読み込み・保存の失敗の警告のうち、打った語が入りうる 4 か所 (`conversions.json`・`translations.json`・`languages.json`・`model.json` の読み込み) は外した (e19853c・0ef95e2)。残りは例外のメッセージ (パス = ユーザー名が入りうる) をそのまま出す (RC-5・RE-5)。(4) `.broken` のコピーのうち `languages.json`・`config.json`・`model.json` の分は、`.broken` がリンクだとリンク先を書き換える (RE-4)。(5) 書けないフォルダー (権限・読み取り専用・容量不足) では、保存のたびに警告が増え、ログの輪 (1000 件) から原因の行が押し出される (RI-3)。保存の失敗の既存の警告には、保存先のパス (ユーザー名入り) が出る (RI-4。P46-36 のコミット本文の「パスも打った語も入らない」は、新しく足した 1 行についてだけ正しい)。ユーザーモデルだけは、書けなくても `ClearAll` が成功を返す (RI-5。Mac は作らない)。 (6) main の変更で BI だけ崩れる形: 伸ばす音の う で終わる日本語 + `unemployment` + する (`souunemploymentsuru`) は、BI では main の時点で `そううねmployめんつる` になる (同梱の英単語表は `ment` を単語と答えないため。取り込み前と P46 だけの木は正しく、NS では main も正しい)。P46 とは無関係で、P46-47 では直らない。別セッションに伝える。 (7) P46-47 の関数は、長い語 L (`unemployment` など) を判定器の答えだけで受け入れ、main の守り (伸ばす音の う・同梱の一覧にない母音 1 つの単位から始まる語・大文字の略語の境目) を L に掛けない (5 回目の査読 RM-02)。判定器が偶然に語と答える並び L の後ろに、日本語のつもりの `…ment` `…dent` + su が来ると、t と s が英字のまま残りうる (理論上)。測った範囲では、直した後に悪くなった入力は 0 件。L に守りを掛けると、直したい形 B (その守りが長い語を飛ばすこと自体) がまた直らなくなるので、掛けなかった。 (8) `config.json` は、読めないときの `.broken` へのコピーに失敗しても何も出さない (本家 #551 の `Settings.cs` の分。P46-17 が移植したのは `conversions.json`・`translations.json` の分だけ。`model.json` も同じだが Mac では作らない。8 回目の査読 RQ3-03。K89 (4))。移植するかは別の項目にする (リョウさんの判断待ち)。 (9) 本家 main に、Mac の PR の調査 (10-11。依頼で渡された一覧の 6 件と、`gh pr list` で取った 100 件のうち「#268 以降で Mac に関係しそうな約 45 件」を見た) のあとから、ウォッチの起点 (`a4b6f1a`、10-13 13:01。ウォッチは a4b6f1a より後ろだけを見る) までに入った PR は、まとめて調べたものが無い (8 回目の査読 RQ3-02 は #560 だけを挙げ、9 回目の査読 RR2-01 で、この時期の全体に広げた)。調査の前に入った PR のうち、調査が選ばなかったものも同じ (10 回目の査読 RS2-02 で足した。下の「窓の前」)。P45 は eval の例 (本家で当たり Mac で外れる 40 件) を直すコミットを a4b6f1a から手で移植したもので、P46 の調査 (UPSTREAM_REVIEW) の範囲は、ウォッチに出たマージ済みの PR 14 件 (ウォッチの初回の #603・#609 と、`db433da` から後ろの 12 件) と、調査の時点で開いていた Issue・PR。数え方: 本家 main の first-parent で、a4b6f1a とその祖先のうち日付 (committer、JST) が 10-11 以降の merge を、PR の番号を問わず数えた (10-11 の調査の前に入ったものも入れた。調査が見た PR は、番号の記録として出てくる) = **52 回の merge** (どれも「Merge pull request」。番号 390 以下の 7 と、a4b6f1a 自身の #476 を含む。first-parent の直接のコミットは 0)。そのうち Core (テスト以外) のファイルに触るのは 23 で、13 (#415・#418・#425・#428・#454・#461・#475・#476・#481・#497・#504・#532・#560) は、Mac の記録 (main の docs と mac の文書、Mac のコミット本文 (main と P46)、P46 の調査の本文 (UPSTREAM_REVIEW の §1 以降)) に、PR の番号も、中のコミットの hash も、枝の名前にある Issue の番号も出てこない (#476 は、merge 自身の hash `a4b6f1a` が、本家と比べる時点として出てくるだけ: 評価基盤 0-1 で比べた本家の版、正解表 `upstream-quality.tsv` の写しの時点 (本家の修正や正解のケースが、その前か後かの区切りにも使う)、本家の修正を探した範囲 (`a4b6f1a..45e14f8`) の起点、P45 で同じ出力になるかを比べた本家の版、ウォッチ 0-2 の起点。11 回目の査読 RT2-05 (b) で 3 つを書き、12 回目の RU4-03・RU5-01 で残りを足した)。残る 10 も、跡があるだけで、PR 全体を見たとは限らない: #355・#370 は 10-11 の調査で番号を見て対象外にした。#225・#484・#512 は中のコミットの一部を P45 で見た (#484 は、P46 の調査が §2 の B2 で見た `803719c`・`ed1ba78` も含む。`803719c` は P46-14 で移植した)。#588 は中の `8af9d19` を P46 の調査が D2 として見た。#402 は、中の PR の多く (47 のうち 38) の番号が 10-11 の調査に出てくる (下の統合 PR)。#426 は、直す Issue #54 を 10-11 の調査が「P26 で解決済み」とした (PR の中身は見ていない)。#477 は、`STRATEGY_2026-10.md` が、Mac にまだ無い機能 (英数状態でローマ字を検知して日本語に戻す。その行は「P11 待ち」と書くが、同じ文書の 1-5 (この機能) は 10-15 に作らないと決め、代わりは K79。12 回目の査読 RU5-03) を本家が IME 方式 (TSF) に入れた PR として番号を挙げている (PR の中身 (Core の `MeltypeSession`・`CompositionController`・`Settings` を変える) は見ていない。10 回目までは「番号が別の話の中に出てくるだけ」と書いていたが誤りで、11 回目の査読 RT2-04・RT3-01 で直した)。#540 は、P46 の調査の §6 が中の `9ab8f10` を「誰も独立に見ていない」ものとして挙げただけ。(10-12 の 0 時から数えると、#560 と a4b6f1a を除いて 37 の merge・Core に触るのは 15 で、9 回目の査読員の数と同じ。) 中に PR の merge を含む統合 PR は 2 つ: #560 (10-13、`9038438`。38 回の merge で #547〜#584 の 37 の PR (#575 が 2 回) をまとめたもの。中のコミットはどれも 10-13 の 0 時台〜1 時台。#551 は 2 つ目のコミットが 10-14 に別に merge されたのでウォッチに出て、P46-17 で扱った。残る 36 の PR のうち Core (テスト以外) に触るのは 15 (#548・#549・#550・#553・#554・#556・#557・#558・#573・#574・#575・#576・#577・#583・#584)。25 文字以上の追加行 (注を除く) がそのまま Mac の同じファイルにあるかを見ると、8 (#548・#549・#550・#553・#556・#576・#577・#583) は 1 行も無く、#554 は 3 行中 2、#557 は 25 行中 1、#558 は 23 行中 5、#573 は 11 行中 1、#574 は 3 行中 2、#575 は 8 行中 2、#584 は照らせる行が無い。8 回目の査読 RQ3-02) と、#402 (10-11、v1.2.0 の統合。中の merge 65 のうち PR の merge は 47 で、47 の PR のうち 38 は 番号が 10-11 の調査に出てきて、9 (#306・#392・#394・#395・#396・#398・#399・#400・#401) は出てこない。残る 18 の merge は、件名が PR の merge の形でないもの (main や、レビューの直しの枝の取り込み) で、その件名にある #319・#299 は、別の merge で main に入った PR。9 回目に書いた 49・40 は、この 2 つまで中の PR に数えていた。10 回目の査読 RS2-01)。ほかに、中で枝をまとめた PR が 2 つある (#484 は 10 回の merge・37 コミット、#588 は 5 回の merge・10 コミット。中の merge の件名に PR の番号は無い)。P46 の調査の §6 が「PR を通らない本家の直接コミット」と書いた 8 つも、どれも PR の merge で入っていて、そのうち 4 (#445・#484・#540・#588) は、この時期の PR で、残る 4 (#167・#184・#212・#235) は 10-11 の調査の前に入った PR (4 つとも、調査が選んだ範囲の外。UPSTREAM_REVIEW の冒頭の訂正の表)。窓の前: Mac の最初の Core (`c3c247f`、10-07 11:05) と中身が同じファイルが最も多い本家 main の版は、10-06 12:58〜10-07 06:25 の 6 つ (どれも 40 ファイルのうち 29) で、その最後の `b546845` (10-07 06:25) のあとから 10-11 の 0 時 (JST) より前に本家 main に入ったものは、first-parent の merge 62 (どれも番号 390 以下) と直接のコミット 6。この 6 つの版のあいだに入った merge 5 (#33・#37・#49・#50・#91) のうち、Core (テスト以外) に触るのは #37 (`97a9640`、10-06 15:43、再変換) だけで、これは下の数に入っていない: Mac の最初の Core の `CompositionController.cs` は #37 の前とも後とも違い、#37 が足した再変換の形 (`GetReconversionSelection`・`TryReplaceSelection`) は無い (Mac の最初の Core の再変換は、`MeltypeSession.Reconvert` から `CompositionController` の `ReconvertKana` (internal) に入る形で、選択範囲を取って置き換える #37 の形とは別)。#37 が足した行で、前後の空白を除いて 25 字以上の注でない行 (上の #560 と同じ数え方) は 21 で、そのうち、同じ字の行が Mac の最初の Core にもある 3 行 (#37 の足した行の側で数えた数。中身は 2 種類: `_spaceStartedConversion = false;` と、#37 が 2 か所に足した `StartConversion(preferJapanese: true);`。Mac の最初の Core のファイルの中では、どちらも 1 回) は、どれも #37 の前のファイルにもある行 (今の main・P46 の枝・統合した木では、同じ数え方で 4 行。ファイルの中の回数は、`_spaceStartedConversion = false;` が 2 回で、`StartConversion(preferJapanese: true);` と下の 1 行が 1 回ずつ。足された 1 行の `_text.Mode = DisplayMode.Hiragana;` は、ほかの 3 行と違って #37 の前のファイルには無い、#37 が足した行と同じ形の行で、Mac には P36 (`0ac6381`。ローマ字のまま確定した文を、選んで Shift+Space で再変換する。本家 #332 の改良導入) が入れた。#37 の、選んだ文を取り出して置き換える `GetReconversionSelection`・`TryReplaceSelection` は、今の Mac にも無い)。Mac の記録に #37 の番号も hash も枝の名前も出てこない (12 回目の査読 RU2-01。行の数え方と再変換の入り方の言い方は、13 回目の査読 RW4-01・02 で直し、今の Mac の行の数は 14 回目の査読 RX5-05 で書き足し、4 つ目の行が #37 の前のファイルに無いことは 15 回目の査読 RY2-02 で、数えた側とファイルの中の回数は 16 回目の査読 RZ2-10 で書いた)。merge のうち Core (テスト以外) に触るのは 36 で、7 (#10・#32・#86・#87・#267・#269・#299) は 10-11 の調査に番号があり、10 (#105・#167・#171・#184・#186・#187・#189・#212・#217・#235) は中のコミットの hash や枝の Issue の番号が Mac の記録に出てくる (跡があるだけで、PR 全体を見たとは限らない。#167 は P46 の調査の §6 の一言と §3 の D2 の注 (同じ `case F9/F10` の行を触る `8af9d19` と順番を決める、と書いた。ただし本家では `8af9d19` が #167 の `96d8228` の上に書かれていて (`96d8228` は `8af9d19` の祖先)、Mac の Core は `96d8228` の前の形のままなので、D2 を入れるなら `96d8228` を先に入れるか、手で合わせる。12 回目の査読 RU5-04) だけ、#184 は §6 の一言だけ。#212 は v1.1.0 のまとめ (中のコミット 51、うち merge 28。PR #141 と、Issue の枝 17 本 (#24・#38・#52 など) を取り込む) で、P45 で中の `b2e33ca` (#153) だけを見た)。残る 19 (#90・#113・#136・#139・#143・#149・#166・#169・#172・#176・#177・#179・#182・#190・#200・#202・#206・#210・#211) は、番号も hash も枝の Issue の番号も出てこない。直接のコミット 6 のうち 4 (`45711a0`・`55c8a29`・`947ff64`・`e95c07e`) も Core に触り、Mac の記録に hash が出てこない (`e95c07e` は、テストの `669543f` と一緒に `45711a0` で戻されて差し引き 0、`55c8a29` は Core の版の番号だけで、中身を変えるのは `947ff64` (1 字だけの l・q・v・x を英字として確定する規則。Mac の Core には無い。ほかに、そのファイルの頭の SPDX と著作権の注 2 行と、続く空行を消す) の 1 つ。11 回目の査読 RT3-03。注の行は 12 回目の RU2-07 で足した)。窓の前で Core に触る merge 36 と直接のコミット 4 が変えた Core のファイルは、のべ 103 (異なるファイル 28)。変えた後の中身が、Mac の最初の Core (`c3c247f`) の同じパスのファイルと同じものは 0 (ファイルごとの中身 (blob) の照合): 11 は Mac の最初の Core に無いファイル (8 種類)、13 は Mac の最初の Core が変える前の中身のまま (#10 の 3・#32 の 2・#212 の 3 と、#87・#90・#179・#202・`947ff64` の 1 ずつ。#90 は Core では `RomajiDetector.cs` だけを変える)、残る 79 は両方にあって中身が違う。40 の変更のうち、Mac の最初の Core (10-07 11:05) より前に本家 main に入ったのは #90 (06:27) だけで、中の Core のコミットがそれより前に作られたものも 5 つ (#90 と、10-11 の調査に番号のある #10・#32・#86・#87) だけ。なので、記録の無い merge 19 と直接のコミット 4 のうち、手を入れた形で Mac の最初の版に入りえたのは #90 だけで、そのファイルは #90 の前の中身。それでも数を上限の目安とするのは、Mac がのちに記録なしで同じ直しを入れた場合を除いていないため (変えた後の中身が Mac の今の main と P46 の先端の同じパスと同じものも 0。数えた道具は `gap4.py`、照合と時刻は `fill11.py`・`fill12.py` の assert。照合は 11 回目の査読 RT2-01 で、10 回目に書いた「数え始めの 2 分後の #90 などは Mac の最初の版に入っているかもしれない」が中身と合わないと分かったので足し、内訳と時刻は 12 回目の RU2-04・RU4-02 で足した)。ただし、上の #37 (数え始めより前の、同点の版のあいだ) は、この数にも上限にも入っていない。10-11 の調査の前提 (`UPSTREAM_PR_SURVEY.md` の §2「本家の #267 までは取り込み済み」) も、この数え方と合わない: Mac の最初の Core に最も近い本家の版は、上の同点の 6 つ (10-06 12:58〜10-07 06:25。最後が `b546845`) で、#267 は P29 で個別に取り込んだもの (窓の前の記録の無い 19 と #37 は、どれも #211 以下)。調査が「#268 以降」を選んだのは、この前提によるとみられる (11 回目の査読 RT2-02。調査の文書は、調査時点の記録として変えていない)。番号・hash・行の照合を見ただけで、Mac に要るかは確かめていない。精査は別の項目にする (リョウさんの判断待ち。対象を窓だけにするか、Mac の最初の Core に最も近い本家の版のあとからにするかも。同点の 6 つの版の最初の `8623bbf` のあとからなら #37 も入り、最後の `b546845` のあとからなら入らない)。
- **未確認**: (1) 実機 (IME に入れて打った結果) は全項目。(2) 変換エンジン azooKey の実際の出力: やほやほ・VRChat (P46-10)、ライブ変換の `na~~~` (`名～～～` にならないか) と `12gatsu` (`12月` になるか。P46-19・P46-24)、候補の並び (P46-3・P46-8・P46-11)。(3) NSSpellChecker の 73 文字の境界は macOS 26.6.2 だけ (P46-18)。(4) Windows / Linux でのビルドと動作 (Core は共有)。(5) P46-17・P46-26 の `.broken` を実機で作って確かめること (任意)。(6) NativeAOT の dylib の起動時間と、IME プロセス全体のメモリ (P46-20・P46-27 の測定は .NET JIT 上の Core.dll)。(7) 取り込み 22 コミットのうち個別にビルドとテストを確かめたのは 12 本 (RC。残り 10 本は前後と同じ形で、確認していない)。(8) 全消去の確認ダイアログの文面 (P46-40・P46-44・P46-46) と、書けないフォルダーでの失敗表示 (P46-36。「消去できなかったものがあります」) の実機での見え方。(9) 統合した木の Mac アプリでの打鍵 (日時の候補 P47・`そうについて` と P46 を合わせた動き) は、実機では見ていない。(10) 先頭が `.` の入力の確定 (P46-42) は、`--repro`・NS の表・コピーの木のテストでしか確かめていない。(11) 統合でだけ起きる崩れの直し (P46-47) は、`--repro` の BI と NS の表 (実機と同じ NSSpellChecker を差し込んだ再現) でしか確かめていない。IME の中の NSSpellChecker での動きは実機未確認。探索員と査読員が流した範囲の外は未確認 (探索員の打ち切り: LV は BI の全組み合わせ 287,625 のうち 75,795 だけ、TSU は NS の同梱の t で終わる 2,596 語の全数と BI の語幹 8 つが時間切れ、LETTERS は乱数の入力で全数ではない、P46WORDS は N3a の 6,000 入力が時間切れ。7 回目の査読 RP2-04): 母音始まり・t 終わりで一覧にない語 (web2 の 2,256 語) と、子音始まり (無作為 2,000 語) 以外の総当たり、大文字始まり、73 文字以上、ライブ変換の長さ、日本語の読みに su・suki・shita・site などの語尾を続けた形、NS の答えを同じ入力で 2 回流して比べること (ただし崩れの数 119・36 と、無害な差 2 は元の報告と再現で一致した)。

## コード総点検 (2026-10-27)

- **目的**: Mac 版に関わるコード全体 (Core・FFI・Swift・配布スクリプト・ツール・テスト基盤) の不具合・改善できる点・機能改善につながるリファクタリング候補・テストの穴を、根拠つきで一覧にした。**修正はしていない** (直すときはここに項目を立てて 1 つずつ)。記録: [CODE_REVIEW_2026-10.md](CODE_REVIEW_2026-10.md) (結論 → 中 21 件の表 → 低の概観 → リファクタ候補 12 → 良い点 → 決めていただきたいこと 7 → 未確認。付録に低 135 件の全文と査読の記録)。
- **方法**: 11 レーンが並列に読み (`--repro` と使い捨てテストで再現)、独立査読 5 名が「中」の全件を再実行・再読 (P0 = 0、P1 = 22、P2 = 35)、統合版をさらに別の査読者が確かめた。すべて `delegate_task` の子 (Fable 5。`claude` CLI 未ログインのため Opus への振り分けなし)。基準は `main` 8a54521 (= v1.2.4) の読み取り専用 worktree、Core テスト 1145/1145。
- **結果**: 高 0・中 21 (不具合 7、堅牢性 4、改善 2、テストの穴 8)・低 135。いま直す価値が高い 6 件: 予測確定の学習が変換の最初の候補を乗っ取る (Q17) / 「BackSpace で文節を消す」設定で確定時に後ろの文節が消える (Q18) / config.json の 1 項目の誤りで全設定が初期化 (Q20) / 文脈つき学習語を辞書アプリから消せない (Q21) / 辞書アプリの設定タブでの ⌘Z が隠れたタブの操作を取り消す (Q22) / ABI 不一致で IME が無言で死ぬ (Q23)。繰り返し出ている型は A 状態のリセット漏れ・B 二重管理の食い違い・C 黙って失敗、の 3 つ (リファクタ候補 R1〜R12 の根拠)。K88 (3) の `_zoverdesu` → `_zovedesu` は原因を読取で確定した (KNOWN_LIMITS に追記)。
- **管理**: 中の不具合・堅牢性のうち新規 7 件を UPSTREAM_PR_TRACKER の Q17〜Q23 に登録。文書の §8 に判断待ち 7 件 (F6 → BackSpace の戻し / 電卓候補の日付・電話番号 / uninstall の退避に `dictionaries/` を含めるか / 予測学習の直し方 / テスト用判定器の構成 / IME の `--self-test` / 修正の進め方)。
- **未確認**: 実機は全項目 (Swift 側は読取か推測)。粗読みの範囲・未計測の数は文書の §9。**push 前**。
- **決定 (2026-10-27、リョウさん)**: §8 の 7 件はすべて (a)。修正は Q17〜Q25 を独立した worktree で並列に実装し、査読を付けてから統合する。進捗は各項目の節に追記する。

## コード総点検の修正バッチ (Q17〜Q27、2026-10-27)

- **進め方**: 決定 (§8 はすべて (a)) どおり、Q17〜Q25 を独立した worktree で並列に実装 (1 項目 1 コミット、テスト先行で RED を確認)。あわせてリョウさんの報告「文章の途中の変換で候補の語が著しく少なくなる」を調査 (I1) し、原因 2 つを Q26・Q27 として直した。
- **検証**: 全ブランチで、全テスト合格・`--eval` が基準と同一・新しいテストが修正前の Core で赤になることを機械的に再実行して確認。統合ツリーで全テスト 1177 件 (基準 1145 + 32)・Swift ビルド・codesign・`check-mac-aot-settings.py`・辞書アプリの `--self-test` を確認。独立査読 (Core 9 件を 2 名: P0 0・P1 4・P2 10、Swift 2 件は統括が読取) の P1 4 件を実装 2 (Q17 の学習の上書き・Q20 の `LoadForUpdate`) とテスト 2 (Q18 の `my`・Q19 の続けて入力) で閉じ、P2 は各節の損に書いた。
- **未確認**: 実機は全項目 (各節の「残る限界・未確認」)。**push 前**。

## Q17 の修正 (予測候補の確定を変換の最初の候補に使わない、2026-10-27)

### 目的
予測候補 (Tab → Enter) で「おせわ → お世話になります」を確定すると、`History.Remember` が変換の学習 (読み → 語) に入り、次に `osewa` を Space で変換したとき 1 番目の候補が `お世話になります` に乗っ取られていた。文節にも波及し、「たんいを → 単位を取得する」を予測で確定したあと `tanniwotoru` Space が `単位を取得する取る` になった (ライブ変換も)。予測の確定は「この語をまた予測に出してほしい」であって「この読みの変換はこの語」ではないので、予測の並びにだけ効かせる。

### 変更
- `src/Meltype.Core/Composition/ConversionPreferences.cs` `ConversionHistory.Entry`: `Prediction` (予測から覚えた印) を追加。false は書かない (`JsonIgnore WhenWritingDefault`) ので `conversions.json` の形式は今までどおり。既存のファイルは欄が無く、false (変換の学習) として読む。保存・読み込みはソース生成の `LearningJsonContext` のまま
- 同 `ConversionHistory.Remember(reading, text, fromPrediction = false)`: 予測からは印つきで覚える。変換で学習済みの同じ語なら印を付けない (学習を消さない)。変換で同じ読みを選び直せば (通常の `Remember`) 印は外れる
- 同 `ConversionHistory.Get`: 印のある項目は返さない。`StartingWith`・`Usage` (予測の並び・使用頻度) は印に関係なく今までどおり
- `src/Meltype.Core/Composition/CompositionController.cs` `CommitPrediction`: `Remember(..., fromPrediction: true)`
- `Get` を通る経路 (`ConvertWithEngineOnce`・`Reconvert`・`LiveConvert`・ユーザー辞書の語の学習・辞書登録の提案 `RecordSuggestions`) はすべて同じ効果

### 得と損
- 得: 予測で長い語を確定しても、同じ読みの変換 (Space・ライブ変換・区切りの変更) の 1 番目が変わらない。予測の並び (使うほど上に出る) は今までどおり
- 損: 予測で確定した語は、変換で選び直すまで変換の 1 番目にならない (以前はなっていた。それが本件の不具合なので意図どおり)。変換で学習済みの読みに別の語を予測で確定しても、変換の学習は上書きしない (査読 F1-1 で「学習が消える」を見つけて直した) ので、その読みの予測の並びにはその語が効かない。辞書登録の提案も、予測で確定しただけの語を「学習済みの語をそのまま確定」とは数えない

### 残る限界・未確認
- 実機 (mac / Windows) は未確認
- 設定の「学習した語」の一覧には予測から覚えた項目も今までどおり並ぶ (印は表示しない)
- `ReadingOf` (確定後の再変換で読みに戻す) は印に関係なく返す (今までどおり)

### テスト (`src/Meltype.Core.Tests/CompositionTests.cs`、5 件)
- `Prediction_CommitRemembersAndSpaceConverts` (更新): 予測確定後の `osewa` Space の 1 番目が `お世話になります` でない
- `Prediction_CommitDoesNotHijackConversion`: 予測の並び (履歴から次の予測に出る・Usage に入る) は今までどおり、変換で選んだ語は今までどおり `Get` が返す、変換で学習済みの語を予測で確定しても学習は消えない
- `Prediction_CommitDoesNotHijackClauseConversion`: `たんいを → 単位を取得する` のあと `tanniwotoru` Space が `単位を|取る`、Shift+←→ で `たんいを|とる`、ライブ変換で `単位を取る`
- `ConversionHistory_PredictionFlagRoundTrips`: 保存 (印は true のときだけ書く)・読み込み・印の無い旧形式
- `ConversionHistory_PredictionDoesNotOverwriteConversionLearning` (査読 F1-1): 変換で学習済みの読みに別の語を予測で確定しても `Get` と予測の並びに学習が残る
- `--eval` は変更前と同じ

### 近傍の --repro (変更前後で差なし)
| 入力 | 最後 | 結果 |
|---|---|---|
| osewa | space | composing おせわ |
| osewa | enter | committed おせわ |
| osewaninarimasu | space | composing おせわになります |
| tanniwotoru | space | composing 単位を取る |
| tanniwotoru | enter | committed たんいをとる |
| tanniwo | space | composing たんいを |
| kigoutou | space | composing 記号等 |
| hashiwo | space | composing 橋を |
| kyouha | enter | committed きょうは |
| nihongowohanasu | space | composing にほんごをはなす |
| kawa | space | composing 川 |
| konnnichiha | enter | committed こんにちは |
| osewa | none | composing おせわ |

の節

## Q18 の修正 (文節を消して打ち直した後の Space で後ろの文節が消える、2026-10-27)

**目的**: 設定「変換中の BackSpace で選んだ文節を消す」(DeleteClauseOnBackspace、既定 OFF) で文節を消して打ち直したあと、打ち直しが「日本語 + 英語」(wogithub) やパス (./build.sh) で終わって Space を押すと、表示は「えをgithub知らん」なのに確定が「えをgithub 」になり、後ろの文節 (知らん) が消えていた。Enter と同じく、後ろの文節も確定に残す。

**原因**: Space の「英語で終わる語は変換せず確定して空白」の 2 経路 (`EndsWithEnglish` / `IsEnglishAtWordEnd`) が `Commit()` を通らず `CommitText` を直接呼んでいて、`TakeFixedClauses()` は後ろの文節 (`_retypeSuffix`) を足さず、`CommitText` が `_retypeSuffix = null` で黙って捨てていた。後ろの文節を足すのは `Commit()` だけだった。

**変更** (`src/Meltype.Core/Composition/CompositionController.cs`):
- `HandleKey` の Space、英語で終わる 2 経路の `CommitText(...)`: `TakeFixedClauses() + <打ち直し> + SuffixText + " "` にして後ろの文節を足す (空白は `Commit(suffix: " ")` と同じく後ろの文節のあと)
- 同じ 2 経路で、後ろの文節に日本語があれば `english = false` / `hasJapanese = true` に補正 (`Commit()` の補正と同じ。次に打つ「2 + Space」を「英文の後ろの数字」として確定しない)
- `SuffixHasJapanese` プロパティを追加
- `CommitText`: `_retypeSuffix` が残っていて確定の文字に含まれていなければ `Log.Warn` (呼ぶ側の足し忘れを黙って捨てないための守り)

**得と損**:
- 得: 表示と確定が一致する。Enter と Space で確定の内容が変わらない (空白の有無だけ)
- 損: なし (設定 OFF では `SuffixText` が常に空なので動作は変わらない。`--repro` 18 入力で前後一致)

**残る限界・未確認**:
- 実機 (Mac) は未確認。Core のテストハーネスのみ
- 1 音確定 (Shift+↓) で入力が空になる経路に後ろの文節が残っていると、後ろの文節は落ちる (元からの別の問題。今回は `Log.Warn` が出るだけで、直していない。再現も未確認)
- 日本語 (え) の直後に `./build.sh` を打ち直すと `.` は `。` になる (保護区間は英語の直後・語頭でしか始まらない、元からの仕様)

**テスト** (`DeleteClauseOnBackspaceTests.cs`、+3 本):
- `On_Enter_AfterRetypeEndingWithEnglish_KeepsTheTail`: 表駆動 6 入力 (github / wogithub / my / ./build.sh / src/main.rs / -。`my` は `IsEnglishAtWordEnd` の経路。査読 F2-1) × Enter。確定 = 表示
- `On_Space_AfterRetypeEndingWithEnglish_KeepsTheTail`: 同 5 入力 × Space。英語で終わる 4 つは確定 = 表示 + 空白、`-` (ー) は今までどおり変換が始まる
- `On_Space_AfterRetypeEndingWithEnglish_DoesNotCountAsAnEnglishCommit`: `wogithub` → Space のあとの `2` + Space が確定せず変換になる (直前の確定を「英語で終わった」扱いにしない旗の補正)
- 注: この場面では前の文節が必ず固定されているので、確定後の F10 / F6 は旗に関係なく素通し (`_lastWord` を記録しない)。旗の効果は次の入力の「英文の後ろか」の判定に出るので、そこをテストにした

**近傍の --repro** (設定 OFF。変更前 = 変更後):

| 入力 | 最後 | 表示 | 確定 / 変換中 |
|---|---|---|---|
| wogithub | space | をgithub | 確定 `をgithub ` |
| egithub | space | えgithub | 確定 `えgithub ` |
| kyouhagithubwomiru | space | きょうはgithubをみる | 変換 `今日はgithubをみる` |
| kyouhagithubwomiru | enter | きょうはgithubをみる | 確定 `きょうはgithubをみる` |
| nihongowohanasu | space | にほんごをはなす | 変換 |
| kyouhasushidesu | space | きょうはすしです | 変換 |
| kyouhasushidesu | enter | きょうはすしです | 確定 |
| ./build.sh | space | ./build.sh | 確定 `./build.sh ` |
| src/main.rs | space | src/main.rs | 確定 `src/main.rs ` |
| watashihagoogle | space | わたしはgoogle | 確定 `わたしはgoogle ` |
| thisisapen | space | てぃしさぺn | 変換 |
| e,shiran | enter | え、しらn | 確定 `え、しらん` |
| tanniwotoru | space | たんいをとる | 変換 `単位を取る` |
| ihave2cats | space | いhave2cats | 確定 `いhave2cats ` |
| sushi | space | すし | 変換 |
| - | enter | ー | 確定 `ー` |
| ~/build.sh | space | ~/build.sh | 確定 `~/build.sh ` |
| pullrequestwo | space | pullrequestを | 変換 |

の節

## Q19 の修正 (F6 の変換ボックスで読みを消し切ったら元の語を書き戻す、2026-10-27)

**目的**: 確定した直後の語を F6 でかなの変換ボックスに移したあと、BackSpace で変換を取り消し、さらに BackSpace で読みを消し切ると、ボックスは閉じるのに入力欄から消した語 (`hello api` の `api`) が戻らず、Esc・F10 でも取り戻せなかった (コード総点検 §2 の 3、L1b-1)。書き戻しが変換前の Esc にしか無く、BackSpace の経路に無かったため。直すつもりで押した F6 で語を失わないようにする。

**変更** (`src/Meltype.Core/Composition/CompositionController.cs`):
- 変換前の BackSpace (`case VirtualKeys.Back`、`:1226-1228`): 読みが空になったとき、確定後の F6 で開いた変換ボックス (`_toggleBox`) があれば、Esc と同じく元の語を書き戻して記録も戻す (`RestoreReconvertedOriginal`)。文節を固定した続き (`_headCount > 0`) は今までどおり前の文節の選択に戻る
- 変換前の Esc (`:1242`): 既存の書き戻し (`_reconvertOriginal` → `_host.CommitText` → `RestoreToggleBox`) を新設の `RestoreReconvertedOriginal()` (`:3458-3469`) に共通化 (挙動は同じ)
- 選択の再変換 (選択 + Shift+Space。`_toggleBox` が無い) は対象外: 読みを消し切ったら今までどおり閉じるだけ (選んだ文字は消える)。ローマ字の再変換は既に `CancelRomajiReconversion` で書き戻すので変更なし

**得と損**:
- 得: F6 → BackSpace で読みを消し切っても語が消えない。Esc と同じ戻り方で、戻した語はもう一度 F6 で直せる。学習・文脈の記録はしない (Esc と同じ)
- 損: F6 の箱で「読みを全部消す = 語を消す」つもりだった人は、BackSpace が 1 手増える (戻った語を消す)。選択の再変換は変えていないので、そちらは今までどおり消える

**残る限界・未確認**:
- 実機 (Mac) 未確認。Swift は Esc の書き戻しと同じ edit (`DeleteBackward` のあと `CommitText`) を受けるので、Esc が動く入力欄なら同じに動く見込み
- Windows は Core 共通の経路 (ビルド未確認)
- 選択の再変換 (かなの再変換) で読みを消し切ると選んだ文字が消えるのは、決定どおり今までのまま (ガードのテストで固定)

**テスト** (`src/Meltype.Core.Tests/LastWordToggleTests.cs`、+3):
- `F6_BackspacingTheReadingAway_PutsTheOriginalBack_WithoutLearning`: `hello api`⏎ → F6 → BackSpace ×3 で入力欄が `hello api` に戻り、箱が閉じ、学習せず、もう一度 F6 が効き、続けて `neko`⏎ で `hello apiねこ`。Space で確定した `hello api␣` でも空白ごと `hello api␣` に戻る
- `F6_ThenContinueTyping_BackspacingTheNewReadingAway_DoesNotPutTheOriginalBack` (査読 F3-1): F6 の箱で変換 → 続けて入力 (ContinueAfterConversion ON) → 新しい読みを消し切っても、前の文節が固定されている間は書き戻さない (二重にしない)
- `Reconvert_BackspacingTheReadingAway_StaysDeleted` (ガード): 選択の再変換 (`Reconvert("にほんご")`) で読みを消し切ったときは書き戻さない (入力欄への書き込み 0 件)
- `--eval` は変更前と同じ (外れた例 1 件: `my name is taro → my name is たろ` は従来どおり)

**近傍の --repro** (確定の経路が変わっていないことの確認。変更は F6 の箱の BackSpace だけで効く):
| 入力 | showing | committed | composing |
|---|---|---|---|
| hello enter | hello | hello | null |
| hello space | hello | `hello ` | null |
| api enter | あぴ | あぴ | null |
| api space | あぴ | (空) | あぴ |
| nihongo enter | にほんご | にほんご | null |
| sushi enter | すし | すし | null |
| sushi space | すし | (空) | すし |
| neko enter | ねこ | ねこ | null |
| kyouha enter | きょうは | きょうは | null |
| taro enter | たろ | たろ | null |
| meeting space | meeting | `meeting ` | null |
| nihongowohanasu enter | にほんごをはなす | にほんごをはなす | null |
| sorekara space | それから | (空) | それから |

の節

## Q20 の修正 (config.json の 1 項目の型違いで全設定が既定に戻る、2026-10-27)

**目的**: 手で編集した config.json の 1 項目だけ型が違う (`"PredictionMinLength": "2"`、`"Punctuation": "Z"`) と、`JsonSerializer.Deserialize` 一発の `Settings.Load` が `JsonException` で全設定を既定値に戻し、元のファイルを `.broken` に退避していた (IME は黙って既定で動く)。型の違う項目だけを既定に戻し、他の項目は保つ。

**変更**:
- `src/Meltype.Core/Config/Settings.cs` `Load` (759 行目付近): 一発の `Deserialize` を `Parse(json)` に置換
- 同 `Parse` (新設、`ToJson` の下): 1 段目は従来どおり全体を一度に読む。読めなければ 2 段目として `ImportProfile` と同じ作法 (`new Settings()` を `SerializeToNode` した既定の `JsonObject` に、知っている項目だけを 1 つずつ重ねて `Deserialize`) で、失敗した項目だけ既定に戻し `Log.Warn("config.json の <項目名> を読めないので、この項目だけ既定値にします: …")`。JSON 全体が読めない (構文の誤り・0 バイト・先頭がオブジェクトでない) ときは従来どおり例外 → `.broken` に退避して既定値
- ソース生成の `SettingsJsonContext` と `JsonNode` だけを使う (reflection を足さない。NativeAOT の Mac 版 libMeltypeNative 用)
- 同 `LoadForUpdate` (書き換えのための読み込み。辞書アプリの設定タブ・`ConfigFlag`・分野の保存が使う) も `Parse(json)` を通す (査読 F4-1: `Load` だけ救済すると IME は正常に動くのに設定の保存が「読めない」で黙って見送られていた)。JSON 全体が読めないときは今までどおり null
- `Migrate` / `Normalize` / ロック内の保存し直しの流れは変えていない

**得と損**:
- 得: 1 項目の打ち間違いで全設定 (ライブ変換・アプリ別設定・プロファイルなど) を失わない。どの項目が読めなかったかがログに残る。元のファイルは書き換えない
- 損: 2 段目は項目数 × 全体 `Deserialize` (起動時に、壊れたファイルのときだけ)。型違いの項目は黙って既定で動く (ログの警告のみ。画面には出ない)。次に設定を保存すると、型の違う項目は既定値で書き戻される (IME が使っている値と同じ。以前は `.broken` 退避で全部失っていた)

**残る限界・未確認**:
- `"Punctuation": 7` のような「未定義の整数」は列挙が整数を受けるので型違いにならず、そのまま入る (別件)
- `Log.Warn` に出る位置 (LineNumber) は内部の JsonObject のもので、利用者のファイルの行とは合わない (項目名で見る。値は書かない)。`.broken` の場所を画面に出す案 (L5-1 (b)) は未実施
- Mac 実機・`tools/check-mac-aot-settings.py` は未確認。Windows はビルド不可 (`Settings.Load(path)` の署名は不変)

**テスト** (`src/Meltype.Core.Tests/SettingsLoadTests.cs`、6 件):
- `BrokenSyntax_DefaultsAndBackup`: 構文が壊れた JSON → 既定値 + `.broken` (今の挙動の固定)
- `WrongTypeItem_OnlyThatItemDefaults`: `"PredictionMinLength": "2"`、`"Punctuation": "Z"` → その 2 項目だけ既定、`LiveConversion: false` などは保たれ、`.broken` なし (RED→GREEN)
- `UnknownKey_IgnoredOthersKept`: 未知のキーは無視、他は保たれる
- `MissingVersion_Migrates`: `SettingsVersion` 無し → v1 として移行し保存し直す (今の挙動の固定)
- `EmptyFile_DefaultsAndBackup`: 0 バイト → 既定値 + `.broken` (今の挙動の固定)
- `WrongTypeItem_LoadForUpdateStillReads` (査読 F4-1): 型違い 1 項目では `LoadForUpdate` も通り、JSON 全体が壊れていれば null

**近傍の --repro** (enter。設定の読み込みは打鍵の経路に無く、変更前後で同じ。`--eval` も変更前と同じ):

| typed | committed |
|---|---|
| konnichiwa | こんいちわ |
| hello | hello |
| nihongowohanasu | にほんごをはなす |
| my name is taro | my name is たろ |
| settei | せってい |
| config | config |
| kyouhaiitenki | きょうはいいてんき |
| json | json |
| watashihagakuseidesu | わたしはがくせいです |
| the quick brown fox | the quick brown fox |
| ohayougozaimasu | おはようございます |
| github | github |

## Q21 の修正 (文脈ごとに覚えた語を辞書アプリから消す・直す、2026-10-27)

**目的**: 設定「英語 / 日本語の学習を前の文脈ごとに分ける」(既定 OFF) で覚えた語は、languages.json のキーに印が付く (`api@e` = 英語の後、`api@j` = 日本語の後)。辞書アプリの「学習した語」の一覧は印を外した語だけを出し (`LanguageMemory.Entries`)、消す・直すも語だけを渡すので、`Remove` は何も消さずに理由 null (= 成功と表示)、`Set` は「一覧にありません」、`Check` は重複を見逃していた。画面・FFI の形式を変えずに、Core 側が印つきのキーも見るようにする。

**変更** (`src/Meltype.Core/Composition/LanguageMemory.cs`):
- `KeysOf(word)` (:485): 語のキーになりうる 3 つ (印なし・`@e`・`@j`)
- `FindKey(word)` (:491): 画面が渡した語に当たるキーを探す。印なしがあればそれ。無ければ `@e` / `@j` のうちある方。**両方あれば理由を返す** (「『api』は前の文脈ごとに 2 つ覚えています。消してから覚え直してください。」)
- `Remove` (:546): 語ごとに 3 キーを消す
- `Set` (:505): `old` のキーを `FindKey` で探し、見つかった印を引き継いで書き戻す (綴りを直しても文脈は変えない。`sushi@j` → `sashimi@j`)。両方あれば理由を返して何も変えない
- `CheckCore` (:473): 重複の判定も 3 キーを見る (`except` = 編集中の元の語は 3 キーとも重複にしない)
- Swift・FFI (`Exports.cs`)・`DictionaryManagement`・ファイル形式・`Remember` / `Get` の経路は変えない

**得**: 設定 ON で覚えた語を、辞書アプリから消せる・英字 / かなを直せる・綴りを直せる。足すときの重複も見逃さない。「消したのに残る」が無くなる。
**損**: 画面は印を知らないままなので、同じ語を英語の後と日本語の後の両方で覚えているときは、どちらを直すか選べず断る (消すのは両方いっぺん)。印なしと `@e` が両方あるときは印なしだけを直す (これまでの挙動のまま) ので、`@e` 側は画面から直せない (消すことはできる)。

**残る限界・未確認**:
- 画面に印 (文脈) を出す・印ごとに直す (`Learned.Context` を FFI に乗せる) は別の項目 (L4b-8 / R-1 (L4b))
- `MaxWordLength` が印の 2 文字も数える (`:313`) は今回は触っていない
- 辞書アプリの実機 (Swift 側) は未確認。Core の経路 (`FormatLearned` の行 → `ParseLearnedWords` → `Remove` / `Set`) はテストで確認

**テスト** (`src/Meltype.Core.Tests/LearnedWordsTests.cs`、6 件、合計 1151/1151):
`ContextSensitive_RemoveForgetsSuffixedKeys` (語だけで `api@e` が消える。印なし・`@e`・`@j` が 3 つそろっていても 1 回で消える) / `ContextSensitive_CheckSeesSuffixedKeys` (`sushi@j` があれば重複。`except` は重複にしない) / `ContextSensitive_SetEditsTheOnlySuffixedKey` (`@j` 1 つなら印のまま直る。綴りを直しても印を引き継ぐ。印なしと `@e` の両方があれば印なしだけ) / `ContextSensitive_SetRefusesWhenBothContextsExist` (両方あれば理由を返して版も内容も変えない) / `ContextSensitive_PlainWordsBehaveAsBefore` (印なしの語の消す・直す・重複はこれまでどおり) / `ContextSensitive_DictionaryApp_RemovesAndEditsByListedWord` (IME が文脈つきで覚えた語を、画面の別インスタンスが一覧の行で消す・直す。IME にもすぐ効く)

**近傍の `--repro`** (学習データは使わないので、変更前後で同じ):

| 入力 (enter) | 結果 |
|---|---|
| api | あぴ |
| sushi | すし |
| kyouha | きょうは |
| github | github |
| go | ご |
| my name is taro | my name is たろ |
| apiwotsukau | あぴをつかう |
| sushiwotaberu | すしをたべる |
| kubernetes | kubernetes |
| notoukouha | のとうこうは |
| xnotoukouha | んおとうこうは |
| swift | swift |

## Q22 の修正 (辞書アプリの設定・判定タブでの ⌘Z、2026-10-27)

- **目的**: 設定タブ・判定を調べるタブを選んでいる間に ⌘Z を押すと、隠れたユーザー辞書 / 専門用語集タブの直前の操作 (登録・削除・除外・分野の削除・取り込み…) が黙って取り消されていた (CODE_REVIEW_2026-10 §2 の 6、L8-1)。見える反応は隠れたタブの状態欄だけだった。
- **変更**:
  - `mac/Sources/MeltypeDictionary/MainWindowController.swift` `windowWillReturnUndoManager`: ユーザー辞書・専門用語集のタブのときだけ共有の `sharedUndoManager` を返す。学習した語・設定・判定を調べるのタブは、タブごとの空の UndoManager (`learnedUndoManager` / `settingsUndoManager` / `judgeUndoManager`) を返す (学習した語のタブと同じ作法)。
  - `UserDictionaryViewController` / `TermDictionaryViewController` に `dictionaryUndoManager` を足し、`registerUndo` (と `performMove` の undo グループ、取り込み後の「取り込み」の登録) は `window.undoManager` を経由せずそれに積む。`MainWindowController` が `sharedUndoManager` を渡す。`windowWillReturnUndoManager` は積むときにも呼ばれるので、積む経路がタブの選択と非同期になっても (シートの完了後など) 別の undoManager に入らない。
  - ファイル冒頭と `windowWillReturnUndoManager` のコメントを実装に合わせた (L8-8 の「設定のタブで共有」は誤りだった)。
- **得と損**: 得: 見えないところで辞書が変わらない。専門用語集の「直す」はユーザー辞書も変えるので、2 つのタブの履歴は 1 本のまま。損: 設定タブに移った直後に辞書の操作を取り消すには、辞書のタブへ戻してから ⌘Z を押す必要がある。
- **残る限界・未確認**: Swift に自動テストは無く、実機未確認。設定タブ・判定タブのテキスト欄の文字の取り消しが、そのタブ専用の UndoManager に積まれるか (⌘Z で文字が戻るか) は実機で見る。設定タブの変更 (トグル・数値) は今までどおり undo に積まない (L8-8 の後段は範囲外)。タブをまたぐやり直し (P20) は別件のまま。
- **テスト**: 自動テストなし (self-test は UI を通らない)。ビルド・codesign・`check-mac-aot-settings.py`・`MeltypeDictionary --self-test` が通ることを確認。

## Q23 の修正 (本体の版が合わないときの IME の通知、2026-10-27)

- **目的**: `libMeltypeNative.dylib` に `meltype_abi_version` が無い、または値が期待 (13) と違うとき、IME は NSLog だけ残して初期化を中止し、全キーがアプリへ素通しになるが利用者への通知は無かった (§3 の 2、L6-1)。dlopen の失敗だけは M7 対応で通知が入っていた (ABI 側は意図的に範囲外のままだった)。
- **変更**: `mac/Sources/MeltypeIME/NativeCore.swift` `notifyLoadFailureOnce` を理由の文を受ける形 (`notifyLoadFailureOnce(_ reason: String)`) にし、`initialize()` の 2 つの guard からも呼ぶ。文言は「Meltype の判定エンジン (libMeltypeNative.dylib) の版が合いません (期待 13 / 実際 N)。」「… に版の情報がありません (古い版です)。」で、後ろに従来と同じ「日本語入力は無効のまま、キーはそのままアプリに渡します。Meltype を入れ直してください。」が付く。dlopen に失敗したときは `init` で知らせているので、`meltype_abi_version` が無い guard は `library != nil` のときだけ知らせる (重ねない)。起動ごとに 1 回の制限 (`didNotifyLoadFailure`) と、通知の仕組み (osascript の `display dialog`、120 秒で閉じる) は変えない。
- **得と損**: 得: 版の不一致の原因に 1 回で辿り着ける (ダイアログに期待と実際の版が出る)。損: なし。
- **残る限界・未確認**: 実機未確認 (通常の配布では dylib と IME が同じ pkg に入るので再現しにくい。確かめるなら別の版の dylib を Frameworks に置くか、`meltype_abi_version` の無い dylib を用意する)。M7 の「dylib を壊しての表示」の未検証も同じまま。
- **テスト**: 自動テストなし。ビルド・codesign が通ることを確認。

## Q24 の修正 (計算候補 ON で日付・電話番号を式にしない、2026-10-27)

目的: 設定「計算式を変換すると結果を候補に出す」(既定 OFF) が ON のとき、演算子が `-` だけの列 (日付 `2026-10-27`、電話番号 `03-1234-5678`) も式として受理され、Space で確定ではなく変換が始まっていた (候補に `1989`)。あわせて `0*-1` の結果が `-0` と出ていた。

変更 (2 コミット):
- `src/Meltype.Core/Composition/Calculator.cs` `Evaluate`: `=` で終わらない式は、「`-` 以外の二項演算子 (`+ * /`) を含む」か「`-` がちょうど 1 個」のときだけ受ける。`=` で終わる式 (`10-3-2=`) は今までどおり。`=` の判定は `Calculator` の中 (呼び側 `CompositionController` の `TrimEnd('=')` は候補の表示用だけなので変更なし)。
- 同 `Evaluate`: 丸めた結果が 0 なら `+0` に正規化 (`0*-1`・`-0` → `0`)。

得と損:
- 得: 日付・電話番号は設定 ON でも従来どおり Space で確定する。`10-3`・`10-3=`・`-3`・`2*3-1`・`1+2-3-4` は今までどおり計算する。
- 損: `-` が 2 個以上で `+ * /` も `=` も無い式 (`10-3-2`、`1--1`) の候補が出なくなる。`=` を付ければ (`10-3-2=`) 出る。単項の `-` も数えるので `-10-3`・`(10-3)-2` も同じ (査読 F8-1。二項の `-` だけ数える改善案はあるが、日付と区別する目的には今の形で足りるので入れていない)。

残る限界・未確認:
- 実機未確認 (Core のテストで固定)。
- 全角の `＝` は前から受けていない (`１＋２＝` は候補なし)。今回も変えていない。
- 日付でも `+ * /` を含む列は対象外のまま (例: `2026/10/27` は `/` を含むので従来どおり式として読み、`7.5037037037` が候補に出る。設定 OFF では無関係。テストでは未固定)。

テスト (4 件追加、1149/1149):
- `CalculatorTests.Evaluate_HyphenOnly_RejectsDateAndPhoneLikeSequences` (`2026-10-27`・`03-1234-5678`・`10-3-2`・`２０２６－１０－２７` → null)
- `CalculatorTests.Evaluate_HyphenOnly_AcceptsSingleMinusOrEqualsSuffix` (`10-3`→7、`10-3=`→7、`10-3-2=`→5、`2*3-1`→5、`1+2-3-4`→-4、`-3`→-3)
- `CalculatorTests.Evaluate_NegativeZero_IsShownAsZero` (`0*-1`・`-0` → `0`)
- `CalculatorCandidatesTests.DateAndPhoneLikeHyphens_CommitWithoutConversion` (設定 ON: `2026-10-27`+Space は変換に入らず `2026-10-27 ` と確定、`10-3 `→候補 `7`、`10-3-2= `→候補 `5`)

近傍の `--repro` (設定 OFF = 既定。変更前と同じ):

| 入力 | Space | Enter |
|---|---|---|
| `2026-10-27` | `2026-10-27 ` 確定 | `2026-10-27` |
| `03-1234-5678` | `03-1234-5678 ` 確定 | `03-1234-5678` |
| `10-3` | `10-3 ` 確定 | `10-3` |
| `10-3-2` | `10-3-2 ` 確定 | `10-3-2` |
| `-3` | `-3 ` 確定 | `-3` |
| `1+2*3` | 変換中 `1＋2＊3` | `1＋2＊3` |
| `2*3-1` | 変換中 `2＊3-1` | `2＊3-1` |
| `0*-1` | 変換中 `0＊ー1` | `0＊ー1` |
| `10/4` | `10/4 ` 確定 | `10/4` |
| `10-ji` | 変換中 `10-じ` | `10-じ` |
| `- kyou wa` | `- 今日` 確定 + `わ` | `- 今日わ` |

の節

## Q25 の修正 (uninstall の退避と Backup.cs に dictionaries/ を含める、2026-10-27)

- **目的**: 「設定・学習データも消す」(`--remove-data` / y 回答) で、利用者が手で書く自作のローマ字表 `dictionaries/romaji.tsv` と辞書の上書き `dictionaries/*.txt` が、デスクトップへ写されずに `rm -rf` で消えていた (コード総点検 L9-1、§8 の 3 = (a))。Core の `Backup.cs` (Mac 未配線) も `dictionaries/*.txt` だけを入れ、`.tsv` は `IsRestorableName` が拒むので `Restore` が黙って飛ばしていた。
- **変更**:
  - `mac/uninstall.sh` の退避ループ (`for sub in "terms" "terms/.trash" "dictionaries"`): `dictionaries/` も対象にし、`*.txt` と `*.tsv` を 1 件ずつ `cp -p` (失敗で `set -e` が止める形は不変)。問い・完了文・ヘッダーの説明に「自作のローマ字表・辞書」を追加。
  - `src/Meltype.Core/Config/Backup.cs` `TargetFiles`: `dictionaries/romaji.tsv` (`RomajiTable.FileName`) が存在すれば明示的に列挙。`IsRestorableName`: 正規表現を `\.(txt|tsv)\z` に (`..`・`:`・予約名・ドット始まりの守りは不変)。
- **得と損**: 得: 自作のローマ字表・辞書が消える前にデスクトップ / バックアップに残る。損: `dictionaries/` の下のサブフォルダーは写さない (今は使っていない)。`cp` が失敗すると `set -e` で止まり、空の退避フォルダーが残る (元からの挙動。査読 F9-1/2)。
- **残る限界・未確認**: `uninstall.sh` の通し実行は実機でのみ確認可能 (構文 `bash -n` と、退避ブロックを一時フォルダーに向けた実行で確認済み)。`Backup.cs` は Mac 未配線 (L5-8 と同じ) で、Windows の画面での動作は未確認 (Core のテストで確認)。
- **テスト**: `BackupTests.Backup_IncludesCustomRomajiTable_AndUserDictionaries` (1 本追加。`romaji.tsv` が Create に入り Restore で戻る、既存の `dictionaries/*.txt` も引き続き入る、`.md` など他の拡張子は入らない)。1146/1146 passed、`--eval` は変更前と同一。
- **近傍の --repro** (打鍵経路は触っていない。変化なし):

| 入力 | 最後 | committed |
|---|---|---|
| konnnichiha | enter | こんにちは |
| ka | enter | か |
| kya | enter | きゃ |
| tsu | enter | つ |
| xtu | enter | っ |
| nn | enter | ん |
| kanji henkan | enter | かんじへんかん |
| hello | enter | hello |
| this is a pen | enter | this is a pen |
| ka, | enter | か、 |
| meeting | space | `meeting ` |

## Q26 の修正 (Shift+←→ で伸縮した文節の候補が 3〜4 件に固定される、2026-10-27)

- **目的**: リョウさんの報告「文章の途中の変換で、候補の語が一部著しく少なくなる」の原因 1 (調査 I1、再現済み)。変換中に Shift+← / Shift+→ で文節の区切りを動かすと、その文節の候補が 3〜4 件 (単独変換 + ひらがな + カタカナ + 半角) に固定され、Space・↓・数字・PageDown のどれでもエンジンの候補一覧 (`MoreCandidates`) が出ず、Esc まで戻らなかった。文の途中の文節は azooKey の区切りで助詞を伴う (`きょう|のかいぎ`) ことが多く、利用者が区切りを直す操作でこそ起きていた。
- **原因**: `Resize` → `Reconvert` が候補を作り直すとき、選択中の文節に残っていた `Expanded` (候補一覧を引き終えた印) を戻していなかった。`Expand` は `!clause.Expanded` のときしか呼ばれない。
- **変更** (`src/Meltype.Core/Composition/CompositionController.cs` `Reconvert`): `clause.Expanded = false` を 1 行。`Reconvert` の呼び元は `Resize` の 3 か所だけで、`:735` の「候補を開くだけ」の経路 (`Expanded = true` で作る) は通らない。
- **得と損**: 得: 伸縮した文節でも同音異義語の一覧が出る (本物のエンジンの計測: `かいぎ` 4 → 24 件)。損: 伸縮のたびにエンジンへ一覧を 1 回引き直す (伸縮した文節の分だけ。候補を動かすたびには引かない)。
- **残る限界・未確認**: 実機未確認 (Core のハーネスと `--bench` の計測で確認)。
- **テスト** (`src/Meltype.Core.Tests/ResizeCandidatesTests.cs`、5 件): 縮める / 伸ばす (修正前は 3 件で赤) / 伸縮で生まれた隣の文節 / 引く回数が伸縮 1 回につき変わった文節の分だけで候補を動かしても増えない / 伸縮しない文節の候補数は不変。全体 1150/1150、`--eval` は基準と同じ。

## Q27 の修正 (助詞で終わる文節の候補一覧に語幹の同音異義語を足す、2026-10-27)

- **目的**: 同じ報告の原因 2 (調査 I1、再現済み)。文の途中の文節は読みに助詞が付く (`かいぎで`・`しりょうを`) ので、その読みで候補一覧を引くと azooKey の辞書語の一覧 (同音異義語) が落ち、全文候補 + 先頭文節候補 + かなの 11〜12 件で頭打ちになっていた (単独の読みは 17〜30 件)。Shift+←→ で区切りを動かしていなくても起きる (「両方」の報告と一致)。
- **変更** (`mac/Sources/MeltypeIME/Converter.swift` `candidates(for:)`): 読みの末尾が助詞 1 文字 (を・で・に・の・は・が・と・も・へ) で、先頭候補の表記も同じかなで終わり、読みが 3 文字以上のときは、語幹 (末尾を除いた読み) でも `results(for:)` を引き、「語幹の候補 + 助詞」を重複なしで後ろに足す。上限 30 は今までどおり。Core・FFI は変えていない (Mac だけで試せる形を先に取った)。
- **得と損**: 得: 助詞つきの文節で同音異義語が出る (`--bench` の計測、学習データは空: `かいぎで` 12 → 22 (`回議で`・`會議で`・`開議で` が入る)、`こうえんを` 11 → 30、`きかいは` 12 → 27)。損: 文節を選んだときにエンジンをもう 1 回呼ぶ (キャッシュ `resultCache` に乗る)。足した候補 (`開議で`) を選んで確定しても、azooKey 側の学習は「その読みで azooKey が出した候補」に限られるので掛からない (Core の学習 `ConversionHistory` には入るので、次からは 1 番目に出る)。
- **残る限界・未確認**: 判定は品詞を見ない (末尾のかな + 先頭候補の末尾 + 3 文字以上) ので、て形 (`しんで`・`およいで`) や副詞 (`すでに`・`さらに`)・接続詞 (`なので`) にも発火し、一覧の後ろに `新で`・`既で` のような無関係な候補が並ぶことがある (先頭側の並びは変わらない。査読 F10-1。絞るなら「語幹の先頭候補が先頭候補の語幹と一致するときだけ」の案があり、実機の件数を見てから)。足した候補を選んで確定しても azooKey 側の学習には入らず (Core の学習には入る)、語幹の候補が `recentCandidates` (上限 10) を 1 件使う (査読 F10-2)。実機未確認。助詞が**前**に付く区切り (`のかいぎ`・`をみた`・`がくる`) には効かない (azooKey の `data` 要素が `の会議` の形で来るため。L7-17 の助詞結合の隣接。別件)。語幹が 2 文字以上の読みだけ (`はし` + `を` = 3 文字は対象)。
- **テスト**: Swift に自動テストは無い。`--bench` に一時的に足した計測で前後を比較 (コミットには入れていない)。ビルド・codesign・`check-mac-aot-settings.py`・辞書アプリの `--self-test` は統合後に確認。

## Q28 の修正 (登録シートの Tab と漢字の「ひらがなにする」、2026-10-28)

- **目的**: 辞書アプリの登録・編集シートで、Tab で読み⇄単語を移れなかった (読みが確定済みでも、入力中でも)。読み欄に漢字を入れても「ひらがなにする」が効かなかった (ローマ字・カタカナしか扱っていなかった)。リョウさんの報告 (Q17〜Q27 の実機確認のついで)。
- **変更**:
  - `mac/Sources/MeltypeDictionary/WordEditor.swift`: `autorecalculatesKeyViewLoop = false` にして 読み → 単語 → 読み の順を明示 (自動の順では移らないことがあった)。
  - `mac/Sources/MeltypeIME/InputController.swift`: クライアントが辞書アプリ (`io.github.yksr-melt.Meltype.Dictionary`) のときだけ、未確定の文字があるときの Tab (修飾は Shift まで) を、そのまま確定してからアプリに渡す。ほかのアプリの Tab (予測候補・格子) は変えない。
  - `mac/Sources/MeltypeDictionary/SystemReading.swift` (新規): 漢字を含む語を macOS の日本語解析 (`CFStringTokenizer` の `LatinTranscription` → `latinToHiragana`) で読みにする。「ひらがなにする」はこれのあとに本体の `ToReading` (ローマ字・カタカナ) を通す。ボタンの有効判定も同じ関数。
- **得と損**: 得: 登録が手だけで済む。漢字から読みを引ける。損: 読みは macOS の解析なので人名・専門語で外れることがある (ツールチップに明記。画面で直す前提)。辞書アプリの中だけ Tab の意味が変わる (入力中の Tab で予測候補に入れない)。
- **確認**: 使い捨ての Swift スクリプトで 東京 → とうきょう、佐々木 → ささき、新宿御苑 → しんじゅくぎょえん、ABC会社 → ABCかいしゃ、テスト・yomi は不変。実機 (2026-10-28、リョウさん): 確定後の Tab・入力中の Tab・漢字の「ひらがなにする」とも期待どおり。自動テストなし (Swift)。

## 本家追従 W16 (2026-10-28 の精査から小さな 9 件)

- **目的**: 本家 main `a2d74f0` までの新着を精査し ([UPSTREAM_REVIEW_2026-10-28.md](UPSTREAM_REVIEW_2026-10-28.md))、リョウさんの指示「軽微なものは先に全部」で、手間の小さい 9 件を入れる。
- **進め方**: 1 項目 1 ブランチ (起点 main `67e5341`)。実装はテスト先行、項目ごとに機械ゲート (全テスト・`--eval`・main の Core / 辞書に戻すと新テストが赤になるか)、統合の木で全テスト・`--eval`・コーパス比較・Mac ビルド・codesign・AOT 設定・辞書アプリの自己テスト。独立査読 (1 回目 3 レーン・2 回目 1 名) の指摘は、項目のブランチに新しいコミットで直した。
- **項目とコミット**:
  - W16-1 (`a0d54a4`・`7229d04`): `dictionaries/propernouns.txt` から `Wakatte` を外す。`wakatte`・`sorewakatte` が英字のまま確定されていた (本家 issue #1231、本家は未修正)。既存テストの `Wakatte.TV` (ドメインの `.` の後ろの規則の例) は `Tetr.TV` に置き換えた。
  - W16-2 (`175e488`・`430fadd`): `CompositionController.HandlePredictionKey` の `default:`。予測候補を選んでいる (`_predictionIndex >= 0`) ときに文字として入るキー (`TypesCharacter`: かな入力のかなキー、または空白・制御文字でない文字) を打つと、`CommitPrediction` してから `StartWith`。今までは選んだ語を捨てて読みに戻った (設計どおりだったが、本家 issue #1187 #1214 と同じ不満があるので変える。リョウさんの選択)。BackSpace・Esc・Enter・Space・←→・修飾キーは今までどおり。既存テスト `Prediction_TypingLeaves` は旧仕様を固定していたので、期待と名前を変えた。docs/USAGE.md の Tab の行に 1 文。
  - W16-3 (`571d0ab`): `UserDictionary.Split` が、読みの終わりの次の文字が小さいかな (ゃゅょぁぃぅぇぉゎ と片仮名。っ は別の拍なので除く) なら、その長さでは切らない (本家 PR #1160 の考え方。実装は写していない)。
  - W16-4 (`adde6d4`): `TimeCandidates` の きょう の候補に `2026/10/28(水)`・`10/28(水)` (本家 #1072 (f)) と `20261028` (本家 PR #1189) を足す。並びは本家 main と同じで、既存の 7 つの相対順は変えない。
  - W16-5 (`7b8c228`): `InputController.swift` の変換中の候補 (`updateCandidates`) で、一覧を出し直すときに `hide()` → `update()` → `show()` ×2 (本家 PR #1250 と同じ)。予測候補 (`updatePredictions`) には入れない (打つたびに一覧が変わるので出し直すとちらつく。本家も変えていない)。
  - W16-6 (`19fc5f3`・`b238321`): `english.txt` に printenv・nohup・doomscroll・tmux・claudecode・vocaloid・opencode、`english-readable.txt` に vocaloid・opencode。`CompositionDetector.EndsWithParticleAfterUnknownWord` で english-readable の語を「知らない語 + 助詞」にしない (本家 main と同じ形)。査読 R-B の必須: english-readable にしか無い語は、確定直前の打ち間違いの補正 (`CompositionText.FixTypos` の守り `EnglishMask` が使う `IsKnownEnglishWord` は english.txt・固有名・スペルチェッカーしか見ない) に守られず、表示 `vocaloidは` が Enter で `ゔぉかぉいしゃ` (d→s) になっていた。english.txt にも置いて直した。
  - W16-7 (`7061829`・`4764b8b`): `english.txt` に bookoff・adb (本家 issue #1191・#1131 の adb 単体)、`misspellings.txt` に クダシア → クダサイ (issue #1190。自動では直さず「もしかして」に出る)。
  - W16-8 (`755fdee`・`fe28a44`): `UserDictionaryFile` の書き出しの日時、`Backup` の作成日時、`NumberCandidates` の数値・元号の年、`CompositionController` の元号の年、`DictionarySuggestions` の 1 日 1 回のヒントの日付を `CultureInfo.InvariantCulture` に (本家 #1157 の Core の部分。Mac の dylib は `InvariantGlobalization=false` なので、タイ語の地域設定では書き出しの年が仏暦になっていた)。
  - W16-9 (`e821ad6`・`ddbe42a`): `mac/install.sh` (登録できたときの案内) と `mac/README.md` で、入力ソースが出てこないときはシステム設定からの追加を先に、ログアウトを次に (本家 PR #1026 #1027 の文言)。登録できなかったときの案内 (直前に有効な入力ソースの一覧へ直接書き込む #21 の分岐。「+」の一覧に出ない Mac のためで、「+」を先に案内すると一覧への二重登録の条件もそろいうる。査読 R-C の必須) と、前の Meltype を消した直後の案内は元のまま。
- **確認 (統合の木)**: 全テスト 1177 → 1213 (W16-1 +2、W16-2 +12、W16-3 +2、W16-4 +5、W16-6 +5、W16-7 +6、W16-8 +4)。`--eval` は起点と同じ (1293/1294、外れは `my name is taro` の 1 件)。コーパス比較 (同梱の英単語表。実機の NSSpellChecker は通していない): 日本語の読み 120,714 入力・英単語 6,000 語 × 3 形 = 18,000 入力・対象語の綴りを含む日本語 222 入力で変化 0、対象語 10 語 × 前 5 × 後ろ 12 = 600 入力で 528 変化 (対象語が英字になる・wakatte が日本語になる方向)。変化しなかった 72 入力のうち 30 件は崩れたまま (K108 の (2) の yo の形 20 件と、(4) の `koreadb` + 助詞・語尾 10 件。どちらも取り込みの前と同じ)、残る 42 件は取り込みの前から正しい。Mac ビルド・`codesign --verify --deep --strict`・`tools/check-mac-aot-settings.py` (3 PASS)・辞書アプリの `--self-test` (213 件 PASS)。
- **得と損**: 得: 毎日打つ語 (分かって) と、AI・端末の道具の名前が崩れない。予測候補を選んで続けて打てる。損: `Wakatte.TV` は わかって。TV になる。予測を選んだあと打ち直したいときは Esc か BackSpace が要る (K108 と W16-2 の説明)。
- **残る限界・未確認**: すべて実機未確認 (漢字変換の結果・候補窓・外部ディスプレイ)。W16-2 のテンキーは Core のテストの仮のキー表に無く未確認。W16-5 は Swift に自動テストが無い。子音で終わる語 + yo・yu の形、予測候補の窓、`koreadb` + 助詞・語尾は K108。
