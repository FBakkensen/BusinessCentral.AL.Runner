namespace Repro.Ndd;

using Microsoft.Inventory.Ledger;

query 64681 "Ndd Join"
{
    QueryType = Normal;
    elements
    {
        dataitem(Link; "Ndd Link")
        {
            column(EntryNo; "Entry No.") { }
            column(Qty; Qty) { }
            dataitem(ItemLedgerEntry; "Item Ledger Entry")
            {
                DataItemLink = "Entry No." = Link."Item Ledger Entry No.";
                SqlJoinType = InnerJoin;
                column(ItemNo; "Item No.") { }
                column(IleQuantity; Quantity) { }
            }
        }
    }
}
