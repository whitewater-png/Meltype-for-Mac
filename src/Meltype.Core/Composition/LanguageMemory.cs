// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 英語とも日本語とも読める語 (api, sushi …) を、ユーザーが自分で英字 / かなに直したときに覚えておく。
/// 次からその語は、自動の判定より覚えた方を優先する (api と打って F10 で英字にして確定 → 次から api は英字)。
/// %LOCALAPPDATA%\Meltype\languages.json に「打った英字 → 英語か」だけを保存する。
/// </summary>
public sealed class LanguageMemory
{
    private const int MaxEntries = 3000;
    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    // 全消去 (メニュー) と入力中の学習が別スレッドになりうるので直列にする。
    private readonly object _gate = new();

    internal sealed class Entry
    {
        public bool English { get; set; }
        public DateTime Used { get; set; }

        /// <summary>同じ向きに直した回数 (前の版で保存したものは 0 = 1 回)。</summary>
        public int Count { get; set; }

        /// <summary>F10 / F6 などで、はっきり英字 / かなに直したか (変換の候補から選んだだけなら false)。</summary>
        public bool Explicit { get; set; }

        /// <summary>
        /// 覚えている向きと逆の向きに、候補から選んだだけ (はっきり直していない) で続けて確定した回数 (1-6)。
        /// 一度の例外で向きが反転しないよう、<see cref="ReverseToFlip"/> 回続いたときだけ反転する。前の版で保存したものは 0。
        /// </summary>
        public int Reverse { get; set; }

        /// <summary>直近の逆向き (<see cref="Reverse"/> を増やした) の時刻 (UTC)。<see cref="ReverseWindow"/> を超えたら数え直す。前の版で保存したものは既定値 (= 古い)。</summary>
        public DateTime ReverseAt { get; set; }
    }

    /// <summary>はっきり直したわけではない逆向きの確定が、この回数続いたら向きを反転する (R41-7)。</summary>
    internal const int ReverseToFlip = 2;

    /// <summary>「続けて」の長さ: 前の逆向きからこの時間を超えたら、逆向きの回数は 1 から数え直す (R43-6)。</summary>
    internal static readonly TimeSpan ReverseWindow = TimeSpan.FromHours(24);

    /// <summary>今の時刻 (UTC)。テストが差し替える。</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary><see cref="RememberUndoable"/> の取り消し用 (覚える前の項目。無かったら Previous は null)。</summary>
    internal sealed record RememberUndo(string Word, Entry? Previous);

    /// <summary>学習した語 (設定の「学習した語」の一覧に出す)。Active は今効いているか (2 回目を待っている語は false)。</summary>
    public sealed record Learned(string Word, bool English, int Count, DateTime Used, bool Active, bool? Context = null);

    /// <summary>
    /// ローマ字としてよく使う日本語になる語か (kyouha = 今日は、sushi = すし)。こういう語は、一度英字にして確定しただけでは
    /// 英語として覚えない (2 回で覚える)。間違えて一度確定しただけで、ふつうの日本語がずっと英字になってしまうため。
    /// </summary>
    public Func<string, bool>? IsCommonJapanese { get; set; }

    /// <summary>
    /// ローマ字として読み切れる語か (go = ご)。こういう語を、変換の候補から英字を選んだだけで英語として覚えると、
    /// 日本語の中 (nihongo) まで英字になりやすいので、2 文字の語 (go・to・no) は、はっきり直したとき (F10) 以外は 2 回で覚える。
    /// </summary>
    public Func<string, bool>? IsReadableRomaji { get; set; }

    /// <summary>英語として覚えるのに 2 回要る語か。</summary>
    private bool NeedsTwice(string word, Entry entry) =>
        entry.English && Math.Max(1, entry.Count) < 2 &&
        (IsCommonJapanese?.Invoke(word) == true || (!entry.Explicit && word.Length <= 2 && IsReadableRomaji?.Invoke(word) == true));

    // パスごとの共有インスタンス (入力欄ごとのセッションが別々に持つと、「学習データをすべて消去」が他のセッションの
    // メモリ上の内容を消せず、次の保存で消したはずの語がファイルに戻ってしまうため。ほかの学習データと同じ)。
    private static readonly Dictionary<string, LanguageMemory> Shared_ = new(StringComparer.Ordinal);

