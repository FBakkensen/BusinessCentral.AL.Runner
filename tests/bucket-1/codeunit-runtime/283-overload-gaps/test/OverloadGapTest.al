codeunit 50203 "OGap Test"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;

    // ── FindSet(ForUpdate, ForceNewQuery) ────────────────────────────────────


    local procedure Initialize()
    var
        Rec1: Record "OGap Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure FindSet_TwoArg_ReturnsTrueWhenRecordExists()
    var
        Src: Codeunit "OGap Source";
        Rec: Record "OGap Table";
    begin
        Initialize();
        // [GIVEN] One record in the table
        Rec.Init();
        Rec."No." := 'F1';
        Rec.Insert();

        // [WHEN] FindSet(true, false) — ForUpdate=true, ForceNewQuery=false
        // [THEN] Returns true (record found)
        Assert.IsTrue(Src.FindSet_TwoArg(Rec), 'FindSet(true,false) must return true when records exist');
    end;

    [Test]
    procedure FindSet_TwoArg_ReturnsFalseWhenEmpty()
    var
        Src: Codeunit "OGap Source";
        Rec: Record "OGap Table";
    begin
        Initialize();
        // [GIVEN] No records matching the filter
        Rec.SetFilter("No.", 'NEVEREXISTS');

        // [WHEN/THEN] FindSet(true, false) returns false on empty set
        Assert.IsFalse(Src.FindSet_TwoArg(Rec), 'FindSet(true,false) must return false when no records match');
    end;

    // ── Insert(RunTrigger, CheckMandatoryFields) ─────────────────────────────

    [Test]
    procedure Insert_TwoArg_InsertsRecord()
    var
        Src: Codeunit "OGap Source";
        Rec: Record "OGap Table";
    begin
        Initialize();
        // [GIVEN] A new record
        Rec.Init();
        Rec."No." := 'I1';

        // [WHEN] Insert(true, true)
        Src.Insert_TwoArg(Rec);

        // [THEN] Record exists in table
        Rec.Reset();
        Rec.SetFilter("No.", 'I1');
        Assert.IsTrue(Rec.FindFirst(), 'Insert(true,true) must persist the record');
    end;

    [Test]
    procedure Insert_TwoArg_DuplicateErrors()
    var
        Rec: Record "OGap Table";
    begin
        Initialize();
        Rec.DeleteAll();
        // [GIVEN] A record already inserted and committed
        Rec.Init();
        Rec."No." := 'DUP';
        Rec.Insert();

        // [WHEN] A fresh Insert(true,true) with same key + Commit to flush cache
        // [THEN] Either the insert or the commit throws "already exists"
        asserterror InsertTwoArgDuplicate();
        Assert.ExpectedError('already exists');
    end;

    local procedure InsertTwoArgDuplicate()
    var
        Src: Codeunit "OGap Source";
        DupRec: Record "OGap Table";
    begin
        DupRec.Init();
        DupRec."No." := 'DUP';
        Src.Insert_TwoArg(DupRec);
        Commit();
    end;
}
