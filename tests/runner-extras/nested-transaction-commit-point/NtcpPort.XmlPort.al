xmlport 64652 "Ntcp Port"
{
    Direction = Both;
    UseRequestPage = false;
    schema
    {
        textelement(root)
        {
            tableelement(Row_; "Ntcp Row")
            {
                XmlName = 'Row';
                fieldelement(EntryNo; Row_."Entry No.") { }
                fieldelement(RowName; Row_.Name) { }
            }
        }
    }
}
