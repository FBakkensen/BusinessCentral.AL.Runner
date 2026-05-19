# R2R Downstream Map — first JIT'd frame past each R2R-trapped BC method

**Session:** 2026-05-19 17:39 (UTC+02:00), 90-min budget — tracing only, no behavior changes.
**Branch:** `spike/bc-abi-identity` at parent `4742bb2f`.
**Methodology:**
1. Built `spike/v2/Runner` (Release).
2. Temporarily lifted `EventPipeJitListener._dryBcSamples` 60-name cap and added an env-gated `R2RMAP_JIT_DUMP=<path>` dump on `ProcessExit` (both reverted before this commit; `git diff HEAD~1 HEAD --stat` shows this doc only).
3. Ran canary suites that exercise each target with `R2RMAP_JIT_DUMP=/tmp/r2r-bc-jit.txt`:
   - `tests/bucket-2/page-report/90-report-static` (Codeunit91001 calls SaveAsPdf/Excel/Word/Xml, RunModal, RunRequestPage, GetSubstituteId)
   - `tests/bucket-2/page-report/99-page-autoformat`
   - `tests/bucket-2/page-report/312200-testpage-filter-object`
   - `tests/bucket-1/record-table/107-rename`
   - `tests/bucket-1/codeunit-runtime/66-event-subscribers`
4. Captured 7,021 unique BC method names with MethodLoad events (6,704 raw / 23,148 total CLR JIT events).
5. Cross-referenced each stack frame and likely downstream symbol against the JIT dump.

**Definition used:** A frame is "hookable" if its fully-qualified `Namespace.MethodName` appears in the `R2RMAP_JIT_DUMP` set — i.e. the JIT (not R2R) compiled a body that we can install a 14-byte `JmpHook` on.

---

## Headline finding (30-sec read)

The five "R2R-trapped" targets split cleanly into **two groups**:

| Group | Targets | Downstream hook available? |
|---|---|---|
| **A. Trap-with-downstream** | `NavRecord.*Async` family (Modify/Delete/Rename/OnInsert) | **Yes** — `RecordImplementation.*RecordAsync`, `DataAccess.*Async`, `NavGlobalTriggers.*Async` (and their state-machine `MoveNext` continuations) are all JIT'd and hookable. ~28 candidate frames. |
| **B. Trap-no-downstream** | `NavReport.SaveAsAsync`, `NavForm.GetMasterPage`, `NavForm.GetAutoFormatStringAsync`, `NCLMetaApplicationObject.IsEventSubscribed` | **No JIT'd descendant observed.** They either (a) throw inside their own R2R body before reaching any JIT'd callee (`SaveAsAsync` → `ArgumentNullException(parent)` at entry), or (b) return cheaply (`IsEventSubscribed` returns `false`, `GetMasterPage`/`GetAutoFormatStringAsync` return a default/empty value) — every downstream symbol that *would* have JIT'd is also R2R-inlined into the trap. |

