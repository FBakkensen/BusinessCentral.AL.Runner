// Reporter — aggregates per-test results into per-bucket and overall summaries,
// and writes a JSON failure-classification file for follow-up parallel work.
using System.Text.Json;

namespace AlRunnerV2;

public enum BucketStage { CompileFailed, ExecuteFailed, Ran }

public sealed record BucketResult(string BucketPath, BucketStage Stage,
                                   IReadOnlyList<string> CompileErrors,
                                   string? ProcessError,
                                   IReadOnlyList<TestResult> Tests,
                                   TimeSpan EmitTime, TimeSpan CompileTime, TimeSpan RunTime);

public static class Reporter
{
    public static void PrintSummary(IReadOnlyList<BucketResult> buckets, TextWriter w)
    {
        int totalTests = 0, pass = 0, fail = 0, err = 0;
        int compileFailed = 0, execFailed = 0;
        TimeSpan emit = TimeSpan.Zero, comp = TimeSpan.Zero, run = TimeSpan.Zero;
        foreach (var b in buckets)
        {
            emit += b.EmitTime; comp += b.CompileTime; run += b.RunTime;
            if (b.Stage == BucketStage.CompileFailed) { compileFailed++; continue; }
            if (b.Stage == BucketStage.ExecuteFailed) { execFailed++; continue; }
            foreach (var t in b.Tests)
            {
                totalTests++;
                if (t.Outcome == TestOutcome.Pass) pass++;
                else if (t.Outcome == TestOutcome.Fail) fail++;
                else err++;
            }
        }
        w.WriteLine();
        w.WriteLine("=================================================================");
        w.WriteLine("AlRunner v2 — clean-pipeline test run summary");
        w.WriteLine("=================================================================");
        w.WriteLine($"Buckets:       {buckets.Count} total");
        w.WriteLine($"  ran:         {buckets.Count - compileFailed - execFailed}");
        w.WriteLine($"  compile-fail:{compileFailed}");
        w.WriteLine($"  exec-fail:   {execFailed}");
        w.WriteLine($"Tests:         {totalTests} total");
        w.WriteLine($"  pass:        {pass}");
        w.WriteLine($"  fail:        {fail}");
        w.WriteLine($"  error:       {err}");
        w.WriteLine($"Time:");
        w.WriteLine($"  AL emit:     {emit.TotalSeconds:F1}s");
        w.WriteLine($"  C# compile:  {comp.TotalSeconds:F1}s");
        w.WriteLine($"  test run:    {run.TotalSeconds:F1}s");
        w.WriteLine($"  total:       {(emit + comp + run).TotalSeconds:F1}s");
        w.WriteLine("=================================================================");
    }

