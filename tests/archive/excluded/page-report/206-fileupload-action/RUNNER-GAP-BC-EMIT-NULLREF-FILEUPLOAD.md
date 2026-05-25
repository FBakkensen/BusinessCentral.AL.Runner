# RUNNER GAP: BC compiler emit NullReferenceException on `fileupload` action property

## Error

```
AggregateException: One or more errors occurred.
  (Failure while emitting metadata for object:'Page "FUA Page"'
   (Object reference not set to an instance of an object.))
inner[NullReferenceException]: Object reference not set to an instance of an object.
  at Microsoft.Dynamics.Nav.CodeAnalysis.Emit.ObjectMetadataEmitHelper.WriteAttributeProperties(...)
  at Microsoft.Dynamics.Nav.CodeAnalysis.Emit.PageBaseMetadataEmitter`1.WriteAction(...)
  at Microsoft.Dynamics.Nav.CodeAnalysis.Emit.PageMetadataEmitter.<EmitMetadata>b__1_0(...)
```

Also surface parse errors (AL0124) for `fileupload` and `AllowedFileExtensions` properties:
```
AL0124: The property 'fileupload' cannot be used in this context
AL0124: The property 'AllowedFileExtensions' cannot be used in this context
```

## Trigger

`206-fileupload-action/src/FuaPage.al` defines a page action with the `fileupload` property and
`AllowedFileExtensions`. BC 27.5 ALC does not recognise these as valid action properties in this
context, generating AL0124 parse errors. During metadata emit the BC compiler then crashes with a
NullReferenceException in `WriteAttributeProperties` because the property symbol cannot be resolved.

This is a **real BC compiler bug**: the emit path does not guard against unresolved/null property
symbols that survive the parse stage.

## Root cause

BC 27.5 ALC version does not support the `fileupload` action type / property. The parse errors are
expected. The NullReferenceException in the emit path is an additional BC compiler defect: it should
degrade gracefully (emit an AL diagnostic) rather than throw.

In bundled mode this crash causes `emitSuccess=False` for the entire bundle, blocking all other
suites. Per-suite it only fails this one suite.

## Fix path (not implemented)

No runner-side fix is possible without modifying AL source (not permitted under no-rewrite rule).
The BC emit crash would need to be fixed upstream in BC, or the suite updated to use a supported
action type.

Track as a BC compiler defect: `ObjectMetadataEmitHelper.WriteAttributeProperties` crashes on
unresolved action properties instead of emitting a diagnostic.

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
