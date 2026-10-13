// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Tests;

/// <summary>正解表 (eval/cases/*.tsv) の読み込みと、3 つの比べ方 (full / ascii / nojp) のテスト。</summary>
internal static class EvalFormatTests
{
    [Test]
    public static void Parse_FourColumns()
    {
        var cases = Checks.ParseCases([
            "# カテゴリ: 混在",
            "",
            "kyouhagoogledekensaku\tきょうはgoogleでけんさく\tfull\t本家 QualityTests",
            "ollama run gemma\tollama run gemma\tascii\t計画書 1-4",
            "git push origin main\tgit push origin main\tnojp\t実機 TextEdit",
        ], "t.tsv");
        Assert.Equal(3, cases.Count, "コメント行と空行は読まない");
        Assert.Equal("full", cases[0].Mode);
        Assert.Equal("本家 QualityTests", cases[0].Source);
        Assert.Equal("ascii", cases[1].Mode);
        Assert.Equal("nojp", cases[2].Mode);
        Assert.Equal("git push origin main", cases[2].Typed);
    }

    [Test]
    public static void Parse_TwoColumns_StillWorks()
    {
        // 2 列だけの古い表 (Pull Request のチェック) は、比べ方が空のまま読む
        var cases = Checks.ParseCases(["nihongowohanasu\tにほんごをはなす", "kyouhagoogle\t今日はgoogle"]);
        Assert.Equal(2, cases.Count);
        Assert.Equal("", cases[0].Mode);
        Assert.Equal("", cases[0].Source);
        Assert.True(Checks.Judge(cases[0].Mode, cases[0].Expected, "にほんごをはなす"), "漢字の無い期待は full");
        Assert.True(Checks.Judge(cases[1].Mode, cases[1].Expected, "きょうはgoogle"), "漢字のある期待は英字の語だけ比べる");
        Assert.True(!Checks.Judge("", "hello world", "Hello world"), "2 列の古い表は変更前と同じ: 漢字が無ければ完全一致 (大小も区別)");
        Assert.True(!Checks.Judge("", "ホスティング", "ほすてぃんぐ"), "古い表はカタカナを正規化しない");
    }

    [Test]
    public static void Parse_TwoColumns_KeepsOldReading()
    {
        // 空でない行がすべて 2 列なら、# の行も読み (変更前と同じ)、件数が減らない
        var cases = Checks.ParseCases(["#a\tb", "hello\thello", "", "x\ty"]);
        Assert.Equal(3, cases.Count, "# で始まる行も 2 列なら読む");
        // 2 列でない行が混ざれば 4 列の表として読む (列の数が違えば例外)
        var threw = false;
        try { Checks.ParseCases(["hello\thello\tfull\t出典", "bad\trow"]); }
        catch (FormatException) { threw = true; }
        Assert.True(threw, "4 列の表の中の 2 列の行は黙って捨てず、例外にする");
    }

