/// Proves SessionInformation.AITokensUsed — rewritten to 0L standalone.
codeunit 50168 "AIT Src"
{
#if ONPREM
    procedure GetAITokens(): BigInteger
    begin
        exit(SessionInformation.AITokensUsed());
    end;
#endif
}
