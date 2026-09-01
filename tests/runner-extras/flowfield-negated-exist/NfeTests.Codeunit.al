namespace ALRunnerExtras.NegatedExistFlowField;

using Microsoft.Inventory.Item;
using Microsoft.Inventory.Costing;

codeunit 64591 "Nfe Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "Nfe Assert";

    [Test]
    procedure NegatedExist_NoMatchingRow_IsTrue()
    var
        Item: Record Item;
    begin
        CreateItem(Item, 'NFE-NONE');

        Assert.IsTrue(Item.CalcFields("Cost is Posted to G/L"), 'CalcFields must report success for -exist with no matching row');
        Assert.IsTrue(Item."Cost is Posted to G/L", '-exist must be true when no "Post Value Entry to G/L" row matches');
    end;

    [Test]
    procedure NegatedExist_MatchingRow_IsFalse()
    var
        Item: Record Item;
        PostValueEntryToGL: Record "Post Value Entry to G/L";
    begin
        CreateItem(Item, 'NFE-ONE');

        PostValueEntryToGL.Init();
        PostValueEntryToGL."Value Entry No." := 64591;
        PostValueEntryToGL."Item No." := Item."No.";
        PostValueEntryToGL."Posting Date" := WorkDate();
        PostValueEntryToGL.Insert();

        Assert.IsTrue(Item.CalcFields("Cost is Posted to G/L"), 'CalcFields must report success for -exist with a matching row');
        Assert.IsFalse(Item."Cost is Posted to G/L", '-exist must be false when a "Post Value Entry to G/L" row matches');
    end;

    [Test]
    procedure NegatedExist_MatchingRowForOtherItem_IsTrue()
    var
        Item: Record Item;
        OtherItem: Record Item;
        PostValueEntryToGL: Record "Post Value Entry to G/L";
    begin
        CreateItem(Item, 'NFE-MINE');
        CreateItem(OtherItem, 'NFE-OTHER');

        PostValueEntryToGL.Init();
        PostValueEntryToGL."Value Entry No." := 64592;
        PostValueEntryToGL."Item No." := OtherItem."No.";
        PostValueEntryToGL."Posting Date" := WorkDate();
        PostValueEntryToGL.Insert();

        Assert.IsTrue(Item.CalcFields("Cost is Posted to G/L"), 'CalcFields must report success');
        Assert.IsTrue(Item."Cost is Posted to G/L", 'the where("Item No." = field("No.")) filter must exclude the other item''s row');
    end;

    local procedure CreateItem(var Item: Record Item; ItemNo: Code[20])
    begin
        Item.Init();
        Item."No." := ItemNo;
        Item.Insert();
    end;
}
