// Win32Stubs — installs a P/Invoke resolver redirecting Win32 imports to a Linux .so
// built from bc-linux's win32_stubs.c.
using System.Reflection;
using System.Runtime.InteropServices;

namespace AlRunnerV2;

internal static class Win32Stubs
{
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _registered;

    private static readonly HashSet<string> _libs = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32", "kernel32.dll", "user32", "user32.dll", "wintrust", "wintrust.dll",
        "nclcsrts", "nclcsrts.dll", "dhcpcsvc", "dhcpcsvc.dll", "ntdsapi", "ntdsapi.dll",
        "advapi32", "advapi32.dll", "secur32", "secur32.dll", "iphlpapi", "iphlpapi.dll",
        "wtsapi32", "wtsapi32.dll", "userenv", "userenv.dll", "netapi32", "netapi32.dll",
        "psapi", "psapi.dll", "ws2_32", "ws2_32.dll", "shlwapi", "shlwapi.dll",
    };

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            TryRegister(asm);
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryRegister(e.LoadedAssembly);
    }

    private static void TryRegister(Assembly asm)
    {
        var n = asm.GetName().Name ?? "";
        if (!n.Contains("Nav.")) return;
        try { NativeLibrary.SetDllImportResolver(asm, Resolver); }
        catch (InvalidOperationException) { /* already registered */ }
    }

    private static IntPtr Resolver(string library, Assembly asm, DllImportSearchPath? sp)
    {
        if (!_libs.Contains(library)) return IntPtr.Zero;
        try { return GetOrBuild(); }
        catch (Exception ex) { Console.WriteLine($"[Win32Stubs] build failed for {library}: {ex.Message}"); return IntPtr.Zero; }
    }

    private static IntPtr GetOrBuild()
    {
        if (_handle != IntPtr.Zero) return _handle;
        var dir = Path.Combine(Path.GetTempPath(), "alrunner-v2-win32-stubs");
        Directory.CreateDirectory(dir);
        // From bin/Debug/net8.0 → up 3 → spike/v2/Runner → up 2 → spike → bc-abi-identity/shims/win32_stubs.c
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "bc-abi-identity", "shims", "win32_stubs.c"));
        if (!File.Exists(src))
            throw new FileNotFoundException($"Win32 stubs source not found at {src}");
        var cFile = Path.Combine(dir, "win32_stubs.c");
        var soFile = Path.Combine(dir, "libwin32_stubs.so");
        if (File.Exists(src)) File.Copy(src, cFile, overwrite: true);
        var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cc",
            $"-shared -fPIC -o \"{soFile}\" \"{cFile}\"")
        { RedirectStandardError = true, UseShellExecute = false })!;
        proc.WaitForExit(10000);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"cc failed: {proc.StandardError.ReadToEnd()}");
        _handle = NativeLibrary.Load(soFile);
        return _handle;
    }
}
