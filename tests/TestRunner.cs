using System;
using System.Linq;
using System.Reflection;
using Godot;

namespace Otchlan.Tests;

/// <summary>Oznacza metodę statyczną jako test. Test zgłasza błąd wyjątkiem (np. przez <see cref="Assert"/>).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public static class Assert
{
    public static void True(bool cond, string msg = "warunek nie jest spełniony")
    {
        if (!cond) throw new Exception(msg);
    }

    public static void Equal<T>(T expected, T actual, string msg = null)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{msg ?? "różne wartości"}: oczekiwano <{expected}>, jest <{actual}>");
    }

    public static void Near(double expected, double actual, double eps = 1e-6, string msg = null)
    {
        if (Math.Abs(expected - actual) > eps)
            throw new Exception($"{msg ?? "różne wartości"}: oczekiwano {expected}±{eps}, jest {actual}");
    }
}

/// <summary>
/// Prosty runner testów działający w samym Godocie (także bez okna):
///   godot --headless --path . -- --run-tests
/// Wyszukuje statyczne metody z [Test] w przestrzeni nazw Otchlan.Tests. Kod wyjścia 0 = wszystko OK.
/// </summary>
public static class TestRunner
{
    public static int RunAll()
    {
        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.Namespace == "Otchlan.Tests")
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();
        int failed = 0;
        foreach (var m in tests)
        {
            try
            {
                m.Invoke(null, null);
                GD.Print($"  OK    {m.DeclaringType!.Name}.{m.Name}");
            }
            catch (TargetInvocationException e)
            {
                failed++;
                GD.PrintErr($"  BŁĄD  {m.DeclaringType!.Name}.{m.Name}: {e.InnerException?.Message}");
            }
        }
        GD.Print($"TESTY: {tests.Count - failed}/{tests.Count} OK");
        return failed;
    }
}
