codeunit 64654 "Ntcp Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    // ── Plain nested transactions: NOT a commit point (real BC: rolled back) ──────────

    [Test]
    procedure ModifyThenQueryOpenThenError_ModifyRolledBack()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        asserterror ModifyQueryError();
        if GetLastErrorText() <> 'Ntcp boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        Row.Get(1);
        if Row.Qty <> 10 then
            Error('row 1 not rolled back: Qty=%1', Row.Qty);
    end;

    [Test]
    procedure InsertThenQueryOpenThenError_InsertRolledBack()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        asserterror InsertQueryError();
        if GetLastErrorText() <> 'Ntcp boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        if Row.Get(7) then
            Error('row 7 survived the rollback');
    end;

    [Test]
    procedure UncommittedInsert_QueryOpen_UnrelatedAssertError_RowRolledBack()
    var
        Row: Record "Ntcp Row";
        Q: Query "Ntcp Query";
    begin
        Seed();
        Row."Entry No." := 8;
        Row.Insert();
        Q.Open();
        Q.Read();
        Q.Close();
        asserterror Error('unrelated');
        if Row.Get(8) then
            Error('row 8 survived: Query.Open must not move the commit point');
    end;

    [Test]
    procedure StatementImportSucceeds_UnrelatedAssertError_RowRolledBack()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        ImportRowStatement(11);
        if not Row.Get(11) then
            Error('statement-form XmlPort.Import must insert row 11');
        asserterror Error('unrelated');
        if Row.Get(11) then
            Error('row 11 survived: statement-form XmlPort.Import must not move the commit point');
    end;

    [Test]
    procedure StatementRunSucceeds_UnrelatedAssertError_RowRolledBack()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        Codeunit.Run(Codeunit::"Ntcp Helper");
        if not Row.Get(900) then
            Error('statement-form Codeunit.Run must insert row 900');
        asserterror Error('unrelated');
        if Row.Get(900) then
            Error('row 900 survived: statement-form Codeunit.Run must not move the commit point');
    end;

    // ── Transaction worlds: a durable commit point (real BC: rows stay) ──────────────

    [Test]
    procedure GuardedRunSucceeds_UnrelatedAssertError_RowStays()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        if not Codeunit.Run(Codeunit::"Ntcp Helper") then
            Error('helper failed: %1', GetLastErrorText());
        asserterror Error('unrelated');
        if not Row.Get(900) then
            Error('row 900 lost: a guarded Codeunit.Run that succeeds is a commit');
    end;

    [Test]
    procedure GuardedRunInsideFailingStatement_RowStays()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        asserterror GuardedRunThenError();
        if GetLastErrorText() <> 'Ntcp boom' then
            Error('unexpected error text: %1', GetLastErrorText());
        if not Row.Get(900) then
            Error('row 900 lost: a guarded Codeunit.Run commits even when the enclosing statement then fails');
    end;

    [Test]
    procedure TrapImportSucceeds_UnrelatedAssertError_RowStays()
    var
        Row: Record "Ntcp Row";
    begin
        Seed();
        if not ImportRowTrap(10) then
            Error('import failed: %1', GetLastErrorText());
        asserterror Error('unrelated');
        if not Row.Get(10) then
            Error('row 10 lost: Ok := XmlPort.Import(...) that succeeds is a commit');
    end;

    local procedure ModifyQueryError()
    var
        Row: Record "Ntcp Row";
        Q: Query "Ntcp Query";
    begin
        Row.Get(1);
        Row.Qty := 11;
        Row.Modify(false);
        Q.Open();
        Q.Read();
        Q.Close();
        Error('Ntcp boom');
    end;

    local procedure InsertQueryError()
    var
        Row: Record "Ntcp Row";
        Q: Query "Ntcp Query";
    begin
        Row."Entry No." := 7;
        Row.Insert();
        Q.Open();
        Q.Read();
        Q.Close();
        Error('Ntcp boom');
    end;

    local procedure GuardedRunThenError()
    begin
        if not Codeunit.Run(Codeunit::"Ntcp Helper") then
            Error('helper failed: %1', GetLastErrorText());
        Error('Ntcp boom');
    end;

    local procedure ImportRowTrap(EntryNo: Integer): Boolean
    var
        InStr: InStream;
    begin
        PreparePayload(EntryNo, InStr);
        exit(XmlPort.Import(64652, InStr));
    end;

    local procedure ImportRowStatement(EntryNo: Integer)
    var
        InStr: InStream;
    begin
        PreparePayload(EntryNo, InStr);
        XmlPort.Import(64652, InStr);
    end;

    local procedure PreparePayload(EntryNo: Integer; var InStr: InStream)
    var
        Carrier: Record "Ntcp Row";
        OutStr: OutStream;
    begin
        Carrier.Payload.CreateOutStream(OutStr);
        OutStr.WriteText('<?xml version="1.0" encoding="utf-8"?><root><Row><EntryNo>' + Format(EntryNo) + '</EntryNo><RowName>X</RowName></Row></root>');
        Carrier.Payload.CreateInStream(InStr);
    end;

    local procedure Seed()
    var
        Row: Record "Ntcp Row";
    begin
        Row.DeleteAll();
        Row."Entry No." := 1;
        Row.Qty := 10;
        Row.Insert();
        Commit();
    end;
}
