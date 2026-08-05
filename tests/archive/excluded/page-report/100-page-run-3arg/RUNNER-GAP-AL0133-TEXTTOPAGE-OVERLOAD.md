# RUNNER GAP: AL0133 — Text-to-Integer type mismatch in Page.Run 3-argument overload

## Error

```
AL0133: Argument 1: cannot convert from 'Text' to 'Integer'  (x5 @ Source.al)
AL0133: Argument 3: cannot convert from 'FieldRef' to 'Integer'  (@ Source.al@69:43)
```

## Trigger

`100-page-run-3arg/src/Source.al` calls `Page.Run(Text, ...)` — passing a `Text` value where BC
27.5 expects `Integer` (the page ID). This suggests the suite was written for a `Page.Run` overload
that accepts a page name (text) instead of an ID, or the AL syntax for named-page references.

## Root cause

BC 27.5 ALC does not recognise the `Text`-argument form of `Page.Run(...)`. The suite compiles with
AL0133 errors even in per-suite mode (0P/0F — no tests run), confirming this is not a
bundled-mode-specific issue.

Per-suite mode shows `0P/0F/0E across 0 tests` — the compile errors block test discovery entirely.

## Fix path (not implemented)

Use the correct page ID (integer) or a supported overload. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/0F (compile fail in both modes).
