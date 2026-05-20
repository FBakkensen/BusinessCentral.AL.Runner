/// Proves SessionInformation.AITokensUsed — rewritten to 0L standalone.
codeunit 50168 "AIT Src"
{
    procedure GetAITokens(): BigInteger
    begin
        exit(SessionInformation.AITokensUsed());
    end;
}
