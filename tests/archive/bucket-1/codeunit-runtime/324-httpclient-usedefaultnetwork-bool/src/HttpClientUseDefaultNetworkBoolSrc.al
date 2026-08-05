codeunit 50269 "HC UseDefaultNetwork Bool Src"
{
#if ONPREM
    /// <summary>
    /// Exercises UseDefaultNetworkWindowsAuthentication() in a boolean context.
    /// </summary>
    procedure UseDefaultNetworkInIf(): Boolean
    var
        Client: HttpClient;
    begin
        if not Client.UseDefaultNetworkWindowsAuthentication() then
            exit(false);
        exit(true);
    end;
#endif
}
