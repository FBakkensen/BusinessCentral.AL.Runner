# Path forward — closing the remaining corpus gap

**Status as of HEAD `4db4455e` (2026-05-19 evening):** 3195/4080 = **78.31%** pass. 885 fails left. Cold-run wall-clock: **308s** (was 617s — light-bucket migration cut 61%).

## Today's architectural breakthroughs

1. **Cecil-rewrite of `Microsoft.Dynamics.Nav.Ncl.dll` is viable** (uncommitted in `stash@{0}`). Rewrite trapped method bodies' IL → strip R2R native data → write modified bytes back to bin path before CLR class-init → CLR JITs from our IL. Probe-verified end-to-end on `NCLMetaApplicationObject.IsEventSubscribed`. Zero regressions. Per `precompiled-dll-respect.md`, Ncl.dll is explicitly modifiable (runtime engine, NOT AL-business-logic).

2. **Light-bucket migration landed** (3 commits: `4a4a6d33`, `05748492`, `9b82e385`). `tests/bucket-1/` and `tests/bucket-2/` declare `dependencies: []` + 5 v1 stubs in `_shared/`. Only 1 suite needed heavy splitting (`bucket-1-heavy/codeunit-runtime/316-no-series-getnextno-overloads`).

3. **R2R Downstream Map + corpus classification refresh** (`cbd1cbf7`, `4db4455e`) replaced stale 12-day-old failure sizing. **279 of 885 fails are Cecil-addressable** — the highest-yield single intervention.

## Live corpus snapshot

| Bucket | P / F / T | Wall |
|---|---:|---:|
| bucket-1/codeunit-runtime | 764 / 224 / 988 | 41s |
| bucket-1/record-table | 749 / 156 / 905 | 84s |
| bucket-1-heavy/codeunit-runtime | 0 / 3 / 3 | 56s |
| bucket-2/data-formats | 1402 / 157 / 1559 | 37s |
| bucket-2/page-report | 272 / 345 / 617 | 30s |
| spike-a-baseapp | 8 / 0 / 8 | 60s |
| **Total** | **3195 / 885 / 4080 (78.31%)** | **308s** |

## Top 5 ranked failure clusters

(Full detail in `spike/v2/CORPUS-CLASSIFICATION-2026-05-19-FINAL.md`)

| # | Cluster | Tests | Cat | Action |
|---:|---|---:|---|---|
| 1 | `NavForm.GetAutoFormatStringAsync` | 115 | D | Cecil-rewrite body → `ValueTask.FromResult("")` |
| 2 | `NavReport.{RunReport,SaveAs,RunRequestPage}Async` | 62 | D | Cecil-rewrite → throw `RunnerOutOfScopeException` (shared body, 1 rewrite drains all 3) |
| 3 | `NCLMetadata.ThrowMetaApplicationObjectNotFound` | 56 | A | Register stub NCLMeta entries (Cat A, not Cecil) |
| 4 | `Report*..ctor` family (8 codeunits, shared shape) | 45 | D | Cecil-rewrite (see PAGE-REPORT-CLUSTERS.md §1) |
| 5 | `NavForm.GetMasterPage` | 33 | D | Cecil-rewrite → return null |

**Cat D total: ~279 tests** (32% of remaining fails). Cat A NCLMetadata: 56. Cat B/C tail: ~550.

## Session deltas (evening)

- `c95debdc` — Inv 1 NavSession.Authenticator → NavUser (+9 P, AL UserId/UserSecurityId)
- `d05bab7f` — Inv 2 NavRecordId.CollationAwareStringComparer (+1 P)
- `6ab7b420` — PATH-FORWARD morning session summary
- `33642638` — Inv 1b ALDatabase.ALTenantID JmpHook (+2-3 P)
- `4742bb2f` — EventPipe Phase A (DryRun listener, infra)
- `cbd1cbf7` — R2R Downstream Map (research)
- `8a7a0b6a` — Light-bucket A/B spike (proves concept)
- `4a4a6d33` — Migrate bucket-1 to light + heavy split (infra)
- `05748492` — Migrate bucket-2 to light (infra)
- `9b82e385` — Light-bucket migration doc
- `4db4455e` — Corpus classification refresh (research)

`stash@{0}`: Cecil-rewrite mechanism scaffold, ready for next session.

