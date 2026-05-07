using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Install kernel32 shim BEFORE touching BC types (their static ctors P/Invoke into kernel32)
Kernel32Shim.EnsureRegistered();

// Force-load BC DLLs so we can hook NavEnvironment.cctor BEFORE first access.
var serviceTier = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".local/share/al-runner/artifacts/27.5.46862.48827");
foreach (var n in new[] { "Microsoft.Dynamics.Nav.Common", "Microsoft.Dynamics.Nav.Types", "Microsoft.Dynamics.Nav.Language", "Microsoft.Dynamics.Nav.Ncl" })
    Assembly.LoadFrom(System.IO.Path.Combine(serviceTier, n + ".dll"));
var navNcl = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
LinuxBootstrap.Apply(navNcl);

// Spike: try to load SpikeBuild.dll (compiled directly against real BC DLLs)
// and invoke the Discount Calculator codeunit's ApplyDiscount method.
// Surfaces the next layer of blockers — what real BC DLLs need at runtime.

var spikeBuild = Assembly.LoadFrom(
    System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Debug", "net8.0", "SpikeBuild.dll")));

Console.WriteLine($"Loaded {spikeBuild.GetName()}");

var calc = spikeBuild.GetTypes().FirstOrDefault(t => t.Name == "Codeunit50100");
if (calc is null) { Console.WriteLine("Codeunit50100 not found"); return 1; }
Console.WriteLine($"Found {calc.FullName}, base = {calc.BaseType?.FullName}");

// Try to instantiate. Pass a stub ITreeObject — NavComplexValue's null-check
// is satisfied; AL Runner's AlScope confirms BC ctors only store it as a field.
try
{
    var ctor = calc.GetConstructors().First();
    Console.WriteLine($"Calling ctor: {ctor}");
    var stub = new StubTreeObject();
    var instance = ctor.Invoke(new object[] { stub });
    Console.WriteLine($"Instantiated: {instance}");

    var apply = calc.GetMethod("ApplyDiscount");
    Console.WriteLine($"Found method: {apply}");
    var paramTypes = apply.GetParameters().Select(p => p.ParameterType).ToArray();
    Console.WriteLine($"Param types: [{string.Join(", ", paramTypes.Select(t => t.FullName))}]");

    // Convert decimals to Decimal18 if needed
    var decimal18 = paramTypes[0];
    var fromDecimal = decimal18.GetMethod("op_Implicit", new[] { typeof(decimal) })
                    ?? decimal18.GetMethod("op_Explicit", new[] { typeof(decimal) })
                    ?? decimal18.GetMethod("FromDecimal", new[] { typeof(decimal) });
    Console.WriteLine($"Decimal18 conv: {fromDecimal}");
    object price = fromDecimal != null ? fromDecimal.Invoke(null, new object[] { 200m }) : (object)200m;
    object pct   = fromDecimal != null ? fromDecimal.Invoke(null, new object[] { 10m })  : (object)10m;
    var result = apply.Invoke(instance, new[] { price, pct });
    Console.WriteLine($"ApplyDiscount(200, 10) = {result}  (expected: 180)");
}
catch (Exception ex)
{
    Console.WriteLine($"BLOCKER at: {ex.GetType().Name}");
    Console.WriteLine(ex.ToString());
    return 2;
}
return 0;

static class Kernel32Shim
{
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _registered = false;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;
        var bcAssemblies = new System.Collections.Generic.HashSet<string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name ?? "";
            if (name.Contains("Nav.Types") || name.Contains("Nav.Ncl") ||
                name.Contains("Nav.Runtime") || name.Contains("Nav.Core") ||
                name.Contains("Nav.Common") || name.Contains("Nav.Language"))
            {
                if (bcAssemblies.Add(name))
                {
                    try { NativeLibrary.SetDllImportResolver(asm, Resolver); }
                    catch (InvalidOperationException) { }
                }
            }
        }
        // Also register on assemblies that load later
        AppDomain.CurrentDomain.AssemblyLoad += (_, a) =>
        {
            var name = a.LoadedAssembly.GetName().Name ?? "";
            if (name.Contains("Nav.Types") || name.Contains("Nav.Ncl") ||
                name.Contains("Nav.Runtime") || name.Contains("Nav.Core") ||
                name.Contains("Nav.Common") || name.Contains("Nav.Language"))
            {
                try { NativeLibrary.SetDllImportResolver(a.LoadedAssembly, Resolver); }
                catch (InvalidOperationException) { }
            }
        };
    }

    static readonly System.Collections.Generic.HashSet<string> _stubLibs = new(StringComparer.OrdinalIgnoreCase)
    { "kernel32", "kernel32.dll", "user32", "user32.dll", "wintrust", "wintrust.dll",
      "nclcsrts", "nclcsrts.dll", "dhcpcsvc", "dhcpcsvc.dll", "ntdsapi", "ntdsapi.dll",
      "advapi32", "advapi32.dll", "secur32", "secur32.dll", "iphlpapi", "iphlpapi.dll",
      "wtsapi32", "wtsapi32.dll", "userenv", "userenv.dll", "netapi32", "netapi32.dll",
      "psapi", "psapi.dll", "ws2_32", "ws2_32.dll", "shlwapi", "shlwapi.dll" };

    public static IntPtr Resolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (_stubLibs.Contains(libraryName)) return GetOrCreate();
        return IntPtr.Zero;
    }

    private static IntPtr GetOrCreate()
    {
        if (_handle != IntPtr.Zero) return _handle;
        var shimDir = Path.Combine(Path.GetTempPath(), "spike-win32-stubs");
        Directory.CreateDirectory(shimDir);
        // Source-of-truth: bc-linux's stubs file, copied into our repo at shims/win32_stubs.c
        var srcRepo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "shims", "win32_stubs.c"));
        var cFile = Path.Combine(shimDir, "win32_stubs.c");
        var soFile = Path.Combine(shimDir, "libwin32_stubs.so");
        if (File.Exists(srcRepo)) File.Copy(srcRepo, cFile, overwrite: true);
        else File.WriteAllText(cFile, SHIM_SOURCE);
        var psi = new System.Diagnostics.ProcessStartInfo("cc",
            $"-shared -fPIC -o \"{soFile}\" \"{cFile}\"")
        { RedirectStandardError = true, UseShellExecute = false };
        var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10000);
        if (proc.ExitCode != 0) throw new InvalidOperationException($"cc failed: {proc.StandardError.ReadToEnd()}");
        _handle = NativeLibrary.Load(soFile);
        return _handle;
    }

    private const string SHIM_SOURCE = @"
