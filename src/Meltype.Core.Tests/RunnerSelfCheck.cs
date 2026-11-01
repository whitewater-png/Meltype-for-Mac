// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Reflection;
using System.Reflection.Emit;

namespace Meltype.Tests;

/// <summary>
/// テストランナー (TestHost.Run) 自身の検査 (`--self-check`)。取りこぼしが FAIL として数えられることを確かめる。
/// 偽のテストは実行中にだけ作るメモリ上のアセンブリに置く。テストのアセンブリには入らないので、ふだんの全テストの実行には出てこない。
/// </summary>
internal static class RunnerSelfCheck
{
    private const string NotRun = "SELF-CHECK: 実行されてはいけない偽のテストが実行された";

    public static int Run()
    {
        var fakes = BuildFakeAssembly();
        var problems = new List<string>();

        // (a)(c) すべての偽のテスト: 通る 1 件だけが PASS、残りは FAIL として数えられ、最後まで走る。
        var all = Capture(fakes, null);
        Expect(problems, "全件", all, exitCode: 1, lastLine: "1/5 passed", contains:
        [
            "  PASS  ZFake.Passes",
            "  FAIL  AFake.Fails\n        SELF-CHECK: わざと失敗",
            "  FAIL  BFake.NotStatic\n        [Test] が static ではないので実行していません",
            "  FAIL  CFake.TakesArgument\n        [Test] が引数を取るので実行していません",
            "  FAIL  DFake.Generic\n        ランナーが呼び出せませんでした: InvalidOperationException",
        ]);
        if (all.Output.Contains(NotRun)) problems.Add("全件: static でない / 引数つきの偽のテストが実行された");

        // フィルターで 1 件だけ: 今までどおり `1/1 passed` で 0 を返す。
        Expect(problems, "フィルター一致", Capture(fakes, "Passes"), exitCode: 0, lastLine: "1/1 passed", contains: ["  PASS  ZFake.Passes"]);

        // (b) フィルターが 1 件も一致しない: 理由を出して 1 を返し、`0/0 passed` を出さない。
        var none = Capture(fakes, "NoSuchTestName");
        Expect(problems, "0 件一致", none, exitCode: 1, lastLine: null, contains: ["フィルター \"NoSuchTestName\" に一致するテストがありません"]);
        if (none.Output.Contains("0/0 passed")) problems.Add("0 件一致: `0/0 passed` が出ている");

        foreach (var p in problems) Console.WriteLine($"  NG  {p}");
        Console.WriteLine(problems.Count == 0 ? "self-check OK (テストランナーの数え方 3 項目)" : $"self-check NG ({problems.Count} 件)");
        return problems.Count == 0 ? 0 : 1;
    }

    private sealed record Result(int? ExitCode, string Output, string? Crash);

    /// <summary>TestHost.Run の表示を取り込む。Run 自体が例外で止まったら、それも結果として返す。</summary>
    private static Result Capture(Assembly assembly, string? filter)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var code = TestHost.Run([assembly], filter);
            return new Result(code, writer.ToString().Replace("\r\n", "\n"), null);
        }
        catch (Exception ex)
        {
            return new Result(null, writer.ToString().Replace("\r\n", "\n"), $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static void Expect(List<string> problems, string label, Result result, int exitCode, string? lastLine, string[] contains)
    {
        if (result.Crash is not null)
        {
            problems.Add($"{label}: TestHost.Run が例外で止まった ({result.Crash})");
            return;
        }
        if (result.ExitCode != exitCode) problems.Add($"{label}: 終了コード 期待 {exitCode} / 実際 {result.ExitCode}");
        var last = result.Output.TrimEnd('\n').Split('\n').LastOrDefault() ?? "";
        if (lastLine is not null && last != lastLine) problems.Add($"{label}: 最後の行 期待 \"{lastLine}\" / 実際 \"{last}\"");
        foreach (var text in contains)
            if (!result.Output.Contains(text)) problems.Add($"{label}: 表示に \"{text.Replace("\n", "\\n")}\" が無い");
    }

    /// <summary>
    /// 偽のテストを持つアセンブリをメモリ上に作る。
    /// ZFake.Passes (通る static) / AFake.Fails (中で失敗する static) / BFake.NotStatic (static でない) /
    /// CFake.TakesArgument (引数つき) / DFake.Generic&lt;T&gt; (Invoke そのものが失敗する)。
    /// 実行されてはいけないもの (BFake・CFake) は、実行されたら <see cref="NotRun"/> を投げる。
    /// </summary>
    private static Assembly BuildFakeAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Meltype.RunnerSelfCheck.Fakes"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Fakes");
        var test = new CustomAttributeBuilder(typeof(TestAttribute).GetConstructor(Type.EmptyTypes)!, []);
        const MethodAttributes Static = MethodAttributes.Public | MethodAttributes.Static;

        MethodBuilder Method(TypeBuilder type, string name, MethodAttributes attributes, Type[] parameters, string? throwMessage)
        {
            var method = type.DefineMethod(name, attributes, typeof(void), parameters);
            method.SetCustomAttribute(test);
            var il = method.GetILGenerator();
            if (throwMessage is not null)
            {
                il.Emit(OpCodes.Ldstr, throwMessage);
                il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
                il.Emit(OpCodes.Throw);
            }
            else il.Emit(OpCodes.Ret);
            return method;
        }

        var passes = module.DefineType("ZFake", TypeAttributes.Public | TypeAttributes.Class);
        Method(passes, "Passes", Static, [], null);
        var fails = module.DefineType("AFake", TypeAttributes.Public | TypeAttributes.Class);
        Method(fails, "Fails", Static, [], "SELF-CHECK: わざと失敗");
        var notStatic = module.DefineType("BFake", TypeAttributes.Public | TypeAttributes.Class);
        notStatic.DefineDefaultConstructor(MethodAttributes.Public);
        Method(notStatic, "NotStatic", MethodAttributes.Public, [], NotRun);
        var argument = module.DefineType("CFake", TypeAttributes.Public | TypeAttributes.Class);
        Method(argument, "TakesArgument", Static, [typeof(int)], NotRun);
        var generic = module.DefineType("DFake", TypeAttributes.Public | TypeAttributes.Class);
        var genericMethod = generic.DefineMethod("Generic", Static, typeof(void), Type.EmptyTypes);
        genericMethod.DefineGenericParameters("T");
        genericMethod.SetCustomAttribute(test);
        genericMethod.GetILGenerator().Emit(OpCodes.Ret);

        // AssemblyBuilder そのものではなく、作った型が属する実行時のアセンブリを返す (AssemblyBuilder.GetTypes() は作った型の [Test] を返さない)。
        var created = new[] { passes, fails, notStatic, argument, generic }.Select(t => t.CreateType()!).ToList();
        return created[0].Assembly;
    }
}
