interface "IC Source List"
{
    procedure GetCount(): Integer;
    procedure GetName(): Text;
}

codeunit 50302 "IC Source List Impl A" implements "IC Source List"
{
    procedure GetCount(): Integer
    begin
        exit(42);
    end;

    procedure GetName(): Text
    begin
        exit('ImplA');
    end;
}

codeunit 50303 "IC Source List Impl B" implements "IC Source List"
{
    procedure GetCount(): Integer
    begin
        exit(99);
    end;

    procedure GetName(): Text
    begin
        exit('ImplB');
    end;
}
