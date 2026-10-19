# B-2 取り込みの要件定義と設計計画（本家 2026-10-18 追従・条件付き項目）

## 0. この文書の位置づけ

- B-1（P48〜P55）完了後の続き。B-2 は「追加型だが、**Mac 側の実装・プライバシー・判定変更のリスク**があるため条件付き」の項目。
- 進み具合の正本は `docs/UPSTREAM_PR_TRACKER.md`、数字・限界は `docs/IMPROVEMENT_PLAN.md`。この文書は要件と設計だけ。
- 進め方は B-1 と同じ（赤→緑、BI と NS の両方で前後比較、独立査読、1 項目 1 コミット、push しない）。
- **B2-1〜B2-7 は仮番号**。管理表へ入れるときに P 番号へ読み替える。
- リョウさんの決定（2026-10-19）: **B2-8・B2-9・B2-10 は見送り**。B2-1〜B2-7 は推奨どおり採用。**悪化が出た項目は見送り**。

## 1. 対象と方針

| 仮 ID | 本家 | 内容 | 変更種別 | Mac 側の追加実装 | 方針 |
| --- | --- | --- | --- | --- | --- |
| B2-1 | #848 (`c07cbb76` `37b160ef`) | ローマ字入力表に拗音・外来音の綴りを足す | Core のみ | なし | **採用**（安全な部分集合） |
| B2-2 | #841 (`d076086f`) | 大文字で打った英字の最後の n を ん と読む（TEN → てん） | Core のみ | なし | **採用** |
| B2-3 | #829 (`af098abb` `bfd137d1`) | iPhone・YouTube を 2 つ含む長い日本語文 | Core のみ | なし | **採用（測定で悪化なら見送り）** |
| B2-4 | #800 (`9368c98d`) | 英語 / 日本語の学習を前の文脈ごとに分ける | Core + 設定 | なし | **採用（既定 OFF）** |
| B2-5 | #799 (`02743917` `60f75b07`) | かな + w を笑い（ねw・さw）にする | Core + 設定 | なし | **採用（既定 OFF）** |
| B2-6 | #807 (`ae9e64bc`) | クリップボードの文字を変換候補に出す | Core + 設定 + Swift/FFI | **あり** | **採用（既定 OFF）** |
| B2-7 | #788 (`335e15d4`) | 予測候補を選んでいる間の Delete で学習から消す | Core + Swift/FFI | **あり** | **採用** |
| B2-8 | #806 (`1d476464` ほか) | #番号で GitHub の Issue/PR タイトルを候補に出す | Core + 通信 + 設定 | あり | **見送り**（通信・プライバシー方針の変更が必要） |
| B2-9 | #842 (`e15165d5`) | 「英字を主に入力」モード | Core + 設定 | あり | **見送り**（Mac の「英数 (Meltype)」と役割が重なる） |
| B2-10 | #738（一連の refactor） | Core の重複整理（KanaChars・AtomicFile・DataFile など） | Core のみ | なし | **見送り**（工数大・今は不要） |

## 2. 共通方針

1. **新しい設定は既定 OFF**。OFF のときは現在の挙動を 1 ミリも変えない。
2. 新設定は `Settings` と `MacSettingsCatalog` の両方に追加し、`MacSettingsTests` の「ラベル・説明の一致」を通す。
3. 判定を変える項目（B2-1・B2-2・B2-3）は、**BI（同梱の英単語表）と NS（NSSpellChecker 相当）の両方**で前後比較する。
4. FFI のコールバックを増やす項目（B2-6・B2-7）は、**ABI の版数を上げ**、`src/Meltype.Mac.Native/Exports.cs` と Swift の `NativeCore`/`Converter` を同時に更新する。
5. プライバシー: 打った文字を外部に送らない、という既存の約束を守る（B2-8 を見送りにした理由の 1 つ）。
6. 1 項目 1 コミット。実装後は必ず独立した観点で査読し、P0/P1 が無いことを確かめてから報告する。
7. 実機確認は、**新しいビルドをインストールしてから**行う（B-1 の教訓）。

## 2.1 悪化時の見送りルール（全項目共通）

各項目で、次を満たせない場合は**その項目を取り消して見送り**にする。

