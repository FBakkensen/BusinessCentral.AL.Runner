codeunit 50582 "Test CurrentTransactionType"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure CurrentTransactionType_ReturnsUpdate()
    var
        TxType: TransactionType;
    begin
        // [GIVEN] The runner has no real transaction system
        // [WHEN] CurrentTransactionType() is called
        TxType := CurrentTransactionType();

        // [THEN] Returns TransactionType::UpdateNoLocks (the BC default transaction type)
        Assert.AreEqual(Format(TransactionType::UpdateNoLocks), Format(TxType), 'CurrentTransactionType() must return UpdateNoLocks in BC');
    end;

    [Test]
    procedure CurrentTransactionType_IsStable()
    var
        T1: TransactionType;
        T2: TransactionType;
    begin
        // [GIVEN] Multiple calls to CurrentTransactionType()
        T1 := CurrentTransactionType();
        T2 := CurrentTransactionType();

        // [THEN] Always returns the same value
        Assert.AreEqual(Format(T1), Format(T2), 'CurrentTransactionType() must return a stable value');
    end;

    [Test]
    procedure CurrentTransactionType_NotBrowse()
    var
        TxType: TransactionType;
    begin
        // [GIVEN] The stub always returns Update
        TxType := CurrentTransactionType();

        // [THEN] It does not return Browse (proves it is not defaulting to ordinal 0)
        Assert.AreNotEqual(Format(TransactionType::Browse), Format(TxType), 'CurrentTransactionType() must not return Browse');
    end;
}
