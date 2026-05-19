# page-report Category-A Cluster Analysis

> Generated from `v2-classification.json` (2026-05-18T20:58:26Z)  
> Bucket run: `tests/bucket-2/page-report` — **267 pass / 350 fail**  
> AVOID list removed: 245 failures (GetAutoFormatStringAsync 90, NavReport.RunReportAsync 45,
>   NCLMetadata.ThrowMetaApplicationObjectNotFound 47, NavForm.GetMasterPage 25,
>   NavDialog.ALError 21, NavReport.SaveAsAsync 13, NavReport.RunRequestPageAsync 4).
> Remaining headroom inventoried below.

---

## Summary Table

| # | Cluster | Count | Risk | Yield/Risk | Rec |
|---|---------|------:|------|------------|-----|
| 1 | `Report*.ctor` super-cluster | 54 | Low | ★★★★★ | **Start here** |
| 2 | `NavForm.RunModalAsync` | 4 | Low | ★★★★ | Quick win |
| 3 | `NavFilterPageBuilder.RunModalAsync` | 3 | Low | ★★★ | Bundle with #2 |
| 4 | `NavMediaSet.ALInsert` | 5 | Medium | ★★★ | |
| 5 | `StrMenu` + `Confirm` callbacks | 9 | Medium | ★★★ | |
| — | `NavTestPageBase.CheckPageOpened` | 8 | — | Category C | Fix tests, not runner |

**Next-session recommendation:** Tackle Cluster 1 (Report*.ctor). A single patch to
`NavReportHandle_CreateTarget` / `LookupNclMetaForReport` should unlock ~54 pass-flips
with no R2R risk since the JmpHook on `NavReportHandle.CreateTarget` is already firing.

---

## Cluster 1: `Report*.ctor` super-cluster (54 tests, 9 suites)

**Root cause hypothesis:**
`NavReportHandle_CreateTarget` (JmpHook on `NavReportHandle.CreateTarget`) fires correctly.
It finds the `Report{N}` type in the test assembly, finds only a 2-arg constructor
`(ITreeObject parent, NCLMetaReport metadata)`, calls `LookupNclMetaForReport(id)` which
reads `NavGlobal.NCLMetadata` (the real global NCLMetadata, not the skeleton we injected
into `NavGlobal.SystemTenant.NCLMetadata`). The report ID is not found there — the
`try { return getMeta?.Invoke(...) }` catch swallows the `NavNCLApplicationObjectNotFoundException`
and returns `null`. Then `twoArg.Invoke(new object?[] { self, null })` passes literal null,
and the report constructor dereferences `metadata` on line 1 → NRE.

**Fix shape:** In `LookupNclMetaForReport`, when `GetMetaReportById` returns null or throws,
fall back to `_metaReportCache.GetOrAdd(id, BuildNCLMetaReport)` directly, bypassing
`NavGlobal.NCLMetadata` entirely. Alternatively, update `NavReportHandle_CreateTarget` to
skip the `LookupNclMetaForReport` call when `metaArg == null` and instead call
`BuildNCLMetaReport(id)` as the source-of-truth. Since `BuildNCLMetaReport` calls
`CreateEmptyNCLMetaReport(null, id, _baseAppGroup, -1, "")`, it requires no parsed info and
can succeed for any numeric report ID.

**Top BC frame:** `at Microsoft.Dynamics.Nav.BusinessApplication.Report126000..ctor(ITreeObject parent, NCLMetaReport metadata)`

**Example failing tests:**
- `tests/bucket-2/page-report/279-report-instance/` — `DefaultLayout_Compiles`, `ExcelLayout_ReturnsFalse` (Report126000, 19 tests)
- `tests/bucket-2/page-report/*/` — Report84501 (6), Report91000 (5), Report50113 (3), Report307200 (3), Report50258 (3), Report60480 (3), Report230001 (3)

**Risk level:** Low — JmpHook already installed and firing; fix is entirely in C# fallback logic, no new hooks needed.

**Est. yield if fixed:** ~54 pass-flips across 9 test suites.

---

## Cluster 2: `NavForm.RunModalAsync` (4 tests)

**Root cause hypothesis:**
`Page.RunModal()` (and the static `Page.RunModal(pageId)` form) reaches
`NavForm.RunModalAsync(...)` which NREs early in execution on skeleton session state
(likely accessing `Tree.Session` or a UI component). The runner has `NavFormHandle_CreateTarget`
patched but not `RunModalAsync`.

**Top BC frame:**
`at Microsoft.Dynamics.Nav.Runtime.NavForm.RunModalAsync(Boolean isInLookupTrigger, Boolean isLookup, Int32 formId, NavRecord record, ...)`
and `at Microsoft.Dynamics.Nav.Runtime.NavForm.RunModalAsync(NavRecord record, Int32 fieldNo)`

**Example failing tests:**
- `tests/bucket-2/page-report/*/` — `PageRunModal_ReturnsAction` (Codeunit89100)
- `tests/bucket-2/page-report/*/` — `RunModal_ReturnsAction` (Codeunit60470)

**Fix shape:** JmpHook all `NavForm.RunModalAsync` overloads to return `Action::Ok` (integer 1).
Same pattern as existing `NavFormHandle_CreateTarget`. Both overloads needed:
`RunModalAsync(Boolean, Boolean, Int32, NavRecord, ...)` and `RunModalAsync(NavRecord, Int32)`.

**Risk level:** Low — these are virtual-dispatch paths unlikely to be R2R-inlined.

