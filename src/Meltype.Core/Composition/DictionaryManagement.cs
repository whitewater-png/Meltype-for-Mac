// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 辞書の管理画面 (Mac の「Meltype 辞書」アプリ。IME とは別のプロセス) が使う操作と、FFI でやり取りする文字列の形式。
/// FFI (src/Meltype.Mac.Native/Exports.cs の meltype_userdict_* / meltype_term_*) はここを呼ぶだけにして、
/// 中身は managed のテスト (DictionaryManagementTests) で確かめられるようにする。
/// 文字列の形式は既存の FFI (meltype_term_domains) と同じく、1 行 1 件・欄は Tab 区切り。読み・語には Tab・改行が入らない (UserDictionary.Validate)。
/// </summary>
internal static class DictionaryManagement
{
    /// <summary>取り込むファイルの大きさの上限 (ユーザー辞書の読み込みの上限と同じ)。</summary>
    internal const long MaxImportBytes = 20L * 1024 * 1024;

    // 管理画面のプロセスで使うユーザー辞書。同梱の語句・専門用語集は読まない (画面に出さず、変換もしないので)。
    // IME のプロセスの共有インスタンスとは別のプロセスだが、ファイルを正にしているので (UserDictionary)、互いの変更は消えない。
    private static readonly Lazy<UserDictionary> s_userDictionary = new(() => new UserDictionary(AppPaths.UserDictionaryFile, builtIn: false));

    /// <summary>管理画面のプロセスのユーザー辞書。</summary>
    public static UserDictionary UserWords => s_userDictionary.Value;

    // 管理画面のプロセスの「学習した語」(languages.json)。IME のプロセスとは別のインスタンスだが、変更の前とファイルの版が変わったときに
    // 読み直すので (LanguageMemory.Refresh)、互いの変更は消えない。
    // 「2 回目を待っている」かの判定 (Active) を IME と同じにするため、よく使う日本語・読み切れるローマ字の判定をここでも付ける
    // (付けないと、kyouha を 1 回英字にしただけの語が、画面では「有効」と出てしまう)。
    private static readonly Lazy<LanguageMemory> s_languages = new(() =>
    {
        var memory = LanguageMemory.Shared(AppPaths.LanguageMemoryFile);
        var romaji = new Detection.RomajiDetector();
        memory.IsCommonJapanese ??= RomajiTypoCorrector.Load(romaji).IsCommonJapanese;
        memory.IsReadableRomaji ??= word => romaji.AnalyzeFragment(word) is { IsValid: true, Partial: "" };
        return memory;
    });

    /// <summary>管理画面のプロセスの「学習した語」(英語 / 日本語の学習)。</summary>
    public static LanguageMemory Languages => s_languages.Value;

    /// <summary>
    /// 学習した語を 1 行 1 語で返す (新しく使ったものから)。欄は Tab 区切りで
    /// 「語 \t E (英字) か J (かな) \t 回数 \t 最後に使った日時 (UTC、ISO 8601 の \"o\") \t 効いていれば 1、2 回目を待っていれば 0」。0 語なら空文字列。
    /// </summary>
    public static string FormatLearned(IEnumerable<LanguageMemory.Learned> entries) =>
        string.Join('\n', entries.Select(e =>
            $"{e.Word}\t{(e.English ? 'E' : 'J')}\t{e.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t" +
            $"{(e.Used.Kind == DateTimeKind.Local ? e.Used.ToUniversalTime() : DateTime.SpecifyKind(e.Used, DateTimeKind.Utc)).ToString("o", System.Globalization.CultureInfo.InvariantCulture)}\t" +
            $"{(e.Active ? 1 : 0)}"));

    /// <summary>改行で区切った語を読む (空の行は飛ばす。Tab があれば最初の欄だけ = FormatLearned の行をそのまま渡してもよい)。</summary>
    public static List<string> ParseLearnedWords(string? text) =>
        (text ?? "").Split('\n').Select(line => line.TrimEnd('\r').Split('\t')[0].Trim()).Where(word => word.Length > 0).ToList();

    // ---- 文字列の形式 ----

