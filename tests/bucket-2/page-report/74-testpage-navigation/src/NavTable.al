table 50103 "TPN Test Record"
{
    fields
    {
        field(1; Id; Integer) { }
        field(2; Name; Text[100]) { }
    }
    keys { key(PK; Id) { Clustered = true; } }
}

page 50051 "TPN Test Card"
{
    PageType = Card;
    SourceTable = "TPN Test Record";

    layout
    {
        area(Content)
        {
            field(NameField; Rec.Name) { }
        }
    }
}
