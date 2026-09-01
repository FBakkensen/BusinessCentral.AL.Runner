namespace ALRunnerExtras.QueryColumnWildcardFilter;

query 64601 "Qcw Local Rows"
{
    QueryType = Normal;

    elements
    {
        dataitem(QcwLocal; "Qcw Local")
        {
            column(Code; "Code") { }
            column(Description; Description) { }
            column(Amount; Amount) { }
        }
    }
}
