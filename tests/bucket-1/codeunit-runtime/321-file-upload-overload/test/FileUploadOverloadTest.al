/// Tests for File.Upload 5-param AL form (issue #1531).
///
/// BC AL: File.Upload(DialogTitle, FromFolder, FilterText, FromFile, var ToFile)
/// This browser-roundtrip upload variant is out-of-scope (§3.4 file-storage).
/// All call sites must throw RunnerOutOfScopeException with api="NavFile.Upload".
codeunit 50265 "File Upload Overload Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    // ── File.Upload 5-param static form — OOS (browser round-trip) ─────────

    /// File.Upload (browser round-trip) is out-of-scope (§3.4 file-storage).
    [Test]
    procedure Upload_FiveParam_ThrowsOutOfScope()
    var
        toFile: Text;
    begin
        toFile := 'original';
        asserterror File.Upload('Choose File', 'C:\temp', '*.txt', 'source.txt', toFile);
        Assert.ExpectedError('out-of-scope: NavFile.Upload');
    end;

    /// File.Upload with empty strings — still out-of-scope.
    [Test]
    procedure Upload_FiveParam_EmptyArgs_ThrowsOutOfScope()
    var
        toFile: Text;
    begin
        asserterror File.Upload('My Dialog', '', 'All Files|*.*', '', toFile);
        Assert.ExpectedError('out-of-scope: NavFile.Upload');
    end;
}
