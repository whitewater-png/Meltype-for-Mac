// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Text.Json;

namespace Meltype.Config;

/// <summary>Mac の「設定」タブで編集する項目の種類。</summary>
internal enum MacSettingKind
{
    Bool,
    Choice,
    Int,
}

/// <summary>選択肢 1 つ (config.json に書く名前と、画面に出す名前)。</summary>
internal sealed record MacSettingOption(string Value, string Label);

/// <summary>
/// 「設定」タブの項目 1 つ。Key は <see cref="Settings"/> のプロパティ名 (config.json の項目名)。
/// Label と Description は Settings の DisplayName / Description と同じ文字 (テストが見比べる)。
/// 値の読み書きは reflection を使わず、手で書いた Func / Action で行う (NativeAOT の libMeltypeNative の中でも動く)。
/// </summary>
internal sealed class MacSettingItem(string key, string label, string description, string group, MacSettingKind kind,
    Action<Utf8JsonWriter, Settings> write, Func<Settings, JsonElement, string?> apply, Action<Settings, Settings> copy)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    /// <summary>Settings の Description 属性と同じ文字 (Windows の設定画面と共通)。</summary>
    public string Description { get; } = description;

    /// <summary>Mac の画面に出す説明。Windows の言い回し (トレイ・Keyboard モードなど) を Mac 向けに直したいときだけ書く (無ければ <see cref="Description"/>)。</summary>
    public string? MacDescription { get; set; }

    /// <summary>画面に出す説明。</summary>
    public string DisplayDescription => MacDescription ?? Description;
    public string Group { get; } = group;
    public MacSettingKind Kind { get; } = kind;
    public IReadOnlyList<MacSettingOption> Options { get; init; } = [];
    public int Min { get; init; }
    public int Max { get; init; }

    /// <summary>今の値を JSON の値として書く (bool / 数 / 選択肢の名前の文字列)。</summary>
    internal Action<Utf8JsonWriter, Settings> WriteValue { get; } = write;

    /// <summary>JSON の値を検めて settings に入れる。入れられなければ理由 (settings は変えない)。</summary>
    internal Func<Settings, JsonElement, string?> Apply { get; } = apply;

    /// <summary>from の値を to に写す (既定値に戻すのに使う)。</summary>
    internal Action<Settings, Settings> Copy { get; } = copy;
}

/// <summary>
/// Mac の「Meltype 辞書」の「設定」タブで編集できる設定の一覧と、その読み書き (FFI の meltype_settings_* の中身)。
/// Windows の設定画面は Settings の属性から作るが、Mac の libMeltypeNative (NativeAOT) では reflection が使えないので、一覧は手で書く。
/// Mac で実際に効く設定だけを入れる (Windows 専用の判定・保留・アプリ別設定などは入れない)。
/// 書くときは、IME と画面が別のプロセスなので、ロックの中で読み直してから書く (ConfigFlag と同じ作法)。読めない config.json は上書きしない。
/// IME は SharedSettings / ConfigFlag が config.json の変更を見て、開いたままの入力欄にも反映する。
/// </summary>
internal static class MacSettingsCatalog
{
    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ConfigPath { get; set; } = () => AppPaths.ConfigFile;

    private const string InputGroup = "入力";
    private const string ConversionGroup = "変換・候補";
    private const string DictionaryGroup = "辞書";
    private const string LogGroup = "ログ";

    /// <summary>Windows の言い回し (トレイ・Keyboard モード・Windows の自動修正など) を Mac 向けに直した説明。ここに無い項目は Settings の Description のまま。</summary>
    private static readonly Dictionary<string, string> MacDescriptions = new()
    {
        [nameof(Settings.LiveConversion)] = "Space を押さなくても、打ったそばから漢字に変換して表示します。",
        [nameof(Settings.DetectionLevel)] = "英語か日本語かを自動で見分ける強さ。積極的 = 英語らしければすぐ英字 / 標準 = 短い語 (no, to, ga) は前後が英語のときだけ英字 / 慎重 = 確信度が高いときだけ英字 / 手動 = 自動では切り替えず提案だけ (変換ボックスで Tab を押すと提案どおり英字に)。",
        [nameof(Settings.CorrectTypos)] = "Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: 同梱のよくある打ち間違いの一覧にある語を直します (teh → the、recieve → receive)。",
        [nameof(Settings.FileLog)] = "~/Library/Application Support/Meltype/meltype.log にログを書きます (OFF のときは書きません)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。",
    };

