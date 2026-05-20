// Table with NO explicit keys{} block.
// In BC, field 1 is implicitly the primary key in this case.
// al-runner must enforce PK uniqueness even when the key is not
// explicitly declared (falling back to field 1).
table 50200 "No Key Table"
{
    fields
    {
        field(1; "Entry No."; Integer) { }
        field(2; Description; Text[50]) { }
    }
}
