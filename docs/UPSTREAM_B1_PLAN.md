# B-1 取り込みの要件定義と設計計画（本家 2026-10-18 追従）

## 0. この文書の位置づけ

- 対象: 前回同期点 `45e14f8`（2026-10-14）以降、本家 main `af2d1a68`（2026-10-18）までに入った変更のうち、**B-1（追加型で悪化リスクが低い Core 項目）**。
- 本家の出どころ: 統合 PR `#768` と、10/18 にマージされた個別 PR（各項目に commit を記載）。
- **進み具合の正本は `docs/UPSTREAM_PR_TRACKER.md`**、数字・限界の正本は `docs/IMPROVEMENT_PLAN.md`。この文書は「何を作るか（要件）」と「どう作るか（設計）」だけを持つ。
- 進め方は `UPSTREAM_PR_TRACKER.md` の 7 ステップに従う（赤→緑、BI と NS の両方で前後比較、独立査読、1 項目 1 コミット、push しない）。
- **P48〜P55 の番号は仮**。管理表へ入れるときに確定する（P47 まで使用済み）。

## 1. 対象と範囲

| 仮 ID | 本家 | 内容 | 設定 | 変更種別 |
| --- | --- | --- | --- | --- |
| P48 | #760 #753 #791 #792 #756 #758 #830 #840 #831 | 辞書語 9 語（whois・tver・mozc・DeVito・ショタ・ラティス・autoclicker/clicker・docs・ext） | なし | 辞書のみ |
| P49 | #796 (`c947c240`) | 計算式を変換すると結果を候補に出す | `CalculatorCandidates`（既定 OFF） | 追加型 |
| P50 | #769 / #786 (`621a24bd` / `7674f78b`) | 数字 → 漢数字・元号、元号の年 → 西暦 | なし | 追加型 |
| P51 | #787 (`c458d748`) | 尺貫法の単位 → 単位の字とメートル法の大きさ | なし | 追加型 |
| P52 | #785 (`c6dac85a`) | じこう → 今月の時候の挨拶 | なし | 追加型 |
| P53 | #795 (`adf9baaf` / `53311a61`) | 子音の略入力（ast → 明日） | `ConsonantAbbreviations`（既定 OFF） | 追加型 |
| P54 | #763 (`660ec4bf`) | ユーザー辞書に 1 文字の読みを登録できるようにする | なし | 機能追加 |
| P55 | #801 (`eb3ddcc7`) | 数字・記号 1 文字の選び直しも学習する | なし | 学習の変更 |

**対象外（この文書では扱わない）**: B-2（クリップボード・GitHub タイトル・予測 Delete・#841・#829・#848・#799・#800・#842・#738）、B-3（open・change・mobisode・open+no）、C（設定群）、Windows/Linux 固有。

## 2. 前提: Mac 版の実装構造

| 場所 | 役割 | この計画での扱い |
| --- | --- | --- |
| `src/Meltype.Core/Composition/CompositionOptions` | 設定と外の判定器へのつなぎ | 新しい設定・関数をここに足す |
| `CompositionController.JapaneseCandidates`（2140 行付近） | 文節の候補の組み立て | 追加候補は**既存候補の後ろ**に足す |
| `CompositionController.TimeCandidates`（2175 行付近） | いま・なう・きょう | 元号・尺貫法・時候もここに足す（本家と同じ） |
| `CompositionController.StartConversion` | 変換開始 | 計算式・子音略入力の分岐を先頭に足す（本家と同じ） |
| `CompositionController.AddRawCandidates` | 打ったままの候補 | 数字候補・子音略入力を足す |
| `CompositionController.Learn`（3010 行） | 選び直しの学習 | P55 で条件を変更。計算式・数字・尺貫法・時候は除外を追加 |
| `MeltypeSession.CreateDefault`（155 行） | 設定 → `CompositionOptions` の配線 | 新しい設定を配線 |
| `src/Meltype.Core/Config/Settings.cs` | 設定の定義（属性付き） | 新しい設定を追加 |
| `src/Meltype.Core/Config/MacSettingsCatalog.cs` | Mac の「設定」タブ（NativeAOT・手書き） | 新しい設定を `Bool` で追加 |
| `mac/Sources/MeltypeIME/*` | Swift の IME | **候補の追加では変更不要**（`view.candidates` の JSON をそのまま出す） |
| `dictionaries/*.txt` | 辞書 | 形式に従って追記（1 行に複数語のものと 1 行 1 語のものがある） |