1. 変更前後で `eval-compare` の **broken 0**（回帰なし）。
2. BI と NS の両方で、変えないはずの入力に悪化がない。
3. 全テスト通過。
4. 独立査読で P0/P1 がない。

悪化が出た場合は、無理に条件を足さずに**コミットを取り消し**、管理表に「見送り（悪化の例: …）」として記録する。

---

## 3. 各項目の要件・設計

### 3.1 B2-1 #848 ローマ字入力表の追加

**要件**
- 次の綴りをローマ字として読めるようにする。
  - `kye` → きぇ、`gye` → ぎぇ、`pye` → ぴぇ、`mye` → みぇ
  - `kwi` → くぃ、`kwu` → くぅ、`kwe` → くぇ
  - `gwi` → ぐぃ、`gwu` → ぐぅ、`gwe` → ぐぇ
  - `zwa` → ずぁ、`zwi` → ずぃ、`zwu` → ずぅ、`zwe` → ずぇ
- `fya`・`fyu`・`fyo` は Mac に既にある（P46-12）。変更しない。
- **本家自身が悪化で外した綴りは入れない**: `*yi`（trying・studying）、`hw*`（highway）、`hye`（hyena）、`sw*`（swan・swine）。

**設計**
- `src/Meltype.Core/Detection/RomajiDetector.cs` の `Table` に、本家の安全な部分集合だけを追加する。
- Swift・設定の変更なし。

**テスト**
- 追加した綴りが期待どおりのかなになること。
- 守り: `trying`・`highway`・`hyena`・`swan`・`swine` が今までどおりの判定を変えないこと（BI・NS）。
- 既存の `fya`・`fyu`・`fyo` が変わらないこと。

**リスク**: 低。追加した綴りが英単語の中に出る形だけ、BI/NS で確認する。

### 3.2 B2-2 #841 大文字の最後の n を ん と読む

**要件**
- **すべて大文字**の英字（`TEN`）を変換キーで日本語にすると、最後の `n` を ん と読んで `てん` を候補の先頭に出す。
- 小文字の英文の中の語（`pen`）は、今までどおり日本語にしない。

**設計（本家 `d076086f`）**
- `CompositionController.RomajiCandidates` を、`raw.All(char.IsAsciiLetterUpper)` のときだけ `Partial == "n"` を ん として扱う形に変える。
- 英字の候補（`TEN`）は残る。変換キー（`preferJapanese`）のときだけ日本語が先頭になる。

**テスト**
- `TEN` + Convert → `てん` が候補にあり、先頭が `てん`。
- `pen`・`open`（小文字）は今までどおり。
- 守り: `VPN`・`OPEN` のような大文字の略語で、英字の候補が消えないこと（日本語の候補が増えるだけ）。

**リスク**: 中。大文字の略語の末尾 N に日本語候補が増える。英字の候補は残るため、確定は利用者が選ぶ。BI・NS と既存テストで確認する。

### 3.3 B2-3 #829 iPhone・YouTube を含む長い日本語文

**要件**
- 長い日本語のローマ字文に、大文字で始まる英字の語（iPhone・YouTube）が 2 つ入っていても、文全体を日本語として扱う。
  - 例: `korewoiPhonedesatueisi,YouTubeniappuro-dosiyou` → `これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう`

**設計（本家 `af098abb`）**
- `CompositionDetector.StartsWithJapaneseReading` の条件を、`head.Length >= 7` または従来の「助詞で終わる」に変える。
- 条件は従来どおり「head が全部小文字・ローマ字として最後まで読める・同梱の英単語でない」。

**テスト**
- 本家の長文テスト。
- 守り（重要）: 7 文字以上の小文字 + 大文字の形で、**実在の長い英単語**（`universityGitHub`・`internationaliPhone` など）が日本語に倒れないこと。BI・NS の両方。
- 既存の `iPhone`・`YouTube` 単独、`kyouhaiPhone` などが変わらないこと。

**リスク**: 中〜高。7 文字以上の「ローマ字として読める・辞書に無い」英単語の頭が日本語に倒れ得る。悪化が出たら `head.Length >= 7` の条件を絞る（助詞で終わる場合に限定する等）。それでも悪化するなら**見送り**。

