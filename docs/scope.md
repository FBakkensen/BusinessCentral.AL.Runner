# AL Runner — Scope Manifest

Authoritative list of what the runner does, what it fakes faithfully, and what
it refuses to run. Error messages from `RunnerOutOfScopeException` reference
section anchors here, so the developer reading test output lands in the right
row of the right table.

This file is contract; see `.claude/rules/loud-failures.md` for the rule that
makes it binding.

The structure is four buckets, in order of decreasing fidelity:

1. **Real code, real path.** Runner provides the surroundings; the BC / ISV
   business logic executes unmodified.
2. **Faithful replacement.** The runner substitutes an in-process implementation
   that the BC code or AL test cannot distinguish from real BC for observable
   purposes.
3. **Out of scope — runner throws.** AL tests that reach these surfaces fail
   loudly with `RunnerOutOfScopeException`. Move the test to a real-service-tier
   test app.
4. **In scope, not yet implemented (TODO).** Placeholder throws today; should
   land in bucket 1 or 2 over time.

Audit status of each existing patch against this manifest is tracked in
`spike/v2/SCOPE-AUDIT.md`.

---

## §1. Real code, real path

The runner loads these unmodified and lets them execute on the real BC code.
The runner's job is to populate enough state around them that they don't NRE.

| Surface | Form | Notes |
|---|---|---|
| **MS-shipped BC DLLs** | `Microsoft.Dynamics.Nav.SystemApplication.dll`, `Microsoft.Dynamics.Nav.BaseApplication.dll`, language packs, etc. | Loaded from `.app` files; R2R-precompiled bodies execute. |
| **ISV-shipped extension DLLs** | Any `.app` the runner is told about via `--package-cache` | Same load path as MS DLLs. |
| **AL business logic compiled in the test run** | The user's `src/` AL that the runner compiles | Cached as `<key>.dll`, can be re-used like MS DLLs across runs. |
| **Posting routines** | `Sales-Post`, `Purch-Post`, `Gen. Jnl.-Post`, `Item Jnl.-Post`, etc. | All real Base App posting logic; runs against in-memory tables (§2). |
| **Validation triggers** | `OnInsert`, `OnModify`, `OnValidate(field)`, `OnDelete`, `OnRename` | Fire as real triggers; subscribers receive `Rec`. |
| **Event subscribers** | `[IntegrationEvent]` / `[BusinessEvent]` + subscribers | `RunEvent` is rewritten to `AlCompat.FireEvent`, real subscriber dispatch. |
| **.NET interop the apps use in-process** | `System.IO.MemoryStream`, `System.Text.Encoding.*`, `System.Text.RegularExpressions.Regex`, in-process `System.Security.Cryptography` primitives | These execute natively, no replacement needed. |
| **Number / string / date primitives** | `Format`, `Evaluate`, `CalcDate`, `Date2DMY`, etc. | All real BC implementations. |

---

## §2. Faithful replacement

The runner provides a substitute that the BC code cannot tell apart from the
real thing for any test that only observes documented BC behaviour.

| Surface | Real BC | Runner replacement | Faithfulness boundary |
|---|---|---|---|
| **Table storage** (record CRUD) | SQL Server | `TempTableDataProvider` in-memory store | Faithful for all functional reads/writes, keys, filters, ranges, modify-in-place. Different on: transaction commit/rollback (no-op), no row locking, no parallel-session isolation. |
| **Metadata system** (`NCLMetaTable`, `NCLMetaField`, `NCLMetaCodeunit`, …) | Loaded from compiled `.app` metadata streams | `NclMetadataCachePopulator` parses AL source, builds equivalent structures via reflection | Faithful for field types, lengths, FieldClass, FlowField CalcFormula, primary keys, tableextension field merging. Boundary: anything the populator hasn't been taught about throws or NREs into the populator's logged-error channel. |
| **Session / company / tenant / user** | Live BC session | Skeleton `NavSession`, `NavCompany`, `NavTenant` we populate with defaults | Faithful for any test that doesn't probe authentication state, license features, or telemetry identity. `UserId()` defaults to `""`, configurable via `--user-id`. `CompanyName()` configurable via `--company-name`. |
| **Permissions** | Permission sets evaluated against entitlements | All-granted `PermissionSet` returned by `NavSession.GetPermissionSet` | Faithful for any test that doesn't probe permission *denial* paths. Tests asserting "access denied" must be excluded or moved to real service tier. |
| **Time / random / GUID** | Real .NET implementations | Same — no replacement | Faithful. |
| **Field caption / table caption / lookup-page IDs** | From metadata + language pack | From parsed AL source (real values for AL-compiled tables; falls back to `"FieldNN"` for base-app tables not compiled in this run) | Faithful for in-scope tables; documented stub for non-compiled base-app tables. |
| **Event publisher → subscriber dispatch** | Service-tier event dispatcher | `AlCompat.FireEvent` scans loaded assemblies for `[NavEventSubscriber]` and calls them | Faithful for documented event semantics including `var` params and `IncludeSender`. |
| **`Page.RunModal` / report `[RequestPageHandler]`** | Real UI dialog | Looks up registered `[ModalPageHandler]` / `[RequestPageHandler]` and calls it | Faithful for the handler dispatch contract that test code relies on; no actual UI is rendered. |

