// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>入力欄への書き込み 1 回分。DeleteBefore 文字をキャレットの前から消してから Text を入れる (確定し直すとき以外は 0)。</summary>
public readonly record struct TextEdit(int DeleteBefore, string Text, string? Original = null);

/// <summary>
/// 1 回のキー入力の結果。Consumed が false ならそのキーはアプリにそのまま渡す (Commits を入れた後で)。
/// View は変換ボックスの内容 (null なら変換ボックスを閉じる)。
/// </summary>
public sealed record SessionResult(bool Consumed, IReadOnlyList<TextEdit> Commits, CompositionView? View)
{
    /// <summary>Swift などから読みやすいように JSON にする (NativeAOT でも使えるよう手書き)。</summary>
    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.Append("{\"consumed\":").Append(Consumed ? "true" : "false").Append(",\"commits\":[");
        for (var i = 0; i < Commits.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append("{\"deleteBefore\":").Append(Commits[i].DeleteBefore).Append(",\"text\":");
            AppendString(builder, Commits[i].Text);
            if (Commits[i].Original is { } original)
            {
                builder.Append(",\"original\":");
                AppendString(builder, original);
            }
            builder.Append('}');
        }
        builder.Append("],\"view\":");
        if (View is not { } view)
        {
            builder.Append("null}");
            return builder.ToString();
        }
        builder.Append("{\"text\":");
        AppendString(builder, view.Text);
        builder.Append(",\"converting\":").Append(view.Converting ? "true" : "false");
        builder.Append(",\"selectedIndex\":").Append(view.SelectedIndex);
        builder.Append(",\"selectedClause\":").Append(view.SelectedClause);
        builder.Append(",\"hint\":");
        AppendString(builder, view.Hint);
        builder.Append(",\"candidates\":");
        AppendArray(builder, view.Candidates);
        builder.Append(",\"clauses\":");
        AppendArray(builder, view.Clauses ?? []);
        // 選んでいる候補の意味 (無ければ null)。少し止まってから出すのは Swift・Python 側
        builder.Append(",\"suggestion\":");
        if (view.Suggestion is { } suggestion) AppendString(builder, suggestion);
        else builder.Append("null");
        builder.Append(",\"meaning\":");
        if (view.Meaning is { } meaning) AppendString(builder, meaning);
        else builder.Append("null");
        builder.Append(",\"predictions\":");
        AppendArray(builder, view.Predictions ?? []);
        builder.Append(",\"selectedPrediction\":").Append(view.SelectedPrediction);
        builder.Append("}}");
        return builder.ToString();
    }

    private static void AppendArray(StringBuilder builder, IReadOnlyList<string> items)
    {
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) builder.Append(',');
            AppendString(builder, items[i]);
        }
        builder.Append(']');
    }

    private static void AppendString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}

/// <summary>
/// OS の正式な IME の仕組み (Mac の Input Method Kit、Linux の IBus / fcitx5) から使う、Meltype の入力の本体。
/// Windows 版はキーボードフックで打鍵を横取りするが、正式な IME では OS がキーを 1 つずつ渡してきて、
/// 「使ったか (アプリに渡さないか)」をその場で返す。そのやり取りを同期的に行う。
///
/// キーは Windows の仮想キーコード (A-Z = 0x41-0x5A、Space = 0x20 …) で渡す。入力する文字は ch で渡す (キーボード配列の違いは OS 側で解決済み)。
/// 変換ボックスの表示・確定する文字は、戻り値の <see cref="SessionResult"/> で返す。1 つのスレッドから使う。
/// </summary>
public sealed class MeltypeSession
{
    private readonly CaptureGate _gate;
    private readonly CompositionController _controller;
    private readonly Host _host = new();
    private readonly Func<Settings> _settings;
    // ユーザー辞書への登録 (AddUserWord) のために、受け取った options を持っておく。
    private readonly CompositionOptions _options;

