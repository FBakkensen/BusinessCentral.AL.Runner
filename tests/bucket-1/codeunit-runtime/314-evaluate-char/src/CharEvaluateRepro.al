codeunit 50243 "Char Evaluate Repro"
{
    procedure TryParseChar(Input: Text; var Result: Char): Boolean
    begin
        exit(Evaluate(Result, Input));
    end;
}