    public static void WriteClassification(IReadOnlyList<BucketResult> buckets, string path)
    {
        var failures = new List<object>();
        foreach (var b in buckets)
        {
            if (b.Stage == BucketStage.CompileFailed)
            {
                failures.Add(new
                {
                    bucket = b.BucketPath,
                    kind = "compile",
                    errors = b.CompileErrors.Take(10).ToList(),
                    classification = ClassifyCompile(b.CompileErrors),
                });
            }
            else if (b.Stage == BucketStage.ExecuteFailed)
            {
                failures.Add(new
                {
                    bucket = b.BucketPath,
                    kind = "execute",
                    error = b.ProcessError,
                    classification = "process-error",
                });
            }
            else
            {
                foreach (var t in b.Tests.Where(t => t.Outcome != TestOutcome.Pass))
                {
                    failures.Add(new
                    {
                        bucket = b.BucketPath,
                        kind = t.Outcome.ToString().ToLowerInvariant(),
                        codeunit = t.Codeunit,
                        method = t.Method,
                        message = t.Message,
                        // First few stack frames (after the test method) — enough to identify
                        // which BC API the failure hit, but not so many that the JSON explodes.
                        stack_top = StackTop(t.FullException, 6),
                        classification = ClassifyTest(t.Message ?? "", t.FullException ?? ""),
                    });
                }
            }
        }
        var grouped = failures
            .GroupBy(f => f.GetType().GetProperty("classification")!.GetValue(f) as string ?? "unknown")
            .OrderByDescending(g => g.Count())
            .Select(g => new { classification = g.Key, count = g.Count(), examples = g.Take(3).ToList() })
            .ToList();
        var doc = new
        {
            generated = DateTime.UtcNow.ToString("o"),
            total_failures = failures.Count,
            classifications = grouped,
            all_failures = failures,
        };
        File.WriteAllText(path,
            JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IReadOnlyList<string> StackTop(string? full, int max)
    {
        if (string.IsNullOrEmpty(full)) return Array.Empty<string>();
        return full.Split('\n')
            .Where(l => l.TrimStart().StartsWith("at "))
            .Take(15)  // more frames for diagnosis
            .Select(l => l.Trim())
            .ToArray();
    }

    // Hand-tuned classification heuristics — purely descriptive, not authoritative.
    private static string ClassifyCompile(IReadOnlyList<string> errors)
    {
        var first = errors.FirstOrDefault() ?? "";
        if (first.Contains("CS0246") || first.Contains("CS0103")) return "compile/missing-type-or-name";
        if (first.Contains("CS0117")) return "compile/missing-member";
        if (first.Contains("CS1503") || first.Contains("CS1501")) return "compile/signature-mismatch";
        if (first.Contains("CS0029") || first.Contains("CS0030")) return "compile/conversion";
        if (first.Contains("CS0234")) return "compile/missing-namespace";
        return "compile/other";
    }

    private static string ClassifyTest(string message, string full)
    {
        // Out-of-scope failures (loud-failures.md / docs/scope.md) are classified
        // by API name, not by stack frame — they're a contract decision, not an NRE.
        // Format: "RunnerOutOfScopeException: <api> is out of scope. Reason: <reason>. ..."
        if (message.Contains("RunnerOutOfScopeException", StringComparison.Ordinal)
            || full.Contains("RunnerOutOfScopeException", StringComparison.Ordinal))
        {
            int oosIdx = message.IndexOf("RunnerOutOfScopeException", StringComparison.Ordinal);
            string tail = oosIdx >= 0 ? message[oosIdx..] : message;
            int colon = tail.IndexOf(": ", StringComparison.Ordinal);
            int isOut = tail.IndexOf(" is out of scope", StringComparison.Ordinal);
            if (colon > 0 && isOut > colon)
            {
                var api = tail[(colon + 2)..isOut].Trim();
                return $"out-of-scope/{api}";
            }
            return "out-of-scope/unknown";
        }

        // Classify by the FIRST (innermost) BC stack frame — that's where the actual NRE
        // originates. Looking anywhere in the stack mis-buckets every AL test as
        // NavMethodScope because every AL method body wraps in a NavMethodScope.
        var first = full.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("at Microsoft.Dynamics.Nav."));
        if (first != null)
        {
            // Strip "at Microsoft.Dynamics.Nav.<Group>." prefix and the parameter list.
            var cleaned = first;
            int paren = cleaned.IndexOf('(');
            if (paren > 0) cleaned = cleaned[..paren];
            cleaned = cleaned.Replace("at Microsoft.Dynamics.Nav.", "");
            return $"runtime/{cleaned}";
        }
        // Fallbacks for non-BC frames or empty stacks.
        if (message.Contains("MissingMethodException")) return "runtime/missing-method";
        if (message.Contains("MissingFieldException")) return "runtime/missing-field";
        if (message.Contains("TypeInitializationException")) return "runtime/cctor";
        if (message.Contains("InvalidCastException")) return "runtime/cast";
        if (message.Contains("PlatformNotSupportedException")) return "runtime/platform-not-supported";
        if (message.Contains("NullReferenceException")) return "runtime/null-deref";
        return "runtime/other";
    }
}
