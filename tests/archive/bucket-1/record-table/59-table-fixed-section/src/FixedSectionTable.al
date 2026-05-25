table 50182 "Fixed Section Test Table"
{
    DataClassification = ToBeClassified;

    fields
    {
        field(1; Id; Integer) { }
        field(2; Name; Text[100]) { }
        field(3; Amount; Decimal) { }
    }

    keys
    {
        key(PK; Id) { Clustered = true; }
    }

    fieldgroups
    {
        fieldgroup(DropDown; Id, Name) { }
        fieldgroup(Fixed; Id, Name, Amount) { }
    }
}

page 50013 "Fixed Section Page"
{
    ApplicationArea = All;
    PageType = Card;
    SourceTable = "Fixed Section Test Table";

    layout
    {
        area(Content)
        {
            fixed(grpFixed)
            {
                group(grpLeft)
                {
                    field(IdField; Rec.Id) { }
                    field(NameField; Rec.Name) { }
                }
                group(grpRight)
                {
                    field(AmountField; Rec.Amount) { }
                }
            }
        }
    }
}
