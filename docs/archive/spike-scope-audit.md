# Scope audit — existing patches vs `docs/scope.md`

Status: first pass at v2 commit `d33ad78e`. Living document.

Rule under audit: `.claude/rules/loud-failures.md` — no silent out-of-scope failures.

## Legend

| Tag | Meaning |
|---|---|
| **§1** | Real BC code runs as written; patch only populates surrounding state. |
| **§2** | Faithful replacement — substitutes an in-process implementation observably equivalent to real BC for in-scope test code. |
| **§3** | Silent fake — returns a default value or no-ops, no observable-equivalence argument. **Must be converted** to either a faithful §2 implementation or a §3.x out-of-scope throw (per the API category in `docs/scope.md`). |
| **§4** | In-scope TODO — placeholder hook; replace with `RunnerScope.ThrowNotYetImplemented(api, plan)` so developers see it instead of getting a silent default. |
| **infra** | Populator / builder / classifier scaffolding. Not a behavioural hook — runs once at startup. |

Anchors below the table point at `docs/scope.md` rows for permanently-out-of-scope conversions.

## File-level summary

| File | Bucket | Notes |
|---|---|---|
| `RecordPatches.cs` | §2 + infra | NavRecordHandle.CreateTarget builds real Record{ID} against in-memory backend. DataAccessSource skeleton wires `TempTableDataProvider`. Faithful per `docs/scope.md §2 Table storage`. Single residual gap: `tableId=0` clone path throws InvalidOperationException — promote to §4 NotYetImplemented (HANDOFF §6 row E). |
| `RecordPatches.AlSourceParser.cs` | infra | AL `table` / `tableextension` → ParsedTable. Pure parser. |
| `RecordPatches.AlPageParser.cs` | infra | Same shape, pages. |
| `RecordPatches.AlReportParser.cs` | infra | Same shape, reports. |
| `RecordPatches.NclMetaTableBuilder.cs` | infra (§2) | Builds real `NCLMetaTable` via reflection. Faithful for declared field/key shapes incl. FlowField and tableextension. |
| `RecordPatches.NclMetaFormReportBuilder.cs` | infra (§2) | Same for forms/reports — minimal but real metadata. |
| `RecordPatches.NclMetadataCachePopulator.cs` | infra | Populates skeleton `NCLMetadata` cache from parsed sources. |
| `RecordPatches.BcAppFallback.cs` | infra | Falls back to parsing AL out of BC `.app` packages when test src doesn't define it. |
| `RecordWritePatches.cs` | §2 (drained 2026-05-11) | Insert/Modify/Delete/Rename bypass replacements removed (`ae15b158`, `c2df0bcd`, `29b5acc9`); the real Ncl `*Async` bodies now run and dispatch AL triggers + BC subscribers natively. Side-effect no-ops kept: `RecordLink.MoveLinksAsync` and `NavRecord.UpdateReferencesOnRenameAsync` (skeleton has no link cascade / no FK references). Other hooks (`VerifyPermissions` → `NoOp3`, `VerifySecurityFiltersAsync` → `ReturnValueTask3`, `RecordImplementation.get_IsOpen` → `ReturnTrue`): faithful per scope.md §2/§3.8 — permissions all-granted, security filters not enforced, in-memory provider always open. |
| `NavRecordRefPatches.cs` | §2 | NavRecordRef.get_Target builds real SharedRecordRef against the skeleton container. Faithful for RecordRef semantics. |
| `ApplicationObjectBasePatches.cs` | §2 | Rebuilds skeleton ctor state — faithful for all object lifetime semantics. |
| `CodeunitPatches.cs` | §2 | DoRunAsync calls the real AL-emitted OnRun via reflection. NavCodeunitHandle.CreateTarget finds `Codeunit{ID}` in the test assembly. Faithful. |
| `CodeunitPatches.MetaCodeunit.cs` | §2 | Option-C polyfill: reflects `[NavCodeunitOptionsAttribute]` off the AL class. Faithful for `IsEventManualBinding` and friends. |
| `MethodScopePatches.cs` | §2 with caveat | NavMethodScope ctor + AssertError + ProcessException. **AssertError:** correctly routes asserterror through BC's error channel (scope.md §1). **ProcessException → false:** silent — means "exception not handled, propagate"; needs justification or convert. **Rollback as no-op (`SessionTransactionExtensions.Rollback` → NoOp_OneArg in BcRuntime.cs):** out of scope per `docs/scope.md §3.10 transactions`. Currently silently no-ops; **must convert** to either run as a no-op faithfully (no transactions exist) — defensible since `Commit` already documented no-op in limitations.md — or throw on explicit Rollback. Recommend documenting Commit/Rollback as faithful per scope.md §3.10 (no transactions → no-op IS the closest faithful behaviour). |
| `SessionPatches.cs` | §2 | All hooks (NavAppGroup, LocalLanguageNoFallback, GetSecurityFilters, SyncFormatSettings, Culture) provide skeleton state that the BC code reads. NavSession.get_IsLocalLanguage → ReturnFalse_1Arg, PushDynamicCaptionStack → ReturnFalse_3Args: faithful — IsLocalLanguage=false matches the "no live BC session" reality, PushDynamicCaptionStack=false signals "no dynamic caption to push", which is the correct skeleton answer. |
| `MetadataPatches.cs` | §2 + infra | Field-pokes skeleton NavSystemTenant + NCLMetadata. `NCLMetaApplicationObject.Populate` → NoOp_OneArg: faithful — populate is a metadata refresh that we don't need because we've already built the metadata directly. `CompileAndLoadClrObject` → NoOp_OneArg: faithful — the type is already loaded, no compile needed. |
| `EnumMetadataPatches.cs` | §2 | Populates real `NCLOptionMetadata` for AL enums. Faithful. |
| `EnvironmentPatches.cs` | §2 | Skeleton NavEnvironment for headless mode. Faithful. |
| `MiscPatches.cs` | mostly §2 | `ALSession_GetALCurrentClientType`: returns a fixed `NavClientType` (faithful per scope.md §2, headless = "background" effectively). `ALSession.ALStopSessionAsync`: ValueTask<bool> noop — needs a look (is this an out-of-scope **§3.9 parallel-session** call, or faithful inline?). `ALSystemErrorHandling.*`: error-channel reads, faithful per BC contract. |
| `TelemetryPatches.cs` | §2 | All hooks (NavServerEventSource, CallStackElement, NavDialog.ALError → NavALErrorInfo) are either telemetry no-ops (scope.md §2 — observable-equivalent because no in-scope test asserts on telemetry) or the actual ALError → throwable conversion that keeps `asserterror` working. **Faithful.** |
| `XmlPortPatches.cs` | §4 TODO (converted from §3 on 2026-05-11) | Export/Import/Run/SetTableView/static Export/static Import now `ThrowNotYetImplemented` via `4a8ba526`. Ctor-time scaffolding (BeginInit/EndInit/InitializeComponent/AddXNode/TableNode ctor) remains faithful no-op with inline justification — required for XmlPort{ID} construction to succeed. In-memory XmlPort variants eventually in scope per user decision; deferred. |
| `HelperShims.cs` | infra (helpers) | Generic NoOp / ReturnFalse / ReturnValueTask shims. Themselves not classified — each call site that uses them is what counts. See per-hook table. |
| `AsyncStateMachineSpike.cs` | spike | Spike scaffolding for entry-point hook on async methods. Used for `ALFieldCaptionAsync` originally; that path is now faithful via Option-C. File can probably be retired or shrunk. |
| `EventPipeJitListener.cs` | scaffolding (disabled) | Spike-4 mechanism. Not currently enabled. Becomes §2-mechanism once Tier 1C deployment lands. |
| `BcRuntime.cs` | mixed | Wires every hook. See per-hook decisions below for specific ones that don't fit a single category. |