現在のテスト: **`[Test]` 853 件 / 45 ファイル**。テストは `bash --noprofile --norc -c 'export PATH=$HOME/.dotnet:$PATH; cd src/Meltype.Core.Tests && dotnet run -c Release'` で回す。名前で絞るときは末尾に語を渡す（例: `... dotnet run -c Release -- P48_`）。

## 3. 共通方針

1. **既存の判定を変えない**。候補は必ず既存の後ろに足し、先頭候補・Space 2 回目の候補を動かさない。
2. **既定の動きを変えない**。新しい設定は既定 OFF、または「候補が増えるだけ」にする。
3. **学習しない候補**（計算結果・漢数字/元号・尺貫法・時候）は、次の 3 経路すべてから除外する。
   - `ConversionHistory.Remember`（`Learn()` 内）
   - `DictionarySuggestions.Record`（`RecordSuggestions()`。Mac 独自）
   - 変換エンジンへの学習（`LearnConversion()`。数字を含む読みは既に除外）
4. 1 項目 1 コミット。辞書語は**ファイル単位**（`english.txt` / `propernouns.txt` / `candidates.txt`）でコミットし、語ごとにテストを分ける（各コミットで赤→緑を確かめる）。
5. 先にテストを書き、**修正前に赤**になることを確かめる（辞書語は「語を足す前の赤」）。
6. 前後比較は **BI（同梱の英単語表）と NS（NSSpellChecker 相当）の両方**。`tools/eval-compare.sh` の **broken 0** を条件にする。
7. 実機確認は最後にまとめて行い、それまでは「実機確認待ち」として残す。

---

# 第 I 部 要件定義

## 4.1 P48 辞書語 9 語

| 語 | ファイル | 期待（入力 → 確定/候補） | 本家 | 備考 |
| --- | --- | --- | --- | --- |
| whois | `english.txt` | `whois` → `whois`（`うぉis` にしない） | `5e4c2c29` #760 | `whoiswo` → `whoisを` |
| tver | `english.txt` | `tver` → `tver`（`tゔぇr` にしない） | `7a775dfd` #753 | |
| mozc | `english.txt` | `mozc` → `mozc`、後ろは日本語（`mozcde` → `mozcで`） | `eab91571` #791 | `propernouns.txt` に `Mozc` は既にある |
| DeVito | `propernouns.txt` | `devito` → `DeVito` | `acaad1d1` #792 | 固有名詞の正しい形 |
| しょた | `candidates.txt` | `しょた` の候補に `ショタ` | `f7237fd7` #756 | 候補追加のみ |
| らてぃす | `candidates.txt` | `らてぃす` の候補に `ラティス` | `40374eb8` #758 | 候補追加のみ |
| autoclicker | `english.txt` | `autoclicker` → `autoclicker` | `3762374c` #830 | |
| clicker | `english.txt` | `clicker` → `clicker` | 同上 | |
| docs | `english.txt` | `docs` → `docs`（`どか` にしない） | `23348287` #840 | `docs/` のパス保護と両立 |
| ext | `english.txt` | `ext` → `ext` | `bfd137d1` #831 | 3 文字。誤判定に注意 |

**要件の詳細**
- 追記位置は本家と同じ「末尾」。`# Issue #NNN` のコメントを添える（出どころを残す）。
- 既存行は変更しない。
- `mozc` は `propernouns.txt` の `Mozc` と重複しない（大文字は固有名詞、小文字は英単語として後ろを日本語にする）。
- `docs` は P42 の保護区間（`./docs/` など）を壊さない。
- `ext` は `text`・`next`・`context` などの一部と誤認しない（単語境界で判定されること）。