Session deltas (2026-05-19 follow-up, on top of `8ca3f84f`):
- `c95debdc` — **Inv 1**: NavSession.Authenticator → NavUser → unlocks AL UserId/UserSecurityId. **+9 P** in record-table. Skeleton field-poke pattern.
- `d05bab7f` — **Inv 2**: NavRecordId.CollationAwareStringComparer JmpHook (TempTable Modify NRE). +1 P (cluster much smaller than hypothesized).

Inv 3 (Report*.ctor) BLOCKED with two-part fix shape documented in handoff. Inv 4 (FlowField parser) premise INVALIDATED — only 2 tests match, both are AL fixture bugs.

Earlier session deltas (overnight, HEAD `678f77c5`/`8ca3f84f`):
- `33f8c5f7` — locked `RunnerOutOfScopeException` message contract. Category B Step 1.
- `292d9bab` — XmlPort batch (+12 P).
- `c8c09f3b` — working-list docs.
- `4221bc51` — NavFile.Upload/Download OOS (+2 P). Includes JmpHook FF 25 indirection fix.
- `8a72e2c2` — NavForm.RunAsync OOS (+9 P). Includes BcAssembler polyfill redirect pattern for R2R-trapped sync wrappers.
- `d65b07cb` — page-report cluster analysis.
- `43ef05d3` — ALDatabase.ALSid hook (+6 P).
- `678f77c5` — ALDatabase.ALSessionID hook (+3 P).
- `8ca3f84f` — PATH-FORWARD overnight session-end summary.

Findings (consolidated):
1. **`DOTNET_ReadyToRun=0` doesn't bypass R2R-inline trap** — JIT also inlines tiny bool returners.
2. **`NavReport.SaveAs*` is R2R-trapped** — ~30 hooks installed, none fired. Reverted. ~54 tests stuck behind this.
3. **AL identity builtins (UserId/UserSecurityId/TenantId/ServiceInstanceId) DO compile to `ALDatabase` statics — but those statics are R2R-inlined.** Their inlined bodies read `NavCurrentThread.Session.User.{Name,Id}` and `Session.Tenant.Id`. Inv 1 unlocked this via skeleton field-poke on `NavSession.<Authenticator>k__BackingField → NavUser`. **Pattern reusable for any R2R-inlined ALDatabase getter that reads a Session field.** TenantId + ServiceInstanceId need more state (NavTenant Tree+database, NavEnvironment lazy-init) — followup.
4. **`get_ALCompanyName` flips +1 in target test but bucket-wide regresses by -1** — still uncommitted; investigate later.
5. **Report*.ctor "doc-predicted single fix" is incomplete** — fixing `LookupNclMetaForReport` makes metaArg non-null but the NRE shifts to `NavReport..ctor`'s null `metadata.StaticMetadata`. Needs two-part fix.
6. **Cross-`Patches/*Patches.cs` calls from a JmpHook'd method segfault at startup.** `RuntimeHelpers.PrepareMethod` during `JmpHook.Apply` cascades into the cross-class, colliding with a precode mid-patch. **Workaround:** resolve callee via `Type.GetType().GetMethod().Invoke()` reflection instead of a static-symbol reference.

The remaining fails are not all the same shape. They split into four categories
with very different fixing strategies and very different yield-per-effort.
This document defines the categories, the strategies, and the order to attack.

---

## Mission recap (unchanged)

The runner exists so AL test code runs against **unmodified** MS-AL and
ISV-AL compiled DLLs. We do not implement AL business logic. We only:

1. Wire what the BC service tier normally wires (skeleton state on
   `NavSession`, `NavTenant`, `NavCompany`, `DataAccessSource`, transaction
   manager, metadata cache, isolated storage, encryption provider, etc.) so
   precompiled BC bodies read sensible BC-faithful defaults and behave
   correctly.
2. Provide an in-memory table provider as the "fake DB" the service tier
   would normally provide via SQL.
3. Throw `RunnerOutOfScopeException` (loudly, with named API + reason)
   when AL code reaches a surface we cannot faithfully provide
   (HTTP, SMTP, external file I/O, etc.).

Rules: `.claude/rules/precompiled-dll-respect.md` (no MS-AL body rewrites),
`.claude/rules/loud-failures.md` (no silent fakes), `.claude/rules/tdd.md`
(RED → GREEN), `.claude/rules/no-changelog-edits.md`, `feedback_aldatabase_hard`,
`feedback_r2r_inlining_traps`.

