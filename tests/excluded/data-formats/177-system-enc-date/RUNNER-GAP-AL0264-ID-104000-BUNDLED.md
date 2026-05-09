# RUNNER GAP: AL0264 — Codeunit ID 104000 conflicts with Base Application in bundled mode

## Error

```
AL0264: An application object of type 'Codeunit' with ID '104000' is already declared by the extension 'Base Application by Microsoft (27.5.46862.48827)'
```

## Trigger

The suite defines `codeunit 104000 "SED Src"` (src/SystemEncDateSrc.al). ID 104000 is used by the
Base Application (BC 27.5). In per-suite mode the Base Application package is not loaded, so the
ID is available. In bundled mode the Base Application package IS part of the shared symbol set,
causing an ID collision.

## Root cause

**BUNDLED dep-loading asymmetry.** Per-suite compilation loads a minimal symbol set per suite;
bundled compilation loads the full Base Application, which reserves ID 104000. The test suite
codeunit uses an ID that was free in isolated compilation but collides in the full symbol set.

## Fix path (not implemented)

Reassign the codeunit to an ID outside the Base Application range (e.g., in the 50000+ range).
This is an AL source change — not permitted under the no-rewrite rule.

Investigate whether bundled mode can scope ID-collision checking to the test extension only,
or whether a renumbering pass is needed for all suites using IDs in the reserved range.

## Status

Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-2/data-formats`.
Revisit as part of the dep-loading asymmetry investigation.
