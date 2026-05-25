codeunit 50627 "FPB FieldNo Src"
{
    /// AddFieldNo with name, table name, and field number.
    /// This matches the telemetry pattern: AddFieldNo(TableName(), FieldNo("..."))
    procedure AddFieldNoAndGetCount(): Integer
    var
        Rec: Record "FPB FieldNo Table";
        FPB: FilterPageBuilder;
    begin
        // In BC, AddFieldNo requires the table to be registered in the FilterPageBuilder first.
        FPB.AddRecord(Rec.TableName(), Rec);
        FPB.AddFieldNo(Rec.TableName(), Rec.FieldNo("Task No."));
        exit(FPB.Count);
    end;
}
