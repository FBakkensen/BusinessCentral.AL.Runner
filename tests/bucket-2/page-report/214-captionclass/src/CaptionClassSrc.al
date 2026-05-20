/// Exercises CaptionClassTranslate — BC's resource translation system.
codeunit 50398 "CCT Src"
{
    procedure TranslateCaption(expr: Text): Text
    begin
        exit(CaptionClassTranslate(expr));
    end;
}
