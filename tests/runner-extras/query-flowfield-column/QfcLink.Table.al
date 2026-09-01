namespace ALRunnerExtras.QueryFlowFieldColumn;

table 64622 "Qfc Link"
{
    DataClassification = SystemMetadata;
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; "Item Ledger Entry No."; Integer) { }
        field(3; Quantity; Decimal) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }
}