    [Test]
    public static void Parse_TwoColumns_SkipsOtherRows()
    {
        // 2 列の古い表に、3 列の行や Tab の無い見出し行が混ざっても例外にせず、2 列の行だけを数える
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["# 見出し", "hello\thello", "a\tb\tc", "bye\tbye", "見出しだけの行"]);
            var cases = Checks.ParseCases(File.ReadAllLines(path), "t.tsv");
            Assert.Equal(2, cases.Count, "2 列の行の数と同じ");
            Assert.Equal("hello", cases[0].Typed);
            Assert.Equal("bye", cases[1].Typed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Parse_KanaTypedWithMode_Throws()
    {
        var threw = false;
        try { Checks.ParseCases(["しゃおみ\tXiaomi\tfull\t出典"]); }
        catch (FormatException) { threw = true; }
        Assert.True(threw, "かなで打つ行に比べ方を付けると、使われないのに合格に見えるので例外にする");
        Assert.Equal(1, Checks.ParseCases(["しゃおみ\tXiaomi\t\t出典"]).Count, "比べ方が空なら読める");
    }

    [Test]
    public static void Parse_UnknownMode_Throws()
    {
        var threw = false;
        try { Checks.ParseCases(["abc\tabc\tpartial\t出典"], "t.tsv"); }
        catch (FormatException) { threw = true; }
        Assert.True(threw, "知らない比べ方は黙って full にせず、例外にする");
    }

    [Test]
    public static void Parse_AsciiWithoutWords_Throws()
    {
        var threw = false;
        try { Checks.ParseCases(["kyou\tきょう\tascii\t出典"]); }
        catch (FormatException) { threw = true; }
        Assert.True(threw, "比べる英字が無い ascii は、何でも合格になるので例外にする");
    }

    [Test]
    public static void Full_NormalizesKatakanaAndCase()
    {
        Assert.True(Checks.Judge("full", "ホスティング", "ほすてぃんぐ"), "期待にカタカナがあれば、ひらがなに直して比べる");
        Assert.True(Checks.Judge("full", "OEMのホスティング", "oemのほすてぃんぐ"), "期待にカタカナがあれば、英字の大小も無視する");
        Assert.True(!Checks.Judge("full", "Tokyo", "tokyo"), "期待にカタカナが無ければ、英字の大小は区別する (本家と同じ)");
        Assert.True(Checks.Judge("full", "きょうはgoogleでけんさく", "きょうはgoogleでけんさく"), "同じなら合格");
        Assert.True(!Checks.Judge("full", "きょうはgoogleでけんさく", "きょうはごおgleでけんさく"), "違えば不合格");
        Assert.True(!Checks.Judge("full", "すし", "sushi"), "かなと英字は別");
    }

    [Test]
    public static void Ascii_ComparesOnlyEnglishWords()
    {
        Assert.True(Checks.Judge("ascii", "今日は google で検索", "きょうは google で けんさく"), "英字の語が同じなら、日本語の部分は問わない");
        Assert.True(!Checks.Judge("ascii", "今日は google で検索", "きょうは ごおgle で けんさく"), "google が ごおgle に割れたら不合格");
        Assert.True(Checks.Judge("ascii", "Claude Code でかく", "claude code でかく"), "大小は無視する");
        Assert.True(!Checks.Judge("ascii", "ollama run gemma", "ollama るん gemma"), "語が日本語になれば不合格");
        Assert.True(!Checks.Judge("ascii", "今日はgoogleで検索", "きょうはごおgleでけんさく"), "google が割れたら不合格");
        Assert.True(Checks.Judge("ascii", "setup.exeを実行", "setup.exeをじっこう"), "記号 (.) を含む語は 1 つの語");
        Assert.True(!Checks.Judge("ascii", "gpt-oss を使う", "gpt-おっsをつかう"), "gpt-oss が割れたら不合格");
        Assert.True(!Checks.Judge("ascii", "gemma4-27b を使う", "gemma4ー27bをつかう"), "- が ー になったら不合格");
    }

    [Test]
    public static void Nojp_RequiresNoJapaneseCharacters()
    {
        Assert.True(Checks.Judge("nojp", "git push origin main", "git push origin main"), "英文のまま残れば合格");
        Assert.True(!Checks.Judge("nojp", "git push origin main", "git push origin まいん"), "ひらがなが混ざれば不合格");
        Assert.True(!Checks.Judge("nojp", "git push", "git プッシュ"), "カタカナが混ざれば不合格");
        Assert.True(!Checks.Judge("nojp", "git push", "git 今日"), "漢字が混ざれば不合格");
        Assert.True(!Checks.Judge("nojp", "a-b", "a－b"), "全角記号 (U+FF0D) が混ざれば不合格");
        Assert.True(!Checks.Judge("nojp", "a,b", "a、b"), "句読点 (U+3001) が混ざれば不合格");
        Assert.True(!Checks.Judge("nojp", "git", "git｡"), "半角カナ・記号 (U+FF61) が混ざれば不合格");
    }

    [Test]
    public static void ExpectAll_WritesFileAndFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"meltype-eval-format-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.tsv"), "hello\thello\tfull\tテスト\n");
            File.WriteAllText(Path.Combine(dir, "b.tsv"), "hello\thello\tnojp\tテスト\n# コメント\n");
            var output = Path.Combine(dir, "out.json");
            Checks.ExpectAll(dir, output);
            using var json = JsonDocument.Parse(File.ReadAllText(output));
            var items = json.RootElement.EnumerateArray().ToList();
            Assert.Equal(2, items.Count, "2 ファイル 1 件ずつ");
            Assert.Equal("a.tsv", items[0].GetProperty("file").GetString());
            Assert.Equal("b.tsv", items[1].GetProperty("file").GetString());
            foreach (var key in new[] { "typed", "expected", "mode", "source", "actual", "ok" })
                Assert.True(items[0].TryGetProperty(key, out _), $"{key} がある");
            Assert.True(items[0].GetProperty("ok").GetBoolean() && items[1].GetProperty("ok").GetBoolean(), "hello は英語のまま");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
