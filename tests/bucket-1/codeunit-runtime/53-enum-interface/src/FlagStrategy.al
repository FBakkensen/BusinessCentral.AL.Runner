interface "EI Has Flag"
{
    procedure GetFlag(): Boolean
}

codeunit 50325 "EI True Flag Impl" implements "EI Has Flag"
{
    procedure GetFlag(): Boolean
    begin
        exit(true);
    end;
}

codeunit 50326 "EI False Flag Impl" implements "EI Has Flag"
{
    procedure GetFlag(): Boolean
    begin
        exit(false);
    end;
}

enum 50007 "EI Flag Strategy" implements "EI Has Flag"
{
    Extensible = true;

    value(0; Yes)
    {
        Implementation = "EI Has Flag" = "EI True Flag Impl";
    }
    value(1; No)
    {
        Implementation = "EI Has Flag" = "EI False Flag Impl";
    }
}
