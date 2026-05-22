codeunit 50200 "CE Test"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;
        Src: Codeunit "CE Src";

    // ── Source-side [ErrorBehavior(Collect)] (baseline — already proven) ────────

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure SourceSideCollect_CollectsError()
    begin
        // Positive: [ErrorBehavior(Collect)] on a source method collects the error.
        // BC 16.1: test must also be in [ErrorBehavior(Collect)] scope for collection to work.
        Src.RaiseOneInCollectContext('hello');
        Assert.IsTrue(HasCollectedErrors(), 'Error must be collected after source-side Collect call');
        ClearCollectedErrors();
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure SourceSideCollect_MessagePreserved()
    begin
        // Positive: collected error message is preserved.
        // BC 16.1: test must also be in [ErrorBehavior(Collect)] scope.
        Src.RaiseOneInCollectContext('specific-value');
        Assert.AreEqual('specific-value', Src.GetFirstMessage(), 'Collected error message must equal set value');
        ClearCollectedErrors();
    end;

    // ── Test-side [ErrorBehavior(Collect)] ─────────────────────────────────────

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure TestSideCollect_CollectsTwoErrors()
    begin
        // Positive: [ErrorBehavior(Collect)] on the TEST procedure activates collecting mode
        // so that collectible errors from called code are collected, not thrown.
        Src.RaiseTwoErrors();
        Assert.AreEqual(2, Src.CountCollectedErrors(), 'Both collectible errors must be collected');
        // Clear at end so BC does not re-raise collected errors as "Multiple errors occurred"
        ClearCollectedErrors();
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure TestSideCollect_IsCollectingErrors()
    begin
        // Positive: IsCollectingErrors() must return true inside [ErrorBehavior(Collect)] test.
        Assert.IsTrue(Src.IsCollecting(), 'IsCollectingErrors must be true inside ErrorBehavior::Collect');
        ClearCollectedErrors();
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure TestSideCollect_MessagePreserved()
    begin
        // Positive: first collected error has the correct message.
        Src.RaiseTwoErrors();
        Assert.AreEqual('First error', Src.GetFirstMessage(), 'First collected error message must be correct');
        // Clear at end so BC does not re-raise collected errors as "Multiple errors occurred"
        ClearCollectedErrors();
    end;

    // ── HasCollectedErrors / ClearCollectedErrors ──────────────────────────────

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure AfterClear_HasCollectedErrors_IsFalse()
    begin
        // Positive: after ClearCollectedErrors the state is clean.
        // [ErrorBehavior(Collect)] activates collection so ClearCollectedErrors resets to false.
        ClearCollectedErrors();
        Assert.IsFalse(HasCollectedErrors(), 'HasCollectedErrors must be false after ClearCollectedErrors');
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure ClearCollectedErrors_EmptiesCollection()
    begin
        // Positive: after ClearCollectedErrors the collection is empty.
        // BC 16.1: test must be in [ErrorBehavior(Collect)] scope for collection to work.
        Src.RaiseOneInCollectContext('to be cleared');
        ClearCollectedErrors();
        Assert.IsFalse(HasCollectedErrors(), 'HasCollectedErrors must be false after ClearCollectedErrors');
    end;

    [Test]
    procedure IsCollectingErrors_FalseOutside()
    begin
        // Ensure no leftover collected errors from prior tests
        ClearCollectedErrors();
        // Negative: IsCollectingErrors must be false outside any [ErrorBehavior(Collect)] scope.
        Assert.IsFalse(IsCollectingErrors(), 'IsCollectingErrors must be false outside collect scope');
    end;

    // ── GetCollectedErrors count ───────────────────────────────────────────────

    [Test]
    procedure GetCollectedErrors_ZeroWithoutCollection()
    begin
        // Ensure no leftover collected errors from prior tests
        ClearCollectedErrors();
        // Positive: GetCollectedErrors returns empty list when nothing collected.
        Assert.AreEqual(0, Src.CountCollectedErrors(), 'CountCollectedErrors must be 0 before collection');
    end;
}
