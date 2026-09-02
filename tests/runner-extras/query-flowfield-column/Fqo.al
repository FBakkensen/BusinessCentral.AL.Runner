table 64710 "Fqo Header"
{
    fields
    {
        field(1; "No."; Code[20]) { }
        field(2; "Line Total"; Decimal)
        {
            FieldClass = FlowField;
            CalcFormula = sum("Fqo Line".Amount where("Header No." = field("No.")));
        }
    }
    keys { key(PK; "No.") { Clustered = true; } }
}

table 64711 "Fqo Line"
{
    fields
    {
        field(1; "Header No."; Code[20]) { }
        field(2; "Line No."; Integer) { }
        field(3; Amount; Decimal) { }
    }
    keys { key(PK; "Header No.", "Line No.") { Clustered = true; } }
}

query 64712 "Fqo Totals"
{
    QueryType = Normal;
    elements
    {
        dataitem(Header; "Fqo Header")
        {
            column(No; "No.") { }
            column(LineTotal; "Line Total") { }
        }
    }
}


codeunit 64713 "Fqo Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    [Test]
    procedure QueryWithFlowFieldColumnOverOwnTable_ReadsCalculatedValue()
    var
        H: Record "Fqo Header";
        L: Record "Fqo Line";
        Q: Query "Fqo Totals";
        Rows: Integer;
    begin
        L.DeleteAll();
        H.DeleteAll();
        H."No." := 'H1';
        H.Insert();
        L."Header No." := 'H1'; L."Line No." := 1; L.Amount := 4; L.Insert();
        L."Header No." := 'H1'; L."Line No." := 2; L.Amount := 6; L.Insert();
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            if Q.LineTotal <> 10 then
                Error('expected FlowField column 10, got %1', Q.LineTotal);
        end;
        if Rows <> 1 then
            Error('expected 1 row, got %1', Rows);
    end;
}
