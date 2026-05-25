/// Source objects for "Report.Run on layout-rendering report throws OOS".
/// The runner has no service tier and cannot render RDLC/Word/Excel layouts.
/// AL semantics: a report whose ProcessingOnly is false (the AL default) must
/// attempt rendering after lifecycle triggers — the runner surfaces that
/// attempt as an AL-observable error from the layout-resolution code path
/// (NavReport.RDLCLayout → GetLayoutCore).

table 50600 "RLT Record"
{
    fields
    {
        field(1; Id; Integer) { }
    }
    keys { key(PK; Id) { Clustered = true; } }
}

/// ProcessingOnly is intentionally omitted → AL default is false.
/// Calling .Run() must throw out-of-scope (no layout rendering in standalone).
report 50600 "RLT Layout Report"
{
    dataset
    {
        dataitem(Item; "RLT Record") { }
    }
}

/// Sanity counterpart: ProcessingOnly = true → Run() succeeds (lifecycle only).
report 50601 "RLT Processing Report"
{
    ProcessingOnly = true;
    dataset
    {
        dataitem(Item; "RLT Record") { }
    }
}

codeunit 50600 "RLT Driver"
{
    procedure RunLayoutReport()
    var
        Rpt: Report "RLT Layout Report";
    begin
        Rpt.Run();
    end;

    procedure RunProcessingReport()
    var
        Rpt: Report "RLT Processing Report";
    begin
        Rpt.Run();
    end;
}