---

## Category breakdown of the remaining ~929 fails

| Category | Est. size | Strategy | Effort shape | Yield/effort |
|---|---:|---|---|---|
| A. Wire-shaped | ~500-600 | 3-cycle pattern: classifier → 1 impl agent ≤60min → verify | Recurring sessions, 1-3 commits each | Medium |
| B. Out-of-scope (silent OOS fails) | ~80-120 | Document scope → lock exception-message contract → convert tests to `asserterror`+`Assert.ExpectedError` | One focused session, mechanical | **High — biggest $/min** |
| C. AL test bugs | ~30-50 | Fix tests in place (e.g. `Assert.ExpectedError('')` wildcard misuse) | Mechanical sweep | High but small |
| D. R2R-inlining trap | ~140+ | EventPipe post-JIT body patching infrastructure | Multi-day dedicated investigation | High but slow |

Estimates are eyeballs from today's classifier passes; refine with a fresh
classifier per category before committing to a session-length spend.

---

## Category A — Wire-shaped tests (~500-600)

This is the bread-and-butter pattern that produced today's three commits
(`bcb35b57`, `37288046`, `e98ca03c`) and the five before them. **Continue it.**

### Recipe (proven across 8+ commits)

1. **Read-only classifier subagent** on one bucket. Top 6-8 clusters by
   error-signature count. Strict AVOID list:
   - `NavDialog.ALError` catch-all (multi-root noise)
   - ALDatabase JmpHook cluster (poisoned per `feedback_aldatabase_hard`)
   - TestPage methods via `NavForm.GetAutoFormatStringAsync` /
     `NavForm.GetMasterPage` (R2R-inline trap — category D)
   - Query metadata §3.1 alone (needs §3.1+§3.2+§3.3 bundle; see
     `QUERY-INVESTIGATION.md`)
   - Report.RunModal stubs (category B, not category A)
2. **Pick ONE cluster, 15-40 tests, single root cause, 1-3 codeunits.**
3. **One impl agent, ≤60-min budget, rules cited inline, "STOP if you
   reach for ALDatabase JmpHook" guardrail.**
4. **Brain verifies signature + corpus delta** before next cycle.

### Promising starting points next session

- **`bucket-2/page-report`** — biggest untouched headroom (255P/362F = 41.3%).
  Re-classify with strict avoidance (no TestPage, no Report.RunModal, no
  Query §3.1). Look for Page lifecycle / FieldGroup / Views / Action
  metadata clusters that flow through populated `NCLMeta*` registries.
- **Codeunit-runtime re-classify** at 760P/231F — state shifted +16 since
  the last classifier; new cluster mix possible.
- **Downstream micro-clusters from today's wins:**
  - `Assert.AreEqual` enum-equality (~5+ tests in record-table, downstream
    of DAS-STM fix). Extend `NavValue` equality to enum-typed values.
  - `CloneForVariant tableId=0` (7 in data-formats; in-scope-not-implemented).

### Realistic ceiling under wiring alone

If categories B/C/D are not addressed, category A alone probably tops out
around **85-90% corpus pass rate** (cumulative). Beyond that requires the
other categories.

---

## Category B — Out-of-scope, currently silent-fail (~80-120) — HIGHEST PRIORITY NEXT

These tests are red today because either (i) the runner silently default-returns
on an OOS surface (in violation of `loud-failures.md`) and the AL test asserts
a real value, or (ii) the runner throws `RunnerOutOfScopeException` correctly
but the AL test was written assuming "this would pass in BC service tier".

In **both** cases the fix is the same shape: **the AL test should expect the
OOS exception**, not a real value. AL's `asserterror` + `Assert.ExpectedError`
catches our managed `RunnerOutOfScopeException` cleanly, so the test stays in
the main corpus, the pass rate becomes truthful, and the runner's loud-failure
contract is *proven by the test* rather than just asserted by the rule.

### Why this is the highest $/min

- Mechanical pattern, one focused session.
- Probably ~100 pass-flips with no JmpHook / no skeleton state / no risk.
- Locks down `docs/scope.md` and the exception-message format as the
  *contract surface* between runner and tests — pays compounding interest
  on every future test added.
- Surfaces all the silent-fake stubs that pre-date `loud-failures.md`
  (per `SCOPE-AUDIT.md`) so they get converted to honest throws or
  honest implementations.

