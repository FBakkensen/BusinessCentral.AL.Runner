# RUNNER GAP: AL0132 — `Record.FullyQualifiedName()` not found in bundled mode

## Error
```
AL0132: The name 'FullyQualifiedName' does not exist in the current context.
```

## Trigger
`Record.FullyQualifiedName()` is a real BC platform method. It resolves correctly in per-suite
compilation mode but fails to resolve in bundled-compile mode.

## Root cause hypothesis (NOT confirmed — do not investigate now)
This is a **bundled-mode dep-loading asymmetry**. Per-suite invokes the BC compiler once per
suite and loads a fresh symbol set for each; bundled invokes it once for the entire directory.
The bundled compilation appears to not load the same set of platform symbols (or loads them in
a different order/scope) as per-suite, leaving `FullyQualifiedName` invisible.

Likely candidates:
- The bundled compile passes different `packageCachePaths` or `dependencies` than per-suite.
- A platform `.app` reference that per-suite picks up automatically is missing from the bundled
  symbol resolver.

## Fix path (not implemented)
Investigate the dep-loading logic in `spike/v2/Runner` for bundled vs per-suite paths and ensure
both modes surface the same symbol set. This is the right fix — do NOT remove or stub
`FullyQualifiedName()` in the AL source.

## Status
Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-1/record-table`.
1 AL0132 diagnostic blocked the entire bundled compile. Revisit as part of the dep-loading
asymmetry investigation.
