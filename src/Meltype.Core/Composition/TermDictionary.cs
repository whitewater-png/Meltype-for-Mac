// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>専門用語集の 1 語 (ファイルの 1 行)。読みは正規化したもの (TermDictionary.Normalize)、注記は 3 つ目の欄 (無ければ空)。</summary>
public sealed record TermEntry(string Reading, string Word, string Note);

/// <summary>
/// 専門用語集 (dictionaries/terms-*.txt、アプリに同梱する。分野ごとに有効/無効を切り替える: TermDomains)。1 行に「読み[Tab]語[Tab]注記(任意)」。# で始まる行と空行はコメント。
/// 読みが ForcedMinReadingLength 文字以上で、語が ASCII だけではない語は「強制型」(ユーザー辞書の組み込み語句と同じ。変換する読みの中に含まれていれば、
/// その部分を語にする)、それより短い語と、語が ASCII だけ (英単語など。日常語を英語に置き換えないため) の語は「候補追加型」(変換候補に足すだけ。日常語の途中に現れる短い読みを巻き込まないため)。
/// 読みが日常語の読み (dictionaries/readings.txt。<see cref="CommonReadings"/>) と同じ語も「候補追加型」にする (こうせい → 構成 を強制すると、こうせいろうどうしょう が 構成|ろうどうしょう になるため)。
/// 日常語と同じ読みでも強制したい語は、4 つ目の欄に「強制」と書く (<see cref="KeepForcedMark"/>)。
/// 数万語でも起動が重くならないよう、読み込み後は変更しない配列と辞書にして、探す側は文字列を作らずに引く。
/// ユーザー辞書の保存・表示・書き出しには混ぜない (UserDictionary が持つだけ)。
/// </summary>
public sealed class TermDictionary
{
    /// <summary>この文字数以上の読みの語を強制型にする (しきい値はここ 1 か所)。</summary>
    public const int ForcedMinReadingLength = 4;

    /// <summary>4 つ目の欄にこの文字列があれば、日常語と同じ読みでも強制型にする (tools/check-terms.mjs と同じ)。</summary>
    public const string KeepForcedMark = "強制";

    /// <summary>予測変換のために、読みの前方一致を数える上限 (数千語が一致する短い読みでも 1 キーを重くしない)。</summary>
    internal const int PredictionScanLimit = 64;

    public static readonly TermDictionary Empty = new([], [], [], [], [], 0);

    // 強制型: 読み → 語 (ファイル順)。探すときは ReadOnlySpan<char> のまま引く。
    private readonly Dictionary<string, string[]> _forced;
    private readonly Dictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> _forcedLookup;
    // 先頭の 1 文字ごとの、強制型の読みの最大の長さ (先頭が違えば照合しない)
    private readonly Dictionary<char, int> _forcedMaxByFirst = [];
    // 自作の専門用語集の語 (ユーザー辞書の語と同じ扱いで、すべて強制型。同梱の強制型とは分けて持つ: ユーザー辞書の組み込み語句より先に使うため)。
    // 読み → 語 (後に書いた語が先)。探すときは ReadOnlySpan<char> のまま引く。
    private readonly Dictionary<string, string[]> _userForced;
    private readonly Dictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> _userForcedLookup;
    private readonly Dictionary<char, int> _userForcedMaxByFirst = [];
    // 自作の専門用語集の語を、読み込んだ順 (古い分野から、ファイルの順) に並べたもの。予測変換用 (ユーザー辞書の語と同じく、ファイルの順に探す)
    private readonly (string Reading, string Word)[] _userAll;
    // 候補追加型: 読み → 語
    private readonly Dictionary<string, string[]> _candidates;
    // 予測変換用: 読みの昇順 (同じ読みはファイル順) に並べた全部の語。前方一致を二分探索で引く。
    private readonly string[] _sortedReadings;
    private readonly string[] _sortedWords;

    /// <summary>飛ばした不正な行の数。</summary>
    public int Skipped { get; }

    /// <summary>利用者が除外した (terms-excluded.txt) ので入れなかった語の数。</summary>
    public int Excluded { get; }

    public int ForcedCount { get; }

    /// <summary>自作の専門用語集の語の数 (すべて強制型)。</summary>
    public int UserForcedCount { get; }

    public int CandidateCount { get; }
    public int Count => ForcedCount + UserForcedCount + CandidateCount;

