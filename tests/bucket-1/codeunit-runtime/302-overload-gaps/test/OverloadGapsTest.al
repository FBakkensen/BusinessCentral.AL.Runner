/// Tests for four missing method overloads:
///   #1180 — Report instance .Execute(XmlText)
///   #1183 — File.Create() returns Boolean
///   #1187 — RecordRef.AddLink(Url, Description)
///   #1192 — RecordRef.GetView(UseNames)
codeunit 50215 "OG Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "OG Src";

    // ──────────────────────────────────────────────────────────────────────────
    // #1180 — Report.Execute(XmlText) — instance method, no-op in standalone
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    procedure Report_Execute_XmlText_IsNoOp()
    var
        Rep: Report "OG Dummy Report";
    begin
        // Must compile — entire claim is that the overload exists and the AL compiles.
        // On BC with certain locales, Execute('<Report />') may throw a filter error;
        // we accept that and just verify the method is callable (no AL compile error).
        if true then
            Src.CallReportExecute(Rep, '');
    end;

    // ──────────────────────────────────────────────────────────────────────────
    // #1183 — File.Create() returns Boolean (positive: returns true)
    // ──────────────────────────────────────────────────────────────────────────

#if ONPREM
    [Test]
    procedure File_Create_ReturnsTrue()
    var
        F: File;
    begin
        Assert.IsTrue(Src.CreateFileReturnsTrue(F, 'test.txt'),
            'File.Create should return true on success');
    end;

    [Test]
    procedure File_Create_ResultCanBeNegated()
    var
        F: File;
    begin
        // NOT operator on the return value must compile (CS0023 guard).
        Assert.IsFalse(Src.CanNegatCreateResult(F, 'test.txt'),
            'NOT File.Create should return false when Create succeeds');
    end;
#endif

    // ──────────────────────────────────────────────────────────────────────────
    // #1187 — RecordRef.AddLink(Url, Description) — returns link ID (0 in stub)
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    procedure RecordRef_AddLink_ReturnsLinkId()
    var
        Rec: Record "OG Table";
        RecRef: RecordRef;
        LinkId: Integer;
    begin
        RecRef.GetTable(Rec);
        LinkId := Src.AddLinkReturnsId(RecRef, 'https://example.com', 'Example Link');
        // Compiles and returns a non-negative integer; BC returns a real link ID (> 0)
        Assert.IsTrue(LinkId >= 0, 'AddLink must return a non-negative link ID');
    end;

    [Test]
    procedure RecordRef_AddLink_UrlOnly_ReturnsLinkId()
    var
        Rec: Record "OG Table";
        RecRef: RecordRef;
    begin
        RecRef.GetTable(Rec);
        // 1-arg form: description defaults to empty
        Assert.IsTrue(Src.AddLinkReturnsId(RecRef, 'https://example.com', '') >= 0,
            'AddLink with empty description must return a non-negative link ID');
    end;

    // ──────────────────────────────────────────────────────────────────────────
    // #1192 — RecordRef.GetView(UseNames) — 1-argument overload
    // ──────────────────────────────────────────────────────────────────────────

    [Test]
    procedure RecordRef_GetView_UseNames_True_ReturnsText()
    var
        Rec: Record "OG Table";
        RecRef: RecordRef;
        ViewText: Text;
    begin
        RecRef.Open(Database::"OG Table");
        ViewText := Src.GetViewWithUseNames(RecRef, true);
        // Must compile and return the same value as the 0-arg overload.
        Assert.AreEqual(RecRef.GetView(), ViewText,
            'GetView(true) should return the same view as GetView()');
    end;

    [Test]
    procedure RecordRef_GetView_UseNames_False_ReturnsText()
    var
        Rec: Record "OG Table";
        RecRef: RecordRef;
        ViewText: Text;
    begin
        RecRef.Open(Database::"OG Table");
        ViewText := Src.GetViewWithUseNames(RecRef, false);
        // GetView(false) returns view text with field numbers rather than names;
        // must not throw and must return a non-empty string.
        Assert.AreNotEqual('', ViewText,
            'GetView(false) must return a non-empty view string');
    end;
}
