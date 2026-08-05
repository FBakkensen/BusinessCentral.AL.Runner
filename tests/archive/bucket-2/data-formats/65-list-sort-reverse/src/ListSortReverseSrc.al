codeunit 50278 "LSR List Helper"
{
    procedure ReverseIntegerList(var Items: List of [Integer])
    begin
        Items.Reverse();
    end;

    procedure ReverseTextList(var Items: List of [Text])
    begin
        Items.Reverse();
    end;
}
