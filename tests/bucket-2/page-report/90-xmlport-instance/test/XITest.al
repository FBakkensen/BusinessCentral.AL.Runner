codeunit 50536 "XI Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "XI Src";

    [Test]
    procedure Export_ThrowsOutOfScope()
    begin
        asserterror Src.CallExport();
        Assert.ExpectedError('out-of-scope: NavXmlPort.Export');
    end;

    [Test]
    procedure Import_ThrowsOutOfScope()
    begin
        asserterror Src.CallImport();
        Assert.ExpectedError('out-of-scope: NavXmlPort.Import');
    end;

    [Test]
    procedure Run_ThrowsOutOfScope()
    begin
        asserterror Src.CallRun();
        Assert.ExpectedError('out-of-scope: NavXmlPort.Run');
    end;

    [Test]
    procedure SetDestination_IsNoOp()
    begin
        Assert.IsTrue(Src.CallSetDestination(), 'XmlPort.SetDestination() must be a no-op');
    end;

    [Test]
    procedure SetSource_IsNoOp()
    begin
        Assert.IsTrue(Src.CallSetSource(), 'XmlPort.SetSource() must be a no-op');
    end;

    [Test]
    procedure SetTableView_ThrowsOutOfScope()
    begin
        asserterror Src.CallSetTableView();
        Assert.ExpectedError('out-of-scope: NavXmlPort.SetTableView');
    end;

    [Test]
    procedure CurrentPath_ReturnsEmpty()
    begin
        Assert.AreEqual('', Src.CallCurrentPath(), 'XmlPort.CurrentPath() must return empty string');
    end;

    [Test]
    procedure FieldDelimiter_RoundTrip()
    begin
        Assert.IsTrue(Src.CallFieldDelimiter(), 'XmlPort.FieldDelimiter(set) must be a no-op');
    end;

    [Test]
    procedure FieldDelimiter_Get_ReturnsDefault()
    begin
        Assert.AreEqual('', Src.GetFieldDelimiter(), 'XmlPort.FieldDelimiter() default must be empty');
    end;

    [Test]
    procedure FieldSeparator_RoundTrip()
    begin
        Assert.IsTrue(Src.CallFieldSeparator(), 'XmlPort.FieldSeparator(set) must be a no-op');
    end;

    [Test]
    procedure FieldSeparator_Get_ReturnsDefault()
    begin
        Assert.AreEqual('', Src.GetFieldSeparator(), 'XmlPort.FieldSeparator() default must be empty');
    end;

    [Test]
    procedure Filename_RoundTrip()
    begin
        Assert.IsTrue(Src.CallFilename(), 'XmlPort.Filename(set) must be a no-op');
    end;

    [Test]
    procedure Filename_Get_ReturnsDefault()
    begin
        Assert.AreEqual('', Src.GetFilename(), 'XmlPort.Filename() default must be empty');
    end;

    [Test]
    procedure RecordSeparator_RoundTrip()
    begin
        Assert.IsTrue(Src.CallRecordSeparator(), 'XmlPort.RecordSeparator(set) must be a no-op');
    end;

    [Test]
    procedure RecordSeparator_Get_ReturnsDefault()
    begin
        Assert.AreEqual('', Src.GetRecordSeparator(), 'XmlPort.RecordSeparator() default must be empty');
    end;

    [Test]
    procedure TableSeparator_RoundTrip()
    begin
        Assert.IsTrue(Src.CallTableSeparator(), 'XmlPort.TableSeparator(set) must be a no-op');
    end;

    [Test]
    procedure TableSeparator_Get_ReturnsDefault()
    begin
        Assert.AreEqual('', Src.GetTableSeparator(), 'XmlPort.TableSeparator() default must be empty');
    end;

    [Test]
    procedure TextEncoding_IsNoOp()
    begin
        Assert.IsTrue(Src.CallTextEncoding(), 'XmlPort.TextEncoding(set) must be a no-op');
    end;
}
