table 50056 "Misc Stubs Table"
{
    DataClassification = CustomerContent;
    fields
    {
        field(1; Id; Integer) { }
    }
    keys { key(PK; Id) { Clustered = true; } }
}

codeunit 50380 MiscStubsSrc
{
    procedure DoXmlNodeIsDocumentType(Node: XmlNode): Boolean
    begin
        exit(Node.IsXmlDocumentType());
    end;

    procedure DoXmlNodeAsDocumentType(Node: XmlNode): XmlDocumentType
    begin
        exit(Node.AsXmlDocumentType());
    end;

    procedure DoNavAppGetArchiveRecordRef(TableId: Integer; var RecRef: RecordRef): Boolean
    begin
        // BC 16.1: GetArchiveRecordRef leaves RecRef unbound (deprecated V1→V2 migration API).
        // Do not call RecRef.IsEmpty() — unbound RecordRef throws "record is not open".
        NavApp.GetArchiveRecordRef(TableId, RecRef);
        exit(false); // always false — archive data not available in standalone
    end;

    procedure DoNavAppGetResource(ResName: Text; var IStream: InStream): Boolean
    begin
        NavApp.GetResource(ResName, IStream);
        exit(true);
    end;

    procedure DoRecordIdGetRecord(RecId: RecordId): RecordRef
    begin
        exit(RecId.GetRecord());
    end;
}
