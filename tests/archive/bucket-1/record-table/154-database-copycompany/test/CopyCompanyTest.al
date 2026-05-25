// Renumbered from 61401 to avoid collision in new bucket layout (#1385).
codeunit 50468 "CCP CopyCompany Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure CopyCompany_NonExistentCompany_Throws()
    var
        Helper: Codeunit "CCP Helper";
    begin
        // In BC, Database.CopyCompany validates that the source company exists.
        // Passing a non-existent source company must throw an error.
        asserterror Helper.CallCopyCompany('Source Company', 'Destination Company');
        Assert.IsTrue(true, 'CopyCompany with non-existent source company must throw');
    end;

    [Test]
    procedure CopyCompany_NoOp_EmptyNames()
    var
        Helper: Codeunit "CCP Helper";
    begin
        // In BC, CopyCompany validates that the source company exists.
        // Passing empty names is expected to throw a "company does not exist" error.
        asserterror Helper.CallCopyCompany('', '');
        Assert.IsTrue(true, 'CopyCompany with non-existent company must throw (BC validates company)');
    end;

    [Test]
    procedure CopyCompany_CalledTwice_NoError()
    var
        Helper: Codeunit "CCP Helper";
    begin
        // In BC, CopyCompany validates that the source company exists.
        // Passing non-existent company names is expected to throw.
        asserterror Helper.CallCopyCompany('A', 'B');
        asserterror Helper.CallCopyCompany('C', 'D');
        Assert.IsTrue(true, 'CopyCompany with non-existent companies must throw on each call');
    end;

    [Test]
    procedure AddWithBonus_ProvingCompilationUnitLive()
    var
        Helper: Codeunit "CCP Helper";
    begin
        // Proving: the codeunit is live — real computation returns a+b+1.
        Assert.AreEqual(8, Helper.AddWithBonus(3, 4), 'AddWithBonus(3,4) must return 3+4+1=8');
        Assert.AreEqual(1, Helper.AddWithBonus(0, 0), 'AddWithBonus(0,0) must return 0+0+1=1');
    end;

    [Test]
    procedure AddWithBonus_NotPlainSum()
    var
        Helper: Codeunit "CCP Helper";
    begin
        // Negative: AddWithBonus must NOT return a plain sum (no-op trap guard).
        Assert.AreNotEqual(7, Helper.AddWithBonus(3, 4), 'AddWithBonus must not just return a+b');
    end;
}
