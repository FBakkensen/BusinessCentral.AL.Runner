# RUNNER GAP: AL0240 — ReportHandler signature mismatch in bundled mode

## Error

```
AL0240: The signature of procedure 'REGVReportHandler' does not match the signature required by
  attribute 'ReportHandler': parameter 1 is expected to be of type 'var Report' but found type
  'var TestRequestPage REGV Base Report'.
  (@ ReportExtGlobalVarTest.al@30:37)
```

## Trigger

`311800-reportext-globalvar/test/ReportExtGlobalVarTest.al` uses a `TestRequestPage`-typed
parameter in a `ReportHandler` where BC 27.5 requires `var Report: Report`.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 1P/1F. In bundled mode AL0240
causes `emitSuccess=False` for the entire bundle.

## Fix path (not implemented)

Correct the `ReportHandler` parameter type. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 1P/1F.
