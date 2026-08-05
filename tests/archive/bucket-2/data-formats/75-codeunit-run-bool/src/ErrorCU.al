codeunit 50288 "Error CU"
{
    trigger OnRun()
    begin
        Error('Intentional error from Error CU');
    end;
}
