// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #1029 の (2): 英語で確定して空白が入った直後に Space をもう一度押したら、空白を取って直前の語だけを日本語として読み直す設定 (DoubleSpaceToJapanese)。
/// 既定は OFF。OFF のときは 2 回目の Space がそのまま空白になる (今までと同じ)。
/// </summary>
internal static class DoubleSpaceTests
{
    /// <summary>入力欄のつもり。Swift と同じく、消す前に中身を確かめ、アプリに渡った Space は空白として足す。</summary>
    private sealed class Editor
    {
        public MeltypeSession Session { get; }
        public string Doc { get; private set; } = "";
        public SessionResult? Last { get; private set; }

        public Editor(bool on)
        {
            var options = new CompositionOptions { DoubleSpaceToJapanese = () => on };
            Session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings());
        }

        private string? Before => Session.IsComposing ? null : Doc.Length > 20 ? Doc[^20..] : Doc;

        public SessionResult Key(int vk, char? ch = null, bool shift = false)
        {
            var result = Session.HandleKey(vk, ch, shift, false, false, false, Before);
            foreach (var edit in result.Commits)
            {
                if (edit.DeleteBefore > 0)
                {
                    Assert.True(Doc.Length >= edit.DeleteBefore && (edit.Original is null || Doc.EndsWith(edit.Original, StringComparison.Ordinal)), $"消す文字が入力欄と違う: Doc={Doc} edit={edit}");
                    Doc = Doc[..^edit.DeleteBefore];
                }
                Doc += edit.Text;
            }
            if (!result.Consumed && vk == VirtualKeys.Space) Doc += " ";
            if (!result.Consumed && vk == VirtualKeys.Back && Doc.Length > 0) Doc = Doc[..^1];
            return Last = result;
        }

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (c == '\n') Key(VirtualKeys.Return);
                else if (c == ' ') Key(VirtualKeys.Space);
                else if (c == '\b') Key(VirtualKeys.Back);
                else Key(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c, c, char.IsAsciiLetterUpper(c));
            }
        }
    }

    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使う。</summary>
    private static T WithWords<T>(Func<T> body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { return body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    // ---- 設定そのもの ----

    [Test]
    public static void Setting_DefaultsToOff_AndIsInCatalog()
    {
        Assert.Equal(false, new Settings().DoubleSpaceToJapanese);
        Assert.Equal(false, new CompositionOptions().DoubleSpaceToJapanese());
        var item = Item(MacSettingsCatalog.ToJson(new Settings()), nameof(Settings.DoubleSpaceToJapanese));
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.Equal(false, item.GetProperty("value").GetBoolean());
        Assert.Equal("入力", item.GetProperty("group").GetString());
        var attribute = typeof(Settings).GetProperty(nameof(Settings.DoubleSpaceToJapanese))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    // ---- OFF: 今までどおり (2 回目の Space はそのまま空白) ----

    [Test]
    public static void Off_SecondSpaceIsJustASpace()
    {
        WithWords(() =>
        {
            var e = new Editor(on: false);
            e.Type("can ");
            Assert.Equal("can ", e.Doc, "前提: 英語は Space で確定して空白");
            var second = e.Key(VirtualKeys.Space);
            Assert.True(!second.Consumed && !second.Toggle, "OFF: 2 回目の Space はアプリに渡す");
            Assert.Equal("can  ", e.Doc);
            Assert.True(!e.Session.IsComposing, "変換ボックスは開かない");
            return 0;
        });
    }

    // ---- ON ----

    [Test]
    public static void On_SecondSpaceRereadsLastWordAsJapanese()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can ");
            Assert.Equal("can ", e.Doc, "前提: 1 回目の Space は今までどおり英語で確定して空白 (確定を保留しない)");
            var second = e.Key(VirtualKeys.Space);
            Assert.True(second.Consumed && second.Toggle, "2 回目の Space は使う (トグル由来の印つき)");
            Assert.Equal("", e.Doc, "語も確定の空白も取る");
            Assert.True(e.Session.IsComposing, "読みの変換ボックスが開く");
            Assert.Equal("かん", e.Last?.View?.Text ?? "", "打った英字を読みにした変換ボックス");
            // 続く Space は通常の候補送り
            var third = e.Key(VirtualKeys.Space);
            Assert.True(third.Consumed && e.Session.IsComposing, "その後の Space は候補送り (空白は入らない)");
            Assert.Equal("", e.Doc);
            return 0;
        });
    }

    [Test]
    public static void On_Enter_CommitsWithoutTrailingSpace_AndLearnsJapanese()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can  \n");
            Assert.Equal("かん", e.Doc, "Enter で確定。取った空白は戻さない");
            return 0;
        });
    }

    [Test]
    public static void On_Esc_RestoresWordAndSpace()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can  ");
            Assert.Equal("", e.Doc);
            e.Key(VirtualKeys.Escape);
            Assert.Equal("can ", e.Doc, "Esc で語も空白も元に戻る");
            Assert.True(!e.Session.IsComposing, "変換ボックスは開かない");
            return 0;
        });
    }

    [Test]
    public static void On_SpaceAfterEsc_IsJustASpace()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can  ");
            e.Key(VirtualKeys.Escape);
            Assert.Equal("can ", e.Doc, "前提: Esc で戻った");
            var next = e.Key(VirtualKeys.Space);
            Assert.True(!next.Consumed && !next.Toggle, "戻したあとの Space は読み直さない");
            Assert.Equal("can  ", e.Doc, "普通の空白が入る");
            Assert.True(!e.Session.IsComposing, "変換ボックスは開かない");
            return 0;
        });
    }

    [Test]
    public static void On_InTheMiddleOfASentence_OnlyTheLastWordIsReread()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("I like can ");
            Assert.Equal("I like can ", e.Doc, "前提: 英文は Space で確定して空白");
            e.Key(VirtualKeys.Space);
            Assert.Equal("I like ", e.Doc, "それ以前の英単語は英語のまま、空白は 1 つ前の語の後に残る");
            Assert.True(e.Session.IsComposing, "変換ボックスが開く");
            Assert.Equal("かん", e.Last?.View?.Text ?? "");
            return 0;
        });
    }

    [Test]
    public static void On_DoesNothingWhenLastCommitWasNotEnglish()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("kyou ");
            Assert.True(e.Session.IsComposing, "前提: kyou は日本語として変換中 (Space で変換)");
            e.Key(VirtualKeys.Return);
            var before = e.Doc;
            var next = e.Key(VirtualKeys.Space);
            Assert.True(!next.Consumed && !next.Toggle, "日本語の確定の後の Space は今までどおり");
            Assert.Equal(before + " ", e.Doc);
            return 0;
        });
    }

    [Test]
    public static void On_DoesNothingAfterAnotherKeyWasPassedToTheApp()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can \b ");
            // BackSpace で空白を消したので、次の Space は普通の空白 (直前の語を読み直さない)
            Assert.Equal("can ", e.Doc);
            Assert.True(!e.Session.IsComposing, "変換ボックスは開かない");
            return 0;
        });
    }

    [Test]
    public static void On_ShiftSpace_IsNotAffected()
    {
        WithWords(() =>
        {
            var e = new Editor(on: true);
            e.Type("can ");
            var shiftSpace = e.Key(VirtualKeys.Space, shift: true);
            Assert.True(!shiftSpace.Toggle, "Shift+Space は今までどおり (この設定の対象ではない)");
            return 0;
        });
    }
}
