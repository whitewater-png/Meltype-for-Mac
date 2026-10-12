// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 「学習した語」(英語 / 日本語の学習 = languages.json) の一覧を、辞書の管理画面 (IME とは別のプロセス) から足す・直す・消す。
/// 「別のプロセス」は、同じファイルを持つ別のインスタンス (IME 側・画面側) で代える。ファイルはどれも一時フォルダーに作り、ユーザーのデータには触らない。
/// </summary>
internal static class LearnedWordsTests
{
    private static string TempFile(out Action cleanup)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-learned-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var previous = LanguageMemory.FileCheckIntervalMs;
        cleanup = () =>
        {
            LanguageMemory.FileCheckIntervalMs = previous;
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return Path.Combine(directory, "languages.json");
    }

    private static LanguageMemory.Learned? Find(LanguageMemory memory, string word) => memory.Entries().FirstOrDefault(e => e.Word == word);

    [Test]
    public static void Check_RejectsBadInputAndDuplicates()
    {
        var memory = new LanguageMemory(null);
        Assert.Equal(null, memory.Check("api"), "ふつうの語");
        Assert.Equal(null, memory.Check("  API "), "大文字・前後の空白は直して調べる");
        Assert.True(memory.Check("") is not null, "空");
        Assert.True(memory.Check("   ") is not null, "空白だけ");
        Assert.True(memory.Check("a") is not null, "1 文字");
        Assert.True(memory.Check(new string('a', Meltype.Detection.WordList.MaxWordLength + 1)) is not null, "長すぎる");
        Assert.Equal(null, memory.Check(new string('a', Meltype.Detection.WordList.MaxWordLength)), "上限ちょうど");
        Assert.True(memory.Check("ab") is null && memory.Check("a b") is not null, "空白の入った語");
        Assert.True(memory.Check("git-hub") is not null && memory.Check("api2") is not null && memory.Check("すし") is not null && memory.Check("ｓｕｓｈｉ") is not null, "英字以外");
        memory.Remember("api", english: true, explicitChoice: true);
        Assert.True(memory.Check("api") is not null && memory.Check("API") is not null, "もうある語は重複");
        Assert.Equal(null, memory.Check("api", except: "api"), "編集中の元の語とは重複にしない");
        Assert.Equal(null, memory.Check("API", except: "Api"), "編集中の元の語 (大文字でも)");
        Assert.True(memory.Check("api", except: "sushi") is not null, "ほかの語の編集なら重複");
    }

    [Test]
    public static void Set_AddsActiveEntry_EvenForCommonJapanese()
    {
        var typos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji);
        var memory = new LanguageMemory(null) { IsCommonJapanese = typos.IsCommonJapanese, IsReadableRomaji = _ => true };
        Assert.Equal(null, memory.Set("kyouha", english: true), "足せる");
        Assert.Equal(true, memory.Get("kyouha") ?? false, "よく使う日本語でも、手で足した語はすぐ英語 (2 回目を待たない)");
        Assert.True(Find(memory, "kyouha") is { English: true, Active: true, Count: >= 2 }, "一覧でも有効");
        Assert.Equal(null, memory.Set("Go", english: true), "2 文字の読み切れるローマ字も");
        Assert.Equal(true, memory.Get("go") ?? false, "go もすぐ英語 (大文字は小文字にして覚える)");
        Assert.Equal(null, memory.Set("sushi", english: false), "かなにする語");
        Assert.Equal(false, memory.Get("sushi") ?? true, "sushi はかな");
        Assert.True(memory.Set("sushi", english: true) is not null, "同じ語をもう一度足すのは重複");
        Assert.True(memory.Set("x", english: true) is not null && memory.Set("ab1", english: true) is not null, "不正な語は足さない");
        Assert.Equal(3, memory.Count, "足した語だけ");

