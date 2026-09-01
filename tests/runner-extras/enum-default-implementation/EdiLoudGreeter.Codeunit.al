namespace ALRunnerExtras.EnumDefaultImplementation;

codeunit 64631 "Edi Loud Greeter" implements "Edi Greeter"
{
    procedure Greet(): Text
    begin
        exit('LOUD');
    end;
}
