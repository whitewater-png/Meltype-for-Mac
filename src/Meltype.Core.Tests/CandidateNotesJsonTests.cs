// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// P39 (本家 #86 の考え方): 候補ごとの注釈 (英訳・全角・半角) を JSON の view.notes に出す。
/// 候補 (view.candidates) と同じ長さ・同じ順で、注釈の無い候補は null。注釈が 1 つも無いときは notes のキーごと出さない
/// (Swift が未知のキーを無視でき、版数 meltype_abi_version を上げずに済む)。
/// </summary>
internal static class CandidateNotesJsonTests
{
    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? "(null)" : e.GetString()!).ToArray();

    private static SessionResult WithNotes(IReadOnlyList<string> candidates, IReadOnlyList<string?>? notes) =>
        new(true, [], new CompositionView("x", candidates, 0, true, "h", ["x"], 0, notes));

    [Test]
    public static void Json_NotesAlignWithCandidates_NullForNoNote()
    {
        var json = WithNotes(["a", "b", "c"], ["英訳", null, "全角"]).ToJson();
        Assert.True(json.EndsWith(",\"notes\":[\"英訳\",null,\"全角\"]}}"), "notes は view の最後のキー。候補と同じ順: " + json);
        using var document = JsonDocument.Parse(json);
        var view = document.RootElement.GetProperty("view");
        Assert.Equal(Strings(view.GetProperty("candidates")).Length, Strings(view.GetProperty("notes")).Length, "候補と同じ長さ");
        Assert.Equal("英訳,(null),全角", string.Join(",", Strings(view.GetProperty("notes"))));
    }

    [Test]
    public static void Json_OmitsNotesWhenThereIsNone()
    {
        Assert.True(!WithNotes(["a", "b"], null).ToJson().Contains("\"notes\""), "Notes が null なら出さない");
        Assert.True(!WithNotes(["a", "b"], [null, null]).ToJson().Contains("\"notes\""), "全部 null なら出さない");
        Assert.True(!WithNotes(["a", "b"], []).ToJson().Contains("\"notes\""), "空なら出さない");
        Assert.True(!WithNotes([], ["余り"]).ToJson().Contains("\"notes\""), "候補が無ければ注釈も出さない (候補の範囲の外は数えない)");
        var plain = new SessionResult(true, [], new CompositionView("x", ["y"], 0, true, "h", ["x"], 0)).ToJson();
        Assert.True(plain.EndsWith("\"selectedPrediction\":-1}}"), "注釈が無ければ今までと同じ形: " + plain);
    }

    [Test]
    public static void Json_NotesAreCutOrPaddedToTheCandidates_AndEscaped()
    {
        var shorter = WithNotes(["a", "b", "c"], [null, "半角"]).ToJson();
        Assert.True(shorter.EndsWith(",\"notes\":[null,\"半角\",null]}}"), "足りない分は null: " + shorter);
        var longer = WithNotes(["a", "b"], [null, "全角", "余り"]).ToJson();
        Assert.True(longer.EndsWith(",\"notes\":[null,\"全角\"]}}"), "候補より多い分は捨てる: " + longer);
        var escaped = WithNotes(["a"], ["\"引用\"\\\n"]).ToJson();
        using var document = JsonDocument.Parse(escaped);
        Assert.Equal("\"引用\"\\\n", document.RootElement.GetProperty("view").GetProperty("notes")[0].GetString(), "引用符・バックスラッシュ・改行は JSON として正しく書く");
    }

    private static MeltypeSession CreateWithTranslations() => new(
        CompositionTests.Detector,
        new CompositionTests.FakeConverter(),
        new CompositionOptions { Translations = TranslationDictionary.Parse("川\tn\triver,stream\n") },
        () => new Settings());

    private static SessionResult TypeAll(MeltypeSession session, string text, Func<char, int> vk, Func<char, char?> ch)
    {
        SessionResult? last = null;
        foreach (var c in text) last = session.HandleKey(vk(c), ch(c), char.IsAsciiLetterUpper(c), false, false, false);
        return last!;
    }

    private static SessionResult Type(MeltypeSession session, string text) => TypeAll(session, text,
        c => c == ' ' ? VirtualKeys.Space : char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : 0x07,
        c => c == ' ' ? null : c);

    [Test]
    public static void Session_TranslationCandidatesCarryEiyakuNotesInTheJson()
    {
        // かわ → 川 のあとに river / stream (英訳) が並ぶ。JSON の notes は候補と同じ位置で「英訳」。
        var result = Type(CreateWithTranslations(), "kawa ");
        using var document = JsonDocument.Parse(result.ToJson());
        var view = document.RootElement.GetProperty("view");
        var candidates = Strings(view.GetProperty("candidates"));
        var notes = Strings(view.GetProperty("notes"));
        Assert.Equal(candidates.Length, notes.Length, "候補と同じ長さ: " + string.Join(",", candidates));
        Assert.Equal("(null)", notes[Array.IndexOf(candidates, "川")], "日本語の候補に注釈は付かない");
        Assert.Equal("英訳", notes[Array.IndexOf(candidates, "river")], "river の位置に 英訳");
        Assert.Equal("英訳", notes[Array.IndexOf(candidates, "stream")], "stream の位置に 英訳");
        Assert.Equal(2, notes.Count(n => n == "英訳"), "英訳は 2 つだけ: " + string.Join(",", notes));
    }

    [Test]
    public static void Session_SymbolCandidatesCarryHalfAndFullWidthNotesInTheJson()
    {
        // 記号は半角・全角の両方が候補に並ぶ (# と ＃ は見分けにくい)。
        var result = Type(new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings()), "@ ");
        using var document = JsonDocument.Parse(result.ToJson());
        var view = document.RootElement.GetProperty("view");
        var candidates = Strings(view.GetProperty("candidates"));
        var notes = Strings(view.GetProperty("notes"));
        var detail = string.Join(",", candidates) + " / " + string.Join(",", notes);
        Assert.Equal("半角", notes[Array.IndexOf(candidates, "@")], detail);
        Assert.Equal("全角", notes[Array.IndexOf(candidates, "＠")], detail);
    }

    [Test]
    public static void Session_NoNotesWhileTypingOrWithoutTranslations()
    {
        var typing = Type(CreateWithTranslations(), "kawa");
        Assert.True(!typing.ToJson().Contains("\"notes\""), "変換前 (候補の一覧が無い) は notes を出さない: " + typing.ToJson());
        // 英訳の辞書が無ければ、日本語だけの候補に 英訳 は付かない (全角・半角が両方ある記号だけが対象)
        var result = Type(new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings()), "kawa ");
        using var document = JsonDocument.Parse(result.ToJson());
        var view = document.RootElement.GetProperty("view");
        var candidates = Strings(view.GetProperty("candidates"));
        var notes = view.TryGetProperty("notes", out var n) ? Strings(n) : [];
        Assert.True(!notes.Contains("英訳"), "辞書が無ければ 英訳 は出ない: " + string.Join(",", candidates));
    }

    // ---- R26 (独立査読) の指摘: 候補が 9 個以上のとき・2 文節目以降を選んだときも、notes は候補と同じ位置 ----

    private static MeltypeSession CreateWith(IKanjiConverter converter, Func<string, IReadOnlyList<string>>? more = null) => new(
        CompositionTests.Detector,
        converter,
        new CompositionOptions
        {
            Translations = TranslationDictionary.Parse("川\tn\triver,stream\n橋\tn\tbridge\n雨\tn\train,shower,drizzle\n"),
            MoreCandidates = more,
        },
        () => new Settings());

    /// <summary>読み全体を決まった文節に分ける変換エンジン (かわ|はし|あめ|くも → 川|橋|雨|雲)。</summary>
    private sealed class FourClauseConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "かわはしあめくも"
                ? [new("かわ", "川"), new("はし", "橋"), new("あめ", "雨"), new("くも", "雲")]
                : null;
    }

    /// <summary>候補 1 つの注釈を、位置に頼らず候補の文字から決める (英訳の語 → 英訳、打った英字 → 半角、その全角 → 全角、それ以外 → なし)。</summary>
    private static string Expected(string candidate, string[] translations, string raw) =>
        translations.Contains(candidate) ? "英訳"
        : candidate == raw ? "半角"
        : candidate == CompositionText.ToFullWidth(raw) ? "全角"
        : "(null)";

    [Test]
    public static void Session_NotesStayAlignedWhenThereAreMoreThanEightCandidates()
    {
        // 変換エンジンの候補の一覧 (MoreCandidates) で候補が 46 個以上になる。英訳・半角・全角は一覧の後ろ (9 個目より先) に並ぶ。
        var session = CreateWith(new CompositionTests.FakeConverter(), reading => Enumerable.Range(0, 40).Select(i => "候補" + i).ToList());
        var result = Type(session, "kawa ");
        using var document = JsonDocument.Parse(result.ToJson());
        var view = document.RootElement.GetProperty("view");
        var candidates = Strings(view.GetProperty("candidates"));
        var notes = Strings(view.GetProperty("notes"));
        var detail = string.Join(",", candidates) + " / " + string.Join(",", notes);
        Assert.True(candidates.Length > 40, "MoreCandidates の 40 個が足されて 9 個を超える: " + candidates.Length);
        Assert.Equal(candidates.Length, notes.Length, "候補が 9 個以上でも notes は候補と同じ長さ: " + detail);
        Assert.True(Array.IndexOf(candidates, "river") > 8, "英訳は 9 個目より後ろ: " + detail);
        for (var i = 0; i < candidates.Length; i++)
            Assert.Equal(Expected(candidates[i], ["river", "stream"], "kawa"), notes[i], $"{i} 番目 {candidates[i]}: " + detail);
        Assert.Equal(2, notes.Count(n => n == "英訳"), detail);
        Assert.Equal(1, notes.Count(n => n == "半角"), detail);
        Assert.Equal(1, notes.Count(n => n == "全角"), detail);
    }

    [Test]
    public static void Session_NotesFollowTheSelectedClauseNotTheFirstOne()
    {
        // 4 文節 川|橋|雨|雲 を →で 1 つずつ選ぶ。文節ごとに候補の数・英訳の有無・並びが違う (英訳が付く文節 2 つ・付かない文節 1 つ・奇数/偶数の番号)。
        var clauses = new (string Raw, string[] Translations)[] { ("kawa", ["river", "stream"]), ("hashi", ["bridge"]), ("ame", ["rain", "shower", "drizzle"]), ("kumo", []) };
        var session = CreateWith(new FourClauseConverter(), reading => Enumerable.Range(0, 3).Select(i => reading + "の候補" + i).ToList());
        var result = Type(session, "kawahashiamekumo ");
        for (var clause = 0; clause < clauses.Length; clause++)
        {
            if (clause > 0) result = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
            using var document = JsonDocument.Parse(result.ToJson());
            var view = document.RootElement.GetProperty("view");
            Assert.Equal(clause, view.GetProperty("selectedClause").GetInt32(), "選んでいる文節");
            var candidates = Strings(view.GetProperty("candidates"));
            var notes = Strings(view.GetProperty("notes"));
            var detail = $"文節 {clause}: " + string.Join(",", candidates) + " / " + string.Join(",", notes);
            Assert.Equal(candidates.Length, notes.Length, "候補と同じ長さ: " + detail);
            for (var i = 0; i < candidates.Length; i++)
                Assert.Equal(Expected(candidates[i], clauses[clause].Translations, clauses[clause].Raw), notes[i], $"{i} 番目 {candidates[i]}: " + detail);
            Assert.Equal(clauses[clause].Translations.Length, notes.Count(n => n == "英訳"), detail);
            Assert.Equal(1, notes.Count(n => n == "半角"), detail);
            Assert.Equal(1, notes.Count(n => n == "全角"), detail);
        }
    }
}
