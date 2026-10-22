// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Meltype.Config;

/// <summary>
/// config.json の JSON Schema (config.schema.json) を書き出す (本家 #1073)。
/// 起動時に 1 回だけ、config.json と同じフォルダーへ書く。エディターが config.json を書くときに
/// 項目名・型・選択肢を示せるので、手で書き換えたときの書き間違い (綴り・型・選択肢) に気づける。
/// config.json 本体には触らない (IME と「設定」タブが別のプロセスで書き合うので、書き換えると衝突する)。
/// 書き出しに失敗しても設定の読み込みは止めない (無くても動く補助)。
/// </summary>
internal static class ConfigSchema
{
    /// <summary>書き出すファイル名 (config.json と同じフォルダー)。</summary>
    internal const string FileName = "config.schema.json";

    /// <summary>この文書が従う JSON Schema の版。</summary>
    internal const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>このプロセスで書き出したパス (起動時に 1 回だけ書くため)。</summary>
    private static readonly HashSet<string> Written = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>人が読むファイルなので、日本語を \uXXXX にせずそのまま書く。</summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>config.json と同じフォルダーの config.schema.json のパス。</summary>
    internal static string PathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, FileName);

    /// <summary>
    /// 起動時に 1 回だけ config.schema.json を書く (同じパスには 1 回)。失敗しても例外を投げない (ログに 1 行残すだけ)。
    /// config.json は読まないので、config.json が壊れていても書ける。
    /// </summary>
    internal static void WriteNextTo(string configPath)
    {
        string path;
        try
        {
            path = PathFor(configPath);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"config.schema.json の場所を決められませんでした: {ex.Message}");
            return;
        }

        lock (Gate)
        {
            if (!Written.Add(path)) return;
        }

        try
        {
            var text = Generate();
            // 内容が同じなら書き直さない (起動のたびに更新時刻が変わって、エディターが読み直さないように)。
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            // 一時ファイル → 置き換え (書き込み中に落ちても、前の schema を壊さない)。
            SafeFile.WriteAllText(path, text);
        }
        catch (Exception ex)
        {
            // 設定の読み込み・入力は止めない。書き間違いに気づくための補助なので、無くても動く。
            Diagnostics.Log.Warn($"config.schema.json を書き出せませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// <see cref="Settings"/> から JSON Schema を作る。ソース生成の型情報 (SettingsJsonContext) から作るので NativeAOT でも動く。
    /// DisplayName / Description が取れれば title / description に入れる (取れなければ入れない)。
    /// </summary>
    internal static string Generate()
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(
            SettingsJsonContext.Default.Settings,
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        if (schema is not JsonObject exported) return schema.ToJsonString(Indented);

        // $schema を先頭に置く (エディターが版を読みやすいように)。exported の子は親を消してから移す。
        var result = new JsonObject
        {
            ["$schema"] = Dialect,
            ["title"] = "Meltype の設定 (config.json)",
            ["description"] = "Meltype の設定ファイル。手で書き換えるときの書き間違いを見つけるために、起動時にこの隣へ書き出したもの。",
        };
        foreach (var (key, value) in exported.ToList()) result[key] = value?.DeepClone();
        // 知らない項目名 (綴りの間違い) をエディターが指せるようにする。設定の読み込みは知らない項目を無視するので、動きは変わらない。
        result["additionalProperties"] = false;

        AddTitles(result);
        return result.ToJsonString(Indented);
    }

    /// <summary>
    /// Settings の各プロパティの DisplayName / Description を、スキーマの title / description に入れる。
    /// 属性が取れない (トリムされた) ときは、入れずに書き出す。
    /// </summary>
    private static void AddTitles(JsonObject root)
    {
        if (root["properties"] is not JsonObject properties) return;
        try
        {
            foreach (var property in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (properties[property.Name] is not JsonObject node) continue;
                if (property.GetCustomAttribute<DisplayNameAttribute>() is { } display) node["title"] = display.DisplayName;
                if (property.GetCustomAttribute<DescriptionAttribute>() is { } description) node["description"] = description.Description;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"設定の名前と説明を config.schema.json に入れられませんでした: {ex.Message}");
        }
    }

    /// <summary>テスト用: 「起動時に 1 回」の記録を消す。</summary>
    internal static void ResetForTests()
    {
        lock (Gate) Written.Clear();
    }
}
