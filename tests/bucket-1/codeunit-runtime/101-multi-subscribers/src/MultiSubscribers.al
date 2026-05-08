codeunit 50033 "MS Publisher"
{
    [IntegrationEvent(false, false)]
    procedure OnDoCalc(var Value: Integer)
    begin
    end;

    procedure DoCalc(BaseValue: Integer): Integer
    var
        Result: Integer;
    begin
        Result := BaseValue;
        OnDoCalc(Result);
        exit(Result);
    end;
}

codeunit 50034 "MS Subscriber A"
{
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"MS Publisher", 'OnDoCalc', '', true, true)]
    local procedure HandleCalcA(var Value: Integer)
    begin
        Value += 10;
    end;
}

codeunit 50035 "MS Subscriber B"
{
    [EventSubscriber(ObjectType::Codeunit, Codeunit::"MS Publisher", 'OnDoCalc', '', true, true)]
    local procedure HandleCalcB(var Value: Integer)
    begin
        Value += 20;
    end;
}
