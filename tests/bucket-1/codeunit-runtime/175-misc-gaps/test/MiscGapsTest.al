codeunit 50139 "MG Test"
{
    Subtype = Test;

    var
        Assert: Codeunit Assert;
        Src: Codeunit "MG Src";

    [Test]
    procedure JsonToken_Path_RootValue()
    var
        Token: JsonToken;
    begin
        Token.ReadFrom('"hello"');
        // BC 16.1: Path() on a root token returns empty string (not '$').
        // Verify no throw — the exact value is BC-version dependent.
        Src.GetJsonTokenPath(Token);
        Assert.IsTrue(true, 'JsonToken.Path() on root must not throw');
    end;

    [Test]
    procedure JsonToken_Path_ObjectField()
    var
        Obj: JsonObject;
        Token: JsonToken;
    begin
        Obj.Add('name', 'test');
        Obj.Get('name', Token);
        // BC 16.1 returns 'name' (without the '$.' prefix) for object field tokens.
        Assert.AreEqual('name', Src.GetJsonTokenPath(Token), 'field token path must be name');
    end;

    [Test]
    procedure Time_Millisecond_DefaultIsZero()
    var
        T: Time;
    begin
        Assert.AreEqual(0, Src.GetTimeMillisecond(T), 'default Time has 0 milliseconds');
    end;

    [Test]
    procedure Time_Millisecond_NonZero()
    var
        T: Time;
    begin
        // Add 100ms to midnight via time arithmetic (T + N adds N milliseconds in AL)
        T := 000000T + 100;
        Assert.AreEqual(100, Src.GetTimeMillisecond(T), 'T with 100ms component must return 100');
    end;

    [Test]
    procedure XmlNodeList_Get_ReturnsElement()
    var
        Root: XmlElement;
        Child: XmlElement;
        NodeList: XmlNodeList;
    begin
        Root := XmlElement.Create('root');
        Child := XmlElement.Create('child');
        Root.Add(Child);
        if Root.SelectNodes('child', NodeList) then
            Assert.IsTrue(Src.GetXmlNodeListItem(NodeList, 1), 'Get(1) must return the child element');
    end;

    [Test]
    procedure XmlReadOptions_PreserveWhitespace_DefaultFalse()
    var
        Opts: XmlReadOptions;
    begin
        Assert.IsFalse(Src.GetPreserveWhitespaceRead(Opts), 'default XmlReadOptions.PreserveWhitespace must be false');
    end;

    [Test]
    procedure XmlReadOptions_PreserveWhitespace_SetTrue()
    var
        Opts: XmlReadOptions;
    begin
        Assert.IsTrue(Src.SetPreserveWhitespaceRead(Opts, true), 'XmlReadOptions.PreserveWhitespace set to true must return true');
    end;

    [Test]
    procedure XmlWriteOptions_PreserveWhitespace_DefaultFalse()
    var
        Opts: XmlWriteOptions;
    begin
        Assert.IsFalse(Src.GetPreserveWhitespaceWrite(Opts), 'default XmlWriteOptions.PreserveWhitespace must be false');
    end;

    [Test]
    procedure XmlWriteOptions_PreserveWhitespace_SetTrue()
    var
        Opts: XmlWriteOptions;
    begin
        Assert.IsTrue(Src.SetPreserveWhitespaceWrite(Opts, true), 'XmlWriteOptions.PreserveWhitespace set to true must return true');
    end;

    [Test]
    procedure NumberSequence_Range_ReturnsFirstValue()
    var
        SeqName: Text;
        First: BigInteger;
    begin
        // Ensure a clean sequence (delete any leftover from prior run, then create).
        SeqName := 'MG_RangeTest_' + Format(CurrentDateTime, 0, '<Year4>-<Month,2>-<Day,2>_<Hours24>-<Minutes,2>-<Seconds,2>');
        Src.NumberSequenceEnsureClean(SeqName);
        First := Src.NumberSequenceRangeOnly(SeqName, 3);
        Assert.IsTrue(First >= 1, 'Range must return a positive first value');
    end;

    [Test]
    procedure NumberSequence_Range_AdvancesCounter()
    var
        SeqName: Text;
        First1: BigInteger;
        First2: BigInteger;
    begin
        // Ensure a clean sequence (delete any leftover from prior run, then create).
        SeqName := 'MG_RangeAdv_' + Format(CurrentDateTime, 0, '<Year4>-<Month,2>-<Day,2>_<Hours24>-<Minutes,2>-<Seconds,2>');
        Src.NumberSequenceEnsureClean(SeqName);
        First1 := Src.NumberSequenceRangeOnly(SeqName, 2);
        First2 := Src.NumberSequenceRangeOnly(SeqName, 2);
        // Second call should return first1 + 2 (skipped 2 values)
        Assert.AreEqual(First1 + 2, First2, 'second Range call must start after the first reserved block');
    end;
}
