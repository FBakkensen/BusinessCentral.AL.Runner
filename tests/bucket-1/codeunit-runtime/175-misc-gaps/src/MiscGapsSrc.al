/// Helper codeunit exercising misc gaps — issue #776.
codeunit 50138 "MG Src"
{
    procedure GetJsonTokenPath(Token: JsonToken): Text
    begin
        exit(Token.Path());
    end;

    procedure GetTimeMillisecond(T: Time): Integer
    begin
        exit(T.Millisecond());
    end;

    procedure GetXmlNodeListItem(NodeList: XmlNodeList; Idx: Integer): Boolean
    var
        Node: XmlNode;
    begin
        NodeList.Get(Idx, Node);
        exit(Node.IsXmlElement());
    end;

    procedure GetPreserveWhitespaceRead(Opts: XmlReadOptions): Boolean
    begin
        exit(Opts.PreserveWhitespace());
    end;

    procedure SetPreserveWhitespaceRead(Opts: XmlReadOptions; Val: Boolean): Boolean
    begin
        Opts.PreserveWhitespace(Val);
        exit(Opts.PreserveWhitespace());
    end;

    procedure GetPreserveWhitespaceWrite(Opts: XmlWriteOptions): Boolean
    begin
        exit(Opts.PreserveWhitespace());
    end;

    procedure SetPreserveWhitespaceWrite(Opts: XmlWriteOptions; Val: Boolean): Boolean
    begin
        Opts.PreserveWhitespace(Val);
        exit(Opts.PreserveWhitespace());
    end;

    procedure NumberSequenceRange(SeqName: Text; Count: BigInteger): BigInteger
    var
        FirstVal: BigInteger;
    begin
        if not NumberSequence.Exists(SeqName) then
            NumberSequence.Insert(SeqName, 1, 1, false);
        FirstVal := NumberSequence.Range(SeqName, Count, false);
        exit(FirstVal);
    end;

    procedure NumberSequenceEnsureClean(SeqName: Text)
    begin
        // Delete a leftover sequence before (re)creating it in a test.
        if NumberSequence.Exists(SeqName) then
            NumberSequence.Delete(SeqName);
        NumberSequence.Insert(SeqName, 1, 1, false);
    end;

    procedure NumberSequenceRangeOnly(SeqName: Text; Count: BigInteger): BigInteger
    begin
        // Call Range without creating — sequence must already exist.
        exit(NumberSequence.Range(SeqName, Count, false));
    end;
}
