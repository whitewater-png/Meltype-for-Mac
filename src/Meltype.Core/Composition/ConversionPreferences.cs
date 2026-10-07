// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// 文脈の手がかりで変換を選ぶ規則 (dictionaries/contexts.txt)。
/// 形式は 1 行に「読み 候補 : 手がかり 手がかり …」。前後の文字列に手がかりが含まれていれば、その候補を最初にする
/// (気温 が近くにあれば あつい → 暑い、財布 なら かわ → 革)。変換エンジンだけでは文脈を読み切れない語を補う。
/// </summary>
public sealed class ContextRules
{
    private readonly Dictionary<string, List<(string Candidate, string[] Cues)>> _rules = new(StringComparer.Ordinal);

    public int Count => _rules.Sum(r => r.Value.Count);

    public static ContextRules Load(string? userDirectory)
    {
        var rules = new ContextRules();
        rules.AddText(Detection.DictionarySource.ReadEmbedded("contexts.txt"));
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, "contexts.txt");
            try
            {
                if (File.Exists(path)) rules.AddText(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"ユーザーの文脈辞書を読めませんでした: {ex.Message}");
            }
        }
        return rules;
    }

    public void AddText(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var head = line[..colon].Split([' ', '\t', '　'], StringSplitOptions.RemoveEmptyEntries);
            var cues = line[(colon + 1)..].Split([' ', '\t', '\r', '　'], StringSplitOptions.RemoveEmptyEntries);
            if (head.Length != 2 || cues.Length == 0) continue;
            if (!_rules.TryGetValue(head[0], out var list)) _rules[head[0]] = list = [];
            list.Add((head[1], cues));
        }
    }

    /// <summary>
    /// 文節の読み (はしを) に対して、前後の文字列 (surrounding) の手がかりに合う候補 (箸を) を返す。無ければ null。
    /// 辞書にある最長の先頭部分で探し、残り (を) はそのまま付ける。手がかりが多く当たった規則を優先する。
    /// </summary>
    public string? Choose(string reading, string surrounding)
    {
        if (surrounding.Length == 0) return null;
        for (var length = reading.Length; length >= 1; length--)
        {
            if (!_rules.TryGetValue(reading[..length], out var list)) continue;
            var best = list
                .Select(rule => (rule.Candidate, Hits: rule.Cues.Count(cue => surrounding.Contains(cue, StringComparison.Ordinal))))
                .Where(r => r.Hits > 0)
                .OrderByDescending(r => r.Hits)
                .FirstOrDefault();
            return best.Hits > 0 ? best.Candidate + reading[length..] : null;
        }
        return null;
    }
}

/// <summary>
/// ユーザーが選び直した変換の記録 (Microsoft IME の学習と同じ)。次に同じ読みを変換したとき最初の候補にする。
/// %LOCALAPPDATA%\Meltype\conversions.json に「文節の読み → 選んだ文字列」だけを保存する。
/// </summary>
public sealed class ConversionHistory
{
    private const int MaxEntries = 5000;

    /// <summary>Touch (そのまま確定したときの回数更新) の保存を遅らせる時間。確定のたびに全件を書かない・メインスレッドで書かないため。</summary>
    private const int TouchSaveDelayMs = 5000;

    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    // 複数のセッション (別スレッドの遅延保存も) から触るので直列にする。
    private readonly object _gate = new();
    private Timer? _saveTimer;
    private bool _dirty;

    private sealed class Entry
    {
        public string Text { get; set; } = "";
        public DateTime Used { get; set; }

        /// <summary>確定した回数。古い conversions.json には無い (読み込むと 0) ので、0 は 1 回として扱う。</summary>
        public int Count { get; set; }
    }

    // パスごとの共有インスタンス。セッションごとに別インスタンスだと、後からの保存が他のセッションの更新
    // (回数・新しく覚えた語) を古いメモリ内容で上書きして消すので、同じファイルは 1 つで持つ (ユーザー辞書と同じ)。
    private static readonly Dictionary<string, ConversionHistory> Shared_ = new(StringComparer.Ordinal);

    /// <summary>同じパスなら同じインスタンスを返す (プロセス内で共有)。path が null なら共有せず毎回新しく作る。</summary>
    public static ConversionHistory Shared(string? path)
    {
        if (path is null) return new ConversionHistory(null);
        lock (Shared_)
        {
            var key = Path.GetFullPath(path);
            if (!Shared_.TryGetValue(key, out var history))
            {
                Shared_[key] = history = new ConversionHistory(path);
                // 遅延保存が残ったまま終了しても失わないよう、終了時に書く。
                AppDomain.CurrentDomain.ProcessExit += (_, _) => history.Flush();
            }
            return history;
        }
    }

    /// <summary>時計 (テストで差し替える)。</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// 使用頻度のスコア = 確定回数 × 0.5^(最後に使ってからの日数 / 30)。
    /// 回数が多いほど上、同じ回数なら新しいほど上になる。30 日で半分に減衰させるのは、「前によく使った語」が
    /// いつまでも居座らず、最近の使い方に追従するため (回数だけだと古い語が永久に勝つ、新しさだけだと 1 回きりの語が勝つ)。
    /// 式が単純なので、なぜこの並びなのか利用者にも説明できる。
    /// </summary>
    private double Score(Entry entry)
    {
        var days = Math.Max(0, (Clock() - entry.Used).TotalDays);
        return Math.Max(1, entry.Count) * Math.Pow(0.5, days / 30.0);
    }

