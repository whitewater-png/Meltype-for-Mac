// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// Mozc と OS の変換エンジン (Windows では Microsoft IME) を組み合わせる。
/// 両方 (既定) なら Mozc で変換し、Mozc が使えない・変換できないときは OS の変換エンジンで変換する。
/// 候補の一覧は Mozc の候補の後ろに OS の変換エンジンの候補を足す (Microsoft IME に登録した単語も出る)。
/// </summary>
public sealed class HybridConverter(Func<ConversionEngine> engine, MozcConverter? mozc, IKanjiConverter system,
    Func<string, IReadOnlyList<string>>? systemCandidates) : IKanjiConverter, ILearningConverter
{
    private bool UseMozc => engine() != ConversionEngine.System && mozc is { IsAvailable: true };
    private bool UseSystem => engine() != ConversionEngine.Mozc || mozc is not { IsAvailable: true };

    public string? Convert(string hiragana) =>
        (UseMozc ? mozc!.Convert(hiragana) : null) ?? (UseSystem ? system.Convert(hiragana) : null);

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
        (UseMozc ? mozc!.ConvertClauses(hiragana, context) : null) ?? (UseSystem ? system.ConvertClauses(hiragana, context) : null);

    /// <summary>確定した文節を Mozc に覚えさせる (Microsoft IME の学習は Microsoft IME に任せる)。</summary>
    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
    {
        if (UseMozc) mozc!.Learn(context, clauses);
    }

    /// <summary>読みの候補の一覧 (Mozc → OS の変換エンジン)。</summary>
    public IReadOnlyList<string> Candidates(string reading)
    {
        var list = new List<string>();
        if (UseMozc) list.AddRange(mozc!.Candidates(reading));
        if (engine() != ConversionEngine.Mozc && systemCandidates is not null)
        {
            foreach (var candidate in systemCandidates(reading)) if (!list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }
}
