// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;

namespace Meltype.Tests;

// public なのは `--self-check` のため (実行中に作る別アセンブリの偽のテストに付けると、internal の属性は読み出せない)。
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

internal sealed class AssertionException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message} 期待値: {expected} / 実際: {actual}".Trim());
    }
}

/// <summary>[Test] の付いた static メソッドをすべて実行する最小限のテストランナー (NuGet が使えないので xUnit の代わり)。</summary>
internal static class TestHost
{
    /// <summary>
    /// 実行の決まり (取りこぼしを黙って飛ばさない):
    /// ・static でない [Test]・引数を取る [Test] は実行せず FAIL として数える (理由を表示する)。
    /// ・フィルターに 1 件も一致しない (または [Test] が 1 件も無い) ときは理由を表示して 1 を返す (`0/0 passed` で 0 を返さない)。
    /// ・テストの中の例外だけでなく、ランナー側の Invoke の失敗 (ジェネリックのメソッドなど) も FAIL として数えて続ける。
    /// 最後の行は `N/M passed` (外部のスクリプトが読む)。この決まりは `--self-check` で確かめる。
    /// </summary>
    public static int Run(IEnumerable<Assembly> assemblies, string? filter)
    {
        var tests = assemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .Where(m => filter is null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        if (tests.Count == 0)
        {
            Console.WriteLine(filter is null
                ? "[Test] の付いたメソッドが 1 件も見つかりません (何も実行していません)"
                : $"フィルター \"{filter}\" に一致するテストがありません (何も実行していません。クラス名・メソッド名の綴りを確かめてください)");
            return 1;
        }

        var failed = 0;
        foreach (var test in tests)
        {
            var name = $"{test.DeclaringType!.Name}.{test.Name}";
            var rejected = !test.IsStatic ? "[Test] が static ではないので実行していません (static にしてください)"
                : test.GetParameters().Length > 0 ? "[Test] が引数を取るので実行していません (引数なしにしてください)"
                : null;
            if (rejected is not null)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {rejected}");
                continue;
            }
            // 共有の判定器 (CompositionTests.Detector) は全テストで使い回す。テストの中で差し替えたスペルチェッカー・学習を
            // 元へ戻し忘れると、後ろのテストが本番と違う構成で走る (どのテストが残したかわからなくなる) ので、そのテストを失敗にする。
            var shared = CompositionTests.Detector;
            var (checkerBefore, memoryBefore) = (shared.SpellChecker, shared.Memory);
            try
            {
                test.Invoke(null, null);
                if (!ReferenceEquals(shared.SpellChecker, checkerBefore) || !ReferenceEquals(shared.Memory, memoryBefore))
                {
                    var leaked = $"共有の判定器を元に戻していない: SpellChecker {checkerBefore?.GetType().Name ?? "null"} → {shared.SpellChecker?.GetType().Name ?? "null"}, " +
                        $"Memory {(memoryBefore is null ? "null" : "あり")} → {(shared.Memory is null ? "null" : "あり")}";
                    throw new TargetInvocationException(new AssertionException(leaked));
                }
                Console.WriteLine($"  PASS  {name}");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {ex.InnerException.Message.Replace("\n", "\n        ")}");
            }
            catch (Exception ex)
            {
                // テストの中ではなく、呼び出しそのものが失敗した (ジェネリックのメソッド・開いたジェネリック型など)。残りのテストは続ける。
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        ランナーが呼び出せませんでした: {ex.GetType().Name}: {ex.Message.Replace("\n", "\n        ")}");
            }
            finally
            {
                // 失敗したテストが残したものも、次のテストへ持ち越さない
                (shared.SpellChecker, shared.Memory) = (checkerBefore, memoryBefore);
            }
        }
        Console.WriteLine();
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>1 文字ずつの判定とその理由を表示する (辞書・閾値の調整用)。</summary>
    public static void Explain(IEnumerable<string> words)
    {
        var engine = TestSupport.CreateEngine();
        foreach (var word in words)
        {
            for (var i = 1; i <= word.Length; i++)
            {
                var prefix = word[..i];
                var result = engine.Evaluate(new Detection.DetectionInput(prefix, prefix.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), i == word.Length));
                Console.WriteLine(result.Describe());
                if (result.Verdict != Detection.Verdict.Undecided) break;
            }
            Console.WriteLine();
        }
    }
}
