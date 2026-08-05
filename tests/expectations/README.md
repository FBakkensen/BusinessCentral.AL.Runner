# tests/expectations/

Runner-owned manifest declaring expected outcomes for tests in
`tests/al-language/` (the BusinessCentral.AL.Language.Tests submodule).

See [`docs/expectations.md`](../../docs/expectations.md) for the schema, mode
semantics, and result-classification table.

Each JSON file is an array of expectation objects following the schema. File
naming convention:

- `oos-<area>.json` — out-of-scope-by-design (most common)
- `known-gaps-<area>.json` — in-scope but not yet implemented (transient, links
  to GitHub issues)
- `disabled-<area>.json` — won't compile or won't run; pure skip

Sharding by area keeps PR diffs small. A single PR adding or removing one
expectation should touch one file with one entry.
