# RUNNER GAP: AL0853 — Circular page reference (page used as part in itself)

## Error

```
AL0853: 'Page "TPX Test Card"' cannot be used as a part in 'Page "TPX Test Card"'
  because it causes a circular reference.  (@ Source.al@13:12)
```

## Trigger

`90-testpage-extended/src/Source.al` defines a page that includes itself as a part (circular
reference). BC 27.5 ALC rejects this with AL0853 in bundled mode.

## Root cause

This is a real AL semantic error: a page cannot include itself as a page part. The suite produces
0P/10F per-suite (all tests fail even without bundled-mode constraints), confirming the test is
genuinely broken.

BC 27.5 enforces AL0853 in bundled mode and the per-suite mode likely also rejects it but runs
the test body anyway (producing failures). In bundled mode it causes `emitSuccess=False`.

## Fix path (not implemented)

Fix the circular page reference in `Source.al`. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/10F (already all-failing).
