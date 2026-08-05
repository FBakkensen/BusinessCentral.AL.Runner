codeunit 50441 "Test CheckType"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;


    local procedure Initialize()
    var
        Rec1: Record "CheckType Base Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure CheckType_DiscountNonZero_ReturnsAmount()
    var
        Rec: Record "CheckType Base Table";
        Logic: Codeunit "CheckType Logic";
        Result: Decimal;
    begin
        Initialize();
        // Positive: non-zero discount % returns computed discount amount
        Rec.Init();
        Rec."No." := 'CT01';
        Rec."Amount" := 200;
        Rec."Line Discount %" := 10;
        Rec.Insert(true);

        Rec.Get('CT01');
        Result := Logic.CalcDiscountAmount(Rec);
        Assert.AreEqual(20, Result, 'Discount on 200 at 10% should be 20');
    end;

    [Test]
    procedure CheckType_DiscountZero_ReturnsZero()
    var
        Rec: Record "CheckType Base Table";
        Logic: Codeunit "CheckType Logic";
        Result: Decimal;
    begin
        Initialize();
        // Positive: zero discount % returns 0
        Rec.Init();
        Rec."No." := 'CT02';
        Rec."Amount" := 500;
        Rec."Line Discount %" := 0;
        Rec.Insert(true);

        Rec.Get('CT02');
        Result := Logic.CalcDiscountAmount(Rec);
        Assert.AreEqual(0, Result, 'Zero discount should return 0');
    end;

    [Test]
    procedure CheckType_ExtraCodeNonEmpty_ReturnsTrue()
    var
        Rec: Record "CheckType Base Table";
        Logic: Codeunit "CheckType Logic";
    begin
        Initialize();
        // Positive: non-empty Extra Code returns true
        Rec.Init();
        Rec."No." := 'CT03';
        Rec."Extra Code" := 'ABC';
        Rec.Insert(true);

        Rec.Get('CT03');
        Assert.IsTrue(Logic.HasExtraCode(Rec), 'Should return true for non-empty Extra Code');
    end;

    [Test]
    procedure CheckType_ExtraCodeEmpty_ReturnsFalse()
    var
        Rec: Record "CheckType Base Table";
        Logic: Codeunit "CheckType Logic";
    begin
        Initialize();
        // Negative: empty Extra Code returns false
        Rec.Init();
        Rec."No." := 'CT04';
        Rec.Insert(true);

        Rec.Get('CT04');
        Assert.IsFalse(Logic.HasExtraCode(Rec), 'Should return false for empty Extra Code');
    end;
}
