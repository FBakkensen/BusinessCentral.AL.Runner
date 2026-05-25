# RUNNER GAP: AL0264 — Codeunit ID 130002 conflicts with Library Assert (bundled mode)

## Error

```
AL0264: An application object of type 'Codeunit' with ID '130002' is already declared by
  the extension 'Library Assert by Microsoft (27.5.46862.48827)'
  (@ RieTest.al@1:10)
```

## Trigger

`281-report-instance-ext/test/RieTest.al` defines a codeunit with ID 130002. In bundled mode
the full platform symbol set is loaded, which includes the BC `Library Assert` extension that
already owns ID 130002. This collision triggers AL0264.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 0P/6F (already all-failing due
to the collision not being caught at test-run time). In bundled mode AL0264 causes
`emitSuccess=False` for the entire bundle.

The ID 130002 is reserved by the BC platform `Library Assert` extension. This suite reuses the
same ID, which is an AL source defect (duplicate ID).

## Fix path (not implemented)

Assign a different codeunit ID to `RieTest.al`. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/6F (already all-failing).
