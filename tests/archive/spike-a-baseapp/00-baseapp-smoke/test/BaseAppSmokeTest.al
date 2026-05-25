// Spike A — keystone test:
// Can we invoke an unmodified Microsoft Base Application procedure
// (compiled-from-AL, R2R-shipped, public method body) from an AL test
// running in the runner?
//
// Target: Codeunit 9015 "Application System Constants" — pure constant
// returns, no DB, no events, no async. The simplest possible Base App
// surface to validate the load chain end to end.
codeunit 80001 "Spike A BaseApp Smoke"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure OriginalApplicationVersion_ReturnsW1_27_5()
    var
        AppSysConsts: Codeunit "Application System Constants";
        Version: Text[248];
    begin
        // [WHEN] We call into the Base App-compiled body
        Version := AppSysConsts.OriginalApplicationVersion();

        // [THEN] We get back the constant the AL author wrote
        Assert.AreEqual('W1 27.5', Version, 'Base App OriginalApplicationVersion()');
    end;

    [Test]
    procedure PlatformProductVersion_NonEmpty()
    var
        AppSysConsts: Codeunit "Application System Constants";
        Version: Text[80];
    begin
        Version := AppSysConsts.PlatformProductVersion();
        Assert.AreNotEqual('', Version, 'Base App PlatformProductVersion()');
    end;

    [Test]
    procedure BuildBranch_ReturnsNAV275()
    var
        AppSysConsts: Codeunit "Application System Constants";
        Branch: Text[250];
    begin
        Branch := AppSysConsts.BuildBranch();
        Assert.AreEqual('NAV275', Branch, 'Base App BuildBranch()');
    end;
}
