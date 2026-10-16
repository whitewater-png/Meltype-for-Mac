// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Learning;

namespace Meltype.Composition;

/// <summary>
/// 学習データをまとめて消す (Mac の入力メニュー「学習データをすべて消去…」)。
/// 消すのは、変換の学習・登録提案の履歴・英語/日本語の学習・英訳の学習・ユーザーモデルと、その退避コピー (.broken・.oversize)・保存の途中で残った一時ファイル (.tmp)・ログ。ユーザー辞書 (userdict.txt) と設定 (config.json) は消さない。
/// </summary>
public static class LearningData
{
    /// <summary>
    /// 渡された学習データ (共有しているメモリ上のもの) を空にして、ファイルにも書く。null のものは飛ばす。
    /// modelFile はユーザーモデル (Windows 版の学習。Mac は作らないので、無ければ何もしない) のファイル。あるときだけ空にする (無ければ作らない)。
    /// UserModel は書けなくても例外にしないので、このファイルだけは、書けなかった失敗が戻り値に出ない。
    /// learningFiles は学習データのファイル (渡すと、読めない・退避できないために保存を止めていたものを先に消して、止めた状態を解く)。
    /// 1 つが失敗しても残りは消す (失敗は例外にせずログに残し、false を返す)。ファイルに書けなかった (フォルダーに書けない・保存を止めたまま) ときも失敗として返す。
    /// </summary>
    internal static bool ClearAll(ConversionHistory? history, DictionarySuggestions? suggestions, LanguageMemory? languages, TranslationHistory? translations, string? modelFile, IEnumerable<string>? learningFiles = null)
    {
        var ok = true;
        // action は、消してファイルにも書けたら true。書けなかったときも失敗にする (メモリだけ空になって、中身がファイルに残るのに、成功と答えないため)。
        void Run(string name, Func<bool> action)
        {
            try
            {
                if (action()) return;
                ok = false;
                Diagnostics.Log.Warn($"{name}をファイルに書き込めなかったので、中身がファイルに残っているかもしれません。");
            }
            catch (Exception ex)
            {
                ok = false;
                Diagnostics.Log.Warn($"{name}を消せませんでした: {ex.Message}");
            }
        }
        // 読めない・退避できないために保存を止めたファイル (SafeFile.Block) は、メモリを空にしても書けず、中身が残る。
        // 消去は利用者が確認して選んだ操作なので、そのファイルは消して、止めた状態を解く。消せなければ失敗として返す。
        if (learningFiles is not null && !ReleaseBlocked(learningFiles)) ok = false;
        Run("変換の学習データ", () => history?.TryClear() ?? true);
        Run("辞書の提案データ", () => suggestions?.TryClear() ?? true);
        Run("英語 / 日本語の学習データ", () => { languages?.Clear(); return true; });   // 書けないときは IOException を投げる
        Run("英訳の学習データ", () => translations?.TryClear() ?? true);
        Run("ユーザーモデル", () =>
        {
            if (modelFile is not null && File.Exists(modelFile)) new UserModel(modelFile).Reset();
            return true;
        });
        return ok;
    }

    /// <summary>
    /// 保存を止めているファイル (読めなかった・退避できなかった。SafeFile.Block) を削除して、止めた状態を解く。
    /// 止めていたのは「読めない中身を新しい内容で上書きしない」ためで、消去は中身ごと消すと利用者が選んだ操作なので、守るものがない。
    /// 削除できなかったものは止めたままにして、false を返す (Mac は「消去できなかったものがあります」と出す)。
    /// </summary>
    internal static bool ReleaseBlocked(IEnumerable<string> learningFiles)
    {
        var ok = true;
        foreach (var file in learningFiles)
        {
            if (!SafeFile.IsBlocked(file)) continue;
            try
            {
                try { File.Delete(file); }
                catch (DirectoryNotFoundException) { }
                SafeFile.Unblock(file);
                Diagnostics.Log.Warn($"保存を止めていた {Path.GetFileName(file)} を、消去のために削除しました。");
            }
            catch (Exception ex)
            {
                ok = false;
                Diagnostics.Log.Warn($"保存を止めている {Path.GetFileName(file)} を消せませんでした: {SafeFile.Describe(ex)}");
            }
        }
        return ok;
    }

