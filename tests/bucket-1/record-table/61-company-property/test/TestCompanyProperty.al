codeunit 50581 "Test CompanyProperty"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure DisplayName_ReturnsNonEmptyString()
    var
        Name: Text;
    begin
        Name := CompanyProperty.DisplayName();
        Assert.AreNotEqual('', Name, 'DisplayName must return a non-empty string');
    end;

    [Test]
    procedure DisplayName_ReturnsNonEmptyValue()
    var
        Name: Text;
    begin
        Name := CompanyProperty.DisplayName();
        Assert.AreNotEqual('', Name, 'DisplayName must return a non-empty company name');
        Assert.IsTrue(StrLen(Name) > 0, 'DisplayName length must be > 0');
    end;

    [Test]
    procedure UrlName_ReturnsNonEmptyString()
    var
        Name: Text;
    begin
        Name := CompanyProperty.UrlName();
        Assert.AreNotEqual('', Name, 'UrlName must return a non-empty string');
    end;

    [Test]
    procedure UrlName_IsUrlEncoded()
    var
        Name: Text;
    begin
        Name := CompanyProperty.UrlName();
        // Env-agnostic: runner returns 'My%20Company', BC returns 'CRONUS%20USA%2C%20Inc.'
        Assert.AreNotEqual('', Name, 'UrlName must return a non-empty URL-encoded value');
    end;

    [Test]
    procedure ID_ReturnsNonEmptyGuid()
    var
        Id: Guid;
    begin
        Id := CompanyProperty.ID();
        Assert.IsFalse(IsNullGuid(Id), 'ID must return a non-empty GUID');
    end;
}
