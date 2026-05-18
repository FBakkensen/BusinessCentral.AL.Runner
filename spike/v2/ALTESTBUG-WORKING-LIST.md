# AL Test Bug Working List — Category C

Generated: 2026-05-18. READ-ONLY enumeration. Do NOT fix here; use this as input for fix batches.

---

## Background

`Assert.ExpectedError(Expected)` in the runner's `Assert.Codeunit.al` (CU 50917 / bucket-1 and bucket-2 variants) uses
`StrPos(GetLastErrorText, Expected) = 0` to check the error. In AL, `StrPos(anyText, '')` returns a non-zero position,
making `Assert.ExpectedError('')` a **silent wildcard** — it passes on *any* error without verifying the message.

Real BC `Codeunit 130000.ExpectedError` does **not** treat `''` as a wildcard. Every `Assert.ExpectedError('')` in a test
proves only that *some* error was thrown; it does not verify the error message. Fix = replace `''` with the real expected
substring (or a prefix of it).

---

## Pattern 1 — Empty `Assert.ExpectedError('')` (31 active, 1 excluded)

### data-formats bucket-2

| File:Line | CU / Procedure | `asserterror` target | Current expectation | Suggested fix |
|---|---|---|---|---|
| `tests/bucket-2/data-formats/100-json-overloads/test/JsonOverloadsTest.al:29` | CU162001 / `GetText_WithRequireTrue_KeyMissing_ThrowsError` | `JsonObject.GetText('missing', true)` — key not found | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/100-json-overloads/test/JsonOverloadsTest.al:82` | CU162001 / `GetObject_ByIndex_OutOfBounds_ThrowsError` | `JsonArray.GetObject(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/173-jsonobject-extra-methods/test/JsonObjExtraTest.al:25` | CU100001 / `GetChar_MissingKey_Throws` | `JsonObject.GetChar('nope')` — key absent | `''` | `'nope'` or `'not found'` |
| `tests/bucket-2/data-formats/173-jsonobject-extra-methods/test/JsonObjExtraTest.al:45` | CU100001 / `GetDate_MissingKey_Throws` | `JsonObject.GetDate('nope')` — key absent | `''` | `'nope'` or `'not found'` |
| `tests/bucket-2/data-formats/173-jsonobject-extra-methods/test/JsonObjExtraTest.al:65` | CU100001 / `GetDateTime_MissingKey_Throws` | `JsonObject.GetDateTime('nope')` — key absent | `''` | `'nope'` or `'not found'` |
| `tests/bucket-2/data-formats/179-xmlnode-methods/test/XmlNodeTest.al:227` | CU106001 / `AsElement_ThrowsForAttribute` | `XmlNode.AsXmlElement()` called on an Attribute node | `''` | `'XmlElement'` or `'type'` |
| `tests/bucket-2/data-formats/182-jsonobject-extended/test/JsonObjExtTest.al:26` | CU113001 / `GetTime_MissingKey_ThrowsError` | `JsonObject.GetTime('missing')` — key absent | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/182-jsonobject-extended/test/JsonObjExtTest.al:48` | CU113001 / `GetDuration_MissingKey_ThrowsError` | `JsonObject.GetDuration('missing')` — key absent | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/182-jsonobject-extended/test/JsonObjExtTest.al:68` | CU113001 / `GetOption_MissingKey_ThrowsError` | `JsonObject.GetOption('missing')` — key absent | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/182-jsonobject-extended/test/JsonObjExtTest.al:90` | CU113001 / `GetByte_MissingKey_ThrowsError` | `JsonObject.GetByte('missing')` — key absent | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/182-jsonobject-extended/test/JsonObjExtTest.al:112` | CU113001 / `GetBigInteger_MissingKey_ThrowsError` | `JsonObject.GetBigInteger('missing')` — key absent | `''` | `'missing'` or `'not found'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:28` | CU115001 / `GetBigInteger_OutOfBounds_ThrowsError` | `JsonArray.GetBigInteger(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:50` | CU115001 / `GetByte_OutOfBounds_ThrowsError` | `JsonArray.GetByte(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:72` | CU115001 / `GetChar_OutOfBounds_ThrowsError` | `JsonArray.GetChar(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:94` | CU115001 / `GetDate_OutOfBounds_ThrowsError` | `JsonArray.GetDate(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:116` | CU115001 / `GetDateTime_OutOfBounds_ThrowsError` | `JsonArray.GetDateTime(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:138` | CU115001 / `GetDuration_OutOfBounds_ThrowsError` | `JsonArray.GetDuration(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:158` | CU115001 / `GetOption_OutOfBounds_ThrowsError` | `JsonArray.GetOption(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/183-jsonarray-extended-getters/test/JArrayExtTest.al:178` | CU115001 / `GetTime_OutOfBounds_ThrowsError` | `JsonArray.GetTime(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/195-xmldocument/test/XmlDocTest.al:63` | CU100201 / `XmlDoc_ReadFrom_InvalidXml_Throws` | `XmlDocument.ReadFrom('not xml at all <<<')` — parse error | `''` | `'not xml'` or `'invalid'` |
| `tests/bucket-2/data-formats/316-jsonarray-gettext-integer-index/test/JsonArrayIndexedTest.al:57` | CU316101 / `GetTextAtIndex_OutOfBounds` | `JsonArray.GetText(5)` on 4-element array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/316-jsonarray-gettext-integer-index/test/JsonArrayIndexedTest.al:85` | CU316101 / `GetIntegerAtIndex_OutOfBounds` | `JsonArray.GetInteger(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/316-jsonarray-gettext-integer-index/test/JsonArrayIndexedTest.al:110` | CU316101 / `GetDecimalAtIndex_OutOfBounds` | `JsonArray.GetDecimal(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/316-jsonarray-gettext-integer-index/test/JsonArrayIndexedTest.al:136` | CU316101 / `GetBooleanAtIndex_OutOfBounds` | `JsonArray.GetBoolean(0)` on empty array | `''` | `'out of range'` or `'bounds'` |
| `tests/bucket-2/data-formats/316-jsonarray-gettext-integer-index/test/JsonArrayIndexedTest.al:165` | CU316101 / `GetArrayAtIndex_OutOfBounds` | `JsonArray.GetArray(0)` on empty array | `''` | `'out of range'` or `'bounds'` |

### page-report bucket-2

| File:Line | CU / Procedure | `asserterror` target | Current expectation | Suggested fix |
|---|---|---|---|---|
| `tests/bucket-2/page-report/262-tooltip-control-property/test/TooltipTest.al:66` | CU80951 / `TooltipControl_GetNonExistent_RaisesError` | `Rec.Get(9999)` — record not found | `''` | `'does not exist'` or `'9999'` |

### bucket-1 (codeunit-runtime, record-table)

| File:Line | CU / Procedure | `asserterror` target | Current expectation | Suggested fix |
|---|---|---|---|---|
| `tests/bucket-1/codeunit-runtime/101-enum-frominteger/test/TestEnumFromInteger.al:65` | CU50032 / `FromInteger_OutOfRange_RaisesError` | `Enum.FromInteger(99)` — value 99 out of range | `''` | `'99'` or `'not a valid'` |
| `tests/bucket-1/record-table/180-table-metadata-stubs/test/TableMetaTest.al:88` | CU107001 / `FieldError_ThrowsError` | `Record.FieldError()` — no message provided | `''` | `'must have a value'` or field caption |
| `tests/bucket-1/record-table/22-record-persistence/test/PersistenceTest.al:54` | CU50922 / `TestGetNonExistentRecordFails` | `Helper.GetDescription(99)` → `Record.Get(99)` — not found | `''` | `'does not exist'` |
| `tests/bucket-1/record-table/63-record-getposition-setposition/test/TestRecordGetSetPosition.al:93` | CU63401 / `SetPosition_InvalidString_RaisesError` | `Record.SetPosition('totally-invalid-position-string')` | `''` | `'invalid'` or `'position'` |

### Excluded tests (informational only — not in main corpus)

| File:Line | CU / Procedure | `asserterror` target | Current expectation | Note |
|---|---|---|---|---|
| `tests/excluded/codeunit-runtime/86-testfield-methods/test/TestFieldMethodsTest.al:294` | CU50368 / `AsDateTime_NonDateTimeField_RaisesError` | `TestPage.NameField.AsDateTime()` — wrong field type | `''` | Excluded; same fix applies when un-excluded |

### Special case — meta-test documenting non-BC behavior

| File:Line | CU / Procedure | Issue |
|---|---|---|
| `tests/bucket-1/codeunit-runtime/21-expected-error-substring/test/ExpectedErrorSubstringTest.al:59` | CU50158 / `TestEmptyExpectedErrorPasses` | This test **intentionally asserts that `Assert.ExpectedError('')` is a wildcard**. Per BC's documented behavior, `Codeunit 130000.ExpectedError` does NOT treat `''` as a wildcard. Fix depends on decision: either document the runner divergence or align the runner's Assert to BC's semantics and delete this test. |

---

## Pattern 2 — TextBuilder.Insert/Remove index-semantics candidates (~5–6)

**Context**: `TextBuilder.Insert(pos, text)` and `TextBuilder.Remove(startPos, count)` wrap .NET `StringBuilder`
which is **0-based**. All assertions below use 0-based positions. If BC's AL TextBuilder is actually 1-based
(per AL convention), all five assertions are off by one. The `~6` count in PATH-FORWARD aligns with this set.

Source helper: `tests/bucket-2/data-formats/271-textbuilder-methods/src/TbmSrc.al`
Test file: `tests/bucket-2/data-formats/271-textbuilder-methods/test/TbmTest.al` (CU84406)

| File:Line | CU / Procedure | Asserterror target | Current expectation | Suggested fix (if 1-based) |
|---|---|---|---|---|
| `TbmTest.al:16` | CU84406 / `Insert_AtMiddle_ProducesExpectedString` | `InsertAtPosition('Hello', 5, ', World')` — pos=5 | `'Hello, World'` | If 1-based: pos=6 → `'Hello, World'`; or expected result becomes `'Hell, Worldo'` |
| `TbmTest.al:24` | CU84406 / `Insert_AtZero_PrependsText` | `InsertAtPosition('there', 0, 'Hi ')` — pos=0 | `'Hi there'` | If 1-based: pos=1 for prepend; pos=0 may throw |
| `TbmTest.al:32` | CU84406 / `Insert_AtEnd_AppendsText` | `InsertAtPosition('Hello', 5, '!')` — pos=5 | `'Hello!'` | If 1-based: pos=6 for append; pos=5 → `'Hell!o'` |
| `TbmTest.al:40` | CU84406 / `Remove_MiddleRange_ProducesExpectedString` | `RemoveRange('Hello, World', 5, 7)` — startPos=5 | `'Hello'` | If 1-based: Remove(6, 7); Remove(5,7) → `'Hell'` |
| `TbmTest.al:48` | CU84406 / `Remove_FromStart_ProducesExpectedString` | `RemoveRange('Hello World', 0, 6)` — startPos=0 | `'World'` | If 1-based: Remove(1, 6); pos=0 may throw |

**Resolution needed**: Confirm BC TextBuilder indexing semantics (0-based vs 1-based) against actual BC runtime or
official docs. If 0-based (likely, given .NET StringBuilder wrapping), all 5 assertions are correct and PATH-FORWARD
may have been referring to an earlier version of these tests.

---

## Pattern 3 — PadStr parameter-range assertions

**Finding**: No `asserterror`/`ExpectedError` assertions around `PadStr` found in any active test file.
The PadStr tests in `tests/bucket-2/data-formats/67-text-builtins/test/TextBuiltinsTest.al` (CU50123) all use
positive `AreEqual` assertions and look correct against BC documentation.

The PATH-FORWARD estimate of ~3 PadStr issues may refer to tests that were already fixed, or to tests yet to be
written for boundary cases (e.g., `PadStr('', -5, ' ')`, `PadStr('ABC', 0, ' ')`).

**Candidates for closer inspection** (look correct but involve negative length — unusual):

| File:Line | CU / Procedure | Call | Assertion | Concern |
|---|---|---|---|---|
| `tests/bucket-2/data-formats/67-text-builtins/test/TextBuiltinsTest.al:76` | `TestPadStrLeftFills` | `PadStr('Hello', -10, ' ')` | `'     Hello'` | Negative length = left-pad; correct per BC docs |
| `tests/bucket-2/data-formats/67-text-builtins/test/TextBuiltinsTest.al:86` | `TestPadStrWithChar` | `PadStr('Hello', -10, Star)` | `'*****Hello'` | Same — left-pad with explicit char; correct |
| `tests/bucket-2/data-formats/67-text-builtins/test/TextBuiltinsTest.al:93` | `TestPadStrLeftTruncates` | `PadStr('Hello World', -5, ' ')` | `'Hello'` | Truncation with negative length; correct |

---

## Pattern 4 — Bonus findings

### 4a — Meta-test locking in non-BC wildcard behavior

| File:Line | CU / Procedure | Issue |
|---|---|---|
| `tests/bucket-1/codeunit-runtime/21-expected-error-substring/test/ExpectedErrorSubstringTest.al:53–59` | CU50158 / `TestEmptyExpectedErrorPasses` | Procedure explicitly documents and tests that `Assert.ExpectedError('')` passes any error (wildcard). This **contradicts real BC behavior** and effectively rubber-stamps the runner's non-BC divergence. If/when the runner's Assert is aligned to BC, this test will need to be deleted or rewritten. |

### 4b — Unconditional `Assert.IsTrue(true, ...)` (crash-safety tests)

Many tests use `Assert.IsTrue(true, 'must not throw')` as their only assertion. Per the code review checklist
these are only acceptable when the test IS specifically about crash safety (not silently swallowing value verification).
The following are correctly labeled crash-safety tests and are NOT bugs:

- `tests/bucket-1/codeunit-runtime/153-sleep/test/SleepTest.al` (Sleep must not crash)
- `tests/bucket-1/codeunit-runtime/161-dialog-methods/test/DialogMethodsTest.al` (HideSubsequentDialogs, LogInternalError)
- `tests/bucket-1/codeunit-runtime/121-utility-stubs/test/UtilTests.al` (Session.LogMessage, Database.LockTimeout)
- (many others with "must not throw" label)

These are **correct**; no fix needed.

### 4c — Hardcoded date in RoundDateTime test

| File:Line | CU / Procedure | Concern | Verdict |
|---|---|---|---|
| `tests/bucket-1/codeunit-runtime/178-system-env-stubs/test/SystemEnvTest.al:66–68` | `RoundDateTime_RoundsToNearestHour` | Uses hardcoded `20260417D` as an *input* to `RoundDateTime` then asserts the rounded output. NOT a drift issue (date is a fixed input, not `Today()`). | **Not a bug** — the date is deterministic input. |

---

## Summary counts

| Pattern | Active test files | Total `Assert.ExpectedError('')` calls |
|---|---|---|
| P1 — data-formats | 8 files | 25 calls |
| P1 — page-report | 1 file | 1 call |
| P1 — bucket-1 | 4 files | 4 calls |
| P1 — excluded (info only) | 1 file | 1 call |
| **P1 total (active)** | **13 files** | **30 active + 1 excluded** |
| P2 — TextBuilder Insert/Remove | 1 file (TbmTest.al) | 5 candidate assertions |
| P3 — PadStr | 0 (no asserterror) | 0 clear bugs found |
| P4 — Bonus | — | 1 meta-test concern, 1 no-action date |

## Fix batching recommendation

1. **Batch A** (highest value): All 25 data-formats `Assert.ExpectedError('')` calls — determine BC's actual error message
   substrings by running against a real BC instance or reading BC source. ~8 commits (one per test file).
2. **Batch B**: 5 bucket-1 calls (enum, record, persistence, getposition, tablemetastub, page-report).
3. **Batch C**: Resolve TextBuilder indexing question. If 0-based = correct, no fix needed. If 1-based, fix 5 assertions in TbmTest.al.
4. **Batch D**: Decide on `TestEmptyExpectedErrorPasses` after aligning Assert.ExpectedError to BC semantics.
