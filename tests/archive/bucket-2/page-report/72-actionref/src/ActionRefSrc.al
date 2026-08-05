/// Helper codeunit to prove the compilation unit compiles correctly even when
/// it contains a page with actionref (promoted-action) declarations.
codeunit 50491 "AR Helper"
{
    procedure GetValue(): Text
    begin
        exit('ok');
    end;

    procedure Add(a: Integer; b: Integer): Integer
    begin
        exit(a + b);
    end;
}

/// A page that declares an actionref section (promoted action binding).
/// This is the construct that used to crash the runner's Roslyn compilation step.
page 50047 "AR Test Page"
{
    PageType = Card;

    actions
    {
        area(Promoted)
        {
            actionref(MyAction_Promoted; MyAction)
            {
            }
        }
        area(Processing)
        {
            action(MyAction)
            {
                ApplicationArea = All;
                trigger OnAction()
                begin
                end;
            }
        }
    }
}