### Plan

**Step 1 — Lock the exception-message contract.** One-time decision:
```
out-of-scope: <BC.API.Name> — <reason-key> — see docs/scope.md#<anchor>
```
Stable enough that AL tests can match on `'out-of-scope: NavEmail.Send'`
or `'out-of-scope:'` (prefix) without churn. Audit
`RunnerOutOfScopeException` ctor sites; normalize.

**Step 2 — Complete `docs/scope.md`.** Every OOS surface listed with its
anchor + reason-key. This becomes the source of truth tests grep against.

**Step 3 — Classifier pass to enumerate OOS-shaped failures across all
buckets.** Categorize each by API name. Build a working list.

**Step 4 — Mechanical test conversion.** For each OOS-shaped failing test:
```al
// before
NavEmail.Send(...);
Assert.AreEqual(true, sent, 'email should send');

// after
asserterror NavEmail.Send(...);
Assert.ExpectedError('out-of-scope: NavEmail.Send');
```
Batches of 10-20 tests per commit, one per OOS surface. Each commit
also audits the corresponding `Patches/*.cs` stub to ensure it actually
throws `RunnerOutOfScopeException` and isn't silent-faking.

**Step 5 — Audit pass against `SCOPE-AUDIT.md`.** Any surface marked
silent-fake there gets converted to a loud throw as part of the same
commit that converts the tests.

### What this does NOT do

- Does not touch precompiled DLL bodies.
- Does not move tests to `tests/excluded/`. The denominator stays at 4071.
- Does not silently default. Every conversion makes the runner louder, not
  quieter.

---

## Category C — AL test bugs (~30-50)

Tests that are simply wrong against BC's documented semantics. Examples
identified by classifiers today:

- `Assert.ExpectedError('')` — the empty string is *not* a wildcard in real
  BC `Codeunit130000.ExpectedError`. ~22 of these in data-formats alone
  (CU115001, 316101, 113001, 100001). Fix: replace `''` with the real
  expected substring, or with a meaningful prefix.
