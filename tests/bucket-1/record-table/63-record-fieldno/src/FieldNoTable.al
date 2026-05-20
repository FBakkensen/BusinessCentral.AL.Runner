table 50187 "FN Test Table"
{
    fields
    {
        field(1; Code; Code[10]) { }
        field(2; Description; Text[100]) { }
    }
    keys
    {
        key(PK; Code) { Clustered = true; }
    }
}
