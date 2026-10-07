// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>文節 1 つ分の変換結果。Reading はその文節の読み (ひらがな)。</summary>
public sealed record ConversionClause(string Reading, string Text);

/// <summary>
/// ひらがな → 漢字かな混じりの変換。Meltype 自身は変換の辞書を持たず、OS や外部の変換エンジンを借りる
/// (Windows: Microsoft IME、Mac / Linux: Mozc など)。
/// </summary>
public interface IKanjiConverter
{
    /// <summary>全体を 1 回で変換する。変換できなければ null。</summary>
    string? Convert(string hiragana);

    /// <summary>
    /// 文節に区切って変換する。区切れなければ null。context は直前の確定済みの文字列 (変換の文脈として使うだけで、結果には含めない)。
    /// </summary>
    IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null);
}

/// <summary>確定した変換を覚えられる変換エンジン (Mozc)。次から同じ読みで同じ文字列を先に出す。</summary>
public interface ILearningConverter
{
    /// <summary>ユーザーが確定した文節 (読みと文字列、区切りも確定したとおり) を覚えさせる。context は直前の確定済みの文字列。</summary>
    void Learn(string? context, IReadOnlyList<ConversionClause> clauses);
}