        // 1 回だけ英字にした kyouha (2 回目待ち) を、一覧で直すと有効になる。
        var waiting = new LanguageMemory(null) { IsCommonJapanese = typos.IsCommonJapanese };
        waiting.Remember("kyouha", english: true);
        Assert.True(Find(waiting, "kyouha") is { Active: false }, "2 回目を待っている");
        Assert.Equal(null, waiting.Set("kyouha", english: true, oldWord: "kyouha"), "そのまま保存");
        Assert.True(Find(waiting, "kyouha") is { Active: true }, "手で保存したら有効");
        Assert.Equal(true, waiting.Get("kyouha") ?? false, "Get でも英語");
    }

    [Test]
    public static void Set_EditsLanguageAndRenames()
    {
        var memory = new LanguageMemory(null);
        memory.Remember("xnotoukouha", english: true, explicitChoice: true);
        memory.Remember("xnotoukouha", english: true, explicitChoice: true);
        memory.Remember("xnotoukouha", english: true, explicitChoice: true);
        var before = Find(memory, "xnotoukouha")!;
        var version = memory.Version;

        // 英字 → かなに直す (同じ語: 最後に使った日は残し、回数は新しい向きで数え直す)。
        Assert.Equal(null, memory.Set("xnotoukouha", english: false, oldWord: "xnotoukouha"), "英字 / かなを変える");
        Assert.Equal(false, memory.Get("xnotoukouha") ?? true, "かなになった");
        var flipped = Find(memory, "xnotoukouha")!;
        Assert.True(flipped.Used == before.Used && flipped.Count == 2 && flipped.Active, "最後に使った日は同じ・回数は 2・有効");
        Assert.True(memory.Version > version, "版が上がる");

        // 同じ向きのまま保存し直すと、回数は残す。
        memory.Remember("api", english: true, explicitChoice: true);
        memory.Remember("api", english: true, explicitChoice: true);
        memory.Remember("api", english: true, explicitChoice: true);
        Assert.Equal(null, memory.Set("api", english: true, oldWord: "api"), "同じ内容");
        Assert.Equal(3, Find(memory, "api")!.Count, "回数は残す");

        // 綴りを直す: 元の語は消え、新しい語が入る。
        Assert.Equal(null, memory.Set("NoToukouha", english: false, oldWord: "xnotoukouha"), "綴りを直す");
        Assert.True(Find(memory, "xnotoukouha") is null && memory.Get("xnotoukouha") is null, "元の語は無い");
        Assert.True(Find(memory, "notoukouha") is { English: false, Active: true }, "新しい語 (小文字)");
        Assert.True(memory.Set("api", english: true, oldWord: "notoukouha") is not null, "ほかの語の綴りに直すのは重複");
        Assert.True(Find(memory, "notoukouha") is not null, "重複で失敗したら元の語は残る");
        Assert.True(memory.Set("abc", english: true, oldWord: "nosuchword") is not null, "無い語は直せない");
        Assert.True(Find(memory, "abc") is null, "無い語を直そうとしても足さない");
    }

    [Test]
    public static void Remove_ForgetsWords_AndSaves()
    {
        var path = TempFile(out var cleanup);
        try
        {
            var memory = new LanguageMemory(path);
            memory.Set("api", english: true);
            memory.Set("sushi", english: false);
            memory.Set("github", english: true);
            var version = memory.Version;
            Assert.Equal(null, memory.Remove(["API", "sushi", "nosuchword", ""]), "消す (大文字・無い語・空は気にしない)");
            Assert.True(memory.Get("api") is null && memory.Get("sushi") is null && memory.Get("github") == true, "消した語だけ消える");
            Assert.True(memory.Version > version, "版が上がる");
            version = memory.Version;
            Assert.Equal(null, memory.Remove(["nosuchword"]), "無い語だけなら何もしない");
            Assert.Equal(version, memory.Version, "何も消さなければ版は同じ");
            var reloaded = new LanguageMemory(path);
            Assert.True(reloaded.Count == 1 && reloaded.Get("github") == true, "ファイルにも残っていない");
        }
        finally { cleanup(); }
    }

    /// <summary>
    /// IME (入力中にメモリに持ち続ける) と画面 (別のプロセス) が同じ languages.json を持つ。
    /// 画面で消した語を、IME が使い続けたり、IME の次の保存で書き戻したりしない。画面で足した語は IME でも効く。
    /// </summary>
    [Test]
    public static void TwoProcesses_RemoveAndAdd_AreSeenByTheOther()
    {
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var ime = new LanguageMemory(path);
            ime.Remember("xnotoukouha", english: true, explicitChoice: true);
            ime.Remember("api", english: true, explicitChoice: true);
            Assert.Equal(true, ime.Get("xnotoukouha") ?? false, "IME が間違えて覚えた");

            var app = new LanguageMemory(path);
            Assert.True(app.Entries().Any(e => e.Word == "xnotoukouha"), "画面にも出る");
            var imeVersion = ime.Version;
            Assert.Equal(null, app.Remove(["xnotoukouha"]), "画面で消す");

            Assert.True(ime.Get("xnotoukouha") is null, "IME でもすぐ効かなくなる");
            Assert.True(ime.Version > imeVersion, "IME の版も上がる (判定の結果を使い回さない)");
            ime.Remember("sushi", english: false, explicitChoice: true);
            var after = new LanguageMemory(path);
            Assert.True(after.Get("xnotoukouha") is null, "IME の次の保存で書き戻さない");
            Assert.True(after.Get("sushi") == false && after.Get("api") == true, "IME の保存は最新のファイルに重ねる");
            Assert.True(app.Entries().Any(e => e.Word == "sushi"), "画面にも IME の学習が出る");

            var appVersion = app.Version;
            Assert.Equal(null, app.Set("kubernetes", english: true), "画面で足す");
            Assert.Equal(true, ime.Get("kubernetes") ?? false, "IME でも効く");
            Assert.True(app.Version > appVersion, "画面の版が上がる");

            // 画面で直す (英字 → かな) と、IME も直った方を使う。
            Assert.Equal(null, app.Set("api", english: false, oldWord: "api"), "画面で直す");
            Assert.Equal(false, ime.Get("api") ?? true, "IME も直った方");

            // 画面の Check も最新のファイルで重複を見る (IME が今覚えた語)。
            ime.Remember("github", english: true, explicitChoice: true);
            Assert.True(app.Check("github") is not null, "ほかのプロセスが覚えた語も重複");

            // 画面の Refresh は、ほかのプロセスが書いたときだけ版を上げる。
            var v = app.Version;
            Assert.True(!app.Refresh() && app.Version == v, "変わっていなければ読み直さない");
            ime.Remember("swift", english: true, explicitChoice: true);
            Assert.True(app.Refresh() && app.Version > v, "書き換わったら読み直して版を上げる");

            // 「学習データをすべて消去」をほかのプロセスでしたとき (ファイルが空になる)。
            app.Clear();
            Assert.True(ime.Get("github") is null && ime.Entries().Count == 0, "IME も空になる");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void Get_ChecksTheFileOnlyAfterTheInterval()
    {
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 60_000;
            var ime = new LanguageMemory(path);
            ime.Remember("api", english: true, explicitChoice: true);
            Assert.Equal(true, ime.Get("api") ?? false, "覚えた");
            new LanguageMemory(path).Remove(["api"]);
            // キーごとの Get では毎回ファイルを調べない (間隔の内側)。一覧・変更の前は間隔を待たずに読み直す。
            Assert.Equal(true, ime.Get("api") ?? false, "間隔の内側では、まだ前の内容");
            Assert.True(ime.Entries().Count == 0 && ime.Get("api") is null, "一覧を取ると読み直す");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void Remember_MergesWithLatestFile_EvenBeforeGetRefreshes()
    {
        // キーごとの Get が読み直す前 (間隔の内側) に IME が別の語を覚えても、画面で消した語を書き戻さず、画面で足した語も消さない。
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 60_000;
            var ime = new LanguageMemory(path);
            ime.Remember("xnotoukouha", english: true, explicitChoice: true);
            var app = new LanguageMemory(path);
            app.Remove(["xnotoukouha"]);
            app.Set("kubernetes", english: true);
            ime.Remember("sushi", english: false, explicitChoice: true);
            var after = new LanguageMemory(path);
            Assert.True(after.Get("xnotoukouha") is null, "消した語を書き戻さない");
            Assert.True(after.Get("kubernetes") == true && after.Get("sushi") == false, "画面で足した語も IME の学習も残る");
            Assert.True(ime.Get("xnotoukouha") is null && ime.Get("kubernetes") == true, "IME のメモリも最新");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void BrokenFile_DuringRefresh_KeepsMemory()
    {
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var ime = new LanguageMemory(path);
            ime.Remember("api", english: true, explicitChoice: true);
            File.WriteAllText(path, "{ broken");
            Assert.Equal(true, ime.Get("api") ?? false, "読めないファイルに変わっても、今の内容で続ける");
            ime.Remember("sushi", english: false, explicitChoice: true);
            var reloaded = new LanguageMemory(path);
            Assert.True(reloaded.Get("api") == true && reloaded.Get("sushi") == false, "次の保存で読める内容に書き直す");
            // 手で書いた不正なキー・null の値は、読み直しでも入れない。
            File.WriteAllText(path, "{\"Bad\":{\"English\":true},\"ok\":{\"English\":true,\"Count\":2},\"x\":{\"English\":true},\"nul\":null}");
            Assert.True(ime.Entries().Select(e => e.Word).SequenceEqual(["ok"]), "正しいキーだけ");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void FfiFormat_LearnedLines()
    {
        var used = new DateTime(2026, 10, 12, 3, 4, 5, DateTimeKind.Utc);
        var text = DictionaryManagement.FormatLearned([
            new LanguageMemory.Learned("api", true, 3, used, true),
            new LanguageMemory.Learned("kyouha", false, 1, DateTime.SpecifyKind(used, DateTimeKind.Unspecified), false),
        ]);
        Assert.Equal("api\tE\t3\t2026-10-12T03:04:05.0000000Z\t1\nkyouha\tJ\t1\t2026-10-12T03:04:05.0000000Z\t0", text, "語・E/J・回数・UTC の日時・有効");
        Assert.Equal("", DictionaryManagement.FormatLearned([]), "0 語なら空文字列");
        Assert.True(DictionaryManagement.ParseLearnedWords("api\r\n\nsushi\tJ\t1\n  go \n").SequenceEqual(["api", "sushi", "go"]), "改行で区切った語 (一覧の行でもよい)");
        Assert.Equal(0, DictionaryManagement.ParseLearnedWords(null).Count, "NULL は 0 語");
    }

    [Test]
    public static void FfiExports_LearnedFunctionsExist()
    {
        // 画面の Swift (NativeDictionary) が名前で探す入口。名前・引数は管理画面側と合わせてある (変えるなら AbiVersion も上げる)。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Meltype.Mac.Native", "Exports.cs"))) directory = directory.Parent;
        if (directory is null) return;
        var exports = File.ReadAllText(Path.Combine(directory.FullName, "src", "Meltype.Mac.Native", "Exports.cs"));
        foreach (var signature in new[]
                 {
                     "\"meltype_learned_words\")]\n    public static byte* LearnedWords()",
                     "\"meltype_learned_version\")]\n    public static int LearnedVersion()",
                     "\"meltype_learned_check\")]\n    public static byte* LearnedCheck(byte* word, byte* exceptWord)",
                     "\"meltype_learned_set\")]\n    public static byte* LearnedSet(byte* word, int english, byte* oldWord)",
                     "\"meltype_learned_remove\")]\n    public static byte* LearnedRemove(byte* lines)",
                 })
        {
            Assert.True(exports.Replace("\r\n", "\n").Contains(signature), $"FFI の入口 {signature[..signature.IndexOf('"', 1)]}");
        }
        // 引数の文字列への変換 (FromUtf8) も try の内側で行う (例外を UnmanagedCallersOnly の外へ出さないため)。
        var source = exports.Replace("\r\n", "\n");
        foreach (var name in new[] { "LearnedCheck(", "LearnedSet(", "LearnedRemove(" })
        {
            var start = source.IndexOf("public static byte* " + name, StringComparison.Ordinal);
            var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
            var body = source[start..end];
            var conversion = body.IndexOf("FromUtf8(", StringComparison.Ordinal);
            var guard = body.IndexOf("try", StringComparison.Ordinal);
            Assert.True(conversion > 0 && guard > 0 && guard < conversion, $"{name} の FromUtf8 は try の中");
        }
    }

    // ---- ロック・保存の失敗・壊れたファイル (査読 A の指摘) ----

    private static string ReadText(string path) => File.ReadAllText(path);

    /// <summary>ほかのプロセスがロックを持ったままのとき、画面からの変更は理由を返して保存しない (メモリも変えない)。</summary>
    [Test]
    public static void LockHeld_AppEdits_ReturnReason_AndDoNotSave()
    {
        var path = TempFile(out var cleanup);
        var previousTimeout = Meltype.Config.FileLock.TimeoutMs;
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var app = new LanguageMemory(path);
            Assert.Equal(null, app.Set("api", english: true), "ロックが無ければ足せる");
            var before = ReadText(path);
            Meltype.Config.FileLock.TimeoutMs = 50;
            using (Meltype.Config.FileLock.Acquire(path))
            {
                var version = app.Version;
                var set = app.Set("github", english: true);
                Assert.True(set is not null && set.Contains("書き込み中"), $"足すのは理由を返す: {set}");
                var remove = app.Remove(["api"]);
                Assert.True(remove is not null && remove.Contains("書き込み中"), $"消すのも理由を返す: {remove}");
                var edit = app.Set("api", english: false, oldWord: "api");
                Assert.True(edit is not null && edit.Contains("書き込み中"), $"直すのも理由を返す: {edit}");
                Assert.True(!LearningData.ClearAll(null, null, app, null, null), "全消去は失敗を返す");
                Assert.Equal(before, ReadText(path), "ファイルは変わらない");
                Assert.True(app.Get("api") == true && app.Get("github") is null && app.Count == 1, "メモリも変わらない");
                Assert.Equal(version, app.Version, "版も変わらない");
            }
            Assert.Equal(null, app.Remove(["api"]), "ロックが外れたら消せる");
        }
        finally { Meltype.Config.FileLock.TimeoutMs = previousTimeout; cleanup(); }
    }

    /// <summary>
    /// IME の学習 (Remember) はロックを長く待たない (確定のたびに止まらないため)。取れなければロック無しで保存し、
    /// 次の読み直しでは版を比べずに必ず読む (ロック無しの保存の直後に、ほかのプロセスが書いたかもしれないため)。
    /// </summary>
    [Test]
    public static void LockHeld_Remember_WaitsShortly_AndRereadsNextTime()
    {
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var ime = new LanguageMemory(path);
            ime.Remember("abcd", english: true, explicitChoice: true);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (Meltype.Config.FileLock.Acquire(path))
            {
                ime.Remember("swift", english: true, explicitChoice: true);
            }
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 1000, $"ロックを長く待たない ({watch.ElapsedMilliseconds} ms)");
            Assert.True(new LanguageMemory(path).Get("swift") == true, "ロック無しでも保存する (入力を止めない)");

            // 同じ大きさ・同じ時刻のまま中身だけ変わった (1 秒単位の時刻のファイルシステムで、同じ秒に書き換わったのと同じ)。
            ChangeKeepingStamp(path, "\"abcd\"", "\"abce\"");
            Assert.True(ime.Refresh() && ime.Get("abce") == true && ime.Get("abcd") is null, "ロック無しで保存した後は、版が同じでも読み直す");
        }
        finally { cleanup(); }
    }

    private static void ChangeKeepingStamp(string path, string from, string to)
    {
        var info = new FileInfo(path);
        var (write, creation) = (info.LastWriteTimeUtc, info.CreationTimeUtc);
        var text = ReadText(path);
        Assert.True(text.Contains(from) && from.Length == to.Length, "置き換える語がある");
        Meltype.Config.SafeFile.WriteAllText(path, text.Replace(from, to));
        File.SetLastWriteTimeUtc(path, write);
        File.SetCreationTimeUtc(path, creation);
        var after = new FileInfo(path);
        Assert.True(after.Length == info.Length && after.LastWriteTimeUtc == write && after.CreationTimeUtc == creation, "版 (大きさ・時刻) は同じ");
    }

    /// <summary>変更する処理は、ファイルの版を比べずに必ず読み直す (版が同じでも中身が変わっていることがある)。</summary>
    [Test]
    public static void Edits_AlwaysReread_EvenWhenStampIsUnchanged()
    {
        var path = TempFile(out var cleanup);
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var app = new LanguageMemory(path);
            app.Set("abcd", english: true);
            var ime = new LanguageMemory(path);
            Assert.Equal(true, ime.Get("abcd") ?? false, "前提");
            ChangeKeepingStamp(path, "\"abcd\"", "\"abce\"");
            ime.Remember("zzzz", english: true, explicitChoice: true);
            var after = new LanguageMemory(path);
            Assert.True(after.Get("abce") == true && after.Get("zzzz") == true && after.Get("abcd") is null, "IME の保存はほかのプロセスの変更に重ねる");

            ChangeKeepingStamp(path, "\"abce\"", "\"abcf\"");
            Assert.Equal(null, app.Remove(["zzzz"]), "画面で消す");
            var final = new LanguageMemory(path);
            Assert.True(final.Get("abcf") == true && final.Get("abce") is null && final.Get("zzzz") is null, "画面の保存もほかのプロセスの変更に重ねる");
        }
        finally { cleanup(); }
    }

    /// <summary>保存できなかったら、メモリも変更の前に戻す (画面の一覧と IME とファイルがずれない。後の別の保存で黙って書かれない)。</summary>
    [Test]
    public static void SaveFailure_RestoresMemory()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = TempFile(out var cleanup);
        var directory = Path.GetDirectoryName(path)!;
        const UnixFileMode all = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var app = new LanguageMemory(path);
            app.Set("api", english: true);
            app.Set("github", english: true);
            var ime = new LanguageMemory(path);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var version = app.Version;
            var remove = app.Remove(["api"]);
            Assert.True(remove is not null, "消せなければ理由");
            Assert.True(app.Entries().Any(e => e.Word == "api") && app.Get("api") == true, "画面のメモリにも残る");
            Assert.True(app.Version > version, "版は上がる (判定の結果を使い回さない)");
            Assert.True(app.Set("swift", english: true) is not null && app.Get("swift") is null, "足せなければ足さない");
            Assert.True(app.Set("api", english: false, oldWord: "api") is not null && app.Get("api") == true, "直せなければ元のまま");
            Assert.True(app.Set("apis", english: true, oldWord: "api") is not null && app.Get("api") == true && app.Get("apis") is null, "綴りも元のまま");
            ime.Remember("kotlin", english: true, explicitChoice: true);
            Assert.True(ime.Get("kotlin") is null, "IME の学習も、保存できなければ覚えない");
            Assert.True(ThrowsIo(() => app.Clear()) && app.Count == 2, "全消去も、保存できなければ消さない");
            File.SetUnixFileMode(directory, all);
            Assert.Equal(null, app.Set("swift", english: true), "保存できるようになったら足せる");
            var after = new LanguageMemory(path);
            Assert.True(after.Get("api") == true && after.Get("github") == true && after.Get("swift") == true, "失敗した削除が後の保存で黙って書かれない");
        }
        finally { File.SetUnixFileMode(directory, all); cleanup(); }
    }

    private static bool ThrowsIo(Action action)
    {
        try { action(); return false; }
        catch (IOException) { return true; }
    }

    /// <summary>読めない (壊れた) languages.json に書く前に、languages.json.broken に残す (黙って消さない)。</summary>
    [Test]
    public static void BrokenFile_IsKeptAsBroken_BeforeOverwrite()
    {
        var path = TempFile(out var cleanup);
        try
        {
            const string broken = "{\"api\":{\"English\":true,\"Count\":3},\"sushi\":{\"English\":false,\"Count\":2}, BROKEN";
            File.WriteAllText(path, broken);
            var app = new LanguageMemory(path);
            Assert.Equal(0, app.Entries().Count, "読めない");
            Assert.Equal(null, app.Set("github", english: true), "足せる");
            Assert.Equal(broken, File.Exists(path + ".broken") ? ReadText(path + ".broken") : "", "元の中身は .broken に残る");
            Assert.True(new LanguageMemory(path).Get("github") == true, "languages.json は読める内容になる");
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path + ".broken"), ".broken も本人だけ");

            // IME の学習も同じ (使っている途中で壊れたとき)。
            LanguageMemory.FileCheckIntervalMs = 0;
            const string broken2 = "{ broken again";
            File.WriteAllText(path, broken2);
            var ime = new LanguageMemory(path);
            ime.Remember("swift", english: true, explicitChoice: true);
            Assert.Equal(broken2, ReadText(path + ".broken"), "IME の保存の前にも残す");
            Assert.True(new LanguageMemory(path).Get("swift") == true, "IME の保存は読める内容");
            var leftovers = LearningData.ClearLeftovers([path], []);
            Assert.True(leftovers && !File.Exists(path + ".broken"), "全消去の残りもの (.broken) として消せる");
        }
        finally { cleanup(); }
    }

    /// <summary>使っている途中でファイルが大きすぎるようになっても、読めなかった扱いで今の内容のまま続ける (空にしない)。</summary>
    [Test]
    public static void Oversize_DuringRefresh_KeepsMemory()
    {
        var path = TempFile(out var cleanup);
        var previousMax = Meltype.Config.SafeFile.MaxReadBytes;
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            var ime = new LanguageMemory(path);
            ime.Set("api", english: true);
            ime.Set("github", english: true);
            var app = new LanguageMemory(path);
            app.Set("kubernetes", english: true);
            Meltype.Config.SafeFile.MaxReadBytes = 60;
            var version = ime.Version;
            Assert.True(ime.Get("api") == true && ime.Get("github") == true, "今の内容で続ける");
            Assert.Equal(version, ime.Version, "読めなかったので版は変わらない");
            Assert.True(File.Exists(path + ".oversize"), "大きすぎるファイルは退避されている");
            Meltype.Config.SafeFile.MaxReadBytes = previousMax;
            ime.Remember("swift", english: true, explicitChoice: true);
            var after = new LanguageMemory(path);
            Assert.True(after.Get("api") == true && after.Get("github") == true && after.Get("kubernetes") == true && after.Get("swift") == true, "読めるようになれば全部残る");
        }
        finally { Meltype.Config.SafeFile.MaxReadBytes = previousMax; Meltype.Config.SafeFile.ResetBlockedForTests(); cleanup(); }
    }

    /// <summary>同じファイルを持つ 2 つのインスタンス (IME・画面) が別々のスレッドで変更し続けても、どちらの変更も消えない (ロックが取れる場合)。</summary>
    [Test]
    public static void TwoInstances_ConcurrentEdits_LoseNothing()
    {
        var path = TempFile(out var cleanup);
        var previousRememberTimeout = LanguageMemory.RememberLockTimeoutMs;
        try
        {
            LanguageMemory.FileCheckIntervalMs = 0;
            // ここで見るのはロックが取れる場合。画面側が間を空けずにロックを取り直し続けるので、IME の短い待ち (200 ms) では
            // 取れずにロック無しの保存になることがある (そのときは変更が消えうる: 入力を止めないための割り切り)。
            LanguageMemory.RememberLockTimeoutMs = 10_000;
            static string Letters(int i) => $"{(char)('a' + i / 26 % 26)}{(char)('a' + i % 26)}";
            var seed = new LanguageMemory(path);
            for (var i = 0; i < 60; i++) seed.Set("del" + Letters(i), english: true);
            var ime = new LanguageMemory(path);
            var app = new LanguageMemory(path);
            var failures = 0;
            var t1 = new Thread(() => { for (var i = 0; i < 80; i++) { ime.Remember("ime" + Letters(i), true, true); ime.Get("delaa"); } });
            var t2 = new Thread(() =>
            {
                for (var i = 0; i < 60; i++)
                {
                    if (app.Remove(["del" + Letters(i)]) is not null) Interlocked.Increment(ref failures);
                    if (app.Set("app" + Letters(i), false) is not null) Interlocked.Increment(ref failures);
                }
            });
            t1.Start(); t2.Start(); t1.Join(); t2.Join();
            var final = new LanguageMemory(path).Entries().Select(e => e.Word).ToList();
            Assert.Equal(0, failures, "画面の変更はどれも通る");
            Assert.True(final.Count(w => w.StartsWith("ime")) == 80 && final.Count(w => w.StartsWith("app")) == 60 && !final.Any(w => w.StartsWith("del")),
                $"どちらの変更も残り、消した語は戻らない ({final.Count} 語)");
        }
        finally { LanguageMemory.RememberLockTimeoutMs = previousRememberTimeout; cleanup(); }
    }

    /// <summary>理由に入れる元の語は、改行・Tab を除き、長ければ切る (画面にそのまま出すため)。</summary>
    [Test]
    public static void Reason_EchoOfOldWord_IsShortAndSingleLine()
    {
        var memory = new LanguageMemory(null);
        var reason = memory.Set("abc", english: true, oldWord: new string('x', 1_000_000) + "\n\tinjected");
        Assert.True(reason is { Length: < 80 } && !reason.Contains('\n') && !reason.Contains('\t') && !reason.Contains('\r'), $"短い 1 行: {reason?[..Math.Min(reason.Length, 100)]}");
        var shortReason = memory.Set("abc", english: true, oldWord: "no\r\nsuch");
        Assert.True(shortReason is not null && shortReason.Contains("no  such") && !shortReason.Contains('\n'), $"改行は空白に: {shortReason}");
    }
}