### 3.4 B2-4 #800 学習を前の文脈ごとに分ける

**要件**
- 設定 `ContextualLearning`（既定 **OFF**）。
- ON のとき、英字/かなに直した語の学習を「直前が英語だったか日本語だったか」ごとに分ける。
- ON にする前に覚えた学習は、どちらの文脈でも使う。

**設計（本家 `9368c98d`）**
- `LanguageMemory` に `ContextSensitive`・`Context`・`ContextSuffix`（`@e`/`@j`）を足し、`Get`/`Remember` のキーに付ける。古いキー（サフィックスなし）はフォールバックとして残す。
- `CompositionController.UpdateLearningContext` を `BeginComposition` と `RequestSurroundingText` の後に呼ぶ。
- `CompositionOptions.ContextualLearning`、`MeltypeSession` の配線、`Settings`、`MacSettingsCatalog`。

**テスト**
- 同じ語を英語の後ろで英字、日本語の後ろでかなに確定 → 次にそれぞれの文脈で覚えた方が優先される。
- OFF のときは従来どおり（サフィックスなし）。
- 古い `languages.json`（サフィックスなし）がどちらの文脈でも使える。
- 保存・読み直し・全消去・版数。

**リスク**: 低〜中。`languages.json` のキーが増えるだけで、打った文字は保存しない（`@e`/`@j` のみ）。既定 OFF。

### 3.5 B2-5 #799 かな + w を笑いにする

**要件**
- 設定 `LaughterAfterKana`（既定 **OFF**）。
- ON のとき、1 文字のかなの後ろの `w` を英単語（new・saw）にせず、笑いの `w` にする（`なるほどnew` → `なるほどねw`）。
- 前が英語の文（`I saw`）のときは、英単語のまま。
- `new`・`saw` を英単語として打つ人は OFF のまま。

**設計（本家 `02743917`・`60f75b07`）**
- `CompositionText.SplitLaughter`・`KeepLaughterAt`・`SegmentsKey` の変更（本家の修正版を使用）。
- `CompositionOptions.LaughterAfterKana`、`MeltypeSession`、`Settings`、`MacSettingsCatalog`。

**テスト**
- `naruhodonew` → `なるほどねw`（ON）。
- `I saw` は英単語のまま。
- **既知の悪化**: `おnewの新品` が壊れる（精査 D6）。ON のときの限界としてテストに固定し、KNOWN_LIMITS に記録する。
- OFF のときは従来どおり。

**リスク**: 中（ON のときのみ）。既定 OFF で回避。

### 3.6 B2-6 #807 クリップボードの文字を変換候補に出す

**要件**
- 設定 `ClipboardCandidates`（既定 **OFF**）。
- ON のとき、`くりっぷぼーど`・`ぺーすと` を変換すると、クリップボードの **1 行**（200 文字まで、制御文字なし）を候補に出す。
- **変換した瞬間に 1 回だけ読み**、監視・保存・送信はしない。
- パスワード管理ソフトなど「履歴に残さない」印のある内容は読まない。

**設計**
- Core: `CompositionOptions.ClipboardCandidates`、`ICompositionHost.ClipboardText`（または `MeltypeSession.CreateDefault` の `Func<string?> clipboard`）、`TimeCandidates` に `ClipboardReadings` と `ClipboardLine` を追加。
- Mac 側:
  - Swift の `NativeCore`/`Exports.Init` に **clipboard コールバック**を追加（FFI ABI を上げる）。
  - `NSPasteboard.general` を読み、`org.nspasteboard.ConcealedType`・`org.nspasteboard.TransientType` が付いているときは `null` を返す（Windows の `ExcludeClipboardContentFromMonitorProcessing` に相当）。
  - `MacSettingsCatalog` に追加。Windows 向けの「Windows のみ」を Mac の説明では外す（`MacDescriptions` を使う）。
- Core の `ClipboardLine` が最終検証（1 行・200 文字・制御文字なし・前後の空白を除く）。

**テスト**
- `ClipboardLine`: 空・200 文字・201 文字・改行・制御文字・前後の空白。
- 統合: 偽のクリップボードで `くりっぷぼーど` + Space → 候補。設定 OFF では読まない。
- Swift の自己テスト（`NSPasteboard` の偽物）と実機。