    /// <summary>
    /// 既定の保存場所 (<see cref="AppPaths"/>) の学習データをすべて消す。入力欄のセッションがなくても呼べる
    /// (共有インスタンスはパスごとにプロセス内で 1 つなので、ここで取り直しても同じものが消える)。成功したら true。
    /// </summary>
    public static bool ClearAll() => ClearAllAt(
        AppPaths.ModelFile, AppPaths.ConversionHistoryFile, AppPaths.DictionarySuggestionFile, AppPaths.TranslationHistoryFile, AppPaths.LanguageMemoryFile,
        [AppPaths.LogFile, AppPaths.CrashLogFile, AppPaths.TraceLogFile]);

    /// <summary>
    /// ClearAll() の本体。保存場所を引数で受け取る (AppPaths の値はプロセスの起動時に決まるので、公開の ClearAll() をテストから呼ぶと本物の学習データを消してしまう。
    /// 公開の入口は AppPaths の値を渡すだけにした)。学習データの 5 ファイルの一覧 (止めたものを削除して止めた状態を解く対象) は、ここで作る。
    /// </summary>
    internal static bool ClearAllAt(string modelFile, string conversionsFile, string suggestionsFile, string translationsFile, string languagesFile, IEnumerable<string> logFiles)
    {
        string[] learningFiles = [modelFile, conversionsFile, suggestionsFile, translationsFile, languagesFile];
        // ログは最初に消す。ログへの書き出しは、積んでおいた分を 1 秒おきにまとめて追記する (無ければ作る) 作りなので、消去のあとで消すと、
        // 書き出しが消去の途中に重なったとき、失敗の警告 (ダイアログが「meltype.log を確認してください」と案内する理由) まで一緒に消える。
        // 積んである分 (消去の前の記録。打った文字が入ることがある) を先に書いて消せば、消去の途中の警告は、あとから新しいログに必ず残る。
        Diagnostics.Log.FlushFile();
        var logsCleared = ClearLeftovers([], logFiles);
        var ok = ClearAll(
            ConversionHistory.Shared(conversionsFile),
            DictionarySuggestions.Shared(suggestionsFile),
            LanguageMemory.Shared(languagesFile),
            TranslationHistory.Shared(translationsFile),
            modelFile,
            learningFiles);
        var leftoversCleared = ClearLeftovers(learningFiles, []);
        return ok && leftoversCleared && logsCleared;
    }

    /// <summary>
    /// 学習データの「残りもの」を消す: 読めなくて退避したコピー (名前.broken)・大きすぎて退避したコピー (名前.oversize、.oversize.1 …)・保存の途中で止まって残った一時ファイル (名前.<番号>.tmp) と、
    /// ログ (名前、名前.old。「入力した文字を残す」設定が ON だと、打った文字が入っている)。ユーザー辞書・設定・専門用語集の除外とそのバックアップは対象外。
    /// 消せないものがあっても残りは消す (失敗はログに残し、false を返す)。
    /// </summary>
    internal static bool ClearLeftovers(IEnumerable<string> learningFiles, IEnumerable<string> logFiles)
    {
        var ok = true;
        void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex)
            {
                ok = false;
                Diagnostics.Log.Warn($"{Path.GetFileName(path)} を消せませんでした: {ex.Message}");
            }
        }
        foreach (var file in learningFiles)
        {
            Delete(file + ".broken");
            var directory = Path.GetDirectoryName(Path.GetFullPath(file));
            if (directory is null || !Directory.Exists(directory)) continue;
            // 名前.oversize と 名前.oversize.<番号> (SafeFile が退避したもの)
            foreach (var path in Directory.EnumerateFiles(directory, Path.GetFileName(file) + ".oversize*")) Delete(path);
            // 保存の途中で止まって残った一時ファイル: 名前.<GUID>.tmp と、.broken を書く途中の 名前.broken.<GUID>.tmp (打った内容が入っている)
            foreach (var path in Directory.EnumerateFiles(directory, Path.GetFileName(file) + ".*.tmp")) Delete(path);
        }
        foreach (var file in logFiles)
        {
            Delete(file);
            Delete(file + ".old");
        }
        return ok;
    }
}