**実機の NSSpellChecker の答え (2026-10-19、開発機、language: "en")**
- `word`: `whois`・`docs`・`ext`・`who`・`is`・`t`・`ver`・`mo`・`de`・`auto`・`clicker`・`click`・`er`・`doc`・`e`
- `not-word`: `tver`・`mozc`・`zc`・`devito`・`vito`・`autoclicker`・`cs`・`xt`
- このため `whois`・`docs`・`ext` は NS では既に単語。同梱の表 (`BuiltIn`) では崩れるので、`english.txt` への追加は BI のためと、NS でも語の途中で割れないようにするために行う。
- **`mozc` は Mac では既に期待どおり**（`propernouns.txt` の `Mozc` が効いている。`mozcde` → `mozcで` を確認済み）。テストは guard とし、本家とそろえるために `english.txt` にも足す。

**対象外**
- Windows/Linux の辞書ファイル（Core 共通のため結果は届くが、検証は Mac のみ）。

## 4.2 P49 計算式

**要件**
- 設定 `CalculatorCandidates`（既定 **OFF**）が ON のとき、次のすべてを満たす入力を式として扱う。
  - ローマ字入力（`KanaInput == false`）
  - 自動判定モード（`DisplayMode.Auto`）
  - 数字と `+ - * / ( ) .` と全角の `× ÷ ＋ －`、空白だけを含む
  - 演算子を 1 つ以上、数字を 1 つ以上含む
  - 長さ 80 以下、結果が NaN・Infinity・`1e15` 以上でない、0 除算がない
- 変換（Space）したとき、候補は **打った式 / 結果 / `式=結果`** の 3 つ（重複は除く）。
  - `1+2*3` → `1+2*3` / `7` / `1+2*3=7`
  - `(1+2)*3` → `9`
  - `10/4` → `2.5`
  - `1/3` → `0.3333333333`（小数 10 桁で丸め）
  - 全角 `１＋２`・`×`・`÷` も受ける。末尾の `=` は無視する。
- 式として読めないときは、今までどおりの変換（何も変えない）。
- 結果は**学習しない**（設定を OFF に戻したときに古い結果が先頭に出ないように）。

**対象外**
- かな入力、手動モード、英数モード、関数・累乗・変数。

**受け入れ条件**
- 設定 OFF では候補が 1 つも増えない。
- 設定 ON でも、式でない入力（`abc`・`1`・`1-`・`(1+2`・`1/0`）は今までと同じ。

## 4.3 P50 数字の漢数字・元号・西暦

**要件**
- 数字だけの区間（半角・全角・カンマ区切り、16 桁以下）で、次の候補を**既存の後ろ**に出す。
  - 漢数字: `2026` → `二〇二六`
  - 読み下し: `二千二十六`（十・百・千の前の `一` は省く）
  - 正式: `弐千弐拾六`
  - 4 桁で西暦に見えるとき: `令和8年`（明治 1868 〜 令和 2100）
  - 先頭が `0` のときは漢数字だけ（`007` → `〇〇七`）
- 元号の年: `れいわ8ねん` → `2026年` / `2026`、`れいわがんねん` → `2019年` / `2019`。範囲外は出さない。

**対象外**
- 17 桁以上、数字以外を含む区間、単位付き（P51 で扱う）。

**受け入れ条件**
- 先頭候補と Space 2 回目の候補は変わらない。
- 漢数字・元号を選んでも、次回の先頭候補が変わらない（学習しない）。

## 4.4 P51 尺貫法

**要件**
- `数字 + 単位の読み` の文節に、**単位の字**と**メートル法の大きさ**を出す。
  - `3しゃく` → `3尺` / `約90.91cm`
  - `1じょう` → `1丈` / `約3.03m`
  - `1つぼ` → `1坪` / `約3.31㎡`
  - `1ごう` → `1合` / `約180.39mL`
  - `1かん` → `1貫` / `約3.75kg`
- 単位: 尺・寸・間・丈・坪・合・升・斗・貫・匁・斤・両（本家の 12 種）。
- 数字は 1〜6 桁、小数は 1〜3 桁。全角数字・全角小数点も受ける。
- 候補は既存の後ろ。学習しない。

