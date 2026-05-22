codeunit 50142 "SEVS Src"
{

#if ONPREM
    procedure GetApplicationPath(): Text
    begin
        exit(ApplicationPath());
    end;

    procedure GetTemporaryPath(): Text
    begin
        exit(TemporaryPath());
    end;
#endif

    procedure IsGui(): Boolean
    begin
        exit(GuiAllowed());
    end;

    procedure IsSvcTier(): Boolean
    begin
        exit(IsServiceTier());
    end;

    procedure GetWorkDate(): Date
    begin
        exit(WorkDate());
    end;

    procedure SetWorkDate(D: Date)
    begin
        WorkDate(D);
    end;

    procedure GetGlobalLang(): Integer
    begin
        exit(GlobalLanguage());
    end;

    procedure SetGlobalLang(LCID: Integer)
    begin
        GlobalLanguage(LCID);
    end;

    procedure GetWindowsLang(): Integer
    begin
        exit(WindowsLanguage());
    end;

    procedure RoundDT(DT: DateTime; Precision: Duration): DateTime
    begin
        exit(RoundDateTime(DT, Precision));
    end;

#if ONPREM
    procedure NullCheck(V: Variant): Boolean
    begin
        exit(IsNull(V));
    end;
#endif
    procedure OpenHyperlink(Url: Text)
    begin
        Hyperlink(Url);
    end;
}
