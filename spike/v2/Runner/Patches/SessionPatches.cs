// SessionPatches — replacements for NavSession property/method NREs.
//
// The skeleton NavSession is constructed with GetUninitializedObject so most of its
// internal state (globalLanguageStack, Database.SecurityAndLicense, cultureSettings,
// Diagnostics, …) is null. These replacements give safe defaults that let downstream
// code paths complete without NREs.
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AlRunnerV2;

public static partial class BcRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetSessionReplacement(object self) => _skeletonSession;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? GetCurrentMethodScopeReplacement(object self) => _skeletonRootScope;

    /// <summary>
    /// Replacement for TreeHandler.get_Session.
    /// The tree hierarchy is built from skeleton objects whose session fields are null.
    /// Always return the skeleton session so NavRecord.ctor and NavApplicationObjectBase.ctor
    /// can access a non-null session without needing a real BC tree.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? TreeHandler_get_Session(object self) => _skeletonSession;

    private static object? _baseAppGroup;

    /// <summary>
    /// Replacement for NavSession.get_NavAppGroup. The real getter accesses
    /// <c>tenant.NavAppGroup</c>; on the skeleton session, <c>tenant</c> is null
    /// so the original NREs. NavForm..ctor reads this to resolve the page's
    /// owning app group. Return <c>NavAppGroup.BaseGroup</c> (the platform-base
    /// singleton already used by the metadata cache builders) so page/report
    /// ctors can complete.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_NavAppGroup(object? self)
    {
        if (_baseAppGroup != null) return _baseAppGroup;
        var nclAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Dynamics.Nav.Ncl");
        var tAppGroup = nclAsm?.GetType("Microsoft.Dynamics.Nav.Runtime.Apps.NavAppGroup");
        _baseAppGroup = tAppGroup?.GetProperty("BaseGroup",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? tAppGroup?.GetField("BaseGroup",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return _baseAppGroup;
    }

    /// <summary>
    /// Replacement for NavSession.get_LocalLanguageNoFallback.
    /// The real getter reads globalLanguageStack which is null in our skeleton session.
    /// Return -1 = "no override, use default language" (same as empty stack result).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int NavSession_LocalLanguageNoFallback(object? self) => -1;

    /// <summary>
    /// Replacement for NavSession.GetSecurityFilters — bypasses Database.SecurityAndLicense which
    /// NREs on the skeleton database. Return null; RecordImplementation treats null as "no security
    /// filters" (matches the IsPermissionSystemEnabled=false code path in the original method).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? NavSession_GetSecurityFilters(object self,
        int companyNameToken, int tableId, object securityFilterType,
        object? callingObject, object? securableObject) => null;

    /// <summary>
    /// Replacement for NavSession.SyncFormatSettings().
    /// Accesses cultureSettings (null in skeleton) → NRE.  Return a default FormatSettings.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Microsoft.Dynamics.Nav.Runtime.FormatSettings NavSession_SyncFormatSettings(object? self)
        => new Microsoft.Dynamics.Nav.Runtime.FormatSettings();

    /// <summary>
    /// Replacement for NavIntegerFormatter.FormatWithFormatNumber.
    /// Real body calls value.ToInt32().ToString("d", session.WindowsCulture); on the
    /// skeleton runtime the NavValue passed in can be null (NavValue[] entries
    /// uninitialized in the AL emit's varargs-build), which NREs. Bypass: format
    /// any non-null int value with InvariantCulture; null becomes empty string.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string NavIntegerFormatter_FormatWithFormatNumber(
        object self,
        object? session,
        object? value,
        int length,
        int formatNumber,
        object formatsetting)
    {
        if (value == null) return string.Empty;
        try
        {
            // NavValue.ToInt32() — call via reflection to avoid hard reference.
            var toInt32 = value.GetType().GetMethod("ToInt32",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (toInt32 != null)
            {
                var i = (int)toInt32.Invoke(value, null)!;
                return i.ToString("d", CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Replacement for NavSession.get_Culture / get_WindowsCulture.
    /// The real getters call CultureInfo.GetCultureInfo(int) with a culture id that
    /// is 0 on the skeleton session (uninitialized field) and throws
    /// ArgumentOutOfRangeException ("culture must be a non-negative and non-zero value").
    /// Return InvariantCulture so format/parse paths work in headless mode.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CultureInfo NavSession_get_Culture(object? self) => CultureInfo.InvariantCulture;
}