---

## §3. Out of scope — runner throws

When AL test code reaches any of these, the runner throws
`RunnerOutOfScopeException(api, reason, anchor)` and the test fails with a
message naming the API and pointing here.

The developer's options: change the test to not depend on the unsupported
surface, or move it to a separate test app that runs against a real BC service
tier with SQL Server.

### §3.1. Email <a id="email"></a>

| API | Reason |
|---|---|
| `Email.Send`, `Email.OpenInEditor`, `Email.Enqueue` | Sending email requires SMTP / Graph API connectivity. Out of process. |
| `SMTP Mail`, `Mail Management.SendMail` | Same. |

### §3.2. External HTTP / Web APIs <a id="external-http"></a>

| API | Reason |
|---|---|
| `HttpClient.Send`, `.Get`, `.Post`, `.Put`, `.Delete`, `.Patch` | Real HTTP requires a network and an external server. Tests that need an HTTP boundary must inject an AL interface and provide a fake in the test project. |
| OAuth / Azure AD token acquisition | Same — external network. |
| Outbound REST/SOAP consumers | Same. |

### §3.3. Web service publishing <a id="web-services"></a>

| API | Reason |
|---|---|
| OData / SOAP endpoints exposed by pages or codeunits | Requires a web server hosting the endpoints. Out of process. |
| `Web Service Management`, `tenantwebservice` table-driven publishing | Same. |

### §3.4. File / blob storage <a id="file-storage"></a>

| API | Reason |
|---|---|
| `File.Download`, `File.Upload` (browser round-trip) | Browser interaction; no client. |
| Azure Blob Storage, Azure Files connectors | External storage. |
| `File Management.BLOBImportFromServerFile` etc. against real filesystems outside the test directory | External filesystem dependency. |

### §3.5. Printing <a id="printing"></a>

| API | Reason |
|---|---|
| `Report.Run(Print, ...)` / `SaveAsPdf` to a real printer or PDF | Requires renderer + driver. Report **callbacks** (`[ReportHandler]`, `[RequestPageHandler]`) fire — see §2. |

### §3.6. Background jobs / scheduling <a id="jobs"></a>

