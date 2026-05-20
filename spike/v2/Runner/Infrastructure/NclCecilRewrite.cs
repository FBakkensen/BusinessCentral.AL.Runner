// NclCecilRewrite — spike: rewrite Microsoft.Dynamics.Nav.Ncl.dll IL at load time
// to neutralize R2R-trapped methods that JmpHook and EventPipe-post-JIT can't reach.
//
// Allowed surface per .claude/rules/precompiled-dll-respect.md: Ncl.dll is runtime engine,
// not BaseApplication / SystemApplication / ISV business logic.
using System;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AlRunnerV2.Infrastructure;

public static class NclCecilRewrite
{
    /// <summary>
    /// Reads Ncl.dll bytes, rewrites IsEventSubscribed body to return true,
    /// strips R2R header, returns modified bytes ready for Assembly.Load.
    /// </summary>
    public static byte[] RewriteNcl(string nclPath)
    {
        var originalBytes = File.ReadAllBytes(nclPath);

        var resolver = new DefaultAssemblyResolver();
        var dir = Path.GetDirectoryName(nclPath)!;
        resolver.AddSearchDirectory(dir);

        using var inStream = new MemoryStream(originalBytes);
        var asm = AssemblyDefinition.ReadAssembly(inStream, new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver });

        var type = asm.MainModule.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        if (type == null)
            throw new InvalidOperationException("NCLMetaApplicationObject type not found in Ncl.dll");

