/// Canary for the OnModify trigger drain (W-8a PR3).
/// Three tests prove the real Ncl ModifyAsync trigger-dispatch path works:
///   1. Positive    — Modify(true) fires the trigger (counter row created).
///   2. Negative    — Modify(false) skips the trigger; right-reason counter check.
///   3. Rec state   — trigger sees the correct (updated) Rec.Val at modify time.
codeunit 50403 "OMT Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        ExtraRec: Record "OMT Counter";
        Rec1: Record "OMT Source";
    begin
        ExtraRec.DeleteAll(false);
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure OnModifyTrigger_CounterCreated_AfterModify()
    var
        Src: Record "OMT Source";
        Counter: Record "OMT Counter";
    begin
        Initialize();
        // Positive: Modify(true) must fire OnModify which inserts Counter(PK=1).
        Src.PK := 1;
        Src.Val := 0;
        Src.Insert(false);
        Src.Val := 1;
        Src.Modify(true);

        Assert.IsTrue(Counter.Get(1), 'OnModify trigger must have inserted Counter row PK=1');
    end;

    [Test]
    procedure OnModifyTrigger_WithoutRunTrigger_DoesNotFire()
    var
        Src: Record "OMT Source";
        Counter: Record "OMT Counter";
    begin
        Initialize();
        // Negative: Modify(false) must NOT fire the AL trigger.
        Src.PK := 2;
        Src.Val := 0;
        Src.Insert(false);
        Src.Val := 2;
        Src.Modify(false);

        // Right-reason check: no counter row means the trigger body never ran.
        Assert.IsFalse(Counter.Get(2), 'OnModify trigger body must not run when runTrigger=false (Counter row PK=2 must not exist)');
    end;

    [Test]
    procedure OnModifyTrigger_Rec_HasCorrectFieldValues()
    var
        Src: Record "OMT Source";
        Counter: Record "OMT Counter";
    begin
        Initialize();
        // Rec-state: trigger stores Rec.Val into Counter.Hits.
        // Verify Counter.Hits = 77, proving the trigger sees the updated value
        // (Rec holds the new state at the point the trigger fires).
        Src.PK := 3;
        Src.Val := 0;
        Src.Insert(false);
        Src.Val := 77;
        Src.Modify(true);

        Assert.IsTrue(Counter.Get(3), 'Counter row keyed by Rec.PK must exist after Modify(true)');
        Assert.AreEqual(77, Counter.Hits, 'Counter.Hits must equal Rec.Val at trigger time (77)');
    end;
}
