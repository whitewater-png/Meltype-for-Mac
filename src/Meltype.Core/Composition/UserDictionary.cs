// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Composition;

/// <summary>ユーザー辞書の 1 語。</summary>
public sealed record UserWord(string Reading, string Word);

/// <summary>
/// ユーザー辞書 (%LOCALAPPDATA%\Meltype\userdict.txt、1 行に「読み[Tab]単語」)。
/// 変換で最優先に使う: 変換する読みの中に登録した読みが含まれていれば、その部分は変換エンジンの区切りに関係なく
/// 登録した単語にする (きごうとう → 記号等 を登録すると、きごうとうふくめ → 記号等|含め)。
/// トレイの「ユーザー辞書...」から登録・削除する。
/// </summary>
public sealed class UserDictionary
{
    /// <summary>1 文字の読みは、ほかの語の中にも現れやすく巻き込みが大きいので登録させない。</summary>
    public const int MinReadingLength = 2;

    private readonly string? _path;
    private readonly List<UserWord> _words = [];
    // 同梱の語句 (dictionaries/phrases.txt)。変換エンジンが苦手な語句を補う。ユーザーの登録より後回しで、保存も表示もしない。
    private readonly List<UserWord> _builtIn = [];
    private Dictionary<string, List<string>> _byReading = new(StringComparer.Ordinal);
    private int _maxReadingLength;

    // パスごとの共有インスタンス。セッションごとに別インスタンスだと、登録が他セッションに見えず、
    // 後からの Save でファイルを古い内容で上書きして登録が消えるため、同じファイルは 1 つのインスタンスで持つ。
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
        if (builtIn) Parse(Detection.DictionarySource.ReadEmbedded("phrases.txt").Split('\n'), _builtIn);
        try
        {
            if (path is not null && File.Exists(path)) Parse(File.ReadAllLines(path, Encoding.UTF8), _words);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"ユーザー辞書を読めませんでした: {ex.Message}");
        }
        Rebuild();
    }

    private static void Parse(IEnumerable<string> lines, List<UserWord> words)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith('#')) continue;
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length >= 2 && parts[0].Trim().Length >= MinReadingLength && parts[1].Trim().Length > 0)
            {
                words.Add(new UserWord(parts[0].Trim(), parts[1].Trim()));
            }
        }
    }

    /// <summary>登録内容が変わるたびに増える (変換結果のキャッシュを捨てるため)。</summary>
    public int Version { get; private set; }

    public int Count => _words.Count;

    public IReadOnlyList<UserWord> Words => _words;

    /// <summary>登録する。同じ読み・同じ単語が既にあれば何もしない。登録できなければ理由を返す。</summary>
    public string? Add(string reading, string word)
    {
        reading = reading.Trim();
        word = word.Trim();
        if (reading.Length < MinReadingLength) return $"読みは {MinReadingLength} 文字以上にしてください。";
        if (word.Length == 0) return "単語を入力してください。";
        if (reading.Contains('\t') || word.Contains('\t')) return "タブ文字は使えません。";
        lock (_gate)
        {
            if (_words.Any(w => w.Reading == reading && w.Word == word)) return null;
            _words.Add(new UserWord(reading, word));
            Changed();
        }
        return null;
    }

    /// <summary>まとめて登録する (取り込み)。読みと単語が同じものが既にあれば飛ばす。登録した数を返す。</summary>
    public int AddRange(IEnumerable<UserWord> words)
    {
        lock (_gate)
        {
            var added = 0;
            foreach (var word in words)
            {
                if (word.Reading.Length < MinReadingLength || word.Word.Length == 0 || word.Reading.Contains('\t') || word.Word.Contains('\t')) continue;
                if (_words.Any(w => w.Reading == word.Reading && w.Word == word.Word)) continue;
                _words.Add(word);
                added++;
            }
            if (added > 0) Changed();
            return added;
        }
    }

    public void Remove(UserWord word)
    {
        lock (_gate)
        {
            if (_words.Remove(word)) Changed();
        }
    }

    /// <summary>読みに登録されている単語 (新しく登録したものが先)。</summary>
    public IReadOnlyList<string> Lookup(string reading) =>
        _byReading.TryGetValue(reading, out var words) ? words : [];

    /// <summary>
    /// かなを、登録した読みの部分とそれ以外に分ける。先頭から見て、その位置から始まる最も長い登録済みの読みを取る。
    /// 登録した読みが 1 つも含まれていなければ null。
    /// </summary>
    public List<(string Reading, string? Word)>? Split(string kana)
    {
        if (_byReading.Count == 0 || kana.Length < MinReadingLength) return null;
        var pieces = new List<(string Reading, string? Word)>();
        var plain = new StringBuilder();
        var found = false;
        var i = 0;
        while (i < kana.Length)
        {
            string? matched = null;
            for (var length = Math.Min(_maxReadingLength, kana.Length - i); length >= MinReadingLength; length--)
            {
                if (_byReading.ContainsKey(kana.Substring(i, length)))
                {
                    matched = kana.Substring(i, length);
                    break;
                }
            }
            if (matched is null)
            {
                plain.Append(kana[i]);
                i++;
                continue;
            }
            if (plain.Length > 0)
            {
                pieces.Add((plain.ToString(), null));
                plain.Clear();
            }
            pieces.Add((matched, _byReading[matched][0]));
            found = true;
            i += matched.Length;
        }
        if (plain.Length > 0) pieces.Add((plain.ToString(), null));
        return found ? pieces : null;
    }

    private void Changed()
    {
        Rebuild();
        Version++;
        Save();
    }

    private void Rebuild()
    {
        var byReading = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // 後から登録したものを先にする。同梱の語句はユーザーの登録の後。
        foreach (var word in Enumerable.Reverse(_words).Concat(_builtIn))
        {
            if (!byReading.TryGetValue(word.Reading, out var list)) byReading[word.Reading] = list = [];
            if (!list.Contains(word.Word)) list.Add(word.Word);
        }
        _byReading = byReading;
        _maxReadingLength = byReading.Count == 0 ? 0 : byReading.Keys.Max(k => k.Length);
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var lines = new List<string> { "# Meltype ユーザー辞書: 1 行に「読み<Tab>単語」" };
            lines.AddRange(_words.Select(w => $"{w.Reading}\t{w.Word}"));
            var temp = _path + ".tmp";
            File.WriteAllLines(temp, lines, new UTF8Encoding(true));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"ユーザー辞書を保存できませんでした: {ex.Message}");
        }
    }
}
