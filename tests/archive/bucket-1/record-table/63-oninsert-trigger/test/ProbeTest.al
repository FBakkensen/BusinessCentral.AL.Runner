codeunit 50585 "OI Probe Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "OI Probe Row";
        Rec2: Record "OI Counter";
        Rec3: Record "OI With Side Effect";
    begin
        Rec1.DeleteAll(false);
        Rec2.DeleteAll(false);
        Rec3.DeleteAll(false);
    end;

    [Test]
    procedure OnInsertSetsTraceField()
    var
        R: Record "OI Probe Row";
    begin
        Initialize();
        R.Id := 1;
        R.Insert(true);
        Assert.AreEqual('touched', R.Trace, 'OnInsert must populate Trace');
    end;

    [Test]
    procedure OnInsertWithoutRunTriggerFlagSkipsTrigger()
    var
        R: Record "OI Probe Row";
    begin
        Initialize();
        // Insert(false) — explicitly skip the trigger
        R.Id := 2;
        R.Insert(false);
        Assert.AreEqual('', R.Trace, 'Insert(false) must not run OnInsert');
    end;

    [Test]
    procedure OnInsertCounterIncrementsAcrossInserts()
    var
        A: Record "OI With Side Effect";
        B: Record "OI With Side Effect";
        Counter: Record "OI Counter";
    begin
        Initialize();
        A.Id := 1; A.Insert(true);
        B.Id := 2; B.Insert(true);
        Counter.Get(1);
        Assert.AreEqual(2, Counter.Count, 'OnInsert should have incremented the counter twice');
    end;
}
