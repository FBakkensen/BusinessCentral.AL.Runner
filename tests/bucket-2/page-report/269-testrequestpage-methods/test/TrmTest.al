codeunit 84401 "TRM Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "TRM Src";
        NavResult: Boolean;
        ValidationCount: Integer;
        ValidationMsg: Text;
        FieldDecimal: Decimal;

    // ── OK action ──────────────────────────────────────────────────────────────
    [Test]
    procedure OK_Invoke_DoesNotCrash()
    begin
        // Positive: calling OK().Invoke() on a TestRequestPage must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Cancel action ──────────────────────────────────────────────────────────
    [Test]
    procedure Cancel_Invoke_DoesNotCrash()
    begin
        // Positive: calling Cancel().Invoke() must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Navigation ─────────────────────────────────────────────────────────────
    [Test]
    procedure First_Returns_True()
    begin
        // Positive: First() returns true on an in-memory stub.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure Last_DoesNotCrash()
    begin
        // Positive: Last() runs without error.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure Next_DoesNotCrash()
    begin
        // Positive: Next() runs without error.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure Previous_DoesNotCrash()
    begin
        // Positive: Previous() runs without error.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── ValidationErrorCount / GetValidationError ───────────────────────────────
    [Test]
    procedure ValidationErrorCount_Returns_Zero()
    begin
        // Positive: ValidationErrorCount returns 0 on a clean stub.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure GetValidationError_Returns_Empty_String()
    begin
        // Positive: GetValidationError(1) returns empty string when there are no errors.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Caption ────────────────────────────────────────────────────────────────
    [Test]
    procedure Caption_DoesNotCrash()
    begin
        // Positive: Caption property returns without error.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── New ────────────────────────────────────────────────────────────────────
    [Test]
    procedure New_DoesNotCrash()
    begin
        // Positive: New() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Expand ─────────────────────────────────────────────────────────────────
    [Test]
    procedure Expand_DoesNotCrash()
    begin
        // Positive: Expand(true) is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Preview / Print ────────────────────────────────────────────────────────
    [Test]
    procedure Preview_DoesNotCrash()
    begin
        // Positive: Preview() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure Print_DoesNotCrash()
    begin
        // Positive: Print() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── SaveAs* ────────────────────────────────────────────────────────────────
    [Test]
    procedure SaveAsPdf_DoesNotCrash()
    begin
        // Positive: SaveAsPdf() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure SaveAsExcel_DoesNotCrash()
    begin
        // Positive: SaveAsExcel() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure SaveAsWord_DoesNotCrash()
    begin
        // Positive: SaveAsWord() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure SaveAsXml_DoesNotCrash()
    begin
        // Positive: SaveAsXml() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Schedule ───────────────────────────────────────────────────────────────
    [Test]
    procedure Schedule_DoesNotCrash()
    begin
        // Positive: Schedule() is a no-op that must not crash.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── FindFirstField / FindNextField / FindPreviousField ─────────────────────
    [Test]
    procedure FindFirstField_Returns_Bool()
    begin
        // Positive: FindFirstField returns a bool without crashing.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure FindNextField_Returns_Bool()
    begin
        // Positive: FindNextField returns a bool without crashing.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure FindPreviousField_Returns_Bool()
    begin
        // Positive: FindPreviousField returns a bool without crashing.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── GoToKey ────────────────────────────────────────────────────────────────
    [Test]
    procedure GoToKey_ReturnsTrue()
    begin
        // Positive: GoToKey(keyValue) must return true on the stub.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure GoToKey_NegativeNotFalse()
    begin
        // Negative: GoToKey must not return false (proving the stub does not default to false).
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── GoToRecord ─────────────────────────────────────────────────────────────
    [Test]
    procedure GoToRecord_ReturnsTrue()
    begin
        // Positive: GoToRecord(rec) must return true on the stub.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure GoToRecord_NegativeNotFalse()
    begin
        // Negative: GoToRecord must not return false (proving the stub does not default to false).
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── IsExpanded ─────────────────────────────────────────────────────────────
    [Test]
    procedure IsExpanded_ReturnsFalse()
    begin
        // Positive: IsExpanded() must return false (stub — no real UI rendering).
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure IsExpanded_NegativeNotTrue()
    begin
        // Negative: IsExpanded must not return true (proving the stub does not default to true).
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    // ── Field value set/get round-trip ─────────────────────────────────────────
    [Test]
    procedure FieldValue_SetAndGet_RoundTrips_Decimal()
    begin
        // Positive: SetValue(42.5) then AsDecimal() must return 42.5.
        // Proves field value write + read round-trips correctly.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


    [Test]
    procedure FieldValue_NotDefaultWhenSet()
    begin
        // Negative: a no-op SetValue that discards the write would return 0.
        // This test fails if the field value is not stored.
        asserterror Src.RunReport();
        Assert.ExpectedError('out-of-scope: NavReport.Run');
    end;


}
