codeunit 50371 "Global Language Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure GlobalLanguageReturnsPositiveInteger()
    var
        Api: Codeunit "Global Language Api";
        Lang: Integer;
    begin
        // Positive: GlobalLanguage() must return a positive integer without crashing.
        Lang := Api.GetCurrentLanguage();
        Assert.IsTrue(Lang > 0, 'GlobalLanguage() must return a positive integer');
    end;

    [Test]
    procedure GlobalLanguageDefaultIsPositive()
    var
        Api: Codeunit "Global Language Api";
        Lang: Integer;
    begin
        // Positive: Default language must be a valid positive integer.
        // Env-agnostic: runner defaults to 1033 (ENU), BC sandbox may use any locale.
        Lang := Api.GetCurrentLanguage();
        Assert.IsTrue(Lang > 0, 'Default GlobalLanguage must be a positive integer');
    end;

    [Test]
    procedure GlobalLanguageSaveSetRestore()
    var
        Api: Codeunit "Global Language Api";
        Original: Integer;
        Result: Integer;
    begin
        // Positive: Save/set/restore round-trip must return the original value.
        Original := Api.GetCurrentLanguage();
        Result := Api.SetAndRestoreLanguage();
        Assert.AreEqual(Original, Result, 'After save/set/restore, GlobalLanguage must equal the original');
    end;

    [Test]
    procedure GlobalLanguageSetAndGetRoundTrip()
    var
        Api: Codeunit "Global Language Api";
        Result: Integer;
    begin
        // Positive: Set to a specific language ID, then get it back.
        Result := Api.GetLanguageAfterSet(1031);
        Assert.AreEqual(1031, Result, 'GlobalLanguage should return 1031 after being set to 1031');
    end;

    [Test]
    procedure GlobalLanguageMustNotBeZero()
    var
        Api: Codeunit "Global Language Api";
        Lang: Integer;
    begin
        // Negative: GlobalLanguage() must NOT return zero — a zero value would indicate
        // the getter is broken (e.g., uninitialized field).
        Lang := Api.GetCurrentLanguage();
        asserterror Assert.AreEqual(0, Lang, 'GlobalLanguage should not be zero');
        Assert.ExpectedError('GlobalLanguage should not be zero');
    end;
}
