codeunit 50026 "BS Tests"
{
    Subtype = Test;
    var
        Assert: Codeunit Assert;

    local procedure Initialize()
    var
        C: Record "BS Counter";
    begin
        C.DeleteAll(false);
    end;

    [Test]
    procedure ManualSubscriberNotCalledBeforeBind()
    var
        Publisher: Codeunit "BS Publisher";
        C: Record "BS Counter";
    begin
        Initialize();
        // Negative: manual subscriber should NOT be called without BindSubscription
        Publisher.DoSomething();
        Assert.IsFalse(C.Get(1), 'Manual subscriber must NOT fire before BindSubscription');
    end;

    [Test]
    procedure ManualSubscriberCalledAfterBind()
    var
        Publisher: Codeunit "BS Publisher";
        Sub: Codeunit "BS Manual Subscriber";
        C: Record "BS Counter";
    begin
        Initialize();
        // Positive: after BindSubscription, the manual subscriber IS called
        BindSubscription(Sub);
        Publisher.DoSomething();
        Assert.IsTrue(C.Get(1), 'Manual subscriber must fire after BindSubscription');
        Assert.AreEqual(1, C.CallCount, 'Subscriber should have been called once');
        UnbindSubscription(Sub);
    end;

    [Test]
    procedure ManualSubscriberNotCalledAfterUnbind()
    var
        Publisher: Codeunit "BS Publisher";
        Sub: Codeunit "BS Manual Subscriber";
        C: Record "BS Counter";
    begin
        Initialize();
        // Negative: after UnbindSubscription, the subscriber is NOT called again
        BindSubscription(Sub);
        Publisher.DoSomething();
        UnbindSubscription(Sub);

        // Counter is 1 from the first call
        Assert.IsTrue(C.Get(1), 'Counter should exist from bound call');
        Assert.AreEqual(1, C.CallCount, 'Counter should be 1 from bound call');

        // Second call — subscriber should NOT fire
        Publisher.DoSomething();
        C.Get(1);
        Assert.AreEqual(1, C.CallCount, 'Counter must still be 1 after unbind');
    end;
}