    public MeltypeSession(CompositionDetector detector, IKanjiConverter converter, CompositionOptions options, Func<Settings> settings)
    {
        _settings = settings;
        _options = options;
        _gate = new CaptureGate(() => { });
        _controller = new CompositionController(_gate, detector, converter, _host, options);
    }

    /// <summary>
    /// 既定の辞書・学習データ (保存場所は <see cref="AppPaths"/>) で作る。converter は OS 側の変換エンジン、
    /// moreCandidates は読みに対する候補の一覧 (無ければ null)、predictions は読みに対する予測候補 (無ければ null)、wordChecker は OS のスペルチェッカー (無ければ null)。
    /// </summary>
    public static MeltypeSession CreateDefault(IKanjiConverter converter, Func<string, IReadOnlyList<string>>? moreCandidates, IWordChecker? wordChecker,
        Func<string, IReadOnlyList<string>>? predictions = null, Func<string, string?>? reader = null)
    {
        AppPaths.MigrateFromOldName();
        Config.SafeFile.EnsureDirectory(AppPaths.DataDirectory);
        var settings = Settings.Load(AppPaths.ConfigFile);
        // 設定で「ファイルにログを書く」を ON にしていれば、Mac でも meltype.log に書く (動かないときの調査用)。
        Diagnostics.Log.SetFileOutput(settings.FileLog ? AppPaths.LogFile : null);
        Diagnostics.Log.RecordText = settings.LogTypedText;
        var userDirectory = AppPaths.UserDictionaryDirectory;
        var detector = CompositionDetector.CreateDefault(userDirectory);
        // OS のスペルチェッカーが無ければ (Linux)、同梱のよく使う英単語の一覧を使う (meeting を英語と分かるように)。
        detector.SpellChecker = wordChecker is { IsAvailable: true } ? wordChecker : Detection.BuiltInWordChecker.Shared;
        var languages = LanguageMemory.Shared(AppPaths.LanguageMemoryFile);
        detector.Memory = languages;
        var options = new CompositionOptions
        {
            LiveConversion = () => settings.LiveConversion,
            // 入力メニューで切り替える。すべての入力欄にすぐ反映されるよう、各セッションの設定ではなく共有の値を毎回見る。
            ContinueAfterConversion = () => ContinueAfterConversionSetting.IsOn,
            ShiftEnterNewline = () => ShiftEnterNewlineSetting.IsOn,
            AutoCorrect = () => settings.AutoCorrectAfterCommit && settings.DetectionLevel != DetectionLevel.Manual,
            Level = () => settings.DetectionLevel,
            Candidates = CandidateDictionary.Load(userDirectory),
            ContextRules = ContextRules.Load(userDirectory),
            History = ConversionHistory.Shared(AppPaths.ConversionHistoryFile),
            UserDictionary = UserDictionary.Shared(AppPaths.UserDictionaryFile),
            MoreCandidates = moreCandidates,
            Predictions = predictions,
            Prediction = () => settings.Prediction,
            PredictionMinLength = () => settings.PredictionMinLength,
            Suggestions = DictionarySuggestions.Shared(AppPaths.DictionarySuggestionFile),
            DictionarySuggest = () => settings.DictionarySuggest,
            DictionarySuggestThreshold = () => settings.DictionarySuggestThreshold,
            Misspellings = MisspellingDictionary.Load(userDirectory),
            Languages = languages,
            Translations = TranslationDictionary.Load(),
            TranslationCandidates = () => settings.TranslationCandidates,
            Meanings = MeaningDictionary.Load(),
            CandidateMeanings = () => settings.ShowCandidateMeanings,
            RomajiTypos = RomajiTypoCorrector.Load(detector.Romaji),
            CorrectTypos = () => settings.CorrectTypos,
            SpaceAroundEnglish = () => settings.SpaceAroundEnglish,
            Punctuation = () => settings.Punctuation,
            FullWidthSymbols = () => settings.FullWidthSymbols,
            TranslationHistory = TranslationHistory.Shared(AppPaths.TranslationHistoryFile),
        };
        return new MeltypeSession(detector, converter, options, () => settings) { ReadingProvider = reader };
    }

