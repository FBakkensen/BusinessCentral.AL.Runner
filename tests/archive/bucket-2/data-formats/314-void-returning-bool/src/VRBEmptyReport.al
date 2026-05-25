/// Minimal local table used as report dataitem to avoid dependency on Integer table.
table 50024 "VRB Dummy Table"
{
    fields
    {
        field(1; Id; Integer) { }
    }
    keys { key(PK; Id) { } }
}

/// Minimal report fixture used by VRB tests.
report 50000 "VRB Empty Report"
{
    ProcessingOnly = true;
    dataset
    {
        dataitem(DummyItem; "VRB Dummy Table")
        {
            DataItemTableView = where(Id = const(0));
        }
    }
}

/// Minimal local blob-table replacement for Temp Blob codeunit (not available in runner).
table 50025 "VRB Blob Store"
{
    fields
    {
        field(1; Id; Integer) { }
        field(2; Data; Blob) { }
    }
    keys { key(PK; Id) { } }
}
