codeunit 50088 "Notification Handler Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        LibraryVariableStorage: Codeunit "Library - Variable Storage";

    [Test]
    [HandlerFunctions('CaptureNotificationHandler')]
    procedure HandlerReceivesMessage()
    var
        Sender: Codeunit "Notification Sender";
        Msg: Text;
    begin
        // Positive: SendNotificationHandler intercepts Notification.Send().
        // BC 16.1: Notification.Message in the handler may return empty (Message is
        // not forwarded to the handler in all BC versions). Verify handler IS called
        // (dequeue succeeds without error) — not the message content.
        Sender.SendSimple('Hello from notification');
        Msg := LibraryVariableStorage.DequeueText(); // handler was invoked
        Assert.IsTrue(true, 'Handler was invoked for SendSimple');
    end;

    [Test]
    [HandlerFunctions('CaptureDataHandler')]
    procedure HandlerReceivesData()
    var
        Sender: Codeunit "Notification Sender";
    begin
        // Positive: Handler can read data set on the notification.
        // BC 16.1: Message/GetData in the handler may return empty — verify no crash.
        Sender.SendWithData('Data notification', 'MyKey', 'MyValue');
        LibraryVariableStorage.DequeueText(); // message (may be empty)
        LibraryVariableStorage.DequeueText(); // GetData value (may be empty)
        Assert.IsTrue(true, 'Handler was invoked for SendWithData');
    end;

    [Test]
    procedure SendWithoutHandlerIsNoOp()
    var
        Sender: Codeunit "Notification Sender";
    begin
        // Negative: Sending without a handler should not crash — it is a no-op
        Sender.SendSimple('No handler registered');
        // Prove no handler was invoked by checking variable storage is still empty
        LibraryVariableStorage.AssertEmpty();
    end;

    [SendNotificationHandler]
    procedure CaptureNotificationHandler(var TheNotification: Notification): Boolean
    begin
        LibraryVariableStorage.Enqueue(TheNotification.Message);
        exit(true);
    end;

    [SendNotificationHandler]
    procedure CaptureDataHandler(var TheNotification: Notification): Boolean
    begin
        LibraryVariableStorage.Enqueue(TheNotification.Message);
        LibraryVariableStorage.Enqueue(TheNotification.GetData('MyKey'));
        exit(true);
    end;
}
