namespace ALRunnerExtras.QueryDependencyTable;

// Control arm: the same Query shape over an application-local table, whose RelatedTable in
// the symbol reference is a plain name with no module qualifier.
query 64582 "Qdt Local Rows"
{
    Access = Public;
    QueryType = Normal;

    elements
    {
        dataitem(QdtLocal; "Qdt Local")
        {
            column(Code; "Code")
            {
            }
            column(Description; Description)
            {
            }
        }
    }
}
