# RUNNER GAP: AL0217 — CueGroup field in RoleCenter area not allowed in bundled mode

## Error

```
AL0217: Only parts and groups are valid in an area of type 'RoleCenter'
  (x3 @ CuegroupSrc.al@30:26, 32:27, 36:27)
```

## Trigger

`71-cuegroup-section/src/CuegroupSrc.al` places CueGroup field items directly in a `RoleCenter`
area. BC 27.5 bundled-compile enforces AL0217: only parts and groups are valid in RoleCenter areas.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 7P/0F — all tests pass. The
AL0217 warning is emitted but does not prevent execution. In bundled mode the same error causes
`emitSuccess=False`, blocking all suites in the bundle.

## Fix path (not implemented)

Wrap the CueGroup fields in a `group` or `part`. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 7P/0F.
