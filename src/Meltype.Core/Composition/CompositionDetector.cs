// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスの中身のうち、どこを英字のまま見せるかを決める。
///
/// 日本語入力中に google と打って「ごおｇぇ」になるのを防ぐのが目的。英語かどうかは区間ごとに判定するので、
/// 「きょうは google」なら google の部分だけが英字になる。区間の区切りはかな 1 音の境目だけ。
/// 未確定のうちは何度でも表示を作り直せるので、ここでの判定は IME 自動切替より積極的でよいが、
/// 既定は日本語で、英語と判断できる根拠があるときだけ英字にする。
/// </summary>
public sealed class CompositionDetector
{
    private readonly RomajiDetector _romaji;
    private readonly DictionaryDetector _japanese;
    private readonly EnglishDetector _english;
    private readonly TypoDetector _typo;
    private readonly ProperNouns _proper;
    private readonly KanaDetector? _kana;

    /// <summary>ローマ字としても読める、よく使う拡張子 (Meltype.ace、setup.ini)。読めない拡張子 (dll、json) は一覧に無くても英字のまま。</summary>
    private static readonly HashSet<string> FileExtensions =
    [
        "a", "ace", "ada", "ado", "ann", "ape", "ase", "dae", "di", "do", "fifo", "fon", "geo", "go", "ifo", "ii", "ini", "ino", "ipa", "iso", "jade", "ko",
        "ma", "metadata", "midi", "mime", "mo", "mobi", "o", "oga", "ora", "oso", "otio", "po", "pyo", "ra", "re", "rego", "rei", "resi", "ru", "sami",
        "so", "suo", "tese", "ufo", "vue", "gcno", "gcda", "tga",
        // 末尾が助詞・日本語の打ち間違いに見える拡張子 (l|ha、acc|de、benco|de、sarif、shacl)
        "lha", "accde", "bencode", "sarif", "shacl", "afphoto", "ssa", "tta", "mka", "rda", "usda",
    ];

    /// <summary>
    /// 助詞の前で名前を分けるときに、拡張子・ドメインとみなすよく使う名前 (読めない拡張子の一覧)。ここに無い読めない短い語 (gc・t) は、助詞だけが続いても分けない。
    /// (Mac 版で追加: 本家の PR #267 は名前全体を英字にしてしまい、readme.md|を、amazon.co.jp|で のような助詞が日本語にならなかった)
    /// </summary>
    private static readonly HashSet<string> SplitStems =
    [
        "txt", "pdf", "json", "md", "html", "htm", "css", "js", "ts", "tsx", "jsx", "py", "rs", "rb", "php", "java", "kt", "swift", "cpp", "hpp", "cs", "sh", "bat", "ps1",
        "yml", "yaml", "toml", "xml", "csv", "tsv", "log", "sql", "lua", "dart", "scala", "ex", "exs", "zig", "zip", "gz", "tar", "tgz", "7z", "rar", "mp3", "mp4", "mov", "avi", "mkv", "wav", "flac", "aac", "m4a",
        "png", "jpg", "jpeg", "gif", "svg", "webp", "psd", "prproj", "aep", "srt", "ass", "vtt", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "rtf", "exe", "dll", "msi",
        "dmg", "pkg", "apk", "img", "bak", "tmp", "cfg", "conf", "lock", "env", "tv", "jp", "com", "net", "org", "io", "dev", "app",
        "c", "h", "m", "r", "ai",
    ];

    private static readonly HashSet<string> DomainSuffixes = ["ai", "app", "au", "biz", "ca", "cn", "co", "com", "de", "dev", "edu", "eu", "fr", "gg", "gov", "info", "in", "io", "jp", "kr", "me", "net", "org", "uk", "us", "xyz"];

    public CompositionDetector(RomajiDetector romaji, DictionaryDetector japanese, EnglishDetector english, TypoDetector typo, ProperNouns? proper = null,
        KanaDetector? kana = null)
    {
        _kana = kana;
        _romaji = romaji;
        _japanese = japanese;
        _english = english;
        _typo = typo;
        _proper = proper ?? new ProperNouns();
    }

    public static CompositionDetector CreateDefault(string? userDictionaryDirectory = null)
    {
        var romaji = new RomajiDetector();
        var japaneseWords = DictionarySource.Load("japanese.txt", userDictionaryDirectory).ToList();
        var japanese = new DictionaryDetector(japaneseWords, romaji);
        var proper = ProperNouns.Load(userDictionaryDirectory);
        var english = new EnglishDetector(DictionarySource.Load("english.txt", userDictionaryDirectory).Concat(proper.LowercaseWords));
        var detector = new CompositionDetector(romaji, japanese, english, new TypoDetector(japanese.Words), proper, new KanaDetector(japaneseWords, romaji));
        // 設定「日本語入力中は一覧の語をかなで変換する」(本家 #1023) の語。ユーザー辞書の japanese-mode.txt も足せる。
        foreach (var word in DictionarySource.Load("japanese-mode.txt", userDictionaryDirectory)) detector.KanaInJapaneseModeWords.Add(word);
        return detector;
    }

    /// <summary>
    /// 語 (lower) に後ろの 1 音 (最初の母音まで) を足した読みが、よく使う日本語の語 (readings.txt)・ブランド名 (brands.txt) の頭になるか
    /// (main + do = まいんど = マインド、main + ku = まいんく = マインクラフト)。english-readable.txt の語でも、そう続くときは英語にしない (Q11)。
    /// </summary>
    private bool ContinuesJapaneseWord(string lower, string? next)
    {
        if (string.IsNullOrEmpty(next)) return false;
        var mora = new StringBuilder();
        var vowel = false;
        foreach (var c in next)
        {
            if (!char.IsAsciiLetter(c)) break;
            mora.Append(char.ToLowerInvariant(c));
            if (char.ToLowerInvariant(c) is 'a' or 'i' or 'u' or 'e' or 'o') { vowel = true; break; }
        }
        return vowel && StartsJapaneseReading(lower + mora);
    }

    /// <summary>
    /// english-readable.txt の語のうち、日本語のすぐ後ろでは英語にしない語。読みの頭 (まい) が、日本語の語の終わり (うまい・あまい・せまい) と
    /// つながってしまう (umaindesu = うまいんです、kumainda = くまいんだ)。英語にするのは、入力の先頭 (mainni)・英語の後ろ (gitmainni・origin/main) だけ。
    /// 同じ入力の中の日本語や、かな側の記号 (`main`・(main)・。) の後ろでは今までどおりかな。入力の先頭は、前に確定した文 (前の行) が日本語でも英語にする
    /// (前の行が日本語の TextEdit で mainnima-ji が まいんに → mine に になっていた。Q11 の実機確認)。
    /// </summary>
    private static readonly HashSet<string> NotAfterJapanese = new(StringComparer.Ordinal) { "main", "domain", "data" };

    /// <summary>ローマ字 letters の読みで始まる、よく使う日本語の語・ブランド名があるか。</summary>
    private bool StartsJapaneseReading(string letters)
    {
        var head = ReadingOf(letters);
        if (head is null) return false;
        var readings = JapaneseReadings.Value;
        var index = Array.BinarySearch(readings, head, StringComparer.Ordinal);
        if (index < 0) index = ~index;
        return index < readings.Length && readings[index].StartsWith(head, StringComparison.Ordinal);
    }

    /// <summary>ローマ字の読み (最後の n は ん)。最後まで読めなければ null。</summary>
    private string? ReadingOf(string letters)
    {
        var analysis = _romaji.AnalyzeFragment(letters);
        if (!analysis.IsValid || analysis.Partial is { Length: > 0 } and not "n") return null;
        return analysis.Kana + (analysis.Partial == "n" ? "ん" : "");
    }

    /// <summary>よく使う日本語の語の読み (readings.txt) とブランド名の読み (brands.txt) を、並べたもの。</summary>
    private static readonly Lazy<string[]> JapaneseReadings = new(() =>
    {
        var words = new SortedSet<string>(StringComparer.Ordinal);
        // まいんち (毎日のくだけた形) は readings.txt に無いので足す
        words.Add("まいんち");
        foreach (var file in new[] { "readings.txt", "brands.txt" })
        {
            foreach (var rawLine in DictionarySource.ReadEmbedded(file).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var reading = line.Split([' ', '\t'], 2)[0];
                if (reading.Length > 0 && reading[0] is >= 'ぁ' and <= 'ゖ') words.Add(reading);
            }
        }
        return [.. words];
    });

    /// <summary>english-readable.txt の語か (ローマ字として最後まで読めても英語にする語)。</summary>
    public bool IsReadableEnglishWord(string lower) => ReadableEnglish.Value.ContainsWord(lower);

    private static readonly Lazy<WordList> ReadableEnglish = new(() =>
    {
        var list = new WordList();
        foreach (var word in DictionarySource.Load("english-readable.txt", null)) list.Add(word);
        return list;
    });

