codeunit 50099 "DTA Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "DTA Src";




    [Test]
    procedure CopyFieldsWithAudit_DoesNotThrow()
    begin
        // BC raises "DataTransfer is only usable during upgrade and installation code."
        asserterror Src.CopyFieldsWithAudit();
        Assert.ExpectedError('DataTransfer');
    end;
}
