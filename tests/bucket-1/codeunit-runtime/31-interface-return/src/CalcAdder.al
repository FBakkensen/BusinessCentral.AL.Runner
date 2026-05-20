codeunit 50227 "Calc Adder" implements "ICalc"
{
    procedure Calculate(A: Decimal; B: Decimal): Decimal
    begin
        exit(A + B);
    end;
}
