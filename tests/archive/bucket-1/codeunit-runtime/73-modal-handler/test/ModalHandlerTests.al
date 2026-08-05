codeunit 50344 "Modal Handler Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;





    [Test]
    procedure TestModalNoHandler()
    var
        Opener: Codeunit "Modal Opener";
    begin
        // [GIVEN] No handler registered
        // [WHEN] Code opens a modal page
        // [THEN] Should fail with descriptive error
        asserterror Opener.OpenModalAndGetResult();
        Assert.ExpectedError('Unhandled UI');
    end;
}
