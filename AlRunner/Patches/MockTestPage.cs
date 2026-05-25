// MockTestPage — lightweight ITestPage / ITestField / ITestAction implementations
// for the runner's NavTestPage vtable fix.
//
// NavTestPageHandle_CreateTarget constructs a real NavTestPage via its internal
// 3-arg ctor passing a MockITestPage as the ITestPage.  Cecil IL rewrites in
// NclCecilRewrite ensure the runtime never calls out to the real TestPageClient
// or TestClientProxy.Proxy, so these mocks only need to satisfy the direct method
// calls NavTestPageBase.GetField / GetAction / GetDataItem make into them.
using System;
using System.Collections.Generic;
using Microsoft.Dynamics.Nav.Types;
using Microsoft.Dynamics.Nav.Types.Data;

namespace AlRunnerV2;

/// <summary>
/// Minimal ITestPage + ITestFilter + IDisposable implementation.
/// All field/action/filter state is held in plain dictionaries; navigation
/// always reports "no more rows" (returns false / empty).
/// </summary>
internal class MockITestPage : ITestPage
{
    private readonly Dictionary<int, string>      _filters     = new();
    private readonly Dictionary<int, MockITestField>  _fields  = new();
    private readonly Dictionary<int, MockITestAction> _actions = new();
    private bool   _ascending        = true;
    private int[]? _currentKeyFields;

    // ── ITestPage ──────────────────────────────────────────────────────────

    // IsOpened() = false so NavTestPageBase.Open() "already open" guard passes.
    public bool IsOpened()  => false;
    public void Close()     { }
    public void Dispose()   { }

    public ITestField GetField(int id)
    {
        if (!_fields.TryGetValue(id, out var f))
            _fields[id] = f = new MockITestField();
        return f;
    }

    public ITestAction GetAction(int id)
    {
        if (!_actions.TryGetValue(id, out var a))
            _actions[id] = a = new MockITestAction();
        return a;
    }

    public ITestPart          GetPart(int id)                                           => new MockITestPart();
    public ITestAction        GetBuiltInAction(FormResult formResult)                   => new MockITestAction();
    public ITestFilter        GetDataItemFilter(string id)                              => this;
    public void               SetSelection(bool value)                                  { }
    public void               InsertEmptyRow(bool beforeCurrent)                        { }
    public bool               MoveNext()                                                => false;
    public bool               MovePrevious()                                            => false;
    public bool               MoveFirst()                                               => false;
    public bool               MoveLast()                                                => false;
    public string             GetValidationError(int index)                             => string.Empty;
    public bool               FindRowFromTableFieldValues(int[] f, object[] v, bool fw) => false;
    public bool               FindRowFromControlFieldValue(int fId, object v, bool fw)  => false;
    public object?            GetBookmark()                                             => null;
    public bool               GoToBookmark(object bookmark)                             => false;
    public object[]           GetTableFieldValues(int[] fieldIds)                       => Array.Empty<object>();
    public ITestAction        Edit()                                                    => new MockITestAction();
    public ITestAction        View()                                                    => new MockITestAction();
    public bool               Expand(bool doExpand)                                     => false;

    public int        ValidationErrorCount => 0;
    public FormResult FormResult           => FormResult.OK;
    public string     Name                 => string.Empty;
    public string     Caption              => string.Empty;
    public int        PageId               => 0;
    public Guid       FormHandle           => Guid.Empty;
    public bool       Creatable            => false;
    public bool       IsExpanded           => false;
    public bool       RuntimeEditable      => true;

    // ── ITestFilter (inherited via ITestPage) ─────────────────────────────

    public void   SetFilter(int fieldId, string filterValue) => _filters[fieldId] = filterValue;
    public IEnumerable<NavFilter> GetFilter() => Array.Empty<NavFilter>();
    public string GetFilter(int fieldId) => _filters.TryGetValue(fieldId, out var v) ? v : string.Empty;
    public void   SetCurrentKeyFields(int[] fields) { _currentKeyFields = fields; }
    public int[]  GetCurrentKeyFields() => _currentKeyFields ?? Array.Empty<int>();

    public bool   Ascending
    {
        get => _ascending;
        set => _ascending = value;
    }

    public string CurrentKey
    {
        get
        {
            if (_currentKeyFields == null || _currentKeyFields.Length == 0) return string.Empty;
            return string.Join(", ", _currentKeyFields);
        }
    }
}

/// <summary>Minimal ITestField implementation — all reads return safe defaults.</summary>
internal sealed class MockITestField : ITestField
{
    private string _value = string.Empty;

    public string Value         { get => _value; set => _value = value; }
    public string Name          => string.Empty;
    public string Caption       => string.Empty;
    public NavType FieldType    => NavType.Text;
    public int    ValidationErrorCount        => 0;
    public long   LastUsedValidationErrorId   => 0;
    public long   MaxValidationErrorId        => 0;
    public object? ObjectValue               => _value;
    public int    OptionCount                => 0;
    public bool   Enabled                   => true;
    public bool   Editable                  => true;
    public bool   Visible                   => true;
    public bool   HideValue                 => false;
    public bool   ShowMandatory             => false;

    public string GetValidationError(int index)   => string.Empty;
    public void   Activate()                      { }
    public void   Lookup()                        { }
    public void   Lookup(NavDataSet dataSet)      { }
    public void   AssistEdit()                    { }
    public void   Drilldown()                     { }
    public void   Invoke()                        { }
    public string ValueToString(object? value)    => value?.ToString() ?? string.Empty;
    public string GetOption(int index)            => string.Empty;
}

/// <summary>Minimal ITestAction implementation — Invoke is a no-op.</summary>
internal sealed class MockITestAction : ITestAction
{
    public void Invoke()         { }
    public bool Visible          => true;
    public bool Enabled          => true;
}

/// <summary>
/// Minimal ITestPart implementation.
/// ITestPart extends ITestPage + ITestFilter + IDisposable, so this derives
/// from MockITestPage which already implements all required members.
/// </summary>
internal sealed class MockITestPart : MockITestPage, ITestPart
{
    new public bool Enabled => true;
    new public bool Visible => true;
}
