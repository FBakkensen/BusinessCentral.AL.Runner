codeunit 50264 "Guid Helper"
{
    procedure GetNewGuid(): Guid
    begin
        exit(CreateGuid());
    end;
}