    private static MacSettingItem WithMac(MacSettingItem item)
    {
        if (MacDescriptions.TryGetValue(item.Key, out var text)) item.MacDescription = text;
        return item;
    }

    public static IReadOnlyList<MacSettingItem> Items { get; } = Build().Select(WithMac).ToList();

    private static MacSettingItem[] Build() =>
    [
        Bool(nameof(Settings.LiveConversion), "ライブ変換",
            "Keyboard モードで、Space を押さなくても打ったそばから漢字に変換して表示します。",
            InputGroup, s => s.LiveConversion, (s, v) => s.LiveConversion = v),
        Bool(nameof(Settings.ContinueAfterConversion), "変換後も続けて入力できる",
            "Space で変換したあと、続けて文字を打っても確定せず、打った文字を含めて編集・変換を続ける。OFF のときは、今までどおり変換した結果を確定して新しい入力を始めます。現在は Mac 版だけで動きます (入力メニューの「変換後も続けて入力できる」で切り替え)。",
            InputGroup, s => s.ContinueAfterConversion, (s, v) => s.ContinueAfterConversion = v),
        Bool(nameof(Settings.ShiftEnterNewline), "Shift+Enter で確定して改行",
            "変換中に Shift+Enter を押すと、確定したうえで Shift+Enter をアプリに渡します (チャットや Web アプリでは改行)。OFF のときは Enter と同じく確定だけします。Mac 版は入力メニューの「Shift+Enter で確定して改行」で切り替え。",
            InputGroup, s => s.ShiftEnterNewline, (s, v) => s.ShiftEnterNewline = v),
        Choice(nameof(Settings.DetectionLevel), "自動判定の強さ",
            "積極的 = 英語らしければすぐ英字 / 標準 = 短い語 (no, to, ga) は前後が英語のときだけ英字 / 慎重 = 確信度が高いときだけ英字 / 手動 = 自動では切り替えず提案だけ (変換ボックスで Tab を押すと提案どおり英字に)。Meltype キーボード・IME 自動切替・英数状態の検知・かな入力のすべてに効きます。",
            InputGroup, s => s.DetectionLevel, (s, v) => s.DetectionLevel = v,
            (DetectionLevel.Aggressive, "Aggressive", "積極的 (Aggressive)"),
            (DetectionLevel.Balanced, "Balanced", "標準 (Balanced)"),
            (DetectionLevel.Conservative, "Conservative", "慎重 (Conservative)"),
            (DetectionLevel.Manual, "Manual", "手動 (提案のみ)")),
        Bool(nameof(Settings.AutoCorrectAfterCommit), "確定後も文脈に合わせて直す",
            "英語とも日本語とも読める語 (i, sushi など) を確定した後、次の語で英語か日本語かがはっきりしたら自動で確定し直します (i → 胃 と確定した後に want と打つと I want)。",
            InputGroup, s => s.AutoCorrectAfterCommit, (s, v) => s.AutoCorrectAfterCommit = v),
        Bool(nameof(Settings.CorrectTypos), "打ち間違いを直す",
            "Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: Windows の自動修正の一覧にある打ち間違いを直します (teh → the、recieve → receive)。",
            InputGroup, s => s.CorrectTypos, (s, v) => s.CorrectTypos = v),
        Bool(nameof(Settings.SpaceAroundEnglish), "英単語の前後に半角スペース",
            "確定するときに、日本語と英単語の間に半角スペースを入れます (今日はGitHubにpushした → 今日は GitHub に push した)。数字だけの語 (3時) には入れません。",
            InputGroup, s => s.SpaceAroundEnglish, (s, v) => s.SpaceAroundEnglish = v),
        Choice(nameof(Settings.Punctuation), "句読点の表記",
            "日本語の中で , と . を打ったときの句読点。技術文書・論文で「，．」を指定されるときに使います。変換ボックスに入っている間の入力から変わります。",
            InputGroup, s => s.Punctuation, (s, v) => s.Punctuation = v,
            (PunctuationStyle.Japanese, "Japanese", "、。"),
            (PunctuationStyle.Comma, "Comma", "，．"),
            (PunctuationStyle.CommaJapanese, "CommaJapanese", "，。")),
        Bool(nameof(Settings.FullWidthSymbols), "記号を全角にする",
            "日本語の中で打った ! ? ~ などの記号を全角 (！？～) にします。OFF にすると、打ったままの半角で入ります (チャットで !? を半角にしたいとき)。",
            InputGroup, s => s.FullWidthSymbols, (s, v) => s.FullWidthSymbols = v),

        Bool(nameof(Settings.TranslationCandidates), "英訳の候補",
            "変換の候補の後ろに英訳も出します (複雑な → complex, complicated)。JMdict のよく使う語から。選んだ英訳は少しずつ前に出ます。",
            ConversionGroup, s => s.TranslationCandidates, (s, v) => s.TranslationCandidates = v),
        Bool(nameof(Settings.ShowCandidateMeanings), "候補の意味を表示",
            "変換中に同じ候補で少し (約 1.5 秒) 止まると、その候補の意味をウィクショナリー日本語版から候補の一覧の横に出します (日本語の意味が無い語は JMdict の英訳: 橋 → bridge)。同音異義語を選ぶときの手がかりに。",
            ConversionGroup, s => s.ShowCandidateMeanings, (s, v) => s.ShowCandidateMeanings = v),
        Bool(nameof(Settings.Prediction), "予測変換",
            "ひらがなを数文字打つと、続きの候補 (ユーザー辞書・変換エンジン・過去に確定した語) を変換ボックスの下に出します。Tab で候補に入り、Enter で確定します。英語と判定した語には出しません。現在は Mac 版だけで表示されます。",
            ConversionGroup, s => s.Prediction, (s, v) => s.Prediction = v),
        Int(nameof(Settings.PredictionMinLength), "予測変換を出す最小の文字数",
            "読みがこの文字数以上になってから予測変換を出します (1〜5)。",
            ConversionGroup, 1, 5, s => s.PredictionMinLength, (s, v) => s.PredictionMinLength = v),

        Bool(nameof(Settings.DictionarySuggest), "ユーザー辞書への登録提案",
            "選び直した変換 (漢字・カタカナ・英字を含む語) を数え、同じ読みと語を何度も確定したら、入力メニューに「辞書に登録」の提案を出します。数えるのは読みと語と回数だけで、前後の文章は保存しません。現在は Mac 版だけで表示されます。",
            DictionaryGroup, s => s.DictionarySuggest, (s, v) => s.DictionarySuggest = v),
        Int(nameof(Settings.DictionarySuggestThreshold), "登録を提案するまでの確定回数",
            "同じ読みと語をこの回数確定したら、ユーザー辞書への登録を提案します (2〜10)。",
            DictionaryGroup, 2, 10, s => s.DictionarySuggestThreshold, (s, v) => s.DictionarySuggestThreshold = v),

        Bool(nameof(Settings.FileLog), "ファイルにログを書く",
            "%LOCALAPPDATA%\\Meltype\\meltype.log にログを書きます (OFF でも、トレイの「ログ / 判定理由」で見られるログは Meltype が動いている間だけメモリに残ります)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。",
            LogGroup, s => s.FileLog, (s, v) => s.FileLog = v),
        Bool(nameof(Settings.LogTypedText), "ログに入力した文字を残す",
            "確定した文字列・打った英字・読み・直した語をログに残します (不具合を調べるとき用)。OFF なら文字数だけを残します。OS が「秘匿入力」(パスワード欄など) と知らせている欄では入力を扱わないので残りません (アプリが知らせていない欄は対象外です)。",
            LogGroup, s => s.LogTypedText, (s, v) => s.LogTypedText = v),
    ];

