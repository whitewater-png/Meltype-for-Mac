// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// 辞書の管理画面 (Mac の「Meltype 辞書」アプリ。IME とは別のプロセス) のための Core の部分:
/// ユーザー辞書のファイルを正にした変更 (ほかのプロセスの変更の反映・同時に書いても消えない・読めないファイルを上書きしない)、
/// 入力チェック、編集・削除と元に戻す、専門用語の除外 (terms-excluded.txt)・編集・一覧、FFI の文字列の形式。
/// 「別のプロセス」は、同じファイルを持つ別のインスタンス (ロックは flock / 共有モードなので、同じプロセスの別のインスタンスでも効く) で代える。
/// 本物の別プロセス・AOT 版は tools/check-mac-aot-settings.py で確かめる。
/// </summary>
internal static class DictionaryManagementTests
{
    private const string Civil = "# 名称: 土木・建設\nこうぞうぶつ\t構造物\t\t強制\nほそう\t舗装\nこうぞうけいさん\t構造計算\tメモ\n";
    private const string Medical = "# 名称: 医療\nけつあつそくてい\t血圧測定\n";

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-dict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>ファイルの確認の間隔を 0 にして (すぐ反映を確かめるため)、偽の分野と一時的な config.json・terms-excluded.txt に差し替える。</summary>
    private static string Use(out Action restore)
    {
        var directory = TempDirectory();
        var previousConfig = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        var previousTermInterval = TermDomains.ExternalCheckIntervalMs;
        var previousFileInterval = UserDictionary.FileCheckIntervalMs;
        TermDomains.ConfigPath = () => Path.Combine(directory, "config.json");
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.Source = () => [("civil", () => Civil), ("medical", () => Medical)];
        TermDomains.ExternalCheckIntervalMs = 0;
        UserDictionary.FileCheckIntervalMs = 0;
        TermDomains.Reset();
        restore = () =>
        {
            TermDomains.ConfigPath = previousConfig;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.ExternalCheckIntervalMs = previousTermInterval;
            UserDictionary.FileCheckIntervalMs = previousFileInterval;
            TermDomains.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return directory;
    }

#pragma warning disable CA1416 // Windows では呼ばない (各テストが先に IsWindows で抜ける)
    private static int Mode(string path) => (int)File.GetUnixFileMode(path) & 0x1FF;
#pragma warning restore CA1416

    // ---- ユーザー辞書: ほかのプロセスの変更の反映 ----

    [Test]
    public static void UserDict_OtherProcessChange_IsReflected()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var ime = new UserDictionary(path, builtIn: false);   // IME のプロセスのつもり
            var gui = new UserDictionary(path, builtIn: false);   // 管理画面のプロセスのつもり
            var before = ime.Version;
            Assert.True(gui.AddNew("きごうとう", "記号等") is null, "登録できる");
            Assert.Equal("記号等", ime.Lookup("きごうとう").SingleOrDefault() ?? "", "IME 側にすぐ反映 (ファイルの版を見て読み直す)");
            Assert.True(ime.Version != before, "Version が進む (変換結果のキャッシュを捨てる)");

            // 編集・削除も
            Assert.True(gui.Update(new UserWord("きごうとう", "記号等"), "きごうとう", "記号党") is null, "編集できる");
            Assert.Equal("記号党", ime.Lookup("きごうとう").SingleOrDefault() ?? "", "編集も反映");
            Assert.True(gui.RemoveRange([new UserWord("きごうとう", "記号党")], out _) is null, "削除できる");
            Assert.Equal(0, ime.Lookup("きごうとう").Count, "削除も反映");
            Assert.Equal(0, ime.Count, "語数も");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_ControlAndFormatChars_AreRejected_EverywhereTheyCanEnter()
    {
        // 制御文字 (ESC・Ctrl+O・BEL)・書式文字 (右から左に並べ替える U+202E)・行/段落区切りは、読みにも語にも使えない
        foreach (var bad in new[] { "\u000f", "\u001b", "\u0007", "\u007f", "\u0085", "\u202e", "\u200b", "\ufeff", "\u2028", "\u2029" })
        {
            Assert.True(UserDictionary.Validate("ありがとう", "語" + bad) is not null, $"語に U+{(int)bad[0]:X4}");
            Assert.True(UserDictionary.Validate("あり" + bad + "がとう", "語") is not null, $"読みに U+{(int)bad[0]:X4}");
        }
        // 通常の語・絵文字をつなぐ ZWJ・異体字選択子は通る
        Assert.True(UserDictionary.Validate("ありがとう", "感謝") is null, "通常の語");
        Assert.True(UserDictionary.Validate("かぞく", "👨\u200d👩\u200d👧") is null, "ZWJ でつないだ絵文字");
        Assert.True(UserDictionary.Validate("つじ", "辻\ufe00") is null, "異体字選択子");

        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            // 1. 登録・取り込み (AddMany) の経路
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.True(dictionary.AddNew("ありがとう", "echo pwned\u000f\u001b[2K") is not null, "画面からの登録は断る");
            Assert.True(dictionary.AddMany([new UserWord("ありがとう", "悪\u202e語"), new UserWord("たろう", "太郎")], out var added) is null, "取り込みは正常な語だけ入る");
            Assert.Equal(1, added.Count, "不正な語は取り込まない");
            Assert.Equal(0, dictionary.Lookup("ありがとう").Count, "不正な語は引けない");

            // 2. ファイルを直接編集・復元した経路 (読み込み時にも落とす)
            File.WriteAllText(path, "ありがとう\tx\u000fy\nありがとう\t感謝\nたろう\t太郎\n", new System.Text.UTF8Encoding(false));
            var loaded = new UserDictionary(path, builtIn: false);
            Assert.Equal("感謝", string.Join(",", loaded.Lookup("ありがとう")), "制御文字入りの行だけ読み込まない");
            Assert.Equal(1, loaded.Lookup("たろう").Count, "ほかの行は読む");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_Reading_IsNormalizedToHiragana_AndNonKanaReadingsAreRejected()
    {
        // カタカナの読みは、ひらがなにそろえて登録する (変換側はひらがなで引く)。ローマ字・漢字だけの読みは、変換に一度も出ないので断る
        Assert.True(UserDictionary.Validate("kigoutou", "記号等") is not null, "ローマ字だけの読みは登録できない");
        Assert.True(UserDictionary.Validate("123", "数") is not null, "数字だけの読みも");
        Assert.True(UserDictionary.Validate("記号とう", "記号等") is not null, "漢字を含む読みも");
        Assert.True(UserDictionary.Validate("きごう とう", "記号等") is not null, "空白を含む読みも");
        Assert.True(UserDictionary.Validate("ごーる", "GOAL") is null, "長音は可");
        Assert.True(UserDictionary.Validate("ごじゅっcc", "50cc") is null, "ひらがなと英数字が混ざった読みは可");
        Assert.Equal("きごうとう", UserDictionary.NormalizeReading(" キゴウトウ "), "カタカナ → ひらがな、前後の空白を取る");
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.True(dictionary.AddNew("キゴウトウ", "記号等") is null, "カタカナの読みで登録できる");
        Assert.Equal("記号等", dictionary.Lookup("きごうとう").SingleOrDefault() ?? "", "ひらがなで引ける");
        Assert.Equal(UserDictionary.DuplicateMessage, dictionary.Check("キゴウトウ", "記号等"), "カタカナで確かめても重複と分かる");
        Assert.True(dictionary.Update(new UserWord("きごうとう", "記号等"), "キゴウトウ", "記号党") is null, "編集もそろえる");
        Assert.Equal("記号党", dictionary.Lookup("きごうとう").SingleOrDefault() ?? "", "編集後もひらがなで引ける");
        Assert.True(dictionary.AddMany([new UserWord("タロウ", "太郎"), new UserWord("jiro", "次郎")], out var added) is null && added.Count == 1, "取り込み・複製もそろえる (ローマ字は入れない)");
        Assert.Equal("太郎", dictionary.Lookup("たろう").SingleOrDefault() ?? "", "取り込みもひらがなで引ける");
    }

    [Test]
    public static void UserDict_CheckInterval_LimitsFileChecks_RefreshForces()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            UserDictionary.FileCheckIntervalMs = 60 * 60 * 1000;
            var ime = new UserDictionary(path, builtIn: false);
            new UserDictionary(path, builtIn: false).AddNew("きごうとう", "記号等");
            Assert.Equal(0, ime.Count, "間隔の間はファイルを確かめない (1 キーごとに stat しない)");
            Assert.True(ime.Refresh(), "Refresh はすぐ確かめて、読み直したら true");
            Assert.Equal(1, ime.Count, "読み直した");
            Assert.True(!ime.Refresh(), "変わっていなければ false");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_ExternalChange_InvalidatesLiveConversionCache()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var ime = new UserDictionary(path, builtIn: false);
            var k = new CompositionTests.Keyboard(live: true, userDictionary: ime);
            k.Type("nurupoga");
            Assert.True(k.Showing != "ヌルポ", "登録前は出ない");
            new UserDictionary(path, builtIn: false).AddNew("ぬるぽが", "ヌルポ");   // 管理画面で登録
            k.Type("\bga"); // 同じかなに戻す (キャッシュが残っていれば登録前の表示のまま)
            Assert.Equal("ヌルポ", k.Showing, "ほかのプロセスでの登録が、次の変換に出る");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_SharedInstance_SeesOtherProcess()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var shared = UserDictionary.Shared(path);   // IME のセッションが使うもの (同梱の語句・専門用語集つき)
            new UserDictionary(path, builtIn: false).AddNew("めるたいぷ", "Meltype");
            Assert.Equal("Meltype", shared.Lookup("めるたいぷ").FirstOrDefault() ?? "", "共有インスタンスにも反映");
            Assert.True(shared.Split("めるたいぷです") is { } pieces && pieces[0].Word == "Meltype", "強制 (最優先) として効く");
        }
        finally { restore(); }
    }

