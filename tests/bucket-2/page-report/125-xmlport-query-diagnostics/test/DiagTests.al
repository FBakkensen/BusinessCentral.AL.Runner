codeunit 56254 "XmlPort Query Diag Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Logic: Codeunit "Diag Logic";

    // ==================================================================
    // XmlPort lifecycle — must NOT throw
    // ==================================================================

    [Test]
    procedure XmlPortDeclarationCompiles()
    begin
        // [GIVEN] A codeunit that declares an XmlPort variable
        // [WHEN]  We call a procedure that doesn't invoke Import/Export
        // [THEN]  It returns the expected value
        Assert.AreEqual('xmlport-ok', Logic.XmlPortDeclareAndReturn(),
            'XmlPort declaration and non-I/O logic should work');
    end;

    [Test]
    procedure XmlPortInvokeDoesNotCrash()
    begin
        // [GIVEN] A codeunit with an XmlPort variable
        // [WHEN]  A custom procedure on the XmlPort is called (exercises Invoke dispatch)
        // [THEN]  No error — MockXmlPortHandle.Invoke returns null silently
        Logic.XmlPortInvoke();
    end;

    // ==================================================================
    // XmlPort I/O — no-ops in standalone mode (require BC service tier)
    // ==================================================================

    [Test]
    procedure XmlPortImport_ThrowsOutOfScope()
    var
        InStr: InStream;
    begin
        // [GIVEN] An XmlPort variable
        // [WHEN]  Import() is called
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryXmlPortImport(InStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.Import');
    end;

    [Test]
    procedure XmlPortExport_ThrowsOutOfScope()
    var
        OutStr: OutStream;
    begin
        // [GIVEN] An XmlPort variable
        // [WHEN]  Export() is called
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryXmlPortExport(OutStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.Export');
    end;

    [Test]
    procedure StaticXmlPortImport_ThrowsOutOfScope()
    var
        InStr: InStream;
    begin
        // [GIVEN] A static XmlPort.Import call
        // [WHEN]  XmlPort.Import(portId, InStr) is called
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryStaticXmlPortImport(InStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.StaticImport');
    end;

    [Test]
    procedure StaticXmlPortExport_ThrowsOutOfScope()
    var
        OutStr: OutStream;
    begin
        // [GIVEN] A static XmlPort.Export call
        // [WHEN]  XmlPort.Export(portId, OutStr) is called
        // [THEN]  RunnerOutOfScopeException — in-memory XmlPort not yet implemented
        asserterror Logic.TryStaticXmlPortExport(OutStr);
        Assert.ExpectedError('out-of-scope: NavXmlPort.StaticExport');
    end;

    // ==================================================================
    // Query lifecycle — must NOT throw
    // ==================================================================

    [Test]
    procedure QueryDeclarationCompiles()
    begin
        // [GIVEN] A codeunit that declares a Query variable
        // [WHEN]  We call a procedure that doesn't invoke Open/Read
        // [THEN]  It returns the expected value
        Assert.AreEqual('query-ok', Logic.QueryDeclareAndReturn(),
            'Query declaration and non-data logic should work');
    end;

    [Test]
    procedure QuerySetRangeAndCloseDoNotThrow()
    begin
        // [GIVEN] A Query variable
        // [WHEN]  SetRange + Close are called
        // [THEN]  No error — both are no-ops
        Logic.QuerySetRangeAndClose();
    end;

    [Test]
    procedure QuerySetFilterAndCloseDoNotThrow()
    begin
        // [GIVEN] A Query variable
        // [WHEN]  SetFilter + TopNumberOfRows + Close are called
        // [THEN]  No error — all are no-ops
        Logic.QuerySetFilterAndClose();
    end;

    // ==================================================================
    // Query: Open now works in-memory; Read without Open still fails.
    // SaveAsCsv/SaveAsXml error messages checked for actionable hints.
    // ==================================================================

    [Test]
    procedure QueryOpenSucceeds()
    begin
        // [GIVEN] A Query variable (single-dataitem, table is empty)
        // [WHEN]  Open() is called
        // [THEN]  No error — Open now works in-memory
        Logic.TryQueryOpen();
    end;

    [Test]
    procedure QueryReadWithoutOpenErrorMentionsInterface()
    begin
        // [GIVEN] A Query variable that has NOT been opened
        // [WHEN]  Read() is called
        // [THEN]  Error message contains "interface"
        asserterror Logic.TryQueryRead();
        Assert.ExpectedMessage('interface', GetLastErrorText);
    end;

    [Test]
    procedure QueryReadWithoutOpenErrorMentionsServiceTier()
    begin
        // [GIVEN] A Query variable that has NOT been opened
        // [WHEN]  Read() is called
        // [THEN]  Error message mentions "BC service tier"
        asserterror Logic.TryQueryRead();
        Assert.ExpectedMessage('BC service tier', GetLastErrorText);
    end;

    [Test]
    procedure QueryReadWithoutOpenErrorMentionsRecord()
    begin
        // [GIVEN] A Query variable that has NOT been opened
        // [WHEN]  Read() is called
        // [THEN]  Error message suggests Record operations as alternative
        asserterror Logic.TryQueryRead();
        Assert.ExpectedMessage('Record', GetLastErrorText);
    end;
}
