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

    private static readonly string[] OffProbes =
    {
        "ryo", "ryp", "kakikukeko", "konnnichiha", "ngh", "tanni", "konnichiwa", "nihongo", "shinbun", "kyou", "gakkou", "matcha",
        "xtu", "vu", "thi", "ltu", "nn", "n", "tt", "sensei", "ohayou", "arigatou", "string", "ka", "kak", "kakk",
    };

    private static string[] ProbeAll(bool final) =>
        OffProbes.Select(w => CompositionTests.Detector.Romaji.ConvertForDisplay(w, final)).ToArray();

    [Test]
    public static void Off_SameAsBefore()
    {
        var romaji = CompositionTests.Detector.Romaji;
        var baseline = ProbeAll(true);
        var baselineMid = ProbeAll(false);
        // 表が非 null でも入力と無関係 (zz→x) なら、従来 (CustomTable なし) と完全に一致する。
        foreach (var tsv in new string?[] { null, "", "# comment only\n", "bad line without tab\n", "zz\tx\n", "qq\tき\nzzz\tx\n" })
        {
            WithTable(tsv, () =>
            {
                Assert.Equal(string.Join("|", baseline), string.Join("|", ProbeAll(true)), "表が無い・空・無関係なら従来と同じ (final): " + tsv);
                Assert.Equal(string.Join("|", baselineMid), string.Join("|", ProbeAll(false)), "表が無い・空・無関係なら従来と同じ (途中): " + tsv);
            });
        }
        Assert.Equal(romaji.ConvertLenient("ryo", true), romaji.ConvertForDisplay("ryo", true), "表なしで ConvertForDisplay = ConvertLenient");
    }

    [Test]
    public static void Off_Keyboard_UnrelatedTableTypesSameAsBefore()
    {
        string TypeAll()
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kakikukeko konnichiwa ");
            return string.Concat(k.Host.Output) + "|" + k.Showing;
        }
        var before = TypeAll();
        string? with = null;
        WithTable("zz\tx\n", () => with = TypeAll());
        Assert.Equal(before, with, "無関係な表では打鍵の結果も従来と同じ");
    }

    [Test]
    public static void Off_Wiring_DoesNotLoadWhenSettingOff()
    {
        var calls = 0;
        var on = false;
        var table = RomajiTable.Parse("ry\tらい\n");
        var provider = Meltype.Composition.MeltypeSession.CustomTableProvider(() => on, _ => { calls++; return table; }, "/nonexistent/romaji.tsv");
        Assert.True(provider() is null && calls == 0, "OFF のときは Load を呼ばず null");
        on = true;
        Assert.True(ReferenceEquals(provider(), table) && calls == 1, "ON のとき Load を呼ぶ");
        on = false;
        Assert.True(provider() is null && calls == 1, "OFF に戻したらまた呼ばない");
    }

    [Test]
    public static void H1_PartialPrefix_StandardReadableRest_IsNotHeld()
    {
        // 表に kak だけあり ka が無い。ka は標準で完結して読めるので か にする (待たない)。
        WithTable("kak\tかく\n", () =>
        {
            var r = CompositionTests.Detector.Romaji;
            Assert.Equal("か", r.ConvertForDisplay("ka", true), "ka (final) は か");
            Assert.Equal("か", r.ConvertForDisplay("ka", false), "ka (途中) も か");
            Assert.Equal("かく", r.ConvertForDisplay("kak", true), "kak は表どおり");
            Assert.Equal("かくあ", r.ConvertForDisplay("kaka", true), "最長一致で kak が先 (表が標準より優先)");
            var k = new CompositionTests.Keyboard();
            k.Type("ka ");
            Assert.True(k.Showing is not null && !k.Showing.Contains("ka"), "ka+Space の変換ボックスに英字 ka が残らない: " + k.Showing);
            var e = new CompositionTests.Keyboard();
            e.Type("ka\n");
            Assert.Equal("か", string.Concat(e.Host.Output), "ka+Enter は か");
        });
    }

    [Test]
    public static void H1_StandardPartial_StillHeld()
    {
        // 標準では途中 (ng の g) の残りは、表のキー (ngh) の先頭なら引き続き待つ。
        WithTable("ngh\tにゅう\n", () =>
        {
            var r = CompositionTests.Detector.Romaji;
            var mid = r.AnalyzeFragmentForKana("ng");
            Assert.True(mid.IsValid && mid.Partial == "ng", "ng は途中扱い: Partial=" + mid.Partial);
            Assert.Equal("にゅう", r.ConvertForDisplay("ngh", true), "ngh");
            Assert.Equal("ng", r.ConvertForDisplay("ng", true), "待ちのまま確定すると英字のまま (docs 制限 4)");
            var k = new CompositionTests.Keyboard();
            k.Type("ngh");
            Assert.Equal("にゅう", k.Showing, "ngh を打つと にゅう");
        });
    }

    [Test]
    public static void M1_Load_DoesNotBlockOnFifoOrLinkedSpecialFile()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "meltype-romaji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fifo = Path.Combine(dir, "fifo");
            var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false });
            mk!.WaitForExit();
            Assert.True(mk.ExitCode == 0, "mkfifo");
            var linkToFifo = Path.Combine(dir, "romaji.tsv");
            File.CreateSymbolicLink(linkToFifo, fifo);
            Assert.True(LoadWithin(linkToFifo, out var viaLink) && viaLink is null, "FIFO へのシンボリックリンクで固まらず null");
            Assert.True(LoadWithin(fifo, out var direct) && direct is null, "FIFO 直接でも固まらず null");
            var linkToDir = Path.Combine(dir, "dirlink");
            File.CreateSymbolicLink(linkToDir, dir);
            Assert.True(LoadWithin(linkToDir, out var viaDir) && viaDir is null, "ディレクトリへのリンクは null");
            // 通常ファイルへのリンクは読める
            var real = Path.Combine(dir, "real.tsv");
            File.WriteAllText(real, "ry\tらい\n");
            var okLink = Path.Combine(dir, "oklink.tsv");
            File.CreateSymbolicLink(okLink, real);
            Assert.True(LoadWithin(okLink, out var ok) && ok is { Count: 1 }, "通常ファイルへのリンクは読める");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static bool LoadWithin(string path, out RomajiTable? table)
    {
        RomajiTable? result = null;
        var task = Task.Run(() => result = RomajiTable.Load(path));
        var finished = task.Wait(TimeSpan.FromSeconds(5));
        table = result;
        return finished;
    }

    [Test]
    public static void M1_Load_OverOneMegabyte_IsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meltype-romaji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "romaji.tsv");
            File.WriteAllText(path, "ry\tらい\n" + new string('#', 1_100_000));
            Assert.True(RomajiTable.Load(path) is null, "1MB 超は無効");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public static void M2_Load_TransientFailureIsRetried()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var dir = Path.Combine(Path.GetTempPath(), "meltype-romaji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "romaji.tsv");
        try
        {
            File.WriteAllText(path, "ry\tらい\n");
            File.SetUnixFileMode(path, UnixFileMode.None);
            Assert.True(RomajiTable.Load(path) is null, "読めない間は null");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.True(RomajiTable.Load(path) is { Count: 1 }, "更新日時・大きさが同じでも次の呼び出しで読み直す");
        }
        finally
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public static void M3_CustomCache_ConcurrentSwitchingTables()
    {
        var a = RomajiTable.Parse("ry\tらい\n")!;
        var b = RomajiTable.Parse("ry\tりょ\n")!;
        var romaji = new RomajiDetector();
        var errors = 0;
        Parallel.For(0, 4000, n =>
        {
            var useA = n % 2 == 0;
            romaji.CustomTable = () => useA ? a : b;
            try
            {
                var result = romaji.AnalyzeFragmentForKana("ry");
                if (!result.IsValid) Interlocked.Increment(ref errors);
            }
            catch { Interlocked.Increment(ref errors); }
        });
        Assert.Equal(0, errors, "テーブルを切り替えながら並行に解析しても例外・不正なし");
        var single = new RomajiDetector { CustomTable = () => a };
        Assert.Equal("らい", string.Concat(single.AnalyzeFragmentForKana("ry").Tokens.Select(t => t.Kana)), "表 a の結果");
        single.CustomTable = () => b;
        Assert.Equal("りょ", string.Concat(single.AnalyzeFragmentForKana("ry").Tokens.Select(t => t.Kana)), "表を替えたらキャッシュも替わる");
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
    public static void Load_Utf8Bom_FirstLineStillWorks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meltype-romaji-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "romaji.tsv");
            File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("ry\tらい\nst\tした\n")]);
            var table = RomajiTable.Load(path);
            Assert.True(table is { Count: 2 }, "BOM つきでも 1 行目が効く (2 行とも有効)");
            Assert.True(table!.TryGet("ry", out var kana) && kana == "らい", "1 行目 ry が効く");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public static void Limitation_Keyboard_KeyThatIsPrefixOfStandardSpelling_Unreachable()
    {
        // 表が kak だけ (ka 無し) のとき、キーボードでは ka が先に か になり kak の定義には届かない (制限。docs/W11 制限 1・4)。
        // 一括 (ConvertForDisplay) なら かく。
        // 保留機構 B1/B2 を実装したらキーボードでも かく になるので、この Assert は赤になる。そのとき期待値を "かく" に直すこと。
        WithTable("kak\tかく\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kak");
            Assert.Equal("かk", k.Showing, "現状の制限: ka が先に確定する (B1 実装時に赤→ かく に直す)");
            Assert.Equal("かく", CompositionTests.Detector.Romaji.ConvertForDisplay("kak", true), "一括なら届く");
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
