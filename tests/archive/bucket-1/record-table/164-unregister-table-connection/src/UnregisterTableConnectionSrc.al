/// Helper codeunit exercising Database.UnregisterTableConnection.
codeunit 50483 "UTC Src"
{
    procedure CallUnregister(ct: TableConnectionType; name: Text)
    begin
        Database.UnregisterTableConnection(ct, name);
    end;

    procedure CallUnregisterAndReturnFlag(ct: TableConnectionType; name: Text): Boolean
    begin
        Database.UnregisterTableConnection(ct, name);
    exit(true);
    end;
}
