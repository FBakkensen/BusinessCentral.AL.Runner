namespace Repro.Ndd;

using Microsoft.Inventory.Ledger;

codeunit 64682 "Ndd Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    [Test]
    procedure QueryJoiningNamespacedDependencyTable_Reads()
    var
        Ile: Record "Item Ledger Entry";
        Link: Record "Ndd Link";
        Q: Query "Ndd Join";
        Rows: Integer;
    begin
        Link.DeleteAll();
        Ile.Init();
        Ile."Entry No." := 990001;
        Ile."Item No." := 'NDD-ITEM';
        Ile.Quantity := 7;
        if not Ile.Insert() then
            Ile.Modify();
        Link."Entry No." := 1;
        Link."Item Ledger Entry No." := 990001;
        Link.Qty := 3;
        Link.Insert();

        Q.SetRange(EntryNo, 1);
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            if Q.ItemNo <> 'NDD-ITEM' then
                Error('unexpected item %1', Q.ItemNo);
            if Q.IleQuantity <> 7 then
                Error('unexpected ILE quantity %1', Q.IleQuantity);
            if Q.Qty <> 3 then
                Error('unexpected link qty %1', Q.Qty);
        end;
        if Rows <> 1 then
            Error('expected 1 joined row, got %1', Rows);
    end;
}
