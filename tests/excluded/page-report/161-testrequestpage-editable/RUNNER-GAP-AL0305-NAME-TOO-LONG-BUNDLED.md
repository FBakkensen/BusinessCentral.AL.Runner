# RUNNER GAP: AL0305 — Codeunit name exceeds 30 characters in bundled mode

## Error

```
AL0305: The length of the application object identifier 'TRE TestRequestPage Editable Test'
  cannot exceed 30 characters.  (@ TestRequestPageEditableTest.al@1:16)
```

The identifier `TRE TestRequestPage Editable Test` is 33 characters (exceeds the 30-char limit).

## Trigger

`161-testrequestpage-editable/test/TestRequestPageEditableTest.al` defines a codeunit whose name
is 33 characters long. BC 27.5 bundled compilation enforces the AL0305 identifier length limit.

## Root cause

**Bundled-mode dep-load asymmetry (BUNDLED):** Per-suite produces 2P/3F. In bundled mode AL0305
causes `emitSuccess=False` for the entire bundle.

BC 27.5 bundled compilation applies the 30-character namespace-anchor length check globally across
all compiled objects. Per-suite mode does not enforce this limit.

## Fix path (not implemented)

Shorten the codeunit name to ≤30 characters. This is an AL source change — not permitted under
the no-rewrite rule.

Investigate whether bundled mode can suppress AL0305 (or whether it is intentional BC behavior
for multi-object compilation).

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 2P/3F.