    /// <summary>「読み Tab 単語」を改行でつなぐ (ファイルの順)。</summary>
    public static string FormatWords(IEnumerable<UserWord> words) => string.Join('\n', words.Select(w => $"{w.Reading}\t{w.Word}"));

    /// <summary>「読み Tab 単語」の行を読む (欄が足りない行は飛ばす。3 つ目以降の欄は読まない)。</summary>
    public static List<UserWord> ParseWords(string? text)
    {
        var words = new List<UserWord>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length >= 2 && fields[0].Length > 0) words.Add(new UserWord(fields[0], fields[1]));
        }
        return words;
    }

    /// <summary>「位置 Tab 読み Tab 単語」を改行でつなぐ (削除した語と元の位置。元に戻すときにそのまま返してもらう)。</summary>
    public static string FormatIndexed(IEnumerable<(int Index, UserWord Word)> entries) =>
        string.Join('\n', entries.Select(e => $"{e.Index}\t{e.Word.Reading}\t{e.Word.Word}"));

    /// <summary>「位置 Tab 読み Tab 単語」の行を読む (位置が数でない行は飛ばす)。</summary>
    public static List<(int Index, UserWord Word)> ParseIndexed(string? text)
    {
        var entries = new List<(int, UserWord)>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length >= 3 && int.TryParse(fields[0], out var index) && index >= 0) entries.Add((index, new UserWord(fields[1], fields[2])));
        }
        return entries;
    }

    /// <summary>専門用語集の 1 分野の語: 「読み Tab 語 Tab 注記 Tab 除外していれば 1」を改行でつなぐ。注記の Tab・改行は空白にする。</summary>
    public static string FormatTermWords(IEnumerable<(TermEntry Entry, bool Excluded)> words) =>
        string.Join('\n', words.Select(w => $"{w.Entry.Reading}\t{w.Entry.Word}\t{Clean(w.Entry.Note)}\t{(w.Excluded ? 1 : 0)}"));

    /// <summary>自作の専門用語集の語: 「読み Tab 語 Tab 注記」を改行でつなぐ (注記の Tab・改行は空白にする)。</summary>
    public static string FormatTermEntries(IEnumerable<TermEntry> entries) =>
        string.Join('\n', entries.Select(e => $"{e.Reading}\t{e.Word}\t{Clean(e.Note)}"));

    /// <summary>「読み Tab 語 (Tab 注記)」の行を読む (欄が足りない行は飛ばす)。</summary>
    public static List<(string Reading, string Word, string Note)> ParseTermEntries(string? text)
    {
        var items = new List<(string, string, string)>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length >= 2 && fields[0].Length > 0) items.Add((fields[0], fields[1], fields.Length >= 3 ? fields[2] : ""));
        }
        return items;
    }

    /// <summary>「位置 Tab 読み Tab 語 Tab 注記」を改行でつなぐ (自作の専門用語集で消した語と元の位置)。</summary>
    public static string FormatIndexedTerms(IEnumerable<(int Index, TermEntry Entry)> entries) =>
        string.Join('\n', entries.Select(e => $"{e.Index}\t{e.Entry.Reading}\t{e.Entry.Word}\t{Clean(e.Entry.Note)}"));

    /// <summary>「位置 Tab 読み Tab 語 (Tab 注記)」の行を読む (位置が数でない行は飛ばす)。</summary>
    public static List<(int Index, TermEntry Entry)> ParseIndexedTerms(string? text)
    {
        var entries = new List<(int, TermEntry)>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length >= 3 && int.TryParse(fields[0], out var index) && index >= 0)
                entries.Add((index, new TermEntry(fields[1], fields[2], fields.Length >= 4 ? fields[3] : "")));
        }
        return entries;
    }

    /// <summary>入れられなかった語: 「読み Tab 語 Tab 理由」を改行でつなぐ。</summary>
    public static string FormatSkipped(IEnumerable<(string Reading, string Word, string Reason)> items) =>
        string.Join('\n', items.Select(i => $"{i.Reading}\t{i.Word}\t{Clean(i.Reason)}"));

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // ---- 入力の補助 ----

    private static readonly Lazy<Detection.RomajiDetector> s_romaji = new(() => new Detection.RomajiDetector());

    /// <summary>
    /// 読みの入力を、登録する読みの候補にする: 英字があればローマ字としてひらがなにし (kigoutou → きごうとう)、カタカナはひらがなにする。
    /// 画面は「ひらがなにする」ボタンで、押したときだけ入れ替える (黙って変えない)。
    /// </summary>
    public static string ToReading(string text)
    {
        var trimmed = text.Trim();
        var kana = trimmed.Any(char.IsAsciiLetter) ? s_romaji.Value.ConvertLenient(trimmed.ToLowerInvariant(), final: true) : trimmed;
        return new string(kana.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
    }

    // ---- 判定を調べる (「判定を調べる」タブ) ----

    /// <summary>
    /// 画面から登録された外部のスペルチェッカー (Mac の NSSpellChecker)。IME は meltype_init で登録した同じ関数で英単語を調べるので、
    /// 答えを IME と揃えるには画面のプロセスでも登録する。無ければ同梱の英単語表 (BuiltInWordChecker) に落ちる (K13 と同じ差が出うる)。
    /// </summary>
    public static Detection.IWordChecker? ExternalWordChecker { get; set; }

    /// <summary>文脈として読む前後の文字数の上限 (IME が Core に渡す長さ = InputController.surroundingText と同じ)。</summary>
    internal const int ContextLength = 20;

    private static readonly object s_explainGate = new();

    // 判定器は辞書の読み込みが重いので 1 回だけ作って使い回す。設定は IME の MeltypeSession.CreateDefault と同じ:
    // 学習した語 (共有の Languages) と IsCommonJapanese (CompositionController の生成時に付くもの) を付けないと、答えが IME とずれる。
    private static readonly Lazy<CompositionDetector> s_explainDetector = new(() =>
    {
        var detector = CompositionDetector.CreateDefault(AppPaths.UserDictionaryDirectory);
        detector.IsCommonJapanese ??= RomajiTypoCorrector.Load(detector.Romaji).IsCommonJapanese;
        return detector;
    });

    /// <summary>
    /// 打ったローマ字 (typed) が、前の文字 (before)・後ろの文字 (after)・判定の強さ (level) の下で
    /// どう英語 / 日本語に分かれるか (確定する前の区間分け) と、条件を 1 つだけ変えたときに結果が変わるかを返す。
    /// Mac の判定は理由を持たない (bool を返すだけ) ので、理由を作らず、同じ入力を条件を変えて判定し直して比べる (切り分け)。
    /// 形は 1 行 1 件・欄は Tab 区切り:
    ///   segments \t 区間 (EN:語|JA:かな)
    ///   variant \t 条件の名前 \t その条件での区間 \t 変わったなら 1 (変わらなければ 0)
    ///   memory-pending \t 語 (学習したが 2 回目を待っていて、まだ効いていない語)
    /// 条件は memory-off・memory-without:語・before-english/japanese/none・after-english/none・level:強さ。1 つずつ変え、組み合わせない。
    /// languages.json は読むだけで変えない (学習した語を外すのはメモリ上の写しで試す)。
    /// ローマ字入力としてだけ調べる (かな入力は IME が別の組み立て方 = AppendKana をするので、同じ答えにならない。常に KanaInput = false)。
    /// 打った文字に空白は入れない (Space は変換のキーなので、区間分けに現れない。画面が先に断る)。
    /// 外部のスペルチェッカー (Mac の NSSpellChecker) を呼びうるので、メインスレッドから呼ぶこと (呼び元の Swift が守る)。
    /// </summary>
    public static string ExplainDetection(string typed, string before, string after, DetectionLevel level) =>
        ExplainDetection(typed, before, after, level, s_explainDetector.Value, Languages, ExternalWordChecker ?? Detection.BuiltInWordChecker.Shared);

    /// <summary>判定器・学習・スペルチェッカーを渡す版 (テストが一時フォルダーの学習で確かめる。渡した判定器の Memory・SpellChecker は、終わったら元に戻す)。</summary>
    internal static string ExplainDetection(string typed, string before, string after, DetectionLevel level,
        CompositionDetector detector, LanguageMemory memory, Detection.IWordChecker spellChecker)
    {
        // 打つ文字は長くても数十文字。際限なく長いと判定し直しの回数が膨らむので、画面の入力欄の上限を超えたぶんは読まない。
        // Tab・改行は戻りの形 (TSV) を壊すので、打った文字には含めない
        typed = string.Concat((typed ?? "").Where(c => !char.IsControl(c))).Trim();
        if (typed.Length > 200) typed = typed[..(char.IsHighSurrogate(typed[199]) ? 199 : 200)];
        before = Tail(before ?? "", ContextLength);
        after = Head(after ?? "", ContextLength);
        var lines = new List<string>();
        // Space は変換のキーなので、空白の入った入力は IME の区間分けにならない (画面が先に断るが、本体でも判定しない)
        if (typed.Length == 0 || typed.Any(char.IsWhiteSpace)) return "segments\t";

        lock (s_explainGate)
        {
            var savedMemory = detector.Memory;
            var savedChecker = detector.SpellChecker;
            try
            {
                detector.SpellChecker = spellChecker;
                detector.Memory = memory;
                var baseline = Segmented(detector, typed, before, after, level);
                lines.Add("segments\t" + baseline);

                void Variant(string name, string segments) =>
                    lines.Add($"variant\t{name}\t{segments}\t{(segments == baseline ? 0 : 1)}");

                // 学習した語: 全部外す / 区間に含まれる語を 1 つずつ外す
                detector.Memory = null;
                Variant("memory-off", Segmented(detector, typed, before, after, level));
                var raws = SegmentRaws(detector, typed, before, after, level, memory);
                var learned = memory.Entries();
                // 打った文字全体 (小文字) に含まれる学習した語をすべて試す (区間の境目をまたぐ語も拾う。長い語が先。多いときの折りたたみは画面側)
                var lowerTyped = typed.ToLowerInvariant();
                foreach (var word in learned.Where(e => e.Active && lowerTyped.Contains(e.Word, StringComparison.Ordinal))
                             .Select(e => e.Word).OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal))
                {
                    detector.Memory = memory.CopyWithout(w => w == word);
                    Variant("memory-without:" + word, Segmented(detector, typed, before, after, level));
                }
                detector.Memory = memory;

                // 前後の文字: 今と同じ分類の条件は試さない (変わりようがない)
                var beforeNow = CompositionController.LanguageOf(before);
                if (beforeNow != true) Variant("before-english", Segmented(detector, typed, "meeting", after, level));
                if (beforeNow != false) Variant("before-japanese", Segmented(detector, typed, "今日は", after, level));
                if (beforeNow != null) Variant("before-none", Segmented(detector, typed, "", after, level));
                var afterNow = CompositionController.LanguageOfStart(after);
                if (afterNow != true) Variant("after-english", Segmented(detector, typed, before, "meeting", level));
                if (afterNow != null) Variant("after-none", Segmented(detector, typed, before, "", level));

                foreach (var other in Enum.GetValues<DetectionLevel>().Where(l => l != level))
                {
                    Variant("level:" + other, Segmented(detector, typed, before, after, other));
                }

                foreach (var entry in learned.Where(e => !e.Active && raws.Any(r => r.Contains(e.Word, StringComparison.Ordinal))).Take(8))
                {
                    lines.Add("memory-pending\t" + entry.Word);
                }
            }
            finally
            {
                detector.Memory = savedMemory;
                detector.SpellChecker = savedChecker;
            }
        }
        return string.Join('\n', lines);
    }

    /// <summary>区間の文字列 (英語は打った英字、日本語はかな)。--segments と同じく CompositionText に 1 文字ずつ入れて、確定する前 (Enter の前) の区間を読む。</summary>
    private static string Segmented(CompositionDetector detector, string typed, string before, string after, DetectionLevel level) =>
        string.Join('|', SegmentsOf(detector, typed, before, after, level).Select(s => s.IsEnglish ? "EN:" + s.Raw : "JA:" + s.Kana));

    private static List<string> SegmentRaws(CompositionDetector detector, string typed, string before, string after, DetectionLevel level, LanguageMemory memory)
    {
        detector.Memory = memory;
        return SegmentsOf(detector, typed, before, after, level).Select(s => s.Raw.ToLowerInvariant()).ToList();
    }

    private static IReadOnlyList<CompositionSegment> SegmentsOf(CompositionDetector detector, string typed, string before, string after, DetectionLevel level)
    {
        // 前後の文脈は BeginComposition と同じ作り (LanguageOf で分類。日本語か分からなければ null のまま)。
        // 前の文字が分類できないときの null は、IME が「直前の確定の向き」を残すのと違って本当に「無し」(この画面には直前の確定が無い。画面の案内に書いてある)。
        var text = new CompositionText(detector)
        {
            Level = () => level,
            KanaInput = false,
            PrecedingEnglish = CompositionController.LanguageOf(before),
            PrecedingEnglishSentence = CompositionController.IsEnglishSentence(before),
            PrecedingTail = before,
            FollowingEnglish = CompositionController.LanguageOfStart(after),
        };
        foreach (var c in typed) text.Append(c);
        return text.Segments(final: true);
    }

    /// <summary>末尾の n 文字 (UTF-16)。サロゲートペアの途中では切らない。</summary>
    private static string Tail(string text, int n)
    {
        if (text.Length <= n) return text;
        var start = text.Length - n;
        if (char.IsLowSurrogate(text[start])) start++;
        return text[start..];
    }

    /// <summary>先頭の n 文字 (UTF-16)。サロゲートペアの途中では切らない。</summary>
    private static string Head(string text, int n)
    {
        if (text.Length <= n) return text;
        return text[..(char.IsHighSurrogate(text[n - 1]) ? n - 1 : n)];
    }

    // ---- 専門用語集の「編集」 ----

    /// <summary>
    /// 専門用語集の語を「直す」: 同梱のファイルは変えられないので、直した語 (reading, word) をユーザー辞書に登録し、元の語 (original) を除外する。
    /// ユーザー辞書は専門用語集より優先されるので、直した語が先に出る。直した語がすでにユーザー辞書にあれば、登録はせず除外だけ行う (added = false)。
    /// 読みも語も変えていなければ、ユーザー辞書への複製だけ (除外しない)。だめなら理由を返す。
    /// 途中でだめになっても中途半端に残さない: 直した語を登録できなければ何も変えず、元の語を除外できなければ、新しく登録した語を消して戻す
    /// (戻すことにも失敗したときだけ、added = true のまま理由を返す。画面はそれを取り消せるように積む)。
    /// </summary>
    public static string? EditTerm(UserDictionary dictionary, UserWord original, string reading, string word, out bool added)
    {
        added = false;
        reading = UserDictionary.NormalizeReading(reading);
        word = word.Trim();
        if (UserDictionary.Validate(reading, word) is { } invalid) return invalid;
        switch (dictionary.AddNew(reading, word))
        {
            case null:
                added = true;
                break;
            case UserDictionary.DuplicateMessage:
                break;
            case var error:
                return error;
        }
        var unchanged = TermDictionary.Normalize(reading) == TermDictionary.Normalize(original.Reading.Trim()) && word == original.Word.Trim();
        if (unchanged) return null;
        if (TermDomains.SetExcluded([(original.Reading, original.Word)], excluded: true) is not { } excludeError) return null;
        if (!added) return $"元の語を除外できませんでした: {excludeError}";
        if (dictionary.RemoveRange([new UserWord(reading, word)], out _) is { } rollbackError)
        {
            return $"直した語はユーザー辞書に登録しましたが、元の語を除外できませんでした: {excludeError} (登録の取り消しにも失敗: {rollbackError})";
        }
        added = false;
        return $"元の語を除外できなかったので、何も変えませんでした: {excludeError}";
    }

    // ---- 取り込み・書き出し (UserDictionaryFile の形式) ----

    /// <summary>
    /// ほかの日本語入力の辞書 (Microsoft IME・Google 日本語入力・Meltype の userdict.txt) を取り込む。
    /// summary は「登録した数 Tab 登録済み・登録できなかった数 Tab 飛ばした行の数 Tab 文字コード」。
    /// added は新しく登録した語 (画面が「取り込みの取り消し」で消す。取り込みの間に IME が登録した語は入らない)。だめなら理由を返す。
    /// </summary>
    public static string? Import(UserDictionary dictionary, string path, out string summary) => Import(dictionary, path, out summary, out _);

    public static string? Import(UserDictionary dictionary, string path, out string summary, out IReadOnlyList<UserWord> added)
    {
        summary = "";
        added = [];
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "ファイルが見つかりません。";
            if (info.Length > MaxImportBytes) return $"ファイルが大きすぎます ({MaxImportBytes / 1024 / 1024} MB まで)。";
            var result = UserDictionaryFile.Parse(File.ReadAllBytes(path));
            if (dictionary.AddMany(result.Words, out added) is { } error) return error;
            summary = $"{added.Count}\t{result.Words.Count - added.Count}\t{result.Skipped}\t{result.Encoding}";
            return null;
        }
        catch (Exception ex)
        {
            return $"取り込めませんでした: {ex.Message}";
        }
    }

    /// <summary>
    /// 書き出し先として使ってよいか。だめなら理由。次を断る:
    /// - シンボリックリンクそのもの (リンク先へ書かれる)
    /// - 自作の専門用語集のフォルダー (terms/) の中 (使っている分野のファイルを上書き・増やしかねない)
    /// - Meltype のデータフォルダーの中の、すでにあるファイル (userdict.txt・config.json などの動いているデータを上書きしかねない)
    /// フォルダーのシンボリックリンクはたどってから比べる (データフォルダーへのリンクを経由して抜けられないように)。
    /// </summary>
    public static string? CheckExportTarget(string path) => CheckExportTarget(path, AppPaths.DataDirectory);

    /// <summary>dataDirectory を指定して判定する (テストが一時フォルダーで確かめるため)。</summary>
    internal static string? CheckExportTarget(string path, string dataDirectory)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var file = new FileInfo(full);
            if (file.Exists && file.LinkTarget is not null) return "書き出し先がシンボリックリンクのため、書き出せません。別の名前を選んでください。";
            var folder = ResolveDirectory(Path.GetDirectoryName(full)!).TrimEnd(Path.DirectorySeparatorChar);
            static bool Inside(string folder, string root)
            {
                var resolved = ResolveDirectory(Path.GetFullPath(root)).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(folder, resolved, StringComparison.OrdinalIgnoreCase)
                    || folder.StartsWith(resolved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            if (Inside(folder, TermDomains.UserDirectory())) return "自作の専門用語集のフォルダーの中には書き出せません。別の場所を選んでください。";
            if (file.Exists && Inside(folder, dataDirectory))
            {
                return "Meltype のデータフォルダーの中にあるファイルは上書きできません (使っている辞書や設定を壊してしまうため)。別の場所か名前を選んでください。";
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"書き出せませんでした: {ex.Message}";
        }
    }

    /// <summary>フォルダーのパスを、途中のシンボリックリンクをたどった実際のパスにする (無いフォルダーはそのまま)。</summary>
    private static string ResolveDirectory(string full, int depth = 0)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            // リンク先のパスにもリンクが含まれうる (/var → /private/var など) ので、たどった先をもう一度解決する (循環に備えて深さを限る)
            if (info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = depth < 8 ? ResolveDirectory(target.FullName, depth + 1) : target.FullName;
            }
        }
        return current;
    }

    /// <summary>Microsoft IME の形式 (UTF-16) で書き出す。書き出した語の数を count に返す。だめなら理由を返す。</summary>
    public static string? Export(UserDictionary dictionary, string path, out int count)
    {
        count = 0;
        if (CheckExportTarget(path) is { } invalid) return invalid;
        try
        {
            dictionary.Refresh();
            var words = dictionary.Words;
            // 打った語に近い内容なので、ほかのデータと同じく本人だけが読める権限で、置き換えで書く。
            SafeFile.WriteAllBytes(path, UserDictionaryFile.Export(words));
            count = words.Count;
            return null;
        }
        catch (Exception ex)
        {
            return $"書き出せませんでした: {ex.Message}";
        }
    }
}
