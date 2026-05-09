# RUNNER GAP: AL0275 + BC emit NRE — `Image` codeunit name collides with System Application in bundled mode

## Errors

```
AL0197: An application object of type 'Codeunit' with name 'Image' is already declared by the extension 'System Application by Microsoft (27.5.46862.48827)'
AL0275: 'Image' is an ambiguous reference between 'Image' defined by the extension 'V2_data-formats ...' and 'Image' defined by the extension 'System Application by Microsoft (27.5.46862.48827)'.
InvalidOperationException: Unexpected value 'None' of type 'Microsoft.Dynamics.Nav.CodeAnalysis.NavTypeKind'
  at Microsoft.Dynamics.Nav.CodeAnalysis.Emit.CodeGenerator.EmitFieldInitializer(TypeSymbol typeSymbol)
```

## Trigger

The suite defines a local stub `codeunit 53971 "Image"` (src/Image.al) to stand in for BC's built-in
`Image` codeunit from System Application. In per-suite mode the System Application package is not
loaded, so the stub is the only definition and compiles cleanly.

In bundled mode the System Application package IS loaded (it is part of the shared symbol set), so
two codeunits named `"Image"` exist simultaneously. BC 27.5 emits AL0197 (duplicate name) and AL0275
(ambiguous reference) for all usage sites. The ambiguity leaves the type symbol in a `NavTypeKind.None`
state; the BC code-generator hits an `InvalidOperationException` trying to emit field initializers for
that unresolved type.

## Fix path (not implemented)

**BUNDLED**: The dep-loading asymmetry is the root cause. In bundled mode the full System Application
is always present; the local `Image` stub shadows it incorrectly.

Options (do not implement now):
1. Rename the local stub codeunit ID/name so it does not clash (AL source change — violates no-rewrite rule).
2. Investigate whether bundled mode can exclude specific symbol packages per-suite (runner change).
3. Move 315-image-codeunit to a dedicated per-suite-only bucket.

## Status

Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-2/data-formats`.
The BC emit InvalidOperationException is a downstream consequence of the AL0275 ambiguity — fixing the
symbol-load asymmetry will resolve both. Revisit as part of the dep-loading asymmetry investigation.
