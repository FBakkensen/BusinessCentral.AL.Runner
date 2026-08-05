codeunit 50273 "HC GetHeaders Bool Src"
{
    /// <summary>
    /// Exercises HttpContent.GetHeaders() in a boolean context.
    /// </summary>
    procedure GetHeadersInIf(): Boolean
    var
        Content: HttpContent;
        Headers: HttpHeaders;
    begin
        if not Content.GetHeaders(Headers) then
            exit(false);
        exit(true);
    end;
}
