/// Exercises GetLastErrorCallStack().
codeunit 50163 "LEC Src"
{
    procedure GetCallStack_NoPriorError(): Text
    begin
        exit(GetLastErrorCallStack());
    end;

    procedure GetCallStack_AfterCaughtError(): Text
    var
        result: Text;
    begin
        if not TryErrorMethod() then
            result := GetLastErrorCallStack();
        exit(result);
    end;

    [TryFunction]
    local procedure TryErrorMethod()
    begin
        Error('Test error');
    end;
}
