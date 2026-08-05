# RUNNER GAP: AL0295 — Empty field-name in 6-arg [EventSubscriber]

## Error
```
AL0295: The field name '' is not valid.
```

## Trigger
BC 27.5 (bundled-compile mode) rejects an empty string `''` as the `fieldName` argument in the
6-argument form of `[EventSubscriber]`:

```al
[EventSubscriber(ObjectType::Table, Database::<TableName>, 'OnAfterValidateEvent', '', false, false)]
```

The sixth positional argument (`''`) is the field name. BC 27.5 strictly validates that this
argument is either a real field name or omitted by using the 5-arg overload.

## Fix path (not implemented)
Switch to the 5-argument overload to subscribe to all fields:

```al
[EventSubscriber(ObjectType::Table, Database::<TableName>, 'OnAfterValidateEvent', true, false)]
```

This is intentionally not fixed because:
- Per-suite mode tolerates the empty string (the asymmetry is worth investigating separately).
- Changing the AL signature would alter the test's coverage intent.

## Status
Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-1/record-table`.
Revisit when the per-suite vs bundled symbol-resolution asymmetry is understood.
