namespace ALRunnerExtras.QueryFlowFieldColumn;

using Microsoft.Inventory.Ledger;

codeunit 64628 "Qfc Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "Qfc Assert";

    [Test]
    procedure LocalFlowFieldColumn_ReadsCalculatedValue()
    var
        Q: Query "Qfc Local FlowField";
    begin
        SeedHeader('H1');
        SeedLine(1, 'H1', 10.5);
        SeedLine(2, 'H1', 4.5);

        Q.SetRange(No, 'H1');
        Q.Open();
        Assert.IsTrue(Q.Read(), 'the header row must be read');
        Assert.AreEqual(15, Q.TotalAmount, 'the FlowField column carries the sum of the header''s lines');
        Assert.IsFalse(Q.Read(), 'exactly one header matches');
        Q.Close();
    end;

    [Test]
    procedure LocalFlowFieldColumn_NoSourceRows_ReadsZero()
    var
        Q: Query "Qfc Local FlowField";
    begin
        SeedHeader('H2');

        Q.SetRange(No, 'H2');
        Q.Open();
        Assert.IsTrue(Q.Read(), 'the header row must be read even with no lines');
        Assert.AreEqual(0, Q.TotalAmount, 'a sum FlowField over no source rows is 0');
        Q.Close();
    end;

    [Test]
    procedure IleFlowFieldColumn_ReadsCalculatedValue()
    var
        Q: Query "Qfc ILE FlowField";
    begin
        SeedItemLedgerEntry(64624, 'QFC', 12.5);

        Q.SetRange(EntryNo, 64624);
        Q.Open();
        Assert.IsTrue(Q.Read(), 'the Item Ledger Entry row must be read');
        Assert.AreEqual(12.5, Q.CostAmountActual, '"Cost Amount (Actual)" is the sum of the entry''s Value Entries');
        Q.Close();
    end;

    [Test]
    procedure JoinIleFlowFieldColumn_ReadsCalculatedValue()
    var
        Q: Query "Qfc Join ILE FlowField";
    begin
        SeedItemLedgerEntry(64625, 'QFC', 7.25);
        SeedLink(1, 64625, 1);

        Q.Open();
        Assert.IsTrue(Q.Read(), 'the joined row must be read');
        Assert.AreEqual('QFC', Q.ItemNo, 'the joined Item Ledger Entry''s Item No.');
        Assert.AreEqual(7.25, Q.CostAmountActual, 'the joined dataitem''s FlowField column is calculated for its row');
        Assert.IsFalse(Q.Read(), 'exactly one link row joins');
        Q.Close();
    end;

    [Test]
    procedure JoinWithSumAndFlowField_GroupsPerItemLedgerEntry()
    var
        Q: Query "Qfc Valuation";
        Rows: Text;
    begin
        SeedItemLedgerEntry(64631, 'QFC-A', 7.25);
        SeedItemLedgerEntry(64632, 'QFC-B', 1.5);
        SeedLink(11, 64631, 2);
        SeedLink(12, 64631, 3);
        SeedLink(13, 64632, 5);

        // Rows inserted by the other tests of this codeunit stay visible (codeunit isolation),
        // so pin the query to this test's own entries; the range is evaluated after the join.
        Q.SetRange(ItemLedgerEntryNo, 64631, 64632);
        Q.Open();
        while Q.Read() do
            Rows += Format(Q.ItemLedgerEntryNo) + ':' + Q.ItemNo + ':' + Format(Q.AssignedQuantity) + ':' + Format(Q.CostAmountActual) + ';';
        Q.Close();

        Assert.AreEqual('64631:QFC-A:5:7.25;64632:QFC-B:5:1.5;', Rows,
            'Method = Sum groups per Item Ledger Entry, with the FlowField column as a group key like any Normal column');
    end;

    [Test]
    procedure SetRange_OnFlowFieldColumn_FiltersOnTheCalculatedValue()
    var
        Q: Query "Qfc Local FlowField";
        NoMatch: Query "Qfc Local FlowField";
        Nos: Text;
    begin
        SeedHeader('H3');
        SeedLine(31, 'H3', 15.75);
        SeedHeader('H4');
        SeedLine(41, 'H4', 4);

        Q.SetRange(TotalAmount, 15.75);
        Q.Open();
        while Q.Read() do
            Nos += Q.No + ';';
        Q.Close();
        Assert.AreEqual('H3;', Nos, 'SetRange on the FlowField column keeps only the header whose calculated sum matches');

        NoMatch.SetRange(TotalAmount, 99);
        NoMatch.Open();
        Assert.IsFalse(NoMatch.Read(), 'a SetRange no calculated value matches reads nothing');
        NoMatch.Close();
    end;

    local procedure SeedHeader(No: Code[20])
    var
        QfcHeader: Record "Qfc Header";
    begin
        QfcHeader.Init();
        QfcHeader."No." := No;
        QfcHeader.Insert();
    end;

    local procedure SeedLine(EntryNo: Integer; HeaderNo: Code[20]; Amount: Decimal)
    var
        QfcLine: Record "Qfc Line";
    begin
        QfcLine.Init();
        QfcLine."Entry No." := EntryNo;
        QfcLine."Header No." := HeaderNo;
        QfcLine.Amount := Amount;
        QfcLine.Insert();
    end;

    local procedure SeedLink(EntryNo: Integer; ItemLedgerEntryNo: Integer; Quantity: Decimal)
    var
        QfcLink: Record "Qfc Link";
    begin
        QfcLink.Init();
        QfcLink."Entry No." := EntryNo;
        QfcLink."Item Ledger Entry No." := ItemLedgerEntryNo;
        QfcLink.Quantity := Quantity;
        QfcLink.Insert();
    end;

    local procedure SeedItemLedgerEntry(EntryNo: Integer; ItemNo: Code[20]; CostAmountActual: Decimal)
    var
        ItemLedgerEntry: Record "Item Ledger Entry";
        ValueEntry: Record "Value Entry";
    begin
        ItemLedgerEntry.Init();
        ItemLedgerEntry."Entry No." := EntryNo;
        ItemLedgerEntry."Item No." := ItemNo;
        ItemLedgerEntry.Insert();

        ValueEntry.Init();
        ValueEntry."Entry No." := EntryNo;
        ValueEntry."Item Ledger Entry No." := EntryNo;
        ValueEntry."Cost Amount (Actual)" := CostAmountActual;
        ValueEntry.Insert();
    end;
}
