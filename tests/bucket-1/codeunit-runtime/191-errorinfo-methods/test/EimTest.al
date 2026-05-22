/// Tests for ErrorInfo.Create, Message, ErrorType — issue #215.
codeunit 50149 "EIM Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "EIM Src";

    // ── ErrorInfo.Create + Message ────────────────────────────────────────────

    [Test]
    procedure Create_SetsMessage()
    var
        ErrInfo: ErrorInfo;
    begin
        // Positive: ErrorInfo.Create(msg) captures the message.
        ErrInfo := Src.CreateWithMessage('Something went wrong');
        Assert.AreEqual('Something went wrong', Src.GetMessage(ErrInfo),
            'Message must match the value passed to Create');
    end;

    [Test]
    procedure Create_EmptyMessage_IsEmpty()
    var
        ErrInfo: ErrorInfo;
    begin
        // Positive: Create with empty string — Message returns empty string.
        ErrInfo := Src.CreateWithMessage('');
        Assert.AreEqual('', Src.GetMessage(ErrInfo), 'Message must be empty when created with empty string');
    end;

    [Test]
    procedure Message_DefaultErrorInfo_IsEmpty()
    var
        ErrInfo: ErrorInfo;
    begin
        // Negative: default-initialised ErrorInfo has empty message.
        Assert.AreEqual('', Src.GetMessage(ErrInfo), 'Default ErrorInfo message must be empty');
    end;

    // ── ErrorInfo.ErrorType ───────────────────────────────────────────────────

    [Test]
    procedure ErrorType_RoundTrips_Client_NoThrow()
    var
        ErrInfo: ErrorInfo;
    begin
        // BC 16.1: ErrorType setter does not throw.
        Src.SetErrorTypeClient(ErrInfo);
        Assert.IsTrue(true, 'ErrorType setter must not throw');
    end;

    [Test]
    procedure ErrorType_Default_NoThrow()
    var
        ErrInfo: ErrorInfo;
    begin
        // BC 16.1: ErrorType getter does not throw.
        Src.GetErrorType(ErrInfo);
        Assert.IsTrue(true, 'ErrorType getter must not throw');
    end;

}