**Est. yield if fixed:** ~4 pass-flips.

---

## Cluster 3: `NavFilterPageBuilder.RunModalAsync` (3 tests)

**Root cause hypothesis:**
`FilterPageBuilder.RunModal()` in AL calls `NavFilterPageBuilder.ALRunModalAsync(DataError)`
which delegates to `NavFilterPageBuilder.RunModalAsync(ITreeObject parent)` → NRE on
skeleton `ITreeObject` state (Tree/Session null).

**Top BC frame:** `at Microsoft.Dynamics.Nav.Runtime.NavFilterPageBuilder.RunModalAsync(ITreeObject parent)`

**Example failing tests:**
- `tests/bucket-2/page-report/*/` — `FilterBuilderAndBool_CondFalse_ReturnsFalse`, `FilterBuilderAndBool_CondTrue_ReturnsTrue` (Codeunit132001)

**Fix shape:** JmpHook `NavFilterPageBuilder.RunModalAsync(ITreeObject)` to return `Action::Ok`
(same as Cluster 2). Bundle this into the same session as Cluster 2.

**Risk level:** Low.

**Est. yield if fixed:** ~3 pass-flips.

---

## Cluster 4: `NavMediaSet.ALInsert` (5 tests)

**Root cause hypothesis:**
`NavMediaSet.ALInsert(DataError errorLevel, Guid mediaId)` NREs on null internal backing
state — the `NavMediaSet` skeleton lacks the in-memory collection it writes to when
`Insert(MediaId)` is called in AL.

**Top BC frame:** `at Microsoft.Dynamics.Nav.Runtime.NavMediaSet.ALInsert(DataError errorLevel, Guid mediaId)`

**Example failing tests:**
- `tests/bucket-2/page-report/*/` — `Count_ReturnsOne_AfterInsert`, `Insert_ReturnsTrue`, `Item_ReturnsInsertedGuid` (Codeunit125001)

**Fix shape:** Skeleton-state patch — either JmpHook `NavMediaSet.ALInsert` + `ALCount` + `ALItem`
to maintain a `ConcurrentDictionary<identity, List<Guid>>` keyed on the MediaSet instance,
or field-poke the backing collection field to a pre-allocated container in the
`NavMediaSet` constructor.

**Risk level:** Medium — requires identifying and poking the exact backing field(s), and
the overload signatures for Count/Item need to be confirmed.

**Est. yield if fixed:** ~5 pass-flips.

---

## Cluster 5: `NavDialog.StrMenu` + `NavDialog.Confirm` callbacks (9 tests)

**Root cause hypothesis:**
`StrMenu(options)` and `StrMenu(options, defaultNo)` in AL invoke BC's callback mechanism
to show a UI menu; the runner throws `NavNCLCallbackNotAllowedException: Callback functions
are not allowed` because callbacks are disabled in standalone mode.
Similarly for `Confirm(question)`. The existing `NavDialog.ALOpen` no-op and
`NavDialog.ALError` hooks do not cover StrMenu/Confirm.

**Classification signatures:**
- `runtime/BusinessApplication.Codeunit59750.Pick_Scope_...OnRun` (2 tests — 1-arg StrMenu)
- `runtime/BusinessApplication.Codeunit59750.PickWithDefault_Scope_...OnRun` (4 tests — 2-arg StrMenu)
- `runtime/BusinessApplication.Codeunit56701.DoSomethingWithConfirm_Scope_...OnRun` (3 tests)

**Top BC frame:**
`at Microsoft.Dynamics.Nav.BusinessApplication.Codeunit59750.PickWithDefault_Scope_....OnRun()`
(thrown inside `StrMenu(options, defaultNo)` internal callback)

**Example failing tests:**
- `tests/bucket-2/page-report/162-strmenu/` — `StrMenu_NoHandler_ReturnsCancel` (expects 0), `StrMenu_DefaultLast_ReturnsLast` (expects 3)
- `tests/bucket-2/page-report/71-testpage/` — `ConfirmHandlerAnswersNo`, `ConfirmHandlerAnswersYes`

**Fix shape:** JmpHook `NavDialog.ALStrMenu` (all overloads) to extract `defaultNo` from
args and return it (0 if not provided). JmpHook `NavDialog.ALConfirm` to return `false`
in standalone mode, or call the registered `[ConfirmHandler]` if one exists in the current
test's handler stack.

**Risk level:** Medium — R2R inlining is possible for `NavDialog` methods since they are
non-virtual. Test the hook boots successfully before claiming fixed. If R2R blocks the
hook, these tests become Category D.

**Est. yield if fixed:** ~9 pass-flips.

---

## Excluded from list

### `NavTestPageBase.CheckPageOpened` (8 tests) — Category C: test design issues

Tests in `tests/bucket-2/page-report/312200-testpage-filter-object/test/Test312200.al`
call `Page.Filter.SetFilter(...)` and `Page.Filter.GetFilter(...)` without calling
`Page.Open()`, `Page.OpenNew()`, or `Page.Trap()` first. BC's own runtime throws
`NavTestPageNotOpenedException: The TestPage is not open.` This is correct BC behavior —
TestPage filter operations require an open page.

These tests were written against the v1 mock, which did not enforce the open-page check.
Fix: update the AL tests to call `Page.OpenNew()` (or `Page.Trap()` in a modal context)
before filter operations, then `Page.Close()` at the end.

**Not a runner gap — update the tests.**
