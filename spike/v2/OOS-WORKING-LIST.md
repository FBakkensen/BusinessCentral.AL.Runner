# OOS-shaped failing tests — working list (generated 2026-05-18)

Read-only classifier pass for Category B (`spike/v2/PATH-FORWARD.md` §B).
This document enumerates AL tests that should fail loudly via
`RunnerOutOfScopeException` (or do today, but the test was written without
`asserterror` so it counts as a corpus fail). Mechanical conversion target:
each test gets `asserterror <call>;` + `Assert.ExpectedError('out-of-scope: <api>')`.

## Data sources & caveats

- `spike/v2/results-*.json` (4 files, generated 2026-05-07/08)
  - `results-after-w1.json` — bucket-1 (158 fails)
  - `results-codeunit-runtime.json` — bucket-1/codeunit-runtime (164 fails)
  - `results-record-table.json` — bucket-1/record-table (113 fails)
  - `results-bucket-2.json` — bucket-2/**data-formats only** (522 fails)
- **`page-report` bucket is NOT covered by any results-*.json file.** The bulk of
  OOS-shaped tests (Report.Run, XmlPort.Run, Page.Run non-modal, NavForm.* R2R)
  live under `tests/bucket-2/page-report/**` and their pass/fail state must be
  inferred from AL source + scope.md alone. Numbers below reflect AL-source
  call-site counts where results aren't available; an updated `page-report` run
  is needed before committing to the exact yield.
- Patches with `RunnerOutOfScopeException` today: only `XmlPortPatches.cs`
  (7 sites, `ThrowNotYetImplemented`) and `EventSubscriberPatches.cs` (1 site).
  Everything else listed below is a **silent-default** stub the test
  currently passes through OR the runner's NRE manifests on a different surface
  (skeleton-state gap, Category A) before hitting the OOS surface.

## Headline table (sorted by Category-B yield potential, biggest $/min first)

| API | Count (tests) | Shape today | docs/scope.md anchor | Example test |
|-----|---:|---|---|---|
| NavReport.Run / RunModal / SaveAs* | ~39 [Test] across 7 dirs | silent-fake stub; test asserts state after | §4 (#todo-runreport / #todo-saveas) | `tests/bucket-2/page-report/279-report-instance/test/RimTest.al:183` |
| NavXmlPort.Run / Import / Export | ~37 [Test] across 3 dirs (+ `163-xmlport-run` already 5 failing NRE) | now `ThrowNotYetImplemented` after `4a8ba526` (Run only converted to Run; others too); tests lack `asserterror` | §4 (#todo-xmlport) | `tests/bucket-2/page-report/90-xmlport-instance/test` (18 [Test]) |
| NavForm.GetAutoFormatStringAsync (R2R trap — Category D, not B) | ~90 across page-report TestPage suites | R2R-inlined, JmpHook bypassed | §4 (#todo-form-autoformat) | `tests/bucket-2/page-report/99-page-autoformat/test/PageAutoFormatTest.al` |
| StartSession (parallel session contract) | 12 [Test] across 4 dirs | inline-run replacement (faithful for some uses; out-of-scope for parallel-contract uses) | §3.9 (#parallel-sessions) | `tests/bucket-1/codeunit-runtime/1402-startsession-overloads/test` (3 [Test]) |
| NavForm.RunAsync (Page.Run non-modal) | 9 [Test] across 3 dirs | silent no-op | §3.11 (#ui) | `tests/bucket-2/page-report/48-page-variable/test/ProbePageTest.al` |
| NavFile.Upload / Download (browser round-trip) | 2 [Test] in 1 dir | silent no-op | §3.4 (#file-storage) | `tests/bucket-1/codeunit-runtime/321-file-upload-overload/test/FileUploadOverloadTest.al` |
| NavHttpClient.Send / Get / Post / … | 0 actual call sites (test files declare HttpClient & exercise getters/headers — IN scope) | n/a | §3.2 (#external-http) | — none — see HttpClient note below |
| NavEmail.Send / .Enqueue / .OpenInEditor | 0 call sites in test corpus | n/a | §3.1 (#email) | — none — |
| DotNet AL surface | 0 (init-only mentions in `319-variant-stub-is-methods`) | n/a | §3.14 (#dotnet-interop) | — none — |
| Debugger.Attach/Break/etc. | 0 | n/a | §3.12 | — none — |

## Per-API detail

### NavReport.Run / RunModal / SaveAs (largest cluster — ~39 [Test])

- **Shape:** silent-fake. The runner currently has no `Patches/ReportPatches.cs`;
  Report.Run goes through unmodified Ncl code that NREs into our skeleton, or it
  silently completes the no-op `OnAfterGetRecord` etc. without a real run. The
  AL tests then assert on `WasOnAfterGetRecordCalled` and friends and fail.
- **Existing patch classification:** **not patched** — should be added, then
  classified §4 (`todo-runreport`/`todo-saveas`) using `ThrowNotYetImplemented`.
- **Recommendation:** Either (a) implement an in-memory `Report.Run` faithful
  replacement that runs `OnPreReport` → dataset → `OnAfterGetRecord` → handler
  callbacks (true Category-A wiring, ~40 pass-flips), OR (b) treat it as
  permanently out-of-scope file-emitting variants and convert SaveAs* tests
  to `asserterror` (Category B). Mixed bag: `Report.Run` with handler is
  feasible §2; `Report.SaveAs(filename, ...)` with real file output is §3.4.
- **Files (sorted by [Test] count):**
  - `tests/bucket-2/page-report/279-report-instance/test/RimTest.al` (19 [Test], Report.Run :183, RunModal :193)
  - `tests/bucket-2/page-report/90-report-static/test/ReportStaticTest.al` (8 [Test], Report.Run :17)
  - `tests/bucket-2/page-report/287-report-runmodal-4arg/test/Test.al` (4 [Test], RunModal :17 :26)
  - `tests/bucket-2/page-report/298-report-run-4arg/test/Rr4Test.al` (2 [Test], Report.Run :10 :19)
  - `tests/bucket-2/page-report/300-report-run-3arg/test/test.al` (2 [Test], Report.Run :10 :18)
  - `tests/bucket-2/page-report/310200-report-run-2arg/test/test.al` (2 [Test], Report.Run :10 :18)
  - `tests/bucket-2/page-report/222-report-saveas-recordref/test/ReportSaveAsRecordRefTest.al` (2 [Test], Report.SaveAs :16 :27)

### NavXmlPort.Run / Import / Export (~37 [Test])

- **Shape:** today the runner converts `Export/Import/Run/SetTableView` and the
  static variants to `RunnerScope.ThrowNotYetImplemented` via `XmlPortPatches.cs`
  (commit `4a8ba526`). The throw fires; AL tests not written with `asserterror`
  surface as **failing** with `RunnerOutOfScopeException`. This is the canonical
  Category-B conversion target.
- **Existing patch classification:** §4 TODO (loud throws — correct).
- **Recommendation:** **direct asserterror conversion** for all calls today.
  Eventually some XmlPort variants will move to §2 (in-memory `Run(InStream)`
  / `Run(OutStream)`), at which point the asserterror flips back. Per
  `SCOPE-AUDIT.md`: "In-memory XmlPort variants eventually in scope".
- **Files:**
  - `tests/bucket-2/page-report/90-xmlport-instance/test/XITest.al` (18 [Test])
  - `tests/bucket-2/page-report/125-xmlport-query-diagnostics/test/DiagTests.al` (13 [Test])
  - `tests/bucket-2/page-report/84-xmlport/test/XmlPortTests.al` (6 [Test])
  - `tests/bucket-2/page-report/163-xmlport-run/test/...` (5 [Test] — **confirmed failing in `results-bucket-2.json`**, all `XmlportRun_*` on Codeunit59761, NRE before the throw fires because file lives in data-formats; results file already shows them at NRE on `NavMethodScope..ctor` not on the OOS throw, so a wiring fix may need to land first)
  - related (declarations only, no .Run): `tests/bucket-2/page-report/100-xmlport-attribute`, `69-xmlport-schema`, `193-report-xmlport-clear`, `158-xmlport-import-file`

### NavForm.GetAutoFormatStringAsync (R2R trap — **CATEGORY D, NOT B**)

- **Shape:** R2R-inlined inside Ncl bodies; JmpHook silently bypassed.
- **Existing patch classification:** §4 TODO; see `feedback_r2r_inlining_traps.md`.
- **Recommendation:** **do NOT include in the Category-B sweep.** These need
  EventPipe post-JIT patching infrastructure (Category D, multi-day spike). The
  test bodies do call surfaces that *look* OOS, but the runner currently
  silently returns garbage instead of throwing, so converting tests to
  `asserterror` would hide the trap, not surface it.
- **Files:** `tests/bucket-2/page-report/99-page-autoformat/test`, `269-testrequestpage-methods/test`, `311900-testrequestpage-getdataitem/test`, plus larger TestPage cluster across page-report.

### StartSession parallel-session contract (12 [Test])

- **Shape:** mixed. The runner runs StartSession inline (synchronous dispatch);
  tests that only assert "the codeunit got called" pass faithfully. Tests that
  assert on parallel-session semantics (session timeout, cross-process state,
  cancellation) are §3.9 out-of-scope.
- **Existing patch classification:** §2 faithful for the inline-run case; no
  §3.9 throw on the parallel-contract path today.
- **Recommendation:** triage per-test:
  - `tests/bucket-1/codeunit-runtime/79-startsession/test/SessionApiTest.al` —
    `Assert.IsTrue(Api.TryStartSession(), 'should return true (synchronous)')` —
    this is the **faithful inline contract**, KEEP as a pass.
  - `tests/bucket-1/codeunit-runtime/1402-startsession-overloads/test` (3 [Test]) —
    inspect; likely inline-faithful, keep.
  - `tests/bucket-1/codeunit-runtime/311435-startsession-2arg/test` (3 [Test]) —
    inspect; if it asserts parallel behaviour, convert to asserterror against
    `out-of-scope: StartSession`. Need to add a §3.9 throw site first.
- **Net Category-B candidates:** likely 3-6 tests at most. Lower yield.

### NavForm.RunAsync (Page.Run non-modal, ~9 [Test])

- **Shape:** today silent no-op (Page.RunModal handler dispatch is §2; Page.Run
  non-modal is §3.11). No throw site today.
- **Existing patch classification:** silent-fake (§3 in audit terms) — should
  convert to `out-of-scope/NavForm.RunAsync`.
- **Recommendation:** add throw in a new `FormPatches.cs` for
  `NavForm.RunAsync` (non-modal entry point), then convert the 9 tests to
  `asserterror`.
- **Files:**
  - `tests/bucket-2/page-report/48-page-variable/test/ProbePageTest.al` (2 [Test])
  - `tests/bucket-2/page-report/291-page-run-with-var/test/PageRunWithVarTest.al` (5 [Test])
  - `tests/bucket-1/record-table/40-page-run-record/test/PageRunRecordTest.al` (2 [Test])

### NavFile.Upload / Download (2 [Test])

- **Shape:** silent no-op (browser round-trip — needs a client). No throw today.
- **Recommendation:** add §3.4 throw on `NavFile.Upload` / `NavFile.Download`,
  convert the 2 tests to `asserterror`. Smallest cluster but cleanest.
- **Files:**
  - `tests/bucket-1/codeunit-runtime/321-file-upload-overload/test/FileUploadOverloadTest.al` (2 [Test])

### HttpClient — NOT a Category-B cluster

Eighteen test dirs touch `HttpClient` types, but **zero of them call
`.Send / .Get / .Post / .Put / .Delete / .Patch`**. They exercise local
construction, header configuration, cookie handling, SecretText conversion,
`HttpRequestMessage.SetRequestUri`, etc. — all in-process and IN SCOPE per
scope.md §1 (".NET interop the apps use in-process"). If these tests fail
today it is a Category-A wiring issue (e.g. NavMethodScope NRE on first call),
not an OOS surface.

### Empty groups

- **Email / SMTP** — no Email.Send/Enqueue/OpenInEditor call sites in the test corpus.
- **DotNet AL surface** — no `DotNet` typed variables in active tests.
- **Debugger** — no Debugger.Attach/Break/etc. call sites.
- **Web service publishing** — no WebServiceManagement/TenantWebService usage.
- **Job queue scheduler** — no `JobQueueEntry` typed variables in tests (TaskScheduler.CreateTask uses inline-run §2).
- **KeyVault / Cert external** — no usage in test corpus.

These rows in `scope.md` are correct as written but don't currently consume
any test corpus footprint.

## Net Category-B yield estimate

| Cluster | Tests | Conversion shape | Confidence |
|---|---:|---|---|
| NavXmlPort.* | ~37 | direct asserterror conversion, no patch change needed | high (patches already throw) |
| NavReport.* | ~39 | needs `Patches/ReportPatches.cs` first; then asserterror | medium — some may move to §2 faithful Run-with-handler |
| Page.Run non-modal | ~9 | new throw site + asserterror | high |
| StartSession parallel contract | ~3-6 | new §3.9 throw site + per-test triage | low (inline-faithful path is likely the majority) |
| NavFile.Upload/Download | 2 | new throw site + asserterror | high |
| **Total realistic** | **~85-90** | one focused session per the PATH-FORWARD §B plan | matches §B estimate "~80-120" |

## Action items (suggested order for the session)

1. **Lock the exception-message contract** — `RunnerOutOfScopeException`
   already produces `out-of-scope: <api> — <reason> — see docs/scope.md#<anchor>`.
   No change needed; the `'out-of-scope:'` prefix is stable.
2. **Add throw sites** for the OOS surfaces not yet throwing:
   - `Patches/ReportPatches.cs` — `NavReport.RunReportAsync` / `SaveAsAsync` →
     `ThrowNotYetImplemented("NavReport.RunReportAsync", "HANDOFF §6 Tier 1C")`
   - `Patches/FormPatches.cs` (new) — `NavForm.RunAsync` →
     `ThrowOutOfScope("NavForm.RunAsync", "non-modal-ui", "ui")`
   - extend `Patches/MiscPatches.cs` or new — `NavFile.Upload`, `NavFile.Download` →
     `ThrowOutOfScope("NavFile.Upload", "browser-roundtrip", "file-storage")`
   - `Patches/SessionPatches.cs` — `NavSession.StartSession` (parallel-contract
     overload only — be careful to keep the inline-faithful path intact) →
     decide which overload throws after reading `MiscPatches.cs` ALStopSessionAsync
     audit (`SCOPE-AUDIT.md` row).
3. **Re-run a fresh classifier on `bucket-2/page-report`** before converting
   tests — the missing results data is the biggest gap in this list.
4. **Mechanically convert** the test files listed above, ~10-20 per commit,
   one OOS surface per commit, per `PATH-FORWARD.md §B Step 4`.
5. **Update `SCOPE-AUDIT.md`** rows for each newly-loud throw site (silent-fake → §3.x).

## Open questions

- Is `Report.Run(reportId, RequestPageOnly, record)` realistically §2-faithful
  (run the dataset + fire `[RequestPageHandler]` / `[ReportHandler]`) instead
  of §4? If yes, the 39-test cluster is Category A, not B. **Decide before
  spending Category-B budget on those tests.**
- Are the `StartSession` tests testing the inline-faithful synchronous-dispatch
  contract (keep passing) or the parallel-session contract (asserterror)?
  Per-file triage required.
- `163-xmlport-run` tests are recorded as failing on `NavMethodScope..ctor` NRE,
  not on `RunnerOutOfScopeException` — meaning the wiring gap masks the OOS
  throw. Worth confirming the throw still surfaces after the codeunit-runtime
  recursion-guard / scope wiring lands more state.
