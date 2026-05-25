# RUNNER GAP: AL0133 — Text-to-Integer type mismatch in Report.Run overloads

## Error

```
AL0133: Argument 1: cannot convert from 'Text' to 'Integer'  (x3 @ ReportTextNameSrc.al)
```

## Trigger

`309700-report-text-name-overloads/src/ReportTextNameSrc.al` calls `Report.Run(Text, ...)` —
passing a text report name where BC 27.5 expects `Integer` (report ID). This likely tests
text-name overloads of `Report.Run` that require a newer BC ALC version.

## Root cause

BC 27.5 ALC does not provide a `Report.Run(Text, ...)` overload. Fails in both per-suite (0P/0F)
and bundled mode. Not a bundled-mode-specific issue.

## Fix path (not implemented)

Use the integer-ID overload or a supported text-name mechanism. AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/0F (compile fail in both modes).
