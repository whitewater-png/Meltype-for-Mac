// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// R3: 読みをそろえる処理を ReadingNormalizer に集めても、呼び出し元 (ユーザー辞書・取り込み・専門用語集・辞書アプリの補助) の結果と、
/// 保存されるファイルのバイト列が変わらないこと。このファイルは集める前からある API だけを使う (集める前のコードでも同じく通る)。
/// 比べる相手は LegacyReadingNormalization (集める前の実装の写し) と、集める前のコードで取った保存結果 (下の期待値)。
/// </summary>
internal static class ReadingNormalizationCallSiteTests
{
    private const string Word = "語";

    private static IEnumerable<string> AllSingleChars()
    {
        for (var code = 0; code <= 0xFFFF; code++) yield return ((char)code).ToString();
    }

    [Test]
    public static void UserDictionary_NormalizeReading_IsUnchanged()
    {
        Assert.True(LegacyReadingNormalization.Samples.Length >= 30, "代表的な読みは 30 件以上");
        foreach (var text in AllSingleChars().Concat(LegacyReadingNormalization.Samples))
        {
            Assert.Equal(LegacyReadingNormalization.UserDictionaryNormalizeReading(text), UserDictionary.NormalizeReading(text), $"NormalizeReading U+{Codes(text)}");
        }
    }

    [Test]
    public static void UserDictionary_Validate_IsUnchanged()
    {
        // 1 文字だけの読み・ひらがなに挟んだ読みの両方で、全文字について登録の判定 (理由の文言まで) が変わらない
        foreach (var c in AllSingleChars())
        {
            foreach (var reading in new[] { c, "あ" + c + "い" })
            {
                Assert.Equal(LegacyReadingNormalization.UserDictionaryValidate(reading, Word), UserDictionary.Validate(reading, Word), $"Validate U+{Codes(reading)}");
            }
        }
        foreach (var text in LegacyReadingNormalization.Samples)
        {
            var reading = UserDictionary.NormalizeReading(text);
            Assert.Equal(LegacyReadingNormalization.UserDictionaryValidate(reading, Word), UserDictionary.Validate(reading, Word), $"Validate U+{Codes(text)}");
        }
    }

    [Test]
    public static void UserDictionaryFile_Parse_IsUnchanged()
    {
        // 取り込み: 1 文字ずつ (読み 2 文字にするため「あ」を前に) と代表的な読みを、1 行ずつのファイルにして読む
        var readings = AllSingleChars().Where(c => c[0] is not ('\t' or '\n' or '\r' or '\0') && !char.IsSurrogate(c[0])).Select(c => "あ" + c)
            .Concat(LegacyReadingNormalization.Samples.Where(s => !s.Contains('\t') && !s.Contains('\n') && !s.Contains('\r') && !s.Any(char.IsSurrogate)))
            .ToList();
        var text = new StringBuilder();
        foreach (var reading in readings) text.Append(reading).Append('\t').Append(Word).Append("\t名詞\n");
        var result = UserDictionaryFile.Parse(Encoding.UTF8.GetBytes(text.ToString()));

        var expected = new List<UserWord>();
        var expectedSkipped = 0;
        foreach (var reading in readings)
        {
            var line = $"{reading}\t{Word}\t名詞";
            if (line[0] is '!' or '#') continue;
            if (LegacyReadingNormalization.UserDictionaryFileParseLine([reading, Word, "名詞"]) is { } word) expected.Add(word);
            else expectedSkipped++;
        }
        Assert.Equal(expected.Count, result.Words.Count, "取り込める語の数");
        Assert.Equal(expectedSkipped, result.Skipped, "飛ばした行の数");
        for (var i = 0; i < expected.Count; i++) Assert.Equal(expected[i], result.Words[i], $"取り込んだ語 {i}");
    }