    /// <summary>英数 (直接入力) か。true の間はキーをすべてアプリに渡す (Mac の「英数」キー、「かな」キーで戻す)。</summary>
    public bool Direct { get; set; }

    /// <summary>
    /// アプリ別設定に行が無いときに「コード」として扱う Mac のアプリの bundle ID (エディター・ターミナル)。
    /// 既定のアプリ別設定は Windows のプロセス名なので、Mac では bundle ID を持つここで補う。行を足せばそちらが優先。
    /// </summary>
    public static readonly string[] MacCodeApps =
    [
        "com.microsoft.VSCode", "com.microsoft.VSCodeInsiders", "com.todesktop.230313mzl4w4u92", "com.exafunction.windsurf", "dev.zed.Zed",
        "com.apple.dt.Xcode", "com.sublimetext.4", "com.sublimetext.3",
        "com.apple.Terminal", "com.googlecode.iterm2", "dev.warp.Warp-Stable", "com.mitchellh.ghostty", "net.kovidgoyal.kitty", "org.alacritty",
        "com.jetbrains.intellij", "com.jetbrains.pycharm", "com.jetbrains.WebStorm", "com.jetbrains.rider", "com.jetbrains.CLion", "com.jetbrains.goland",
    ];

    /// <summary>今のアプリ (bundle ID)。分からなければ null。</summary>
    public string? AppName { get; private set; }

    /// <summary>今のアプリの種類。<see cref="SetApp"/> で決まる。</summary>
    public AppProfile AppProfile { get; private set; } = AppProfile.General;

    /// <summary>今のアプリで Meltype が動くか。アプリ別設定で OFF・種類「ゲーム」なら false (キーはすべてアプリに渡す)。</summary>
    public bool AppEnabled { get; private set; } = true;

    /// <summary>
    /// 入力欄がどのアプリのものかを伝える (Mac の activateServer)。アプリ別設定 (プロセス名の欄に bundle ID を書く) から種類を決める。
    /// 種類が「コード」のアプリ・独自の種類で「最初は英数」にしたアプリは、英数 (直接入力) から始める。
    /// </summary>
    public void SetApp(string? appName)
    {
        var settings = _settings();
        AppName = string.IsNullOrEmpty(appName) ? null : appName;
        AppProfile = settings.ProfileFor(AppName);
        if (AppProfile == AppProfile.General && AppName is not null && !settings.HasAppRule(AppName) &&
            MacCodeApps.Contains(AppName, StringComparer.OrdinalIgnoreCase))
        {
            AppProfile = AppProfile.Code;
        }
        AppEnabled = settings.IsAppEnabled(AppName) && AppProfile != AppProfile.Game;
        if (AppEnabled && (AppProfile == AppProfile.Code || settings.KindFor(AppName) is { StartInEnglish: true })) Direct = true;
    }

    /// <summary>ユーザー辞書に登録する (Mac の入力メニュー用)。登録できなければ理由を返す。辞書が無ければ null (何もしない)。</summary>
    public string? AddUserWord(string reading, string word) => _options.UserDictionary?.Add(reading, word);

    /// <summary>入力メニューに出す登録の提案 (提案待ちのうち新しい順に最大 <see cref="MaxSuggestionsShown"/> 件)。設定で OFF・提案が無ければ空。</summary>
    public IReadOnlyList<UserWord> PendingSuggestions() =>
        _options.Suggestions is { } suggestions && _settings().DictionarySuggest
            // メニューの「選択中の文字を登録」で先に登録された組は、提案から外す。
            ? suggestions.Pending(_settings().DictionarySuggestThreshold)
                .Where(w => _options.UserDictionary?.Lookup(w.Reading).Contains(w.Word) != true).Take(MaxSuggestionsShown).ToList()
            : [];

    public const int MaxSuggestionsShown = 3;

