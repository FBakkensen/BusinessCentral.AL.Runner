# Runner gap: `Codeunit "AL Runner Config"` not implemented in v2

**Suites quarantined here that hit this gap:**
- `271-companyproperty` — tests `CompanyProperty.{DisplayName,UrlName,ID}()` after `Set*` calls.
- `242-company-name` — tests `CompanyName()` after `SetCompanyName()`.

**The gap:**
The al-runner-only configuration codeunit `131100 "AL Runner Config"` is the
runtime contract between AL test code and the runner's session settings (the
AL surface for `--company-name` / company display props).

- v1 (`AlRunner/`) implements this via type-renaming + `MockSession` routing —
  the `MockCodeunitHandle` for codeunit 131100 dispatches to `MockSession` field
  setters and the `CompanyName()` / `CompanyProperty.X()` system getters read
  from there.
- v2 has no equivalent. The v1 stub at `AlRunner/stubs/AlRunnerConfig.al` has
  empty procedure bodies (the routing happens at runtime via the rename).
  Pulling that stub into v2 would silently make all setters no-ops — tests
  would assert wrong values without an obvious diagnostic. **§2 invariant #2
  forbids that path: never paper over.**

**Fix path:** implement the v2 equivalent of the v1 routing — JMP-hooks on the
8 procedures (Set/Get × CompanyName/CompanyDisplayName/CompanyUrlName/CompanyId)
that write through to the real `NavSession` company state, and matching read
paths in `NavSession.CompanyName` / `NavCompanyProperty.X`. Plus reset between
test runs (mirrors v1's `--company-name` CLI default).

**Re-include after fix:** move the suites back to
`tests/bucket-1/record-table/` and re-run `--bundled tests/bucket-1/record-table`
to confirm pass-count parity.

**GitHub issue:** TODO — file under runner-gap template before next session.
