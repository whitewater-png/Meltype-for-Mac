// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Composition;

/// <summary>
/// ユーザー辞書への登録提案。選び直して確定した「読み → 語」を数え、同じ組を何度も確定したら「提案待ち」にする。
/// %LOCALAPPDATA%\Meltype\suggest.json (Mac: ~/Library/Application Support/Meltype/suggest.json) に、読み・語・回数・却下したかだけを保存する
/// (前後の文章は保存しない。端末の外には出さない)。
/// </summary>
public sealed class DictionarySuggestions
{
    /// <summary>覚えておく組の上限。超えたら古いものから捨てる。</summary>
    public const int MaxEntries = 500;

    /// <summary>この長さ以上の英数字だけの語はパスワードの可能性が高いので数えない (以前は 16 文字。短めのパスワードも拾うため 12 に下げた)。</summary>
    private const int PasswordLikeLength = 12;

    /// <summary>メニューに出す語の長さの上限。超えたら「…」で省略する (長い語でメニューが画面からはみ出さないように)。</summary>
    public const int MaxDisplayLength = 20;

    /// <summary>メニューに出す表示用の語。<see cref="MaxDisplayLength"/> 文字を超えたら先頭だけにして「…」を付ける。</summary>
    public static string DisplayWord(string word) => word.Length <= MaxDisplayLength ? word : word[..MaxDisplayLength] + "…";

    private sealed class Entry
    {
        public string Reading { get; set; } = "";
        public string Word { get; set; } = "";
        public int Count { get; set; }

        /// <summary>「登録しない」と選んだ。数えず、提案もしない。</summary>
        public bool Rejected { get; set; }
    }

    private sealed class Data
    {
        /// <summary>登録ヒントを最後に出した日 (yyyy-MM-dd)。1 日 1 回までにするため。</summary>
        public string? LastHint { get; set; }

        /// <summary>古い順 (使うたびに末尾へ動かす)。</summary>
        public List<Entry> Items { get; set; } = [];
    }

    private readonly string? _path;
    private readonly object _gate = new();
    private readonly Data _data = new();
    private UserWord? _hintWord;

    /// <summary>時計 (テストで差し替える)。</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    // ユーザー辞書と同じく、パスごとの共有インスタンス (セッションごとに別だと、後からの保存で他のセッションの回数が消える)。
    private static readonly Dictionary<string, DictionarySuggestions> Shared_ = new(StringComparer.Ordinal);

    /// <summary>同じパスなら同じインスタンスを返す (プロセス内で共有)。path が null なら共有せず毎回新しく作る。</summary>
    public static DictionarySuggestions Shared(string? path)
    {
        if (path is null) return new DictionarySuggestions(null);
        lock (Shared_)
        {
            var key = Path.GetFullPath(path);
            if (!Shared_.TryGetValue(key, out var suggestions)) Shared_[key] = suggestions = new DictionarySuggestions(path);
            return suggestions;
        }
    }

