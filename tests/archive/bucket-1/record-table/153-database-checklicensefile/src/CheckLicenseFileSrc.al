/// Helper codeunit that wraps Database.CheckLicenseFile(KeyNumber) so the
/// test can exercise it without calling it inline.
codeunit 50463 "CLF Helper"
{
#if ONPREM
    /// Call Database.CheckLicenseFile(keyNumber) — must be a no-op stub in standalone mode.
    procedure CallCheckLicenseFile(keyNumber: Integer)
    begin
        Database.CheckLicenseFile(keyNumber);
    end;
#endif

    /// Proving helper — returns a+b+1 to verify the codeunit is live.
    procedure AddWithBonus(a: Integer; b: Integer): Integer
    begin
        exit(a + b + 1);
    end;
}
