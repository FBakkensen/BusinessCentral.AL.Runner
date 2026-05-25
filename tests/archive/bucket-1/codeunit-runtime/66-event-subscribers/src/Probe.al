table 50048 "ES Counter"
{
    fields
    {
        field(1; PK; Integer) { }
        field(2; Before; Integer) { }
        field(3; After; Integer) { }
    }
    keys { key(PK; PK) { Clustered = true; } }
}

codeunit 50337 "ES Publisher"
{
    procedure DoIt()
    begin
        OnBeforeDoIt();
        OnAfterDoIt();
    end;

    [IntegrationEvent(false, false)]
    local procedure OnBeforeDoIt()
    begin
    end;

    [IntegrationEvent(false, false)]
    local procedure OnAfterDoIt()
    begin
    end;
}

codeunit 50338 "ES Subscriber"
{
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"ES Publisher", 'OnBeforeDoIt', '', true, true)]
    local procedure HandleBefore()
    var
        C: Record "ES Counter";
    begin
        if not C.Get(1) then begin
            C.PK := 1;
            C.Before := 1;
            C.Insert();
        end else begin
            C.Before += 1;
            C.Modify();
        end;
    end;

    [EventSubscriber(ObjectType::Codeunit, Codeunit::"ES Publisher", 'OnAfterDoIt', '', true, true)]
    local procedure HandleAfter()
    var
        C: Record "ES Counter";
    begin
        if not C.Get(1) then begin
            C.PK := 1;
            C.After := 1;
            C.Insert();
        end else begin
            C.After += 1;
            C.Modify();
        end;
    end;
}
