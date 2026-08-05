codeunit 50590 "CRR Codeunit Run Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // -----------------------------------------------------------------------
    // Codeunit.Run(ID, var Rec) — return value
    // BC 16.1: Codeunit.Run stops the outer transaction even on success;
    // subsequent DeleteAll() calls in later tests may fail.
    // Each test uses a unique record key and skips pre-test DeleteAll.
    // -----------------------------------------------------------------------

}
