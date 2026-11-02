using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;

namespace PhotonCanvas.App.Addons;

/// <summary>
/// Avalonia remembers, for every control type it meets, the property metadata it looked up (in each
/// property and in the property registry, keyed by Type). An add-on's window or control class would
/// stay in those caches for good and keep the add-on in memory after it stops, so as it stops its
/// types are taken out. These are Avalonia's private fields: if a later Avalonia renames them this
/// finds nothing and the add-on simply stays in memory until the program closes, as before.
/// </summary>
static class AvaloniaTypeCaches
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Take the types that <paramref name="context"/> loaded out of Avalonia's per-type caches. Call on the UI thread.</summary>
    public static int Forget(AssemblyLoadContext context)
    {
        int removed = 0;
        try
        {
            bool Its(object? key) => key is Type t && AssemblyLoadContext.GetLoadContext(t.Assembly) == context;
            void Scrub(object o)
            {
                for (var t = o.GetType(); t is not null && t != typeof(object); t = t.BaseType)
                    foreach (var f in t.GetFields(Fields))
                        if (f.GetValue(o) is IDictionary d)
                            foreach (var k in d.Keys.Cast<object>().Where(Its).ToList()) { d.Remove(k); removed++; }
            }
            var registry = AvaloniaPropertyRegistry.Instance;
            Scrub(registry);
            if (typeof(AvaloniaPropertyRegistry).GetField("_properties", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(registry) is IDictionary all)
                foreach (var p in all.Values.Cast<object>().ToList()) Scrub(p);
        }
        catch (Exception ex)
        {
            AppLog.Error("addons: clearing Avalonia's caches", ex);
        }
        return removed;
    }
}
