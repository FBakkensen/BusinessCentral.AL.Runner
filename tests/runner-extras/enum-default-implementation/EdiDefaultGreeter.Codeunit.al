namespace ALRunnerExtras.EnumDefaultImplementation;

codeunit 64630 "Edi Default Greeter" implements "Edi Greeter"
{
    procedure Greet(): Text
    begin
        exit('default');
    end;
}
