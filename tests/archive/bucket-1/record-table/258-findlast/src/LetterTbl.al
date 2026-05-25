table 50129 "FL Letter"
{
    fields
    {
        field(1; "Code"; Code[10]) { }
        field(2; "Category"; Integer) { }
        field(3; "Weight"; Integer) { }
    }
    keys
    {
        key(PK; "Code") { Clustered = true; }
    }
}
