// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>
/// ユーザー辞書の取り込み・書き出し (ほかの日本語入力から乗り換えるとき・別の PC に移すとき)。
/// 取り込める形式: Microsoft IME の「一覧の出力」(UTF-16、「読み[Tab]語句[Tab]品詞」)、Google 日本語入力の「エクスポート」
/// (UTF-8、「読み[Tab]単語[Tab]品詞[Tab]コメント」)、Meltype の userdict.txt (「読み[Tab]単語」)。! と # で始まる行は飛ばす。
/// 書き出しは Microsoft IME の形式 (Microsoft IME・Google 日本語入力・ATOK のどれでも取り込める)。
/// </summary>
public static class UserDictionaryFile
{
    /// <summary>取り込んだ結果。Skipped は読みがかなでない・短すぎるなどで飛ばした行の数。</summary>
    public sealed record ImportResult(List<UserWord> Words, int Skipped, string Encoding);

    public static ImportResult Parse(byte[] bytes)
    {
        var (text, encoding) = Decode(bytes);
        var words = new List<UserWord>();
        var skipped = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] is '!' or '#') continue;
            var fields = line.Split('\t');
            if (fields.Length < 2)
            {
                skipped++;
                continue;
            }
            var reading = ToHiragana(fields[0].Trim());
            var word = fields[1].Trim();
            if (reading.Length < UserDictionary.MinReadingLength || word.Length == 0 || !reading.All(IsReadingChar))
            {
                skipped++;
                continue;
            }
            words.Add(new UserWord(reading, word));
        }
        return new ImportResult(words, skipped, encoding);
    }

    /// <summary>Microsoft IME の一覧の形式 (UTF-16 LE、BOM 付き) で書き出す。品詞はすべて名詞。</summary>
    public static byte[] Export(IEnumerable<UserWord> words)
    {
        var builder = new StringBuilder();
        builder.Append("!Microsoft IME Dictionary Tool\r\n");
        builder.Append("!Version:\r\n");
        builder.Append("!Format:WORDLIST\r\n");
        builder.Append("!User Dictionary Name: Meltype\r\n");
        builder.Append("!Output File Name:\r\n");
        builder.Append($"!DateTime:{DateTime.Now:yyyy/MM/dd HH:mm:ss}\r\n\r\n");
        foreach (var word in words) builder.Append($"{word.Reading}\t{word.Word}\t名詞\r\n");
        return [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(builder.ToString())];
    }

    /// <summary>BOM を見て UTF-16 (LE / BE) か UTF-8 として読む。BOM が無ければ UTF-8、それで読めなければ UTF-16 LE とみなす。</summary>
    private static (string Text, string Encoding) Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..]) return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16");
        if (bytes is [0xFE, 0xFF, ..]) return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8");
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            // 英字だけの BOM の無い UTF-16 は UTF-8 としても読めてしまうが、0 の文字が入る
            if (!utf8.Contains('\0')) return (utf8, "UTF-8");
            return (Encoding.Unicode.GetString(bytes), "UTF-16");
        }
        catch (DecoderFallbackException)
        {
            // BOM の無い UTF-16 (タブ・改行などの後ろに 0 が入る。UTF-8 の文には 0 は入らない)。Shift_JIS のファイルは読めないので、UTF-8 で保存し直してもらう。
            if (bytes.Length % 2 == 0 && bytes.Where((_, i) => i % 2 == 1).Any(b => b == 0)) return (Encoding.Unicode.GetString(bytes), "UTF-16");
            throw new InvalidDataException("文字コードを読めませんでした。UTF-8 か UTF-16 で保存したファイルを選んでください (Shift_JIS のファイルは、メモ帳などで UTF-8 で保存し直すと取り込めます)。");
        }
    }

    private static string ToHiragana(string text) => new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    private static bool IsReadingChar(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ー' or 'ゔ' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