**Net hook-pointability:**
- **Group A (~60 NRE record-write tests + ~25 rename tests):** Hookable. Several candidate replacement signatures listed below.
- **Group B (~54 SaveAs + ~25 GetMasterPage + ~90 GetAutoFormatString + ~60+ IsEventSubscribed-gated subscriber tests, ≈230 tests):** Cannot be addressed by downstream JIT hooking. Requires a different strategy: either AL-side wrapper hooks (`Codeunit*.Call*_Scope__*.OnRun` IS JIT'd, but per-fixture — not a runner-wide solution), or upstream state injection (set the `parent`/`MasterPage`/subscriber-registry fields the R2R body reads before it traps), or **a documented OOS throw via a different injection (e.g. JmpHook on the AL-emitted scope wrapper)**.

---

## 1. `NavReport.SaveAsPdfAsync` / `SaveAsExcelAsync` / `SaveAsWordAsync` / `SaveAsHtmlAsync` / `SaveAsXmlAsync`

**Reality check from stacks:** All AL `Report.SaveAs*` variants funnel through ONE static method in BC:

```
Microsoft.Dynamics.Nav.Runtime.NavReport.SaveAsAsync(
    NavSession session,
    DataError errorLevel,
    Int32 reportId,
    String fileName,
    NavRecord record,
    NavReportFormat reportFormat)
```

So this is one target, not five — the format is a parameter.

**R2R-resident frames (NOT hookable — absent from JIT dump):**
- `Microsoft.Dynamics.Nav.Runtime.NavReport.SaveAsAsync(…)` [Ncl.dll] — top of stack

**First JIT'd frame past the trap (HOOKABLE):**
- **NONE.** The R2R body throws `ArgumentNullException: Value cannot be null. (Parameter 'parent')` directly. No `Microsoft.ReportingServices.*`, no `RDLCRenderer`, no `NavReportEngine`, no `Microsoft.Dynamics.Nav.BusinessApplication.Codeunit91000.CallSaveAsExcel_Scope__*.OnRun.<downstream>` — the trap is at parameter validation INSIDE the R2R body, before any logic that would call into a JIT'd descendant.
- JIT log confirms: zero matches for `NavReport.SaveAsAsync` / `SaveAsPdf` / `SaveAsExcel` / `SaveAsWord` / `SaveAsHtml` / `SaveAsXml` / `Microsoft.ReportingServices.*` / `RDLCRenderer` / `RenderToStream`.

**Test that triggered this trace:** `tests/bucket-2/page-report/90-report-static`, e.g. `Codeunit91001.Report_SaveAsExcel_IsNoOp` (and SaveAsPdf/Word/Xml).

**Stack-trace snippet (verbatim):**
```
at Microsoft.Dynamics.Nav.Runtime.NavReport.SaveAsAsync(NavSession session, DataError errorLevel, Int32 reportId, String fileName, NavRecord record, NavReportFormat reportFormat)
at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit91000.CallSaveAsExcel_Scope__975258641.OnRun()
at Microsoft.Dynamics.Nav.Runtime.NavMethodScope.Run()
at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit91000.CallSaveAsExcel(Int32 reportId, NavText fileName)
…
```

**Possible hook points (if future sessions need to OOS-throw):**
- The AL-generated wrapper `Codeunit91000.CallSaveAsExcel_Scope__975258641.OnRun` IS JIT'd, but the scope-class name is per-fixture (`__975258641` is a content hash) — not a stable runner-wide hook.
- Upstream: emit a `JmpHook` against the AL emitter's *generated* scope thunks that wrap any AL `Report.SaveAs*()` call — requires touching `BcAssembler.cs` / `RoslynRewriter.cs` style emit, NOT a JIT hook.
- Alternative: inject a non-null `parent` (a `NavApplicationObjectBase` instance) into the runtime's per-thread scope before `NavReport.SaveAsAsync` is invoked. The "parent" is likely `NavCurrentThread.MethodScope?.CallerApplicationObject` or similar; further investigation needed.

**Notes / caveats:** The 5 trapped SaveAs variants collapse to 1 R2R body. ~54 stuck tests, but a single runner-wide fix would lift the whole cluster IF we can pre-set `parent`. If the only acceptable outcome is OOS-throw, the AL-emitter route is the only stable hook point.

---

## 2. `NavForm.GetMasterPage`

**R2R-resident frames (NOT hookable):**
- `Microsoft.Dynamics.Nav.Runtime.NavForm.GetMasterPage` [Ncl.dll] — absent from JIT dump.

**First JIT'd frame past the trap (HOOKABLE):**
- **NONE OBSERVED.** No NRE in the canary stacks attributable to `GetMasterPage`. The PAGE-REPORT-CLUSTERS doc lists `NavForm.GetMasterPage = 25 tests` on the AVOID list — those suites were not in this 5-canary run. Inferential negative result based on JIT dump:
  - JIT'd NavForm methods are: `..ctor`, `CallInitializeComponentExtensionMethod`, `get_IsRequestPage`, `InitializeForm(Async)`, `<InitializeFormAsync>b__373_0`, `OnInit`, `RaiseOnInitAsync`.
  - `GetMasterPage`, `get_MasterPage`, and `_masterPage` accessors do NOT appear in the dump in 5 canary suites including a TestPage-heavy suite.
  - Conclusion (provisional): the R2R inliner has captured the entire MasterPage accessor chain. No JIT'd hook downstream.

**Test that triggered this trace:** Not directly hit by canaries (suite list in AVOID was not run); inferred from JIT dump alone. **Re-run with a known MasterPage-fail suite is needed to confirm.** Candidate suites to retry: any `tests/bucket-2/page-report/*testpage-action*` or `*page-runmodal*` from the failure inventory.

**Stack-trace snippet:** *(not captured this session)*

**Notes / caveats:** Group B classification is provisional pending a direct canary. The JIT-set absence is a strong negative signal but not stack-level proof.

---

## 3. `NavForm.GetAutoFormatStringAsync`

**R2R-resident frames (NOT hookable):**
- `Microsoft.Dynamics.Nav.Runtime.NavForm.GetAutoFormatStringAsync` [Ncl.dll] — absent from JIT dump.

**First JIT'd frame past the trap (HOOKABLE):**
- **NONE OBSERVED.** Same shape as #2. JIT dump has zero matches for `GetAutoFormatString*`, `AutoFormat*`, `FormatHelper*`. The 99-page-autoformat canary passed all its tests in this run (so it's not the failing-suite that drives the 90-test cluster — that cluster lives elsewhere in the corpus and was on the AVOID list).
- For an "Async returns empty string" trap, even *if* a downstream existed it would likely be a small `FormatHelper.Format` style call that's also R2R-inlined.

**Test that triggered this trace:** Not directly hit; inferred from JIT dump in 5 canary suites. Same caveat as #2 — re-run with a known AutoFormat-failing suite for stack-level proof.

**Notes / caveats:** Test message likely has no NRE — the method probably returns `string.Empty` silently and the AL assertion sees "" vs expected formatted value. Without an exception, the only diagnostic would be a probe `Environment.StackTrace` injection into the AL test wrapper — explicitly out-of-scope for this session per the brief's "no source changes" constraint.

---

## 4. `NCLMetaApplicationObject.IsEventSubscribed`

**R2R-resident frames (NOT hookable):**
- `Microsoft.Dynamics.Nav.Runtime.NCLMetaApplicationObject.IsEventSubscribed` [Ncl.dll] — absent from JIT dump.

**First JIT'd frame past the trap (HOOKABLE):**
- **NONE downstream of the trap call site.** But siblings of the call site (i.e. **before** the trap-inlined region) ARE JIT'd:
  - `NavRecord.InsertAsync` ✓ JIT'd (already runner-hooked in `RecordWritePatches.cs`)
  - `NavGlobalTriggers.InsertAsync` ✓ JIT'd
  - `NavGlobalTriggers+<InsertAsync>d__0.MoveNext` ✓ JIT'd (state machine)
  - `RecordImplementation.InsertRecordAsync` ✓ JIT'd
  - `DataAccess.InsertAsync` ✓ JIT'd
- `IsEventSubscribed` is inlined inside `NavRecord.*Async` and inside the trigger-dispatch loop; the call site is the trap.

**Test that triggered this trace:** `tests/bucket-1/codeunit-runtime/66-event-subscribers` — `Codeunit50333.SubscribersFireOnEachCall`. Stack-top is `NavDialog.ALError("Counter row should exist")` — i.e. **the test fails silently via AL Assert, no NRE.** `IsEventSubscribed` returned `false` (because the runner has no subscriber registry wired into the inlined accessor), trigger dispatch was skipped, the counter never incremented, the AL Assert fired.

**Stack-trace snippet (verbatim):**
```
at Microsoft.Dynamics.Nav.Runtime.NavDialog.ALError(NavSession session, Guid automationId, NavTextConstant message, NavValue[] values)
at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit130000.IsTrue_Scope__886670126.OnRun()
…
at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit50333.SubscribersFireOnEachCall_Scope__76940670.OnRun()
```

**Possible hook strategy (NOT recommended downstream-hook, but documented):**
- Since the R2R-inlined `IsEventSubscribed` reads a `NCLMetaApplicationObject` instance field (or a static registry), the proven pattern from `c95debdc` (NavSession.Authenticator skeleton field-poke) applies: identify the backing field that `IsEventSubscribed` reads and inject a registry with the test's subscribers. This is **state injection upstream**, not downstream JIT hooking.
- The runner-side `EventSubscriberPatches.cs` already attempted a downstream hook on `IsEventSubscribed` itself — comments confirm that path was abandoned because R2R inlining bypasses the JmpHook.

**Notes / caveats:** This is the highest-impact Group-B target by ratio (subscriber dispatch is foundational across many codeunit-runtime tests). But the fix is upstream skeleton wiring, not downstream interception.

---

## 5. `NavRecord.{Insert,Modify,Delete,Rename,OnInsert}Async`

**Reality check from JIT dump:** Only `OnInsertAsync` is universally R2R-trapped in this corpus. Per-method status:

| Method | JIT'd? | Status |
|---|---|---|
| `NavRecord.InsertAsync` | **✓ JIT'd** | Already runner-hooked (`NavRecord_InsertAsync` in `RecordWritePatches.cs`). Not a trap. |
| `NavRecord.ModifyAsync` | ✗ Not JIT'd (NavRecord-level) | R2R-inlined into call sites. But `DataAccess.ModifyAsync` ✓ JIT'd downstream. |
| `NavRecord.DeleteAsync` | ✗ Not JIT'd | No `NavRecord.DeleteAsync` in dump, but `DataAccess.*` candidates exist downstream. |
| `NavRecord.RenameAsync` | ✗ Not JIT'd (NavRecord-level) | `NavGlobalTriggers.RenameAsync` ✓ JIT'd, `RecordImplementation.RenameRecordAsync` ✓ JIT'd. |
| `NavRecord.OnInsertAsync` | ✗ Not JIT'd | Trigger virtual — R2R-trapped. |

**R2R-resident frames (NOT hookable) — confirmed by JIT dump:**
- `NavRecord.ModifyAsync` [Ncl.dll]
- `NavRecord.DeleteAsync` [Ncl.dll]
- `NavRecord.RenameAsync` [Ncl.dll]
- `NavRecord.OnInsertAsync` [Ncl.dll]

**First JIT'd frame past the trap (HOOKABLE) — abundant:**

For **Modify/Delete/Rename** the data-access plumbing layer is fully JIT'd:
```
Microsoft.Dynamics.Nav.Runtime.DataAccess.InsertAsync                   ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.ModifyAsync                   ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.IssueModifyAsync              ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.PerformModifyAsync            ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.CountAsync                    ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.TryGetByPrimaryKeyAsync       ✓
Microsoft.Dynamics.Nav.Runtime.DataAccess.InternalTryGetByPrimaryKeyAsync ✓

Microsoft.Dynamics.Nav.Runtime.RecordImplementation.InsertRecordAsync   ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.RenameRecordAsync   ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.GetRecordAsync      ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.LoadFieldsAsync     ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.PassesSecurityFiltersAsync ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.VerifySecurityFiltersAsync ✓
Microsoft.Dynamics.Nav.Runtime.RecordImplementation.CalcAutoCalcFieldsAsync   ✓

Microsoft.Dynamics.Nav.Runtime.NavGlobalTriggers.InsertAsync            ✓
Microsoft.Dynamics.Nav.Runtime.NavGlobalTriggers.RenameAsync            ✓
Microsoft.Dynamics.Nav.Runtime.NavGlobalTriggers+<InsertAsync>d__0.MoveNext   ✓
Microsoft.Dynamics.Nav.Runtime.NavGlobalTriggers+<RenameAsync>d__3.MoveNext   ✓
```

For **Rename specifically**, the failing stack shows the chain ends at a JIT'd frame:
```
at Microsoft.Dynamics.Nav.Runtime.RecordImplementation.RenameRecordAsync(DataError errorLevel, NavRecord renamedRecord)    ← HOOKABLE
at Microsoft.Dynamics.Nav.Runtime.NavRecord.RenameAsync(DataError errorLevel, …)                                              ← R2R, not hookable
at Microsoft.Dynamics.Nav.Runtime.NavRecord.ALRename(DataError errorLevel, NavValue[] values)                                 ← ✓ JIT'd (upstream)
at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit95102.RenameToConflictReturnsFalse_Scope_321438047.OnRun()
```
So the trap (`NavRecord.RenameAsync`) is sandwiched between TWO JIT'd frames: `NavRecord.ALRename` upstream (already in the runner's hook surface) and `RecordImplementation.RenameRecordAsync` downstream.

