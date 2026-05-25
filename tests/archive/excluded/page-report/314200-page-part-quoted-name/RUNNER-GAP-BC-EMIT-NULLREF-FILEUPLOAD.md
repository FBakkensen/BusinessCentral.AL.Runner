# RUNNER GAP: BC compiler emit NullReferenceException on `fileupload` action property

## Error

Cascade of AL0104/AL0124/AL0198 parse errors from a `fileupload` action property in
`314200-page-part-quoted-name/src/QuotedPartPage.al`, followed by BC compiler emit crash
(NullReferenceException in `WriteAttributeProperties`).

```
AL0124: The property 'fileupload' cannot be used in this context
AL0104: Syntax error, '=' expected
AL0124: The property 'ApplicationArea' cannot be used in this context
AL0104: Syntax error, '}' expected  (x2)
AL0198: Expected one of the application object keywords (...)
```

## Trigger

`QuotedPartPage.al` contains an action with the `fileupload` property at line 54. BC 27.5 ALC does
not recognise `fileupload` as a valid action property in this page type, causing a parse cascade.
During metadata emit the BC compiler crashes with a NullReferenceException (same defect as
`206-fileupload-action`).

In bundled mode this cascade contributes to `emitSuccess=False` for the entire bundle.

## Root cause

Same root as `206-fileupload-action`: BC 27.5 does not support the `fileupload` action property,
and the emit path does not guard against unresolved property symbols.

## Fix path (not implemented)

No runner-side fix. The `fileupload` property is not supported in this BC 27.5 ALC version.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
