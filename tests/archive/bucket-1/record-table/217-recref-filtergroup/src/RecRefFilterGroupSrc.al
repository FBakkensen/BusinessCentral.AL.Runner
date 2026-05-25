table 50121 "RFG Row"
{
    fields
    {
        field(1; "Id"; Integer) { }
    }
    keys { key(PK; "Id") { Clustered = true; } }
}

/// Exercises RecordRef.FilterGroup.
codeunit 50507 "RFG Src"
{
    procedure FilterGroup_SetThenGet(group: Integer): Integer
    var
        rr: RecordRef;
    begin
        rr.Open(50121);
        rr.FilterGroup(group);
        exit(rr.FilterGroup());
    end;

    procedure FilterGroup_DefaultIsZero(): Integer
    var
        rr: RecordRef;
    begin
        rr.Open(50121);
        exit(rr.FilterGroup());
    end;
}
