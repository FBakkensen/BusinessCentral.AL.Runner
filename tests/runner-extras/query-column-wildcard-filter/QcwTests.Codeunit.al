namespace ALRunnerExtras.QueryColumnWildcardFilter;

codeunit 64603 "Qcw Tests"
{
    Subtype = Test;

    var
        Assert: Codeunit "Qcw Assert";

    [Test]
    procedure SetFilter_Wildcard_ReadsOnlyMatchingRows()
    var
        LocalRows: Query "Qcw Local Rows";
        Codes: Text;
    begin
        Seed('W1-A', 1);
        Seed('W1-B', 2);
        Seed('X1-C', 3);

        LocalRows.SetFilter(Code, 'W1-*');
        LocalRows.Open();
        while LocalRows.Read() do
            Codes += LocalRows.Code + ';';
        LocalRows.Close();

        Assert.AreEqual('W1-A;W1-B;', Codes, 'a wildcard filter on a Query column must narrow to the matching rows');
    end;

    [Test]
    procedure SetFilter_Wildcard_NoMatch_ReadsNothing()
    var
        LocalRows: Query "Qcw Local Rows";
        RowCount: Integer;
    begin
        Seed('W2-A', 1);

        LocalRows.SetFilter(Code, 'ZZ*');
        LocalRows.Open();
        while LocalRows.Read() do
            RowCount += 1;
        LocalRows.Close();

        Assert.AreEqual(0, RowCount, 'a wildcard that matches nothing must read no row');
    end;

    [Test]
    procedure SetFilter_NegatedWildcard_ExcludesMatchingRows()
    var
        LocalRows: Query "Qcw Local Rows";
        Codes: Text;
    begin
        Seed('W3-A', 1);
        Seed('W3-B', 2);
        Seed('X3-C', 3);

        LocalRows.SetFilter(Code, '<>W3-*');
        LocalRows.Open();
        while LocalRows.Read() do
            if CopyStr(LocalRows.Code, 2, 1) = '3' then
                Codes += LocalRows.Code + ';';
        LocalRows.Close();

        Assert.AreEqual('X3-C;', Codes, 'a negated wildcard must exclude the matching rows and keep the rest');
    end;

    [Test]
    procedure SetFilter_Range_ReadsRowsInsideTheRange()
    var
        LocalRows: Query "Qcw Local Rows";
        Total: Integer;
    begin
        Seed('W4-A', 10);
        Seed('W4-B', 20);
        Seed('W4-C', 30);

        LocalRows.SetFilter(Amount, '15..25');
        LocalRows.Open();
        while LocalRows.Read() do
            Total += LocalRows.Amount;
        LocalRows.Close();

        Assert.AreEqual(20, Total, 'a range filter on a Query column must keep only rows inside the range');
    end;

    [Test]
    procedure SetRange_Control_ReadsTheOneRow()
    var
        LocalRows: Query "Qcw Local Rows";
        RowCount: Integer;
    begin
        Seed('W5-A', 1);
        Seed('W5-B', 2);

        LocalRows.SetRange(Code, 'W5-B');
        LocalRows.Open();
        while LocalRows.Read() do begin
            RowCount += 1;
            Assert.AreEqual(2, LocalRows.Amount, 'control: SetRange reads the matching row');
        end;
        LocalRows.Close();

        Assert.AreEqual(1, RowCount, 'control: exactly one row matches the SetRange');
    end;

    local procedure Seed(CodeValue: Code[20]; AmountValue: Integer)
    var
        QcwLocal: Record "Qcw Local";
    begin
        QcwLocal.Init();
        QcwLocal."Code" := CodeValue;
        QcwLocal.Description := 'Row ' + CodeValue;
        QcwLocal.Amount := AmountValue;
        QcwLocal.Insert();
    end;
}
