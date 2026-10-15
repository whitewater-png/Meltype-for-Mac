// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>
/// 変換ボックスに表示する内容。変換中は文節ごとの文字列 (Clauses) と選択中の文節、その文節の候補を持つ。
/// </summary>
public sealed record CompositionView(
    string Text,
    IReadOnlyList<string> Candidates,
    int SelectedIndex,
    bool Converting,
    string Hint,
    IReadOnlyList<string>? Clauses = null,
    int SelectedClause = -1,
    IReadOnlyList<string?>? Notes = null,
    string? Meaning = null,
    string? Suggestion = null,
    IReadOnlyList<string>? Predictions = null,
    int SelectedPrediction = -1);

/// <summary>CompositionController が外界とやり取りする口。テストでは偽物に差し替える。</summary>
public interface ICompositionHost
{
    /// <summary>確定した文字列を、フォーカスのあるテキストボックスへ入力する。</summary>
    void CommitText(string text);

    /// <summary>握りつぶしていた打鍵を、そのままアプリへ送り直す。</summary>
    void Replay(KeyEvent e);

    /// <summary>キャレットの前の文字を count 文字消す (確定し直すとき)。</summary>
    void DeleteBackward(int count);

    /// <summary>
    /// DeleteBackward に、消すはずの文字列 (expected) を添える。入力欄の範囲が不正確なアプリ (Google ドキュメントなど) で、
    /// 消す前に中身を確かめられるホスト用。既定は expected を無視する (Windows 版はこれまでどおり)。
    /// </summary>
    void DeleteBackward(int count, string expected) => DeleteBackward(count);

    void Replay(MouseButtonEvent e);

    /// <summary>その打鍵で入力される文字 (記号・数字を含む)。文字を生まないキーなら null。</summary>
    char? CharFromKey(KeyEvent e, bool shift);

    /// <summary>今 Shift キーが押されているか (かな入力の小書き文字・句読点の判定に使う)。</summary>
    bool IsShiftDown() => false;

    /// <summary>
    /// 入力欄のキャレットの前後の文字列 (確定済みの文字) を取りに行く。結果は後から UI スレッドで callback(前, 後ろ) に渡す。
    /// 取れなければ null を渡す。
    /// </summary>
    void RequestSurroundingText(Action<string?, string?> callback);

    void Show(CompositionView view);

    void Hide();
}

/// <summary>CompositionController の設定と、外の判定器へのつなぎ。</summary>
public sealed class CompositionOptions
{
    /// <summary>打ったそばから漢字に変換して見せるか。</summary>
    public Func<bool> LiveConversion { get; init; } = () => false;

    /// <summary>かな漢字変換のエンジン (Windows の CompositionService が Mozc / Microsoft IME を選ぶのに使う)。</summary>
    public Func<Config.ConversionEngine> Engine { get; init; } = () => Config.ConversionEngine.System;

    /// <summary>
    /// Space で変換したあと、続けて文字を打っても確定せず、打った文字を未変換の文節として末尾に足して編集・変換を続けるか。
    /// 既定は false (今までどおり、確定して新しい入力を始める)。
    /// </summary>
    public Func<bool> ContinueAfterConversion { get; init; } = () => false;

    /// <summary>
    /// Space で変換した直後 (候補をまだ動かしていない間) に打った数字を、候補の番号ではなく数字の入力として扱うか。
    /// 区切りのつもりで押した Space の後の数字 (denwa wa 03-1234、kyou wa 12) が、候補の選択になって消えるのを防ぐ。既定は false (今までどおり)。
    /// </summary>
    public Func<bool> TypeDigitsAfterConversion { get; init; } = () => false;

    /// <summary>
    /// 変換中の Shift+Enter を、確定したうえでキーをアプリに渡す (チャットや Web アプリで 1 回で確定 + 改行) か。
    /// 既定は true。false のときは Enter 単体と同じく確定のみ。変換ボックスが空のときの Shift+Enter は、どちらでも素通し。
    /// </summary>
    public Func<bool> ShiftEnterNewline { get; init; } = () => true;

    /// <summary>
    /// ATOK スタイルのキー操作 (変換中)。← で文節を縮める (区切りを前へ)、→ で文節を伸ばす (区切りをうしろへ)、
    /// ↓ で選んでいる文節までを確定 (部分確定)、Shift+← → で文節を移る、Ctrl+← → で先頭 / 最後の文節へ (MeltypeSession)。
    /// 既定は false (今までどおり ← → で文節を移り、↓ で次の候補、Shift+← → で区切りを変える)。
    /// </summary>
    public Func<bool> TokMode { get; init; } = () => false;

    /// <summary>
    /// 入力中の Tab で、Space と同じく変換を始めるか (本家の「Tab で変換」。issue #219)。もしかして・手動の提案・予測候補が出ているときは今までどおりそちらに使う。
    /// 既定は false (今までどおり、確定して Tab をアプリに渡す)。
    /// </summary>
    public Func<bool> TabConversion { get; init; } = () => false;

    /// <summary>
    /// 今のアプリがコード系 (ターミナル・エディタ。AppProfile.Code) か。コード系の行頭では、コマンド名 (cd・ls・make) を確定で英字にする (K79)。
    /// 既定は false (一般アプリでは何も変えない。Windows 側は渡さない)。
    /// </summary>
    public Func<bool> CodeApp { get; init; } = () => false;

    /// <summary>
    /// 確定した直後の語を直す Ctrl+' (英字) / Ctrl+J (かな) を、F10 / F6 と同じに読み替えるか (MeltypeSession)。
    /// 既定は false: Ctrl+J はターミナルの改行と重なり、Ctrl は変換ボックスが空ならアプリの操作なので、利用者が選んだときだけ。
    /// </summary>
    public Func<bool> ToggleLastWordCtrlKeys { get; init; } = () => false;

    /// <summary>英数 (直接入力) 状態か。</summary>
    public Func<bool> DirectMode { get; init; } = () => false;

    /// <summary>
    /// 英数状態で打ち始めた文字がローマ字 (日本語) かを判定する。null なら英数状態では判定しない。
    /// 引数は (打った英字, もう続きが無いか)。
    /// </summary>
    public Func<string, bool, Verdict>? ClassifyDirect { get; init; }

    /// <summary>英数状態での判定結果を知らせる (true: 日本語だったので日本語入力に戻った / false: 英語だった)。</summary>
    public Action<bool>? DirectDecided { get; init; }

    /// <summary>同音異義語などの補助候補。</summary>
    public CandidateDictionary? Candidates { get; init; }

    /// <summary>前後の文字列の手がかりで候補を選ぶ規則 (気温 → 暑い)。</summary>
    public ContextRules? ContextRules { get; init; }

    /// <summary>ユーザーが選び直した変換の記録 (次から最初の候補にする)。</summary>
    public ConversionHistory? History { get; init; }

    /// <summary>
    /// 読みに対する変換候補の一覧 (Windows の変換候補 API)。時間がかかることがあるので、
    /// 候補を切り替え始めたときだけ呼ぶ。null なら補助辞書の候補だけ。
    /// </summary>
    public Func<string, IReadOnlyList<string>>? MoreCandidates { get; init; }

    /// <summary>読みに対する予測候補 (読み → 続きの語)。変換エンジンの予測 (Mac: azooKey) をつなぐ。null なら出さない。</summary>
    public Func<string, IReadOnlyList<string>>? Predictions { get; init; }

    /// <summary>予測変換を出すか (設定)。</summary>
    public Func<bool> Prediction { get; init; } = () => true;

    /// <summary>予測変換を出し始める読みの文字数 (設定)。</summary>
    public Func<int> PredictionMinLength { get; init; } = () => 2;

    /// <summary>ユーザー辞書への登録提案 (選び直した変換を数える)。null なら数えない。</summary>
    public DictionarySuggestions? Suggestions { get; init; }

    /// <summary>登録提案をするか (設定)。</summary>
    public Func<bool> DictionarySuggest { get; init; } = () => true;

    /// <summary>登録を提案するまでの確定回数 (設定)。</summary>
    public Func<int> DictionarySuggestThreshold { get; init; } = () => 3;

    /// <summary>英訳の候補 (複雑な → complex)。null なら出さない。</summary>
    public TranslationDictionary? Translations { get; init; }

    /// <summary>英訳の候補を出すか (設定)。</summary>
    public Func<bool> TranslationCandidates { get; init; } = () => true;

    /// <summary>候補の日本語の意味 (ウィクショナリー)。null なら英訳だけ。</summary>
    public MeaningDictionary? Meanings { get; init; }

    /// <summary>変換中に選んでいる候補の意味 (英訳) を変換ボックスに渡すか (設定)。</summary>
    public Func<bool> CandidateMeanings { get; init; } = () => true;

    /// <summary>選んだ英訳の記録 (普通の変換の学習より弱く効かせる)。</summary>
    public TranslationHistory? TranslationHistory { get; init; }

    /// <summary>英語とも日本語とも読める語を、次の語の文脈に合わせて確定し直すか。</summary>
    public Func<bool> AutoCorrect { get; init; } = () => true;

    /// <summary>ユーザー辞書 (変換で最優先に使う)。</summary>
    public UserDictionary? UserDictionary { get; init; }

    /// <summary>英語か日本語かの自動判定の強さ。</summary>
    public Func<DetectionLevel> Level { get; init; } = () => DetectionLevel.Balanced;

    /// <summary>かな入力 (JIS) か。</summary>
    public Func<bool> KanaInput { get; init; } = () => false;

    /// <summary>よくある書き間違い (ブレスレッド → ブレスレット) の辞書。「もしかして」に使う。null なら出さない。</summary>
    public MisspellingDictionary? Misspellings { get; init; }

    /// <summary>ローマ字の打ち間違いを直すもの (onegaishimsu → お願いします)。null なら直さない。</summary>
    public RomajiTypoCorrector? RomajiTypos { get; init; }

    /// <summary>ローマ字の打ち間違いを直すか (設定)。</summary>
    public Func<bool> CorrectTypos { get; init; } = () => true;

    /// <summary>日本語の中で打った , . の句読点の表記 (設定)。</summary>
    public Func<PunctuationStyle> Punctuation { get; init; } = () => PunctuationStyle.Japanese;

    /// <summary>! ? ~ などの記号を全角にするか (設定)。</summary>
    public Func<bool> FullWidthSymbols { get; init; } = () => true;

    /// <summary>日本語の中で打った ( ) の幅 (設定)。</summary>
    public Func<ParenthesisWidth> Parentheses { get; init; } = () => ParenthesisWidth.HalfWidth;

    /// <summary>日本語の中で打った { } の幅 (設定)。</summary>
    public Func<BraceWidth> Braces { get; init; } = () => BraceWidth.FullWidth;

    /// <summary>確定するときに、日本語と英単語の間に半角スペースを入れるか (設定)。</summary>
    public Func<bool> SpaceAroundEnglish { get; init; } = () => false;

    /// <summary>ユーザーが英字 / かなに直した語の学習。</summary>
    public LanguageMemory? Languages { get; init; }

    /// <summary>入力欄に入ったときなどに、入力モード (あ / A) をカーソルの近くに出すか。</summary>
    public Func<bool> ModeIndicator { get; init; } = () => false;

    /// <summary>変換ボックスを出す位置 (入力位置に重ねる / カーソルの下)。</summary>
    public Func<Config.CompositionPlacement> Placement { get; init; } = () => Config.CompositionPlacement.Overlay;

    /// <summary>変換ボックスの文字の大きさ。</summary>
    public Func<Config.CompositionSize> Size { get; init; } = () => Config.CompositionSize.Auto;

    /// <summary>入力欄に入った (フォーカスが入った) ときにも入力モードを出すか。false なら 半角/全角 を押したときだけ。</summary>
    public Func<bool> ModeIndicatorOnFocus { get; init; } = () => true;
}

/// <summary>
/// Meltype キーボードの本体。変換ボックス (未確定文字列) を持ち、
///   文字キー → ボックスに追加 (日本語ならかな、英単語なら英字で自動表示)
///   Space   → 文節に区切って漢字変換 (英単語で終わっているときは確定して空白)
///   Shift+Space → 英単語と判定した語も、ローマ字として読んで変換 (go → 語)
///   ←→      → 文節を選ぶ (変換前に押しても文節の選択に入る) / Space・↓↑ でその文節の候補 / Shift+←→ で区切りを変える
///   Shift+↓   → 入力中: 先頭の 1 音だけ確定 (ATOK の 1 音確定。ATOK スタイルのキー操作 (TokMode) が ON のとき)
///   Enter   → 確定してテキストボックスへ入力
///   BackSpace / Esc → 1 音削除 / 変換取り消し・入力取り消し
///   F6 / F7 / F8 / F9 / F10, 半角/全角 → ひらがな / カタカナ / 全角英数 / 半角英数 / 日本語⇔英字
///   その他のキー・クリック → 確定してからそのキーやクリックを通す
/// 英数状態でも、打ち始めの数文字でローマ字 (日本語) かを判定し (打鍵は待たせずに送る)、日本語なら送った分を消して日本語入力に戻し、変換ボックスに入れる。
/// UI スレッドだけで動く。フックからは CaptureGate 経由で入力が順番どおり届く。
/// </summary>
public sealed class CompositionController
{
    /// <summary>
    /// ライブ変換は 4 文字以上のかなだけ。短い断片は変換エンジンが的外れな漢字を返しやすい
    /// (きょ → 居, きょう → 喬) ので、打っている途中はかなのまま見せる。短い語は Space で変換する。
    /// </summary>
    private const int LiveConversionMinLength = 4;

    /// <summary>英数状態の判定で、この時間打鍵が無ければ英語とみなして判定をやめる。</summary>
    private const long DirectHoldIdleMs = 700;

    /// <summary>自分が確定してから、この時間内はアプリ側のテキストがまだ更新されていないかもしれないので自分の記録を優先する。</summary>
    private const long OwnCommitTrustMs = 1500;

    private readonly CaptureGate _gate;
    private readonly CompositionText _text;
    private readonly CompositionDetector _detector;
    private readonly IKanjiConverter _converter;
    private readonly ICompositionHost _host;
    private readonly CompositionOptions _options;
    private readonly Dictionary<string, string> _conversionCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _liveCache = new(StringComparer.Ordinal);
    // 変換エンジンの学習が終わるたびに進む (学習は裏のスレッドで走るので、終わったあとに変換結果のキャッシュを捨てるための目印)
    private int _learnGeneration;
    private int _cacheGeneration;

    /// <summary>学習が終わっていれば、変換結果のキャッシュを捨てる (学習の前の結果を使い続けないため)。</summary>
    private void DropCachesIfLearned()
    {
        var generation = Volatile.Read(ref _learnGeneration);
        if (generation == _cacheGeneration) return;
        _conversionCache.Clear();
        _liveCache.Clear();
        _cacheGeneration = generation;
    }
    private readonly HashSet<int> _swallowedShift = [];
    private readonly HashSet<int> _replayedDown = [];
    private readonly HashSet<int> _capturedDown = [];
    private List<Clause> _clauses = [];
    private int _selectedClause;
    private bool _converting;
    // 変換後も続けて入力 (ContinueAfterConversion): _clauses の先頭 _headCount 個は、選んだ候補のまま固定した文節。
    // 続けて打った文字は、固定した文節とは別の _text (未変換の文節) に入る。0 なら今までどおり。
    // 変換していない間 (_converting == false) の _clauses は、固定した文節だけ。
    private int _headCount;
    private List<CompositionUnit> _fixedUnits = [];
    // 固定する前の、_text の前の文字の英語/日本語の手がかり (読みに戻すときに戻す)。
    private bool? _basePrecedingEnglish;
    private bool _basePrecedingSentence;
    private string? _basePrecedingTail;
    // 予測変換: 候補の一覧と、Tab で候補に入っているか・選んでいる位置。
    private bool _predicting;
    private int _predictionIndex;
    private List<string> _predictions = [];
    private bool? _lastCommitEnglish;
    private string? _lastCommitText;
    private string? _precedingText;
    private string? _followingText;
    private long _lastCommitTime = long.MinValue / 2;
    private int _compositionId;

    // 英数状態で判定中の語。打鍵はすぐアプリに送り (待たせない)、ローマ字と分かったら消して変換ボックスに入れ直す。
    private readonly StringBuilder _heldLetters = new();
    private int _heldSent;
    private long _heldLastKeyTime;

    /// <summary>変換中の文節。英語の区間も 1 つの文節として扱う (候補は英字/全角英字)。</summary>
    private sealed class Clause(string reading, bool isEnglish, List<string> candidates)
    {
        public string Reading { get; set; } = reading;
        public bool IsEnglish { get; } = isEnglish;
        public List<string> Candidates { get; set; } = candidates;
        public int Index { get; set; }
        public string Text => Candidates[Index];

        /// <summary>ユーザーが候補を選び直したか (確定時に学習する)。</summary>
        public bool Changed { get; set; }

        /// <summary>Windows の変換候補 API の候補を足したか。</summary>
        public bool Expanded { get; set; }

        /// <summary>この文節の読みを打ったときの英字 (あぴ → api)。分からなければ null。</summary>
        public string? Raw { get; set; }

        /// <summary>候補のうち英訳 (複雑な → complex) のもの。</summary>
        public HashSet<string> Translations { get; } = new(StringComparer.Ordinal);
    }

    public CompositionController(CaptureGate gate, CompositionDetector detector, IKanjiConverter converter, ICompositionHost host, CompositionOptions? options = null)
    {
        _gate = gate;
        _text = new CompositionText(detector);
        _detector = detector;
        _converter = converter;
        _host = host;
        _options = options ?? new CompositionOptions();
        _text.CodeApp = () => _options.CodeApp();
        _text.Level = () => _options.Level();
        _text.TypoCorrector = _options.RomajiTypos;
        // よく使う日本語の読み (kyouha = 今日は) は、一度英字にして確定しただけでは英語として覚えない。
        if (_options.Languages is { } languages && _options.RomajiTypos is { } typos) languages.IsCommonJapanese ??= typos.IsCommonJapanese;
        if (_options.Languages is { } memory) memory.IsReadableRomaji ??= word => detector.Romaji.AnalyzeFragment(word) is { IsValid: true, Partial: "" };
        if (_options.RomajiTypos is { } lexicon) detector.IsCommonJapanese ??= lexicon.IsCommonJapanese;
        _text.CorrectTypos = () => _options.CorrectTypos();
        _text.Punctuation = () => _options.Punctuation();
        _text.FullWidthSymbols = () => _options.FullWidthSymbols();
        _text.Parentheses = () => _options.Parentheses();
        _text.Braces = () => _options.Braces();
    }

    /// <summary>変換ボックスに入力中か、英数状態で打ち始めの語を判定中か。</summary>
    /// <summary>今の変換ボックスの、確定する前 (Enter の前) の自動の区間分け (「判定を調べる」の答えと突き合わせるテスト用)。</summary>
    internal IReadOnlyList<CompositionSegment> AutomaticSegments() => _text.Segments(final: true);

    public bool IsComposing => !_text.IsEmpty || _heldLetters.Length > 0 || _headCount > 0;

    /// <summary>直近に確定した文字列 (テスト・ログ用)。</summary>
    public event Action<string>? Committed;

    /// <summary>変換中の文節があるか。</summary>
    internal bool IsConverting => _converting && _clauses.Count > 0;

    /// <summary>
    /// ATOK スタイル (TokMode) の Ctrl+← / Ctrl+→: 先頭 / 最後の文節を選ぶ (MeltypeSession が呼ぶ。Ctrl は
    /// ふだん「確定してショートカットへ」なので、変換ボックスの外で受ける)。変換中でなければ何もしない。
    /// </summary>
    internal void SelectEdgeClause(bool last)
    {
        if (!IsConverting) return;
        _candidatesMoved = true;
        _selectedClause = last ? _clauses.Count - 1 : 0;
        UpdateView();
    }

    /// <summary>選んでいる文節を delta だけ動かす (端では止まる)。</summary>
    private void MoveClauseBy(int delta) => _selectedClause = Math.Clamp(_selectedClause + delta, 0, _clauses.Count - 1);

    /// <summary>
    /// ATOK スタイル (TokMode) の ↓ (部分確定): 先頭から選んでいる文節までを確定し、残りの文節は変換中のまま残して先頭を選ぶ。
    /// 最後の文節を選んでいれば、全部を確定する (Enter と同じ)。
    /// 変換後も続けて入力 (固定した文節) ・選んだ文の再変換の途中は、残す読みの扱いが合わないので、全部を確定する。
    /// </summary>
    private void CommitThroughSelectedClause()
    {
        var count = _selectedClause + 1;
        if (count >= _clauses.Count || _headCount > 0 || _reconvertRomaji || _reconvertOriginal is not null)
        {
            Commit();
            return;
        }
        var committed = _clauses.Take(count).ToList();
        var rest = _clauses.Skip(count).ToList();
        var text = string.Concat(committed.Select(c => c.Text));
        // 確定する文節だけを学習する (Learn は _clauses を見る)
        _clauses = committed;
        Learn();
        // 残りの読み: 打った単位 (打った英字つき) をそのまま残せればそうする (F10 で打ったままの英字に戻せる)。合わなければ読みだけ。
        if (!_text.RemoveLeading(committed.Select(c => (c.Reading, c.IsEnglish))))
        {
            var mode = _text.Mode;
            _text.SetKana(string.Concat(rest.Select(c => c.Reading)));
            _text.Mode = mode;
        }
        var english = committed.All(c => c.IsEnglish);
        CommitPart(text, english);
        _clauses = rest;
        _selectedClause = 0;
        _converting = true;
        _candidatesMoved = false;
        // 確定した分は、残りの変換と学習の文脈 (キャレットの前) になる
        _precedingText = (_precedingText ?? "") + text;
        _text.PrecedingEnglish = english;
        _text.PrecedingTail = _precedingText;
    }

