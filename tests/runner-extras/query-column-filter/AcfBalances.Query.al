query 64671 "Acf Balances"
{
    QueryType = Normal;
    elements
    {
        dataitem(Entry; "Acf Entry")
        {
            column(ProjectNo; "Project No.") { }
            column(ItemNo; "Item No.") { }
            column(AssignedQuantity; Quantity)
            {
                ColumnFilter = AssignedQuantity = filter(> 0);
                Method = Sum;
            }
            filter(ProjectNoFilter; "Project No.") { }
            filter(ItemNoFilter; "Item No.") { }
        }
    }
}
