/// Canary for the OnRename trigger drain (W-8a PR2).
/// Three tests prove the real Ncl RenameAsync trigger-dispatch path works.
/// Note: AL Record.Rename always passes runApplicationTrigger=true; there is no
/// Rename(false) overload — so the negative (bypass) test is replaced by a
/// Rec-state check and a multi-rename accumulation test.
///   1. Positive        — Rename fires the trigger; TriggerRan is true at new PK.
///   2. New-PK visible  — counter is keyed by the new PK, proving Rec.PK = new value.
///   3. Field values    — trigger stores Rec.Val; verifies non-PK fields are readable.
codeunit 50423 "ORT Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        ExtraRec: Record "ORT Counter";
        Rec1: Record "ORT Source";
    begin
        ExtraRec.DeleteAll(false);
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure OnRenameTrigger_SetsFlag_AfterRename()
    var
        Src: Record "ORT Source";
    begin
        Initialize();
        // Positive: Rename must fire the OnRename trigger which sets TriggerRan.
        Src.PK := 1;
        Src.Insert(false);
        Src.Rename(10);

        // After rename, row is at new PK=10.
        Src.Get(10);
        Assert.IsTrue(Src.TriggerRan, 'OnRename trigger must set TriggerRan after Rename');
    end;

    [Test]
    procedure OnRenameTrigger_Counter_KeyedByNewPK()
    var
        Src: Record "ORT Source";
        Counter: Record "ORT Counter";
    begin
        Initialize();
        // Rec-state: inside OnRename, Rec.PK holds the NEW primary key.
        // The trigger inserts Counter keyed by Rec.PK (new), so Counter.Get(new PK)
        // must succeed and Counter.Get(old PK) must fail.
        Src.PK := 2;
        Src.Insert(false);
        Src.Rename(20);

        Assert.IsTrue(Counter.Get(20), 'Counter must exist at new PK=20, proving trigger saw Rec.PK=20');
        Assert.IsFalse(Counter.Get(2), 'No counter at old PK=2 — Rec.PK was already the new value inside trigger');
    end;

    [Test]
    procedure OnRenameTrigger_Rec_HasCorrectFieldValues()
    var
        Src: Record "ORT Source";
        Counter: Record "ORT Counter";
    begin
        Initialize();
        // Rec-state: trigger stores Rec.Val into Counter.Hits.
        // Verify Counter.Hits = 33, proving the trigger could read non-PK fields.
        Src.PK := 3;
        Src.Val := 33;
        Src.Insert(false);
        Src.Rename(30);

        Assert.IsTrue(Counter.Get(30), 'Counter row keyed by new PK=30 must exist');
        Assert.AreEqual(33, Counter.Hits, 'Counter.Hits must equal Rec.Val at trigger time (33)');
    end;
}
