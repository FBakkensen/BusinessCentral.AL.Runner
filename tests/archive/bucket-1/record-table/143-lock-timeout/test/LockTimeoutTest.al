codeunit 50452 "LT Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "LT Src";

    [Test]
    procedure LockTimeout_DefaultIsTrue()
    begin
        // Positive: BC's Database.LockTimeout default is true.
        Assert.IsTrue(Src.GetLockTimeout(), 'LockTimeout must default to true');
    end;

    [Test]
    procedure LockTimeoutDuration_IsNonNegative()
    var
        d: Duration;
    begin
        // Positive: LockTimeoutDuration must be >= 0 (a negative duration
        // would indicate an unwired stub returning garbage).
        d := Src.GetLockTimeoutDuration();
        Assert.IsTrue(d >= 0, 'LockTimeoutDuration must be non-negative');
    end;

    [Test]
    procedure LockTimeout_SetFalseReadBack()
    var
        result: Boolean;
    begin
        // In BC, setting LockTimeout(false) takes effect and the getter returns false.
        result := Src.SetAndGetLockTimeout(false);
        Assert.IsFalse(result, 'After setting LockTimeout to false, read-back must return false');
    end;

    [Test]
    procedure LockTimeout_SetTrueReadBack()
    begin
        Assert.IsTrue(Src.SetAndGetLockTimeout(true), 'SetAndGetLockTimeout(true) must return true');
    end;

    [Test]
    procedure LockTimeout_ReadReturnsBoolean_NegativeTrap()
    var
        b: Boolean;
    begin
        // Negative: guard against a stub that throws — just reading into a local
        // variable proves the property getter completes and yields a Boolean.
        b := Src.GetLockTimeout();
        Assert.IsTrue(b or (not b), 'LockTimeout read must complete and return a Boolean');
    end;
}
