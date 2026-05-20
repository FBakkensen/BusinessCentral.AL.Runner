/// Minimal report stub for Clear() coverage.
report 50009 "RXC Report"
{
    dataset { }
}

/// Minimal xmlport stub for Clear() coverage.
xmlport 97709 "RXC XmlPort"
{
    Direction = Both;
    Format = Xml;
    schema { }
}

/// Source helpers for Report/XmlPort Clear() coverage (issue #967).
/// Exercises Clear(rep) and Clear(xp) — which the BC compiler lowers to
/// rep.Clear() / xp.Clear() on the mock handle types.
codeunit 50395 "RXC Src"
{
    procedure ClearReport()
    var
        rep: Report "RXC Report";
    begin
        Clear(rep);
    end;

    procedure ClearXmlPort()
    var
        xp: XmlPort "RXC XmlPort";
    begin
        Clear(xp);
    end;

    procedure ClearReportTwice()
    var
        rep: Report "RXC Report";
    begin
        Clear(rep);
        Clear(rep);
    end;

    procedure ClearXmlPortTwice()
    var
        xp: XmlPort "RXC XmlPort";
    begin
        Clear(xp);
        Clear(xp);
    end;
}
