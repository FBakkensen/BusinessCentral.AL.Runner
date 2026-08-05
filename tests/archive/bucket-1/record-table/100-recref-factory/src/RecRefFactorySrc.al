codeunit 50396 "RecRef Factory Src"
{
    /// Uses an array of RecordRef variables, which causes the BC compiler
    /// to emit NavRecordRef.Factory (rewritten to MockRecordRef.Factory).
    procedure GetRecRefFromArray(): Integer
    var
        RecRefs: array[3] of RecordRef;
    begin
        // Open each RecordRef to a different local table (50000..50002 are valid in this bundle).
        RecRefs[1].Open(50000);
        RecRefs[2].Open(50001);
        RecRefs[3].Open(50002);

        // Return the table number of the second element to prove array works
        exit(RecRefs[2].Number);
    end;
}