    /// <summary>提案を受けてユーザー辞書に登録し、提案待ちから消す。登録できなければ理由を返す (提案は残す)。</summary>
    public string? AcceptSuggestion(string reading, string word)
    {
        var error = AddUserWord(reading, word);
        if (error is null) _options.Suggestions?.Remove(reading, word);
        return error;
    }

    /// <summary>「登録しない」。その組はもう数えず、提案もしない。</summary>
    public void RejectSuggestion(string reading, string word) => _options.Suggestions?.Reject(reading, word);

    /// <summary>提案の履歴 (数えた回数・提案待ち・却下) をすべて消す。</summary>
    public void ClearSuggestions() => _options.Suggestions?.Clear();

    /// <summary>提案待ちができた直後に 1 行だけ出すヒント (1 日 1 回まで)。出さないときは null。確定のたびに呼んでよい。</summary>
    public string? TakeSuggestionHint()
    {
        if (_options.Suggestions is not { } suggestions || !_settings().DictionarySuggest) return null;
        return suggestions.TakeHint() is { } word ? $"『{DictionarySuggestions.DisplayWord(word.Word)}』を辞書に登録できます (入力メニューから)" : null;
    }

    /// <summary>
    /// 履歴・意味辞書で読みが分からない文字列 (漢字混じり) の読みを OS に尋ねる関数 (Mac の CFStringTokenizer)。無ければ null。
    /// 同音異義語は読み違えることがある。
    /// </summary>
    public Func<string, string?>? ReadingProvider { get; set; }

    /// <summary>再変換できる選択の長さの上限 (文字数)。Swift 側 (InputController) でも同じ値で先に弾く。</summary>
    public const int MaxReconvertLength = 200;

    /// <summary>
    /// 確定済みの文字列 (入力欄で選択されているもの) を読みに戻して変換を始める (Mac の選択 + Shift+Space)。
    /// 読みに戻せないとき・入力中・直接入力のときは Consumed = false (キーはアプリに渡す)。
    /// </summary>
    public SessionResult Reconvert(string text)
    {
        _host.Begin(null, false, null, null);
        // 段落のような長い選択を誤って再変換にかけない (読みに戻す処理と変換が重くなるため)。
        if (text.Length > MaxReconvertLength) return _host.Result(consumed: false);
        if (Direct || !AppEnabled || !_settings().Enabled || _controller.IsComposing || ReadingOf(text) is not { } reading ||
            !_controller.ReconvertKana(reading, text))
        {
            return _host.Result(consumed: false);
        }
        _controller.Pump();
        return _host.Result(consumed: true);
    }

