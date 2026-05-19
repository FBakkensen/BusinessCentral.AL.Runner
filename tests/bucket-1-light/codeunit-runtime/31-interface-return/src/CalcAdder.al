codeunit 50220 "Calc Adder" implements "ICalc"
{
    procedure Calculate(A: Decimal; B: Decimal): Decimal
    begin
        exit(A + B);
    end;
}