    /// <summary>同じパスなら同じインスタンスを返す (プロセス内で共有)。path が null なら共有せず毎回新しく作る。</summary>
    public static LanguageMemory Shared(string? path)
    {
        if (path is null) return new LanguageMemory(null);
        lock (Shared_)
        {
            var key = Path.GetFullPath(path);
            if (!Shared_.TryGetValue(key, out var memory)) Shared_[key] = memory = new LanguageMemory(path);
            return memory;
        }
    }

    public LanguageMemory(string? path)
    {
        _path = path;
        if (path is null) return;
        // 版を先に取ってから読む (読んでいる間に書き換わっても、次の確認で版が違うので読み直せる)。
        _stamp = FileStamp.Of(path);
        if (Read(path, out _brokenOnDisk) is { } loaded) foreach (var (word, entry) in loaded) _entries[word] = entry;
    }

    /// <summary>
    /// ファイルから読む (ファイルが無ければ空)。読めないとき (壊れている・権限・大きすぎる) は null (ログに残す)。
    /// broken は、ファイルはあるのに読めなかった (大きすぎる以外) とき true。保存の前に「名前.broken」に残す
    /// (大きすぎるファイルは SafeFile が「名前.oversize」に退避済み)。
    /// 手で書き換えた・壊れたファイルの null の値や不正なキーは入れない (入れると、その語を打つたびにキー処理の途中で例外になる)。
    /// キーは Remember と同じ決まり (2 文字以上の英小文字。長さは単語リストの上限まで)。
    /// </summary>
    private static Dictionary<string, Entry>? Read(string path, out bool broken)
    {
        broken = false;
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        try
        {
            // 大きすぎる: 読めなかった扱い (使っている途中なら、空にせず今の内容のまま続ける)。
            if (SafeFile.ReadAllText(path) is not { } json) return null;
            var loaded = JsonSerializer.Deserialize(json, LearningJsonContext.Default.LanguageEntries);
            if (loaded is null) return result;
            foreach (var (word, entry) in loaded)
            {
                if (entry is null || !IsValidWord(word)) continue;
                result[word] = entry;
            }
            if (result.Count > MaxEntries)
            {
                foreach (var key in result.OrderBy(e => e.Value.Used).Take(result.Count - MaxEntries).Select(e => e.Key).ToList()) result.Remove(key);
            }
            return result;
        }
        catch (Exception ex)
        {
            broken = File.Exists(path);
            Diagnostics.Log.Warn($"英語 / 日本語の学習データを読めませんでした: {SafeFile.Describe(ex)}");
            return null;
        }
    }

    private static bool IsValidWord(string? word)
    {
        if (word is null) return false;
        var baseWord = BaseWord(word);
        return baseWord.Length is >= 2 and <= Detection.WordList.MaxWordLength && baseWord.All(char.IsAsciiLetterLower);
    }

    // ---- ほかのプロセスの変更の反映 ----
    // Mac では IME と「Meltype 辞書」の画面が別のプロセスで、それぞれが languages.json をメモリに持つ。保存はファイル全体の書き直しなので、
    // 読み直さずに保存すると、画面で消した語を IME の次の保存が書き戻してしまい、IME もその語を使い続ける。
    // そこで、キーごとの Get と一覧 (Entries・Check) は、ファイルの版 (FileStamp) が前に読んだ / 書いたときと違えば読み直す。
    // 変更する処理は、ロックを取ってから版を比べずに必ず読み直し (版が同じでも中身が変わっていることがある: 時刻が 1 秒単位のファイルシステム)、
    // 最新のファイルに重ねて保存する。

    /// <summary>Get がファイルを確かめる間隔 (ミリ秒)。Get はキーごとに呼ばれるので毎回は調べない。テストが 0 にする。</summary>
    internal static int FileCheckIntervalMs { get; set; } = 1000;

    /// <summary>
    /// IME の学習 (Remember) がロックを待つ時間の上限 (ミリ秒)。確定のたびに呼ばれるので長く待たない
    /// (取れなければロック無しで保存する)。画面の変更は <see cref="FileLock.TimeoutMs"/> まで待つ。
    /// </summary>
    internal static int RememberLockTimeoutMs { get; set; } = 200;

