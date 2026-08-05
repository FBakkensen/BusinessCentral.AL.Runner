report 50005 "Report With Skip"
{
    ProcessingOnly = true;
    UsageCategory = ReportsAndAnalysis;
    ApplicationArea = All;

    dataset
    {
    }

    trigger OnPreReport()
    begin
        CurrReport.Skip();
    end;
}