        int rewroteCount = 0;
        foreach (var method in type.Methods.Where(mm => mm.Name == "IsEventSubscribed").ToList())
        {
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName}");
            if (method.ReturnType.FullName != "System.Boolean")
            {
                Console.Error.WriteLine($"[Cecil]  - skipping: return type is {method.ReturnType.FullName}");
                continue;
            }
            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));
            body.MaxStackSize = 1;
            rewroteCount++;
        }
        if (rewroteCount == 0)
            throw new InvalidOperationException("IsEventSubscribed method not found");
        Console.Error.WriteLine($"[Cecil] Rewrote {rewroteCount} IsEventSubscribed overload(s) → return true");

        // NavReport.SaveAsAsync → throw OOS (report-rendering is out-of-scope)
        var navReportType = asm.MainModule.GetType("Microsoft.Dynamics.Nav.Runtime.NavReport");
        if (navReportType == null)
            throw new InvalidOperationException("NavReport type not found in Ncl.dll — Ncl shape changed");

        var oosCtorInfo = typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })
            ?? throw new InvalidOperationException("InvalidOperationException(string) ctor not found via reflection");
        var oosCtor = asm.MainModule.ImportReference(oosCtorInfo);

        int saveAsRewroteCount = 0;
        foreach (var method in navReportType.Methods.Where(mm => mm.Name == "SaveAsAsync").ToList())
        {
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName}");
            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, "out-of-scope: NavReport.SaveAs — report-rendering — see docs/scope.md#report-rendering"));
            il.Append(il.Create(OpCodes.Newobj, oosCtor));
            il.Append(il.Create(OpCodes.Throw));
            body.MaxStackSize = 1;
            saveAsRewroteCount++;
        }
        if (saveAsRewroteCount == 0)
            throw new InvalidOperationException("SaveAsAsync method not found in NavReport — Ncl shape changed; do not commit");
        Console.Error.WriteLine($"[Cecil] Rewrote {saveAsRewroteCount} SaveAsAsync overload(s) → throw OOS");

        // NavReport.RunRequestPageAsync → throw OOS (request-page UI is out-of-scope)
        int runRequestPageRewroteCount = 0;
        foreach (var method in navReportType.Methods.Where(mm => mm.Name == "RunRequestPageAsync").ToList())
        {
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName}");
            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, "out-of-scope: NavReport.RunRequestPage — request-page-ui — see docs/scope.md#report-rendering"));
            il.Append(il.Create(OpCodes.Newobj, oosCtor));
            il.Append(il.Create(OpCodes.Throw));
            body.MaxStackSize = 1;
            runRequestPageRewroteCount++;
        }
        if (runRequestPageRewroteCount == 0)
            throw new InvalidOperationException("RunRequestPageAsync method not found in NavReport — Ncl shape changed; do not commit");
        Console.Error.WriteLine($"[Cecil] Rewrote {runRequestPageRewroteCount} RunRequestPageAsync overload(s) → throw OOS");

        // NavForm.GetMasterPage → return null/default (R2R-trapped; Cecil-rewrite is the only path)
        var navFormType = asm.MainModule.GetType("Microsoft.Dynamics.Nav.Runtime.NavForm");
        if (navFormType == null)
            throw new InvalidOperationException("NavForm type not found in Ncl.dll — Ncl shape changed; do not commit");

        int getMasterPageRewroteCount = 0;
        foreach (var method in navFormType.Methods.Where(mm => mm.Name == "GetMasterPage").ToList())
        {
            var returnType = method.ReturnType;
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName} → return null/default (ReturnType={returnType.FullName}, IsValueType={returnType.IsValueType})");

            if (returnType.FullName.StartsWith("System.Threading.Tasks.Task`"))
                throw new InvalidOperationException($"GetMasterPage returns Task<T> ({returnType.FullName}) — cannot safely emit default; do not commit");

            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();

            if (!returnType.IsValueType)
            {
                il.Append(il.Create(OpCodes.Ldnull));
                il.Append(il.Create(OpCodes.Ret));
                body.MaxStackSize = 1;
            }
            else if (returnType.FullName is "System.Int32" or "System.Boolean" or "System.Byte"
                                         or "System.Int16" or "System.Int64" or "System.Char")
            {
                il.Append(il.Create(OpCodes.Ldc_I4_0));
                il.Append(il.Create(OpCodes.Ret));
                body.MaxStackSize = 1;
            }
            else
            {
                // ValueTask<T>, ValueTuple<...>, or other value types → default(T) via initobj
                var local = new VariableDefinition(asm.MainModule.ImportReference(returnType));
                body.Variables.Add(local);
                body.InitLocals = true;
                il.Append(il.Create(OpCodes.Ldloca_S, local));
                il.Append(il.Create(OpCodes.Initobj, asm.MainModule.ImportReference(returnType)));
                il.Append(il.Create(OpCodes.Ldloc_0));
                il.Append(il.Create(OpCodes.Ret));
                body.MaxStackSize = 1;
            }
            getMasterPageRewroteCount++;
        }
        if (getMasterPageRewroteCount == 0)
            throw new InvalidOperationException("GetMasterPage method not found in NavForm — Ncl shape changed; do not commit");
        Console.Error.WriteLine($"[Cecil] Rewrote {getMasterPageRewroteCount} GetMasterPage overload(s) → return null/default");

        // NavForm.RequiresExecutePermissionCheck(MasterPage) → return false
        // GetMasterPage() now returns null/default, so its callers pass null into this method,
        // which then NREs when it dereferences the parameter. Since this is a permission-guard
        // inside InitializeFromMetadata, returning false (= no extra permission check needed)
        // is the safe stub behaviour for the runner environment (R2R-trapped; Cecil is only path).
        int requiresExecPermCheckRewroteCount = 0;
        foreach (var method in navFormType.Methods
            .Where(mm => mm.Name == "RequiresExecutePermissionCheck").ToList())
        {
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName} → return false");
            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));
            body.MaxStackSize = 1;
            requiresExecPermCheckRewroteCount++;
        }
        if (requiresExecPermCheckRewroteCount == 0)
            throw new InvalidOperationException("RequiresExecutePermissionCheck method not found in NavForm — Ncl shape changed; do not commit");
        Console.Error.WriteLine($"[Cecil] Rewrote {requiresExecPermCheckRewroteCount} RequiresExecutePermissionCheck overload(s) → return false");

        // NavForm.InitializeFromMetadata() → prepend null-guard on this.masterPage field.
        // The method reads this.masterPage at ~15 separate IL sites. When masterPage is null
        // (because no BC metadata is available in the runner), each site NREs in a cascade.
        // Adding an early-return when masterPage==null lets the form proceed without
        // metadata-dependent initialisation. Passing tests that have a non-null masterPage
        // field are unaffected — they fall through to the original code normally.
        var initFromMetadataMethod = navFormType.Methods
            .FirstOrDefault(mm => mm.Name == "InitializeFromMetadata" && mm.Parameters.Count == 0)
            ?? throw new InvalidOperationException("InitializeFromMetadata() not found in NavForm — Ncl shape changed; do not commit");
        var masterPageField = navFormType.Fields
            .FirstOrDefault(f => f.Name == "masterPage")
            ?? throw new InvalidOperationException("masterPage field not found in NavForm — Ncl shape changed; do not commit");
        {
            var body = initFromMetadataMethod.Body;
            var il = body.GetILProcessor();
            var firstOriginalInstr = body.Instructions[0];
            // Prepend: if (this.masterPage == null) return;
            var ldarg0 = il.Create(OpCodes.Ldarg_0);
            var ldfld  = il.Create(OpCodes.Ldfld, asm.MainModule.ImportReference(masterPageField));
            var brtrue = il.Create(OpCodes.Brtrue_S, firstOriginalInstr);
            var ret    = il.Create(OpCodes.Ret);
            il.InsertBefore(firstOriginalInstr, ldarg0);
            il.InsertBefore(firstOriginalInstr, ldfld);
            il.InsertBefore(firstOriginalInstr, brtrue);
            il.InsertBefore(firstOriginalInstr, ret);
        }
        Console.Error.WriteLine("[Cecil] Prepended masterPage null-guard to NavForm.InitializeFromMetadata → early return when masterPage is null");

        // NavForm.GetAutoFormatStringAsync → return default/empty (R2R-trapped; cluster #2 in CORPUS-CLASSIFICATION-2026-05-19-FINAL.md)
        int getAutoFormatRewroteCount = 0;
        foreach (var method in navFormType.Methods.Where(mm => mm.Name == "GetAutoFormatStringAsync").ToList())
        {
            var returnType = method.ReturnType;
            Console.Error.WriteLine($"[Cecil] Rewriting {method.FullName} (ReturnType={returnType.FullName})");

            var body = method.Body;
            body.Instructions.Clear();
            body.Variables.Clear();
            body.ExceptionHandlers.Clear();
            var il = body.GetILProcessor();

            if (returnType.FullName.StartsWith("System.Threading.Tasks.ValueTask`1<"))
            {
                // ValueTask.FromResult<string>("") — returns completed ValueTask<string> with Result=""
                var fromResultGenericDef = typeof(System.Threading.Tasks.ValueTask)
                    .GetMethods()
                    .FirstOrDefault(m => m.Name == "FromResult" && m.IsGenericMethod && m.GetParameters().Length == 1)
                    ?? throw new InvalidOperationException("ValueTask.FromResult<T> not found via reflection");
                var fromResultRef = asm.MainModule.ImportReference(
                    fromResultGenericDef.MakeGenericMethod(typeof(string)));
                il.Append(il.Create(OpCodes.Ldstr, ""));
                il.Append(il.Create(OpCodes.Call, fromResultRef));
                il.Append(il.Create(OpCodes.Ret));
                body.MaxStackSize = 1;
            }
            else if (returnType.FullName.StartsWith("System.Threading.Tasks.Task`1<"))
            {
                // Task.FromResult<string>("") — returns completed Task<string> with Result=""
                var fromResultMethodInfo = typeof(System.Threading.Tasks.Task)
                    .GetMethods()
                    .FirstOrDefault(m => m.Name == "FromResult" && m.IsGenericMethod && m.GetParameters().Length == 1)
                    ?? throw new InvalidOperationException("Task.FromResult<T> not found via reflection");
                var fromResultRef = asm.MainModule.ImportReference(
                    fromResultMethodInfo.MakeGenericMethod(typeof(string)));
                il.Append(il.Create(OpCodes.Ldstr, ""));
                il.Append(il.Create(OpCodes.Call, fromResultRef));
                il.Append(il.Create(OpCodes.Ret));
                body.MaxStackSize = 1;
            }
            else
            {
                throw new InvalidOperationException(
                    $"GetAutoFormatStringAsync has unexpected return type: {returnType.FullName} — log and STOP; do not commit");
            }
            getAutoFormatRewroteCount++;
        }
        if (getAutoFormatRewroteCount == 0)
            throw new InvalidOperationException("GetAutoFormatStringAsync method not found in NavForm — Ncl shape changed; do not commit");
        Console.Error.WriteLine($"[Cecil] Rewrote {getAutoFormatRewroteCount} GetAutoFormatStringAsync overload(s) → return default ValueTask");

        // NavMediaValueBase.get_ALMediaId → mark NoInlining so JmpHook can intercept the
        // property getter at runtime (without NoInlining, the JIT inlines the trivial body
        // `return Key.Value` into every call site, bypassing our entry-point hook).
        var navMediaValueBaseType = asm.MainModule.GetType("Microsoft.Dynamics.Nav.Runtime.NavMediaValueBase");
        if (navMediaValueBaseType != null)
        {
            var alMediaIdGetter = navMediaValueBaseType.Methods
                .FirstOrDefault(m => m.Name == "get_ALMediaId");
            if (alMediaIdGetter != null)
            {
                alMediaIdGetter.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining;
                Console.Error.WriteLine($"[Cecil] Marked NavMediaValueBase.get_ALMediaId NoInlining");
            }
            else
            {
                Console.Error.WriteLine($"[Cecil] WARNING: get_ALMediaId not found on NavMediaValueBase");
            }
        }
        else
        {
            Console.Error.WriteLine($"[Cecil] WARNING: NavMediaValueBase not found in Ncl");
        }

        // NavDialog.ALStrMenu* and ALConfirm* → mark NoInlining so JmpHooks can intercept
        // them reliably. These are static non-virtual methods; R2R may inline them into
        // caller IL, bypassing the JmpHook entry-point patch.
        var navDialogCecilType = asm.MainModule.GetType("Microsoft.Dynamics.Nav.Runtime.NavDialog");
        if (navDialogCecilType != null)
        {
            int navDialogMarked = 0;
            foreach (var m in navDialogCecilType.Methods
                .Where(m => m.Name == "ALStrMenu" || m.Name == "ALConfirm"))
            {
                m.ImplAttributes |= Mono.Cecil.MethodImplAttributes.NoInlining;
                navDialogMarked++;
            }
            Console.Error.WriteLine($"[Cecil] Marked {navDialogMarked} NavDialog.ALStrMenu/ALConfirm overloads NoInlining");
        }
        else
        {
            Console.Error.WriteLine("[Cecil] WARNING: NavDialog not found in Ncl");
        }

        var outStream = new MemoryStream();
        asm.Write(outStream);
        var modifiedBytes = outStream.ToArray();

        StripR2RHeader(modifiedBytes);

        Console.Error.WriteLine($"[Cecil] Ncl rewrite complete: {originalBytes.Length} → {modifiedBytes.Length} bytes");
        return modifiedBytes;
    }

    /// <summary>
    /// Zero the CorHeader.ManagedNativeHeader directory entry so CoreCLR sees the
    /// assembly as IL-only. Cecil's writer typically already drops the R2R native
    /// data because it rebuilds the PE; this is belt-and-suspenders.
    /// </summary>
    private static void StripR2RHeader(byte[] peBytes)
    {
        int peOffset = BitConverter.ToInt32(peBytes, 0x3C);
        int optHeaderOffset = peOffset + 4 + 20;
        ushort magic = BitConverter.ToUInt16(peBytes, optHeaderOffset);
        bool pe32Plus = magic == 0x20B;
        int dataDirOffset = optHeaderOffset + (pe32Plus ? 112 : 96);
        // Directory 14 (0-indexed) is the CLR header.
        int cliDirOffset = dataDirOffset + 14 * 8;
        uint cliRva = BitConverter.ToUInt32(peBytes, cliDirOffset);
        uint cliSize = BitConverter.ToUInt32(peBytes, cliDirOffset + 4);
        if (cliRva == 0 || cliSize == 0)
        {
            Console.Error.WriteLine("[Cecil] No CLI header found, skipping R2R strip");
            return;
        }
        int sectionCount = BitConverter.ToUInt16(peBytes, peOffset + 4 + 2);
        ushort sizeOfOptHeader = BitConverter.ToUInt16(peBytes, peOffset + 4 + 16);
        int sectionsStart = optHeaderOffset + sizeOfOptHeader;
        int cliFileOffset = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            int secHdr = sectionsStart + i * 40;
            uint virtSize = BitConverter.ToUInt32(peBytes, secHdr + 8);
            uint virtAddr = BitConverter.ToUInt32(peBytes, secHdr + 12);
            uint rawAddr = BitConverter.ToUInt32(peBytes, secHdr + 20);
            if (cliRva >= virtAddr && cliRva < virtAddr + Math.Max(virtSize, 1u))
            {
                cliFileOffset = (int)(rawAddr + (cliRva - virtAddr));
                break;
            }
        }
        if (cliFileOffset < 0)
        {
            Console.Error.WriteLine("[Cecil] Could not locate CLI header in sections, skipping R2R strip");
            return;
        }
        // ManagedNativeHeader: offset 64 (8 bytes: RVA + Size)
        bool wasNonZero = false;
        for (int j = 0; j < 8; j++) if (peBytes[cliFileOffset + 64 + j] != 0) { wasNonZero = true; break; }
        for (int j = 0; j < 8; j++) peBytes[cliFileOffset + 64 + j] = 0;
        // Also clear the COMIMAGE_FLAGS_IL_LIBRARY/NATIVE_ENTRYPOINT bits if set.
        // CorHeader.Flags is at offset 16; bit 0x10 = COMIMAGE_FLAGS_NATIVE_ENTRYPOINT, bit 0x04 = COMIMAGE_FLAGS_IL_LIBRARY.
        uint flags = BitConverter.ToUInt32(peBytes, cliFileOffset + 16);
        uint clearedFlags = flags & ~0x10u; // clear NATIVE_ENTRYPOINT
        if (clearedFlags != flags)
        {
            var bytes = BitConverter.GetBytes(clearedFlags);
            Array.Copy(bytes, 0, peBytes, cliFileOffset + 16, 4);
        }
        Console.Error.WriteLine($"[Cecil] R2R ManagedNativeHeader zeroed (was non-zero: {wasNonZero}), Flags: 0x{flags:X8} → 0x{clearedFlags:X8}");
    }

    public static bool PreloadRewrittenNcl(string dir)
    {
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (alreadyLoaded)
        {
            Console.Error.WriteLine("[Cecil] WARNING: Ncl already loaded before Cecil preload — rewrite will NOT take effect");
            return false;
        }
        var nclPath = Path.Combine(dir, "Microsoft.Dynamics.Nav.Ncl.dll");
        var modifiedBytes = RewriteNcl(nclPath);

        Assembly? rewritten = null;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (alc, name) =>
        {
            if (name.Name == "Microsoft.Dynamics.Nav.Ncl")
            {
                Console.Error.WriteLine($"[Cecil] ALC.Resolving Ncl → returning rewritten copy");
                return rewritten;
            }
            return null;
        };
        rewritten = Assembly.Load(modifiedBytes);
        Console.Error.WriteLine($"[Cecil] Loaded modified Ncl: {rewritten.FullName} (Location='{rewritten.Location}')");
        return true;
    }

    /// <summary>
    /// Rewrites Ncl from the BC artifacts dir and writes the result to the runner's
    /// bin path (overwriting the build-time copy). Runs BEFORE the CLR's TPA probe
    /// resolves Ncl, so when CLR loads Ncl by name it gets our rewritten bytes.
    /// </summary>
    public static void RewriteInPlace(string srcDir, string binNclPath)
    {
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (alreadyLoaded)
        {
            Console.Error.WriteLine("[Cecil] WARNING: Ncl already loaded before in-place rewrite — no effect");
            return;
        }
        var nclSrc = Path.Combine(srcDir, "Microsoft.Dynamics.Nav.Ncl.dll");
        var modifiedBytes = RewriteNcl(nclSrc);
        File.WriteAllBytes(binNclPath, modifiedBytes);
        Console.Error.WriteLine($"[Cecil] Wrote rewritten Ncl to {binNclPath} ({modifiedBytes.Length} bytes)");
    }

    public static void VerifyRewriteLanded()
    {
        var ncl = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (ncl == null) { Console.Error.WriteLine("[Cecil] VERIFY: Ncl not loaded"); return; }
        var t = ncl.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject");
        if (t == null) { Console.Error.WriteLine("[Cecil] VERIFY: type not found"); return; }
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                            .Where(mi => mi.Name == "IsEventSubscribed"))
        {
            var body = m.GetMethodBody();
            var il = body?.GetILAsByteArray();
            var sig = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
            Console.Error.WriteLine($"[Cecil] VERIFY: {m.Name}({sig}) IL len={il?.Length} bytes={(il==null?"<null>":string.Join(" ", il.Take(20).Select(b => b.ToString("X2"))))}");
        }
    }
}