    /// <summary>画面の変更でロックを取れなかったときの理由 (画面にそのまま出す)。</summary>
    internal const string BusyReason = "ほかの画面が学習データを書き込み中です。少し待ってからもう一度試してください。";

    // 「版が分からない」(ロック無しで保存した後)。どの実際の版とも等しくならないので、次の確認で必ず読み直す。
    private static readonly FileStamp UnknownStamp = new(true, -1, -1, -1);

    private FileStamp _stamp = FileStamp.Missing;
    private long _nextFileCheck;

    // 最後に読んだファイルが壊れていて読めなかった (保存の前に .broken に残す)。
    private bool _brokenOnDisk;

    /// <summary>ファイルが書き換わっていれば今すぐ読み直す (画面が一覧を出す前など)。内容が変わったら true (Version も増える)。</summary>
    public bool Refresh()
    {
        lock (_gate) return RefreshCore();
    }

    /// <summary>force なら版を比べずに読む (変更の前)。</summary>
    private bool RefreshCore(bool force = false)
    {
        if (_path is null) return false;
        _nextFileCheck = Environment.TickCount64 + FileCheckIntervalMs;
        var stamp = FileStamp.Of(_path);
        if (!force && stamp.Equals(_stamp)) return false;
        _stamp = stamp;
        // 読めないファイル (壊れている・大きすぎる・権限) なら今の内容のまま続ける (空にしない)。
        // 変更したときは、壊れたファイルを .broken に残してから今の内容に書き直す。
        if (Read(_path, out _brokenOnDisk) is not { } loaded || SameAs(loaded)) return false;
        _entries.Clear();
        foreach (var (word, entry) in loaded) _entries[word] = entry;
        Version++;
        return true;
    }

    private bool SameAs(Dictionary<string, Entry> other) =>
        other.Count == _entries.Count && other.All(e => _entries.TryGetValue(e.Key, out var mine) &&
            mine.English == e.Value.English && mine.Count == e.Value.Count && mine.Explicit == e.Value.Explicit && mine.Reverse == e.Value.Reverse && mine.ReverseAt == e.Value.ReverseAt && mine.Used == e.Value.Used);

