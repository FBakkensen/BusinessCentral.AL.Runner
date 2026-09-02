namespace Repro.Ndd;

table 64680 "Ndd Link"
{
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; "Item Ledger Entry No."; Integer) { }
        field(3; Qty; Decimal) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }
}
