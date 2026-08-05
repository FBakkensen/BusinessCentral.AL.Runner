# RUNNER GAP: AL0680 + AL0240 — reportextension top-level data item + ReportHandler signature in bundled mode

## Errors

```
AL0680: Cannot use addBefore or addAfter on a top-level data item.
  The anchor Cust is a top level data item.  (@ Ext.al@10:18)

AL0240: The signature of procedure 'NoOpHandler' does not match the signature required by attribute
  'ReportHandler': parameter 1 is expected to be of type 'var Report' but found type
  'var TestRequestPage RptExt GetDI Base'.  (@ Tests.al@54:31)
```

## Trigger

`300-reportext-getdataitem/src/Ext.al` uses `addBefore`/`addAfter` to position a data item
relative to a top-level data item `Cust`. BC 27.5 rejects this with AL0680.

`300-reportext-getdataitem/test/Tests.al` defines `NoOpHandler` with a `TestRequestPage` typed
parameter where BC 27.5 requires `var Report: Report` for the `ReportHandler` attribute.

## Root cause

Two distinct BC 27.5 stricter checks active in bundled mode:
- **AL0680**: `addBefore`/`addAfter` cannot target top-level data items in reportextensions.
- **AL0240**: `ReportHandler` attribute requires the handler parameter to be `var Report: Report`,
  not a `TestRequestPage` subtype. Per-suite mode this suite produces 2P/1F; bundled mode the
  AL0680 + AL0240 errors cause `emitSuccess=False`.

Both checks are **bundled-mode stricter validations** (BUNDLED tag: wider symbol set triggers
stricter cross-object checks).

## Fix path (not implemented)

- AL0680: restructure the reportextension to use a nested data item instead of `addBefore` on a top-level anchor.
- AL0240: correct the `ReportHandler` parameter type. Both require AL source changes.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 2P/1F.