    /// <summary>
    /// 変更 (読み直し → 変更 → 保存) の間、ほかのプロセスが同じファイルを書かないようにするロックを取る。取れたら fileLock に入れて null。
    /// 画面の変更 (fromApp) は、取れなければ理由を返す (保存しない)。IME の学習は短く待ち、取れなければロック無しで続ける
    /// (入力を止めないため。その後は版が分からない扱いにして、次の確認で必ず読み直す)。
    /// 損: ロック無しの保存は、同じ瞬間のほかのプロセスの書き込みと重なりうる。画面がロックを持ち続けると、その間の IME の学習が消えるか、
    /// まれに画面で消した語が書き戻される。画面の変更がロックを持つのは 1 回の読み直し + 保存 (約 10 ms) だけなので、人の操作では起きにくい。
    /// 既定の 200 ms で取り合っても失わないことを確かめるテストは無い (LearnedWordsTests の同時書き込みは待ち時間を長くして、ロックが取れる場合だけを見る)。
    /// </summary>
    private string? TryLock(bool fromApp, out IDisposable? fileLock)
    {
        fileLock = null;
        if (_path is null) return null;
        try
        {
            fileLock = FileLock.Acquire(_path, fromApp ? FileLock.TimeoutMs : Math.Min(FileLock.TimeoutMs, RememberLockTimeoutMs));
            return null;
        }
        catch (Exception ex)
        {
            if (fromApp)
            {
                Diagnostics.Log.Warn($"英語 / 日本語の学習データのロックを取れなかったので、変更しませんでした: {ex.Message}");
                return ex is FileLockTimeoutException ? BusyReason : $"学習データのロックを取れなかったので、変更しませんでした: {ex.Message}";
            }
            Diagnostics.Log.Warn($"英語 / 日本語の学習データのロックを取れませんでした (ロック無しで続けます): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 変更の共通の流れ: ロック → 必ず読み直す → 変更 → 保存。change は (変更しない理由, 変えたか) を返す (理由があるときは何も変えないこと)。
    /// 保存できなかったら、メモリも変更の前に戻して理由を返す (画面の一覧・IME・ファイルがずれないように。
    /// 戻さないと、保存できなかった削除が、後の別の保存で黙ってファイルに書かれる)。
    /// </summary>
    private string? Edit(bool fromApp, Func<(string? Reason, bool Changed)> change)
    {
        lock (_gate)
        {
            if (TryLock(fromApp, out var fileLock) is { } busy) return busy;
            using (fileLock)
            {
                RefreshCore(force: true);
                var before = _entries.Select(e => KeyValuePair.Create(e.Key, Copy(e.Value))).ToList();
                var (reason, changed) = change();
                if (reason is not null || !changed) return reason;
                Trim();
                Version++;
                if (Save() is { } failure)
                {
                    _entries.Clear();
                    foreach (var (word, entry) in before) _entries[word] = entry;
                    // 変えた内容で判定の結果を作ったかもしれないので、戻したときも版を上げる。
                    Version++;
                    return failure;
                }
                // ロック無しで保存したときは、直後にほかのプロセスが書いても版が同じに見えることがあるので、次は必ず読み直す。
                if (fileLock is null && _path is not null) _stamp = UnknownStamp;
                return null;
            }
        }
    }

    /// <summary>
    /// 覚えている内容の写し (ファイルに結び付かない)。exclude が true を返す語は入れない。「判定を調べる」画面が、
    /// 学習した語を 1 つだけ外した場合の判定を、languages.json に触れずに試すのに使う
    /// (このクラスは封印されていて Get も仮想ではないので、包みではなく写しにする)。写しには Remember・Set・Remove を呼ばない。
    /// </summary>
    internal LanguageMemory CopyWithout(Func<string, bool> exclude)
    {
        var copy = new LanguageMemory(null) { IsCommonJapanese = IsCommonJapanese, IsReadableRomaji = IsReadableRomaji, ContextSensitive = ContextSensitive, Context = Context };
        lock (_gate)
        {
            // 写す前に読み直す (画面が Entries で見た内容と、判定に使う内容を揃える)。Get と同じく読むだけでファイルは変えない。
            RefreshCore();
            foreach (var (word, entry) in _entries)
            {
                if (!exclude(BaseWord(word))) copy._entries[word] = Copy(entry);
            }
        }
        return copy;
    }

    private static Entry Copy(Entry entry) => new() { English = entry.English, Used = entry.Used, Count = entry.Count, Explicit = entry.Explicit, Reverse = entry.Reverse, ReverseAt = entry.ReverseAt };

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>学習を前の文脈 (英語の後か日本語の後か) ごとに分けるか (設定「英語 / 日本語の学習を前の文脈ごとに分ける」: 本家 #800)。</summary>
    public bool ContextSensitive { get; set; }

    /// <summary>今の入力の前が英語か (true)、日本語か (false)、分からないか (null)。</summary>
    public bool? Context { get; set; }

    /// <summary>文脈ごとに分けるときの、語の後ろにつける印 (分けないときは空)。</summary>
    private string ContextSuffix => ContextSensitive && Context is { } english ? (english ? "@e" : "@j") : "";

    /// <summary>文脈の印 (@e・@j) を外した語。</summary>
    private static string BaseWord(string key) =>
        key.EndsWith("@e", StringComparison.Ordinal) || key.EndsWith("@j", StringComparison.Ordinal) ? key[..^2] : key;

    /// <summary>覚えている内容が変わるたびに増える (判定の結果を使い回してよいかを見るのに使う。ほかのプロセスの変更を読み直したときも)。</summary>
    public int Version { get; private set; }

    private int _maxLengthVersion = -1;
    private int _maxLength;

    /// <summary>覚えている語の最長の文字数 (長い区間の判定を飛ばしてよいかを見るのに使う)。</summary>
    public int MaxWordLength
    {
        get
        {
            lock (_gate)
            {
                if (_maxLengthVersion != Version)
                {
                    _maxLength = _entries.Count == 0 ? 0 : _entries.Keys.Max(k => k.Length);
                    _maxLengthVersion = Version;
                }
                return _maxLength;
            }
        }
    }

    /// <summary>覚えている語なら英語か (true) 日本語か (false)。覚えていなければ null。word は小文字の英字。</summary>
    public bool? Get(string word)
    {
        lock (_gate)
        {
            // キーごとに呼ばれるので、ファイルの確認は間隔をあける (ほかのプロセスで消した・足した語は、遅くともこの間隔の後に効く)。
            if (_path is not null && Environment.TickCount64 >= _nextFileCheck) RefreshCore();
            return GetCore(word);
        }
    }

    private bool? GetCore(string word)
    {
        // 文脈ごとに分けているときは、その文脈の項目を先に見る (無ければ、分ける前に覚えた項目).
        var suffix = ContextSuffix;
        if (suffix.Length > 0 && _entries.TryGetValue(word + suffix, out var contextual))
            return NeedsTwice(word, contextual) ? null : contextual.English;
        if (!_entries.TryGetValue(word, out var entry)) return null;
        if (NeedsTwice(word, entry)) return null;
        return entry.English;
    }

    /// <summary>
    /// ユーザーが英字 / かなに直した語を覚える (2 文字以上の英字だけ)。
    /// explicitChoice は F10 / F6 / Tab ではっきり直したとき (変換の候補から選んだだけなら false)。
    /// 保存できなければ覚えない (ログに残す)。ロックは短くしか待たない (取れなければロック無しで保存する)。
    /// </summary>
    public void Remember(string word, bool english, bool explicitChoice = false) => RememberUndoable(word, english, explicitChoice);

    /// <summary>
    /// <see cref="Remember"/> と同じ。あとで <see cref="Undo"/> で覚える前に戻せるよう、覚える前の項目を返す
    /// (確定後の F10 / F6 で、Swift が入力欄の書き換えを確かめられず飛ばしたとき、学習も取り消すのに使う)。覚えなかった (語が不正・保存できない) ときは null。
    /// </summary>
    internal RememberUndo? RememberUndoable(string word, bool english, bool explicitChoice = false)
    {
        word = word.ToLowerInvariant();
        if (word.Length < 2 || !word.All(char.IsAsciiLetterLower)) return null;
        var entryKey = word + ContextSuffix;
        Entry? previous = null;
        if (Edit(fromApp: false, () =>
            {
                previous = _entries.TryGetValue(entryKey, out var existing) ? Copy(existing) : null;
                RememberCore(word, entryKey, english, explicitChoice);
                return (null, true);
            }) is { } failure)
        {
            // 保存を止めているときは、止めた警告 (Save が止めてから 1 回だけ出す) が原因を言っているので、ここでは出さない (保存のたびに出すと、ログの上限の 1000 件が埋まる)。
            if (_path is null || !SafeFile.IsBlocked(_path))
                Diagnostics.Log.Warn($"英語 / 日本語の学習を保存できなかったので、今回の学習は取り消しました: {failure}");
            return null;
        }
        return new RememberUndo(entryKey, previous);
    }

    /// <summary><see cref="RememberUndoable"/> で覚えたことを、覚える前の項目に戻す。保存できなければログに残すだけ。</summary>
    internal void Undo(RememberUndo undo)
    {
        if (Edit(fromApp: false, () =>
            {
                if (undo.Previous is null) _entries.Remove(undo.Word);
                else _entries[undo.Word] = Copy(undo.Previous);
                return (null, true);
            }) is { } failure)
        {
            Diagnostics.Log.Warn($"英語 / 日本語の学習を取り消せませんでした: {failure}");
        }
    }

    private void RememberCore(string word, string entryKey, bool english, bool explicitChoice)
    {
        if (!_entries.TryGetValue(entryKey, out var old))
        {
            AddEntry(entryKey, word, english, explicitChoice);
            return;
        }
        if (old.English == english)
        {
            old.Used = DateTime.UtcNow;
            var waiting = NeedsTwice(word, old);
            old.Count = Math.Max(1, old.Count) + 1;
            old.Explicit |= explicitChoice;
            old.Reverse = 0;
            old.ReverseAt = default;
            if (waiting) Diagnostics.Log.Decision($"{Diagnostics.Log.Text(word)}は次から英語にします (2 回目の学習)。");
            return;
        }
        // 逆向き。F10 / F6 ではっきり直したなら、すぐ反転する。候補から選んだだけなら、続けて ReverseToFlip 回になるまで向きを変えない
        // (一度の例外で、覚えた向きが逆転しないように。1-6)。「続けて」は前の逆向きから ReverseWindow 以内 (超えたら 1 から数え直す。R43-6)。
        // 反転は Count = 2 で作る: 2 回ルールが重なって、反転後にもう 1 回要らないように (R41-7)。
        // ただし、はっきりした反転でも、英語向きでよく使う日本語 (sushi) は Count = 1: 2 回ルールを効かせる (うっかり F10 を 1 回押しただけで英語にならない。R43-5)。
        var now = UtcNow();
        var reverse = old.Reverse > 0 && now - old.ReverseAt <= ReverseWindow ? old.Reverse : 0;
        if (explicitChoice || reverse + 1 >= ReverseToFlip)
        {
            AddEntry(entryKey, word, english, explicitChoice, count: explicitChoice && english && IsCommonJapanese?.Invoke(word) == true ? 1 : 2);
            return;
        }
        old.Reverse = reverse + 1;
        old.ReverseAt = now;
        Diagnostics.Log.Decision($"{Diagnostics.Log.Text(word)}は逆向きの選択を {old.Reverse} 回見ましたが、向きは変えません (あと {ReverseToFlip - old.Reverse} 回続いたら変えます)。");
    }

    private void AddEntry(string entryKey, string word, bool english, bool explicitChoice, int count = 1)
    {
        var entry = new Entry { English = english, Used = DateTime.UtcNow, Count = count, Explicit = explicitChoice };
        _entries[entryKey] = entry;
        Diagnostics.Log.Decision(NeedsTwice(word, entry)
            ? $"{Diagnostics.Log.Text(word)}はローマ字としても読めるので、もう一度英字にして確定したら英語にします (学習)。"
            : $"{Diagnostics.Log.Text(word)}は次から{(english ? "英語" : "日本語")}にします (学習)。");
    }

    /// <summary>学習した語の一覧 (新しく使ったものから)。ほかのプロセスの変更を読み直してから返す。</summary>
    public IReadOnlyList<Learned> Entries()
    {
        lock (_gate)
        {
            RefreshCore();
            return _entries.OrderByDescending(e => e.Value.Used)
                .Select(e =>
                {
                    var word = BaseWord(e.Key);
                    var context = e.Key.EndsWith("@e", StringComparison.Ordinal) ? true
                        : e.Key.EndsWith("@j", StringComparison.Ordinal) ? false : (bool?)null;
                    return new Learned(word, e.Value.English, Math.Max(1, e.Value.Count), e.Value.Used, !NeedsTwice(word, e.Value), context);
                })
                .ToList();
        }
    }

    /// <summary>
    /// 一覧に足す・直してよいか (保存はしない)。だめなら理由 (画面にそのまま出す)、よければ null。
    /// except は編集中の元の語 (その語とは重複にしない)。大文字は小文字にして調べる。
    /// </summary>
    public string? Check(string word, string? except = null)
    {
        lock (_gate)
        {
            RefreshCore();
            return CheckCore(Normalize(word), except is null ? null : Normalize(except));
        }
    }

    private static string Normalize(string word) => word.Trim().ToLowerInvariant();

    /// <summary>理由に入れる語 (画面にそのまま出す)。改行・Tab などの制御文字は空白にし、40 文字を超えたら切る。</summary>
    private static string Excerpt(string text)
    {
        const int max = 40;
        if (text.Length > max) text = text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)] + "…";
        return string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));
    }

