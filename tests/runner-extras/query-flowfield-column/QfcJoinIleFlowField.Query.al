namespace ALRunnerExtras.QueryFlowFieldColumn;

using Microsoft.Inventory.Ledger;

query 64625 "Qfc Join ILE FlowField"
{
    QueryType = Normal;
    elements
    {
        dataitem(QfcLink; "Qfc Link")
        {
            column(LinkEntryNo; "Entry No.") { }
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
