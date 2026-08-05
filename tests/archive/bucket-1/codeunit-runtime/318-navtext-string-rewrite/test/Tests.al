// Test suite for issue #1528 — CS0029 string→NavText rewriter fix.
//
// The RoslynRewriter was replacing user-defined ToText() calls with
// AlCompat.Format(receiver) which returns C# string. When BC scope classes
// call _parent.ToText(this) (passing the scope as first arg), the rewriter
// treated it as a BC runtime session call and wrongly replaced it.
// After the fix, bare-`this` first-arg calls are excluded from the replacement.
codeunit 50257 "NavText Rewrite Tests"
{
    Subtype = Test;

    var Assert: Codeunit Assert;

    // Positive: user-defined ToText() called via GetTextViaToText() must
    // return the exact string — proves the rewriter did NOT replace it with
    // AlCompat.Format (which would return the codeunit's object representation,
    // not 'hello-from-totext').
    [Test]
    procedure UserToText_ReturnsDeclaredValue()
    var
        Src: Codeunit "NavText Rewrite Source";
    begin
        Assert.AreEqual('hello-from-totext', Src.GetTextViaToText(),
            'GetTextViaToText must return the value from the user ToText() procedure');
    end;

    // Negative: direct call to ToText() also returns the declared value.
    [Test]
    procedure UserToText_DirectCall_ReturnsDeclaredValue()
    var
        Src: Codeunit "NavText Rewrite Source";
    begin
        Assert.AreEqual('hello-from-totext', Src.ToText(),
            'Direct call to ToText() must return declared value');
    end;

    // Positive: TenantId() returns a non-empty string.
    // Runner returns 'STANDALONE'; real BC returns a real tenant ID.
    [Test]
    procedure TenantId_ReturnsNonEmptyString()
    var
        Src: Codeunit "NavText Rewrite Source";
    begin
        Assert.AreNotEqual('', Src.GetTenantId(),
            'TenantId() must return a non-empty string');
    end;

    // Positive: SerialNumber() returns a non-empty string.
    // Runner returns 'STANDALONE'; real BC returns the installation serial number.
    [Test]
    procedure SerialNumber_ReturnsNonEmptyString()
    var
        Src: Codeunit "NavText Rewrite Source";
    begin
        Assert.AreNotEqual('', Src.GetSerialNumber(),
            'SerialNumber() must return a non-empty string');
    end;

    // Positive: Callstack() must not throw.
    [Test]
    procedure CallStack_DoesNotThrow()
    var
        Src: Codeunit "NavText Rewrite Source";
        Cs: Text;
    begin
        Cs := Src.GetCallStack();
        Assert.IsTrue(true, 'CallStack must not throw');
    end;

    // Positive: ApplicationIdentifier() returns a Text value without crashing.
    // Runner returns ''; real BC returns 'FIN' or similar.
    [Test]
    procedure ApplicationIdentifier_ReturnsText()
    var
        Src: Codeunit "NavText Rewrite Source";
    begin
        // Just verify it doesn't crash; actual value is env-dependent.
        Src.GetApplicationIdentifier();
        Assert.IsTrue(true, 'ApplicationIdentifier must not throw');
    end;
}