**対象外**
- 数字のない単位だけの入力、単位のない数字（P50）。

**受け入れ条件**
- `3と` のような「数字 + と」でも、既存の候補の後ろに `3斗` / `約54.2L` が増えるだけで、先頭は変わらない。

## 4.5 P52 時候の挨拶

**要件**
- `じこう` / `じこうのあいさつ` の文節に、**今月・前月・翌月**の時候の挨拶を候補として出す。
  - 10 月: `秋冷の候` / `紅葉の候` / `秋涼の候` / `寒露の候` など
  - 先頭に `拝啓　秋冷の候、` も出す
- 月は `CompositionOptions.Now()` のローカル時刻で決める。
- 候補は既存の後ろ。学習しない（古い挨拶が先頭に出ないように）。

**対象外**
- 月をまたぐ候補の網羅（本家どおり今月・前月・翌月のみ）。

**受け入れ条件**
- `じこう` の既存候補（時効・事項・施行など）の先頭は変わらない。
- 4 月なら `陽春の候`、10 月なら `秋冷の候` が出る。

## 4.6 P53 子音の略入力

**要件**
- 設定 `ConsonantAbbreviations`（既定 **OFF**）が ON のとき、2 文字以上の英字だけの入力が `candidates.txt` の英字の読み（例: `ast`）に一致したら、その語（`明日`）を候補に出す。
  - `ast` → `明日`（候補。確定は利用者が選ぶ）
  - 打った英字全体が一致したときは 1 文節の候補にする（本家と同じ）。
  - 英字の文節にも候補として足す。
- 設定 OFF では今までどおり。

**対象外**
- かな入力、3 文字未満、英字以外を含む入力。

**受け入れ条件**
- `ast` の候補に `明日` が増えるが、先頭は `ast` のまま。
- 既存の英語判定（`ast` を含む単語）を変えない。

## 4.7 P54 ユーザー辞書の 1 文字の読み

**要件**
- 自分で登録する読みの最小長を **1 文字**にする（画面・`userdict.txt`・`AddRange`）。
- ほかの辞書（macOS の plist など）から**取り込む**読みの最小長は **2 文字のまま**。
- 1 文字の読みは、**変換する読みがちょうど 1 文字のときだけ**使う。
  - `わ` → `輪` を登録 → `わ` 単独では `輪`。`わたし` は `わたし`（`輪たし` にしない）。
- 空の読みは登録できない。エラー文言は「読みを入力してください。」。

**対象外**
- 既存の登録済みデータの移行（そのまま読める）。

**受け入れ条件**
- 2 文字以上の読みの動きが変わらない。
- 取り込みの最小長が変わらない。

## 4.8 P55 数字・記号 1 文字の選び直しの学習

**要件**
- 1 文字の読みでも、**かな以外**（数字・記号）の選び直しは学習する。
  - `1` の候補から `①` を選ぶ → 次回 `1` の先頭は `①`
  - `(` の候補から `（` を選ぶ → 次回 `(` の先頭は `（`
- 1 文字のかな（`き` → `記`）は今までどおり学習しない（`きごうとう` への巻き込みを防ぐ）。
- 2 文字以上は今までどおり。

**対象外**
- 学習済みの語をそのまま確定したときの回数更新（`Touch`）は 2 文字以上のまま（本家と同じ）。

**受け入れ条件**
- かな 1 文字の学習が増えない。
- 数字・記号の学習で、かなの変換が変わらない。

---

# 第 II 部 設計計画

## 5. 設定の追加

| 設定名 | 既定 | Settings の属性 | Mac 設定タブ | 配線 |
| --- | --- | --- | --- | --- |
| `CalculatorCandidates` | `false` | `[Category("1. 全般")]`、DisplayName「計算式を変換すると結果を候補に出す」、本家と同じ Description | `ConversionGroup` に `Bool` | `MeltypeSession.CreateDefault` → `CompositionOptions` |
| `ConsonantAbbreviations` | `false` | 同上「子音の略入力を候補に出す」 | `ConversionGroup` に `Bool` | 同上 |

