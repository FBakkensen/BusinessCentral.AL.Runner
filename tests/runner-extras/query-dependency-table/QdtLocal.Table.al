namespace ALRunnerExtras.QueryDependencyTable;

table 64581 "Qdt Local"
{
    DataClassification = SystemMetadata;

    fields
    {
        field(1; "Code"; Code[20]) { }
        field(2; Description; Text[100]) { }
    }

    keys
    {
        key(PK; "Code") { Clustered = true; }
    }
}
