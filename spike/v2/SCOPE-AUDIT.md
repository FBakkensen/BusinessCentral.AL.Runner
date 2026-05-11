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
| `RecordWritePatches.cs` | §2 with caveat | InsertAsync / ModifyAsync / DeleteAsync / RenameAsync replacements bypass trigger dispatch + DataModificationListener. **Caveat:** if a test relies on `OnInsert` / `OnModify` / `OnRename` triggers firing inside these calls, the replacement makes them silently no-op. Triggers fired through other entry points still work. Decision pending — see audit row below. Other hooks in file (`VerifyPermissions` → `NoOp3`, `VerifySecurityFiltersAsync` → `ReturnValueTask3`, `RecordImplementation.get_IsOpen` → `ReturnTrue`) match §2-with-caveat: permissions are §2 (all-granted, scope.md §3.8); IsOpen=true matches the in-memory-storage contract; security filters are §2 (scope.md §3.8 boundary). |
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
| `XmlPortPatches.cs` | **§3 silent fake** | Export/Import/Run/SetTableView/BeginInit/EndInit/InitializeComponent/static Export/static Import all return `true` or no-op. XmlPort is a serialization mechanism that **should be in scope** as an in-memory data transform (it's all in-process — no external file required for memory-stream-based variants). **Decision needed:** either implement a real in-memory XmlPort serializer (§2 / §4), or throw `RunnerOutOfScope("NavXmlPort.<method>", "not-yet-implemented", "todo")` for now. Today's stubs make tests silently pass without doing any serialization — a green test is misleading. |
| `HelperShims.cs` | infra (helpers) | Generic NoOp / ReturnFalse / ReturnValueTask shims. Themselves not classified — each call site that uses them is what counts. See per-hook table. |
| `AsyncStateMachineSpike.cs` | spike | Spike scaffolding for entry-point hook on async methods. Used for `ALFieldCaptionAsync` originally; that path is now faithful via Option-C. File can probably be retired or shrunk. |
| `EventPipeJitListener.cs` | scaffolding (disabled) | Spike-4 mechanism. Not currently enabled. Becomes §2-mechanism once Tier 1C deployment lands. |
| `BcRuntime.cs` | mixed | Wires every hook. See per-hook decisions below for specific ones that don't fit a single category. |

## Notable per-hook decisions (not yet acted on)

These are the hooks where the rule's bite is sharpest. Each row's "Action" column is the proposed next step, not yet executed.

| Hook | Replacement today | Bucket | Reasoning | Action |
|---|---|---|---|---|
| `NavXmlPort.Export(DataError)` and overloads | `return true` | §3 silent fake | XmlPort serialization happens entirely in-process; faithful in-memory impl is realistic. | Either land a real in-process XmlPort serializer or replace each with `ThrowNotYetImplemented("NavXmlPort.Export", "real in-memory XmlPort serializer — open issue")`. Tests that depend on actual XML output currently silent-pass. |
| `NavXmlPort.Import(...)` (incl. static overload) | `return true` | §3 silent fake | Same as Export. | Same. |
| `NavXmlPort.Run()` / `RunXmlPort()` | no-op | §3 silent fake | Same. | Same. |
| `NavXmlPort.SetTableView(NavRecord)` | no-op | §3 silent fake | Reasonable as a setter no-op IF Run/Export/Import are real. If they stay no-op, this is also silent. | Tie decision to Run/Export/Import. |
| `NavXmlPort.BeginInitialization` / `EndInitialization` / `InitializeComponent` | no-op | §2 (with caveat) | These are ctor-time scaffolding that BC's XmlPort body needs to NOT NRE on. With Export/Import faithful, these stay as faithful skeleton-init no-ops. With Export/Import as throws, these are unreachable. | Re-evaluate after Export/Import decision. |
| `NavXmlPortTableNode.ctor` | sets empty child lists via field-poke | §2 (infra) | Faithful skeleton init for an internal BC ctor. Keep. | — |
| `RecordWritePatches.NavRecord_InsertAsync` (and Modify/Delete/Rename Async) | calls real `RecordImplementation.<Op>RecordAsync` but bypasses trigger dispatch / DataModificationListener / event subscribers | §2 with caveat | Storage is faithful. **Trigger / subscriber dispatch is silently dropped.** Per `docs/scope.md §1 Event subscribers` and `docs/scope.md §1 Validation triggers`, both are listed as in-scope-real. Today's bypass contradicts the manifest. | Decide: (a) rewire the bypass to fire AlCompat.FireEvent for table-modification events the way the real BC service tier does, then mark this row §2-faithful; or (b) document an explicit carve-out in scope.md §2 (with the caveat as the boundary). Recommended (a) eventually; (b) interim if (a) blocks. |
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
| `ALDatabase.AL*` cluster (reverted from session b01c0111) | — | §3 silent + §4 TODO | Yesterday's worker-3 attempt returned fixed values (S-1-0-0, "", 0). Per the new rule these would have been §3 silent fakes. The stash@{0} WIP has real diagnostic value (3 null roots on skeleton session identified) — keep for reference but re-approach as either skeleton-state population (§2) or per-method `ThrowOutOfScope("ALDatabase.X", "external-license/identity", "licensing")`. | Re-plan with new rule. Most ALDatabase methods are licensing/auth-shape → scope.md §3.8 throws. A few (`ALSid`?) might be §2-populatable if we wire a deterministic UserId. |
| `NavApplicationObjectBaseHandle\`1.get_Target` on tableId=0 | throws InvalidOperationException ("AL source not parsed") | §4 (almost) | Today already throws — but with the wrong type and a misleading message ("not parsed" implies a populator gap, not the real "default-variant clone semantic mystery"). | Convert throw to `ThrowNotYetImplemented("NavRecord.CloneForVariant from default", "HANDOFF §6 row E — synthetic empty NavRecord for tableId=0")`. |

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

## Action queue (post-audit)

1. **Decide the trigger-bypass question** for `RecordWritePatches.NavRecord_InsertAsync` & siblings — §2 carve-out vs faithful trigger dispatch. This is the biggest fidelity question still open.
2. **XmlPort family — decide §2 vs §4.** Either implement an in-memory serializer or convert each method to ThrowNotYetImplemented. Recommend ThrowNotYetImplemented as an interim — XmlPort matters less than core record operations and a real implementation is days of work.
3. **ALDatabase cluster** — reuse stash@{0}'s null-root diagnosis. Most methods → throws against scope.md §3.8; ALSid/ALSessionID may be §2 if we expose a configurable session identity.
4. **Inline-comment justification on every kept §2 hook** so future readers (and agents) don't mistake them for silent fakes. ~10-20 short comments.
5. **Convert tableId=0 path in NavRecordHandle_CreateTarget** to ThrowNotYetImplemented.
6. **Walk the remaining hooks** registered in BcRuntime.cs not covered by the per-file table (e.g. `NavCancellationToken.*`, `NavStringValue.op_Implicit`, `NavTextConstant.get_Value`, `SequentialUuidCreator.NewSequentialId`, etc.) for completeness. ~30-60 minutes.

## Open questions

- Some hooks (`NavTextConstant.get_Value` etc.) are the JIT-inlining TFO residual cases — Tier 1C territory. Their replacement strategy is tied to EventPipe. Document those as §4 with "Tier 1C dependency".
- `HelperShims` themselves should probably have a `BcRuntime.NoOp_Verified` variant that also logs the first time it's called, so we get telemetry on which silent paths are ACTUALLY reached during the test corpus run. Cheap diagnostic, would help prioritize action queue.
- The audit can be re-run by grep'ing the corpus output for `out-of-scope/<api>` once §3 fakes are converted — we'll see which APIs actually fire and how many tests they affect.
