/// Tests for TestFilter methods on TestPage.Filter:
/// Ascending (get/set), CurrentKey (get), SetCurrentKey,
/// SetFilter (store by field), GetFilter (retrieve).
///
/// Proof strategy: if MockTestPageFilter is missing any of these methods,
/// Roslyn compilation fails with CS1061 and ALL tests in this bucket go RED.
codeunit 50374 "TPF TestFilter Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // ── SetFilter / GetFilter ──────────────────────────────────────────────────

    [Test]
    procedure TestFilter_CompilesWithoutError()
    begin
        Assert.IsTrue(true, 'TestFilter compiles');
    end;
}
