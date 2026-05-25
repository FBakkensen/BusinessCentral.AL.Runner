/// Tests for SecretText → NavText conversion in MockHttpClient (issue #1533).
///
/// BC emits NavSecretText where MockHttpClient mock methods expect NavText.
/// Overloads accepting NavSecretText must exist in the mock.
codeunit 50268 "STN Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "STN Src";

#if ONPREM
    // ── UseWindowsAuthentication with SecretText ─────────────────────────────

    /// Positive: UseWindowsAuthentication(SecretText, SecretText) must not throw.
    [Test]
    procedure UseWindowsAuth_SecretText_NoError()
    begin
        Src.UseWindowsAuthWithSecret('user1', 'pass1');
        // [THEN] No error — NavSecretText overload exists
    end;

    /// Positive: UseWindowsAuthentication(SecretText, SecretText, SecretText) must not throw.
    [Test]
    procedure UseWindowsAuthDomain_SecretText_NoError()
    begin
        Src.UseWindowsAuthDomainWithSecret('user1', 'pass1', 'CORP');
        // [THEN] No error
    end;
#endif

    // ── AddCertificate with SecretText ────────────────────────────────────────

    /// Negative: AddCertificate(SecretText) throws when certificate cannot be found.
    [Test]
    procedure AddCert_SecretText_Throws()
    begin
        // BC 16.1: AddCertificate validates the thumbprint against the cert store;
        // an unknown thumbprint causes an HTTP error.
        asserterror Src.AddCertWithSecret('abc123thumbprint');
        Assert.ExpectedError('There was an error while executing the HTTP request');
    end;

    /// Negative: AddCertificate(SecretText, SecretText) throws when certificate cannot be found.
    [Test]
    procedure AddCertWithPassword_SecretText_Throws()
    begin
        // BC 16.1: AddCertificate validates the thumbprint against the cert store;
        // an unknown thumbprint causes an HTTP error.
        asserterror Src.AddCertWithPasswordSecret('abc123thumbprint', 'certpass');
        Assert.ExpectedError('There was an error while executing the HTTP request');
    end;
}
