---
title: サロゲートペアの漢字 (𠮷・𩸽) だけの文節を「記号だけ」とみなし、変換・確定がひらがなになる
status: approved   # 2026-11-22 の人の指示「案 A で approved にして、PR を作って」(「人の判断」の 1 の決定) / 実装 2026-11-25 (ブランチ fix/surrogate-kanji-clause。実機の確認待ちで approved のまま) / 棚卸し 2026-11-25: 実装は main に無かった (要件定義書のみ。IsSymbolOnly は UTF-16 のまま・サロゲートのテスト無し)
source: PR #29 の要件 017 (前の文を Zenzai に渡す。ブランチ ccr-0fc8aeab-sra4z7) の作業中に見つけた不具合 (2026-11-22)
scope: core
---

# 024 サロゲートペアの漢字 (𠮷・𩸽) だけの文節を「記号だけ」とみなし、変換・確定がひらがなになる

> 番号の注: ここでの「要件 017」は PR #29 の `017-zenzai-previous-sentence.md` (前の文を Zenzai に渡す)、「要件 015」は PR #29 の `015-boundary-sweep.md` (境界例の一括比較) のこと。ブランチ `req/017-reto-ga` (長音の語 + 助詞)・`req/015-ci-english` の同じ番号とは別。
> 024 は、どのブランチの `docs/requirements/` にも無い次の番号 (2026-11-22 に origin の全ブランチで確かめた。最大は 023)。

> **要約**
> - 何が起きるか: 変換エンジンが返した文節のどれかが BMP の外の漢字だけ (𠮷・𩸽 など) だと、その文節はひらがなが先頭になり、Enter でひらがなが確定する。`IsSymbolOnly` が UTF-16 の 1 単位ずつ見るため。Core の偽の変換エンジンで再現した。
> - 実機で起きるか: **未確認**。azooKey (と Zenzai) がこういう文節を最初に返すかで決まる。`yoshi`・`hokke`・`yoshisan` + Space の並びを実機で見ると分かる (Mac 実機チェックリストの 1 つ目)。
> - 決まったこと: 2026-11-22 の人の指示で、案 A で approved (末尾の「人の判断」)。承認の前の実機の確認はしていない。

