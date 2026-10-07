// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 専門用語集 (dictionaries/terms-*.txt、アプリに同梱する。分野ごとに有効/無効を切り替える: TermDomains)。1 行に「読み[Tab]語[Tab]注記(任意)」。# で始まる行と空行はコメント。
/// 読みが ForcedMinReadingLength 文字以上で、語が ASCII だけではない語は「強制型」(ユーザー辞書の組み込み語句と同じ。変換する読みの中に含まれていれば、
/// その部分を語にする)、それより短い語と、語が ASCII だけ (英単語など。日常語を英語に置き換えないため) の語は「候補追加型」(変換候補に足すだけ。日常語の途中に現れる短い読みを巻き込まないため)。
/// 数万語でも起動が重くならないよう、読み込み後は変更しない配列と辞書にして、探す側は文字列を作らずに引く。
/// ユーザー辞書の保存・表示・書き出しには混ぜない (UserDictionary が持つだけ)。
/// </summary>
public sealed class TermDictionary
{
    /// <summary>この文字数以上の読みの語を強制型にする (しきい値はここ 1 か所)。</summary>
    public const int ForcedMinReadingLength = 4;

    /// <summary>予測変換のために、読みの前方一致を数える上限 (数千語が一致する短い読みでも 1 キーを重くしない)。</summary>
    internal const int PredictionScanLimit = 64;

    public static readonly TermDictionary Empty = new([], [], [], 0);

    // 強制型: 読み → 語 (ファイル順)。探すときは ReadOnlySpan<char> のまま引く。
    private readonly Dictionary<string, string[]> _forced;
    private readonly Dictionary<string, string[]>.AlternateLookup<ReadOnlySpan<char>> _forcedLookup;
    // 先頭の 1 文字ごとの、強制型の読みの最大の長さ (先頭が違えば照合しない)
    private readonly Dictionary<char, int> _forcedMaxByFirst = [];
    // 候補追加型: 読み → 語
    private readonly Dictionary<string, string[]> _candidates;
    // 予測変換用: 読みの昇順 (同じ読みはファイル順) に並べた全部の語。前方一致を二分探索で引く。
    private readonly string[] _sortedReadings;
    private readonly string[] _sortedWords;

    /// <summary>飛ばした不正な行の数。</summary>
    public int Skipped { get; }

    public int ForcedCount { get; }
    public int CandidateCount { get; }
    public int Count => ForcedCount + CandidateCount;

    private TermDictionary(Dictionary<string, string[]> forced, Dictionary<string, string[]> candidates, List<(string Reading, string Word)> all, int skipped)
    {
        _forced = forced;
        _forcedLookup = forced.GetAlternateLookup<ReadOnlySpan<char>>();
        _candidates = candidates;
        Skipped = skipped;
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

    /// <summary>テキスト (ファイルの中身。複数可) を読む。不正な行は飛ばす (例外にしない)。同じ読み・同じ語は 1 つにする。</summary>
    public static TermDictionary Parse(IEnumerable<string> texts)
    {
        var intern = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>();
        var forced = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new List<(string, string)>();
        var skipped = 0;
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
                if (!TryParseLine(line, out var reading, out var word))
                {
                    skipped++;
                    continue;
                }
                // 読みは同じ文字列を共有する (同じ読みの語が多いので、メモリを節約する)
                if (!intern.TryGetValue(reading, out var shared)) intern[reading] = shared = reading;
                if (!seen.Add((shared, word))) continue;
                // 語が ASCII だけ (英数字・記号) のときは、読みが長くても強制型にしない。強制すると、日常語を英単語に置き換えてしまう
                // (例: IT 用語の「あいこん→icon」「くりっく→click」で、「あいこんをくりっく」が「icon|を|click」になる)。候補には出る。
                var target = shared.Length >= ForcedMinReadingLength && !IsAscii(word) ? forced : candidates;
                if (!target.TryGetValue(shared, out var list)) target[shared] = list = [];
                list.Add(word);
                all.Add((shared, word));
            }
        }
        return new TermDictionary(
            forced.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal),
            candidates.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal),
            all, skipped);
    }

    /// <summary>語が ASCII の文字 (英数字・記号・空白) だけか。</summary>
    internal static bool IsAscii(string word) => word.AsSpan().IndexOfAnyExceptInRange('\0', '\u007f') < 0;

    /// <summary>1 行を読み・語に分ける。UserDictionary.Validate に合わない行・欄が足りない行は false。</summary>
    internal static bool TryParseLine(ReadOnlySpan<char> line, out string reading, out string word)
    {
        reading = word = "";
        var first = line.IndexOf('\t');
        if (first < 0) return false;
        var rest = line[(first + 1)..];
        var second = rest.IndexOf('\t');
        var wordSpan = (second < 0 ? rest : rest[..second]).Trim();
        var readingSpan = line[..first].Trim();
        if (readingSpan.Length < UserDictionary.MinReadingLength || readingSpan.Length > UserDictionary.MaxLength) return false;
        if (wordSpan.Length == 0 || wordSpan.Length > UserDictionary.MaxLength) return false;
        // 注記の欄 (3 つ目) は読まない。改行・タブは、欄の区切りで切っているのでここでは入らない (\r・\n は念のため)。
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
