# RUNNER GAP: AL0223 — `PromptDialog` page type requires `Extensible = false` in bundled mode

## Error

```
AL0223: The property 'PromptDialog' can only be used if the property 'Extensible' is set to 'False'
  @ PromptModeSrc.al@7:5
```

## Trigger

`100-page-promptmode/src/PromptModeSrc.al` defines a page with `PageType = PromptDialog` but without
`Extensible = false`. BC 27.5 ALC in bundled-compile mode enforces AL0223 strictly.

## Root cause

BC 27.5 stricter semantic check: `PromptDialog` pages must declare `Extensible = false`. Per-suite
this suite passes (6P/0F), suggesting per-suite compilation tolerates the omission. Bundled mode
enforces the constraint globally, causing `emitSuccess=False`.

This is a **bundled-mode dep-load asymmetry**: the stricter validation only triggers when the full
symbol set is compiled together.

## Fix path (not implemented)

Add `Extensible = false;` to the page definition. This is an AL source change — not permitted under
the no-rewrite rule.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 6P/0F.
