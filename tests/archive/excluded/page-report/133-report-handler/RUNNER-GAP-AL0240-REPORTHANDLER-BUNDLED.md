# RUNNER GAP: AL0240 — ReportHandler signature mismatch in bundled mode

## Error

```
AL0240: The signature of procedure 'TestReportStaticRunHandler' does not match the signature
  required by attribute 'ReportHandler': parameter 1 is expected to be of type 'var Report'
  but found type 'var TestRequestPage Test Report Handler'.
  (@ TestReportHandler.al@90:46)
```

## Trigger

`133-report-handler/test/TestReportHandler.al` declares `TestReportStaticRunHandler` with a
`TestRequestPage` typed parameter where BC 27.5 requires `var Report: Report`.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 0P/6F — all tests fail even
without bundled mode. In bundled mode AL0240 causes `emitSuccess=False` for the entire bundle.

## Fix path (not implemented)

Correct the `ReportHandler` parameter type. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/6F (already all-failing).
