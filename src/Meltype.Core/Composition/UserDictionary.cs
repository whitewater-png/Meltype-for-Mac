// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>ユーザー辞書の 1 語。</summary>
public sealed record UserWord(string Reading, string Word);

/// <summary>
/// ユーザー辞書 (%LOCALAPPDATA%\Meltype\userdict.txt、1 行に「読み[Tab]単語」)。
/// 変換で最優先に使う: 変換する読みの中に登録した読みが含まれていれば、その部分は変換エンジンの区切りに関係なく
/// 登録した単語にする (きごうとう → 記号等 を登録すると、きごうとうふくめ → 記号等|含め)。
/// トレイの「ユーザー辞書...」(Windows)・「Meltype 辞書」の画面 (Mac) から登録・編集・削除する。
///
/// ファイルが正: Mac では IME と「Meltype 辞書」の画面が別のプロセスで同じファイルを扱うので、
///   - 変更 (登録・編集・削除・取り込み) は、プロセスをまたぐロック (<see cref="FileLock"/>) の中でファイルを読み直してから行い、すぐ保存する
///     (相手の変更を古い内容で上書きして消さないため)。読めないファイル (権限・壊れた文字コード) には書かない。
///   - 読む側 (変換) は、ファイルの版 (<see cref="FileStamp"/>) を <see cref="FileCheckIntervalMs"/> ごとに確かめ、変わっていれば読み直して
///     <see cref="Version"/> を進める (変換結果のキャッシュを捨てる)。1 キーごとに増えるのは整数の比較だけ。
/// 検索用の索引は、読み直し・変更のたびに作り直した不変のもの (<see cref="Index"/>) を参照ごと入れ替える (読む側はロックしない)。
/// </summary>
public sealed class UserDictionary
{
    /// <summary>1 文字の読みは、ほかの語の中にも現れやすく巻き込みが大きいので登録させない。</summary>
    public const int MinReadingLength = 2;

    /// <summary>読み・語の長さの上限。選択範囲をそのまま登録できるので、段落全体のような巨大な語が入って変換のたびに照合されるのを防ぐ。</summary>
    public const int MaxLength = 100;

    /// <summary>同じ読み・同じ単語がすでにあるとき (管理画面の登録・編集で使う。入力メニューの登録は、黙って成功にする)。</summary>
    public const string DuplicateMessage = "同じ読みと単語が、すでに登録されています。";

    /// <summary>保存すると読み込みの上限を超えるとき (書かずに返す)。</summary>
    public static string TooLargeMessage => $"ユーザー辞書が大きくなりすぎます (上限 {Math.Max(1, SafeFile.MaxReadBytes / 1024 / 1024)} MB)。語を減らすか、分けて専門用語集に移してください。何も変えていません。";

    /// <summary>編集しようとした語が、ファイルに無くなっていたとき。</summary>
    public const string NotFoundMessage = "元の語が見つかりません (ほかの画面で変更・削除された可能性があります)。一覧を読み直してください。";

    /// <summary>読みと語として登録してよいか。だめなら理由を返す (Add / AddRange / 画面の入力チェックで共通)。</summary>
    public static string? Validate(string reading, string word)
    {
        if (reading.Length < MinReadingLength) return $"読みは {MinReadingLength} 文字以上にしてください。";
        if (word.Length == 0) return "単語を入力してください。";
        if (reading.Length > MaxLength || word.Length > MaxLength) return $"読みも単語も {MaxLength} 文字までにしてください。";
        // 変換はひらがなで引くので、ひらがなを 1 文字も含まない読み (ローマ字・カタカナだけ) は、登録しても変換に一度も出ない。
        if (!reading.Any(IsHiragana) || !reading.All(IsReadingChar)) return "読みはひらがなで入力してください (カタカナは自動でひらがなになります)。";
        // 改行はファイルの 1 行に収まらず、タブは「読み<Tab>単語」の区切りと衝突する。
        if (reading.Any(c => c is '\t' or '\r' or '\n') || word.Any(c => c is '\t' or '\r' or '\n')) return "改行・タブ文字は使えません。";
        // 制御文字 (ESC・Ctrl+O など) は、確定した先がターミナルだと「行の実行」や画面の書き換えになりうる。
        // 書式文字 (右から左に並べ替える U+202E など) は、見えている語と確定する語を食い違わせる。どちらも候補欄では見えない。
        // 判定は 1 文字 (コードポイント) ずつ。UTF-16 の 1 単位ずつだと、補助面の書式文字 (タグ文字 U+E0000 台など) がサロゲートとして通ってしまう。
        if (HasUnsafeText(reading) || HasUnsafeText(word)) return "制御文字・書式文字は使えません。";
        return null;
    }

