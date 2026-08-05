# RUNNER GAP: AL0288 — `var RunTrigger` mismatch when publisher is non-var

## Error
```
AL0288: The parameter 'RunTrigger' must not be passed by reference because the corresponding
parameter in the publisher method is not passed by reference.
```

## Trigger
BC 27.5 introduced stricter validation on event subscriber parameter modifiers. This suite
subscribes to `OnAfterInsertEvent` / `OnAfterModifyEvent` / `OnAfterDeleteEvent` / etc. with
a `var RunTrigger : Boolean` parameter, but the corresponding publisher parameter is declared
as a plain (non-var) `Boolean`.

BC 27.5 now rejects the mismatch at compile time. Earlier BC versions were lenient.

## Why we do NOT fix the AL
The entire purpose of this suite is to validate that the runner correctly wraps `bool → ByRef<bool>`
when a subscriber declares `var RunTrigger`. Removing the `var` modifier would:
- Suppress the AL0288 error, but
- Eliminate the very scenario the test was designed to cover.

The test would pass trivially without ever exercising the ByRef-wrapping code path.

## Fix path (not implemented)
Two valid approaches (neither implemented here):
1. Update the suite to target an event whose publisher signature uses `var RunTrigger` natively
   (if such an event exists in the BC platform symbols).
2. Wait for a runner-level suppression of AL0288 in per-suite mode (investigate the asymmetry
   between per-suite tolerance and bundled rejection first).

## Status
Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-1/record-table`.
6 AL0288 diagnostics blocked the entire bundled compile. Revisit after the per-suite vs bundled
symbol-resolution asymmetry is understood.
