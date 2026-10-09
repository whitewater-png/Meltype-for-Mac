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
    /// FFI の版数。関数の引数や意味を変えたら上げ、Swift の NativeCore.expectedAbiVersion (IME) と
    /// NativeDictionary.expectedAbiVersion (「Meltype 辞書」の画面) も同じ値にする
    /// (別の版の libMeltypeNative.dylib が混ざったとき、引数の食い違いで落ちる代わりに初期化を止めるため)。
    /// 6: 辞書の管理画面の関数 (meltype_userdict_* / meltype_term_words ほか) を足した。
    /// 7: 設定タブの関数 (meltype_settings_get / meltype_settings_set / meltype_settings_reset) を足した。
    /// 8: 自作の専門用語集の関数 (meltype_term_create ほか) を足し、meltype_term_domains の各行に 5 つ目の欄 (自作なら 1) を足した。
    /// </summary>
    public const int AbiVersion = 8;

    [UnmanagedCallersOnly(EntryPoint = "meltype_abi_version")]
    public static int GetAbiVersion() => AbiVersion;

    /// <summary>「変換後も続けて入力できる」が ON なら 1、OFF なら 0。入力メニューのチェック表示用。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_get_continue_after_conversion")]
    public static int GetContinueAfterConversion() => Config.ContinueAfterConversionSetting.IsOn ? 1 : 0;

    /// <summary>「変換後も続けて入力できる」を切り替えて config.json に保存する。すべての入力欄にすぐ反映される。保存できたら 1、できなければ 0。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_continue_after_conversion")]
    public static int SetContinueAfterConversion(int on) => Config.ContinueAfterConversionSetting.Set(on != 0) ? 1 : 0;

    /// <summary>「Shift+Enter で確定して改行」が ON なら 1、OFF なら 0。入力メニューのチェック表示用。既定は ON。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_get_shift_enter_newline")]
    public static int GetShiftEnterNewline() => Config.ShiftEnterNewlineSetting.IsOn ? 1 : 0;

    /// <summary>「Shift+Enter で確定して改行」を切り替えて config.json に保存する。すべての入力欄にすぐ反映される。保存できたら 1、できなければ 0。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_shift_enter_newline")]
    public static int SetShiftEnterNewline(int on) => Config.ShiftEnterNewlineSetting.Set(on != 0) ? 1 : 0;

    /// <summary>
    /// 専門用語集の分野の一覧。1 行 1 分野で「ID\t名称\t語数\t有効なら 1・そうでなければ 0」を改行でつないだ文字列 (meltype_free で解放する)。
    /// 分野が無い・取れないときは NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_domains")]
    public static byte* TermDomainList()
    {
        try
        {
            var domains = TermDomains.List();
            return domains.Count == 0 ? null : ToUtf8(TermDomains.FormatForFfi(domains));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 専門用語集の一覧を取れませんでした: {ex}");
            return null;
        }
    }

    /// <summary>専門用語集の分野 (ID) を有効 (on != 0) / 無効にして config.json に保存する。すべての入力欄にすぐ反映される。保存できたら 1、できなければ 0。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_set_term_domain")]
    public static int SetTermDomain(byte* id, int on)
    {
        try
        {
            return FromUtf8(id) is { Length: > 0 } name && TermDomains.Set(name, on != 0) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 専門用語集の設定を変えられませんでした: {ex}");
            return 0;
        }
    }

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

    /// <summary>マウスのクリックなどでキャレットが動いたかもしれない。直前の語は確定し直さない (変換中の文字は変えない)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_forget_last_commit")]
    public static void ForgetLastCommit(IntPtr handle)
    {
        try
        {
            if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.ForgetLastCommit();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"確定の記録の破棄に失敗しました: {ex.Message}");
        }
    }

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
        // 例外が UnmanagedCallersOnly の外へ出るとプロセスごと落ちるので、ほかの関数と同じく包む
        try
        {
            if (handle != IntPtr.Zero && GCHandle.FromIntPtr(handle).Target is MeltypeSession session) session.Direct = direct != 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"直接入力の切り替えに失敗しました: {ex.Message}");
        }
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

    // ---- 辞書の管理画面 (mac/Sources/MeltypeDictionary、IME とは別のプロセス) 用 ----
    // 返す文字列はどれも meltype_free で解放する。「理由」を返す関数は、成功なら NULL、だめなら理由の文字列 (画面にそのまま出す)。
    // 中身は Meltype.Core の DictionaryManagement / UserDictionary / TermDomains (managed のテストで確かめている)。

    /// <summary>ユーザー辞書の語 (ファイルの順 = 登録した順)。「読み\t単語」を改行でつなぐ (0 語なら空文字列)。取れなければ NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_words")]
    public static byte* UserDictWords() => Text("ユーザー辞書の一覧", () =>
    {
        var dictionary = DictionaryManagement.UserWords;
        dictionary.Refresh();
        return DictionaryManagement.FormatWords(dictionary.Words);
    });

    /// <summary>ユーザー辞書の版 (変わるたびに増える。ほかのプロセスがファイルを書き換えたときも)。画面が一覧を読み直すかを決めるのに使う。取れなければ -1。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_version")]
    public static int UserDictVersion()
    {
        try
        {
            var dictionary = DictionaryManagement.UserWords;
            dictionary.Refresh();
            return dictionary.Version;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: ユーザー辞書の版を取れませんでした: {ex}");
            return -1;
        }
    }

    /// <summary>登録・編集してよいか (保存はしない)。不正な入力・重複なら理由、よければ NULL。exceptReading / exceptWord は編集中の元の語 (どちらかが NULL なら無し)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_check")]
    public static byte* UserDictCheck(byte* reading, byte* word, byte* exceptReading, byte* exceptWord)
    {
        var readingText = FromUtf8(reading) ?? "";
        var wordText = FromUtf8(word) ?? "";
        var except = exceptReading == null || exceptWord == null ? null : new UserWord(FromUtf8(exceptReading)!, FromUtf8(exceptWord)!);
        return Reason("ユーザー辞書の入力チェック", () => DictionaryManagement.UserWords.Check(readingText, wordText, except));
    }

    /// <summary>ユーザー辞書に登録する (すぐ保存。すでにあれば重複の理由)。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_add")]
    public static byte* UserDictAdd(byte* reading, byte* word)
    {
        var readingText = FromUtf8(reading) ?? "";
        var wordText = FromUtf8(word) ?? "";
        return Reason("ユーザー辞書への登録", () => DictionaryManagement.UserWords.AddNew(readingText, wordText));
    }

    /// <summary>
    /// まとめて登録する (lines は「読み\t単語」の行。管理画面の「ユーザー辞書へ複製」)。1 回の読み直し・保存で済ませる。
    /// 新しく登録した語を「読み\t単語」の行で *added に返す (登録済み・不正で 1 語も増えなければ NULL)。だめなら理由、できたら NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_add_many")]
    public static byte* UserDictAddMany(byte* lines, byte** added)
    {
        if (added != null) *added = null;
        try
        {
            var error = DictionaryManagement.UserWords.AddMany(DictionaryManagement.ParseWords(FromUtf8(lines)), out var words);
            if (error is not null) return ToUtf8(error);
            if (added != null && words.Count > 0) *added = ToUtf8(DictionaryManagement.FormatWords(words));
            return null;
        }
        catch (Exception ex)
        {
            return Failure("ユーザー辞書へのまとめての登録", ex);
        }
    }

    /// <summary>
    /// userdict.txt を読めなかった (権限・文字コード) か、大きすぎて読み込まなかったときの理由 (画面が警告に出す)。読めていれば NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_problem")]
    public static byte* UserDictProblem() => Text("ユーザー辞書の状態", () =>
    {
        var dictionary = DictionaryManagement.UserWords;
        dictionary.Refresh();
        return dictionary.Problem;
    });

    /// <summary>登録した語 (oldReading, oldWord) を直す (位置はそのまま)。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_update")]
    public static byte* UserDictUpdate(byte* oldReading, byte* oldWord, byte* reading, byte* word)
    {
        var old = new UserWord(FromUtf8(oldReading) ?? "", FromUtf8(oldWord) ?? "");
        var readingText = FromUtf8(reading) ?? "";
        var wordText = FromUtf8(word) ?? "";
        return Reason("ユーザー辞書の編集", () => DictionaryManagement.UserWords.Update(old, readingText, wordText));
    }

    /// <summary>
    /// 語を消す (lines は「読み\t単語」の行)。消した語と元の位置を「位置\t読み\t単語」の行で *removed に返す (元に戻すとき meltype_userdict_restore に渡す。
    /// 何も消さなければ NULL)。消す前の内容は userdict.txt.bak に残る。だめなら理由、できたら NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_remove")]
    public static byte* UserDictRemove(byte* lines, byte** removed)
    {
        if (removed != null) *removed = null;
        try
        {
            var error = DictionaryManagement.UserWords.RemoveRange(DictionaryManagement.ParseWords(FromUtf8(lines)), out var entries);
            if (error is not null) return ToUtf8(error);
            if (removed != null && entries.Count > 0) *removed = ToUtf8(DictionaryManagement.FormatIndexed(entries));
            return null;
        }
        catch (Exception ex)
        {
            return Failure("ユーザー辞書の削除", ex);
        }
    }

    /// <summary>消した語を元の位置に戻す (lines は meltype_userdict_remove が返した「位置\t読み\t単語」の行)。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_restore")]
    public static byte* UserDictRestore(byte* lines)
    {
        var text = FromUtf8(lines);
        return Reason("ユーザー辞書の復元", () => DictionaryManagement.UserWords.Restore(DictionaryManagement.ParseIndexed(text)));
    }

    /// <summary>
    /// ほかの日本語入力の辞書ファイル (path) を取り込む。*summary に「登録した数\t登録済み・登録できなかった数\t飛ばした行の数\t文字コード」、
    /// *added に新しく登録した語 (「読み\t単語」の行。取り込みを取り消すときに消す。1 語も増えなければ NULL)。だめなら理由、できたら NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_import")]
    public static byte* UserDictImport(byte* path, byte** summary, byte** added)
    {
        if (summary != null) *summary = null;
        if (added != null) *added = null;
        try
        {
            var error = DictionaryManagement.Import(DictionaryManagement.UserWords, FromUtf8(path) ?? "", out var text, out var words);
            if (error is not null) return ToUtf8(error);
            if (summary != null) *summary = ToUtf8(text);
            if (added != null && words.Count > 0) *added = ToUtf8(DictionaryManagement.FormatWords(words));
            return null;
        }
        catch (Exception ex)
        {
            return Failure("ユーザー辞書の取り込み", ex);
        }
    }

    /// <summary>ユーザー辞書を Microsoft IME の形式 (UTF-16) で path に書き出す。*count に書き出した語の数。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_userdict_export")]
    public static byte* UserDictExport(byte* path, int* count)
    {
        if (count != null) *count = 0;
        try
        {
            var error = DictionaryManagement.Export(DictionaryManagement.UserWords, FromUtf8(path) ?? "", out var written);
            if (count != null) *count = written;
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            return Failure("ユーザー辞書の書き出し", ex);
        }
    }

    /// <summary>読みの入力をひらがなにしたもの (ローマ字 → ひらがな、カタカナ → ひらがな)。画面の「ひらがなにする」用。取れなければ NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_to_reading")]
    public static byte* ToReading(byte* text)
    {
        var input = FromUtf8(text) ?? "";
        return Text("読みの変換", () => DictionaryManagement.ToReading(input));
    }

    /// <summary>
    /// 専門用語集の分野 (ID) の語。「読み\t語\t注記\t除外していれば 1」を改行でつなぐ (ファイルの順)。未知の ID・取れなければ NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_words")]
    public static byte* TermWords(byte* id)
    {
        var name = FromUtf8(id) ?? "";
        return Text("専門用語集の語の一覧", () => TermDomains.Words(name) is { } words ? DictionaryManagement.FormatTermWords(words) : null);
    }

    /// <summary>除外した専門用語 (すべての分野)。「読み\t語」を改行でつなぐ (無ければ空文字列)。取れなければ NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_excluded")]
    public static byte* TermExcluded() => Text("除外した専門用語の一覧", () =>
        DictionaryManagement.FormatWords(TermDomains.Excluded().Select(e => new UserWord(e.Reading, e.Word))));

    /// <summary>専門用語を除外する (excluded != 0) / 元に戻す (0)。lines は「読み\t語」の行。すべての入力欄にすぐ反映される。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_set_excluded")]
    public static byte* TermSetExcluded(byte* lines, int excluded)
    {
        var items = DictionaryManagement.ParseWords(FromUtf8(lines)).Select(w => (w.Reading, w.Word)).ToList();
        return Reason("専門用語の除外", () => TermDomains.SetExcluded(items, excluded != 0));
    }

    /// <summary>
    /// 専門用語を直す: 直した語をユーザー辞書に登録し、元の語を除外する (DictionaryManagement.EditTerm)。
    /// *added は、ユーザー辞書に新しく登録したら 1 (すでにあったら 0)。だめなら理由、できたら NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_edit")]
    public static byte* TermEdit(byte* oldReading, byte* oldWord, byte* reading, byte* word, int* added)
    {
        if (added != null) *added = 0;
        try
        {
            var error = DictionaryManagement.EditTerm(DictionaryManagement.UserWords, new UserWord(FromUtf8(oldReading) ?? "", FromUtf8(oldWord) ?? ""),
                FromUtf8(reading) ?? "", FromUtf8(word) ?? "", out var isNew);
            if (added != null) *added = isNew ? 1 : 0;
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            return Failure("専門用語の編集", ex);
        }
    }

    /// <summary>専門用語集の版 (有効な分野・除外した語が変わるたびに増える。ほかのプロセスが変えたときも)。画面が読み直すかを決めるのに使う。取れなければ -1。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_revision")]
    public static int TermRevision()
    {
        try
        {
            // 一覧を取ると、ほかのプロセスの変更を今すぐ確かめる (間隔を待たない)。
            _ = TermDomains.List();
            return TermDomains.Revision;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: 専門用語集の版を取れませんでした: {ex}");
            return -1;
        }
    }

    // ---- 「Meltype 辞書」の「設定」タブ用 (編集できる設定は Config.MacSettingsCatalog) ----

    /// <summary>
    /// 設定タブに出す項目と今の値の JSON (config.json から読み直す)。形は MacSettingsCatalog.ToJson。
    /// config.json が読めない (壊れている・大きすぎる) ときは NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_settings_get")]
    public static byte* SettingsGet() => Text("設定の取得", Config.MacSettingsCatalog.Read);

    /// <summary>
    /// 設定を 1 つ変えて config.json に保存する (IME の入力欄にはすぐ反映される)。key は項目名、valueJson は JSON の値
    /// (真偽は true / false、数は 3、選択肢は "Balanced" のように名前の文字列)。だめなら理由、できたら NULL。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_settings_set")]
    public static byte* SettingsSet(byte* key, byte* valueJson)
    {
        var name = FromUtf8(key) ?? "";
        var value = FromUtf8(valueJson) ?? "";
        return Reason("設定の保存", () => Config.MacSettingsCatalog.Apply(name, value));
    }

    /// <summary>設定タブにある項目をすべて既定値に戻して保存する (ほかの設定は触らない)。だめなら理由、できたら NULL。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_settings_reset")]
    public static byte* SettingsReset() => Reason("設定を既定値に戻す処理", Config.MacSettingsCatalog.ResetToDefaults);

    // ---- 自作の専門用語集 (「Meltype 辞書」の画面。中身は Composition.TermDomains.User.cs) ----
    // 「理由」を返す関数は、成功なら NULL、だめなら理由の文字列。*out の引数は、増えなければ NULL。語の行は「読み\t語\t注記」(注記は省略できる)。

    /// <summary>自作の専門用語集を作る (すぐ有効)。*id に新しい ID。名前が空・長い・かぶるときは理由。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_create")]
    public static byte* TermCreate(byte* name, byte** id)
    {
        if (id != null) *id = null;
        try
        {
            var error = TermDomains.CreateUserDomain(FromUtf8(name) ?? "", out var created);
            if (error is not null) return ToUtf8(error);
            if (id != null) *id = ToUtf8(created);
            return null;
        }
        catch (Exception ex)
        {
            return Failure("専門用語集の作成", ex);
        }
    }

    /// <summary>自作の専門用語集の名前を変える。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_rename")]
    public static byte* TermRename(byte* id, byte* name)
    {
        var idText = FromUtf8(id) ?? "";
        var nameText = FromUtf8(name) ?? "";
        return Reason("専門用語集の名前の変更", () => TermDomains.RenameUserDomain(idText, nameText));
    }

    /// <summary>自作の専門用語集を消す。*content に消す前の中身、*wasEnabled に有効だったか (1/0)。元に戻すとき meltype_term_restore_domain に渡す。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_delete")]
    public static byte* TermDelete(byte* id, byte** content, int* wasEnabled)
    {
        if (content != null) *content = null;
        if (wasEnabled != null) *wasEnabled = 0;
        try
        {
            var error = TermDomains.DeleteUserDomain(FromUtf8(id) ?? "", out var text, out var enabled);
            if (error is not null) return ToUtf8(error);
            if (content != null) *content = ToUtf8(text);
            if (wasEnabled != null) *wasEnabled = enabled ? 1 : 0;
            return null;
        }
        catch (Exception ex)
        {
            return Failure("専門用語集の削除", ex);
        }
    }

    /// <summary>消した自作の専門用語集を、同じ ID・中身で戻す。enabled != 0 なら有効にする。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_restore_domain")]
    public static byte* TermRestoreDomain(byte* id, byte* content, int enabled)
    {
        var idText = FromUtf8(id) ?? "";
        var text = FromUtf8(content) ?? "";
        return Reason("専門用語集の復元", () => TermDomains.RestoreUserDomain(idText, text, enabled != 0));
    }

    /// <summary>自作の専門用語集に語を登録・編集してよいか (保存しない)。exceptReading / exceptWord は編集中の元の語 (どちらかが NULL なら無し)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_user_check")]
    public static byte* TermUserCheck(byte* id, byte* reading, byte* word, byte* exceptReading, byte* exceptWord)
    {
        var idText = FromUtf8(id) ?? "";
        var readingText = FromUtf8(reading) ?? "";
        var wordText = FromUtf8(word) ?? "";
        (string, string)? except = exceptReading == null || exceptWord == null ? null : (FromUtf8(exceptReading)!, FromUtf8(exceptWord)!);
        return Reason("専門用語の入力チェック", () => TermDomains.CheckUserWord(idText, readingText, wordText, except));
    }

    /// <summary>自作の専門用語集に語を足す。*added に実際に足した語 (「読み\t語\t注記」の行)、*skipped に入れられなかった語 (「読み\t語\t理由」の行。すでにある語は含めない)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_user_add")]
    public static byte* TermUserAdd(byte* id, byte* lines, byte** added, byte** skipped)
    {
        if (added != null) *added = null;
        if (skipped != null) *skipped = null;
        try
        {
            var error = TermDomains.AddUserWords(FromUtf8(id) ?? "", DictionaryManagement.ParseTermEntries(FromUtf8(lines)), out var entries, out var rejected);
            if (error is not null) return ToUtf8(error);
            if (added != null && entries.Count > 0) *added = ToUtf8(DictionaryManagement.FormatTermEntries(entries));
            if (skipped != null && rejected.Count > 0) *skipped = ToUtf8(DictionaryManagement.FormatSkipped(rejected.Select(r => (r.Item.Reading, r.Item.Word, r.Reason))));
            return null;
        }
        catch (Exception ex)
        {
            return Failure("専門用語の登録", ex);
        }
    }

    /// <summary>自作の専門用語集の語を消す (lines は「読み\t語」の行)。*removed に消した語と元の位置 (「位置\t読み\t語\t注記」。meltype_term_user_restore に渡す)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_user_remove")]
    public static byte* TermUserRemove(byte* id, byte* lines, byte** removed)
    {
        if (removed != null) *removed = null;
        try
        {
            var items = DictionaryManagement.ParseTermEntries(FromUtf8(lines)).Select(i => (i.Reading, i.Word));
            var error = TermDomains.RemoveUserWords(FromUtf8(id) ?? "", items, out var entries);
            if (error is not null) return ToUtf8(error);
            if (removed != null && entries.Count > 0) *removed = ToUtf8(DictionaryManagement.FormatIndexedTerms(entries));
            return null;
        }
        catch (Exception ex)
        {
            return Failure("専門用語の削除", ex);
        }
    }

    /// <summary>消した語を元の位置に戻す (lines は meltype_term_user_remove が返した行)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_user_restore")]
    public static byte* TermUserRestore(byte* id, byte* lines)
    {
        var idText = FromUtf8(id) ?? "";
        var text = FromUtf8(lines);
        return Reason("専門用語の復元", () => TermDomains.RestoreUserWords(idText, DictionaryManagement.ParseIndexedTerms(text)));
    }

    /// <summary>自作の専門用語集の語を直す (位置はそのまま)。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_user_update")]
    public static byte* TermUserUpdate(byte* id, byte* oldReading, byte* oldWord, byte* reading, byte* word)
    {
        var idText = FromUtf8(id) ?? "";
        var old = (FromUtf8(oldReading) ?? "", FromUtf8(oldWord) ?? "");
        var readingText = FromUtf8(reading) ?? "";
        var wordText = FromUtf8(word) ?? "";
        return Reason("専門用語の編集", () => TermDomains.UpdateUserWord(idText, old, readingText, wordText));
    }

    /// <summary>
    /// ユーザー辞書の語 (lines は「読み\t単語」の行) を、自作の専門用語集へ移す。専門用語集に入れられない語は、ユーザー辞書に残す。
    /// *removed にユーザー辞書から消した語と元の位置 (「位置\t読み\t単語」。元に戻すとき meltype_userdict_restore に渡す)、
    /// *added に専門用語集へ新しく足した語 (「読み\t語\t注記」。元に戻すとき meltype_term_user_remove に渡す)、
    /// *skipped に移せなかった語 (「読み\t語\t理由」)。専門用語集に書けたがユーザー辞書から消せなかったときは理由を返す (*added は返す)。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_move")]
    public static byte* TermMove(byte* id, byte* lines, byte** removed, byte** added, byte** skipped)
    {
        if (removed != null) *removed = null;
        if (added != null) *added = null;
        if (skipped != null) *skipped = null;
        try
        {
            var error = TermDomains.MoveFromUserDictionary(DictionaryManagement.UserWords, FromUtf8(id) ?? "", DictionaryManagement.ParseWords(FromUtf8(lines)), out var result);
            if (removed != null && result.RemovedFromUser.Count > 0) *removed = ToUtf8(DictionaryManagement.FormatIndexed(result.RemovedFromUser));
            if (added != null && result.AddedToDomain.Count > 0) *added = ToUtf8(DictionaryManagement.FormatTermEntries(result.AddedToDomain));
            if (skipped != null && result.Skipped.Count > 0) *skipped = ToUtf8(DictionaryManagement.FormatSkipped(result.Skipped.Select(s => (s.Word.Reading, s.Word.Word, s.Reason))));
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            return Failure("専門用語集への移動", ex);
        }
    }

    /// <summary>自作の専門用語集を、同梱と同じ形式のテキストファイル (path) に書き出す。*count に語数。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_export")]
    public static byte* TermExport(byte* id, byte* path, int* count)
    {
        if (count != null) *count = 0;
        try
        {
            var error = TermDomains.ExportUserDomain(FromUtf8(id) ?? "", FromUtf8(path) ?? "", out var written);
            if (count != null) *count = written;
            return error is null ? null : ToUtf8(error);
        }
        catch (Exception ex)
        {
            return Failure("専門用語集の書き出し", ex);
        }
    }

    /// <summary>ファイル (path) を新しい自作の専門用語集として取り込む (すぐ有効)。*id に新しい ID、*summary に「取り込んだ語数\t飛ばした行数\t重複して省いた数\t名前」。</summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_term_import")]
    public static byte* TermImport(byte* path, byte** id, byte** summary)
    {
        if (id != null) *id = null;
        if (summary != null) *summary = null;
        try
        {
            var error = TermDomains.ImportUserDomain(FromUtf8(path) ?? "", out var created, out var text);
            if (error is not null) return ToUtf8(error);
            if (id != null) *id = ToUtf8(created);
            if (summary != null) *summary = ToUtf8(text);
            return null;
        }
        catch (Exception ex)
        {
            return Failure("専門用語集の取り込み", ex);
        }
    }

    /// <summary>文字列を返す処理を、例外で落ちないように包む (例外は UnmanagedCallersOnly の外へ出せない)。null・失敗は NULL。</summary>
    private static byte* Text(string what, Func<string?> action)
    {
        try
        {
            return action() is { } text ? ToUtf8(text) : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"Mac: {what}で例外: {ex}");
            return null;
        }
    }

    /// <summary>「理由」を返す処理を包む。成功 (null) なら NULL、理由があればその文字列。例外も理由にする。</summary>
    private static byte* Reason(string what, Func<string?> action)
    {
        try
        {
            return action() is { } reason ? ToUtf8(reason) : null;
        }
        catch (Exception ex)
        {
            return Failure(what, ex);
        }
    }

    private static byte* Failure(string what, Exception ex)
    {
        Diagnostics.Log.Error($"Mac: {what}で例外: {ex}");
        return ToUtf8($"{what}に失敗しました: {ex.Message}");
    }

    /// <summary>
    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。platform は "Mac" か "Linux"。meltype_free で解放する。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "meltype_report_url")]
    public static byte* ReportUrl(byte* platform)
    {
        var name = "Mac";
        try
        {
            name = FromUtf8(platform) ?? "Mac";
            var settings = Config.Settings.Load(Config.AppPaths.ConfigFile);
            return ToUtf8(Config.ProjectInfo.ReportUrl($"{name} (プレビュー版)", Config.ProjectInfo.CoreVersion, Config.ProjectInfo.Environment(settings, name)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"報告の URL を作れませんでした: {ex}");
            return ToUtf8(Config.ProjectInfo.FallbackReportUrl(name));
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
