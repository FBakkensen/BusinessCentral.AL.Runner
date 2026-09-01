namespace ALRunnerExtras.QueryFlowFieldColumn;

using Microsoft.Inventory.Ledger;

query 64624 "Qfc ILE FlowField"
{
    QueryType = Normal;
    elements
    {
        dataitem(ItemLedgerEntry; "Item Ledger Entry")
        {
            column(EntryNo; "Entry No.") { }
            column(CostAmountActual; "Cost Amount (Actual)") { }
        }
    }
}
