---
title: サロゲートペアの漢字 (𠮷・𩸽) だけの文節を「記号だけ」とみなし、変換・確定がひらがなになる
status: draft   # draft | approved | done。approved にするのは人 (CLAUDE.md §4)
source: 要件 017 (前の文を Zenzai に渡す。PR #29、ブランチ ccr-0fc8aeab-sra4z7) の作業中に見つけた不具合 (2026-11-22)
scope: core
---

# 024 サロゲートペアの漢字 (𠮷・𩸽) だけの文節を「記号だけ」とみなし、変換・確定がひらがなになる

> 番号の注: ここでの「要件 017」は PR #29 の `017-zenzai-previous-sentence.md` (前の文を Zenzai に渡す) のこと。ブランチ `req/017-reto-ga` の 017 (長音の語 + 助詞) とは別。
> 024 は、どのブランチの `docs/requirements/` にも無い次の番号 (2026-11-22 に origin の全ブランチで確かめた。最大は 023)。

## 背景 (誰が、何に困っているか)
- 変換エンジンが、読み全体を **BMP の外の漢字だけの 1 文節** で返すと、Meltype はその文節を「記号だけ」とみなし、ひらがなを先頭の候補にする。BMP の外の漢字は UTF-16 で 2 単位 (サロゲートペア) になる字で、𠮷 (U+20BB7)・𩸽 (U+29E3D) など CJK 統合漢字拡張 B 以降の字。Space で変換しても先頭はひらがなで、そのまま Enter するとひらがなが確定する。
- 困る人 (推定): 人名・店名の 𠮷 (よし)・魚の 𩸽 (ほっけ) などを 1 字で変換したい人。毎回 2 番目の候補を選び直すことになる (1 回選んで確定すれば、Core の学習で次からは先頭になる。下の再現 2 の E2)。
- 見つけた経緯: 2026-11-22、要件 017 の作業中。偽の変換エンジンで よし → 𠮷 を返すようにして `yoshi` + Space + Enter とすると、𠮷 ではなく よし が確定した。Zenzai の設定 (PR #29 では Core の広い文脈 `WideConversionContext`) の ON・OFF に関係なく起きる (再現 3)。
- **実際の azooKey が よし・ほっけ に 𠮷・𩸽 を 1 文節で返すかは未確認** (Mac でしか確かめられない)。返さなければ、利用者の目に見える不具合にはならない。
- 同じ系統 (UTF-16 の 1 単位ずつ扱う) として、変換エンジンに渡す文脈を作る 2 か所 (`ContextOf` の末尾 10 単位・自分の確定の記録 `_lastCommitText` の末尾 20 単位) も、サロゲートペアの途中で切る。切ると文脈が下位サロゲート単独で始まり、Swift に渡るときは U+FFFD (�) に置き換わる見込み (下の「文脈が Mac で使われるところ」)。

## 今の動き (既に分かっていること)
### 原因の場所 (main `5544133`)
- `src/Meltype.Core/Composition/CompositionController.cs` 2513 行の `IsSymbolOnly`:
  ```csharp
  private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);
  ```
  `text.Any(...)` は UTF-16 の 1 単位 (char) ずつ見る。サロゲートの単位の分類は `Surrogate` で、`char.IsLetterOrDigit` は false を返す (再現 2 の F)。そのため 𠮷 だけの文字列は「文字が 1 つも無い = 記号だけ」になる。
- 呼び出し元は 2351 行 (`ConvertWithEngineOnce`)。2350 行のコメントは「変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。」。記号だけで、読みが全部ひらがな (ぁ〜ゖ) なら、ひらがなを先頭にする (`Prefer`)。
- この規則より先に、計算の記号・手がかり辞書 (`ContextRules`)・学習 (`History`) が先頭を決める (2349 行)。そのため 𠮷 を 1 回選んで確定すると、次からは学習で 𠮷 が先頭になる (E2)。ユーザー辞書に登録した語は、この規則の影響を受けない (E1)。
- 同じ式は、Mac 版を追加したコミット `c3c247f` (2026-10-07。今の main の祖先ではない古い履歴) にもある。本家 (yksr-melt/Meltype) にあるか・直したかは未確認 (クラウドから届かない)。
- この規則 (から → ～ をひらがなの後ろ) を守るテストは今は無い。`src/Meltype.Core.Tests` で、変換エンジンが `～` を返す偽物と `IsSymbolOnly` の名前を探して 0 件。写しの木で `IsSymbolOnly` を一時的に「いつも false」(規則を外す) にしても、Core のテストは `1266/1266 passed` だった (2026-11-22。確かめた後に戻した)。
- Zenzai の設定は、main の Core の変換の流れに入っていない (`src/Meltype.Core` で Zenzai が出てくるのは `Config/Settings.cs`・`Config/MacSettingsCatalog.cs` だけ)。

### 同じ系統の 2 か所 (サロゲートを見ずに切る)
- `ContextOf` (2501〜2507 行): 変換エンジンに渡す文脈 (同じ文の最大 10 単位)。`text.Length > 10 ? text[^10..] : text` で切る。確定した文節を変換エンジンに覚えさせる `LearnConversion` (3877 行) も同じ `ContextOf` を使う。
- `_lastCommitText` (自分の確定の記録。最大 20 単位): 5 か所で `[^20..]` で切る。`CommitPart` (598 行。部分確定)・`CommitFirstSound` (3131 行。1 音確定)・`SetOwnContext` (3445 行)・`NoteBreakKeyPassedToApp` (3527 行。アプリに渡した Enter・Tab・Space を記録に足す)・`CommitText` (3976 行。確定)。確定の直後 (1.5 秒以内。`OwnCommitTrustMs`) と、入力欄の文字が取れないときは、この記録がキャレットの前の文字になる (`BeginComposition` 1388〜1389 行)。
  - main では `ContextOf` が両端の空白を除いてから末尾 10 単位で切るので、記録の先頭が文脈に入るのは、記録の末尾に空白・タブなどの空白文字が 10 個以上続くときだけ (再現 2 の D1)。PR #29 の広い文脈 (`WideContextOf`。末尾 20 単位) では記録の先頭がそのまま入るので、PR #29 は受け取る側で「先頭の下位サロゲート単独を落とす」処理を入れてある (再現 3)。
- 文脈が Mac で使われるところ: Core から Swift へは UTF-8 で渡す (`src/Meltype.Mac.Native/Exports.cs` 1255〜1262 行の `ToUtf8`。`Encoding.UTF8.GetBytes`)。単独のサロゲートは U+FFFD (EF BF BD) に置き換わる (再現 2 の G。string の形の `GetBytes` で確かめた。`ToUtf8` は char* の形で、同じ `Encoding.UTF8` なので同じ置き換えになる見込み)。Swift は文脈を Zenzai を使うときだけ左文脈 (`leftSideContext`) に使う (`mac/Sources/MeltypeIME/Converter.swift` 163〜166・174〜176 行)。学習 (`learn(context:clauses:)`。296 行〜) は文脈を使っていない。
- サロゲートペアの途中で切らないお手本 (既にあるもの): `src/Meltype.Core/Composition/DictionaryManagement.cs` の `Tail` (289〜295 行。293 行で先頭の下位サロゲートを飛ばす)・`Head` (298〜302 行)、`TermDomains.User.cs` 172・341 行、`LanguageMemory.cs` 469 行、PR #29 の `WideContextOf`。コードポイントで分類を見るお手本: `UserDictionary.cs` 96 行 (`Rune.GetUnicodeCategory`)。
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
A〜D は「直したあとの期待」で書いたテストで、FAIL は今の不具合を示す。B・E は今のまま正しい動き。

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
| E1 | 既定の偽物 + ユーザー辞書に よし → 𠮷 | `yoshi` + Space + Enter | 候補 `𠮷,よし,ヨシ,ﾖｼ` | `𠮷` | 影響なし |
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
同じやり方で、広い文脈 (`WideContext` = Core での Zenzai のときの文脈) の ON・OFF を比べた。使い捨てのテストは終わったら消した。
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

## minimum の要件 (これだけは満たす)
1. 変換エンジンが返した文節の文字列に、BMP の外の文字・数字 (𠮷・𩸽 など。分類が Letter か DecimalDigitNumber のコードポイント) が 1 つでもあれば、「記号だけ」とみなさない。文字の分類は UTF-16 の 1 単位ではなく、コードポイントで見る (例: `text.EnumerateRunes().Any(Rune.IsLetterOrDigit)`。単独のサロゲートは U+FFFD = 記号として数えられ、例外にならない: 再現 2 の G)。
2. 今「記号だけ」として扱っている文節は今のまま: BMP の記号 (から → ～) も、BMP の外の記号 (にこにこ → 😊。分類 OtherSymbol) も、ひらがなが先。BMP の漢字 (よし → 吉) と、BMP の字を含む文節 (よしのや → 𠮷野家) も今のまま。
3. 変換エンジンに渡す文脈が、下位サロゲート単独で始まらない。キャレットの前の文字から作るとき (`ContextOf` の末尾 10 単位) も、自分の確定の記録から作るとき (`_lastCommitText` の末尾 20 単位) も。切り目がペアの途中なら、その下位サロゲートを落とす (人の判断 2)。直す場所は `ContextOf` と、記録を切る 5 か所 (上の「同じ系統の 2 か所」)。

## 非目標 (今回はやらない)
- Swift (`mac/`) の変更。Swift が入力欄から読むキャレットの前の 20 単位 (`mac/Sources/MeltypeIME/InputController.swift` 999〜1006 行の `surroundingText(of:)`。`NSRange` で UTF-16 の 20 単位) もペアの途中から始まりうるが、Swift の入力経路の変更は止める条件 (CLAUDE.md §5)。そのとき Core には U+FFFD が届く見込み (推定。Swift の文字列が単独のサロゲートを置き換えるため。未確認)。
- PR #29 の `WideContextOf` の切り方 (既に先頭の下位サロゲート単独を落としている。変えない)。
- 「記号だけの変換結果はひらがなの後ろ」の規則そのもの (～・絵文字の並び) を変えること。
- ほかの UTF-16 単位の扱い: `LanguageOf`・`Classify` (サロゲートの単位を日本語の文字として数える)、`LengthForLiveConversion` (読みはかなだけなので影響なし)、`docs/CODE_REVIEW_2026-10.md` の L1a-5 (`ReconvertRomaji`)・L1b-6 (`Resize`)・L7-4 (Swift の `character`)。
- 正解表 (`eval/cases/*.tsv`) の行: 正解表は既定の偽の変換エンジンで打つので、𠮷 を返す変換エンジンを表せない。テストで守る。
- Windows・Linux: 対象外。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] テスト (先に赤): 新しいファイル `src/Meltype.Core.Tests/SurrogatePairTests.cs` に次の 6 つを足す (開いている PR #29 が `CompositionTests.cs` を変えているので、衝突しないよう別のファイルにする)。偽の変換エンジンは「読み → 1 文節」を返す小さなもの (再現 2 と同じ形)。**実装前に 1・2・5・6 が赤になることを確かめる。**
  1. `Req024_SurrogateKanjiClause_StaysFirst`: よし → 𠮷・ほっけ → 𩸽・よしよし → 𠮷𠮷 のそれぞれで、`yoshi`・`hokke`・`yoshiyoshi` + Space の文節と先頭の候補が変換エンジンの答え (𠮷・𩸽・𠮷𠮷) で、+ Enter の確定も同じ。今: よし・ほっけ・よしよし (A1〜A3)。
  2. `Req024_SurrogateKanjiClause_LiveConversion`: ライブ変換 ON で `yoshiyoshi` の表示が `𠮷𠮷`、Enter の確定も `𠮷𠮷`。今: どちらも よしよし (A4)。
  3. `Req024_SymbolOnlyClause_StaysBehindHiragana` (守り): から → ～ で `kara` + Space の候補の先頭 2 つが `から,～`・Enter で `から`。にこにこ → 😊 で `にこにこ,😊`・Enter で `にこにこ`。今も通る (B3・B4)。
  4. `Req024_BmpAndMixedClauses_Unchanged` (守り): よし → 吉 で `yoshi` + Space + Enter が `吉`、よしのや → 𠮷野家 で `yoshinoya` + Space + Enter が `𠮷野家`。今も通る (B1・B2)。
  5. `Req024_Context_FromTextBeforeCaret_DoesNotSplitSurrogatePair`: キャレットの前が `𠮷あいうえおかきくけ` (11 単位) で `kawa` + Space のとき、変換エンジンに渡る文脈が `あいうえおかきくけ` (9 単位)。`𠮷あいうえおかき` (9 単位) なら `𠮷あいうえおかき` のまま。今: U+DFB7 + `あいうえおかきくけ` (C1)。
  6. `Req024_Context_FromOwnCommits_DoesNotSplitSurrogatePair`: Mac の経路 (`MeltypeSession`。入力欄の文字は渡さない) で、`yoshinoya` + Space + Enter (𠮷野家) → `aiueokaki` + Enter → 何も入力していないときの Space 10 回 → `kawa` + Space のとき、変換エンジンに渡る文脈が `野家あいうえおかき` (9 単位)。今: U+DFB7 + `野家あいうえおかき` (D1)。
- [ ] 守りのテスト 3・4 は、わざと崩した木 (`IsSymbolOnly` を一時的に「いつも false」「いつも true」にして戻す) でそれぞれ赤になることを一度確かめ、記録欄に書く。
- [ ] 回帰なし:
  - Core のテストが全件通る (base の main `5544133` は 1266/1266。クラウド Linux)。
  - `tools/eval-compare.sh` の broken 0。
  - `tools/boundary-sweep.sh <base-tree> <new-tree> <out-dir> light` の違いが 0 件 (`compare.md` の日本語の損失・英語の損失・その他の変化がどれも 0。終了コード 0 だけでは「その他の変化」を見落とす)。
  - 品質テスト (`-- --eval`) の出力が base と同じ。
  - どれも既定の偽の変換エンジンで打つので、この直しの効果は出ない見込み (違いが出たら意図しない変化)。
  - `tools/boundary-sweep.sh` は今は PR #29 (要件 015) にだけあり、main に無い。PR #29 が main に入る前に着手するなら、PR #29 の写しにある `tools/boundary-sweep.sh` に base と new の木を渡して使う (道具は渡された 2 つの木を比べる)。
- [ ] 文書: `docs/KNOWN_LIMITS.md` は該当の記載なし (確認のみ。`サロゲート` で探して 0 件)。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
azooKey の候補は Core では測れないので、まず並びを記録する。
- [ ] TextEdit: `yoshi` + Space → 候補の並びを記録する (𠮷 が出るか・何番目か)。azooKey が 𠮷 を最初に返すなら、𠮷 が先頭で、Enter で 𠮷 が入る
- [ ] TextEdit: `hokke` + Space → 同じく記録する (𩸽)
- [ ] TextEdit: `yoshinoya` + Space → 𠮷野家 が候補に出るなら選んで Enter (表示と確定が同じ)
- [ ] TextEdit: `kara` + Space → `から` が先頭で、`～` は後ろ (今と同じ)
- [ ] TextEdit: 𠮷 を 1 回選んで確定したあと、もう一度 `yoshi` + Space → 𠮷 が先頭 (学習。今と同じ)
- [ ] TextEdit: ユーザー辞書に よし → 𠮷 を登録して `yoshi` + Space → 𠮷 が先頭 (今と同じ)
- [ ] TextEdit (Zenzai ON・OFF): 𠮷野家 を確定した直後 (1.5 秒以内) に `kawa` + Space → 候補が出て、落ちない
- [ ] TextEdit (Zenzai ON): 空の書類に `𠮷あいうえおかきくけ` を貼り付け、その後ろで `kawa` + Space → 候補が出て、落ちない (`ContextOf` の切り目がペアの途中になる形)
- [ ] Safari のフォーム: `yoshi` + Space + Enter → TextEdit と同じ結果

## 測れていないこと
- azooKey が よし・ほっけ などに 𠮷・𩸽 を 1 文節で返すか、何番目に返すか (= この不具合が実機で起きるか・どのくらい起きるか)。Core のテストと正解表は偽の変換エンジンで動くので測れない。
- Zenzai の左文脈の先頭に U+FFFD が入ったときの、変換結果への影響。
- Swift が入力欄から読む前の 20 単位がペアの途中から始まったときに、Core に何が届くか (非目標。推定は U+FFFD)。
- 実機の入力欄 (TextEdit・Safari・ターミナル・LibreOffice) での見え方。

## 受け入れる変化 (見込み。実装の後に base と新しい木の両方で打ち、2 列の表に直す: docs/LESSONS.md 2026-11-13)
- 変換エンジンが BMP の外の漢字だけの 1 文節を返す読みで、Space の先頭とライブ変換の表示が、ひらがなから変換エンジンの答えに変わる (直したいこと)。
- 文脈の切り目がペアの途中のとき、変換エンジンに渡る文脈が 1 単位短くなる (下位サロゲート単独を落とす)。

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 直すと、BMP の外の文字・数字を含まない文節 (～・😊・BMP の漢字) の候補の並びが 1 つでも変わる (規則の意味が変わる)。
- `tools/eval-compare.sh`・`tools/boundary-sweep.sh`・`--eval` に違いが出て、理由を説明できない。
- PR #29 が main に入っていないまま着手し、`CompositionController.cs` の同じ場所で衝突する (PR #29 は `ConversionContext`・`ContextOf` の周り、2495 行付近を変えている。行番号は分岐点 `20736db` と main `5544133` で同じ)。
- Swift (`mac/`) の変更が要ると分かったとき。

## 人の判断 (下書きの提案。決めるのは人)
1. 範囲: **案 A (推奨)** `IsSymbolOnly` と文脈の切り目 2 か所を、この要件でまとめて直す (原因が同じ「UTF-16 の 1 単位ずつ扱う」で、どれも小さい)。案 B: `IsSymbolOnly` だけ直し、切り目は別の要件にする (文脈の害は、Zenzai ON のときの左文脈の先頭 1 字が U+FFFD になることで、気付きにくい)。
2. 切り目がペアの途中のとき: **落とす (推奨)**。最大 10・20 単位を守れ、`DictionaryManagement.Tail` と PR #29 の `WideContextOf` と同じ形。もう 1 つの案は、ペアごと含めること (11・21 単位になる)。
3. 着手の時期: **PR #29 が main に入ってから (推奨)**。`tools/boundary-sweep.sh` が main に入り、`CompositionController.cs` の同じ場所の衝突も避けられる。

## 記録 (ループが書く)
- 下書き: 2026-11-22。再現だけ行い、実装はしていない。再現に使った写しの木と使い捨てのテストは消した。
