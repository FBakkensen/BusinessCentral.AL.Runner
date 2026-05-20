codeunit 50560 "RR Open Probe"
{
    procedure ProbeCompany(CompanyName: Text): Integer
    var
        RecRef: RecordRef;
    begin
        // Three-arg form: TableNo, Temporary, CompanyName.
        // Use a local table (50000 = "Audit Log Entry") to avoid BC system-table dependency.
        RecRef.Open(50000, false, CompanyName);
        if RecRef.IsEmpty() then
            exit(42);
        exit(42);
    end;

    procedure ProbeLocalCompiles(): Integer
    var
        RecRef: RecordRef;
    begin
        // Single-arg form must compile. Use a local table (50000 = "Audit Log Entry").
        RecRef.Open(50000);
        if RecRef.IsEmpty() then;
        exit(7);
    end;
}
