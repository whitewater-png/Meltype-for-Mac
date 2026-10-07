// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meltype.Composition;
using Meltype.Detection;

namespace Meltype.Mac;

/// <summary>
/// Mac 版の IME (Swift, Input Method Kit) と Linux 版の IME (Python, IBus) から呼ぶ C の関数。
/// NativeAOT で libMeltypeNative.dylib (mac/build.sh) / libMeltypeNative.so (linux/build.sh) にする。
///
/// 文字列はすべて UTF-8 の NUL 終端。こちらが返す文字列は meltype_free で解放する。
/// Swift 側が登録する関数 (漢字変換・候補・英単語の判定) が返す文字列は malloc したもので、こちらで free する。
/// Input Method Kit はメインスレッドから呼ぶので、排他はしない。
/// ただし学習 (CallbackConverter.Learn) だけは裏のスレッドから来る (Swift 側でメインスレッドに移してから azooKey を呼ぶ)。
/// </summary>
public static unsafe class Exports
{
    /// <summary>(ひらがな, 文脈 or NULL) → 「読み\t変換結果」を改行でつないだ文字列 (文節ごと)。変換できなければ NULL。</summary>
    private static delegate* unmanaged<byte*, byte*, byte*> s_clauses;

    /// <summary>読み → 候補を改行でつないだ文字列。</summary>
    private static delegate* unmanaged<byte*, byte*> s_candidates;

    /// <summary>小文字の英単語 → 英語として正しい綴りなら 1。</summary>
    private static delegate* unmanaged<byte*, int> s_isWord;

    /// <summary>(文脈 or NULL, 「読み\t確定した文字列」を改行でつないだ文節の列) → 確定した変換を azooKey に学習させる。</summary>
    private static delegate* unmanaged<byte*, byte*, void> s_learn;

    /// <summary>読み → 予測候補を改行でつないだ文字列 (azooKey の予測)。</summary>
    private static delegate* unmanaged<byte*, byte*> s_predictions;

    /// <summary>漢字混じりの文字列 → 読み (ひらがな)。読めなければ NULL。確定後の再変換用 (macOS の CFStringTokenizer)。</summary>
    private static delegate* unmanaged<byte*, byte*> s_reading;

    /// <summary>再変換の読みを調べる Swift 側の関数を登録する (meltype_init の後、最初に 1 回)。meltype_init の引数を増やさないよう別にした。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_reader")]
    public static void SetReader(delegate* unmanaged<byte*, byte*> reading) => s_reading = reading;

    /// <summary>
    /// FFI の版数。関数の引数や意味を変えたら上げ、Swift の NativeCore.expectedAbiVersion も同じ値にする
    /// (別の版の libMeltypeNative.dylib が混ざったとき、引数の食い違いで落ちる代わりに初期化を止めるため)。
    /// </summary>
    public const int AbiVersion = 4;

    [UnmanagedCallersOnly(EntryPoint = "meltype_abi_version")]
    public static int GetAbiVersion() => AbiVersion;

    /// <summary>「変換後も続けて入力できる」が ON なら 1、OFF なら 0。入力メニューのチェック表示用。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_get_continue_after_conversion")]
    public static int GetContinueAfterConversion() => Config.ContinueAfterConversionSetting.IsOn ? 1 : 0;

    /// <summary>「変換後も続けて入力できる」を切り替えて config.json に保存する。すべての入力欄にすぐ反映される。保存できたら 1、できなければ 0。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_continue_after_conversion")]
    public static int SetContinueAfterConversion(int on) => Config.ContinueAfterConversionSetting.Set(on != 0) ? 1 : 0;

