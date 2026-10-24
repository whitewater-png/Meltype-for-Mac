// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>カスタムローマ字表 (本家 #979 Phase 1 最小版)。既定 OFF・表が無い/空なら従来と同じ。</summary>
internal static class CustomRomajiTableTests
{
    private static void WithTable(string? tsv, Action body)
    {
        var romaji = CompositionTests.Detector.Romaji;
        var previous = romaji.CustomTable;
        var table = tsv is null ? null : RomajiTable.Parse(tsv);
        romaji.CustomTable = () => table;
        try { body(); }
        finally { romaji.CustomTable = previous; }
    }

    private const string Sample = "ry\tらい\nryp\tりょう\nngh\tにゅう\n";

    [Test]
    public static void DefaultIsOff() => Assert.True(!new Settings().CustomRomajiTable, "設定の既定は OFF");

    [Test]
    public static void Catalog_HasToggleWithLocation()
    {
        var item = MacSettingsCatalog.Items.Single(i => i.Key == nameof(Settings.CustomRomajiTable));
        Assert.True(item.Description.Contains("dictionaries/romaji.tsv") && item.Description.Contains("Application Support"), "置き場所を説明文に書く");
    }

    [Test]
    public static void Off_SameAsBefore()
    {
        var romaji = CompositionTests.Detector.Romaji;
        var baseline = new[] { "ryo", "ryp", "kakikukeko", "konnnichiha", "ngh", "tanni" }.Select(w => romaji.ConvertForDisplay(w, true)).ToArray();
        foreach (var tsv in new string?[] { null, "", "# comment only\n", "bad line without tab\n" })
        {
            WithTable(tsv, () =>
            {
                var now = new[] { "ryo", "ryp", "kakikukeko", "konnnichiha", "ngh", "tanni" }.Select(w => romaji.ConvertForDisplay(w, true)).ToArray();
                Assert.Equal(string.Join("|", baseline), string.Join("|", now), "表が無い・空・全行不正なら従来と同じ");
            });
        }
        Assert.Equal(romaji.ConvertLenient("ryo", true), romaji.ConvertForDisplay("ryo", true), "表なしで ConvertForDisplay = ConvertLenient");
    }

    [Test]
    public static void Parse_IgnoresBadLinesAndKeepsGood()
    {
        var warnings = new List<string>();
        var table = RomajiTable.Parse("ry\tらい\n;\tっ\nA1\tあ\nnospace\nab\t\nry\tりぃ\n#c\n\ntoolongkeyxx\tあ\nok\tお\n", warnings);
        Assert.True(table is not null, "有効な行があれば表ができる");
        Assert.Equal(2, table!.Count, "ry と ok だけ");
        Assert.True(table.TryGet("ry", out var kana) && kana == "りぃ", "重複は後の行が勝つ");
        Assert.True(warnings.Count == 6, "不正 5 行 + 重複 1 行 の警告: " + warnings.Count);
        Assert.True(RomajiTable.Parse("only bad\n") is null, "有効行なしは null");
    }

    [Test]
    public static void Load_MissingEmptyOrBrokenFile_IsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meltype-romaji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "romaji.tsv");
            Assert.True(RomajiTable.Load(path) is null, "ファイルなし");
            File.WriteAllText(path, "");
            Assert.True(RomajiTable.Load(path) is null, "空");
            File.WriteAllText(path, "ry\tらい\n");
            Assert.True(RomajiTable.Load(path) is { Count: 1 }, "読める");
            File.WriteAllBytes(path, [0xff, 0xfe, 0x00, 0x01, 0x02]);
            Assert.True(RomajiTable.Load(path) is null, "壊れたファイルでも例外にならず null");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public static void On_LongestMatchReplaces()
    {
        WithTable(Sample, () =>
        {
            var r = CompositionTests.Detector.Romaji;
            Assert.Equal("らい", r.ConvertForDisplay("ry", true), "ry");
            Assert.Equal("りょう", r.ConvertForDisplay("ryp", true), "ryp は ry より長いキーが勝つ");
            Assert.Equal("にゅう", r.ConvertForDisplay("ngh", true), "ngh");
            Assert.Equal("らいt", r.ConvertForDisplay("ryt", true), "後続が表に無ければ短い定義 + 後続はそのまま");
            Assert.Equal("かりょうき", r.ConvertForDisplay("kaRYpki".ToLowerInvariant(), true), "表の外は従来どおり");
            Assert.Equal("らいお", r.ConvertForDisplay("ryo", true), "表が標準より優先: ryo は ry + o");
            Assert.Equal("みょ", r.ConvertForDisplay("myo", true), "表に関係ない綴りは従来どおり");
        });
    }

    [Test]
    public static void On_TableBeatsFixedRules()
    {
        WithTable("kk\tきん\nnk\tにん\n", () =>
        {
            var r = CompositionTests.Detector.Romaji;
            Assert.Equal("きん", r.ConvertForDisplay("kk", true), "kk は っ より表が先");
            Assert.Equal("にん", r.ConvertForDisplay("nk", true), "nk は ん+k より表が先");
        });
    }

    [Test]
    public static void Limitation_NoHold_ShorterKeyCommitsFirst()
    {
        // 表に ka と kak があるとき、保留しないので ka は先に確定する (制限。docs/W11 に記録)。
        WithTable("ka\tか\nkak\tかく\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kak");
            Assert.Equal("かk", k.Showing, "ka が先に確定し、kak には伸びない");
        });
    }

    [Test]
    public static void On_Keyboard_ReplacesInCompositionBox()
    {
        WithTable(Sample, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ryp");
            Assert.Equal("らいp", k.Showing, "保留しないので ry が先に確定する (制限)。ryp 完全一致は一括変換側 (ConvertForDisplay) のみ");
        });
    }

    [Test]
    public static void On_Keyboard_FullKeyArrivingTogetherReplaces()
    {
        // ngh は ng の時点で「途中」(表のキーの先頭) なので英字のまま待ち、h で にゅう に確定する。
        WithTable(Sample, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ngh");
            Assert.Equal("にゅう", k.Showing, "表のキーが揃ったら置換");
        });
    }

    [Test]
    public static void On_EnglishJudgmentUnaffected()
    {
        WithTable("ng\tにゅ\nst\tした\nri\tり\n", () =>
        {
            var r = CompositionTests.Detector.Romaji;
            // 判定用の解析は表を見ない
            Assert.True(!r.AnalyzeFragment("st").IsValid, "st は判定では日本語として読まない");
            Assert.True(!r.Analyze("string").IsValid, "string は従来どおりローマ字として成立しない");
            var k = new CompositionTests.Keyboard();
            k.Type("string ");
            Assert.Equal("string ", string.Concat(k.Host.Output), "英単語は英字のまま");
        });
    }
}
