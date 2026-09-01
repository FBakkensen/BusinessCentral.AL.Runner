namespace ALRunnerExtras.EnumDefaultImplementation;

enum 64632 "Edi Greeting" implements "Edi Greeter"
{
    Extensible = true;
    DefaultImplementation = "Edi Greeter" = "Edi Default Greeter";

    value(0; Default) { }
    value(1; Quiet) { }
    value(2; Loud)
    {
        Implementation = "Edi Greeter" = "Edi Loud Greeter";
    }
}