    /// <summary>
    /// 学習データ (変換・登録提案・英語/日本語・英訳・ユーザーモデル) をすべて消す。ユーザー辞書と設定は消さない。
    /// メモリ上の共有インスタンスも空にする。全部消せたら 1、一部でも失敗したら 0 (失敗はログに残る)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_clear_learning")]
    public static int ClearLearning()
    {
        try
        {
            return LearningData.ClearAll() ? 1 : 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 学習データを消せませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>Swift 側の関数を登録する (最初に 1 回)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init")]
    public static int Init(delegate* unmanaged<byte*, byte*, byte*> clauses, delegate* unmanaged<byte*, byte*> candidates, delegate* unmanaged<byte*, int> isWord, delegate* unmanaged<byte*, byte*, void> learn, delegate* unmanaged<byte*, byte*> predictions)
    {
        s_clauses = clauses;
        s_candidates = candidates;
        s_isWord = isWord;
        s_learn = learn;
        s_predictions = predictions;
        return 1;
    }

    /// <summary>
    /// 漢字変換を Mozc の変換ヘルパー (meltype_mozc_helper) で行う (Linux 版)。meltype_init の代わりに最初に 1 回呼ぶ。
    /// helper はヘルパーの実行ファイルのパス、profile は Mozc の学習データの保存先 (NULL ならデータの保存場所の mozc)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_init_mozc")]
    public static int InitMozc(byte* helper, byte* profile)
    {
        try
        {
            var path = FromUtf8(helper);
            if (string.IsNullOrEmpty(path)) return 0;
            s_mozc?.Dispose();
            s_mozc = new MozcConverter(path, FromUtf8(profile) ?? Path.Combine(Config.AppPaths.DataDirectory, "mozc"));
            if (!s_mozc.IsInstalled) return 0;
            s_mozc.WarmUp();
            return 1;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mozc を使えませんでした: {ex}");
            return 0;
        }
    }

    /// <summary>Mozc で変換するとき (Linux 版)。null なら登録された関数で変換する (Mac 版)。</summary>
    private static MozcConverter? s_mozc;

    /// <summary>入力欄 (Input Method Kit のクライアント) ごとの入力の本体を作る。失敗したら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_create")]
    public static IntPtr Create()
    {
        try
        {
            var session = s_mozc is { } mozc
                ? MeltypeSession.CreateDefault(mozc, mozc.Candidates, s_isWord == null ? null : new CallbackWordChecker())
                : MeltypeSession.CreateDefault(new CallbackConverter(), MoreCandidates, s_isWord == null ? null : new CallbackWordChecker(), Predictions, s_reading == null ? null : ReadingOf);
            return GCHandle.ToIntPtr(GCHandle.Alloc(session));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 初期化できませんでした: {ex}");
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_destroy")]
    public static void Destroy(IntPtr handle)
    {
        // 二重解放・不正なハンドルで InvalidOperationException になっても、IME のプロセスごと落とさない (例外は UnmanagedCallersOnly の外へ出せない)。
        try
        {
            if (handle != IntPtr.Zero) GCHandle.FromIntPtr(handle).Free();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 入力の後始末で例外: {ex.Message}");
        }
    }

    /// <summary>
    /// キーを 1 つ処理して、結果を JSON で返す (<see cref="SessionResult.ToJson"/>)。
    /// vk は Windows の仮想キーコード、ch は入力する文字 (UTF-16 の 1 文字、無ければ 0)、
    /// modifiers は Shift = 1, Control = 2, Option = 4, Command = 8。before / after はキャレットの前後の文字列 (NULL 可)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_handle_key")]
    public static byte* HandleKey(IntPtr handle, int vk, int ch, int modifiers, byte* before, byte* after)
    {
        return Run(handle, session => session.HandleKey(vk, ch > 0 ? (char)ch : null,
            (modifiers & 1) != 0, (modifiers & 2) != 0, (modifiers & 4) != 0, (modifiers & 8) != 0, FromUtf8(before), FromUtf8(after)));
    }

    /// <summary>未確定の内容を確定する (フォーカスが外れたときなど)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_commit")]
    public static byte* Commit(IntPtr handle) => Run(handle, session => session.CommitPending());

    /// <summary>候補ウィンドウで候補を選んだ。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_select_candidate")]
    public static byte* SelectCandidate(IntPtr handle, int index) => Run(handle, session => session.SelectCandidate(index));

    /// <summary>予測候補をクリックで選んで確定する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_select_prediction")]
    public static byte* SelectPrediction(IntPtr handle, int index) => Run(handle, session => session.SelectPrediction(index));

    /// <summary>
    /// 確定済みの文字列 (入力欄で選択されているもの) を読みに戻して変換を始める。結果は他と同じ JSON。
    /// 読みに戻せなければ consumed が false (キーはアプリに渡す)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_reconvert")]
    public static byte* Reconvert(IntPtr handle, byte* text)
    {
        var selected = FromUtf8(text) ?? "";
        return Run(handle, session => session.Reconvert(selected));
    }

    /// <summary>英数 (直接入力) にするか (1) 日本語にするか (0)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_direct")]
    public static void SetDirect(IntPtr handle, int direct)
    {
        if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.Direct = direct != 0;
    }

    /// <summary>
    /// 入力欄のアプリ (bundle ID。NULL 可) を伝える。戻り値は種類: 0 = 一般、1 = コード (英数から始める)、2 = Meltype が動かないアプリ (アプリ別設定で OFF・ゲーム)、-1 = 失敗。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_app")]
    public static int SetApp(IntPtr handle, byte* app)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return -1;
            session.SetApp(FromUtf8(app));
            return !session.AppEnabled ? 2 : session.AppProfile == Config.AppProfile.Code ? 1 : 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: アプリの種類を決められませんでした: {ex}");
            return -1;
        }
    }

    /// <summary>ユーザー辞書に登録する。登録できなければ理由の文字列 (meltype_free で解放する)、登録できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_add_user_word")]
    public static byte* AddUserWord(IntPtr handle, byte* reading, byte* word)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return ToUtf8("入力の準備ができていません。");
            var error = session.AddUserWord(FromUtf8(reading) ?? "", FromUtf8(word) ?? "");
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: ユーザー辞書に登録できませんでした: {ex}");
            return ToUtf8("登録できませんでした。");
        }
    }

