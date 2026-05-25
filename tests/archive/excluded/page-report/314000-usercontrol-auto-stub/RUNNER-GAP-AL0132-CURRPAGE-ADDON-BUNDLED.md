# RUNNER GAP: AL0132 — `CurrPage.MyAddin` not defined in bundled mode

## Error

```
AL0132: 'CurrPage "UCStub Page"' does not contain a definition for 'MyAddin'
  (@ UCStubPage.al@15:18)
```

## Trigger

`314000-usercontrol-auto-stub/src/UCStubPage.al` references `CurrPage.MyAddin`, a usercontrol
add-in registered via `controladdin`. In bundled mode the usercontrol symbol is not resolved in
the global namespace — `CurrPage` type does not expose `MyAddin`.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 0P/0F (compile fail). In
bundled mode AL0132 additionally blocks the entire bundle from emitting.

The `controladdin` registration and `CurrPage` member injection for usercontrols requires a
symbol-resolution path not active in bundled compilation with BC 27.5 ALC.

## Fix path (not implemented)

Requires usercontrol stub support in the bundled dep-loader. Track as part of the bundled-mode
dep-load investigation.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 0P/0F (compile fail in both modes).
