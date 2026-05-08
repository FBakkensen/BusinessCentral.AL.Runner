// RecordPatches.NclMetaTableBuilder — turns ParsedTable into a real NCLMetaTable.
//
// NCLMetaTable is BC's runtime table-metadata object. It's normally built by
// NavGlobal.NCLMetadata from compiled .app metadata; we don't have that, so we
// reflectively call its NonPublic CreateFromMetaTable factory with a
// hand-constructed Microsoft.Dynamics.Nav.Types.Metadata.MetaTable. The data
// classes (MetaTable / MetaField / MetaKey / FieldMetadataRelation) live in
// Types.dll and have public ctors with many named/optional parameters — we
// resolve them by parameter name and fall back to defaults / zero-values for
// any we don't care about.
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Dynamics.Nav.Runtime;

namespace AlRunnerV2.Patches;

public static partial class RecordPatches
{
    private static Type? FindRecordType(int id)
    {
        var name = $"Record{id}";
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = Array.Find(asm.GetTypes(),
                    x => x.Name == name && typeof(NavRecord).IsAssignableFrom(x));
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static NCLMetaTable? BuildNCLMetaTable(int tableId)
    {
        if (!_parsedTables.TryGetValue(tableId, out var parsed)) return null;
        if (_tMetaTable == null || _mCreateFromMetaTable == null) return null;

        try
        {
            // Build MetaField[] — include a synthetic timestamp field (id=0, BigInteger)
            // and the SystemId system field (id=2000000000, Guid), which is required by
            // MutableRecordBuffer.get_SystemId → NCLMetaTable.SystemIdField.
            var timestampParsed = new ParsedField(0, "timestamp", "BigInteger", 0);
            var systemIdParsed = new ParsedField(2000000000, "SystemId", "Guid", 0);
            var allParsed = new[] { timestampParsed }.Concat(parsed.Fields)
                .Concat(new[] { systemIdParsed }).ToArray();
            var fields = allParsed.Select((f, idx) =>
                BuildMetaField(f, idx, parsed.PkFieldIds.Contains(f.FieldId))).ToArray();

            // Build primary key MetaKey via FieldMetadataRelation[]
            var pkRelations = parsed.PkFieldIds
                .Select(fid => BuildFieldMetadataRelation(fid))
                .ToArray();
            var pkKey = BuildMetaKey("PK", pkRelations, clustered: true);

            // Build MetaTable via named-parameter ctor.  The public ctor takes many
            // named params with defaults; we resolve by name and fall back to defaults.
            var defaultMetaTable = CallMetaTableCtor(tableId, parsed.TableName, fields, pkKey);
            if (defaultMetaTable == null) return null;

            // NavAppGroup.BaseGroup
            var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
            var tAppGroup = nclAsm.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppGroup")!;
            var baseGroup = tAppGroup.GetProperty("BaseGroup",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? tAppGroup.GetField("BaseGroup",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

            var built = (NCLMetaTable?)_mCreateFromMetaTable.Invoke(null,
                new object?[] { defaultMetaTable, baseGroup });

            // §O — mark metadataLoaded=true on every NCLMetaTable we construct, so when
            // NCLMetadata.GetMetaApplicationObjectInternal later loops and re-checks
            // `!nclMetaApplicationObject.MetadataLoaded`, it sees true and skips Populate()
            // (which would NRE on our hand-built instance — no NCLObjectXmlMetadataLoader,
            // no NavAppMetadata, etc.).
            if (built != null)
            {
                EnsureCachePopulatorReflection();
                if (_fNCLMetaAppObjMetadataLoaded != null)
                    AlRunnerV2.Infrastructure.FieldPoke.SetInstance(_fNCLMetaAppObjMetadataLoaded, built, true);
            }
            return built;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            Console.Error.WriteLine($"[RecordPatches] BuildNCLMetaTable({tableId}) failed: {inner.GetType().Name}: {inner.Message}");
            if (inner.StackTrace != null)
                Console.Error.WriteLine(inner.StackTrace.Split('\n')[0]);
            return null;
        }
    }

    private static object? CallMetaTableCtor(int id, string name, object[] fields, object pkKey)
    {
        if (_tMetaTable == null) return null;
        var ctor = _tMetaTable.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var ps = ctor.GetParameters();

        // Build arg array, filling in defaults where possible.
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.Name == "id") { args[i] = id; continue; }
            if (p.Name == "name") { args[i] = name; continue; }
            if (p.Name == "fields")
            {
                // ImmutableArray<MetaField>
                args[i] = MakeImmutableArray(_tMetaField!, fields);
                continue;
            }
            if (p.Name == "keys")
            {
                args[i] = MakeImmutableArray(_tMetaKey!, new[] { pkKey });
                continue;
            }
            if (p.Name == "fieldsById")
            {
                // Build ImmutableDictionary<int, MetaField> from fields
                var immDictType = typeof(ImmutableDictionary<,>).MakeGenericType(typeof(int), _tMetaField!);
                var builderMethod = immDictType.GetMethod("CreateRange",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, object>>) }, null);
                // Use ImmutableDictionary.CreateRange<TKey,TValue>(IEnumerable<KVP>)
                var createRangeMethod = typeof(ImmutableDictionary).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "CreateRange" && m.GetParameters().Length == 1)?
                    .MakeGenericMethod(typeof(int), _tMetaField!);
                if (createRangeMethod != null)
                {
                    // Build KeyValuePair<int, MetaField>[] from fields
                    var kvpType = typeof(System.Collections.Generic.KeyValuePair<,>).MakeGenericType(typeof(int), _tMetaField!);
                    var kvpArray = Array.CreateInstance(kvpType, fields.Length);
                    var kvpCtor = kvpType.GetConstructor(new[] { typeof(int), _tMetaField! })!;
                    for (int j = 0; j < fields.Length; j++)
                    {
                        var fid = (int)_tMetaField!.GetProperty("Id")!.GetValue(fields[j])!;
                        kvpArray.SetValue(kvpCtor.Invoke(new[] { (object)fid, fields[j] }), j);
                    }
                    args[i] = createRangeMethod.Invoke(null, new object[] { kvpArray })!;
                }
                else
                {
                    args[i] = null;
                }
                continue;
            }
            // Use parameter default if available.
            if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
                continue;
            }
            // Provide safe zero-values.
            args[i] = p.ParameterType.IsValueType
                ? Activator.CreateInstance(p.ParameterType)
                : null;
        }
        return ctor.Invoke(args);
    }

    private static object BuildMetaField(ParsedField f, int index, bool isPk)
    {
        var ctor = _tMetaField!.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.Name == "id") { args[i] = f.FieldId; continue; }
            if (p.Name == "name") { args[i] = f.FieldName; continue; }
            if (p.Name == "type") { args[i] = MapNavType(f.TypeName); continue; }
            if (p.Name == "length") { args[i] = f.Length; continue; }
            if (p.Name == "enabled") { args[i] = (bool?)true; continue; }
            if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }
            args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
        return ctor.Invoke(args)!;
    }

    private static object BuildMetaKey(string name, object[] fieldRelations, bool clustered)
    {
        var ctor = _tMetaKey!.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.Name == "name" || p.Name == "keyName") { args[i] = name; continue; }
            if (p.Name == "clustered") { args[i] = clustered; continue; }
            if (p.Name == "enabled") { args[i] = (bool?)true; continue; }
            if (p.Name == "fieldRelations")
            {
                args[i] = MakeImmutableArray(_tFieldMetadataRelation!, fieldRelations);
                continue;
            }
            if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }
            args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
        return ctor.Invoke(args)!;
    }

    private static object BuildFieldMetadataRelation(int fieldId)
    {
        var ctor = _tFieldMetadataRelation!.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.Name == "id") { args[i] = fieldId; continue; }
            if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }
            args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
        return ctor.Invoke(args)!;
    }

    private static object MakeImmutableArray(Type elementType, object[] elements)
    {
        // ImmutableArray<T>.Empty.AddRange(elements)
        var arrType = typeof(ImmutableArray<>).MakeGenericType(elementType);
        var empty = arrType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        if (elements.Length == 0) return empty;

        // Use ImmutableArray.CreateRange<T>(IEnumerable<T>)
        var createRange = typeof(ImmutableArray).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "CreateRange" && m.GetParameters().Length == 1)
            .MakeGenericMethod(elementType);
        // Cast elements to IEnumerable<elementType>
        var typedArray = Array.CreateInstance(elementType, elements.Length);
        for (int i = 0; i < elements.Length; i++) typedArray.SetValue(elements[i], i);
        return createRange.Invoke(null, new object[] { typedArray })!;
    }

    private static object MapNavType(string typeName)
    {
        // Map AL type name → NavType enum
        if (_tNavType == null) return 0;
        var n = typeName.Trim().ToUpperInvariant();
        // Code[N] → Code (NavType.Code = 8)
        if (n.StartsWith("CODE")) return Enum.Parse(_tNavType, "Code");
        if (n.StartsWith("TEXT")) return Enum.Parse(_tNavType, "Text");
        if (n == "INTEGER") return Enum.Parse(_tNavType, "Integer");
        if (n == "DECIMAL") return Enum.Parse(_tNavType, "Decimal");
        if (n == "BOOLEAN") return Enum.Parse(_tNavType, "Boolean");
        if (n == "DATE") return Enum.Parse(_tNavType, "Date");
        if (n == "TIME") return Enum.Parse(_tNavType, "Time");
        if (n == "DATETIME") return Enum.Parse(_tNavType, "DateTime");
        if (n.StartsWith("BIGINTEGER") || n == "BIGINT") return Enum.Parse(_tNavType, "BigInteger");
        if (n == "GUID") return Enum.Parse(_tNavType, "GUID");
        if (n == "BLOB") return Enum.Parse(_tNavType, "Blob");
        if (n.StartsWith("OPTION")) return Enum.Parse(_tNavType, "Option");
        return Enum.Parse(_tNavType, "Text"); // fallback
    }
}
