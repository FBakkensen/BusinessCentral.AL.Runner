// InProcessAppPackager — emit a source bundle dir as a real .app package in-process.
//
// Strategy: build a synthetic NAVX .app (= 40-byte BC header + zip) that contains
// NavxManifest.xml and all src/*.al files. This is sufficient for:
//   • DependencyResolver.Resolve — reads NavxManifest.xml for identity
//   • AppLoader.ExtractAl — reads src/*.al for Tier-3 compile-on-the-fly
//
// We intentionally do NOT use PackageModuleOutputter: that API requires the AL
// compiler's Compilation.Emit to succeed without AL1153 errors, but the BC 28.x
// artifact packages (runtime 17.0) exceed the v27.5 CodeAnalysis.dll's known
// runtime ceiling (16.1). The manual approach is simpler and fully sufficient for
// the dependency-resolution + DependencyLoader.LoadAll paths.
//
// CONTRACT: the caller must have called BcRuntime.EnsureApplied() and
// DependencyLoader.EnsureResolverInstalled_Public() before invoking EmitAppPackage.

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AlRunnerV2.Infrastructure;

/// <summary>
/// Identity read from a bundle's app.json, used to synthesize a NAVX .app.
/// </summary>
public sealed record BundleIdentity(
    Guid AppId,
    string Name,
    string Publisher,
    Version Version,
    Version RuntimeVersion,
    IReadOnlyList<DependencyRef> Dependencies);

public static class InProcessAppPackager
{
    // NAVX header magic bytes (BC .app format).
    // Bytes 0-3: 'N','A','V','X'
    // Bytes 4-7: LE uint32 = offset of the zip data within the file (= 8 for our output).
    private static readonly byte[] NavxMagic = [(byte)'N', (byte)'A', (byte)'V', (byte)'X'];
    private const uint NavxZipOffset = 8; // immediately after the 8-byte header

    /// <summary>
    /// Read the identity (id/name/publisher/version/runtime/dependencies) from an app.json.
    /// Returns null if the file does not exist or cannot be parsed.
    /// </summary>
    public static BundleIdentity? ReadIdentity(string appJsonPath)
    {
        if (!File.Exists(appJsonPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appJsonPath));
            var root = doc.RootElement;

            var idStr = root.TryGetProperty("id", out var pid) ? pid.GetString() : null;
            var name = root.TryGetProperty("name", out var pn) ? pn.GetString() ?? "Unknown" : "Unknown";
            var pub = root.TryGetProperty("publisher", out var pp) ? pp.GetString() ?? "Unknown" : "Unknown";
            var verStr = root.TryGetProperty("version", out var pv) ? pv.GetString() ?? "1.0.0.0" : "1.0.0.0";
            var rtStr = root.TryGetProperty("runtime", out var pr) ? pr.GetString() ?? "1.0" : "1.0";

            Guid appId = Guid.Empty;
            if (!string.IsNullOrEmpty(idStr)) Guid.TryParse(idStr, out appId);
            if (!Version.TryParse(verStr, out var ver)) ver = new Version(1, 0, 0, 0);
            if (!Version.TryParse(rtStr, out var rtVer)) rtVer = new Version(1, 0);

            var deps = new List<DependencyRef>();
            if (root.TryGetProperty("dependencies", out var depsEl)
                && depsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in depsEl.EnumerateArray())
                {
                    var dIdStr = d.TryGetProperty("id", out var di) ? di.GetString() : null;
                    var dName = d.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "";
                    var dPub = d.TryGetProperty("publisher", out var dp) ? dp.GetString() ?? "" : "";
                    var dVerStr = d.TryGetProperty("version", out var dv) ? dv.GetString() ?? "0.0.0.0" : "0.0.0.0";
                    Guid dId = Guid.Empty;
                    if (!string.IsNullOrEmpty(dIdStr)) Guid.TryParse(dIdStr, out dId);
                    if (!Version.TryParse(dVerStr, out var dVer)) dVer = new Version(0, 0, 0, 0);
                    deps.Add(new DependencyRef(dId, dName, dPub, dVer));
                }
            }
            // Inject implicit MS deps from application/platform fields (same logic as
            // Program.cs ReadDependencies) so the reference loader resolves them.
            foreach (var (field, implName) in new[] { ("application", "Application"), ("platform", "System") })
            {
                if (root.TryGetProperty(field, out var fv)
                    && fv.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(fv.GetString()))
                {
                    if (!Version.TryParse(fv.GetString(), out var iv)) iv = new Version(0, 0, 0, 0);
                    deps.Add(new DependencyRef(Guid.Empty, implName, "Microsoft", iv, Optional: true));
                }
            }

