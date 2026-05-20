codeunit 50293 "VG Src"
{
    procedure GetBySystemIdFromVariant(reference: Variant): Boolean
    var
        rr: RecordRef;
    begin
        rr.Open(Database::"VG Table");
        exit(rr.GetBySystemId(reference));
    end;
}
