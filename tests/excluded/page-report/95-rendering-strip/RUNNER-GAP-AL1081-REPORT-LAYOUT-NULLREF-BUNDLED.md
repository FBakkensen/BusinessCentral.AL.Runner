# RUNNER GAP: AL1081 — BC compiler NullReferenceException updating report layout in bundled mode

## Error

```
AL1081: Unable to update report layout 'ExcelLayout' for 'Report "RS Rendering Report"'.
  Reason: Object reference not set to an instance of an object.  (@ Report.al@14:16)
AL1081: Unable to update report layout 'WordLayout' for 'Report "RS Rendering Report"'.
  Reason: Object reference not set to an instance of an object.  (@ Report.al@20:16)
```

## Trigger

`95-rendering-strip/src/Report.al` defines a report with `ExcelLayout` and `WordLayout` rendering
sections. BC 27.5 ALC cannot update these report layouts during metadata emit — the layout update
path throws a NullReferenceException, surfaced as AL1081.

## Root cause

**Bundled-mode BC compiler bug:** In per-suite mode the AL1081 is emitted as a warning and the
suite still runs (2P/0F). In bundled mode the same error causes `emitSuccess=False`, blocking the
entire bundle.

This is a **real BC compiler defect**: `ObjectMetadataEmitter` / report-layout update path throws
a NullReferenceException instead of degrading gracefully. The asymmetry (per-suite tolerates,
bundled fails) suggests the error is treated as fatal only in the bundled compilation path.

## Fix path (not implemented)

No runner-side fix. The BC compiler should degrade gracefully on layout update failures instead of
surfacing them as fatal `emitSuccess=False` in bundled mode.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 2P/0F.