    /// <summary>text の読み (ひらがな)。かなならそのまま、次に変換履歴 (直前の誤変換を直す用途で一番当たる)、意味辞書、OS の順。無ければ null。</summary>
    private string? ReadingOf(string text)
    {
        if (text.Length == 0) return null;
        var candidates = new[]
        {
            text,
            _options.History?.ReadingOf(text),
            _options.Meanings?.ReadingOf(text),
        };
        foreach (var candidate in candidates)
        {
            if (candidate is not null && ToHiraganaOnly(candidate) is { } kana) return kana;
        }
        // OS への問い合わせは最後 (精度が一番低いので)。例外は握りつぶして「読めない」にする。
        try
        {
            return ReadingProvider?.Invoke(text) is { } read && ToHiraganaOnly(read) is { } hiragana ? hiragana : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"再変換の読みを取れませんでした: {ex.Message}");
            return null;
        }
    }

    /// <summary>ひらがな・カタカナ・長音だけの文字列を、ひらがなにして返す。それ以外が混じっていれば null。</summary>
    private static string? ToHiraganaOnly(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'ぁ' and <= 'ゖ' or 'ー') builder.Append(c);
            else if (c is >= 'ァ' and <= 'ヶ') builder.Append((char)(c - 0x60));
            else return null;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>変換ボックスに何か入っているか。</summary>
    public bool IsComposing => _controller.IsComposing;

    /// <summary>
    /// キーを 1 つ処理する。before / after は入力欄のキャレットの前後の文字列 (分かれば。英語とも日本語とも読める語の判定と変換の文脈に使う)。
    /// </summary>
    public SessionResult HandleKey(int vk, char? ch, bool shift, bool control, bool alt, bool command, string? before = null, string? after = null)
    {
        if (ch is >= '\uF700' and <= '\uF8FF') ch = null; // 機能キーの文字 (右矢印 = U+F703 など)
        _host.Begin(ch, shift, before, after);
        // Apple 日本語入力と同じ Ctrl キーで表示モードを切り替える (変換ボックスが出ているときだけ)。
        // Mac では F キーが OS のショートカットに取られることがあるため。
        if (control && !alt && !command && !shift && _controller.IsComposing && CtrlShortcutToFunctionKey(vk) is { } functionKey)
        {
            vk = functionKey;
            ch = null;
            control = false;
        }
        var down = new KeyEvent(vk, ch ?? 0, false, false, false, Environment.TickCount64);
        // Ctrl・Option・Command と一緒のキーは、変換ボックスが空ならアプリの操作 (コピーなど) なので触らない。
        var modifier = control || alt || command;
        if (Direct || !AppEnabled || !_settings().Enabled)
        {
            // アプリに渡したキーでキャレットが動くかもしれないので、確定し直しの記録は捨てる (Windows 版と同じ)。
            _controller.ForgetLastCommit();
            return _host.Result(consumed: false);
        }
        // 英数へ切り替えるときなどに、Shift を押したことを変換ボックスにも伝える (Shift + 英字は大文字)。
        if (shift && _controller.IsComposing) Feed(new KeyEvent(VirtualKeys.LShift, 0, false, false, false, down.TimeMs));
        if (modifier && _controller.IsComposing) Feed(new KeyEvent(control ? VirtualKeys.LControl : VirtualKeys.LMenu, 0, false, false, false, down.TimeMs));

        var swallowed = Feed(down, e => !modifier && StartsComposition(e, ch, shift));
        // このキーをアプリに送り直した (= 使わなかった) なら、アプリに渡す。
        var consumed = swallowed && !_host.ReplayedCurrent;
        // アプリに渡したキーでキャレットが動くかもしれないので、確定し直しの記録は捨てる (Windows の MeltypeEngine と同じ)。
        if (!consumed && !VirtualKeys.IsModifier(vk)) _controller.ForgetLastCommit();
        Feed(down with { IsUp = true });
        if (modifier && _controller.IsComposing) Feed(new KeyEvent(control ? VirtualKeys.LControl : VirtualKeys.LMenu, 0, false, true, false, down.TimeMs));
        if (shift && _controller.IsComposing) Feed(new KeyEvent(VirtualKeys.LShift, 0, false, true, false, down.TimeMs));
        return _host.Result(consumed);
    }

    /// <summary>Ctrl + 文字を、同じ意味の F キーにする (J=F6 K=F7 L=F9 '=F10 は Apple と同じ、; = F8 は半角カナ用の独自割り当て)。</summary>
    private static int? CtrlShortcutToFunctionKey(int vk) => vk switch
    {
        0x4A => VirtualKeys.F6,
        0x4B => VirtualKeys.F7,
        0x4C => VirtualKeys.F9,
        VirtualKeys.Oem7 => VirtualKeys.F10,
        VirtualKeys.Oem1 => VirtualKeys.F8,
        _ => null,
    };

    /// <summary>キャレットが動いたかもしれないとき (マウスのクリックなど)。直前の語は確定し直さない。変換中の文字には触らない。</summary>
    public void ForgetLastCommit() => _controller.ForgetLastCommit();

    /// <summary>予測候補ウィンドウで候補をクリックしたとき。</summary>
    public SessionResult SelectPrediction(int index)
    {
        _host.Begin(null, false, null, null);
        _controller.SelectPrediction(index);
        return _host.Result(consumed: true);
    }

    /// <summary>フォーカスが外れたときなど。未確定の内容をそのまま確定する。</summary>
    public SessionResult CommitPending()
    {
        _host.Begin(null, false, null, null);
        _controller.CommitPending();
        _controller.ResetContext();
        return _host.Result(consumed: true);
    }

    /// <summary>候補ウィンドウで候補をクリックしたとき。</summary>
    public SessionResult SelectCandidate(int index)
    {
        _host.Begin(null, false, null, null);
        _controller.SelectCandidate(index);
        return _host.Result(consumed: true);
    }

    private bool Feed(KeyEvent e, Func<KeyEvent, bool>? starts = null)
    {
        var swallowed = _gate.OnKey(e, starts ?? (_ => false));
        _controller.Pump();
        return swallowed;
    }

    /// <summary>変換ボックスを開くキーか (Windows 版の MeltypeEngine.StartsComposition と同じ考え方)。</summary>
    private static bool StartsComposition(KeyEvent e, char? ch, bool shift)
    {
        if (ch is not { } c) return false;
        if (VirtualKeys.IsLetter(e.Vk) && char.IsAsciiLetter(c)) return true;
        // 句読点・かぎかっこ・長音・数字・記号 (Shift で打つものも)
        return CompositionController.StartsWithSymbol(c);
    }

    /// <summary>変換ボックスからの指示を集めて、1 回のキー入力の結果にまとめる。</summary>
    private sealed class Host : ICompositionHost
    {
        private readonly List<TextEdit> _commits = [];
        private int _pendingDelete;
        private string? _pendingDeleteText;
        private char? _char;
        private bool _shift;
        private string? _before, _after;
        private CompositionView? _view;
        private bool _hidden;

        public bool ReplayedCurrent { get; private set; }

        public void Begin(char? ch, bool shift, string? before, string? after)
        {
            _commits.Clear();
            _pendingDelete = 0;
            _pendingDeleteText = null;
            _char = ch;
            _shift = shift;
            _before = before;
            _after = after;
            ReplayedCurrent = false;
            _hidden = false;
        }

        public SessionResult Result(bool consumed)
        {
            if (_pendingDelete > 0) _commits.Add(new TextEdit(_pendingDelete, "", OriginalOrNull()));
            _pendingDelete = 0;
            _pendingDeleteText = null;
            return new SessionResult(consumed, _commits.ToList(), _hidden ? null : _view);
        }

        public void CommitText(string text)
        {
            _commits.Add(new TextEdit(_pendingDelete, text, OriginalOrNull()));
            _pendingDelete = 0;
            _pendingDeleteText = null;
        }

        public void DeleteBackward(int count) => _pendingDelete += count;

        /// <summary>消す文字数と保持した文字列の長さが合わないとき (1 引数版が混ざったとき) は、確かめようがないので null。</summary>
        private string? OriginalOrNull() => _pendingDeleteText is { } text && text.Length == _pendingDelete ? text : null;

        public void DeleteBackward(int count, string expected)
        {
            _pendingDelete += count;
            _pendingDeleteText = (_pendingDeleteText ?? "") + expected;
        }

        public void Replay(KeyEvent e)
        {
            // 送り直すのは「今処理しているキー」(押したとき)。修飾キーやキーを離したことは、OS がアプリに渡すので何もしない。
            if (e.IsDown && !VirtualKeys.IsModifier(e.Vk)) ReplayedCurrent = true;
        }

        public void Replay(MouseButtonEvent e)
        {
        }

        // U+F700〜U+F8FF は macOS の機能キー (矢印など) の文字。文字としては扱わない (変換ボックスに足さない)。
        public char? CharFromKey(KeyEvent e, bool shift) => e.Scan is > 0 and < 0x10000 and not (>= 0xF700 and <= 0xF8FF) ? (char)e.Scan : null;

        public bool IsShiftDown() => _shift;

        public void RequestSurroundingText(Action<string?, string?> callback) => callback(_before, _after);

        public void Show(CompositionView view)
        {
            _view = view;
            _hidden = false;
        }

        public void Hide()
        {
            _view = null;
            _hidden = true;
        }
    }
}
