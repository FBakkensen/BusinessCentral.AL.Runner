page 50001 "Inv Ext Pg"
{
    ApplicationArea = All;
    PageType = Card;
    SourceTable = "Inv Ext Tbl";

    layout
    {
        area(Content)
        {
            field(EntryNo; Rec."Entry No.") { }
        }
    }

    procedure GetBaseNumber(): Integer
    begin
        exit(100);
    end;
}