## 背景 (誰が、何に困っているか)
- 変換エンジンが返した文節のどれかが **BMP の外の漢字だけ** だと、Meltype はその文節を「記号だけ」とみなし、その文節のひらがなを先頭の候補にする。読み全体が 1 文節のとき (よし → 𠮷) も、複数の文節の 1 つのとき (よしさん → 𠮷|さん) も起きる。BMP の外の漢字は UTF-16 で 2 単位 (サロゲートペア) になる字で、𠮷 (U+20BB7)・𩸽 (U+29E3D) など CJK 統合漢字拡張 B 以降の字。Space で変換しても先頭はひらがなで、そのまま Enter するとひらがなが確定する。
- 困る人 (推定): 人名・店名の 𠮷 (よし)・魚の 𩸽 (ほっけ) などを変換したい人。最初の 1 回は 2 番目の候補を選び直すことになる (選んで確定すれば、Core の学習で次からは先頭になる: 再現 2 の E2。偽の変換エンジンでの確認で、実機は未確認)。
- 見つけた経緯: 2026-11-22、要件 017 の作業中。偽の変換エンジンで よし → 𠮷 を返すようにして `yoshi` + Space + Enter とすると、𠮷 ではなく よし が確定した。Zenzai の設定 (PR #29 では Core の広い文脈 `WideConversionContext`) の ON・OFF に関係なく起きる (再現 3)。
- **実際の azooKey が よし・ほっけ に 𠮷・𩸽 だけの文節を返すかは未確認** (Mac でしか確かめられない)。返さなければ、利用者の目に見える不具合にはならない。
- 同じ系統 (UTF-16 の 1 単位ずつ扱う) として、変換エンジンに渡す文脈を作る 2 か所 (`ContextOf` の末尾 10 単位・自分の確定の記録 `_lastCommitText` の末尾 20 単位) も、サロゲートペアの途中で切る。切ると文脈が下位サロゲート単独で始まり、Swift に渡るときは U+FFFD (�) に置き換わる (下の「文脈が Mac で使われるところ」)。Mac での効き目は狭く、PR #29 の後はさらに狭まる (人の判断 1)。

## 今の動き (既に分かっていること)
### 原因の場所 (main `5544133`)
- `src/Meltype.Core/Composition/CompositionController.cs` 2513 行の `IsSymbolOnly`:
  ```csharp
  private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);
  ```
  `text.Any(...)` は UTF-16 の 1 単位 (char) ずつ見る。サロゲートの単位の分類は `Surrogate` で、`char.IsLetterOrDigit` は false を返す (再現 2 の F)。そのため 𠮷 だけの文字列は「文字が 1 つも無い = 記号だけ」になる。
- 呼び出し元は 2351 行 (`ConvertWithEngineOnce`)。文節ごとに呼ぶ。2350 行のコメントは「変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。」。記号だけで、読みが全部ひらがな (ぁ〜ゖ) なら、ひらがなを先頭にする (`Prefer`)。
- この規則より先に、計算の記号・手がかり辞書 (`ContextRules`)・学習 (`History`) が先頭を決める (2349 行)。そのため 𠮷 を 1 回選んで確定すると、次からは学習で 𠮷 が先頭になる (E2)。ユーザー辞書に登録した語も、この規則の影響を受けない (変換エンジンが よし → 𠮷 を返し、ユーザー辞書にも よし → 𠮷 を登録した形で、𠮷 が先頭・Enter で 𠮷: 再現 4 の E1b)。
- 同じ式は、Mac 版を追加したコミット `c3c247f` (2026-10-07。今の main の祖先ではない古い履歴) にもある。本家 (yksr-melt/Meltype) にあるか・直したかは未確認 (クラウドから届かない)。
- この規則 (から → ～ をひらがなの後ろ) を守るテストは今は無い。`src/Meltype.Core.Tests` で、変換エンジンが `～` を返す偽物と `IsSymbolOnly` の名前を探して 0 件。写しの木で `IsSymbolOnly` を一時的に「いつも false」(規則を外す) にしても、Core のテストは `1266/1266 passed` だった (2026-11-22。確かめた後に戻した)。
- Zenzai の設定は、main の Core の変換の流れに入っていない (`src/Meltype.Core` で Zenzai が出てくるのは `Config/Settings.cs`・`Config/MacSettingsCatalog.cs` だけ)。

### 同じ系統の 2 か所 (サロゲートを見ずに切る)
- `ContextOf` (2501〜2507 行): 変換エンジンに渡す文脈 (同じ文の最大 10 単位)。両端の空白を除いてから `text.Length > 10 ? text[^10..] : text` で切る。使うところは 3 つ: キャレットの前の文字からの文脈 (`ConversionContext()`。2499 行)、長い読み (50 字超。`ConvertChunkLength`) を区切って変換するときの 2 つ目以降の区切りの文脈 (前の区切りの変換結果から作る。2233 行)、確定した文節を変換エンジンに覚えさせる `LearnConversion` (3877 行)。
- `_lastCommitText` (自分の確定の記録。最大 20 単位): 5 か所で `[^20..]` で切る。`CommitPart` (598 行。部分確定)・`CommitFirstSound` (3131 行。1 音確定)・`SetOwnContext` (3445 行)・`NoteBreakKeyPassedToApp` (3527 行。アプリに渡した Enter・Tab・Space を記録に足す)・`CommitText` (3976 行。確定)。この 5 か所の複写は `docs/CODE_REVIEW_2026-10.md` の R2 (確定の口を 1 つにするリファクタの候補) にも挙がっている。確定の直後 (1.5 秒以内。`OwnCommitTrustMs`) と、入力欄の文字が取れないときは、この記録がキャレットの前の文字になる (`BeginComposition` 1388〜1389 行)。
  - main では `ContextOf` が両端の空白を除いてから末尾 10 単位で切るので、記録の先頭が文脈に入るのは、記録の末尾に空白・タブなどの空白文字が 10 個以上続くときだけ (再現 2 の D1)。実機でこの形になる条件はさらに狭い: Swift は変換ボックスが出ていない間、キーごとに入力欄の前後の文字を渡し (`mac/Sources/MeltypeIME/InputController.swift` 223 行)、アプリに渡した Space では `_lastCommitTime` が進まない (進むのは部分確定・1 音確定・入力欄の書き換え・確定の 599・3132・3447・3977 行と、取り消しで戻す 3425 行)。そのため、最後の確定から 1.5 秒以内に空白 10 個と次の入力を済ませたときか、入力欄の文字を返さないアプリのときだけ。
  - PR #29 の広い文脈 (`WideContextOf`。末尾 20 単位) では記録の先頭がそのまま入るので、PR #29 は受け取る側で「先頭の下位サロゲート単独を、切ったかどうかに関係なく落とす」処理を入れてある (コミット `8bc34e4`。再現 3)。
- 文脈が Mac で使われるところ: Core から Swift へは UTF-8 で渡す (`src/Meltype.Mac.Native/Exports.cs` 1255〜1262 行の `ToUtf8`。`Encoding.UTF8.GetBytes` の char* の形)。単独のサロゲートは U+FFFD (EF BF BD) に置き換わる (再現 2 の G は string の形、再現 4 の G2 は `ToUtf8` と同じ char* の形で確かめた)。Swift は文脈を Zenzai を使うときだけ左文脈 (`leftSideContext`) に使う (`mac/Sources/MeltypeIME/Converter.swift` 163〜166・174〜176 行)。学習 (`learn(context:clauses:)`。296 行〜) は文脈を使っていない。
- PR #29 が入った後の効き目 (コードを読んだ見立て): PR #29 の `ConversionContext()` は、区切りの文脈 → 広い文脈 → `ContextOf` の順に選ぶ。広い文脈を使うかは `MeltypeSession.WideContextProvider` が決める (Zenzai の設定が ON で、変換エンジンが Mozc でないとき。Mac は Zenzai の設定どおり)。Swift は文脈を Zenzai を使うときだけ使うので、Mac で `ContextOf` の文脈が Zenzai に届くのは、長い読みの 2 つ目以降の区切りの文脈だけになる。記録の先頭は、Zenzai ON では `WideContextOf` が落とし、OFF では Swift が文脈を使わないので、Mac では効かなくなる。
- サロゲートペアの途中で切らないお手本 (既にあるもの。落とし方が 2 通りある):
  - 自分で切ったときだけ、先頭の下位サロゲートを落とす: `src/Meltype.Core/Composition/DictionaryManagement.cs` の `Tail` (289〜295 行。293 行)。先頭から n 単位を取るときに、末尾に上位サロゲートを残さないもの: 同じファイルの `Head` (298〜302 行)、`TermDomains.User.cs` 172・341 行、`LanguageMemory.cs` 469 行。
  - 切ったかどうかに関係なく、先頭の下位サロゲート単独を落とす: PR #29 の `WideContextOf`。
  - コードポイントで分類を見るお手本: `UserDictionary.cs` 96 行 (`Rune.GetUnicodeCategory`)。
- 関連する過去の記録: `docs/IMPROVEMENT_PLAN.md` の P47 の経緯 (2750 行) に、古いブランチ `fix/input-fixes-1.1.3` のコミット `e8fbc64` の「ほかの直し」として「サロゲートペア」が挙がっている (P47 では扱わなかった)。同じ計画書の 2778 行は、そのブランチの「main に入っていない修正」を 2 つ (tabde・そうについて) としている。サロゲートペアの直しが何を直したものか・main に入っているかは分からない (`e8fbc64` はこの写し (クラウド) に無く未確認)。`docs/CODE_REVIEW_2026-10.md` の L1a-5・L1b-6・L7-4 は、ほかの UTF-16 単位の扱い (この要件の範囲外)。

### 再現 1: `--repro` と `--segments` (main `5544133`、2026-11-22、クラウド Linux)
既定の偽の変換エンジン (`CompositionTests.FakeConverter`) は よし・ほっけ を知らない (漢字にしない) ので、`--repro` ではこの不具合は出ない。判定がかな (日本語) になることだけを確かめた。写しの木 (`git worktree add --detach <scratchpad>/wt-main HEAD`) で `dotnet build -c Release src/Meltype.Core.Tests` の後、`--no-build` を付けて実行した。
```
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- --repro yoshi enter
{"typed":"yoshi","last":"enter","showing":"よし","committed":"よし","spellChecker":true,"composing":null}
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- --repro yoshi space
{"typed":"yoshi","last":"space","showing":"よし","committed":"","spellChecker":true,"composing":"よし"}
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- --repro yoshinoya space
{"typed":"yoshinoya","last":"space","showing":"よしのや","committed":"","spellChecker":true,"composing":"よしのや"}
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- --repro hokke space
{"typed":"hokke","last":"space","showing":"ほっけ","committed":"","spellChecker":true,"composing":"ほっけ"}
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- --segments yoshi
units: yo=よ shi=し
segments: JA:yoshi
```

### 再現 2: 写しの木の使い捨てのテスト (main `5544133`、2026-11-22)
写しの木にだけ `src/Meltype.Core.Tests/Scratch024Tests.cs` を置いて実行し、終わったら消した (コミットしていない。メインの木は変えていない)。偽の変換エンジンは「読み → 1 文節」を返す小さなもの (抜粋。実物は渡された文脈も記録する):
```csharp
private sealed class MapConverter(Dictionary<string, string> map) : IKanjiConverter
{
    public string? Convert(string hiragana) => map.TryGetValue(hiragana, out var t) ? t : null;
    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        map.TryGetValue(hiragana, out var t) ? [new ConversionClause(hiragana, t)] : null;
}
// A1: new CompositionTests.Keyboard(converter: new MapConverter(new() { ["よし"] = "𠮷" })) に "yoshi" → " " → "\n"
// D1: new MeltypeSession(CompositionTests.Detector, converter, new CompositionOptions(), () => new Settings()) に HandleKey で 1 キーずつ (入力欄の文字は渡さない)
```
A〜D は「直したあとの期待」で書いたテストで、FAIL は今の不具合を示す。B・E は今のまま正しい動き。候補は全部ではなく、出力の先頭 6 つまで (E1 は 4 つ、E2 は 3 つ)。

| 記号 | 変換エンジンの答え | 打鍵 | Space の文節と候補 | Enter の確定 | 判定 |
|---|---|---|---|---|---|
| A1 | よし → 𠮷 (U+20BB7) | `yoshi` + Space + Enter | 文節 `よし`、候補 `よし,𠮷,ヨシ,ﾖｼ,…` | `よし` | **不具合** |
| A2 | ほっけ → 𩸽 (U+29E3D) | `hokke` + Space + Enter | 文節 `ほっけ`、候補 `ほっけ,𩸽,ホッケ,…` | `ほっけ` | **不具合** |
| A3 | よしよし → 𠮷𠮷 | `yoshiyoshi` + Space + Enter | 文節 `よしよし`、候補 `よしよし,𠮷𠮷,…` | `よしよし` | **不具合** |
| A4 | よしよし → 𠮷𠮷 (ライブ変換 ON) | `yoshiyoshi` + Enter | (表示 `よしよし`) | `よしよし` | **不具合** |
| B1 | よし → 吉 (U+5409。BMP。対照) | `yoshi` + Space + Enter | 文節 `吉`、候補 `吉,よし,…` | `吉` | 正しい |
| B2 | よしのや → 𠮷野家 (BMP の字を含む) | `yoshinoya` + Space + Enter | 文節 `𠮷野家`、候補 `𠮷野家,よしのや,…` | `𠮷野家` | 正しい |
| B3 | から → ～ (BMP の記号) | `kara` + Space + Enter | 文節 `から`、候補 `から,～,空,殻,…` | `から` | 今の決まりどおり |
| B4 | にこにこ → 😊 (BMP の外の記号) | `nikoniko` + Space + Enter | 文節 `にこにこ`、候補 `にこにこ,😊,…` | `にこにこ` | 今の決まりどおり |
| E1 | 既定の偽物 + ユーザー辞書に よし → 𠮷 | `yoshi` + Space + Enter | 候補 `𠮷,よし,ヨシ,ﾖｼ` | `𠮷` | 既定の偽物は よし を漢字にしないので、規則が動かない形。裏付けにならない (再現 4 の E1b で確かめ直した) |
| E2 | よし → 𠮷 | `yoshi` + Space 2 回 (2 番目の候補) + Enter、続けて `yoshi` + Space + Enter | 1 回目の候補 `よし,𠮷,ヨシ`、2 回目の文節 `𠮷` | `𠮷`、2 回目も `𠮷` | 学習で 2 回目から先頭 |

| 記号 | 前の文字 | 打鍵 | 変換エンジンに渡った文脈 | 判定 |
|---|---|---|---|---|
| C1 | キャレットの前 `𠮷あいうえおかきくけ` (11 単位) | `kawa` + Space | 先頭 U+DFB7 U+3042、長さ 10 (𠮷 の下位サロゲート単独 + `あいうえおかきくけ`) | **不具合** |
| C2 | キャレットの前 `𠮷あいうえおかき` (9 単位) | `kawa` + Space | `𠮷あいうえおかき` (切らない) | 正しい |
| D1 | Mac の経路で `yoshinoya` + Space + Enter (𠮷野家)、`aiueokaki` + Enter (あいうえおかき)、何も入力していないときの Space 10 回 (どれもアプリに渡った) | `kawa` + Space | 先頭 U+DFB7 U+91CE、長さ 10 (下位サロゲート単独 + `野家あいうえおかき`) | **不具合** |

出力 (そのまま。D1 の `�` は、コンソールが単独の下位サロゲートを U+FFFD に置き換えて出したもの。中身は U+DFB7):
```
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- Scratch024
  ユーザー辞書に よし → 𠮷 を登録 (Add の戻り値: null = 登録できた): yoshi + Space 候補 [𠮷,よし,ヨシ,ﾖｼ] / + Enter: [𠮷]
  PASS  Scratch024MoreTests.E1_UserDictionary_Yoshi
  1 回目: 候補 [よし,𠮷,ヨシ] → Space 2 回目 [𠮷] → Enter [𠮷] / 2 回目の yoshi + Space [𠮷] → Enter 後の入力欄 [𠮷𠮷]
  PASS  Scratch024MoreTests.E2_ChooseOnce_ThenAgain
  実行環境 .NET 10.0.15
  char.IsLetterOrDigit('\uD842') = False (分類 Surrogate)
  char.IsLetterOrDigit('\uDFB7') = False (分類 Surrogate)
  "𠮷".Any(char.IsLetterOrDigit) = False / "吉".Any(char.IsLetterOrDigit) = True
  char.IsLetterOrDigit("𠮷", 0) = True (string と位置で聞く形)
  Rune U+20BB7 [𠮷] IsLetterOrDigit = True (分類 OtherLetter)
  Rune U+29E3D [𩸽] IsLetterOrDigit = True (分類 OtherLetter)
  Rune U+1F60A [😊] IsLetterOrDigit = False (分類 OtherSymbol)
  Rune U+FF5E [～] IsLetterOrDigit = False (分類 MathSymbol)
  Rune U+5409 [吉] IsLetterOrDigit = True (分類 OtherLetter)
  Rune U+1D7D8 [𝟘] IsLetterOrDigit = True (分類 DecimalDigitNumber)
  PASS  Scratch024MoreTests.F_RuntimeFacts
  yoshi の表示 [よし] / + Space: 文節 [よし] 候補 [よし,𠮷,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter: [よし]
  FAIL  Scratch024Tests.A1_Yoshi_Surrogate
        よし → 𠮷 (U+20BB7) のとき 期待値: 𠮷 / 実際: よし
  hokke の表示 [ほっけ] / + Space: 文節 [ほっけ] 候補 [ほっけ,𩸽,ホッケ,ﾎｯｹ,hokke,ｈｏｋｋｅ] / + Enter: [ほっけ]
  FAIL  Scratch024Tests.A2_Hokke_Surrogate
        ほっけ → 𩸽 (U+29E3D) のとき 期待値: 𩸽 / 実際: ほっけ
  yoshiyoshi の表示 [よしよし] / + Space: 文節 [よしよし] 候補 [よしよし,𠮷𠮷,ヨシヨシ,ﾖｼﾖｼ,yoshiyoshi,ｙｏｓｈｉｙｏｓｈｉ] / + Enter: [よしよし]
  FAIL  Scratch024Tests.A3_Yoshiyoshi_TwoSurrogates
        よしよし → 𠮷𠮷 のとき 期待値: 𠮷𠮷 / 実際: よしよし
  ライブ変換 ON: yoshiyoshi の表示 [よしよし] / + Enter: [よしよし]
  FAIL  Scratch024Tests.A4_Yoshiyoshi_Live
        ライブ変換 ON・Enter で 期待値: 𠮷𠮷 / 実際: よしよし
  yoshi の表示 [よし] / + Space: 文節 [吉] 候補 [吉,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter: [吉]
  PASS  Scratch024Tests.B1_Yoshi_BmpKanji_Control
  yoshinoya の表示 [よしのや] / + Space: 文節 [𠮷野家] 候補 [𠮷野家,よしのや,ヨシノヤ,ﾖｼﾉﾔ,yoshinoya,ｙｏｓｈｉｎｏｙａ] / + Enter: [𠮷野家]
  PASS  Scratch024Tests.B2_Yoshinoya_MixedWithBmp
  kara の表示 [から] / + Space: 文節 [から] 候補 [から,～,空,殻,カラ,ｶﾗ] / + Enter: [から]
  PASS  Scratch024Tests.B3_Kara_Tilde_KeptBehindHiragana
  nikoniko の表示 [にこにこ] / + Space: 文節 [にこにこ] 候補 [にこにこ,😊,(^_^),(*^^*),ニコニコ,ﾆｺﾆｺ] / + Enter: [にこにこ]
  PASS  Scratch024Tests.B4_Nikoniko_Emoji_KeptBehindHiragana
  キャレットの前 [𠮷あいうえおかきくけ] (11 単位) + kawa + Space: 変換エンジンに渡った文脈の先頭 2 単位 [U+DFB7 U+3042] 長さ 10
  FAIL  Scratch024Tests.C1_ContextOf_CutsPair
        文脈が下位サロゲート単独で始まらない (実際: U+DFB7 U+3042)
  キャレットの前 [𠮷あいうえおかき] (9 単位) + kawa + Space: 文脈 [𠮷あいうえおかき] 先頭 [U+D842 U+DFB7]
  PASS  Scratch024Tests.C2_ContextOf_Within10_NoCut
    確定: [𠮷野家]
    確定: [あいうえおかき]
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
    Space をアプリに渡した
  変換エンジンに渡った文脈 [�野家あいうえおかき] 先頭 [U+DFB7 U+91CE] 長さ 10
  FAIL  Scratch024Tests.D1_LastCommitText_CutsPair_ViaSession
        文脈が下位サロゲート単独で始まらない
  Encoding.UTF8.GetBytes("\uDFB7野家") = EFBFBDE9878EE5AEB6 (GetByteCount = 9)
  "\uDFB7a".EnumerateRunes() = U+FFFD U+0061
  PASS  Scratch024Utf8.G_Utf8_LoneLowSurrogate

9/15 passed
```
同じ写しの木で、使い捨てのテストを外して Core のテストを全件流すと `1266/1266 passed` (root で動くクラウドのため、権限で失敗させるテストはスキップと表示される)。

### 再現 3: PR #29 の木 (ブランチ `ccr-0fc8aeab-sra4z7`、`d5bbd0f`、2026-11-22)
同じやり方で、広い文脈 (`WideContext` = Core での Zenzai のときの文脈) の ON・OFF を比べた。使い捨てのテストは終わったら消した。候補は先頭 3 つまで出した。
```
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- Scratch024Ccr
  WideContext=False: yoshi + Space 候補 [よし,𠮷,ヨシ] / + Enter: [よし]
  WideContext=True: yoshi + Space 候補 [よし,𠮷,ヨシ] / + Enter: [よし]
  PASS  Scratch024CcrTests.A1_Yoshi_WideOffOn
  WideContext=False 前 [𠮷あいうえおかきくけ] (11 単位): 文脈の先頭 [U+DFB7 U+3042] 長さ 10
  WideContext=True 前 [𠮷あいうえおかきくけ] (11 単位): 文脈の先頭 [U+D842 U+DFB7] 長さ 11
  WideContext=False 前 [𠮷あいうえおかきくけこさしすせそたちつて] (21 単位): 文脈の先頭 [U+3053 U+3055] 長さ 10
  WideContext=True 前 [𠮷あいうえおかきくけこさしすせそたちつて] (21 単位): 文脈の先頭 [U+3042 U+3044] 長さ 19
  PASS  Scratch024CcrTests.C_Context_WideOffOn
  WideConversionContext=False: 文脈の先頭 [U+304F U+3051] 長さ 10 [くけこさしすせそたち]
  WideConversionContext=True: 文脈の先頭 [U+91CE U+5BB6] 長さ 19 [野家あいうえおかきくけこさしすせそたち]
  PASS  Scratch024CcrTests.D_LastCommitText_Session_WideOffOn

3/3 passed
```
- 1 段目: 広い文脈の ON・OFF のどちらでも よし が確定する (`IsSymbolOnly` は広い文脈と関係ない)。
- 2 段目: OFF の `ContextOf` は main と同じく下位サロゲート単独で始まる。ON の `WideContextOf` は、11 単位なら切らず、21 単位なら切って先頭の下位サロゲート単独を落とす (19 単位)。
- 3 段目: Mac の経路で 𠮷野家 (4 単位) + あいうえおかきくけこさしすせそたち (17 単位) を確定した直後 (記録は 21 単位の末尾 20 単位で、先頭は下位サロゲート単独)。ON は記録の先頭まで使い、下位サロゲート単独を落としている (`野家…`、19 単位)。

### 再現 4: 査読 1 周目の後に足した打鍵と、試しの直し (2026-11-22)
査読の指摘を確かめるため、写しの木 (`deb0ff4`。コードは main `5544133` と同じ) で使い捨てのテスト `Scratch024dTests` を流した。同じテストを、**写しの木だけで試しに直した版** (人の判断 1 の案 A の形。コミットしていない) でも流した。試しの直しは 2 か所: `IsSymbolOnly` を `text.EnumerateRunes().Any(Rune.IsLetterOrDigit)` で判定し、`ContextOf` の戻り値の先頭の下位サロゲート単独を (切ったかどうかに関係なく) 落とす。出力の `<U+DFB7>` は単独の下位サロゲート (ペアはそのまま表示)。候補は全部ではなく、先頭 6 つまで。G2 のために、写しの木のテストの csproj に `AllowUnsafeBlocks` を足した。

今の版 (`deb0ff4`):
```
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- Scratch024d
  G2 Encoding.UTF8.GetBytes(char*, int, byte*, int) (ToUtf8 と同じ形) で "\uDFB7野家" = EFBFBDE9878EE5AEB6
  PASS  Scratch024dTests.G2
  A1 よし→𠮷: yoshi + Space 文節 [よし] 候補 [よし,𠮷,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [よし]
  A2 ほっけ→𩸽: hokke + Space 文節 [ほっけ] 候補 [ほっけ,𩸽,ホッケ,ﾎｯｹ,hokke,ｈｏｋｋｅ] / + Enter [ほっけ]
  A3 よしよし→𠮷𠮷: yoshiyoshi + Space 文節 [よしよし] 候補 [よしよし,𠮷𠮷,ヨシヨシ,ﾖｼﾖｼ,yoshiyoshi,ｙｏｓｈｉｙｏｓｈｉ] / + Enter [よしよし]
  A4 よしよし→𠮷𠮷 (ライブ変換 ON): yoshiyoshi の表示 [よしよし] / + Enter [よしよし]
  M1 [よし→𠮷][さん]: yoshisan + Space 文節 [よし|さん] 候補 [よし,𠮷,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [よしさん]
  M2 [あの][よし→𠮷]: anoyoshi + Space 文節 [あの|よし] 候補 [あの,アノ,ｱﾉ,ano,ａｎｏ] / + Enter [あのよし]
  B1 よし→吉: yoshi + Space 文節 [吉] 候補 [吉,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [吉]
  B2 よしのや→𠮷野家: yoshinoya + Space 文節 [𠮷野家] 候補 [𠮷野家,よしのや,ヨシノヤ,ﾖｼﾉﾔ,yoshinoya,ｙｏｓｈｉｎｏｙａ] / + Enter [𠮷野家]
  B3 から→～: kara + Space 文節 [から] 候補 [から,～,空,殻,カラ,ｶﾗ] / + Enter [から]
  B4 にこにこ→😊: nikoniko + Space 文節 [にこにこ] 候補 [にこにこ,😊,(^_^),(*^^*),ニコニコ,ﾆｺﾆｺ] / + Enter [にこにこ]
  E1b よし→𠮷 + ユーザー辞書 よし→𠮷: yoshi + Space 文節 [𠮷] 候補 [𠮷,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [𠮷]
  Z1 ぜろ→𝟘 (U+1D7D8): zero + Space 文節 [ぜろ] 候補 [ぜろ,𝟘,ゼロ,ｾﾞﾛ,zero,ｚｅｒｏ] / + Enter [ぜろ]
  Z2 よし→𠮷～: yoshi + Space 文節 [よし] 候補 [よし,𠮷～,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [よし]
  Z3 じょう→𠀋 (U+2000B): jou + Space 文節 [じょう] 候補 [じょう,𠀋,🔒,🔓,🔐,ジョウ] / + Enter [じょう]
  Z4 りにあ→𐀀 (U+10000): rinia + Space 文節 [りにあ] 候補 [りにあ,𐀀,リニア,ﾘﾆｱ,rinia,ｒｉｎｉａ] / + Enter [りにあ]
  C1 キャレットの前 [𠮷あいうえおかきくけ] (11 単位) + kawa + Space: 文脈 [<U+DFB7>あいうえおかきくけ] (10 単位)
  C2 キャレットの前 [𠮷あいうえおかき] (9 単位) + kawa + Space: 文脈 [𠮷あいうえおかき] (9 単位)
  C3 キャレットの前 [あ𠮷あいうえおかきく] (11 単位) + kawa + Space: 文脈 [𠮷あいうえおかきく] (10 単位)
  D1 (MeltypeSession) 𠮷野家・あいうえおかき を確定 → Space 10 回 → kawa + Space: 文脈 [<U+DFB7>野家あいうえおかき] (10 単位)
  PASS  Scratch024dTests.R4
2/2 passed
```
試しの直し (案 A の形):
```
$ dotnet run -c Release --no-build --project src/Meltype.Core.Tests -- Scratch024d
  G2 Encoding.UTF8.GetBytes(char*, int, byte*, int) (ToUtf8 と同じ形) で "\uDFB7野家" = EFBFBDE9878EE5AEB6
  PASS  Scratch024dTests.G2
  A1 よし→𠮷: yoshi + Space 文節 [𠮷] 候補 [𠮷,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [𠮷]
  A2 ほっけ→𩸽: hokke + Space 文節 [𩸽] 候補 [𩸽,ほっけ,ホッケ,ﾎｯｹ,hokke,ｈｏｋｋｅ] / + Enter [𩸽]
  A3 よしよし→𠮷𠮷: yoshiyoshi + Space 文節 [𠮷𠮷] 候補 [𠮷𠮷,よしよし,ヨシヨシ,ﾖｼﾖｼ,yoshiyoshi,ｙｏｓｈｉｙｏｓｈｉ] / + Enter [𠮷𠮷]
  A4 よしよし→𠮷𠮷 (ライブ変換 ON): yoshiyoshi の表示 [𠮷𠮷] / + Enter [𠮷𠮷]
  M1 [よし→𠮷][さん]: yoshisan + Space 文節 [𠮷|さん] 候補 [𠮷,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [𠮷さん]
  M2 [あの][よし→𠮷]: anoyoshi + Space 文節 [あの|𠮷] 候補 [あの,アノ,ｱﾉ,ano,ａｎｏ] / + Enter [あの𠮷]
  B1 よし→吉: yoshi + Space 文節 [吉] 候補 [吉,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [吉]
  B2 よしのや→𠮷野家: yoshinoya + Space 文節 [𠮷野家] 候補 [𠮷野家,よしのや,ヨシノヤ,ﾖｼﾉﾔ,yoshinoya,ｙｏｓｈｉｎｏｙａ] / + Enter [𠮷野家]
  B3 から→～: kara + Space 文節 [から] 候補 [から,～,空,殻,カラ,ｶﾗ] / + Enter [から]
  B4 にこにこ→😊: nikoniko + Space 文節 [にこにこ] 候補 [にこにこ,😊,(^_^),(*^^*),ニコニコ,ﾆｺﾆｺ] / + Enter [にこにこ]
  E1b よし→𠮷 + ユーザー辞書 よし→𠮷: yoshi + Space 文節 [𠮷] 候補 [𠮷,よし,ヨシ,ﾖｼ,yoshi,ｙｏｓｈｉ] / + Enter [𠮷]
  Z1 ぜろ→𝟘 (U+1D7D8): zero + Space 文節 [𝟘] 候補 [𝟘,ぜろ,ゼロ,ｾﾞﾛ,zero,ｚｅｒｏ] / + Enter [𝟘]
  Z2 よし→𠮷～: yoshi + Space 文節 [𠮷～] 候補 [𠮷～,よし,ヨシ,ﾖｼ,𠮷~,yoshi] / + Enter [𠮷～]
  Z3 じょう→𠀋 (U+2000B): jou + Space 文節 [𠀋] 候補 [𠀋,🔒,🔓,🔐,じょう,ジョウ] / + Enter [𠀋]
  Z4 りにあ→𐀀 (U+10000): rinia + Space 文節 [𐀀] 候補 [𐀀,りにあ,リニア,ﾘﾆｱ,rinia,ｒｉｎｉａ] / + Enter [𐀀]
  C1 キャレットの前 [𠮷あいうえおかきくけ] (11 単位) + kawa + Space: 文脈 [あいうえおかきくけ] (9 単位)
  C2 キャレットの前 [𠮷あいうえおかき] (9 単位) + kawa + Space: 文脈 [𠮷あいうえおかき] (9 単位)
  C3 キャレットの前 [あ𠮷あいうえおかきく] (11 単位) + kawa + Space: 文脈 [𠮷あいうえおかきく] (10 単位)
  D1 (MeltypeSession) 𠮷野家・あいうえおかき を確定 → Space 10 回 → kawa + Space: 文脈 [野家あいうえおかき] (9 単位)
  PASS  Scratch024dTests.R4
2/2 passed
```
- 試しの直しで、使い捨てのテストを外して Core のテストを全件流すと `1266/1266 passed`。
- `IsSymbolOnly` を一時的に「いつも true」にした写しの木 (今の版) では `1156/1266 passed` (既存のテストが 110 件落ちる)。「いつも false」では `1266/1266 passed` (上の「原因の場所」)。
- 試しの直しを一時的なコミット (どこからも参照しない) にして PR #29 (`d5bbd0f`) と `git merge-tree --write-tree` で合わせると、`CompositionController.cs` は自動で合わさった (合わせた結果では `WideContextOf` と `ContextOf` の両方が先頭の下位サロゲート単独を落とす)。衝突するのは、前から main と PR #29 の間で衝突している `docs/LESSONS.md`・`docs/requirements/README.md` だけ。
- PR #29 (`d5bbd0f`) の写しの木に同じ試しの直しを当てると、Core のテストは `1290/1290 passed` (PR #29 そのままも `1290/1290 passed`)。

## minimum の要件 (これだけは満たす)
1. 変換エンジンが返した文節ごとに、文字列に BMP の外の文字・数字 (𠮷・𩸽 など。分類が Letter か DecimalDigitNumber のコードポイント) が 1 つでもあれば、その文節を「記号だけ」とみなさない。文字の分類は UTF-16 の 1 単位ではなく、コードポイントで見る (例: `text.EnumerateRunes().Any(Rune.IsLetterOrDigit)`。単独のサロゲートは U+FFFD = 記号として数えられ、例外にならない: 再現 2 の G)。
2. 今「記号だけ」として扱っている文節は今のまま: BMP の記号 (から → ～) も、BMP の外の記号 (にこにこ → 😊。分類 OtherSymbol) も、ひらがなが先。BMP の漢字 (よし → 吉) と、BMP の字を含む文節 (よしのや → 𠮷野家) も今のまま。
3. 変換エンジンに渡す文脈が、下位サロゲート単独で始まらない (キャレットの前の文字から作るときも、自分の確定の記録から作るときも)。直し方は案 A (人の判断 1 の決定): `ContextOf` が返す文脈の先頭の下位サロゲート単独を、切ったかどうかに関係なく落とす (PR #29 の `WideContextOf` と同じ形)。記録を切る 5 か所は変えない。

## 非目標 (今回はやらない)
- Swift (`mac/`) の変更。Swift が入力欄から読むキャレットの前の 20 単位 (`mac/Sources/MeltypeIME/InputController.swift` 999〜1006 行の `surroundingText(of:)`。`NSRange` で UTF-16 の 20 単位) もペアの途中から始まりうるが、Swift の入力経路の変更は止める条件 (CLAUDE.md §5)。そのとき Core には U+FFFD が届く見込み (推定。Swift の文字列が単独のサロゲートを置き換えるため。未確認)。これは Core の直しでは消えない。
- 記録 `_lastCommitText` を切る 5 か所の変更 (案 A の決定)。記録の先頭の単独の下位サロゲートが文脈の結果に効くのは `ContextOf` と PR #29 の `WideContextOf` だけで、どちらも受け取る側で落とす。ほかに記録を使う所 (`IsEnglishSentence` の 1393 行・周りの文字の 2149・2346 行など) は、案 A では記録を変えないので今と同じ。5 か所の複写は `docs/CODE_REVIEW_2026-10.md` の R2 で別に扱う。
- PR #29 の `WideContextOf` の切り方 (既に先頭の下位サロゲート単独を落としている。変えない)。
- 「記号だけの変換結果はひらがなの後ろ」の規則そのもの (～・絵文字の並び) を変えること。
- ほかの UTF-16 単位の扱い: `LanguageOf`・`Classify` (サロゲートの単位を日本語の文字として数える)、`LengthForLiveConversion` (読みはかなだけなので影響なし)、`docs/CODE_REVIEW_2026-10.md` の L1a-5 (`ReconvertRomaji`)・L1b-6 (`Resize`)・L7-4 (Swift の `character`)。
- 正解表 (`eval/cases/*.tsv`) の行: 正解表は既定の偽の変換エンジンで打つので、𠮷 を返す変換エンジンを表せない。テストで守る。
- Windows・Linux: 対象外。

## 完成条件 (先に決める。機械で判定できる形)
> 実装 2026-11-25 (ブランチ `fix/surrogate-kanji-clause`、base = main `bc1b603`)。実測の数は末尾の「記録」の「実装の記録」に書いた。テストは 6 つに、異体字セレクタ・結合文字・絵文字の列を固定する 7 つ目 (`Req024_VariationSelectorsAndCombiningMarks`) を足した。
- [x] テスト (先に赤): 新しいファイル `src/Meltype.Core.Tests/SurrogatePairTests.cs` に次の 6 つを足す (開いている PR #29 が `CompositionTests.cs` を変えているので、衝突しないよう別のファイルにする)。偽の変換エンジンは「読み → 文節」を返す小さなもの (再現 2・4 と同じ形)。**実装前に 1・2・5・6 が赤になることを確かめる。**
  1. `Req024_SurrogateKanjiClause_StaysFirst`: よし → 𠮷・ほっけ → 𩸽・よしよし → 𠮷𠮷 のそれぞれで、`yoshi`・`hokke`・`yoshiyoshi` + Space の文節と先頭の候補が変換エンジンの答え (𠮷・𩸽・𠮷𠮷) で、+ Enter の確定も同じ。複数の文節の形として、[よし → 𠮷][さん] で `yoshisan` + Space の文節が `𠮷` と `さん`、Enter で `𠮷さん`。今: よし・ほっけ・よしよし (A1〜A3)、`よし` と `さん`・`よしさん` (M1)。
  2. `Req024_SurrogateKanjiClause_LiveConversion`: ライブ変換 ON で `yoshiyoshi` の表示が `𠮷𠮷`、Enter の確定も `𠮷𠮷`。今: どちらも よしよし (A4)。
  3. `Req024_SymbolOnlyClause_StaysBehindHiragana` (守り): から → ～ で `kara` + Space の候補の先頭 2 つが `から,～`・Enter で `から`。にこにこ → 😊 で `にこにこ,😊`・Enter で `にこにこ`。今も通る (B3・B4)。
  4. `Req024_BmpAndMixedClauses_Unchanged` (守り): よし → 吉 で `yoshi` + Space + Enter が `吉`、よしのや → 𠮷野家 で `yoshinoya` + Space + Enter が `𠮷野家`。今も通る (B1・B2)。
  5. `Req024_Context_FromTextBeforeCaret_DoesNotSplitSurrogatePair`: キャレットの前が `𠮷あいうえおかきくけ` (11 単位) で `kawa` + Space のとき、変換エンジンに渡る文脈が `あいうえおかきくけ` (9 単位)。`𠮷あいうえおかき` (9 単位) なら `𠮷あいうえおかき` のまま。切り目が上位サロゲートに当たる `あ𠮷あいうえおかきく` (11 単位) なら `𠮷あいうえおかきく` (10 単位) のまま (落としすぎない)。今: C1 は U+DFB7 + `あいうえおかきくけ`、C2・C3 は期待どおり。
  6. `Req024_Context_FromOwnCommits_DoesNotSplitSurrogatePair`: Mac の経路 (`MeltypeSession`。入力欄の文字は渡さない) で、`yoshinoya` + Space + Enter (𠮷野家) → `aiueokaki` + Enter → 何も入力していないときの Space 10 回 → `kawa` + Space のとき、変換エンジンに渡る文脈が `野家あいうえおかき` (9 単位)。今: U+DFB7 + `野家あいうえおかき` (D1)。案 A では、このテストは `ContextOf` の「いつも落とす」で緑になる (記録の切り方は確かめない。再現 4 の試しの直しで緑の形になった)。
- [x] 守りのテスト 3・4 は、わざと崩した木で赤になることを一度確かめ、記録欄に書く: `IsSymbolOnly` を一時的に「いつも false」にすると 3 が赤 (既存のテストは 1266/1266 のまま)、「いつも true」にすると 4 が赤 (既存のテストも 1156/1266 に落ちる)。どちらも確かめた後に戻す。
- [x] 回帰なし:
  - Core のテストが全件通る (base の main `5544133` は 1266/1266。クラウド Linux)。
  - `tools/eval-compare.sh` の broken 0。
  - `tools/boundary-sweep.sh <base-tree> <new-tree> <out-dir> light` の違いが 0 件 (`compare.md` の日本語の損失・英語の損失・その他の変化がどれも 0。終了コード 0 だけでは「その他の変化」を見落とす)。
  - 品質テスト (`-- --eval`) の出力が base と同じ。
  - どれも既定の偽の変換エンジンで打つので、この直しの効果は出ない見込み (違いが出たら意図しない変化)。
  - `tools/boundary-sweep.sh` は今は PR #29 の要件 015 にだけあり、main に無い。PR #29 が main に入る前に着手するなら、PR #29 の写しにある `tools/boundary-sweep.sh` に base と new の木を渡して使う (道具は渡された 2 つの木を比べる)。入力の一覧は道具のある木の辞書から作られる。main は分岐点 `20736db` の後に `dictionaries/english.txt` に 3 語 (pazolite・oeksound・obsidian) を足しているので、PR #29 の写しで作る入力は、main の辞書で作る入力とその分だけ違う。
- [x] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ。`サロゲート` で探して 0 件)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
azooKey の候補は Core では測れないので、まず並びを記録する。文脈の直しの効果 (Zenzai の左文脈の先頭が U+FFFD かどうか) は画面に出ないので、その項目は落ちないことだけを確かめる。
- [ ] TextEdit: `yoshi`・`hokke`・`yoshisan` + Space → 候補の並びと区切りを記録する (BMP の外の漢字が出るか・何番目か)。直す前の版でも記録しておくと、直した後と比べられる
- [ ] TextEdit: 上で azooKey が BMP の外の漢字だけの文節を最初に返した読みがあれば、直した版でその読み + Space → その漢字が先頭で、Enter でその漢字が入る
- [ ] TextEdit: `yoshinoya` + Space → 𠮷野家 が候補に出るなら選んで Enter (表示と確定が同じ)
- [ ] TextEdit: `kara` + Space → `から` が先頭で、`～` は後ろ (今と同じ)
- [ ] TextEdit: 𠮷 を 1 回選んで確定したあと、もう一度 `yoshi` + Space → 𠮷 が先頭 (学習。今と同じ)
- [ ] TextEdit: ユーザー辞書に よし → 𠮷 を登録して `yoshi` + Space → 𠮷 が先頭 (今と同じ)
- [ ] TextEdit (Zenzai ON・OFF): 𠮷野家 を確定した直後 (1.5 秒以内) に `kawa` + Space → 落ちない (落ちないことだけ)
- [ ] TextEdit (Zenzai ON): 空の書類に `𠮷あいうえおかきくけ` を貼り付け、その後ろで `kawa` + Space → 落ちない (落ちないことだけ。PR #29 の前の版では `ContextOf` の切り目を通る形。PR #29 の後は広い文脈になり、11 単位なので切らない)
- [ ] Safari のフォーム: `yoshi` + Space + Enter → TextEdit と同じ結果

## 測れていないこと
- azooKey (と Zenzai) が よし・ほっけ などに 𠮷・𩸽 だけの文節を返すか、何番目に返すか (= この不具合が実機で起きるか・どのくらい起きるか)。Core のテストと正解表は偽の変換エンジンで動くので測れない。
- Zenzai の左文脈の先頭に U+FFFD が入ったときの、変換結果への影響。画面にも出ないので、実機チェックリストでも測れない。
- Swift が入力欄から読む前の 20 単位がペアの途中から始まったときに、Core に何が届くか (非目標。推定は U+FFFD)。
- 実機の入力欄 (TextEdit・Safari・ターミナル・LibreOffice) での見え方。

## 受け入れる変化 (再現 4 の試しの直しで見えた変化。実装の後に base と新しい木の両方で打ち直して確かめる: docs/LESSONS.md 2026-11-13)
候補の並びは先頭 6 つまで (再現 4 の出力と同じ)。

| 形 | 今 (main) | 試しの直し (案 A の形) |
|---|---|---|
| A1〜A3 (よし → 𠮷・ほっけ → 𩸽・よしよし → 𠮷𠮷) | Space の先頭・Enter とも、ひらがな | 変換エンジンの答え (直したいこと) |
| A4 (ライブ変換 ON) | 表示・Enter とも `よしよし` | `𠮷𠮷` |
| M1 [よし → 𠮷][さん] | 文節 `よし` と `さん`・Enter `よしさん` | `𠮷` と `さん`・`𠮷さん` |
| M2 [あの][よし → 𠮷] | 文節 `あの` と `よし`・Enter `あのよし` | `あの` と `𠮷`・`あの𠮷` |
| Z1 ぜろ → 𝟘 (数学用の数字。漢字ではない) | 先頭 `ぜろ` | 先頭 `𝟘` |
| Z4 りにあ → 𐀀 (線文字 B。漢字ではない) | 先頭 `りにあ` | 先頭 `𐀀` |
| Z3 じょう → 𠀋 | 候補 `じょう,𠀋,🔒,🔓,🔐,ジョウ` | `𠀋,🔒,🔓,🔐,じょう,ジョウ` (ひらがなが、辞書から足した候補 🔒 などの後ろに下がる) |
| Z2 よし → 𠮷～ (BMP の外の漢字 + 記号) | 先頭 `よし` | 先頭 `𠮷～`。半角に戻した候補 `𠮷~` が増える (2056〜2060 行の規則) |
| C1 の文脈 | U+DFB7 + `あいうえおかきくけ` (10 単位) | `あいうえおかきくけ` (9 単位) |
| D1 の文脈 | U+DFB7 + `野家あいうえおかき` (10 単位) | `野家あいうえおかき` (9 単位) |
| B1〜B4・E1b・C2・C3 | — | 変わらない |

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 直すと、BMP の外の文字・数字を含まない文節 (～・😊・BMP の漢字) の候補の並びが 1 つでも変わる (規則の意味が変わる)。
- `tools/eval-compare.sh`・`tools/boundary-sweep.sh`・`--eval` に違いが出て、理由を説明できない。
- 着手の時点で `git merge-tree` が PR #29 との `CompositionController.cs` の衝突を示す (PR #29 は `ConversionContext`・`ContextOf` の周りを変えている。2026-11-22 の試しの直しでは自動で合わさった)。
- Swift (`mac/`) の変更が要ると分かったとき。

## 人の判断 (2026-11-22 に決定。人の指示「案 A で approved にして、PR を作って」)
1. 範囲と直し方 (どれか 1 つ) → **決定**: 案 A。
   - **案 A (推奨)**: `IsSymbolOnly` をコードポイントで見る + `ContextOf` が返す文脈の先頭の下位サロゲート単独を、切ったかどうかに関係なく落とす (PR #29 の `WideContextOf` と同じ形)。記録を切る 5 か所は変えない。再現 4 の試しでは、2 か所・6 行で、完成条件のテスト 1〜6 の形になり、Core のテストは 1266/1266。
   - 案 B: `IsSymbolOnly` だけ直し、文脈の切り目は直さない (別の要件にする)。minimum 3 とテスト 5・6 を外す。
   - 案 C: `IsSymbolOnly` + 記録を切る 5 か所 + `ContextOf` を「切ったときだけ落とす」形 (`DictionaryManagement.Tail` と同じ形) にする。記録そのものもペアの途中から始まらなくなるが、変更が `IsSymbolOnly` を含めて 7 か所に散る (案 A は `IsSymbolOnly` を含めて 2 か所)。
   - 判断の材料 (文脈の直しが Mac で効く範囲。上の「同じ系統の 2 か所」のコードを読んだ見立て): 今の main では、Zenzai ON の左文脈 (`ContextOf`) に効く。記録の先頭が入る D1 の形は、最後の確定から 1.5 秒以内に空白 10 個と次の入力を済ませたときか、入力欄の文字を返さないアプリだけ。PR #29 が入った後は、Zenzai ON の文脈が `WideContextOf` (既に落とす) になるので、`ContextOf` の直しが Mac で効くのは長い読み (50 字超) の 2 つ目以降の区切りの文脈だけ、記録の切り目は Mac では効かなくなる。
   - 案 A・C では、切り目がペアの途中なら下位サロゲートを落とす (ペアごと含めて 11・21 単位にする案は、最大の長さが変わるので採らない)。
2. 着手の時期: **PR #29 を待たなくてよい (推奨)**。案 A の試しの直しは PR #29 と自動で合わさり、PR #29 に当てた木でも Core のテストは 1290/1290 だった (再現 4)。`tools/boundary-sweep.sh` は、PR #29 の写しの道具を使えば今でも測れる (完成条件の「回帰なし」)。→ 指示では決めていない (推奨のまま)。
3. 承認の前に、実機で `yoshi`・`hokke`・`yoshisan` + Space の並びを見るか (推奨: 見る)。𠮷 を 1 回選べば Core の学習 (2349 行) が先に効くので、この不具合が実機で目に見えるのは、azooKey (と Zenzai) が BMP の外の漢字だけの文節を最初に返すときだけ (推定)。返さないなら、作るのを後回しにしてよい。→ 見ないで approved になった (指示)。

## 記録 (ループが書く)
- 下書き: 2026-11-22。再現だけ行い、実装はしていない。再現に使った写しの木と使い捨てのテストは消した。
- 査読 1 周目 (2026-11-22、別のエージェント): 止める 0・直す 4・任意 9。直す 4 件 (PR #29 の後の効き目と着手の時期・落とし方の 2 つの形とテスト 6・複数の文節の形・実機の 2 項目) と、任意 8 件 (1〜7・9) を反映した。任意 8 (読みやすさ) は冒頭に要約を足し、出力は依頼 (再現のコマンドと出力を写す) のため畳まずに残した。査読員の報告にあった出力・数字は、文書に写す前に写しの木で打ち直した (再現 4。docs/LESSONS.md 2026-11-18)。報告のうち「分岐点 `20736db` から main までで `dictionaries/` は変わっていない」は誤りだった (main は `english.txt` に 3 語を足している) ので、確かめた事実で書いた。試しの直しは写しの木だけで行い、コミットしていない。
- 査読 2 周目 (2026-11-22、同じエージェント): 止める 0・直す 0・任意 7。任意 1〜6 (「どの案でも」の言い過ぎ・か所の数え方・候補が先頭 6 つまでの注・記録を使う所の言い過ぎ・LESSONS の原因の当て方・PR #29 に当てた木のテストの数) と、7 の後半 (査読員の読み違いを LESSONS に 1 行) を反映した。7 の前半 (今回の決まりを `docs/requirements/TEMPLATE.md` にも書く) は依頼の範囲の外なので、人の判断に残す。
- 査読 3 周目 (2026-11-22、同じエージェント。2 周目の後の差分だけ): 止める 0・直す 0・任意 1 (非目標の、記録を使う所の一覧に「など」を足す) を反映した。査読を終える。
- 承認 (2026-11-22): 人の指示「案 A で approved にして、PR を作って」で approved (案 A)。minimum 3・非目標・テスト 6 を案 A の決定に合わせて書き直した。この要件定義書の PR #30 を作った。実装は未着手 (`/improve 024` で回す)。
- 実装の記録 (2026-11-25、ブランチ `fix/surrogate-kanji-clause`。base = main `bc1b603`、基準 1348/1348):
  - **変えたのは 2 か所** (`src/Meltype.Core/Composition/CompositionController.cs`): `IsSymbolOnly` を `text.EnumerateRunes().Any(Rune.IsLetterOrDigit)` (コードポイント) に。`ContextOf` は末尾 10 単位で切ったあと、先頭が下位サロゲート単独なら落とす (`WideContextOf` と同じ形)。記録を切る 5 か所は案 A のとおり変えていない。Swift は変えていない。
  - **先に赤**: テストを足した変更前の Core で 7 つ中 5 つが赤 (1 `StaysFirst`・2 `LiveConversion`・5 `FromTextBeforeCaret`・6 `FromOwnCommits`・7 `VariationSelectorsAndCombiningMarks`)、守りの 3・4 は緑。直した後は 7/7 緑。5 の実際の出力は U+DFB7 + `あいうえおかきくけ`、6 は U+DFB7 + `野家あいうえおかき` で、再現 2 の C1・D1 と同じ。
  - **わざと崩した木** (確かめた後に戻した): `IsSymbolOnly` を「いつも false」にすると 3 (と 7 の絵文字の列) だけが赤で、既存の 1348 件は緑のまま (1353/1355)。「いつも true」にすると 4 が赤になり、既存のテストも 139 件落ちる (1216/1355)。規則そのものは、既存のテストでは「いつも false」を検出できないので、3 と 7 が初めてこの規則を守る。
  - **全件テスト**: 1348/1348 → 1355/1355 (新規 7 件)。
  - **eval-compare** (base = `bc1b603` の木): 804/905 → 804/905、broken 0 / fixed 0 / added 0 / removed 0。`--eval` の出力 (20 行) は base と md5 一致 (`5c5e2c61…`)。
  - **boundary-sweep full** (base = `bc1b603`): 684,122 入力で日本語の損失 0 / 英語の損失 0 / その他の変化 0。既定の偽の変換エンジンは 𠮷 を返さないので、変わらないのが期待どおり (変わったら意図しない変化)。生データは作業木の外に置いた。
  - **受け入れた変化 (表の予想との照合)**: テスト 1・2・5・6・7 の出力が表の「試しの直し」の列になった。Z1〜Z4 (𝟘・𐀀・𠀋・𠮷～) は、BMP の外の「字」(漢字以外の数字・線文字を含む) を返す変換エンジンのとき、先頭が変換エンジンの答えになる (漢字だけでなく Letter / DecimalDigitNumber の全部。要件の minimum 1 の書き方どおり)。固定したのは 𠮷・𩸽・異体字セレクタ・結合文字・絵文字で、Z1〜Z4 は固定していない (実機で出る見込みが無いため)。
  - **異体字セレクタ・結合文字** (テスト 7 で固定): 漢字 + U+FE0F・U+E0100 (IVS)・濁点の結合文字は漢字を含むので記号だけでない (変換エンジンの答えが先頭)。BMP の漢字 + IVS も同じ。絵文字 + U+FE0F・肌色・ZWJ で繋いだ絵文字は記号だけのまま (ひらがなが先)。文脈: 切り目が 𠮷 の下位サロゲートに当たると、残った IVS (U+E0100) から始まる (落とすのは下位サロゲート単独だけ。IVS・結合文字で始まる文脈は直さない。Zenzai の左文脈に出る影響は測れていない)。
  - **実際の azooKey は 𠮷・𩸽 を返すか** (3 つ目の人の判断の材料。`MeltypeIME --convert-eval --top`、base `bc1b603` を `swift build` したもの): Zenzai OFF の上位 12・Zenzai ON (`zenz-v3.1-small-Q5_K_M`) の上位 20 の候補に、`よし`・`ほっけ`・`よしさん`・`よしのや`・`よしだ`・`つちよし`・`ほっけの`・`じょう` と、左文脈付きの `よしのや`・`よし` のどれにも 𠮷・𩸽・𠀋 は無かった (`よし` の先頭は よし・ヨシ・吉・義、`よしのや` は 吉野家・吉野や)。**今の azooKey (と Zenzai) では、この不具合は実機で目に見えない**。直しは、変換エンジンが BMP の外の漢字を返すようになったとき (azooKey の更新・別の変換エンジン) の備え。
  - **同じ種類の判定の洗い出し** (`git grep` で `char.IsLetter`・`IsLetterOrDigit`・`IsSymbol`・`IsPunctuation`・`IsDigit`・`IsLowSurrogate` などを `src/` で探した。`IsAscii…` は ASCII だけを見るので対象外):
    - 直した: `CompositionController.IsSymbolOnly` (文節の記号判定)、`ContextOf` (文脈の切れ目)。
    - 既に安全: `WideContextOf` (先頭の下位サロゲートを落とす)・`DictionaryManagement.Tail`/`Head`・`LanguageMemory` 469 行・`TermDomains.User` 172・341 行 (ペアを切らない)。
    - 範囲外 (直さない。1 単位ずつ見るので、BMP の外の字はどれも「文字でも記号でもない」側に落ちる):
      - `CompositionController` の数式 (かける・わる) の後ろの判定 `char.IsLetter(following)` (今 2447 行): 後ろの最初の字が 𠮷 などだと「文の終わり」とみなして × ÷ にしうる。
      - `LengthForLiveConversion` の `char.IsLetterOrDigit` (読みはかななので影響なし)。
      - `CompositionText` の全角 ！〜～ を半角にする `!char.IsLetterOrDigit` (BMP の全角の範囲だけ)。
      - `DictionarySuggestions` 103 行の `IsPunctuation`・`IsSymbol` (単語に絵文字などの記号があるかを見る。サロゲートは記号と数えない)。
      - `Input/LineContext` 102・133・235 行の `char.IsLetterOrDigit` (引用符・拡張子の前後の字の判定)。
      - Windows の `src/Meltype/Composition/ColorTextRenderer.cs` の `char.IsSurrogate` (絵文字の判定)。
    - 記録を切る 5 か所 (`[^20..]`) と `CODE_REVIEW_2026-10.md` の L1a-5・L1b-6・L7-4 は、文書の非目標のとおり。
- 2026-11-26 自動打鍵 (リョウさんの依頼。人の手ではない。TextEdit。手順と結果は docs/HANDS_ON_CHECKLIST_2026-11.md の追加分 94〜96): 94〜96 は OK。`yoshi`・`hokke`・`yoshisan` の先頭 10 件前後に BMP の外の漢字 (𠮷・𩸽) は出ず、実機では差が見えない (この不具合は今の変換エンジンでは起きない見込み。推定)。`kara` は `から` が先頭。𠮷 を含む文字列を貼り付けたあとの `kawa` Space でも落ちず、表示も崩れない。status は変えない。

## Mac 実機で試す手順 (打つ > 期待)
実機の入れ替えは AI はしていない。リョウさんが入れ替えたあとの確認用。**今の azooKey は 𠮷・𩸽 だけの文節を返さない (上の「実装の記録」)ので、「𠮷 が先頭になる」ことは実機では再現できない。** ユーザー辞書・学習は規則より先に先頭を決める (`ContextRules`・`History` が先) ので、これらに 𠮷 を入れても変更前後で同じになる (E1b)。そのため、実機の確認は「何も壊れていない」ことと「𠮷 を出せる経路が今までどおり」を見る。
1. ユーザー辞書に 読み `よし`・単語 `𠮷` を登録 → TextEdit で `yoshi` + Space > 先頭が 𠮷、Enter で 𠮷 が入る (変更前も同じ)。
2. 𠮷 を 1 回選んで確定 (Space を押して候補から選ぶ) → もう一度 `yoshi` + Space > 𠮷 が先頭 (学習。変更前も同じ)。
3. `yoshinoya` + Space > 候補に 𠮷野家 が出るなら選んで Enter > 表示と確定が同じ (出ない環境ではユーザー辞書に よしのや → 𠮷野家 を登録して同じことをする)。
4. `kara` + Space > `から` が先頭で `～` は後ろ (記号だけの文節の規則が壊れていない)。`nikoniko` + Space > ひらがなが先頭 (絵文字の候補があっても)。
5. `yoshi`・`hokke`・`yoshisan` + Space > 候補の並びを記録する (azooKey が 𠮷・𩸽 を出し始めたら、その読みで先頭が 𠮷・𩸽 になり Enter で入る = この直しの効果)。
6. Zenzai ON・OFF のそれぞれで、𠮷野家 を確定した直後 (1.5 秒以内) に `kawa` + Space > 落ちずに候補が出る (文脈の先頭の直しは画面に出ない。落ちないことだけ)。
7. TextEdit に `𠮷あいうえおかきくけ` を貼り付け、その後ろで `kawa` + Space > 落ちずに候補が出る (11 単位。切り目がペアの途中に当たる形)。
8. Safari のフォーム: `yoshi` + Space + Enter > TextEdit と同じ結果。
