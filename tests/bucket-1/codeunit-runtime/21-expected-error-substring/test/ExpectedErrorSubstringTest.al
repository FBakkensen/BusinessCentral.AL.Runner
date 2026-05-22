codeunit 50165 "Expected Error Substring Tests"
{
    Subtype = Test;

    var
        ErrorProducer: Codeunit "Error Producer";
        Assert: Codeunit Assert;

    [Test]
    procedure TestSubstringMatch()
    begin
        // [WHEN] An error with a long message is thrown
        asserterror ErrorProducer.RaiseCustomerNoError();

        // [THEN] ExpectedError with a substring should pass
        Assert.ExpectedError('must have a value');
    end;

    [Test]
    procedure TestExactMatch()
    begin
        // [WHEN] An error is thrown
        asserterror ErrorProducer.RaiseCustomerNoError();

        // [THEN] ExpectedError with the exact message should pass
        Assert.ExpectedError('The field Customer No. must have a value');
    end;

    [Test]
    procedure TestSubstringMatchMiddle()
    begin
        // [WHEN] An error with a long message is thrown
        asserterror ErrorProducer.RaiseAmountError();

        // [THEN] ExpectedError with a middle substring should pass
        Assert.ExpectedError('must be greater than 0');
    end;

    [Test]
    procedure TestWrongSubstringFails()
    begin
        // [WHEN] An error is thrown
        asserterror ErrorProducer.RaiseCustomerNoError();

        // [THEN] ExpectedError with wrong substring should fail
        asserterror Assert.ExpectedError('completely wrong message');

        // [THEN] The assertion failure itself should be caught
        Assert.ExpectedError('Assert.ExpectedError failed');
    end;

    [Test]
    procedure TestEmptyExpectedErrorFails()
    begin
        // [GIVEN] A real error has been raised
        asserterror ErrorProducer.RaiseCustomerNoError();

        // [WHEN] ExpectedError is called with an empty expected string
        // [THEN] It must fail — BC's LibraryAssert uses StrPos(actual, expected)
        //       to test for substring containment, and StrPos(<any>, '') = 0
        //       (empty string is never "found"). So empty expected ≠ "match any".
        asserterror Assert.ExpectedError('');

        // [THEN] The assertion failure itself surfaces with the standard format.
        Assert.ExpectedError('Assert.ExpectedError failed');
    end;
}
