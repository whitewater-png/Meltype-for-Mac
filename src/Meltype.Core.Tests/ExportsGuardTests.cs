// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.RegularExpressions;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// Mac / Linux の FFI の入口 (src/Meltype.Mac.Native/Exports.cs) を、ソースのテキストとして確かめる。
/// NativeAOT では [UnmanagedCallersOnly] の関数から例外が外へ出るとプロセス (IME) ごと落ちるので、
/// (1) すべての入口の本体が決まった「包み」(try/catch か、Run / Text / Reason / Guard) を通ること、
/// (2) Swift (mac/Sources) が dlsym で探す名前、(3) ABI 検査ツール (tools/check-mac-aot-settings.py) が呼ぶ名前が、すべて EntryPoint にあることを見る
/// (IME 側は関数が見つからないと黙ってその機能を止めるので、名前のずれは実行しても気づきにくい)。
/// リポジトリの外 (配布物) で動かしたときはファイルが無いので、何も確かめない (AbiVersion_MatchesBetweenCSharpAndSwift と同じ)。
/// </summary>
internal static class ExportsGuardTests
{
    /// <summary>
    /// 包みを通さなくてよい入口と、その理由。Body は本体の形 (正規表現。式本体なら => の後、ブロックなら { } の中):
    /// 中身を足すと形が変わってこのテストで落ちるので、足すときは包みに入れる。
    /// アップデート・Zenzai の入口 (meltype_update_evaluate・meltype_get_zenzai) は変更の対象外だが、ここには載せず、
    /// ほかの入口と同じ形の検査に掛ける (今は try/catch で包まれていて通る。テストは読むだけで、そのコードは変えない)。
    /// </summary>
    private static readonly (string Name, string Reason, string Body)[] Allowed =
    [
        ("meltype_abi_version", "定数を返すだけ (例外が起きない)", @"^AbiVersion$"),
        ("meltype_set_reader", "関数ポインタを static 変数に入れるだけ (例外が起きない)", @"^s_reading = reading$"),
        ("meltype_init", "関数ポインタを static 変数に入れて 1 を返すだけ (例外が起きない)", @"^(s_\w+ = \w+;\s*)+return 1;$"),
        ("meltype_free", "NativeMemory.Free は C の free で、managed の例外を投げない", @"^NativeMemory\.Free\(text\)$"),
    ];

    internal sealed record Entry(string Name, bool Expression, string Body, string Modifiers = "");

    [Test]
    public static void Exports_EveryEntryPointIsGuarded()
    {
        if (ReadExports() is not { } source) return;
        var entries = ParseEntries(source);
        Assert.Equal(Regex.Matches(source, @"\[UnmanagedCallersOnly\b").Count, entries.Count, "[UnmanagedCallersOnly] をすべて読み分けた");
        Assert.True(entries.Count >= 70, $"入口の数 ({entries.Count})");
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct().Count(), "EntryPoint の名前に重複が無い");

        var allowed = Allowed.ToDictionary(a => a.Name);
        var constants = ConstDefaults();
        foreach (var a in Allowed) Assert.True(entries.Any(e => e.Name == a.Name), $"許可リストの {a.Name} が Exports.cs にある (消したならリストからも消す)");

        var problems = new List<string>();
        foreach (var entry in entries)
        {
            if (allowed.TryGetValue(entry.Name, out var a))
            {
                if (!Regex.IsMatch(entry.Body, a.Body)) problems.Add($"{entry.Name}: 許可リストの形 ({a.Reason}) から変わった。包みに入れる: {entry.Body}");
                continue;
            }
            if (Problem(entry, constants) is { } problem) problems.Add($"{entry.Name}: {problem}");
        }

        // 包みそのものも、例外を外へ出さない形であること。同じ名前のオーバーロードは、修飾子によらずすべて見る。
        var helpers = ParseHelpers(source);
        foreach (var name in new[] { "Run", "Text", "Reason", "Guard" })
            if (!helpers.Any(h => h.Name == name)) problems.Add($"包み {name} が無い");
        foreach (var helper in helpers)
        {
            if (Problem(helper, constants) is { } problem || !helper.Body.TrimStart().StartsWith("try", StringComparison.Ordinal))
                problems.Add($"包み {helper.Name}: try/catch (Exception) だけの形ではない");
            if (!helper.Modifiers.Split(' ').Contains("private"))
                problems.Add($"包み {helper.Name}: private ではない ({helper.Modifiers.Trim()}。包みは Exports.cs の中だけで使う)");
        }

