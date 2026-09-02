query 64675 "Acf Balances Const"
{
    QueryType = Normal;
    elements
    {
        dataitem(Entry; "Acf Entry")
        {
            column(ProjectNo; "Project No.") { }
            column(ItemNo; "Item No.") { ColumnFilter = ItemNo = const('B'); }
            column(AssignedQuantity; Quantity) { Method = Sum; }
        }
    }
}
