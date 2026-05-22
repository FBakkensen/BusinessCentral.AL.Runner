codeunit 50555 "Test UserId"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure UserIdReturnsNonEmptyText()
    var
        Id: Text;
    begin
        // Positive: UserId() must return a non-empty string on any environment.
        // Runner returns 'TESTUSER'; real BC returns the logged-in user's ID.
        Id := UserId();
        Assert.AreNotEqual('', Id, 'UserId() must return a non-empty string');
    end;
}
