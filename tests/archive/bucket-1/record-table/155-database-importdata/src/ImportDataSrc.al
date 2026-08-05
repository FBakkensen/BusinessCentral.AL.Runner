/// Helper codeunit that wraps Database.ImportData so the test can
/// exercise it without calling it inline.
codeunit 50471 "IDT Helper"
{
#if ONPREM
    /// Call Database.ImportData(showDialog, path, create) — must be a no-op stub
    /// in standalone mode (no external file I/O).
    procedure CallImportData(showDialog: Boolean)
    var
        Path: Text;
        Create: Boolean;
    begin
        Database.ImportData(showDialog, Path, Create);
    end;
#endif

    /// Proving helper — returns a+b+1 to verify the codeunit is live.
    procedure AddWithBonus(a: Integer; b: Integer): Integer
    begin
        exit(a + b + 1);
    end;
}
