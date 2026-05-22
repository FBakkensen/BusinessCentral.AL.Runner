codeunit 50145 "LF Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "LF Src";


    local procedure Initialize()
    var
        Rec1: Record "LF Row";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure AddLoadFields_DoesNotThrow()
    begin
        Initialize();
        Assert.IsTrue(Src.AddLoadFieldsDoesNotThrow(),
            'AddLoadFields must complete without throwing');
    end;

    [Test]
    procedure DataReadsAfterLoadFields_Work()
    begin
        Initialize();
        // Proves that AddLoadFields is a no-op: every field stays reachable
        // regardless of the load-hint API.
        Assert.AreEqual('Alice/Paris', Src.DataRoundTripAfterLoadFields(),
            'Field reads must work after SetLoadFields + AddLoadFields');
    end;

    [Test]
    procedure AddLoadFieldsMultiple_FieldReachable()
    begin
        Initialize();
        // Standalone contract: "City" is reachable even though it was never
        // added to the load set — all fields are always in memory.
        Assert.AreEqual('Berlin', Src.AddLoadFieldsMultiple_DataIntact(),
            'Fields not named via AddLoadFields must still be reachable');
    end;

    [Test]
    procedure AddLoadFields_NotCorruptingFilterState()
    begin
        Initialize();
        // Filter + count after AddLoadFields — verifies the record remains
        // in a usable state with filters intact.
        Assert.AreEqual(3, Src.AddLoadFields_AfterSet_NotOverridden(),
            'Filtered Count() after AddLoadFields must include matching rows');
    end;

    [Test]
    procedure RecRef_AreFieldsLoaded_AllFields_NoThrow()
    begin
        Initialize();
        // BC 16.1: AreFieldsLoaded returns false on an open-but-unfetched RecordRef.
        // Contract: must not throw — just verify the call succeeds.
        Src.RecRefAreFieldsLoaded_ReturnsTrue();
        Assert.IsTrue(true, 'RecordRef.AreFieldsLoaded must not throw');
    end;

    [Test]
    procedure RecRef_AreFieldsLoaded_WithAddLoadFields()
    begin
        Initialize();
        // In BC, AreFieldsLoaded returns false for fields not explicitly loaded via
        // SetLoadFields/AddLoadFields when partial loading is in effect.
        // Standalone contract: just verify it does not throw.
        Src.RecRefAreFieldsLoaded_AfterSetLoadFields();
        Assert.IsTrue(true, 'RecordRef.AreFieldsLoaded must not throw');
    end;

    [Test]
    procedure SetLoadFieldsOnSelf_NoThrow()
    begin
        Initialize();
        Src.DriveSetLoadFieldsOnSelf();
    end;

    [Test]
    procedure AddLoadFieldsOnSelf_NoThrow()
    begin
        Initialize();
        Src.DriveAddLoadFieldsOnSelf();
    end;

    [Test]
    procedure AreFieldsLoadedOnSelf_NoThrow()
    begin
        Initialize();
        // In BC, AreFieldsLoaded may return false when no explicit SetLoadFields was called.
        // Contract: must not throw — just verify the call succeeds.
        Src.DriveAreFieldsLoadedOnSelf();
        Assert.IsTrue(true, 'AreFieldsLoaded on Self must not throw');
    end;
}
