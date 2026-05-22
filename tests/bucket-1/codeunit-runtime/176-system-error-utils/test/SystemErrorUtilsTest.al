codeunit 50141 "SEU Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "SEU Src";

    [Test]
    procedure GetLastErrorText_NoError_ReturnsEmpty()
    begin
        Src.ClearLast();
        Assert.AreEqual('', Src.GetLastErrText(), 'GetLastErrorText must be empty when no error');
    end;

    [Test]
    procedure GetLastErrorText_AfterError_ReturnsMessage()
    begin
        asserterror Error('Test error message');
        Assert.IsTrue(Src.GetLastErrText().Contains('Test error message'), 'GetLastErrorText must contain the error message');
    end;

    [Test]
    procedure ClearLastError_ResetsText()
    begin
        asserterror Error('Some error');
        Src.ClearLast();
        Assert.AreEqual('', Src.GetLastErrText(), 'GetLastErrorText must be empty after ClearLastError');
    end;

    [Test]
    procedure GetLastErrorCode_ReturnsText()
    begin
        // Error codes may be empty in the runner — just verify it does not crash and returns Text
        Assert.AreEqual('', Src.GetLastErrCode(), 'GetLastErrorCode must return empty string when no error code set');
    end;

    [Test]
    procedure GetLastErrorCallStack_ReturnsText()
    begin
        // Call stack is empty in standalone runner — verify no crash
        Assert.AreEqual('', Src.GetLastErrCallStack(), 'GetLastErrorCallStack must return empty string in runner');
    end;

    [Test]
    procedure IsCollectingErrors_FalseByDefault()
    begin
        Assert.IsFalse(Src.IsCollecting(), 'IsCollectingErrors must be false outside a collect scope');
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure AfterClear_HasCollectedErrors_IsFalse()
    begin
        // Positive: after ClearCollectedErrors the state is clean.
        // [ErrorBehavior(Collect)] activates collection so ClearCollectedErrors resets to false.
        Src.ClearCollected();
        Assert.IsFalse(Src.HasCollected(), 'HasCollectedErrors must be false after ClearCollectedErrors');
    end;

    [Test]
    procedure GetCollectedErrors_EmptyByDefault()
    var
        Errors: List of [ErrorInfo];
    begin
        Src.ClearCollected();
        Errors := Src.GetCollected();
        Assert.AreEqual(0, Errors.Count(), 'GetCollectedErrors must return empty list when no errors collected');
    end;

    [Test]
    [ErrorBehavior(ErrorBehavior::Collect)]
    procedure ClearCollectedErrors_DoesNotCrash()
    begin
        // [ErrorBehavior(Collect)] activates collection so ClearCollectedErrors resets to false.
        Src.ClearCollected();
        Assert.IsFalse(Src.HasCollected(), 'HasCollectedErrors must be false after ClearCollectedErrors');
    end;
}
