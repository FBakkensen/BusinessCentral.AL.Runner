codeunit 50261 "Greeting Service"
{
    procedure MakeGreeting(Greeter: Interface "IGreeter"; Name: Text): Text
    begin
        exit(Greeter.Greet(Name));
    end;
}
