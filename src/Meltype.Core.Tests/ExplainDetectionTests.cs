// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 「判定を調べる」タブ (DictionaryManagement.ExplainDetection)。学習はどれも一時フォルダーに作り、ユーザーのデータには触らない。
/// </summary>
internal static class ExplainDetectionTests
{
    private static string Explain(string typed, LanguageMemory memory, string before = "", string after = "", DetectionLevel level = DetectionLevel.Balanced, bool kana = false) =>
        DictionaryManagement.ExplainDetection(typed, before, after, level, kana, CompositionTests.Detector, memory, BuiltInWordChecker.Shared);

    private static string Segments(string result) => result.Split('\n').First(l => l.StartsWith("segments\t", StringComparison.Ordinal))["segments\t".Length..];

    private static string[]? Variant(string result, string name) =>
        result.Split('\n').Select(l => l.Split('\t')).FirstOrDefault(f => f.Length == 4 && f[0] == "variant" && f[1] == name);

    private static LanguageMemory Memory(string path, params (string Word, bool English)[] words)
    {
        var memory = new LanguageMemory(path)
        {
            IsCommonJapanese = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji).IsCommonJapanese,
        };
        foreach (var (word, english) in words) Assert.Equal(null, memory.Set(word, english), $"{word} を足せる");
        return memory;
    }

    private static string TempFile(out Action cleanup)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-explain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        cleanup = () =>
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return Path.Combine(directory, "languages.json");
    }

    [Test]
    public static void Segments_ThreeExamples()
    {
        var empty = new LanguageMemory(null);
        // 英語の 1 語だけ・日本語だけ・混ざった文。形は EN:打った英字 と JA:かな を | でつなぐ (JA は読みのかな)
        Assert.Equal("EN:hello", Segments(Explain("hello", empty)), "英語だけ");
        Assert.Equal("JA:にほんご", Segments(Explain("nihongo", empty)), "日本語だけ");
        Assert.Equal("JA:きょうは|EN:github|JA:をつかう", Segments(Explain("kyouhagithubwotsukau", empty)), "日本語の中の英単語");
    }

    [Test]
    public static void MemoryWithout_ChangesResult()
    {
        var path = TempFile(out var cleanup);
        try
        {
            // api は英語とも日本語 (あぴ) とも読める。日本語と判定される文でも、学習した語は自動の判定より優先される。
            var empty = Explain("apiwotsukau", new LanguageMemory(null));
            var memory = Memory(path, ("api", true));
            var learned = Explain("apiwotsukau", memory);
            Assert.True(Segments(learned) != Segments(empty), $"学習すると区間が変わる ({Segments(empty)} → {Segments(learned)})");
            Assert.True(Segments(learned).StartsWith("EN:api", StringComparison.Ordinal), "api は英語になる: " + Segments(learned));

            var off = Variant(learned, "memory-off");
            Assert.True(off is { } && off[3] == "1" && off[2] == Segments(empty), "学習を全部外すと、学習前と同じ区間に戻る");
            var without = Variant(learned, "memory-without:api");
            Assert.True(without is { } && without[3] == "1" && without[2] == Segments(empty), "api だけ外しても戻る");

            // 区間に含まれない語は、外す候補に出ない
            var other = Memory(TempFile(out var cleanup2), ("github", true), ("api", true));
            try
            {
                Assert.True(Variant(Explain("apiwotsukau", other), "memory-without:github") is null, "含まれない語は出さない");
            }
            finally { cleanup2(); }
        }
        finally { cleanup(); }
    }

    [Test]
    public static void MemoryPending_ListsWordsWaitingForSecondTime()
    {
        var path = TempFile(out var cleanup);
        try
        {
            // kyouha を 1 回だけ英字にした → まだ効かない (2 回目待ち)
            var memory = Memory(path);
            memory.Remember("kyouha", english: true, explicitChoice: true);
            var result = Explain("kyouhasushi", memory);
            Assert.True(result.Split('\n').Contains("memory-pending\tkyouha"), "2 回目待ちの語を出す");
            Assert.True(Variant(result, "memory-without:kyouha") is null, "効いていない語は外す候補にしない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void Level_ChangesResult_AndSkipsCurrent()
    {
        var empty = new LanguageMemory(null);
        // 同じ入力でも、強さを変えると区間が変わる語がある (どれかの強さで変わることを、短い語の組で確かめる)
        var changed = new[] { "kyouhamakeshimasu", "apiwotsukau", "nobody", "makeitso", "nohitoga", "toitsu", "goto", "repo" }
            .Any(t => Enum.GetValues<DetectionLevel>().Any(level =>
                Explain(t, empty, level: level).Split('\n').Any(l => l.StartsWith("variant\tlevel:", StringComparison.Ordinal) && l.EndsWith("\t1", StringComparison.Ordinal))));
        Assert.True(changed, "強さを変えると結果が変わる入力がある");

        var result = Explain("kyouhasushi", empty, level: DetectionLevel.Conservative);
        Assert.True(Variant(result, "level:Conservative") is null, "今の強さは試さない");
        Assert.True(Variant(result, "level:Aggressive") is not null && Variant(result, "level:Balanced") is not null && Variant(result, "level:Manual") is not null, "ほかの 3 つは試す");
    }

    [Test]
    public static void Context_VariantsSkipTheCurrentKind()
    {
        var empty = new LanguageMemory(null);
        var plain = Explain("sushi", empty);
        Assert.True(Variant(plain, "before-english") is not null && Variant(plain, "before-japanese") is not null, "前の文字が無いなら英語・日本語を試す");
        Assert.True(Variant(plain, "before-none") is null && Variant(plain, "after-none") is null, "もう無いものは試さない");
        Assert.True(Variant(plain, "after-english") is not null, "後ろが無いなら後ろが英語を試す");

        var japanese = Explain("sushi", empty, before: "今日は");
        Assert.True(Variant(japanese, "before-japanese") is null && Variant(japanese, "before-none") is not null, "前が日本語なら日本語は試さず、無しは試す");
    }

    [Test]
    public static void DoesNotChangeLanguagesFile()
    {
        var path = TempFile(out var cleanup);
        try
        {
            var memory = Memory(path, ("api", true), ("sushi", false));
            var bytes = File.ReadAllBytes(path);
            var stamp = File.GetLastWriteTimeUtc(path);
            var files = Directory.GetFiles(Path.GetDirectoryName(path)!).Order().ToArray(); // 足したときのロックなども含む
            Thread.Sleep(1100); // 更新時刻が 1 秒単位のファイルシステムでも、書き直せば変わって見えるように
            for (var i = 0; i < 3; i++) Explain("kyouhaapiwotsukausushi", memory);
            Assert.True(bytes.SequenceEqual(File.ReadAllBytes(path)), "中身が同じ");
            Assert.True(stamp == File.GetLastWriteTimeUtc(path), "更新時刻も同じ");
            Assert.True(files.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(path)!).Order()), "ファイルを増やさない");
            Assert.True(memory.Get("api") == true && memory.Get("sushi") == false, "写しを使ったので、元の学習は消えていない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void RestoresDetectorSettings()
    {
        var memory = new LanguageMemory(null);
        var checker = BuiltInWordChecker.Shared;
        var detector = CompositionDetector.CreateDefault();
        var mine = new LanguageMemory(null);
        detector.Memory = mine;
        detector.SpellChecker = null;
        DictionaryManagement.ExplainDetection("hello", "", "", DetectionLevel.Balanced, false, detector, memory, checker);
        Assert.True(ReferenceEquals(detector.Memory, mine) && detector.SpellChecker is null, "渡した判定器の設定は元に戻す");
    }

    [Test]
    public static void LongOrOddContext_DoesNotThrow()
    {
        var empty = new LanguageMemory(null);
        var before = string.Concat(Enumerable.Repeat("今日は good day 😀 ", 10));
        var after = string.Concat(Enumerable.Repeat("😀 hello です ", 10));
        var result = Explain("kyouhasushi", empty, before, after);
        Assert.True(result.StartsWith("segments\t", StringComparison.Ordinal), "20 文字を超える前後の文字でも落ちない");
        // サロゲートペアの途中で切っても落ちない (切り方を変えて何通りか)
        for (var n = 18; n <= 24; n++)
        {
            Explain("sushi", empty, string.Concat(Enumerable.Repeat("😀", n)), string.Concat(Enumerable.Repeat("😀", n)));
        }
        Assert.Equal("segments\t", Explain("", empty), "空の入力は区間なし");
        Assert.Equal("segments\t", Explain("   ", empty), "空白だけも区間なし");
        Assert.True(Segments(Explain(new string('a', 5000), empty)).Length > 0, "長すぎる入力も落ちない");
    }

    [Test]
    public static void ContextIsCutLikeTheIme()
    {
        var empty = new LanguageMemory(null);
        // 20 文字より前は読まない: 日本語の文字が 21 文字目より前にあっても、直前 20 文字がすべて英字なら「前が英語」と同じ答え
        var farJapanese = "今日は" + new string('x', 20);
        Assert.Equal(Explain("sushi", empty, before: new string('x', 20)), Explain("sushi", empty, before: farJapanese), "20 文字より前は読まない");
    }
}
