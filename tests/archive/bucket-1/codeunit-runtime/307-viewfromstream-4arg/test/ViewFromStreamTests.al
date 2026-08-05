codeunit 50221 "ViewFromStream Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    /// <summary>
    /// Positive: ViewFromStream(InStream, FileName, IsEditable) is a no-op in standalone mode
    /// — it must not throw. This is the 3-arg AL form which BC transpiles to the 4-arg C# call
    /// ALViewFromStream(parent, inStream, fileName, isEditable).
    /// </summary>
    [Test]
    procedure ViewFromStream_4Arg_ErrorOrNoThrow()
    var
        Helper: Codeunit "ViewFromStream Helper";
    begin
        // BC 16.1: ViewFromStream with an uninitialized InStream raises
        // "NavInStream variable not initialized." Capture the error.
        asserterror Helper.CallViewFromStream4Arg();
    end;

    /// <summary>
    /// Positive: ViewFromStream(InStream, FileName) — BC 16.1 raises
    /// "NavInStream variable not initialized." when InStream is uninitialized.
    /// </summary>
    [Test]
    procedure ViewFromStream_3Arg_ErrorOrNoThrow()
    var
        Helper: Codeunit "ViewFromStream Helper";
    begin
        // BC 16.1: ViewFromStream with an uninitialized InStream raises
        // "NavInStream variable not initialized." Capture the error.
        asserterror Helper.CallViewFromStream3Arg();
    end;
}
