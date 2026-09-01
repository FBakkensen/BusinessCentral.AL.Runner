namespace ALRunnerExtras.QueryColumnWildcardFilter;

table 64600 "Qcw Local"
{
    DataClassification = SystemMetadata;

    fields
    {
        field(1; "Code"; Code[20]) { }
        field(2; Description; Text[100]) { }
        field(3; Amount; Integer) { }
    }

    keys
    {
        key(PK; "Code") { Clustered = true; }
    }
}