- Wrong assertion of enum equality through `Assert.AreEqual` (downstream
  of today's DAS-STM fix — overlaps with category A).
- Off-by-one expectations on `TextBuilder.Insert/Remove` index semantics
  (6 in data-formats).
- `ALPadStr` parameter-range assertions (3 in data-formats).

### Plan

One mechanical sweep, one classifier pass to enumerate, then PRs against
the test source. **No runner changes.** These are pure AL-test bug fixes.

### Order

Run after category B because some category-B test rewrites might surface
additional category-C bugs (e.g. an `asserterror` conversion that reveals
the test was also checking the wrong post-condition).

---

## Category D — R2R-inlining trap (~140+)

The architectural ceiling. See `feedback_r2r_inlining_traps` and the
"ALDatabase / R2R-inlining trap — plain explanation" section in this
session's transcript.

### What's stuck behind it

- **41-test `ALDatabase.AL*` cluster** — `get_ALCompanyName`, `ALSid`,
  `ALSerialNumber`, `ALTenantID`, `ALUserSecurityID`,
  `ALEvaluateAndCheck...`, ~11 getters total. Two prior Sonnet workers
  failed; do not retry as a one-shot task.
- **`NavForm.GetAutoFormatStringAsync` cluster** — ~90 TestPage tests in
  page-report bucket.
- **`NavForm.GetMasterPage`** — smaller TestPage cluster.
- **`Consistent_*` tests in codeunit 97702/50811** — re-confirmed today on
  the DAS-STM follow-up: hook fires once then is bypassed.

### Why JmpHook alone can't fix these

MS's R2R compiler inlined the tiny accessor bodies into their callers
inside the BC DLLs. The standalone native entry-point we patch with
JmpHook is never executed at the call sites — the body is baked in.
Instrumented `Console.Error.WriteLine` in the replacement never fires
despite hook install success. Today's `bcb35b57` worked around it for
ONE getter (`ALCurrentTransactionType`) by populating a deeper skeleton
field (`DataAccessSource.sessionTransactionManager`) that the inlined
body still reads. That pattern doesn't generalize to accessors that
compute live values from multiple fields.

### Workarounds, in order of preference

1. **Deeper skeleton field (per-accessor opportunistic).** When the
   inlined body reads exactly one field, populate it. Today's DAS-STM
   fix is the template. Cheap when it works; doesn't generalize.
2. **Hook the outer caller.** If the BC method that ends up calling the
   inlined getter isn't itself inlined, hook there. Painful — many
   outer callers per getter.
3. **EventPipe post-JIT body patching.** Use .NET EventPipe to detect
   when each BC method has been JIT-compiled (note: JIT-compiled, not
   R2R-loaded) and overwrite its native body in memory at runtime.
   Bypasses R2R entirely because by the time we patch, the JIT has
   emitted a fresh non-inlined copy.

### Plan for category D

Workaround #3 is the right architectural answer. **Multi-day dedicated
investigation, not a one-shot task.** Approach:

1. Spike: prove EventPipe can detect a single MS DLL method's JIT
   completion and rewrite its body in memory without crashing the
   runtime.
2. Spike: prove the rewrite survives subsequent calls (i.e. the JIT
   doesn't re-emit the original IL on top of our patch).
3. Build: a generalized `EventPipePatch.Install(method, replacement)`
   API that mirrors `JmpHook.Install` but operates post-JIT.
4. Migrate: convert the ALDatabase 11 accessors first as a proving set.
   If yield ≈ 41 tests, the infrastructure has paid for itself.
5. Roll out: TestPage / `NavForm.*` cluster, `Consistent_*` cluster.

Open questions to answer in the spike:
- Does R2R actually disable JIT for these methods, or does .NET JIT
  re-emit on first non-R2R call? (If the latter, the patch window
  exists and EventPipe can catch it.)
- ~~Can we force the runtime to skip R2R for specific assemblies via
  `DOTNET_ReadyToRun=0` per-DLL?~~ **ANSWERED 2026-05-18 spike: NO.**
  `DOTNET_ReadyToRun=0`, `DOTNET_TieredCompilation=0`, `DOTNET_TC_QuickJit=0`
  individually and combined do NOT bypass the inline trap — the regular
  JIT also inlines tiny bool returners like `IsEventSubscribed`. Env-var
  path is dead. See `feedback_r2r_envvar_doesnt_help` memory.
- **Side finding from the same spike:** `ALDatabase.ALSid` is NOT
  R2R-trapped — JmpHook fires 7+ times on `83-database-sid`. Prior
  `feedback_aldatabase_hard` fabricated-success crashes were a different
  bug (calling-convention / return-shape / native-ref issue in the
  replacement body). **The ALDatabase 41-test cluster may be Category A**,
  not D. Worth a careful retry with safe constant-returning replacements,
  one getter at a time, instrumented.

### Do NOT retry category-D fixes as Sonnet one-shots

Two prior workers ran `feedback_aldatabase_hard` — both produced
fabricated-success commits that segfault on verify. Pattern: hook
installs, agent reports green, no verification of the actual call path.
Re-attempting without instrumented investigation will reproduce the same
failure mode.

---

## Suggested next-session ordering

1. **Category B — OOS + `asserterror` conversion** (1 session, ~100 pass-flips,
   highest $/min, makes corpus truthful).
2. **Investigate `DOTNET_ReadyToRun=0` per-DLL** (1 hour spike — if this
   works, category D collapses; if not, document and move on).
3. **Category A — Continue 3-cycle pattern on page-report** (untouched
   bucket, biggest A-shaped headroom).
4. **Category C — AL test bug sweep** (after category B may have surfaced
   more).
5. **Category D — EventPipe infrastructure** (only if step 2 didn't work
   and category A's ceiling is reached).

---

## Decision points for next session brain

- "Should I keep wiring category-A clusters or pivot to B?" → If we're
  ≥80% pass, pivot to B. Below 80% the category-A muscle memory is fine.
- "Is this cluster R2R-inline-trapped?" → Instrument the replacement
  with `Console.Error.WriteLine` *before* committing. If it doesn't fire
  on a verified failing test, it's category D — STOP and document.
- "Should I touch ALDatabase?" → No. Until category D infrastructure
  lands.
- "Should I retry Query §3.1?" → Only as a bundled §3.1+§3.2+§3.3 brief
  per `QUERY-INVESTIGATION.md`. Never §3.1 alone.

---

## Living document

This file is the strategic plan. Each session that closes a category
chunk should update the corresponding section with what was learned
and what's left. The category breakdown numbers are estimates from
2026-05-18 classifiers; refresh them whenever a session re-classifies.
