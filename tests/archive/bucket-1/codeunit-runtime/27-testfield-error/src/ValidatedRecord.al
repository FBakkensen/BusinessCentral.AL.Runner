table 50028 TestFieldTable
{
    DataClassification = SystemMetadata;

    fields
    {
        field(1; "Code"; Code[10])
        {
        }
        field(2; "Mandatory Field"; Text[100])
        {
        }
    }

    keys
    {
        key(PK; "Code")
        {
            Clustered = true;
        }
    }
}

codeunit 50197 TestFieldHelper
{
    procedure ValidateRecord(var Rec: Record TestFieldTable)
    begin
        Rec.TestField("Mandatory Field");
    end;
}
