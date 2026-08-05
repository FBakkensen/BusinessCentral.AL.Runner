# RUNNER GAP: AL0240 — ReportHandler signature mismatch in bundled mode

## Error

```
AL0240: The signature of procedure 'NoOpReportHandler' does not match the signature required by
  attribute 'ReportHandler': parameter 1 is expected to be of type 'var Report' but found type
  'var TestRequestPage TestBaseReportForExt'.
  (@ Tests.al@40:37)
```

## Trigger

`130-reportext-header-scope/test/Tests.al` declares a `ReportHandler` with a `TestRequestPage`
typed first parameter. BC 27.5 requires `var Report: Report` for this attribute.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** In per-suite mode the suite produces 1P/1F (some
tests still run despite the AL0240 warning). In bundled mode the same AL0240 error contributes to
`emitSuccess=False` for the entire bundle, blocking all suites.

BC 27.5 bundled compilation applies stricter cross-object validation for handler attributes.

## Fix path (not implemented)

Change the `ReportHandler` parameter type to `var Report: Report`. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 1P/1F.
