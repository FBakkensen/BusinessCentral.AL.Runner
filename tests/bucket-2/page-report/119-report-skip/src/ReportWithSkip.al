report 50005 "Report With Skip"
{
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
