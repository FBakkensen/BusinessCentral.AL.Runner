namespace ALRunnerExtras.EnumDefaultImplementation;

// An enumextension's values inherit the base enum's DefaultImplementation unless they
// declare their own Implementation.
enumextension 64635 "Edi Greeting Ext" extends "Edi Greeting"
{
    value(64635; Inherited) { }
    value(64636; Whisper)
    {
        Implementation = "Edi Greeter" = "Edi Whisper Greeter";
    }
}
