# RUNNER GAP: AL0616/AL0440 — Interface method named `Run` conflicts with codeunit built-in in bundled mode

## Errors

```
AL0616: Defining the contract 'Run' on interface 'Run()' is not allowed because it is matching a built-in procedure for codeunits.
AL0440: The Codeunit 'Low Priority Alert' already defines a method called 'Run' with the same parameter types in 'V2_data-formats by AlRunnerV2 (1.0.0.0)'.
AL0440: The Codeunit 'High Priority Alert' already defines a method called 'Run' with the same parameter types in 'V2_data-formats by AlRunnerV2 (1.0.0.0)'.
```

## Trigger

The suite defines interface `IMyAlert` with a method `Run(): Integer` (src/Alert.al). Two codeunits
(`Low Priority Alert`, `High Priority Alert`) implement this interface.

In per-suite mode this compiles cleanly. In bundled mode BC 27.5 enforces AL0616: an interface
procedure cannot be named `Run` because it clashes with the codeunit built-in `Run` procedure.
The AL0440 errors for the implementing codeunits are downstream of AL0616.

## Root cause

This is a **bundled-mode stricter semantic validation**. BC 27.5 bundled compilation applies stricter
AL semantic rules than per-suite mode, possibly because the cross-extension symbol resolver is active.
The `Run` procedure is a built-in for codeunits; using it as an interface contract is only tolerated
in isolated per-suite compilation.

## Fix path (not implemented)

Rename the interface method (e.g., to `Execute(): Integer`) and update the implementing codeunits.
This is an AL source change — not permitted under the no-rewrite rule.

Investigate whether this stricter validation is intentional BC behavior or a bundled-mode quirk.

## Status

Quarantined on `spike/bc-abi-identity` during the bundled-compile migration of `bucket-2/data-formats`.
Revisit as part of the bundled-mode semantic parity investigation.
