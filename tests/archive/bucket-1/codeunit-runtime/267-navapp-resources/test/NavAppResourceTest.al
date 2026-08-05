/// Tests for NavApp resource method stubs (GetResourceAsText, GetResourceAsJson, ListResources).
codeunit 50194 "NavApp Resource Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // ------------------------------------------------------------------
    // Positive: methods return safe defaults in standalone mode.
    // ------------------------------------------------------------------

    [Test]
    procedure GetResourceAsText_MissingResource_Throws()
    var
        Src: Codeunit "NavApp Resource Src";
    begin
        // [GIVEN] Resource does not exist in the app bundle
        // [WHEN] GetResourceAsText is called for a non-existent resource
        // [THEN] BC throws "could not be found"
        asserterror Src.GetTextResource('nonexistent.txt');
        Assert.ExpectedError('could not be found');
    end;

    [Test]
    procedure GetResourceAsJson_MissingResource_Throws()
    var
        Src: Codeunit "NavApp Resource Src";
    begin
        // [GIVEN] Resource does not exist in the app bundle
        // [WHEN] GetResourceAsJson is called for a non-existent resource
        // [THEN] BC throws "could not be found"
        asserterror Src.GetJsonResource('nonexistent.json');
        Assert.ExpectedError('could not be found');
    end;

    [Test]
    procedure ListResources_ReturnsEmpty()
    var
        Src: Codeunit "NavApp Resource Src";
    begin
        // [GIVEN] No .app is loaded
        // [WHEN] ListResources is called
        // [THEN] Returns 0 resources — no exception
        Assert.AreEqual(0, Src.ListAllResources(), 'ListResources must return empty list in standalone mode');
    end;

    // ------------------------------------------------------------------
    // Negative: calling twice still returns empty (no state leak).
    // ------------------------------------------------------------------

    [Test]
    procedure GetResourceAsText_CalledTwice_BothThrow()
    var
        Src: Codeunit "NavApp Resource Src";
    begin
        // Both calls should throw "could not be found" — BC does not return empty for missing resources.
        asserterror Src.GetTextResource('a.txt');
        Assert.ExpectedError('could not be found');
        asserterror Src.GetTextResource('b.txt');
        Assert.ExpectedError('could not be found');
    end;
}