    /// <summary>
    /// 変換中の一部だけを確定して入力する (部分確定)。変換ボックスの中身は呼び出し側が残す。
    /// 確定し直し (英語とも日本語とも読める語) の対象にはしない。
    /// </summary>
    private void CommitPart(string text, bool english)
    {
        _spaceStartedConversion = false;
        _correctable.Clear();
        _lastWord = null;
        if (text.Length == 0) return;
        if (_options.SpaceAroundEnglish()) text = AddSpacesAroundEnglish(text, _precedingText, null);
        _lastCommitEnglish = english;
        var joined = (_lastCommitText ?? "") + text;
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        _lastCommitTime = Environment.TickCount64;
        _host.CommitText(text);
        Committed?.Invoke(text);
    }

    /// <summary>キューにたまった入力をすべて処理する。UI スレッドで呼ぶ。</summary>
    public void Pump()
    {
        while (true)
        {
            while (_gate.TryDequeue(out var input))
            {
                if (input.Key is { } key) HandleKey(key);
                else if (input.Mouse is { } mouse) HandleMouse(mouse);
            }
            UpdateView();
            if (IsComposing) return;
            if (_gate.TryRelease())
            {
                // 以降のキーアップはフックを素通りしてアプリに直接届くので、追跡をやめる。
                _swallowedShift.Clear();
                _replayedDown.Clear();
                _capturedDown.Clear();
                return;
            }
            // 解放しようとした間に新しい入力が届いた。続けて処理する。
        }
    }

    /// <summary>定期的に呼ぶ。英数状態で判定中の語が、無入力のまま一定時間たったら英語とみなす。</summary>
    public void Tick(long nowMs)
    {
        if (_heldLetters.Length == 0 || nowMs - _heldLastKeyTime < DirectHoldIdleMs) return;
        DecideHeld(final: true);
        Pump();
    }

    /// <summary>変換中の文節の候補を番号で選ぶ (Mac の候補ウィンドウをクリックしたときなど)。</summary>
    public void SelectCandidate(int index)
    {
        if (!_converting || _clauses.Count == 0) return;
        var clause = _clauses[_selectedClause];
        if (index < 0 || index >= clause.Candidates.Count) return;
        clause.Index = index;
        clause.Changed = true;
        UpdateView();
    }

    /// <summary>予測候補を番号で選んで確定する (Mac の候補ウィンドウをクリックしたとき)。</summary>
    internal void SelectPrediction(int index)
    {
        if (_converting || index < 0 || index >= _predictions.Count) return;
        CommitPrediction(_predictions[index]);
        UpdateView();
    }

    /// <summary>
    /// 確定済みの文字列の読み (ひらがな) から変換ボックスを開き、そのまま変換を始める (再変換)。
    /// 入力中のときや、変換できなかったときは何もせず false。開いたら関所も閉じ、続くキーを変換中として扱う。
    /// </summary>
    internal bool ReconvertKana(string kana, string original)
    {
        if (kana.Length == 0 || IsComposing) return false;
        // 選んだことでキャレットは動いている。直前の語は、あとの F10 / F6 の対象にしない (ReconvertRomaji と同じ)
        _lastWord = null;
        BeginComposition();
        _reconvertOriginal = original;
        _text.SetKana(kana);
        StartConversion();
        if (!_converting)
        {
            _text.Clear();
            _reconvertOriginal = null;
            return false;
        }
        _gate.Capture();
        return true;
    }

    /// <summary>
    /// ローマ字のまま入った文か (英字を含み、半角の英数字・記号・空白だけ)。
    /// 本家 #332 (CompositionController.IsRomajiText) を取り込んだ。途中に改行・タブ・全角の文字を含むものは、今までの再変換に任せる。
    /// 選択の前後の空白・改行・タブは、<see cref="TrimLineEdges"/> で先に切り離してから渡す (ReconvertRomaji がやる)。
    /// </summary>
    internal static bool IsRomajiText(string text) =>
        text.Any(char.IsAsciiLetter) && text.All(c => c is >= ' ' and <= '~') && text.Trim().Length > 0;

    /// <summary>
    /// 選択の前後に付いた空白・改行 (\n・\r\n)・タブを切り離す: (前, 本体, 後)。段落を 3 回クリックすると末尾に改行が付き、Windows 系の文書は \r\n、表計算のセルはタブが付く。
    /// 本体の途中の改行・タブは切り離さない (複数行・複数セルの選択は <see cref="IsRomajiText"/> が偽にして、今までどおり候補を出さない)。
    /// 空白と改行・タブ以外 (全角空白・垂直タブ・U+2029 など) は切り離さない: 確信が持てない文字は、今までの判定に任せる。
    /// </summary>
    internal static (string Lead, string Core, string Trail) TrimLineEdges(string text)
    {
        static bool IsEdge(char c) => c is ' ' or '\n' or '\r' or '\t';
        var start = 0;
        while (start < text.Length && IsEdge(text[start])) start++;
        var end = text.Length;
        while (end > start && IsEdge(text[end - 1])) end--;
        return (text[..start], text[start..end], text[end..]);
    }

    /// <summary>
    /// ローマ字のまま入った文 (Meltype が止まっていた・英数のまま打った) を、読みに戻さず Meltype の判定で日本語にして、変換中の候補として見せる (Mac の選択 + Shift+Space)。
    /// 候補は 日本語にした文 → 選んだままの文。Enter で選んだ文字と置き換え、Esc / BackSpace で選んだままに戻す。
    /// 日本語だと言える所が無い (英文・URL・パス・コード。<see cref="IsRomajiEvidence"/>・<see cref="IsParticleAfterEnglish"/>・<see cref="RomajiMinShare"/>) ときは、何もせず false (今までの再変換に任せる)。
    /// 学習はしない (打ち直した文ではないため)。
    /// 本家 #332 の移植 (候補の出し方は本家と同じ。変換は ConvertJapanese にして、ユーザー辞書・学習した変換を使う。本家は日本語だと言える所の判定をしない = 英文の to・on・so も日本語にする)。
    /// </summary>
    internal bool ReconvertRomaji(string text)
    {
        // 前後の改行・タブ (と空白) は本体から切り離して判定・変換し、候補と置き換えにそのまま付け直す (Enter は選んだ範囲全体を置き換えるので、改行を落とさない)
        var (lead, core, trail) = TrimLineEdges(text);
        if (IsComposing || !IsRomajiText(core)) return false;
        var parts = SplitRomajiText(core, out var hasEvidence, out var hasParticle);
        if (!(hasParticle || (hasEvidence && EnoughJapanese(parts))))
        {
            Diagnostics.Log.Info("再変換: 選んだ文字に、日本語にする所がありませんでした。");
            return false;
        }
        // 選んだことでキャレットは動いている。直前に確定した語を、あとで確定し直さない (確定し直すと、選んだ文字ではなく手前の文字を消す)
        _correctable.Clear();
        _lastWord = null;
        BeginComposition();
        // 選んだ文字の前後の空白・改行・タブは、置き換えても残す
        var converted = lead
            + JoinRomajiParts(parts, kana => DigitHyphens(string.Concat(ConvertJapanese(kana).Select(c => c.Text))))
            + trail;
        if (converted.Trim().Length == 0 || converted == text)
        {
            _text.Clear();
            return false;
        }
        _reconvertOriginal = text;
        _reconvertRomaji = true;
        _tabStartedConversion = false;
        foreach (var c in converted) _text.AppendKana(c, c);
        _text.Mode = DisplayMode.Hiragana;
        // 候補を開くだけなので、Expanded にして変換エンジンの追加候補 (読み = 日本語にした文) を引かない
        _clauses = [new Clause(converted, false, [converted, text]) { Expanded = true }];
        _selectedClause = 0;
        _converting = true;
        _gate.Capture();
        return true;
    }

    /// <summary>
    /// 数字のすぐ後ろの長音 ー を、打った - に戻す (10-ji → 10-時、03-1234-5678desu → 03-1234-5678です、3-4nin → 3-4人)。
    /// 長音はかなの母音の後ろにしか意味が無いので、数字の後ろの ー は区切りの - を打ったもの。変換エンジンには今までどおり ー で渡し (読みと変換結果を変えない)、結果だけ直す。
    /// 全角の数字の後ろは全角の －。かなの後ろの長音 (ko-hi-2hai のこーひー) は変えない。
    /// </summary>
    internal static string DigitHyphens(string text)
    {
        if (!text.Contains('ー')) return text;
        var chars = text.ToCharArray();
        for (var i = 1; i < chars.Length; i++)
        {
            if (chars[i] != 'ー') continue;
            if (char.IsAsciiDigit(chars[i - 1])) chars[i] = '-';
            else if (chars[i - 1] is >= '０' and <= '９') chars[i] = '－';
        }
        return new string(chars);
    }

    /// <summary>日本語の区間の英字が、文全体の英字の <see cref="RomajiMinShare"/> 以上か。英文に紛れた 1 語 (Hello, my name is Ryo.) で日本語にしないための下限。</summary>
    private static bool EnoughJapanese(List<RomajiPart> parts)
    {
        var total = parts.Sum(p => p.Letters);
        // 小文字に直した語 (文頭の語) の字数は、日本語の割合に数えない: 数えると、英文の頭が make・run・you のような曖昧な語のとき、割合の下限を越えて英文に候補が出る
        var japanese = parts.Where(p => !p.English && !p.Lowered).Sum(p => p.Letters);
        return total > 0 && japanese >= total * RomajiMinShare;
    }

    /// <summary>日本語の区間の英字が文全体に占める割合の下限 (前後比較で決めた値)。</summary>
    internal const double RomajiMinShare = 0.25;

    /// <summary>ローマ字の文の 1 区間。English は英語 (Text は打ったまま)、でなければ日本語 (Text はかな)。NewWord は、空白で区切った語の先頭か。</summary>
    private readonly record struct RomajiPart(bool English, string Text, bool NewWord, int Letters = 0, bool Lowered = false, bool Literal = false);