## Notable per-hook decisions (not yet acted on)

These are the hooks where the rule's bite is sharpest. Each row's "Action" column is the proposed next step, not yet executed.

| Hook | Replacement today | Bucket | Reasoning | Action |
|---|---|---|---|---|
| `NavXmlPort.Export(DataError)` and overloads, `Import`, `Run`, `SetTableView`, static Export/Import | `ThrowNotYetImplemented(...)` | §4 TODO | Converted from silent no-op via `4a8ba526` per loud-failures rule. In-memory XmlPort serializer is in scope eventually. | Land an in-process XmlPort serializer when prioritized — until then the loud throw classifies the gap honestly. |
| `NavXmlPort.BeginInitialization` / `EndInitialization` / `InitializeComponent` / `AddXNode` / `NavXmlPortTableNode.ctor` | no-op (with inline justification) | §2 (faithful) | Ctor-time scaffolding required so `XmlPort{ID}` construction itself succeeds; no observable AL-test behavior to fake. Inline comments added in `4a8ba526`. | Keep. |
| `RecordWritePatches.NavRecord_InsertAsync` / Modify / Delete / Rename (the four bypass replacements) | **NOT installed** as of 2026-05-11 | §2 faithful | Drained via `ae15b158` (Insert), `c2df0bcd` (Delete + Rename), `29b5acc9` (Modify). Real Ncl bodies now run end-to-end, AL triggers fire, BC subscribers dispatched via `c4bce11a` A-prime registry injection. Recursion guard at 500 frames (`f8367536`) catches recursive triggers. | Done. Replacement method bodies left in place for reference / quick re-enable. |
| Subscriber dispatch | `EventSubscriberPatches.cs` (NEW) — scans `[NavEventSubscriber]`, constructs real `NavEventSubscription`, injects into BC's `eventScopes[evt].registeredSubscriptions` | §2 faithful | BC's own `NavEventScope.CheckAndFireTriggerEventsAsync` then dispatches naturally. R2R-inlining bypassed entirely (see `feedback_r2r_inlining_traps.md`). | Done as of `c4bce11a`. Follow-on: manual-binding subscribers (`BindSubscription`). |
| `NavMethodScope` ctor + Dispose | 500-frame ThreadStatic depth counter, throws `NavNCLDialogException("Maximum recursion depth")` past threshold | §2 faithful | Mirrors BC's runtime-error-after-N-frames contract per user clarification. Landed `f8367536`. | Follow-on: make threshold configurable, verify matches real BC's limit precisely. |
| `NCLMetaApplicationObject.Populate` → NoOp | NoOp_OneArg | §2 | Metadata already built directly by our populator. Populate's role on real BC is to refresh from compiled .app; not applicable. | Add inline comment justifying observable equivalence. Keep. |
| `NCLMetaApplicationObject.CompileAndLoadClrObject` → NoOp | NoOp_OneArg | §2 | The CLR type is already loaded. CompileAndLoad's real job is to JIT-compile metadata-driven runtime objects; we don't need that. | Same — keep with comment. |
| `RecordImplementation.VerifyPermissions` → NoOp | NoOp3 | §2 | Permissions are §2 in scope.md (all-granted skeleton). Bypassing the verify is observably equivalent to "all permissions granted". | Inline comment. Keep. |
| `RecordImplementation.VerifySecurityFiltersAsync` → ReturnValueTask3 | ReturnValueTask3/4/5 | §2 | Same — security filters not enforced in skeleton, scope.md §3.8 boundary. | Inline comment. Keep. |
| `RecordImplementation.get_IsOpen` → ReturnTrue | ReturnTrue(self) | §2 | In-memory provider is always "open" by definition. | Keep. |
| `SessionTransactionExtensions.Rollback` → NoOp | NoOp_OneArg | §2 | `docs/scope.md §3.10`: no transactions → no-op is the closest faithful behaviour, parallel to `Commit`. | Document in inline comment so it doesn't read as a silent fake. Keep. |
| `NavMethodScope.ProcessException(Exception)` → false | ReturnFalse2 | §2 (with caveat) | "false" = "not handled, propagate" — that IS the correct BC contract when there's no service-tier ProcessException to consume the exception. | Inline comment. Keep. |
| `NavRecord.Dispose(bool)` → NoOp2 | NoOp2 | §2 | Skeleton records don't hold OS resources to dispose; no-op is faithful. | Keep. |
| `NavRecord.IsGlobalTriggerImplemented` → ReturnFalse2 | ReturnFalse2 | §2 with caveat | "No global trigger implemented" matches "no GlobalTriggerImplementation registered in skeleton"; faithful IF tests don't register one. | Keep, but flag for re-evaluation if global-trigger AL test patterns surface. |
| `ALTaskScheduler.CheckCodeUnit` → NoOp | NoOp2 | §2 | TaskScheduler is documented as scope.md §3.6 — inline runs. Skipping CheckCodeUnit is consistent with "no real scheduler to validate against". | Keep. |
| `NavDataTransfer.SetTables` → NoOp | NoOp3 | §2 with caveat | DataTransfer is bulk-copy between staging tables. With in-memory storage the staging concept doesn't fully translate. | Audit further — could be a §3 silent if AL tests run DataTransfer and read the result. |
| `ALFunctionTimingExecutionListener.{EnsureRegistered,Start,Exit}` → NoOp | NoOp_0Args / NoOp_OneArg | §2 | Diagnostic timing — telemetry-shape. Faithful per scope.md §2 "no test asserts on telemetry". | Keep. |
| `NavOpenTelemetryLogger..ctor` | NoOpN | §2 | Telemetry-shape. | Keep. |
| `TempTableStatistics.ReportIncrementChange` → NoOp | NoOp4 | §2 | Diagnostic statistics. Faithful. | Keep. |
| `NavServerEventSource.get_Log` → skeleton ES | hook | §2 | EventSource skeleton, telemetry-shape. | Keep. |
| `ALDatabase.AL*` cluster (8 NRE-shape methods) | **Two attempts reverted** — silent stubs `b01c0111` and ThrowOutOfScope `5dce5c23` both segfaulted runner | §3.x but unable to land | Both attempts crashed during BC dep-resolution after "patches applied" succeeded; root cause not yet diagnosed (R2R native references suspected). See `feedback_aldatabase_hard.md`. | NOT a Sonnet task. Next attempt requires instrumented per-hook address logging + coredump capture, or EventPipe post-JIT body patch instead of precode JmpHook. |
| `NavApplicationObjectBaseHandle\`1.get_Target` on tableId=0 | `ThrowNotYetImplemented("NavRecord.CloneForVariant (default-variant tableId=0)", ...)` | §4 TODO | Landed `8efcc462`. Worker discovered the 14-test cluster split: 5 are tableId=0 default-variant, 8 are genuine "AL source not parsed" for system table 2000000041 (different gap). | Synthetic empty NavRecord for the default-variant case eventually. System-table-2000000041 case needs a separate BcAppFallback entry. |