    /// <summary>
    /// english.txt の「日本語由来で英語としても使われる語」(sushi, anime, tofu …)。本家 Pull Request #317 と同じ読み方。
    /// </summary>
    private static readonly Lazy<HashSet<string>> JapaneseOriginWords = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in DictionarySource.ReadEmbedded("english.txt").Split('\n'))
        {
            if (line.StartsWith("# ---", StringComparison.Ordinal))
            {
                inSection = line.Contains("日本語由来");
                continue;
            }
            if (!inSection || line.StartsWith('#')) continue;
            // 日本語由来の語の後ろに、同じ段落で続けて技術用語などを並べてあるので、空行までを読む
            if (line.Trim().Length == 0)
            {
                inSection = false;
                continue;
            }
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) words.Add(word);
        }
        return words;
    });

    /// <summary>日本語由来で英語としても使われる語か (sushi, ramen)。英文の中に出てきても、日本語のつもりのことがある。</summary>
    public static bool IsJapaneseOriginWord(string lower) => JapaneseOriginWords.Value.Contains(lower);

    public RomajiDetector Romaji => _romaji;

    /// <summary>普通の英単語の判定に使う Windows のスペルチェッカー。null なら同梱の辞書だけ。</summary>
    public IWordChecker? SpellChecker { get; set; }

    /// <summary>ユーザーが英字 / かなに直して覚えた語 (自動の判定より優先する)。</summary>
    public LanguageMemory? Memory { get; set; }

    /// <summary>英字の並びが、ローマ字としてよく使う日本語になるか (kyouha = 今日は、tomato = とまと)。無ければ使わない。</summary>
    public Func<string, bool>? IsCommonJapanese { get; set; }

    /// <summary>
    /// 日本語入力中に、一覧の語 (<see cref="KanaInJapaneseModeWords"/>) を英語ではなく かな として変換するか (本家 #1023)。
    /// 既定は false (今までどおり)。英語の文の中 (前が英単語) とファイルの拡張子は、ON でも英字のまま。
    /// 英数 (直接入力) では変換ボックスを通らないので、この設定に関係なく英字で入る。
    /// </summary>
    public Func<bool> KanaInJapaneseMode { get; set; } = () => false;

    /// <summary>
    /// 英単語のすぐ後ろに続けて打った大文字の英字 (Koki Ogawa の Ogawa) を、日本語の文の中でも英語にするか (本家 #1039)。
    /// 既定は false (今までどおり、O|gawa のように 1 文字の大文字 + 助詞で始まる語に分かれる)。
    /// </summary>
    public Func<bool> KeepEnglishNameAfterEnglish { get; set; } = () => false;

    /// <summary>日本語入力中に かな として変換する語 (dictionaries/japanese-mode.txt)。CreateDefault が読み込む。</summary>
    public WordList KanaInJapaneseModeWords { get; } = new();

    public ProperNouns ProperNouns => _proper;

    /// <summary>
    /// 単位列 (+ 入力途中の子音) を英語区間と日本語区間に分ける。
    /// 先頭から見て、ある単位から始まる最長の「英語と言える」区間があればそこを英語にする。
    /// </summary>
    /// <param name="precedingEnglish">入力欄のキャレットの前の確定済みの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="followingEnglish">キャレットの後ろの文字が英語なら true、日本語なら false、分からなければ null。</param>
    /// <param name="level">判定の強さ。手動 (Manual) では Shift で打った大文字始まりの語だけを英語にする。</param>
    /// <param name="englishSentence">キャレットの前が英文 (空白で区切った英単語が 2 語以上続いて空白で終わる: "I want ")。
    /// 日本語の文の中の英単語 (GitHub の) より強い英語の根拠として扱う。</param>
    /// <param name="startsWord">先頭の単位が語の頭か (キャレットの前が無い・分からない・空白・開きの記号)。保護区間 (/tmp/foo・--no-install) は語の頭からだけ守る。
    /// 日本語を確定した直後の /fuka は語の頭ではない。<see cref="IsWordStartAfter"/> で決める。</param>
    /// <param name="precedingOpener">キャレットの前の文字が開きの記号ならその記号 (<see cref="OpenerBefore"/>)。保護区間の範囲を、対応する閉じの記号までにする。</param>
    /// <param name="afterNewline">キャレットの前が行頭か (最後の改行の後ろが空白・タブ・全角空白だけ。<see cref="IsNewlineAfter"/>)。前の行の英語は、オプションの「直前が英語」にしない。</param>
    public IReadOnlyList<CompositionSegment> Segment(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish = null, bool? followingEnglish = null,
        DetectionLevel level = DetectionLevel.Balanced, bool englishSentence = false, bool kanaInput = false, bool final = false, bool startsWord = true, string? precedingOpener = null, bool afterNewline = false)
    {
        // 先頭の z + h/j/k/l の矢印 (zh = ←) は、英語かどうかを判定せず日本語の区間にする。
        // (macOS のスペルチェッカーは zh を英単語と答えるので、判定に回すと矢印が元の綴りに戻る。先頭の zhang・zlib は CompositionText が矢印を取り消して英字に戻す)
        var arrows = 0;
        while (!kanaInput && arrows < units.Count && CompositionText.IsArrowUnit(units[arrows])) arrows++;
        if (arrows > 0)
        {
            var arrowSegment = Japanese(units, 0, arrows, "");
            if (arrows == units.Count && pending.Length == 0) return [arrowSegment];
            // 矢印のあとは語の頭ではない (直前の単位が空白ではない)
            return [arrowSegment, .. Segment([.. units.Skip(arrows)], pending, precedingEnglish, followingEnglish, level, englishSentence, kanaInput, final, startsWord: false)];
        }
        // 記号を混ぜて書く名前・略語 (B.o.B、U.S.A.、Chri$tian、Ke$ha) は、入力全体がその形のときだけ英字のまま 1 語にする (本家 #303・PR #315)。
        // 英単語に見えないので、FindSpans (日本語の打ちかけとして B.お。B などに区切る) や P29 の DotSuffixEnd には回さない。
        if (!kanaInput && level != DetectionLevel.Manual && Raw(units, 0, units.Count) + pending is var stylized && IsStylizedName(stylized))
        {
            return [new CompositionSegment(true, "", stylized)];
        }
        var segments = FindSpans(units, pending, precedingEnglish, followingEnglish, level, englishSentence && precedingEnglish == true, kanaInput, final, startsWord: startsWord, precedingOpener: precedingOpener, afterNewline: afterNewline);
        if (kanaInput) return segments;
        // 英語の後ろの , . は半角にしたが、すぐ後ろに日本語が続いた (Ok,konnna → Ok、こんな) なら、日本語の文の句読点にして分け直す。
        if (PunctuationBeforeJapanese(units, segments) is { } japanesePunctuation)
        {
            segments = FindSpans(units, pending, precedingEnglish, followingEnglish, level, englishSentence && precedingEnglish == true, kanaInput, final, japanesePunctuation, startsWord, precedingOpener, afterNewline);
        }
        // 辞書にない英単語 (stackoverflow など) を最初から打っているなら全体を英語にする (カスタム表でしか読めない綴りは除く)。
        // 途中の区間 (… flow) だけを英語にすると「sたcこvえrflow」のようになってしまう。
        // ただし先頭が辞書の英単語として区切れている (github に push) ならその区切りを使う。
        var whole = Raw(units, 0, units.Count) + pending;
        if (UnknownWordThenJapanese(units, pending, segments, level, whole) is { } split) return split;
        if (level != DetectionLevel.Manual && !segments[0].IsEnglish && Memory?.Get(whole.ToLowerInvariant()) != false && IsUnknownEnglishWord(whole) && !IsTableOnlySpelling(whole))
        {
            return [new CompositionSegment(true, "", whole)];
        }
        return segments;
    }

    /// <summary>
    /// 記号を混ぜて書く名前・略語か (本家 #303・PR #315 の IsStylizedName を、Mac 版の Segment に合わせて狭めた)。次の 2 つだけ。
    /// ・英字 1 文字ずつを . でつないだ、大文字を含む略語 (B.o.B、U.S.A.、T.K.O.)。後ろに日本語が続く形 (U.S.A.no) は含まない。
    /// ・大文字で始まり、$ の両側に英字がある名前 (Chri$tian、Ke$ha、A$AP)。語末の $ (Kyou$) と先頭の $ ($100)・数字つき (1$) は含まない。
    /// どちらも入力全体 (変換ボックスの先頭から最後まで。空白は含まない) がその形のときだけ。数字・単位・ドメイン (10.5、No.5、example.com) は対象外。
    /// </summary>
    private static bool IsStylizedName(string token) =>
        token.Length >= 3 && token.IndexOfAny(['.', '$']) > 0 && // 1 キーごとに呼ばれるので、記号が無い入力はここで除く
        (token.Any(char.IsAsciiLetterUpper) && DottedLetters.IsMatch(token) ||
         char.IsAsciiLetterUpper(token[0]) && DollarName.IsMatch(token));

    private static readonly System.Text.RegularExpressions.Regex DottedLetters = new(@"^(?:[A-Za-z]\.)+[A-Za-z]\.?\z");
    private static readonly System.Text.RegularExpressions.Regex DollarName = new(@"^[A-Za-z]+(?:\$[A-Za-z]+)+\z");

    private List<CompositionSegment>? UnknownWordThenJapanese(IReadOnlyList<CompositionUnit> units, string pending, List<CompositionSegment> segments, DetectionLevel level, string whole)
    {
        // 知らない英字の語 + 助詞で始まる日本語 (grokga、grokniyoruto) を最初から打っているなら、語は英字・後ろは日本語 (grokが)。
        // 語は、ローマ字の打ちかけとしても読めない (gr) もの。後ろは最後までローマ字として読めるもの。
        if (level != DetectionLevel.Manual && !segments[0].IsEnglish && whole.All(char.IsAsciiLetter) && !IsKnownEnglishWord(whole))
        {
            for (var k = 1; k < units.Count; k++)
            {
                var stem = Raw(units, 0, k).ToLowerInvariant();
                if (stem.Length < 3 || _romaji.AnalyzeFragment(stem).IsValid || IsKnownEnglishWord(stem) && stem.Length >= 5) continue;
                // 語の頭から読めない (gr): 日本語の後ろの英単語 (kyouha|google) ではない。
                // 読めない 1 文字の後ろが最後まで読める (s|dake) なら、英字 1 文字 + 日本語。
                var readable = _romaji.Analyze(stem).Tokens.Sum(t => t.Romaji.Length);
                if (readable >= 2 || _romaji.Analyze(stem[(readable + 1)..]) is { IsValid: true, Partial: "" }) continue;
                var rest = (Raw(units, k, units.Count) + pending).ToLowerInvariant();
                if (!TrailingParticles.Any(p => rest.StartsWith(p, StringComparison.Ordinal)) ||
                    _romaji.Analyze(rest) is not { IsValid: true, Partial: "" or "n" }) continue;
                return [new CompositionSegment(true, "", Raw(units, 0, k)), Japanese(units, k, units.Count, pending)];
            }
        }
        return null;
    }

    /// <summary>
    /// 英語の区間にした , . だけの区間のうち、すぐ後ろが英字を打った日本語の区間 (こんな) のものの単位の位置。無ければ null。
    /// 後ろが空白・記号だけ (Ok, thanks の空白) や英語なら英文の句読点のまま。
    /// </summary>
    private static HashSet<int>? PunctuationBeforeJapanese(IReadOnlyList<CompositionUnit> units, List<CompositionSegment> segments)
    {
        HashSet<int>? result = null;
        var unit = 0;
        for (var s = 0; s < segments.Count; s++)
        {
            var first = unit;
            for (var remaining = segments[s].Raw.Length; unit < units.Count && remaining > 0; unit++) remaining -= units[unit].Raw.Length;
            if (!segments[s].IsEnglish || segments[s].Raw is not { Length: > 0 } raw || !raw.All(c => c is ',' or '.')) continue;
            if (s + 1 < segments.Count && !segments[s + 1].IsEnglish && segments[s + 1].Raw is [var next, ..] && char.IsAsciiLetter(next))
            {
                result ??= [];
                for (var k = first; k < unit; k++) result.Add(k);
            }
        }
        return result;
    }

    private List<CompositionSegment> FindSpans(IReadOnlyList<CompositionUnit> units, string pending, bool? precedingEnglish, bool? followingEnglish, DetectionLevel level, bool englishSentence, bool kanaInput, bool final,
        HashSet<int>? japanesePunctuation = null, bool startsWord = true, string? precedingOpener = null, bool afterNewline = false)
    {
        var n = units.Count;
        var segments = new List<CompositionSegment>();
        var japaneseStart = 0;
        var table = new SpanTable(units);
        // 区間の前が英語か: 先頭なら入力欄の確定済みの文字、途中なら直前の区間 (英語区間の直後なら英語、それ以外は日本語)。
        bool? PrecededByEnglish(int start) => start == 0 ? precedingEnglish : segments.Count > 0 && segments[^1].IsEnglish && japaneseStart == start;
        // 前の文脈の点数: 英文の続きなら +2、英語なら +1、日本語なら -1、分からなければ 0。
        int BeforeScore(int start) => start == 0 && englishSentence ? 2 : Score(PrecededByEnglish(start));
        // 語の頭から始まる直前が英語の語 (オプション -name の根拠): 先頭なら入力欄の確定済みの文字、途中なら直前の英語の区間 (間は空白・記号だけ)
        bool EnglishWordBefore(int start) => start == 0 ? precedingEnglish == true && !afterNewline
            : segments.Count > 0 && segments[^1].IsEnglish && Enumerable.Range(japaneseStart, start - japaneseStart).All(k => IsAsciiSymbol(units[k]) || units[k].Raw.All(char.IsWhiteSpace));
        var i = 0;
        var bracketCloser = -1;
        while (i < n)
        {
            var found = -1;
            // 保護区間 (パス・オプション・変数) は、語の頭から空白まで英字のまま (1-4a)。以下の判定より先に決める。
            var protectedEnd = !kanaInput && level != DetectionLevel.Manual ? ProtectedEnd(units, i, pending, final, startsWord, EnglishWordBefore, precedingOpener, PrecededByEnglish(i) == true) : -1;
            if (protectedEnd >= 0)
            {
                found = protectedEnd;
                // 日本語の 「 (確定済みの 「 も) に対応する ] は範囲に入れず、日本語の 」 のまま (下の「英語の後ろの記号は半角」に回さない)
                if (protectedEnd < n && units[protectedEnd].Raw == "]" && (i > 0 ? units[i - 1].Raw == "[" && PrecededByEnglish(i) != true : precedingOpener == "「")) bracketCloser = protectedEnd;
            }
            // ドメイン・拡張子の . の後ろは、ローマ字として読めても英字のままにする (tetr.io、Tetr.TV、MeltypeTip.dll)。
            // (本家 yksr-melt/Meltype の PR #267 / issue #260・#254 と同じ変更)
            if (found < 0 && !kanaInput && level != DetectionLevel.Manual && i > 0 && units[i - 1].Raw == "." && PrecededByEnglish(i) == true) found = DotSuffixEnd(units, i, pending, final);
            // 入力の先頭の .gitignore / .env / .exe などドットファイルは英字のままにする (本家 803719c、#34)。
            // 途中の 3.14desu や「です。」の . には使わない (i == 0 のときだけ)。
            if (found < 0 && !kanaInput && level != DetectionLevel.Manual && i == 0 && units[i].Raw == ".")
            {
                var dotfile = DotSuffixEnd(units, i + 1, pending, final);
                if (dotfile > i + 1) found = dotfile;
            }
            // 英文の中の記号 (, . ! ? -) は読点・句点にせず半角のまま。日本語の文の中の英単語の後 (今日はgoogle、) は日本語の記号。
            // (かな入力では 、。 も かなのキーなので対象外。改行の直後の - は前の行の英語の続きではない: --sorekara の「ーー」。A-5)
            if (found < 0 && !kanaInput && i != bracketCloser && IsAsciiSymbol(units[i]) && PrecededByEnglish(i) == true && !(i == 0 && afterNewline && units[i].Raw == "-") && segments.All(s => s.IsEnglish || !s.Raw.Any(char.IsAsciiLetter)) &&
                japanesePunctuation?.Contains(i) != true) found = i + 1;
            if (!kanaInput && found < 0) found = UserNameEnd(units, i, pending);
            // 設定「英単語の後ろの英字の名前を英語にする」(本家 #1039): 前が英語の語 (Koki Ogawa の Ogawa) は、
            // 1 文字の大文字 + 助詞で始まる語 (O|gawa) に分けない。英語の文脈の続きなので、そのまま英語の区間として見る。
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual && !(KeepEnglishNameAfterEnglish() && table.StartsWithUpper(i) && PrecededByEnglish(i) == true))
                found = CapitalizedWordEnd(units, i, pending, final);
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual) found = HyphenatedWordEnd(units, i, pending);
            // 英語の語 + 数字のすぐ後ろの英単語 (part1026|beta、win11|pro) は、ローマ字として読めても英語 (ベタ にしない)。
            // 助詞で始まるなら日本語 (PS5|wokaitai)。
            if (!kanaInput && found < 0 && level != DetectionLevel.Manual && AlphanumericSuffixEnd(units, i, pending, segments, japaneseStart) is var suffix and > 0) found = suffix;
            // 英語の区間になれるのは、英字 (と ') だけでできた、長すぎない区間 (IsEnglishSpan を参照)。
            // それ以外の区間は判定するまでもなく英語ではないので、判定を飛ばす (長い文で、打つたびに全部の区間を調べて遅くなっていた)。
            var upperStart = table.StartsWithUpper(i);
            // 学習した語 (Memory) は、長くても今までどおり調べる
            var lengthLimit = Math.Max(MaxEnglishSpanLength, Memory?.MaxWordLength ?? 0);
            for (var j = ReferenceMode ? n : kanaInput ? table.LastLetterEnd(i) : table.LastEnglishEnd(i); j > i && found < 0; j--)
            {
                if (!ReferenceMode && table.Length(i, j, pending) > lengthLimit && !(j == n && upperStart) && !table.HasApostrophe(i, j)) continue;
                // 区間の後ろ: 末尾まで打っているならキャレットの後ろの文字、途中なら続きの日本語。
                // 後ろが記号だけ (let's go! の !) なら、語はそこで打ち終わっている: Enter で確定するときと同じく末尾の語として見る
                // (記号を日本語の続きとみなして、英文の中の go・no を ご・の にしていた)。
                // (入力が 1 語 + 記号だけのとき。途中の区間 (BE|kana|?) の後ろの記号は、今までどおり日本語の続きとみなす)
                var symbolsAfter = i == 0 && j < n && pending.Length == 0 && Enumerable.Range(j, n - j).All(k => IsAsciiSymbol(units[k]));
                var after = j == n || symbolsAfter ? followingEnglish : false;
                // 英単語のすぐ後ろの する の活用 (push + site = して、commit + sita = した) は、英単語 (site) でも日本語
                // (末尾だと pushsite 全体が英字になっていた)。
                if (!kanaInput && PrecededByEnglish(i) == true && IsSuruForm(table.Kana(i, j))) continue;
                // カスタム表: 実機のスペルチェッカーだけが英単語と言う短い綴り (hast) の後ろに表のキーが続き (kt)、
                // 区間と続きの英字を合わせると表でだけ最後まで読める綴りになる (hastkt = ha + st + kt) なら、英語の区間にしない。
                if (!kanaInput && !ReferenceMode && j < n && IsTableOnlyContinuation(table.Raw(i, j), table.Raw(j, n) + pending)) continue;
                var english = kanaInput
                    ? IsEnglishSpanKana(table.Raw(i, j), table.Kana(i, j), atEnd: j == n, BeforeScore(i), after, level, final)
                    : IsEnglishSpan(table.Raw(i, j) + (j == n ? pending : ""), atEnd: j == n, BeforeScore(i), after, startOfInput: i == 0, level, final, endsWord: symbolsAfter,
                        unreadable: table.HasUnreadable(i, j) || EndsWithLoneSokuon(units, j), next: j < n ? units[j].Raw + (j + 1 == n ? pending : "") : null,
                        // 略語の形の語 (api) + の + 英単語 (apinoerror)。先に安い条件 (の・残りの長さ) を見てから文字列を作る (長い文で遅くしない)
                        noThenEnglish: j + 1 < n && !symbolsAfter && units[j].Raw == "no" && table.Length(j + 1, n, pending) >= 3 &&
                            IsSpelledAcronym(table.Raw(i, j).ToLowerInvariant()) && IsEnglishWordAfterNo(table.Raw(j + 1, n) + pending));
                if (english)
                {
                    found = j;
                    break;
                }
            }
            // 途中で終わる英語の区間 (te + al… の teal) より、少し後ろから末尾まで続く長い英単語 (alcoholic) があれば、そちらを取る
            // (sometealcoholic → 染めて + alcoholic。teal を取ると残りの coholic がローマ字になってしまう)。
            if (found > i && found < n && !kanaInput && !IsAsciiSymbol(units[i]) && protectedEnd < 0)
            {
                // 後ろに日本語が続いてもよい (motte|school|he → mottes を取ると chool が ちょおl になる。持って + school + へ)。
                var foundLength = Raw(units, i, found).Length;
                // ただし、見つけた区間が同梱の一覧の語 (over・email・candidate) で、長い語が「見つけた区間の後ろの部分 + 助詞 1 つ」
                // (o|ver + de、e|mail + to、ca|ndidate + de) で同梱の一覧に無い語なら、それでは捨てない。実機の NSSpellChecker は
                // そういう断片 (verde・mailto・ndidatede・thermo・steno) も英単語と答えるので、overdesu が英字のまま、emailto が えmailto、
                // candidatedesu が かndidatedeす になっていた。捨てる条件に AND を 1 つ足しただけなので、捨てるのは変更前に捨てていた
                // 場合の一部だけ。助詞 1 つに限るのは、長い語が後ろの英字まで続く場合 (hi|ring + tone の ringtone、de|al + pine の alpine)
                // は、長い語のほうが打ちたい語のことが多いため (前後比較で、助詞 1 つに限らないと よく使う語 で外れが出た)。
                var foundListed = IsBundledEnglishWord(Raw(units, i, found).ToLowerInvariant());
                for (var k = i + 1; k < found && found >= 0; k++)
                {
                    for (var e = ReferenceMode ? n : table.LastLetterEnd(k); e > found; e--)
                    {
                        if (table.Length(k, e, pending) < foundLength || !ReferenceMode && table.Length(k, e, pending) > lengthLimit) continue;
                        var word = table.Raw(k, e) + (e == n ? pending : "");
                        if (word.Length >= foundLength && IsLongEnglishWord(word) &&
                            !(foundListed && !IsBundledEnglishWord(word.ToLowerInvariant()) && IsParticle(word[Raw(units, k, found).Length..])))
                        {
                            found = -1;
                            break;
                        }
                    }
                }
            }
            if (found < 0)
            {
                i++;
                continue;
            }
            if (i > japaneseStart) segments.Add(Japanese(units, japaneseStart, i, ""));
            segments.Add(new CompositionSegment(true, "", Raw(units, i, found) + (found == n ? pending : ""), Protected: protectedEnd >= 0));
            i = found;
            japaneseStart = found;
            if (found == n) return segments;
        }

        // 打ちかけの 1 文字だけ (how r u の r): 1 文字の語の決まり (前が英語なら r・u は英字) で見る。
        if (n == 0 && !kanaInput && pending.Length == 1 && char.IsAsciiLetterLower(pending[0]) &&
            IsEnglishSpan(pending, atEnd: true, BeforeScore(0), followingEnglish, startOfInput: true, level, final))
        {
            return [new CompositionSegment(true, "", pending)];
        }
        // Shift を押して打った入力途中の子音 (W, K) は大文字のまま英字で見せる (かなの読み途中として小文字にしない)。
        if (pending.Length > 0 && char.IsAsciiLetterUpper(pending[0]))
        {
            if (japaneseStart < n) segments.Add(Japanese(units, japaneseStart, n, ""));
            segments.Add(new CompositionSegment(true, "", pending));
            return segments;
        }
        if (japaneseStart < n || pending.Length > 0 || segments.Count == 0)
        {
            segments.Add(Japanese(units, japaneseStart, n, pending));
        }
        return segments;
    }

    /// <summary>
    /// これより長い英字の並びは、1 語の英単語 (辞書・スペルチェッカーの語、固有名詞、短縮形) にならない。
    /// 打った内容が長くなっても区間分けが遅くならないよう、これを超える区間は判定しない。
    /// (大文字で始めて末尾まで打った区間だけは、長さに関係なく英語なので、今までどおり判定する)
    /// </summary>
    internal const int MaxEnglishSpanLength = 48;

    /// <summary>テスト用: true なら区間を飛ばす最適化と判定の覚えを使わない (素朴な全探索。最適化が結果を変えないことの照合用)。</summary>
    internal static bool ReferenceMode { get; set; }

    /// <summary>区間の判定の覚えを捨てる (変換ボックスが空になったとき・リセットのとき)。</summary>
    public void ClearSpanCache()
    {
        lock (_spanCache) _spanCache.Clear();
    }

    /// <summary>
    /// FindSpans が区間 [i, j) の読み・打った英字・長さを何度も使うので、単位列から 1 度だけ作る表。
    /// 区間のたびに単位を 1 つずつつなぎ直すと、長さの 3 乗で遅くなる。
    /// </summary>
    private sealed class SpanTable
    {
        private readonly IReadOnlyList<CompositionUnit> _units;
        private readonly int _n;
        private readonly string _raw;
        private readonly string _kana;
        private readonly int[] _rawOffset;
        private readonly int[] _kanaOffset;
        // k 以降で最初に「英字と ' だけ」でない単位 / 「英字だけ」でない単位の位置 (無ければ n)
        private readonly int[] _nextNonEnglish;
        private readonly int[] _nextNonLetter;
        private readonly int[] _unreadable;
        private readonly int[] _apostrophes;

        public SpanTable(IReadOnlyList<CompositionUnit> units)
        {
            _units = units;
            _n = units.Count;
            _rawOffset = new int[_n + 1];
            _kanaOffset = new int[_n + 1];
            _nextNonEnglish = new int[_n + 1];
            _nextNonLetter = new int[_n + 1];
            _unreadable = new int[_n + 1];
            _apostrophes = new int[_n + 1];
            var raw = new StringBuilder();
            var kana = new StringBuilder();
            for (var k = 0; k < _n; k++)
            {
                _rawOffset[k] = raw.Length;
                _kanaOffset[k] = kana.Length;
                raw.Append(units[k].Raw);
                kana.Append(units[k].Kana);
            }
            _rawOffset[_n] = raw.Length;
            _kanaOffset[_n] = kana.Length;
            _raw = raw.ToString();
            _kana = kana.ToString();
            _nextNonEnglish[_n] = _n;
            _nextNonLetter[_n] = _n;
            for (var k = _n - 1; k >= 0; k--)
            {
                var text = units[k].Raw;
                _nextNonEnglish[k] = text.All(c => char.IsAsciiLetter(c) || c == '\'') ? _nextNonEnglish[k + 1] : k;
                _nextNonLetter[k] = text.All(char.IsAsciiLetter) ? _nextNonLetter[k + 1] : k;
            }
            for (var k = 0; k < _n; k++)
            {
                _apostrophes[k + 1] = _apostrophes[k] + (units[k].Raw.Contains('\'') ? 1 : 0);
                // 読めなかった英字 (読みが打った英字のまま) の数。笑いの w は数えない
                var unreadable = units[k] is { Raw.Length: 1 } unit && unit.Kana == unit.Raw && char.IsAsciiLetter(unit.Raw[0]) && !IsLaughter(units, k);
                _unreadable[k + 1] = _unreadable[k] + (unreadable ? 1 : 0);
            }
        }

        /// <summary>[i, j) の打った英字 (pending は含まない)。</summary>
        public string Raw(int i, int j) => _raw.Substring(_rawOffset[i], _rawOffset[j] - _rawOffset[i]);

        public string Kana(int i, int j) => _kana.Substring(_kanaOffset[i], _kanaOffset[j] - _kanaOffset[i]);

        /// <summary>[i, j) の打った英字の長さ。j が末尾なら入力途中の子音 (pending) も含む。</summary>
        public int Length(int i, int j, string pending) => _rawOffset[j] - _rawOffset[i] + (j == _n ? pending.Length : 0);

        /// <summary>[i, j) に ' がある (短縮形 don't の判定は、語の長さを見ないので、長さで飛ばさない)。</summary>
        public bool HasApostrophe(int i, int j) => _apostrophes[j] - _apostrophes[i] > 0;

        public bool HasUnreadable(int i, int j) => _unreadable[j] - _unreadable[i] > 0;

        public bool StartsWithUpper(int i) => i < _n && _units[i].Raw.Length > 0 && char.IsAsciiLetterUpper(_units[i].Raw[0]);

        /// <summary>i から始まる区間の終わりの最大値 (これより後ろは英字と ' 以外の単位を含むので英語にならない)。</summary>
        public int LastEnglishEnd(int i) => _nextNonEnglish[i] < _n ? _nextNonEnglish[i] : _n;

        /// <summary>同じく、英字だけの区間 (かな入力の判定・長い英単語の判定用)。</summary>
        public int LastLetterEnd(int i) => _nextNonLetter[i] < _n ? _nextNonLetter[i] : _n;
    }

    /// <summary>
    /// 英語とも日本語とも読める語か (i, sushi, make, repo): 英単語で、ローマ字としても最後まで読める (母音か ん で終わる)。
    /// 確定した後で前後の文脈と食い違ったら、確定し直す対象になる。
    /// </summary>
    public bool IsAmbiguousWord(string raw)
    {
        if (raw.Length == 0 || !raw.All(char.IsAsciiLetter) || raw.Any(char.IsAsciiLetterUpper)) return false;
        var lower = raw.ToLowerInvariant();
        if (!_english.Words.ContainsWord(lower) || _proper.Contains(lower)) return false;
        var analysis = _romaji.Analyze(lower);
        return analysis.IsValid && analysis.Partial is "" or "n";
    }

    /// <summary>
    /// 確実に英語の語か (want, google, Tokyo): ローマ字として読めない・子音で終わる英単語・固有名詞・大文字で始まる。
    /// 前後の文脈にかかわらず英語なので、直前に確定した語を確定し直す根拠にできる。
    /// </summary>
    public bool IsDefinitelyEnglish(string raw)
    {
        if (raw.Length == 0 || !raw.All(char.IsAsciiLetter)) return false;
        if (char.IsAsciiLetterUpper(raw[0])) return true;
        var lower = raw.ToLowerInvariant();
        if (_proper.Contains(lower)) return true;
        var analysis = _romaji.Analyze(lower);
        if (!analysis.IsValid) return _english.Words.ContainsWord(lower) || _english.IsPrefix(lower) || lower.Length >= 4;
        // 確定するときに呼ぶので、語は打ち終わっている (it が itai の打ちかけかは気にしない)。
        var word = _english.Words.ContainsWord(lower) || IsSpellWord(lower);
        return word && analysis.Partial.Length > 0 && analysis.Partial != "n";
    }

    /// <summary>
    /// Space を押した時点で、かなにならない子音が残る英単語か (my, by, meeting)。日本語として変換しても子音が残るだけなので、
    /// 英語として確定して空白を入れる。
    /// </summary>
    public bool IsEnglishAtWordEnd(string raw, DetectionLevel level)
    {
        if (level == DetectionLevel.Manual || raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        // 設定「日本語入力中は一覧の語をかなで変換する」(本家 #1023): 一覧の語は、Space でも英語として確定しない
        // (変換を始めて かな の候補を出す)。英語の文の中は、このメソッドに来る前に英語の区間として確定する。
        if (KanaInJapaneseMode() && KanaInJapaneseModeWords.ContainsWord(lower)) return false;
        var analysis = _romaji.Analyze(lower);
        if (!analysis.IsValid || analysis.Partial.Length == 0 || analysis.Partial is "n" or "nn") return false;
        return _english.Words.ContainsWord(lower) || IsSpellWord(lower);
    }

    /// <summary>スペルチェッカーが正しいと言う英単語か、よくある打ち間違い (teh、recieve) か。</summary>
    internal bool IsSpellWord(string lower) =>
        // macOS の NSSpellChecker は 73 文字以上の未知の文字列を検査せず「誤りなし」(= 語) と返す (実測 60〜72 文字は語でない、73 文字以上は全部語)。
        // 長いローマ字の文を 1 語の英単語とみなさない (本家 PR #709)。
        lower.Length <= 72 && SpellChecker is { } checker && (checker.IsWord(lower) || checker.AutoCorrection(lower) is not null);

    /// <summary>
    /// 助詞 (de・ni・no…) で始まり、残り 3 文字以上が英単語の語 (de|vote = devote、no|vice = novice)。
    /// 残りの判定にスペルチェッカーは使わず、同梱の表 (english.txt・固有名詞・4 文字以上は english-words.txt) と覚えた語だけを見る。
    /// 実機の NSSpellChecker は rive を英単語と答えるので、スペルチェッカーで見ると derive も de|rive になり、
    /// deriveyadevote が でりゔぇやでvote になっていた (テストの同梱の表には rive が無いので deriveやでvote)。
    /// </summary>
    private bool StartsWithParticleThenEnglishWord(string lower)
    {
        if (Detection.DictionaryDetector.StartsWithParticle(lower) is not { } particle || lower.Length - particle.Length < 3) return false;
        var rest = lower[particle.Length..];
        if (!rest.All(char.IsAsciiLetterLower)) return false;
        if (Memory?.Get(rest) is { } learned) return learned;
        return _english.Words.ContainsWord(rest) || _proper.Contains(rest) || (rest.Length >= 4 && Detection.BuiltInWordChecker.Shared.IsWord(rest));
    }

    internal bool IsJapaneseWordOrPrefix(string kana) => _kana?.IsJapaneseWordOrPrefix(kana) == true;

    /// <summary>よくある英語の打ち間違いなら正しい綴り (teh → the)。大文字で始まる語は大文字で始める。</summary>
    public string? EnglishAutoCorrection(string word)
    {
        if (word.Length < 2 || !word.All(char.IsAsciiLetter) || SpellChecker?.AutoCorrection(word.ToLowerInvariant()) is not { } right) return null;
        if (word.All(char.IsAsciiLetterUpper) && word.Length > 1) return right.ToUpperInvariant();
        return char.IsAsciiLetterUpper(word[0]) ? char.ToUpperInvariant(right[0]) + right[1..] : right;
    }

    /// <summary>同梱の英単語の辞書・固有名詞にある語か、ユーザーが英字に直して覚えた語か (ok、github)。スペルチェッカーは使わない。</summary>
    public bool IsListedEnglishWord(string lower) =>
        lower.Length >= 2 && (Memory?.Get(lower) ?? (_english.Words.ContainsWord(lower) || _proper.Contains(lower)));

    /// <summary>
    /// 同梱の一覧にある英単語か (IsListedEnglishWord か、4 文字以上なら english-words.txt = BuiltInWordChecker)。スペルチェッカーは使わない
    /// (IsListedEnglishWord は学習した語も見るので、厳密には「同梱の一覧 + 学習」)。
    /// 語の途中から始まる断片 (verde・umol) は実機の NSSpellChecker が英単語と答えるので、それを根拠にする規則の守りに使う。
    /// </summary>
    internal bool IsBundledEnglishWord(string lower) =>
        IsListedEnglishWord(lower) || (lower.Length >= 4 && Detection.BuiltInWordChecker.Shared.IsWord(lower));

    /// <summary>
    /// 知っている英単語か (同梱の辞書・固有名詞・ユーザーが英字に直して覚えた語・4 文字以上ならスペルチェッカー)。
    /// python + no の n のように、英単語の最後の n と次の音がくっつくのを防ぐのに使う。
    /// </summary>
    public bool IsKnownEnglishWord(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length < 3 || !lower.All(char.IsAsciiLetterLower)) return false;
        if (Memory?.Get(lower) is { } learned) return learned;
        return _english.Words.ContainsWord(lower) || _proper.Contains(lower) || (lower.Length >= 4 && IsSpellWord(lower));
    }

    private static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>する の活用 (して・した・します …) だけでできたかなか。</summary>
    private static bool IsSuruForm(string kana) => SuruForms.Any(kana.StartsWith) && kana.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー');

    private static int Score(bool? english) => english switch { true => 1, false => -1, null => 0 };

    /// <summary>英文の中では半角のままにする記号。[ ] は日本語の入力では「」なので含めない (英単語の後ろでも「」: bot「Thinking」)。</summary>
    private static bool IsAsciiSymbol(CompositionUnit unit) =>
        unit.Raw.Length == 1 && unit.Raw[0] is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(unit.Raw[0]) && unit.Raw[0] is not ('[' or ']');


    /// <param name="final">打ち終わった (Space・Enter)。末尾の区間でも、英単語の打ちかけ (amaz) は英語の根拠にしない。</param>
    /// <param name="unreadable">区間にローマ字として読めなかった英字がある (zoom + de の m、bug + wo の g)。</param>
    /// <param name="endsWord">区間の後ろが記号だけ (let's go! の go)。語はそこで打ち終わっているので、短い語も末尾の語と同じく見る。</param>
    /// <param name="noThenEnglish">区間が略語の形の語 (api) で、後ろが の + 英単語 (apinoerror の no + error)。</param>
    private bool IsEnglishSpan(string span, bool atEnd, int before, bool? after, bool startOfInput, DetectionLevel level, bool final = false, bool unreadable = false, string? next = null, bool endsWord = false, bool noThenEnglish = false)
    {
        // 1 キー打つたびに、ほぼ同じ区間 (前のキーまでに調べた区間) を何度も判定するので、結果を覚えておく。
        // 判定は引数と、学習した語 (Memory) とスペルチェッカーだけで決まる。それらが変わったら捨てる。
        var flags = (atEnd ? 1 : 0) | (startOfInput ? 2 : 0) | (final ? 4 : 0) | (unreadable ? 8 : 0) | (endsWord ? 16 : 0) |
                    ((after is null ? 0 : after.Value ? 1 : 2) << 5) | ((int)level << 7) | (KanaInJapaneseMode() ? 1 << 9 : 0) | ((before + 8) << 10) | (noThenEnglish ? 1 << 20 : 0);
        // カスタムローマ字表でしか最後まで読めない綴り (表 st・kt に対する stkt) は、英語ではなく日本語 (C1)。結果は表で変わるので、覚えた結果は使わない。
        if (!ReferenceMode && IsTableOnlySpelling(span)) return false;
        if (ReferenceMode) return IsEnglishSpanCore(span, atEnd, before, after, startOfInput, level, final, unreadable, next, endsWord, noThenEnglish);
        var key = (span, next, flags);
        lock (_spanCache)
        {
            if (!ReferenceEquals(_spanCacheMemory, Memory) || _spanCacheMemoryVersion != (Memory?.Version ?? -1) || !ReferenceEquals(_spanCacheChecker, SpellChecker) || _spanCache.Count > 20000)
            {
                _spanCache.Clear();
                _spanCacheMemory = Memory;
                _spanCacheMemoryVersion = Memory?.Version ?? -1;
                _spanCacheChecker = SpellChecker;
            }
            if (_spanCache.TryGetValue(key, out var cached)) return cached;
        }
        var result = IsEnglishSpanCore(span, atEnd, before, after, startOfInput, level, final, unreadable, next, endsWord, noThenEnglish);
        lock (_spanCache) _spanCache[key] = result;
        return result;
    }

    /// <summary>
    /// 区間全体が、標準の綴りでは読めず、カスタム表のキーを使うと最後まで読める小文字の綴りか (C1)。
    /// 標準で読める綴り・表のキーを 1 つも使わない綴り・英単語として知っている語 (string / ring / apple / stereo) は対象にしない:
    /// 表 ON でも、表のキーが語全体を覆うときだけ日本語にして、英語の判定は変えない。表が無い・OFF のときは常に false。
    /// </summary>
    private bool IsTableOnlySpelling(string span)
    {
        // 安いもの (表を引かない判定) を先に見る: 区間ごとに呼ばれ、表の取得はファイルの更新確認を伴うため。
        if (span.Length < 2 || !span.All(char.IsAsciiLetterLower)) return false;
        if (_romaji.AnalyzeFragment(span) is { IsValid: true, Partial: "" or "n" }) return false;
        if (_romaji.CustomTable?.Invoke() is not { } table) return false;
        var analysis = _romaji.AnalyzeFragmentForKana(span);
        if (!analysis.IsValid || analysis.Partial.Length > 0 || !analysis.Tokens.Any(t => table.TryGet(t.Romaji, out _))) return false;
        return !(IsListedEnglishWord(span) || IsKnownEnglishWord(span) || IsSpellWord(span));
    }

    /// <summary>
    /// 区間 (hast) が実機のスペルチェッカーだけで英単語になる短い綴りで、その後ろの英字 (kt…) と合わせた全体 (hastkt) が
    /// カスタム表でだけ最後まで読める綴り (IsTableOnlySpelling) か。そうなら区間は英語ではなく日本語の一部 (きょう + は + したこと)。
    /// 同梱の英単語・固有名詞・学習した語 (IsBundledEnglishWord) は対象にしない: string / ring / apple / stop は表 ON でも変わらない。
    /// 表が無い・OFF・区間の後ろが英小文字でないときは常に false。
    /// </summary>
    private bool IsTableOnlyContinuation(string span, string rest)
    {
        // 安いもの (表を引かない判定) を先に見る
        if (span.Length < 2 || rest.Length == 0 || !span.All(char.IsAsciiLetterLower) || !char.IsAsciiLetterLower(rest[0])) return false;
        var run = rest.TakeWhile(char.IsAsciiLetterLower).Count();
        if (run + span.Length > MaxEnglishSpanLength) return false;
        if (IsBundledEnglishWord(span) || !IsSpellWord(span)) return false;
        return IsTableOnlySpelling(span + rest[..run]);
    }

    private readonly Dictionary<(string Span, string? Next, int Flags), bool> _spanCache = [];
    private LanguageMemory? _spanCacheMemory;
    private int _spanCacheMemoryVersion;
    private Detection.IWordChecker? _spanCacheChecker;

    private bool IsEnglishSpanCore(string span, bool atEnd, int before, bool? after, bool startOfInput, DetectionLevel level, bool final, bool unreadable, string? next, bool endsWord, bool noThenEnglish)
    {
        // まだ続きを打つかもしれない末尾の区間 (打ちかけの英単語を英語と見てよい)。
        var growing = atEnd && !final;
        if (IsContraction(span)) return level != DetectionLevel.Manual || char.IsAsciiLetterUpper(span[0]);
        if (span.Length == 0 || !span.All(char.IsAsciiLetter)) return false;
        var lower = span.ToLowerInvariant();
        // 小文字で始まって途中に大文字がある区間 (iPC、meteSNS) は、固有名詞の書き方 (iPhone・eBay・macOS) でなければ 1 語ではない:
        // 小文字の部分は前の日本語の続き (atarashi|i|PC → あたらしいPC、motome|te|SNS → 求めてSNS)。
        if (char.IsAsciiLetterLower(span[0]) && span.Skip(1).Any(char.IsAsciiLetterUpper) && _proper.Canonical(lower) != span) return false;
        // 短い区間の最後の子音が、次の音と合わせて っ になる (u|lo|t + ti = ぉっち) なら、英単語 (lot) ではなく日本語の途中
        if (lower.Length <= 4 && next is [var following, ..] && char.ToLowerInvariant(following) == lower[^1] && lower[^1] is not ('a' or 'i' or 'u' or 'e' or 'o' or 'n') &&
            _romaji.AnalyzeFragment(lower[..^1]) is { IsValid: true, Partial: "" })
        {
            return false;
        }
        // 数字のすぐ前の、ローマ字として読めない子音で始まる短い英字 (kaibunsho|rta|2026、ps5): 略語。日本語の打ちかけではない
        if (next is [var digit, ..] && char.IsAsciiDigit(digit) && span.Length is >= 2 and <= 6 && span.All(char.IsAsciiLetter) &&
            _romaji.AnalyzeFragment(lower[..2]) is { IsValid: false })
        {
            return true;
        }
        // 日本語のすぐ後ろで、助詞 + 英単語 (dochira|mo|user の mouser) は、スペルチェッカーが 1 語と言っても 助詞 + 英単語
        // (同梱の英語の辞書の語は除く)。後ろの英単語の区間は、この後で別に見る。
        // ただし区間全体が同梱の英単語の一覧 (english-words.txt) の語 (derive・delivery・topics) なら、残りが同梱の表の語
        // (#69 の守りと同じ StartsWithParticleThenEnglishWord の意味) のときだけ分ける。スペルチェッカーだけが語と言う残りでは分けない:
        // 実機の NSSpellChecker は rive・livery・pics を英単語と答えるので、kyouhaderive が きょうはでりゔぇ、
        // kyounotopics が きょうのとpics になっていた (K82)。区間全体が一覧に無い語 (decarbonize) は、今までどおりスペルチェッカーにも聞く。
        // この除外は分けないほう (英字のまま) にしか働かない: 分けるのは、除外が無かったとき (K82 の前) に分けていた場合の一部だけ。
        if (before < 0 && Detection.DictionaryDetector.StartsWithParticle(lower) is { } leading && lower.Length - leading.Length >= 3 &&
            !_english.Words.ContainsWord(lower) && IsKnownEnglishWord(lower[leading.Length..]) &&
            !(Detection.BuiltInWordChecker.Shared.IsWord(lower) && !StartsWithParticleThenEnglishWord(lower)))
        {
            return false;
        }

        var inDictionary = _english.Words.ContainsWord(lower);
        // 知らない英字の語 + 助詞 (grok|ga、figma|de) は、助詞までを 1 語にしない。語の部分だけの区間はこの後で別に見る
        if (EndsWithParticleAfterUnknownWord(lower)) return false;
        // Windows のスペルチェッカーの英単語 (meeting, name …)。ローマ字の語 (kore, sore) まで含む緩いものなので、
        // ローマ字として読めない語か、前後の文脈で英語と分かるときだけ使う (同梱の辞書の語より弱い)。
        var conservative = level == DetectionLevel.Conservative;
        // 小書き文字の綴り (mala = まぁ, xtu = っ) で最後まで読める語は、日本語をわざわざ打っている。同梱の辞書の英単語以外は日本語。
        // (6 文字以上のスペルチェッカーの英単語は除く: chocolate の la = ぁ でも英語)
        var smallKanaSpelling = !_romaji.Analyze(lower).IsValid && _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" } && !(lower.Length >= 6 && IsSpellWord(lower));
        var spellWord = !inDictionary && !smallKanaSpelling && IsSpellWord(lower);
        var exact = inDictionary || (spellWord && !_romaji.Analyze(lower).IsValid);
        var prefix = growing && lower.Length >= 4 && !conservative && !smallKanaSpelling && _english.IsPrefix(lower);

        // 大文字で始まる語 (Shift を押して打った) は固有名詞や英文。1 文字 (I) でも、末尾まで打っている途中でも英語。
        // 手動でもこれだけは英語にする (Shift を押したのはユーザーの明示的な指定)。
        // 大文字で始まる語の後ろに記号が続く (Ah! Ooh, Wow.) なら、その語で終わっている: スペルチェッカーの語でも英語。
        if (char.IsAsciiLetterUpper(span[0]) && (exact || prefix || atEnd || (spellWord && next is [var symbol, ..] && !char.IsAsciiLetterOrDigit(symbol)))) return true;
        // 大文字で始まり、ローマ字として最後まで読めない語 (Tel の l) の後ろに : ; が続く (Tel: 03-…、Fax: …) なら、その語で終わっている英語 (項目名)。
        // 打っている途中 (Tel) は末尾の語として英語なので、: を打っても てl: に変えない。最後まで読める語は日本語のまま。
        // , . ! ? は日本語の句読点でもあるので対象にしない。
        if (char.IsAsciiLetterUpper(span[0]) && span.Length >= 3 && next is [var closing, ..] && closing is (':' or ';') &&
            !(_romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" or "n" }) && !(Memory?.Get(lower) is false)) return true;
        if (level == DetectionLevel.Manual) return false;
        // 設定「日本語入力中は一覧の語をかなで変換する」(本家 #1023): 英語の辞書にある語でも、
        // 前が英語でない (日本語の続き・入力の先頭) なら日本語として扱う。英語の文の中と .exe は今までどおり英語。
        if (KanaInJapaneseMode() && before <= 0 && KanaInJapaneseModeWords.ContainsWord(lower)) return false;
        // 小書き文字の綴り (xe = ぇ) でしか最後まで読めない、同梱の辞書の 3 文字の英単語 (exe) は、日本語のすぐ後ろ・後ろに日本語が続くときも英字のまま
        // (korehaexedesu → これはexeです。入力の先頭の exewo → exeを と同じ。K98)。後ろの日本語を数えると えぇ になっていた。
        // 日本語の語の頭になる綴り (xa・xi で始まる語など) と固有名詞は除く。設定が ON のときは、上で一覧の語を先に かな にしている。
        if (inDictionary && smallKanaSpelling && lower.Length == 3 && before <= 0 && !_japanese.IsPrefix(lower) && !_proper.Contains(lower)) return true;
        // ユーザーが英字 / かなに直して覚えた語。ただし短くてローマ字として読める語 (go、no) は、日本語のすぐ後ろ
        // (nihon|go) では使わない (一度 go を英字で確定しただけで、日本語 が にほんgo になっていた)。
        // english-readable.txt の語 (main) を英字で覚えていても、続く 1 音とでよく使う語・ブランド名の頭になる (main + do = マインド) ときは、覚えた英字を使わない (Q11 の実機確認)。
        // 日本語の後ろでは覚えた英字を今までどおり使う (記号の後ろの `main`・(main) は、覚えた英字でしか英字にならないため。査読 R33 の 3 回目)。
        if (Memory?.Get(lower) is { } learned && !(learned && before < 0 && lower.Length <= 3 && _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" }) &&
            !(learned && ReadableEnglish.Value.ContainsWord(lower) && ContinuesJapaneseWord(lower, next))) return learned;
        // 5 文字以上の英単語で、ローマ字としても読めるもの:
        // - c 行の綴り (camera、coffee、class) は英語。日本語を打つときは k を使う (カメラ は kamera)。
        // - ローマ字として読むと ぢ・づ になる綴り (radio = らぢお、studio、audio) で、ふつうの日本語の語にならないなら英語。
        //   (sake・tokyo・suzuki のような日本由来の語は、ふつうのかなになるので、ここでは英語にしない)
        //   日本語の語にもなるもの (tomato = とまと、piano = ぴあの、anime) は、今までどおり前後の文脈で決める。
        // 日本語の辞書の語 (suzuki) と、慎重なときは使わない。続きと合わせて日本語の語になる (sense + i = せんせい) ときも使わない。
        if (lower.Length >= 5 && !conservative && (inDictionary || IsSpellWord(lower)) && !_japanese.IsPrefix(lower) &&
            !_proper.Contains(lower) && _romaji.AnalyzeFragment(lower) is { IsValid: true } &&
            !(next is { Length: > 0 } && IsCommonJapanese is { } common && !common(lower) && common(lower + next.ToLowerInvariant())))
        {
            if (lower.Contains('c') && !lower.Contains("ch")) return true;
            if (_romaji.AnalyzeFragment(lower).Kana.IndexOfAny(['ぢ', 'づ']) >= 0 && IsCommonJapanese?.Invoke(lower) != true) return true;
        }
        // ローマ字として最後まで読めても、日本語の語にならない英単語 (feature = ふぇあつれ、remote = れもて。dictionaries/english-readable.txt、#12)。
        // 日本語の語の始まりにもならない語だけを入れているので、後ろに日本語が続いても (feature|wo) 英語。
        // ただし、続く 1 音と合わせて日本語の語の頭になるなら日本語 (main + do = マインド・マインドセット。main + de = main で は英語。Q11)。
        // main・domain は、日本語のすぐ後ろでは使わない (う + まいんです = うまいんです、あまいんだよ、せまいんじゃない。査読 R33)。
        if (lower.Length >= 4 && ReadableEnglish.Value.ContainsWord(lower) && !ContinuesJapaneseWord(lower, next) &&
            !(!startOfInput && before < 0 && NotAfterJapanese.Contains(lower))) return true;
        // c 行の綴りで読める語 (care = かれ、can = かん) が日本語の途中にあるなら、日本語を打っている (fucarete → ふかれて、shoucanshi → しょうかんし)。
        // 入力全体がその語だけのときは英語。
        if (!(startOfInput && atEnd))
        {
            if (lower.Contains('c') && _romaji.Analyze(RomajiDetector.ReadCRow(lower)) is { IsValid: true, Partial: "" or "n" }) return false;
            // v 行 (va = ゔぁ): 辞書の英単語 (video) でなければ日本語 (vanpaia → ゔぁんぱいあ → ヴァンパイア)。
            // スペルチェッカーの 5 文字以上の英単語 (invite、private) は、ゔぃ と読める綴りでも英語 (いんviteしました になっていた: 本家 c6b281e、issue #69)。
            // ただし 助詞 + 英単語 とも読める語 (de|vote = devote) は今までどおり (githubdevote → githubでvote。本家の c6b281e はここを壊す)。
            if (lower.Contains('v') && !lower.Contains('l') && !lower.Contains('x') && !inDictionary && !_proper.Contains(lower) &&
                !(lower.Length >= 5 && IsSpellWord(lower) && IsCommonJapanese?.Invoke(lower) != true && !StartsWithParticleThenEnglishWord(lower)) &&
                _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" or "n" })
            {
                return false;
            }
        }
        // 1 文字は英文の中の a / i だけ。
        // 1 文字は英文の中の a / i と、チャットの略し方の u (you)・r (are) だけ。前が英語の語なら英字 (A fool a fool a, for u / how r u)。
        // 語として独立している (後ろが空白・記号・終わり) ときだけ。後ろにかなが続く (HH|i|reta = HH いれた) なら日本語の 1 音。
        if (span.Length < 2) return lower is "a" or "i" or "u" or "r" && before >= 1 && !(next is { Length: > 0 } && char.IsAsciiLetter(next[0]));

        // 英語の固有名詞 (amazon, adobe, netflix) は、ローマ字として読めても英語。日本語の語と同じ綴りなら除く。
        // 短い名前 (ben, tom) の偶然の一致 (にほんごの|ben|きょう) を避けるため 4 文字以上。文の途中の区間なら
        // 5 文字以上 (きょうは|amazon|で) か、ローマ字として読めないもの。末尾の 4 文字の語は、日本語のすぐ後ろでなければ
        // (ある程度は の teido|ha を tei|doha = Doha にしない)。慎重なら、ローマ字として読めないものだけ。
        // ただし、ローマ字として最後まで読める固有名詞 (korea = これあ) の後ろに、助詞でない日本語が続くなら、日本語の語の途中
        // (korea|reka → これあれか。Korea|reka にしない)。助詞が続くなら固有名詞 (korea|de → Koreaで)。
        // 「n」で終わる語 (karen) は Analyze の Partial が "n" になる。続きと合わせて日本語の語になるなら、
        // 固有名詞ではなく日本語の語の途中 (karenda- → カレンダー。本家 #920・#965)。
        if (lower.Length >= 4 && _proper.Contains(lower) && next is { Length: > 0 } && char.IsAsciiLetter(next[0]) &&
            (_romaji.Analyze(lower) is { IsValid: true, Partial: "" } || _japanese.IsPrefix(lower + next.ToLowerInvariant())) &&
            Detection.DictionaryDetector.StartsWithParticle(next.ToLowerInvariant()) is null)
        {
            return false;
        }
        if (lower.Length >= 4 && _proper.Contains(lower) && !_japanese.Words.ContainsWord(lower) &&
            (conservative ? !_romaji.Analyze(lower).IsValid : (atEnd && before >= 0) || lower.Length >= 5 || !_romaji.Analyze(lower).IsValid))
        {
            return true;
        }
        if (growing && lower.Length >= 4 && !conservative && !smallKanaSpelling && _proper.HasPrefix(lower) && !_japanese.IsPrefix(lower)) return true;

        // 英単語で、ローマ字として読めない英字を含む (zoom + でかいぎ → m が読めない)。日本語の文の途中でも英語。
        // 日本語の後ろで、助詞 + 読めない英字 (の + ts: jissainotsyu) は、英単語 (not) ではなく 助詞 + 英字。
        // 助詞の後ろがローマ字の打ちかけ (ts = つ) のときだけ。ローマ字にならない (he + lp: help) なら英単語。
        if (unreadable && before < 0 && Detection.DictionaryDetector.StartsWithParticle(lower) is { } particle && lower.Length - particle.Length <= 2 &&
            _romaji.AnalyzeFragment(lower[particle.Length..]).IsValid) return false;
        // 同梱の辞書の英単語で、ローマ字としては促音 (っ) を使わないと読めない語 (issue = いっすえ, apple) は英語。
        // 日本語の語 (の先頭) なら除く。
        if (inDictionary && lower.Length >= 4 && _romaji.Analyze(lower) is { IsValid: true, Sokuon: > 0 } && !_japanese.IsPrefix(lower)) return true;
        // 2 文字でも、同梱の辞書の語で読めない英字がある (ok + notasuku の k) なら英語。
        if (unreadable && (exact || spellWord) && (lower.Length >= 3 || inDictionary)) return true;

        // 英語とも日本語とも読める語 (sushi, repo, make) は前後の両方で決める。前が英語なら +1・日本語なら -1、
        // 後ろも同じように数え、合計が必要な点数に届けば英語 (どちらも分からない・食い違うときは日本語)。
        // 入力の先頭で末尾まで打っている途中なら英単語の先頭でも数える (英文の続きを打っている途中を英字で見せる)。
        // 変換ボックス内の英語区間の続きは、偶然の一致 (google + de) を避けるため 3 文字以上の英単語そのものだけ。
        // 助詞などと同じ 2 文字の語 (no, to, ga) は、前後の両方が英語のときだけ (標準)。
        var ambiguous = level switch
        {
            // 積極的でも、助詞と同じ形の 2 文字 (ni, ga) は英単語の先頭というだけでは英語にしない。
            DetectionLevel.Aggressive => startOfInput && atEnd ? exact || spellWord || (!final && lower.Length >= 3 && _english.IsPrefix(lower)) : exact || spellWord,
            DetectionLevel.Conservative => (exact || spellWord) && (lower.Length >= 3 || before >= 2),
            _ => startOfInput && endsWord ? exact || spellWord :
                startOfInput && atEnd ? exact || spellWord || lower.Length == 1 || (!final && !smallKanaSpelling && _english.IsPrefix(lower)) : (exact || spellWord) && lower.Length >= 3,
        };
        var needed = level switch
        {
            DetectionLevel.Aggressive => 1,
            DetectionLevel.Conservative => 2,
            // 助詞と同じ形の 2 文字の語 (no, to, ga) は両側が必要。子音で終わる語 (is, at, my) は日本語の語にならないので片側でよい。
            _ => lower.Length <= 2 && _romaji.Analyze(lower) is { IsValid: true, Partial: "" or "n" } ? 2 : 1,
        };
        // スペルチェッカーだけが知っている、最後までローマ字として読める語 (shite, kore) は、前の英単語 1 つ (push) では足りない
        // (pushshite → pushして)。英文の続き (+2) か、前後の両方が英語のときだけ。
        if (spellWord && !inDictionary && _romaji.Analyze(lower) is { IsValid: true, Partial: "" }) needed = Math.Max(needed, 2);
        // 略語の形の辞書の語 (api) の後ろが の + 英単語 (apinoerror の no + error) なら、後ろは英語として数える
        // (後ろを常に日本語と数えると api が あぴ になる。要件 001)。日本語の語 (sore・are) と、略語でない語 (sake・take) は今までどおり。
        if (noThenEnglish && after == false && inDictionary && !_japanese.Words.ContainsWord(lower)) after = true;
        // 前が英文でも、後ろが日本語なら英文の強さは数えない (I love |sushi| が好き → 食い違うので日本語)。
        if (ambiguous && (after == false ? Math.Min(before, 1) : before) + Score(after) >= needed) return true;
        var analysis = _romaji.Analyze(lower);
        if (!exact && !prefix && !spellWord) return false;

        if (!analysis.IsValid)
        {
            if (!exact && !prefix) return false;
            // 日本語のすぐ後ろの短い英単語 (thin) が、変換ボックスの綴り (thi = てぃ) では最後まで読めて、続き (gu) とも読めるなら、
            // 外来語のカタカナ (hosu|thin|gu = ホスティング) を打っている途中。英単語にしない (ほすthinぐ になっていた。本家 #153、P45-2)。
            if (before < 0 && !atEnd && lower.Length <= 4 && next is { Length: > 0 } && char.IsAsciiLetter(next[0]) &&
                _romaji.AnalyzeFragment(lower) is { IsValid: true, Partial: "" or "n" } &&
                _romaji.AnalyzeFragment(lower + next.ToLowerInvariant()).IsValid)
            {
                return false;
            }
            if (!exact && smallKanaSpelling) return false;
            // 日本語のすぐ後ろの 2 文字の語で、変換ボックスでは読める綴り (こ + we = こうぇ、wi = うぃ) は日本語。
            if (smallKanaSpelling && lower.Length <= 2 && before < 0) return false;
            // ローマ字として読めない英単語。途中の区間は 3 文字以上だけ (短い語の偶然の一致を避ける)。
            return (atEnd && (exact || !conservative)) || span.Length >= 3;
        }
        // ローマ字として読めても、末尾が子音の英単語 (git, zoom, about) で、日本語の語の途中でもないなら英語。
        // スペルチェッカーだけが知っている語 (meeting, my) は、前が日本語でないときだけ。
        // 打ち終わっていれば、日本語の語の打ちかけ (it → itai) かどうかは気にしなくてよい。
        var notJapanesePrefix = final || !_japanese.IsPrefix(lower);
        if (spellWord && atEnd && before >= 0 && analysis.Partial.Length > 0 && analysis.Partial != "n" && notJapanesePrefix) return true;
        return atEnd && exact && (!conservative || lower.Length >= 3) && analysis.Partial.Length > 0 && analysis.Partial != "n" && notJapanesePrefix;
    }

    /// <summary>
    /// かな入力 (JIS) の区間が英語か。打ったキーの英字 (Raw) が英単語で、かなとしては日本語の語にならないなら英語。
    /// かなとしても日本語の語 (の先頭) になるなら、ローマ字入力の「英語とも日本語とも読める語」と同じく前後の文脈で決める。
    /// </summary>
    private bool IsEnglishSpanKana(string span, string kana, bool atEnd, int before, bool? after, DetectionLevel level, bool final = false)
    {
        if (span.Length == 0 || !span.All(char.IsAsciiLetter)) return false;
        var lower = span.ToLowerInvariant();
        var inDictionary = _english.Words.ContainsWord(lower) || (lower.Length >= 4 && _proper.Contains(lower));
        // キー列が偶然スペルチェッカーの語になることがあるので、スペルチェッカーの語は 4 文字以上だけ。
        var word = inDictionary || (lower.Length >= 4 && IsSpellWord(lower));
        var prefix = atEnd && !final && level == DetectionLevel.Aggressive && lower.Length >= 4 && _english.IsPrefix(lower);

        // Shift を押して打った大文字で始まる語は英語 (手動でも)。
        if (char.IsAsciiLetterUpper(span[0]) && (word || prefix || atEnd)) return true;
        if (level == DetectionLevel.Manual) return false;
        if (Memory?.Get(lower) is { } learned) return learned;
        if (!(word || prefix) || lower.Length < 2) return false;

        var japanese = _kana?.IsJapaneseWordOrPrefix(kana) == true;
        var context = (after == false ? Math.Min(before, 1) : before) + Score(after);
        if (context >= (level == DetectionLevel.Conservative ? 2 : 1)) return true;
        if (japanese || context < 0) return false;
        var minimum = level switch { DetectionLevel.Aggressive => 2, DetectionLevel.Conservative => 4, _ => 3 };
        return lower.Length >= minimum;
    }

    // 大文字で終わる略語と見なす頭の最小の長さ。3 文字以下 (McA・LeB) は名前の途中、4 文字以上 (PlanB・TypeA) は英単語 + 大文字 1 文字として略語に残す
    // (本家 yksr-melt/Meltype の 1640142 / issue #266 と同じ変更)
    private const int MinCamelAcronymLength = 4;

    // SNS の X の後ろでだけ助詞として見る、2 音以上の助詞 (Xnado → Xなど、Xkara → Xから)。
    // ほかの大文字 1 文字には広げない: 名前 (Ishikawa → Iしかわ、Imadegawa → Iまでがわ、Okara → Oから) を区切ってしまうため (査読 R32)。
    // や (Xya) は入れない。全体の助詞の表 (DictionaryDetector) は変えない。
    private static readonly string[] ParticlesAfterX = ["kara", "made", "nado", "nara", "nante", "yori", "dake", "shika", "sika", "bakari", "kurai", "gurai"];

    /// <summary>
    /// 大文字で終わる略語 (OCR、AutoIME、PlanB) か。McA・LeB のように、短い頭 (Mc・Le) の後ろの大文字で終わるものは、
    /// 略語ではなく名前 (McAfee・LeBron) の途中と見る (本家 issue #266)。
    /// </summary>
    private static bool EndsWithAcronym(string head) =>
        head.Length >= 2 && char.IsAsciiLetterUpper(head[^1]) && !(head.Length < MinCamelAcronymLength && char.IsAsciiLetterLower(head[^2]));

    /// <summary>
    /// 大文字で始まる語 (Shift を押して打った) の後ろに日本語が続いているなら、その語の終わり (単位の位置)。無ければ -1。
    /// 大文字で始まる区間は末尾まで英語になるので、そのままでは AutoIMEnotesuto → 全部英字 になってしまう。
    /// 語は、知っている英単語 (Github) か、大文字で終わる語 (AutoIME, OK, NHK)。後ろは 3 文字以上の小文字で、ローマ字として読めるもの。
    /// </summary>
    private int CapitalizedWordEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, bool final)
    {
        // 見るのは、次の記号・数字・空白まで (長い文の OCR|woshi, … では、後ろの , までの woshi を見る)
        var n = start;
        while (n < units.Count && units[n].Raw.All(char.IsAsciiLetter) && units[n].Raw.Length > 0) n++;
        if (n < units.Count) pending = "";
        var whole = Raw(units, start, n) + pending;
        // 2 文字目が大文字の語 (iPad、iPhone、eSports) も、知っている語なら同じように区切る (iPad|deii → iPad でいい)
        var lowerStart = whole.Length >= 2 && char.IsAsciiLetterLower(whole[0]) && char.IsAsciiLetterUpper(whole[1]);
        if (whole.Length == 0 || !char.IsAsciiLetterUpper(whole[0]) && !lowerStart) return -1;
        // 全体が英単語・固有名詞 (Tokyo, Github) なら区切らない。
        if (IsKnownCapitalizedWord(whole)) return -1;
        for (var k = n - 1; k > start; k--)
        {
            var head = Raw(units, start, k);
            if (!head.All(char.IsAsciiLetter)) continue;
            if (lowerStart && !(head.Length >= 3 && IsKnownCapitalizedWord(head))) continue;
            // 大文字の略語に小文字が続いた形 (AIde、AIni) は語ではない。略語 (AI) の後ろがローマ字 (dekiru) と見る (本家 #129、P45-10)
            if (IsAcronymWithLowerTail(head)) continue;
            // 後ろは小文字のローマ字 (長音の - を含んでもよい: TSyu-za- の yu-za-)。
            var rest = Raw(units, k, n) + pending;
            // 後ろが助詞 1 つだけ (OCR|wo、English|ga) なら 2 文字でもよい
            if ((rest.Length < 3 && Detection.DictionaryDetector.StartsWithParticle(rest) != rest) || !rest.All(c => char.IsAsciiLetterLower(c) || c == '-') || !char.IsAsciiLetterLower(rest[0])) continue;
            // 知っている英単語・略語 (English、OCR) の後ろが助詞で始まるなら、その後ろに英単語が続いても (English|wo|happy) 区切る
            var knownHead = EndsWithAcronym(head) || head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant());
            if (knownHead && Detection.DictionaryDetector.StartsWithParticle(rest) is not null) return k;
            var analysis = _romaji.AnalyzeFragment(rest.Replace("-", ""));
            if (!analysis.IsValid || (final && analysis.Partial.Length > 0 && analysis.Partial != "n")) continue;
            // 大文字で終わる略語 (OCR)、知っている語 (Tokyo)、スペルチェッカーの 4 文字以上の語 (English)
            if (EndsWithAcronym(head) || head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant())) return k;
            // 大文字 1 文字 + 助詞で始まるローマ字 (A|nisiyouka → Aにしようか、B|noan → Bの案)。
            // 名前 (Tanaka、Hanako) を区切らないよう、後ろが助詞で始まるときだけ。
            if (head.Length == 1 && char.IsAsciiLetterUpper(head[0]) && Detection.DictionaryDetector.StartsWithParticle(rest.Replace("-", "")) is not null) return k;
            // SNS の X の後ろだけは、2 音以上の助詞 (Xnado → Xなど、Xkara → Xから) でも区切る。
            if (head == "X" && ParticlesAfterX.FirstOrDefault(rest.Replace("-", "").StartsWith) is not null) return k;
        }
        // 後ろに大文字で始まる語が続く (Japanese|to|English…) と、後ろが小文字だけにならず上では区切れない。
        // 次の大文字の前までを見て、知っている語 + 助詞で始まるローマ字 (Japanese|to) なら区切る。
        var camel = start + 1;
        while (camel < n && !units[camel].Raw.Any(char.IsAsciiLetterUpper)) camel++;
        for (var k = camel - 1; camel < n && k > start; k--)
        {
            var head = Raw(units, start, k);
            var rest = Raw(units, k, camel);
            if (!rest.All(char.IsAsciiLetterLower) || Detection.DictionaryDetector.StartsWithParticle(rest) is null || !_romaji.AnalyzeFragment(rest).IsValid) continue;
            if (head.Length >= 3 && IsKnownCapitalizedWord(head) || head.Length >= 4 && IsSpellWord(head.ToLowerInvariant())) return k;
        }
        return -1;
    }

    /// <summary>
    /// 大文字 2 文字以上の後ろに小文字が続く (AIde・GPTni)。iOS・IDEs のような知っている書き方でなければ、
    /// 1 つの語ではなく、略語 + ローマ字の打ち始め。(本家 #129、P45-10)
    /// </summary>
    private bool IsAcronymWithLowerTail(string head)
    {
        var upper = 0;
        while (upper < head.Length && char.IsAsciiLetterUpper(head[upper])) upper++;
        return upper >= 2 && upper < head.Length && head[upper..].All(char.IsAsciiLetterLower) && !IsKnownCapitalizedWord(head);
    }

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)。接頭辞 + - + 3 文字以上の英単語なら英語。
    // 1 文字の母音 (o-bun = オーブン) は日本語の長音とまぎらわしいので e と x だけ。
    private static readonly HashSet<string> HyphenPrefixes = ["e", "x", "re", "co", "ex", "non", "anti", "semi", "multi", "pre", "sub", "post", "mid", "self", "well"];

    // 接頭辞の規則では拾えない、- の入ったよく使う英単語 (後ろが 2 文字以下など)
    private static readonly HashSet<string> HyphenatedWords = ["bio-break", "co-op", "re-do", "x-ray", "t-shirt", "wi-fi", "hi-fi", "lo-fi", "sci-fi", "e-book", "e-sports", "k-pop", "j-pop", "j-rock", "j-core", "p-hub", "talk-admin", "r-18", "sub-6", "gpt-6.7", "a-z", "u-turn", "check-in", "log-in", "sign-in", "add-on", "plug-in", "built-in", "follow-up", "set-up", "pop-up", "drop-down", "blu-ray"];
    // 日本語のローマ字の途中を英語の接頭辞と誤認しないよう、日本語に続けて拾うのは明示した英数字表記だけ。
    private static readonly HashSet<string> NumericHyphenatedWords = ["r-18", "sub-6", "gpt-6.7"];

    /// <summary>- を付けて使う英語の接頭辞か (e、re、co …)。英数状態で、- の後を見てから英語か決めるのに使う。</summary>
    public static bool IsHyphenPrefix(string lower) => HyphenPrefixes.Contains(lower);

    /// <summary>- の入った英単語か (e-mail、co-op、re-do、x-ray)。</summary>
    public bool IsHyphenatedEnglishWord(string lower)
    {
        if (HyphenatedWords.Contains(lower)) return true;
        var dash = lower.IndexOf('-');
        if (dash <= 0 || lower.IndexOf('-', dash + 1) >= 0) return false;
        var rest = lower[(dash + 1)..];
        return HyphenPrefixes.Contains(lower[..dash]) && rest.Length >= 3 && rest.All(char.IsAsciiLetterLower) && IsKnownEnglishWord(rest);
    }

    /// <summary>語の頭になる開きの記号 (`「『(（"'[) と、対応する閉じの記号。</summary>
    private static readonly Dictionary<string, string> WordOpeners = new()
    {
        ["`"] = "`", ["「"] = "」", ["『"] = "』", ["("] = ")", ["（"] = "）", ["\""] = "\"", ["'"] = "'", ["["] = "]",
    };

    /// <summary>打った 1 単位が空白か開きの記号なら、その次は語の頭。</summary>
    internal static bool IsWordBoundaryRaw(string raw) => raw.Length > 0 && (raw.All(char.IsWhiteSpace) || WordOpeners.ContainsKey(raw));

    /// <summary>
    /// キャレットの前の確定済みの文字列の次が語の頭か (保護区間を始めてよいか)。
    /// 前の文字が無い・分からない (null・空)・空白・開きの記号なら語の頭。それ以外 (日本語を確定した直後の /fuka) は語の途中。
    /// </summary>
    /// <param name="codeApp">コード系アプリか。そうなら、前の文字が ASCII (英字・記号) でも語の頭とみなす。末尾の ASCII 数字の並びはとばして、その前の文字で決める (sleep 10・v2・10 は語の頭、予算は10・第3 は語の途中)。日本語に当たる ASCII 以外の文字のあとは語の途中。</param>
    public static bool IsWordStartAfter(string? preceding, bool codeApp = false)
    {
        if (string.IsNullOrEmpty(preceding) || char.IsWhiteSpace(preceding[^1]) || WordOpeners.ContainsKey(preceding[^1].ToString())) return true;
        if (!codeApp) return false;
        var end = preceding.Length;
        while (end > 0 && char.IsAsciiDigit(preceding[end - 1])) end--;
        return end == 0 || char.IsWhiteSpace(preceding[end - 1]) || WordOpeners.ContainsKey(preceding[end - 1].ToString()) || char.IsAscii(preceding[end - 1]);
    }

    /// <summary>キャレットの前が改行のあとか (最後の改行 \n・\r・U+2028・U+2029・U+0085 より後ろが空白 (半角・タブ・全角) だけ。空も含む)。オプション (-name) の「直前が英語」は同じ行の英語だけ。</summary>
    public static bool IsNewlineAfter(string? preceding)
    {
        if (string.IsNullOrEmpty(preceding)) return false;
        for (var i = preceding.Length - 1; i >= 0; i--)
        {
            var c = preceding[i];
            if (c is '\n' or '\r' or '\u2028' or '\u2029' or '\u0085') return true;
            if (!char.IsWhiteSpace(c)) return false;
        }
        return false;
    }

    /// <summary>
    /// コード系アプリの語の頭で、打ち終わりに英字のままにするコマンド名 (K79。小文字・完全一致)。今の判定で、Space で変換が始まる・確定で日本語になるものだけを入れる。
    /// </summary>
    public static readonly HashSet<string> CommandNames = new(StringComparer.Ordinal)
    {
        // 英語と判定されず、Space で変換が始まる (cd・ls・rm・cp・df・sh・gh・rg・fd・jq・wc)・確定で日本語になる (ln → lん、du → づ、vi → ゔぃ、su → す、man → まん、make → まけ、
        // sudo → すど、open → おぺん、node → ので、go → ご、bun → ぶん、nano → なの、more → もれ、uv → うv、eza → えざ、env → えんv、unalias → うなぃas)
        "cd", "ls", "rm", "cp", "ln", "du", "df", "vi", "su", "sh", "man", "make", "sudo", "open", "node", "go", "bun", "nano", "more", "gh", "uv", "rg", "fd", "jq", "eza", "wc", "env", "unalias",
        // `--repro` で Space・Enter を試して崩れたもの (bg・fg・tr・bc は Space で変換が始まる。dd → っd、nc → んc、uname → うなめ、nohup → のふp、time → ちめ、date → だて、tee → てえ)
        "bg", "fg", "tr", "bc", "dd", "nc", "uname", "nohup", "time", "date", "tee",
        // 指示書で崩れると挙げられた。同梱の英単語の一覧では崩れない。原因は未確認 (echo → えちょ、code → こで)
        "echo", "code",
    };

    /// <summary>キャレットの前の文字が開きの記号 (`「『(（"'[) ならその記号。無ければ null (保護区間の範囲を閉じの記号までにするため)。</summary>
    public static string? OpenerBefore(string? preceding) =>
        !string.IsNullOrEmpty(preceding) && WordOpeners.ContainsKey(preceding[^1].ToString()) ? preceding[^1].ToString() : null;

    /// <summary>
    /// 保護区間 (1-4a): 語の頭 (入力の先頭か空白・開きの記号の後) から始まる パス・オプション・変数 の終わり (単位の位置)。無ければ -1。
    /// 入力の先頭 (start == 0) が語の頭かは startsWord で決める (直前に日本語を確定した直後の /fuka・/iie を守らないため)。
    /// ・パス: ./ ../ ~/ のあとか、/ のあとにパスの要素が続く (a/b・1/2・/// は守らない)。パスの判定は本家 1.2.1 の ProtectedSpanScanner.TryPosixPath
    ///   (GPL-3.0-or-later、Copyright (C) 2026 0924haruto12) と同じ規則。範囲は本家と違い、空白まで。
    /// ・オプション (Mac 独自): - + 英字は、ローマ字として読めない (-xzvf)・大文字を含む (-Wall)・1〜2 文字で終わる (-la)・直前が英語 (find . -name) のとき。
    ///   -- + 英字 (小説・台詞の「ーー」と同じ打ち方) も同じく絞る: = を含む (--color=auto)・- で区切った部分のどれかがローマ字として最後まで読めない (--no-install・--dry-run)・
    ///   最初の英字がローマ字として読めない (--help・--force)・大文字を含む・直前が英語のときだけ。--sorekara・--sate・--ko-hi-wo (長音の -) は守らない。
    /// ・変数 (Mac 独自): $ のあとが英字か { ($HOME、${PATH}。$100 は守らない)。
    /// 範囲は空白か入力の終わりまで。開きの記号の直後から始まったときは、対応する閉じの記号まで (`./build.sh` wo → `./build.sh` を)。
    /// ただしパス・変数の最後の要素が拡張子付きなら、<see cref="DotSuffixEnd"/> の終わりで止める
    /// (./build.shde → ./build.sh|で)。拡張子が無ければ空白まで英字のまま (/tmp/fooni)。
    /// </summary>
    private int ProtectedEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, bool final, bool startsWord, Func<int, bool> englishWordBefore, string? precedingOpener, bool precededByEnglish)
    {
        static bool IsSpace(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(char.IsWhiteSpace);
        var n = units.Count;
        if (units[start].Raw.Length == 0 || IsSpace(units[start]) || !(start == 0 ? startsWord : IsWordBoundaryRaw(units[start - 1].Raw))) return -1;
        var wordEnd = start;
        while (wordEnd < n && !IsSpace(units[wordEnd])) wordEnd++;
        // 開きの記号の直後なら、範囲は対応する閉じの記号まで (閉じの記号も含める)。閉じが無ければ空白まで。
        // 開きの記号が確定済みの文字の側にあるとき (start == 0) は、キャレットの前の文字から決める。
        // 確定済みの 「 に対応するのは、打った ] (日本語の 」)。確定済みの全角の （ に対応するのは、打った ) (閉じは半角の ) のまま範囲に入り、幅はそろわない。後ろの助詞は日本語に戻る)
        var closeAt = -1;
        var opener = start > 0 ? units[start - 1].Raw : precedingOpener;
        var closer = "";
        var hasCloser = opener is not null && (start == 0 && opener == "「" ? (closer = "]") is not null
            : start == 0 && opener == "（" ? (closer = ")") is not null
            : WordOpeners.TryGetValue(opener, out closer!));
        if (hasCloser)
        {
            for (var k = start; k < wordEnd; k++)
            {
                if (units[k].Raw != closer) continue;
                closeAt = k;
                wordEnd = k;
                break;
            }
        }
        // [ ] は、[ が日本語の区間にあれば「」になるので、閉じの ] は範囲に入れず、日本語の区間に残す (「~/Documents」を)。
        // 半角の [ が確定済みの側にあるとき (E の経路。打った [ は英語の後ろでも半角にならない) だけ、] も半角で範囲に入れる (see [~/Documents]を)。ほかの閉じの記号は半角のまま範囲に入れる
        var closerStaysJapanese = start == 0 ? opener == "「" : opener == "[" && !precededByEnglish;
        int Finish(int end) => closeAt >= 0 && end == closeAt && !closerStaysJapanese ? closeAt + 1 : end;
        var word = Raw(units, start, wordEnd) + (wordEnd == n ? pending : "");
        if (word.Length < 2) return -1;
        static bool ElementStart(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~';
        var afterHead = word.StartsWith("../", StringComparison.Ordinal) ? 3
            : word.StartsWith("./", StringComparison.Ordinal) || word.StartsWith("~/", StringComparison.Ordinal) ? 2
            : word[0] == '/' ? 1 : -1;
        var isPath = afterHead > 0 && afterHead < word.Length && ElementStart(word[afterHead]);
        var isVariable = word[0] == '$' && (char.IsAsciiLetter(word[1]) || word[1] == '{');
        var isOption = false;
        if (word[0] == '-' && char.IsAsciiLetter(word[1]) || word.StartsWith("--", StringComparison.Ordinal) && word.Length >= 3 && char.IsAsciiLetter(word[2]))
        {
            var dashes = word[1] == '-' ? 2 : 1;
            var letters = new string(word.Skip(dashes).TakeWhile(char.IsAsciiLetter).ToArray());
            var unreadable = !_romaji.AnalyzeFragment(letters.ToLowerInvariant()).IsValid;
            if (dashes == 2)
            {
                // -- は小説・台詞の「ーー」(--sorekara) にも使うので、- と同じく絞る。--name のようなローマ字として読めるオプションは英語の後ろでだけ守る。
                // 長音の - (--ko-hi-wo・--ra-men) は日本語なので、- で区切った部分が全部ローマ字として「最後まで」読めるなら守らない。
                // 最後まで読める = { IsValid, Partial が "" か n (語末の n は ん) }。語末が n 以外の子音 (--no-edit の t・--ff-only の ly・--all の l) で終わる日本語は無いので、守る。
                // - の前の部分はいつもこの条件。最後の部分は、打ち終わり (final か後ろが空白) のときだけこの条件にする
                // (打っている途中は IsValid だけ。「ーー」を打つ途中で子音ごとに英字と日本語が入れ替わらないため。--no-edit は打っている間は日本語に見え、確定で英字になる)。= と大文字を含むものは守る
                var complete = final || wordEnd < n;
                var parts = word[2..].Split('-', StringSplitOptions.RemoveEmptyEntries);
                bool Unreadable(string part, bool isLast)
                {
                    var analysis = _romaji.AnalyzeFragment(part.ToLowerInvariant());
                    return !analysis.IsValid || (!isLast || complete) && analysis.Partial is { Length: > 0 } and not "n";
                }
                var hasUnreadablePart = word.Length > 3 && word.IndexOf('-', 2) >= 0 && parts.Select((part, i) => Unreadable(part, i == parts.Length - 1)).Any(bad => bad);
                var firstUnreadable = Unreadable(letters, letters.Length == word.Length - 2);
                isOption = word.IndexOf('=', 2) >= 0 || hasUnreadablePart || firstUnreadable || word.Skip(2).Any(char.IsAsciiLetterUpper) || englishWordBefore(start);
            }
            else
            {
                isOption = unreadable || letters.Any(char.IsAsciiLetterUpper)
                    || letters.Length <= 2 && letters.Length == word.Length - 1 || englishWordBefore(start);
            }
        }
        if (!isPath && !isVariable && !isOption) return -1;
        if (isOption) return Finish(wordEnd);
        // 最後の要素が拡張子付き (最初の . が要素の先頭ではない) なら、拡張子の終わりまで。後ろの助詞は今までどおり判定する。
        var elementStart = word.LastIndexOf('/') + 1;
        if (elementStart + 1 >= word.Length) return Finish(wordEnd);
        var dot = word.IndexOf('.', elementStart + 1);
        if (dot < 0 || dot + 1 >= word.Length) return Finish(wordEnd);
        var offset = 0;
        for (var k = start; k < wordEnd; k++)
        {
            if (offset == dot && units[k].Raw == ".")
            {
                var end = k + 1 < n ? DotSuffixEnd(units, k + 1, pending, final) : -1;
                return Finish(end > k + 1 && end <= wordEnd ? end : wordEnd);
            }
            offset += units[k].Raw.Length;
        }
        return Finish(wordEnd);
    }

    /// <summary>
    /// start から始まるユーザー名の終わり。無ければ -1。
    /// @ の後ろ (Discord・X のメンション @kuraido) と、_ の入った語 (upah_setu、cafely_latte) は、ローマ字として読めても英字のまま。
    /// 英字・数字・_ が続く所までがユーザー名 (@ の後ろは、メールアドレスのドメインの . - も含める)。
    /// </summary>
    private static int UserNameEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        static bool IsNameUnit(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        var n = units.Count;
        if (!IsNameUnit(units[start]) || start > 0 && IsNameUnit(units[start - 1])) return -1;
        var mention = start > 0 && units[start - 1].Raw == "@";
        var end = start;
        while (end < n && IsNameUnit(units[end])) end++;
        // メールアドレスのドメイン (taro@gmail.com) の . - も続けて英字に。
        while (mention && end + 1 < n && units[end].Raw is "." or "-" && IsNameUnit(units[end + 1]))
        {
            end++;
            while (end < n && IsNameUnit(units[end])) end++;
        }
        var name = Raw(units, start, end) + (end == n ? pending : "");
        if (!name.Any(char.IsAsciiLetter)) return -1;
        if (mention || name.Contains('_')) return end;
        // メールアドレスの @ より前 (tanaka@、yamada.taro@): @ を打ったら、その前も英字のまま。
        // @ の後ろのドメインは、上の @ の後ろの決まりで英字になる (たなか@gmail.com になっていた。本家 #59、P45-7)。
        if (start == 0 || units[start - 1].Raw is " " or "<" or "(" or "\"" or "'" or ":" or ",")
        {
            var local = end;
            while (local + 1 < n && units[local].Raw is "." or "-" or "+" && IsNameUnit(units[local + 1]))
            {
                local++;
                while (local < n && IsNameUnit(units[local])) local++;
            }
            // @ の後ろに英字が続いたとき (ドメインを打ち始めた) だけ。あと@3人 (ato@3nin) のような @ は日本語のまま
            var domainStarts = local + 1 < n ? units[local + 1].Raw is [var first, ..] && char.IsAsciiLetter(first) : local + 1 == n && pending is [var p, ..] && char.IsAsciiLetter(p);
            if (local < n && units[local].Raw == "@" && domainStarts) return local + 1;
        }
        return -1;
    }

    /// <summary>
    /// 英語の語のすぐ後ろの . に続く、ドメイン・拡張子 (tetr.io、MeltypeTip.dll、abi.json、go.mod) の終わり。無ければ -1 (issue #260)。
    /// ドメインは語全体が一致するときだけ (config の co、index の in を英字にして、残りをローマ字にしていた)。
    /// 拡張子は、ローマ字として読めない (dll、aab)・数字や . / を含む (e57、go.mod)・よく使う拡張子 (ace、ini) なら英字のまま。
    /// 最後までローマ字として読める語 (Meltype.desu) は、今までどおり判定する。後ろの助詞からは日本語 (MeltypeTip.dll|wokesu)。
    /// </summary>
    private int DotSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, bool final)
    {
        static bool IsPart(CompositionUnit unit) => unit.Raw.Length > 0 && unit.Raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '+');
        var n = units.Count;
        if (start >= n || !IsPart(units[start])) return -1;
        var end = start;
        while (end < n && IsPart(units[end])) end++;
        // . / - で続く所まで 1 つの名前 (abi.json、go.work.sum、cargo/config.toml、db-journal)
        while (end + 1 < n && units[end].Raw is "." or "/" or "-" && IsPart(units[end + 1]))
        {
            end++;
            while (end < n && IsPart(units[end])) end++;
        }
        var name = Raw(units, start, end) + (end == n ? pending : "");
        if (!name.Any(char.IsAsciiLetter)) return -1;
        var lower = name.ToLowerInvariant();
        var growing = end == n && !final;
        static bool Known(string label) => DomainSuffixes.Contains(label) || FileExtensions.Contains(label);
        var lastLabel = lower[(lower.LastIndexOfAny(['.', '/']) + 1)..];
        // 打っている途中も、最後の子音までかなになるときだけ読めるとみなす (Meltype.bas を ばs と表示すると、確定で打ち間違いとして ば に直される)
        bool Readable(string text) => _romaji.Analyze(text) is { IsValid: true, Partial: "" or "n" };
        // 「っ」で始まる語 (tte・ppoi) も読める (ローマ字の解析は先頭の子音の重なりを読めないとする)
        bool ReadableJ(string text) => Readable(text) || text.Length >= 3 && text[0] == text[1] && char.IsAsciiLetter(text[0]) && text[0] is not ('a' or 'e' or 'i' or 'o' or 'u' or 'n') && Readable(text[1..]);
        // 拡張子の一覧にあっても、日本語としてよく打つ綴り (OK.ii yo、etc.mo、Hello.ko、rei) は英字に決めない (Mac 版の修正)
        bool Forced(string label) => DomainSuffixes.Contains(label) || FileExtensions.Contains(label) && !(label.Length <= 2 || label == "rei");
        if (Forced(lower) || Forced(lastLabel)) return end;
        // ドメイン・拡張子の打ちかけ (tetr.i)
        // 打っている途中の「っ」(OK.tt → OK.っt) は、英字に決めない (英字にすると っ の単位にならない)
        if (growing && lastLabel.Length >= 2 && lastLabel[^1] == lastLabel[^2] && lastLabel[^1] is not ('a' or 'e' or 'i' or 'o' or 'u' or 'n') && char.IsAsciiLetter(lastLabel[^1])
            && Readable(lastLabel[..^2] + lastLabel[^1] + "a")) return -1;
        if (growing && DomainSuffixes.Concat(FileExtensions).Any(s => s.Length >= 3 && s.StartsWith(lastLabel, StringComparison.Ordinal))) return end;
        // 最後のラベル (co.jpde なら jpde) の先頭の単位
        var labelStart = start;
        for (var q = end - 1; q >= start; q--)
        {
            if (units[q].Raw is "." or "/") { labelStart = q + 1; break; }
        }
        // 「っ」で始まり、単位がすべてかなになっている語 (tte = っ + て、ppoi) も読める語とみなす (ローマ字の解析は先頭の tt を読めないとする)
        var allKana = labelStart < end && units[labelStart].Kana == "っ" && (end < n || pending.Length == 0)
            && Enumerable.Range(labelStart, end - labelStart).All(q => units[q].Kana != units[q].Raw && units[q].Raw.All(char.IsAsciiLetter));
        var readable = Readable(lastLabel.Replace("-", "")) || Readable(lastLabel) || allKana;
        string RestFrom(int k) => (Raw(units, k, end) + (end == n ? pending : "")).ToLowerInvariant();
        // 助詞の後ろが大文字の略語・数字で始まる (mp4|wo|MP3ni、Next.js|no|SSR)
        bool UpperOrDigitStart(int k, int particleLength)
        {
            var raw = Raw(units, k, end) + (end == n ? pending : "");
            return raw.Length > particleLength + 1 && (char.IsAsciiLetterUpper(raw[particleLength]) && char.IsAsciiLetterUpper(raw[particleLength + 1]) || char.IsAsciiDigit(raw[particleLength]));
        }
        // ラベルが頭より長い英単語で始まる (doc|tored の doctor、tar|get の target) か
        bool LongerEnglishPrefix(int stemLength)
        {
            var label = RestFrom(labelStart);
            for (var j = Math.Min(label.Length, 24); j > stemLength + 1; j--)
                if (j >= 4 && label[..j].All(char.IsAsciiLetter) && IsKnownEnglishWord(label[..j])) return true;
            return false;
        }
        // 最後のラベルがそれ自体ふつうの英単語 (video・storage・organize) なら、途中で分けない (vi|deo を vi|で|お にしない)
        var labelIsEnglishWord = lastLabel.Length >= 4 && lastLabel.All(char.IsAsciiLetter) && IsKnownEnglishWord(lastLabel);
        // 拡張子・ドメイン・英単語の後ろが助詞で始まる日本語なら、そこまで (Mac 版の修正: 本家の PR #267 のままだと、名前を丸ごと英字に
        // 決めるので、今まで日本語にできていた助詞がローマ字のまま残った: mp4wokesu、co.jpde、readme.mdwo、plistwokesu、systemwo)
        if (!labelIsEnglishWord && !readable)
        {
            // 0. 一覧にある拡張子・ドメインの頭を先に、長いものから (com|made|kesu を comma|de・co|mmade と、md|dayo を m|ddayo と分けない)
            //   0a. 助詞が続く (m|kara|kesu を mka|rakesu としない)
            for (var k = end - 1; k > labelStart; k--)
            {
                var stem = Raw(units, labelStart, k).ToLowerInvariant();
                if (stem.Length > 8 || !Known(stem) && !SplitStems.Contains(stem)) continue;
                var rest = RestFrom(k);
                if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
                var after = rest[particle.Length..];
                // made だけ: ドメイン (github.com|made) は英字のまま、拡張子は今までどおり判定する
                if (particle == "made" && after.Length == 0) return DomainSuffixes.Contains(stem) ? end : -1;
                if ((stem + particle).Length >= 4 && IsKnownEnglishWord(stem + particle)) continue;
                if (after.Length == 0 || Readable(after) || UpperOrDigitStart(k, particle.Length) || after.Length >= 3 && IsKnownEnglishWord(after) && (stem.Length >= 3 || SplitStems.Contains(stem) || after.Length >= 4)) return k;
            }
            // 1. 長い英単語の頭を先に探す (golden|ga を gol|de|nga、extension|ganai を ex|tension と分けない)。0b より先に
            for (var k = end - 1; k > labelStart; k--)
            {
                var stem = Raw(units, labelStart, k).ToLowerInvariant();
                if (stem.Length < 4 || stem.Length > 24 || !stem.All(char.IsAsciiLetter) || !IsKnownEnglishWord(stem)) continue;
                var rest = RestFrom(k);
                // 助詞以外の日本語 (export|ttara、documentation|dayo): 4 文字以上の読める語で、英単語でないもの (user|name・data|base は分けない)
                if ((rest.Length >= 4 || rest.Length == 3 && rest[0] == rest[1]) && ReadableJ(rest) && !IsKnownEnglishWord(rest) && !TrailingParticles.Any(p => rest.StartsWith(p, StringComparison.Ordinal))) return k;
                if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
                var after = rest[particle.Length..];
                if (after.Length == 0 || ParticleTails.Contains(after) || after.Length >= 2 && Readable(after) || after.Length >= 3 && IsKnownEnglishWord(after)) return k;
            }
            //   0b. 助詞以外の日本語が続く (README.md|dayo・くらい・っぽい、github.com|janai)。3 文字以上の読める日本語は何でも、
            //   2 文字は一覧の短い頭だけ。頭が 2 文字以下なら、よく使う拡張子 (md・ts・js) のときだけ (mka・usda を m|か・us|だ にしない)
            for (var k = end - 1; k > labelStart; k--)
            {
                var stem = Raw(units, labelStart, k).ToLowerInvariant();
                if (stem.Length > 8 || !Known(stem) && !SplitStems.Contains(stem)) continue;
                var rest = RestFrom(k);
                var shortOk = stem.Length >= 3 || stem.Length == 2 && SplitStems.Contains(stem);
                // 頭より長い英単語で始まる (tar|getttara の target) なら分けない
                if (LongerEnglishPrefix(stem.Length)) continue;
                if (rest.Length >= 4 && ReadableJ(rest) || rest.Length == 3 && shortOk && ReadableJ(rest)
                    || rest.Length == 2 && shortOk && Readable(rest) && FollowWordsShort.Contains(rest)) return k;
            }
            // 数字の後ろが読める日本語 (No.1.1made の 1|made、5kaime) なら、数字入りの頭 (1ma|de) では分けない
            var digitLead = lastLabel.TakeWhile(char.IsAsciiDigit).Count();
            var digitThenJapanese = digitLead > 0 && lastLabel.Length - digitLead >= 2 && Readable(lastLabel[digitLead..]);
            // 2. 短い頭から
            for (var k = labelStart + 1; k < end; k++)
            {
                var stem = Raw(units, labelStart, k).ToLowerInvariant();
                if (stem.Length > 24) break;
                var rest = RestFrom(k);
                // 13 文字以上の長い名前 (preventDefault|to) は、助詞だけが続くときだけ
                if (stem.Length > 12)
                {
                    if (stem.All(char.IsAsciiLetter) && !Readable(stem) && TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is { } longParticle
                        && (rest.Length == longParticle.Length || rest.Length - longParticle.Length >= 3 && Readable(rest[longParticle.Length..]))) return k;
                    continue;
                }
                // 一覧に無い読めない名前の後ろに、「っ」で始まる 4 文字以上の読める語 (pub|ttara、xyz|kke)
                //   「っ」の後ろが助詞なら、名前の最後の文字 + 助詞 (plis|ttokesu ではなく plist|to|kesu)
                if (stem.Length >= 2 && stem.All(char.IsAsciiLetter) && !Readable(stem) && rest.Length >= 3 && rest[0] == rest[1] && ReadableJ(rest) && !Readable(rest)
                    && !TrailingParticles.Any(p => rest[1..].StartsWith(p, StringComparison.Ordinal)) && !LongerEnglishPrefix(stem.Length)) return k;
                // 一覧に無い読めない名前の後ろに、助詞以外の日本語の長い頭が続く (plist|dayo、notes|janai)
                if (stem.Length >= 2 && stem.All(char.IsAsciiLetter) && !Readable(stem) && rest.Length >= 3 && ReadableJ(rest)
                    && FollowWordsLong.Any(w => rest.StartsWith(w, StringComparison.Ordinal)) && !IsKnownEnglishWord(stem + rest[..Math.Min(rest.Length, 3)])) return k;
                if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
                var afterParticle = rest[particle.Length..];
                // 助詞の made だけが続くときは、英単語の made と区別できないので今までどおり判定する (dwg|made)
                if (particle == "made" && afterParticle.Length == 0) return -1;
                // 1 文字の頭 (s|no、v|wo|kesu、s|ni|push): 助詞だけか、助詞の後ろが 3 文字以上の読める語・4 文字以上の英単語のとき。
                // 助詞 to (英単語が続くときは mo も) は分けない (s|to|rage、s|mo|king の英単語と区別できない)
                if (stem.Length == 1 && char.IsAsciiLetter(stem[0]) && stem[0] is not ('a' or 'e' or 'i' or 'o' or 'u')
                    && (afterParticle.Length == 0 || ParticleTails.Contains(afterParticle) || particle != "to" && afterParticle.Length >= 3 && Readable(afterParticle)
                        || particle is not ("to" or "mo") && afterParticle.Length >= 4 && IsKnownEnglishWord(afterParticle))) return k;
                var known = Known(stem) || SplitStems.Contains(stem);
                // 数字入りの拡張子 (mp4・7z・h264)。数字だけ (No.1.1made の 1) や - の入った頭 (1-3set|to) は除く
                var stemLike = known || !digitThenJapanese && stem.Any(char.IsAsciiDigit) && stem.Any(char.IsAsciiLetter) && stem.All(char.IsAsciiLetterOrDigit) && stem.Length <= 8;
                // 英単語が続くのは、ドメイン・拡張子が 3 文字以上か、よく使う拡張子のときだけ (au|to|mount にしない)
                var englishAfter = afterParticle.Length >= 3 && IsKnownEnglishWord(afterParticle) && (stem.Length >= 3 || SplitStems.Contains(stem) || stem.Any(char.IsAsciiDigit));
                if (stemLike && !LongerEnglishPrefix(stem.Length) && (afterParticle.Length == 0 || Readable(afterParticle) || englishAfter || UpperOrDigitStart(k, particle.Length))) return k;
                // ドメイン・拡張子の後ろが、助詞で始まる日本語の語 (jp|nikki)
                if (known && rest.Length >= 4 && Readable(rest)) return k;
                // 一覧に無い読めない頭 + 助詞 (plist|wo|kesu、ahk|de|wa、db|wo、id|ga、lib|ni|push)。英単語の途中で分けないように:
                //   頭は読めない語 (ローマ字として読める頭は、ふつうの判定に任せる)。頭 + 助詞が英単語の頭 (pho|to = photo) なら分けない
                //   助詞の後ろが読める語は、3 文字以上か、頭に母音が無いとき (gitig|no|re・car|to|on は分けない)。英単語が続くのは頭が 3 文字以上のとき
                if (stem.Length < 2 || !stem.All(char.IsAsciiLetter) || Readable(stem)) continue;
                if ((stem + particle).Length >= 4 && IsKnownEnglishWord(stem + particle)) continue;
                var hasVowel = stem.Any(c => c is 'a' or 'e' or 'i' or 'o' or 'u');
                var ok = afterParticle.Length == 0 || ParticleTails.Contains(afterParticle)
                    || Readable(afterParticle) && (afterParticle.Length >= 3 || !hasVowel)
                    || (stem.Length >= 3 || !hasVowel) && afterParticle.Length >= 3 && IsKnownEnglishWord(afterParticle);
                if (ok) return k;
            }
        }
        // ラベル全体が読める語は、ドメインの後ろの助詞だけ分ける (tetr.io|de、github.com|ni|push。Meltype.desu は今までどおり)
        if (!labelIsEnglishWord && readable)
        {
            for (var k = labelStart + 1; k < end; k++)
            {
                var stem = Raw(units, labelStart, k).ToLowerInvariant();
                if (stem.Length > 6) break;
                if (!DomainSuffixes.Contains(stem)) continue;
                var rest = RestFrom(k);
                if (rest.Length >= 3 && rest[0] == rest[1] && ReadableJ(rest) && !Readable(rest)) return k;
                if (TrailingParticles.FirstOrDefault(p => rest.StartsWith(p, StringComparison.Ordinal)) is not { } particle) continue;
                var after = rest[particle.Length..];
                if (after.Length == 0 || Readable(after) || stem.Length >= 3 && after.Length >= 3 && IsKnownEnglishWord(after)) return k;
            }
        }
        // 日本語の語の打ち間違い (wakarimashta・sumimasne・gomne) は、今までどおり判定する (変更前は確定で直していた)
        if (!labelIsEnglishWord && !readable && lastLabel.All(char.IsAsciiLetter) && lastLabel.Length >= TypoDetector.MinLength)
        {
            var typo = new List<Contribution>();
            _typo.Evaluate(lastLabel, typo);
            if (typo.Count > 0) return -1;
        }
        if (lower.Any(c => char.IsAsciiDigit(c) || c is '.' or '/' or '_' or '+'))
        {
            // 最後のラベルが、数字の後ろの日本語 (No.5kaime、No.1.2bai、Vol.1-3setto) や、1 文字の略語の後ろの日本語 (A.I.nohanashi、A.I.ha) なら、
            // 英字に決めず今までどおり判定する。URL・パス (github.com/foo)・ファイル名 (a.b.json) は、今までどおり英字のまま
            if (!lower.Any(c => c is '/' or '_' or '+') && !Known(lastLabel))
            {
                var tail = new string(lastLabel.SkipWhile(c => char.IsAsciiDigit(c) || c is '-' or '~').ToArray());
                var lead = lastLabel[..^tail.Length];
                var tailLetters = tail.Replace("-", "");
                if (tailLetters.Length >= 2 && tail.Length > 0 && char.IsAsciiLetter(tail[0]) && tail.All(c => char.IsAsciiLetter(c) || c == '-') && ReadableJ(tailLetters))
                {
                    if (lead.Any(char.IsAsciiDigit)) return -1;
                    var labels = lower.Split('.');
                    if (lead.Length == 0 && labels.Length >= 2 && labels[..^1].All(l => l.Length == 1 && char.IsAsciiLetter(l[0]))) return -1;
                }
            }
            return end;
        }
        // 最後までローマ字として読める語 (Meltype.desu) は、ふつうに判定する
        return readable ? -1 : end;
    }

    /// <summary>
    /// start から始まる - の入った英単語 (e-mail、co-op) の終わり。無ければ -1。
    /// 後ろに日本語が続いてもよい (e-mail|de) ので、- の後ろは長い方から英単語になる所を探す。
    /// </summary>
    private int HyphenatedWordEnd(IReadOnlyList<CompositionUnit> units, int start, string pending)
    {
        var n = units.Count;
        // 日本語を打っている最中のローマ字列では、明示した英数字表記だけを英語として切り出す。
        var afterJapaneseRomaji = start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter) &&
                                  units[start - 1].Kana != units[start - 1].Raw;
        if (start > 0 && units[start - 1].Raw.Length > 0 && units[start - 1].Raw.All(char.IsAsciiLetter) && !afterJapaneseRomaji) return -1;
        var dash = start;
        while (dash < n && units[dash].Raw.Length > 0 && units[dash].Raw.All(char.IsAsciiLetter)) dash++;
        if (dash == start || dash >= n || units[dash].Raw != "-") return -1;
        for (var end = n; end > dash + 1; end--)
        {
            var word = (Raw(units, start, end) + (end == n ? pending : "")).ToLowerInvariant();
            if (afterJapaneseRomaji ? NumericHyphenatedWords.Contains(word) : IsHyphenatedEnglishWord(word)) return end;
        }
        return -1;
    }

    /// <summary>英語の短縮形 (don't, it's, I'm, you're, we'll, can't)。' の前が英単語か n't の形。</summary>
    private bool IsContraction(string span)
    {
        var apostrophe = span.IndexOf('\'');
        if (apostrophe <= 0 || apostrophe != span.LastIndexOf('\'')) return false;
        var stem = span[..apostrophe].ToLowerInvariant();
        var suffix = span[(apostrophe + 1)..].ToLowerInvariant();
        // 語の最後の g を ' にした書き方 (swingin' = swinging、rockin'): stem + g が英単語なら英語
        if (suffix.Length == 0 && stem.Length >= 3 && stem.EndsWith("in", StringComparison.Ordinal) && stem.All(char.IsAsciiLetterLower)) return IsKnownEnglishWord(stem + "g");
        if (!stem.All(char.IsAsciiLetterLower) || suffix is not ("t" or "s" or "re" or "ve" or "ll" or "d" or "m")) return false;
        if (suffix == "t") return stem.Length >= 2 && stem[^1] == 'n';
        return stem == "i" || _english.Words.ContainsWord(stem) || _proper.Contains(stem);
    }

    /// <summary>ちょうど助詞 1 つ (de・mo・to・no …、DictionaryDetector.StartsWithParticle の表) か。</summary>
    private static bool IsParticle(string raw) => Detection.DictionaryDetector.StartsWithParticle(raw) is { } particle && particle.Length == raw.Length;

    /// <summary>ローマ字として読めない、5 文字以上の英単語 (alcoholic, pressure)。</summary>
    private bool IsLongEnglishWord(string raw)
    {
        if (raw.Length < 5 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        if (_romaji.Analyze(lower).IsValid) return false;
        return _english.Words.ContainsWord(lower) || _proper.Contains(lower) || IsSpellWord(lower);
    }

    /// <summary>助詞の後ろに続く短い助詞 (de|wa、to|mo、ni|ha)。拡張子の後ろの助詞の重なりとして扱う。</summary>
    private static readonly HashSet<string> ParticleTails = ["wa", "ha", "mo", "no", "ni", "de", "to", "ga", "wo", "ne", "yo", "made", "kara"];

    /// <summary>
    /// 拡張子・ドメインの後ろに直接続く、助詞以外の日本語の頭 (README.md|dayo、github.com|janai、Node.js|nara)。
    /// 一覧にある拡張子・ドメインの後ろでは、3 文字以上の読める日本語なら何でも分け、2 文字ならこの短い一覧のものだけ。一覧に無い名前の後ろでは長いものだけを使う (pho|ne = phone のような英単語の途中を分けないため)。
    /// </summary>
    private static readonly string[] FollowWordsShort = ["da", "ka", "ne", "yo", "he", "ya", "sa", "na", "tte", "tto", "kke", "jan"];
    private static readonly string[] FollowWordsLong = ["dayo", "dane", "dakara", "dake", "desu", "deshita", "janai", "jaa", "nara", "nanode", "nano", "nanoni", "shika", "mitai", "ppoi", "rashii", "kamo", "kana", "toka", "nado", "tte", "datta", "kurai", "gurai", "koso", "sura", "bakari", "nomi", "goto", "zenbu", "igai", "ijou", "ika"];

    /// <summary>
    /// の の後ろの打鍵が、英字のまま残る 3 文字以上の英単語だけでできているか (apinoerror の error)。同梱の一覧の語だけを見る (スペルチェッカーは使わない)。
    /// 最後までローマ字として読める語 (hero・game・time) は後ろもかなになるので数えない (animenohero の前だけ英字になっていた)。
    /// 読めない英字が残る語 (error の最後の r、update の p) だけを数える。
    /// </summary>
    private bool IsEnglishWordAfterNo(string rest)
    {
        var lower = rest.ToLowerInvariant();
        return lower.Length >= 3 && lower.AsSpan().IndexOfAnyExceptInRange('a', 'z') < 0 && IsBundledEnglishWord(lower) &&
            _romaji.Analyze(lower) is not { IsValid: true, Partial: "" };
    }

    /// <summary>
    /// 英字 1 文字ずつ読む略語か (api = えーぴーあい、url、cpu)。loanwords.txt (JMdict) で大文字だけで書き、読みが英字の名前を並べたものになっている語。
    /// 大文字で書いても語として読む略語 (NASA = なさ、TEN = てん) や、日本由来の語 (sake)・英単語 (take) は含まない。
    /// </summary>
    private static bool IsSpelledAcronym(string lower) => SpelledAcronyms.Value.Contains(lower);

    private static readonly Lazy<HashSet<string>> SpelledAcronyms = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in DictionarySource.ReadEmbedded("loanwords.txt").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var p = 1; p < parts.Length; p++)
            {
                if (parts[p].Length >= 2 && parts[p].All(char.IsAsciiLetterUpper) && IsLetterNames(parts[p], parts[0])) words.Add(parts[p].ToLowerInvariant());
            }
        }
        return words;
    });

    /// <summary>読み (ひらがな) が、英字 (大文字) の名前を 1 文字ずつ並べたものか (API と えーぴーあい)。</summary>
    private static bool IsLetterNames(string letters, string reading)
    {
        if (letters.Length == 0) return reading.Length == 0;
        foreach (var name in LetterNames[letters[0] - 'A'])
        {
            if (reading.StartsWith(name, StringComparison.Ordinal) && IsLetterNames(letters[1..], reading[name.Length..])) return true;
        }
        return false;
    }

    // 英字の名前 (JMdict の読みに出る書き方)。A から Z の順
    private static readonly string[][] LetterNames =
    [
        ["えー", "えい"], ["びー"], ["しー"], ["でぃー", "でー"], ["いー"], ["えふ"], ["じー"], ["えいち", "えっち"], ["あい"], ["じぇー", "じぇい"],
        ["けー", "けい"], ["える"], ["えむ"], ["えぬ"], ["おー"], ["ぴー"], ["きゅー"], ["あーる"], ["えす"], ["てぃー", "てー"],
        ["ゆー"], ["ぶい", "ぶいー", "ゔぃー"], ["だぶりゅー", "だぶる"], ["えっくす"], ["わい"], ["ぜっと", "ぜっど", "ずぃー"],
    ];

    private static readonly string[] TrailingParticles = ["kara", "made", "yori", "ga", "wo", "ni", "de", "no", "to", "mo", "ha", "wa"];

    /// <summary>
    /// 知らない英字の語 + 助詞 (grokga = grok + が) か。語の部分は 3 文字以上でローマ字として読めないもの、全体は辞書に無いもの。
    /// 語の最後の子音と助詞の頭が同じ (lot + to = ろっと) なら っ の綴りなので除く。
    /// </summary>
    private bool EndsWithParticleAfterUnknownWord(string lower)
    {
        if (_english.Words.ContainsWord(lower) || _proper.Contains(lower) || Memory?.Get(lower) == true || IsSpellWord(lower)) return false;
        foreach (var particle in TrailingParticles)
        {
            if (!lower.EndsWith(particle, StringComparison.Ordinal)) continue;
            var stem = lower[..^particle.Length];
            if (stem.Length < 3 || stem[^1] == particle[0]) return false;
            return !_romaji.Analyze(stem).IsValid;
        }
        return false;
    }

    private bool IsKnownCapitalizedWord(string word)
    {
        var lower = word.ToLowerInvariant();
        return Memory?.Get(lower) == true || _english.Words.ContainsWord(lower) || _proper.Contains(lower);
    }

    /// <summary>最初の 3 文字以内でローマ字として読めなくなる、4 文字以上の語 (日本語の打ち間違いでもないもの)。</summary>
    private bool IsUnknownEnglishWord(string raw)
    {
        if (raw.Length < 4 || !raw.All(char.IsAsciiLetter)) return false;
        var lower = raw.ToLowerInvariant();
        var analysis = _romaji.Analyze(lower);
        if (analysis.IsValid) return false;
        // 小書き文字などの綴り (kaxnji, ulo) まで含めれば読めるなら、日本語を打っている。
        if (_romaji.AnalyzeFragment(lower).IsValid) return false;
        var firstInvalid = analysis.Tokens.Sum(t => t.Romaji.Length);
        if (firstInvalid >= 3) return false;
        // 読めない文字の後ろが普通のローマ字なら、英字 1 文字 + 日本語 (sだけが, Xがわかる) を打っている。
        var rest = lower[(firstInvalid + 1)..];
        if (rest.Length >= 3 && _romaji.AnalyzeFragment(rest).IsValid) return false;
        var typo = new List<Contribution>();
        _typo.Evaluate(lower, typo);
        return typo.Count == 0;
    }

    private static CompositionSegment Japanese(IReadOnlyList<CompositionUnit> units, int start, int end, string pending)
    {
        var kana = string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Kana));
        return new CompositionSegment(false, kana, Raw(units, start, end) + pending);
    }

    /// <summary>単位 [start, end) に、ローマ字として読めなかった英字 (かなにならなかった 1 文字) があるか。</summary>
    /// <summary>
    /// 英語の区間 + 数字のすぐ後ろ (part1026|beta) から始まる英字の並びが英単語なら、その終わり。違えば -1。
    /// segments・japaneseStart は FindSpans の途中の状態 (数字が、直前の英語の区間のすぐ後ろにあるかを見る)。
    /// </summary>
    private int AlphanumericSuffixEnd(IReadOnlyList<CompositionUnit> units, int start, string pending, List<CompositionSegment> segments, int japaneseStart)
    {
        static bool IsDigit(CompositionUnit unit) => unit.Raw is [var d] && char.IsAsciiDigit(d);
        if (start == 0 || !IsDigit(units[start - 1]) || units[start].Raw.Length == 0 || !char.IsAsciiLetter(units[start].Raw[0])) return -1;
        var k = start - 1;
        while (k >= 0 && IsDigit(units[k])) k--;
        if (k < 0 || segments.Count == 0 || !segments[^1].IsEnglish || japaneseStart != k + 1) return -1;
        var end = start;
        while (end < units.Count && units[end].Raw.Length > 0 && units[end].Raw.All(char.IsAsciiLetter)) end++;
        var word = Raw(units, start, end) + (end == units.Count ? pending : "");
        if (Detection.DictionaryDetector.StartsWithParticle(word.ToLowerInvariant()) is not null || !IsKnownEnglishWord(word)) return -1;
        return end;
    }

    /// <summary>
    /// 区間 [.., end) が、ん の後の っ (1 文字の子音) で終わるか (meeting|ga の g = っ)。区間だけを見るとこの子音は読めない
    /// (英単語の最後の子音) ので、読めない英字を含む区間と同じに扱う。
    /// </summary>
    private static bool EndsWithLoneSokuon(IReadOnlyList<CompositionUnit> units, int end) =>
        end >= 2 && end < units.Count && units[end - 1] is { Kana: "っ", Raw.Length: 1 } && units[end - 2].Kana == "ん";

    private static bool HasUnreadable(IReadOnlyList<CompositionUnit> units, int start, int end)
    {
        for (var k = start; k < end; k++)
        {
            if (units[k] is { Raw.Length: 1 } unit && unit.Kana == unit.Raw && char.IsAsciiLetter(unit.Raw[0]) && !IsLaughter(units, k)) return true;
        }
        return false;
    }

    /// <summary>
    /// かなのすぐ後ろに続く w の単位 (きた|w|w): 笑いとして w のまま残したもの。ローマ字として読めなかった英字 (zoom の m) とは違うので、
    /// 英単語の根拠にしない (kiyagat|ta|w の ta + w を英単語 taw にしていた)。
    /// </summary>
    private static bool IsLaughter(IReadOnlyList<CompositionUnit> units, int k)
    {
        var i = k;
        while (i >= 0 && units[i].Raw is "w" or "W" && units[i].Kana == units[i].Raw) i--;
        return i < k && i >= 0 && units[i].Kana is [var kana, ..] && kana is >= 'ぁ' and <= 'ヺ' &&
               Enumerable.Range(k + 1, units.Count - k - 1).TakeWhile(j => units[j].Raw.Length > 0 && char.IsAsciiLetter(units[j].Raw[0])).All(j => units[j].Raw is "w" or "W");
    }

    private static string Kana(IReadOnlyList<CompositionUnit> units, int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Kana));

    private static string Raw(IReadOnlyList<CompositionUnit> units, int start, int end) =>
        string.Concat(Enumerable.Range(start, end - start).Select(k => units[k].Raw));
}