**リスク**: 中。プライバシー（読むのは変換時 1 回だけ）、FFI の ABI 変更、Swift の実装。既定 OFF。

### 3.7 B2-7 #788 予測候補を選んでいる間の Delete で学習から消す

**要件**
- 予測候補を選んでいる間（`_predicting` かつ `_predictionIndex >= 0`）に Delete を押すと、その語句を学習から消し、予測の一覧からも消える。
- **ユーザー辞書・同梱の専門用語集は消さない**（登録した語を Delete で消すのは危険なため）。消すのは「学習した予測」だけ。

**設計**
- Core:
  - `CompositionController.HandlePredictionKey` に `case 0x2E /* Delete */` を追加。`ConversionHistory.Forget(text)` を呼び、予測のキャッシュを捨てて一覧を作り直す。
  - `ConversionHistory` に `Forget(string text)`（同じ語をすべて削除して保存）を追加。
  - `CompositionOptions.ForgetPrediction`（`Action<string>?`）を追加。azooKey の予測を消すため、Swift 側で `forgetMemory` 相当を呼ぶ。
- Mac 側: `Exports.Init` に forget コールバックを追加（ABI を上げる）。Swift の `Converter` が azooKey の `forgetMemory` を呼ぶ。
- 予測源の扱い:
  - `History`（Core の学習）: 消す。
  - `Predictions`（azooKey）: 消す（コールバック）。
  - `UserDictionary`・`PredictTerm`/`PredictTerms`: **消さない**（限界として記録）。

**テスト**
- `ConversionHistory.Forget` の単体。
- 予測を Tab で選び、Delete → 一覧から消える。ユーザー辞書の語は残る。
- エンジンの forget コールバックが呼ばれること（偽のコールバック）。

**リスク**: 中。予測源の違い（Mac は `Predictor` ではなく `History` + azooKey）、FFI、Swift。ユーザー辞書を消さない方針を明記する。

### 3.8 B2-8 #806 #番号で GitHub のタイトル（見送り）

**見送りの理由（リョウさんの決定 2026-10-19）**
- `#123` を変換するたびに、リポジトリ名と番号を **api.github.com に送る**。Mac 版の「打った内容をネットワークに送らない」という約束を変える必要がある。
- IME は launchd 起動なので、シェルの `MELTYPE_GITHUB_TOKEN` が見えない可能性がある。非公開リポジトリの扱いが不安定。
- NativeAOT に `System.Net.Http` を入れることによる dylib のサイズ・起動時間の影響も未計測。

**将来やる場合の設計（記録）**
- 本家 `1d476464`・`5820a96b`・`3dacadd4`・#871 の形（`GitHubIssueTitles`、`#数字` の分岐、設定、セッションキャッシュ、既定 OFF、公開リポジトリのみ）。
- README のプライバシー節の更新と、リョウさんの明示の承認が前提。

### 3.9 B2-9 #842 英字を主に入力（見送り）

**見送りの理由**
- Mac には既に **「英数 (Meltype)」入力モード**（`Direct`）がある。Core の `EnglishFirstInput` は `DisplayMode.HalfWidthAlphanumeric` を強制し、Convert/Down で日本語にする。Mac のモードと役割が重なり、二重の状態管理になる。
- Mac の「英数 (Meltype)」で同じ目的を満たせるかを先に実機で確認する。

**将来やる場合の設計（記録）**
- 本家 `e15165d5` の形。Mac の入力モードとの関係を決めてから。

### 3.10 B2-10 #738 Core の重複整理（見送り）

**見送りの理由**
- 動作変更は無いが、Mac の Core は本家と分岐しており、`KanaChars`・`AtomicFile`・`DataFile`・`DictionarySource`・`Corrections`・`WordJudge` の手移植は工数が大きく、他の変更と競合しやすい。
- 今の B-2 の目的（機能の取り込み）には不要。必要になったファイルだけ、将来個別に移植する。

---

## 4. テスト計画

