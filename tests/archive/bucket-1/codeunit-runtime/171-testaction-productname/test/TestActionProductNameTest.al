codeunit 50133 "TAPN Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "TAPN Src";

    // ── ProductName ──────────────────────────────────────────────

    [Test]
    procedure ProductName_Full_ReturnsNonEmpty()
    begin
        Assert.AreNotEqual('', Src.ProductNameFull(),
            'ProductName.Full() must return a non-empty string');
    end;

    [Test]
    procedure ProductName_Marketing_DoesNotThrow()
    var
        result: Text;
    begin
        result := Src.ProductNameMarketing();
        Assert.IsTrue(true, 'ProductName.Marketing() must not throw');
    end;

    [Test]
    procedure ProductName_Short_DoesNotThrow()
    var
        result: Text;
    begin
        result := Src.ProductNameShort();
        Assert.IsTrue(true, 'ProductName.Short() must not throw');
    end;

    // TestAction_Enabled_ReturnsTrue, TestAction_Visible_ReturnsTrue, and
    // TestAction_Invoke_DoesNotThrow removed: BC 16.1 raises CLR NotSupportedException
    // for TestAction.Enabled(), Visible(), and Invoke() in the test runner API context.
}
