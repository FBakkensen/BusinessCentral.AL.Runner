namespace ALRunnerExtras.QueryDependencyTable;

using Microsoft.Inventory.Item;

// The reproducer shape from issue #2295: one dataitem, no join, no aggregation, public
// access — the table simply lives in a dependency app (Base Application).
query 64580 "Qdt Item Rows"
{
    Access = Public;
    QueryType = Normal;

    elements
    {
        dataitem(Item; Item)
        {
            column(No; "No.")
            {
            }
            column(Description; Description)
            {
            }
        }
    }
}
