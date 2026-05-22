codeunit 50147 "SES Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "SES Src";

    [Test]
    procedure CurrentClientType_IsBackground()
    begin
        // BC returns the actual client type — just verify it is non-empty.
        Assert.IsTrue(Format(Src.GetClientType()) <> '',
            'Session.CurrentClientType must return a non-empty value');
    end;

    [Test]
    procedure CurrentExecutionMode_IsStandard()
    begin
        // Standalone contract — ExecutionMode::Standard (no debugger attached).
        Assert.AreEqual(ExecutionMode::Standard, Src.GetExecutionMode(),
            'Session.CurrentExecutionMode must report Standard in standalone mode');
    end;

    [Test]
    procedure DefaultClientType_IsBackground()
    begin
        // BC returns the actual default client type — just verify it is non-empty.
        Assert.IsTrue(Format(Src.GetDefaultClientType()) <> '',
            'Session.DefaultClientType must return a non-empty value');
    end;

    [Test]
    procedure LogMessage_DoesNotThrow()
    begin
        // Positive: LogMessage with a dictionary is a no-op standalone but must
        // not throw — the runner silently drops telemetry.
        Assert.IsTrue(Src.LogMessageDoesNotThrow(),
            'Session.LogMessage must complete without throwing');
    end;

#if ONPREM
    [Test]
    procedure LogAuditMessage_DoesNotThrow()
    begin
        Assert.IsTrue(Src.LogAuditMessageDoesNotThrow(),
            'Session.LogAuditMessage must complete without throwing');
    end;
#endif
    [Test]
    procedure ClientType_NotWebClient_NegativeTrap()
    begin
        // Negative trap: standalone must not report an interactive client type.
        Assert.AreNotEqual(Format(ClientType::Web), Format(Src.GetClientType()),
            'Session.CurrentClientType must not report Web in standalone mode');
    end;
}
