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
        reading = reading.Trim();
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

    /// <summary>Microsoft IME の形式 (UTF-16) で書き出す。書き出した語の数を count に返す。だめなら理由を返す。</summary>
    public static string? Export(UserDictionary dictionary, string path, out int count)
    {
        count = 0;
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