    private string? CheckCore(string word, string? except)
    {
        if (word.Length == 0) return "語を入力してください。";
        if (!word.All(char.IsAsciiLetterLower)) return "英字 (a〜z) だけで入力してください。";
        if (word.Length < 2) return "2 文字以上にしてください。";
        if (word.Length > Detection.WordList.MaxWordLength) return $"{Detection.WordList.MaxWordLength} 文字までにしてください。";
        if (word != except && _entries.ContainsKey(word)) return $"「{word}」はもう一覧にあります。";
        return null;
    }

    /// <summary>
    /// 一覧に足す (oldWord が null) か、oldWord を直す (綴り・英字 / かな)。すぐ保存する。だめなら理由、できたら null。
    /// 手で足した・直した語はすぐ効くようにする (はっきり直した扱いで回数を 2 以上にする。1 回では覚えない語 (kyouha) も 2 回目を待たない)。
    /// ほかのプロセスがロックを持ったままなら、保存せずに理由を返す。
    /// </summary>
    public string? Set(string word, bool english, string? oldWord = null)
    {
        word = Normalize(word);
        var old = oldWord is null ? null : Normalize(oldWord);
        return Edit(fromApp: true, () =>
        {
            if (CheckCore(word, old) is { } reason) return (reason, false);
            Entry? previous = null;
            if (old is not null && !_entries.TryGetValue(old, out previous)) return ($"「{Excerpt(old)}」は一覧にありません (ほかの画面で消されたかもしれません)。", false);
            if (old is not null && old != word) _entries.Remove(old);
            // 同じ語を直すときは、最後に使った日と回数を残す (英字 / かなを変えたら、回数は新しい向きで 2 から数え直す)。
            // 綴りを変えたら別の語なので、今足した語として扱う。
            var same = previous is not null && old == word;
            _entries[word] = new Entry
            {
                English = english,
                Used = same ? previous!.Used : DateTime.UtcNow,
                Count = same && previous!.English == english ? Math.Max(previous.Count, 2) : 2,
                Explicit = true,
            };
            Diagnostics.Log.Decision(old is null
                ? $"{Diagnostics.Log.Text(word)}を{(english ? "英語" : "日本語")}として足しました (学習した語の一覧)。"
                : $"{Diagnostics.Log.Text(old)}を{Diagnostics.Log.Text(word)} ({(english ? "英語" : "日本語")}) に直しました (学習した語の一覧)。");
            return (null, true);
        });
    }

