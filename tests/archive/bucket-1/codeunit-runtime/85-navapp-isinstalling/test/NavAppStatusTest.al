codeunit 50369 "NAS Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "NAS Src";

    // -----------------------------------------------------------------------
    // Positive: NavApp.IsInstalling() must return false in standalone mode —
    // there is no installation lifecycle without a service tier.
    // -----------------------------------------------------------------------

    [Test]
    procedure IsInstalling_ReturnsFalse()
    begin
        // Positive: no installation is in progress in standalone mode.
        Assert.IsFalse(Src.GetIsInstalling(),
            'NavApp.IsInstalling() must return false in standalone mode');
    end;

    // -----------------------------------------------------------------------
    // Positive: NavApp.IsUnlicensed() must return false in standalone mode —
    // no license enforcement is applied without a service tier.
    // -----------------------------------------------------------------------

    [Test]
    procedure IsUnlicensed_ReturnsFalse()
    begin
        // Positive: standalone mode is never considered unlicensed.
        Assert.IsFalse(Src.GetIsUnlicensed(),
            'NavApp.IsUnlicensed() must return false in standalone mode');
    end;

    // -----------------------------------------------------------------------
    // Positive: NavApp.IsEntitled() returns false in BC test context —
    // entitlement is not granted without a proper license check.
    // -----------------------------------------------------------------------

    [Test]
    procedure IsEntitled_ReturnsFalse()
    begin
        // In BC test context, NavApp.IsEntitled returns false (no license check passes).
        Assert.IsFalse(Src.GetIsEntitled('STANDARD'),
            'NavApp.IsEntitled(id) must return false in BC test context');
    end;

    [Test]
    procedure IsEntitled_EmptyId_ReturnsFalse()
    begin
        // In BC test context, NavApp.IsEntitled returns false for any entitlement ID.
        Assert.IsFalse(Src.GetIsEntitled(''),
            'NavApp.IsEntitled with empty id must return false in BC test context');
    end;

    // -----------------------------------------------------------------------
    // Positive: NavApp.IsEntitled() with a string literal argument — this
    // previously failed with CS1503: cannot convert from 'string' to 'NavText'
    // (issue #1231).  The fix adds string overloads so the literal compiles.
    // -----------------------------------------------------------------------

    [Test]
    procedure IsEntitled_StringLiteral_ReturnsFalse()
    begin
        // Positive: direct string literal passed to IsEntitled must compile and not throw.
        // In BC test context, returns false.
        Assert.IsFalse(Src.GetIsEntitledLiteral(),
            'NavApp.IsEntitled with a string literal must return false in BC test context');
    end;

    [Test]
    procedure StatusFlags_AreConsistent()
    var
        IsInstalling: Boolean;
        IsUnlicensed: Boolean;
    begin
        // In BC test context: IsInstalling=false, IsUnlicensed=false, IsEntitled=false.
        IsInstalling := Src.GetIsInstalling();
        IsUnlicensed := Src.GetIsUnlicensed();

        Assert.IsFalse(IsInstalling, 'Must not be installing in test context');
        Assert.IsFalse(IsUnlicensed, 'Must not be unlicensed in test context');
    end;
}
