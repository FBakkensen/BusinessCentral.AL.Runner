codeunit 108002 "Trigger Recursion Test"
{
    Subtype = Test;

    [Test]
    procedure Modify_WithRecursiveTrigger_DoesNotStackOverflow()
    var
        Rec: Record "Recursive Trigger Table";
        Assert: Codeunit "Library Assert";
    begin
        // [GIVEN] A record in a table with a recursive OnModify trigger
        Rec.PK := 'TEST';
        Rec.Counter := 0;
        Rec.Insert(false);

        // [WHEN] Modify with runTrigger = true, which causes infinite recursion via OnModify.
        // [THEN] The runner's NavMethodScope depth guard fires and raises a runtime error
        //        instead of crashing the process with a StackOverflowException.
        asserterror Rec.Modify(true);
        Assert.ExpectedError('Maximum recursion depth');
    end;
}
