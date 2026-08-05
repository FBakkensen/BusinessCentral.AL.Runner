table 50096 "PV Row"
{
    fields
    {
        field(1; Id; Integer) { }
    }
    keys { key(PK; Id) { Clustered = true; } }
}

page 50042 "PV Probe Page"
{
    PageType = List;
    SourceTable = "PV Row";
}
