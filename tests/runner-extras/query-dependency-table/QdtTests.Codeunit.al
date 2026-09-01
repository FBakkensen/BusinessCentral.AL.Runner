namespace ALRunnerExtras.QueryDependencyTable;

using Microsoft.Inventory.Item;

codeunit 64584 "Qdt Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "Qdt Assert";

    [Test]
    procedure DependencyTableQuery_Open_ReadsInsertedRows()
    var
        ItemRows: Query "Qdt Item Rows";
        RowCount: Integer;
        LastDescription: Text;
    begin
        CreateItem('QDTA-A', 'Alpha');
        CreateItem('QDTA-B', 'Bravo');

        // Read every row and count ours client-side rather than SetFilter(No, 'QDTA-*'):
        // a wildcard filter on a Query column is a separate, pre-existing runner gap (#2299)
        // (InvalidCastException NCLMetaQueryColumn -> NCLMetaField in BC's
        // RecordBufferEvaluatorVisitor) that also hits an application-local Query.
        Assert.IsTrue(ItemRows.Open(), 'Open() must succeed for a Query over a dependency table');
        while ItemRows.Read() do
            if CopyStr(ItemRows.No, 1, 5) = 'QDTA-' then begin
                RowCount += 1;
                LastDescription := ItemRows.Description;
            end;
        ItemRows.Close();

        Assert.AreEqual(2, RowCount, 'both inserted Item rows must be read back through the Query');
        Assert.AreEqual('Bravo', LastDescription, 'rows come back in primary-key order, so the last one is QDTA-B');
    end;

    [Test]
    procedure DependencyTableQuery_SetRange_NarrowsToOneRow()
    var
        ItemRows: Query "Qdt Item Rows";
        RowCount: Integer;
    begin
        CreateItem('QDTB-A', 'Alpha');
        CreateItem('QDTB-B', 'Bravo');

        ItemRows.SetRange(No, 'QDTB-B');
        ItemRows.Open();
        while ItemRows.Read() do begin
            RowCount += 1;
            Assert.AreEqual('QDTB-B', ItemRows.No, 'SetRange must restrict the rows to the one Item');
            Assert.AreEqual('Bravo', ItemRows.Description, 'the row carries the matching Item''s Description');
        end;
        ItemRows.Close();

        Assert.AreEqual(1, RowCount, 'exactly one row matches the SetRange');
    end;

    [Test]
    procedure DependencyTableQuery_SetRange_NoMatch_ReadsNothing()
    var
        ItemRows: Query "Qdt Item Rows";
    begin
        CreateItem('QDTC-A', 'Alpha');

        ItemRows.SetRange(No, 'QDTC-MISSING');
        ItemRows.Open();
        Assert.IsFalse(ItemRows.Read(), 'a SetRange that matches no Item must read no row');
        ItemRows.Close();
    end;

    [Test]
    procedure LocalTableQuery_StillReadsRows()
    var
        QdtLocal: Record "Qdt Local";
        LocalRows: Query "Qdt Local Rows";
        RowCount: Integer;
    begin
        QdtLocal.Init();
        QdtLocal."Code" := 'L1';
        QdtLocal.Description := 'Local one';
        QdtLocal.Insert();

        LocalRows.SetRange(Code, 'L1');
        LocalRows.Open();
        while LocalRows.Read() do begin
            RowCount += 1;
            Assert.AreEqual('Local one', LocalRows.Description, 'control: local-table Query reads its row');
        end;
        LocalRows.Close();

        Assert.AreEqual(1, RowCount, 'control: exactly one local row matches');
    end;

    local procedure CreateItem(ItemNo: Code[20]; Description: Text[100])
    var
        Item: Record Item;
    begin
        Item.Init();
        Item."No." := ItemNo;
        Item.Description := Description;
        Item.Insert();
    end;
}
