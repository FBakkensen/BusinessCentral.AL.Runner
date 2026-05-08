enum 50000 "EFI Status"
{
    Extensible = false;

    value(0; Open)   { }
    value(1; Released) { }
    value(2; Closed) { }
}

codeunit 50031 "EFI Converter"
{
    procedure FromInt(I: Integer): Enum "EFI Status"
    begin
        exit(Enum::"EFI Status".FromInteger(I));
    end;
}