    /// <summary>
    /// 学習した語を忘れる (「学習した語」の一覧から消したとき)。一覧に無い語は飛ばす。だめなら理由、できたら null。
    /// ほかのプロセスがロックを持ったままなら、保存せずに理由を返す。
    /// </summary>
    public string? Remove(IEnumerable<string> words)
    {
        var keys = words.Select(Normalize).ToList();
        return Edit(fromApp: true, () =>
        {
            var removed = false;
            foreach (var key in keys) removed |= _entries.Remove(key);
            return (null, removed);
        });
    }

    /// <summary>すべて忘れる (「学習データをすべて消去」)。ロックを取れない・保存できないときは、消さずに <see cref="IOException"/> (理由)。</summary>
    public void Clear()
    {
        if (Edit(fromApp: true, () =>
            {
                _entries.Clear();
                return (null, true);
            }) is { } failure)
        {
            throw new IOException(failure);
        }
    }

    private void Trim()
    {
        if (_entries.Count <= MaxEntries) return;
        foreach (var key in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
        {
            _entries.Remove(key);
        }
    }

    /// <summary>
    /// 保存する。だめなら理由 (ログにも残す)、できたら null。保存した後のファイルの版を覚える (自分の保存で読み直さないため)。
    /// 読めなかった (壊れた) ファイルは、上書きする前に「名前.broken」に残す (残せなければ保存しない)。
    /// </summary>
    private string? Save()
    {
        if (_path is null) return null;
        try
        {
            if (SafeFile.IsBlocked(_path))
            {
                SafeFile.WarnBlockedOnce(_path, $"{Path.GetFileName(_path)} は、大きすぎて退避できなかったので、保存しません (元のファイルを守るため)。");
                return "学習データのファイルが大きすぎて退避もできなかったので、保存しませんでした (元のファイルを守るため)。";
            }
            if (_brokenOnDisk && File.Exists(_path))
            {
                var backup = _path + ".broken";
                File.Copy(_path, backup, overwrite: true);
                SafeFile.Restrict(backup);
                Diagnostics.Log.Warn($"読めなかった {Path.GetFileName(_path)} を {Path.GetFileName(backup)} に残してから書き直します。");
            }
            SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_entries, LearningJsonContext.Default.LanguageEntries));
            _brokenOnDisk = false;
            _stamp = FileStamp.Of(_path);
            return null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"英語 / 日本語の学習データを保存できませんでした: {ex.Message}");
            return $"保存できませんでした: {ex.Message}";
        }
    }
}