    [Test]
    public static void TermDictionary_TryParseLine_ReadingIsUnchanged()
    {
        foreach (var text in AllSingleChars().Select(c => "あ" + c + "Ａ").Concat(LegacyReadingNormalization.Samples).Where(t => !t.Contains('\t')))
        {
            if (!TermDictionary.TryParseLine($"{text}\t{Word}", out var reading, out _, out _)) continue;
            Assert.Equal(LegacyReadingNormalization.TermDictionaryNormalize(text.AsSpan().Trim()), reading, $"TryParseLine U+{Codes(text)}");
        }
    }

    [Test]
    public static void TermDomains_NormalizeUserWord_IsUnchanged()
    {
        foreach (var text in AllSingleChars().Select(c => "あ" + c).Concat(LegacyReadingNormalization.Samples))
        {
            var legacyReading = LegacyReadingNormalization.TermDomainsUserReading(text);
            var legacyInvalid = LegacyReadingNormalization.TermDomainsNormalizeUserWordReason(text, Word);
            var invalid = TermDomains.NormalizeUserWord(text, Word, out var reading, out var word);
            Assert.Equal(legacyReading, reading, $"NormalizeUserWord の読み U+{Codes(text)}");
            Assert.Equal(Word, word);
            // 入れられるかどうかと理由の文言は、どちらの側が null でも比べる (旧実装で通る読みに新実装が理由を返しても、その逆でも落ちる)
            Assert.Equal(legacyInvalid is null, invalid is null, $"NormalizeUserWord で入れられるか U+{Codes(text)}: 旧={legacyInvalid ?? "null"} 新={invalid ?? "null"}");
            Assert.Equal(legacyInvalid ?? "null", invalid ?? "null", $"NormalizeUserWord の理由 U+{Codes(text)}");
        }
    }

    [Test]
    public static void DictionaryManagement_ToReading_IsUnchanged()
    {
        foreach (var text in AllSingleChars().Where(c => !char.IsAsciiLetter(c[0])).Concat(LegacyReadingNormalization.Samples))
        {
            Assert.Equal(LegacyReadingNormalization.DictionaryManagementToReading(text), DictionaryManagement.ToReading(text), $"ToReading U+{Codes(text)}");
        }
    }

    // ---- 保存されるバイト列 (一時フォルダーで読み書きして、集める前のコードで取った結果と比べる) ----

