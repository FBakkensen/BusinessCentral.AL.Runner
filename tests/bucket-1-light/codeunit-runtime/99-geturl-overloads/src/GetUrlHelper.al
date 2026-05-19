codeunit 50375 "GetUrl Helper"
{
    procedure GetUrlOneArg(): Text
    begin
        exit(GetUrl(ClientType::Web));
    end;

    procedure GetUrlTwoArgs(): Text
    begin
        exit(GetUrl(ClientType::Web, 'CRONUS'));
    end;

    procedure GetUrlFourArgs(): Text
    begin
        exit(GetUrl(ClientType::Web, 'CRONUS', ObjectType::Page, 22));
    end;
}
