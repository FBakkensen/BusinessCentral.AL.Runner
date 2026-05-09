// TestExecutor — discovers and runs AL test methods on compiled BC IL.
// AL test convention: codeunit with [SubType=Test], methods with [Test] attribute.
// In emitted C#: codeunits become classes named CodeunitNNNN; test methods carry
// [NavTest] attribute (via NCLAttribute system). We discover by attribute name to
// avoid coupling to specific BC types.
using System.Reflection;

namespace AlRunnerV2;

public enum TestOutcome { Pass, Fail, Error }

public sealed record TestResult(string Codeunit, string Method, TestOutcome Outcome,
                                string? Message, string? FullException, TimeSpan Duration);

public sealed class TestExecutor
{
    public IReadOnlyList<TestResult> Run(Assembly assembly)
    {
        var results = new List<TestResult>();
        var ctorParam = typeof(Microsoft.Dynamics.Nav.Runtime.ITreeObject);

        foreach (var t in assembly.GetTypes())
        {
            if (!IsTestCodeunit(t)) continue;

            object? instance;
            try { instance = InstantiateCodeunit(t); }
            catch (Exception ex)
            {
                results.Add(new TestResult(t.Name, "<ctor>", TestOutcome.Error,
                    Unwrap(ex).Message, ex.ToString(), TimeSpan.Zero));
                continue;
            }
            if (instance == null) continue;

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsTestMethod(m)) continue;
                results.Add(RunOne(t.Name, m, instance));
            }
        }
        return results;
    }

    private static bool IsTestCodeunit(Type t)
    {
        if (!t.Name.StartsWith("Codeunit")) return false;
        // Has any method tagged with NavTest attribute?
        return t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(IsTestMethod);
    }

    private static bool IsTestMethod(MethodInfo m) =>
        m.GetCustomAttributes(inherit: false)
         .Any(a => a.GetType().Name is "NavTestAttribute" or "TestAttribute");

    private static object? InstantiateCodeunit(Type t)
    {
        var ctor = t.GetConstructors().FirstOrDefault(c =>
            c.GetParameters().Length == 1 &&
            c.GetParameters()[0].ParameterType.Name == "ITreeObject");
        if (ctor == null) return null;
        return ctor.Invoke(new object[] { BcRuntime.RootTreeStub! });
    }

    private static TestResult RunOne(string codeunit, MethodInfo m, object instance)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Mirror BC's per-test isolation transaction: drain in-memory table state so
        // each test starts empty. Without this, Insert calls in later tests collide
        // with leftover rows from earlier tests on the same table.
        AlRunnerV2.Patches.RecordPatches.ResetPerTestState();
        try
        {
            var args = m.GetParameters().Length == 0 ? Array.Empty<object>() : null;
            if (args == null)
                return new TestResult(codeunit, m.Name, TestOutcome.Error,
                    $"unsupported test signature ({m.GetParameters().Length} params)", null, sw.Elapsed);
            m.Invoke(instance, args);
            return new TestResult(codeunit, m.Name, TestOutcome.Pass, null, null, sw.Elapsed);
        }
        catch (TargetInvocationException tex)
        {
            var inner = Unwrap(tex);
            // BC's Assert.* throws specific exception types for test failures.
            // We can't classify Pass/Fail vs Error perfectly without knowing all of them,
            // so for now: any thrown exception is Fail.
            return new TestResult(codeunit, m.Name, TestOutcome.Fail,
                $"{inner.GetType().Name}: {inner.Message}", inner.ToString(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new TestResult(codeunit, m.Name, TestOutcome.Error,
                ex.Message, ex.ToString(), sw.Elapsed);
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException tex && tex.InnerException != null)
            ex = tex.InnerException;
        return ex;
    }
}