    /// <summary>userdict.txt を手で書いた形 (カタカナ・大文字・半角カナ・前後の空白・制御文字の読み) で置き、読み込み・登録・取り込み・削除をしたあとのファイル。</summary>
    [Test]
    public static void UserDict_RoundTrip_SavesSameBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-r3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "userdict.txt");
        try
        {
            File.WriteAllText(path, "# 手で書いたコメント\nキゴウ\t記号\nABCけい\tABC系\n  きごう \t 記号 \nｷｺﾞｳ\t記号\nゔぁいおりん\tヴァイオリン\nらーめん・や\tラーメン屋\n50cc\t50cc\nＡＢＣ\t全角\nき\u0001ごう\t制御\n", new UTF8Encoding(false));
            var dictionary = new UserDictionary(path, builtIn: false);
            var log = new List<string>
            {
                "words=" + string.Join("|", dictionary.Words.Select(w => $"{w.Reading}:{w.Word}")),
                "lookup(きごう)=" + string.Join("|", dictionary.Lookup("きごう")),
                "lookup(abcけい)=" + string.Join("|", dictionary.Lookup("abcけい")),
                "addnew(ニホン)=" + (dictionary.AddNew(" ニホン ", "日本") ?? "ok"),
                "addnew(ABCけい)=" + (dictionary.AddNew("ABCけい", "ABC系") ?? "ok"),
                "addnew(ｷｺﾞｳ)=" + (dictionary.AddNew("ｷｺﾞｳ", "記号") ?? "ok"),
                "addnew(キゴウ)=" + (dictionary.AddNew("キゴウ", "記号") ?? "ok"),
            };
            var import = UserDictionaryFile.Parse(Encoding.UTF8.GetBytes("!Microsoft IME\r\nＡＢＣケイ\tABC系\t名詞\r\nｷｺﾞｳ\t記号\t名詞\r\nヴァイオリン\tバイオリン\t名詞\r\nABCケイ\tABC系\t名詞\r\nキゴウ\t記号\t名詞\r\n"));
            log.Add("import=" + string.Join("|", import.Words.Select(w => $"{w.Reading}:{w.Word}")) + $" skipped={import.Skipped}");
            log.Add("addmany=" + (dictionary.AddMany(import.Words, out var added) ?? "ok") + " added=" + string.Join("|", added.Select(w => $"{w.Reading}:{w.Word}")));
            log.Add("removerange=" + (dictionary.RemoveRange([new UserWord(" ラーメン・ヤ ", "ラーメン屋")], out var removed) ?? "ok") + " removed=" + string.Join("|", removed.Select(r => $"{r.Index}:{r.Word.Reading}:{r.Word.Word}")));

            Assert.Equal(string.Join("\n", ExpectedUserDictLog), string.Join("\n", log), "操作の結果");
            var expectedBytes = Encoding.UTF8.GetBytes(ExpectedUserDictFile.Replace("\n", Environment.NewLine));
            var actualBytes = File.ReadAllBytes(path);
            Assert.True(expectedBytes.AsSpan().SequenceEqual(actualBytes), $"保存したバイト列が集める前と同じ。実際: {Escape(Encoding.UTF8.GetString(actualBytes))}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly string[] ExpectedUserDictLog =
    [
        "words=キゴウ:記号|ABCけい:ABC系|きごう:記号|ｷｺﾞｳ:記号|ゔぁいおりん:ヴァイオリン|らーめん・や:ラーメン屋|50cc:50cc|ＡＢＣ:全角",
        "lookup(きごう)=記号",
        "lookup(abcけい)=",
        "addnew(ニホン)=ok",
        "addnew(ABCけい)=同じ読みと単語が、すでに登録されています。",
        "addnew(ｷｺﾞｳ)=読みはひらがなで入力してください (カタカナは自動でひらがなになります)。",
        "addnew(キゴウ)=同じ読みと単語が、すでに登録されています。",
        "import=ゔぁいおりん:バイオリン|ABCけい:ABC系|きごう:記号 skipped=2",
        "addmany=ok added=ゔぁいおりん:バイオリン",
        "removerange=ok removed=5:らーめん・や:ラーメン屋",
    ];

    private const string ExpectedUserDictFile = "\uFEFF# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」\nキゴウ\t記号\nABCけい\tABC系\nきごう\t記号\nｷｺﾞｳ\t記号\nゔぁいおりん\tヴァイオリン\n50cc\t50cc\nＡＢＣ\t全角\nにほん\t日本\nゔぁいおりん\tバイオリン\n";

    /// <summary>自作の専門用語集と除外の一覧に、そろえ方の違う読みで語を入れたあとのファイル。</summary>
    [Test]
    public static void UserTermDomain_RoundTrip_SavesSameBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-r3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousConfig = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        var previousUser = TermDomains.UserDirectory;
        var previousInterval = TermDomains.ExternalCheckIntervalMs;
        TermDomains.ConfigPath = () => Path.Combine(directory, "config.json");
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.UserDirectory = () => Path.Combine(directory, "terms");
        TermDomains.Source = () => [("civil", () => "こうがく\t工学\nキゴウ\t記号\nＡＢＣけい\tABC系\n")];
        TermDomains.ExternalCheckIntervalMs = 0;
        TermDomains.Reset();
        try
        {
            var log = new List<string>();
            log.Add("create=" + (TermDomains.CreateUserDomain("R3", out var id) ?? "ok"));
            var items = new (string, string, string)[]
            {
                (" キゴウ ", "記号", "メモ"), ("ＡＢＣケイ", "ABC系", ""), ("ABCけい", "ABC系", ""), ("ｷｺﾞｳ", "記号", ""), ("ヴァイオリン", "バイオリン", ""),
                ("５０ＣＣ", "50cc", ""), ("らーめん・や", "ラーメン屋", ""), ("Kigou", "記号", ""), ("ヵげつ", "か月", ""),
            };
            log.Add("add=" + (TermDomains.AddUserWords(id, items, out var added, out var skipped) ?? "ok")
                + " added=" + string.Join("|", added.Select(e => $"{e.Reading}:{e.Word}"))
                + " skipped=" + string.Join("|", skipped.Select(s => $"{s.Item.Reading}:{s.Item.Word}:{s.Reason}")));
            log.Add("check=" + (TermDomains.CheckUserWord(id, "abcケイ", "ABC系", null) ?? "ok"));
            log.Add("check-except=" + (TermDomains.CheckUserWord(id, "abcケイ", "ABC系", ("ＡＢＣけい", "ABC系")) ?? "ok"));
            log.Add("update=" + (TermDomains.UpdateUserWord(id, ("ﾗｰﾒﾝ・ﾔ", "ラーメン屋"), "ラーメン・ヤ", "ラーメン屋") ?? "ok"));
            log.Add("update2=" + (TermDomains.UpdateUserWord(id, (" ラーメン・ヤ ", "ラーメン屋"), "ラーメン・ヤ", "らーめん屋") ?? "ok"));
            // 消す側も追加と同じ深さでそろえる: 全角・大文字・カタカナ・前後の空白の読みで、保存済みの abcけい の行を消せる (ＫＩＧＯＵ は追加で弾かれたので消す行が無い)
            log.Add("remove=" + (TermDomains.RemoveUserWords(id, [(" ＫＩＧＯＵ ", "記号"), (" ＡＢＣケイ ", "ABC系")], out var removed) ?? "ok") + " removed=" + string.Join("|", removed.Select(r => $"{r.Index}:{r.Entry.Reading}:{r.Entry.Word}")));
            log.Add("exclude=" + (TermDomains.SetExcluded([(" キゴウ ", "記号"), ("ＡＢＣケイ", "ABC系")], excluded: true) ?? "ok"));

            Assert.Equal(string.Join("\n", ExpectedTermLog), string.Join("\n", log), "操作の結果");
            var files = Directory.EnumerateFiles(Path.Combine(directory, "terms"), "terms-*.txt").ToList();
            Assert.Equal(1, files.Count, "自作の分野のファイルは 1 つ");
            // 作成日時の行だけは毎回変わるので * に置き換えて比べる
            var domainText = Encoding.UTF8.GetString(File.ReadAllBytes(files[0]));
            var domainCompared = System.Text.RegularExpressions.Regex.Replace(domainText, "^# 作成: [^\r\n]*", "# 作成: *", System.Text.RegularExpressions.RegexOptions.Multiline);
            var excludedText = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "terms-excluded.txt")));
            Assert.Equal(Escape(ExpectedTermFile.Replace("\n", Environment.NewLine)), Escape(domainCompared), "自作の分野のファイルのバイト列");
            Assert.Equal(Escape(ExpectedExcludedFile.Replace("\n", Environment.NewLine)), Escape(excludedText), "除外の一覧のバイト列");
            Assert.True(Encoding.UTF8.GetBytes(domainText).AsSpan().SequenceEqual(File.ReadAllBytes(files[0])), "UTF-8 として往復する (比べた文字列がファイルのバイト列そのもの)");
        }
        finally
        {
            TermDomains.ConfigPath = previousConfig;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.UserDirectory = previousUser;
            TermDomains.ExternalCheckIntervalMs = previousInterval;
            TermDomains.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static readonly string[] ExpectedTermLog =
    [
        "create=ok",
        "add=ok added=きごう:記号|abcけい:ABC系|ゔぁいおりん:バイオリン|らーめん・や:ラーメン屋|ゕげつ:か月 skipped=ｷｺﾞｳ:記号:読みはひらがなで入力してください (カタカナは自動でひらがなになります)。|５０ＣＣ:50cc:読みはひらがなで入力してください (カタカナは自動でひらがなになります)。|Kigou:記号:読みはひらがなで入力してください (カタカナは自動でひらがなになります)。",
        "check=同じ読みと単語が、すでに登録されています。",
        "check-except=ok",
        "update=元の語が見つかりません (ほかの画面で変更・削除された可能性があります)。一覧を読み直してください。",
        "update2=ok",
        "remove=ok removed=1:abcけい:ABC系",
        "exclude=ok",
    ];

    private const string ExpectedTermFile = "# 名称: R3\n# 出典: 自作\n# 作成: *\n# 1 行に「読み<Tab>語」(3 つ目の欄は注記。省略できます)。「Meltype 辞書」の画面で作った専門用語集です。\nきごう\t記号\tメモ\nゔぁいおりん\tバイオリン\nらーめん・や\tらーめん屋\nゕげつ\tか月\n";

    private const string ExpectedExcludedFile = "# Meltype 専門用語集で使わない語 (「Meltype 辞書」の画面で除外したもの): 1 行に「読み<Tab>語」\nabcけい\tABC系\nきごう\t記号\n";

    /// <summary>
    /// 専門用語集の「編集」(DictionaryManagement.EditTerm): 読みも語も変えていないかは専門用語集の深さ (全角→半角・大文字→小文字まで) で比べる。
    /// 大文字・全角の違いだけなら「変えていない」なので、元の語を除外しない (ユーザー辞書への複製だけ)。
    /// </summary>
    [Test]
    public static void DictionaryManagement_EditTerm_ComparesAtTermDictionaryDepth()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-r3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousConfig = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        var previousInterval = TermDomains.ExternalCheckIntervalMs;
        TermDomains.ConfigPath = () => Path.Combine(directory, "config.json");
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.Source = () => [("civil", () => "abcけい\tABC系\n")];
        TermDomains.ExternalCheckIntervalMs = 0;
        TermDomains.Reset();
        try
        {
            var gui = new UserDictionary(Path.Combine(directory, "userdict.txt"), builtIn: false);
            string Excluded() => string.Join("|", TermDomains.Excluded().Select(e => $"{e.Reading}:{e.Word}"));
            var log = new List<string>();
            // 編集後の読みが大文字・カタカナ・前後の空白 (元は半角小文字)
            log.Add("upper=" + (DictionaryManagement.EditTerm(gui, new UserWord("abcけい", "ABC系"), " ABCケイ ", "ABC系", out var added) ?? "ok") + $" added={added} excluded={Excluded()}");
            // 元の語の読みが全角大文字 (編集後は半角小文字)
            log.Add("fullwidth=" + (DictionaryManagement.EditTerm(gui, new UserWord(" ＡＢＣけい ", "ABC系"), "abcけい", "ABC系", out added) ?? "ok") + $" added={added} excluded={Excluded()}");
            // 対照: 語を変えたら元の語を除外する (除外の一覧に書ける構成であることの確認)
            log.Add("changed=" + (DictionaryManagement.EditTerm(gui, new UserWord("abcけい", "ABC系"), "ABCけい", "ABC形", out added) ?? "ok") + $" added={added} excluded={Excluded()}");
            log.Add("words=" + string.Join("|", gui.Words.Select(w => $"{w.Reading}:{w.Word}")));
            Assert.Equal(string.Join("\n", ExpectedEditTermLog), string.Join("\n", log), "操作の結果");
        }
        finally
        {
            TermDomains.ConfigPath = previousConfig;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.ExternalCheckIntervalMs = previousInterval;
            TermDomains.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static readonly string[] ExpectedEditTermLog =
    [
        "upper=ok added=True excluded=",
        "fullwidth=ok added=True excluded=",
        "changed=ok added=True excluded=abcけい:ABC系",
        "words=ABCけい:ABC系|abcけい:ABC系|ABCけい:ABC形",
    ];

    private static string Codes(string text) => string.Join(" ", text.Select(c => ((int)c).ToString("X4")));

    /// <summary>失敗したときに見えるように、BOM・改行・制御文字を \u 表記にする。</summary>
    internal static string Escape(string text) => string.Concat(text.Select(c => c is '\n' ? "\\n" : c < 0x20 || c == 0xFEFF ? $"\\u{(int)c:X4}" : c.ToString()));
}
