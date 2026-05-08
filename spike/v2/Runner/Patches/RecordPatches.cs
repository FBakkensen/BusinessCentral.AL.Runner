// RecordPatches.cs — Attempt A prototype: NavRecord redirection to BC's own TempTableDataProvider.
//
// Strategy:
//   1. Parse AL source files → MetaField/MetaKey/MetaTable (public data classes in Types.dll).
//   2. Call NCLMetaTable.CreateFromMetaTable (internal) via reflection → real NCLMetaTable.
//   3. Hook NavRecordHandle.CreateTarget → construct Record{ID} with real NCLMetaTable.
//   4. Hook NavSession.DataAccessSource getter → return skeleton DataAccessSource.
//   5. Hook DataAccessSource.GetDataAccessForTable → call CreateTempDataAccess on self.
//   6. Hook NavDatabase.CollationAwareStringComparer → return OrdinalIgnoreCase comparer.
//
// This file is a SPIKE — not production code. Goal: get ≥1 test in 02-record-operations to PASS.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunnerV2.Patches;

/// <summary>Builds real NCLMetaTable objects from AL source, bypassing NCLMetadata service.</summary>
public static partial class RecordPatches
{
    // Reflected BC types — populated by Register().
    private static Type? _tMetaTable;
    private static Type? _tMetaField;
    private static Type? _tMetaKey;
    private static Type? _tFieldMetadataRelation;
    private static Type? _tNavType;
    private static Type? _tFieldClass;
    private static Type? _tNCLMetaTable;
    private static MethodInfo? _mCreateFromMetaTable;
    private static MethodInfo? _mCreateForTempTable;
    private static FieldInfo? _fVatdcInstance;
    private static Type? _tDataAccessSource;
    private static MethodInfo? _mCreateTempDataAccess;
    private static Type? _tGlobalFilters;
    private static Type? _tNavDatabase;
    private static Type? _tCollationAwareStringComparer;
    private static Type? _tSqlSortingProperties;
    private static FieldInfo? _fNavDatabaseCollation;
    private static FieldInfo? _fNavDatabaseSqlSortingProperties;
    private static object? _sqlSortingProperties;     // pre-built SqlSortingProperties
    private static FieldInfo? _fSessionDataAccessSource;
    private static FieldInfo? _fDasSession;
    private static FieldInfo? _fDasGlobalFilters;
    private static FieldInfo? _fDasTableVersionTokens;
    private static FieldInfo? _fNavRecordHandleTemp;
    private static object? _skeletonDatabase;   // pre-built NavDatabase skeleton

    // TempTableDataProvider fields for manual construction (bypass session.Database in ctor)
    private static FieldInfo? _fTtdpNavSession;
    private static FieldInfo? _fTtdpTable;
    private static FieldInfo? _fTtdpComparer;
    private static FieldInfo? _fTtdpPrimaryKeySortingFields;
    private static PropertyInfo? _pNclMetaKeySortingFieldsWithPK;  // NCLMetaKey.SortingFieldsWithPrimaryKeyFields (internal)
    private static object? _collationComparer;   // pre-built CollationAwareStringComparer

    // Cache: tableId → NCLMetaTable built from AL source.
    private static readonly ConcurrentDictionary<int, object?> _metaTableCache = new();

    // Source directories scanned for AL table definitions.
    private static readonly List<string> _sourceDirs = new();

    // Parsed table schemas: tableId → (fields, pkFieldIds).
    private static readonly Dictionary<int, ParsedTable> _parsedTables = new();

    // Set to true once Register() has been called.
    private static bool _registered;

    public static void AddSourceDir(string dir)
    {
        if (!Directory.Exists(dir)) return;
        _sourceDirs.Add(dir);
        // If Register() already ran (it runs before the bucket loop), parse immediately
        // and feed the freshly-parsed tables into the NCLMetadata cache.
        if (_registered)
        {
            foreach (var file in Directory.GetFiles(dir, "*.al", SearchOption.AllDirectories))
                TryParseTableFile(File.ReadAllText(file));
            PopulateNclMetadataCache();
        }
    }

    /// <summary>
    /// Reflect on the BC assemblies and build NCLMetaTable objects from any AL sources added so far.
    /// Must be called after ForceLoadBcDlls() but before any test runs.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        var typesAsm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Types");
        var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");

