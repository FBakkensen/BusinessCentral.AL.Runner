namespace ALRunnerExtras.EnumDefaultImplementation;

using Microsoft.Finance.VAT.Registration;
using Microsoft.Sales.Document;

codeunit 64633 "Edi Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "Edi Assert";

    [Test]
    procedure DefaultImplementation_ResolvesValueWithoutOwnImplementation()
    var
        Greeter: Interface "Edi Greeter";
    begin
        Greeter := Enum::"Edi Greeting"::Default;
        Assert.AreEqual('default', Greeter.Greet(), 'the Default value resolves to the DefaultImplementation codeunit');
        Greeter := Enum::"Edi Greeting"::Quiet;
        Assert.AreEqual('default', Greeter.Greet(), 'every value without its own Implementation resolves to the default');
    end;

    [Test]
    procedure ValueImplementation_OverridesDefaultImplementation()
    var
        Greeter: Interface "Edi Greeter";
    begin
        Greeter := Enum::"Edi Greeting"::Loud;
        Assert.AreEqual('LOUD', Greeter.Greet(), 'a value-level Implementation wins over the DefaultImplementation');
    end;

    [Test]
    procedure EnumExtensionValue_InheritsDefaultImplementation()
    var
        Greeter: Interface "Edi Greeter";
    begin
        Greeter := Enum::"Edi Greeting"::Inherited;
        Assert.AreEqual('default', Greeter.Greet(), 'an extension value without its own Implementation inherits the base enum''s default');
        Greeter := Enum::"Edi Greeting"::Whisper;
        Assert.AreEqual('whisper', Greeter.Greet(), 'an extension value with its own Implementation uses it');
    end;

    [Test]
    procedure BaseAppEnumWithDefaultImplementation_ConvertsToInterface_NoThrow()
    var
        AltCustVATRegDoc: Interface "Alt. Cust. VAT Reg. Doc.";
    begin
        AltCustVATRegDoc := Enum::"Alt. Cust VAT Reg. Doc."::Default;
    end;

    [Test]
    procedure SalesHeaderInsert_ReachesDefaultImplementation()
    var
        SalesHeader: Record "Sales Header";
    begin
        SalesHeader.Init();
        SalesHeader."Document Type" := SalesHeader."Document Type"::Order;
        SalesHeader."No." := 'EDI-1';
        SalesHeader.Insert(true);

        Assert.AreEqual('EDI-1', SalesHeader."No.", 'the Sales Header was inserted, so codeunit 200 Init reached the Alt. Cust. VAT Reg. Doc. implementation');
        Assert.AreEqual(true, SalesHeader.Get(SalesHeader."Document Type"::Order, 'EDI-1'), 'the inserted header is readable');
    end;
}
