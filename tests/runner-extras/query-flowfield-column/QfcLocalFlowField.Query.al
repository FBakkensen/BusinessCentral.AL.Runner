namespace ALRunnerExtras.QueryFlowFieldColumn;

query 64623 "Qfc Local FlowField"
{
    QueryType = Normal;
    elements
    {
        dataitem(QfcHeader; "Qfc Header")
        {
            column(No; "No.") { }
            column(TotalAmount; "Total Amount") { }
        }
    }
}
