/// Tests that Report.SaveAs / SaveAsPdf / SaveAsWord / SaveAsExcel / SaveAsHtml / SaveAsXml
/// are out-of-scope (report-rendering): NavReport.SaveAsAsync is Cecil-rewritten to throw OOS.
codeunit 50472 "Report SaveAs Bool Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure ReportSaveAs_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        // [GIVEN] Report.SaveAs called with OutStream overload in an if-expression
        // [WHEN]  NavReport.SaveAsAsync is Cecil-rewritten to throw OOS
        // [THEN]  Throws out-of-scope error
        asserterror Src.SaveAsReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAs_RecordRef_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        // [GIVEN] Report.SaveAs called with 5-arg (RecordRef) overload in an if-expression
        // [WHEN]  NavReport.SaveAsAsync is Cecil-rewritten to throw OOS
        // [THEN]  Throws out-of-scope error
        asserterror Src.SaveAsRecordRefReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAsPdf_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        asserterror Src.SaveAsPdfReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAsWord_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        asserterror Src.SaveAsWordReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAsExcel_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        asserterror Src.SaveAsExcelReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAsHtml_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        asserterror Src.SaveAsHtmlReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;

    [Test]
    procedure ReportSaveAsXml_UsedAsBoolean_OutOfScope()
    var
        Src: Codeunit "Report SaveAs Bool Src";
    begin
        asserterror Src.SaveAsXmlReturnsTrue(99999, '');
        Assert.ExpectedError('out-of-scope: NavReport.SaveAs');
    end;
}
