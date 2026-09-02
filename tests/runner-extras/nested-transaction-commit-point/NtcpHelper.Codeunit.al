codeunit 64653 "Ntcp Helper"
{
    trigger OnRun()
    var
        Row: Record "Ntcp Row";
    begin
        Row."Entry No." := 900;
        Row.Qty := 9;
        Row.Insert();
    end;
}
