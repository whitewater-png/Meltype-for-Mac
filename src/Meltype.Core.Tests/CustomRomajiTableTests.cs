// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

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
            Assert.Equal("んg", r.ConvertForDisplay("ng", true), "表キーの途中で確定するときは標準の綴りにフォールバックする");
            var final = new CompositionTests.Keyboard();
            final.Type("ng\n");
            Assert.Equal("んg", string.Concat(final.Host.Output), "Enter でも英字 ng を残さない");
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
        var table = RomajiTable.Parse("ry\tらい\n(\tっ\nA1\tあ\nnospace\nab\t\nry\tりぃ\n#c\n\ntoolongkeyxx\tあ\nok\tお\n", warnings);
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
    public static void Hold_Keyboard_StandardPrefixCanReachLongerKey()
    {
        // 表が kak だけ (ka 無し) でも、ka は標準の か と表示しつつ Raw を保留する。
        WithTable("kak\tかく\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ka");
            Assert.Equal("か", k.Showing, "標準で読める途中は従来どおり か と見せる");
            k.Type("k");
            Assert.Equal("かく", k.Showing, "キーボードでも kak に届く");
            Assert.Equal("かく", CompositionTests.Detector.Romaji.ConvertForDisplay("kak", true), "一括変換と同じ");
        });
    }

    [Test]
    public static void Hold_ShorterKeyWaitsForLongerKey()
    {
        // 短いキーは表示だけ確定し、より長いキーの可能性がある間は Raw を保留する。
        WithTable("ka\tか\nkak\tかく\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ka");
            Assert.Equal("か", k.Showing, "短いキーの表示は従来どおり");
            k.Type("k");
            Assert.Equal("かく", k.Showing, "kak が揃ったら長いキーで置き換える");
        });
    }

    [Test]
    public static void Hold_LongerKeyBeyondShortKeyAndStandardPartial()
    {
        // 査読 A-1: 短い表キーがあると、2 文字以上先の長いキーに届かなかった。
        WithTable("ka\tか\nkaku\t長\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ka");
            Assert.Equal("か", k.Showing, "短いキーは表示だけ確定");
            k.Type("k");
            Assert.Equal("かk", k.Showing, "長いキーの途中は短いキー + 標準の途中");
            k.Type("u");
            Assert.Equal("長", k.Showing, "kaku に届いたら長いキーで置き換える");

            var mismatch = new CompositionTests.Keyboard();
            mismatch.Type("kat");
            Assert.Equal("かt", mismatch.Showing, "不一致なら短いキーで確定");

            var enter = new CompositionTests.Keyboard();
            enter.Type("ka\n");
            Assert.Equal("か", string.Concat(enter.Host.Output), "Enter は短いキーで確定");
        });

        WithTable("n\tん\nngh\tにゅう\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("n");
            Assert.Equal("ん", k.Showing, "短いキー n は ん と表示");
            k.Type("g");
            Assert.Equal("んg", k.Showing, "ngh の途中は標準の途中形");
            k.Type("h");
            Assert.Equal("にゅう", k.Showing, "ngh に届く");

            var enter = new CompositionTests.Keyboard();
            enter.Type("ng\n");
            Assert.Equal("んg", string.Concat(enter.Host.Output), "途中で確定したら標準の綴りに戻す");
        });
    }

    [Test]
    public static void On_Keyboard_ReplacesInCompositionBox()
    {
        WithTable(Sample, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ryp");
            Assert.Equal("りょう", k.Showing, "ry を保留し、p が来た時点で ryp に置き換える");
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
    public static void Hold_Mismatch_CommitsShortKeyAndProcessesRest()
    {
        WithTable(Sample + "ka\tか\nkak\tかく\n", () =>
        {
            var r = new CompositionTests.Keyboard();
            r.Type("ryt");
            Assert.Equal("らいt", r.Showing, "ryp に届かない t は短いキーのあと標準どおり残す");
            var k = new CompositionTests.Keyboard();
            k.Type("kat");
            Assert.Equal("かt", k.Showing, "kak に届かないときも短いキーで確定する");
        });
    }

    [Test]
    public static void Hold_EnterSpaceAndEscape_FinalizeShortKey()
    {
        WithTable("ka\tか\nkak\tかく\n", () =>
        {
            var enter = new CompositionTests.Keyboard();
            enter.Type("ka\n");
            Assert.Equal("か", string.Concat(enter.Host.Output), "Enter は短いキーで確定");

            var space = new CompositionTests.Keyboard();
            space.Type("ka ");
            Assert.Equal("か", space.Showing, "Space の変換読みも短いキー");
            space.Press(VirtualKeys.Escape);
            Assert.Equal("か", space.Showing, "変換を Esc で戻しても短いキーの読みを保持");
            space.Type("\n");
            Assert.Equal("か", string.Concat(space.Host.Output), "Esc 後の Enter でも英字が残らない");

            var cancel = new CompositionTests.Keyboard();
            cancel.Type("ka");
            cancel.Press(VirtualKeys.Escape);
            Assert.True(cancel.Showing is null && cancel.Host.Output.Count == 0, "入力中の Esc は従来どおり取り消し");
        });
    }

    [Test]
    public static void Hold_Backspace_CanRetypeLongerKey()
    {
        WithTable("ka\tか\nkak\tかく\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kak\b");
            Assert.Equal("か", k.Showing, "長いキーの 1 文字を消すと短いキーの表示に戻る");
            k.Type("k");
            Assert.Equal("かく", k.Showing, "打ち直せる");
        });
    }

    [Test]
    public static void Hold_TableReplacement_IsUsedOnNextKey()
    {
        var romaji = CompositionTests.Detector.Romaji;
        var previous = romaji.CustomTable;
        var table = RomajiTable.Parse("ka\tか\nkak\tかく\n");
        romaji.CustomTable = () => table;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ka");
            table = RomajiTable.Parse("ka\tか\n");
            k.Type("t");
            Assert.Equal("かt", k.Showing, "長いキーを消した表では保留を伸ばさず短いキーで確定する");
        }
        finally { romaji.CustomTable = previous; }
    }

    [Test]
    public static void Hold_InvalidLongerLine_DoesNotCreatePrefix()
    {
        WithTable("ka\tか\nkak\t\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kak");
            Assert.Equal("かk", k.Showing, "無効な長いキーは保留の根拠にしない");
        });
    }

    [Test]
    public static void Hold_NAndNn_KeepStandardFinalization()
    {
        WithTable("ngh\tにゅう\n", () =>
        {
            var n = new CompositionTests.Keyboard();
            n.Type("n\n");
            Assert.Equal("ん", string.Concat(n.Host.Output), "n は確定時に ん");
            var nn = new CompositionTests.Keyboard();
            nn.Type("nn");
            Assert.Equal("ん", nn.Showing, "nn は従来どおり即時に ん");
        });
    }

    [Test]
    public static void Hold_Keyboard_TableStillBeatsSokuonAndN()
    {
        WithTable("kk\tきん\nnk\tにん\n", () =>
        {
            var kk = new CompositionTests.Keyboard();
            kk.Type("kk");
            Assert.Equal("きん", kk.Showing, "キーボードでも kk は っ より表が先");
            var nk = new CompositionTests.Keyboard();
            nk.Type("nk");
            Assert.Equal("にん", nk.Showing, "キーボードでも nk は ん+k より表が先");
        });
    }

    [Test]
    public static void Hold_EnglishJudgment_UnaffectedByLongerPrefix()
    {
        WithTable("st\tした\nstr\tスト\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("string ");
            Assert.Equal("string ", string.Concat(k.Host.Output), "表の短縮キーとその長い接頭辞があっても英単語は英字のまま");
        });
    }

    [Test]
    public static void Hold_Symbol_FinalizesStandardPartial()
    {
        WithTable("ngh\tにゅう\n", () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ng.");
            Assert.Equal("んg。", k.Showing, "記号で終わるときも標準の綴りで確定する");
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
