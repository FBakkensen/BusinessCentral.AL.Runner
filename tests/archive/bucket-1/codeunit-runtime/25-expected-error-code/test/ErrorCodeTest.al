codeunit 50184 ErrorCodeTest
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure TestExpectedErrorCode_DialogError()
    var
        Producer: Codeunit ErrorProducer;
    begin
        // [SCENARIO] ExpectedErrorCode('Dialog') verifies that an Error() was raised.
        // [GIVEN] A codeunit that calls Error().
        // [WHEN] We wrap it in asserterror.
        asserterror Producer.RaiseDialogError();

        // [THEN] ExpectedErrorCode with 'Dialog' passes because an error occurred.
        Assert.ExpectedErrorCode('Dialog');
    end;

    // TestExpectedErrorCode_NoError_Fails removed: BC 16.1 raises "no error occurred
    // inside asserterror" immediately when asserterror wraps a non-throwing call,
    // so the nested asserterror pattern is not portable across environments.
}
