// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 1-4a 保護区間 (P42): パス・オプション・変数は、語の頭から空白まで英字のまま (半角)。
/// 本家 1.2.1 の ProtectionTests.cs (GPL-3.0-or-later、Copyright (C) 2026 0924haruto12) の該当テストを参考にして、Mac 版の道具 (Keyboard) で書いたもの (写してはいない)。
/// </summary>
internal static class ProtectedSpanTests
{
    /// <summary>製品と同じく、英単語の判定にスペルチェッカーを使う (cd・ls を英語として扱う)。終わったら元に戻す (ほかのテストに漏らさない)。</summary>
    private static void WithSpellChecker(Action body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static string Commit(string typed, Action<CompositionTests.Keyboard>? setup = null)
    {
        var result = "";
        WithSpellChecker(() =>
        {
            var k = new CompositionTests.Keyboard();
            setup?.Invoke(k);
            k.Type(typed + "\n");
            result = k.Host.Document;
        });
        return result;
    }

    private static void ShiftDown(CompositionTests.Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Down);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Paths_StayHalfWidthEnglish()
    {
        var cases = new Dictionary<string, string>
        {
            ["./src/main.ts"] = "./src/main.ts", ["./build.sh"] = "./build.sh", ["~/Documents"] = "~/Documents",
            ["echo ~/Downloads"] = "echo ~/Downloads", ["/home/taro/project"] = "/home/taro/project", ["../src/main.ts"] = "../src/main.ts",
            ["./shumire-shon"] = "./shumire-shon", ["/tmp/buresureddo"] = "/tmp/buresureddo", ["$HOME/.zshrc"] = "$HOME/.zshrc",
            // 拡張子の後ろの助詞は今までどおり日本語 (DotSuffixEnd の終わりで止める)
            ["./build.shde"] = "./build.shで", ["./build.shwojikkousite"] = "./build.shをじっこうして", ["../README.mdwo"] = "../README.mdを",
            // 拡張子が無ければ空白まで英字のまま (空白の後は今までどおり)
            ["/tmp/fooni"] = "/tmp/fooni", ["/tmp/fooni hozon"] = "/tmp/fooni ほぞん",
            ["~/.config/nvim wo"] = "~/.config/nvim を", ["./node_modules/.bin/tsc"] = "./node_modules/.bin/tsc", ["/etc/hosts ni"] = "/etc/hosts に",
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void Options_StayHalfWidthEnglish()
    {
        var cases = new Dictionary<string, string>
        {
            ["--no-install"] = "--no-install", ["mac/build.sh --no-install wo jikkou"] = "mac/build.sh --no-install をじっこう",
            ["-la"] = "-la", ["-m"] = "-m", ["tar -xzvf a.tar.gz"] = "tar -xzvf a.tar.gz", ["gcc -Wall"] = "gcc -Wall", ["find . -name"] = "find . -name",
            ["-rn"] = "-rn", ["-Wall"] = "-Wall", ["-xzvf"] = "-xzvf",
            // -- のオプションも、= を含む・- で区切った部分にローマ字として読めないものがある・ローマ字として読めない・大文字を含む・直前が英語のときは守る
            ["--color=auto"] = "--color=auto", ["--help"] = "--help", ["--force"] = "--force", ["--version"] = "--version", ["--Werror"] = "--Werror",
            ["git --name"] = "git --name", ["--dry-run"] = "--dry-run",
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void DoubleDash_ReadableAsRomaji_IsJapaneseDash()
    {
        // 小説・台詞の「ーー」を -- で打つ書き方 (--sorekara) は守らない。ローマ字として読めるオプション (--name) は、英語の後ろでないと守らない (抜け道: F10 で英字に)
        // 長音の - を含むローマ字 (--ko-hi-wo・--ra-men・--no-sushi) も、- で区切った部分が全部ローマ字として読めるので守らない (査読 R57 C)。--no-install・--dry-run は読めない部分があるので守る (Options_StayHalfWidthEnglish)
        var cases = new Dictionary<string, string>
        {
            ["--sorekara"] = "ーーそれから", ["--sate"] = "ーーさて", ["--ko-hi-wo"] = "ーーこーひーを", ["--ra-men"] = "ーーらーめん", ["--no-sushi"] = "ーーのーすし",
        };
        foreach (var (typed, expected) in cases)
        {
            Assert.Equal(expected, Commit(typed), $"「{typed}」");
            Assert.True(!HasProtected(typed), $"「{typed}」は保護しない");
        }
    }

    [Test]
    public static void Variables_StayHalfWidthEnglish()
    {
        foreach (var typed in new[] { "$HOME", "${PATH}", "echo $HOME" }) Assert.Equal(typed, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void AfterOpeningSymbol_IsAlsoWordStart_AndStopsAtTheClosingSymbol()
    {
        // ` ( [ の直後も語の頭。範囲は対応する閉じの記号まで (閉じの記号の後ろの助詞は日本語)。[ ] は日本語の入力では 「」
        var cases = new Dictionary<string, string>
        {
            ["`./build.sh` wo"] = "`./build.sh` を", ["(~/Documents)wo"] = "(~/Documents)を", ["[~/Documents]wo"] = "「~/Documents」を",
            ["`--no-install` wo"] = "`--no-install` を",
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void SquareBracket_FollowsTheWidthOfTheOpener()
    {
        // 打った [ ] は、英語の後ろでも日本語の入力では 「」 (bot「Thinking」。IsAsciiSymbol が [ ] を除く)。保護区間の ] も 」 で、対にそろう (査読 R57 D)
        Assert.Equal("Hello「~/Documents」を", Commit("Hello[~/Documents]wo"), "英語の後ろでも [ ] は 「」 の対");
        Assert.Equal("「~/Documents」を", Commit("[~/Documents]wo"), "日本語の [ ] は 「」");

        // 開きの記号が確定済みの文字の側にあるとき (査読 R57 E): 確定済みの 「 に対応する ] は 」、半角の [ に対応する ] は半角
        var closed = new CompositionTests.Keyboard();
        closed.Host.PrecedingText = "ファイルは「";
        closed.Type("~/Documents]wo\n");
        Assert.Equal("~/Documents」を", closed.Host.Document, "確定済みの 「 に対応する ] は 」");

        var half = new CompositionTests.Keyboard();
        half.Host.PrecedingText = "see [";
        half.Type("~/Documents]wo\n");
        Assert.Equal("~/Documents]を", half.Host.Document, "確定済みの半角の [ に対応する ] は半角");

        var paren = new CompositionTests.Keyboard();
        paren.Host.PrecedingText = "ファイルは(";
        paren.Type("~/Documents)wo\n");
        Assert.Equal("~/Documents)を", paren.Host.Document, "確定済みの ( に対応する ) は半角で範囲に入る");
    }

    [Test]
    public static void OrdinaryText_IsNotProtected()
    {
        // 今の結果を確かめて期待値にした (回帰)。守らない: 日本語の箇条書き -kyou・1~2ko・aka/ao・$100・a/b・日付・1/2・///
        var unprotected = new[] { "-kyou", "1~2ko", "aka/ao", "$100", "a/b", "2026-10-05" };
        foreach (var typed in unprotected) Assert.True(!ProtectedSpanTestHelper.IsEnglishSegmentWhole(typed), $"「{typed}」は保護しない");
        // 保護の印 (Protected) も付かない (本家 ProtectionTests の 1/2・/// を含む)
        foreach (var typed in unprotected.Concat(["1/2", "///", "--sorekara"])) Assert.True(!HasProtected(typed), $"「{typed}」に保護の印が付かない");
        // 既にうまくいくもの
        var cases = new Dictionary<string, string>
        {
            ["taro.yamada@example.com"] = "taro.yamada@example.com", ["@yksr_melt arigatou"] = "@yksr_melt ありがとう",
            ["https://example.com wo mite"] = "https://example.com をみて", ["README.mdwo"] = "README.mdを", ["localhost:3000"] = "localhost:3000",
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void ManualLevel_DoesNotProtect()
    {
        // 判定の強さ「手動」は、Shift で打った大文字始まりの語だけを英語にする (Mac 版の既存の規則)。保護区間も守らない (本家は強さに関係なく守る)
        var text = new CompositionText(CompositionTests.Detector) { Level = () => DetectionLevel.Manual };
        foreach (var c in "./build.sh") text.Append(c);
        Assert.True(!text.Segments(final: true).Any(s => s.Protected), "手動では保護区間にしない");
    }

    [Test]
    public static void KanaInput_DoesNotProtect()
    {
        // かな入力では保護区間を使わない (かなのキーを英字として読まない)
        var k = new CompositionTests.Keyboard { Kana = true };
        k.TypeKanaKeys("./a");
        // JIS かなの . / a は る め ち (/ は「め」。「・」ではない)。英字の ./a のまま守らない
        Assert.Equal("るめち", k.Showing, "かな入力では ./a を英字のまま守らない");
    }

    [Test]
    public static void AfterJapaneseCommit_TheNextSlashWordIsNotProtected()
    {
        // 直前に日本語を確定した直後の /fuka・/iie・/oosaka・/2kai は、パスではなく日本語の打ちかけ (査読 R50 必須 1)。守らない
        WithSpellChecker(() =>
        {
            // 変換で確定 (kawa + Space → 川) してそのまま次を打つ
            var converted = new CompositionTests.Keyboard();
            converted.Type("kawa /iie\n");
            Assert.True(converted.Host.Document.EndsWith("いいえ") && !converted.Host.Document.Contains("iie"), "変換で確定 → /iie: " + converted.Host.Document);

            // Enter で確定したあと
            var entered = new CompositionTests.Keyboard();
            entered.Type("kyouha\n");
            entered.Type("/oosaka\n");
            Assert.True(entered.Host.Document.EndsWith("おおさか") && !entered.Host.Document.Contains("oosaka"), "Enter で確定 → /oosaka: " + entered.Host.Document);

            // 変換後も続けて入力 (ContinueAfterConversion) が ON
            var continued = new CompositionTests.Keyboard { Continue = true };
            continued.Type("kawa /2kai\n");
            Assert.True(continued.Host.Document.EndsWith("かい") && !continued.Host.Document.Contains("2kai"), "続けて入力 ON → /2kai: " + continued.Host.Document);

            // 1 音確定 (Shift+↓) で最後の 1 音まで確定したあと
            var firstSound = new CompositionTests.Keyboard { TokMode = true };
            firstSound.Type("kyo");
            ShiftDown(firstSound);
            firstSound.Type("/ashita\n");
            Assert.True(firstSound.Host.Document.EndsWith("あした") && !firstSound.Host.Document.Contains("ashita"), "1 音確定のあと → /ashita: " + firstSound.Host.Document);

            // 1 音だけ確定して残りがある (kyou → きょ + う)。続きの /ashita は固定した単位のあとから呼び直す道 (units[frozen - 1]) を通る (査読 R57 任意 F)
            var partial = new CompositionTests.Keyboard { TokMode = true };
            partial.Type("kyou");
            ShiftDown(partial);
            Assert.True(partial.Host.Document.Length > 0, "前提: 1 音だけ確定した: " + partial.Host.Document);
            partial.Type("/ashita\n");
            Assert.True(partial.Host.Document.EndsWith("あした") && !partial.Host.Document.Contains("ashita"), "1 音確定で残りがある → /ashita: " + partial.Host.Document);

            // キャレットの前が日本語 (空白なし) の入力欄
            var afterJapanese = new CompositionTests.Keyboard();
            afterJapanese.Host.PrecedingText = "ファイルは";
            afterJapanese.Type("/fuka\n");
            Assert.True(afterJapanese.Host.Document.EndsWith("ふか") && !afterJapanese.Host.Document.Contains("fuka"), "入力欄の前が日本語 → /fuka: " + afterJapanese.Host.Document);
        });
    }

    [Test]
    public static void WordStart_AfterSpaceOrAtTheStart_IsProtected()
    {
        WithSpellChecker(() =>
        {
            // 日本語 + 空白 + /tmp/foo (守る)
            var afterSpace = new CompositionTests.Keyboard();
            afterSpace.Host.PrecedingText = "ファイルは ";
            afterSpace.Type("/tmp/foo\n");
            Assert.Equal("/tmp/foo", afterSpace.Host.Document, "空白の後は守る");

            // 入力欄の先頭 (キャレットの前が無い) の /tmp/foo (守る)
            var atStart = new CompositionTests.Keyboard();
            atStart.Type("/tmp/foo\n");
            Assert.Equal("/tmp/foo", atStart.Host.Document, "入力欄の先頭は守る");

            // 開きの記号の後 (守る)
            var afterOpener = new CompositionTests.Keyboard();
            afterOpener.Host.PrecedingText = "ファイルは(";
            afterOpener.Type("/tmp/foo\n");
            Assert.Equal("/tmp/foo", afterOpener.Host.Document, "開きの記号の後は守る");
        });
    }

    [Test]
    public static void SpaceAroundEnglish_KeepsProtectedSpansIntact()
    {
        // 「英語の前後に空白を入れる」が ON なら、設定どおり守った区間の後ろにも空白が入る (本家は末尾が保護区間のとき入れないが、Mac 版は設定どおり)。パスそのものは崩れない (査読 R57 A2)
        Assert.Equal("./build.sh を", Commit("./build.shwo", k => k.SpaceAroundEnglish = true), "守った区間の後ろにも設定どおり空白が入る (パスは崩れない)");
        Assert.Equal("~/Documents を", Commit("~/Documents wo", k => k.SpaceAroundEnglish = true), "打った空白のまま、二重にならない");
        Assert.Equal("~/.config/nvim を", Commit("~/.config/nvim wo", k => k.SpaceAroundEnglish = true), "空白は打ったとおり");
    }

    [Test]
    public static void Protected_IsNotLearnedOrCorrected()
    {
        // 打ち間違いの直し・もしかして・学習の対象にしない (本家 ProtectionTests の MixedProtectedSpans_DoNotAutocorrectRomajiTypos / Protected_IsNotLearnedOrCorrected を参考にした)
        foreach (var raw in new[] { "./onegaishimsu", "/tmp/buresureddo", "~/shumire-shon", "--onegaishimsu" })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(raw);
            Assert.Equal(raw, k.Showing, $"表示も原文のまま: {raw}");
            k.Type("\n");
            Assert.Equal(raw, k.Host.Document, raw);
        }
        // 確定後の F6 → Enter は、MeltypeSession を通さないと経路に届かない (Keyboard は届かない)。LastWordToggleTests.ProtectedSpan_AfterCommit_F6_DoesNotLearn で確かめる

        // 入力中の F6 (ひらがな) → Enter が LearnLanguage を通っても学習しない
        var whileTyping = new LanguageMemory(null);
        var typing = new CompositionTests.Keyboard(languages: whileTyping);
        typing.Type("--Sushi");
        Assert.True(HasProtected("--Sushi"), "前提: --Sushi は保護区間");
        typing.Press(VirtualKeys.F6);
        typing.Press(VirtualKeys.Return);
        Assert.True(whileTyping.Get("--Sushi") is null && whileTyping.Get("Sushi") is null && whileTyping.Get("sushi") is null, "入力中の F6 → Enter で学習しない");
    }

    [Test]
    public static void Protected_Tab_DoesNotSuggestACorrection()
    {
        // もしかして (書き間違いの辞書): 守った区間は英語の単位なので候補を出さない。Tab はアプリに渡り、原文のまま確定する
        var k = new CompositionTests.Keyboard();
        k.Type("/tmp/buresureddo");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("/tmp/buresureddo", k.Host.Document, "Tab で原文のまま確定する");
        Assert.True(k.Host.Events.Any(e => e.EndsWith(":09")), "Tab はアプリへ渡る: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void Protected_IsNotRewrittenByTheNextCommit()
    {
        // 確定し直し (CorrectPreviousCommit): 守った区間の後ろで次の語を確定しても、守った区間は書き換わらない
        var k = new CompositionTests.Keyboard();
        k.Type("/tmp/sushi\n");
        k.Type("sushi\n");
        Assert.True(k.Host.Document.StartsWith("/tmp/sushi"), "守った区間は変わらない: " + k.Host.Document);
    }

    private static bool HasProtected(string typed)
    {
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in typed) text.Append(c);
        return text.Segments(final: true).Any(s => s.Protected);
    }
}

/// <summary>保護区間のテスト用: 打った全体が 1 つの英語の区間になるか。</summary>
internal static class ProtectedSpanTestHelper
{
    public static bool IsEnglishSegmentWhole(string typed)
    {
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in typed) text.Append(c);
        return text.Segments(final: true) is [{ IsEnglish: true }];
    }
}
