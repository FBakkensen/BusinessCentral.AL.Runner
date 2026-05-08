// DependencyResolver — turns a bucket-level app.json dependency list +
// a set of package-cache dirs into a topologically-sorted list of (manifest, appPath).
//
// Indexes every `.app` under the cache dirs by AppId (with (Name, Publisher)
// as a fallback for declarations missing a GUID). Recursively expands declared
// deps via NavxManifest.xml's <Dependencies>. Detects cycles via colour-marker
// DFS. Output order = post-order DFS = topological order (deps before dependents).
//
// Throws on unresolved references with the requested name + version + the cache
// dirs that were searched, so the failure mode is obviously a missing-package
// problem and not a runner bug.
using System.Collections.Concurrent;

namespace AlRunnerV2;

public sealed class DependencyResolver
{
    private readonly IReadOnlyList<string> _cacheDirs;
    private readonly Dictionary<Guid, (AppManifest Manifest, string Path)> _byId = new();
    private readonly Dictionary<(string Name, string Publisher), (AppManifest Manifest, string Path)> _byNamePub
        = new(NamePublisherComparer.Instance);
    private bool _indexed;

    public DependencyResolver(IReadOnlyList<string> cacheDirs)
    {
        _cacheDirs = cacheDirs;
    }

    /// <summary>
    /// Resolve a list of root deps (typically the bucket's app.json
    /// <c>dependencies</c>) and return the full transitive closure in
    /// topological order (deps before dependents).
    /// </summary>
    public IReadOnlyList<(AppManifest Manifest, string AppPath)> Resolve(
        IEnumerable<DependencyRef> roots)
    {
        EnsureIndexed();

        var visited = new Dictionary<Guid, byte>(); // 0 = unvisited, 1 = on-stack, 2 = done
        var result = new List<(AppManifest, string)>();

        foreach (var root in roots)
            Visit(root, visited, result, new Stack<string>());

        return result;
    }

    private void Visit(
        DependencyRef dep,
        Dictionary<Guid, byte> state,
        List<(AppManifest, string)> output,
        Stack<string> stack)
    {
        if (!TryFind(dep, out var found))
            throw new InvalidOperationException(
                $"Dependency not found: {dep.Publisher}/{dep.Name} v{dep.Version} " +
                $"(id={dep.AppId}). Searched: {string.Join(", ", _cacheDirs)}. " +
                $"Stack: {string.Join(" -> ", stack.Reverse())}");

        var id = found.Manifest.AppId;
        if (state.TryGetValue(id, out var s))
        {
            if (s == 1)
                throw new InvalidOperationException(
                    $"Dependency cycle detected at {found.Manifest.Name}: " +
                    $"{string.Join(" -> ", stack.Reverse())} -> {found.Manifest.Name}");
            if (s == 2) return;
        }

        state[id] = 1;
        stack.Push(found.Manifest.Name);
        foreach (var child in found.Manifest.Dependencies)
            Visit(child, state, output, stack);
        stack.Pop();
        state[id] = 2;
        output.Add((found.Manifest, found.Path));
    }

    private bool TryFind(DependencyRef dep, out (AppManifest Manifest, string Path) found)
    {
        if (dep.AppId != Guid.Empty && _byId.TryGetValue(dep.AppId, out found))
            return true;
        if (_byNamePub.TryGetValue((dep.Name, dep.Publisher), out found))
            return true;
        found = default;
        return false;
    }

    private void EnsureIndexed()
    {
        if (_indexed) return;
        foreach (var dir in _cacheDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.app", SearchOption.AllDirectories))
            {
                var m = AppLoader.ReadManifest(file);
                if (m == null) continue;
                // First-wins for identical AppIds (multiple cache dirs may overlap).
                if (!_byId.ContainsKey(m.AppId)) _byId[m.AppId] = (m, file);
                var key = (m.Name, m.Publisher);
                if (!_byNamePub.ContainsKey(key)) _byNamePub[key] = (m, file);
            }
        }
        _indexed = true;
    }

    private sealed class NamePublisherComparer : IEqualityComparer<(string Name, string Publisher)>
    {
        public static readonly NamePublisherComparer Instance = new();
        public bool Equals((string Name, string Publisher) x, (string Name, string Publisher) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Publisher, y.Publisher);
        public int GetHashCode((string Name, string Publisher) o)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(o.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(o.Publisher));
    }
}
