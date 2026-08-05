codeunit 50250 "Char Evaluate Repro"
{
    procedure TryParseChar(Input: Text; var Result: Char): Boolean
    begin
        exit(Evaluate(Result, Input));
    end;
}
