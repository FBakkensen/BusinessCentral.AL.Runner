# RUNNER GAP: AL0810 + AL0725 — Invalid system action names and type in bundled mode

## Errors

```
AL0810: The name 'Print' cannot be used for a system action.
  The allowed names in a 'Card' page are: ''.  (@ SystemActionSrc.al@27:26)
AL0810: The name 'SendMail' cannot be used for a system action.
  The allowed names in a 'Card' page are: ''.  (@ SystemActionSrc.al@28:26)
AL0725: The action type 'SystemAction' is not allowed in area 'Promoted'.
  (@ SystemActionSrc.al@27:26)
```

## Trigger

`73-systemaction/src/SystemActionSrc.al` defines system actions named `Print` and `SendMail` in a
`Card` page, and places a `SystemAction` in the `Promoted` area. BC 27.5 ALC rejects all of these.

## Root cause

BC 27.5 stricter validation in bundled compile:
- **AL0810**: `Card` pages do not support system actions named `Print` or `SendMail` (allowed set
  is empty for Card pages in BC 27.5).
- **AL0725**: `SystemAction` type is not valid in `Promoted` action areas.

Per-suite produces 2P/0F — these checks are not enforced in per-suite mode. In bundled mode they
cause `emitSuccess=False`.

## Fix path (not implemented)

Remove or replace the invalid system actions. This is an AL source change.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 2P/0F.
