/// <summary>
/// One method per ExpectationManifest classification path (#1734). Method names
/// are the selection mechanism: the integration test runs `--test GreenPath` to
/// prove the reclassifying paths exit 0, then the whole codeunit to prove both
/// drift directions fail the run loudly.
/// </summary>
codeunit 60810 "Expct Fixture Tests"
{
    Subtype = Test;

    // ── Green paths: with the paired manifest these four must land the run at exit 0 ──

    [Test]
    procedure GreenPath_PlainPass()
    begin
        // No manifest entry; a normal pass. Concrete assertion so the method
        // cannot pass vacuously.
        if 1 + 2 <> 3 then
            Error('arithmetic broke: 1 + 2 <> 3');
    end;

    [Test]
    procedure GreenPath_OosDeclared()
    begin
        // Manifest declares expect-oos with the matching reason → pass-oos.
        OpenRightOuterJoinQuery();
    end;

    [Test]
    procedure GreenPath_KnownGapDeclared()
    begin
        // Manifest declares expect-fail-known-gap → pass-known-gap.
        Error('simulated known gap: surface not yet implemented in the runner');
    end;

    [Test]
    procedure GreenPath_SkipDeclared()
    begin
        // Manifest declares skip → the runner must never invoke this body.
        // The integration test asserts this marker is ABSENT from the output.
        Error('SKIP-DECLARED TEST BODY RAN - skip must prevent invocation');
    end;

    // ── Drift paths: each must FAIL the run with the documented diagnostic ──

    [Test]
    procedure Drift_OosEntryButPasses()
    begin
        // Manifest declares expect-oos but the test passes cleanly →
        // "Remove the entry … runner now supports this surface."
        if 2 * 2 <> 4 then
            Error('arithmetic broke: 2 * 2 <> 4');
    end;

    [Test]
    procedure Drift_KnownGapEntryButPasses()
    begin
        // Manifest declares expect-fail-known-gap but the test passes cleanly →
        // "Remove the entry … and close the linked issue."
        if 10 div 2 <> 5 then
            Error('arithmetic broke: 10 div 2 <> 5');
    end;

    [Test]
    procedure Drift_OosThrownButNoEntry()
    begin
        // No manifest entry, but the runner throws RunnerOutOfScopeException →
        // "Add an expect-oos entry … or implement the surface."
        OpenRightOuterJoinQuery();
    end;

    local procedure OpenRightOuterJoinQuery()
    var
        RightJoin: Query "Expct Right Join";
    begin
        // Deliberately NOT wrapped in asserterror: the typed
        // RunnerOutOfScopeException must reach the test executor uncaught so
        // the manifest classification sees it.
        RightJoin.Open();
        RightJoin.Read();
        Error('RightOuterJoin query unexpectedly opened - the OOS surface is gone');
    end;
}
