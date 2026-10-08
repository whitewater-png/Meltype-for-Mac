// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>セキュリティ監査の修正 (P14) のテスト: 保存の権限・一時ファイル・読み込みの上限・全消去・ユーザー辞書の入力チェック。</summary>
internal static class SecurityTests
{
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-sec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>ファイルの権限 (Unix の下 9 ビット)。</summary>
    // Windows では呼ばない (各テストが先に IsWindows で抜ける) ので、プラットフォームの警告は抑える。
#pragma warning disable CA1416
    private static int Mode(string path) => (int)File.GetUnixFileMode(path) & 0x1FF;
#pragma warning restore CA1416

    // ---- M2(a) 権限 / L9 一時ファイル ----

    [Test]
    public static void Save_FilesAreOwnerOnly_AndNoTempLeft()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(TempDirectory(), "data");
        var history = new ConversionHistory(Path.Combine(directory, "conversions.json"));
        history.Remember("きごう", "記号");
        var suggestions = new DictionarySuggestions(Path.Combine(directory, "suggest.json"));
        suggestions.Record("きごう", "記号", 3);
        var languages = new LanguageMemory(Path.Combine(directory, "languages.json"));
        languages.Remember("sushi", true, explicitChoice: true);
        var translations = new TranslationHistory(Path.Combine(directory, "translations.json"));
        translations.Remember("ふくざつ", "complex");
        var words = new UserDictionary(Path.Combine(directory, "userdict.txt"), builtIn: false);
        words.Add("きごう", "記号");
        new UserModel(Path.Combine(directory, "model.json")).Reset();
        new Settings().Save(Path.Combine(directory, "config.json"));

        foreach (var name in new[] { "conversions.json", "suggest.json", "languages.json", "translations.json", "userdict.txt", "model.json", "config.json" })
        {
            Assert.True(File.Exists(Path.Combine(directory, name)), $"{name} が保存される");
            Assert.Equal(0x180, Mode(Path.Combine(directory, name)), $"{name} は 0600");
        }
        Assert.Equal(0x1C0, Mode(directory), "新しく作ったフォルダーは 0700");
        Assert.True(Directory.GetFiles(directory, "*.tmp").Length == 0, "一時ファイルを残さない");
    }

    [Test]
    public static void Save_FixesPermissionOfExistingFile()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = TempDirectory();
        var path = Path.Combine(directory, "conversions.json");
        File.WriteAllText(path, "{}");
#pragma warning disable CA1416
        File.SetUnixFileMode(path, (UnixFileMode)0x1A4); // 0644