        // Data types (Microsoft.Dynamics.Nav.Types)
        _tMetaTable = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.MetaTable")!;
        _tMetaField = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.MetaField")!;
        _tMetaKey   = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.MetaKey")!;
        _tFieldMetadataRelation = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.FieldMetadataRelation")!;
        _tNavType   = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.NavType")!;
        _tFieldClass = typesAsm.GetType("Microsoft.Dynamics.Nav.Types.Metadata.FieldClass")!;

        // NCLMetaTable and factory (Microsoft.Dynamics.Nav.Runtime / Ncl)
        _tNCLMetaTable = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaTable")!;
        _mCreateFromMetaTable = _tNCLMetaTable.GetMethod("CreateFromMetaTable",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // DataAccessTableVersionTokens.CreateForTempTable()
        var tDatv = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.DataAccessTableVersionTokens")!;
        _mCreateForTempTable = tDatv.GetMethod("CreateForTempTable",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

        // VirtualAndTempTransactionalDataCache.Instance
        var tVatdc = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.VirtualAndTempTransactionalDataCache")!;
        _fVatdcInstance = tVatdc.GetField("Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

        // DataAccessSource and CreateTempDataAccess
        _tDataAccessSource = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.DataAccessSource")!;
        _mCreateTempDataAccess = _tDataAccessSource.GetMethod("CreateTempDataAccess",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        // GlobalFilters (public ctor)
        _tGlobalFilters = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.GlobalFilters")!;

        // NavSession fields for DataAccessSource
        var tNavSession = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavSession")!;
        _fSessionDataAccessSource = tNavSession.GetField("<DataAccessSource>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // NavDatabase — skeleton instance (returned by NavSession.Database hook)
        _tNavDatabase = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavDatabase")!;
        _tCollationAwareStringComparer = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.CollationAwareStringComparer");
        _tSqlSortingProperties = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.SqlSortingProperties");
        _fNavDatabaseCollation = _tNavDatabase.GetField("collationAwareStringComparer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _fNavDatabaseSqlSortingProperties = _tNavDatabase.GetField("sqlSortingProperties",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Pre-build SqlSortingProperties so it's available for both the skeleton DB and the
        // NavSession.SortingProperties hook (used by RecordBufferComparer in TempTableDataProvider).
        _sqlSortingProperties = BuildSqlSortingProperties();

        // Build skeleton NavDatabase once — NavDatabase.CollationAwareStringComparer is JMP-hooked
        // so any non-null NavDatabase is sufficient; we just need it to not NRE.
        _skeletonDatabase = RuntimeHelpers.GetUninitializedObject(_tNavDatabase);
        if (_fNavDatabaseCollation != null && _tCollationAwareStringComparer != null)
        {
            var comparer = BuildCollationAwareComparer();
            if (comparer != null) _fNavDatabaseCollation.SetValue(_skeletonDatabase, comparer);
        }
        if (_fNavDatabaseSqlSortingProperties != null && _sqlSortingProperties != null)
            _fNavDatabaseSqlSortingProperties.SetValue(_skeletonDatabase, _sqlSortingProperties);
        Console.Error.WriteLine($"[RecordPatches] Skeleton NavDatabase built: {_skeletonDatabase.GetType().Name}");

        // DataAccessSource fields to poke when creating skeleton
        _fDasSession = _tDataAccessSource.GetField("session",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _fDasGlobalFilters = _tDataAccessSource.GetField("globalFilters",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _fDasTableVersionTokens = _tDataAccessSource.GetField("tableVersionTokens",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // TempTableDataProvider fields (for manual construction bypassing session.Database in ctor)
        var tTtdp = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.TempTableDataProvider")!;
        _fTtdpNavSession = tTtdp.GetField("navSession", BindingFlags.NonPublic | BindingFlags.Instance);
        _fTtdpTable = tTtdp.GetField("table", BindingFlags.NonPublic | BindingFlags.Instance);
        _fTtdpComparer = tTtdp.GetField("comparer", BindingFlags.NonPublic | BindingFlags.Instance);
        _fTtdpPrimaryKeySortingFields = tTtdp.GetField("primaryKeySortingFields",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // NCLMetaKey.SortingFieldsWithPrimaryKeyFields is internal — access via reflection
        var tNclMetaKey = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NCLMetaKey")!;
        _pNclMetaKeySortingFieldsWithPK = tNclMetaKey.GetProperty("SortingFieldsWithPrimaryKeyFields",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Pre-build and cache the collation comparer
        _collationComparer = BuildCollationAwareComparer();
        Console.Error.WriteLine($"[RecordPatches] Collation comparer built: {_collationComparer?.GetType().Name ?? "null"}");

        // Parse AL source files
        ParseAllSources();

        // §O: lazy-populate the skeleton NCLMetadata cache with one NCLMetaTable
        // per parsed table so NavGlobal.NCLMetadata.GetMetaTableById / Codeunit.Run
        // call sites find an entry instead of throwing
        // NavNCLApplicationObjectNotFoundException.
        PopulateNclMetadataCache();

        // NavRecordHandle private field 'temp'
        var tRecHandle = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavRecordHandle")!;
        _fNavRecordHandleTemp = tRecHandle.GetField("temp",
            BindingFlags.NonPublic | BindingFlags.Instance);
    }

    /// <summary>
    /// Replacement for NavSession.Database getter.
    /// NavSession.Database => Tenant.Database which requires a real tenant.
    /// Return the skeleton NavDatabase instead.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_get_Database(object self)
    {
        Console.Error.WriteLine($"[RecordPatches] NavSession_get_Database: _skeletonDatabase={_skeletonDatabase?.GetType().Name ?? "null"}");
        return _skeletonDatabase;
    }

    /// <summary>
    /// Replacement for TempTableDataProvider.ctor(NavSession, NCLMetaTable).
    /// The real ctor calls navSession.Database.CollationAwareStringComparer which NREs on our
    /// skeleton session (no Tenant). We manually set all fields instead.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TempTableDataProviderCtorReplacement(object self, NavSession session, NCLMetaTable table)
    {
        _fTtdpNavSession?.SetValue(self, session);
        _fTtdpTable?.SetValue(self, table);
        // NCLMetaKey.SortingFieldsWithPrimaryKeyFields is internal — use reflection
        var pkSortingFields = _pNclMetaKeySortingFieldsWithPK?.GetValue(table.PrimaryKey);
        _fTtdpPrimaryKeySortingFields?.SetValue(self, pkSortingFields);
        _fTtdpComparer?.SetValue(self, _collationComparer);
    }

    /// Pre-populate the skeleton session's dataAccessSource field directly.
    /// NavSession.DataAccessSource getter is a trivial field return and gets inlined by JIT,
    /// so the JMP hook on it never fires. We must inject the DAS into the field directly.
    /// </summary>
    public static void InitializeSkeletonSession(object skeletonSession)
    {
        Console.Error.WriteLine($"[RecordPatches] InitializeSkeletonSession: _fSessionDataAccessSource={_fSessionDataAccessSource != null}, _tDataAccessSource={_tDataAccessSource != null}");
        if (_fSessionDataAccessSource == null || _tDataAccessSource == null) return;

        // If already set, nothing to do.
        var existing = _fSessionDataAccessSource.GetValue(skeletonSession);
        Console.Error.WriteLine($"[RecordPatches] existing DAS on session: {existing}");
        if (existing != null) return;

        // Ensure skeleton DB (needed by TempTableDataProvider ctor via navSession.Database.CollationAwareStringComparer)
        EnsureSkeletonDatabase(skeletonSession);

        // Build skeleton DataAccessSource
        var das = RuntimeHelpers.GetUninitializedObject(_tDataAccessSource);
        _fDasSession!.SetValue(das, skeletonSession);
        _fDasGlobalFilters!.SetValue(das, Activator.CreateInstance(_tGlobalFilters!));
        _fDasTableVersionTokens!.SetValue(das, _mCreateForTempTable!.Invoke(null, null));

        // Inject directly into the session field (bypass the inlined getter)
        _fSessionDataAccessSource.SetValue(skeletonSession, das);
        Console.Error.WriteLine($"[RecordPatches] Skeleton DAS injected on session: {das.GetType().Name}");

        // Build a minimal NavSystemCodeunitFactory+GlobalTriggers on the skeleton company so that
        // NavRecord.IsGlobalTriggerImplemented doesn't NRE when it calls
        // Session.SystemCodeunitFactory.GlobalTriggers.GetTriggersOnTable().
        // The factory's GlobalTriggers.session is our skeleton which is not "IsCompanyOpen",
        // so GetTriggersOnTable() returns Triggers.None immediately.
        InjectSkeletonSystemCodeunitFactory(skeletonSession);
    }

    private static void InjectSkeletonSystemCodeunitFactory(object skeletonSession)
    {
        var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        if (nclAsm == null) return;

        var tFactory = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavSystemCodeunitFactory");
        var tGlobalTriggers = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavSystemCodeunitGlobalTriggers");
        var tNavCompany = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.NavCompany");
        if (tFactory == null || tGlobalTriggers == null || tNavCompany == null) return;

        // Get the skeleton company from the session.
        var companyField = skeletonSession.GetType().GetField("company",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var skeletonCompany = companyField?.GetValue(skeletonSession);
        if (skeletonCompany == null) return;

        // Build skeleton factory (uninitialized — the ctor requires a real TreeObject).
        var factory = RuntimeHelpers.GetUninitializedObject(tFactory);

        // Build skeleton GlobalTriggers (uninitialized).
        // NavSystemCodeunitGlobalTriggers.GetTriggersOnTable checks session.IsCompanyOpen first.
        // Our skeleton session has IsCompanyOpen = false (default bool), so it returns Triggers.None.
        var globalTriggers = RuntimeHelpers.GetUninitializedObject(tGlobalTriggers);
        var fSession = tGlobalTriggers.GetField("session",
            BindingFlags.NonPublic | BindingFlags.Instance);
        fSession?.SetValue(globalTriggers, skeletonSession);

        // Wire global triggers into factory.
        var fGlobalTriggers = tFactory.GetField("globalTriggers",
            BindingFlags.NonPublic | BindingFlags.Instance);
        fGlobalTriggers?.SetValue(factory, globalTriggers);

        // Inject factory into NavCompany.SystemCodeunitFactory auto-property backing field.
        var fFactory = tNavCompany.GetField("<SystemCodeunitFactory>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        fFactory?.SetValue(skeletonCompany, factory);
    }

    // ─── Hook Implementations ───────────────────────────────────────────────────

    /// <summary>
    /// Replacement for NavRecordHandle.CreateTarget():
    /// bypasses NCLMetadata by constructing Record{ID} directly with a real NCLMetaTable
    /// built from parsed AL source.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static NavRecord NavRecordHandle_CreateTarget(NavRecordHandle self)
    {
        int id = self.ObjectId.ObjectNumber;
        bool isTemp = _fNavRecordHandleTemp != null && (bool)(_fNavRecordHandleTemp.GetValue(self) ?? false);

        var metaTable = (NCLMetaTable?)_metaTableCache.GetOrAdd(id, BuildNCLMetaTable);
        if (metaTable == null)
            throw new InvalidOperationException(
                $"NavRecordHandle.CreateTarget: no NCLMetaTable for table {id} (AL source not parsed)");

        // Find Record{ID} : NavRecord in the loaded test assembly.
        var recordType = FindRecordType(id);
        if (recordType == null)
            throw new InvalidOperationException(
                $"NavRecordHandle.CreateTarget: no loaded type Record{id} found");

        var ctor = recordType.GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Length == 6);
        if (ctor == null)
            throw new InvalidOperationException($"Record{id} has no 6-arg constructor");

        // Construct Record{ID}(parent, metaTable, isTemporary, sharedTable, companyName, securityFiltering)
        return (NavRecord)ctor.Invoke(new object?[] { self, metaTable, isTemp, null, null,
            SecurityFiltering.Ignored });
    }

    /// <summary>
    /// Replacement for NavSession.DataAccessSource getter.
    /// Returns a skeleton DataAccessSource backed by TempTableDataProvider (in-memory).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_get_DataAccessSource(NavSession self)
    {
        Console.Error.WriteLine("[RecordPatches] NavSession_get_DataAccessSource called");
        // Return cached DataAccessSource stored on the session's field.
        var existing = _fSessionDataAccessSource?.GetValue(self);
        if (existing != null) return existing;

        // Ensure the session has a skeleton NavDatabase — needed by TempTableDataProvider ctor.
        EnsureSkeletonDatabase(self);

        // Build a skeleton DataAccessSource.
        var das = RuntimeHelpers.GetUninitializedObject(_tDataAccessSource!);
        _fDasSession!.SetValue(das, self);
        _fDasGlobalFilters!.SetValue(das, Activator.CreateInstance(_tGlobalFilters!));
        _fDasTableVersionTokens!.SetValue(das, _mCreateForTempTable!.Invoke(null, null));

        // Cache it on the session field.
        _fSessionDataAccessSource?.SetValue(self, das);
        return das;
    }

    private static void EnsureSkeletonDatabase(object session)
    {
        // _skeletonDatabase is pre-built in Register(); NavSession.Database is JMP-hooked to return it.
        // Nothing to inject on the session object itself.
    }

    private static object? BuildSqlSortingProperties()
    {
        if (_tSqlSortingProperties == null) return null;
        try
        {
            // SqlSortingProperties(CultureInfo culture, CompareOptions compareOptions, string collation)
            var sortingPropsCtor = _tSqlSortingProperties.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => {
                    var ps = c.GetParameters();
                    return ps.Length == 3
                        && ps[0].ParameterType == typeof(System.Globalization.CultureInfo)
                        && ps[1].ParameterType == typeof(System.Globalization.CompareOptions);
                });
            if (sortingPropsCtor == null) return null;
            return sortingPropsCtor.Invoke(new object[] {
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.CompareOptions.IgnoreCase,
                "Latin1_General_CI_AS"
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RecordPatches] BuildSqlSortingProperties failed: {ex.Message}");
            return null;
        }
    }

    private static object? BuildCollationAwareComparer()
    {
        if (_tCollationAwareStringComparer == null || _tSqlSortingProperties == null) return null;
        var sortingProps = _sqlSortingProperties ?? BuildSqlSortingProperties();
        if (sortingProps == null) return null;
        try
        {
            var compCtor = _tCollationAwareStringComparer
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => c.GetParameters().Length == 1
                    && c.GetParameters()[0].ParameterType == _tSqlSortingProperties);
            return compCtor?.Invoke(new[] { sortingProps });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RecordPatches] BuildCollationAwareComparer failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Replacement for NavSession.get_SortingProperties — Database.SqlSortingProperties NREs because
    /// the skeleton NavDatabase does not have a collation set up for the lazy-init path. Return the
    /// pre-built SqlSortingProperties from RecordPatches.Register.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_get_SortingProperties(object self) => _sqlSortingProperties;

    /// <summary>
    /// Replacement for DataAccessSource.GetDataAccessForTable(NCLMetaTable, bool).
    /// Ignores the isTemporary flag — always routes to TempTableDataProvider (in-memory).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object NavDataAccessSource_GetDataAccessForTable(object self, NCLMetaTable table, bool isTemporary)
    {
        try
        {
            var result = _mCreateTempDataAccess!.Invoke(self, new object[] { table })!;
            return result;
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Console.Error.WriteLine($"[RecordPatches] GetDataAccessForTable failed for table {table?.TableName ?? "null"}: {inner.GetType().Name}: {inner.Message}");
            Console.Error.WriteLine(inner.StackTrace ?? "");
            throw;
        }
    }

    /// <summary>
    /// Replacement for NavDatabase.CollationAwareStringComparer getter.
    /// Returns a CollationAwareStringComparer using InvariantCulture + IgnoreCase.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavDatabase_get_CollationAwareStringComparer(NavDatabase self)
    {
        // Check if already populated on the skeleton instance (set by EnsureSkeletonDatabase).
        if (_fNavDatabaseCollation != null)
        {
            var existing = _fNavDatabaseCollation.GetValue(self);
            if (existing != null) return existing;
        }
        var built = BuildCollationAwareComparer();
        if (built != null && _fNavDatabaseCollation != null)
            _fNavDatabaseCollation.SetValue(self, built);
        return built;
    }

    /// <summary>
    /// Replacement for NCLMetaApplicationObject.get_ApplicationObjectClrType.
    /// The real getter does lock(nclMetaObjectCLRTypeContainer) which NREs when the container
    /// is null (our CreateFromMetaTable-built tables never go through NCLCodeLoader).
    /// Instead, look up Record{ID} in the currently-loaded assemblies.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Type? NCLMetaApplicationObject_get_ApplicationObjectClrType(object self)
    {
        // NCLMetaApplicationObject has private readonly ApplicationObjectId objectId.
        var objIdField = self.GetType().GetField("objectId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (objIdField == null) return null;
        var objId = objIdField.GetValue(self);
        if (objId == null) return null;
        var numProp = objId.GetType().GetProperty("ObjectNumber",
            BindingFlags.Public | BindingFlags.Instance);
        if (numProp == null) return null;
        int id = (int)numProp.GetValue(objId)!;
        return FindRecordType(id);
    }

    /// <summary>
    /// Replacement for SequentialUuidCreator.NativeMethods.NewSequentialId.
    /// The original P/Invokes rpcrt4.dll!UuidCreateSequential which doesn't exist on Linux.
    /// Replace with a standard Guid.NewGuid() on all platforms.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Guid NewSequentialId_Replacement()
        => Guid.NewGuid();
}
