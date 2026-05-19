# Corpus Classification — 2026-05-19 (HEAD `9b82e385`, post light-bucket migration)

**Branch:** `spike/bc-abi-identity` at `9b82e385d1f779e4978f96298b210a94bf2188c`.
**Generated:** 2026-05-19 21:25 (UTC+02:00).
**Inputs:** Six classifier passes (one per bucket) against `dotnet run --project spike/v2/Runner -c Release`, JSONs captured at `/tmp/classify/*.json`. Cluster sizes are live, not eyeballed; previous inventories
(`PAGE-REPORT-CLUSTERS.md`, `OOS-WORKING-LIST.md`, `ALTESTBUG-WORKING-LIST.md`, `R2R-DOWNSTREAM-MAP.md`)
were sized off the pre-Inv-1/Inv-2/Inv-1b state and have steered Inv 3+4 wrong — supersede with this table.

**Stash status:** Cecil-rewrite mechanism remains at `stash@{0}` (untouched, as required).

---

## 1. Per-bucket pass / fail (authoritative live baseline)

| Bucket | P | F | Total | Pass-rate | Wall-clock (cold, sequential) |
|---|---:|---:|---:|---:|---:|
| `bucket-1/codeunit-runtime` | 764 | 224 | 988 | 77.3% | 41 s |
| `bucket-1/record-table` | 749 | 156 | 905 | 82.8% | 84 s |
| `bucket-1-heavy/codeunit-runtime` | 0 | 3 | 3 | 0.0% | 56 s |
| `bucket-2/data-formats` | 1 402 | 157 | 1 559 | 89.9% | 37 s |
| `bucket-2/page-report` | 272 | 345 | 617 | 44.1% | 30 s |
| `spike-a-baseapp` | 8 | 0 | 8 | 100.0% | 60 s |
| **Total** | **3 195** | **885** | **4 080** | **78.31 %** | **308 s** |

Notes:
- 308 s here is six sequential `dotnet run` startups (one per bucket); the parallel light-bucket cold-run figure of ~240 s from the migration commit message is still valid. Per-bucket "test run" inner times sum to ~275 s.
- `bucket-1-heavy/codeunit-runtime` is intentionally tiny — three suites that fail by design today; not a regression.
- `spike-a-baseapp` is a green canary; including it for completeness.

---

## 2. Headline

- **Total remaining fails: 885** (vs. 897 in `PATH-FORWARD.md` baseline against `d05bab7f` — net **−12** since Inv 1/Inv 2/Inv 1b landed).
- Approximate category split (clusters with ≥ 3 tests classified inline; long tail of < 3 left as `Cat ?`):

  | Cat | Tests | % of fails | Notes |
  |---|---:|---:|---|
  | **A — wire-shaped** | ≈ 150 | 17 % | Skeleton field-poke / JmpHook downstream-frame — proven pattern |
  | **B — out-of-scope** | ≈ 40 | 5 % | `asserterror` + `Assert.ExpectedError('out-of-scope: …')` rewrite |
  | **C — AL test bug** | ≈ 30 | 3 % | Fix in test source |
  | **D — R2R-trapped, no downstream** | ≈ 280 | 32 % | Needs the Cecil-rewrite mechanism (`stash@{0}`) |
  | **? — multi-root / unclassified** | ≈ 230 | 26 % | Dominated by `NavDialog.ALError` (204) — silent AL Assert failures with many roots |
  | **long-tail clusters < 3 tests each** | ≈ 155 | 18 % | Not yet enumerated; cheap to harvest after the big buckets |