    private static bool IsHiragana(char c) => c is >= 'ぁ' and <= 'ゖ' or 'ゔ';

    // 読みに使える文字: ひらがな・長音・中点・数字・英字 (50cc のように、数字や英字を含むかなの並びの読みがある)
    private static bool IsReadingChar(char c) => IsHiragana(c) || c is 'ー' or '・' or >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>登録・編集・確認の前に読みをそろえる: 前後の空白を取り、カタカナをひらがなにする (変換側はひらがなで引くため。取り込みと同じ考え方)。</summary>
    public static string NormalizeReading(string reading)
    {
        reading = reading.Trim();
        return reading.Any(c => c is >= 'ァ' and <= 'ヶ') ? new string(reading.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray()) : reading;
    }

    /// <summary>
    /// 語・名前に入れてはいけない文字を含むか: 制御文字 (Cc)・書式文字 (Cf)・行/段落区切り (Zl・Zp)・対になっていないサロゲート。
    /// 1 文字 (コードポイント) ずつ見る (補助面の書式文字、たとえばタグ文字 U+E0001〜E007F を見落とさないため)。
    /// 許すもの: 絵文字をつなぐ ZWJ (U+200D)、旗の絵文字 (U+1F3F4 の直後のタグ文字の並びで、U+E007F で終わるもの)。
    /// 異体字選択子 (U+FE00 台・U+E0100 台) は Mn なので対象外 (許す)。
    /// </summary>
    public static bool HasUnsafeText(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (System.Text.Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed) != System.Buffers.OperationStatus.Done) return true;
            i += consumed;
            if (rune.Value == 0x200D) continue;
            if (rune.Value == 0x1F3F4 && TagSequenceLength(text[i..]) is > 0 and var length)
            {
                i += length;
                continue;
            }
            if (System.Text.Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator
                or System.Globalization.UnicodeCategory.Surrogate) return true;
        }
        return false;
    }

    /// <summary>
    /// 許す旗の絵文字のタグ列 (Unicode の推奨する絵文字: イングランド・スコットランド・ウェールズ)。
    /// タグ文字は ASCII と 1 対 1 に対応するので、任意のタグ列を許すと「🏴 + 見えない任意の文字列」を確定させられる。実在する旗だけに絞る。
    /// </summary>
    private static readonly string[] AllowedFlagTags = ["gbeng", "gbsct", "gbwls"];

    /// <summary>許す旗のタグ列 (<see cref="AllowedFlagTags"/> + 終わりの U+E007F) の UTF-16 の長さ。そうでなければ 0。</summary>
    private static int TagSequenceLength(ReadOnlySpan<char> text)
    {
        foreach (var tag in AllowedFlagTags)
        {
            var i = 0;
            var matched = true;
            foreach (var letter in tag + "\u007f")
            {
                if (System.Text.Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed) != System.Buffers.OperationStatus.Done || rune.Value != 0xE0000 + letter)
                {
                    matched = false;
                    break;
                }
                i += consumed;
            }
            if (matched) return i;
        }
        return 0;
    }

    /// <summary>入れてはいけない文字 (<see cref="HasUnsafeText"/> と同じ決まり) を取り除く (注記など、断らずに整えるところで使う)。</summary>
    public static string RemoveUnsafeText(string text)
    {
        if (!HasUnsafeText(text)) return text;
        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (System.Text.Rune.DecodeFromUtf16(text.AsSpan(i), out _, out var consumed) != System.Buffers.OperationStatus.Done)
            {
                i += Math.Max(1, consumed);
                continue;
            }
            var piece = text.AsSpan(i, consumed);
            if (!HasUnsafeText(piece)) builder.Append(piece);
            i += consumed;
        }
        return builder.ToString();
    }

    /// <summary>ファイルが書き換わっていないかを確かめる間隔 (ミリ秒)。テストが 0 にする。</summary>
    internal static int FileCheckIntervalMs { get; set; } = 500;

    private readonly string? _path;
    // 同梱の語句 (dictionaries/phrases.txt)。変換エンジンが苦手な語句を補う。ユーザーの登録より後回しで、保存も表示もしない。
    private readonly UserWord[] _builtIn = [];
    // 同梱の語句を読みで引くもの (ユーザーの登録・自作の専門用語集の後ろ、同梱の専門用語集の前に使う)。builtIn: false なら空。
    private readonly Dictionary<string, List<string>> _builtInByReading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>>.AlternateLookup<ReadOnlySpan<char>> _builtInLookup;
    private readonly int _builtInMaxLength;

    /// <summary>
    /// 登録した語 (ファイルの順) と、そこから作った検索用の辞書。作ったあとは変えない (参照ごと入れ替える)。
    /// ByReading は読み → 単語 (後から登録したものが先。ユーザーの登録だけ。同梱の語句は <see cref="_builtInByReading"/>)。
    /// </summary>
    private sealed class Index
    {
        public required UserWord[] Words { get; init; }
        public required Dictionary<string, List<string>> ByReading { get; init; }
        public required Dictionary<string, List<string>>.AlternateLookup<ReadOnlySpan<char>> Lookup { get; init; }
        public required int MaxReadingLength { get; init; }
    }

    private Index _index;
    // 今の索引の元にしたファイルの版 (ファイルが無いときは Missing)。
    private FileStamp _stamp = FileStamp.Missing;
    // 次にファイルの版を確かめる時刻 (Environment.TickCount64)。
    private long _nextFileCheck;
    // ファイルを読めなかった・大きすぎて読まなかったときの理由 (読めていれば null)。管理画面が警告に出す。
    private string? _problem;

    // 専門用語集 (dictionaries/terms-*.txt)。ユーザー辞書の後・変換エンジンの前。保存も表示もしない。差し替えは参照ごと (読む側は止めない)。
    // 同梱の語は、利用者が有効にした分野だけ (TermDomains)。分野の切り替えは、プロセス内のすべてのインスタンス
    // (共有・非共有を問わない) に届けたいので、各インスタンスが TermDomains.Revision を見て、変わっていれば入れ替える (Terms)。
    // 辞書・取った時点の Revision・分野の設定に従うか を 1 つの不変オブジェクトにまとめ、参照ごと入れ替える (読む側は Volatile.Read 1 回で、
    // 辞書と Revision の食い違いを見ない)。従わないのは builtIn: false と、LoadTerms で語を直接渡した辞書。
    private sealed record TermSnapshot(TermDictionary Terms, int Revision, bool Follows);
    private TermSnapshot _terms = new(TermDictionary.Empty, -1, false);

    // パスごとの共有インスタンス。セッションごとに別インスタンスだと、登録が他セッションに見えるまで待つことになるので、
    // 同じファイルはプロセスで 1 つのインスタンスで持つ (別インスタンス・別プロセスでも、ファイルを正にしているので登録は消えない)。
    private static readonly Dictionary<string, UserDictionary> Shared_ = new(StringComparer.Ordinal);

    /// <summary>同じパスなら同じインスタンスを返す (プロセス内で共有)。path が null なら共有せず毎回新しく作る。</summary>
    public static UserDictionary Shared(string? path)
    {
        if (path is null) return new UserDictionary(null);
        lock (Shared_)
        {
            var key = Path.GetFullPath(path);
            if (!Shared_.TryGetValue(key, out var dictionary)) Shared_[key] = dictionary = new UserDictionary(path);
            return dictionary;
        }
    }

    // 登録・削除・保存の同時実行 (複数セッションが別スレッドから登録する) を直列にする。
    private readonly object _gate = new();

    public UserDictionary(string? path, bool builtIn = true)
    {
        _path = path;
        if (builtIn)
        {
            var phrases = new List<UserWord>();
            Parse(Detection.DictionarySource.ReadEmbedded("phrases.txt").Split('\n'), phrases);
            _builtIn = [.. phrases];
            foreach (var phrase in _builtIn)
            {
                if (!_builtInByReading.TryGetValue(phrase.Reading, out var list)) _builtInByReading[phrase.Reading] = list = [];
                if (!list.Contains(phrase.Word)) list.Add(phrase.Word);
            }
            _builtInMaxLength = _builtInByReading.Count == 0 ? 0 : _builtInByReading.Keys.Max(k => k.Length);
            // 取る前の Revision を覚える (取っている間に切り替わっても、次の確認でもう一度入れ替わる)
            var revision = TermDomains.Revision;
            _terms = new TermSnapshot(TermDomains.Current, revision, true);
        }
        var words = new List<UserWord>();
        if (path is not null)
        {
            if (TryRead(out var read, out var stamp, out var error)) words = read;
            else Diagnostics.Log.Warn(error!);
            _stamp = stamp;
            _problem = error ?? OversizeProblem(stamp);
        }
        _builtInLookup = _builtInByReading.GetAlternateLookup<ReadOnlySpan<char>>();
        _index = BuildIndex([.. words]);
        _nextFileCheck = Environment.TickCount64 + FileCheckIntervalMs;
    }

    private static void Parse(IEnumerable<string> lines, List<UserWord> words)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;
            var parts = line.TrimEnd('\r').Split('\t');
            // 取り込み・登録は Validate を通るが、ファイルを直接編集・復元した場合はここが唯一の入口なので、制御文字・書式文字の語は読み込まない。
            if (parts.Length >= 2 && parts[0].Trim().Length >= MinReadingLength && parts[1].Trim().Length > 0
                && !HasUnsafeText(parts[0]) && !HasUnsafeText(parts[1]))
            {
                words.Add(new UserWord(parts[0].Trim(), parts[1].Trim()));
            }
        }
    }

    /// <summary>登録内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。ほかのプロセスがファイルを書き換えたときも増える。</summary>
    public int Version
    {
        // 専門用語集の分野・ファイルが変わっていれば、ここで入れ替えて Version を進める (読む側はまず Version でキャッシュを確かめるため)。
        get
        {
            SyncTerms();
            SyncFile(force: false);
            return Volatile.Read(ref _version);
        }
    }

    private int _version;

    private TermDictionary Terms
    {
        get { SyncTerms(); return Volatile.Read(ref _terms).Terms; }
    }

    /// <summary>今の索引 (ファイルが書き換わっていれば読み直したもの)。</summary>
    private Index Current
    {
        get
        {
            SyncFile(force: false);
            return Volatile.Read(ref _index);
        }
    }

    private void SyncTerms()
    {
        var snapshot = Volatile.Read(ref _terms);
        if (!snapshot.Follows || snapshot.Revision == TermDomains.Revision) return;
        lock (_gate)
        {
            snapshot = _terms;
            // 取る前の Revision を覚える (取っている間に切り替わっても、次の確認でもう一度入れ替わる)。
            var revision = TermDomains.Revision;
            if (!snapshot.Follows || snapshot.Revision == revision) return;
            Volatile.Write(ref _terms, new TermSnapshot(TermDomains.Current, revision, true));
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// ほかのプロセス (Mac の「Meltype 辞書」の画面など) がファイルを書き換えていれば読み直す。force でなければ
    /// <see cref="FileCheckIntervalMs"/> に 1 回だけ確かめる。読めなかったときは今の内容のまま (次に書き換わるまで読み直さない)。
    /// </summary>
    private void SyncFile(bool force)
    {
        if (_path is null) return;
        var now = Environment.TickCount64;
        if (!force && now < Volatile.Read(ref _nextFileCheck)) return;
        Volatile.Write(ref _nextFileCheck, now + FileCheckIntervalMs);
        if (FileStamp.Of(_path).Equals(Volatile.Read(ref _stamp))) return;
        lock (_gate)
        {
            if (FileStamp.Of(_path).Equals(_stamp)) return;
            if (TryRead(out var words, out var stamp, out var error))
            {
                Adopt(words, stamp);
                _problem = OversizeProblem(stamp);
            }
            else
            {
                Diagnostics.Log.Warn(error!);
                Volatile.Write(ref _stamp, stamp);
                _problem = error;
            }
        }
    }

    /// <summary>
    /// ファイルが書き換わっていないかを今すぐ確かめ、変わっていれば読み直す (管理画面が一覧を出す前など)。読み直したら true。
    /// </summary>
    public bool Refresh()
    {
        var before = Volatile.Read(ref _version);
        SyncTerms();
        SyncFile(force: true);
        return Volatile.Read(ref _version) != before;
    }

    /// <summary>
    /// ファイルを読めなかった (権限・文字コード) か、大きすぎて読み込まなかったときの理由。読めていれば null。
    /// 読めないファイルには書かない (変更は理由を返す)。大きすぎたファイルは .oversize に退避済みで、ここで登録するとその語だけのファイルになる。
    /// </summary>
    public string? Problem
    {
        get
        {
            SyncFile(force: false);
            return Volatile.Read(ref _problem);
        }
    }

    public int Count => Current.Words.Length;

    /// <summary>登録した語 (ファイルの順 = 登録した順。後のものほど新しい)。変更のたびに別の配列になる (受け取った一覧は変わらない)。</summary>
    public IReadOnlyList<UserWord> Words => Current.Words;

    /// <summary>専門用語集の語数 (強制型 + 候補追加型)。ユーザー辞書の Count・Words には含めない。</summary>
    public int TermCount => Terms.Count;

    /// <summary>
    /// 専門用語集を、渡したテキスト (terms-*.txt の中身) で置き換える。同梱の読み込みはコンストラクターが済ませるので、
    /// これは主にテスト用の入口。不正な行は飛ばす。変換結果のキャッシュを捨てるため Version を進める。
    /// </summary>
    public TermDictionary LoadTerms(IEnumerable<string> texts, IReadOnlySet<string>? commonReadings = null)
    {
        var terms = TermDictionary.Parse(texts, null, commonReadings);
        lock (_gate)
        {
            Volatile.Write(ref _terms, new TermSnapshot(terms, -1, false));
            Interlocked.Increment(ref _version);
        }
        return terms;
    }

    /// <summary>候補追加型の専門用語 (読みが短い語)。変換候補に足すだけで、文節の区切りは変えない。</summary>
    public IReadOnlyList<string> LookupTermCandidates(string reading) => Terms.LookupCandidates(reading);

    /// <summary>読みが reading で始まる専門用語 (予測変換用。強制型・候補追加型の両方)。</summary>
    public IEnumerable<string> PredictTerms(string reading) => Terms.StartingWith(reading);

    /// <summary>
    /// 読みが reading で始まる、自作の専門用語集の語 (読み込んだ順)。予測変換では、ユーザー辞書の語の次・同梱の専門用語集の前に置く
    /// (自作の語は、ユーザー辞書の語と同じ扱いなので、同梱の語の使用実績に負けないように、別の列にする)。
    /// </summary>
    public IEnumerable<string> PredictUserTerms(string reading) => Terms.UserStartingWith(reading);

    // ---- 変更 (どれもファイルを読み直してから行い、すぐ保存する) ----

    /// <summary>登録する。同じ読み・同じ単語が既にあれば何もしない (成功)。登録できなければ理由を返す。</summary>
    public string? Add(string reading, string word) => Add(reading, word, failOnDuplicate: false);

    /// <summary>登録する。同じ読み・同じ単語が既にあれば <see cref="DuplicateMessage"/> (管理画面用)。登録できなければ理由を返す。</summary>
    public string? AddNew(string reading, string word) => Add(reading, word, failOnDuplicate: true);

    private string? Add(string reading, string word, bool failOnDuplicate)
    {
        reading = NormalizeReading(reading);
        word = word.Trim();
        if (Validate(reading, word) is { } error) return error;
        var entry = new UserWord(reading, word);
        return Mutate(words =>
        {
            if (words.Contains(entry)) return (false, failOnDuplicate ? DuplicateMessage : null);
            words.Add(entry);
            return (true, null);
        }, backup: false);
    }

    /// <summary>まとめて登録する (取り込み)。読みと単語が同じものが既にあれば飛ばす。登録した数を返す (保存できなければ 0)。</summary>
    public int AddRange(IEnumerable<UserWord> words) => AddRange(words, out _);

    /// <summary>まとめて登録する (取り込み)。登録した数を返す。保存できなければ 0 で、error に理由。</summary>
    public int AddRange(IEnumerable<UserWord> words, out string? error)
    {
        error = AddMany(words, out var added);
        return added.Count;
    }

    /// <summary>
    /// まとめて登録する (取り込み・管理画面の「複製」)。読みと単語が同じものが既にあれば飛ばし、不正なものも飛ばす。
    /// 1 回の読み直し・1 回の保存で済ませる (1 語ずつ登録すると、語数の 2 乗の時間がかかる)。新しく登録した語を added に返す (取り消すときに使う)。
    /// 保存できなければ理由を返し、added は空。
    /// </summary>
    public string? AddMany(IEnumerable<UserWord> words, out IReadOnlyList<UserWord> added)
    {
        var incoming = words.Select(w => new UserWord(NormalizeReading(w.Reading), w.Word.Trim())).Where(w => Validate(w.Reading, w.Word) is null).ToList();
        var list = new List<UserWord>();
        var error = Mutate(current =>
        {
            list.Clear();
            var seen = current.ToHashSet();
            foreach (var word in incoming)
            {
                if (!seen.Add(word)) continue;
                current.Add(word);
                list.Add(word);
            }
            return (list.Count > 0, null);
        }, backup: false);
        added = error is null ? list : [];
        return error;
    }

    /// <summary>1 語を消す (同じ読み・単語が複数あれば最初の 1 つ)。消す前の内容は userdict.txt.bak に残す。</summary>
    public void Remove(UserWord word)
    {
        if (Mutate(words => (words.Remove(word), null), backup: true) is { } error) Diagnostics.Log.Warn(error);
    }

    /// <summary>
    /// 渡した語をすべて消す (同じ読み・単語が複数あれば全部)。消した語と、消す前の位置 (0 始まり) を removed に返す (元に戻すときに使う)。
    /// 消す前の内容は userdict.txt.bak に残す。消せなければ理由を返す。
    /// </summary>
    public string? RemoveRange(IEnumerable<UserWord> words, out IReadOnlyList<(int Index, UserWord Word)> removed)
    {
        // 照合は読みをそろえてから (カタカナで渡された語・10/08 より前にカタカナのまま保存された語も、同じ語として扱う)
        var targets = words.Select(Key).ToHashSet();
        var list = new List<(int, UserWord)>();
        var error = Mutate(current =>
        {
            for (var i = 0; i < current.Count; i++)
            {
                if (targets.Contains(Key(current[i]))) list.Add((i, current[i]));
            }
            // 消す語を渡されたのに 1 つも無いときは、成功にしない (取り消しが効かなかったのに「取り消しました」と出さないため)
            if (list.Count == 0) return (false, targets.Count > 0 ? NotFoundMessage : null);
            current.RemoveAll(w => targets.Contains(Key(w)));
            return (true, null);
        }, backup: true);
        removed = error is null ? list : [];
        return error;
    }

    /// <summary>照合用に、読みをそろえ (<see cref="NormalizeReading"/>)、語の前後の空白を取った語。</summary>
    private static UserWord Key(UserWord word) => new(NormalizeReading(word.Reading), word.Word.Trim());

    /// <summary>
    /// 消した語を元の位置に戻す (RemoveRange の removed をそのまま渡す)。位置が今の数より後ろなら末尾に足す。
    /// 同じ読み・単語がもうあれば飛ばす。戻せなければ理由を返す。
    /// </summary>
    public string? Restore(IEnumerable<(int Index, UserWord Word)> entries)
    {
        var ordered = entries.Where(e => Validate(e.Word.Reading, e.Word.Word) is null).OrderBy(e => e.Index).ToList();
        return Mutate(current =>
        {
            var changed = false;
            foreach (var (index, word) in ordered)
            {
                if (current.Contains(word)) continue;
                current.Insert(Math.Clamp(index, 0, current.Count), word);
                changed = true;
            }
            return (changed, null);
        }, backup: false);
    }

    /// <summary>
    /// 登録した語を直す (位置はそのまま。同じ読みの語の中での優先順を変えない)。直せなければ理由を返す:
    /// 入力が不正 (<see cref="Validate"/>)、元の語が無い (<see cref="NotFoundMessage"/>)、ほかの語と重なる (<see cref="DuplicateMessage"/>)。
    /// 直す前の内容は userdict.txt.bak に残す。
    /// </summary>
    public string? Update(UserWord old, string reading, string word)
    {
        reading = NormalizeReading(reading);
        word = word.Trim();
        if (Validate(reading, word) is { } invalid) return invalid;
        var replacement = new UserWord(reading, word);
        var oldKey = Key(old);
        return Mutate(current =>
        {
            // 元の語も読みをそろえて探す (画面がカタカナの読みのまま渡しても見つかる)
            var index = current.FindIndex(w => Key(w) == oldKey);
            if (index < 0) return (false, NotFoundMessage);
            if (replacement == current[index]) return (false, null);
            if (current.Contains(replacement)) return (false, DuplicateMessage);
            current[index] = replacement;
            return (true, null);
        }, backup: true);
    }

    /// <summary>
    /// 登録・編集してよいかを確かめる (管理画面が入力のたびに呼ぶ。保存はしない)。不正な入力と、ほかの語との重なりを理由で返す。
    /// except は編集中の元の語 (それと同じなのは重なりとみなさない)。
    /// </summary>
    public string? Check(string reading, string word, UserWord? except = null)
    {
        reading = NormalizeReading(reading);
        word = word.Trim();
        if (Validate(reading, word) is { } invalid) return invalid;
        var entry = new UserWord(reading, word);
        if (except is not null && entry == Key(except)) return null;
        return Current.Words.Contains(entry) ? DuplicateMessage : null;
    }

    /// <summary>読みに登録されている単語 (新しく登録したものが先)。</summary>
    public IReadOnlyList<string> Lookup(string reading)
    {
        // 順序: ユーザーの登録 → 自作の専門用語集 (ユーザー辞書の語と同じ扱い) → 組み込み語句 → 同梱の専門用語集 (強制型)。重複は除く。
        var words = Current.ByReading.TryGetValue(reading, out var own) ? own : null;
        var terms = Terms;
        var userTerms = terms.LookupUserForced(reading);
        var builtIn = _builtInByReading.TryGetValue(reading, out var phrases) ? phrases : null;
        var bundled = terms.LookupForced(reading);
        if (userTerms.Count == 0 && bundled.Count == 0 && builtIn is null) return words ?? [];
        var merged = words is null ? [] : new List<string>(words);
        foreach (var term in userTerms) if (!merged.Contains(term)) merged.Add(term);
        foreach (var phrase in builtIn ?? []) if (!merged.Contains(phrase)) merged.Add(phrase);
        foreach (var term in bundled) if (!merged.Contains(term)) merged.Add(term);
        return merged;
    }

    /// <summary>
    /// かなを、登録した読みの部分とそれ以外に分ける。先頭から見て、その位置から始まる最も長い登録済みの読みを取る。
    /// 登録した読みが 1 つも含まれていなければ null。
    /// </summary>
    public List<(string Reading, string? Word)>? Split(string kana)
    {
        var terms = Terms;
        var index = Current;
        var maxReadingLength = index.MaxReadingLength;
        var lookup = index.Lookup;
        if ((index.ByReading.Count == 0 && terms.ForcedCount == 0 && terms.UserForcedCount == 0 && _builtInByReading.Count == 0) || kana.Length < MinReadingLength) return null;
        var pieces = new List<(string Reading, string? Word)>();
        var plain = new StringBuilder();
        var found = false;
        var i = 0;
        while (i < kana.Length)
        {
            // その位置から始まる最も長い読み。同じ長さならユーザー辞書・組み込み語句が先。文字列は作らずに引く。
            var termMax = terms.MaxForcedLengthAt(kana, i);
            var userTermMax = terms.MaxUserForcedLengthAt(kana, i);
            var matchedLength = 0;
            string? matchedWord = null;
            var longest = Math.Max(Math.Max(maxReadingLength, _builtInMaxLength), Math.Max(termMax, userTermMax));
            for (var length = Math.Min(longest, kana.Length - i); length >= MinReadingLength; length--)
            {
                var span = kana.AsSpan(i, length);
                // 同じ長さなら: ユーザーの登録 → 自作の専門用語集 → 組み込み語句 → 同梱の専門用語集
                if (length <= maxReadingLength && lookup.TryGetValue(span, out var own))
                {
                    matchedLength = length;
                    matchedWord = own[0];
                    break;
                }
                if (length <= userTermMax && terms.TryGetUserForced(span, out var userForced))
                {
                    matchedLength = length;
                    matchedWord = userForced[0];
                    break;
                }
                if (length <= _builtInMaxLength && _builtInLookup.TryGetValue(span, out var phrase))
                {
                    matchedLength = length;
                    matchedWord = phrase[0];
                    break;
                }
                if (length <= termMax && terms.TryGetForced(span, out var forced))
                {
                    matchedLength = length;
                    matchedWord = forced[0];
                    break;
                }
            }
            if (matchedWord is null)
            {
                plain.Append(kana[i]);
                i++;
                continue;
            }
            var matched = kana.Substring(i, matchedLength);
            if (plain.Length > 0)
            {
                pieces.Add((plain.ToString(), null));
                plain.Clear();
            }
            pieces.Add((matched, matchedWord));
            found = true;
            i += matched.Length;
        }
        if (plain.Length > 0) pieces.Add((plain.ToString(), null));
        return found ? pieces : null;
    }

    // ---- 読み込み・保存 ----

    /// <summary>
    /// 変更の共通処理: (ファイルがあれば) プロセスをまたぐロックを取り、ファイルを読み直して change を当て、変わったら保存する。
    /// change は (変わったか, 理由) を返す。読めないファイルには書かない (上書きして元の内容を失わないため)。
    /// backup なら、保存の前の内容を「userdict.txt.bak」に残す (削除・編集の直前の状態。1 世代だけ)。
    /// 保存できなかったときは、メモリ上の内容も変えずに理由を返す (ファイルと食い違わないように)。
    /// </summary>
    private string? Mutate(Func<List<UserWord>, (bool Changed, string? Error)> change, bool backup)
    {
        lock (_gate)
        {
            if (_path is null)
            {
                var words = new List<UserWord>(_index.Words);
                var (changed, error) = change(words);
                if (changed) Adopt(words, null);
                return error;
            }
            try
            {
                using var _ = FileLock.Acquire(_path);
                if (!TryRead(out var words, out var stamp, out var readError))
                {
                    _problem = readError;
                    return readError;
                }
                var (changed, error) = change(words);
                if (!changed)
                {
                    // 変更は無くても、読み直した内容 (ほかのプロセスの変更) は取り込む。
                    if (!stamp.Equals(_stamp)) Adopt(words, stamp);
                    _problem = OversizeProblem(stamp);
                    return error;
                }
                if (SafeFile.IsBlocked(_path)) return "ユーザー辞書のファイルが大きすぎて退避できなかったため、保存を止めています (元のファイルを守るため)。";
                var lines = new List<string> { "# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」" };
                lines.AddRange(words.Select(w => $"{w.Reading}\t{w.Word}"));
                // 読み込める大きさ (SafeFile.MaxReadBytes) を超えるファイルは書かない。超えると、次に読むときに退避されて空の辞書として扱われ、
                // そこで 1 語登録するとファイルがその 1 語だけになる (自作の専門用語集の WriteUserLines と同じ守り)。
                long bytes = 3; // BOM
                foreach (var line in lines) bytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                if (bytes > SafeFile.MaxReadBytes) return TooLargeMessage;
                // 書かずに終わるときに .bak (1 世代前) を上書きしないよう、大きさを確かめてから作る
                if (backup && stamp.Exists && stamp.Length <= SafeFile.MaxReadBytes) SafeFile.WriteAllBytes(_path + ".bak", File.ReadAllBytes(_path));
                SafeFile.WriteAllLines(_path, lines, new UTF8Encoding(true));
                // ロックを持ったまま版を取るので、ここで見る版は自分が書いたもの。
                Adopt(words, FileStamp.Of(_path));
                _problem = null;
                return error;
            }
            catch (FileLockTimeoutException ex)
            {
                Diagnostics.Log.Warn($"ユーザー辞書: {ex.Message}");
                return ex.Message;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザー辞書を保存できませんでした: {ex.Message}");
                return $"ユーザー辞書を保存できませんでした: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// ファイルを読む。無ければ空 (成功)。大きすぎるときは SafeFile が退避して null を返すので、空 (成功。既存の作法: 退避できなければ保存も止まる)。
    /// 読めないとき (権限・文字コード) は false と理由。stamp は読む前に取った版 (読む間に書き換わっても、次の確認でもう一度読む)。
    /// </summary>
    private bool TryRead(out List<UserWord> words, out FileStamp stamp, out string? error)
    {
        words = [];
        error = null;
        stamp = FileStamp.Of(_path!);
        // 前はあったのに今は無い: ロックを守らないエディター (消してから作り直す) の途中かもしれないので、少し待ってもう一度だけ見る
        // (空として読んで保存すると、その語だけのファイルになるため)。それでも無ければ、消されたとみなす。
        if (!stamp.Exists && Volatile.Read(ref _stamp).Exists)
        {
            Thread.Sleep(50);
            stamp = FileStamp.Of(_path!);
        }
        if (!stamp.Exists) return true;
        try
        {
            // 文字コードは厳密に読む (不正なバイトを U+FFFD にして書き戻すと、元のバイト列を失う)
            if (SafeFile.ReadAllTextStrict(_path!) is { } text) Parse(text.Split('\n'), words);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (DecoderFallbackException)
        {
            error = "ユーザー辞書 (userdict.txt) の文字コードが UTF-8 / UTF-16 ではないため読めません。上書きして元の内容を失わないよう、変更しませんでした (テキストエディターで UTF-8 で保存し直してください)。";
            return false;
        }
        catch (Exception ex)
        {
            error = $"ユーザー辞書を読めないため、変更しませんでした (上書きして元の内容を失わないため): {ex.Message}";
            return false;
        }
    }

    /// <summary>大きすぎて読み込まなかった (SafeFile が .oversize に退避した) ときの理由。そうでなければ null。</summary>
    private static string? OversizeProblem(FileStamp stamp) =>
        stamp.Exists && stamp.Length > SafeFile.MaxReadBytes
            ? $"ユーザー辞書 (userdict.txt) が大きすぎる (上限 {SafeFile.MaxReadBytes / 1024 / 1024} MB) ため読み込んでいません。元のファイルは userdict.txt.oversize に退避しています。ここで登録すると、userdict.txt はその語だけになります。"
            : null;

    /// <summary>読み直した・変更した内容に索引を入れ替え、Version を進める。_gate の中で呼ぶ。</summary>
    private void Adopt(List<UserWord> words, FileStamp? stamp)
    {
        Volatile.Write(ref _index, BuildIndex([.. words]));
        if (stamp is not null) Volatile.Write(ref _stamp, stamp);
        Interlocked.Increment(ref _version);
    }

    private Index BuildIndex(UserWord[] words)
    {
        var byReading = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // 後から登録したものを先にする。同梱の語句は別に持つ (_builtInByReading)。
        foreach (var word in Enumerable.Reverse(words))
        {
            if (!byReading.TryGetValue(word.Reading, out var list)) byReading[word.Reading] = list = [];
            if (!list.Contains(word.Word)) list.Add(word.Word);
        }
        return new Index
        {
            Words = words,
            ByReading = byReading,
            Lookup = byReading.GetAlternateLookup<ReadOnlySpan<char>>(),
            MaxReadingLength = byReading.Count == 0 ? 0 : byReading.Keys.Max(k => k.Length),
        };
    }
}
