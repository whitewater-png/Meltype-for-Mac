// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>自作の専門用語集 (TermDomains.User.cs): 作成・名前の変更・削除、語の編集、ユーザー辞書から移す (変換が変わらないこと)、書き出し・取り込み、ほかのプロセスの変更。</summary>
internal static class UserTermDomainTests
{
    private const string Civil = "# 名称: 土木・建設\n# 出典: テスト\nこうぞうぶつ\t構造物\t\t強制\nほそう\t舗装\n";

    private sealed class Env(string directory, Action restore)
    {
        public string Directory { get; } = directory;
        public string Config => Path.Combine(Directory, "config.json");
        public string Terms => Path.Combine(Directory, "terms");
        public string UserDict => Path.Combine(Directory, "userdict.txt");
        public void Dispose() => restore();
    }

    /// <summary>偽の同梱の分野 (civil) と、一時フォルダーの config.json・terms/ に差し替える。</summary>
    private static Env Use()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var previousConfig = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        var previousUser = TermDomains.UserDirectory;
        var previousInterval = TermDomains.ExternalCheckIntervalMs;
        var previousFileInterval = UserDictionary.FileCheckIntervalMs;
        TermDomains.ConfigPath = () => Path.Combine(directory, "config.json");
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.UserDirectory = () => Path.Combine(directory, "terms");
        TermDomains.Source = () => [("civil", () => Civil)];
        TermDomains.ExternalCheckIntervalMs = 0;
        UserDictionary.FileCheckIntervalMs = 0;
        TermDomains.Reset();
        return new Env(directory, () =>
        {
            TermDomains.ConfigPath = previousConfig;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.UserDirectory = previousUser;
            TermDomains.ExternalCheckIntervalMs = previousInterval;
            UserDictionary.FileCheckIntervalMs = previousFileInterval;
            TermDomains.Reset();
            try { System.IO.Directory.Delete(directory, recursive: true); } catch { }
        });
    }

    private static string Create(string name)
    {
        Assert.Equal<string?>(null, TermDomains.CreateUserDomain(name, out var id), $"「{name}」を作れる");
        return id;
    }

    private static string[] Enabled(Env env) => Settings.Load(env.Config).EnabledTermDomains.ToArray();

    [Test]
    public static void Create_Rename_Delete_Restore()
    {
        var env = Use();
        try
        {
            var id = Create("自作の用語");
            Assert.True(TermDomains.IsUserId(id) && id.StartsWith("user-") && id != "civil", "ID は user- と 16 進 8 桁");
            var file = Path.Combine(env.Terms, $"terms-{id}.txt");
            Assert.True(File.Exists(file), "terms/terms-user-xxxxxxxx.txt ができる");
            Assert.True(File.ReadAllText(file).StartsWith("# 名称: 自作の用語\n") || File.ReadAllText(file).StartsWith("# 名称: 自作の用語\r\n"), "同梱と同じ形式のヘッダー");
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file), "ファイルは 0600");
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(env.Terms), "フォルダーは 0700");
            }
            var domain = TermDomains.List().Single(d => d.Id == id);
            Assert.True(domain.IsUser && domain.Enabled && domain.Name == "自作の用語" && domain.Count == 0, "一覧に自作として出て、すぐ有効");
            Assert.True(!TermDomains.List().Single(d => d.Id == "civil").IsUser, "同梱は自作ではない");
            Assert.True(Enabled(env).Contains(id), "config.json の有効な分野に入る");
            Assert.True(TermDomains.FormatForFfi(TermDomains.List()).Split('\n').Single(l => l.StartsWith(id)).EndsWith("\t1\t1"), "FFI の 5 つ目の欄 (自作)");
            Assert.True(TermDomains.FormatForFfi(TermDomains.List()).Split('\n').Single(l => l.StartsWith("civil")).EndsWith("\t0\t0"), "同梱は 0 (有効でなく、自作でもない)");

            // 名前の検め
            Assert.True(TermDomains.CreateUserDomain("  ", out _) is { Length: > 0 }, "空の名前は断る");
            Assert.True(TermDomains.CreateUserDomain("自作の用語", out _) is { Length: > 0 }, "自作とかぶる名前は断る");
            Assert.True(TermDomains.CreateUserDomain("土木・建設", out _) is { Length: > 0 }, "同梱とかぶる名前は断る");
            Assert.True(TermDomains.CreateUserDomain(new string('あ', 51), out _) is { Length: > 0 }, "長すぎる名前は断る");
            Assert.True(TermDomains.CreateUserDomain("a\tb", out _) is { Length: > 0 }, "タブは断る");
            var second = Create("もう 1 つ");
            Assert.True(second != id, "ID は別");

            // 名前の変更
            Assert.Equal<string?>(null, TermDomains.RenameUserDomain(id, "改名した用語"));
            Assert.Equal("改名した用語", TermDomains.List().Single(d => d.Id == id).Name, "名前が変わる");
            Assert.True(TermDomains.RenameUserDomain(id, "もう 1 つ") is { Length: > 0 }, "かぶる名前へは変えられない");
            Assert.Equal<string?>(null, TermDomains.RenameUserDomain(id, "改名した用語"), "自分の名前のままは成功");
            Assert.True(TermDomains.RenameUserDomain("civil", "x") is { Length: > 0 }, "同梱は変えられない");
            Assert.True(TermDomains.RenameUserDomain("user-00000000", "x") is { Length: > 0 }, "無い分野は変えられない");

            // 削除と復元
            TermDomains.AddUserWords(id, [("きごう", "記号", "")], out _, out _);
            Assert.Equal<string?>(null, TermDomains.DeleteUserDomain(id, out var content, out var wasEnabled));
            Assert.True(wasEnabled && content.Contains("きごう\t記号"), "消す前の中身と有効だったかを返す");
            Assert.True(!File.Exists(file) && TermDomains.List().All(d => d.Id != id), "ファイルも一覧からも消える");
            Assert.True(!Enabled(env).Contains(id) && Enabled(env).Contains(second), "有効な分野の一覧から外れる (ほかは残る)");
            Assert.True(TermDomains.DeleteUserDomain("civil", out _, out _) is { Length: > 0 }, "同梱は消せない");
            Assert.True(TermDomains.List().Any(d => d.Id == "civil"), "同梱の分野は触っていない");
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, content, wasEnabled));
            var restored = TermDomains.List().Single(d => d.Id == id);
            Assert.True(restored.Name == "改名した用語" && restored.Enabled && restored.Count == 1 && Enabled(env).Contains(id), "同じ ID・中身・有効で戻る");
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, content, true), "すでにあれば成功 (戻し済み。取り消しを消せなくならない)");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Words_Add_Update_Remove_Restore()
    {
        var env = Use();
        try
        {
            var id = Create("語の編集");
            var error = TermDomains.AddUserWords(id,
                [("きごう", "記号", "メモ"), ("カタカナ", "片仮名", ""), ("あ", "短い", ""), ("きごう", "記号", ""), ("abc", "ローマ字だけ", ""), ("ほそう", "舗装", "")],
                out var added, out var skipped);
            Assert.Equal<string?>(null, error);
            Assert.Equal(3, added.Count, "足した語 (重複は黙って飛ばす)");
            Assert.True(added.Any(a => a.Reading == "かたかな"), "読みは整える (カタカナ → ひらがな)");
            Assert.Equal(2, skipped.Count, "入れられない語は理由つきで返す (1 文字の読み・ひらがなを含まない読み)");
            Assert.True(skipped.All(s => s.Reason.Length > 0), "理由がある");
            Assert.Equal(3, TermDomains.List().Single(d => d.Id == id).Count, "語数");
            var words = TermDomains.Words(id)!;
            Assert.True(words.Select(w => w.Entry.Word).SequenceEqual(["記号", "片仮名", "舗装"]) && words[0].Entry.Note == "メモ" && words.All(w => !w.Excluded), "ファイルの順・注記つき・除外なし");

            // 直す
            Assert.Equal<string?>(null, TermDomains.UpdateUserWord(id, ("きごう", "記号"), "きごう", "記號"));
            Assert.Equal("記號", TermDomains.Words(id)![0].Entry.Word, "位置はそのまま");
            Assert.Equal("メモ", TermDomains.Words(id)![0].Entry.Note, "注記も残る");
            Assert.Equal<string?>(UserDictionary.DuplicateMessage, TermDomains.UpdateUserWord(id, ("きごう", "記號"), "ほそう", "舗装"));
            Assert.Equal<string?>(UserDictionary.NotFoundMessage, TermDomains.UpdateUserWord(id, ("ないよみ", "無い"), "あたらしい", "新しい"));
            Assert.True(TermDomains.UpdateUserWord(id, ("きごう", "記號"), "き", "記") is { Length: > 0 }, "不正な入力は断る");
            Assert.Equal<string?>(null, TermDomains.CheckUserWord(id, "あたらしい", "新しい", null));
            Assert.Equal<string?>(UserDictionary.DuplicateMessage, TermDomains.CheckUserWord(id, "ほそう", "舗装", null));
            Assert.Equal<string?>(null, TermDomains.CheckUserWord(id, "ほそう", "舗装", ("ほそう", "舗装")), "編集中の元の語は重なりではない");

            // 消す・戻す
            Assert.Equal<string?>(null, TermDomains.RemoveUserWords(id, [("きごう", "記號"), ("ほそう", "舗装")], out var removed));
            Assert.True(removed.Select(r => r.Index).SequenceEqual([0, 2]), "元の位置を返す");
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["片仮名"]), "消える");
            Assert.Equal<string?>(null, TermDomains.RestoreUserWords(id, removed));
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["記號", "片仮名", "舗装"]), "元の位置に戻る");
            Assert.Equal("メモ", TermDomains.Words(id)![0].Entry.Note, "注記も戻る");

            // 手で足したコメントは、編集しても消えない
            var file = Path.Combine(env.Terms, $"terms-{id}.txt");
            File.AppendAllText(file, "# 手で足したコメント\n");
            TermDomains.AddUserWords(id, [("ついか", "追加", "")], out _, out _);
            Assert.True(File.ReadAllText(file).Contains("# 手で足したコメント"), "コメントが残る");
            Assert.True(TermDomains.AddUserWords("civil", [("ついか", "追加", "")], out _, out _) is { Length: > 0 }, "同梱の分野には足せない");
        }
        finally { env.Dispose(); }
    }

    // ---- ユーザー辞書から移す ----

    private static readonly (string Reading, string Word)[] Moved =
    [
        ("きごう", "記号"),        // 3 文字の読み (同梱の専門用語集なら候補追加型)
        ("あいこん", "icon"),      // 語が ASCII だけ (同梱なら候補追加型)
        ("こうせい", "構成"),      // 日常語と同じ読み (同梱なら候補追加型)
        ("ぬるぽが", "ヌルポ"),    // ふつうの強制型
    ];

    [Test]
    public static void Move_ConvertsLikeBefore_AsUserDictionaryWords()
    {
        var env = Use();
        try
        {
            Assert.True(CommonReadings.Set.Contains("こうせい"), "前提: こうせい は日常語の読み");
            // 前: ユーザー辞書の語
            var before = new UserDictionary(null, builtIn: false);
            foreach (var (reading, word) in Moved) before.Add(reading, word);
            // 後: 同じ語を自作の分野に移したあとの辞書 (ユーザー辞書は空)
            var path = env.UserDict;
            var source = new UserDictionary(path, builtIn: false);
            foreach (var (reading, word) in Moved) source.Add(reading, word);
            var id = Create("移した語");
            var error = TermDomains.MoveFromUserDictionary(source, id, [.. Moved.Select(m => new UserWord(m.Reading, m.Word))], out var result);
            Assert.Equal<string?>(null, error);
            Assert.Equal(4, result.AddedToDomain.Count, "分野に足した");
            Assert.Equal(4, result.RemovedFromUser.Count, "ユーザー辞書から消えた");
            Assert.Equal(0, source.Count, "ユーザー辞書は空");
            var after = new UserDictionary(null);   // 分野に従う (同梱の語句も読むが、ここで調べる読みには関係しない)
            Assert.True(after.TermCount == 4, "分野の語が辞書に入る");

            foreach (var kana in new[] { "きごう", "きごうをつくる", "あいこんをくりっく", "こうせいろうどうしょう", "こうせい", "ぬるぽがでた", "ほんのきごうとこうせい" })
            {
                var a = before.Split(kana);
                var b = after.Split(kana);
                Assert.Equal(Describe(a), Describe(b), $"「{kana}」の区切りと語が同じ");
            }
            foreach (var (reading, _) in Moved)
                Assert.True(before.Lookup(reading).SequenceEqual(after.Lookup(reading)), $"「{reading}」の候補が同じ");
            Assert.True(after.LookupTermCandidates("きごう").Count == 0, "候補追加型には回らない (強制型のまま)");
            // 予測変換: 移した語も、ユーザー辞書の語と同じ順 (ファイルの順) で出る。同梱の専門用語集 (fake の civil) の語は、そのあと。
            Assert.True(after.PredictUserTerms("きご").SequenceEqual(["記号"]) && !after.PredictTerms("きご").Any(), "予測変換にも出る (自作の語は、同梱の語とは別の列)");
            Assert.True(TermDomains.Set("civil", true), "同梱の土木も使う");
            var withBundled = new UserDictionary(null);
            Assert.True(withBundled.PredictUserTerms("こう").SequenceEqual(["構成"]) && withBundled.PredictTerms("こう").SequenceEqual(["構造物"]),
                "自作の語と同梱の語は別の列 (使用実績で並べ替えても、自作の語が先に出る): " + string.Join(",", withBundled.PredictUserTerms("こう")));
            Assert.True(before.Words.Where(w => w.Reading.StartsWith("こう")).Select(w => w.Word).SequenceEqual(["構成"]), "移す前も、ユーザー辞書の語が出ていた");

            // 比較のための前提: 同梱の専門用語集と同じ読み込み (userTexts なし) だと、同じ語でも扱いが変わる
            var plain = new UserDictionary(null, builtIn: false);
            plain.LoadTerms([string.Join('\n', Moved.Select(m => $"{m.Reading}\t{m.Word}"))], CommonReadings.Set);
            Assert.True(plain.Split("きごうをつくる") is null, "同梱の読み込みなら、3 文字の読みは強制されない (だから user 用の読み込みが要る)");

            // 変換の入力まで通して同じ結果 (文脈の変換エンジンが区切りを変えても、移した語が先に出る)
            var typed = new[] { "kigouwotukuru ", "aikonwokurikku ", "kouseirououdousho ", "nurupogadeta " };
            foreach (var romaji in typed)
            {
                var ka = new CompositionTests.Keyboard(userDictionary: before);
                ka.Type(romaji);
                var kb = new CompositionTests.Keyboard(userDictionary: after);
                kb.Type(romaji);
                Assert.Equal(string.Join("|", ka.Host.View!.Clauses!), string.Join("|", kb.Host.View!.Clauses!), $"「{romaji}」の変換結果が同じ");
            }
        }
        finally { env.Dispose(); }
    }

    private static string Describe(List<(string Reading, string? Word)>? pieces) =>
        pieces is null ? "(なし)" : string.Join("|", pieces.Select(p => $"{p.Reading}={p.Word}"));

    [Test]
    public static void Move_BeatsBuiltInPhrases_LikeUserDictionary()
    {
        var env = Use();
        try
        {
            // 同梱の語句 (phrases.txt の おうじさま→王子様) と同じ読みの語も、ユーザー辞書にあったときと同じく、同梱の語句より先に使う
            var before = new UserDictionary(null);
            before.Add("おうじさま", "王子さま");
            Assert.Equal("王子さま", before.Lookup("おうじさま")[0], "前提: ユーザー辞書の語が先");
            var source = new UserDictionary(env.UserDict, builtIn: false);
            source.Add("おうじさま", "王子さま");
            var id = Create("語句と重なる");
            Assert.Equal<string?>(null, TermDomains.MoveFromUserDictionary(source, id, [.. source.Words], out _));
            var after = new UserDictionary(null);
            Assert.True(before.Lookup("おうじさま").SequenceEqual(after.Lookup("おうじさま")), "候補の順が同じ (自作の語 → 同梱の語句): " + string.Join(",", after.Lookup("おうじさま")));
            Assert.Equal(Describe(before.Split("おうじさまがくる")), Describe(after.Split("おうじさまがくる")), "区切りも同じ");
            Assert.Equal("王子さま", after.Split("おうじさま")![0].Word, "同梱の語句より先");
            Assert.True(after.Split("はいかぶりひめ") is { } phrase && phrase[0].Word == "灰かぶり姫", "同梱の語句は、そのまま使える");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_KeepsFileOrder_WhateverOrderTheCallerSends()
    {
        var env = Use();
        try
        {
            var before = new UserDictionary(null, builtIn: false);
            var source = new UserDictionary(env.UserDict, builtIn: false);
            foreach (var word in new[] { "古い", "中", "新しい" })
            {
                before.Add("きごう", word);
                source.Add("きごう", word);
            }
            var id = Create("順序");
            // 画面は新しい順 (表示順) で渡してくる
            var newestFirst = source.Words.Reverse().ToList();
            Assert.Equal<string?>(null, TermDomains.MoveFromUserDictionary(source, id, newestFirst, out _));
            var after = new UserDictionary(null);
            Assert.True(before.Lookup("きごう").SequenceEqual(after.Lookup("きごう")), "同じ読みの語の優先順 (新しい登録が先) が逆にならない: " + string.Join(",", after.Lookup("きごう")));
            Assert.Equal("新しい", after.Lookup("きごう")[0]);
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["古い", "中", "新しい"]), "分野のファイルには、ユーザー辞書のファイルの順に書く");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_EnablesDisabledDomain_AndRefusesWhenConfigUnreadable_AndSkipsMissingWords()
    {
        var env = Use();
        try
        {
            File.WriteAllText(env.UserDict, "きごう\t記号\nあいこん\ticon\n");
            var source = new UserDictionary(env.UserDict, builtIn: false);
            var id = Create("無効にした");
            Assert.True(TermDomains.Set(id, false), "前提: 無効にする");
            var dictionary = new UserDictionary(null);
            // 無効のままなら、移した語は使われない (だから、移すときに有効にする)
            Assert.Equal<string?>(null, TermDomains.MoveFromUserDictionary(source, id, [new UserWord("きごう", "記号"), new UserWord("ないよみ", "無い語")], out var result));
            Assert.True(TermDomains.List().Single(d => d.Id == id).Enabled && Enabled(env).Contains(id), "移すときに有効になる");
            Assert.True(dictionary.Lookup("きごう").SequenceEqual(["記号"]), "移した語がすぐ変換に使われる");
            Assert.True(result.Skipped.Count == 1 && result.Skipped[0].Word.Word == "無い語" && result.Skipped[0].Reason.Contains("もうありません"), "ユーザー辞書にもう無い語は、飛ばして理由を返す");
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["記号"]), "無い語は専門用語集に入れない");

            // config.json が読めないときは、有効にできないので、ファイルに触る前に断る
            Assert.True(TermDomains.Set(id, false), "前提: また無効にする");
            File.WriteAllText(env.Config, "{ これは壊れた設定");
            var domainFile = Path.Combine(env.Terms, $"terms-{id}.txt");
            var domainBefore = File.ReadAllText(domainFile);
            var userBefore = File.ReadAllText(env.UserDict);
            var error = TermDomains.MoveFromUserDictionary(source, id, [new UserWord("あいこん", "icon")], out result);
            Assert.True(error is { Length: > 0 } && result.AddedToDomain.Count == 0, "有効にできないので、移さない: " + error);
            Assert.Equal(domainBefore, File.ReadAllText(domainFile), "専門用語集のファイルは変わらない");
            Assert.Equal(userBefore, File.ReadAllText(env.UserDict), "ユーザー辞書も変わらない");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_RevertsEnable_WhenWritingTheDomainFails()
    {
        var env = Use();
        try
        {
            File.WriteAllText(env.UserDict, "きごう\t記号\n");
            var source = new UserDictionary(env.UserDict, builtIn: false);
            var id = Create("書けない");
            Assert.True(TermDomains.Set(id, false), "前提: 無効にする");
            byte[] broken = [0x82, 0xA0, 0x09, 0x88, 0xA4, 0x0A];
            File.WriteAllBytes(Path.Combine(env.Terms, $"terms-{id}.txt"), broken);   // 読めない = 書けない
            var userBefore = File.ReadAllText(env.UserDict);
            var error = TermDomains.MoveFromUserDictionary(source, id, [new UserWord("きごう", "記号")], out _);
            Assert.True(error is { Length: > 0 }, "書けないので失敗する");
            Assert.True(!TermDomains.List().Single(d => d.Id == id).Enabled && !Enabled(env).Contains(id), "移すために有効にした設定を、元の無効に戻す");
            Assert.Equal(userBefore, File.ReadAllText(env.UserDict), "ユーザー辞書は変わらない");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Domains_ListedAndApplied_InCreationOrder()
    {
        var env = Use();
        try
        {
            var first = Create("先に作った");
            System.Threading.Thread.Sleep(20);
            var second = Create("あとで作った");
            // ID は乱数なので、どちらが先でも、作った順に並ぶ
            var users = TermDomains.List().Where(d => d.IsUser).Select(d => d.Id).ToList();
            Assert.True(users.SequenceEqual([first, second]), "一覧は、同梱の分野のあとに、作った順");
            TermDomains.AddUserWords(first, [("きごう", "古い分野の語", "")], out _, out _);
            TermDomains.AddUserWords(second, [("きごう", "新しい分野の語", "")], out _, out _);
            Assert.Equal("新しい分野の語", new UserDictionary(null).Lookup("きごう")[0], "同じ読みでは、あとで作った分野の語が先 (ユーザー辞書の「新しい登録が先」と同じ)");
            Assert.True(TermDomains.ParseCreated(File.ReadAllText(Path.Combine(env.Terms, $"terms-{first}.txt"))) > 0, "作った日時が保存される");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Restore_AfterNameClash_KeepsContentUnderDedupedName()
    {
        var env = Use();
        try
        {
            var id = Create("医療");
            TermDomains.AddUserWords(id, [("けつあつ", "血圧", "")], out _, out _);
            Assert.Equal<string?>(null, TermDomains.DeleteUserDomain(id, out var content, out var wasEnabled));
            var other = Create("医療");   // 消したあとに、同じ名前の分野を作った
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, content, wasEnabled));
            var restored = TermDomains.List().Single(d => d.Id == id);
            Assert.True(restored.Name == "医療 (2)" && restored.Enabled && restored.Count == 1, "名前がかぶるときは「医療 (2)」で戻る (中身を失わない)");
            Assert.Equal("医療", TermDomains.List().Single(d => d.Id == other).Name, "あとで作った分野はそのまま");
            Assert.True(TermDomains.Words(id)!.Single().Entry.Word == "血圧", "語も戻る");
            // 戻せないとき (ID が使われている) は、内容を渡し直せばもう一度試せる (内容は変わらない)
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, content, wasEnabled), "同じ ID がもうあるときは成功 (取り消しを消せなくならない)");
            // 戻したが有効にできなかった (config.json が読めない) ときも成功。取り消しが何度やり直しても失敗で止まらない
            Assert.Equal<string?>(null, TermDomains.DeleteUserDomain(id, out var again, out var againEnabled));
            File.WriteAllText(env.Config, "{ これは壊れた設定");
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, again, againEnabled), "有効にできなくても、戻せていれば成功");
            Assert.True(TermDomains.List().Any(d => d.Id == id && !d.Enabled), "戻っている (無効のまま)");
            Assert.Equal<string?>(null, TermDomains.RestoreUserDomain(id, again, againEnabled), "もう一度でも成功");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(env.Config), "壊れた設定は上書きしない");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Revision_DoesNotRiseTwice_AfterOwnWrite()
    {
        var env = Use();
        try
        {
            var id = Create("版");
            _ = TermDomains.Revision;
            var before = TermDomains.Revision;
            TermDomains.AddUserWords(id, [("きごう", "記号", "")], out _, out _);
            var afterWrite = TermDomains.Revision;
            Assert.True(afterWrite != before, "自分の変更で版が進む");
            Assert.Equal(afterWrite, TermDomains.Revision, "自分の変更を、外からの変更として、もう一度作り直さない");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Size_IsCapped_ByBytes_ForAddImportAndMove()
    {
        var env = Use();
        var previous = SafeFile.MaxReadBytes;
        try
        {
            File.WriteAllText(env.UserDict, string.Concat(Enumerable.Range(0, 40).Select(i => $"よみ{i}ばん\t語{i}\n")));
            var source = new UserDictionary(env.UserDict, builtIn: false);
            var id = Create("小さい上限");
            SafeFile.MaxReadBytes = 700;   // 見出しの 3 行 + 数語で超える大きさ
            var fileBefore = File.ReadAllText(Path.Combine(env.Terms, $"terms-{id}.txt"));
            var error = TermDomains.AddUserWords(id, Enumerable.Range(0, 40).Select(i => ($"よみ{i}ばん", $"語{i}", "")), out var added, out _);
            Assert.True(error is { Length: > 0 } && error.Contains("大きく") && added.Count == 0, "上限を超える追加は、理由を返して何も書かない: " + error);
            Assert.Equal(fileBefore, File.ReadAllText(Path.Combine(env.Terms, $"terms-{id}.txt")), "ファイルは変わらない");
            var userBefore = File.ReadAllText(env.UserDict);
            SafeFile.MaxReadBytes = 700;
            error = TermDomains.MoveFromUserDictionary(source, id, [.. source.Words], out _);
            Assert.True(error is { Length: > 0 }, "上限を超える移動も断る");
            Assert.Equal(userBefore, File.ReadAllText(env.UserDict), "断ったときは、ユーザー辞書の語を消さない");
            var big = Path.Combine(env.Directory, "big.txt");
            File.WriteAllText(big, "# 名称: 大きい\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"よみ{i}ばん\t語{i}\n")));
            Assert.True(TermDomains.ImportUserDomain(big, out _, out _) is { Length: > 0 }, "上限を超える取り込みも断る");
            Assert.True(TermDomains.List().All(d => d.Name != "大きい"), "断った取り込みは分野を残さない");
        }
        finally
        {
            SafeFile.MaxReadBytes = previous;
            env.Dispose();
        }
    }

    [Test]
    public static void Export_RefusesTheUserTermsFolder()
    {
        var env = Use();
        try
        {
            var id = Create("書き出し先");
            var live = Path.Combine(env.Terms, $"terms-{id}.txt");
            var before = File.ReadAllText(live);
            Assert.True(TermDomains.ExportUserDomain(id, live, out _) is { Length: > 0 }, "使っている分野のファイルへは書き出せない");
            Assert.True(TermDomains.ExportUserDomain(id, Path.Combine(env.Terms, "別名.txt"), out _) is { Length: > 0 }, "フォルダーの中へは書き出せない");
            Assert.Equal(before, File.ReadAllText(live), "ファイルは変わらない");
            Assert.Equal<string?>(null, TermDomains.ExportUserDomain(id, Path.Combine(env.Directory, "外.txt"), out _), "ほかの場所へは書き出せる");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void SmallDataRules_ExtraColumn_SurrogateName_BackupRestore()
    {
        var env = Use();
        try
        {
            // 4 つ目の欄 (強制) と注記は、語を直しても残る
            var id = Create("欄");
            var file = Path.Combine(env.Terms, $"terms-{id}.txt");
            File.AppendAllText(file, "きごう\t記号\t注記\t強制\n");
            Assert.Equal<string?>(null, TermDomains.UpdateUserWord(id, ("きごう", "記号"), "きごう", "記號"));
            Assert.True(File.ReadAllText(file).Contains("きごう\t記號\t注記\t強制"), "注記と 4 つ目の欄が残る");

            // 名前の切り詰めは、サロゲートペアの途中で切らない
            var path = Path.Combine(env.Directory, "emoji.txt");
            File.WriteAllText(path, "# 名称: " + new string('あ', 49) + "😀😀\nきごう\t記号\n");
            Assert.Equal<string?>(null, TermDomains.ImportUserDomain(path, out var imported, out _));
            var name = TermDomains.List().Single(d => d.Id == imported).Name;
            Assert.True(name.Length <= TermDomains.MaxUserNameLength && !char.IsHighSurrogate(name[^1]), "途中で切れた文字が残らない: " + name.Length);

            // バックアップ: terms/ を 0700 で作って戻す。末尾に改行のある名前は戻さない (\z)
            Assert.True(Backup.IsRestorableName("terms/terms-user-0123abcd.txt") && !Backup.IsRestorableName("terms/terms-user-0123abcd.txt\n"), "名前の検め (\\z)");
            Assert.True(Backup.IsRestorableName("dictionaries/terms-x.txt") && !Backup.IsRestorableName("dictionaries/terms-x.txt\n"), "dictionaries/ の検めも同じ");
            var data = Path.Combine(env.Directory, "restore-target");
            Directory.CreateDirectory(data);
            var (content, count) = Backup.Create(env.Directory, "test");
            Assert.True(count > 0, "自作の分野のファイルがバックアップに入る");
            Assert.True(Backup.Restore(content, data) > 0 && File.Exists(Path.Combine(data, "terms", $"terms-{id}.txt")), "戻せる");
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(data, "terms")), "戻した terms/ は 0700");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_NewestWordWins_LikeUserDictionary()
    {
        var env = Use();
        try
        {
            var before = new UserDictionary(null, builtIn: false);
            before.Add("きごう", "記号");
            before.Add("きごう", "忌号");   // 新しい登録が先
            var source = new UserDictionary(env.UserDict, builtIn: false);
            source.Add("きごう", "記号");
            source.Add("きごう", "忌号");
            var id = Create("新しい順");
            Assert.Equal<string?>(null, TermDomains.MoveFromUserDictionary(source, id, [.. source.Words], out _));
            var after = new UserDictionary(null);
            Assert.True(before.Lookup("きごう").SequenceEqual(after.Lookup("きごう")), "同じ読みの語の順 (新しいものが先) を保つ");
            // 移したあとに分野へ足した語も、新しいものが先
            TermDomains.AddUserWords(id, [("きごう", "新記号", "")], out _, out _);
            Assert.Equal("新記号", after.Lookup("きごう")[0], "あとから足した語が先");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_SkipsInvalid_KeepsThemInUserDictionary_AndUndoRestoresBothSides()
    {
        var env = Use();
        try
        {
            // ユーザー辞書のファイルは手で書ける: 専門用語集に入れられない読み (ひらがなを含まない) も入りうる
            File.WriteAllText(env.UserDict, "# Meltype ユーザー辞書\nきごう\t記号\nabc\tローマ字の読み\nぬるぽが\tヌルポ\nほそう\t舗装\n");
            var source = new UserDictionary(env.UserDict, builtIn: false);
            var id = Create("移す先");
            TermDomains.AddUserWords(id, [("ほそう", "舗装", "")], out _, out _);   // すでに分野にある語
            var error = TermDomains.MoveFromUserDictionary(source, id, [.. source.Words], out var result);
            Assert.Equal<string?>(null, error);
            Assert.True(result.Skipped.Count == 1 && result.Skipped[0].Word.Reading == "abc" && result.Skipped[0].Reason.Length > 0, "入れられない語は理由つきで返す");
            Assert.True(source.Words.Select(w => w.Reading).SequenceEqual(["abc"]), "入れられない語はユーザー辞書に残る");
            Assert.Equal(3, result.RemovedFromUser.Count, "分野にすでにあった語もユーザー辞書からは消える");
            Assert.Equal(2, result.AddedToDomain.Count, "分野に新しく足したのは 2 語 (すでにある語は含めない)");
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["舗装", "記号", "ヌルポ"]), "分野の中身");

            // 取り消し: 分野から足した語を消し、ユーザー辞書へ元の位置に戻す
            Assert.Equal<string?>(null, TermDomains.RemoveUserWords(id, result.AddedToDomain.Select(e => (e.Reading, e.Word)), out _));
            Assert.Equal<string?>(null, source.Restore(result.RemovedFromUser));
            Assert.True(TermDomains.Words(id)!.Select(w => w.Entry.Word).SequenceEqual(["舗装"]), "分野は移す前 (すでにあった語だけ)");
            Assert.True(source.Words.Select(w => w.Word).SequenceEqual(["記号", "ローマ字の読み", "ヌルポ", "舗装"]), "ユーザー辞書は元の並び");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Move_WhenUserDictionaryCannotBeWritten_ReportsAndLosesNothing()
    {
        var env = Use();
        try
        {
            // 先に読めてから、文字コードの壊れたファイルにする (ユーザー辞書の側の書き込みだけが失敗する)
            File.WriteAllText(env.UserDict, "きごう\t記号\n");
            var source = new UserDictionary(env.UserDict, builtIn: false);
            Assert.Equal(1, source.Count, "前提: 読めている");
            var id = Create("失敗する");
            File.WriteAllBytes(env.UserDict, [0x82, 0xA0, 0x09, 0x88, 0xA4, 0x0A]);   // Shift_JIS
            var error = TermDomains.MoveFromUserDictionary(source, id, [new UserWord("きごう", "記号")], out var result);
            Assert.True(error is { Length: > 0 } && error.Contains("専門用語集には移しました"), "2 つ目の失敗をはっきり伝える: " + error);
            Assert.Equal(1, result.AddedToDomain.Count, "分野に足した語は返す (画面が取り消せる)");
            Assert.True(TermDomains.Words(id)!.Any(w => w.Entry.Word == "記号"), "語は分野に残る (消えない)");
            Assert.True(File.ReadAllBytes(env.UserDict).SequenceEqual(new byte[] { 0x82, 0xA0, 0x09, 0x88, 0xA4, 0x0A }), "読めないユーザー辞書は書き換えない");
        }
        finally { env.Dispose(); }
    }

    // ---- 書き出し・取り込み ----

    [Test]
    public static void ExportImport_RoundTrip_NameDedupe_SkippedLines()
    {
        var env = Use();
        try
        {
            var id = Create("書き出す用語");
            TermDomains.AddUserWords(id, [("きごう", "記号", "注記つき"), ("ぬるぽが", "ヌルポ", "")], out _, out _);
            var path = Path.Combine(env.Directory, "export.txt");
            Assert.Equal<string?>(null, TermDomains.ExportUserDomain(id, path, out var count));
            Assert.Equal(2, count);
            var text = File.ReadAllText(path);
            Assert.True(text.Contains("# 名称: 書き出す用語") && text.Contains("# 出典: 自作") && text.Contains("きごう\t記号\t注記つき"), "同梱と同じ形式 (名称・出典: 自作)");
            Assert.Equal("書き出す用語", TermDomains.ParseName(text), "同梱の読み方で名前が読める");
            Assert.True(TermDomains.ExportUserDomain("civil", path, out _) is { Length: > 0 }, "同梱は書き出さない");

            // 取り込み: 名前がかぶるので (2)
            Assert.Equal<string?>(null, TermDomains.ImportUserDomain(path, out var imported, out var summary));
            var domain = TermDomains.List().Single(d => d.Id == imported);
            Assert.True(domain.IsUser && domain.Enabled && domain.Name == "書き出す用語 (2)" && domain.Count == 2, "新しい分野として取り込む (名前は (2)、有効)");
            Assert.Equal("2\t0\t0\t書き出す用語 (2)", summary);
            Assert.True(TermDomains.Words(imported)!.Select(w => (w.Entry.Reading, w.Entry.Word, w.Entry.Note)).SequenceEqual(TermDomains.Words(id)!.Select(w => (w.Entry.Reading, w.Entry.Word, w.Entry.Note))), "往復しても同じ");
            Assert.Equal<string?>(null, TermDomains.ImportUserDomain(path, out var third, out _));
            Assert.Equal("書き出す用語 (3)", TermDomains.List().Single(d => d.Id == third).Name, "さらに (3)");

            // 不正な行・重複・名前の無いファイル
            var messy = Path.Combine(env.Directory, "messy.txt");
            File.WriteAllText(messy, "# 出典: 手書き\nあ\t短すぎる\nよみ\n\nきごう\t記号\nきごう\t記号\nカタカナ\t片仮名\nabc\tローマ字\n");
            Assert.Equal<string?>(null, TermDomains.ImportUserDomain(messy, out var messyId, out var messySummary));
            Assert.Equal("2\t3\t1\tmessy", messySummary);
            Assert.True(TermDomains.Words(messyId)!.Select(w => w.Entry.Reading).SequenceEqual(["きごう", "かたかな"]), "検めた語だけ入る (名前はファイル名)");

            Assert.True(TermDomains.ImportUserDomain(Path.Combine(env.Directory, "ない.txt"), out _, out _) is { Length: > 0 }, "無いファイル");
            var empty = Path.Combine(env.Directory, "empty.txt");
            File.WriteAllText(empty, "# 名称: 空\n");
            Assert.True(TermDomains.ImportUserDomain(empty, out _, out _) is { Length: > 0 }, "語が無ければ作らない");
            Assert.True(TermDomains.List().All(d => d.Name != "空"), "空の分野はできない");
            var sjis = Path.Combine(env.Directory, "sjis.txt");
            File.WriteAllBytes(sjis, [0x82, 0xA0, 0x09, 0x88, 0xA4, 0x0A]);
            Assert.True(TermDomains.ImportUserDomain(sjis, out _, out _) is { Length: > 0 }, "文字コードが読めないファイル");
        }
        finally { env.Dispose(); }
    }

    // ---- 壊れたファイル・ほかのプロセス ----

    [Test]
    public static void UnreadableDomainFile_IsNotOverwritten_AndDoesNotBreakOthers()
    {
        var env = Use();
        try
        {
            var good = Create("読めるほう");
            TermDomains.AddUserWords(good, [("きごう", "記号", "")], out _, out _);
            var bad = Create("壊れるほう");
            var file = Path.Combine(env.Terms, $"terms-{bad}.txt");
            byte[] broken = [0x82, 0xA0, 0x09, 0x88, 0xA4, 0x0A];
            File.WriteAllBytes(file, broken);
            Assert.True(TermDomains.AddUserWords(bad, [("ついか", "追加", "")], out var added, out _) is { Length: > 0 } && added.Count == 0, "読めないファイルには書かない");
            Assert.True(TermDomains.RenameUserDomain(bad, "別の名前") is { Length: > 0 }, "名前の変更も書かない");
            Assert.True(TermDomains.DeleteUserDomain(bad, out _, out _) is { Length: > 0 } && File.Exists(file), "読めないものは (戻せないので) 消さない");
            Assert.True(File.ReadAllBytes(file).SequenceEqual(broken), "元のバイト列のまま");
            var dictionary = new UserDictionary(null, builtIn: true);
            Assert.True(dictionary.Lookup("きごう").Contains("記号"), "ほかの分野は使える");
            Assert.True(TermDomains.List().Count(d => d.IsUser) == 2, "一覧にも出る");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void ExternalChange_IsPickedUp_ByList_Revision_AndRunningDictionary()
    {
        var env = Use();
        try
        {
            var running = new UserDictionary(null);                  // 動いている IME のつもり
            var revision = TermDomains.Revision;
            Assert.True(running.Lookup("きごう").Count == 0 && TermDomains.List().All(d => !d.IsUser), "最初は無い");

            // 別のプロセス (画面) が、分野を作って語を足した: ファイルと config.json を直接書く
            Directory.CreateDirectory(env.Terms);
            File.WriteAllText(Path.Combine(env.Terms, "terms-user-0badc0de.txt"), "# 名称: 外から来た\n# 出典: 自作\nきごう\t記号\n");
            File.WriteAllText(env.Config, "{ \"SettingsVersion\": 5, \"EnabledTermDomains\": [ \"user-0badc0de\" ] }");
            Assert.True(TermDomains.Revision != revision, "Revision が進む");
            Assert.True(TermDomains.List().Any(d => d.Id == "user-0badc0de" && d.IsUser && d.Count == 1 && d.Enabled), "一覧・語数・有効が変わる");
            Assert.True(running.Lookup("きごう").SequenceEqual(["記号"]), "動いている辞書の変換に出る");

            // 別のプロセスが語を足した (分野のファイルだけが変わる)
            File.AppendAllText(Path.Combine(env.Terms, "terms-user-0badc0de.txt"), "ほそう\t舗装\n");
            Assert.Equal(2, TermDomains.List().Single(d => d.Id == "user-0badc0de").Count, "語数が変わる (覚えた語数を作り直す)");
            Assert.True(running.Lookup("ほそう").SequenceEqual(["舗装"]), "足した語も出る");

            // 別のプロセスが消した
            File.Delete(Path.Combine(env.Terms, "terms-user-0badc0de.txt"));
            Assert.True(TermDomains.List().All(d => !d.IsUser), "消えれば一覧から外れる");
            Assert.True(running.Lookup("きごう").Count == 0, "変換にも出なくなる (config.json に ID が残っていても)");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void EmbeddedIds_AreNeverTouched_AndFilesWithBadNamesAreIgnored()
    {
        var env = Use();
        try
        {
            Directory.CreateDirectory(env.Terms);
            File.WriteAllText(Path.Combine(env.Terms, "terms-civil.txt"), "# 名称: 偽の土木\nにせ\t偽\n");          // 同梱と同じ ID のファイル
            File.WriteAllText(Path.Combine(env.Terms, "terms-user-xyz.txt"), "# 名称: ID が違う\nにせ\t偽\n");
            File.WriteAllText(Path.Combine(env.Terms, "notes.txt"), "にせ\t偽\n");
            var list = TermDomains.List();
            Assert.True(list.Count == 1 && list[0].Id == "civil" && list[0].Name == "土木・建設" && !list[0].IsUser, "同梱の分野はそのまま。決まった名前でないファイルは分野にならない");
            Assert.True(TermDomains.IsUserId("user-0123abcd") && !TermDomains.IsUserId("user-0123ABCD") && !TermDomains.IsUserId("civil") && !TermDomains.IsUserId("user-123"), "ID の形");
            var ids = Enumerable.Range(0, 20).Select(i => Create("名前" + i)).ToList();
            Assert.True(ids.Distinct().Count() == 20 && ids.All(i => i != "civil"), "ID は重ならない");
            Assert.True(Backup.IsRestorableName("terms/terms-user-0123abcd.txt") && !Backup.IsRestorableName("terms/terms-civil.txt") && !Backup.IsRestorableName("terms/../config.json"), "バックアップは自作の分野のファイルだけ戻す");
        }
        finally { env.Dispose(); }
    }

    [Test]
    public static void Delete_RemovesIdFromConfig_ButKeepsOthers_AndUnreadableConfigBlocksCreate()
    {
        var env = Use();
        try
        {
            File.WriteAllText(env.Config, "{ \"SettingsVersion\": 5, \"EnabledTermDomains\": [ \"civil\", \"not-a-domain\" ], \"LiveConversion\": false }");
            var id = Create("設定の掃除");
            Assert.True(Enabled(env).SequenceEqual(new[] { "civil", "not-a-domain", id }.Order(StringComparer.Ordinal)), "有効な分野に足される (ほかの値はそのまま)");
            Assert.Equal<string?>(null, TermDomains.DeleteUserDomain(id, out _, out _));
            var saved = Settings.Load(env.Config);
            Assert.True(saved.EnabledTermDomains.SequenceEqual(["civil", "not-a-domain"]) && !saved.LiveConversion, "ID だけが外れ、ほかの設定は残る");
            // config.json が読めないときは、有効にできないので作らない (ファイルを残さない)
            File.WriteAllText(env.Config, "{ これは壊れた設定");
            var before = Directory.GetFiles(env.Terms, "terms-user-*.txt").Length;
            Assert.True(TermDomains.CreateUserDomain("作れない", out _) is { Length: > 0 }, "有効にできないので作らない");
            Assert.Equal(before, Directory.GetFiles(env.Terms, "terms-user-*.txt").Length, "ファイルも残さない");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(env.Config), "壊れた設定は上書きしない");
        }
        finally { env.Dispose(); }
    }
}