#pragma warning restore CA1416
        new ConversionHistory(path).Remember("きごう", "記号");
        Assert.Equal(0x180, Mode(path), "前の版が 0644 で作ったファイルも、保存すると 0600 になる");
    }

    [Test]
    public static void Save_TempNameIsUnique_AndFailureLeavesNoTemp()
    {
        var directory = TempDirectory();
        // 置き換え先がフォルダー (移せない) なので失敗する。一時ファイルは消える。
        var path = Path.Combine(directory, "target");
        Directory.CreateDirectory(path);
        var failed = false;
        try { SafeFile.WriteAllText(path, "x"); }
        catch { failed = true; }
        Assert.True(failed, "置き換えに失敗したら例外になる");
        Assert.True(Directory.GetFiles(directory).Length == 0, "失敗しても一時ファイルを残さない");

        // 固定の .tmp が残っていても (前の版が落ちたあと) 影響されない。
        var ok = Path.Combine(directory, "ok.json");
        File.WriteAllText(ok + ".tmp", "stale");
        SafeFile.WriteAllText(ok, "{}");
        Assert.Equal("{}", File.ReadAllText(ok), "保存できる");
        Assert.Equal("stale", File.ReadAllText(ok + ".tmp"), "固定名の .tmp は使わない");
    }

    // ---- L8 読み込みの上限 ----

    [Test]
    public static void Load_OversizedFilesAreSkipped()
    {
        var directory = TempDirectory();
        var conversions = Path.Combine(directory, "conversions.json");
        var suggest = Path.Combine(directory, "suggest.json");
        var languages = Path.Combine(directory, "languages.json");
        var translations = Path.Combine(directory, "translations.json");
        var userdict = Path.Combine(directory, "userdict.txt");
        new ConversionHistory(conversions).Remember("きごう", "記号");
        new DictionarySuggestions(suggest).Record("きごう", "記号", 3);
        new LanguageMemory(languages).Remember("sushi", true, explicitChoice: true);
        new TranslationHistory(translations).Remember("ふくざつ", "complex");
        new UserDictionary(userdict, builtIn: false).Add("きごう", "記号");

        Assert.Equal(1, new ConversionHistory(conversions).Count, "上限内なら読める");
        var saved = SafeFile.MaxReadBytes;
        try
        {
            SafeFile.MaxReadBytes = 10;
            Assert.Equal(0, new ConversionHistory(conversions).Count, "上限を超えた変換履歴は空で続ける");
            Assert.Equal(0, new DictionarySuggestions(suggest).Count, "上限を超えた提案データは空で続ける");
            Assert.Equal(0, new LanguageMemory(languages).Count, "上限を超えた言語の学習は空で続ける");
            Assert.Equal(0, new TranslationHistory(translations).Get("ふくざつ").Count, "上限を超えた英訳の学習は空で続ける");
            Assert.Equal(0, new UserDictionary(userdict, builtIn: false).Count, "上限を超えたユーザー辞書は空で続ける");
        }
        finally
        {
            SafeFile.MaxReadBytes = saved;
        }
        Assert.Equal(1, new ConversionHistory(conversions).Count, "上限を戻せば読める (ファイルは消していない)");
    }

    [Test]
    public static void Load_Oversized_IsBackedUpAndNotOverwritten()
    {
        var directory = TempDirectory();
        var userdict = Path.Combine(directory, "userdict.txt");
        var conversions = Path.Combine(directory, "conversions.json");
        new UserDictionary(userdict, builtIn: false).Add("きごう", "記号");
        new ConversionHistory(conversions).Remember("きごう", "記号");
        var originalDict = File.ReadAllText(userdict);
        var originalHistory = File.ReadAllText(conversions);
        var saved = SafeFile.MaxReadBytes;
        try
        {
            SafeFile.MaxReadBytes = 10;
            var words = new UserDictionary(userdict, builtIn: false);
            Assert.Equal(0, words.Count, "上限超過は空で続ける");
            // 同じファイルをもう一度読んでも、退避が増えない
            _ = new UserDictionary(userdict, builtIn: false);
            Assert.True(!File.Exists(userdict + ".oversize.1"), "同じ大きさなら再度コピーしない");
            words.Add("あたらしい", "新しい");
            var history = new ConversionHistory(conversions);
            history.Remember("こうほ", "候補");
            Assert.Equal(originalDict, File.ReadAllText(userdict + ".oversize"), "元のユーザー辞書が .oversize に残る");
            Assert.Equal(originalHistory, File.ReadAllText(conversions + ".oversize"), "元の変換履歴が .oversize に残る");
            // 保存で置き換わったあとは大きさが変わるので、次に超過したときは連番になる
            SafeFile.MaxReadBytes = 1;
            _ = new UserDictionary(userdict, builtIn: false);
            Assert.True(File.Exists(userdict + ".oversize.1"), "別の内容なら連番で退避する");
        }
        finally
        {
            SafeFile.MaxReadBytes = saved;
        }
    }

    [Test]
    public static void Load_Oversized_BackupFailureBlocksSave()
    {
        var directory = TempDirectory();
        var conversions = Path.Combine(directory, "conversions.json");
        new ConversionHistory(conversions).Remember("きごう", "記号");
        var original = File.ReadAllText(conversions);
        var saved = SafeFile.MaxReadBytes;
        try
        {
            SafeFile.MaxReadBytes = 10;
            SafeFile.FailBackupForTests = true;
            var history = new ConversionHistory(conversions);
            history.Remember("こうほ", "候補");
            Assert.True(SafeFile.IsBlocked(conversions), "退避できなかったパスは保存を止める");
            Assert.Equal(original, File.ReadAllText(conversions), "元のファイルは上書きしない");
            Assert.True(!File.Exists(conversions + ".oversize"), "退避は作られていない");
        }
        finally
        {
            SafeFile.FailBackupForTests = false;
            SafeFile.MaxReadBytes = saved;
            SafeFile.ResetBlockedForTests();
        }
    }

    // ---- M2(b) 全消去 ----

    [Test]
    public static void ClearAll_ClearsLearningButKeepsDictionaryAndConfig()
    {
        var directory = TempDirectory();
        var conversions = Path.Combine(directory, "conversions.json");
        var suggest = Path.Combine(directory, "suggest.json");
        var languages = Path.Combine(directory, "languages.json");
        var translations = Path.Combine(directory, "translations.json");
        var model = Path.Combine(directory, "model.json");
        var userdict = Path.Combine(directory, "userdict.txt");
        var config = Path.Combine(directory, "config.json");

        // 共有インスタンス (メモリ上) を使う: 消去はファイルだけでなくメモリにも効く
        var history = ConversionHistory.Shared(conversions);
        var suggestions = DictionarySuggestions.Shared(suggest);
        var memory = LanguageMemory.Shared(languages);
        var translation = TranslationHistory.Shared(translations);
        history.Remember("きごう", "記号");
        suggestions.Record("きごう", "記号", 3);
        memory.Remember("sushi", true, explicitChoice: true);
        translation.Remember("ふくざつ", "complex");
        new UserModel(model).Reset();
        File.WriteAllText(model, "{\"version\":1,\"prefixes\":{\"ab\":{\"Japanese\":3,\"English\":0,\"LastUsed\":\"2026-01-01T00:00:00Z\"}}}");
        var words = UserDictionary.Shared(userdict);
        words.Add("きごう", "記号");
        new Settings().Save(config);
        var configBefore = File.ReadAllText(config);

        Assert.True(LearningData.ClearAll(history, suggestions, memory, translation, model), "全部消せる");

        Assert.Equal(0, history.Count, "変換履歴 (メモリ)");
        Assert.Equal(0, suggestions.Count, "提案データ (メモリ)");
        Assert.Equal(0, memory.Count, "言語の学習 (メモリ)");
        Assert.Equal(0, translation.Get("ふくざつ").Count, "英訳の学習 (メモリ)");
        Assert.Equal(0, new ConversionHistory(conversions).Count, "変換履歴 (ファイル)");
        Assert.Equal(0, new DictionarySuggestions(suggest).Count, "提案データ (ファイル)");
        Assert.Equal(0, new LanguageMemory(languages).Count, "言語の学習 (ファイル)");
        Assert.Equal(0, new TranslationHistory(translations).Get("ふくざつ").Count, "英訳の学習 (ファイル)");
        Assert.True(!File.ReadAllText(model).Contains("\"ab\""), "ユーザーモデル (ファイル)");

        Assert.Equal(1, words.Count, "ユーザー辞書は消さない (メモリ)");
        Assert.Equal(1, new UserDictionary(userdict, builtIn: false).Count, "ユーザー辞書は消さない (ファイル)");
        Assert.Equal(configBefore, File.ReadAllText(config), "設定は消さない");

        // 消した後に別の学習をしても、消したはずの内容がファイルに戻らない
        history.Remember("こうほ", "候補");
        var reloaded = new ConversionHistory(conversions);
        Assert.Equal(1, reloaded.Count, "消したあとの学習だけが残る");
        Assert.Equal(null, reloaded.Get("きごう"), "消した語は戻らない");
    }

    [Test]
    public static void ClearAll_WithoutModelFile_DoesNotCreateIt()
    {
        var directory = TempDirectory();
        var model = Path.Combine(directory, "model.json");
        Assert.True(LearningData.ClearAll(new ConversionHistory(null), null, null, null, model), "消せる");
        Assert.True(!File.Exists(model), "Mac には無い model.json を作らない");
    }

    [Test]
    public static void ClearLeftovers_RemovesBrokenOversizeAndLogs_ButNotUserData()
    {
        var directory = TempDirectory();
        string P(string name) => Path.Combine(directory, name);
        var learning = new[] { P("conversions.json"), P("model.json") };
        // 消す: 退避コピー・ログ
        foreach (var name in new[] { "conversions.json.broken", "conversions.json.oversize", "conversions.json.oversize.1", "conversions.json.oversize.2", "model.json.broken", "meltype.log", "meltype.log.old", "crash.log" })
            File.WriteAllText(P(name), "打った文字");
        // 消さない: ユーザー辞書とそのバックアップ・設定・除外・別の学習データ・似た名前
        var keep = new[] { "userdict.txt", "userdict.txt.bak", "userdict.txt.oversize", "config.json", "config.json.broken", "terms-excluded.txt", "suggest.json.broken", "conversions.json" };
        foreach (var name in keep) File.WriteAllText(P(name), "残す");
        Assert.True(LearningData.ClearLeftovers(learning, [P("meltype.log"), P("crash.log")]), "消せる");
        foreach (var name in new[] { "conversions.json.broken", "conversions.json.oversize", "conversions.json.oversize.1", "conversions.json.oversize.2", "model.json.broken", "meltype.log", "meltype.log.old", "crash.log" })
            Assert.True(!File.Exists(P(name)), $"{name} が消える");
        foreach (var name in keep) Assert.True(File.Exists(P(name)), $"{name} は消さない");
        Assert.True(LearningData.ClearLeftovers(learning, [P("meltype.log")]), "何も無くても成功する");
    }

    // ---- L4 ユーザー辞書の入力チェック ----

    [Test]
    public static void UserDictionary_RejectsLineBreaksAndTooLong()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.True(dictionary.Add("きごう", "記\n号") is not null, "語の中の改行は拒否");
        Assert.True(dictionary.Add("きごう", "記\r号") is not null, "語の中の CR は拒否");
        Assert.True(dictionary.Add("き\nごう", "記号") is not null, "読みの中の改行は拒否");
        Assert.True(dictionary.Add("きごう", "記\t号") is not null, "タブは今まで通り拒否");
        Assert.True(dictionary.Add("きごう", new string('あ', 101)) is not null, "101 文字の語は拒否");
        Assert.True(dictionary.Add(new string('あ', 101), "記号") is not null, "101 文字の読みは拒否");
        Assert.Equal(0, dictionary.Count, "拒否したものは入らない");
        Assert.True(dictionary.Add("きごう", "記号\n") is null, "末尾の改行は取り除いて登録できる");
        Assert.True(dictionary.Add(new string('あ', 100), new string('字', 100)) is null, "ちょうど 100 文字は登録できる");
        Assert.Equal(2, dictionary.Count, "登録できたもの");

        var imported = new UserDictionary(null, builtIn: false);
        var added = imported.AddRange([new UserWord("きごう", "記\n号"), new UserWord("きごう", new string('字', 101)), new UserWord("きごう", "記号")]);
        Assert.Equal(1, added, "取り込みでも改行・長すぎる語は飛ばす");
    }

    // ---- L5 提案の対象・表示 ----

    [Test]
    public static void Suggest_DisplayWordIsTruncated()
    {
        Assert.Equal("記号", DictionarySuggestions.DisplayWord("記号"), "短い語はそのまま");
        Assert.Equal(new string('字', 20), DictionarySuggestions.DisplayWord(new string('字', 20)), "20 文字まではそのまま");
        Assert.Equal(new string('字', 20) + "…", DictionarySuggestions.DisplayWord(new string('字', 21)), "20 文字を超えたら省略");
        Assert.True(DictionarySuggestions.IsEligible("じょん", "ジョン・スミス"), "中黒は記号扱いにしない");
        Assert.True(!DictionarySuggestions.IsEligible("めーる", "a@b.com"), "記号を含む語は対象外");
    }

    // ---- L7 再変換の長さ ----

    [Test]
    public static void Reconvert_TooLongSelectionPassesThrough()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());
        Assert.True(session.Reconvert(new string('あ', MeltypeSession.MaxReconvertLength)).Consumed, "200 文字までは再変換する");
        session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());
        Assert.True(!session.Reconvert(new string('あ', MeltypeSession.MaxReconvertLength + 1)).Consumed, "200 文字を超えたらアプリに渡す");
    }
}
