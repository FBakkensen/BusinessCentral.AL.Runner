namespace ALRunnerExtras.QueryFlowFieldColumn;

using Microsoft.Inventory.Ledger;

// The originally reported shape (issue #2300): an aggregated column on the driving
// dataitem plus FlowField columns on the joined Base Application dataitem — the FlowField
// columns group like Normal columns, so the sum is per Item Ledger Entry.
query 64626 "Qfc Valuation"
{
    QueryType = Normal;
    elements
    {
        dataitem(QfcLink; "Qfc Link")
        {
            column(ItemLedgerEntryNo; "Item Ledger Entry No.") { }
            column(AssignedQuantity; Quantity) { Method = Sum; }
            dataitem(ItemLedgerEntry; "Item Ledger Entry")
            {
                DataItemLink = "Entry No." = QfcLink."Item Ledger Entry No.";
                SqlJoinType = InnerJoin;
                column(ItemNo; "Item No.") { }
                column(CostAmountActual; "Cost Amount (Actual)") { }
            }
        }
    }
}
