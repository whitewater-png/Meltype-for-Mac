// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>専門用語集の分野ごとの有効/無効 (TermDomains)。既定 OFF・切り替えの即時反映・config.json への保存・壊れた設定を上書きしない。</summary>
internal static class TermDomainTests
{
    private const string Civil = "# 名称: 土木・建設\n# 出典: テスト\nこうぞうぶつ\t構造物\nほそう\t舗装\n";
    private const string Medical = "# 出典: テスト\n# 名称: 医療\nけつあつそくてい\t血圧測定\n";
    private const string NoName = "# 出典: テスト\nむめいのごい\t無名の語\n";

    /// <summary>偽の分野 (civil / medical / noname) と一時的な config.json に差し替える。戻り値の関数で元に戻す。</summary>
    private static string Use(out Action restore)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        var previousPath = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        TermDomains.ConfigPath = () => path;
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.Source = () => [("civil", () => Civil), ("medical", () => Medical), ("noname", () => NoName)];
        TermDomains.Reset();
        restore = () =>
        {
            TermDomains.ConfigPath = previousPath;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return path;
    }

    [Test]
    public static void Domains_DefaultOff_NoTermsAppear()
    {
        Use(out var restore);
        try
        {
            var dictionary = new UserDictionary(null);
            Assert.Equal(0, dictionary.TermCount, "既定はすべて OFF: 専門語は入らない");
            Assert.Equal(0, dictionary.Lookup("こうぞうぶつ").Count, "出ない");
            Assert.True(dictionary.Split("こうぞうぶつをつくる") is null, "強制もしない");
            Assert.True(TermDomains.List().All(d => !d.Enabled), "一覧もすべて OFF");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_OnAndOff_ReachAllInstances()
    {
        var path = Use(out var restore);
        try
        {
            var shared = UserDictionary.Shared(Path.Combine(Path.GetDirectoryName(path)!, "userdict.txt"));
            var alone = new UserDictionary(null);
            var off = new UserDictionary(null, builtIn: false);
            var before = shared.Version;
            Assert.True(TermDomains.Set("civil", true), "保存できる");
            Assert.Equal("構造物", shared.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "共有インスタンスにすぐ反映");
            Assert.Equal("構造物", alone.Lookup("こうぞうぶつ").SingleOrDefault() ?? "", "共有しないインスタンスにも反映");
            Assert.True(shared.Version != before, "Version が進む (変換結果のキャッシュを捨てる)");
            Assert.Equal(0, off.Lookup("こうぞうぶつ").Count, "builtIn: false は分野の設定に従わない");
            Assert.True(alone.Split("こうぞうぶつをつくる")![0].Word == "構造物", "強制型として効く");
            Assert.Equal(0, alone.Lookup("けつあつそくてい").Count, "有効にしていない分野は出ない");
            // 新しく作ったインスタンスにも
            Assert.Equal(2, new UserDictionary(null).TermCount, "新しい辞書にも有効な分野だけ入る");

            Assert.True(TermDomains.Set("medical", true), "確認");
            Assert.Equal(3, alone.TermCount, "複数の分野をまとめて持つ");
            Assert.Equal("血圧測定", alone.Lookup("けつあつそくてい").Single(), "確認");

            var versionOn = alone.Version;
            Assert.True(TermDomains.Set("civil", false), "確認");
            Assert.Equal(0, alone.Lookup("こうぞうぶつ").Count, "OFF に戻すと出ない");
            Assert.Equal(0, shared.Lookup("こうぞうぶつ").Count, "共有インスタンスも");
            Assert.True(alone.Version != versionOn, "Version が進む");
            Assert.Equal(1, alone.TermCount, "残りは医療だけ");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_SavedToConfig_AndReloaded()
    {
        var path = Use(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"PredictionMinLength\": 4 }");
            Assert.True(TermDomains.Set("medical", true) && TermDomains.Set("civil", true), "保存できる");
            var saved = Settings.Load(path);
            Assert.Equal("civil,medical", string.Join(",", saved.EnabledTermDomains), "有効な分野の ID が config.json に保存される");
            Assert.True(!saved.LiveConversion && saved.PredictionMinLength == 4, "ほかの設定はそのまま");
            // 次の起動 (覚えた状態を捨てて読み直す)
            TermDomains.Reset();
            Assert.Equal("civil,medical", string.Join(",", TermDomains.List().Where(d => d.Enabled).Select(d => d.Id)), "再起動後も有効");
            Assert.Equal(3, new UserDictionary(null).TermCount, "辞書にも入る");
            Assert.True(TermDomains.Set("civil", false) && TermDomains.Set("medical", false), "確認");
            Assert.Equal(0, Settings.Load(path).EnabledTermDomains.Count, "OFF に戻すと一覧から消える");
            Assert.True(!new Settings().EnabledTermDomains.Any(), "Settings の既定は空");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_UnreadableConfig_IsNotOverwritten()
    {
        var path = Use(out var restore);
        try
        {
            File.WriteAllText(path, "{ これは壊れた設定");
            Assert.True(!TermDomains.Set("civil", true), "読めない設定には保存しない");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(path), "元のファイルはそのまま");
            Assert.True(TermDomains.List().All(d => !d.Enabled), "状態も変えない (OFF のまま)");
            Assert.Equal(0, new UserDictionary(null).TermCount, "語も入らない");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_UnknownIds_AreIgnored_ButKept()
    {
        var path = Use(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"EnabledTermDomains\": [\"civil\", \"nosuch\"] }");
            Assert.Equal("civil", string.Join(",", TermDomains.List().Where(d => d.Enabled).Select(d => d.Id)), "未知の ID は無視する");
            Assert.Equal(2, new UserDictionary(null).TermCount, "既知の分野だけ読む");
            Assert.True(!TermDomains.Set("nosuch", true), "未知の ID は切り替えられない");
            Assert.True(TermDomains.Set("medical", true), "確認");
            Assert.True(Settings.Load(path).EnabledTermDomains.Contains("nosuch"), "未知の ID は保存し直しても失わない (新しい版で増えた分野かもしれない)");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_NameAndCount()
    {
        Use(out var restore);
        try
        {
            var list = TermDomains.List();
            Assert.Equal("civil,medical,noname", string.Join(",", list.Select(d => d.Id)), "ID の昇順");
            Assert.Equal("土木・建設", list[0].Name, "名称の行");
            Assert.Equal(2, list[0].Count, "語数");
            Assert.Equal("医療", list[1].Name, "名称の行は出典の後ろでもよい");
            Assert.Equal(1, list[1].Count, "確認");
            Assert.Equal("noname", list[2].Name, "名称の行が無ければ ID");
            Assert.True(list.All(d => !d.Enabled), "一覧を取っても有効にはならない");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_Name_ParsingAndFallback()
    {
        Assert.Equal("土木", TermDomains.ParseName("# 名称: 土木\n") ?? "", "基本");
        Assert.Equal("土木", TermDomains.ParseName("\uFEFF#名称：土木\r\n") ?? "", "BOM・全角コロン・スペース無し・CRLF");
        Assert.Equal("医療", TermDomains.ParseName("# 出典: x\n\n# 名称: 医療\nあいうえ\tお\n") ?? "", "空行をはさんでもよい");
        Assert.Equal("a b", TermDomains.ParseName("# 名称: a\tb\n") ?? "", "名称の中の Tab は空白にする");
        Assert.True(TermDomains.ParseName("# 名称:\n# 出典: x\n") is null, "空の名称は無し (ID にフォールバック)");
        Assert.True(TermDomains.ParseName("# 名称:   \n") is null, "空白だけの名称も無し");
        Assert.True(TermDomains.ParseName("あいうえ\t語\n# 名称: 遅すぎる\n") is null, "語の行より後ろの名称は読まない");
        Assert.True(TermDomains.ParseName("# 名称の説明: x\n") is null, "名称で始まる別の見出しは名称ではない");
        Use(out var restore);
        try
        {
            TermDomains.Source = () => [("blank", () => "# 名称:\n# 出典: x\nかなのよみ\t語\n")];
            TermDomains.Reset();
            Assert.Equal("blank", TermDomains.List().Single().Name, "空の名称は ID が名前になる");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_ConfigBrokenAfterEnabling_KeepsEnabled()
    {
        var path = Use(out var restore);
        try
        {
            Assert.True(TermDomains.Set("civil", true), "確認");
            var dictionary = new UserDictionary(null);
            File.WriteAllText(path, "{ 後から壊れた");
            Assert.Equal(2, dictionary.TermCount, "壊れても、いま有効な分野はそのまま");
            Assert.True(TermDomains.List().Single(d => d.Id == "civil").Enabled, "一覧も有効のまま");
            Assert.True(!TermDomains.Set("civil", false), "壊れた設定には保存できない");
            Assert.Equal("{ 後から壊れた", File.ReadAllText(path), "元のファイルはそのまま");
            Assert.True(TermDomains.List().Single(d => d.Id == "civil").Enabled, "切り替えに失敗しても状態は変わらない");
        }
        finally { restore(); }
    }

    [Test]
    public static void Domains_FfiFormat_ReplacesTabsAndNewlines()
    {
        var text = TermDomains.FormatForFfi([new TermDomain("a", "名\t称\n二行目", 12, true), new TermDomain("b", "医療", 0, false)]);
        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length, "1 分野 1 行 (名称の改行は空白にする)");
        Assert.Equal("a\t名 称 二行目\t12\t1", lines[0], "ID Tab 名称 Tab 語数 Tab 有効");
        Assert.Equal("b\t医療\t0\t0", lines[1], "OFF は 0");
        Assert.True(lines.All(l => l.Split('\t').Length == 4), "どの行も 4 欄");
    }

    [Test]
    public static void Domains_EmbeddedFiles_HaveNames()
    {
        // 同梱の本物のファイル: 雛形を除いて、すべて名称の行がある・既定は全部 OFF
        var list = TermDomains.List();
        Assert.True(list.Count > 0, "同梱の分野が 1 つ以上ある (無ければ、このテストは何も確かめていないことになる)");
        Assert.True(!list.Any(d => d.Id == "template"), "雛形は分野に出さない");
        Assert.True(list.All(d => d.Name != d.Id && d.Count > 0 && !d.Enabled), "同梱の分野には名称と語がある (既定は OFF)");
    }
}
