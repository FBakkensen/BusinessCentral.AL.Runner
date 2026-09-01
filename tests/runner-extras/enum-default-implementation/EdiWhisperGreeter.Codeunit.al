namespace ALRunnerExtras.EnumDefaultImplementation;

codeunit 64634 "Edi Whisper Greeter" implements "Edi Greeter"
{
    procedure Greet(): Text
    begin
        exit('whisper');
    end;
}