## Decisions taken 2026-05-11 (post-audit)

- **Triggers MUST work.** Confirmed ground-truth on `100-uninit-field-fix` suite:
  - `OnInsertTrigger_SetsFlag_AfterInsert` (positive) → FAILS today.
  - `OnBeforeInsertEvent_SubscriberSetsFields` (positive) → FAILS today.
  - `OnInsertTrigger_WithoutRunTrigger_DoesNotSetFlag` (negative) → passes for the wrong reason (bypass means `runTrigger` is *never* honored).
  Tied to W-8 in CLASSIFICATION.md. Highest priority — without it the entire corpus's pass count is misleading on every trigger/subscriber test.
- **XmlPort: not top priority, eventually want in-memory Run(InStream)/Run(OutStream) shapes only.** File-path / browser-roundtrip variants stay out of scope. Interim: convert current silent no-ops to `RunnerScope.ThrowNotYetImplemented` so they stop passing silently.
- **ALDatabase: case by case.** Most methods are licensing/auth/connection-shape → `docs/scope.md §3.8` throws. A few (`ALSid` against a configurable user identity?) may be §2-populatable. Re-plan with stash@{0}'s null-root diagnosis as input.

## Corrections to `docs/scope.md`

- Event subscriber and validation trigger rows were inherited from v1's reality. v2 has neither today. Both rows updated to "PLANNED — not yet implemented" with a pointer back to this audit and CLASSIFICATION.md W-7/W-8.

