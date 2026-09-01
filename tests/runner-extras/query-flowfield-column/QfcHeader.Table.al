namespace ALRunnerExtras.QueryFlowFieldColumn;

table 64621 "Qfc Header"
{
    DataClassification = SystemMetadata;
    fields
    {
        field(1; "No."; Code[20]) { }
        field(2; "Total Amount"; Decimal)
        {
            FieldClass = FlowField;
            CalcFormula = sum("Qfc Line".Amount where("Header No." = field("No.")));
        }
    }
    keys { key(PK; "No.") { Clustered = true; } }
}
