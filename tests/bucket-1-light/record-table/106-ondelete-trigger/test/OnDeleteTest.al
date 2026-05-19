/// Canary for the OnDelete trigger drain (W-8a PR2).
/// Three tests prove the real Ncl DeleteAsync trigger-dispatch path works:
///   1. Positive    — Delete(true) fires the trigger (counter row created).
///   2. Negative    — Delete(false) skips the trigger; right-reason counter check.
///   3. Rec state   — trigger sees the correct Rec.Val before the row is gone.
codeunit 100018 "ODT Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;

    [Test]
    procedure OnDeleteTrigger_CounterCreated_AfterDelete()
    var
        Src: Record "ODT Source";
        Counter: Record "ODT Counter";
    begin
        // Positive: Delete(true) must fire OnDelete which inserts Counter(PK=1).
        Src.PK := 1;
        Src.Val := 0;
        Src.Insert(false);
        Src.Delete(true);

        Assert.IsTrue(Counter.Get(1), 'OnDelete trigger must have inserted Counter row PK=1');
    end;

    [Test]
    procedure OnDeleteTrigger_WithoutRunTrigger_DoesNotFire()
    var
        Src: Record "ODT Source";
        Counter: Record "ODT Counter";
    begin
        // Negative: Delete(false) must NOT fire the AL trigger.
        Src.PK := 2;
        Src.Insert(false);
        Src.Delete(false);

        // Right-reason check: no counter row means the trigger body never ran.
        Assert.IsFalse(Counter.Get(2), 'OnDelete trigger body must not run when runTrigger=false (Counter row PK=2 must not exist)');
    end;

    [Test]
    procedure OnDeleteTrigger_Rec_HasCorrectFieldValues()
    var
        Src: Record "ODT Source";
        Counter: Record "ODT Counter";
    begin
        // Rec-state: trigger stores Rec.Val into Counter.Hits.
        // Verify Counter.Hits = 55, proving the trigger could read the field value
        // from the record at the point it was deleted.
        Src.PK := 3;
        Src.Val := 55;
        Src.Insert(false);
        Src.Delete(true);

        Assert.IsTrue(Counter.Get(3), 'Counter row keyed by Rec.PK must exist after Delete(true)');
        Assert.AreEqual(55, Counter.Hits, 'Counter.Hits must equal Rec.Val at trigger time (55)');
    end;
}