#include <stdint.h>
#include <string.h>
typedef uint16_t WCHAR;
static void u16copy(WCHAR* dst, const char* src, int max) {
    int i;
    for (i = 0; src[i] && i < max - 1; i++) dst[i] = (WCHAR)src[i];
    if (i < max) dst[i] = 0;
}
int LCIDToLocaleName(uint32_t lcid, WCHAR* buf, int bufSize, uint32_t flags) {
    const char* name = 0;
    switch (lcid) {
        case 1033: name = ""en-US""; break;
        case 1031: name = ""de-DE""; break;
        case 1036: name = ""fr-FR""; break;
        case 1034: name = ""es-ES""; break;
        case 1040: name = ""it-IT""; break;
        case 1043: name = ""nl-NL""; break;
        case 1044: name = ""nb-NO""; break;
        case 1045: name = ""pl-PL""; break;
        case 1046: name = ""pt-BR""; break;
        case 1049: name = ""ru-RU""; break;
        case 1053: name = ""sv-SE""; break;
        case 2052: name = ""zh-CN""; break;
        case 2057: name = ""en-GB""; break;
        case 0: case 127: name = """"; break;
        default: return 0;
    }
    int len = strlen(name);
    if (!buf || bufSize == 0) return len + 1;
    u16copy(buf, name, bufSize);
    return len + 1;
}
uint32_t GetLastError(void) { return 0; }
void SetLastError(uint32_t e) { }
";
}

class RootHandler : Microsoft.Dynamics.Nav.Runtime.TreeHandler
{
    static readonly System.Reflection.FieldInfo _fHost =
        typeof(Microsoft.Dynamics.Nav.Runtime.TreeHandler).GetField("hostObject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    public RootHandler(Microsoft.Dynamics.Nav.Runtime.ITreeObject host) : base()
    {
        // Flip IsDisposed off — IsDisposed is computed as (hostObject == null).
        _fHost.SetValue(this, host);
    }
}

class StubTreeObject : Microsoft.Dynamics.Nav.Runtime.ITreeObject
{
    readonly RootHandler _h;
    public StubTreeObject() { _h = new RootHandler(this); }
    Microsoft.Dynamics.Nav.Runtime.TreeHandler Microsoft.Dynamics.Nav.Runtime.ITreeObject.Tree => _h;
    Microsoft.Dynamics.Nav.Runtime.TreeObjectType Microsoft.Dynamics.Nav.Runtime.ITreeObject.Type => default;
    bool Microsoft.Dynamics.Nav.Runtime.ITreeObject.SingleThreaded => false;
}