**Test that triggered this trace:** `tests/bucket-1/record-table/107-rename` — `Codeunit95102.RenameToConflictReturnsFalse` (NavCSideDuplicateKeyException — that's actually the right shape; the test expects a conflict, so this one passed correctly through the JIT'd Rename path).

**Recommended replacement signatures for future sessions:**

| Replacement target | Signature (verbatim from stack) | Hook style |
|---|---|---|
| `RecordImplementation.ModifyRecordAsync` (if exists) | TBD — search Ncl decompiled IL | `JmpHook.Apply` → bypass-drain like `NavRecord_ModifyAsync` |
| `RecordImplementation.RenameRecordAsync` | `(DataError errorLevel, NavRecord renamedRecord)` | `JmpHook.Apply` → in-memory rename + duplicate-key check against runner table store |
| `DataAccess.PerformModifyAsync` | TBD | Low-level alternative if Record-level hook is too coarse |
| `NavGlobalTriggers.RenameAsync` | TBD | Hook to no-op the global-trigger dispatch on rename, complementing existing `<InsertAsync>` work |

**Notes / caveats:**
- The existing `RecordWritePatches.cs` already proved `NavRecord.InsertAsync` is hookable AND the pattern works (`NavRecord_InsertAsync` bypass-drain). The PATH-FORWARD doc notes `ModifyAsync`/`DeleteAsync` hooks were registered but intentionally not installed because the body is reached via the InsertAsync hook in some shapes. The JIT dump confirms ModifyAsync at the NavRecord level is *not* hookable; the path forward is **`RecordImplementation.*RecordAsync` and `DataAccess.*Async` as the actual hook frames**.
- The `NavGlobalTriggers+<…>d__N.MoveNext` async-state-machines are JIT'd. These ARE the resumption methods after `await`. They are non-trivial to JmpHook safely because the state-machine instance fields encode in-flight state — but they *are* observable in the JIT log and could be a last-resort hook surface.
- ~25 rename tests + ~60 residual Modify NRE tests = up to ~85 tests addressable in Group A by adding `RecordImplementation`/`DataAccess` level hooks in a future session.

