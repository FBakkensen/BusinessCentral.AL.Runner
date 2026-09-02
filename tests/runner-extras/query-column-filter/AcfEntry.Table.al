table 64670 "Acf Entry"
{
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; "Project No."; Code[20]) { }
        field(3; "Item No."; Code[20]) { }
        field(4; Quantity; Decimal) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }
}
