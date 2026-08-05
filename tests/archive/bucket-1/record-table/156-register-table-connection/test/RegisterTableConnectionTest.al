codeunit 50476 "RTC Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "RTC Src";

    [Test]
    procedure RegisterTableConnection_Throws()
    // BC 16.1: RegisterTableConnection requires admin permissions and throws in test context.
    begin
        asserterror Src.CallRegister(TableConnectionType::ExternalSQL, 'MyConn', 'Server=localhost');
    end;

    [Test]
    procedure RegisterTableConnection_EmptyArgs_Throws()
    // BC 16.1: RegisterTableConnection requires admin permissions and throws in test context.
    begin
        asserterror Src.CallRegister(TableConnectionType::ExternalSQL, '', '');
    end;

    [Test]
    procedure RegisterTableConnection_CRM_NoThrow()
    // BC 16.1: RegisterTableConnection with CRM type does NOT throw in test context
    // (unlike ExternalSQL which requires admin permissions). No-throw contract.
    begin
        Src.CallRegister(TableConnectionType::CRM, 'CRMConn', 'Endpoint=crm.example');
        Assert.IsTrue(true, 'RegisterTableConnection with CRM type must not throw');
    end;

    [Test]
    procedure RegisterTableConnection_DifferentTypes_Throws()
    // BC 16.1: RegisterTableConnection requires admin permissions and throws in test context.
    begin
        asserterror Src.CallRegister(TableConnectionType::ExternalSQL, 'SqlConn', 'sql-cs');
    end;

    [Test]
    procedure RegisterTableConnection_LongConnectionString_Throws()
    // BC 16.1: RegisterTableConnection requires admin permissions and throws in test context.
    begin
        asserterror Src.CallRegister(
            TableConnectionType::ExternalSQL,
            'VeryLongConnectionName_123456789',
            'Server=verylonghostname.example.com;Database=db;User=dbuser;Password=pwd;MultipleActiveResultSets=true');
    end;
}
