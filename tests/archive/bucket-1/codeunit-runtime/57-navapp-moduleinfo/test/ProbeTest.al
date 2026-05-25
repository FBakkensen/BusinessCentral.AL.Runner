codeunit 50330 "NA Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure GetModuleInfoDoesNotCrash()
    var
        Probe: Codeunit "NA Probe";
        Result: Text;
    begin
        // [GIVEN] A GUID passed to NavApp.GetModuleInfo
        // [THEN] Must not throw; BC returns a module name (e.g. 'W1') or '<unknown>'
        Result := Probe.TryUnknown();
        Assert.IsTrue(Result <> '', 'GetModuleInfo must not crash and must return a non-empty result');
    end;

    [Test]
    procedure DefaultModuleInfoNameIsEmpty()
    var
        Probe: Codeunit "NA Probe";
    begin
        // A default ModuleInfo instance must have a readable Name (empty by default)
        Assert.AreEqual('', Probe.ReadsNamePropertyWhenMissing(), 'Default ModuleInfo.Name is empty string');
    end;
}