| 種別 | 内容 |
| --- | --- |
| 赤→緑 | B2-1・B2-2・B2-3・B2-4・B2-5・B2-6・B2-7 は先にテストを足し、修正前に赤を確認 |
| 回帰 | 全テスト（現在 905 件 + 追加） |
| 品質 | `--eval` の前後が同じ |
| 回帰ゲート | `tools/eval-compare.sh`（base = B-2 開始前の `main`）で **broken 0** |
| 前後比較 | B2-1〜B2-3 は BI と NS の両方。B2-4〜B2-7 は設定 OFF の不変を確認 |
| FFI | B2-6・B2-7 は ABI の版数、`Exports.cs`、Swift の `NativeCore`/`Converter`、AOT チェッカー |
| 実機 | 各項目の「実機で確かめる入力」を管理表に残す |

**追加するテストファイル（予定）**
- `RomajiSpellingTests.cs`（B2-1）
- `UpperFinalNTests.cs`（B2-2）
- `LongJapaneseWithCamelCaseTests.cs`（B2-3）
- `ContextualLearningTests.cs`（B2-4）
- `LaughterAfterKanaTests.cs`（B2-5）
- `ClipboardCandidatesTests.cs`（B2-6）
- `PredictionDeleteTests.cs`（B2-7）

## 5. リスクと対策

| リスク | 対策 |
| --- | --- |
| B2-3 の 7 文字条件で長い英単語が日本語に倒れる | BI・NS の前後比較を厚くし、悪化が出たら条件を絞る。それでも悪化するなら見送り |
| B2-2 で大文字の略語に日本語候補が増える | 英字の候補が残ることをテストで固定。Convert のときだけ先頭にする |
| B2-5 の `おnewの新品` | 既定 OFF。ON のときの限界として KNOWN_LIMITS に記録 |
| B2-4 の学習ファイル形式 | サフィックスなしの古いキーをフォールバック。全消去・保存のテスト |
| B2-6 のプライバシー | 変換時 1 回だけ・保存/送信しない・Concealed/Transient を読まない |
| B2-7 でユーザー辞書を消す | 消すのは History と azooKey の予測だけ。ユーザー辞書・専門用語集は消さない |
| FFI の ABI 変更 | 版数を上げ、Swift と同時に更新。AOT チェッカーで確認 |

## 6. 作業順序とコミット分割

| 順 | 項目 | 理由 |
| --- | --- | --- |
| 1 | B2-1 #848 | Core のみ・最も安全 |
| 2 | B2-2 #841 | Core のみ |
| 3 | B2-4 #800 | 既定 OFF |
| 4 | B2-5 #799 | 既定 OFF |
| 5 | B2-3 #829 | 測定を厚くする |
| 6 | B2-6 #807 | FFI + Swift |
| 7 | B2-7 #788 | FFI + Swift |

各コミット: テスト → 赤 → 実装 → 緑 → 全テスト → `--eval` → `eval-compare` broken 0 → AOT/ビルド/自己テスト → 独立査読 → コミット。

## 7. 完了条件（DoD）

- 各項目のテストが赤→緑
- 全テスト通過（905 + 追加）
- `--eval` 変化なし
- `eval-compare` broken 0
- AOT チェッカー PASS、Mac ビルド成功、辞書アプリ自己テスト PASS
- 独立査読で P0/P1 なし
- 管理表・計画書に記録
- 実機確認（新ビルドをインストールしてから）
- push しない

## 8. 実機で確かめること

詳細な手順と記入欄は [B2_DEVICE_CHECK.md](B2_DEVICE_CHECK.md) にあります。

| 項目 | 入力・操作 | 期待 |
| --- | --- | --- |
| B2-1 | `kye`・`gwi`・`zwa` など | きぇ・ぐぃ・ずぁ など |
| B2-1 | `trying`・`highway`・`swan` | 今までどおり（悪化なし） |
| B2-2 | `TEN` + 変換キー | `てん` が先頭 |
| B2-2 | `pen`・`open` | 今までどおり |
| B2-3 | `korewoiPhonedesatueisi,YouTubeniappuro-dosiyou` | これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう |
| B2-4 | 同じ語を英語の後ろ / 日本語の後ろで確定 | 文脈ごとに別々に覚える（ON） |
| B2-5 | `naruhodonew` | なるほどねw（ON） |
| B2-5 | `おnewの新品` | ON のときの既知の限界 |
| B2-6 | 1 行をコピーして `くりっぷぼーど` + Space | その行が候補（ON） |
| B2-6 | パスワード管理ソフトの内容 | 候補に出ない |
| B2-7 | 予測を Tab で選び Delete | 一覧から消える。ユーザー辞書の語は残る |

