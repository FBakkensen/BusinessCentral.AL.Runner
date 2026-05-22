codeunit 50278 "File ViewFromStream Bool Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure ViewFromStream_ReturnsFalse()
    var
        Helper: Codeunit "File ViewFromStream Bool Src";
    begin
        // In BC, File.ViewFromStream returns false when there is no UI to display the content.
        Assert.AreEqual(false, Helper.ViewFromStreamInIf(),
            'File.ViewFromStream (2-arg) must return false in test context (no UI)');
        Assert.AreEqual(false, Helper.ViewFromStreamEditableInIf(),
            'File.ViewFromStream (3-arg) must return false in test context (no UI)');
    end;

    [Test]
    procedure ViewFromStream_TrueExpectationFails()
    var
        Helper: Codeunit "File ViewFromStream Bool Src";
    begin
        // Negative trap: expecting true should fail since ViewFromStream returns false.
        asserterror Assert.AreEqual(true, Helper.ViewFromStreamInIf(),
            'ViewFromStream should not return true in test context');
        Assert.ExpectedError('AreEqual');
    end;
}
