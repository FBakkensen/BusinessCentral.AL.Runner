table 50002 "BS Counter"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; CallCount; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }
}

codeunit 50016 "BS Publisher"
{
    [IntegrationEvent(false, false)]
    procedure OnDoSomething()
    begin
    end;

    procedure DoSomething()
    begin
        OnDoSomething();
    end;
}

codeunit 50017 "BS Manual Subscriber"
{
    EventSubscriberInstance = Manual;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"BS Publisher", 'OnDoSomething', '', true, true)]
    local procedure HandleDoSomething()
    var
        C: Record "BS Counter";
    begin
        if not C.Get(1) then begin
            C.PK := 1;
            C.CallCount := 1;
            C.Insert();
        end else begin
            C.CallCount += 1;
            C.Modify();
        end;
    end;
}