    /// <summary>入力メニューに出す登録の提案 (最大 3 件)。「読み\t語」を改行でつないだ文字列 (meltype_free で解放する)。無ければ NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_suggestions")]
    public static byte* Suggestions(IntPtr handle)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return null;
            var items = session.PendingSuggestions();
            return items.Count == 0 ? null : ToUtf8(string.Join('\n', items.Select(w => $"{w.Reading}\t{w.Word}")));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 登録の提案を取れませんでした: {ex}");
            return null;
        }
    }

    /// <summary>提案を受けてユーザー辞書に登録する。登録できなければ理由の文字列 (meltype_free で解放する)、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_suggest_accept")]
    public static byte* SuggestAccept(IntPtr handle, byte* reading, byte* word)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return ToUtf8("入力の準備ができていません。");
            var error = session.AcceptSuggestion(FromUtf8(reading) ?? "", FromUtf8(word) ?? "");
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 提案を登録できませんでした: {ex}");
            return ToUtf8("登録できませんでした。");
        }
    }

    /// <summary>提案を「登録しない」にする。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_suggest_reject")]
    public static void SuggestReject(IntPtr handle, byte* reading, byte* word)
    {
        try
        {
            if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.RejectSuggestion(FromUtf8(reading) ?? "", FromUtf8(word) ?? "");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 提案を却下できませんでした: {ex}");
        }
    }

    /// <summary>提案の履歴をすべて消す。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_suggest_clear")]
    public static void SuggestClear(IntPtr handle)
    {
        try
        {
            if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.ClearSuggestions();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 提案の履歴を消せませんでした: {ex}");
        }
    }

    /// <summary>提案待ちができた直後の確定で出す 1 行のヒント (1 日 1 回まで)。出さないときは NULL。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_suggest_hint")]
    public static byte* SuggestHint(IntPtr handle)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return null;
            return session.TakeSuggestionHint() is { } hint ? ToUtf8(hint) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 登録のヒントを取れませんでした: {ex}");
            return null;
        }
    }

    /// <summary>データの保存場所 (設定・学習・ユーザー辞書)。meltype_free で解放する。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_data_directory")]
    public static byte* DataDirectory() => ToUtf8(Config.AppPaths.DataDirectory);

    /// <summary>
    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。platform は "Mac" か "Linux"。meltype_free で解放する。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_report_url")]
    public static byte* ReportUrl(byte* platform)
    {
        try
        {
            var name = FromUtf8(platform) ?? "Mac";
            var settings = Config.Settings.Load(Config.AppPaths.ConfigFile);
            return ToUtf8(Config.ProjectInfo.ReportUrl($"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, name)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8($"{Config.ProjectInfo.SourceUrl}/issues/new/choose");
        }
    }

    /// <summary>
    /// GitHub の最新 Release の JSON と今の版から、更新してよい新しい版かを判定する。
    /// 更新できるなら「版・ダウンロード URL・SHA-256・サイズ・リリースページ」を改行でつないだ文字列 (meltype_free で解放)、無い・不正なら NULL。
    /// 通信は Swift 側が行う。ここは文字列を判定するだけ (UpdateCheck)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_update_evaluate")]
    public static byte* UpdateEvaluate(byte* releaseJson, byte* currentVersion)
    {
        try
        {
            return Update.UpdateCheck.Evaluate(FromUtf8(releaseJson), FromUtf8(currentVersion)) is { } info ? ToUtf8(info.ToLines()) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 更新の判定で例外: {ex.Message}");
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "meltype_free")]
    public static void Free(byte* text) => NativeMemory.Free(text);

    private static byte* Run(IntPtr handle, Func<MeltypeSession, SessionResult> action)
    {
        try
        {
            if (handle == IntPtr.Zero || GCHandle.FromIntPtr(handle).Target is not MeltypeSession session) return null;
            return ToUtf8(action(session).ToJson());
        }
        catch (Exception ex)
        {
            // 例外を Swift 側へ投げると IME ごと落ちるので、ここで止めてキーはアプリに渡す (NULL)。
            Diagnostics.Log.Error($"Mac: 入力の処理で例外: {ex}");
            return null;
        }
    }

    private static IReadOnlyList<string> MoreCandidates(string reading)
    {
        if (s_candidates == null) return [];
        var text = TakeUtf8(s_candidates(ToUtf8(reading, out var buffer)));
        NativeMemory.Free(buffer);
        return text is null ? [] : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? ReadingOf(string text)
    {
        if (s_reading == null) return null;
        var pointer = ToUtf8(text, out var buffer);
        try
        {
            return TakeUtf8(s_reading(pointer));
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    private static IReadOnlyList<string> Predictions(string reading)
    {
        if (s_predictions == null) return [];
        var text = TakeUtf8(s_predictions(ToUtf8(reading, out var buffer)));
        NativeMemory.Free(buffer);
        return text is null ? [] : text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class CallbackConverter : IKanjiConverter, ILearningConverter
    {
        /// <summary>確定した文節を Swift 側に渡す。Core の確定処理から ThreadPool 経由で呼ばれる (メインスレッドではない)。</summary>
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            if (s_learn == null || clauses.Count == 0) return;
            var contextPointer = context is null ? null : ToUtf8(context, out var contextBuffer);
            var clausesPointer = ToUtf8(string.Join('\n', clauses.Select(c => $"{c.Reading}\t{c.Text}")), out var clausesBuffer);
            try
            {
                s_learn(contextPointer, clausesPointer);
            }
            finally
            {
                NativeMemory.Free(clausesBuffer);
                if (contextPointer != null) NativeMemory.Free(contextPointer);
            }
        }

        public string? Convert(string hiragana) =>
            ConvertClauses(hiragana) is { Count: > 0 } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (s_clauses == null) return null;
            var input = ToUtf8(hiragana, out var inputBuffer);
            var contextPointer = context is null ? null : ToUtf8(context, out var contextBuffer);
            try
            {
                var text = TakeUtf8(s_clauses(input, contextPointer));
                if (text is null) return null;
                var clauses = new List<ConversionClause>();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) return null;
                    clauses.Add(new ConversionClause(line[..tab], line[(tab + 1)..]));
                }
                // 読みをつなげると元のひらがなになること (Meltype の文節の扱いの前提)。
                return clauses.Count > 0 && string.Concat(clauses.Select(c => c.Reading)) == hiragana ? clauses : null;
            }
            finally
            {
                NativeMemory.Free(inputBuffer);
                if (contextPointer != null) NativeMemory.Free(contextPointer);
            }
        }
    }

    private sealed class CallbackWordChecker : IWordChecker
    {
        public bool IsAvailable => s_isWord != null;

        public bool IsWord(string lower)
        {
            if (s_isWord == null || lower.Length < 2) return false;
            var pointer = ToUtf8(lower, out var buffer);
            try
            {
                return s_isWord(pointer) != 0;
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }
    }

    // ---- UTF-8 の文字列 ----

    private static string? FromUtf8(byte* text) => text == null ? null : Marshal.PtrToStringUTF8((IntPtr)text);

    /// <summary>Swift 側が malloc した文字列を読んで解放する。</summary>
    private static string? TakeUtf8(byte* text)
    {
        if (text == null) return null;
        try
        {
            return Marshal.PtrToStringUTF8((IntPtr)text);
        }
        finally
        {
            NativeMemory.Free(text);
        }
    }

    /// <summary>malloc した UTF-8 の文字列にする (呼び出し側が解放する)。</summary>
    private static byte* ToUtf8(string text) => ToUtf8(text, out _);

    private static byte* ToUtf8(string text, out byte* buffer)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        buffer = (byte*)NativeMemory.Alloc((nuint)length + 1);
        fixed (char* chars = text) Encoding.UTF8.GetBytes(chars, text.Length, buffer, length);
        buffer[length] = 0;
        return buffer;
    }
}
