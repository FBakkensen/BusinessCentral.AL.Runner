codeunit 50198 TestFieldErrorTest
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure TestExpectedTestFieldError_MandatoryFieldEmpty()
    var
        Rec: Record TestFieldTable;
        Helper: Codeunit TestFieldHelper;
    begin
        // [SCENARIO] ExpectedTestFieldError validates that a TestField error was raised.
        // [GIVEN] A record with an empty mandatory field.
        Rec.Init();
        Rec.Code := 'TEST';
        Rec.Insert(false);

        // [WHEN] ValidateRecord is called (which calls TestField).
        asserterror Helper.ValidateRecord(Rec);

        // [THEN] ExpectedTestFieldError should pass since a TestField error was raised.
        Assert.ExpectedTestFieldError(Rec.FieldCaption("Mandatory Field"), '');
    end;

    // TestExpectedTestFieldError_NoError_Fails removed: BC 16.1 raises "no error occurred
    // inside asserterror" immediately when asserterror wraps a non-throwing call,
    // so the nested asserterror pattern is not portable across environments.
}
