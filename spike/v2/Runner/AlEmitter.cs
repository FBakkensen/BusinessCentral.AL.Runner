// AlEmitter — drives the existing AlRunner CLI to emit C# from AL source.
// We treat AL→C# as a black box for now (the existing pipeline handles
// dependency resolution, packages, manifest, etc.). Net new work is
// downstream of emission.
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

    public IReadOnlyList<EmittedSource> Emit(string srcDir, string testDir)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{AlRunnerDll}\" --dump-csharp \"{srcDir}\" \"{testDir}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"AlRunner --dump-csharp failed (exit {proc.ExitCode}):\n{err}");
        }
        return Parse(output);
    }

    internal static IReadOnlyList<EmittedSource> Parse(string output)
    {
        var lines = output.Split('\n');
        var result = new List<EmittedSource>();
        string? currentName = null;
        var buf = new StringBuilder();
        foreach (var line in lines)
        {
            // Header: "=== Generated C# for {Name} (before rewriting) ==="
            const string headerPrefix = "=== Generated C# for ";
            const string headerSuffix = " (before rewriting) ===";
            if (line.StartsWith(headerPrefix) && line.TrimEnd('\r').EndsWith(headerSuffix))
            {
                currentName = line[headerPrefix.Length..^headerSuffix.Length];
                buf.Clear();
                continue;
            }
            // Footer: "=== End {Name} ==="
            if (currentName != null && line.StartsWith("=== End ") && line.TrimEnd('\r').EndsWith(" ==="))
            {
                result.Add(new EmittedSource(currentName, buf.ToString()));
                currentName = null;
                continue;
            }
            if (currentName != null) buf.AppendLine(line.TrimEnd('\r'));
        }
        return result;
    }
}
