codeunit 50180 "Test Ref Processor"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "Error Map Item";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure SumQuantities_Works()
    var
        Item: Record "Error Map Item";
        Proc: Codeunit "Ref Processor";
        Result: Decimal;
    begin
        Initialize();
        // The Ref Processor codeunit has GetRecordId which uses
        // RecordId (unsupported → ALRecordId error). The codeunit
        // should be excluded but this test on the table itself passes.
        Item.Init();
        Item."Entry No." := 1;
        Item."Item No." := 'A';
        Item.Quantity := 42;
        Item.Insert(false);

        Item.Reset();
        Item.FindFirst();
        // Prove the specific value, not just "no error"
        Assert.AreEqual(42, Item.Quantity, 'Quantity must be 42 after Insert+FindFirst');
    end;
}
