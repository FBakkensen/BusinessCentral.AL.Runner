codeunit 50270 "HC UseDefaultNetwork Bool Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

#if ONPREM
    [Test]
    procedure UseDefaultNetwork_ReturnsTrue()
    var
        Helper: Codeunit "HC UseDefaultNetwork Bool Src";
    begin
        Assert.AreEqual(true, Helper.UseDefaultNetworkInIf(),
            'UseDefaultNetworkWindowsAuthentication must return true in bool context');
    end;

    [Test]
    procedure UseDefaultNetwork_WrongExpectationFails()
    var
        Helper: Codeunit "HC UseDefaultNetwork Bool Src";
    begin
        asserterror Assert.AreEqual(false, Helper.UseDefaultNetworkInIf(),
            'UseDefaultNetworkWindowsAuthentication should not return false');
        Assert.ExpectedError('AreEqual');
    end;
#endif
}
