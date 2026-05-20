table 50174 "FF Master"
{
    fields
    {
        field(1; Id; Integer) { }
        field(2; "Has Child"; Boolean)
        {
            CalcFormula = exist("FF Child" where(ParentId = field(Id)));
            FieldClass = FlowField;
        }
    }
    keys { key(PK; Id) { Clustered = true; } }
}

table 50175 "FF Child"
{
    fields
    {
        field(1; Id; Integer) { }
        field(2; ParentId; Integer) { }
    }
    keys { key(PK; Id) { Clustered = true; } }
}
