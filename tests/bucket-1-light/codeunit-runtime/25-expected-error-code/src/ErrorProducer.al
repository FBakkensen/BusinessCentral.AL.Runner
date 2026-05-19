codeunit 50176 ErrorProducer
{
    procedure RaiseDialogError()
    begin
        Error('Something went wrong: invalid input.');
    end;

    procedure NoError()
    begin
        // Does nothing — no error raised.
    end;
}
