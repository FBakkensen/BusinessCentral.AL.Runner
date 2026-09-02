codeunit 64673 "Acf Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    // Seed: item A has +3 and -3 (total 0), item B has +5 (total 5), all on project P1.

    [Test]
    procedure ColumnFilterOnSum_ExcludesZeroTotalGroup()
    var
        Q: Query "Acf Balances";
        Rows: Integer;
        LastItem: Code[20];
    begin
        Seed();
        Q.SetRange(ProjectNoFilter, 'P1');
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            LastItem := Q.ItemNo;
            if Q.AssignedQuantity <= 0 then
                Error('group %1 returned with AssignedQuantity %2 despite ColumnFilter > 0', Q.ItemNo, Q.AssignedQuantity);
        end;
        if Rows <> 1 then
            Error('expected 1 group (item B), got %1 (last item %2)', Rows, LastItem);
        if LastItem <> 'B' then
            Error('expected item B, got %1', LastItem);
    end;

    [Test]
    procedure NoColumnFilter_ReturnsZeroTotalGroup()
    var
        Q: Query "Acf Balances No Filter";
        Rows: Integer;
        SawZero: Boolean;
    begin
        Seed();
        Q.SetRange(ProjectNoFilter, 'P1');
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            if Q.AssignedQuantity = 0 then
                SawZero := true;
        end;
        if Rows <> 2 then
            Error('expected 2 groups, got %1', Rows);
        if not SawZero then
            Error('expected the zero-total group to be returned');
    end;

    [Test]
    procedure RuntimeSetFilterOnSum_ExcludesZeroTotalGroup()
    var
        Q: Query "Acf Balances No Filter";
        Rows: Integer;
    begin
        Seed();
        Q.SetRange(ProjectNoFilter, 'P1');
        Q.SetFilter(AssignedQuantity, '>0');
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            if Q.AssignedQuantity <= 0 then
                Error('group %1 returned with AssignedQuantity %2 despite SetFilter > 0', Q.ItemNo, Q.AssignedQuantity);
        end;
        if Rows <> 1 then
            Error('expected 1 group, got %1', Rows);
    end;

    // ColumnFilter on a NON-aggregated column is a WHERE filter: only item B's group comes back.
    [Test]
    procedure ColumnFilterConstOnPlainColumn_FiltersRows()
    var
        Q: Query "Acf Balances Const";
        Rows: Integer;
    begin
        Seed();
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            if Q.ItemNo <> 'B' then
                Error('unexpected item %1: ColumnFilter const(B) must exclude it', Q.ItemNo);
            if Q.AssignedQuantity <> 5 then
                Error('unexpected sum %1 for item B', Q.AssignedQuantity);
        end;
        if Rows <> 1 then
            Error('expected 1 row, got %1', Rows);
    end;

    // Real BC 28.4: a runtime SetFilter on a column REPLACES its static ColumnFilter. With
    // static > 0 and runtime < 4, the zero-total group A is returned and B (5) is not.
    [Test]
    procedure RuntimeSetFilterReplacesStaticColumnFilter_Narrower()
    var
        Q: Query "Acf Balances";
        Rows: Integer;
        Items: Text;
    begin
        Seed();
        Q.SetFilter(AssignedQuantity, '<4');
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            Items += Q.ItemNo + '=' + Format(Q.AssignedQuantity) + ';';
        end;
        if Items <> 'A=0;' then
            Error('expected only A=0; (runtime filter replaces the static one), got %1 row(s): %2', Rows, Items);
    end;

    // Same rule, widening: static > 0 replaced by runtime <> 99 returns both groups.
    [Test]
    procedure RuntimeSetFilterReplacesStaticColumnFilter_Wider()
    var
        Q: Query "Acf Balances";
        Rows: Integer;
        Items: Text;
    begin
        Seed();
        Q.SetFilter(AssignedQuantity, '<>99');
        Q.Open();
        while Q.Read() do begin
            Rows += 1;
            Items += Q.ItemNo + '=' + Format(Q.AssignedQuantity) + ';';
        end;
        if Items <> 'A=0;B=5;' then
            Error('expected A=0;B=5; (runtime filter replaces the static one), got %1 row(s): %2', Rows, Items);
    end;

    local procedure Seed()
    var
        E: Record "Acf Entry";
    begin
        E.DeleteAll();
        Add(E, 1, 'A', 3);
        Add(E, 2, 'A', -3);
        Add(E, 3, 'B', 5);
    end;

    local procedure Add(var E: Record "Acf Entry"; No: Integer; Item: Code[20]; Qty: Decimal)
    begin
        E.Init();
        E."Entry No." := No;
        E."Project No." := 'P1';
        E."Item No." := Item;
        E.Quantity := Qty;
        E.Insert();
    end;
}