    private static MacSettingItem Bool(string key, string label, string description, string group, Func<Settings, bool> get, Action<Settings, bool> set) =>
        new(key, label, description, group, MacSettingKind.Bool,
            (w, s) => w.WriteBooleanValue(get(s)),
            (s, v) =>
            {
                if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return $"「{label}」には オン か オフ を指定してください。";
                set(s, v.GetBoolean());
                return null;
            },
            (to, from) => set(to, get(from)));

    private static MacSettingItem Int(string key, string label, string description, string group, int min, int max, Func<Settings, int> get, Action<Settings, int> set) =>
        new(key, label, description, group, MacSettingKind.Int,
            (w, s) => w.WriteNumberValue(get(s)),
            (s, v) =>
            {
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var number)) return $"「{label}」には整数を指定してください。";
                if (number < min || number > max) return $"「{label}」は {min}〜{max} の範囲で指定してください。";
                set(s, number);
                return null;
            },
            (to, from) => set(to, get(from)))
        { Min = min, Max = max };

    private static string? NameOf<T>((T Value, string Name, string Label)[] options, T value) where T : struct, Enum
    {
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value)) return option.Name;
        }
        return null;
    }

    private static MacSettingItem Choice<T>(string key, string label, string description, string group, Func<Settings, T> get, Action<Settings, T> set,
        params (T Value, string Name, string Label)[] options) where T : struct, Enum =>
        new(key, label, description, group, MacSettingKind.Choice,
            // config.json に定義のない数 (手で書いた Punctuation: 7 など) があっても、一覧を返せるよう、既定値の選択肢として見せる。
            (w, s) => w.WriteStringValue(NameOf(options, get(s)) ?? NameOf(options, get(new Settings()))!),
            (s, v) =>
            {
                if (v.ValueKind != JsonValueKind.String) return $"「{label}」には選択肢の名前を指定してください。";
                var name = v.GetString();
                foreach (var option in options)
                {
                    if (option.Name != name) continue;
                    set(s, option.Value);
                    return null;
                }
                return $"「{label}」に「{name}」という選択肢はありません。";
            },
            (to, from) => set(to, get(from)))
        { Options = options.Select(o => new MacSettingOption(o.Name, o.Label)).ToList() };

    private static MacSettingItem? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    /// <summary>
    /// 画面に出す項目と今の値の JSON: {"items":[{"key","label","description","group","kind":"bool|choice|int","value", "options":[{"value","label"}] (choice), "min","max" (int)}]}。
    /// 項目の並びと group の並びが、そのまま画面の並び。
    /// </summary>
    public static string ToJson(Settings settings)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartArray("items");
            foreach (var item in Items)
            {
                w.WriteStartObject();
                w.WriteString("key", item.Key);
                w.WriteString("label", item.Label);
                w.WriteString("description", item.DisplayDescription);
                w.WriteString("group", item.Group);
                w.WriteString("kind", item.Kind switch { MacSettingKind.Bool => "bool", MacSettingKind.Choice => "choice", _ => "int" });
                w.WritePropertyName("value");
                item.WriteValue(w, settings);
                if (item.Kind == MacSettingKind.Choice)
                {
                    w.WriteStartArray("options");
                    foreach (var option in item.Options)
                    {
                        w.WriteStartObject();
                        w.WriteString("value", option.Value);
                        w.WriteString("label", option.Label);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                if (item.Kind == MacSettingKind.Int)
                {
                    w.WriteNumber("min", item.Min);
                    w.WriteNumber("max", item.Max);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>config.json を読んで、画面に出す JSON にする。読めなければ (壊れている・大きすぎる) null。</summary>
    public static string? Read() => Settings.LoadForUpdate(ConfigPath()) is { } settings ? ToJson(settings) : null;

    /// <summary>
    /// 項目 1 つを変えて config.json に保存する。変えられなければ理由 (画面にそのまま出す)、できたら null。
    /// key が未知・値の型や範囲や選択肢が違うときは、何も書かない。
    /// </summary>
    public static string? Apply(string key, string valueJson)
    {
        if (Find(key) is not { } item) return $"「{key}」という設定項目はありません。";
        JsonElement value;
        try
        {
            using var document = JsonDocument.Parse(valueJson ?? "");
            value = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return $"「{item.Label}」の値を読めませんでした。";
        }
        // 先に検めておく (ロックを取る前、何も書かない前に断る)。
        if (item.Apply(new Settings(), value) is { } invalid) return invalid;
        return Update(settings => item.Apply(settings, value));
    }

    /// <summary>一覧にある項目だけを既定値に戻して保存する (ほかの設定・プロファイル・アプリ別設定は触らない)。戻せなければ理由、できたら null。</summary>
    public static string? ResetToDefaults()
    {
        var defaults = new Settings();
        return Update(settings =>
        {
            foreach (var item in Items) item.Copy(settings, defaults);
            return null;
        });
    }

    /// <summary>ロックの中で config.json を読み直し、change で書き換えて保存する。読めない config.json は上書きしない。</summary>
    private static string? Update(Func<Settings, string?> change)
    {
        try
        {
            var path = ConfigPath();
            SafeFile.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (FileLock.Acquire(path))
            {
                if (Settings.LoadForUpdate(path) is not { } settings)
                    return "設定ファイル (config.json) を読めないため、変更しませんでした。ファイルが壊れていないか確認してください。";
                var before = settings.ToJson();
                if (change(settings) is { } reason) return reason;
                settings.Normalize();
                if (settings.ToJson() != before || !File.Exists(path)) settings.Save(path);
            }
            return null;
        }
        catch (FileLockTimeoutException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"設定を保存できませんでした: {ex.Message}");
            return $"設定を保存できませんでした: {ex.Message}";
        }
    }
}
