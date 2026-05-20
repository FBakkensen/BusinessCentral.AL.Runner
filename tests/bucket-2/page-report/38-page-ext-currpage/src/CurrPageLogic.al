codeunit 50476 "CurrPage Logic"
{
    procedure GetStatus(var Rec: Record "CurrPage Table"): Text
    begin
        exit(Rec."Status");
    end;
}
