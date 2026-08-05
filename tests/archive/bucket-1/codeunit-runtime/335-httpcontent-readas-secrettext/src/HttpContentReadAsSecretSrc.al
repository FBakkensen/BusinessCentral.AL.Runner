codeunit 50291 "HC ReadAs Secret Src"
{
#if ONPREM
    procedure ReadAsSecretRoundTrip(body: Text): Text
    var
        content: HttpContent;
        secret: SecretText;
    begin
        content.WriteFrom(body);
        content.ReadAs(secret);
        exit(secret.Unwrap());
    end;

    procedure ReadAsSecretReturnsTrue(): Boolean
    var
        content: HttpContent;
        secret: SecretText;
    begin
        content.WriteFrom('payload');
        exit(content.ReadAs(secret));
    end;

    procedure ReadAsSecretIsEmpty(body: Text): Boolean
    var
        content: HttpContent;
        secret: SecretText;
    begin
        content.WriteFrom(body);
        content.ReadAs(secret);
        exit(secret.Unwrap() = '');
    end;
#endif
}
