# RUNNER GAP: AL0104 — `addFirst` in fieldgroup not supported by BC 27.5 ALC

## Error

```
AL0104: Syntax error, '}' expected  (@ AFGSrc.al@26:9)
AL0198: Expected one of the application object keywords (...)
```

## Trigger

`277-addfirst-fieldgroup/src/AFGSrc.al` uses `addFirst` inside a fieldgroup definition (a BC 27.x+
AL extension syntax). BC 27.5 ALC does not recognise this syntax, producing a parse error at line 26.

## Root cause

The `addFirst` / `addLast` modifiers for fieldgroup extensions require a BC ALC version that supports
them. BC 27.5 ALC rejects this syntax at parse time, which prevents compilation.

Per-suite this suite passes (2P/0F) — suggesting that per-suite mode uses a slightly different
compiler version or parse path. In bundled mode the same parse error causes `emitSuccess=False`.

## Fix path (not implemented)

No runner-side fix. This requires either a newer ALC version or removing the `addFirst` syntax
(AL source change — not permitted under no-rewrite rule).

## Status

Quarantined on `spike/bc-abi-identity` during bundled-compile migration of `bucket-2/page-report`.
Per-suite passes: 2P/0F.
