codeunit 50381 MiscStubsTest
{
    Subtype = Test;
    var Assert: Codeunit Assert;

    [Test]
    procedure TestXmlNodeIsDocumentTypeReturnsFalseForElement()
    var
        Doc: XmlDocument;
        Root: XmlElement;
        ElemNode: XmlNode;
        Src: Codeunit MiscStubsSrc;
    begin
        XmlDocument.ReadFrom('<root/>', Doc);
        Doc.GetRoot(Root);
        ElemNode := Root.AsXmlNode();
        Assert.IsFalse(Src.DoXmlNodeIsDocumentType(ElemNode), 'XmlElement node is not DocumentType');
    end;

    [Test]
    procedure TestXmlNodeIsDocumentTypeReturnsFalseForDocument()
    var
        Doc: XmlDocument;
        DocNode: XmlNode;
        Src: Codeunit MiscStubsSrc;
    begin
        XmlDocument.ReadFrom('<root/>', Doc);
        DocNode := Doc.AsXmlNode();
        Assert.IsFalse(Src.DoXmlNodeIsDocumentType(DocNode), 'XmlDocument node is not DocumentType');
    end;

    [Test]
    procedure TestNavAppGetArchiveRecordRefIsNoOp()
    var
        RecRef: RecordRef;
        Src: Codeunit MiscStubsSrc;
        Result: Boolean;
    begin
        Result := Src.DoNavAppGetArchiveRecordRef(18, RecRef);
        Assert.IsFalse(Result, 'GetArchiveRecordRef should return false in standalone mode');
    end;

    [Test]
    procedure TestNavAppGetResource_MissingThrows()
    // BC 16.1: NavApp.GetResource throws when the requested resource does not exist
    var
        IStream: InStream;
        Src: Codeunit MiscStubsSrc;
    begin
        asserterror Src.DoNavAppGetResource('dummy.txt', IStream);
    end;

    [Test]
    procedure TestRecordIdGetRecordOnBlankThrows()
    // BC 16.1: RecordId.GetRecord() on a blank RecordId raises "The record is not open."
    var
        RecId: RecordId;
        RecRef: RecordRef;
        Src: Codeunit MiscStubsSrc;
    begin
        asserterror RecRef := Src.DoRecordIdGetRecord(RecId);
        Assert.ExpectedError('not open');
    end;
}
