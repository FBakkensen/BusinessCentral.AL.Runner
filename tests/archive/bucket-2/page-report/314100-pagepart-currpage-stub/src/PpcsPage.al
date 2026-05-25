// Regression test for issue #1597.
// A page that has a real part(MySubPage; ...) declaration and calls
// CurrPage.MySubPage.Page.DoSomething() in a trigger. The runner must NOT
// inject a stub usercontrol for MySubPage — it is already a PagePart.
// Without the fix, the runner injects a conflicting usercontrol and either
// produces AL0155 (duplicate member) or AL0132 (wrong return type).

page 50038 "Ppcs Host Page"
{
    PageType = Card;

    layout
    {
        area(Content)
        {
            part(MySubPage; "Ppcs Sub Page") { ApplicationArea = All; }
        }
    }

    trigger OnOpenPage()
    begin
        // Three-level access: CurrPage.<part>.<Page>.<method>()
        // The runner must recognise MySubPage as an existing part, not a missing usercontrol.
        CurrPage.MySubPage.Page.DoSomething();
    end;
}

page 50039 "Ppcs Sub Page"
{
    PageType = ListPart;

    procedure DoSomething()
    begin
    end;
}
