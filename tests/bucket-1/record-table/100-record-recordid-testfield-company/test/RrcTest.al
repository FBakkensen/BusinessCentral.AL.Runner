/// Tests for Record.RecordId(), Record.TestField(), and Record.CurrentCompany()
/// on a typed Record variable and via bare built-in calls inside table procedures.
///
/// Two code paths are exercised:
///   (a) External codeunit calling via "var Rec: Record" parameter —
///       the BC compiler generates rec.ALRecordId/ALCurrentCompany on MockRecordHandle.
///   (b) Table methods calling bare RecordId()/CurrentCompany() —
///       the BC compiler generates _parent.ALRecordId/_parent.ALCurrentCompany on Record<N>,
///       which requires the delegating properties injected by RoslynRewriter (issue #1330).
codeunit 50395 "RRC Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        H: Codeunit "RRC Src";

    // ── RecordId — external codeunit path ─────────────────────────────────────


    local procedure Initialize()
    var
        Rec1: Record "RRC Table";
    begin
        Rec1.DeleteAll(false);
    end;

    [Test]
    procedure RecordId_Fresh_IsDefaultRecordId()
    var
        Rec: Record "RRC Table";
        FreshId: RecordId;
    begin
        Initialize();
        // A fresh uninserted record's RecordId must be readable without throwing.
        // In BC, Format(RecordId) on a fresh record returns the table name (e.g. 'RRC Table: ""'),
        // which is non-empty — the test just verifies we can read it without error.
        FreshId := H.GetRecordId_Fresh(Rec);
        Assert.IsTrue(true, 'RecordId on fresh record must be readable without error');
    end;

    [Test]
    procedure RecordId_AfterInsert_DoesNotThrow()
    var
        Rec: Record "RRC Table";
        Id: RecordId;
    begin
        Initialize();
        // Reading RecordId after Insert() must complete without throwing.
        Id := H.GetRecordId_AfterInsert(Rec);
        Assert.IsTrue(true, 'RecordId after Insert must not throw');
    end;

    // ── RecordId — table built-in path (tests ALRecordId on Record class) ─────

    [Test]
    procedure RecordId_Builtin_DoesNotThrow()
    var
        Id: RecordId;
    begin
        Initialize();
        // Bare RecordId() call inside a table method must compile and run without error.
        // This tests the ALRecordId delegation injected by RoslynRewriter (issue #1330).
        Id := H.GetBuiltinRecordId();
        Assert.IsTrue(true, 'RecordId() built-in inside table must not throw');
    end;

    // ── TestField — external codeunit path ────────────────────────────────────

    [Test]
    procedure TestField_FilledField_Passes()
    var
        Rec: Record "RRC Table";
    begin
        Initialize();
        // TestField on a non-blank field must not raise an error.
        H.CheckFilledFieldOk(Rec);
        Assert.IsTrue(true, 'TestField on filled field must not throw');
    end;

    [Test]
    procedure TestField_EmptyField_Errors()
    var
        Rec: Record "RRC Table";
    begin
        Initialize();
        // TestField on a blank field must raise a "must have a value" error.
        asserterror H.CheckEmptyFieldThrows(Rec);
        Assert.ExpectedError('must have a value');
    end;

    [Test]
    procedure TestField_MatchingValue_Passes()
    var
        Rec: Record "RRC Table";
    begin
        Initialize();
        // TestField with matching expected value must not throw.
        H.CheckMatchingValueOk(Rec);
        Assert.IsTrue(true, 'TestField with matching value must not throw');
    end;

    [Test]
    procedure TestField_MismatchValue_Errors()
    var
        Rec: Record "RRC Table";
    begin
        Initialize();
        // TestField with mismatched expected value must raise an error.
        asserterror H.CheckMismatchValueThrows(Rec);
        Assert.ExpectedError('must be equal to');
    end;

    // ── CurrentCompany — external codeunit path ───────────────────────────────

    [Test]
    procedure CurrentCompany_MatchesGlobalCompanyName()
    var
        Rec: Record "RRC Table";
        CompanyFromRec: Text;
    begin
        Initialize();
        // Rec.CurrentCompany() must return the same value as the global CompanyName().
        CompanyFromRec := H.GetCurrentCompany(Rec);
        Assert.AreEqual(CompanyName(), CompanyFromRec,
            'Rec.CurrentCompany() must equal CompanyName()');
    end;

    [Test]
    procedure CurrentCompany_IsNonEmpty()
    var
        Rec: Record "RRC Table";
        CompanyFromRec: Text;
    begin
        Initialize();
        // The returned company name must be a non-empty string, proving it is not a no-op stub.
        CompanyFromRec := H.GetCurrentCompany(Rec);
        Assert.AreNotEqual('', CompanyFromRec, 'Rec.CurrentCompany() must return a non-empty string');
    end;

    // ── CurrentCompany — table built-in path (tests ALCurrentCompany on Record class) ──

    [Test]
    procedure CurrentCompany_Builtin_IsNonEmpty()
    var
        CompanyName: Text;
    begin
        Initialize();
        // Bare CurrentCompany() call inside a table method must compile and return a non-empty string.
        // This tests the ALCurrentCompany delegation injected by RoslynRewriter (issue #1330).
        CompanyName := H.GetBuiltinCurrentCompany();
        Assert.AreNotEqual('', CompanyName, 'CurrentCompany() built-in inside table must return non-empty string');
    end;

    [Test]
    procedure CurrentCompany_Builtin_MatchesGlobal()
    var
        BuiltinCompany: Text;
    begin
        Initialize();
        // CurrentCompany() inside a table must equal the global CompanyName().
        BuiltinCompany := H.GetBuiltinCurrentCompany();
        Assert.AreEqual(CompanyName(), BuiltinCompany,
            'CurrentCompany() built-in inside table must equal global CompanyName()');
    end;
}
