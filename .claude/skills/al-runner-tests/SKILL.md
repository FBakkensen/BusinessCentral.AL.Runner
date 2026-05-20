---
name: al-runner-tests
description: How to write, place, and run AL test suites in this repo — proving-test rules, bucket/category layout, AL object-ID uniqueness, run-all-buckets command, build commands, stubs invocation. Use when adding a test suite under tests/, picking object IDs for a new codeunit/table/page, running the test matrix locally, or evaluating whether an existing test "proves" anything.
---

# Writing AL tests

## What a proving test looks like

A skeptic unfamiliar with the codebase must be able to read any test and say: "yes, if this passes, feature X works correctly in al-runner." Every test must satisfy all four:

**1. Positive case with a specific assertion**
```al
// Weak — proves almost nothing:
MyProc();                                 // just runs

// Strong — proves the output is correct:
Result := MyProc(3, 4);
Assert.AreEqual(7, Result, 'MyProc should return the sum');
```

**2. Negative case with a specific error**
```al
// Weak — proves something fails, but not what:
asserterror MyProc(-1);

// Strong — proves the right error fires:
asserterror MyProc(-1);
Assert.ExpectedError('Value must be positive');
```

**3. Would catch a broken mock.** If the test passes when the mock always returns a default value (`0`, `''`, `false`), it is not a proving test. Assert a non-default concrete value.

**4. Use `Assert.*` — never `if X then Error(...)`.** Custom guards are easy to get wrong. Use `Assert.AreEqual`, `Assert.IsTrue`, `Assert.IsFalse`, or `Assert.ExpectedError`.

Exception: "no-op stub" tests (`Hyperlink_NoThrow`, `Report_Run_IsNoOp`) are valid only when the *entire* claim is "this does not crash." The name must make that explicit.

## Suite layout

```
tests/
  bucket-1/                 ← backend logic
    record-table/           — record / table / field / filter / database / permissions
    codeunit-runtime/       — codeunit / event / dialog / error / scope / handler / session / library / language features
  bucket-2/                 ← presentation + data
    page-report/            — page / testpage / report / xmlport / query / action / views / fieldgroup
    data-formats/           — text / json / xml / date / numeric / format / stream / http / blob / media
  bucket-feature-niw/       ← suites needing a separate compile unit (NoImplicitWith app.json feature flag)
                            flat layout, no category subfolder
  stubs/                    ← 39-stubs (--stubs flag, separate invocation)
  excluded/                 ← fixtures not in the main loop
```

Each suite:
```
tests/<bucket>/<category>/<NN-descriptive-name>/
  src/   — AL source codeunit(s)
  test/  — AL test codeunit (Subtype = Test)
```

When adding a new suite, pick the `<bucket>/<category>` folder that matches the feature theme. Reuse the next free `NN-` prefix in the chosen category.

## Object IDs — unique within the bucket, inside the bucket's idRange

Each bucket has an idRange declared in `tests/bucket-N/app.json` (currently `50000..99999`). All object IDs in the bucket must fit inside that range AND be unique within the bucket. IDs may repeat across buckets. ID collisions or out-of-range IDs cause AL compile errors.

```bash
grep -rh "^codeunit \|^table \|^page \|^enum " tests/bucket-1/ \
  | awk '{print $1, $2}' | sort -k2 -n
```

**Object resolution is by NAME, not ID.** IDs are a licensing-era artifact in AL — renumbering them does not break cross-references. But two suites in the same bucket cannot share an object NAME (only one definition wins in a bundled compile), so each suite's codeunit/table/page names must be unique within the bucket.

## Running tests

### V2 — bundled per bucket (canonical, fast, matches V1 semantics)

The V2 runner now compiles and runs an entire bucket as one AL package — this is the canonical way to run tests. Each bucket's `app.json` declares its `idRange`, `target`, and any required `preprocessorSymbols`, so the bundle compiles like a normal BC extension.

```bash
dotnet build spike/v2/Runner -c Release
dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-1
dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-2
```

Or pass multiple buckets in one invocation:
```bash
dotnet run --project spike/v2/Runner -c Release --no-build -- tests/bucket-1 tests/bucket-2
```

Per-suite mode (`--per-suite`) still exists for diagnostic comparison but is no longer the default. Bundled mode is 5-7× faster on representative buckets and exercises the same name-based object resolution V1 used.

### V1 — per-suite (legacy)

```bash
# Run all buckets (mirrors .github/workflows/test-matrix.yml)
for bucket in tests/bucket-*/; do
  args=""
  for suite in "$bucket"*/*/; do
    [ -d "${suite}src"  ] && args="$args ${suite}src"
    for appdir in "${suite}"app*/; do
      [ -d "$appdir" ] && args="$args $appdir"
    done
    [ -d "${suite}test" ] && args="$args ${suite}test"
  done
  dotnet run --project AlRunner --framework net10.0 -- --strict --test-isolation method $args
done

# Stubs test (separate invocation)
dotnet run --project AlRunner --framework net10.0 -- --stubs \
  tests/stubs/39-stubs/stubs tests/stubs/39-stubs/src tests/stubs/39-stubs/test
```

## Build

```bash
dotnet build AlRunner/
dotnet run --project AlRunner -- ./src ./test
```
