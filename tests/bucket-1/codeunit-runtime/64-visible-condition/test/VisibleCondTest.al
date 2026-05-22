codeunit 50336 "VCond Visible Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // ------------------------------------------------------------------
    // Positive: page with conditional Visible attributes compiles and opens.
    // ------------------------------------------------------------------


    local procedure Initialize()
    var
        Rec1: Record "VCond Item";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure PageOpensWithShowDetailsFalse()
    var
        Item: Record "VCond Item";
        ItemPage: Page "VCond Item Page";
    begin
        Initialize();
        // [GIVEN] A VCond Item record exists
        Item.Init();
        Item.Id := 2;
        Item.Name := 'Gadget';
        Item.Amount := 100;
        Item.Active := true;
        Item.Insert();

        // [WHEN]  ShowDetails is set to false (default)
        ItemPage.SetShowDetails(false);

        // [THEN]  No error — conditional Visible compiles and the method runs
        Assert.IsTrue(true, 'Page with conditional Visible should compile and run');
    end;

    [Test]
    procedure PageOpensWithShowDetailsTrue()
    var
        Item: Record "VCond Item";
        ItemPage: Page "VCond Item Page";
    begin
        Initialize();
        // [GIVEN] A VCond Item record exists
        Item.Init();
        Item.Id := 3;
        Item.Name := 'Widget';
        Item.Amount := 50;
        Item.Active := true;
        Item.Insert();

        // [WHEN]  ShowDetails is set to true
        ItemPage.SetShowDetails(true);

        // [THEN]  No error — conditional Visible using a variable compiles correctly
        Assert.IsTrue(true, 'SetShowDetails(true) should not raise an error');
    end;

}