**手順**
1. `Settings.cs` にプロパティを追加（本家と同じ属性文言。Mac 向けの言い回しが要るときは `MacSettingsCatalog.MacDescriptions`）。
2. `MacSettingsCatalog.cs` に `Bool(...)` を追加（`Label` と `Description` は `Settings` の属性と一致させる。`MacSettingsTests` が見比べる）。
3. `MeltypeSession.CreateDefault` に `CalculatorCandidates = () => shared.Current.CalculatorCandidates` などを追加。
4. `tools/check-mac-aot-settings.py` を実行（設定数・AOT の検査。期待値が固定なら更新）。
5. Swift は変更不要（設定タブはカタログ JSON を読む）。

## 6. 項目別の設計

### 6.1 P48 辞書語

- `dictionaries/english.txt` の末尾に追記:
  ```
  # Issue #616
  whois
  # Issue #735
  tver
  # Issue #249
  mozc
  # Issue #822
  autoclicker
  clicker
  # Issue #835
  docs
  # Issue #826
  ext
  ```
  ※ 本家は Issue ごとに節を分けている。Mac でも同じ形にして出どころを残す。
- `dictionaries/propernouns.txt` の末尾:
  ```
  # Issue #268
  DeVito
  ```
- `dictionaries/candidates.txt` の末尾:
  ```
  # Issue #653
  しょた ショタ
  # Issue #654
  らてぃす ラティス
  ```
- Core のコード変更なし。

### 6.2 P49 計算式

- 新規 `src/Meltype.Core/Composition/Calculator.cs`（本家をそのまま。四則演算の自前パーサ、MaxLength 80、丸め 10 桁）。
- `CompositionOptions` に `public Func<bool> CalculatorCandidates { get; init; } = () => false;`。
- `StartConversion` の `_tabStartedConversion = false;` の直後に本家と同じ分岐:
  ```csharp
  if (_options.CalculatorCandidates() && !_text.KanaInput && _text.Mode == DisplayMode.Auto &&
      Calculator.Evaluate(_text.Raw) is { } result)
  {
      var expression = _text.Raw.TrimEnd('=');
      _clauses = [new Clause(_text.Raw, true, Distinct([_text.Raw, result, $"{expression}={result}"])) { Raw = _text.Raw }];
      _selectedClause = 0;
      _converting = true;
      return;
  }
  ```
- 学習除外は**追加しない**: 計算式の文節は `new Clause(..., true, ...)`（英語扱い）で作るので、`Learn()`（`!c.IsEnglish`）・`RecordSuggestions()`・`LearnConversion()` のすべてで既に対象外になる。テストで固定する。
- 本家との違い: 本家は半角の数字だけだが、Mac では全角の数字 (１＋２) も受ける（本家で null だった入力だけが対象なので、既存の結果は変わらない）。
- テストファイル: `CalculatorTests.cs`（Evaluate 単体）、`CalculatorCandidatesTests.cs`（統合）。

### 6.3 P50 数字

- 新規 `NumberCandidates.cs`（本家をそのまま）。
- `StartConversion` の英語文節の候補に `NumberCandidates.For(segment.Raw)` を追加（本家と同じ位置）。
- `AddRawCandidates` にも `NumberCandidates.For(raw)` を追加。
- `TimeCandidates` の先頭に `if (EraToYear(reading) is { } year) return year;` と `EraToYear`・`EraReading` を追加。
- 学習除外: `IsNumberChoice(Clause)`（読みが数字だけで、候補が漢数字・元号のとき）を `Learn()` と `RecordSuggestions()` に追加。
  - **判断**: 1 文字の数字（P55 の対象）は除外しない。2 文字以上の漢数字・元号だけ除外する。
- テストファイル: `NumberCandidatesTests.cs`（単体）、`NumberCandidatesIntegrationTests.cs`（統合・学習）。

### 6.4 P51 尺貫法

- `TimeCandidates` の先頭に `if (TypedUnitCandidates(reading) is { } units) return units;`。
- `TraditionalUnits`（12 種）・`UnitReading` 正規表現・`TypedUnitCandidates` を追加（本家をそのまま）。
- 学習除外: `IsUnitChoice(Clause)` を `Learn()` と `RecordSuggestions()` に追加。
- テスト: `TraditionalUnitTests.cs`。

