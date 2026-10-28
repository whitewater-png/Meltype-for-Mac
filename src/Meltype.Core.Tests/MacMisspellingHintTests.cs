// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// Mac 版の「もしかして」の案内: Swift は本体が返す JSON の "suggestion" を読んで、入力位置の行の上に出す
/// (CompositionView.suggestion・SuggestionHint.misspelling)。Swift に自動テストは無いので、Swift が頼る JSON の約束をここで確かめる。
/// </summary>
internal static class MacMisspellingHintTests
{
    private static MeltypeSession Create() =>
        new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Misspellings = MisspellingDictionary.Load(null) }, () => new Settings());

    private static SessionResult Type(MeltypeSession session, string text)
    {
        SessionResult? last = null;
        foreach (var c in text)
            last = session.HandleKey(char.ToUpperInvariant(c), c, char.IsUpper(c), false, false, false); // 英小文字だけを打つ (仮想キーは大文字の文字コード)
        return last!;
    }

    [Test]
    public static void Json_CarriesSuggestion_WhileMisspelled()
    {
        var session = Create();
        var json = Type(session, "buresureddo").ToJson();
        Assert.True(json.Contains("\"suggestion\":\"もしかして: ブレスレット　<Tab>で修正\"", StringComparison.Ordinal), json);
        var kudasia = Create();
        var json2 = Type(kudasia, "kudasia").ToJson();
        Assert.True(json2.Contains("\"suggestion\":\"もしかして: クダサイ", StringComparison.Ordinal), json2);
    }

    [Test]
    public static void Json_SuggestionIsNull_WhenNoMisspelling_AndAfterTab()
    {
        var plain = Create();
        var json = Type(plain, "kudasai").ToJson();
        Assert.True(json.Contains("\"suggestion\":null", StringComparison.Ordinal), json);

        var session = Create();
        Type(session, "kudasia");
        var fixedJson = session.HandleKey(VirtualKeys.Tab, '\t', false, false, false, false).ToJson();
        Assert.True(fixedJson.Contains("\"suggestion\":null", StringComparison.Ordinal), "Tab で直したら案内は消える: " + fixedJson);
        Assert.True(fixedJson.Contains("\"text\":\"ください\"", StringComparison.Ordinal), "Tab で ください に直る: " + fixedJson);
    }
}
