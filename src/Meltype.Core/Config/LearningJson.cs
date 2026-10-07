// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json.Serialization;
using Meltype.Composition;
using Meltype.Learning;

namespace Meltype.Config;

/// <summary>
/// 学習データ (conversions.json・language.json・suggest.json・translation.json) の JSON の読み書き (ソース生成)。
/// reflection だと NativeAOT (Mac 版の libMeltypeNative) で、型によっては「metadata が無い」で例外になる
/// (設定 config.json の列挙型で実際に起きた。SettingsJsonContext)。値の型 (int・bool・DateTime) を含む型も、ここで生成しておく。
/// 形式は以前の reflection 版と同じ (既定のオプション)。
/// </summary>
[JsonSerializable(typeof(Dictionary<string, ConversionHistory.Entry>), TypeInfoPropertyName = "ConversionEntries")]
[JsonSerializable(typeof(Dictionary<string, LanguageMemory.Entry>), TypeInfoPropertyName = "LanguageEntries")]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, int>>), TypeInfoPropertyName = "TranslationCounts")]
[JsonSerializable(typeof(DictionarySuggestions.Data), TypeInfoPropertyName = "SuggestionData")]
[JsonSerializable(typeof(ConversionHistory.Entry), TypeInfoPropertyName = "ConversionEntry")]
[JsonSerializable(typeof(LanguageMemory.Entry), TypeInfoPropertyName = "LanguageEntry")]
[JsonSerializable(typeof(DictionarySuggestions.Entry), TypeInfoPropertyName = "SuggestionEntry")]
internal sealed partial class LearningJsonContext : JsonSerializerContext;

/// <summary>ユーザーモデル (usermodel.json) の JSON の読み書き (ソース生成)。インデントあり。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserModel.ModelFile))]
internal sealed partial class UserModelJsonContext : JsonSerializerContext;
