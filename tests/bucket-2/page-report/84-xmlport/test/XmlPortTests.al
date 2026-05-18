// Renumbered from 58401 to avoid collision in new bucket layout (#1385).
codeunit 1058401 "XmlPort Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Logic: Codeunit "XmlPort Logic";

    // ------------------------------------------------------------------
    // Positive: codeunit with XmlPort variables compiles and business
    // logic that does not call Import/Export runs correctly.
    // ------------------------------------------------------------------

    [Test]
    procedure XmlPortDeclarationCompiles()
    begin
        // [GIVEN] A codeunit that declares an XmlPort variable
        // [WHEN]  We call a procedure that doesn't invoke Import/Export
        // [THEN]  It returns the expected value — proves compilation succeeded
        Assert.AreEqual('ready', Logic.GetStatus(), 'GetStatus should return ready');
    end;

    [Test]
    procedure XmlPortIdConstantCorrect()
    begin
        // [GIVEN] A procedure that returns the XmlPort object ID
        // [WHEN]  We call it
        // [THEN]  The ID matches the XmlPort definition
        Assert.AreEqual(58400, Logic.GetXmlPortId(), 'XmlPort ID should be 58400');
    end;

    // ------------------------------------------------------------------
    // No-op: Import/Export are stubs in standalone mode (require BC service tier).
    // They return without error — inject via AL interface for unit-testable I/O.
    // ------------------------------------------------------------------

    [Test]
    procedure InstanceImport_ThrowsOutOfScope()
    var
        InStr: InStream;
    begin
        // [GIVEN] An uninitialized InStream
        // [WHEN]  We call the instance form XP.Import()
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryInstanceImport(InStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.Import');
    end;

    [Test]
    procedure InstanceExport_ThrowsOutOfScope()
    var
        OutStr: OutStream;
    begin
        // [GIVEN] An uninitialized OutStream
        // [WHEN]  We call the instance form XP.Export()
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryInstanceExport(OutStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.Export');
    end;

    [Test]
    procedure StaticImport_ThrowsOutOfScope()
    var
        InStr: InStream;
    begin
        // [GIVEN] An uninitialized InStream
        // [WHEN]  We call the static form XmlPort.Import(portId, InStr)
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryStaticImport(InStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.StaticImport');
    end;

    [Test]
    procedure StaticExport_ThrowsOutOfScope()
    var
        OutStr: OutStream;
    begin
        // [GIVEN] An uninitialized OutStream
        // [WHEN]  We call the static form XmlPort.Export(portId, OutStr)
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryStaticExport(OutStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.StaticExport');
    end;
}