        Assert.True(problems.Count == 0, "包まれていない FFI の入口:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// 設定の ON/OFF を返す入口 (meltype_get_continue_after_conversion など) は、例外のときその設定の既定値を返す
    /// (0 = OFF に固定すると、既定が ON の設定 (Shift+Enter) だけ既定と逆向きになる)。既定値は設定クラスの const から取る。
    /// </summary>
    [Test]
    public static void Exports_FlagGettersFailToSettingDefault()
    {
        // 既定値の const は、config.json に項目が無いときの値 (Settings の初期値) と同じ。
        Assert.Equal(new Settings().ShiftEnterNewline, ShiftEnterNewlineSetting.Default, "ShiftEnterNewlineSetting.Default = Settings の初期値");
        Assert.Equal(new Settings().ContinueAfterConversion, ContinueAfterConversionSetting.Default, "ContinueAfterConversionSetting.Default = Settings の初期値");
        if (ReadExports() is not { } source) return;
        var getters = 0;
        var problems = new List<string>();
        foreach (var entry in ParseEntries(source))
        {
            if (Regex.Match(entry.Body, @"^Guard\(""[^""]*"", (?<failed>.+?), \(\) => Config\.(?<setting>\w+)\.IsOn \? 1 : 0\)$") is not { Success: true } m) continue;
            getters++;
            var expected = $"Config.{m.Groups["setting"].Value}.Default ? 1 : 0";
            if (m.Groups["failed"].Value != expected) problems.Add($"{entry.Name}: 例外のときの値が {m.Groups["failed"].Value} (期待: {expected})");
        }
        Assert.Equal(2, getters, "設定の ON/OFF を返す入口 (Guard(…, () => Config.….IsOn ? 1 : 0)) を読めた数");
        Assert.True(problems.Count == 0, "例外のとき設定の既定値を返さない入口:\n" + string.Join("\n", problems));
    }

    [Test]
    public static void Exports_SwiftSymbolNamesExist()
    {
        if (RepoRoot() is not { } root || ReadExports() is not { } source) return;
        var names = EntryPoints(source);
        var missing = new List<string>();
        var perFile = new Dictionary<string, int>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "mac", "Sources"), "*.swift", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var found = Regex.Matches(text, @"\b(?:symbol|dlsym)\((?:\w+,\s*)?""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
            perFile[Path.GetFileName(file)] = found.Count;
            missing.AddRange(found.Where(n => !names.Contains(n)).Select(n => $"{Path.GetFileName(file)}: {n}"));
        }
        // 読み取りの正規表現が空振りしていないこと (0 件で素通りしない)。
        Assert.True(perFile.GetValueOrDefault("NativeCore.swift") >= 20, $"NativeCore.swift の symbol(\"…\") を読めた ({perFile.GetValueOrDefault("NativeCore.swift")} 件)");
        Assert.True(perFile.GetValueOrDefault("NativeDictionary.swift") >= 30, $"NativeDictionary.swift の symbol(\"…\") を読めた ({perFile.GetValueOrDefault("NativeDictionary.swift")} 件)");
        Assert.True(missing.Count == 0, "Swift が探すが Exports.cs の EntryPoint に無い名前:\n" + string.Join("\n", missing));
    }

