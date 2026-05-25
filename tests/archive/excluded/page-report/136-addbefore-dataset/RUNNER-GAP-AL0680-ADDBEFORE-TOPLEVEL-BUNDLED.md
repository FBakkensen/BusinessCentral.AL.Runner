# RUNNER GAP: AL0680 — `addBefore`/`addAfter` on top-level data item in bundled mode

## Error

```
AL0680: Cannot use addBefore or addAfter on a top-level data item.
  The anchor ItemRec is a top level data item.  (@ AddbeforeDatasetSrc.al@56:19)
```

## Trigger

`136-addbefore-dataset/src/AddbeforeDatasetSrc.al` uses `addBefore` targeting `ItemRec`, a
top-level data item in the base report. BC 27.5 ALC rejects this with AL0680 in bundled mode.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 6P/0F — all tests pass. In
bundled mode AL0680 causes `emitSuccess=False` for the entire bundle.

BC 27.5 enforces AL0680 strictly in bundled compilation: `addBefore`/`addAfter` can only target
nested data items, not top-level data items in a report.

## Fix path (not implemented)

Restructure the reportextension to use a nested data item strategy. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 6P/0F.
