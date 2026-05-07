// AlEmitter — drives the existing AlRunner CLI to emit C# from AL source.
//
// Uses --dump-csharp (pre-rewrite): v2's premise is unmodified-AL-C#-against-
// real-BC-DLLs, no v1 rewriter in the dependency chain. The pre-rewrite output
// only compiles when each suite stays its own compilation unit (cross-suite
// var/byref calls cross a type-rewrite seam), so callers should bundle the
// whole bucket into ONE emit subprocess for speed but split the resulting C#
// blocks back into per-suite groups for the Roslyn compile step.
using System.Diagnostics;
using System.Text;

namespace AlRunnerV2;

public sealed record EmittedSource(string Name, string Code);

public sealed class AlEmitter
{
    public string AlRunnerDll { get; init; } =
        Path.Combine(RepoRoot, "AlRunner/bin/Debug/net8.0/AlRunner.dll");

    public static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    /// <summary>
    /// Convenience overload for the original (src, test) pair.
    /// </summary>
    public IReadOnlyList<EmittedSource> Emit(string srcDir, string testDir)
        => Emit(new[] { srcDir, testDir });

    /// <summary>
    /// Bundle-mode emit: hand the existing AlRunner an arbitrary set of source folders
    /// (one or many per bucket — every unnamed CLI arg is a folder, and the runner
    /// combines them into a single AL compilation). This matches how the existing
    /// CI loop in test-matrix.yml drives AlRunner.
    /// Subprocess startup amortises across the whole bucket (~1.8s once, not per-suite).
    /// </summary>
    public IReadOnlyList<EmittedSource> Emit(IEnumerable<string> dirs)
    {
        var dirsList = dirs.Where(Directory.Exists).Distinct().ToList();
        if (dirsList.Count == 0)
            throw new InvalidOperationException("AlEmitter.Emit: no source folders to compile");
        var argSb = new StringBuilder($"\"{AlRunnerDll}\" --dump-csharp");
        foreach (var d in dirsList) argSb.Append(' ').Append('"').Append(d).Append('"');
        var psi = new ProcessStartInfo("dotnet", argSb.ToString())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        var parsed = Parse(output);
        // AlRunner --dump-csharp emits C# AND ALSO executes the tests in-process, so a
        // non-zero exit just means some v1 tests failed. We only care about whether the
        // C# blocks parsed successfully — that's what v2 needs for the Roslyn-against-real-
        // BC-DLLs path. Real emit failures show up as zero "=== Generated C# for X ===" headers.
        if (parsed.Count == 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"AlRunner --dump-csharp produced no C# (exit {proc.ExitCode}):\n{err}");
        }
        return parsed;
    }

    internal static IReadOnlyList<EmittedSource> Parse(string output)
    {
        var lines = output.Split('\n');
        var result = new List<EmittedSource>();
        string? currentName = null;
        var buf = new StringBuilder();
        foreach (var line in lines)
        {
            // Header: "=== Generated C# for {Name} (before rewriting) ===" — from --dump-csharp
            //         "=== Rewritten C# for {Name} ==="                    — from --dump-rewritten
            const string genPrefix = "=== Generated C# for ";
            const string genSuffix = " (before rewriting) ===";
            const string rewPrefix = "=== Rewritten C# for ";
            const string rewSuffix = " ===";
            string trimmed = line.TrimEnd('\r');
            if (line.StartsWith(genPrefix) && trimmed.EndsWith(genSuffix))
            {
                currentName = line[genPrefix.Length..^genSuffix.Length];
                buf.Clear();
                continue;
            }
            if (line.StartsWith(rewPrefix) && trimmed.EndsWith(rewSuffix) && !line.StartsWith("=== End "))
            {
                currentName = line[rewPrefix.Length..^rewSuffix.Length];
                buf.Clear();
                continue;
            }
            // Footer: "=== End {Name} ==="
            if (currentName != null && line.StartsWith("=== End ") && trimmed.EndsWith(" ==="))
            {
                result.Add(new EmittedSource(currentName, buf.ToString()));
                currentName = null;
                continue;
            }
            if (currentName != null) buf.AppendLine(trimmed);
        }
        return result;
    }
}