    private TermDictionary(Dictionary<string, string[]> forced, Dictionary<string, string[]> candidates, Dictionary<string, string[]> userForced,
        List<(string Reading, string Word)> userAll, List<(string Reading, string Word)> all, int skipped, int excluded = 0)
    {
        _forced = forced;
        _forcedLookup = forced.GetAlternateLookup<ReadOnlySpan<char>>();
        _userForced = userForced;
        _userForcedLookup = userForced.GetAlternateLookup<ReadOnlySpan<char>>();
        _userAll = [.. userAll];
        foreach (var reading in userForced.Keys)
        {
            _userForcedMaxByFirst[reading[0]] = Math.Max(_userForcedMaxByFirst.GetValueOrDefault(reading[0]), reading.Length);
        }
        UserForcedCount = userForced.Values.Sum(w => w.Length);
        _candidates = candidates;
        Skipped = skipped;
        Excluded = excluded;
        foreach (var reading in forced.Keys)
        {
            _forcedMaxByFirst[reading[0]] = Math.Max(_forcedMaxByFirst.GetValueOrDefault(reading[0]), reading.Length);
        }
        ForcedCount = forced.Values.Sum(w => w.Length);
        CandidateCount = candidates.Values.Sum(w => w.Length);
        // 読みの昇順・同じ読みはファイル順 (並べ替えが安定でないので、元の位置を第 2 キーにする)。
        var order = new int[all.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            var c = string.CompareOrdinal(all[a].Reading, all[b].Reading);
            return c != 0 ? c : a.CompareTo(b);
        });
        _sortedReadings = new string[order.Length];
        _sortedWords = new string[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            _sortedReadings[i] = all[order[i]].Reading;
            _sortedWords[i] = all[order[i]].Word;
        }
    }

    /// <summary>
    /// テキスト (ファイルの中身。複数可) を読む。不正な行は飛ばす (例外にしない)。同じ読み・同じ語は 1 つにする。
    /// excluded (正規化した読み・語の組) に入っている語は入れない (利用者が除外した語。強制型・候補追加型・予測のどれにも出さない)。
    /// commonReadings (日常語の読み。<see cref="CommonReadings.Set"/>) に読みが入っている語は、強制型にせず候補追加型にする (4 つ目の欄が「強制」の語を除く)。省略すると落とさない。
    /// </summary>
    /// <summary>
    /// userTexts は、利用者が自分で作った専門用語集 (ユーザー辞書から移した語など)。ユーザー辞書の語と同じ扱いにする:
    /// 読みの長さ・語が ASCII か・日常語の読みかに関わらず、すべて強制型。同じ読みでは後に書いた語が先 (ユーザー辞書は新しい登録が先)。
    /// 除外した語の一覧は同梱の分野の語だけに効く。同梱の語と読み・語が同じなら、こちらを残す (強制型になる)。
    /// </summary>
    public static TermDictionary Parse(IEnumerable<string> texts, IReadOnlySet<(string Reading, string Word)>? excluded = null, IReadOnlySet<string>? commonReadings = null,
        IEnumerable<string>? userTexts = null)
    {
        var intern = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        var forced = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new List<(string, string)>();
        var userForced = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var userAll = new List<(string, string)>();
        var skipped = 0;
        var excludedCount = 0;
        foreach (var text in userTexts ?? [])
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.AsSpan().TrimEnd('\r');
                if (line.Length > 0 && line[0] == '﻿') line = line[1..];
                if (line.Length == 0 || line.TrimStart().Length == 0 || line.TrimStart()[0] == '#') continue;
                if (!TryParseLine(line, out var reading, out var word) || UserDictionary.Validate(reading, word) is not null)
                {
                    skipped++;
                    continue;
                }
                if (!intern.TryGetValue(reading, out var shared)) intern[reading] = shared = reading;
                if (!seen.Add((shared, word))) continue;
                if (!userForced.TryGetValue(shared, out var list)) userForced[shared] = list = [];
                list.Insert(0, word);
                userAll.Add((shared, word));
            }
        }
        foreach (var text in texts)
        {
            // 1 行ずつ切り出す (Split で全行の配列を作らない)
            var span = text.AsSpan();
            while (span.Length > 0)
            {
                var newline = span.IndexOf('\n');
                var line = newline < 0 ? span : span[..newline];
                span = newline < 0 ? [] : span[(newline + 1)..];
                if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
                if (line.Length > 0 && line[0] == '﻿') line = line[1..];
                if (line.Length == 0 || line.TrimStart().Length == 0 || line.TrimStart()[0] == '#') continue;
                if (!TryParseLine(line, out var reading, out var word, out _, out var keepForced))
                {
                    skipped++;
                    continue;
                }
                // 読みは同じ文字列を共有する (同じ読みの語が多いので、メモリを節約する)
                if (!intern.TryGetValue(reading, out var shared)) intern[reading] = shared = reading;
                if (!seen.Add((shared, word))) continue;
                if (excluded is { Count: > 0 } && excluded.Contains((shared, word)))
                {
                    excludedCount++;
                    continue;
                }
                // 語が ASCII だけ (英数字・記号) のときは、読みが長くても強制型にしない。強制すると、日常語を英単語に置き換えてしまう
                // (例: IT 用語の「あいこん→icon」「くりっく→click」で、「あいこんをくりっく」が「icon|を|click」になる)。候補には出る。
                // 読みが日常語の読みと同じ語も強制型にしない (keepForced=4 つ目の欄が「強制」のときだけ強制する)
                var isForced = shared.Length >= ForcedMinReadingLength && !IsAscii(word)
                    && (keepForced || commonReadings is null || !commonReadings.Contains(shared));
                var target = isForced ? forced : candidates;
                if (!target.TryGetValue(shared, out var list)) target[shared] = list = [];
                list.Add(word);
                all.Add((shared, word));
            }
        }
        return new TermDictionary(
            forced.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal),
            candidates.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal),
            userForced.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal), userAll,
            all, skipped, excludedCount);
    }

    /// <summary>
    /// 1 つのファイルの語を、ファイルの順に並べる (管理画面の一覧用。除外した語も含める)。不正な行・コメントは飛ばし、同じ読み・語は最初の 1 つだけ。
    /// </summary>
    public static List<TermEntry> Entries(string text)
    {
        var entries = new List<TermEntry>();
        var seen = new HashSet<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.AsSpan().TrimEnd('\r');
            if (line.Length > 0 && line[0] == '﻿') line = line[1..];
            if (line.Length == 0 || line.TrimStart().Length == 0 || line.TrimStart()[0] == '#') continue;
            if (!TryParseLine(line, out var reading, out var word, out var note)) continue;
            if (seen.Add((reading, word))) entries.Add(new TermEntry(reading, word, note));
        }
        return entries;
    }

    /// <summary>語が ASCII の文字 (英数字・記号・空白) だけか。</summary>
    internal static bool IsAscii(string word) => word.AsSpan().IndexOfAnyExceptInRange('\0', '\u007f') < 0;

    /// <summary>1 行を読み・語に分ける。UserDictionary.Validate に合わない行・欄が足りない行は false。</summary>
    internal static bool TryParseLine(ReadOnlySpan<char> line, out string reading, out string word) => TryParseLine(line, out reading, out word, out _);

    /// <summary>1 行を読み・語・注記 (3 つ目の欄。無ければ空) に分ける。</summary>
    internal static bool TryParseLine(ReadOnlySpan<char> line, out string reading, out string word, out string note) =>
        TryParseLine(line, out reading, out word, out note, out _);

    /// <summary>1 行を読み・語・注記・強制の印 (4 つ目の欄が「強制」) に分ける。</summary>
    internal static bool TryParseLine(ReadOnlySpan<char> line, out string reading, out string word, out string note, out bool keepForced)
    {
        reading = word = note = "";
        keepForced = false;
        var first = line.IndexOf('\t');
        if (first < 0) return false;
        var rest = line[(first + 1)..];
        var second = rest.IndexOf('\t');
        if (second >= 0)
        {
            var noteSpan = rest[(second + 1)..];
            var third = noteSpan.IndexOf('\t');
            note = (third < 0 ? noteSpan : noteSpan[..third]).Trim().ToString();
            if (third >= 0)
            {
                var markSpan = noteSpan[(third + 1)..];
                var fourth = markSpan.IndexOf('\t');
                keepForced = (fourth < 0 ? markSpan : markSpan[..fourth]).Trim().SequenceEqual(KeepForcedMark);
            }
        }
        var wordSpan = (second < 0 ? rest : rest[..second]).Trim();
        var readingSpan = line[..first].Trim();
        if (readingSpan.Length < UserDictionary.MinReadingLength || readingSpan.Length > UserDictionary.MaxLength) return false;
        if (wordSpan.Length == 0 || wordSpan.Length > UserDictionary.MaxLength) return false;
        // 注記の欄 (3 つ目) は読まない (4 つ目は「強制」の印だけ読む)。改行・タブは、欄の区切りで切っているのでここでは入らない (\r・\n は念のため)。
        if (wordSpan.IndexOfAny('\r', '\n') >= 0 || readingSpan.IndexOfAny('\r', '\n') >= 0) return false;
        reading = Normalize(readingSpan);
        word = wordSpan.ToString();
        // 正規化のあとでも、読みの長さの下限は守る (全角英数などの置き換えは同じ長さなので変わらないが念のため)
        return reading.Length >= UserDictionary.MinReadingLength;
    }

    /// <summary>読みの正規化: カタカナ → ひらがな、全角英数 → 半角、英大文字 → 小文字 (UserDictionaryFile の取り込みと同じ考え方)。</summary>
    internal static string Normalize(ReadOnlySpan<char> reading)
    {
        var needs = false;
        foreach (var c in reading)
        {
            if (c is >= 'ァ' and <= 'ヶ' or >= '！' and <= '～' or >= 'A' and <= 'Z') { needs = true; break; }
        }
        if (!needs) return reading.ToString();
        return string.Create(reading.Length, reading.ToString(), static (buffer, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (c is >= 'ァ' and <= 'ヶ') c = (char)(c - 0x60);
                else if (c is >= '！' and <= '～') c = (char)(c - 0xFEE0);
                if (c is >= 'A' and <= 'Z') c = char.ToLowerInvariant(c);
                buffer[i] = c;
            }
        });
    }

    /// <summary>kana の start から始まる強制型の読みの最大の長さ (先頭の文字が強制型の読みの先頭に無ければ 0)。</summary>
    internal int MaxForcedLengthAt(string kana, int start) =>
        _forcedMaxByFirst.Count == 0 ? 0 : _forcedMaxByFirst.GetValueOrDefault(kana[start]);

    /// <summary>kana の一部 (span) が強制型の読みと一致するか。一致すれば語 (ファイル順) を返す。文字列を作らない。</summary>
    internal bool TryGetForced(ReadOnlySpan<char> reading, out string[] words) => _forcedLookup.TryGetValue(reading, out words!);

    /// <summary>kana の start から始まる、自作の専門用語集の読みの最大の長さ (先頭の文字が無ければ 0)。</summary>
    internal int MaxUserForcedLengthAt(string kana, int start) =>
        _userForcedMaxByFirst.Count == 0 ? 0 : _userForcedMaxByFirst.GetValueOrDefault(kana[start]);

    /// <summary>kana の一部 (span) が、自作の専門用語集の読みと一致するか。一致すれば語 (後に書いた語が先) を返す。</summary>
    internal bool TryGetUserForced(ReadOnlySpan<char> reading, out string[] words) => _userForcedLookup.TryGetValue(reading, out words!);

    /// <summary>読みがちょうど一致する、自作の専門用語集の語 (後に書いた語が先)。</summary>
    public IReadOnlyList<string> LookupUserForced(string reading) => _userForced.TryGetValue(reading, out var words) ? words : [];

    /// <summary>読みが prefix で始まる、自作の専門用語集の語 (読み込んだ順)。ユーザー辞書の語の予測と同じく、数を絞らない。</summary>
    public IEnumerable<string> UserStartingWith(string prefix)
    {
        if (prefix.Length == 0) yield break;
        foreach (var (reading, word) in _userAll)
        {
            if (reading.StartsWith(prefix, StringComparison.Ordinal)) yield return word;
        }
    }

    /// <summary>読みがちょうど一致する強制型の語。</summary>
    public IReadOnlyList<string> LookupForced(string reading) => _forced.TryGetValue(reading, out var words) ? words : [];

    /// <summary>候補追加型の語 (読みがちょうど一致、または「読み + 助詞など」)。助詞などの付け方は CandidateDictionary.Lookup に合わせる。</summary>
    public IReadOnlyList<string> LookupCandidates(string reading)
    {
        if (_candidates.Count == 0) return [];
        if (_candidates.TryGetValue(reading, out var exact)) return exact;
        foreach (var ending in CandidateDictionary.Endings)
        {
            if (reading.Length - ending.Length < UserDictionary.MinReadingLength || !reading.EndsWith(ending, StringComparison.Ordinal)) continue;
            if (_candidates.TryGetValue(reading[..^ending.Length], out var words)) return ending.Length == 0 ? words : words.Select(w => w + ending).ToList();
        }
        return [];
    }

    /// <summary>読みが prefix で始まる語 (読みの昇順・同じ読みはファイル順)。最大 PredictionScanLimit 個まで。</summary>
    public IEnumerable<string> StartingWith(string prefix)
    {
        if (_sortedReadings.Length == 0 || prefix.Length == 0) yield break;
        var low = 0;
        var high = _sortedReadings.Length;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (string.CompareOrdinal(_sortedReadings[mid], prefix) < 0) low = mid + 1;
            else high = mid;
        }
        for (var i = low; i < _sortedReadings.Length && i < low + PredictionScanLimit && _sortedReadings[i].StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            yield return _sortedWords[i];
        }
    }
}

/// <summary>
/// 日常語の読みの集合 (dictionaries/readings.txt の見出し。JMdict のよく使う語)。専門用語集の強制型を、日常語と衝突させないために使う。
/// 活用した形は含めない (「読み + 活用」を日常語と数えると、衝突の判定が広がりすぎるため)。初回の利用で 1 回だけ読む。
/// </summary>
public static class CommonReadings
{
    private static readonly Lazy<HashSet<string>> Shared = new(Load);

    /// <summary>日常語の読み (ひらがな)。</summary>
    public static IReadOnlySet<string> Set => Shared.Value;

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in Detection.DictionarySource.ReadEmbedded("readings.txt").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            set.Add(tab < 0 ? line : line[..tab]);
        }
        return set;
    }
}
