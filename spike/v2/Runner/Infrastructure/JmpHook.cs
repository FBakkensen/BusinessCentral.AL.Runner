// JmpHook — patches a JIT'd method's entry point with an x86-64 absolute-indirect JMP
// to a replacement method. Uses mprotect to make the page writable.
//
// .NET 8 lays out method entries as one of three precode shapes; we follow them through
// to the actual JIT'd code so the JMP lands in the right place when the original was
// already JIT-compiled before we hooked it.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlRunnerV2.Infrastructure;

internal static class JmpHook
{
    [DllImport("libc", SetLastError = true)]
    private static extern int mprotect(IntPtr addr, nuint len, int prot);
    private const int PROT_READ = 1, PROT_WRITE = 2, PROT_EXEC = 4;

    public static void Apply(MethodBase original, MethodInfo replacement, string name)
    {
        RuntimeHelpers.PrepareMethod(original.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
        var origFp = original.MethodHandle.GetFunctionPointer();
        var replFp = replacement.MethodHandle.GetFunctionPointer();

        IntPtr compiledCode = IntPtr.Zero;
        try
        {
            byte[] precode = new byte[24];
            Marshal.Copy(origFp, precode, 0, 24);
            // .NET 8 x64 FixupPrecode: MOV r10,MD ; JMP [rip+disp32]
            if (precode[10] == 0xFF && precode[11] == 0x25)
                compiledCode = Marshal.ReadIntPtr(origFp + 16 + BitConverter.ToInt32(precode, 12));
            // StubPrecode
            if (compiledCode == IntPtr.Zero && precode[0] == 0xFF && precode[1] == 0x25)
                compiledCode = Marshal.ReadIntPtr(origFp + 6 + BitConverter.ToInt32(precode, 2));
            // E9 relative
            if (compiledCode == IntPtr.Zero && precode[0] == 0xE9)
                compiledCode = origFp + 5 + BitConverter.ToInt32(precode, 1);
        }
        catch { }

        WriteJmp(origFp, replFp);
        if (compiledCode != IntPtr.Zero && compiledCode != origFp && compiledCode != replFp)
            try { WriteJmp(compiledCode, replFp); } catch { }
    }

    private static void WriteJmp(IntPtr target, IntPtr destination)
    {
        // x86-64 absolute indirect: FF 25 00 00 00 00 [imm64]
        byte[] jmp = new byte[14];
        jmp[0] = 0xFF; jmp[1] = 0x25;
        BitConverter.GetBytes(destination.ToInt64()).CopyTo(jmp, 6);
        long pageSize = 4096;
        long addr = target.ToInt64();
        long pageStart = addr & ~(pageSize - 1);
        var regionSize = (nuint)((addr - pageStart) + jmp.Length + pageSize);
        if (mprotect(new IntPtr(pageStart), regionSize, PROT_READ | PROT_WRITE | PROT_EXEC) != 0)
        {
            Console.Error.WriteLine($"[JmpHook.WriteJmp] mprotect FAILED for target=0x{target:X} errno={Marshal.GetLastSystemError()}");
            return;
        }
        Marshal.Copy(jmp, 0, target, jmp.Length);
    }
}
