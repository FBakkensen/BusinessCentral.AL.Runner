codeunit 50530 "RecRef Perm Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;

    [Test]
    procedure ReadPermission_ReturnsTrue()
    // Proves ReadPermission returns true (not a default false stub)
    var
        Helper: Codeunit "RecRef Perm Helper";
    begin
        Assert.IsTrue(Helper.TestReadPermission(), 'ReadPermission should return true in standalone mode');
    end;

    [Test]
    procedure ReadPermission_OnClosedRef_Throws()
    // BC 16.1: ReadPermission on a closed/unopen RecordRef raises "The record is not open."
    var
        Helper: Codeunit "RecRef Perm Helper";
    begin
        asserterror Helper.TestReadPermissionOnClosedRef();
        Assert.ExpectedError('not open');
    end;

    [Test]
    procedure SetAutoCalcFields_SingleField_Throws()
    // BC: SetAutoCalcFields requires a FlowField; Amount (Decimal) is a regular field.
    var
        Helper: Codeunit "RecRef Perm Helper";
    begin
        asserterror Helper.TestSetAutoCalcFieldsNoThrow();
        Assert.ExpectedError('must be a FlowField');
    end;

    [Test]
    procedure SetAutoCalcFields_MultipleFields_Throws()
    // BC: SetAutoCalcFields requires a FlowField; Amount and Description are regular fields.
    var
        Helper: Codeunit "RecRef Perm Helper";
    begin
        asserterror Helper.TestSetAutoCalcFieldsMultiple();
        Assert.ExpectedError('must be a FlowField');
    end;
}
