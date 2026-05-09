// Spike A2 — keystone stretch: do non-trivial Base App procedures still
// run unmodified through the runner?
//
// Spike A proved trivial constant-returning procedures work
// (Codeunit "Application System Constants"). This bucket pushes on
// four shapes that matter for v2's mission:
//
//   1. Cross-chunk + intra-codeunit dispatch (TypeHelper.GetOptionNo)
//   2. Record access against a Base App table (Record "Currency".Init)
//   3. Error raising from a Base App body (TypeHelper raises via Error())
//   4. Loop / control flow (TypeHelper.GetOptionNo iterates internally)
//
// Each test uses a precompiled-AL Base App procedure body verbatim — no
// Base App modification. If a test fails the failure is in our runtime
// engine, never in Base App.
codeunit 80002 "Spike A2 BaseApp Stretch"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // Shape 1 — cross-chunk + intra-codeunit dispatch.
    //
    // Codeunit 10 "Type Helper" (Base App chunk 5) is invoked from our
    // freshly-emitted test DLL. Inside, GetOptionNo iterates by calling
    // GetNumberOfOptions, OptionsAreEqual, plus AL built-ins UpperCase,
    // StrPos, CopyStr, DelStr — exercising real intra-Base-App dispatch.
    //
    // Input:   ' ,Foo,Bar,Baz', value 'Bar' → option ordinal 2.
    [Test]
    procedure TypeHelper_GetOptionNo_ResolvesBar_To2()
    var
        TypeHelper: Codeunit "Type Helper";
        OptionNo: Integer;
    begin
        OptionNo := TypeHelper.GetOptionNo('Bar', ' ,Foo,Bar,Baz');
        Assert.AreEqual(2, OptionNo, 'Type Helper GetOptionNo Bar');
    end;

    // Shape 1 negative: missing value returns -1. Strong assertion: would
    // not pass if the runner returned default(Integer)=0.
    [Test]
    procedure TypeHelper_GetOptionNo_MissingValue_ReturnsMinusOne()
    var
        TypeHelper: Codeunit "Type Helper";
        OptionNo: Integer;
    begin
        OptionNo := TypeHelper.GetOptionNo('Quux', ' ,Foo,Bar,Baz');
        Assert.AreEqual(-1, OptionNo, 'Type Helper GetOptionNo missing returns -1');
    end;

    // Shape 4 — pure control flow (loop+arithmetic). GetHMSFromTime
    // unpacks a Time into Hour/Minute/Second by repeated div/mod. Runs
    // entirely in the Base App body, no helpers. By-ref outputs.
    [Test]
    procedure TypeHelper_GetHMSFromTime_091530T_Decomposes()
    var
        TypeHelper: Codeunit "Type Helper";
        H: Integer;
        M: Integer;
        S: Integer;
    begin
        TypeHelper.GetHMSFromTime(H, M, S, 091530T);
        Assert.AreEqual(9, H, 'GetHMSFromTime hour');
        Assert.AreEqual(15, M, 'GetHMSFromTime minute');
        Assert.AreEqual(30, S, 'GetHMSFromTime second');
    end;

    // Shape 2 — Record access against a Base App-defined table.
    //
    // Record "Currency" is table 4, defined in Base App's compiled
    // metadata (not in this test suite's src/). The NCLMetaTable
    // populator's BcAppFallback path
    // (RecordPatches.BcAppFallback.cs) extracts the AL source
    // shipped inside the Base App `.app` NAVX zip and feeds it
    // through the same parser that consumes test src/, so the
    // resulting NCLMetaTable is indistinguishable from one built
    // from the test's own AL.
    //
    // After Init():
    //   - "Invoice Rounding Precision" (decimal field 86) defaults
    //     to 0, the AL Record-level Init contract for unset numeric
    //     fields. Asserting on the exact 0 catches any populator
    //     change that inadvertently leaves the slot uninitialised.
    [Test]
    procedure Currency_Init_LeavesNumericFieldsZero()
    var
        Currency: Record Currency;
    begin
        Currency.Init();
        Assert.AreEqual(
            0, Currency."Invoice Rounding Precision",
            'Currency.Init should leave Invoice Rounding Precision at 0');
        Assert.AreEqual(
            '', Currency.Code,
            'Currency.Init should leave PK Code at empty string');
    end;

    // Shape 3 — Error raising. GLN Calculator.IsValidCheckDigit13 calls
    // the local IsValidCheckDigit which calls Error() with a length
    // mismatch when the GLN length is wrong. We pass a 5-char GLN to
    // force the length-error path, asserterror it, then assert the
    // error text contains the expected fragment.
    //
    // This crosses test-DLL → Base App body → NCL.dll Error() →
    // back to AL test asserterror handler — the full exception channel.
    [Test]
    procedure GLNCalculator_BadLength_RaisesLengthError()
    var
        GLNCalc: Codeunit "GLN Calculator";
    begin
        asserterror GLNCalc.IsValidCheckDigit13('12345');
        Assert.ExpectedError('GLN length should be');
    end;
}
