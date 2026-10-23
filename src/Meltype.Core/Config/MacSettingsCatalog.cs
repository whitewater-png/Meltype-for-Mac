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
        [nameof(Settings.InputStyle)] = "ローマ字入力 / かな入力 (JIS)。かな入力は JIS キーボード専用の実験的な機能です (US 配列では使えません)。JIS かな配列で入力し (Shift+E = ぃ, Shift+Z = っ, Shift+ね = 、、濁点・半濁点は直前のかなに付く)、打ったキーの英字が英単語なら英字で見せます。JIS キーボード以外 (US 配列など) では、かな入力にしてもローマ字入力のまま動きます。変えると、次に打つ文字から効きます。",
        [nameof(Settings.CorrectTypos)] = "Space・Enter で変換・確定するときに打ち間違いを直します。ローマ字: 読めない子音が残ったとき、隣のキーの押し間違い・入れ替わり・抜けを 1 文字だけ直します (onegaishimsu → お願いします、sumimasne → すみません。よく使う語の読みになるときだけ)。英語: 同梱のよくある打ち間違いの一覧にある語を直します (teh → the、recieve → receive)。",
        [nameof(Settings.FileLog)] = "~/Library/Application Support/Meltype/meltype.log にログを書きます (OFF のときは書きません)。判定した語の先頭の数文字・アプリ名・入力欄の名前が含まれます。確定した文字列などは「ログに入力した文字を残す」が ON のときだけ残ります。",
        [nameof(Settings.TabConversion)] = "打っている途中に Tab を押すと、Space と同じく変換を始めます。Tab で始めた変換の中では Tab で次の候補、Shift+Tab で前の候補、Enter で確定します。もしかして・手動の提案・予測変換の候補が出ているときは、今までどおりそちらに使います。英語で終わっているとき・Space で始めた変換の中では、確定してから Tab をアプリに渡します。OFF のときは、今までどおり確定して Tab をアプリに渡します (フォームで日本語を打って Tab で次の欄へ移る操作が変わらないよう、Mac 版は既定 OFF。本家 Windows 版は既定 ON)。",
        [nameof(Settings.DownSelectsPrediction)] = "入力中に ↓ を押したとき、予測候補が出ていれば Tab と同じく予測の選択に入ります。OFF のときは、今までどおり Space と同じく変換を始めます (本家 #993)。",
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
        Bool(nameof(Settings.Zenzai), "Zenzai (ニューラル変換、実験的)",
            "文脈に合う漢字が先頭に来やすくなります。ON にすると変換モデル (約 74 MB) を 1 回だけダウンロードします。ダウンロードが終わるまでは今までどおりの変換です。",
            InputGroup, s => s.Zenzai, (s, v) => s.Zenzai = v),
        Bool(nameof(Settings.ContinueAfterConversion), "変換後も続けて入力できる",
            "Space で変換したあと、続けて文字を打っても確定せず、打った文字を含めて編集・変換を続ける。OFF のときは、今までどおり変換した結果を確定して新しい入力を始めます。現在は Mac 版だけで動きます (入力メニューの「変換後も続けて入力できる」で切り替え)。",
            InputGroup, s => s.ContinueAfterConversion, (s, v) => s.ContinueAfterConversion = v),
        Bool(nameof(Settings.TypeDigitsAfterConversion), "変換直後の数字は入力として打つ",
            "Space で変換した直後に打った数字を、候補の番号ではなく、そのまま数字として入力します (denwa wa 03-1234-5678 や kyou wa 12 の数字が候補の選択にならない)。候補を動かした後 (↓・もう一度 Space・PageDown・← →) は、今までどおり数字キーで候補を選んで確定します。OFF のときは、変換した直後から数字キーで候補を選びます。",
            InputGroup, s => s.TypeDigitsAfterConversion, (s, v) => s.TypeDigitsAfterConversion = v),
        Bool(nameof(Settings.ShiftEnterNewline), "Shift+Enter で確定して改行",
            "変換中に Shift+Enter を押すと、確定したうえで Shift+Enter をアプリに渡します (チャットや Web アプリでは改行)。OFF のときは Enter と同じく確定だけします。Mac 版は入力メニューの「Shift+Enter で確定して改行」で切り替え。",
            InputGroup, s => s.ShiftEnterNewline, (s, v) => s.ShiftEnterNewline = v),
        Bool(nameof(Settings.TokMode), "ATOK スタイルのキー操作",
            "変換中のキー操作を ATOK と同じにします。← で文節を縮め、→ で文節を伸ばす (区切りを前 / うしろへ)。↓ で選んでいる文節までを確定 (部分確定)。Shift+← → で文節を移り、Ctrl+← → で先頭 / 最後の文節へ。次の候補は Space、前の候補は ↑。変換前の入力中は、Shift+↓ で先頭の 1 音だけを確定します (1 音確定)。OFF のときは、今までどおり ← → で文節を選び、↓ で次の候補、Shift+← → で区切りを変えます (Shift+↓ も ↓ と同じ)。",
            InputGroup, s => s.TokMode, (s, v) => s.TokMode = v),
        Bool(nameof(Settings.TabConversion), "Tab で変換",
            "打っている途中に Tab を押すと、Space と同じく変換を始めます (Microsoft IME と同じ)。Tab で始めた変換の中では Tab で次の候補、Shift+Tab で前の候補、Enter で確定します。もしかして・手動の提案・予測変換の候補が出ているときは、今までどおりそちらに使います。英語で終わっているとき・Space で始めた変換の中では、今までどおり確定してから Tab をアプリに渡します。",
            InputGroup, s => s.TabConversion, (s, v) => s.TabConversion = v),
        Bool(nameof(Settings.DownSelectsPrediction), "↓ で予測候補を選ぶ",
            "入力中に ↓ を押したとき、予測候補が出ていれば Tab と同じく予測の選択に入ります。OFF のときは、今までどおり Space と同じく変換を始めます (本家 #993)。",
            InputGroup, s => s.DownSelectsPrediction, (s, v) => s.DownSelectsPrediction = v),
        Bool(nameof(Settings.DeleteClauseOnBackspace), "変換中の BackSpace で選んだ文節を消す",
            "変換中に ← → で文節を選んでから BackSpace を押すと、その文節を消して、消した位置から打ち直せます (本家 #972)。前の文節は変換したまま、後ろの文節も変換したままで、打ち直した文節の後ろに付けて確定します。文節を選ばずに BackSpace を押したときは、今までどおり変換を取り消してかなに戻ります。既定は OFF です。",
            InputGroup, s => s.DeleteClauseOnBackspace, (s, v) => s.DeleteClauseOnBackspace = v),
        Bool(nameof(Settings.TabGridCandidates), "Tab で候補を格子状に広げる",
            "変換中に Tab を押すと、候補の一覧を格子状 (5 列) に広げて出します (本家 #1016)。変換中は Tab で確定せず、次の候補へ移ります (Shift+Tab は前の候補、Enter で確定)。候補が 10 件以上あるときも、ページ送りをせずに一覧を見渡せます。OFF のときは、今までどおり変換中の Tab で確定してアプリに渡します。",
            InputGroup, s => s.TabGridCandidates, (s, v) => s.TabGridCandidates = v),
        Bool(nameof(Settings.ToggleLastWordCtrlKeys), "確定した直後の語を Ctrl+' / Ctrl+J で直す",
            "確定した直後の語を Ctrl+' で英字に、Ctrl+J でかなに直します (F10 / F6 と同じ)。ターミナルの Ctrl+J (改行) と重なるので既定は OFF。現在は Mac 版だけで動きます。",
            InputGroup, s => s.ToggleLastWordCtrlKeys, (s, v) => s.ToggleLastWordCtrlKeys = v),
        Choice(nameof(Settings.DetectionLevel), "自動判定の強さ",
            "積極的 = 英語らしければすぐ英字 / 標準 = 短い語 (no, to, ga) は前後が英語のときだけ英字 / 慎重 = 確信度が高いときだけ英字 / 手動 = 自動では切り替えず提案だけ (変換ボックスで Tab を押すと提案どおり英字に)。Meltype キーボード・IME 自動切替・英数状態の検知・かな入力のすべてに効きます。",
            InputGroup, s => s.DetectionLevel, (s, v) => s.DetectionLevel = v,
            (DetectionLevel.Aggressive, "Aggressive", "積極的 (Aggressive)"),
            (DetectionLevel.Balanced, "Balanced", "標準 (Balanced)"),
            (DetectionLevel.Conservative, "Conservative", "慎重 (Conservative)"),
            (DetectionLevel.Manual, "Manual", "手動 (提案のみ)")),
        Bool(nameof(Settings.KanaInJapaneseMode), "日本語入力中は一覧の語をかなで変換",
            "dictionaries/japanese-mode.txt に書いた語 (exe など) を、日本語の文脈で打ったときは英字ではなく かな (えぇ) として変換します。英語の文の中 (前が英単語) とファイルの拡張子 (.exe) は今までどおり英字のままです。",
            InputGroup, s => s.KanaInJapaneseMode, (s, v) => s.KanaInJapaneseMode = v),
        Bool(nameof(Settings.KeepEnglishNameAfterEnglish), "英単語の後ろの英字の名前を英語にする",
            "英単語 (Koki など) のすぐ後ろに続けて打った大文字の英字 (Ogawa など) を、日本語の文の中でも英字のまま確定します。OFF のときは今までどおり、O|gawa のように 1 文字の大文字と助詞で始まる語に分かれます。",
            InputGroup, s => s.KeepEnglishNameAfterEnglish, (s, v) => s.KeepEnglishNameAfterEnglish = v),
        Choice(nameof(Settings.InputStyle), "入力方式",
            "ローマ字入力 / かな入力 (JIS) / 両方を判定。Meltype キーボードでは、かな入力を選ぶと JIS かな配列で入力し (Shift+E = ぃ, Shift+Z = っ, Shift+ね = 、)、打ったキーの英字が英単語なら英字で見せます。「両方を判定」は IME 自動切替のみ (Meltype キーボードではローマ字入力)。",
            InputGroup, s => s.InputStyle, (s, v) => s.InputStyle = v,
            (InputStyle.Romaji, "Romaji", "ローマ字入力"),
            // 「両方を判定」(InputStyle.Both) は IME 自動切替 (Windows) 用で、Mac ではローマ字入力と同じ動きなので、選択肢に出さない。
            (InputStyle.Kana, "Kana", "かな入力 (JIS)")),
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
        Bool(nameof(Settings.ZSymbolConversion), "z + 記号を変換する",
            "z の直後に / . , - [ ] を打つと ・ … ‥ ～ 『 』 にします。OFF にすると打ったまま (z/ → z/) になります。z + h j k l の矢印 (← ↓ ↑ →) は、この設定にかかわらず今までどおりです。",
            InputGroup, s => s.ZSymbolConversion, (s, v) => s.ZSymbolConversion = v),
        Bool(nameof(Settings.SlashAsNakaguro), "日本語の直後の / を ・ にする",
            "日本語を確定した直後に / を打つと、スラッシュではなく中黒 (・) を入れます。URL やパスを打つときは OFF のままにしてください。",
            InputGroup, s => s.SlashAsNakaguro, (s, v) => s.SlashAsNakaguro = v),
        Bool(nameof(Settings.FullWidthSymbols), "記号を全角にする",
            "日本語の中で打った ! ? ~ などの記号を全角 (！？～) にします。OFF にすると、打ったままの半角で入ります (チャットで !? を半角にしたいとき)。",
            InputGroup, s => s.FullWidthSymbols, (s, v) => s.FullWidthSymbols = v),
        Choice(nameof(Settings.Parentheses), "丸括弧 ( ) の幅",
            "日本語の中で打った ( ) を、半角 ( ) にするか全角 （ ） にするか。英語の中や、すぐ後ろが英語のときは、この設定にかかわらず半角です。[ ] は日本語と判定したときは「」、英語と判定したときは [ ] で、自動判定のままです。",
            InputGroup, s => s.Parentheses, (s, v) => s.Parentheses = v,
            (ParenthesisWidth.HalfWidth, "HalfWidth", "半角 ( )"),
            (ParenthesisWidth.FullWidth, "FullWidth", "全角 （ ）")),
        Choice(nameof(Settings.Braces), "中括弧 { } の幅",
            "日本語の中で打った { } を、全角 ｛ ｝ にするか半角 { } にするか。英語の中や、すぐ後ろが英語のときは、この設定にかかわらず半角です。",
            InputGroup, s => s.Braces, (s, v) => s.Braces = v,
            (BraceWidth.HalfWidth, "HalfWidth", "半角 { }"),
            (BraceWidth.FullWidth, "FullWidth", "全角 ｛ ｝")),

        Bool(nameof(Settings.TranslationCandidates), "英訳の候補",
            "変換の候補の後ろに英訳も出します (複雑な → complex, complicated)。JMdict のよく使う語から。選んだ英訳は少しずつ前に出ます。",
            ConversionGroup, s => s.TranslationCandidates, (s, v) => s.TranslationCandidates = v),
        Bool(nameof(Settings.CalculatorCandidates), "計算式を変換すると結果を候補に出す",
            "数字と + - * / ( ) だけの式を打って Space で変換すると、計算した結果 (1+2*3 → 7) と「式=結果」を候補に出します。",
            ConversionGroup, s => s.CalculatorCandidates, (s, v) => s.CalculatorCandidates = v),
        Bool(nameof(Settings.ConsonantAbbreviations), "子音の略入力を候補に出す",
            "子音だけの略入力 (ast → 明日、ngt → ノート など) を、変換の候補に出します。候補は同梱の candidates.txt の英字の読みから出します。既定は OFF です。",
            ConversionGroup, s => s.ConsonantAbbreviations, (s, v) => s.ConsonantAbbreviations = v),
        Bool(nameof(Settings.ContextualLearning), "英語 / 日本語の学習を前の文脈ごとに分ける",
            "英字 / かなに直した語 (new → ねw など) の学習を、直前が英語だったか日本語だったかごとに別々に覚えます。同じローマ字でも、英語の文の中と日本語の文の中で使い分けられます。ON にする前に覚えた学習は、どちらの文脈でも使います。",
            ConversionGroup, s => s.ContextualLearning, (s, v) => s.ContextualLearning = v),
        Bool(nameof(Settings.LaughterAfterKana), "かな + w を笑い (ねw・さw) にする",
            "ね・さ などの 1 文字のかなの後ろの w を、英単語 (new・saw) にせず笑いの w にします (なるほどnew → なるほどねw)。英単語の new や saw を打つ人は OFF のままにしてください。",
            ConversionGroup, s => s.LaughterAfterKana, (s, v) => s.LaughterAfterKana = v),
        Bool(nameof(Settings.ClipboardCandidates), "クリップボードの文字を候補に出す",
            "くりっぷぼーど・ぺーすと を変換したときに、クリップボードの 1 行 (200 文字まで) を候補に出します。変換した瞬間に 1 回だけ読み、保存も送信もしません。パスワード管理ソフトなど「履歴に残さない」印のある内容は読みません。既定は OFF です。",
            ConversionGroup, s => s.ClipboardCandidates, (s, v) => s.ClipboardCandidates = v),
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
