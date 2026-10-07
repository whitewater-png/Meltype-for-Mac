// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Learning;

namespace Meltype.Composition;

/// <summary>
/// 学習データをまとめて消す (Mac の入力メニュー「学習データをすべて消去…」)。
/// 消すのは、変換の学習・登録提案の履歴・英語/日本語の学習・英訳の学習・ユーザーモデル。ユーザー辞書 (userdict.txt) と設定 (config.json) は消さない。
/// </summary>
public static class LearningData
{
    /// <summary>
    /// 渡された学習データ (共有しているメモリ上のもの) を空にして、ファイルにも書く。null のものは飛ばす。
    /// modelFile は Windows 版が使うユーザーモデルのファイル (あるときだけ空にする。無ければ作らない)。
    /// 1 つが失敗しても残りは消す (失敗は例外にせずログに残し、false を返す)。
    /// </summary>
    internal static bool ClearAll(ConversionHistory? history, DictionarySuggestions? suggestions, LanguageMemory? languages, TranslationHistory? translations, string? modelFile)
    {
        var ok = true;
        void Run(string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                ok = false;
                Diagnostics.Log.Warn($"{name}を消せませんでした: {ex.Message}");
            }
        }
        Run("変換の学習データ", () => history?.Clear());
        Run("辞書の提案データ", () => suggestions?.Clear());
        Run("英語 / 日本語の学習データ", () => languages?.Clear());
        Run("英訳の学習データ", () => translations?.Clear());
        Run("ユーザーモデル", () =>
        {
            if (modelFile is not null && File.Exists(modelFile)) new UserModel(modelFile).Reset();
        });
        return ok;
    }

    /// <summary>
    /// 既定の保存場所 (<see cref="AppPaths"/>) の学習データをすべて消す。入力欄のセッションがなくても呼べる
    /// (共有インスタンスはパスごとにプロセス内で 1 つなので、ここで取り直しても同じものが消える)。成功したら true。
    /// </summary>
    public static bool ClearAll() => ClearAll(
        ConversionHistory.Shared(AppPaths.ConversionHistoryFile),
        DictionarySuggestions.Shared(AppPaths.DictionarySuggestionFile),
        LanguageMemory.Shared(AppPaths.LanguageMemoryFile),
        TranslationHistory.Shared(AppPaths.TranslationHistoryFile),
        AppPaths.ModelFile);
}