            return new BundleIdentity(appId, name, pub, ver, rtVer, deps);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[layered] InProcessAppPackager: failed to read {appJsonPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Emit a bundle directory as a synthetic NAVX .app package to <paramref name="outPath"/>.
    ///
    /// The .app contains:
    ///   • NavxManifest.xml  — identity, used by DependencyResolver.Resolve
    ///   • src/*.al           — AL sources, used by DependencyLoader Tier-3 compile-on-the-fly
    ///
    /// Throws loudly on any failure — never silently swallows.
    /// </summary>
    public static void EmitAppPackageToFile(
        string bundleDir,
        BundleIdentity identity,
        string outPath)
    {
        // Collect AL files.
        var alFiles = Directory.EnumerateFiles(bundleDir, "*.al", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (alFiles.Count == 0)
            throw new InvalidOperationException(
                $"[layered] InProcessAppPackager: no .al files found under {bundleDir}");

        // Build NavxManifest.xml content.
        var manifestXml = BuildNavxManifestXml(identity);

        // Write NAVX header + zip to file.
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteNavxApp(fs, manifestXml, bundleDir, alFiles);
    }

    // ── internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Write the NAVX header + zip to <paramref name="outStream"/>.
    /// Format: 8-byte header (NAVX + LE uint32 offset=8) followed immediately by a zip.
    /// </summary>
    private static void WriteNavxApp(
        Stream outStream,
        string manifestXml,
        string bundleDir,
        IReadOnlyList<string> alFiles)
    {
        // Write NAVX magic header: 'N','A','V','X' + LE uint32 zip-offset (=8).
        outStream.Write(NavxMagic, 0, 4);
        var offsetBytes = BitConverter.GetBytes(NavxZipOffset); // little-endian on x64 Linux
        if (!BitConverter.IsLittleEndian)
        {
            // Ensure little-endian regardless of host byte order.
            Array.Reverse(offsetBytes);
        }
        outStream.Write(offsetBytes, 0, 4);

        // Write the zip archive directly after the header.
        using var zip = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true);

        // NavxManifest.xml
        var manifestEntry = zip.CreateEntry("NavxManifest.xml", CompressionLevel.Optimal);
        using (var mw = manifestEntry.Open())
        {
            var xmlBytes = Encoding.UTF8.GetBytes(manifestXml);
            mw.Write(xmlBytes, 0, xmlBytes.Length);
        }

        // src/<filename>.al for every .al file in the bundle.
        foreach (var alPath in alFiles)
        {
            var entryName = "src/" + Path.GetFileName(alPath);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var ew = entry.Open();
            using var fr = File.OpenRead(alPath);
            fr.CopyTo(ew);
        }
    }

    /// <summary>
    /// Build a minimal NavxManifest.xml string from a <see cref="BundleIdentity"/>.
    /// AppLoader.ReadManifest reads: App/@Id, @Name, @Publisher, @Version
    /// and Dependencies/Dependency elements.
    /// </summary>
    private static string BuildNavxManifestXml(BundleIdentity identity)
    {
        XNamespace ns = "http://schemas.microsoft.com/navx/2015/manifest";

        var depsEl = new XElement(ns + "Dependencies");
        // Only include the explicit user deps (not the implicit platform/application ones
        // that were injected for reference-loader purposes) so the manifest stays clean.
        foreach (var dep in identity.Dependencies.Where(d => !d.Optional))
        {
            depsEl.Add(new XElement(ns + "Dependency",
                new XAttribute("Id", dep.AppId == Guid.Empty ? "" : dep.AppId.ToString()),
                new XAttribute("Name", dep.Name),
                new XAttribute("Publisher", dep.Publisher),
                new XAttribute("MinVersion", dep.Version.ToString())));
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "Package",
                new XAttribute("xmlns", ns.NamespaceName),
                new XElement(ns + "App",
                    new XAttribute("Id", identity.AppId.ToString()),
                    new XAttribute("Name", identity.Name),
                    new XAttribute("Publisher", identity.Publisher),
                    new XAttribute("Version", identity.Version.ToString()),
                    new XAttribute("ShowMyCode", "true")),
                depsEl));

        using var sw = new StringWriter();
        doc.Save(sw);
        return sw.ToString();
    }
}
