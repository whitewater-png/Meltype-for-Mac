// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// Meltype キーボードを Windows につなぐ部分 (UI スレッドで作る)。
///   フック → CaptureGate → (BeginInvoke) → CompositionController → 変換ボックス表示 / SendInput で確定
/// </summary>
internal sealed class CompositionService : ICompositionHost, IDisposable
{
    private readonly Control _invoker;
    private readonly CompositionWindow _window = new();
    private readonly ModeIndicatorWindow _indicator = new();
    private readonly Func<bool> _showIndicator;
    private readonly Func<Config.CompositionPlacement> _placement;
    private readonly Func<Config.CompositionSize> _size;
    private readonly Func<bool> _directMode;
    private readonly MsImeKanjiConverter _converter = new();
    private readonly KeyInjector _injector = new();
    private readonly WinRtCandidates _windowsCandidates = new();
    // Mozc (同梱の mozc/meltype_mozc_helper.exe) と Microsoft IME を組み合わせた変換エンジン。
    private readonly MozcConverter _mozc = new(Path.Combine(AppContext.BaseDirectory, "mozc", "meltype_mozc_helper.exe"), Path.Combine(Config.AppPaths.DataDirectory, "mozc"));
    private HybridConverter _hybrid = null!;
    private readonly IME.Imm32ImeController _imm32 = new();
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 100 };
    private int _pumpScheduled;

    /// <param name="options">LiveConversion などの設定。Candidates を指定しなければ組み込みの補助辞書を読む。</param>
    public CompositionService(Control invoker, CompositionDetector detector, CompositionOptions options)
    {
        _invoker = invoker;
        Gate = new CaptureGate(SchedulePump);
        // 補助辞書・文脈の手がかり・学習データは、指定がなければ既定の場所から読む。
        var userDirectory = Config.AppPaths.UserDictionaryDirectory;
        History = options.History ?? new ConversionHistory(Config.AppPaths.ConversionHistoryFile);
        Languages = options.Languages ?? new LanguageMemory(Config.AppPaths.LanguageMemoryFile);
        detector.Memory = Languages;
        UserDictionary = options.UserDictionary ?? new UserDictionary(Config.AppPaths.UserDictionaryFile);
        _detector = detector;
        var resolved = new CompositionOptions
        {
            LiveConversion = options.LiveConversion,
            ShiftEnterNewline = options.ShiftEnterNewline,
            DirectMode = options.DirectMode,
            ClassifyDirect = options.ClassifyDirect,
            DirectDecided = options.DirectDecided,
            Candidates = options.Candidates ?? CandidateDictionary.Load(userDirectory),
            ContextRules = options.ContextRules ?? ContextRules.Load(userDirectory),
            History = History,
            UserDictionary = UserDictionary,
            MoreCandidates = options.MoreCandidates ?? (reading => _hybrid.Candidates(reading)),
            AutoCorrect = options.AutoCorrect,
            Level = options.Level,
            KanaInput = options.KanaInput,
            Misspellings = options.Misspellings ?? MisspellingDictionary.Load(userDirectory),
            Languages = Languages,
            Translations = options.Translations ?? TranslationDictionary.Load(),
            TranslationCandidates = options.TranslationCandidates,
            CandidateMeanings = options.CandidateMeanings,
            Meanings = options.Meanings ?? MeaningDictionary.Load(),
            RomajiTypos = options.RomajiTypos ?? RomajiTypoCorrector.Load(detector.Romaji),
            CorrectTypos = options.CorrectTypos,
            Punctuation = options.Punctuation,
            FullWidthSymbols = options.FullWidthSymbols,
            // Windows の変換ボックスは予測を描かない。見えない候補に Tab で入ってしまわないよう、予測は出さない。
            Prediction = () => false,
            TranslationHistory = options.TranslationHistory ?? new TranslationHistory(Config.AppPaths.TranslationHistoryFile),
        };
        _hybrid = new HybridConverter(options.Engine, _mozc, _converter, reading => _windowsCandidates.Get(reading));
        Controller = new CompositionController(Gate, detector, _hybrid, this, resolved);
        if (options.Engine() != Config.ConversionEngine.System && _mozc.IsInstalled) _mozc.WarmUp();
        _showIndicator = options.ModeIndicator;
        _placement = options.Placement;
        _size = options.Size;
        _directMode = options.DirectMode;
        var onFocus = options.ModeIndicatorOnFocus;
        Focus.TextInputEntered += () => { if (onFocus()) ShowMode(!_directMode()); };
        Focus.CaptureLost += Abandon;
        Controller.Committed += text => Diagnostics.Log.Decision($"確定: {Diagnostics.Log.Text(text.Length > 20 ? text[..20] + "…" : text)}");
        _tick.Tick += (_, _) => Safely(() => Controller.Tick(Environment.TickCount64));
        _tick.Start();
    }

    public CaptureGate Gate { get; }

    public CompositionController Controller { get; }

    /// <summary>入力言語による制限。フックで保留した後・タイマーで判定する前にも確かめる。</summary>
    public Func<bool> InputAllowed { get; set; } = () => true;

    /// <summary>選び直した変換の学習データ (トレイの「学習データをリセット」で消す)。</summary>
    public ConversionHistory History { get; }

    /// <summary>ユーザーが英字 / かなに直した語の学習 (トレイの「学習データをリセット」で消す)。</summary>
    public LanguageMemory Languages { get; }

    /// <summary>ユーザー辞書 (トレイの「ユーザー辞書...」で編集する)。</summary>
    public UserDictionary UserDictionary { get; }

    private readonly CompositionDetector _detector;

    /// <summary>
    /// ユーザー辞書の登録画面用: 入力された読み (ローマ字でもよい) をひらがなにする。
    /// </summary>
    public string ToReading(string input)
    {
        var text = input.Trim();
        return text.Any(char.IsAsciiLetter) ? _detector.Romaji.ConvertLenient(text.ToLowerInvariant(), final: true) : text;
    }

    /// <summary>ユーザー辞書の登録画面用: 語の読みを推測する (かなならそのまま、漢字は Microsoft IME の逆変換)。分からなければ空。</summary>
    public string GuessReading(string word)
    {
        if (word.All(c => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー')) return new string(word.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
        try
        {
            return _converter.Reading(word) ?? "";
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"読みを推測できませんでした: {ex.Message}");
            return "";
        }
    }

    /// <summary>ユーザー辞書の登録画面用: 読みの変換候補 (変換エンジンの結果 → Windows の候補一覧 → カタカナ)。</summary>
    public IReadOnlyList<string> SuggestWords(string reading)
    {
        var list = new List<string>();
        void Add(string? word) { if (!string.IsNullOrEmpty(word) && !list.Contains(word)) list.Add(word); }
        Add(_converter.Convert(reading));
        foreach (var word in _windowsCandidates.Get(reading, 30)) Add(word);
        Add(CompositionText.ToKatakana(reading));
        return list;
    }

    public FocusInspector Focus { get; } = new();

    public bool ConverterAvailable => _converter.IsAvailable;

    private void SchedulePump()
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        if (Interlocked.Exchange(ref _pumpScheduled, 1) == 1) return;
        _invoker.BeginInvoke(() =>
        {
            Volatile.Write(ref _pumpScheduled, 0);
            Safely(Controller.Pump);
        });
    }

    /// <summary>
    /// 変換ボックスの処理で例外が起きたら、未確定の内容と状態を捨てて、残りの入力はそのまま通す。
    /// 状態を捨てないと、次の打鍵でも同じ例外が起きて英字しか入力できなくなる。
    /// </summary>
    private void Safely(Action action)
    {
        try
        {
            if (!InputAllowed())
            {
                Controller.SuspendInput();
                return;
            }
            action();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error($"変換ボックスで例外が起きたので入力をリセットしました: {ex}");
            try { Controller.Reset(); } catch { }
            ReplayAll(Gate.Abort());
            Hide();
        }
    }

    /// <summary>フォーカス変更・クリックでキャレットが動いたとき (どのスレッドからでも呼べる)。直前の英語/日本語の文脈を忘れる。</summary>
    /// <summary>Meltype を通らずにキーが押された (キャレットが動いたかもしれない) とき (どのスレッドからでも呼べる)。</summary>
    public void ForgetLastCommit()
    {
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(Controller.ForgetLastCommit);
    }

    /// <summary>
    /// 入力先が変わった (別のウィンドウ・パスワード欄・入力欄でない所) とき (どのスレッドからでも呼べる)。
    /// 変換中の内容は確定せずに捨て、そのあと届いていたキー・クリックは変換ボックスを通さずにそのままアプリに通す。
    /// </summary>
    public void Abandon(string reason)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() => Safely(() =>
        {
            if (!Controller.Abandon(reason)) return;
            ReplayAll(Gate.Abort());
            Hide();
        }));
    }

    public void ResetContext()
    {
        if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(Controller.ResetContext);
    }

    /// <summary>自動切替を止めたときなど。未確定の内容を確定し、残りの入力を通す。</summary>
    public void Flush()
    {
        if (!InputAllowed())
        {
            Controller.SuspendInput();
            return;
        }
        Controller.CommitPending();
        ReplayAll(Gate.Abort());
        Hide();
    }

    private void ReplayAll(List<CapturedInput> inputs)
    {
        foreach (var input in inputs)
        {
            if (input.Key is { } key) Replay(key);
            else if (input.Mouse is { } mouse) Replay(mouse);
        }
    }

    // ---- ICompositionHost ----

    /// <summary>前面のアプリで、確定した文字を貼り付けで入れるか (設定の「貼り付けで入力するアプリ」)。</summary>
    public Func<bool> PasteCommit { get; set; } = () => false;

    public void CommitText(string text)
    {
        EnsureSystemImeClosed();
        if (PasteCommit() && TryPaste(text)) return;
        var inputs = new List<Native.INPUT>(text.Length * 2);
        foreach (var c in text)
        {
            inputs.Add(UnicodeInput(c, up: false));
            inputs.Add(UnicodeInput(c, up: true));
        }
        var array = inputs.ToArray();
        Native.SendAll(array, "確定文字列の入力");
    }

    private IDataObject? _savedClipboard;
    private System.Windows.Forms.Timer? _restoreClipboard;

    /// <summary>
    /// 確定した文字をクリップボード経由 (Ctrl+V) で入れる。1 文字ずつのキーとして送ると、最初の文字を打った数だけくり返すアプリがある
    /// (DaVinci Resolve、#5)。元のクリップボードの中身は、アプリが貼り付け終わるのを少し待ってから戻す。
    /// 続けて確定したときは、最初に取っておいた中身を戻す。クリップボードを使えなければ false (1 文字ずつ送る)。
    /// </summary>
    private bool TryPaste(string text)
    {
        if (text.Length == 0) return true;
        try
        {
            if (_restoreClipboard is null) _savedClipboard = Clipboard.GetDataObject();
            else _restoreClipboard.Stop();
            Clipboard.SetText(text);
            KeyInjector.SendShortcut(VirtualKeys.Control, 0x56); // Ctrl+V
            _restoreClipboard ??= new System.Windows.Forms.Timer { Interval = 500 };
            _restoreClipboard.Tick -= RestoreClipboard;
            _restoreClipboard.Tick += RestoreClipboard;
            _restoreClipboard.Start();
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or System.Threading.ThreadStateException)
        {
            Diagnostics.Log.Warn($"クリップボードを使えないので、1 文字ずつ送ります: {ex.Message}");
            return false;
        }
    }

    private void RestoreClipboard(object? sender, EventArgs e)
    {
        var timer = _restoreClipboard;
        _restoreClipboard = null;
        timer?.Dispose();
        try
        {
            if (_savedClipboard is { } saved) Clipboard.SetDataObject(saved, copy: true);
            else Clipboard.Clear();
        }
        catch (ExternalException ex)
        {
            Diagnostics.Log.Warn($"クリップボードの中身を戻せませんでした: {ex.Message}");
        }
        _savedClipboard = null;
    }

    /// <summary>
    /// 確定文字列を送る前に、アプリ側の Microsoft IME が閉じていることを確かめる。
    /// IME が開いたままだと、送り込んだ文字の一部を IME が抱え込み、後から来た文字と順番が入れ替わることがある
    /// (「やあやあ、私だよ」→「やあやあ、だよ私」)。
    /// </summary>
    private void EnsureSystemImeClosed()
    {
        try
        {
            if (!InputAllowed()) return;
            if (IME.ImeTarget.FromForeground() is not { } target) return;
            var state = _imm32.GetState(target);
            if (state.Mode != IME.ImeMode.Open) return;
            var closed = _imm32.TrySetOpen(target, false, null, 150);
            Diagnostics.Log.Warn($"確定時に Microsoft IME が ON になっていました ({state}) → {(closed ? "OFF にしてから入力します" : "OFF にできませんでした")}。");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"Microsoft IME の状態を確認できませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// Meltype がアプリへ送り直したキー (矢印・Ctrl の操作・確定し直しの BackSpace など)。
    /// 自分で送ったキーはフックに届かないので、今の行の追いかけ (コードのコメント判定) に使う。
    /// </summary>
    public event Action<KeyEvent>? KeyReplayed;

    /// <summary>送り直したクリック (キャレットが動く)。</summary>
    public event Action? MouseReplayed;

    public void Replay(KeyEvent e)
    {
        _injector.Inject([e]);
        KeyReplayed?.Invoke(e);
    }

    public void DeleteBackward(int count)
    {
        if (count <= 0) return;
        for (var i = 0; i < count; i++) KeyReplayed?.Invoke(new KeyEvent(VirtualKeys.Back, 0, false, false, false, 0));
        var events = new List<KeyEvent>(count * 2);
        for (var i = 0; i < count; i++)
        {
            events.Add(new KeyEvent(VirtualKeys.Back, 0, false, false, false, 0));
            events.Add(new KeyEvent(VirtualKeys.Back, 0, false, true, false, 0));
        }
        _injector.Inject(events);
    }

    public void RequestSurroundingText(Action<string?, string?> callback)
    {
        Focus.RequestSurroundingText((before, after) =>
        {
            if (_invoker.IsHandleCreated && !_invoker.IsDisposed) _invoker.BeginInvoke(() => Safely(() => callback(before, after)));
        });
    }

    public void Replay(MouseButtonEvent e)
    {
        var (button, data) = e.Message switch
        {
            Native.WM_LBUTTONDOWN => (Native.MOUSEEVENTF_LEFTDOWN, 0u),
            Native.WM_LBUTTONUP => (Native.MOUSEEVENTF_LEFTUP, 0u),
            Native.WM_RBUTTONDOWN => (Native.MOUSEEVENTF_RIGHTDOWN, 0u),
            Native.WM_RBUTTONUP => (Native.MOUSEEVENTF_RIGHTUP, 0u),
            Native.WM_MBUTTONDOWN => (Native.MOUSEEVENTF_MIDDLEDOWN, 0u),
            Native.WM_MBUTTONUP => (Native.MOUSEEVENTF_MIDDLEUP, 0u),
            Native.WM_XBUTTONDOWN => (Native.MOUSEEVENTF_XDOWN, e.MouseData >> 16),
            Native.WM_XBUTTONUP => (Native.MOUSEEVENTF_XUP, e.MouseData >> 16),
            _ => (0u, 0u),
        };
        if (button == 0) return;
        MouseReplayed?.Invoke();
        // 元のクリック位置で再生する (保留中にカーソルが動いていても同じ場所をクリックする)。
        var left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var width = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN) - 1);
        var height = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN) - 1);
        var input = new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.InputUnion
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = (int)Math.Round((e.X - left) * 65535.0 / width),
                    dy = (int)Math.Round((e.Y - top) * 65535.0 / height),
                    mouseData = data,
                    dwFlags = button | Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK,
                    dwExtraInfo = KeyboardMonitor.InjectedMarker,
                },
            },
        };
        Native.SendAll([input], "クリックの再生");
    }

    public char? CharFromKey(KeyEvent e, bool shift) => KeyText.CharFromKey(e.Vk, e.Scan, shift);

    /// <summary>
    /// 入力モード (日本語なら「あ」、英数なら「A」) をカーソルの近くに一瞬出す。どのスレッドから呼んでもよい。
    /// 変換ボックスが出ているときは出さない。
    /// </summary>
    public void ShowMode(bool japanese)
    {
        if (!_invoker.IsHandleCreated || _invoker.IsDisposed) return;
        _invoker.BeginInvoke(() => Safely(() =>
        {
            if (!_showIndicator() || _window.Visible) return;
            var anchor = FindAnchor();
            _indicator.Flash(japanese, new Point(anchor.X + 2, anchor.Y + 2));
        }));
    }

    public bool IsShiftDown() => (Native.GetAsyncKeyState(VirtualKeys.Shift) & 0x8000) != 0;

    /// <summary>入力位置の高さとして信じる上限 (ピクセル)。これより高いのは入力欄や行全体の四角形。</summary>
    private const int MaxLineHeight = 48;

    public void Show(CompositionView view)
    {
        if (_window.Visible)
        {
            _window.ShowView(view, null);
            return;
        }
        var caret = FindCaret();
        // 入力欄が空のとき、アプリによっては入力位置ではなく入力欄の枠 (40px の欄など) や、複数行の欄全体の四角形が返る。
        // 枠と同じ高さなら、文字の高さは枠のおよそ半分 (1 行の欄の文字は上下の真ん中にある)。高すぎる四角形は、文字の高さが分からない。
        var element = Focus.Current.Bounds;
        var wholeField = caret is { Height: > 22 } c0 && element is { } e && Math.Abs(e.Height - c0.Height) <= 6;
        int? textHeight = caret switch
        {
            null => null,
            { Height: > MaxLineHeight } => null,
            { } c when wholeField => (int)(c.Height * 0.45),
            { } c => c.Height,
        };
        Diagnostics.Log.Info($"変換ボックスを出す入力位置: {(caret is { } r ? $"{r.X},{r.Y} 高さ {r.Height}{(wholeField ? " (入力欄の枠)" : "")}" : "分からない")}");
        // 文字の大きさ: 自動なら、入力欄の文字の高さに合わせる。小さな入力欄で大きく出すぎないように。
        _window.SetScale(_size() switch
        {
            Config.CompositionSize.Small => 0.8F,
            Config.CompositionSize.Large => 1.25F,
            Config.CompositionSize.Auto when textHeight is { } h && h >= 8 => Math.Clamp((float)h / _window.BaseTextHeight, 0.7F, 1.4F),
            // 文字の高さが分からないときは、ふつうの画面の文字 (16px 前後) に近い大きさ
            Config.CompositionSize.Auto => 0.8F,
            _ => 1F,
        });
        // Windows の検索・スタートメニューは、ふつうのアプリより上の特別な層に出るので、変換ボックスが隠れてしまう。
        // 検索の画面の右に出す。
        if (ShellSearchBounds() is { } search)
        {
            // 右に入らなければ左に出す (はみ出した分を画面の中に戻すと、スタートメニューの裏に隠れる)。
            var screen = Screen.FromRectangle(search).WorkingArea;
            var width = _window.Width;
            var left = search.Right + 8 + width <= screen.Right || search.Left - 8 - width < screen.Left ? search.Right + 8 : search.Left - 8 - width;
            Diagnostics.Log.Info($"Windows の検索の画面: {search} (変換ボックスはその{(left > search.Left ? "右" : "左")}に出す)");
            _window.ShowView(view, new Point(left, (caret?.Bottom ?? search.Bottom) - 4));
            return;
        }
        // 入力位置に重ねる: 変換ボックスの文字の行を、入力位置の行の高さの真ん中にそろえる。
        if (_placement() == Config.CompositionPlacement.Overlay && caret is { } at)
        {
            var offset = _window.TextOffset;
            // 1 行の入力欄の枠なら、その上下の真ん中。複数行の欄全体 (高すぎる) なら、上端の 1 行目。
            var middle = at.Height <= MaxLineHeight ? at.Height / 2 : offset.Y - 8;
            _window.ShowView(view, new Point(at.Left - offset.X, at.Top + middle - offset.Y), overlay: true);
            return;
        }
        _window.ShowView(view, FindAnchor(caret));
    }

    public void Hide()
    {
        if (_window.Visible) _window.Hide();
    }

    // Windows の検索・スタートメニューの画面のプロセス (Windows 10 / 11)
    private static readonly HashSet<string> ShellSearchProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost", "SearchApp", "SearchUI", "StartMenuExperienceHost",
    };

    /// <summary>
    /// 前面が Windows の検索・スタートメニューなら、その画面の四角形。それ以外は null。
    /// スタートメニューは、検索の欄 (SearchHost) と、その横の画面 (「モバイル デバイスを表示」の欄など、StartMenuExperienceHost) に
    /// 分かれていることがあるので、同じ画面に出ている両方の窓を合わせた四角形にする (片方の右に出すと、もう片方の裏に隠れる、#18)。
    /// </summary>
    private static Rectangle? ShellSearchBounds()
    {
        var foreground = Native.GetForegroundWindow();
        if (!IsShellSearchWindow(foreground) || !Native.GetWindowRect(foreground, out var rect)) return null;
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var screen = Screen.FromRectangle(bounds).Bounds;
        var union = bounds;
        var shell = ShellSearchProcessIds();
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (hwnd != foreground && shell.Contains(pid) && Native.IsWindowVisible(hwnd) && !IsCloaked(hwnd) && Native.GetWindowRect(hwnd, out var r))
            {
                var other = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                // 画面の外に置いてある窓・大きさの無い窓・画面全体の窓 (隠れている検索の画面) は含めない
                if (other.Width > 0 && other.Height > 0 && screen.IntersectsWith(other) && other != screen) union = Rectangle.Union(union, Rectangle.Intersect(other, screen));
            }
            return true;
        }, IntPtr.Zero);
        return union;
    }

    private static bool IsShellSearchWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var processId);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return ShellSearchProcesses.Contains(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<uint> ShellSearchProcessIds()
    {
        var ids = new HashSet<uint>();
        foreach (var name in ShellSearchProcesses)
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                ids.Add((uint)process.Id);
                process.Dispose();
            }
        }
        return ids;
    }

    /// <summary>見えない窓 (スタートメニューを閉じているときの窓など) は、表示中でも DWM が「隠している」(cloaked)。</summary>
    private static bool IsCloaked(IntPtr hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>
    /// キャレット (入力位置) の画面上の四角形。Windows のキャレット (メモ帳など) → UI Automation (Chrome・Discord など) の順に試す。
    /// </summary>
    private Rectangle? FindCaret()
    {
        var foreground = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(foreground, out _);
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (Native.GetGUIThreadInfo(thread, ref info) && info.hwndCaret != IntPtr.Zero)
        {
            var top = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            if (Native.ClientToScreen(info.hwndCaret, ref top) && (top.X != 0 || top.Y != 0))
            {
                return new Rectangle(top.X, top.Y, 1, Math.Max(1, info.rcCaret.Bottom - info.rcCaret.Top));
            }
        }
        return Focus.CaretBounds() is { Width: > 0, Height: > 0 and < 200 } bounds && (bounds.X != 0 || bounds.Y != 0) ? bounds : null;
    }

    /// <summary>変換ボックスを入力位置の下に出すときの位置。キャレット → 入力欄の左下 → マウスカーソルの順に試す。</summary>
    private Point FindAnchor(Rectangle? caret = null)
    {
        if ((caret ?? FindCaret()) is { } found) return new Point(found.Left, found.Bottom + 4);
        if (Focus.Current.Bounds is { } bounds && bounds.Height is > 0 and < 120)
        {
            return new Point(bounds.Left, bounds.Bottom + 2);
        }
        Native.GetCursorPos(out var cursor);
        return new Point(cursor.X + 12, cursor.Y + 20);
    }

    private static Native.INPUT UnicodeInput(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = KeyboardMonitor.InjectedMarker,
            },
        },
    };

    public void Dispose()
    {
        _tick.Dispose();
        Focus.Dispose();
        _converter.Dispose();
        _windowsCandidates.Dispose();
        _mozc.Dispose();
        _window.Dispose();
        _indicator.Dispose();
    }
}