## 9. 判断待ち（解決済み）

1. B2-8 #806: **見送り**（通信・プライバシー方針の変更が必要）。
2. B2-9 #842: **見送り**（Mac の「英数 (Meltype)」と役割が重なる）。
3. B2-10 #738: **見送り**（工数大・今は不要）。
4. B2-6 #807: **採用**（既定 OFF・変換時 1 回）。
5. B2-7 #788: **採用**（History + azooKey のみ。ユーザー辞書は消さない）。
6. B2-3 #829: **採用（測定で悪化なら見送り）**。
7. 番号: B2-1〜B2-7 を管理表に入れるときに確定する。

## 10. 実装結果 (2026-10-19)

| 項目 | 結果 | コミット | 備考 |
| --- | --- | --- | --- |
| B2-1 #848 | 完了 | 55cdfc2 | `kye`・`gye`・`pye`・`mye`、`kwi/kwu/kwe`、`gwi/gwu/gwe`、`zwa/zwi/zwu/zwe` を CompositionTable に追加。`*yi`・`hw*`・`hye`・`sw*` は入れない |
| B2-2 #841 | 完了 | 459b11a | 大文字だけの `TEN` を Shift+Space で `てん`。小文字の `pen` は今までどおり |
| B2-3 #829 | 済（コード変更なし） | fe1c0c9 | Mac はすでに `これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう`。長文と guard をテストで固定 |
| B2-4 #800 | 完了（画面の文脈ラベルは未対応） | 958dccb | 設定 OFF 既定。`@e`/`@j` の保存・読み直し・Undo・一覧の印外し。`Learned` に `Context` を追加。画面は同じ語が 2 行出る |
| B2-5 #799 | 完了 | c1b94ba | 設定 OFF 既定。`naruhodonew` → `なるほどねw`。英文の中の `saw` はそのまま。`おnew` の限界は ON のときだけ |
| B2-6 #807 | 完了 | 2d3eaa3 | 設定 OFF 既定。ABI 12。Concealed/Transient は読まない。Core の `ClipboardLine` が 1 行・200 文字・制御文字を検証 |
| B2-7 #788 | 完了 | 5eb9422 | ABI 13。Core の変換履歴と azooKey の予測から消す。ユーザー辞書・専門用語集は消さない。未選択の Delete は安全 |
| B2-8 #806 | 見送り | - | 通信・プライバシー方針の変更が必要 |
| B2-9 #842 | 見送り | - | Mac の「英数 (Meltype)」と役割が重なる |
| B2-10 #738 | 見送り | - | 工数大・今は不要 |

**検証**
- Core のテスト: 905 → **927 件**（B2-1 2、B2-2 2、B2-3 2、B2-4 6、B2-5 4、B2-6 3、B2-7 3）。
- `--eval`: 1269/1271 のまま（変更前と同じ）。
- AOT チェッカー: PASS（FFI 版数を 13 に更新）。
- 辞書アプリの self-test: 213 PASS。`mac/build.sh` で build/install、インストール済み dylib のハッシュ一致。
- `eval-compare`（基準 `51512b7`）: **broken 0 / fixed 0 / added 0 / removed 0**。合計 278/310 で前後同じ（回帰ゲート PASS）。
- 実機確認: 未。§8 の入力で確認する（B2-2 は Shift+Space、B2-1 は CompositionTable のみ）。

**残っている限界・後続**
- B2-4: 画面（「学習した語」）の文脈ラベルは未対応。同じ語が 2 行出る。`Learned` の `Context` は JSON にある。
- B2-6: Swift 側の Concealed/Transient の自己テストは無い。実機でパスワード管理ソフトの内容が候補に出ないことを確認する。
- B2-7: ユーザー辞書・同梱の専門用語集の予測は消さない（Delete しても一覧に残ることがある）。
- B2-5: `おnewの新品` は ON のとき壊れる（既知の限界）。



