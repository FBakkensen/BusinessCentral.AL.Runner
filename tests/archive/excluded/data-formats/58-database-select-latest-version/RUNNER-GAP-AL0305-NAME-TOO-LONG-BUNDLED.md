# RUNNER GAP: AL0305 — Codeunit name exceeds 30 characters in bundled mode

## Error

```
AL0305: The length of the application object identifier 'Test Database SelectLatestVersion' cannot exceed 30 characters.
```

## Trigger

The codeunit `58401 "Test Database SelectLatestVersion"` has a 33-character name. Per-suite
compilation does not enforce the 30-character namespace-anchor limit. Bundled compilation
enforces it strictly because the entire directory is compiled as a single extension, and the
namespace-anchor length check is applied globally.

## Root cause

BC 27.5 bundled-compile mode enforces AL0305 (identifier >30 chars) where per-suite does not.
The asymmetry is a bundled-mode stricter validation path, not a runner bug per se.

## Fix path (not implemented)

Rename the codeunit to a name of ≤30 characters. This is an AL source change — not permitted
under the no-rewrite rule.

Investigate whether bundled mode can suppress AL0305 (or whether it is intentional BC behavior
for multi-object compilation) before touching the AL source.

## Status

Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-2/data-formats`.
Revisit as part of the bundled-mode semantic parity investigation.
