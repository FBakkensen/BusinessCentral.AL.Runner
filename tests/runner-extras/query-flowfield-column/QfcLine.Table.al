namespace ALRunnerExtras.QueryFlowFieldColumn;

table 64620 "Qfc Line"
{
    DataClassification = SystemMetadata;
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; "Header No."; Code[20]) { }
        field(3; Amount; Decimal) { }
    }
    keys { key(PK; "Entry No.") { Clustered = true; } }
}
