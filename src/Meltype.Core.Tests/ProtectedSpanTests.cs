using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 1-4a 保護区間 (P42): パス・オプション・変数は、語の頭から空白まで英字のまま (半角)。
/// パスの判定は本家 1.2.1 の ProtectionTests.cs (GPL-3.0) の該当テストを、Mac 版の道具 (Keyboard) で写したもの。
/// </summary>
internal static class ProtectedSpanTests
{
    private static string Commit(string typed)
    {
        // 製品と同じく、英単語の判定にスペルチェッカーを使う (cd・ls を英語として扱う)
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
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
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void Variables_StayHalfWidthEnglish()
    {
        foreach (var typed in new[] { "$HOME", "${PATH}", "echo $HOME" }) Assert.Equal(typed, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void OrdinaryText_IsNotProtected()
    {
        // 今の結果を確かめて期待値にした (回帰)。守らない: 日本語の箇条書き -kyou・1~2ko・aka/ao・$100・a/b・日付
        var unprotected = new[] { "-kyou", "1~2ko", "aka/ao", "$100", "a/b", "2026-10-05" };
        foreach (var typed in unprotected) Assert.True(!ProtectedSpanTestHelper.IsEnglishSegmentWhole(typed), $"「{typed}」は保護しない");
        // 既にうまくいくもの
        var cases = new Dictionary<string, string>
        {
            ["taro.yamada@example.com"] = "taro.yamada@example.com", ["@yksr_melt arigatou"] = "@yksr_melt ありがとう",
            ["https://example.com wo mite"] = "https://example.com をみて", ["README.mdwo"] = "README.mdを", ["localhost:3000"] = "localhost:3000",
        };
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Commit(typed), $"「{typed}」");
    }

    [Test]
    public static void KanaInput_DoesNotProtect()
    {
        // JIS かなの / = ・ なので、かな入力では保護区間を使わない
        var k = new CompositionTests.Keyboard { Kana = true };
        k.TypeKanaKeys("./a");
        // JIS かなの . / a は る め ち。英字の ./a のまま守らない
        Assert.Equal("るめち", k.Showing, "かな入力では ./a を英字のまま守らない");
    }

    [Test]
    public static void Protected_IsNotLearnedOrCorrected()
    {
        // 打ち間違いの直し・もしかして・学習の対象にしない (本家 ProtectionTests の MixedProtectedSpans_DoNotAutocorrectRomajiTypos / Protected_IsNotLearnedOrCorrected を写した)
        foreach (var raw in new[] { "./onegaishimsu", "/tmp/buresureddo", "~/shumire-shon", "--shumire-shon" })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(raw);
            Assert.Equal(raw, k.Showing, $"表示も原文のまま: {raw}");
            k.Type("\n");
            Assert.Equal(raw, k.Host.Document, raw);
        }
        // 確定後の F6 で、保護区間を日本語として学習しない
        var languages = new LanguageMemory(null);
        var memory = new CompositionTests.Keyboard(languages: languages);
        memory.Type("./build.sh\n");
        memory.Press(VirtualKeys.F6);
        Assert.True(languages.Get("./build.sh") is null && languages.Get("build") is null && languages.Get("sh") is null, "保護区間は学習しない");
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