    [Test]
    public static void Exports_AotCheckToolNamesExist()
    {
        if (RepoRoot() is not { } root || ReadExports() is not { } source) return;
        var path = Path.Combine(root, "tools", "check-mac-aot-settings.py");
        if (!File.Exists(path)) return;
        var names = EntryPoints(source);
        var text = File.ReadAllText(path);
        // 関数表 ("meltype_…": ([引数], 戻り値)) と、表に無くても lib.meltype_… で呼んでいる名前。
        var table = Regex.Matches(text, @"^\s*""(meltype_\w+)"":\s*\(", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        var calls = Regex.Matches(text, @"\blib\.(meltype_\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.True(table.Count >= 40, $"関数表を読めた ({table.Count} 件)");
        var missing = table.Concat(calls).Distinct().Where(n => !names.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "check-mac-aot-settings.py が使うが Exports.cs の EntryPoint に無い名前:\n" + string.Join("\n", missing));
    }

    // ---- Exports.cs の読み分け ----

    private static string? RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Meltype.Mac.Native", "Exports.cs"))) directory = directory.Parent;
        return directory?.FullName;
    }

    private static string? ReadExports() =>
        RepoRoot() is { } root ? File.ReadAllText(Path.Combine(root, "src", "Meltype.Mac.Native", "Exports.cs")).Replace("\r\n", "\n") : null;

    private static HashSet<string> EntryPoints(string source) =>
        Regex.Matches(source, @"\[UnmanagedCallersOnly\(EntryPoint = ""([^""]+)""\)\]").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>[UnmanagedCallersOnly(EntryPoint = "…")] の付いた関数を、名前と本体 (式本体なら => の後、ブロックなら { } の中) に分ける。</summary>
    internal static List<Entry> ParseEntries(string source) =>
        ParseMethods(source, @"\[UnmanagedCallersOnly\(EntryPoint = ""(?<name>[^""]+)""\)\]\s*public static [^(;{}=]+?\b\w+\(");

    /// <summary>包み (Run / Text / Reason / Guard。private 以外の同名のオーバーロードも拾う)。</summary>
    private static List<Entry> ParseHelpers(string source) =>
        ParseMethods(source, @"\n[ \t]*(?<modifiers>(?:\w+ )*)static [^(;{}=\n]+?\b(?<name>Run|Text|Reason|Guard)(?:<\w+>)?\(");

    /// <summary>src/Meltype.Core/Config の設定クラスのうち、既定値を const bool Default で持つもの (クラス名)。</summary>
    private static HashSet<string> ConstDefaults()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (RepoRoot() is not { } root) return result;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Meltype.Core", "Config"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            if (Regex.Match(text, @"\bstatic class (\w+)") is { Success: true } c && Regex.IsMatch(text, @"\b(?:internal|public) const bool Default = (?:true|false);"))
                result.Add(c.Groups[1].Value);
        }
        return result;
    }

    private static List<Entry> ParseMethods(string source, string pattern)
    {
        var entries = new List<Entry>();
        foreach (Match m in Regex.Matches(source, pattern))
        {
            var rest = Scan(source, m.Index + m.Length - 1, untilSemicolon: false) + 1;   // 引数の ) の次
            while (char.IsWhiteSpace(source[rest])) rest++;
            if (source.AsSpan(rest).StartsWith("=>"))
            {
                var end = Scan(source, rest + 2, untilSemicolon: true);
                entries.Add(new Entry(m.Groups["name"].Value, true, source[(rest + 2)..end].Trim(), m.Groups["modifiers"].Value));
            }
            else if (source[rest] == '{')
            {
                var end = Scan(source, rest, untilSemicolon: false);
                entries.Add(new Entry(m.Groups["name"].Value, false, source[(rest + 1)..end].Trim(), m.Groups["modifiers"].Value));
            }
            else
            {
                throw new AssertionException($"{m.Groups["name"].Value} の本体を読み分けられませんでした");
            }
        }
        return entries;
    }

    /// <summary>許された形なら null、そうでなければ理由。</summary>
    internal static string? Problem(Entry entry, IReadOnlySet<string> constants)
    {
        if (entry.Expression) return HelperCall(entry.Body, constants);
        var body = Regex.Replace(entry.Body, @"^\s*//.*\n", "", RegexOptions.Multiline).Trim();
        // 前置きとして許すのは、例外の起きない行だけ: out の引数を先に空にする / 文字列の定数を入れる。
        while (Regex.Match(body, @"^(?:if \(\w+ != null\) \*\w+ = (?:null|0);|var \w+ = ""[^""\\{}]*"";)\s*") is { Success: true } prelude)
            body = body[prelude.Length..];
        if (body.StartsWith("return ", StringComparison.Ordinal))
        {
            var end = Scan(body, 0, untilSemicolon: true);
            return end != body.Length - 1 ? "return の後に文がある" : HelperCall(body["return ".Length..end].Trim(), constants);
        }
        if (Regex.Match(body, @"^try\s*\{") is { Success: true } tryBlock)
        {
            var tryEnd = Scan(body, tryBlock.Length - 1, untilSemicolon: false);
            var after = body[(tryEnd + 1)..].TrimStart();
            var catchBlock = Regex.Match(after, @"^catch(?:\s*\(Exception(?: \w+)?\))?\s*\{");
            if (!catchBlock.Success) return "try の後が catch (Exception) ではない (finally だけ・型を絞った catch は例外が外へ出る)";
            var catchEnd = Scan(after, catchBlock.Length - 1, untilSemicolon: false);
            // throw キーワードのほか、ExceptionDispatchInfo….Throw()・ThrowIfNull などの投げる呼び出しも、名前に throw があれば落とす
            // (Exports.cs の catch には今 throw という語が無い)。名前に throw の無いメソッドが中で投げる場合は見ない (既知の限界)。
            if (Regex.IsMatch(after[catchBlock.Length..catchEnd], "throw", RegexOptions.IgnoreCase)) return "catch の中で投げている (throw という語がある)";
            return after[(catchEnd + 1)..].Trim().Length > 0 ? "try/catch の外に文がある" : null;
        }
        return "本体が try/catch でも包み (Run / Text / Reason / Guard) の呼び出しでもない";
    }

    /// <summary>
    /// Run / Text / Reason / Guard の呼び出しだけで、引数が try の外で例外を起こさない形なら null。引数は位置ごとに決めた形だけ許す:
    /// 最後 = ラムダかメソッド名、Run の 1 番目 = 小文字で始まる名前 (入口の引数。静的プロパティは大文字で始まる)、
    /// Text / Reason / Guard の 1 番目 = 文字列の定数、Guard (3 引数) の 2 番目 = 数・null・true/false か、
    /// 設定クラスの const の既定値 (Config.X.Default ? 1 : 0。const なので実行時に何も評価しない)。
    /// 最後の「メソッド名」が実はデリゲートを返す静的プロパティでも見分けない (既知の限界)。
    /// </summary>
    private static string? HelperCall(string call, IReadOnlySet<string> constants)
    {
        var head = Regex.Match(call, @"^(Run|Text|Reason|Guard)\(");
        if (!head.Success) return "包み (Run / Text / Reason / Guard) を通っていない";
        var helper = head.Groups[1].Value;
        var close = Scan(call, head.Length - 1, untilSemicolon: false);
        if (close != call.Length - 1) return "包みの呼び出しの後に続きがある";
        var arguments = SplitArguments(call[head.Length..close]);
        if (!(arguments.Count == 2 || (helper == "Guard" && arguments.Count == 3))) return $"包み {helper} の引数の数が違う ({arguments.Count})";
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var safe = i == arguments.Count - 1
                ? Regex.IsMatch(argument, @"^(?:(?:\(\)|\w+)\s*=>[\s\S]*|[A-Z][\w.]*)$")
                : i == 0
                    ? helper == "Run" ? Regex.IsMatch(argument, @"^[a-z]\w*$") && argument is not ("null" or "true" or "false") : Regex.IsMatch(argument, @"^""[^""\\{}]*""$")
                    : Regex.IsMatch(argument, @"^(?:-?\d+|null|true|false)$")
                        || (Regex.Match(argument, @"^Config\.(\w+)\.Default \? 1 : 0$") is { Success: true } d && constants.Contains(d.Groups[1].Value));
            if (!safe) return $"包みの引数が try の外で評価される (または形が違う): {argument}";
        }
        return null;
    }

    private static List<string> SplitArguments(string text)
    {
        var result = new List<string>();
        var start = 0;
        Walk(text, 0, (i, depth, c) =>
        {
            if (depth == 0 && c == ',')
            {
                result.Add(text[start..i].Trim());
                start = i + 1;
            }
            return false;
        });
        result.Add(text[start..].Trim());
        return result;
    }

    /// <summary>
    /// start の括弧に対応する閉じ括弧 (untilSemicolon = false)、または括弧の外の最初の ; (true) の位置。
    /// 文字列・文字・行コメントの中は数えない。
    /// </summary>
    private static int Scan(string text, int start, bool untilSemicolon)
    {
        var found = -1;
        Walk(text, start, (i, depth, c) =>
        {
            if (untilSemicolon ? depth == 0 && c == ';' : depth == 0 && c is ')' or '}' or ']')
            {
                found = i;
                return true;
            }
            return false;
        });
        return found >= 0 ? found : throw new AssertionException("Exports.cs を読み分けられませんでした (括弧・文字列の対応)");
    }

    /// <summary>文字を 1 つずつ見る。visit には括弧の深さ (開き括弧はその後から +1、閉じ括弧はその文字で -1 済み) を渡し、true で止める。</summary>
    private static void Walk(string text, int start, Func<int, int, char, bool> visit)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i = text.IndexOf('\n', i);
                if (i < 0) return;
                continue;
            }
            if (c == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++)
                    if (text[i] == '\\') i++;
                continue;
            }
            if (c == '\'')
            {
                i += text[i + 1] == '\\' ? 3 : 2;
                continue;
            }
            if (c is ')' or '}' or ']') depth--;
            if (visit(i, depth, c)) return;
            if (c is '(' or '{' or '[') depth++;
        }
    }
}