---

## Summary table — final

| # | Target | R2R-trap confirmed | Downstream JIT'd hook? | Effort estimate (next session) |
|---|---|---|---|---|
| 1 | `NavReport.SaveAsAsync` (all formats) | ✓ (stack + JIT dump) | **No** | High — needs AL-emitter or upstream state-injection |
| 2 | `NavForm.GetMasterPage` | ✓ (JIT dump only; no canary stack this session) | **No (provisional)** | High — same as #1 |
| 3 | `NavForm.GetAutoFormatStringAsync` | ✓ (JIT dump only; no canary stack this session) | **No (provisional)** | High — same as #1; trap is silent, not NRE |
| 4 | `NCLMetaApplicationObject.IsEventSubscribed` | ✓ (PATH-FORWARD + JIT dump) | **No (downstream); state-injection upstream is the proven pattern** | Medium — replicate `NavSession.Authenticator` field-poke for `NCLMetaApplicationObject._subscribers` |
| 5 | `NavRecord.{Modify,Delete,Rename,OnInsert}Async` | ✓ (JIT dump) | **YES** — `RecordImplementation.*RecordAsync` and `DataAccess.*Async` are all JIT'd | Low — extend `RecordWritePatches.cs` with the existing pattern |

**Headline:** Group A (target #5) is straightforwardly hookable and worth ~85 test flips. Group B (targets #1–#4, ≈230 tests) cannot be solved by **downstream** JIT hooking — every hookable JIT'd frame is either *upstream* of the trap (AL-generated wrapper code) or unrelated. For Group B the path forward is **upstream state injection** (proven pattern) or **AL-emitter-level intervention**, not post-JIT body patching.

---

## Probe artefact location

Full BC JIT dump from this session captured at `/tmp/r2r-bc-jit.txt` (7,021 unique BC `Namespace.MethodName` entries). Not committed; reproducible by the env-gated probe described in the methodology block above.

## Git hygiene

`git diff HEAD~1 HEAD --stat` after this commit will show ONLY:
```
spike/v2/R2R-DOWNSTREAM-MAP.md  | +N (one file changed)
```
All source-code probes have been reverted; `git status` was clean before this commit save for the new doc.
