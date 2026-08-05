# RUNNER GAP: AL0680 — `addAfter` on top-level data item in bundled mode

## Error

```
AL0680: Cannot use addBefore or addAfter on a top-level data item.
  The anchor ItemRec is a top level data item.  (x2 @ AddafterDatasetSrc.al@54:18, 71:18)
```

## Trigger

`154-addafter-dataset/src/AddafterDatasetSrc.al` uses `addAfter` targeting `ItemRec`, a top-level
data item in the base report. BC 27.5 rejects this with AL0680 in bundled mode.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 6P/0F — all tests pass. In
bundled mode AL0680 causes `emitSuccess=False` for the entire bundle. Same pattern as
`136-addbefore-dataset` (sibling AL0680 quarantine).

## Fix path (not implemented)

Restructure the reportextension to avoid `addAfter` on a top-level data item. AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 6P/0F.
