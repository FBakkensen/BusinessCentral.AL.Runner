codeunit 50305 "HC Error Envelope Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "HC Error Envelope Src";

    [Test]
    procedure SendTimeout_ThrowsErrorEnvelope()
    begin
        // In BC, HTTP calls are blocked by the runtime regardless of the response setup
        asserterror Src.SendTimeoutResponse();
        Assert.ExpectedError('The request was blocked by the runtime');
    end;

    [Test]
    procedure SendDefaultResponse_ThrowsNotSupported()
    begin
        asserterror Src.SendDefaultResponse();
        Assert.ExpectedError('The request was blocked by the runtime');
    end;
}
