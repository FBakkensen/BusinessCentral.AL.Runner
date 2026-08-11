// ExpectationManifestWiringTests — tests/expectations must actually reach the run.
//
// Issue #1734: AlRunner/Infrastructure/ExpectationManifest.cs implemented the whole
// classification table in docs/expectations.md and NOTHING ever called it. Every
// expectation entry was inert: an expect-oos test still failed the run, drift in either
// direction could never fire, and the documented escape hatch for corpus tests the
// runner cannot support did not exist.
//
// These tests spawn the real CLI against Fixtures/ExpectationsBundle (one codeunit,
// one method per classification path) plus Fixtures/ExpectationsManifest and pin the
// contract end-to-end:
//   - the reclassifying paths (pass-oos / pass-known-gap / skipped) reach the exit code,
//   - both drift directions fail the run with the documented diagnostics,
//   - a malformed manifest aborts startup loudly,
//   - without a manifest, behaviour is unchanged.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace AlRunner.Tests;

// See DefineFlagIntegrationTests for why runner-subprocess tests are serialized.
[Collection("server-serial")]
public sealed class ExpectationManifestWiringTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string ProjectPath = Path.Combine(RepoRoot, "AlRunner");
    private static readonly string SuitePath = Path.Combine(
        RepoRoot, "AlRunner.Tests", "Fixtures", "ExpectationsBundle", "suite");
    private static readonly string ManifestDir = Path.Combine(
        RepoRoot, "AlRunner.Tests", "Fixtures", "ExpectationsManifest");
    private static readonly string MalformedManifestDir = Path.Combine(
        RepoRoot, "AlRunner.Tests", "Fixtures", "ExpectationsManifestMalformed");

    private static bool ArtifactsPresent()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var stdCache = Path.Combine(home, ".local", "share", "al-runner", "artifacts");
        return Directory.Exists(stdCache) && Directory.EnumerateDirectories(stdCache).Any();
    }

    private static (string Output, int Exit) RunRunner(string runnerArgs, string? workingDir = null)
    {
        var args = new StringBuilder(TestBuildConfig.RunArgs(ProjectPath));
        args.Append(TestBuildConfig.BcVersionArg);
        args.Append(' ').Append(runnerArgs);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet", Arguments = args.ToString(),
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = workingDir ?? RepoRoot,
        };
        var sb = new StringBuilder();
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(240_000)) { try { p.Kill(true); } catch { } throw new TimeoutException("runner hung"); }
        p.WaitForExit();
        lock (sb) return (sb.ToString(), p.ExitCode);
    }

    private static void AssertCount(string output, string label, int expected)
    {
        var m = Regex.Match(output, Regex.Escape(label) + @"\s*(\d+)");
        Assert.True(m.Success, $"summary must report a '{label}' count.\n{output}");
        Assert.True(int.Parse(m.Groups[1].Value) == expected,
            $"expected {label} {expected}, got {m.Groups[1].Value}.\n{output}");
    }

    /// <summary>
    /// The reclassifying paths in one run: a plain pass, a declared OOS throw, a
    /// declared known-gap failure, and a declared skip. All four must land the run at
    /// exit 0, with each reclassified count reported DISTINCTLY (a green run that got
    /// there via quarantined tests must not read as an unqualified green), and the
    /// skip-declared body must never execute.
    /// </summary>
    [Fact]
    public void DeclaredExpectations_ReclassifyToGreen_AndReachTheExitCode()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifacts not provisioned"); return; }

        var (output, exit) = RunRunner(
            $"--expectations \"{ManifestDir}\" --test GreenPath \"{SuitePath}\"");

        // The skip entry must prevent INVOCATION, not just hide the result.
        Assert.DoesNotContain("SKIP-DECLARED TEST BODY RAN", output, StringComparison.Ordinal);

        // Each reclassified bucket is reported distinctly, per docs/expectations.md.
        AssertCount(output, "pass-oos:", 1);
        AssertCount(output, "pass-known-gap:", 1);
        AssertCount(output, "skipped:", 1);
        AssertCount(output, "  fail:", 0);

        // The whole point of #1734: the reclassification reaches the exit code.
        Assert.True(exit == 0,
            $"declared expectations must reclassify to a green run. exit={exit}\n{output}");
    }

    /// <summary>
    /// Both drift directions, in one run: entries whose tests now pass (expect-oos and
    /// expect-fail-known-gap) and an OOS throw with no entry. Each must surface its
    /// documented diagnostic and the run must exit non-zero — manifest drift is loud.
    /// </summary>
    [Fact]
    public void ManifestDrift_BothDirections_FailTheRunLoudly()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifacts not provisioned"); return; }

        var (output, exit) = RunRunner($"--expectations \"{ManifestDir}\" \"{SuitePath}\"");

        // Direction 1a: expect-oos entry whose test passes → remove the entry.
        Assert.Contains("runner now supports this surface", output, StringComparison.Ordinal);
        // Direction 1b: known-gap entry whose test passes → remove the entry, close the issue.
        Assert.Contains("close the linked issue", output, StringComparison.Ordinal);
        // Direction 2: undeclared OOS throw → add an entry.
        Assert.Contains("Add an expect-oos entry", output, StringComparison.Ordinal);

        // The three drift methods are the only failures; the green-path methods still
        // reclassify (drift must not disable classification for the rest of the run).
        AssertCount(output, "pass-oos:", 1);
        AssertCount(output, "pass-known-gap:", 1);
        AssertCount(output, "skipped:", 1);
        AssertCount(output, "  fail:", 3);

        Assert.True(exit == 1,
            $"manifest drift must fail the run (exit 1 = test failures). exit={exit}\n{output}");
    }

    /// <summary>
    /// A malformed manifest (unknown Mode) must abort startup loudly, naming the file
    /// and the bad value — never run tests against a manifest it could not parse.
    /// </summary>
    [Fact]
    public void MalformedManifest_AbortsStartupLoudly()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifacts not provisioned"); return; }

        var (output, exit) = RunRunner(
            $"--expectations \"{MalformedManifestDir}\" \"{SuitePath}\"");

        Assert.Contains("unknown Mode 'expect-magic'", output, StringComparison.Ordinal);
        Assert.True(exit == 2,
            $"a malformed manifest is a bad invocation and must exit 2 without running tests. exit={exit}\n{output}");
        // Startup aborted — nothing may have run. The loader's diagnostic quotes the
        // entry by AL object name, so probe for the CLR type name ("Codeunit60810"),
        // which only per-test run output produces.
        Assert.DoesNotContain("Codeunit60810", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Negative direction: with NO manifest (cwd without tests/expectations and no
    /// --expectations flag), behaviour is unchanged — an uncaught OOS throw is a plain
    /// FAIL without any drift diagnostic. Without this, the assertions above would
    /// still hold if classification ran unconditionally and rewrote every user-facing
    /// OOS failure into manifest advice.
    /// </summary>
    [Fact]
    public void NoManifest_UnchangedBehaviour_OosIsAPlainFail()
    {
        if (!ArtifactsPresent()) { Console.Error.WriteLine("[skip] BC artifacts not provisioned"); return; }

        var (output, exit) = RunRunner(
            $"--test Drift_OosThrownButNoEntry \"{SuitePath}\"",
            workingDir: Path.Combine(RepoRoot, "AlRunner.Tests", "Fixtures"));

        Assert.DoesNotContain("Add an expect-oos entry", output, StringComparison.Ordinal);
        AssertCount(output, "  fail:", 1);
        Assert.True(exit == 1, $"an uncaught OOS throw stays a failing test. exit={exit}\n{output}");
    }
}
