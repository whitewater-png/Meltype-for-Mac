// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P49 (本家 #796): 計算式の評価。四則演算と括弧だけを自前で読む。</summary>
internal static class CalculatorTests
{
    [Test]
    public static void Evaluate_BasicArithmetic()
    {
        Assert.Equal("7", Calculator.Evaluate("1+2*3"));
        Assert.Equal("9", Calculator.Evaluate("(1+2)*3"));
        Assert.Equal("2.5", Calculator.Evaluate("10/4"));
        Assert.Equal("0.3333333333", Calculator.Evaluate("1/3"), "小数 10 桁で丸める");
        Assert.Equal("2", Calculator.Evaluate("-3+5"), "負の数");
        Assert.Equal("3", Calculator.Evaluate("1.5*2"), "小数");
        Assert.Equal("7", Calculator.Evaluate("1+2*3="), "末尾の = は無視");
        Assert.Equal("3", Calculator.Evaluate("１＋２"), "全角の数字と記号");
        Assert.Equal("12", Calculator.Evaluate("3×4"), "×");
        Assert.Equal("5", Calculator.Evaluate("10÷2"), "÷");
    }

    [Test]
    public static void Evaluate_RejectsInvalidExpressions()
    {
        foreach (var text in new[] { "", "1", "abc", "1+", "(1+2", "1/0", "1e15", "1+2a", new string('1', 81) })
            Assert.True(Calculator.Evaluate(text) is null, text);
        Assert.True(Calculator.Evaluate("1000000000000000+0") is null, "1e15 以上は出さない");
        Assert.Equal("100000000000000", Calculator.Evaluate("100000000000000+0"), "1e14 は出せる");
    }

    [Test]
    public static void Evaluate_HyphenOnly_RejectsDateAndPhoneLikeSequences()
    {
        // Q24: - だけが演算子の列は、日付・電話番号のことが多い。= で終わらなければ - は 1 個までしか式と見なさない。
        foreach (var text in new[] { "2026-10-27", "03-1234-5678", "10-3-2", "２０２６－１０－２７" })
            Assert.True(Calculator.Evaluate(text) is null, text + " は式ではない: " + Calculator.Evaluate(text));
    }

    [Test]
    public static void Evaluate_HyphenOnly_AcceptsSingleMinusOrEqualsSuffix()
    {
        Assert.Equal("7", Calculator.Evaluate("10-3"), "- が 1 個なら式");
        Assert.Equal("7", Calculator.Evaluate("10-3="), "= つき");
        Assert.Equal("5", Calculator.Evaluate("10-3-2="), "= で終われば - が 2 個でも式");
        Assert.Equal("5", Calculator.Evaluate("2*3-1"), "* を含めば - が何個でも式");
        Assert.Equal("-4", Calculator.Evaluate("1+2-3-4"), "+ を含めば - が何個でも式");
        Assert.Equal("-3", Calculator.Evaluate("-3"), "単独の -3 は今までどおり (- が 1 個)");
    }
}
