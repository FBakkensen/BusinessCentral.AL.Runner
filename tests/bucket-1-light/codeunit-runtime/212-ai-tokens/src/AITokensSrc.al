/// Proves SessionInformation.AITokensUsed — rewritten to 0L standalone.
codeunit 50161 "AIT Src"
{
    procedure GetAITokens(): BigInteger
    begin
        exit(SessionInformation.AITokensUsed());
    end;
}
