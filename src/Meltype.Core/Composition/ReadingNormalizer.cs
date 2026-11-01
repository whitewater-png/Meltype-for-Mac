// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 読みをそろえる処理と、読みに使える文字の判定 (ユーザー辞書・取り込み・専門用語集で共通)。
/// 以前は UserDictionary・UserDictionaryFile・TermDictionary・TermDomains.User・DictionaryManagement に別々にあったものを、
/// 中身を変えずにここへ集めた。深さ (どこまでそろえるか) は呼び出し元ごとに違うので、深さごとに名前を分けてある:
///   - <see cref="KatakanaToHiragana"/>: カタカナ (ァ〜ヶ) → ひらがなだけ。
///   - <see cref="ForUserDictionary"/>: 前後の空白を取る + カタカナ → ひらがな (ユーザー辞書の登録・照合)。英大文字・全角英数はそのまま。
///   - <see cref="ForTermDictionary"/>: カタカナ → ひらがな + 全角英数 → 半角 + 英大文字 → 小文字 (専門用語集のファイルの読み)。空白は取らない。
///   - <see cref="ForUserTermDomain"/>: 上の 2 つを重ねたもの (自作の専門用語集へ入れる語)。
/// 深さの違いは意図したものではなく経緯によるもの (ユーザー辞書の読みに大文字が残ると変換に出ない、など)。
/// そろえる深さを変えると、保存されている読みと照合の結果が変わるので、ここでは変えていない (変えるときは利用者データへの影響を先に決める)。
/// Swift の辞書アプリ (WordEditor.normalizedReading) にも <see cref="ForUserDictionary"/> と同じ処理の写しがある。
/// </summary>
internal static class ReadingNormalizer
{
    /// <summary>ひらがな (ぁ U+3041 〜 ゖ U+3096。ゔ U+3094・ゕ・ゖ を含む)。</summary>
    public static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ';

    /// <summary>
    /// 読みに使える文字: ひらがな・長音・中点・半角数字・半角英字 (大文字も)。50cc のように、数字や英字を含むかなの並びの読みがある。
    /// ユーザー辞書の Validate と、ほかの日本語入力からの取り込み (UserDictionaryFile.Parse) の両方がこれを使う
    /// (以前は 2 か所に同じ範囲の判定が別々に書いてあった。U+0000〜U+FFFF の全文字で結果が同じことをテストで確かめてある)。
    /// </summary>
    public static bool IsReadingChar(char c) => IsHiragana(c) || c is 'ー' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>カタカナ (ァ U+30A1 〜 ヶ U+30F6) をひらがなにする。ほかの文字 (半角カナ・長音・中点を含む) はそのまま。空白は取らない。</summary>
    public static string KatakanaToHiragana(string text) => new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    /// <summary>
    /// ユーザー辞書の深さ: 前後の空白を取り、カタカナをひらがなにする。英大文字・全角英数・半角カナはそのまま。
    /// カタカナが無ければ、空白を取っただけの文字列を返す。
    /// </summary>
    public static string ForUserDictionary(string reading)
    {
        reading = reading.Trim();
        return reading.Any(c => c is >= 'ァ' and <= 'ヶ') ? KatakanaToHiragana(reading) : reading;
    }

    /// <summary>
    /// 専門用語集の深さ: カタカナ → ひらがな、全角英数・記号 (！ U+FF01 〜 ～ U+FF5E) → 半角、英大文字 → 小文字。空白は取らない。
    /// 専門用語集のファイルを読むたびに全行で呼ぶので、変える文字が無ければ配列を作らない。
    /// </summary>
    public static string ForTermDictionary(ReadOnlySpan<char> reading)
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

    /// <summary>自作の専門用語集へ入れる語の深さ: <see cref="ForUserDictionary"/> のあとに <see cref="ForTermDictionary"/> (前後の空白も取る)。</summary>
    public static string ForUserTermDomain(string reading) => ForTermDictionary(ForUserDictionary(reading));
}
