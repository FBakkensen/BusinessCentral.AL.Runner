codeunit 50256 "Hello Greeter" implements "IGreeter"
{
    procedure Greet(Name: Text): Text
    begin
        exit('Hello ' + Name);
    end;
}
