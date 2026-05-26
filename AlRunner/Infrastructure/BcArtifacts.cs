namespace AlRunnerV2.Infrastructure;

/// <summary>
/// Single source of truth for the BC service-tier artifact directory.
///
/// The runner downloads BC platform DLLs (Ncl/Types/Common/Language/CodeAnalysis +
/// their runtime closure) to <c>~/.local/share/al-runner/artifacts/&lt;bc-version&gt;/</c>.
/// The exact version is pinned by <c>AlRunner.csproj</c>'s <c>_BCVersion</c> at build
/// time, but runtime code cannot read MSBuild properties — so rather than scatter the
/// version string across the codebase (it was previously hardcoded in five places and
/// drifted on every bump), we resolve the highest-version directory present. This
/// mirrors <see cref="BcCompiler"/>'s symbol-dir resolution and tracks the csproj pin
/// automatically as long as the artifact dir for that version exists.
/// </summary>
public static class BcArtifacts
{
    private static readonly Lazy<string> _serviceTierDir = new(ResolveHighestArtifactDir);

    /// <summary>Highest-version artifact directory, or the legacy default if none found.</summary>
    public static string ServiceTierDir => _serviceTierDir.Value;

    private static string ResolveHighestArtifactDir()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local/share/al-runner/artifacts");
        var best = !Directory.Exists(root) ? null : Directory.EnumerateDirectories(root)
            .Select(d => (Dir: d, Ver: System.Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
            .Where(t => t.Ver != null)
            .OrderByDescending(t => t.Ver)
            .Select(t => t.Dir)
            .FirstOrDefault();
        // Fall back to a stable path if the cache is empty (e.g. first build before download).
        return best ?? Path.Combine(root, "0.0.0.0");
    }
}