## Action queue — refreshed 2026-05-11 EOD

### Closed in this session
- ✅ Trigger-bypass question — DRAINED. Insert/Modify/Delete/Rename real Ncl bodies run; AL triggers fire.
- ✅ Subscriber dispatch — A-prime via BC's own registry, no JmpHook on dispatch path.
- ✅ Recursion guard — 500-frame ThreadStatic depth counter.
- ✅ XmlPort silent fakes → ThrowNotYetImplemented.
- ✅ tableId=0 throw split + reclassified.

### Still open

1. **ALDatabase cluster — needs instrumented investigation, not retries.** Two Sonnet attempts crashed. See `feedback_aldatabase_hard.md`. Plan: instrumented per-hook addr logging + coredump capture, or EventPipe post-JIT body patch.
2. **Inline-comment justification on kept §2 hooks** — RecordWritePatches `VerifyPermissions` / `VerifySecurityFiltersAsync` / `get_IsOpen` / `Rollback` / `ProcessException` / etc. ~10-20 short comments to mark "this is faithful per scope.md §X, not a silent fake."
3. **Walk the remaining hooks in BcRuntime.cs** not covered by the per-file table (`NavCancellationToken.*`, `NavStringValue.op_Implicit`, `NavTextConstant.get_Value` — JIT-inlined TFO, Tier 1C dependency — `SequentialUuidCreator.NewSequentialId`, etc.) for completeness. ~30-60 minutes.
4. **`NavRecord..ctor`** for RecordLink / Company built-in tables — BcAppFallback entries needed. 13-test cluster.
5. **`NavMethodScope` recursion threshold** — currently hard-coded 500; make configurable and verify against real BC's limit precisely.
6. **Subscriber manual-binding** (`BindSubscription`) — auto-binding works; manual deferred.

## Open questions

- `HelperShims` could grow a `BcRuntime.NoOp_Verified` variant that logs first-call so we get telemetry on which silent paths are actually exercised. Cheap diagnostic.
- The audit can be re-run by grep'ing corpus output for `out-of-scope/<api>` — see which APIs actually fire in tests and how many failures they bucket. Now possible because the classifier branch landed in `d33ad78e`.
