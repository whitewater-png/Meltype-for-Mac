// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
using System.Globalization;

namespace Meltype.Composition;

/// <summary>
/// 数字と + - * / ( ) だけの式を計算する (変換の候補に結果を出す。本家 #796)。
/// 式として読めない・0 で割る・桁が大きすぎるときは null。外部の入力を実行しないよう、四則演算だけを自前で読む。
/// </summary>
public static class Calculator
{
    private const int MaxLength = 80;

    /// <summary>式 (末尾の = は除く) を計算した結果の文字列。演算子を 1 つも含まなければ null。
    /// = で終わらない式は、- 以外の二項演算子 (+ * /) を含むか - が 1 個のときだけ受ける (日付・電話番号を式にしない。Q24)。</summary>
    public static string? Evaluate(string expression)
    {
        var trimmed = expression.Trim();
        var endsWithEquals = trimmed.EndsWith('=');
        // 本家は半角の数字だけ。Mac では全角の数字 (１＋２) も受ける (本家の式が null だった入力だけが対象なので、既存の結果は変わらない)。
        var text = new string(trimmed.TrimEnd('=').Select(c => c switch
        {
            '×' => '*', '÷' => '/', '＋' => '+', '－' => '-',
            >= '０' and <= '９' => (char)(c - '０' + '0'),
            _ => c,
        }).ToArray()).Replace(" ", "");
        if (text.Length is 0 or > MaxLength || !text.Any(c => c is '+' or '-' or '*' or '/') || !text.Any(char.IsAsciiDigit)) return null;
        // - だけが演算子の列 (2026-10-27、03-1234-5678) は日付・電話番号のことが多いので、= で終わらなければ - が 2 個以上なら式としない。
        if (!endsWithEquals && !text.Any(c => c is '+' or '*' or '/') && text.Count(c => c == '-') > 1) return null;
        if (!text.All(c => char.IsAsciiDigit(c) || c is '+' or '-' or '*' or '/' or '(' or ')' or '.')) return null;
        var position = 0;
        try
        {
            var value = ParseSum(text, ref position);
            if (position != text.Length || double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) >= 1e15) return null;
            var rounded = Math.Round(value, 10);
            if (rounded == 0) rounded = 0; // 負のゼロ (0*-1) を -0 と出さない
            return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static double ParseSum(string s, ref int i)
    {
        var value = ParseProduct(s, ref i);
        while (i < s.Length && s[i] is '+' or '-')
        {
            var op = s[i++];
            var right = ParseProduct(s, ref i);
            value = op == '+' ? value + right : value - right;
        }
        return value;
    }

    private static double ParseProduct(string s, ref int i)
    {
        var value = ParseUnary(s, ref i);
        while (i < s.Length && s[i] is '*' or '/')
        {
            var op = s[i++];
            var right = ParseUnary(s, ref i);
            if (op == '/' && right == 0) throw new FormatException("0 で割れません");
            value = op == '*' ? value * right : value / right;
        }
        return value;
    }

    private static double ParseUnary(string s, ref int i)
    {
        if (i < s.Length && s[i] == '-')
        {
            i++;
            return -ParseUnary(s, ref i);
        }
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var inner = ParseSum(s, ref i);
            if (i >= s.Length || s[i] != ')') throw new FormatException("閉じ括弧がありません");
            i++;
            return inner;
        }
        var start = i;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
        if (start == i || !double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new FormatException("数字ではありません");
        return number;
    }
}