    // ---- ユーザー辞書: 同時に書いても消えない ----

    [Test]
    public static void UserDict_StaleInstance_DoesNotOverwriteOtherChanges()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            UserDictionary.FileCheckIntervalMs = 60 * 60 * 1000;   // 古いままのインスタンスを作る
            var a = new UserDictionary(path, builtIn: false);
            var b = new UserDictionary(path, builtIn: false);
            Assert.True(a.AddNew("あかいろ", "赤色") is null, "確認");
            Assert.True(b.AddNew("あおいろ", "青色") is null, "b はファイルを読み直さずに見ていても");
            var saved = new UserDictionary(path, builtIn: false).Words;
            Assert.Equal(2, saved.Count, "両方の登録がファイルに残る (変更の前にロックの中で読み直すので)");
            Assert.True(b.Words.Count == 2, "b は保存のときに読み直したので、a の登録も持つ");
            Assert.True(a.RemoveRange([new UserWord("あおいろ", "青色")], out _) is null, "a は b の登録を知らなくても消せる");
            Assert.Equal("赤色", new UserDictionary(path, builtIn: false).Words.Single().Word, "確認");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_ConcurrentWriters_AllWordsSaved()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            UserDictionary.FileCheckIntervalMs = 60 * 60 * 1000;
            var instances = Enumerable.Range(0, 4).Select(_ => new UserDictionary(path, builtIn: false)).ToArray();
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, 4, i =>
            {
                for (var j = 0; j < 25; j++)
                {
                    if (instances[i].AddNew($"よみ{i}の{j}ばん", $"語{i}-{j}") is { } error) errors.Add(error);
                }
            });
            Assert.True(errors.IsEmpty, "どれも保存できる: " + string.Join(" / ", errors.Take(3)));
            var saved = new UserDictionary(path, builtIn: false);
            Assert.Equal(100, saved.Count, "4 つのインスタンスが 25 語ずつ同時に登録しても、100 語すべて残る");
            Assert.Equal(100, saved.Words.Distinct().Count(), "重複しない");
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(0x180, Mode(path), "userdict.txt は 0600 のまま");
                Assert.Equal(0x180, Mode(FileLock.LockPath(path)), "ロックのファイルも 0600");
            }
            Assert.True(Directory.GetFiles(directory, "*.tmp").Length == 0, "一時ファイルを残さない");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_LockHeldElsewhere_FailsWithoutWriting()
    {
        var directory = Use(out var restore);
        var previousTimeout = FileLock.TimeoutMs;
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.True(dictionary.AddNew("まえから", "前から") is null, "確認");
            var original = File.ReadAllText(path);
            FileLock.TimeoutMs = 50;
            using (FileLock.Acquire(path))   // ほかのプロセスが保存中
            {
                var error = dictionary.AddNew("あたらしい", "新しい");
                Assert.True(error is not null && error.Contains("保存中"), "待っても取れなければ、理由を返す: " + error);
            }
            Assert.Equal(original, File.ReadAllText(path), "ファイルは変えない");
            Assert.Equal(1, dictionary.Count, "メモリ上も変えない (ファイルと食い違わない)");
            Assert.True(dictionary.AddNew("あたらしい", "新しい") is null, "外れたら登録できる");
        }
        finally
        {
            FileLock.TimeoutMs = previousTimeout;
            restore();
        }
    }

    [Test]
    public static void UserDict_UnreadableFile_IsNotOverwritten()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Use(out var restore);
        var path = Path.Combine(directory, "userdict.txt");
        try
        {
            File.WriteAllText(path, "だいじな\t大事な\n");
#pragma warning disable CA1416
            File.SetUnixFileMode(path, UnixFileMode.None);
#pragma warning restore CA1416
            if (CanRead(path)) return; // root などで権限が効かない環境では確かめられない
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(0, dictionary.Count, "読めなければ空で動く");
            var error = dictionary.AddNew("あたらしい", "新しい");
            Assert.True(error is not null && error.Contains("読めない"), "読めないファイルには書かず、理由を返す: " + error);
            Assert.True(dictionary.Add("あたらしい", "新しい") is not null, "入力メニューからの登録も同じ");
#pragma warning disable CA1416
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
            Assert.Equal("だいじな\t大事な\n", File.ReadAllText(path), "元の内容はそのまま");
        }
        finally
        {
#pragma warning disable CA1416
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
#pragma warning restore CA1416
            restore();
        }
    }

    private static bool CanRead(string path)
    {
        try { File.ReadAllBytes(path); return true; } catch { return false; }
    }

    [Test]
    public static void UserDict_InvalidEncoding_IsNotRewritten()
    {
        var directory = Use(out var restore);
        try
        {
            // Shift_JIS で保存したファイル (UTF-8 として不正なバイト)。前は U+FFFD に置き換えて読み、次の保存で元のバイト列を失っていた。
            var path = Path.Combine(directory, "userdict.txt");
            byte[] sjis = [0x82, 0xA0, 0x82, 0xA2, 0x09, 0x88, 0xA4, 0x0A];   // 「あい<Tab>愛」
            File.WriteAllBytes(path, sjis);
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal(0, dictionary.Count, "読めないので空で動く (文字化けした語を読み込まない)");
            Assert.True(dictionary.Problem is { } problem && problem.Contains("文字コード"), "画面に出す理由がある: " + dictionary.Problem);
            var error = dictionary.AddNew("あたらしい", "新しい");
            Assert.True(error is not null && error.Contains("文字コード"), "書かずに理由を返す: " + error);
            Assert.True(dictionary.AddMany([new UserWord("とりこみ", "取り込み")], out _) is not null, "取り込みも書かない");
            Assert.True(File.ReadAllBytes(path).SequenceEqual(sjis), "元のバイト列はそのまま");
            Assert.True(!File.Exists(path + ".bak"), "退避も作らない (書いていない)");

            // UTF-16 (BOM 付き) で保存し直したファイルは読める
            File.WriteAllBytes(path, [.. System.Text.Encoding.Unicode.GetPreamble(), .. System.Text.Encoding.Unicode.GetBytes("あい\t愛\n")]);
            Assert.True(dictionary.Refresh() && dictionary.Count == 1 && dictionary.Problem is null, "UTF-16 なら読める (問題も消える)");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_Oversize_IsReportedAsProblem()
    {
        var directory = Use(out var restore);
        var saved = SafeFile.MaxReadBytes;
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            new UserDictionary(path, builtIn: false).AddNew("きごう", "記号");
            SafeFile.MaxReadBytes = 10;
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.True(dictionary.Count == 0 && dictionary.Problem is { } problem && problem.Contains(".oversize"), "大きすぎて読まなかったことを画面に伝える: " + dictionary.Problem);
        }
        finally
        {
            SafeFile.MaxReadBytes = saved;
            SafeFile.ResetBlockedForTests();
            restore();
        }
    }

    [Test]
    public static void UserDict_SameSizeReplacement_IsDetectedByCreationTime()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            UserDictionary.FileCheckIntervalMs = 60 * 60 * 1000;
            var writer = new UserDictionary(path, builtIn: false);
            writer.AddNew("あいう", "愛");
            var reader = new UserDictionary(path, builtIn: false);
            var written = File.GetLastWriteTimeUtc(path);
            Thread.Sleep(20);
            // 同じ大きさの内容で置き換え (SafeFile は一時ファイルから置き換える)、更新時刻だけ元に戻す (更新時刻が粗いファイルシステムのつもり)
            Assert.True(writer.Update(new UserWord("あいう", "愛"), "あいう", "藍") is null, "確認");
            File.SetLastWriteTimeUtc(path, written);
            Assert.True(reader.Refresh(), "大きさ・更新時刻が同じでも、置き換えたファイル (作成時刻が違う) は読み直す");
            Assert.Equal("藍", reader.Words.Single().Word, "確認");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_AddMany_ReturnsOnlyNewWords()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.AddNew("あいう", "愛");
        Assert.True(dictionary.AddMany([new UserWord("あいう", "愛"), new UserWord("かきく", "書"), new UserWord("き", "短"), new UserWord("かきく", "書")], out var added) is null, "確認");
        Assert.Equal("かきく=書", string.Join(",", added.Select(w => $"{w.Reading}={w.Word}")), "登録済み・不正・重複を除いた、新しく登録した語だけ");
        Assert.Equal(2, dictionary.Count, "確認");
    }

    [Test]
    public static void Terms_InvalidEncodingExclusionFile_IsNotRewritten()
    {
        var directory = Use(out var restore);
        try
        {
            var file = Path.Combine(directory, "terms-excluded.txt");
            byte[] sjis = [0x82, 0xD9, 0x82, 0xBB, 0x82, 0xA4, 0x09, 0x95, 0xDC, 0x91, 0x95, 0x0A];   // 「ほそう<Tab>舗装」(Shift_JIS)
            File.WriteAllBytes(file, sjis);
            var error = TermDomains.SetExcluded([("こうぞうぶつ", "構造物")], excluded: true);
            Assert.True(error is not null && error.Contains("文字コード"), "書かずに理由を返す: " + error);
            Assert.True(File.ReadAllBytes(file).SequenceEqual(sjis), "元のバイト列はそのまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void Terms_Edit_RollsBackWhenExclusionFails()
    {
        var directory = Use(out var restore);
        var previousTimeout = FileLock.TimeoutMs;
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            var path = Path.Combine(directory, "userdict.txt");
            var gui = new UserDictionary(path, builtIn: false);
            FileLock.TimeoutMs = 50;
            string? error;
            bool added;
            using (FileLock.Acquire(Path.Combine(directory, "terms-excluded.txt")))   // 除外のファイルを、ほかのプロセスが保存中
            {
                error = DictionaryManagement.EditTerm(gui, new UserWord("こうぞうぶつ", "構造物"), "こうぞうぶつ", "構造仏", out added);
            }
            Assert.True(error is not null, "除外できなければ理由を返す");
            Assert.True(!added, "登録は戻したので added = false");
            Assert.Equal(0, gui.Count, "ユーザー辞書に直した語を残さない (中途半端にしない)");
            Assert.True(!TermDomains.Excluded().Any(), "除外もしていない");
        }
        finally
        {
            FileLock.TimeoutMs = previousTimeout;
            restore();
        }
    }

    [Test]
    public static void Management_Import_ReportsAddedWords()
    {
        var directory = Use(out var restore);
        try
        {
            var file = Path.Combine(directory, "import.txt");
            File.WriteAllText(file, "ゆきしろ\t雪代\nめるたいぷ\tMeltype\n");
            var target = new UserDictionary(Path.Combine(directory, "userdict.txt"), builtIn: false);
            target.AddNew("ゆきしろ", "雪代");
            Assert.True(DictionaryManagement.Import(target, file, out var summary, out var added) is null, "確認");
            Assert.Equal("1\t1\t0\tUTF-8", summary, "確認");
            Assert.Equal("めるたいぷ=Meltype", string.Join(",", added.Select(w => $"{w.Reading}={w.Word}")), "新しく登録した語だけ (取り込みの取り消しで消す語)");
        }
        finally { restore(); }
    }

    // ---- ユーザー辞書: 入力チェック・編集・削除と元に戻す ----

    [Test]
    public static void UserDict_Check_SameRulesAsValidate_PlusDuplicates()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.Equal(UserDictionary.Validate("き", "記"), dictionary.AddNew("き", "記"), "読みが短い");
        Assert.Equal(UserDictionary.Validate("きごう", ""), dictionary.AddNew("きごう", " "), "単語が空 (空白だけも)");
        Assert.Equal(UserDictionary.Validate(new string('あ', 101), "語"), dictionary.Check(new string('あ', 101), "語"), "長すぎる");
        Assert.Equal(UserDictionary.Validate("き\tごう", "記号"), dictionary.Check("き\tごう", "記号"), "タブ");
        Assert.Equal(UserDictionary.Validate("きごう", "記\n号"), dictionary.Check("きごう", "記\n号"), "改行");
        Assert.True(dictionary.AddNew(" きごう ", " 記号 ") is null, "前後の空白は取り除いて登録");
        Assert.Equal(UserDictionary.DuplicateMessage, dictionary.AddNew("きごう", "記号"), "管理画面の登録は、重複を理由で返す");
        Assert.Equal(UserDictionary.DuplicateMessage, dictionary.Check("きごう", "記号"), "入力チェックも");
        Assert.True(dictionary.Check("きごう", "記号", except: new UserWord("きごう", "記号")) is null, "編集中の元の語と同じなのは重複ではない");
        Assert.True(dictionary.Add("きごう", "記号") is null, "入力メニューからの登録は、今までどおり重複でも成功 (何もしない)");
        Assert.Equal(1, dictionary.Count, "増えない");
    }

    [Test]
    public static void UserDict_Update_KeepsPosition_AndRejectsConflicts()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var dictionary = new UserDictionary(path, builtIn: false);
            dictionary.AddRange([new UserWord("いちばん", "一番"), new UserWord("にばん", "二番"), new UserWord("さんばん", "三番")]);
            Assert.True(dictionary.Update(new UserWord("にばん", "二番"), "にばんめ", "二番目") is null, "編集できる");
            Assert.Equal("一番,二番目,三番", string.Join(",", dictionary.Words.Select(w => w.Word)), "位置はそのまま (同じ読みの中の優先順を変えない)");
            Assert.Equal(UserDictionary.DuplicateMessage, dictionary.Update(new UserWord("にばんめ", "二番目"), "さんばん", "三番"), "ほかの語と重なる");
            Assert.Equal(UserDictionary.NotFoundMessage, dictionary.Update(new UserWord("ないもの", "無い"), "ないもの", "無"), "元の語が無い");
            Assert.Equal(UserDictionary.Validate("に", "二"), dictionary.Update(new UserWord("にばんめ", "二番目"), "に", "二"), "不正な入力");
            Assert.True(dictionary.Update(new UserWord("にばんめ", "二番目"), "にばんめ", "二番目") is null, "変えなければ何もしない");
            Assert.True(File.ReadAllText(path + ".bak").Contains("にばん\t二番\n") || File.ReadAllText(path + ".bak").Contains("にばん\t二番\r\n"), "編集の直前の内容が .bak に残る");
            if (!OperatingSystem.IsWindows()) Assert.Equal(0x180, Mode(path + ".bak"), ".bak も 0600");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_RemoveRange_ReportsPositions_AndRestoreUndoes()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            var dictionary = new UserDictionary(path, builtIn: false);
            dictionary.AddRange([new UserWord("えー", "A"), new UserWord("びー", "B"), new UserWord("しー", "C"), new UserWord("でぃー", "D")]);
            var before = File.ReadAllText(path);
            Assert.True(dictionary.RemoveRange([new UserWord("びー", "B"), new UserWord("でぃー", "D"), new UserWord("ない", "無")], out var removed) is null, "消せる");
            Assert.Equal("1:B,3:D", string.Join(",", removed.Select(r => $"{r.Index}:{r.Word.Word}")), "消した語と元の位置 (無い語は数えない)");
            Assert.Equal("A,C", string.Join(",", dictionary.Words.Select(w => w.Word)), "確認");
            Assert.Equal(before, File.ReadAllText(path + ".bak"), "削除の直前の内容が .bak に残る");
            Assert.True(dictionary.Restore(removed) is null, "元に戻せる");
            Assert.Equal("A,B,C,D", string.Join(",", dictionary.Words.Select(w => w.Word)), "元の位置に戻る");
            Assert.True(dictionary.Restore(removed) is null, "もう一度戻しても");
            Assert.Equal(4, dictionary.Count, "重複しない");
            Assert.True(dictionary.RemoveRange([new UserWord("ない", "無")], out var none) is null && none.Count == 0, "何も消さなければ空");
        }
        finally { restore(); }
    }

    [Test]
    public static void UserDict_RemoveRange_RemovesAllDuplicates_AndRestoreAppendsWhenShorter()
    {
        var directory = Use(out var restore);
        try
        {
            var path = Path.Combine(directory, "userdict.txt");
            // 手で編集して同じ行が 2 つあるファイル
            File.WriteAllText(path, "# x\nあいう\t愛\nかきく\t書\nあいう\t愛\n");
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.True(dictionary.RemoveRange([new UserWord("あいう", "愛")], out var removed) is null, "確認");
            Assert.Equal(2, removed.Count, "同じ語は全部消す");
            Assert.Equal(1, dictionary.Count, "確認");
            Assert.True(dictionary.Restore([(10, new UserWord("さしす", "差"))]) is null, "位置が後ろすぎれば末尾に");
            Assert.Equal("書,差", string.Join(",", dictionary.Words.Select(w => w.Word)), "確認");
        }
        finally { restore(); }
    }

    // ---- 専門用語集: 除外 (削除の代わり) ----

    [Test]
    public static void Terms_Excluded_AreGoneFromForcedCandidatesAndPredictions()
    {
        var directory = Use(out var restore);
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            var dictionary = new UserDictionary(null);
            Assert.Equal("構造物", dictionary.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "除外前は強制型として出る");
            Assert.True(dictionary.LookupTermCandidates("ほそう").Contains("舗装"), "候補追加型");
            Assert.True(dictionary.PredictTerms("こうぞう").Contains("構造物"), "予測");
            var version = dictionary.Version;

            Assert.True(TermDomains.SetExcluded([("こうぞうぶつ", "構造物"), ("ほそう", "舗装")], excluded: true) is null, "除外できる");
            Assert.True(dictionary.Version != version, "Version が進む (変換結果のキャッシュを捨てる)");
            Assert.Equal(0, dictionary.Lookup("こうぞうぶつ").Count, "強制型から消える");
            Assert.True(dictionary.Split("こうぞうぶつをつくる") is null, "強制しない");
            Assert.True(!dictionary.LookupTermCandidates("ほそう").Contains("舗装"), "候補追加型から消える");
            Assert.True(!dictionary.PredictTerms("こうぞう").Contains("構造物"), "予測から消える");
            Assert.True(dictionary.PredictTerms("こうぞう").Contains("構造計算"), "除外していない語は残る");
            Assert.Equal(1, dictionary.TermCount, "残りは 1 語");
            Assert.Equal(2, TermDomains.Current.Excluded, "除外した数");

            var file = Path.Combine(directory, "terms-excluded.txt");
            Assert.True(File.ReadAllText(file).Contains("こうぞうぶつ\t構造物"), "terms-excluded.txt に保存される");
            Assert.True(!File.ReadAllText(Path.Combine(directory, "config.json")).Contains("構造物"), "config.json には入れない");
            if (!OperatingSystem.IsWindows()) Assert.Equal(0x180, Mode(file), "0600");
            Assert.Equal("こうぞうぶつ=構造物,ほそう=舗装", string.Join(",", TermDomains.Excluded().Select(e => $"{e.Reading}={e.Word}")), "一覧 (読みの順)");

            // 次の起動 (覚えた状態を捨てて読み直す) でも除外されたまま
            TermDomains.Reset();
            Assert.Equal(0, new UserDictionary(null).Lookup("こうぞうぶつ").Count, "再起動後も除外");

            Assert.True(TermDomains.SetExcluded([("こうぞうぶつ", "構造物")], excluded: false) is null, "元に戻せる");
            Assert.Equal("構造物", dictionary.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "戻すとまた出る");
            Assert.Equal("ほそう=舗装", string.Join(",", TermDomains.Excluded().Select(e => $"{e.Reading}={e.Word}")), "確認");
        }
        finally { restore(); }
    }

    [Test]
    public static void Terms_Exclusion_NormalizesReading_AndSkipsInvalid()
    {
        Use(out var restore);
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            Assert.True(TermDomains.SetExcluded([("コウゾウブツ", " 構造物 "), ("ほ", "舗"), ("", "")], excluded: true) is null, "不正なものは飛ばす");
            Assert.Equal(0, new UserDictionary(null).Lookup("こうぞうぶつ").Count, "カタカナの読みでも、正規化して除外");
            Assert.Equal(1, TermDomains.Excluded().Count, "不正なものは保存しない");
        }
        finally { restore(); }
    }

    [Test]
    public static void Terms_OtherProcessChanges_AreReflected()
    {
        var directory = Use(out var restore);
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            var dictionary = new UserDictionary(null);
            Assert.Equal("構造物", dictionary.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "確認");

            // 管理画面 (別のプロセス) が terms-excluded.txt を書いた
            File.WriteAllText(Path.Combine(directory, "terms-excluded.txt"), "# x\nこうぞうぶつ\t構造物\n");
            Assert.Equal(0, dictionary.Lookup("こうぞうぶつ").Count, "除外がすぐ反映");

            // 管理画面が分野を切り替えた (config.json を書いた)
            var path = Path.Combine(directory, "config.json");
            var settings = Settings.LoadForUpdate(path)!;
            settings.EnabledTermDomains = ["civil", "medical"];
            settings.Save(path);
            Assert.Equal("血圧測定", dictionary.Lookup("けつあつそくてい").SingleOrDefault() ?? "", "分野の切り替えもすぐ反映");
            Assert.True(TermDomains.List().Single(d => d.Id == "medical").Enabled, "一覧にも");

            // 読めなくなった設定は、今の状態のまま (全部 OFF にしない)
            File.WriteAllText(path, "{ 壊れた");
            Assert.Equal("血圧測定", dictionary.Lookup("けつあつそくてい").SingleOrDefault() ?? "", "壊れた設定では変えない");
            Assert.True(!TermDomains.Set("civil", false), "壊れた設定には書かない");
            Assert.Equal("{ 壊れた", File.ReadAllText(path), "元のファイルはそのまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void Terms_UnreadableExclusionFile_IsNotOverwritten()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Use(out var restore);
        var file = Path.Combine(directory, "terms-excluded.txt");
        try
        {
            File.WriteAllText(file, "ほそう\t舗装\n");
#pragma warning disable CA1416
            File.SetUnixFileMode(file, UnixFileMode.None);
#pragma warning restore CA1416
            if (CanRead(file)) return;
            var error = TermDomains.SetExcluded([("こうぞうぶつ", "構造物")], excluded: true);
            Assert.True(error is not null, "読めないファイルには書かない: " + error);
#pragma warning disable CA1416
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
            Assert.Equal("ほそう\t舗装\n", File.ReadAllText(file), "元の内容はそのまま");
        }
        finally
        {
#pragma warning disable CA1416
            try { File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
#pragma warning restore CA1416
            restore();
        }
    }

    [Test]
    public static void Terms_Words_ListEntriesWithNotesAndExcludedFlag()
    {
        Use(out var restore);
        try
        {
            TermDomains.SetExcluded([("ほそう", "舗装")], excluded: true);
            var words = TermDomains.Words("civil")!;
            Assert.Equal("こうぞうぶつ,ほそう,こうぞうけいさん", string.Join(",", words.Select(w => w.Entry.Reading)), "ファイルの順 (OFF の分野でも一覧は出せる)");
            Assert.Equal("メモ", words[2].Entry.Note, "注記");
            Assert.True(words[1].Excluded && !words[0].Excluded, "除外の印");
            Assert.True(TermDomains.Words("nosuch") is null, "未知の ID は null");
            var text = DictionaryManagement.FormatTermWords(words);
            Assert.Equal("こうぞうぶつ\t構造物\t\t0\nほそう\t舗装\t\t1\nこうぞうけいさん\t構造計算\tメモ\t0", text, "FFI の形式");
        }
        finally { restore(); }
    }

    [Test]
    public static void Terms_Edit_AddsToUserDictionary_AndExcludesOriginal()
    {
        var directory = Use(out var restore);
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            var path = Path.Combine(directory, "userdict.txt");
            var gui = new UserDictionary(path, builtIn: false);
            var ime = new UserDictionary(path);

            Assert.True(DictionaryManagement.EditTerm(gui, new UserWord("こうぞうぶつ", "構造物"), "こうぞうぶつ", "構造仏", out var added) is null, "直せる");
            Assert.True(added, "ユーザー辞書に新しく登録した");
            Assert.Equal("構造仏", ime.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "直した語が出て、元の語は出ない (除外)");
            Assert.True(TermDomains.Excluded().Contains(("こうぞうぶつ", "構造物")), "元の語は除外の一覧に");

            // 読みも語も変えない「編集」は、ユーザー辞書への複製だけ (除外しない)
            Assert.True(DictionaryManagement.EditTerm(gui, new UserWord("ほそう", "舗装"), "ほそう", "舗装", out added) is null && added, "複製");
            Assert.True(!TermDomains.Excluded().Contains(("ほそう", "舗装")), "除外しない");

            // 直した語がすでにユーザー辞書にあれば、登録はせず除外だけ
            gui.AddNew("こうぞうけいさん", "構造計算書");
            Assert.True(DictionaryManagement.EditTerm(gui, new UserWord("こうぞうけいさん", "構造計算"), "こうぞうけいさん", "構造計算書", out added) is null, "確認");
            Assert.True(!added && TermDomains.Excluded().Contains(("こうぞうけいさん", "構造計算")), "登録はせず除外だけ");

            // 不正な入力なら何も変えない
            var count = gui.Count;
            Assert.Equal(UserDictionary.Validate("こ", "x"), DictionaryManagement.EditTerm(gui, new UserWord("ほそう", "舗装"), "こ", "x", out _), "不正");
            Assert.True(gui.Count == count && !TermDomains.Excluded().Contains(("ほそう", "舗装")), "何も変えない");
        }
        finally { restore(); }
    }

    // ---- config.json を 2 つのプロセスが同時に書いても消えない ----

    [Test]
    public static void Config_ConcurrentWriters_KeepBothChanges()
    {
        var directory = Use(out var restore);
        var previous = ContinueAfterConversionSetting.ConfigPath;
        try
        {
            var path = Path.Combine(directory, "config.json");
            ContinueAfterConversionSetting.ConfigPath = () => path;
            ContinueAfterConversionSetting.Reset();
            var failures = 0;
            Parallel.Invoke(
                () => { for (var i = 0; i < 20; i++) if (!TermDomains.Set(i % 2 == 0 ? "civil" : "medical", true)) Interlocked.Increment(ref failures); },
                () => { for (var i = 0; i < 20; i++) if (!ContinueAfterConversionSetting.Set(true)) Interlocked.Increment(ref failures); });
            Assert.Equal(0, failures, "どれも保存できる");
            var saved = Settings.Load(path);
            Assert.Equal("civil,medical", string.Join(",", saved.EnabledTermDomains), "分野の切り替えが残る");
            Assert.True(saved.ContinueAfterConversion, "もう一方の設定も残る (読み直してから書くので消し合わない)");
        }
        finally
        {
            ContinueAfterConversionSetting.ConfigPath = previous;
            ContinueAfterConversionSetting.Reset();
            restore();
        }
    }

    // ---- 取り込み・書き出し・形式 ----

    [Test]
    public static void Management_ImportExport_ThroughFiles()
    {
        var directory = Use(out var restore);
        try
        {
            var source = new UserDictionary(Path.Combine(directory, "a.txt"), builtIn: false);
            source.AddRange([new UserWord("ゆきしろ", "雪代"), new UserWord("めるたいぷ", "Meltype")]);
            var exported = Path.Combine(directory, "export.txt");
            Assert.True(DictionaryManagement.Export(source, exported, out var count) is null && count == 2, "書き出せる");
            Assert.True(File.ReadAllBytes(exported) is [0xFF, 0xFE, ..], "Microsoft IME の形式 (UTF-16)");
            if (!OperatingSystem.IsWindows()) Assert.Equal(0x180, Mode(exported), "書き出したファイルも本人だけが読める");

            var target = new UserDictionary(Path.Combine(directory, "b.txt"), builtIn: false);
            target.AddNew("ゆきしろ", "雪代");
            Assert.True(DictionaryManagement.Import(target, exported, out var summary) is null, "取り込める");
            Assert.Equal("1\t1\t0\tUTF-16", summary, "登録した数・登録済み・飛ばした行・文字コード");
            Assert.Equal(2, target.Count, "確認");

            Assert.True(DictionaryManagement.Import(target, Path.Combine(directory, "none.txt"), out _) is not null, "無いファイル");
            File.WriteAllBytes(Path.Combine(directory, "sjis.txt"), [0x82, 0xA0, 0x09, 0x82, 0xA2, 0x0A, 0x82]);
            Assert.True(DictionaryManagement.Import(target, Path.Combine(directory, "sjis.txt"), out _) is { } error && error.Contains("文字コード"), "読めない文字コードは理由を返す");
        }
        finally { restore(); }
    }

    [Test]
    public static void Management_Formats_RoundTrip()
    {
        var words = new[] { new UserWord("きごう", "記号"), new UserWord("めるたいぷ", "Meltype") };
        var text = DictionaryManagement.FormatWords(words);
        Assert.Equal("きごう\t記号\nめるたいぷ\tMeltype", text, "読み Tab 単語");
        Assert.True(DictionaryManagement.ParseWords(text).SequenceEqual(words), "読み戻せる");
        Assert.Equal(1, DictionaryManagement.ParseWords("きごう\t記号\n\n欄が足りない\n\t空の読み\n").Count, "欄が足りない行・空の読みは飛ばす");
        Assert.Equal(0, DictionaryManagement.ParseWords(null).Count, "NULL は空");
        var indexed = DictionaryManagement.FormatIndexed([(3, words[0]), (10, words[1])]);
        Assert.Equal("3\tきごう\t記号\n10\tめるたいぷ\tMeltype", indexed, "位置 Tab 読み Tab 単語");
        Assert.Equal("3:記号,10:Meltype", string.Join(",", DictionaryManagement.ParseIndexed(indexed).Select(e => $"{e.Index}:{e.Word.Word}")), "読み戻せる");
        Assert.Equal(0, DictionaryManagement.ParseIndexed("x\tきごう\t記号\n-1\tきごう\t記号").Count, "位置が数でない・負なら飛ばす");
    }

    [Test]
    public static void Management_ToReading()
    {
        Assert.Equal("きごうとう", DictionaryManagement.ToReading("kigoutou"), "ローマ字");
        Assert.Equal("きごうとう", DictionaryManagement.ToReading(" KigouTou "), "大文字・前後の空白");
        Assert.Equal("かたかな", DictionaryManagement.ToReading("カタカナ"), "カタカナ");
        Assert.Equal("きごう", DictionaryManagement.ToReading("きごう"), "ひらがなはそのまま");
        Assert.Equal("こんにちは", DictionaryManagement.ToReading("konnnitiha"), "nn");
    }

    [Test]
    public static void Backup_IncludesTermExclusions()
    {
        Assert.True(Backup.IsRestorableName("terms-excluded.txt"), "除外した語もバックアップ・復元の対象");
    }
}
