query 64672 "Acf Balances No Filter"
{
    QueryType = Normal;
    elements
    {
        dataitem(Entry; "Acf Entry")
        {
            column(ProjectNo; "Project No.") { }
            column(ItemNo; "Item No.") { }
            column(AssignedQuantity; Quantity) { Method = Sum; }
            filter(ProjectNoFilter; "Project No.") { }
        }
    }
}
