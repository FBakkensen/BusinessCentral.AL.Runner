codeunit 50600 "Company Name Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Helper: Codeunit "Company Name Helper";

    [Test]
    procedure CompanyNameReturnsText()
    var
        Result: Text;
    begin
        // Positive: CompanyName should return a text value without crashing
        Result := Helper.GetCompanyName();
        // Env-agnostic: runner returns 'CRONUS', BC returns 'CRONUS USA, Inc.'.
        // The contract is that CompanyName returns a non-empty value without throwing.
        Assert.AreNotEqual('', Result, 'CompanyName must return a non-empty value');
    end;

    [Test]
    procedure UserIdReturnsText()
    var
        Result: Text;
    begin
        // Positive: UserId should return a non-empty text value on any environment.
        // Runner returns 'TESTUSER'; real BC returns the logged-in user's ID.
        Result := Helper.GetUserId();
        Assert.AreNotEqual('', Result, 'UserId must return a non-empty string');
    end;

    [Test]
    procedure CompanyNameReturnsNonEmpty()
    begin
        // Positive: CompanyName must return a non-empty stub value in standalone mode
        Assert.AreNotEqual('', Helper.GetCompanyName(), 'CompanyName must return a non-empty string in standalone mode');
    end;
}