- **Highest-yield single intervention:** the **Cecil-rewrite mechanism currently in `stash@{0}`** addresses Cat D (≈ 280 tests in one stroke if it works at the breadth implied by the existing R2R map). All four R2R-trapped Group-B targets from `R2R-DOWNSTREAM-MAP.md` line up against the top of this inventory (`NavForm.GetAutoFormatStringAsync` 115, `NavReport.RunReportAsync`/`SaveAsAsync` 58, `NavForm.GetMasterPage` 33, plus the `Report*..ctor` family at 45).
- **Second-highest yield:** splitting the **204-test `NavDialog.ALError` cluster** by Assert message — it's a mixed bag of subscriber-counter assertions (Cat A/D via `IsEventSubscribed` state injection), test-fixture bugs (Cat C), and OOS-shaped assertions (Cat B) that need a dedicated session to disentangle.
- **Classifier re-run wall-clock:** **~ 308 s sequential** (this run) / **~ 240 s parallel** (light-bucket migration baseline). Classification jobs are now cheap enough to run after every meaningful change.

---

## 3. Ranked failure clusters (≥ 3 tests)

Ordered by failing-test count. "Cat" column uses the brief's A/B/C/D/?. The "Action" column is recommended; treat as a hypothesis to validate, not gospel.

| # | Cluster (error-signature root) | Tests | Bucket distribution | Cat | Recommended action |
|---:|---|---:|---|:---:|---|
| 1 | `NavDialog.ALError` (AL Assert / Error catch-all) | **204** | codeu=73 / recor=41 / data=69 / page=21 | **?** | Multi-root noise per AVOID list. Sub-split by Assert message tail (see §6 below); subscriber-counter rows route to Cat D (`IsEventSubscribed` state-injection); HTTP-not-supported asserts route to Cat B; "Index out of bounds" / "no property with this name" likely Cat C. |
| 2 | `NavForm.GetAutoFormatStringAsync` (NRE) | **115** | page=90 / codeu=18 / recor=7 | **D** | R2R-trap confirmed (`R2R-DOWNSTREAM-MAP.md` §3). No downstream JIT'd frame. **Cecil-rewrite candidate.** Trap is silent — return `""` deterministically and assertions will green. |
| 3 | `NCLMetadata.ThrowMetaApplicationObjectNotFound` | **56** | page=46 / codeu=5 / recor=5 | **A** | Wire-shaped — AL test references `Table 57400` / `Page X` that wasn't registered in the runner's `NCLMeta*` registry. Register stub metadata for the missing IDs (or fail loudly with a runner-side OOS instead of letting `NavALException` bubble up unhelpfully). Same shape as the metadata-registry work done in `c95debdc`. |
| 4 | `NavReport.RunReportAsync` (ArgNull `parent`) | **45** | page=45 | **D** | Same R2R trap family as #2 / #8 (`R2R-DOWNSTREAM-MAP.md` §1). One R2R body, no downstream JIT'd frame. **Cecil-rewrite candidate** — collapses to one rewrite target shared with `SaveAsAsync`. |
| 5 | `NavForm.GetMasterPage` (NRE) | **33** | page=24 / codeu=9 | **D** | `R2R-DOWNSTREAM-MAP.md` §2. No downstream JIT'd. **Cecil-rewrite candidate.** |
| 6 | `Report126000..ctor` (NRE) | **19** | page=19 | **D** | Inv 3 documented this exact shape (`PATH-FORWARD.md` finding 5 — "two-part fix"): `LookupNclMetaForReport` fixes metaArg but `metadata.StaticMetadata` is still null. Cecil-rewrite of `Report*..ctor` body is the cleanest path; same root for #11, #21, #41, and all Report-NRE rows below. |
| 7 | `NavRecord.CloneForVariant` (OOS) | **13** | data=7 / recor=5 / codeu=1 | **B** | Already throws `RunnerOutOfScopeException` correctly. **Tests need to be converted to `asserterror`/`Assert.ExpectedError('out-of-scope: NavRecord.CloneForVariant …')`** — mechanical sweep. |
| 8 | `NavReport.SaveAsAsync` (ArgNull `parent`) | **13** | page=13 | **D** | Same body as #4. Counted separately by classifier because the AL caller path differs (SaveAs vs RunReport) but collapses to one Cecil-rewrite target. |
| 9 | `NavRecord..ctor` (NRE) | **9** | codeu=4 / recor=5 | **D** | Same R2R-ctor shape as #6 (NCLMeta null path inside ctor). Likely fixed by the same Cecil-rewrite pattern as `Report*..ctor`. |
| 10 | `NavDialog.ALUpdateAsync` (NRE) | **8** | codeu=5 / data=3 | **A** | NavDialog skeleton state. Wire-shaped field-poke. |
| 11 | `NavTestPageBase.CheckPageOpened` ("TestPage not open") | **8** | page=8 | **C / ?** | Either AL fixture forgot to call `.OpenView()`, or runner's TestPage `Open` state isn't wired through. Inspect a sample suite before classifying. |
| 12 | `NavForm.RunModalAsync` (NRE) | **7** | page=4 / codeu=2 / recor=1 | **D** | NavForm R2R family (sibling of #2 / #5). Likely needs the same Cecil-rewrite. |
| 13 | `NavCodeunit.BindSubscription` (NRE) | **7** | codeu=7 | **A** | Subscriber-registry state injection (`PATH-FORWARD.md` Group-B finding for `IsEventSubscribed` says the proven pattern is upstream field-poke, not downstream hook). |
| 14 | `ALSystemArray.ALCopyArray[T]` (`ArrayLengthMismatch`) | **7** | codeu=2 / data=5 | **C** | This is correct BC behavior. AL tests are calling `CopyArray` with mismatched lengths. Either tests are buggy or they intend `asserterror` and forgot the wrapping — verify, then fix in AL. |
| 15 | `NavDataTransfer.CheckIsOpen` ("SetTables must first be called") | **7** | codeu=7 | **A / C** | If tests omit `SetTables` deliberately → Cat C. If they call `SetTables` and it silently doesn't register → Cat A. Inspect one suite first. |
| 16 | `ALSystemOperatingSystem.GetUrlCore` (NRE) | **7** | codeu=7 | **B** | Returns URL of host service; out of scope for the runner. Convert in BC to OOS-throw, sweep tests to `asserterror`. |
| 17 | `NavNotification.ALSend` (NRE) | **7** | codeu=7 | **A** | Notification dispatch — wire `NavNotificationContext` skeleton or no-op the call after recording the notification, depending on whether tests assert on the notification content. |
| 18 | `NavRecordRef.get_SafeRecord` ("Record is not open") | **6** | codeu=3 / recor=3 | **A / C** | If tests use `RecordRef` without `Open(...)` → Cat C; if runner's RecordRef open-state machine is missing a path → Cat A. |
| 19 | `NavObjectList\`1.get_Target` (NRE) | **6** | codeu=5 / data=1 | **A** | Object-handle target resolution — typically a missing registry-lookup wire. |
| 20 | `TraceStringHelper.CreateALFunctionStatementTrace` (NRE) | **6** | codeu=2 / recor=4 | **A** | Trace infra reads a session/method-scope field that isn't initialised. Field-poke. |
| 21 | `TrappableHttpOperationExecutor.HandleExceptions` ("operation cannot be performed") | **6** | data=6 | **B** | HTTP. Out of scope. Convert tests to `asserterror`. |
| 22 | `Report84501..ctor` (NRE) | **6** | page=6 | **D** | Same as #6. Cecil-rewrite. |
| 23 | `NavCodeunitHandle.ALAssign` (`NotSupportedOperation`) | **5** | codeu=2 / data=3 | **B / C** | "Operation is not supported" — if BC really doesn't support assigning these handles, that's correct behavior → Cat C in tests, or Cat B (convert to asserterror). |
| 24 | `NavSystemCodeunitUIHelperTriggers..ctor` (ArgNull `parent`) | **5** | codeu=1 / data=2 / page=2 | **D** | Same ArgNull(`parent`) R2R-ctor shape as #4 / #6 / #8. Cecil-rewrite. |
| 25 | `ALCompiler.ToInterface` ("cannot cast enum 'No' to interface") | **5** | codeu=5 | **A** | Interface-implementation registry missing. Wire enum → interface map. |
| 26 | `DataAccessSource.CreateTenantDataProvider` (NRE) | **5** | recor=4 / codeu=1 | **A** | `NavTenant` skeleton (`PATH-FORWARD.md` finding 3 — followup). Field-poke `<Database>k__BackingField` and friends. |
| 27 | `ALDatabase.ALRegisterTableConnection` (NRE) | **5** | recor=5 | **?** | "Poisoned" per `feedback_aldatabase_hard` rule — runner cannot JmpHook ALDatabase without regressing. Cecil-rewrite of the ALDatabase static body MIGHT be the right route; needs scoping inside the next session. |
| 28 | `ALDatabase.ALUnregisterTableConnection` (NRE) | **5** | recor=5 | **?** | Same as #27. |
| 29 | `TrappableXmlOperationExecutor.<>c.<ExecuteXmlOperation>b__0_1` | **5** | data=5 | **B** | XML-over-HTTP via Trappable executor. Out of scope. |
| 30 | `ALCompiler.NavIndirectValueToNavValue[T]` (`TypeConversion`) | **5** | data=5 | **A / C** | Likely missing variant-type conversion path. Inspect — if BC genuinely refuses the cast → Cat C; otherwise wire. |
| 31 | `NavMediaSet.ALInsert` (NRE) | **5** | page=5 | **A** | MediaSet store wiring (continuation of impl-17 / `media-gaps` historical work). |
| 32 | `Report91000..ctor` (NRE) | **5** | page=5 | **D** | Report ctor family. |
| 33 | `NavFilterPageBuilder.FindFilterControlInformation` ("Filter control Code is not defined") | **4** | codeu=3 / recor=1 | **C** | Real BC validation; tests use a control name not in the builder. AL fixture bug. |
| 34 | `ALNumberSequence.SequenceNameForALNumberSequence` ("not a valid sequence name") | **4** | codeu=2 / data=2 | **C** | Test fixture uses timestamp-derived name with `:` and `.` chars — BC rejects them. Test bug. |
| 35 | `NavDialog.ALClose` (NRE) | **4** | codeu=4 | **A** | Dialog state machine — close without open. |
| 36 | `ALNavApp.ALGetResourceAsTextAsync` (NRE) | **4** | codeu=3 / data=1 | **A** | App-resource registry not wired for the test app. |
| 37 | `NavSession.get_License` (NRE) | **4** | recor=3 / codeu=1 | **A** | License skeleton on NavSession — extends Inv 1 pattern (`<Authenticator>k__BackingField`). |
| 38 | `NavHttpClient.<>c__DisplayClass53_0.<AddCertificate>b__0` (`HttpRequestException`) | **4** | codeu=2 / data=2 | **B** | HTTP. Out of scope. |
| 39 | `ALTaskScheduler.CanCreateTask` (NRE) | **4** | codeu=4 | **A** | Task scheduler skeleton or OOS-throw (`B`). |
| 40 | `ALDatabase.ALChangeUserPassword` (NRE) | **4** | recor=4 | **B** | Password change is service-tier-only. Out of scope. |
| 41 | `ALDatabase.ALDataFileInformation` ("CompanyRecord parameter required") | **4** | recor=4 | **C** | Real BC contract; AL fixture passes default record. Test bug. |
| 42 | `NavRecord.ValidateTruncateSupport` ("security filter set") | **4** | recor=4 | **C** | Real BC enforcement; test triggers it. Either Cat C (test bug) or Cat B (intentional — convert to asserterror). |
| 43 | `ALDatabase.ALGetDefaultTableConnection` (NRE) | **4** | recor=4 | **?** | ALDatabase-poisoned (see #27). |
| 44 | `Codeunit59730.CallSet_Scope.OnRun` (NRE) | **4** | recor=4 | **?** | Codeunit-scope NRE — need to see the inner frame to classify. Likely descendant of one of #2–#5. |
| 45 | `NavFileHelper.MapAndRethrowClrException` ("path1 null") | **4** | data=4 | **B / C** | File I/O on null path — likely AL test bug (passing empty filename) but might also be Cat B (file I/O surface). |
| 46 | `Codeunit59750.PickWithDefault_Scope.OnRun` (`CallbackNotAllowed`) | **4** | page=4 | **B** | "Callback functions are not allowed" is the standard runner OOS shape; convert tests to `asserterror`. |
| 47 | `NavReport.RunRequestPageAsync` (ArgNull `parent`) | **4** | page=4 | **D** | NavReport R2R family (sibling of #4 / #8). |
| 48 | `NavApplicationObjectBaseHandle\`1.get_Target` ("Codeunit 132217 not present") | **3** | codeu=3 | **C** | Missing test-library dependency in the build. Either add the stub or fix the AL `Uses` declaration. |
| 49 | `NavTenant.get_Database` (ArgNull `NavDatabase`) | **3** | codeu=2 / recor=1 | **A** | Same root as #26 — `NavTenant.<Database>k__BackingField` not wired. |
| 50 | `ALDatabase.ALLastUsedRowVersion` (NRE) | **3** | recor=3 | **?** | ALDatabase-poisoned. |
| 51 | `ALDatabase.ALAlterKeyAsync` ("cannot alter primary key") | **3** | recor=3 | **C** | Real BC enforcement. AL test attempts forbidden alter. |
| 52 | `RecordImplementation.SetSecurityFiltering` (NRE) | **3** | recor=3 | **A** | Security-filter skeleton — wire-shaped. |
| 53 | `ALDatabase.ALImportData` (NRE) | **3** | recor=3 | **B** | Data file I/O. Out of scope. |
| 54 | `PermissionManagement.SessionHasSuperOrSecurityPermissionsForUser` (NRE) | **3** | recor=3 | **A** | Permission skeleton — wire-shaped. |
| 55 | `ALSystemString.ALPadStr` ("outside permitted range, value -10") | **3** | data=3 | **C** | Real BC validation; AL fixture passes negative length. Test bug. |
| 56 | `NavTextBuilder.<>c.<Execute>b__34_1` (`ArgumentOutOfRange`) | **3** | data=3 | **C** | Same shape as #55. |
| 57 | `TrappableOperationExecutor.HandleError` (NRE) | **3** | data=1 / page=2 | **A** | Wire-shaped. |
| 58 | `NavFilterPageBuilder.RunModalAsync` (NRE) | **3** | page=3 | **D** | RunModal family (sibling of #12 / #4). |
| 59 | `Report60480..ctor` (NRE) | **3** | page=3 | **D** | Report ctor family. |
| 60 | `Report230001..ctor` (NRE) | **3** | page=3 | **D** | Report ctor family. |
| 61 | `Report307200..ctor` (NRE) | **3** | page=3 | **D** | Report ctor family. |
| 62 | `Report50113..ctor` (NRE) | **3** | page=3 | **D** | Report ctor family. |
| 63 | `Report50258..ctor` (NRE) | **3** | page=3 | **D** | Report ctor family. |
| 64 | `Codeunit56701.DoSomethingWithConfirm_Scope.OnRun` (`CallbackNotAllowed`) | **3** | page=3 | **B** | Same as #46. |

Long tail (clusters with 1–2 tests) totals ≈ 155 tests. Not enumerated here — cheap to harvest after the four big buckets are drained.

---

## 4. Cat D (Cecil-rewrite-addressable) inventory

These clusters share the "R2R-trapped body, no downstream JIT'd frame" property documented in `R2R-DOWNSTREAM-MAP.md`. The **Cecil-rewrite mechanism currently stashed at `stash@{0}`** is the proposed unblock. **Ranked by yield** (collapse to actual rewrite targets — same body counted once):

| Rewrite target (single Cecil edit) | Tests cleared | Source cluster(s) |
|---|---:|---|
| `NavReport.{RunReport, SaveAs, RunRequestPage}Async` (one shared body, `ArgNull(parent)` at entry) | **62** | #4 (45) + #8 (13) + #47 (4) |
| `NavForm.GetAutoFormatStringAsync` | **115** | #2 |
| `NavForm.GetMasterPage` | **33** | #5 |
| `NavForm.RunModalAsync` + `NavFilterPageBuilder.RunModalAsync` | **10** | #12 (7) + #58 (3) |
| `Microsoft.Dynamics.Nav.BusinessApplication.Report*..ctor` (8 distinct codeunits, identical R2R `metadata.StaticMetadata` null shape) | **45** | #6 (19) + #22 (6) + #32 (5) + #59 (3) + #60 (3) + #61 (3) + #62 (3) + #63 (3) |
| `Microsoft.Dynamics.Nav.Runtime.NavRecord..ctor` (likely same NCLMeta null shape; verify) | **9** | #9 |
| `NavSystemCodeunitUIHelperTriggers..ctor` (same ArgNull(`parent`) shape) | **5** | #24 |
| **Cat D total** | **≈ 279** | |

Pre-condition before unstashing: confirm the Cecil-rewrite mechanism can reach `Ncl.dll` (`NavReport`, `NavForm` are there) and `BusinessApplication.dll` (Report ctors). The stash description ("Cecil-rewrite spike mechanism (uncommitted, verified working — pop after class[ification]")) implies it's already been smoke-tested; trust but verify.

---

## 5. Cat B (out-of-scope conversion) inventory

OOS contract messages locked by `33f8c5f7`. Conversion is mechanical:

```
// before
Report.SaveAsExcel(...);   // crashes with ArgNullException
Assert.IsTrue(true, ...);

// after
asserterror Report.SaveAsExcel(...);
Assert.ExpectedError('out-of-scope: NavReport.SaveAsAsync');
```

| Cluster | Tests | OOS API name to use in `Assert.ExpectedError` |
|---|---:|---|
| `NavRecord.CloneForVariant` (#7) — already OOS-throwing, just needs `asserterror` wrap | **13** | `NavRecord.CloneForVariant (default-variant tableId=0)` |
| `ALSystemOperatingSystem.GetUrlCore` (#16) — needs OOS-throw added to runner | 7 | `ALSystemOperatingSystem.GetUrl` (host URL) |
| `TrappableHttpOperationExecutor.HandleExceptions` (#21) | 6 | `Http.*` (already documented in `docs/scope.md`) |
| `TrappableXmlOperationExecutor` (#29) | 5 | `XmlPort.* (HTTP)` |
| `Codeunit59750.PickWithDefault` + `Codeunit56701.DoSomethingWithConfirm` (`CallbackNotAllowed`, #46 + #64) | 7 | `*.Confirm`/`*.Picker` callback OOS |
| `NavHttpClient.AddCertificate` (#38) | 4 | `HttpClient.AddCertificate` |
| `ALDatabase.ALChangeUserPassword` (#40) | 4 | `User.Password` |
| `ALDatabase.ALImportData` (#53) | 3 | `Database.Import` |
| `NavCodeunitHandle.ALAssign` (#23) (if confirmed BC-correct) | 5 | (audit first) |
| **Cat B total** | **≈ 54** | |

Recommendation: do the conversion in ONE doc-driven sweep session. The OOS API name should already exist in `docs/scope.md` for half of these; for `ALSystemOperatingSystem.GetUrlCore` and `NavHttpClient.AddCertificate` it needs to be added to the runner's OOS throw set first.

---

## 6. The 204-test `NavDialog.ALError` cluster — recommended sub-split

Currently the largest single classifier bucket but provably multi-root (top 25 distinct Assert messages):

| Assert tail | Count | Likely category |
|---|---:|---|
| `Index out of bounds.` | 14 | C (test bug — pass invalid index) |
| `There is no property with this name` | 8 | A (`NCLMeta*` registry — property lookup needs wiring) |
| `HTTP calls are not supported by al-runner` | 3 | B (already runner-loud, convert AL to `asserterror`) |
| `Object reference not set to an instance of an object` (re-thrown as Assert) | 3 | A (the NRE is downstream of an Assert-wrap; locate inner frame) |
| `No RequestPageHandler registered` | 3 | C (test fixture missing handler) |
| `<…> (Integer/BigInteger/Boolean/Text/Time) …` — type-name-formatted equality fails | ~ 35 | C (assertion-value mismatches — likely AL fixture bugs from default-stub returns; audit per-suite) |
| `<01/15/202…>` (date), `<Entry No.>`, `<Code>` — value mismatches | ~ 20 | C / A (stub return defaults bleeding through) |
| `Counter row should exist`, `Subscriber should have inserted counter row` (subscriber-counter family) | 2+ | D (`IsEventSubscribed` upstream state injection per `PATH-FORWARD.md` Inv 4 candidate) |
| `Equality assertions only support …` | 2 | C (AL test passes unsupported variant type to `Assert.AreEqual`) |
| `Assert.IsFalse failed. MediaId must return a non-empty GUID` | 2 | A (MediaSet wiring — overlaps with #31) |
| residual long tail | ~ 110 | mixed — needs per-suite triage |

Dedicated session for this single cluster is the highest-leverage thing after Cat D — at minimum 20–40 tests routinely resolvable per session via the existing classifier-driven workflow.

---

## 7. Top-5 actionable clusters (executive summary)

1. **Cecil-rewrite `NavForm.GetAutoFormatStringAsync` → return `""`** (Cat D, **115 tests**, one rewrite target).
2. **Cecil-rewrite `NavReport.{RunReport, SaveAs, RunRequestPage}Async`** to no-op (Cat D, **62 tests**, one shared body — see `R2R-DOWNSTREAM-MAP.md` §1 confirming all variants funnel through one method).
3. **Register stub `NCLMeta*` for the missing object IDs flagged in `ThrowMetaApplicationObjectNotFound`** (Cat A, **56 tests**, pattern proven by `c95debdc`).
4. **Cecil-rewrite `Report*..ctor`** family — eight distinct codeunits, identical R2R shape (Cat D, **45 tests**, plus a likely free **+9** from the same shape on `NavRecord..ctor`).
5. **Cecil-rewrite `NavForm.GetMasterPage` → return null safely** (Cat D, **33 tests**).

Sum of top-5: **≈ 320 tests**, almost all addressable by the **Cecil-rewrite mechanism in `stash@{0}`** — exactly what that stash was reserved for. Inv-3 (Report*..ctor) and Inv-4 (FlowField) from the previous cycle were misdirected by stale cluster sizes; the live numbers above retire that risk.

---

## 8. Method notes (reproducibility)

```
dotnet build spike/v2/Runner -c Release
for b in bucket-1/codeunit-runtime bucket-1/record-table bucket-1-heavy/codeunit-runtime \
         bucket-2/data-formats bucket-2/page-report spike-a-baseapp; do
  dotnet run --project spike/v2/Runner -c Release --no-build -- tests/$b \
    > /tmp/classify/$(echo $b|tr / -).log 2>&1
  cp v2-classification.json /tmp/classify/$(echo $b|tr / -).json
done
```

Aggregation by `classification` field of `all_failures[]` in each JSON; clusters with ≥ 3 tests listed in §3. Sample suite, error message, and stack-top frame from `Reporter.cs` `ClassifyTest` (innermost `Microsoft.Dynamics.Nav.*` frame; see `spike/v2/Runner/Reporter.cs:137`).

**Doc-only commit:** `git diff HEAD~1 HEAD --stat` after commit shows only this file. No source-code changes; no stash pop.
