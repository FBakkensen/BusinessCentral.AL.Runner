codeunit 64601 "Amwr Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    [Test]
    procedure TwoModifiesSameTable_ThenError_BothRolledBack()
    var
        Row: Record "Amwr Row";
    begin
        SeedTwoRows();

        asserterror ModifyTwoThenError();

        if GetLastErrorText() <> 'Amwr boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        Row.Get(1);
        if Row.Qty <> 10 then
            Error('row 1 not rolled back: Qty=%1', Row.Qty);
        Row.Get(2);
        if Row.Qty <> 20 then
            Error('row 2 not rolled back: Qty=%1', Row.Qty);
    end;

    [Test]
    procedure TwoInsertsSameTable_ThenError_BothRolledBack()
    var
        Row: Record "Amwr Row";
    begin
        Row.DeleteAll();
        Commit();

        asserterror InsertTwoThenError();

        if GetLastErrorText() <> 'Amwr boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        if Row.Count() <> 0 then
            Error('expected 0 rows after rollback, got %1', Row.Count());
    end;

    [Test]
    procedure ModifyThenDelete_ThenError_BothRolledBack()
    var
        Row: Record "Amwr Row";
    begin
        SeedTwoRows();

        asserterror ModifyOneDeleteOtherThenError();

        if GetLastErrorText() <> 'Amwr boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        Row.Get(1);
        if Row.Qty <> 10 then
            Error('row 1 not rolled back: Qty=%1', Row.Qty);
        if not Row.Get(2) then
            Error('row 2 deletion not rolled back');
    end;

    [Test]
    procedure InsertThenFailingInsert_CompletedInsertRolledBack()
    var
        Row: Record "Amwr Row";
    begin
        // A completed Insert() followed, in the same statement, by an Insert() whose OWN
        // OnInsert throws. The completed one is an ordinary write and the commit-point
        // rollback must undo it. Measured on real BC 28.4: row 1 is gone afterwards.
        //
        // Deliberately NOT asserted: whether row 2 (the one whose OnInsert threw) exists
        // afterwards. Real BC 28.4 measured it ABSENT in this shape, while the corpus's
        // TestTriggerRollback.OnInsert_Throws_RecordNotInserted measures the row PRESENT in
        // its shape; the mechanism separating the two is the open question in #2167, and
        // the runner's ForceDurableFailedInserts still keeps row 2 here.
        Row.DeleteAll();
        Commit();

        asserterror InsertOneThenFailingInsert();

        if GetLastErrorText() <> 'Amwr OnInsert refused' then
            Error('unexpected error text: %1', GetLastErrorText());
        if Row.Get(1) then
            Error('completed Insert() of row 1 must be rolled back');
    end;

    [Test]
    procedure TwoModifies_CommitBetween_OnlySecondRolledBack()
    var
        Row: Record "Amwr Row";
    begin
        SeedTwoRows();

        asserterror ModifyCommitModifyThenError();

        Row.Get(1);
        if Row.Qty <> 11 then
            Error('row 1 was committed inside the statement and must survive: Qty=%1', Row.Qty);
        Row.Get(2);
        if Row.Qty <> 20 then
            Error('row 2 not rolled back: Qty=%1', Row.Qty);
    end;

    local procedure SeedTwoRows()
    var
        Row: Record "Amwr Row";
    begin
        Row.DeleteAll();
        Row.Init();
        Row."Entry No." := 1;
        Row.Qty := 10;
        Row.Insert();
        Row."Entry No." := 2;
        Row.Qty := 20;
        Row.Insert();
        Commit();
    end;

    local procedure ModifyTwoThenError()
    var
        Row: Record "Amwr Row";
    begin
        Row.Get(1);
        Row.Qty := 11;
        Row.Modify(false);
        Row.Get(2);
        Row.Qty := 21;
        Row.Modify(false);
        Error('Amwr boom');
    end;

    local procedure ModifyOneDeleteOtherThenError()
    var
        Row: Record "Amwr Row";
    begin
        Row.Get(1);
        Row.Qty := 11;
        Row.Modify(false);
        Row.Get(2);
        Row.Delete(false);
        Error('Amwr boom');
    end;

    local procedure InsertTwoThenError()
    var
        Row: Record "Amwr Row";
    begin
        Row.Init();
        Row."Entry No." := 1;
        Row.Insert();
        Row."Entry No." := 2;
        Row.Insert();
        Error('Amwr boom');
    end;

    local procedure InsertOneThenFailingInsert()
    var
        Row: Record "Amwr Row";
    begin
        Row.Init();
        Row."Entry No." := 1;
        Row.Insert(true);
        Row."Entry No." := 2;
        Row."Fail OnInsert" := true;
        Row.Insert(true);
    end;

    local procedure ModifyCommitModifyThenError()
    var
        Row: Record "Amwr Row";
    begin
        Row.Get(1);
        Row.Qty := 11;
        Row.Modify(false);
        Commit();
        Row.Get(2);
        Row.Qty := 21;
        Row.Modify(false);
        Error('Amwr boom');
    end;
}
