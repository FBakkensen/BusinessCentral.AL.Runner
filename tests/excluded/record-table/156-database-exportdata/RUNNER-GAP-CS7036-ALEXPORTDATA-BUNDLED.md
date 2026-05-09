# RUNNER GAP: CS7036 — ALExportData mock arity mismatch in bundled mode

## Error
```
EDT Helper.cs(118,28): error CS7036: There is no argument given that corresponds to the required
parameter 'description' of 'ALDatabase.ALExportData(DataError, bool, ByRef<NavText>, string, bool, bool, bool, NavRecord)'
```

## Trigger
The AL source calls `Database.ExportData(showDialog, FileName)` — a 2-argument form.

In **per-suite** mode this compiles and runs successfully (the runner no-ops the call).

In **bundled** mode the BC compiler generates a C# call to the 8-argument internal form:
`ALDatabase.ALExportData(DataError, bool, ByRef<NavText>, string, bool, bool, bool, NavRecord)`.

The runner's C# mock for `ALExportData` (no-op stub) does not expose this 8-argument overload,
so the C# compile step fails with CS7036.

## Root cause
Bundled-mode dep-loading asymmetry: the BC compiler resolves a different (richer) overload of
`Database.ExportData` when it has the full bundled symbol set available, generating a call to the
full internal 8-arg form instead of the simplified 2-arg form produced in per-suite mode.

This is the same class of problem as the `309200-table-overloads` / `FullyQualifiedName` gap —
both are symptoms of the bundled compiler seeing more symbols than per-suite.

## Fix path (not implemented)
Add an 8-argument `ALExportData` overload to the runner's `ALDatabase` mock, or investigate
why per-suite triggers the 2-arg form and align bundled to do the same.

## Status
Quarantined on `spike/bc-abi-identity` after the initial 3 AL-emit quarantines unmasked this
C# stage error. This issue was previously hidden because the AL emit was failing first (AL0295 /
AL0288 / AL0132 errors).