| API | Reason |
|---|---|
| Job Queue Entry execution against a scheduler | No scheduler. `TaskScheduler.CreateTask` runs synchronously inline (§2-ish — runs the codeunit but doesn't respect NotBefore). |
| `IsolatedStorage` scoped to *real* session/user/company beyond the runner's flat in-memory bag | Possible TODO if needed; currently a single in-memory bag. |

### §3.7. Cryptography requiring external KMS / certificates <a id="crypto-external"></a>

| API | Reason |
|---|---|
| Key Vault integration | External KMS. |
| Certificate validation against a real cert store / CA | External infrastructure. |
| In-process primitives (hashing, AES, etc.) | **In scope** — those are §1, run natively against .NET. |

### §3.8. Real licensing / entitlements <a id="licensing"></a>

| API | Reason |
|---|---|
| `Session.IsLicensed`, license-file validation | No license system. Replacement returns "all granted" (§2), but tests probing denial paths fall back to out-of-scope. |

### §3.9. Parallel session contract <a id="parallel-sessions"></a>

| API | Reason |
|---|---|
| `StartSession`, `IsSessionActive`, session timeout / cancellation across processes | Runner runs everything in one process, inline. Logic tests work; contract tests don't (see `docs/limitations.md#no-parallel-session-execution`). |

### §3.10. Transaction semantics <a id="transactions"></a>

| API | Reason |
|---|---|
| `Commit`, `Rollback` as real boundaries | No transactions. `Commit` is a no-op. Tests asserting on commit boundaries must move to real service tier. |

### §3.11. Page rendering / client interaction <a id="ui"></a>

| API | Reason |
|---|---|
| `Page.Run` (non-modal), `controladdin`, `usercontrol`, profiles | Requires BC client. UI dialog **callbacks** (`[MessageHandler]`, `[ConfirmHandler]`, …) are in scope under §2; the UI itself isn't. |

### §3.12. Debugger <a id="debugger"></a>

| API | Reason |
|---|---|
| `Debugger.Attach`, `Break`, `StepInto`, etc. | No debug loop. See `docs/limitations.md#no-debugger-infrastructure`. |

### §3.13. NavQuery — multi-dataitem queries <a id="navquery"></a>

| API | Reason |
|---|---|
| Multi-dataitem queries (JOINs), aggregations (`Sum`, `Avg`, `Min`, `Max`), `SaveAsCsv`/`SaveAsXml`/`SaveAsJson`/`SaveAsExcel` | NavQuery compiles AL into SQL projections. A faithful in-memory equivalent is a multi-day workstream (see `spike/v2/QUERY-INVESTIGATION.md`). Single-dataitem queries are in scope today (§2). |

### §3.14. .NET interop (DotNet AL type) <a id="dotnet-interop"></a>

| API | Reason |
|---|---|
| `assembly_declaration`, `dotnet_declaration`, `DotNet` variables, `GetDotNetType` | Requires BC service tier's type-resolution. In-process .NET interop the apps themselves use is **in scope** (§1) — only the AL `DotNet` surface is out. |

---

## §4. In scope, not yet implemented (TODO)

These are surfaces we intend to support but haven't built yet. They throw
`RunnerOutOfScopeException` with reason `not-yet-implemented` so a developer
hitting them files a runner-gap issue rather than silently passing.

| Surface | Plan | Tracking |
|---|---|---|
| `NavReport.RunReportAsync` faithful replacement | EventPipe + handler dispatch | HANDOFF §6 Tier 1C |
| `NavReport.SaveAsAsync` faithful replacement | Same | HANDOFF §6 Tier 1C |
| `NavForm.GetAutoFormatStringAsync` | Investigate Option-C first | HANDOFF §6 Tier 1C |
| `RecordImplementation.CalcFieldsAsync` residual FlowField shapes | Extend populator | partially drained |
| `ALDatabase.AL*` cluster | Each method needs faithful semantics or explicit out-of-scope | audit pending |
| `NavApplicationObjectBaseHandle\`1.get_Target` tableId=0 path | Synthetic empty record for default-variant case | HANDOFF §6 Tier 1B |
| `NavRecord..ctor` for `RecordLink` / `Company` built-in tables | BcAppFallback metadata for system tables | HANDOFF §6 Tier 1B |
| AL Runner Config codeunit `131100` | v2 equivalent of v1's `MockSession` routing | HANDOFF §6 Tier 2 |
| `FilterGroup(n)` scoped filter groups | Track group state on Record | known gap |

---

## How to read this from a failing test

When you see a test fail with `RunnerOutOfScopeException`:

```
NavNCLDialogException: RunnerOutOfScopeException: Email.Send is out of scope.
Reason: external-smtp. See docs/scope.md#email.
```

1. Open `docs/scope.md` at the anchor.
2. The row tells you which bucket the API is in (3.x permanent, or 4 TODO).
3. If §3 — move the test to a real-service-tier test app, or refactor to inject
   an AL interface and pass a fake from the test project.
4. If §4 — file a runner-gap issue and add the test to `tests/excluded/`.

## Sister docs

- `.claude/rules/loud-failures.md` — the rule.
- `docs/limitations.md` — user-facing version with patterns + workarounds.
- `spike/v2/SCOPE-AUDIT.md` — audit table of each existing patch vs this manifest.
- `spike/v2/HANDOFF.md` — what's prioritized for the §4 list.
