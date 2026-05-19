enum 50009 "EI Color"
{
    Extensible = false;

    value(0; "Red")
    {
    }
    value(1; "Green")
    {
    }
    value(2; "Blue")
    {
    }
}

codeunit 50325 "EI Enum Converter"
{
    procedure ToInteger(C: Enum "EI Color"): Integer
    begin
        exit(C.AsInteger());
    end;
}
