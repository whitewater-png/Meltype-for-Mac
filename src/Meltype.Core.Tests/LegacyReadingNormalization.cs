// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// R3 (読みの正規化を ReadingNormalizer に集めた) より前の実装の写し。中身は集める前のコードのまま (変えない)。
/// ReadingNormalizer と呼び出し元が、集める前と全く同じ結果を返すことを確かめるための参照実装。
/// 将来、読みのそろえ方をわざと変えるときは、このファイルではなく ReadingNormalizerEquivalenceTests / ReadingNormalizationCallSiteTests の期待を変える。
/// </summary>
internal static class LegacyReadingNormalization
{
    // ---- UserDictionary (集める前) ----

    public static bool UserDictionaryIsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ゔ';

    public static bool UserDictionaryIsReadingChar(char c) => UserDictionaryIsHiragana(c) || c is 'ー' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    public static string UserDictionaryNormalizeReading(string reading)
    {
        reading = reading.Trim();
        return reading.Any(c => c is >= 'ァ' and <= 'ヶ') ? new string(reading.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray()) : reading;
    }

    public static string? UserDictionaryValidate(string reading, string word)
    {
        if (reading.Length < UserDictionary.MinRegisteredReadingLength) return "読みを入力してください。";
        if (word.Length == 0) return "単語を入力してください。";
        if (reading.Length > UserDictionary.MaxLength || word.Length > UserDictionary.MaxLength) return $"読みも単語も {UserDictionary.MaxLength} 文字までにしてください。";
        if (!reading.Any(UserDictionaryIsHiragana) || !reading.All(UserDictionaryIsReadingChar)) return "読みはひらがなで入力してください (カタカナは自動でひらがなになります)。";
        if (reading.Any(c => c is '\t' or '\r' or '\n') || word.Any(c => c is '\t' or '\r' or '\n')) return "改行・タブ文字は使えません。";
        if (UserDictionary.HasUnsafeText(reading) || UserDictionary.HasUnsafeText(word)) return "制御文字・書式文字は使えません。";
        return null;
    }

    // ---- UserDictionaryFile (集める前) ----

    public static string UserDictionaryFileToHiragana(string text) => new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    public static bool UserDictionaryFileIsReadingChar(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー' or 'ゔ' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>UserDictionaryFile.Parse の 1 行ぶんの判定 (文字コードの判定より後ろ)。飛ばす行なら null。</summary>
    public static UserWord? UserDictionaryFileParseLine(string[] fields)
    {
        var reading = UserDictionaryFileToHiragana(fields[0].Trim());
        var word = fields[1].Trim();
        if (reading.Length < UserDictionary.MinReadingLength || word.Length == 0 || !reading.All(UserDictionaryFileIsReadingChar)) return null;
        return new UserWord(reading, word);
    }

    // ---- TermDictionary (集める前) ----

    public static string TermDictionaryNormalize(ReadOnlySpan<char> reading)
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

    // ---- TermDomains.User (集める前): NormalizeUserWord の読み ----

    public static string TermDomainsUserReading(string reading) => TermDictionaryNormalize(UserDictionaryNormalizeReading(reading));

    /// <summary>集める前の TermDomains.NormalizeUserWord が返す理由 (入れられるなら null)。読みの検査のあと、1 行に書いて読み直せるかも見る。</summary>
    public static string? TermDomainsNormalizeUserWordReason(string reading, string word)
    {
        var normalizedReading = TermDomainsUserReading(reading);
        var normalizedWord = word.Trim();
        if (UserDictionaryValidate(normalizedReading, normalizedWord) is { } invalid) return invalid;
        // ファイルの 1 行に書いて、読み直したときに同じ語になること (TryParseLine と同じ決まり)
        return TermDictionaryTryParseLine($"{normalizedReading}\t{normalizedWord}", out var r, out var w) && r == normalizedReading && w == normalizedWord
            ? null
            : "この語は専門用語集に入れられません。";
    }

    /// <summary>集める前の TermDictionary.TryParseLine (読み・語の部分。注記・強制の印は読み飛ばす)。</summary>
    public static bool TermDictionaryTryParseLine(ReadOnlySpan<char> line, out string reading, out string word)
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
        if (wordSpan.IndexOfAny('\r', '\n') >= 0 || readingSpan.IndexOfAny('\r', '\n') >= 0) return false;
        reading = TermDictionaryNormalize(readingSpan);
        word = wordSpan.ToString();
        return reading.Length >= UserDictionary.MinReadingLength;
    }

    // ---- DictionaryManagement (集める前) ----

    private static readonly Lazy<Meltype.Detection.RomajiDetector> s_romaji = new(() => new Meltype.Detection.RomajiDetector());

    public static string DictionaryManagementToReading(string text)
    {
        var trimmed = text.Trim();
        var kana = trimmed.Any(char.IsAsciiLetter) ? s_romaji.Value.ConvertLenient(trimmed.ToLowerInvariant(), final: true) : trimmed;
        return new string(kana.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
    }

    // ---- 代表的な読み (30 件以上) ----

    public static readonly string[] Samples =
    [
        "キゴウ", " キゴウトウ ", "ｷｺﾞｳ", "ｷｺﾞｳﾄｳ", "ＡＢＣけい", "ABCけい", "abcけい", "Abcけい", "ＡＢＣケイ", "abcケイ",
        "ゔぁいおりん", "ヴァイオリン", "ヵげつ", "ヶしょ", "ヵヶ", "ゕゖ", "らーめん", "ラーメン", "ﾗｰﾒﾝ", "るびー・おん",
        "ルビー・オン", "なかぐろ･", "50cc", "５０ｃｃ", "５０ＣＣ", "50CC", "　きごう　", "\tきごう\t", " きご う ", "きご\u3000う",
        "", "   ", "ア", "ァ", "ン", "ヷヸヹヺ", "ゟヿ", "〜から", "～から", "！？きごう", "ｰ", "ﾞﾟ", "ＫＩＧＯＵ", "kigoutou",
        "KIGOUTOU", "Kigou", "漢字かな", "😀えもじ", "\uD800きごう", "ⅫきごうＩ", "İきごう", "きごう\r", "き\nごう", "キゴウ\u200Bとう",
        "ａ－ｚ", "＃きごう", "きごう２", "Ａ", "Z", "@きごう",
    ];
}