### 6.5 P52 時候

- `TimeCandidates` の先頭に `if (reading is "じこう" or "じこうのあいさつ") return SeasonalGreetings(_options.Now().Month);`。
- `SeasonalTerms`（12 か月 × 4 語）・`SeasonalGreetings` を追加（本家をそのまま）。
- 学習除外: `IsSeasonalChoice(Clause)`（読みが じこう / じこうのあいさつ）を `Learn()` と `RecordSuggestions()` に追加。
- テスト: `SeasonalGreetingsTests.cs`（各月を `Now` で固定）。

### 6.6 P53 子音の略入力

- `CompositionOptions` に `ConsonantAbbreviations`。
- `StartConversion` の先頭（計算式の分岐の後）に本家と同じ分岐:
  ```csharp
  if (_options.ConsonantAbbreviations() && !_text.KanaInput && _text.Mode == DisplayMode.Auto &&
      _text.Raw is { Length: >= 2 } typedRaw && typedRaw.All(char.IsAsciiLetter) &&
      _options.Candidates?.Lookup(typedRaw.ToLowerInvariant()) is { Count: > 0 } abbreviation)
  {
      _clauses = [new Clause(typedRaw, true, Distinct([.. abbreviation, typedRaw, .. EnglishCandidates(typedRaw)])) { Raw = typedRaw }];
      _selectedClause = 0;
      _converting = true;
      return;
  }
  ```
- `AddRawCandidates` にも英字の候補を追加（本家と同じ）。
- `dictionaries/candidates.txt` の末尾:
  ```
  # --- 子音の略入力 (Issue #62: 英字の読みで候補を出せる) ---
  ast 明日
  ```
- テスト: `ConsonantAbbreviationTests.cs`。

### 6.7 P54 1 文字の読み

`UserDictionary.cs` を本家に合わせて変更する（Mac の独自部分は保つ）。

- 定数:
  - `MinReadingLength = 2`（取り込み用。名前は据え置き）
  - `MinRegisteredReadingLength = 1`（自分で登録する用）
  - `MinSplitReadingLength = 2`（語の途中で使う最小長）
- `Validate`: `MinRegisteredReadingLength` で判定。文言は「読みを入力してください。」。
- `Parse`: `MinRegisteredReadingLength`。
- `AddRange`: `MinRegisteredReadingLength`。
- `Split`:
  - 早期 return の `kana.Length < MinReadingLength` を `MinRegisteredReadingLength` に。
  - ループの下限を `kana.Length == 1 ? MinRegisteredReadingLength : MinSplitReadingLength` に。
- `Lookup` は変更なし（完全一致）。
- Swift の辞書画面（`mac/Sources/MeltypeDictionary*/`）に 2 文字以上のチェックがあれば 1 文字に直す。文言も合わせる。
- テスト: `UserDictionaryOneCharTests.cs`。
  - `わ`→`輪` 登録 → `わ` で `輪`、`わたし` で `わたし`
  - 取り込みは 2 文字未満を捨てる
  - 空の読みはエラー

### 6.8 P55 数字・記号 1 文字の学習

`CompositionController.Learn()` の条件を本家と同じにする。

```csharp
foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed &&
    (c.Reading.Length >= 2 ||
     c.Reading.Length == 1 && (c.Reading[0] < 'ぁ' || c.Reading[0] > 'ヿ') && !char.IsAsciiLetter(c.Reading[0]))))
```

- `RecordSuggestions()` は、1 文字の数字・記号を登録提案に数えないようにする（辞書語として不適切。Mac 独自の判断）。
- `Touch` は変更しない。
- テスト: `NumberSymbolLearningTests.cs`。
  - `1` → `①` を選ぶ → 次回 `1` の先頭が `①`
  - `(` → `（`
  - `き` → `記` は今までどおり学習しない
  - かなの変換に差が出ない

## 7. テスト計画

