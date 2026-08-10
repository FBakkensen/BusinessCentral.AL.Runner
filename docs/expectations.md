# Test expectations manifest

The runner consumes tests from the `tests/al-language` submodule
(`StefanMaron/BusinessCentral.AL.Language.Tests`). That corpus is the canonical
spec of AL language behaviour against a real BC service tier. By design, some
tests in the corpus exercise surfaces the runner cannot — and will never —
support in-process (report rendering, SMTP, HTTP egress, real task scheduler,
etc.). The runner does not modify the corpus to make those tests pass; instead
it declares its expectations about them in this directory.

## Activation

The manifest loads once at startup, before any BC initialisation. Without the
flag, the runner probes `./tests/expectations` relative to the working
directory and activates classification only when that directory exists — a run
outside this repo behaves exactly as if the mechanism did not exist. Pass
`--expectations <dir>` to point at another manifest directory explicitly (the
directory must exist). A malformed manifest aborts the invocation with exit
code 2 before a single test runs.

## Layout

```
tests/expectations/
  oos-<area>.json         ← out-of-scope-by-design (most common)
  known-gaps-<area>.json  ← in-scope but not yet implemented (transient, links to GH issues)
  disabled-<area>.json    ← won't compile or won't run; pure skip
```

One file per area. Sharding matches Microsoft's
`ALAppExtensions/Build/DisabledTests/` convention so anyone familiar with the
BC ecosystem recognises the shape — we extend the schema with extra fields
rather than replace it.

## Entry schema

```jsonc
[
  {
    // Required (Microsoft-compatible core)
    "codeunitId":   60042,
    "CodeunitName": "Report Layout Render",
    "Method":       "Report_SaveAs_RendersPdf",   // "*" matches every test in the codeunit

    // Required runner extension
    "Mode": "expect-oos",                          // expect-oos | expect-fail-known-gap | skip

    // Conditional
    "Reason": "report-rendering",                  // required when Mode = expect-oos
                                                   //   must match a section anchor in docs/scope.md
    "Issue":  "https://github.com/.../issues/123", // required when Mode = expect-fail-known-gap

    // Optional
    "Doc":  "docs/scope.md#reports",
    "Note": "BC service tier renders PDF via report engine; runner is in-process only."
  }
]
```

Field names follow Microsoft's casing (`codeunitId` lowercase-c, `CodeunitName`
and `Method` PascalCase) so external BC tooling that reads MS's `DisabledTests/`
shape also reads ours.

### `Method: "*"`

Wildcard matches every `[Test]` procedure in the named codeunit. Use this when
an entire test codeunit is unsupported (e.g. all of `Test Report Saving` is
OOS because report rendering is OOS). Method-level granularity is preferred
when only some tests in a codeunit are affected.

### Mode semantics

| Mode | Test must… | Runner counts as | When to use |
|---|---|---|---|
| `expect-oos` | throw `RunnerOutOfScopeException` with matching `Reason` | `pass-oos` | Surface is OOS by design (see `docs/scope.md`) — runner will never support it in-process |
| `expect-fail-known-gap` | fail (any exception or assertion mismatch) | `pass-known-gap` | Surface is in scope but not yet implemented; `Issue` tracks the work |
| `skip` | n/a — runner does not invoke the test | `skipped` | Test cannot compile against the current AL output, or otherwise must not run |

`Reason` matches on the anchor: throw sites may append free-text detail after
an ` — ` (em-dash) separator, while the entry holds only the leading
`docs/scope.md` anchor (e.g. a throw site's
`query-join-rightouterjoin-not-implemented — only InnerJoin and …` matches an
entry declaring `query-join-rightouterjoin-not-implemented`).

### Result classification table

When the runner finishes a test, it consults the manifest:

| Test outcome | Manifest entry | Classification | Action |
|---|---|---|---|
| Threw `RunnerOutOfScopeException`, `Reason` matches | `expect-oos` | **pass-oos** | (none) |
| Threw OOS, reason mismatches | `expect-oos` | **fail** | Either update `Reason` or fix the throw site to emit the correct reason |
| Threw a different exception type | `expect-oos` | **fail** | Either implement the surface, or replace the silent failure with a proper `RunnerOutOfScopeException` |
| Passed cleanly | `expect-oos` | **fail** | Runner now implements the surface — remove the manifest entry |
| Threw `RunnerOutOfScopeException` | absent | **fail** | New OOS surface — add a manifest entry citing the scope.md reason |
| Any non-pass result | `expect-fail-known-gap` | **pass-known-gap** | (none) |
| Passed cleanly | `expect-fail-known-gap` | **fail** | Gap is fixed — remove the entry and close the linked issue |
| n/a | `skip` | **skipped** | (none — does not contribute to pass/fail counts) |
| Any normal outcome | absent | **pass/fail** as usual | (none) |

Manifest drift in any direction is loud: silent additions, silent fixes, and
silent regressions all surface as test failures with explicit diagnostics
telling the reader what to do.

## Reporter output

```
1463 pass + 47 pass-oos + 12 pass-known-gap + 28 skipped + 0 fail  (1550 total)
```

The three pass categories are surfaced separately so a clean run does not
hide manifested deviations from the corpus.

## Authoring rules

1. **One reason per `expect-oos` entry.** The reason must reference a section
   anchor in `docs/scope.md`. If a new surface is OOS for a reason not yet
   documented, add the section to `docs/scope.md` in the same PR.
2. **`expect-fail-known-gap` requires an `Issue` link.** No untracked known
   failures. The issue must be open at PR time.
3. **`skip` is a last resort.** Prefer fixing the compile gap or quarantining
   in the corpus repo via preprocessor symbols. Use `skip` only when neither
   is feasible.
4. **No `Note` lies.** The note is human-readable context that survives
   reviewers reading the diff months later. Either keep it accurate or omit it.
5. **Schema validation.** The loader (`ExpectationManifest.cs`) rejects
   unknown `Mode` values and missing required fields. Runner startup fails
   loudly if any expectation file is malformed.
