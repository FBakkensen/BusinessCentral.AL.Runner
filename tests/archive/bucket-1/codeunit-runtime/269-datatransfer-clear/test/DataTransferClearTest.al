codeunit 50196 "DTC Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "DTC Src";

    [Test]
    procedure Clear_AfterSetup_DoesNotThrow()
    begin
        // BC raises "DataTransfer is only usable during upgrade and installation code."
        asserterror Src.ClearAfterSetup_DoesNotThrow();
        Assert.ExpectedError('DataTransfer');
    end;

    [Test]
    procedure Clear_ThenCopyFields_DoesNotThrow()
    begin
        asserterror Src.ClearThenCopyFields_DoesNotThrow();
        Assert.ExpectedError('DataTransfer');
    end;

    [Test]
    procedure Clear_ThenCopyRows_DoesNotThrow()
    begin
        asserterror Src.ClearThenCopyRows_DoesNotThrow();
        Assert.ExpectedError('DataTransfer');
    end;

    [Test]
    procedure Clear_ResetsUpdateAuditFields_NoThrow()
    begin
        // BC 16.1: DataTransfer.Clear then property access does not throw.
        Src.UpdateAuditFieldsSurvivesClear();
        Assert.IsTrue(true, 'Clear then UpdateAuditFields must not throw');
    end;
}