    /// <summary>
    /// 語 (text) ごとの使用頻度スコア (履歴に無い語は含まれない)。変換エンジンの予測のうち、使った実績のある語を上に引き上げるのに使う。
    /// 同じ語が複数の読みで覚えられていれば、いちばん高いものを取る。
    /// </summary>
    public Dictionary<string, double> Usage()
    {
        lock (_gate)
        {
            var usage = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var entry in _entries.Values)
            {
                if (string.IsNullOrEmpty(entry.Text)) continue;
                var score = Score(entry);
                if (!usage.TryGetValue(entry.Text, out var best) || score > best) usage[entry.Text] = score;
            }
            return usage;
        }
    }

    public ConversionHistory(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry?>>(File.ReadAllText(path));
            // 1 文字の読み (き → 記) は、以前の版で覚えてしまったものも使わない (関係ない変換を巻き込むため)。
            // 壊れた項目 (値が null・Text が null か空) は読み飛ばす (後の Score / Get で落ちないように)。
            if (loaded is not null)
            {
                foreach (var (reading, entry) in loaded)
                {
                    if (reading is { Length: >= 2 } && entry is { Text.Length: > 0 }) _entries[reading] = entry;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"変換の学習データを読めませんでした: {ex.Message}");
        }
    }

    /// <summary>学習した内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public string? Get(string reading)
    {
        lock (_gate) return _entries.TryGetValue(reading, out var entry) ? entry.Text : null;
    }

    /// <summary>学習した変換の中から、text を選んだときの読み (一番新しいもの)。確定後の再変換で読みに戻すのに使う。無ければ null。</summary>
    public string? ReadingOf(string text)
    {
        lock (_gate) return _entries.Where(e => e.Value.Text == text).OrderByDescending(e => e.Value.Used).Select(e => e.Key).FirstOrDefault();
    }

    /// <summary>読みが prefix で始まる学習済みの変換 (使用頻度のスコアが高い順、同点は新しく使った順)。予測変換に使う。</summary>
    public IEnumerable<(string Reading, string Text)> StartingWith(string prefix)
    {
        lock (_gate)
        {
            return _entries.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(e => Score(e.Value)).ThenByDescending(e => e.Value.Used).Select(e => (e.Key, e.Value.Text)).ToList();
        }
    }

    public void Remember(string reading, string text)
    {
        if (reading.Length == 0 || text.Length == 0) return;
        lock (_gate)
        {
            // 同じ読みで同じ語を選び続けているときだけ回数を足す (別の語に変えたら数え直し)。
            var count = _entries.TryGetValue(reading, out var previous) && previous.Text == text ? Math.Max(1, previous.Count) + 1 : 1;
            _entries[reading] = new Entry { Text = text, Used = Clock(), Count = count };
            if (_entries.Count > MaxEntries)
            {
                foreach (var old in _entries.OrderBy(e => e.Value.Used).Take(_entries.Count - MaxEntries * 9 / 10).Select(e => e.Key).ToList())
                {
                    _entries.Remove(old);
                }
            }
            Version++;
            Save();
        }
    }

    /// <summary>
    /// 学習済みの語をそのまま (選び直さずに) 確定したとき、回数と最終使用時刻だけ更新する。
    /// 並びは変わらない (最初の候補のまま) ので Version は増やさず、変換結果のキャッシュは捨てない。
    /// 保存は数秒まとめて遅らせる (確定のたびに全件を書かない)。選び直し (Remember) や終了時には確実に書く。
    /// </summary>
    public void Touch(string reading, string text)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(reading, out var entry) || entry.Text != text) return;
            entry.Count = Math.Max(1, entry.Count) + 1;
            entry.Used = Clock();
            if (_path is null) return;
            _dirty = true;
            _saveTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(TouchSaveDelayMs, Timeout.Infinite);
        }
    }

    /// <summary>遅らせていた保存があれば今書く (終了時・テスト用)。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_dirty) Save();
        }
    }

    public void Forget(string reading)
    {
        lock (_gate)
        {
            if (!_entries.Remove(reading)) return;
            Version++;
            Save();
        }
    }

    /// <summary>学習した変換 (読み → 選んだ語) の一覧 (新しく使ったものから)。設定の「学習した語」に出す。</summary>
    public IReadOnlyList<(string Reading, string Text, DateTime Used)> Entries()
    {
        lock (_gate) return _entries.OrderByDescending(e => e.Value.Used).Select(e => (e.Key, e.Value.Text, e.Value.Used)).ToList();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Version++;
            Save();
        }
    }

    // 呼び出し側が _gate を持っている。
    private void Save()
    {
        if (_path is null) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"変換の学習データを保存できませんでした: {ex.Message}");
        }
    }
}