| 種別 | 内容 |
| --- | --- |
| 赤→緑 | 各項目、先にテストを足して**修正前に落ちる**ことを確認（辞書語は語を足す前） |
| 単体 | 新規 7 ファイル（上記）。既存の `DateTimeCandidatesTests` の書き方（`Keyboard`・`Now` 固定）に合わせる |
| 回帰 | 全テスト（853 + 新規）が通る |
| 品質 | `--eval` の前後が同じ（数字は実行して記録） |
| 回帰ゲート | `tools/eval-compare.sh` で **broken 0** |
| 前後比較 | 辞書語・数字・尺貫法・時候・子音略入力は、BI と NS の両方で「変えないはずの入力」を数千件。差分は全件見る |
| 学習 | 除外した候補を選んでも、`History`・`Suggestions`・変換エンジンのどれにも入らないことをテストで固定 |
| 実機 | 各項目の「実機で見ること」を `UPSTREAM_PR_TRACKER.md` の実機確認欄に残す |

**主な確認入力**
- 辞書語: `whois`・`whoiswo`・`tver`・`mozcde`・`devito`・`しょた`・`らてぃす`・`autoclicker`・`clicker`・`docs`・`./docs/`・`ext`・`text`・`next`・`context`
- 計算式: `1+2*3`・`(1+2)*3`・`10/4`・`1/3`・`-3+5`・`1/0`・`(1+2`・`1e15`・全角
- 数字: `2026`・`007`・`1234567890123456`・`reiwa8nen`・`heiseigannen`・`meiji1nen`
- 尺貫法: `3shaku`・`1jou`・`1tsubo`・`1gou`・`1kan`・`3to`
- 時候: `jikou`（4 月・10 月・12 月）・`jikounoaisatsu`
- 子音: `ast`・`astwo`・`ngt`
- 1 文字読み: `わ`・`わたし`・`わを`
- 学習: `1`→`①`・`(`→`（`・`き`→`記`

## 8. リスクと対策

| リスク | 対策 |
| --- | --- |
| 候補が増えて候補ウィンドウのページが増える | 既存の後ろにだけ足す。先頭・2 番目を動かさないテストを固定 |
| 学習除外の漏れで古い結果が先頭に出る | 3 経路（History・Suggestions・エンジン）すべてをテストで固定 |
| `ext` が短く、ほかの英単語の一部と誤認される | BI・NS の両方で `text`・`next`・`context` などを前後比較 |
| `TimeCandidates` の正規表現が毎文節走る | 先に安い条件（末尾が「ねん」「しゃく」など、数字を含む）で絞ってから正規表現。長文の性能を測る |
| 1 文字読みの巻き込み | `Split` は「読み全体が 1 文字のとき」だけ 1 文字を許す。`わたし` のテストを固定 |
| 数字・記号の学習でかなの変換が変わる | かな 1 文字の学習が増えないことをテストで固定。前後比較にかな入力を含める |
| 既存テストの期待値が変わる | 変更が必要なテストは理由をコミット本文と計画書に書く。むやみに期待値を書き換えない |

## 9. 作業順序とコミット分割

| 順 | 項目 | コミット数 | 理由 |
| --- | --- | --- | --- |
| 1 | P48 辞書語 | 3（`english.txt`・`propernouns.txt`・`candidates.txt`） | 依存なし・最も安全 |
| 2 | P54 1 文字読み | 1 | 独立。P55 の前提になる |
| 3 | P55 数字・記号の学習 | 1 | P54 と独立だが、学習の条件をそろえる |
| 4 | P49 計算式 | 2（Core + 設定） | 独立・既定 OFF |
| 5 | P50 数字 | 2（Core + 元号） | `TimeCandidates` を触る |
| 6 | P51 尺貫法 | 1 | 同上（P50 の後） |
| 7 | P52 時候 | 1 | 同上（P51 の後） |
| 8 | P53 子音略入力 | 2（Core + 設定） | 独立・既定 OFF |

各コミットの前に: テストを足す → 赤を確認 → 実装 → 緑 → 全テスト → `eval-compare` broken 0 → 独立査読 → コミット。

## 10. 完了条件（DoD）