    public DictionarySuggestions(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            if (Config.SafeFile.ReadAllText(path) is not { } json) return;
            var loaded = JsonSerializer.Deserialize<Data>(json);
            if (loaded is null) return;
            _data.LastHint = loaded.LastHint;
            _data.Items = loaded.Items?.Where(e => e is { Reading.Length: > 0, Word.Length: > 0 }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"辞書の提案データを読めませんでした: {ex.Message}");
        }
    }

    public int Count
    {
        get { lock (_gate) return _data.Items.Count; }
    }

    /// <summary>
    /// 数える対象か。読みはひらがな (UserDictionary.MinReadingLength 以上)、語は漢字・カタカナ・英字のどれかを含むこと。
    /// ひらがなだけの語は登録する意味が薄く、長い英数字の羅列はパスワードかもしれないので数えない。
    /// </summary>
    public static bool IsEligible(string reading, string word)
    {
        if (reading.Length < UserDictionary.MinReadingLength || !reading.All(IsHiragana)) return false;
        // 改行・タブは FFI の行区切り (読み\t語 を改行でつなぐ) や辞書ファイルの形式と衝突するので除く。
        if (word.Length == 0 || word == reading || word.Any(c => c is '\n' or '\r' or '\t')) return false;
        if (word.Length >= PasswordLikeLength && word.All(char.IsAsciiLetterOrDigit)) return false;
        // 記号を含む語 (P@ssw0rd! のような) はパスワードかもしれないので数えない。ただし日本語の語で普通に使う ・ は許す。
        if (word.Any(c => (char.IsPunctuation(c) || char.IsSymbol(c)) && c != '・')) return false;
        return word.Any(c => IsKanji(c) || IsKatakana(c) || char.IsAsciiLetter(c));
    }

    private static bool IsHiragana(char c) => c is (>= 'ぁ' and <= 'ゖ') or 'ー';
    private static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヺ';
    private static bool IsKanji(char c) => c is (>= '一' and <= '鿿') or (>= '㐀' and <= '䶿') or (>= '豈' and <= '﫿') or '々';

    /// <summary>
    /// 確定した読み → 語を 1 回数える。数えたら true を返し、その回数が threshold に達したとき (提案待ちになった直後) は hint として覚える。
    /// 対象外・登録済み (registered に同じ組がある)・却下済みなら何もしない。
    /// </summary>
    public bool Record(string reading, string word, int threshold, UserDictionary? registered = null)
    {
        if (!IsEligible(reading, word)) return false;
        if (registered?.Lookup(reading).Contains(word) == true) return false;
        lock (_gate)
        {
            var entry = Find(reading, word);
            if (entry is { Rejected: true }) return false;
            if (entry is null) entry = new Entry { Reading = reading, Word = word };
            else _data.Items.Remove(entry);
            entry.Count++;
            // 使うたびに末尾へ動かす = 先頭が一番古い。
            _data.Items.Add(entry);
            if (entry.Count == threshold) _hintWord = new UserWord(reading, word);
            Trim();
            Save();
            return true;
        }
    }

    /// <summary>提案待ち (回数が threshold 以上で、却下していない組)。新しく待ちになった順に最大 max 件。</summary>
    public IReadOnlyList<UserWord> Pending(int threshold, int max = int.MaxValue)
    {
        lock (_gate)
        {
            return _data.Items.Where(e => !e.Rejected && e.Count >= threshold).Reverse()
                .Take(max).Select(e => new UserWord(e.Reading, e.Word)).ToList();
        }
    }

    /// <summary>登録した組を提案待ちから消す (辞書に入ったので数える必要がなくなる)。</summary>
    public void Remove(string reading, string word)
    {
        lock (_gate)
        {
            if (Find(reading, word) is { } entry && _data.Items.Remove(entry)) Save();
        }
    }

    /// <summary>「登録しない」。以後その組は数えず、提案もしない (履歴を消去するまで)。</summary>
    public void Reject(string reading, string word)
    {
        lock (_gate)
        {
            var entry = Find(reading, word);
            if (entry is null)
            {
                _data.Items.Add(entry = new Entry { Reading = reading, Word = word });
                Trim();
            }
            entry.Rejected = true;
            Save();
        }
    }

    /// <summary>数えた回数・提案待ち・却下をすべて消す。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _data.Items.Clear();
            _hintWord = null;
            Save();
        }
    }

    /// <summary>
    /// 提案待ちができた直後の 1 回だけ、そのことを知らせる語を返す (1 日 1 回まで。その日にもう出していれば null)。
    /// 確定の直後に 1 行だけ出す用で、返したら出したことを覚える。
    /// 共有インスタンスなので、別のセッションで待ちができた語のヒントがこちらのセッションの確定で出ることもある
    /// (1 日 1 回の制限内なので許容する。どの入力欄に出るかは問わない)。
    /// </summary>
    public UserWord? TakeHint()
    {
        lock (_gate)
        {
            var word = _hintWord;
            _hintWord = null;
            if (word is null) return null;
            var today = Clock().ToString("yyyy-MM-dd");
            if (_data.LastHint == today) return null;
            _data.LastHint = today;
            Save();
            return word;
        }
    }

    private Entry? Find(string reading, string word) => _data.Items.FirstOrDefault(e => e.Reading == reading && e.Word == word);

    private void Trim()
    {
        // 「登録しない」にした組は、捨てると提案が復活してしまうので後回しにして、古い未却下から捨てる。
        while (_data.Items.Count > MaxEntries)
        {
            var index = _data.Items.FindIndex(e => !e.Rejected);
            _data.Items.RemoveAt(index < 0 ? 0 : index);
        }
    }

    /// <summary>一時ファイルに書いてから置き換える (書き込み中に落ちても元のファイルを壊さない。変換履歴と同じ)。</summary>
    private void Save()
    {
        if (_path is null) return;
        try
        {
            Config.SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"辞書の提案データを保存できませんでした: {ex.Message}");
        }
    }
}