    /// <summary>
    /// ローマ字の文を、空白で区切った語ごとに英語 / 日本語に分ける (打ったときと同じ判定。前の語が英語なら、それも手がかりにする)。
    /// hasEvidence は、日本語の区間に「日本語だという証拠」が 1 つでもあるか (<see cref="IsRomajiEvidence"/>)。hasParticle は、英語の語のすぐ後ろ (同じ語の中) に助詞が続くか (<see cref="IsParticleAfterEnglish"/>)。
    /// どちらも無ければ、ローマ字に見える英文を日本語にしてしまわないよう、何もしない。
    /// 記号を含む語 (URL・パス・メール・ファイル名・コマンドの引数・製品名) は、判定にかけず英字のまま残す。
    /// </summary>
    private List<RomajiPart> SplitRomajiText(string text, out bool hasEvidence, out bool hasParticle)
    {
        var parts = new List<RomajiPart>();
        hasEvidence = false;
        hasParticle = false;
        bool? previousEnglish = null;
        var sentenceStart = true;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // 文頭 (と . ! ? の後) の語は、アプリが自動で大文字にしたものかもしれない: 先頭の 1 文字だけ大文字なら、小文字にして判定・変換する (この語自体は日本語の証拠にしない)
            var lowered = sentenceStart && InitialCapitalWord.IsMatch(word);
            sentenceStart = SentenceEnd.IsMatch(word);
            if (IsCodeLikeWord(word))
            {
                parts.Add(new RomajiPart(true, word, true, word.Count(char.IsAsciiLetter)));
                previousEnglish = true;
                continue;
            }
            // 英字を含まず - を含む語 (箇条書きの -、電話番号・日付の 03-1234-5678、空白で区切った -) は、長音 ー にせず前後の空白ごとそのまま残す。
            // 語の中の長音 (ra-men・su-pa-) は英字を含むので、今までどおり。次の語への手がかりは「英語の後ろ」にしない (英文の後ろの - sushi ga suki も日本語として判定する)
            if (word.Contains('-') && !word.Any(char.IsAsciiLetter))
            {
                parts.Add(new RomajiPart(true, word, true, Literal: true));
                previousEnglish = false;
                continue;
            }
            // 前の語が日本語でも「日本語の続き」とはしない (空白で区切って打った語なので、語の途中ではない)。
            // 前までが英文 (英語 2 語以上) という手がかり (PrecedingEnglishSentence) は使わない: pull request no / Visual Studio Code no の no が英語のまま残るため (docs/IMPROVEMENT_PLAN.md の P36 の前後比較)。
            // 使うのは、直前が感動詞 1 語のとき (爆弾にはなれない oh no! の no を の にしない) だけ。
            var typed = new CompositionText(_detector)
            {
                Level = () => _options.Level(),
                TypoCorrector = _options.RomajiTypos,
                CorrectTypos = () => _options.CorrectTypos(),
                Punctuation = () => _options.Punctuation(),
                FullWidthSymbols = () => _options.FullWidthSymbols(),
                Parentheses = () => _options.Parentheses(),
                Braces = () => _options.Braces(),
                PrecedingEnglish = previousEnglish == true ? true : null,
                PrecedingEnglishSentence = previousEnglish == true && AfterSingleEnglishWord(JoinForJudgment(parts)) && IsEnglishSentence(JoinForJudgment(parts) + " "),
            };
            foreach (var c in lowered ? char.ToLowerInvariant(word[0]) + word[1..] : word) typed.Append(c);
            typed.FixTypos();
            var segments = typed.FinalSegments();
            var first = true;
            CompositionSegment? before = null;
            var lastEnglish = false;
            foreach (var (segment, kana) in segments)
            {
                // 日本語と判定されても、かなに読めた所が 1 つも無い (my の m + y のように、読み切れない英字だけ) 区間は英語として扱う
                var english = segment.IsEnglish || (kana.Any(char.IsAsciiLetter) && kana.All(c => c < 0x80));
                // 小文字に直した語が英語として残るときは、打ったままの大文字に戻す
                var raw = lowered && first && english && segment.Raw.Length > 0 && segment.Raw[0] == char.ToLowerInvariant(word[0]) ? word[0] + segment.Raw[1..] : segment.Raw;
                parts.Add(new RomajiPart(english, english ? raw : kana, first, segment.Raw.Count(char.IsAsciiLetter), lowered));
                first = false;
                if (!lowered)
                {
                    if (IsRomajiEvidence(before, segment, kana)) hasEvidence = true;
                    if (IsParticleAfterEnglish(before, segment)) hasParticle = true;
                }
                before = segment;
                lastEnglish = english;
            }
            previousEnglish = segments.Count > 0 && lastEnglish;
        }
        return parts;
    }

    /// <summary>
    /// 日本語のローマ字だと言える区間か: 前後の数字・文末の記号 (2026nen の 2026、arigatou! の !) を除いた中身が英字だけ (長音の - は可) で 3 文字以上、
    /// かなが 2 つ以上読め (Take|shi の shi、by|F|ri|day の ri のように、英単語の端が 1 音だけ読めたものを除く)、英単語 (to・no・so・do・name …) でもない。
    /// to・on・so のような英文の中の短い語や、a3f9c1d のような英数字の並びだけでは、日本語にしない。
    /// 大文字で始まる語 (Kenji・Tanaka-san) と、敬称だけの語 (san・kun・chan・sama) も証拠にしない: 英文の中の人名は、直後に ! . , ? が付くと「語として確定」して日本語に読めるが、
    /// それだけでは日本語だと言えない (Thanks, Kenji! / Hi Tanaka! / Thanks, Takeshi san!)。文頭の Kyou・Ohayou のような大文字の語は、語ごとの判定で元から英語になるので、この規則で失うものは無い。
    /// </summary>
    private bool IsRomajiEvidence(CompositionSegment? before, CompositionSegment segment, string kana)
    {
        if (segment.IsEnglish || kana.Count(c => c is >= 'ぁ' and <= 'ヿ') < 2) return false;
        // 大文字で始まる英語の語のすぐ後ろ (Naka|mura、Take|shi のように、人名・固有名詞を途中で区切ったもの) は、証拠にしない
        if (before is { IsEnglish: true } && char.IsAsciiLetterUpper(before.Value.Raw[0])) return false;
        var core = segment.Raw.Trim("0123456789.,!?~'".ToCharArray());
        if (core.Length > 0 && char.IsAsciiLetterUpper(core[0])) return false;
        if (RomajiHonorifics.Contains(core)) return false;
        return core.Length >= RomajiEvidenceLetters && core.All(c => char.IsAsciiLetter(c) || c == '-') && core.Count(char.IsAsciiLetter) >= RomajiEvidenceLetters
            && !_detector.IsKnownEnglishWord(core);
    }

    /// <summary>先頭の 1 文字だけが大文字の語 (Asita・Kenji,・Tanaka-san。全部大文字の PR や 1 文字の I は含めない)。</summary>
    private static readonly System.Text.RegularExpressions.Regex InitialCapitalWord = new("^[A-Z][a-z]+(-[a-z]+)*[.,!?]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>文の終わりの記号で終わる語。次の語は文頭として扱う。</summary>
    private static readonly System.Text.RegularExpressions.Regex SentenceEnd = new("[.!?]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>敬称。単独の語 (Takeshi san の san) は、英文の中の人名にも付くので、日本語の証拠にしない (sensei は英単語の表にあるので、元から証拠にならない: 入れても結果が変わらなかった)。</summary>
    private static readonly HashSet<string> RomajiHonorifics = new(StringComparer.Ordinal) { "san", "kun", "chan", "sama" };

    private static readonly HashSet<string> RomajiParticles = new(StringComparer.Ordinal) { "de", "wo", "ha", "ga", "ni", "to", "no", "mo", "wa", "he", "ya", "kara", "made", "yori", "dake", "nado", "desu", "dayo" };

    /// <summary>
    /// 小文字の英語の語のすぐ後ろ (同じ語の中) に続く助詞 (sdk|to、xml|de): 英文の中の別の語にはならない、日本語だと言える形。
    /// 大文字で始まる語は除く (Cly|de、Typ|ha、Ma|nto のような固有名詞・人名 Mako|to は、語の終わりが助詞に見えるだけ)。
    /// </summary>
    private static bool IsParticleAfterEnglish(CompositionSegment? before, CompositionSegment segment) =>
        before is { IsEnglish: true, Raw.Length: >= 2 } && char.IsAsciiLetterLower(before.Value.Raw[0]) && !segment.IsEnglish && RomajiParticles.Contains(segment.Raw);

    /// <summary>
    /// 前の語までの文を、次の語の判定 (感動詞 1 語の後ろか・英文の後ろか) のためにつなぐ。そのまま残した - の語は、今までどおり長音 ー として数える
    /// (半角の - のままだと、- oh no の oh の手前で「日本語より後ろ」の走査が止まらず、感動詞の後ろの no が の になる)。
    /// </summary>
    private static string JoinForJudgment(List<RomajiPart> parts) =>
        JoinRomajiParts(parts.Select(p => p.Literal ? p with { English = false, Text = p.Text.Replace('-', 'ー') } : p).ToList(), kana => kana);

    /// <summary>文 (ここまでの区間をつないだもの) の最後の日本語より後ろが、英語 1 語だけか (oh、hey)。</summary>
    private static bool AfterSingleEnglishWord(string text)
    {
        var start = text.Length;
        while (start > 0 && text[start - 1] < 0x80) start--;
        return text[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1;
    }

    /// <summary>ローマ字の再変換で、日本語だと言える区間に必要な英字の数。英文の中の短い語 (to・on・so) だけで日本語にしないための下限。</summary>
    internal const int RomajiEvidenceLetters = 3;

    /// <summary>
    /// URL・パス・メール・ファイル名・コマンドの引数・コード・製品名のように、そのまま英字で残す語か:
    /// 記号 (/ \ : = @ _ < > { } ( ) [ ] | & $ # * ; % ^ ` " +)、先頭の - / .、英数字の間の .、小文字のあとの大文字 (iPhone・azooKey)。
    /// 文末の . , ! ? や、長音の - は含めない。日本語を Shift で区切って打った語 (TanakaMakoto) も、今までどおり何もしない。
    /// </summary>
    private static bool IsCodeLikeWord(string word)
    {
        if (word.Length > 1 && word[0] == '-' && (word[1] == '-' || char.IsAsciiLetterOrDigit(word[1]))) return true;
        if (word.Length > 1 && word[0] == '.' && char.IsAsciiLetterOrDigit(word[1])) return true;
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            if (i > 0 && char.IsAsciiLetterUpper(c) && char.IsAsciiLetterLower(word[i - 1])) return true;
            if (c is '/' or '\\' or ':' or '=' or '@' or '_' or '<' or '>' or '{' or '}' or '(' or ')' or '[' or ']' or '|' or '&' or '$' or '#' or '*' or ';' or '%' or '^' or '`' or '"' or '+') return true;
            if (c == '.' && i > 0 && i < word.Length - 1 && char.IsAsciiLetterOrDigit(word[i - 1]) && char.IsAsciiLetterOrDigit(word[i + 1])) return true;
        }
        return false;
    }

    /// <summary>
    /// SplitRomajiText の結果を 1 つの文にする。日本語の区間は続くものをつなげて convert (かな → 変換結果) にかけ、
    /// 空白で区切って打った語の境目の、英語の前後には空白を残す (asita no meeting de review shimasu → 明日の meeting で review します)。
    /// 空白なしで打った語の中 (kyouhagoogledekensaku) では空白を足さない。
    /// </summary>
    private static string JoinRomajiParts(List<RomajiPart> parts, Func<string, string> convert)
    {
        var output = new StringBuilder();
        var kana = new StringBuilder();
        void FlushKana()
        {
            if (kana.Length > 0) output.Append(convert(kana.ToString()));
            kana.Clear();
        }
        foreach (var (english, part, newWord, _, _, _) in parts)
        {
            if (english)
            {
                FlushKana();
                if (newWord && output.Length > 0 && output[^1] != ' ') output.Append(' ');
                output.Append(part);
                continue;
            }
            // 英語のあとに空白で区切って続く日本語は、空白を残す (続く日本語どうしは空白なしでつなげる)
            if (kana.Length == 0 && newWord && output.Length > 0 && output[^1] != ' ') output.Append(' ');
            kana.Append(part);
        }
        FlushKana();
        return output.ToString();
    }

    /// <summary>ローマ字の再変換で開いた変換ボックスか (学習しない・Esc / BackSpace でそのまま元に戻す)。</summary>
    private bool _reconvertRomaji;
    // この入力で 1 音確定 (Shift+↓) をしたか。残りは語の途中からなので、確定のときに学習しない。
    private bool _firstSoundCommitted;

    /// <summary>ローマ字の再変換をやめて、選んでいた元の文字を書き戻す (変換ボックスは閉じる)。</summary>
    private void CancelRomajiReconversion()
    {
        _text.Clear();
        _converting = false;
        _clauses = [];
        _selectedClause = 0;
        _reconvertRomaji = false;
        ResetPrediction();
        if (_reconvertOriginal is { } original)
        {
            _reconvertOriginal = null;
            _host.CommitText(original);
        }
    }

    /// <summary>無効化・フォーカス喪失などで、未確定の内容をそのまま確定する。</summary>
    public void CommitPending()
    {
        if (_heldLetters.Length > 0) ReleaseHeldAsEnglish();
        CommitIfAny();
        // フォーカス外れ・英数キーなどの暗黙の確定は、確定後の F10 / F6 の対象にしない (打った語を自分で確定したわけではない)
        _lastWord = null;
        UpdateView();
    }

    /// <summary>
    /// 入力先が変わった (別のウィンドウ・パスワード欄・入力欄でない所にフォーカスが移った) とき。未確定の内容を確定せずに捨てる。
    /// 確定すると、移った先 (別のアプリやパスワード欄) に入ってしまうため。捨てたら true。
    /// </summary>
    public bool Abandon(string reason)
    {
        // 英数状態で判定中の語は、もうアプリに送ってあるので判定をやめるだけ。
        ClearHeld();
        if (!IsComposing) return false;
        Diagnostics.Log.Warn($"{reason}ので、変換中の入力を取り消しました (移った先に入らないように)。");
        Reset();
        _correctable.Clear();
        _lastWord = null;
        UpdateView();
        return true;
    }

    /// <summary>例外からの復旧用。未確定の内容と追跡中の状態をすべて捨てる (次の入力で同じ例外を繰り返さないように)。</summary>
    public void Reset()
    {
        _text.Clear();
        _reconvertOriginal = null;
        _reconvertRomaji = false;
        _firstSoundCommitted = false;
        _toggleBox = null;
        _toggleUndo = null;
        _lastWord = null;
        _converting = false;
        _tabStartedConversion = false;
        _clauses = [];
        _headCount = 0;
        _fixedUnits = [];
        ClearConversionCaches();
        ResetPrediction();
        ClearHeld();
        _swallowedShift.Clear();
        _replayedDown.Clear();
        _capturedDown.Clear();
    }

    /// <summary>フォーカスが変わったときなど。前の入力欄の文脈を持ち越さない。</summary>
    public void ResetContext()
    {
        _lastCommitEnglish = null;
        _lastCommitText = null;
        _correctable.Clear();
        _lastWord = null;
    }

    /// <summary>入力言語が対象外になったとき。判定を止め、未処理のキー・クリックは順番どおりに通す。</summary>
    public void SuspendInput()
    {
        Abandon("入力言語が日本語ではなくなった");
        Reset();
        ResetContext();
        foreach (var input in _gate.Abort())
        {
            if (input.Key is { } key) _host.Replay(key);
            else if (input.Mouse is { } mouse) _host.Replay(mouse);
        }
        _host.Hide();
    }

    private void HandleMouse(MouseButtonEvent e)
    {
        // クリックで別の場所に移る前に、今の位置へ確定しておく。
        CommitPending();
        _correctable.Clear();
        _lastWord = null;
        _host.Replay(e);
    }

    private void HandleKey(KeyEvent e)
    {
        var vk = e.Vk;
        if (e.IsUp)
        {
            _swallowedShift.Remove(vk);
            // 押下をアプリに送ったキー、または押下が関所を閉じる前に通っていたキーは、離したこともアプリに伝える。
            var replayed = _replayedDown.Remove(vk);
            var capturedHere = _capturedDown.Remove(vk);
            if (replayed || !capturedHere) _host.Replay(e);
            return;
        }
        _capturedDown.Add(vk);

        if (_heldLetters.Length > 0)
        {
            if (VirtualKeys.IsLetter(vk) && _swallowedShift.Count == 0 && !VirtualKeys.IsModifier(vk))
            {
                Hold(e);
                return;
            }
            // 母音の後の - は長音 (ro-maji → ろーまじ、de-ta → でーた)。英単語の途中にはまず出てこないので、ローマ字として
            // 読めれば日本語に戻す。英語の接頭辞 (e-mail、co-op) かもしれないときは - も送って判定を続け、- の後ろで決める
            // (e-mail → 英語、e-me-ru → えーめーる)。
            var part = LastHyphenPart();
            if (vk == VirtualKeys.OemMinus && _swallowedShift.Count == 0 && part.Length > 0 &&
                _detector.Romaji.AnalyzeFragment(part) is { IsValid: true, Partial: "" })
            {
                var prefix = !_heldLetters.ToString().Contains('-') && CompositionDetector.IsHyphenPrefix(part);
                _heldLetters.Append('-');
                _heldLastKeyTime = e.TimeMs;
                if (prefix)
                {
                    SendHeld(e);
                    return;
                }
                Diagnostics.Log.Decision($"英数状態でローマ字を検知: {Diagnostics.Log.Text(_heldLetters.ToString())} (母音の後の長音)");
                SwitchHeldToJapanese();
                return;
            }
            // 英字以外のキー・Shift で判定を打ち切り、英語として出してから、そのキーを普通に処理する。
            // キーの値は残さない: FileLog だけ ON (打った文字は記録しない設定) でも、数字・記号の仮想キーコードが並ぶと
            // 英字+数字の列 (パスワードに多い) が読み取れてしまう。分類だけ記録する。
            Diagnostics.Log.Info($"英数状態の判定を打ち切り: {VirtualKeys.DescribeKind(vk)} のキー (Shift {_swallowedShift.Count})");
            ReleaseHeldAsEnglish();
        }

        if (IsShift(vk))
        {
            // 大文字入力や文節の区切り変更のための Shift はアプリに渡さない。ほかのキーと一緒に送り直すときにまとめて送る。
            _swallowedShift.Add(vk);
            return;
        }
        if (VirtualKeys.IsModifier(vk))
        {
            // Ctrl / Alt / Win: ショートカットの前に確定する。
            CommitIfAny();
            ReplayDown(e);
            return;
        }
        if (_replayedDown.Any(IsCommandModifier))
        {
            CommitIfAny();
            ReplayDown(e);
            return;
        }

        if (!IsComposing)
        {
            StartWith(e);
            return;
        }

        // Shift+Enter: 確定したうえで、Enter をアプリに渡す (Web アプリの改行用)。Enter 単体は確定のみ (チャット欄で送信されないように)。
        if (vk == VirtualKeys.Return && (_swallowedShift.Count > 0 || _host.IsShiftDown()) && _options.ShiftEnterNewline())
        {
            if (_predicting) CommitPrediction(_predictions[_predictionIndex]);
            else
            {
                if (!_converting) _text.FixTypos();
                Commit();
            }
            ReplayDown(e);
            return;
        }

        if (_converting && HandleConversionKey(vk)) return;
        if (_predicting && HandlePredictionKey(vk)) return;

        switch (vk)
        {
            case VirtualKeys.Return:
                _text.FixTypos();
                Commit();
                return;
            case VirtualKeys.Space when _swallowedShift.Count > 0 || _host.IsShiftDown():
                // Shift+Space: 英語と判定した語でも、ローマ字として読んで変換する (go → 語、camera → かめら)。
                _text.FixTypos();
                _spaceStartedConversion = true;
                StartConversion(preferJapanese: true);
                return;
            case VirtualKeys.Space when _headCount == 0 && _text.Mode == DisplayMode.Auto && _text.KeepLoneHyphen():
                // 箇条書きの - は長音ではない: 半角の - と空白で確定する (- kyou wa)。
                Commit(suffix: " ", fixEnglish: true);
                return;
            case VirtualKeys.Space:
                _text.FixTypos();
                // 英語と判定した語で終わっているなら、変換ではなく確定して空白を入れる
                // (日本語の部分は、ライブ変換が ON なら漢字にして、OFF なら見えているかなのまま確定)。
                // 数字だけ (1、12) は、前が英文なら確定して空白 (I have 2 cats)。それ以外は変換して ① 一 Ⅰ などの候補を出す。
                if (_text.IsAlphanumericAt(final: true) && !(_text.Mode == DisplayMode.Auto && _text.IsNumeric && _text.Raw.All(char.IsAsciiDigit) && _text.PrecedingEnglish != true))
                {
                    Commit(suffix: " ", fixEnglish: true);
                }
                // chosen は文節を見ない: 固定した文節を含む確定は CommitText が確定し直しを記録しない。Esc で戻った後の古い文節で誤って真にしない (R45 任意 1)
                else if (EndsWithEnglish(final: true)) CommitText(TakeFixedClauses() + FixEnglishTypo(_text.RenderSegments(final: true, _options.LiveConversion() ? Convert : null)) + " ", english: true, _text.Raw, ChosenByUser(clausesInUse: false), hasEnglish: true, hasJapanese: _text.Segments(final: true).Any(s => !s.IsEnglish), SegmentRaws(converting: false));
                else if (_text.Mode == DisplayMode.Auto && _detector.IsEnglishAtWordEnd(_text.Raw, _options.Level())) CommitText(TakeFixedClauses() + FixEnglishTypo(_text.Raw) + " ", english: true, _text.Raw, ChosenByUser(clausesInUse: false), hasEnglish: true);
                else
                {
                    // Space で変換した = 語の後に空白を打とうとした、とも取れる (確定し直して英語にするときに空白を足す)。
                    _spaceStartedConversion = true;
                    StartConversion();
                }
                return;
            case VirtualKeys.Back:
                _text.RemoveLast();
                // 未変換の文節を消し切ったら、前の文節の選択に戻る。
                if (_headCount > 0 && _text.IsEmpty) SelectLastFixedClause();
                return;
            case VirtualKeys.Escape when _headCount > 0:
                // 固定した文節を、変換前の読みに戻す (その次の Esc は今までどおり入力を取り消す)。
                UnfixClauses();
                return;
            case VirtualKeys.Escape:
                _text.Clear();
                ResetPrediction();
                // 再変換した変換ボックスを Esc で取り消したら、置き換えた元の文字を書き戻す (消えないように)。学習・文脈の記録はしない。
                if (_reconvertOriginal is { } original)
                {
                    _reconvertOriginal = null;
                    _host.CommitText(original);
                    // 確定後の F6 で開いた変換ボックスなら、直す前の記録も戻す (学習はまだしていない)
                    if (_toggleBox is { } box) RestoreToggleBox(box);
                }
                return;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直す。
                FixMisspelling(typo);
                return;
            case VirtualKeys.Tab when _text.Suggestion() is not null:
                // 判定の強さが手動: 提案どおり英字にする。
                _text.LevelOverride = DetectionLevel.Balanced;
                return;
            case VirtualKeys.Tab when !_converting && _predictions.Count > 0 && !_text.IsCommandName:
                // 予測変換: 候補に入る (もしかして・英字への提案が優先)。
                // 本家と同じ: Shift+Tab は選ばない状態から最後の候補へ。
                _predicting = true;
                _predictionIndex = _swallowedShift.Count > 0 || _host.IsShiftDown() ? _predictions.Count - 1 : 0;
                return;
            case VirtualKeys.Tab when !_converting && _swallowedShift.Count == 0 && !_host.IsShiftDown() && _options.TabConversion() && TabStartsConversion():
                // Space なら変換を始める場面の Tab は、Space と同じく変換を始める (Microsoft IME と同じ。空白を打とうとした意味ではない: issue #219)。
                StartConversion();
                _tabStartedConversion = _converting;
                return;
            case VirtualKeys.F10 when _toggleBox is not null && _headCount == 0:
                // 確定後の F6 で開いた変換ボックスで F10: 打った英字どおりに戻す (F10 → F6 → F10 の往復)
                RevertToggleBox();
                return;
            case VirtualKeys.F6: SetMode(DisplayMode.Hiragana); return;
            case VirtualKeys.F7: SetMode(DisplayMode.Katakana); return;
            case VirtualKeys.F8: SetMode(DisplayMode.HalfWidthKatakana); return;
            case VirtualKeys.F9: SetMode(DisplayMode.FullWidthAlphanumeric); return;
            case VirtualKeys.F10: SetMode(DisplayMode.HalfWidthAlphanumeric); return;
            case VirtualKeys.Down when _options.TokMode() && _headCount == 0 && _toggleBox is null && (_swallowedShift.Count > 0 || _host.IsShiftDown()):
                // ATOK スタイル (TokMode) の Shift+↓: 先頭の 1 音だけ確定し、残りは入力のまま (1 音確定)。
                // OFF のとき・変換中・固定した文節があるとき・確定後の F6 で開いた変換ボックスの中 (F10 / Esc で元の語に戻すので、一部だけ確定すると二重になる。R47-1) は今までどおり。
                CommitFirstSound();
                return;
            case VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down when !_text.IsAlphanumeric:
                // 変換前でも矢印キーで文節の選択に入る (Mac のライブ変換と同じ)。
                EnterClauseSelection(vk);
                return;
        }

        if (VirtualKeys.IsHankakuZenkaku(vk))
        {
            SetMode(_text.IsAlphanumeric ? DisplayMode.Hiragana : DisplayMode.HalfWidthAlphanumeric);
            return;
        }

        if (_text.KanaInput && KanaOf(e) is { } key)
        {
            if (_converting) EndConversionForNewInput();
            DropToggleBoxUnlessContinued();
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }

        if (_host.CharFromKey(e, _swallowedShift.Count > 0) is { } ch && !char.IsControl(ch) && ch != ' ')
        {
            // 変換中に次の文字を打ったら、今の候補で確定して新しい入力を始める (IME と同じ)。
            // ContinueAfterConversion が ON なら、確定せずに、今の候補のまま固定して続きを未変換の文節にする。
            if (_converting) EndConversionForNewInput();
            DropToggleBoxUnlessContinued();
            _text.Append(ch);
            return;
        }

        // 矢印 (英字だけのとき)・Tab・Delete などは確定してから通す (暗黙の確定)。
        Commit(implicitly: true);
        ReplayDown(e);
    }

    /// <summary>
    /// 確定後の F6 で開いた変換ボックスに続けて打った。直した語だけの変換ボックスではなくなるので控えは捨てるが、
    /// ContinueAfterConversion で文節を固定して続けたとき (_headCount > 0) は、確定のときに末尾の空白を戻し、日本語として学習するので残す (R43-4)。
    /// 空白は続けて打った文字の後ろ (確定する文字列の末尾) に付く。
    /// </summary>
    private void DropToggleBoxUnlessContinued()
    {
        if (_headCount == 0) _toggleBox = null;
    }

    /// <summary>変換ボックスを開く記号・数字 (フック側の MeltypeEngine.StartsComposition と合わせる)。</summary>
    internal static bool StartsWithSymbol(char c) => c is >= '!' and <= '~' && !char.IsAsciiLetter(c);

    /// <summary>変換ボックスが空のときの最初の打鍵。英字・句読点なら入力を始め、それ以外はそのまま通す。</summary>
    private void StartWith(KeyEvent e)
    {
        // かな入力: かなのキーならすべて入力を始める (英数状態でなければ)。
        if (_options.KanaInput() && !_options.DirectMode() && KanaOf(e) is { } key)
        {
            BeginComposition();
            _text.AppendKana(key.Raw, key.Kana);
            return;
        }
        var c = _host.CharFromKey(e, _swallowedShift.Count > 0);
        var letter = VirtualKeys.IsLetter(e.Vk) && c is { } l && char.IsAsciiLetter(l);
        if (letter && _options.DirectMode())
        {
            // 英数状態: ローマ字かどうか判定する (打鍵はすぐ送る)。大文字で始まる語は英語なのでそのまま通す。
            if (_options.ClassifyDirect is null || _swallowedShift.Count > 0 || char.IsAsciiLetterUpper(c!.Value))
            {
                Diagnostics.Log.Info($"英数状態: {Diagnostics.Log.Text(c.ToString()!)}は大文字 / Shift なので英語のまま");
                ReplayDown(e);
                _options.DirectDecided?.Invoke(false);
            }
            else Hold(e);
            return;
        }
        // 句読点・かぎかっこ・長音・中黒・数字でも入力を始める (、。「」ー・)。英数状態ではそのまま通す。
        if (letter || (c is { } symbol && StartsWithSymbol(symbol) && !_options.DirectMode()))
        {
            BeginComposition();
            _text.Append(c!.Value);
            return;
        }
        ReplayDown(e);
    }

    /// <summary>
    /// 新しい入力を始める。入力欄のキャレットの前後の確定済みの文字を読みに行き、
    /// 英語とも日本語とも読める語の判定と、変換の文脈に使う。読めるまでは自分が最後に確定した文字列で代用する。
    /// </summary>
    private void BeginComposition()
    {
        _text.KanaInput = _options.KanaInput();
        ResetPrediction();
        _reconvertOriginal = null;
        _reconvertRomaji = false;
        _firstSoundCommitted = false;
        _tabStartedConversion = false;
        _toggleBox = null;
        var id = ++_compositionId;
        // 自分が確定した直後は、アプリ側のテキストがまだ更新されていないかもしれないので自分の記録を信じる。
        var recentOwnCommit = Environment.TickCount64 - _lastCommitTime < OwnCommitTrustMs;
        _precedingText = _lastCommitEnglish is null ? null : _lastCommitText;
        _followingText = null;
        _text.PrecedingEnglish = _lastCommitEnglish;
        _text.PrecedingEnglishSentence = _lastCommitEnglish == true && IsEnglishSentence(_lastCommitText);
        // 保護区間 (/tmp/foo) を始めてよいか: 自分の確定の末尾で決める (日本語を確定した直後の /fuka は守らない)。確定が無い・分からなければ語の頭
        _text.PrecedingTail = _precedingText;
        _text.FollowingEnglish = null;
        _host.RequestSurroundingText((before, after) =>
        {
            // 返ってくるまでに別の入力になっていたら使わない。
            if (id != _compositionId) return;
            if (!recentOwnCommit && before is not null)
            {
                _precedingText = before;
                if (LanguageOf(before) is { } english) _text.PrecedingEnglish = english;
                _text.PrecedingEnglishSentence = IsEnglishSentence(before);
                _text.PrecedingTail = before;
            }
            _followingText = after;
            _text.FollowingEnglish = LanguageOfStart(after);
            UpdateView();
        });
    }

    /// <summary>
    /// 英文の途中か: 最後の行の日本語の文字より後ろが、空白で区切った英単語 2 語以上で、空白で終わる ("I want ", "Thanks, see ")。
    /// 日本語の文の中の英単語 ("今日は GitHub ") は 1 語なので当たらない。
    /// </summary>
    /// <summary>1 語でも英文の始まりとみなす、行の始めのあいさつ・感動詞。</summary>
    private static readonly HashSet<string> SentenceOpeners = ["hey", "hi", "hello", "oh", "wow", "yeah", "yes", "well", "so", "hmm", "ah", "ooh", "oops", "thanks", "sorry", "please", "dear", "yay", "whoa", "nope", "yep"];

    /// <summary>日本語の後ろでも英文の始まりとみなす感動詞 (日本語のローマ字としては使わない綴りのもの)。</summary>
    private static readonly HashSet<string> Interjections = ["oh", "wow", "yeah", "ooh", "oops", "whoa", "woah", "yay", "hmm", "hey"];

    internal static bool IsEnglishSentence(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[^1] != ' ') return false;
        var start = text.Length;
        while (start > 0 && text[start - 1] < 0x80 && text[start - 1] is not ('\n' or '\r')) start--;
        var words = text[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // 行の始めのあいさつ・感動詞 (hey・hi・oh …) なら 1 語でも英文の始まり ("hey " の後の yo)。
        // ほかの 1 語 (GitHub no repo の GitHub) は、日本語の文の中の英単語のことが多いので 2 語以上
        var lineStart = start == 0 || text[start - 1] is '\n' or '\r';
        if (lineStart && words is [var first] && SentenceOpeners.Contains(first.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 感動詞 (oh・wow・yeah …) は日本語の後ろでも英文の始まり (爆弾にはなれない oh + no! の no を の にしない)
        if (words is [var interjection] && Interjections.Contains(interjection.TrimEnd(',', '!', '.').ToLowerInvariant())) return true;
        // 行の始めの、' で縮めた英語 (I'll・We're・don't) も 1 語で英文の始まり ("I'll " の後の go)。ローマ字には ' が入らない
        if (lineStart && words is [var contraction] && System.Text.RegularExpressions.Regex.IsMatch(contraction, @"^[A-Za-z]+['’][A-Za-z]{1,2}$")) return true;
        return words.Length >= 2 && words.All(w => w.Any(char.IsAsciiLetter) && w.All(c => char.IsAsciiLetterOrDigit(c) || c is ',' or '.' or '\'' or '-' or '!' or '?' or ':' or ';'));
    }

    /// <summary>確定済みの文字列の最後の (空白以外の) 文字が英数字なら英語、かな・漢字・全角記号なら日本語。</summary>
    internal static bool? LanguageOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (Classify(text[i]) is { } english) return english;
            if (text[i] >= 0x80 && !char.IsWhiteSpace(text[i]) && text[i] != '　') return null;
        }
        return null;
    }

    /// <summary>キャレットの後ろの文字列の最初の (空白以外の) 文字で判断する。</summary>
    internal static bool? LanguageOfStart(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var c in text)
        {
            if (c is '\r' or '\n') return null; // 次の行は別の文
            if (Classify(c) is { } english) return english;
            if (c >= 0x80 && !char.IsWhiteSpace(c) && c != '　') return null;
        }
        return null;
    }

    /// <summary>
    /// 英数字なら英語 (true)、かな・漢字・全角文字・日本語の文で使う記号 (○ ※ ★ 「」 など U+2000 以降) なら日本語 (false)、
    /// 空白や英文の記号なら判断しない (null)。
    /// </summary>
    private static bool? Classify(char c)
    {
        if (char.IsAsciiLetterOrDigit(c)) return true;
        if (c >= '\u2000' && !char.IsWhiteSpace(c) && c != '\u3000') return false;
        return null;
    }

    // ---- 英数状態のローマ字判定 ----

    // - を付けて使う英語の接頭辞 (e-mail、re-do、co-op、x-ray)

    private void Hold(KeyEvent e)
    {
        _heldLetters.Append(VirtualKeys.ToLetter(e.Vk));
        _heldLastKeyTime = e.TimeMs;
        SendHeld(e);
        DecideHeld(final: false);
    }

    /// <summary>
    /// 判定中の語の打鍵を、判定を待たずにアプリへ送る。前は判定できるまで保留していたので、英単語 (can・game・today) は
    /// Space を押すまで画面に出ず、英数状態の入力が遅れて見えた。ローマ字と分かったら、送った分を BackSpace で消す。
    /// </summary>
    private void SendHeld(KeyEvent e)
    {
        _host.Replay(e);
        _replayedDown.Add(e.Vk);
        _heldSent++;
    }

    /// <summary>判定中の英字の、最後の - より後ろ (e-ma → ma)。- が無ければ全体。</summary>
    private string LastHyphenPart()
    {
        var letters = _heldLetters.ToString();
        return letters[(letters.LastIndexOf('-') + 1)..];
    }

    private void DecideHeld(bool final)
    {
        // 英語の接頭辞の後の - (e-、co-) の後ろだけで判定する。e-mail・co-op のような - の入った英単語なら英語。
        var part = LastHyphenPart();
        var verdict = _detector.IsHyphenatedEnglishWord(_heldLetters.ToString()) ? Verdict.English
            : part.Length == 0 ? Verdict.Undecided
            : _options.ClassifyDirect!(part, final);
        // 2 文字の英単語 (up、my) でも、ローマ字の打ちかけとして読める (う + p) なら、続きを見てから決める
        // (upa- → うぱー、my → みゃ)。Space などで打ち終われば英語。
        if (verdict == Verdict.English && !final && part.Length <= 2 && _heldLetters.Length <= 2 &&
            _detector.Romaji.AnalyzeFragment(part.ToLowerInvariant()) is { IsValid: true, Partial.Length: > 0 })
        {
            verdict = Verdict.Undecided;
        }
        Diagnostics.Log.Info($"英数状態の判定: {Diagnostics.Log.Text(_heldLetters.ToString())}→ {verdict}{(final ? " (打ち終わり)" : "")}");
        if (verdict == Verdict.Japanese) SwitchHeldToJapanese();
        else if (verdict != Verdict.Undecided || final) ReleaseHeldAsEnglish();
    }

    /// <summary>ローマ字だった: 日本語入力に戻し、判定中の英字を変換ボックスに入れる。</summary>
    private void SwitchHeldToJapanese()
    {
        var letters = _heldLetters.ToString();
        var sent = _heldSent;
        ClearHeld();
        // 判定を待たずに送っていた英字を消して、変換ボックスに入れ直す。
        _host.DeleteBackward(sent);
        _options.DirectDecided?.Invoke(true);
        BeginComposition();
        foreach (var c in letters)
        {
            if (_text.KanaInput && Detection.KanaDetector.KanaForKey(char.ToUpperInvariant(c), char.IsAsciiLetterUpper(c)) is { } kana) _text.AppendKana(c, kana);
            else _text.Append(c);
        }
    }

    /// <summary>かな入力で、その打鍵が入力するかなと、そのキーの英字 (英語として見せるとき用)。かなのキーでなければ null。</summary>
    private (char Raw, char Kana)? KanaOf(KeyEvent e)
    {
        var shift = _swallowedShift.Count > 0 || _host.IsShiftDown();
        if (Detection.KanaDetector.KanaForKey(e.Vk, shift) is not { } kana) return null;
        var raw = _host.CharFromKey(e, _swallowedShift.Count > 0) ?? kana;
        // Shift が小書き文字・句読点を打つのに使われたとき (Shift + Z = っ) は、大文字で打った英語ではない。
        // 大文字の英字のまま持つと「大文字で始まる区間は英語」の規則に当たり、きっと が きZと のように英字になってしまう。
        // Shift を押しても同じかな (Shift + G = き) なら、これまでどおり大文字の英語の手がかりとして残す。
        if (shift && char.IsAsciiLetterUpper(raw) && Detection.KanaDetector.KanaForKey(e.Vk, false) != kana) raw = char.ToLowerInvariant(raw);
        return (raw, kana);
    }

    /// <summary>英語だった: 打鍵はもう送ってあるので、判定をやめるだけ。</summary>
    private void ReleaseHeldAsEnglish()
    {
        ClearHeld();
        _correctable.Clear();
        _lastWord = null;
        _options.DirectDecided?.Invoke(false);
    }

    private void ClearHeld()
    {
        _heldLetters.Clear();
        _heldSent = 0;
    }

    // ---- 変換 (文節) ----

    /// <summary>変換中だけ意味を持つキー。処理したら true。</summary>
    private bool HandleConversionKey(int vk)
    {
        var shift = _swallowedShift.Count > 0;
        var clause = _clauses[_selectedClause];
        if (_reconvertRomaji)
        {
            // ローマ字の再変換: 読みに戻れないので、Esc / BackSpace は選んだままの文に戻してやめる。区切りは変えられない。
            if (vk is VirtualKeys.Escape or VirtualKeys.Back)
            {
                CancelRomajiReconversion();
                return true;
            }
            if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) return true;
        }
        // 数字・修飾キー以外の操作 (Space・↓・↑・PageUp/Down・← →) をしたら、候補を見て選んでいる: 以後は数字で選べる (TypeDigitsAfterConversion)。
        if (!VirtualKeys.IsDigit(vk) && !VirtualKeys.IsModifier(vk)) _candidatesMoved = true;
        switch (vk)
        {
            // 0 は候補の番号に無い (候補は 1 から)。電話番号・番号 (denwa wa 03-…) の先頭の 0 が消えないよう、
            // 候補では受けず、今の候補で確定して新しい入力にする (ほかのキーと同じ)。
            case 0x30 when !shift && !_reconvertRomaji:
                return false;
            // 設定が ON なら、変換した直後 (候補をまだ動かしていない) の数字は入力として打つ。候補を動かした後は、番号で選べる。
            // (選んだ文を Shift+Space で再変換したときは、何も打っていないので、最初から番号で選べる)
            case var typed when VirtualKeys.IsDigit(typed) && !shift && !_candidatesMoved && !_reconvertRomaji && _options.TypeDigitsAfterConversion():
                return false;
            // 候補ウィンドウの番号どおりに選んで、そのまま確定する (範囲外の番号はキーだけ消費して何もしない)。
            case var digit when VirtualKeys.IsDigit(digit) && !shift:
                if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
                var n = digit - 0x30;
                if (n >= 1 && n <= clause.Candidates.Count)
                {
                    clause.Index = n - 1;
                    clause.Changed = true;
                    Commit();
                }
                return true;
            // 候補ウィンドウの 1 ページ (9 個) 分ずつ進む／戻る。端では止まる。
            case VirtualKeys.Next:
                if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
                clause.Index = Math.Min(clause.Index + 9, clause.Candidates.Count - 1);
                clause.Changed = true;
                return true;
            case VirtualKeys.Prior:
                if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
                clause.Index = Math.Max(clause.Index - 9, 0);
                clause.Changed = true;
                return true;
            // ATOK スタイル (TokMode): ← → は文節の区切り、Shift+← → は文節の移動、↓ は部分確定。
            case VirtualKeys.Left or VirtualKeys.Right when _options.TokMode():
                if (_reconvertRomaji) return true; // ローマ字の再変換は区切りを変えられない
                if (shift) MoveClauseBy(vk == VirtualKeys.Left ? -1 : +1);
                else Resize(vk == VirtualKeys.Left ? -1 : +1);
                return true;
            case VirtualKeys.Down when _options.TokMode() && !shift:
                CommitThroughSelectedClause();
                return true;
            case VirtualKeys.Space:
            case VirtualKeys.Down:
                NextCandidate(+1);
                return true;
            case VirtualKeys.Up:
                NextCandidate(-1);
                return true;
            case VirtualKeys.Right when shift:
                Resize(+1);
                return true;
            case VirtualKeys.Left when shift:
                Resize(-1);
                return true;
            case VirtualKeys.Right:
                _selectedClause = Math.Min(_selectedClause + 1, _clauses.Count - 1);
                return true;
            case VirtualKeys.Left:
                _selectedClause = Math.Max(_selectedClause - 1, 0);
                return true;
            case VirtualKeys.Return:
                Commit();
                return true;
            case VirtualKeys.Tab when FindMisspelling() is { } typo:
                // もしかして: 書き間違いを直して変換し直す。
                _converting = false;
                FixMisspelling(typo);
                StartConversion();
                return true;
            case VirtualKeys.Tab when _tabStartedConversion:
                // Tab で始めた変換の中では、Tab が次の候補、Shift+Tab が前の候補 (issue #219)。Space で始めた変換の中の Tab は今までどおり (確定して Tab を渡す)。
                NextCandidate(shift || _host.IsShiftDown() ? -1 : +1);
                return true;
            case VirtualKeys.Escape when _toggleBox is { } toggleBox && _headCount == 0:
                // 確定後の F6 で開いた変換ボックス: Esc 1 回で元の語を書き戻す
                CancelToggleBoxToOriginal(toggleBox);
                return true;
            case VirtualKeys.Back:
            case VirtualKeys.Escape:
                _tabStartedConversion = false;
                if (_headCount > 0)
                {
                    // 未変換の文節の変換だけを取り消す (Backspace)。Esc、または未変換の文節が無いときは、固定した文節も変換前の読みに戻す。
                    if (vk == VirtualKeys.Escape || _clauses.Count == _headCount) UnfixClauses();
                    else
                    {
                        _converting = false;
                        TrimToFixedClauses();
                    }
                    return true;
                }
                // 変換を取り消して、かなの入力に戻る。
                _converting = false;
                return true;
            default:
                return false;
        }
    }

    private (int Start, int End, Misspelling Misspelling)? FindMisspelling() =>
        _options.Misspellings is { } dictionary ? _text.FindMisspelling(dictionary) : null;

    private void FixMisspelling((int Start, int End, Misspelling Misspelling) typo)
    {
        Diagnostics.Log.Decision($"もしかして: {Diagnostics.Log.Text(typo.Misspelling.Wrong)}→{Diagnostics.Log.Text(typo.Misspelling.Right)}に直しました。");
        _text.ReplaceReading(typo.Start, typo.End, typo.Misspelling.Right);
    }

    /// <summary>「もしかして」の案内。変換ボックスの、打った文字のすぐ下に出す (書き間違いが無ければ null)。</summary>
    private string? MisspellingSuggestion() => FindMisspelling() is { } typo ? $"もしかして: {typo.Misspelling.Right}　<Tab>で修正" : null;

    /// <summary>
    /// Space の分岐で変換を始める (StartConversion に行く) 場面か。Tab で始めるときも同じ場面に限る (issue #219)。
    /// Space と同じ順で判定するため、英語かを見る前に打ち間違いを直す (Enter で確定するときと同じ直し)。
    /// </summary>
    private bool TabStartsConversion()
    {
        // 保護区間 (パス・オプション・変数。1-4a) を含む入力は Tab で変換を始めない (確定して Tab を渡す)。本家と同じ。保護区間の後ろに日本語が続く
        // ./build.shde も含む。final: true は、--no-edit が打ち終わったときだけ保護区間になるため (統合の査読 R64 必須 1)
        if (_text.Mode == DisplayMode.Auto && _text.Segments(final: true).Any(s => s.Protected)) return false;
        if (_headCount == 0 && _text.Mode == DisplayMode.Auto && _text.KeepLoneHyphen()) return false;
        _text.FixTypos();
        var commitsAlphanumeric = _text.IsAlphanumericAt(final: true) && !(_text.Mode == DisplayMode.Auto && _text.IsNumeric && _text.Raw.All(char.IsAsciiDigit) && _text.PrecedingEnglish != true);
        return !commitsAlphanumeric && !EndsWithEnglish(final: true) && !(_text.Mode == DisplayMode.Auto && _detector.IsEnglishAtWordEnd(_text.Raw, _options.Level()));
    }

    /// <summary>変換前に矢印キーを押したとき: 文節に区切って、← なら最後の文節、→ なら最初の文節を選ぶ。</summary>
    private void EnterClauseSelection(int vk)
    {
        StartConversion();
        if (!_converting) return;
        var shift = _swallowedShift.Count > 0;
        _selectedClause = vk == VirtualKeys.Left ? _clauses.Count - 1 : _headCount;
        if (shift && vk is VirtualKeys.Left or VirtualKeys.Right) Resize(vk == VirtualKeys.Left ? -1 : +1);
        else if (vk == VirtualKeys.Up) NextCandidate(-1);
    }

    private void SetMode(DisplayMode mode)
    {
        // 固定した文節だけが残っている (未変換の文節が空の) ときは、表示モードを変える相手がいない。
        if (_headCount > 0 && _text.IsEmpty) return;
        _converting = false;
        TrimToFixedClauses();
        _text.Mode = mode;
    }

    // ---- 変換後も続けて入力 ----

    /// <summary>固定した文節の、選んだ候補をつなげた文字列。</summary>
    private string FixedText => string.Concat(_clauses.Take(_headCount).Select(c => c.Text));

    /// <summary>変換の文脈にする、キャレットの前の文字列。固定した文節があれば、その分も含める。</summary>
    private string? PrecedingForConversion => _headCount == 0 ? _precedingText : (_precedingText ?? "") + FixedText;

    /// <summary>未変換の文節の変換結果 (_clauses の固定した文節より後ろ) を捨てる。</summary>
    private void TrimToFixedClauses()
    {
        if (_headCount > 0 && _clauses.Count > _headCount) _clauses = _clauses.Take(_headCount).ToList();
    }

    /// <summary>
    /// 変換中に新しい文字が打たれたとき。今までは今の候補で確定して新しい入力を始めていた。
    /// ContinueAfterConversion が ON なら、各文節の選択を保ったまま固定し、新しい文字は空の _text (未変換の文節) に入れる。
    /// </summary>
    private void EndConversionForNewInput()
    {
        if (!_options.ContinueAfterConversion())
        {
            Commit(implicitly: true);
            BeginComposition();
            return;
        }
        // 初めて固定するとき、読みに戻すために元の手がかりを覚えておく。
        if (_headCount == 0)
        {
            _basePrecedingEnglish = _text.PrecedingEnglish;
            _basePrecedingSentence = _text.PrecedingEnglishSentence;
            _basePrecedingTail = _text.PrecedingTail;
        }
        _headCount = _clauses.Count;
        _converting = false;
        ResetPrediction();
        // 読みに戻すとき (Esc) のために、変換していた分の打った英字つきの単位を覚えておく。
        _fixedUnits.AddRange(_text.Units);
        // 変換した読みは文節が持っているので、_text は未変換の文節の分だけにする。
        _text.Clear();
        _text.KanaInput = _options.KanaInput();
        if (LanguageOf(FixedText) is { } english) _text.PrecedingEnglish = english;
        _text.PrecedingEnglishSentence = IsEnglishSentence(PrecedingForConversion);
        _text.PrecedingTail = FixedText;
    }

    /// <summary>固定した文節の最後を選んだ変換中の状態にする (未変換の文節を消し切ったとき)。</summary>
    private void SelectLastFixedClause()
    {
        TrimToFixedClauses();
        _selectedClause = _headCount - 1;
        _converting = true;
    }

    /// <summary>固定した文節を、変換前の読みに戻して、未変換の文節の前に付ける (変換前に打っていた状態に戻る)。</summary>
    private void UnfixClauses()
    {
        var reading = string.Concat(_clauses.Take(_headCount).Select(c => c.Reading));
        // 固定する前に打っていた単位 (打った英字つき) があれば、それを戻す (F10 で打ったままの英字に戻せる)。読みが合わなければ読みだけ。
        if (string.Concat(_fixedUnits.Select(u => u.Kana)) == reading) _text.PrependUnits(_fixedUnits);
        else _text.PrependReading(reading);
        _fixedUnits = [];
        // 未変換の文節だけに効かせていた表示モード (F7 など) は、全体には引き継がない。
        _text.Mode = DisplayMode.Auto;
        _text.PrecedingEnglish = _basePrecedingEnglish;
        _text.PrecedingEnglishSentence = _basePrecedingSentence;
        _text.PrecedingTail = _basePrecedingTail;
        _headCount = 0;
        _clauses = [];
        _converting = false;
        ResetPrediction();
    }

    /// <summary>
    /// 固定した文節があれば、学習して、その文字列を返す (未変換の文節だけを直接 CommitText するとき、前に付ける)。
    /// 確定の処理 (CommitText) が文節を片付けるので、ここでは捨てない。
    /// </summary>
    private string TakeFixedClauses()
    {
        if (_headCount == 0) return "";
        var text = FixedText;
        TrimToFixedClauses();
        Learn();
        return text;
    }

    /// <summary>
    /// 英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// </summary>
    private void StartConversion(bool preferJapanese = false)
    {
        _tabStartedConversion = false;
        // 固定した文節があるときは、未変換の文節だけを変換して、固定した文節の後ろにつなぐ。
        TrimToFixedClauses();
        var clauses = new List<Clause>();
        var segments = _text.ConversionSegments();
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            if (segment.IsEnglish)
            {
                var english = EnglishCandidates(segment.Raw);
                var romaji = RomajiCandidates(segment.Raw);
                clauses.Add(new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english])
                    : Distinct([.. english, .. romaji])));
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapanese(segment.Kana);
            // 英単語 + する (push|した、deploy|しよう): 変換エンジンは英語の後ろの「した」だけを見て 下・死体・使用 にしてしまう。かなのまま
            if (s > 0 && segments[s - 1].IsEnglish && japanese.Count > 0 && IsSuruAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = _text.RawForReading(s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddOldKana(clause);
                AddTranslations(clause);
                AddRawCandidates(clause);
            }
            clauses.AddRange(japanese);
        }
        if (clauses.Count == 0) return;
        if (_headCount > 0) clauses.InsertRange(0, _clauses.Take(_headCount));
        _clauses = clauses;
        _selectedClause = _headCount;
        _converting = true;
        _candidatesMoved = false;
    }

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    private void AddTranslations(Clause clause)
    {
        if (clause.IsEnglish || _options.Translations is not { } dictionary || !_options.TranslationCandidates()) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = _options.TranslationHistory?.Get(clause.Reading) ?? [];
        // 選んだ回数の多い順に前へ
        words = learned.Select(l => l.Word).Where(words.Contains).Concat(words.Where(w => !learned.Any(l => l.Word == w))).ToList();
        foreach (var word in words) clause.Translations.Add(word);
        var often = learned.FirstOrDefault(l => l.Count >= 2 && words.Contains(l.Word)).Word;
        if (often is not null)
        {
            clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), often);
            words.Remove(often);
        }
        clause.Candidates.AddRange(words);
    }

    private static void AddRawCandidates(Clause clause)
    {
        // 全角にした記号 (＃ （ ％) は、半角に戻した候補も出す (Space を続けて押すと半角の # ( %)。
        if (clause.Candidates.Count > 0 && CompositionText.SymbolsToHalfWidth(clause.Candidates[0]) is var half && half != clause.Candidates[0] &&
            !clause.Candidates.Contains(half))
        {
            clause.Candidates.Add(half);
        }
        if (clause.Raw is not { } raw || !raw.Any(c => c is >= '!' and <= '~')) return;
        foreach (var candidate in new[] { raw, CompositionText.ToFullWidth(raw) })
        {
            if (!clause.Candidates.Contains(candidate)) clause.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// wi / we で打った うぃ / うぇ は、ゐ / ゑ (ヰ / ヱ) にした候補も出す (うぃすきー → ゐすきー、ヰスキー)。
    /// 候補の中の うぃ ウィ うぇ ウェ を置き換えた形を、打ったままの英字の候補より前に足す。
    /// </summary>
    private static void AddOldKana(Clause clause)
    {
        if (clause.IsEnglish || clause.Raw is not { } raw) return;
        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("wi") && !lower.Contains("we")) return;
        if (!clause.Reading.Contains("うぃ") && !clause.Reading.Contains("うぇ")) return;
        static string Old(string text) => text.Replace("うぃ", "ゐ").Replace("うぇ", "ゑ").Replace("ウィ", "ヰ").Replace("ウェ", "ヱ");
        var added = new List<string>();
        foreach (var candidate in clause.Candidates.Concat([clause.Reading, CompositionText.ToKatakana(clause.Reading)]))
        {
            var old = Old(candidate);
            if (old != candidate && !clause.Candidates.Contains(old) && !added.Contains(old)) added.Add(old);
        }
        clause.Candidates.AddRange(added);
    }

    /// <summary>
    /// ユーザー辞書の読みが含まれていれば、その部分は登録した単語の文節にし、残りだけを変換エンジンで変換する
    /// (きごうとうふくめ → 記号等|含め)。含まれていなければ普通に変換する。
    /// ユーザーが登録した語のうち短い語が、変換エンジンの文節の途中から始まるときは、その語は使わない (<see cref="AlignUserWords"/>)。
    /// </summary>
    private List<Clause> ConvertJapanese(string kana)
    {
        // ローマ字として読めずに残った英字 (こほぃc の c) と半角の記号 (... @) は、変換エンジンに渡すと別の記号 (© ．．． ＠) にされるので、
        // そのままの文節にする。数字は、記号・英字とつながっていればそのまま (3.0 の 3 を 1 文字だけ渡すと ❸ にされる)、
        // それ以外は変換エンジンに渡す (2026ねん → 2026年)。
        var keep = new bool[kana.Length];
        for (var i = 0; i < kana.Length; i++) keep[i] = kana[i] is >= '!' and <= '~' && !char.IsAsciiDigit(kana[i]);
        for (var i = 0; i < kana.Length; i++)
        {
            if (!char.IsAsciiDigit(kana[i])) continue;
            var end = i;
            while (end < kana.Length && char.IsAsciiDigit(kana[end])) end++;
            var touches = (i > 0 && keep[i - 1]) || (end < kana.Length && keep[end]);
            for (var k = i; k < end; k++) keep[k] = touches;
            i = end - 1;
        }
        if (keep.Any(k => k))
        {
            var result = new List<Clause>();
            var start = 0;
            for (var i = 1; i <= kana.Length; i++)
            {
                if (i < kana.Length && keep[i] == keep[start]) continue;
                var run = kana[start..i];
                if (keep[start]) result.Add(new Clause(run, false, Distinct([run, .. _options.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(run));
                start = i;
            }
            return result;
        }
        if (_options.UserDictionary?.Split(kana) is not { } pieces) return ConvertWithEngine(kana);
        pieces = AlignUserWords(kana, pieces, out var wholeByEngine);
        if (wholeByEngine is not null) return wholeByEngine;
        var clauses = new List<Clause>();
        var registered = new List<Clause>();
        foreach (var (reading, word) in pieces)
        {
            if (word is null)
            {
                clauses.AddRange(ConvertWithEngine(reading));
                continue;
            }
            var clause = new Clause(reading, false, JapaneseCandidates(reading, word));
            clauses.Add(clause);
            registered.Add(clause);
        }
        // 同梱の語句 (しょせん → 所詮) も、文脈の手がかり (試合 → 初戦) と学習で選び直せるようにする。
        // 変換エンジンの文節も、語句の文節を含めた前後で文脈の手がかりを見直す (甲斐性ない + こうかい → 後悔)。
        foreach (var clause in clauses)
        {
            var surrounding = (PrecedingForConversion ?? "") + string.Concat(clauses.Where(c => c != clause).Select(c => c.Text)) + (_followingText ?? "");
            var preferred = _options.ContextRules?.Choose(clause.Reading, surrounding) ?? (registered.Contains(clause) ? _options.History?.Get(clause.Reading) : null);
            if (preferred is not null) Prefer(clause, preferred);
        }
        return clauses;
    }

    /// <summary>
    /// この文字数より短い読みのユーザーの語は、変換エンジンの文節の途中から始まるときは使わない (<see cref="AlignUserWords"/>)。
    /// この文字数以上の読みの語は、途中から始まっても使う。
    /// 短い語 (しの → 紫乃) は、わたしのことがすき の途中の しの のように、ほかの語の一部に偶然現れやすい。
    /// 長い語 (めるたいぷ → Meltype) は偶然つながりにくく、変換エンジンは未知の語を助詞とくっつけて区切りがち
    /// (きょうは|めるたいぷを|つかう のように、助詞の後ろの語が文節の途中になる) なので、途中から始まっても使うほうが登録した意図に合う。
    /// 実際の azooKey で登録語入りの約 140 文を比べた結果: 3 文字にすると、本家の問題例 (しの) が直り、変更前に使えていた登録語が落ちるのは 2 文字の語だけになる。
    /// 4 文字以上にすると、3 文字の語 (さくら・ひなた・ゆうき などの名前) が助詞の後ろで落ちる。
    /// </summary>
    internal const int InsideClauseMinReadingLength = 3;

    /// <summary>
    /// ユーザーが登録した語 (自作の専門用語集の語を含む) の読みが、変換エンジンの文節の途中から始まるなら、その語では区切らない (本家 #374・PR #385)。
    /// しの → 紫乃 を登録すると、わたしのことがすき の途中の しの で区切って 綿紫乃事が好き になっていた
    /// (変換エンジンは わたしの|ことが|すき と区切るので、しの は文節の途中から始まる)。文節の頭から始まる語 (しのがきた の しの) は今までどおり。
    /// 対象は、読みが <see cref="InsideClauseMinReadingLength"/> 文字より短い語だけ。それ以上の長さの語は、途中から始まっても使う
    /// (実際の azooKey は きょうは|めるたいぷを|つかう のように助詞とくっつけて区切るので、文節の頭かどうかだけで決めると、助詞の後ろの登録語が落ちる)。
    /// 同梱の語句 (phrases.txt) と同梱の専門用語集の強制語は、変換エンジンが区切りを間違える語を補うものなので、そのまま使う。
    /// 使わなくなった語は、文節の区切りを Shift+←→ で変えれば、候補から選べる。
    /// 変換エンジンを余分に呼ぶのは、読みの先頭以外から始まる、短い (しきい値より短い) ユーザーの語が入っているときだけ (先頭は必ず文節の頭)。
    /// 結果はキャッシュ付きの ConvertWithEngine と同じなので、同じ読みは打つたびにまた変換しない。
    /// 変換エンジンが文節に区切れなかったとき (ConvertClauses が null) は、文節の頭が分からないので、何も変えずに元の語の割り当てを返す。
    /// 使う語が 1 つも残らなければ、変換エンジンの結果 (wholeByEngine) をそのまま返す。
    /// </summary>
    private List<(string Reading, string? Word)> AlignUserWords(string kana, List<(string Reading, string? Word)> pieces, out List<Clause>? wholeByEngine)
    {
        wholeByEngine = null;
        if (_options.UserDictionary is not { } dictionary) return pieces;
        var position = 0;
        var inside = false;
        foreach (var (reading, word) in pieces)
        {
            if (position > 0 && word is not null && reading.Length < InsideClauseMinReadingLength && dictionary.IsUserReading(reading)) inside = true;
            position += reading.Length;
        }
        if (!inside) return pieces;
        var engine = ConvertWithEngine(kana, out var failed);
        // 失敗した変換は、読み全体を 1 文節にした結果 (文節の頭が位置 0 だけ) なので、使うと先頭以外の語が全部落ちる
        if (failed || string.Concat(engine.Select(c => c.Reading)) != kana) return pieces;
        var starts = new HashSet<int>();
        for (int i = 0, offset = 0; i < engine.Count; offset += engine[i].Reading.Length, i++) starts.Add(offset);
        var aligned = new List<(string Reading, string? Word)>();
        position = 0;
        foreach (var (reading, word) in pieces)
        {
            var keep = word is not null && (reading.Length >= InsideClauseMinReadingLength || !dictionary.IsUserReading(reading) || starts.Contains(position));
            // 使わない語の読みは、前後の変換エンジンに渡す部分とつなげる
            if (!keep && aligned.Count > 0 && aligned[^1].Word is null) aligned[^1] = (aligned[^1].Reading + reading, null);
            else aligned.Add(keep ? (reading, word) : (reading, null));
            position += reading.Length;
        }
        if (aligned.All(p => p.Word is null)) wholeByEngine = engine;
        return aligned;
    }

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// </summary>
    private List<Clause> ConvertWithEngine(string kana) => ConvertWithEngine(kana, out _);

    /// <summary>failed は、変換エンジンが文節に区切れなかった (ConvertClauses が null を返した) ことがあったとき true。その結果は、読み全体を 1 文節にしたものになる。</summary>
    private List<Clause> ConvertWithEngine(string kana, out bool failed)
    {
        failed = false;
        if (kana.Length <= ConvertChunkLength) return ConvertWithEngineOnce(kana, out failed);
        // 長い読みは区切って変換する (変換エンジンの時間は読みの長さに比例するので、1 文字打つたびに全体を変換し直すと、長くなるほど重くなる)。
        // 区切りは「読みの前の部分」だけで決まるので、続きを打っても前の区切りは変わらず、前の部分の変換結果は覚えてあるものを使える。
        var clauses = new List<Clause>();
        var previous = "";
        try
        {
            foreach (var chunk in SplitForConversion(kana))
            {
                // 2 つ目以降の区切りには、前の区切りの変換結果が文脈 (キャレットの前の文字) になる。
                if (previous.Length > 0) _chunkContext = (true, ContextOf(previous));
                var converted = ConvertWithEngineOnce(chunk, out var chunkFailed);
                failed |= chunkFailed;
                clauses.AddRange(converted);
                previous += string.Concat(converted.Select(c => c.Text));
            }
        }
        finally
        {
            _chunkContext = (false, null);
        }
        return clauses;
    }

    /// <summary>この長さ (文字数) を超える読みは区切って変換する。</summary>
    internal const int ConvertChunkLength = 50;

    /// <summary>変換エンジンに渡す文脈を、区切りの途中だけ前の区切りの変換結果にする。</summary>
    private (bool Active, string? Text) _chunkContext;

    /// <summary>
    /// 長い読みを区切る。区切る位置は、先頭から見て決まる (後ろに文字が増えても、すでに区切った位置は動かない):
    /// 文末 (。！？) の後ろ、読点 (、) の後ろ (12 文字以上たまっていれば)、無ければ変換エンジンが文節に分けた位置 (最後の文節は窓の端で切れているので除く)、
    /// それも無ければ長さの上限。小さい ゃ ゅ ょ や っ の途中では切らない。
    /// </summary>
    private List<string> SplitForConversion(string kana)
    {
        var chunks = new List<string>();
        var start = 0;
        while (kana.Length - start > ConvertChunkLength)
        {
            var cut = -1;
            for (var i = start; i < start + ConvertChunkLength && cut < 0; i++)
            {
                if (kana[i] is '。' or '！' or '？' || kana[i] is '、' or '，' && i + 1 - start >= 12) cut = i + 1;
            }
            if (cut < 0)
            {
                var window = kana.Substring(start, ConvertChunkLength);
                var parts = EngineClauses(window, null);
                if (parts is { Count: >= 2 } && string.Concat(parts.Select(p => p.Reading)) == window)
                {
                    var length = window.Length - parts[^1].Reading.Length;
                    if (length >= ConvertChunkLength / 4) cut = start + length;
                }
            }
            if (cut < 0) cut = start + ConvertChunkLength;
            while (cut > start + 1 && cut < kana.Length && (SmallKana.Contains(kana[cut]) || kana[cut] == 'ー' || kana[cut - 1] == 'っ')) cut--;
            chunks.Add(kana[start..cut]);
            start = cut;
        }
        chunks.Add(kana[start..]);
        return chunks;
    }

    /// <summary>
    /// 同じ読み・文脈の変換エンジンの結果を覚えておく (打つたびの変換で、変わっていない前の部分をまた変換しないため)。
    /// 変換ボックスを閉じたら捨てる。いっぱいになったら、使ってから一番長いものから捨てる
    /// (今の区切りは打つたびに使うので残り、一度に全部が変換し直しになる山はできない)。
    /// </summary>
    private readonly Dictionary<(string Kana, string? Context), (IReadOnlyList<ConversionClause> Parts, long Used)> _clauseCache = [];
    private long _clauseCacheClock;
    private const int ClauseCacheLimit = 512;

    private IReadOnlyList<ConversionClause>? EngineClauses(string kana, string? context)
    {
        var key = (kana, context);
        if (_clauseCache.TryGetValue(key, out var cached))
        {
            _clauseCache[key] = (cached.Parts, ++_clauseCacheClock);
            return cached.Parts;
        }
        var parts = _converter.ConvertClauses(kana, context);
        // 失敗 (null) は覚えない: 次のキー・Space・Backspace でもう一度試せるように
        if (parts is null) return null;
        if (_clauseCache.Count >= ClauseCacheLimit)
        {
            foreach (var old in _clauseCache.OrderBy(e => e.Value.Used).Take(ClauseCacheLimit / 8).Select(e => e.Key).ToList()) _clauseCache.Remove(old);
        }
        _clauseCache[key] = (parts, ++_clauseCacheClock);
        return parts;
    }

    private void ClearConversionCaches()
    {
        _clauseCache.Clear();
        _detector.ClearSpanCache();
    }

    private List<Clause> ConvertWithEngineOnce(string kana) => ConvertWithEngineOnce(kana, out _);

    private List<Clause> ConvertWithEngineOnce(string kana, out bool failed)
    {
        // 文脈が無いまま「に」で始まる読みを変換すると、変換エンジンは「に」を語の頭と読む (になってしまう → 担ってしまう)。
        // 前の文脈が取れないときは、仮の文脈「これ」を付けて助詞として読ませる (これ + になってしまう → になってしまう)。
        // 「は」「で」なども助詞になりうるが、仮の文脈を付けると はしる → は知る のように崩れるので「に」だけ。
        var context = ConversionContext();
        var parts = context is not null
            ? ConvertWithContext(kana, context)
            : EngineClauses(kana, kana.Length >= 3 && kana[0] == 'に' ? "これ" : null);
        // 文節に区切れなかったときは、全体を 1 文節にする。この 1 文節は変換エンジンの区切りではない (failed で呼び出し元に知らせる)
        failed = parts is null;
        parts ??= [new ConversionClause(kana, _converter.Convert(kana) ?? kana)];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && _options.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(parts);
        var clauses = parts.Select(p => new Clause(p.Reading, false, JapaneseCandidates(p.Reading, NormalizeHalfWidth(p.Text, p.Reading)))).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (PrecedingForConversion ?? "") + others + (_followingText ?? "");
            // 数字のすぐ後ろの かける・わる は × ÷ (3かける4 → 3×4)。覚えた変換より優先する。たす・ひく などは記号を候補の 2 番目に足す (本家 #321・#293)
            var math = AddMathSymbol(clauses, i);
            var preferred = math ?? _options.ContextRules?.Choose(clauses[i].Reading, surrounding) ?? _options.History?.Get(clauses[i].Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            preferred ??= IsSymbolOnly(clauses[i].Text) && clauses[i].Reading.All(c => c is >= 'ぁ' and <= 'ゖ') ? clauses[i].Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= Particles.Contains(clauses[i].Reading) && clauses[i].Text != clauses[i].Reading ? clauses[i].Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clauses[i].Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clauses[i].Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(clauses, i) ? clauses[i].Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clauses[i].Reading.StartsWith("がち", StringComparison.Ordinal) && clauses[i].Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clauses[i].Text["勝ち".Length..] : null;
            if (preferred is not null) Prefer(clauses[i], preferred);
        }
        return clauses;
    }

    /// <summary>
    /// 計算の言葉 (かける・わる …) で始まる文節の読み。前に数字が付いていてもよい (3かける)。後ろには何が続いてもよい
    /// (azooKey は 3|かける4は12 のように、かけるの後ろの数字と助詞まで 1 つの文節にする)。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MathOperator =
        new(@"^(?<a>(?:[0-9０-９]+(?:[.．][0-9０-９]+)?)?)(?<op>かける|わる|たす|ひく|ぷらす|まいなす|いこーる)(?<rest>.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);

    private static bool IsDigit(char c) => char.IsAsciiDigit(c) || c is >= '０' and <= '９';

    private static readonly char[] MathSymbols = ['×', '÷', '+', '＋', '-', '－', '−', '=', '＝'];

    /// <summary>
    /// 数字のすぐ後ろの計算の言葉の文節に、記号の候補を足す (3かける4 → 3×4、1たす1 → 1+1: 本家 #321・#293)。
    /// かける・わる は、前が数字で、後ろが数字か文の終わりのときだけ記号を最初の候補にする (その文字列を返す)。
    /// 後ろに言葉が続く (3かける人、10わるい) ときは動詞なので動かさない。
    /// たす・ひく などは動詞 (足す・引く) のことも多いので、2 番目の候補に足すだけ (null を返す)。
    /// 5分かける・3時間かける のように、数字と かける の間に単位があれば前が数字ではないので何もしない。
    /// + - = の全角・半角は、前の数字に合わせる。
    /// </summary>
    private string? AddMathSymbol(List<Clause> clauses, int i)
    {
        var clause = clauses[i];
        if (clause.IsEnglish || clause.Candidates.Count == 0 || MathOperator.Match(clause.Reading) is not { Success: true } match) return null;
        var digits = match.Groups["a"].Value;
        var rest = match.Groups["rest"].Value;
        var before = digits.Length > 0 ? digits : i > 0 ? clauses[i - 1].Text : PrecedingForConversion ?? "";
        if (before.Length == 0 || !IsDigit(before[^1])) return null;
        // 数字の前が英字 (mp3・A4) なら、数ではなく名前の一部
        var start = before.Length;
        while (start > 0 && (IsDigit(before[start - 1]) || before[start - 1] is '.' or '．')) start--;
        if (start > 0 && char.IsAsciiLetter(before[start - 1])) return null;
        var op = match.Groups["op"].Value;
        // 後ろが数字で始まるか、文の終わりか (かける人・わるい・かけるなら のように言葉が続くなら動詞)
        var numeric = rest.Length > 0 ? IsDigit(rest[0])
            : i + 1 < clauses.Count ? clauses[i + 1].Text is [var head, ..] && IsDigit(head)
            : _followingText is not [var following, ..] || !char.IsLetter(following);
        if (!numeric) return null;
        var full = !char.IsAsciiDigit(before[^1]);
        var symbol = op switch
        {
            "かける" => "×",
            "わる" => "÷",
            "たす" or "ぷらす" => full ? "＋" : "+",
            "ひく" or "まいなす" => full ? "－" : "-",
            _ => full ? "＝" : "=",
        };
        // 変換エンジンの文字列のうち、動詞の部分 (掛ける・書ける・悪) だけを記号にする。後ろの数字・助詞 (4は12) はそのまま
        var engine = clause.Candidates[0];
        var lead = 0;
        if (digits.Length > 0) while (lead < engine.Length && (IsDigit(engine[lead]) || engine[lead] is '.' or '．')) lead++;
        var tailStart = rest.Length == 0 ? engine.Length : lead;
        while (tailStart < engine.Length && !IsDigit(engine[tailStart])) tailStart++;
        var tail = rest.Length == 0 ? "" : tailStart < engine.Length ? engine[tailStart..] : rest;
        var text = engine[..lead] + symbol + tail;
        if (op is "かける" or "わる") return text;
        // たす・ひく: 変換エンジンがすでに記号を最初にしていれば足さない
        if (engine.IndexOfAny(MathSymbols) >= 0) return null;
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), text);
        return null;
    }

    /// <summary>計算の言葉の文節で、記号の候補 (×、+) を選んだか。学習しない (覚えると 電話をかける まで × になる)。</summary>
    private static bool IsMathSymbolChoice(Clause clause) =>
        MathOperator.Match(clause.Reading) is { Success: true } match && (match.Groups["rest"].Value is not [var head, ..] || IsDigit(head))
        && clause.Text.IndexOfAny(MathSymbols) >= 0;

    /// <summary>する の活用 (した・して・しない・しよう・したい …) で始まる読み。</summary>
    private static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>英単語の後ろの、する の活用の文節を、変換エンジンが漢字で始めた (した → 下、したい → 死体、しよう → 使用)。</summary>
    private static bool IsSuruAfterEnglish(Clause clause) =>
        SuruForms.Any(clause.Reading.StartsWith) && clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    private static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    private bool IsInterjection(List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : PrecedingForConversion ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && SmallKana.Contains(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && SmallKana.Contains(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(reading);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => SmallKana.Contains(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    private const string SmallKana = "ぁぃぅぇぉゃゅょゎァィゥェォャュョヮ";

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    /// <summary>
    /// 文脈付きと文脈なしの両方で変換し、文節の区切りが同じなら文脈付き (漢字の選び方だけが文脈で変わる: この本は + あつい → 厚い)、
    /// 区切りまで変わるなら文脈なしを使う。前に同じ語があると、変換エンジンは文脈に引きずられて区切りを変えてしまうため
    /// (「記号等」含め、 + きごうとう → き|ごうとう → 気強盗)。
    /// </summary>
    private IReadOnlyList<ConversionClause>? ConvertWithContext(string kana, string context)
    {
        var withContext = EngineClauses(kana, context);
        var plain = EngineClauses(kana, null);
        if (withContext is null || plain is null) return withContext ?? plain;
        return withContext.Select(c => c.Reading).SequenceEqual(plain.Select(c => c.Reading)) ? withContext : plain;
    }

    /// <summary>変換エンジンに渡す文脈: キャレットの前の確定済みの文字のうち、同じ文の日本語の部分 (最大 10 文字)。</summary>
    private string? ConversionContext() => _chunkContext.Active ? _chunkContext.Text : ContextOf(PrecedingForConversion);

    private string? ContextOf(string? text)
    {
        if (string.IsNullOrEmpty(text) || LanguageOf(text) != false) return null;
        var start = text.LastIndexOfAny(SentenceEnds);
        text = text[(start + 1)..].Trim();
        return text.Length == 0 ? null : text.Length > 10 ? text[^10..] : text;
    }

    private static readonly char[] SentenceEnds = ['。', '！', '？', '\n', '\r'];

    private static readonly HashSet<string> Particles = ["は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "から", "まで", "より"];

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    private static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → カタカナ。
    /// </summary>
    private List<string> JapaneseCandidates(string reading, string? inContext)
    {
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        var candidates = Distinct(inContext);
        foreach (var word in _options.UserDictionary?.Lookup(reading) ?? []) if (!candidates.Contains(word)) candidates.Add(word);
        if (Convert(reading) is var standalone && !candidates.Contains(standalone)) candidates.Add(standalone);
        // 専門用語集の読みの短い語 (候補追加型) は、候補に足すだけ (強制しない)。
        foreach (var term in _options.UserDictionary?.LookupTermCandidates(reading) ?? []) if (!candidates.Contains(term)) candidates.Add(term);
        foreach (var extra in _options.Candidates?.Lookup(reading) ?? [])
        {
            if (!candidates.Contains(extra)) candidates.Add(extra);
        }
        foreach (var kana in new[] { reading, CompositionText.ToKatakana(reading), CompositionText.ToHalfWidthKatakana(reading) })
        {
            if (!candidates.Contains(kana)) candidates.Add(kana);
        }
        return candidates;
    }

    /// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    private List<string> EnglishCandidates(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(raw, _detector.ProperNouns.Canonical(lower), capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private string? CandidateMeaning(Clause clause) =>
        _options.Meanings?.Lookup(clause.Text, clause.IsEnglish ? null : clause.Reading) ?? _options.Translations?.Meaning(clause.Text);

    /// <summary>
    /// 候補の右に小さく出す注記: 英訳 (complex)、半角 / 全角 (同じ記号・英数字の半角と全角が両方候補にあるとき。# と ＃ は見分けにくい)。
    /// 注記が 1 つも無ければ null。
    /// </summary>
    private static List<string?>? CandidateNotes(Clause clause)
    {
        var notes = clause.Candidates.Select(c =>
        {
            if (clause.Translations.Contains(c)) return "英訳";
            var half = ToHalfWidth(c);
            if (half != c && clause.Candidates.Contains(half)) return "全角";
            var full = CompositionText.ToFullWidth(c);
            if (full != c && clause.Candidates.Contains(full)) return "半角";
            return null;
        }).ToList();
        return notes.Any(n => n is not null) ? notes : null;
    }

    /// <summary>全角の英数字・記号 (！〜～) と全角の空白を半角にする。</summary>
    private static string ToHalfWidth(string text) =>
        new(text.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());

    /// <summary>英語と判定した語を、ローマ字として読んだときの候補 (go → 語 ご ゴ)。読み切れなければ空。</summary>
    private List<string> RomajiCandidates(string raw)
    {
        if (_detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true, Partial: "" } analysis || analysis.Kana.Length == 0) return [];
        return JapaneseCandidates(analysis.Kana, null);
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    private static bool IsJapaneseText(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿');

    private static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

    private void NextCandidate(int step)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
        clause.Index = (clause.Index + step + clause.Candidates.Count) % clause.Candidates.Count;
        clause.Changed = true;
    }

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧 (はし → 橋 端 箸 …) を 2 番目以降に足す。
    /// 打つたびには呼ばない (時間がかかることがあるため。ライブ変換では呼ばない)。今選んでいる候補はそのまま選んだ状態にする。
    /// </summary>
    private void Expand(Clause clause)
    {
        clause.Expanded = true;
        if (_options.MoreCandidates is not { } more) return;
        var current = clause.Text;
        var merged = new List<string> { clause.Candidates[0] };
        foreach (var candidate in more(clause.Reading).Select(c => NormalizeHalfWidth(c, clause.Reading)).Concat(clause.Candidates.Skip(1)))
        {
            if (!merged.Contains(candidate)) merged.Add(candidate);
        }
        clause.Candidates = merged;
        clause.Index = Math.Max(0, merged.IndexOf(current));
    }

    /// <summary>
    /// 変換エンジンは読みの中の英数字を全角にする (2025ねん → ２０２５年、sだけが → ｓだけが) ので、
    /// 半角で打った数字・英字は半角に戻す。読みに半角の英数字が無ければ (全角を選んだ候補など) そのまま。
    /// </summary>
    internal static string NormalizeHalfWidth(string converted, string reading)
    {
        var digits = reading.Any(char.IsAsciiDigit);
        var letters = reading.Any(char.IsAsciiLetter);
        if (!digits && !letters) return converted;
        var chars = converted.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (digits && chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - 0xFEE0);
            else if (letters && chars[i] is >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') chars[i] = (char)(chars[i] - 0xFEE0);
        }
        return new string(chars);
    }

    /// <summary>
    /// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
    /// 変わった文節は読みから変換し直す。英語の文節とは区切りをやり取りしない。
    /// </summary>
    private void Resize(int delta)
    {
        var current = _clauses[_selectedClause];
        if (current.IsEnglish) return;
        var next = _selectedClause + 1 < _clauses.Count ? _clauses[_selectedClause + 1] : null;
        if (next is { IsEnglish: true }) next = null;
        // 固定した文節と未変換の文節の境目は、またいで区切りを変えない。
        if (_headCount > 0 && _selectedClause == _headCount - 1) return;

        if (delta > 0)
        {
            if (next is null) return;
            current.Reading += next.Reading[0];
            next.Reading = next.Reading[1..];
            if (next.Reading.Length == 0)
            {
                if (_clauses.IndexOf(next) < _headCount) _headCount--;
                _clauses.Remove(next);
            }
            else Reconvert(next);
        }
        else
        {
            if (current.Reading.Length <= 1) return;
            var moved = current.Reading[^1];
            current.Reading = current.Reading[..^1];
            if (next is null)
            {
                next = new Clause(moved.ToString(), false, []);
                _clauses.Insert(_selectedClause + 1, next);
                // 固定した文節の中 (後ろが英語の文節のとき) に足した文節は、固定した側に数える。数えないと、
                // 最後の固定した文節が未変換の側に押し出され、あとで捨てられる。
                if (_selectedClause < _headCount) _headCount++;
            }
            else next.Reading = moved + next.Reading;
            Reconvert(next);
        }
        Reconvert(current);
    }

    private void Reconvert(Clause clause)
    {
        clause.Candidates = JapaneseCandidates(clause.Reading, null);
        clause.Index = 0;
        clause.Changed = false;
        if (_options.History?.Get(clause.Reading) is { } learned) Prefer(clause, learned);
    }

    /// <summary>同じかなを何度も変換しないようにキャッシュする。</summary>
    private string Convert(string kana)
    {
        DropCachesIfLearned();
        if (_conversionCache.TryGetValue(kana, out var cached)) return cached;
        var converted = NormalizeHalfWidth(_converter.Convert(kana) ?? kana, kana);
        if (_conversionCache.Count > 256) _conversionCache.Clear();
        _conversionCache[kana] = converted;
        return converted;
    }

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習・ユーザー辞書の状態が同じならキャッシュを使う。
    /// </summary>
    private string LiveConvert(string kana)
    {
        if (kana.Length < LiveConversionMinLength) return kana;
        DropCachesIfLearned();
        var key = string.Join("\u0001", kana, PrecedingForConversion, _followingText, _options.History?.Version, _options.UserDictionary?.Version);
        if (_liveCache.TryGetValue(key, out var cached)) return cached;
        var converted = string.Concat(ConvertJapanese(kana).Select(c => c.Text));
        if (_liveCache.Count > 256) _liveCache.Clear();
        _liveCache[key] = converted;
        return converted;
    }

    /// <summary>今の表示 (ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    private string CurrentDisplay(bool final) =>
        _text.Display(final, _options.LiveConversion() ? LiveConvert : null);

    // ---- 確定 ----

    /// <summary>暗黙の確定 (フォーカス移動・クリック・英数キー・Ctrl ショートカット): 文字は確定するが、F6 の変換ボックスの学習はしない。</summary>
    private void CommitIfAny()
    {
        if (!_text.IsEmpty || _headCount > 0) Commit(implicitly: true);
    }

    /// <param name="implicitly">暗黙の確定か。F6 で開いた変換ボックスは、Enter・Space など自分で確定したときだけ日本語として学習する (R43-3)。</param>
    private void Commit(string suffix = "", bool fixEnglish = false, bool implicitly = false)
    {
        var converting = _converting && _clauses.Count > 0;
        // 変換後も続けて入力: 固定した文節 + 未変換の文節 (変換していなければ、表示のまま)。
        var fixedOnly = !converting && _headCount > 0;
        if (fixedOnly) TrimToFixedClauses();
        var text = converting ? string.Concat(_clauses.Select(c => c.Text)) : fixedOnly ? FixedText + CurrentDisplay(final: true) : CurrentDisplay(final: true);
        if (fixEnglish && !converting && _text.Mode == DisplayMode.Auto) text = FixEnglishTypo(text);
        var english = converting ? _clauses.All(c => c.IsEnglish) : _text.IsAlphanumericAt(final: true);
        // 確定後の F10 / F6 の対象: 英語の区間・日本語の区間があるか (確定 1 回分まるごと)
        var hasEnglish = english || (converting ? _clauses.Any(c => c.IsEnglish) : _text.Mode == DisplayMode.Auto && _text.Segments(final: true).Any(s => s.IsEnglish));
        var hasJapanese = !english && (converting ? _clauses.Any(c => !c.IsEnglish) : _text.Mode != DisplayMode.Auto || _text.Segments(final: true).Any(s => !s.IsEnglish));
        // 利用者がはっきり決めた確定 (候補を選び直した・F6〜F10 で表示を変えた・Tab で手動の提案を受けた) は、後ろの語を見て自動で確定し直さない
        // (sushi を F10 で英字にして確定したのに、次の「です」で「すし」に書き戻されていた)
        var chosen = ChosenByUser(converting || fixedOnly);
        var raws = SegmentRaws(converting);
        // 確定後の F6 で開いた変換ボックス (読みは打った英字から作ったかな。Raw は変換ボックスの中身ではなく、元の英字を使う)。
        // 続けて打って文節を固定していても (ContinueAfterConversion)、控えがあれば末尾の空白を戻す
        var toggle = _toggleBox;
        // ローマ字の再変換は、打ち直した文ではないので学習しない (日本語にした文を読みとして覚えない)
        if (converting && !_reconvertRomaji) Learn();
        else
        {
            if (fixedOnly) Learn();
            if (toggle is null) LearnLanguage();
        }
        // 日英の学習は、ここで 1 回だけ (Learn・LearnLanguage は、打った英字が無い変換ボックスでは日英を書かないので、二重にならない。R41-6)
        // 暗黙の確定 (フォーカス移動・クリックなど) は、文字は確定するが学習しない
        // 学習の単位は、判定と同じ区間の語 (sushi)。確定 1 回分まるごとの打った英字 (kyouhasushidesu) では、判定で引かれず効かない
        // 1 音確定 (Shift+↓) をしたあとの残りでは学習しない (LearnLanguage と同じ。R46 任意 1)
        if (toggle is not null && !implicitly && !_firstSoundCommitted)
        {
            foreach (var word in toggle.Previous.Raws.English) _options.Languages?.Remember(word, english: false, explicitChoice: true);
        }
        CommitText(text + (suffix.Length > 0 ? suffix : toggle?.Tail ?? ""), english, toggle?.Raw ?? _text.Raw, chosen, hasEnglish, hasJapanese, toggle is null ? raws : ToggleRaws(toggle));
    }

    /// <summary>
    /// 利用者がはっきり決めた確定か (候補を選び直した・F6〜F10 で表示を変えた・Tab で手動の提案を受けた)。決めた確定は、後ろの語を見て自動で確定し直さない。
    /// clausesInUse = 文節が確定の中身になっているか (変換中・固定した文節あり)。
    /// </summary>
    private bool ChosenByUser(bool clausesInUse) =>
        (clausesInUse && _clauses.Any(c => c.Changed)) || _text.Mode != DisplayMode.Auto || _text.LevelOverride is not null;

    /// <summary>
    /// 確定後の F6 → Enter の確定の、区間ごとの打った英字。直す前の確定の区間が 1 つで、それが打った英字まるごと (sushi) のときだけ、続く F10 で覚えられる
    /// (kyouhasushidesu のように、まるごとが 1 区間でないものを英語として覚えない)。
    /// </summary>
    private static SegmentRawSet ToggleRaws(ToggleBox toggle)
    {
        var previous = toggle.Previous.Raws;
        var single = previous.English.Concat(previous.Japanese).ToList();
        return single.Count == 1 && string.Equals(single[0], toggle.Raw, StringComparison.OrdinalIgnoreCase)
            ? new SegmentRawSet([], [toggle.Raw], JapaneseWord: true)
            : new SegmentRawSet([], [], JapaneseWord: false);
    }

    /// <summary>
    /// 確定する文字列の、英語だった区間・日本語だった区間それぞれの打った英字 (確定後の F6 / F10 の学習の単位)。
    /// 変換中は文節から (英語の文節 = その英字、続く日本語の文節 = 文節ごとの英字をつないだもの)、そうでなければ表示モードに応じて区間から (区間ごとに 1 つ)。
    /// </summary>
    private SegmentRawSet SegmentRaws(bool converting)
    {
        List<string> english = [];
        List<string> japanese = [];
        if (converting)
        {
            var run = new StringBuilder();
            var runKnown = true;
            var japaneseClauses = 0;
            void Flush()
            {
                if (run.Length > 0 && runKnown) japanese.Add(run.ToString());
                run.Clear();
                runKnown = true;
            }
            foreach (var clause in _clauses)
            {
                if (clause.IsEnglish)
                {
                    Flush();
                    english.Add(clause.Reading);
                }
                else
                {
                    japaneseClauses++;
                    if (clause.Raw is { } raw) run.Append(raw);
                    else runKnown = false;
                }
            }
            Flush();
            // 変換して確定したなら、日本語の文節が 1 つのときだけ 1 語 (今日は|寒い は 2 文節なので 1 語ではない)
            return new SegmentRawSet(english, japanese, JapaneseWord: japaneseClauses == 1 && japanese.Count == 1);
        }
        if (_text.Mode != DisplayMode.Auto)
        {
            // F6〜F10 で表示を決めた: 確定した文字は全体が 1 つの言語。ただし学習の単位は、入力中の F10 / F6 の学習 (LearnLanguage) とそろえて自動の区間から作る
            // (確定後のトグルで、打った英字まるごと kyouhasushidesu を覚えない。R46-3)
            var automatic = _text.Segments(final: true);
            if (_text.IsAlphanumericAt(final: true))
            {
                // 英字にした: 確定後の F6 で日本語として覚え直すのは、自動でも英語だった区間と、入力中の F10 で英語として覚えた日本語 1 区間 (TryToggleLastWord の英字にする側と同じ形)
                english.AddRange(automatic.Where(s => s.IsEnglish).Select(s => s.Raw));
                if (automatic.Count == 1 && !automatic[0].IsEnglish) english.Add(automatic[0].Raw);
                return new SegmentRawSet(english, japanese, JapaneseWord: false);
            }
            // かな・カタカナにした: 自動の区間が 1 つだったときだけ 1 語 (英語と日本語が混ざっていたら、確定後の F10 で文まるごとを英語として覚えない)
            japanese.Add(_text.Raw);
            return new SegmentRawSet(english, japanese, JapaneseWord: automatic.Count == 1);
        }
        else
        {
            foreach (var segment in _text.Segments(final: true)) (segment.IsEnglish ? english : japanese).Add(segment.Raw);
        }
        // 変換しない確定 (ライブ変換・かな) は文節が分からない。日本語の区間が 1 つなら 1 語として扱う
        // (nihongo → F10 は覚えたい。kyouhasamui のような文まるごとも覚えるが、同じ文をそのまま打たない限り効かないだけで害は小さい。KNOWN_LIMITS)
        return new SegmentRawSet(english, japanese, JapaneseWord: japanese.Count == 1);
    }

    /// <summary>
    /// 1 音確定 (Shift+↓): 先頭の 1 音 (英字は 1 文字) だけを、見えている形で確定する。残りは入力のまま続ける。
    /// 1 音だけでは語にならないので、変換・英語/日本語の学習はせず、前の確定の確定し直しの対象にもしない。
    /// </summary>
    private void CommitFirstSound()
    {
        if (_text.TakeFirst() is not { } first)
        {
            // 入力途中の子音だけ (語末の n など) が残っているなら、それを確定する (n → ん)。
            Commit();
            return;
        }
        // 一部を確定したので、再変換の Esc で元の文字を書き戻さない (確定した分と二重になる)。
        _reconvertOriginal = null;
        // 最後の 1 音なら入力を終える (表示は閉じる)。
        if (_text.IsEmpty) CommitText("", first.English);
        // 残りは語の途中から始まるので、このあとの確定で英字 / かなの選び方 (F6〜F10) や変換を学習しない (kanpai の npai を英字として覚えない)。
        else _firstSoundCommitted = true;
        var text = first.Text;
        if (_options.SpaceAroundEnglish()) text = AddSpacesAroundEnglish(text, _precedingText, null);
        _correctable.Clear();
        // 前の確定の、確定後の F10 / F6 の記録も消す (入力欄の末尾が変わった。部分確定 CommitPart と同じ。R46-2)
        _lastWord = null;
        _lastCommitEnglish = first.English;
        var joined = (_lastCommitText ?? "") + text;
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        _lastCommitTime = Environment.TickCount64;
        // 残りの入力の文脈 (変換・英語の判定) は、確定した 1 音の後ろとして続ける。
        _precedingText = (_precedingText ?? "") + text;
        _text.PrecedingEnglish = first.English;
        _text.PrecedingTail = _precedingText;
        _host.CommitText(text);
        Committed?.Invoke(text);
    }

    /// <summary>
    /// 英単語で終わる文字列の最後の語が、よくある打ち間違い (teh、recieve) なら正しい綴りにする (Space で確定するとき)。
    /// </summary>
    private string FixEnglishTypo(string text)
    {
        // 注意: 今の Mac 版のスペルチェッカーには打ち間違いの自動修正が無いので、守った区間 (保護区間 /tmp/teh) は直らない。
        // 自動修正を足すと /tmp/teh も /tmp/the に直してしまうので、そのときは保護区間を除くこと。
        if (!_options.CorrectTypos()) return text;
        var start = text.Length;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        if (start == text.Length || _detector.EnglishAutoCorrection(text[start..]) is not { } right) return text;
        Diagnostics.Log.Decision($"英語の打ち間違いを直しました: {Diagnostics.Log.Text(text[start..])}→{Diagnostics.Log.Text(right)}");
        return text[..start] + right;
    }

    /// <summary>
    /// ユーザーが自分で英字 / かなに直して確定した語を覚える (api と打って F10 で英字にした → 次から api は英字)。
    /// 自動の判定と違う方を選んだときだけ。1 語の英字だけを対象にする。
    /// </summary>
    private void LearnLanguage()
    {
        if (_firstSoundCommitted) return;
        if (_options.Languages is not { } memory) return;
        var raw = _text.Raw;
        var automatic = _text.Segments(final: true);
        // 学習の単位は、判定と同じ区間の語 (sushi)。打った英字まるごと (kyouhasushidesu) では、判定で引かれず効かない。
        // 語として覚えられるか (2 文字以上の英字だけ) は Remember が確かめる (入力に「、」や数字が混ざっても、英語の区間 sushi は覚える。R46-4)
        void RememberWord(string word, bool english) => memory.Remember(word, english, explicitChoice: true);
        switch (_text.Mode)
        {
            case DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric when !automatic.All(s => s.IsEnglish):
                // 日本語の区間が 1 つだけ (英語の区間なし) のときだけ。混ざっていると、どの語を英語にしたいか決められない
                if (automatic.Count == 1) RememberWord(raw, english: true);
                break;
            case DisplayMode.Hiragana or DisplayMode.Katakana or DisplayMode.HalfWidthKatakana when automatic.Any(s => s.IsEnglish):
                foreach (var segment in automatic.Where(s => s.IsEnglish)) RememberWord(segment.Raw, english: false);
                break;
            case DisplayMode.Auto when _text.LevelOverride is not null && automatic.All(s => s.IsEnglish):
                // 判定の強さが手動で、Tab で提案どおり英字にした。
                foreach (var segment in automatic) RememberWord(segment.Raw, english: true);
                break;
        }
    }


    /// <summary>
    /// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
    /// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
    /// </summary>
    private sealed record CommitRecord(string Text, string Raw, bool English, bool SpaceIntended);

    /// <summary>
    /// 確定後の F10 / F6 (<see cref="TryToggleLastWord"/>) で直せる、直前に確定した 1 回分。表示した文字・打った英字・英語の区間があるか・日本語の区間があるか・確定した時刻。
    /// 単位は語ではなく「確定 1 回分」まるごと (変換中の F10 / F6 と同じ): 「きょうはsushiです」は英語も日本語も含むので、F6 でも F10 でも直せる (R43-1)。
    /// 自動の確定し直し用の <see cref="_correctable"/> とは別に持つ: そちらに全部の確定を入れると、曖昧でない語まで自動で書き換わる (R41-1)。
    /// </summary>
    /// <remarks>
    /// Raws = 英語だった区間・日本語だった区間それぞれの打った英字。
    /// 判定は区間の語 (sushi) で学習を引くので、学習も確定 1 回分まるごとの Raw (kyouhasushidesu) ではなく、この区間の語単位で行う。
    /// </remarks>
    private sealed record LastWord(string Text, string Raw, bool HasEnglish, bool HasJapanese, long Time, SegmentRawSet Raws);

    /// <summary>
    /// 確定の区間ごとの打った英字。JapaneseWord = 日本語の区間が 1 語と言えるか (F10 で英語として覚えてよいか)。
    /// 変換して確定したなら日本語の文節が 1 つ、変換しない確定なら日本語の区間が 1 つ。
    /// </summary>
    private sealed record SegmentRawSet(IReadOnlyList<string> English, IReadOnlyList<string> Japanese, bool JapaneseWord);

    private LastWord? _lastWord;

    /// <summary>
    /// 直前の 1 回分を直せる時間 (ミリ秒)。目で確かめて F 段へ手を伸ばすまでに 1〜2.5 秒かかるので、自分の確定を信じる時間 (<see cref="OwnCommitTrustMs"/> = 1.5 秒) とは分けて 3 秒にした (R43-2)。
    /// 主な安全装置は、末尾一致と、Swift が書き換え前に中身を確かめること。時間は、マウス押下が届かないアプリで、キャレットが動いたのに記録が残り続けないための保険。
    /// 実機で 3〜5 秒の間で決める (押したときの経過ミリ秒を診断ログに出す)。
    /// </summary>
    private const long LastWordTrustMs = 3000;

    /// <summary>
    /// F6 で英字からかなに直して開いた変換ボックスの元。Raw = 打った英字、Lead / Tail = 記録した文字の前後の空白 (直さずに残す)、
    /// Body = 入力欄から消した文字 (Esc で書き戻す)、Previous = 直す前の記録 (Esc で戻す)。
    /// Enter で確定するときに Raw を日本語として学習する (Esc では学習しない)。
    /// </summary>
    private sealed record ToggleBox(string Raw, string Lead, string Tail, string Body, LastWord Previous);

    private ToggleBox? _toggleBox;

    /// <summary>
    /// 直前のトグルを取り消す (<see cref="CancelToggle"/>) ための控え。Swift が入力欄の書き換えを確かめられず飛ばしたとき、
    /// 変換ボックス・記録・学習を直前に戻す (R41-3)。
    /// </summary>
    private sealed record ToggleUndo(LastWord Previous, List<CommitRecord> Correctable, string? OwnText, bool? OwnEnglish, long OwnTime, bool OpenedBox, LanguageMemory.RememberUndo? Learning);

    private ToggleUndo? _toggleUndo;
    private string? _toggleNotice;

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    private readonly List<CommitRecord> _correctable = [];
    private const int MaxCorrectable = 4;
    private bool _spaceStartedConversion;

    /// <summary>今の変換を Tab で始めたか (変換中の Tab で候補を送るため。issue #219)。</summary>
    private bool _tabStartedConversion;

    /// <summary>変換を始めてから、候補を動かす操作 (Space・↓・↑・PageUp/Down・← →) をしたか。TypeDigitsAfterConversion が ON のとき、動かす前の数字は入力になる。</summary>
    private bool _candidatesMoved;

    /// <summary>再変換で置き換えた元の文字列 (Esc で取り消すときに書き戻す)。再変換中でなければ null。</summary>
    private string? _reconvertOriginal;

    /// <summary>Chrome・Safari の入力欄は行末の空白を U+00A0 で持つことがあるので、比べる前に半角空白に揃える。</summary>
    private static string NoBreakToSpace(string text) => text.Replace('\u00A0', ' ');

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    public void ForgetLastCommit()
    {
        _correctable.Clear();
        _lastWord = null;
    }

    /// <summary>
    /// 確定した直後の語を、1 キーで英字 (F10) / かな (F6) に直す。変換ボックスが無く、記録があり、キャレットの前 (precedingText) の末尾が
    /// 記録と一致したときだけ直して true (R41-4)。そうでなければ何もせず false で、キーはアプリに渡す (読めないホストでは飲み込まない)。
    /// 記録は選択に頼らない: 選択を返さないホスト (ターミナル・Google ドキュメント) でも、確定し直しと同じ「消す前に中身を確かめる」形で直せる。
    /// 英字にする: 入力欄の語を打った英字に置き換えて確定し、英字として学習する (2 回ルールはそのまま。1 回目で効かなければ <see cref="TakeToggleNotice"/>)。
    /// かなにする: 入力欄の語を消し、打った英字を読みにした変換ボックスを開く。Enter で確定したときに日本語として学習し、Esc なら元の語を書き戻す。
    /// 記録の前後の空白 (確定の Space・英単語の前後の空白の設定) は直さずに残す。
    /// </summary>
    public bool TryToggleLastWord(bool toEnglish, string? precedingText)
    {
        _toggleUndo = null;
        _toggleNotice = null;
        if (IsComposing || _lastWord is not { } word) return false;
        var elapsed = Environment.TickCount64 - word.Time;
        // 有効時間を実機で決めるための記録 (3〜5 秒の間で決める)
        Diagnostics.Log.Info($"確定後の {(toEnglish ? "F10" : "F6")}: 確定から {elapsed} ミリ秒");
        if (elapsed >= LastWordTrustMs)
        {
            _lastWord = null;
            return false;
        }
        // 確定 1 回分まるごとが対象: F10 は日本語の区間があるとき、F6 は英語の区間があるときだけ効かせる
        if (!(toEnglish ? word.HasJapanese : word.HasEnglish) || precedingText is null) return false;
        var preceding = NoBreakToSpace(precedingText);
        var recorded = NoBreakToSpace(word.Text);
        if (!preceding.EndsWith(recorded, StringComparison.Ordinal))
        {
            Diagnostics.Log.Decision("キャレットの前の文字が記録と違うので、確定後の F10 / F6 は効かせません");
            _lastWord = null;
            return false;
        }
        var lead = recorded.Length - recorded.TrimStart(' ').Length;
        var tailLength = recorded.Length - recorded.TrimEnd(' ').Length;
        if (lead + tailLength >= recorded.Length) return false;
        var body = word.Text[lead..];
        var tail = word.Text[^tailLength..];
        var beforeBody = preceding[..^body.Length];
        var previousCorrectable = _correctable.ToList();
        var ownText = _lastCommitText;
        var ownEnglish = _lastCommitEnglish;
        var ownTime = _lastCommitTime;

        if (toEnglish)
        {
            var replacement = word.Raw + tail;
            if (replacement == body) return false;
            // 学習するのは、確定が日本語の区間 1 つだけのとき。英語と日本語が混ざった確定 (きょうはsushiです) は、どの語を覚えるか決められない (文全体を英字にするのは稀) ので学習しない
            // 変換して確定したなら日本語の文節が 1 つのときだけ (今日は|寒い のような文は覚えない)。変換しない確定は日本語の区間が 1 つなら覚える
            var learnable = word.Raws is { English.Count: 0, Japanese.Count: 1, JapaneseWord: true };
            var learning = learnable ? _options.Languages?.RememberUndoable(word.Raws.Japanese[0], english: true, explicitChoice: true) : null;
            // 2 回ルール: よく使う日本語 (sushi・kyouha) は、はっきり英字にしても 1 回目は効かない。そのことを 1 回だけ知らせる (学習したときだけ)
            if (learning is not null && _options.Languages!.Get(word.Raws.Japanese[0].ToLowerInvariant()) != true) _toggleNotice = "もう一度英字にすると覚えます";
            Diagnostics.Log.Decision($"確定した直後の語を英字にしました: {Diagnostics.Log.Text(body)}→{Diagnostics.Log.Text(replacement)}");
            _host.DeleteBackward(body.Length, body);
            _host.CommitText(replacement);
            // 手で直した語を、自動の確定し直しが元に戻さない (R41-2)
            _correctable.Clear();
            // 次の F6 で日本語として覚え直すのは、元の英語の区間の語と、今 F10 で英語として覚えた語
            _lastWord = new LastWord(replacement, word.Raw, HasEnglish: true, HasJapanese: false, Environment.TickCount64,
                new SegmentRawSet(learnable ? [.. word.Raws.English, word.Raws.Japanese[0]] : word.Raws.English, [], JapaneseWord: false));
            SetOwnContext(beforeBody + replacement, english: true);
            Committed?.Invoke(replacement);
            _toggleUndo = new ToggleUndo(word, previousCorrectable, ownText, ownEnglish, ownTime, OpenedBox: false, learning);
            return true;
        }

        var kana = _detector.Romaji.ConvertLenient(word.Raw.ToLowerInvariant(), final: true);
        // かなに読めない語 (数字・記号・綴りがローマ字として成り立たない語) は直せないので、キーをそのまま通す
        if (kana.Length == 0 || kana.Any(char.IsAsciiLetter) || !kana.Any(c => c is >= 'ぁ' and <= 'ゖ')) return false;
        BeginComposition();
        // 変換の文脈は、消す語の前。BeginComposition は今の入力欄の文字 (消す語を含む) と、自分が最後に確定した語 (英語) を文脈にするので、直す
        _precedingText = beforeBody;
        _text.PrecedingEnglish = LanguageOf(beforeBody);
        _text.PrecedingEnglishSentence = IsEnglishSentence(beforeBody);
        _text.PrecedingTail = beforeBody;
        _reconvertOriginal = body;
        _text.SetKana(kana);
        StartConversion();
        if (!_converting)
        {
            _text.Clear();
            _reconvertOriginal = null;
            return false;
        }
        Diagnostics.Log.Decision($"確定した直後の語をかなにして変換を始めました: {Diagnostics.Log.Text(body)}→{Diagnostics.Log.Text(kana)}");
        _host.DeleteBackward(body.Length, body);
        _toggleBox = new ToggleBox(word.Raw, word.Text[..lead], tail, body, word);
        _lastWord = null;
        _correctable.Clear();
        SetOwnContext(beforeBody, LanguageOf(beforeBody));
        _toggleUndo = new ToggleUndo(word, previousCorrectable, ownText, ownEnglish, ownTime, OpenedBox: true, Learning: null);
        _gate.Capture();
        return true;
    }

    /// <summary>
    /// 直前の確定後の F10 / F6 を、なかったことにする。Swift が入力欄の書き換えを確かめられず飛ばしたときに呼ぶ (変換ボックスも出さない)。
    /// 変換ボックス・記録・学習・自分の確定の記録を、トグルの前に戻す (R41-3)。直前にトグルしていなければ何もしない。
    /// </summary>
    public void CancelToggle()
    {
        if (_toggleUndo is not { } undo) return;
        _toggleUndo = null;
        _toggleNotice = null;
        if (undo.OpenedBox)
        {
            _text.Clear();
            _converting = false;
            _clauses = [];
            _selectedClause = 0;
            _headCount = 0;
            _fixedUnits = [];
            _reconvertOriginal = null;
            _toggleBox = null;
            ResetPrediction();
        }
        if (undo.Learning is not null) _options.Languages?.Undo(undo.Learning);
        _correctable.Clear();
        _correctable.AddRange(undo.Correctable);
        _lastCommitText = undo.OwnText;
        _lastCommitEnglish = undo.OwnEnglish;
        _lastCommitTime = undo.OwnTime;
        // 確かめられないアプリでもう一度押しても、また飛ばして取り消すだけなので、記録は捨てる (R43-7)
        _lastWord = null;
        Diagnostics.Log.Decision("入力欄の書き換えを確かめられなかったので、確定後の F10 / F6 を取り消しました");
    }

    /// <summary>直前のトグルを取り消すための控えを捨てる (次のキーを処理するとき。取消は書き換えの直後にだけ呼ぶ)。</summary>
    internal void DiscardToggleUndo() => _toggleUndo = null;

    /// <summary>トグルで 1 回目の学習が効かなかったときの案内 (1 回だけ。無ければ null)。</summary>
    public string? TakeToggleNotice()
    {
        var notice = _toggleNotice;
        _toggleNotice = null;
        return notice;
    }

    /// <summary>自分が最後に確定した文字の記録 (次の入力の文脈。アプリの読みが遅れても使う) を、書き換えた入力欄に合わせる。</summary>
    private void SetOwnContext(string? preceding, bool? english)
    {
        _lastCommitText = preceding is null ? null : preceding.Length > 20 ? preceding[^20..] : preceding;
        _lastCommitEnglish = english;
        _lastCommitTime = Environment.TickCount64;
    }

    /// <summary>F6 で開いた変換ボックスで F10: 打った英字どおりに戻す (往復)。学習はしない (F6 は Enter のときに学習するので、戻す学習が無い)。</summary>
    private void RevertToggleBox()
    {
        var box = _toggleBox!;
        _toggleBox = null;
        _text.Clear();
        _converting = false;
        _clauses = [];
        _selectedClause = 0;
        _reconvertOriginal = null;
        ResetPrediction();
        var restored = box.Raw + box.Tail;
        _host.CommitText(restored);
        _lastWord = new LastWord(restored, box.Raw, HasEnglish: true, HasJapanese: false, Environment.TickCount64, new SegmentRawSet(box.Previous.Raws.English, [], JapaneseWord: false));
        SetOwnContext((_precedingText ?? "") + restored, english: true);
        Committed?.Invoke(restored);
    }

    /// <summary>F6 で開いた変換ボックスを Esc で取り消した: 消した語を書き戻し、記録も元に戻す。</summary>
    private void RestoreToggleBox(ToggleBox box)
    {
        _toggleBox = null;
        _lastWord = box.Previous with { Time = Environment.TickCount64 };
        SetOwnContext((_precedingText ?? "") + box.Body, box.Previous.HasEnglish && !box.Previous.HasJapanese);
    }

    /// <summary>F6 で開いた変換ボックスを、変換中のまま Esc 1 回で取り消す: 元の語を書き戻して閉じる (ローマ字の再変換と同じ。R43-8)。</summary>
    private void CancelToggleBoxToOriginal(ToggleBox box)
    {
        _text.Clear();
        _converting = false;
        _clauses = [];
        _selectedClause = 0;
        ResetPrediction();
        if (_reconvertOriginal is { } original)
        {
            _reconvertOriginal = null;
            _host.CommitText(original);
        }
        RestoreToggleBox(box);
    }

    /// <summary>
    /// 変換中でない Enter・Tab をアプリに渡した。アプリの入力欄には改行・タブが入るので、自分の確定の記録 (直後の 1.5 秒は入力欄より優先して使う) にも入れる。
    /// 入れないと、2 行目の文頭の語が直前の行の最後の文字につながって見え (Hi therePlease)、英文と読めない (実機のトレースで見つかった)。
    /// Space (Shift+Space も。選択が無ければアプリに空白が入る) も同じ: 日本語を確定してから Space で空白を入れ、/tmp/foo を打つとき、空白の後を語の頭と読めず、パスを守れない (査読 R57 必須 A)。
    /// </summary>
    public void NoteBreakKeyPassedToApp(int vk, bool shift = false)
    {
        if (_lastCommitText is null) return;
        // BackSpace で、記録の最後の改行・タブ・空白が消える (I want ⏎⌫to go)
        if (vk == VirtualKeys.Back)
        {
            if (_lastCommitText.EndsWith('\n') || _lastCommitText.EndsWith('\t') || _lastCommitText.EndsWith(' ')) _lastCommitText = _lastCommitText[..^1];
            return;
        }
        var joined = _lastCommitText + (vk == VirtualKeys.Return ? "\n" : vk == VirtualKeys.Tab && !shift ? "\t" : vk == VirtualKeys.Space ? " " : "");
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
    }

    /// <summary>ローマ字としても読める英語の代名詞 (you → よう、are → あれ、we → うぇ)。</summary>
    private static readonly HashSet<string> EnglishPronouns = new(StringComparer.OrdinalIgnoreCase) { "you", "are", "we", "me", "she" };

    /// <summary>
    /// 代名詞の直前に来る英語の機能語 (Are you・Thank you・Let me)。名詞 (iPhone you = iPhone 用・PC you = PC 用) は含めない。
    /// </summary>
    private static readonly HashSet<string> WordsBeforePronoun = new(StringComparer.OrdinalIgnoreCase)
    {
        "are", "is", "am", "was", "were", "do", "does", "did", "can", "could", "will", "would", "should", "shall", "have", "has", "had",
        "how", "what", "where", "who", "why", "when", "if", "that", "and", "but", "so", "because", "to", "for", "with", "about", "from", "like",
        "thank", "love", "miss", "see", "tell", "give", "show", "let", "call", "help", "hello", "hi", "hey", "bye", "hope", "think", "know", "want", "need",
    };

    /// <summary>
    /// go・so・do・to など、ローマ字としても読める英単語 (代名詞以外) の直前に来る英語の機能語。
    /// 上の一覧に、代名詞・助動詞・否定・頻度や動作を表す語などを足したもの (I go・we go・not go・please go・come here・also see)。
    /// ' を含む縮約形 (I'll go・can't see・let's go) は、一覧に無くても機能語として扱う。
    /// 名詞 (New York no tenki の York、dark mode no settei の mode、time・back・way …) は含めない: 名詞のあとの no・to・he は助詞のつもりのことが多い。
    /// </summary>
    private static readonly HashSet<string> WordsBeforeAmbiguousWord = new(WordsBeforePronoun, StringComparer.OrdinalIgnoreCase)
    {
        "i", "you", "we", "they", "he", "she", "it", "me", "us", "them", "be", "may", "might", "must", "not", "never", "please", "going", "gonna", "wanna",
        "also", "always", "just", "really", "only", "even", "still", "ever", "oh", "been", "go", "come", "look", "stay", "get", "got", "went", "wait", "click", "live", "feel", "thanks", "ready", "able", "used",
    };

    /// <summary>助詞 (と・の・へ) と同じ綴りの英単語。代名詞のあとに来たら助詞のつもりのことが多い (I love you no koto = I love you のこと)。</summary>
    private static readonly HashSet<string> ParticleLikeWords = new(StringComparer.OrdinalIgnoreCase) { "to", "no", "he" };

    /// <summary>
    /// to・no・he (助詞と同じ綴り) の直前に来たとき、英語の続きと見なす語。go・so・do などと違い、to・no・he は日本語の助詞のつもりで打つことが多いので、
    /// 題名や名詞の末尾になりうる語 (Let It Go no uta、YouTube Live no haishin、SQL where no baai、Python and Go no hikaku) は含めない。
    /// 助動詞・否定・頻度を表す語など、文の途中にしか来ない語だけにする (査読 R10)。
    /// </summary>
    private static readonly HashSet<string> WordsBeforeParticleLikeWord = new(StringComparer.OrdinalIgnoreCase)
    {
        "not", "never", "please", "going", "gonna", "wanna", "also", "always", "just", "really", "only", "even", "still", "ever", "been", "may", "might", "must", "got", "went", "ready", "able", "used",
    };

    /// <summary>縮約形か (I'll・can't・we're・I've・I'd・let's)。L'Arc・Moody's のような ' を含む名前は含めない (査読 R10)。</summary>
    private static bool IsContraction(string word)
    {
        var lower = word.ToLowerInvariant();
        return lower.EndsWith("n't", StringComparison.Ordinal) || lower.EndsWith("'ll", StringComparison.Ordinal) || lower.EndsWith("'re", StringComparison.Ordinal)
            || lower.EndsWith("'ve", StringComparison.Ordinal) || lower.EndsWith("'m", StringComparison.Ordinal) || lower.EndsWith("'d", StringComparison.Ordinal)
            || lower is "let's" or "it's" or "that's" or "what's" or "there's" or "here's" or "he's" or "she's" or "who's";
    }


    /// <summary>
    /// 直前に英語で確定した語が、英語の機能語 (Are・Thank・I) のすぐ後ろの、英語の文の続きか。
    /// そうなら、後ろに日本語が続いても (Are you すし？、I want to go すし) 日本語に確定し直さない。
    /// - you / are / we / me / she: 直前が <see cref="WordsBeforePronoun"/> のとき (本家 #265。「iPhone 用」の you は直す)
    /// - go・so・do・to など、それ以外の語: 直前が <see cref="WordsBeforeAmbiguousWord"/> のとき (本家 #317)。
    ///   日本語由来の語 (sushi・ramen: I love sushi + がすき) と 1 文字の i は、今までどおり確定し直す
    /// 英語の語の後ろ、は入力欄のキャレットの前 (自分の確定の記録・入力欄から読んだ文字) で見る。分からなければ false (従来どおり)。
    /// </summary>
    private bool StaysEnglishAfterFunctionWord(CommitRecord previous)
    {
        if (_precedingText is null) return false;
        var preceding = NoBreakToSpace(_precedingText);
        var original = NoBreakToSpace(previous.Text);
        if (!preceding.EndsWith(original, StringComparison.Ordinal)) return false;
        // Mac は ' を ’ (U+2019) で入れることがある。文字数は変わらないので、そろえて読む
        var before = preceding[..^original.Length].TrimEnd(' ').Replace('\u2019', '\'');
        var start = before.Length;
        while (start > 0 && (char.IsAsciiLetter(before[start - 1]) || before[start - 1] == '\'')) start--;
        // 直前が記号 (Hello, you) や英字でない文字のときは、従来どおり (語を取り出せない)
        // 語の前は、文頭・空白・行頭 (改行・タブの直後) のいずれか。2 行目以降の文は行頭から始まる (実機のトレースで見つかった)。
        // 日本語・記号・数字に直接つながるとき (これはwe) は読まない。
        if (start >= before.Length || (start > 0 && before[start - 1] is not (' ' or '\n' or '\r' or '\t'))) return false;
        var word = before[start..];
        if (EnglishPronouns.Contains(previous.Raw)) return WordsBeforePronoun.Contains(word);
        if (previous.Raw.Length < 2 || CompositionDetector.IsJapaneseOriginWord(previous.Raw.ToLowerInvariant())) return false;
        // to・no・he は、題名や名詞の末尾になりうる語の後ろ (Let It Go no uta) では助詞のつもりのことが多いので、狭い一覧にある語のときだけ残す
        if (ParticleLikeWords.Contains(previous.Raw)) return WordsBeforeParticleLikeWord.Contains(word) || IsContraction(word);
        return IsContraction(word) || WordsBeforeAmbiguousWord.Contains(word);
    }

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private void CorrectPreviousCommit(string raw, bool english)
    {
        if (_correctable.Count == 0 || !_options.AutoCorrect()) return;
        var previous = _correctable[^1];
        List<CommitRecord> targets = [];
        string? replacement = null;
        if (!previous.English && english && _detector.IsDefinitelyEnglish(raw))
        {
            // 日本語で確定した語を英語に: 代名詞の i は I にし、Space で変換していたなら空白も入れる。
            // その前にも Space で区切って日本語にした語が続いていれば、まとめて英文に直す (make sure you have)。
            var start = _correctable.Count - 1;
            while (start > 0 && !_correctable[start - 1].English && _correctable[start - 1].SpaceIntended) start--;
            targets = _correctable.Skip(start).ToList();
            // 助詞と同じ形の短い語 (to, no) 1 語だけを、大文字で始まる語 (固有名詞) で直すことはしない (Google と Apple は日本語でもよく書く)。
            // 小文字の英単語で英文と分かったとき (let me know、do it) は直す。
            if (targets.Count == 1 && targets[0].Raw.Length <= 2 && targets[0].Raw != "i" && char.IsAsciiLetterUpper(raw.FirstOrDefault(char.IsAsciiLetter))) return;
            replacement = string.Concat(targets.Select(t => (t.Raw == "i" ? "I" : t.Raw) + (t.SpaceIntended ? " " : "")));
        }
        else if (previous.English && !english && !_detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語の機能語のすぐ後ろで確定した語 (Are you・I want to go) は、後ろに日本語が続いても英語のまま (Are you すし？)。
            if (StaysEnglishAfterFunctionWord(previous)) return;
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = _detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true);
        }
        var original = string.Concat(targets.Select(t => t.Text));
        if (replacement is null || replacement == original) return;

        // キャレットの前の文字が記録と違う (クリックで別の場所へ移った、など) なら、別の場所の文字を消してしまうので直さない。
        // 20 文字を超える分は前の文字列と比べられないので、その場合も直さない (安全側)。
        if (_precedingText is not null && (original.Length > 20 || !NoBreakToSpace(_precedingText).EndsWith(NoBreakToSpace(original), StringComparison.Ordinal)))
        {
            Diagnostics.Log.Decision("キャレットの前の文字が記録と違うので確定し直しません");
            _correctable.Clear();
            return;
        }

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement)}");
        _host.DeleteBackward(original.Length, original);
        _host.CommitText(replacement);
        _lastCommitText = replacement;
        _correctable.Clear();
    }

    /// <summary>選び直した文節を学習する (次に同じ読みを変換したとき最初の候補にする)。</summary>
    private void Learn()
    {
        if (_firstSoundCommitted) return;
        // 変換の候補から打ったままの英字 (api) を選んで確定したら、その語は次から英字にする (F10 と同じ)。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Raw is { } raw && c.Text == raw))
        {
            _options.Languages?.Remember(clause.Raw!, english: true);
        }
        // 英語と判定した語で日本語の候補 (go → 語) を選んだら、その語は次から日本語にする (F6 と同じ)。
        foreach (var clause in _clauses.Where(c => c.IsEnglish && IsJapaneseText(c.Text) && c.Reading.All(char.IsAsciiLetter)))
        {
            _options.Languages?.Remember(clause.Reading, english: false);
        }
        // 英訳を選んだら、英訳の記録に (普通の変換の学習より弱く効く)。
        foreach (var clause in _clauses.Where(c => c.Translations.Contains(c.Text)))
        {
            _options.TranslationHistory?.Remember(clause.Reading, clause.Text);
        }
        LearnConversion();
        RecordSuggestions();
        if (_options.History is not { } history) return;
        // 学習済みの語をそのまま確定したときも、回数と新しさを更新する (予測を使用頻度で並べるため。並びは変わらない)。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && !c.Changed && c.Reading.Length >= 2))
        {
            history.Touch(clause.Reading, clause.Text);
        }
        // 1 文字の読み (き → 記) を覚えると、関係ない変換 (き + ごうとう) まで巻き込むので 2 文字以上だけ。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Reading.Length >= 2))
        {
            // かな・カタカナのまま確定したのは、その場限りのことが多いので覚えない。
            // 打ったままの英字を選んだのは、上で英語として覚えた。
            if (clause.Text == clause.Reading || clause.Text == CompositionText.ToKatakana(clause.Reading) || clause.Text == CompositionText.ToHalfWidthKatakana(clause.Reading) || clause.Text == clause.Raw) continue;
            // 英訳は上で英訳の記録に入れた (ここで覚えると次から 1 番目に出てしまう)。
            if (clause.Translations.Contains(clause.Text)) continue;
            // 計算の記号 (かける → ×) も覚えない
            if (IsMathSymbolChoice(clause)) continue;
            history.Remember(clause.Reading, clause.Text);
        }
    }

    /// <summary>
    /// 選び直して確定した「読み → 語」を、ユーザー辞書への登録提案として数える。
    /// 数えるのは、候補を選び直した文節と、前に選び直して学習した語をそのまま確定した文節だけ
    /// (エンジンや文脈辞書が最初から出した語は、辞書に無くても困っていないので数えない)。
    /// 読みがかな以外の文節・英語の文節・ひらがなだけの語・登録済みの語などは Suggestions 側で除く。
    /// </summary>
    private void RecordSuggestions()
    {
        if (_options.Suggestions is not { } suggestions || !_options.DictionarySuggest()) return;
        var threshold = _options.DictionarySuggestThreshold();
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Text != c.Raw))
        {
            // 学習済みの語を最初の候補としてそのまま確定した場合 (History はこの後の Remember で更新されるので、ここで先に見る)。
            if (clause.Changed || _options.History?.Get(clause.Reading) == clause.Text)
            {
                suggestions.Record(clause.Reading, clause.Text, threshold, _options.UserDictionary);
            }
        }
    }

    /// <summary>
    /// 変換エンジン (Mozc) にも、確定した文節を覚えさせる。英語の文節・打ったままの英字で区切った、日本語の文節のまとまりごとに。
    /// 変換エンジンとのやり取りで入力を待たせないよう、裏で行う。
    /// </summary>
    private void LearnConversion()
    {
        if (_converter is not ILearningConverter learner) return;
        // 学習する文節の前の文脈。固定した文節があっても、その前 (キャレットの前) の文字列にする。
        var context = ContextOf(_precedingText);
        var run = new List<ConversionClause>();
        void Flush()
        {
            if (run.Count == 0) return;
            var clauses = run.ToList();
            run.Clear();
            // 変換エンジンが学習すると同じかなの変換結果が変わりうるので、変換結果のキャッシュを捨てる
            // (Swift 側の azooKey の結果キャッシュも学習のたびに捨てている。合わせないと、学習した語が次の変換に出ない)。
            // 学習は裏で後から終わるので、今捨てるのに加え、終わったときにも世代を進めて、その間に入った学習前の結果も捨てる
            _conversionCache.Clear();
            _liveCache.Clear();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // 裏のスレッドの例外はプロセスごと落とすので、学習の失敗はログに残すだけにする
                try { learner.Learn(context, clauses); }
                catch (Exception ex) { Diagnostics.Log.Warn($"変換エンジンの学習に失敗しました: {ex.Message}"); }
                finally { Interlocked.Increment(ref _learnGeneration); }
            });
        }
        foreach (var clause in _clauses)
        {
            if (clause.IsEnglish || clause.Text == clause.Raw || clause.Translations.Contains(clause.Text) || clause.Reading.Any(char.IsAsciiLetterOrDigit) || IsMathSymbolChoice(clause))
            {
                Flush();
                continue;
            }
            run.Add(new ConversionClause(clause.Reading, clause.Text));
        }
        Flush();
    }

    /// <summary>確定して入力する。英語だったか日本語だったか・確定した文字列を、次の入力の文脈として覚えておく。</summary>
    private void CommitText(string text, bool english, string raw = "", bool chosen = false, bool hasEnglish = false, bool hasJapanese = false, SegmentRawSet? raws = null)
    {
        var spaceIntended = _spaceStartedConversion;
        _spaceStartedConversion = false;
        _tabStartedConversion = false;
        // 固定した文節を含む確定は、語の単位の判定 (英語とも日本語とも読める語の確定し直し) の対象にしない。
        var hadFixed = _headCount > 0;
        // 確定後の F6 で開いた変換ボックスの確定は、手で直した語。自動の確定し直しの対象にも、確定し直しの元にもしない (R41-2)。
        var fromToggle = _toggleBox is not null;
        // 1 音確定 (Shift+↓) のあとの残りの確定は語の途中から始まる (kanpai の npai)。確定後の F10 / F6 の対象にしない (npai を英語として覚えない)
        var afterFirstSound = _firstSoundCommitted;
        // 再変換 (選択 + Shift+Space) で入れ替えた文字は、打った英字ではないので、確定後の F10 / F6 の対象にしない。
        var reconverted = (_reconvertOriginal is not null && !fromToggle) || _reconvertRomaji;
        _toggleBox = null;
        // 誤変換の報告を調べられるように、打った英字・読み・文節の区切りもログに残す (ログはファイルに書く設定のときだけ保存される)。
        if (!_text.IsEmpty)
        {
            var clauses = _clauses.Count > 0 ? "　文節 " + string.Join(" | ", _clauses.Select(c => $"{c.Reading}→{c.Text}")) : "";
            if (Diagnostics.Log.RecordText) Diagnostics.Log.Info($"確定の内訳: 打った英字「{_text.Raw}」　読み「{_text.AllKana(final: true)}」{clauses}");
        }
        // コード系アプリで一覧の判定により英字にした確定 (open → open) は、確定し直しの対象にしない (次の「資料」で おぺん に書き戻されない)
        var commandName = _text.IsCommandName;
        _text.Clear();
        _reconvertOriginal = null;
        _reconvertRomaji = false;
        _firstSoundCommitted = false;
        _converting = false;
        _clauses = [];
        _headCount = 0;
        _fixedUnits = [];
        ResetPrediction();
        if (text.Length == 0) return;
        // 保護区間を含む確定も、設定どおり空白を入れる (./build.sh を。本家は末尾が保護区間のときは入れないが、Mac 版は設定どおり。パスそのものは崩れない)
        if (_options.SpaceAroundEnglish()) text = AddSpacesAroundEnglish(text, _precedingText, _followingText);
        if (!hadFixed && !fromToggle) CorrectPreviousCommit(raw, english);
        // 英語とも日本語とも読める語を、文脈を決めずに (選び直さずに) 確定したときだけ、後で確定し直せるようにしておく。
        if (!hadFixed && !fromToggle && !chosen && !afterFirstSound && !commandName && _detector.IsAmbiguousWord(raw))
        {
            // 前の語の後に Space で区切って続けたときだけつなげる (それ以外は新しい並び)。
            if (_correctable.Count > 0 && !_correctable[^1].SpaceIntended) _correctable.Clear();
            _correctable.Add(new CommitRecord(text, raw, english, spaceIntended));
            if (_correctable.Count > MaxCorrectable) _correctable.RemoveAt(0);
        }
        else _correctable.Clear();
        _lastCommitEnglish = english;
        var joined = (_lastCommitText ?? "") + text;
        _lastCommitText = joined.Length > 20 ? joined[^20..] : joined;
        _lastCommitTime = Environment.TickCount64;
        // 確定後の F10 / F6 で直せる直前の 1 語 (打った英字が分かる確定だけ。固定した文節を含む・再変換の確定は記録しない)。
        // 区間ごとの打った英字が渡されない確定 (全部が英語・全部が日本語) は、確定 1 回分の打った英字が 1 区間
        raws ??= new SegmentRawSet(hasEnglish && !hasJapanese ? [raw] : [], hasJapanese && !hasEnglish ? [raw] : [], JapaneseWord: hasJapanese && !hasEnglish);
        _lastWord = !hadFixed && !reconverted && !afterFirstSound && raw.Any(char.IsAsciiLetter) && (hasEnglish || hasJapanese) ? new LastWord(text, raw, hasEnglish, hasJapanese, _lastCommitTime, raws) : null;
        _host.CommitText(text);
        Committed?.Invoke(text);
    }

    /// <summary>
    /// 日本語 (かな・漢字) と英単語の間に半角スペースを入れる (今日はGitHubにpush → 今日は GitHub に push)。
    /// 英字を 1 つ以上含む英数字の並び (iPhone15、C++) を英単語とみなす。数字だけ (3時) と記号 (、。「」) の隣には入れない。
    /// 確定する文字列の端は、入力欄のキャレットの前後の文字 (before・after) との間も見る。
    /// </summary>
    internal static string AddSpacesAroundEnglish(string text, string? before, string? after)
    {
        static bool IsJapanese(char c) => c is (>= '\u3040' and <= '\u30FF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u4E00' and <= '\u9FFF') or (>= '\uF900' and <= '\uFAFF');
        static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '#' or '.' or '\'' or '@';
        var previous = string.IsNullOrEmpty(before) ? '\0' : before[^1];
        var next = string.IsNullOrEmpty(after) ? '\0' : after[0];
        var builder = new StringBuilder(text.Length + 8);
        // 前に確定した英単語のすぐ後ろに日本語を続けるとき (GitHub|に)
        if (IsJapanese(text[0]) && char.IsAsciiLetter(previous)) builder.Append(' ');
        var i = 0;
        while (i < text.Length)
        {
            if (!IsWordChar(text[i]))
            {
                builder.Append(text[i++]);
                continue;
            }
            var end = i;
            while (end < text.Length && IsWordChar(text[end])) end++;
            // 語の端の記号 (. ' -) は語に含めない (Hello. の . の後ろに日本語が続いても、. の前に入れない)
            var start = i;
            var stop = end;
            while (stop > start && text[stop - 1] is '.' or '\'' or '-' or '@' or '_') stop--;
            var word = text[start..stop];
            var left = start > 0 ? text[start - 1] : previous;
            var right = stop < text.Length ? text[stop] : next;
            var isWord = word.Any(char.IsAsciiLetter);
            if (isWord && IsJapanese(left) && (builder.Length == 0 || builder[^1] != ' ')) builder.Append(' ');
            builder.Append(word);
            if (isWord && IsJapanese(right)) builder.Append(' ');
            builder.Append(text, stop, end - stop);
            i = end;
        }
        // 確定した日本語のすぐ後ろが英単語のとき (キャレットの後ろの文字)
        if (IsJapanese(text[^1]) && char.IsAsciiLetter(next)) builder.Append(' ');
        return builder.ToString();
    }

    /// <summary>変換ボックスの最後が英語の区間か (Space を空白として扱うか)。</summary>
    private bool EndsWithEnglish(bool final = false) =>
        _text.Mode == DisplayMode.Auto && _text.Segments(final) is { Count: > 0 } segments && segments[^1].IsEnglish;

    private void ReplayDown(KeyEvent e)
    {
        // Meltype を通らないキーを送る = キャレットが動くかもしれないので、直前の語はもう確定し直さない。
        _correctable.Clear();
        _lastWord = null;
        // 握りつぶしていた Shift を先に送る (Shift+矢印 の範囲選択、Ctrl+Shift+Z など)。
        foreach (var shift in _swallowedShift)
        {
            _host.Replay(new KeyEvent(shift, 0, false, false, false, e.TimeMs));
            _replayedDown.Add(shift);
        }
        _swallowedShift.Clear();
        _host.Replay(e);
        _replayedDown.Add(e.Vk);
    }

    private void UpdateView()
    {
        if (_text.IsEmpty && _headCount == 0)
        {
            ClearConversionCaches();
            ResetPrediction();
            // 英数状態の判定中は何も表示しない (英語ならそのまま出るだけ)。
            _host.Hide();
            return;
        }
        if (_converting && _clauses.Count > 0)
        {
            var selected = _clauses[_selectedClause];
            // 選んでいる文節は、最初から候補の一覧をすべて出す (Space を 1 回押しただけでは一部しか出ず、選び間違えやすかった)。
            if (!selected.IsEnglish && !selected.Expanded) Expand(selected);
            _host.Show(new CompositionView(
                string.Concat(_clauses.Select(c => c.Text)),
                selected.Candidates,
                selected.Index,
                true,
                _options.TokMode()
                    ? "←→ 区切り　Shift+←→ 文節　Space 候補　↓ 部分確定　Enter 確定　Esc 戻る"
                    : "←→ 文節　Space/↓ 候補　Shift+←→ 区切り　Enter 確定　Esc 戻る",
                _clauses.Select(c => c.Text).ToList(),
                _selectedClause,
                CandidateNotes(selected),
                _options.CandidateMeanings() ? CandidateMeaning(selected) : null,
                MisspellingSuggestion()));
        }
        else
        {
            var hint = _text.IsAlphanumeric ? "Enter 確定　Space 確定+空白　Shift+Space 日本語で変換　半角/全角 日本語に" : (_options.TabConversion() ? "Space/Tab 変換" : "Space 変換") + "　←→ 文節　Enter 確定　F7 カタカナ　F8 半角カナ　F10 英字";
            if (_options.TokMode() && _headCount == 0) hint += "　Shift+↓ 1音確定";
            if (_text.Suggestion() is { } suggestion) hint = $"Tab → {suggestion} (英字に)　" + hint;
            RefreshPredictions();
            var tail = CurrentDisplay(final: false);
            if (_headCount > 0)
            {
                // 固定した文節 (選んだ変換結果) + 未変換の文節。文節ごとの表示で、最後の 1 つが未変換 (選択中の文節は無し)。
                var fixedTexts = _clauses.Take(_headCount).Select(c => c.Text).ToList();
                hint = "Space 変換　←→ 文節　Enter 確定　Esc 変換前の読みに戻す";
                if (_text.Suggestion() is { } fixedSuggestion) hint = $"Tab → {fixedSuggestion} (英字に)　" + hint;
                _host.Show(new CompositionView(string.Concat(fixedTexts) + tail, [], -1, false, hint, [.. fixedTexts, tail], -1, Suggestion: MisspellingSuggestion()));
                return;
            }
            _host.Show(new CompositionView(tail, [], -1, false, hint, Suggestion: MisspellingSuggestion(),
                Predictions: _predictions, SelectedPrediction: _predicting ? _predictionIndex : -1));
        }
    }

    private void ResetPrediction()
    {
        _predicting = false;
        _predictionIndex = 0;
        _predictions = [];
    }

    /// <summary>変換前の表示のたびに、今の読みで予測候補を引き直す (英語と判定した語・表示モードが日本語でないときは出さない)。</summary>
    private void RefreshPredictions()
    {
        var reading = _text.AllKana(final: false);
        // 固定した文節があるときは、予測を出さない (予測を確定すると固定した文節が消えてしまうため)。
        if (_headCount == 0 && _options.Prediction() && _text.Mode is DisplayMode.Auto or DisplayMode.Hiragana && !_text.IsAlphanumeric && !EndsWithEnglish(final: false) &&
            reading.Length >= _options.PredictionMinLength())
        {
            _predictions = CollectPredictions(reading);
        }
        else _predictions = [];
        if (_predictions.Count == 0) _predicting = false;
        else _predictionIndex = Math.Min(_predictionIndex, _predictions.Count - 1);
    }

    /// <summary>
    /// 予測候補を、ユーザー辞書 → 専門用語集 → 変換エンジン → 変換履歴の順に集める (重複と、読みそのままのひらがなは除く。最大 8 個)。
    /// 出どころの順は崩さず、同じ出どころの中を使用頻度 (回数が多い・最近使った) の順にする。
    /// ユーザー辞書とエンジンの予測は、履歴に使用実績のある語を上に引き上げ (実績の無い語は元の順のまま)、
    /// 履歴由来の予測は頻度スコア順 (ConversionHistory.StartingWith) で並ぶ。
    /// </summary>
    private List<string> CollectPredictions(string reading)
    {
        var usage = _options.History?.Usage() ?? [];
        double Used(string word) => usage.TryGetValue(word, out var score) ? score : 0;
        // OrderByDescending は安定なので、実績の無い語 (スコア 0) は元の並びを保つ。
        IEnumerable<string> Sources()
        {
            if (_options.UserDictionary is { } dictionary)
            {
                foreach (var word in dictionary.Words.Where(w => w.Reading.StartsWith(reading, StringComparison.Ordinal)).Select(w => w.Word).OrderByDescending(Used)) yield return word;
            }
            if (_options.UserDictionary is { } termSource)
            {
                // 自作の専門用語集の語は、ユーザー辞書の語の次 (同梱の専門用語集の語の使用実績に負けない)
                foreach (var word in termSource.PredictUserTerms(reading).OrderByDescending(Used)) yield return word;
                foreach (var word in termSource.PredictTerms(reading).OrderByDescending(Used)) yield return word;
            }
            if (reading.Length <= MaxPredictionReadingLength && _options.Predictions?.Invoke(reading) is { } engine)
            {
                foreach (var word in engine.OrderByDescending(Used)) yield return word;
            }
            if (_options.History is { } history)
            {
                foreach (var (_, text) in history.StartingWith(reading)) yield return text;
            }
        }
        return Sources().Where(w => w.Length > 0 && w != reading).Distinct().Take(MaxPredictions).ToList();
    }

    private const int MaxPredictions = 8;

    /// <summary>
    /// 予測変換を出す読みの長さの上限 (文字数)。変換エンジンの予測は読みの長さに比例して時間がかかり、
    /// 長い読みの続きが予測で当たることはまず無いので、文章が長くなったら出さない (1 文字打つたびに重くなるのを避ける)。
    /// </summary>
    internal const int MaxPredictionReadingLength = 40;

    /// <summary>
    /// Tab で予測候補に入っている間のキー。処理したら true。↓ で次、↑ で前 (先頭から上は入力に戻る)、Tab で次・Shift+Tab で前 (一巡して入力に戻る)、Enter で確定、
    /// Esc で入力に戻る。それ以外のキーは予測から抜けて、入力として普通に処理する (Space は今までどおり変換)。
    /// </summary>
    private bool HandlePredictionKey(int vk)
    {
        switch (vk)
        {
            case VirtualKeys.Tab:
                // 本家と同じ: Tab で次、Shift+Tab で前。最後の次・先頭の前は選ばない状態に戻る (一巡)。
                var step = _swallowedShift.Count > 0 || _host.IsShiftDown() ? -1 : 1;
                var next = (_predictionIndex + 1 + step + _predictions.Count + 1) % (_predictions.Count + 1) - 1;
                if (next < 0) _predicting = false;
                else _predictionIndex = next;
                return true;
            case VirtualKeys.Down:
                _predictionIndex = Math.Min(_predictionIndex + 1, _predictions.Count - 1);
                return true;
            case VirtualKeys.Up:
                if (_predictionIndex == 0) _predicting = false;
                else _predictionIndex--;
                return true;
            case VirtualKeys.Return:
                CommitPrediction(_predictions[_predictionIndex]);
                return true;
            case VirtualKeys.Escape:
                _predicting = false;
                return true;
            default:
                _predicting = false;
                return false;
        }
    }

    /// <summary>予測候補を確定する。読み → 語を履歴に覚えて、次から予測の上位に出す。</summary>
    private void CommitPrediction(string text)
    {
        var reading = _text.AllKana(final: false);
        CommitText(text, english: false, _text.Raw, hasJapanese: true);
        _options.History?.Remember(reading, text);
    }

    private static bool IsShift(int vk) => vk is VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift;

    private static bool IsCommandModifier(int vk) => VirtualKeys.IsModifier(vk) && !IsShift(vk);
}