- [ ] 各項目のテストが赤→緑（修正前に落ちることを確認）
- [ ] 全テスト通過（853 + 新規）
- [ ] `tools/eval-compare.sh` が broken 0
- [ ] BI と NS の両方で前後比較し、悪化が説明できる（0 件が望ましい）
- [ ] 独立査読で P0・P1 が無い
- [ ] 1 項目 1 コミット（辞書語は 1 語 1 コミット）
- [ ] `UPSTREAM_PR_TRACKER.md` に P48〜P55 を追加（状態は「コミット済み（実機確認待ち）」）
- [ ] `IMPROVEMENT_PLAN.md` に各項目の節を追加（数字・限界・実機で見る入力）
- [ ] 実機で見る入力を管理表に残す
- [ ] push しない

## 11. 未確認・判断待ち

1. **学習除外の範囲**: 本家は計算式・漢数字・尺貫法・時候を学習除外していない。Mac では「設定を OFF に戻したのに古い候補が先頭に出る」のを避けるため除外を推奨。異論があれば本家と同じにする。
2. **P55 と P50 の関係**: 1 文字の数字は学習する（P55）。2 文字以上の漢数字・元号は学習しない（P50）。この切り分けでよいか。
3. **`ext` の採用**: 3 文字で衝突の可能性がある。前後比較で悪化が出れば見送り。
4. **`docs` とパス保護**: `./docs/` が保護区間として英字のままであることと、`docs` 単独が英字であることの両立を確認。
5. **Swift 辞書画面のバリデーション**: 1 文字読みを受け付ける文言・チェックの有無を実装時に確認。
6. **設定のグループ**: `CalculatorCandidates`・`ConsonantAbbreviations` を「変換・候補」に置くか「入力」に置くか（本家は「1. 全般」）。
7. **番号**: P48〜P55 は仮。管理表の既存項目（P39・P43・P44 など未完了）との整合を確認して確定。

## 12. 実施結果 (2026-10-19)

- 実装した項目: **P48〜P55（8 項目）**。文書 `4f7751e` 以降の 15 コミット（コード 13・文書 2）。push はしていない。
- P48: 辞書語 9 語（終了時点で全テスト 861/861）。
- P54: ユーザー辞書の 1 文字読み（865/865。Swift の画面文言と自己テスト、AOT チェッカーも更新）。
- P55: 数字・記号 1 文字の学習（869/869）。
- P49: 計算式（876/876）。設定 `CalculatorCandidates`（既定 OFF）。
- P50: 漢数字・元号・西暦（883/883）。
- P51: 尺貫法（888/888）。
- P52: 時候の挨拶（895/895）。
- P53: 子音の略入力（901/901）。設定 `ConsonantAbbreviations`（既定 OFF）。
- **テスト**: 853 → 901 件（新規 48 件）。全テスト **901/901**。
- **`--eval`**: 1269/1271（変更前と同じ）。
- **回帰ゲート**: `tools/eval-compare.sh`（base = B-1 開始前 `5f9967b`）で **broken 0 / fixed 0 / added 0 / removed 0**（278/310 で前後同じ）。
- **Mac ビルド**: `mac/build.sh --no-install` 成功。辞書アプリ自己テスト **213 件 PASS**。`tools/check-mac-aot-settings.py` PASS。
- **実機**: 未確認（各項目の「実機で確かめる入力」は `UPSTREAM_PR_TRACKER.md` に記載）。全項目「コミット済み（実機確認待ち）」。
- **本家との違い**:
  - P49: 全角数字も受ける（本家は半角のみ）。式の文節は英語扱いなので学習しない（本家と同じ経路）。
  - P50〜P52: 2 文字以上の漢数字・元号、尺貫法、時候は学習しない（本家は学習する）。1 文字の数字は P55 で学習する。
  - P54: 取り込み（他 IME・plist）と専門用語集の除外は 2 文字のまま。
- **残り**: B-2（条件付き）・B-3（見送り）・C（設定群）は未着手。`UPSTREAM_WATCH.md` の seen は `45e14f8` のまま（B-1 は個別取り込み）。
