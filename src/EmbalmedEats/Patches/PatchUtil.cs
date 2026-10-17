using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace EmbalmedEats.Patches;

/// <summary>
/// Harmony helpers. The key trick for compatibility: vanilla and modded food classes often
/// override methods like UpdateAndGetTransitionStates without calling the base version, so
/// patching only the base method would miss them. FindBaseAndOverrides finds the base method
/// plus every override in every loaded assembly (vanilla and all mods) so each gets patched.
/// </summary>
public static class PatchUtil
{
    public static ILogger? Logger;
    public static readonly List<string> Patched = new();
    public static readonly List<string> Failures = new();

    static readonly Dictionary<Type, List<Type>> derivedTypesCache = new();
    static readonly HashSet<MethodBase> alreadyPatched = new();

    public static List<Type> DerivedTypes(Type baseType)
    {
        if (derivedTypesCache.TryGetValue(baseType, out var cached)) return cached;

        var result = new List<Type>();
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;

            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }

            foreach (Type? t in types)
            {
                if (t == null || t == baseType || t.ContainsGenericParameters) continue;
                try
                {
                    if (baseType.IsAssignableFrom(t)) result.Add(t);
                }
                catch { /* type with unloadable dependencies */ }
            }
        }

        derivedTypesCache[baseType] = result;
        return result;
    }

    /// <summary>The method on baseType plus every override of it declared in derived types.</summary>
    public static List<MethodInfo> FindBaseAndOverrides(Type baseType, string name, Type[] paramTypes)
    {
        var result = new List<MethodInfo>();
        MethodInfo? baseMethod = AccessTools.Method(baseType, name, paramTypes);
        if (baseMethod == null)
        {
            Failures.Add($"Method not found: {baseType.Name}.{name}");
            return result;
        }
        result.Add(baseMethod);

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (Type t in DerivedTypes(baseType))
        {
            MethodInfo? m;
            try { m = t.GetMethod(name, flags, null, paramTypes, null); }
            catch { continue; }

            if (m == null || m.IsAbstract) continue;
            try
            {
                if (m.GetBaseDefinition().DeclaringType != baseMethod.GetBaseDefinition().DeclaringType) continue; // a "new" method, not an override
                if (m.GetMethodBody() == null) continue;
            }
            catch { continue; }

            result.Add(m);
        }

        return result;
    }

    public static HarmonyMethod? HM(Type holder, string? methodName)
    {
        if (methodName == null) return null;
        MethodInfo? m = AccessTools.Method(holder, methodName);
        if (m == null) throw new InvalidOperationException($"Patch method {holder.Name}.{methodName} missing");
        return new HarmonyMethod(m);
    }

    public static void Patch(Harmony harmony, MethodBase? target, Type holder, string? prefix = null, string? postfix = null, string? finalizer = null, string label = "")
    {
        if (target == null)
        {
            Failures.Add($"{label}: target method not found");
            Logger?.Warning("[Embalmed Eats] Could not find method to patch for {0}", label);
            return;
        }
        if (!alreadyPatched.Add(target)) return;

        try
        {
            harmony.Patch(target, HM(holder, prefix), HM(holder, postfix), null, HM(holder, finalizer));
            Patched.Add($"{label}: {target.DeclaringType?.FullName}.{target.Name}");
        }
        catch (Exception e)
        {
            Failures.Add($"{label}: {target.DeclaringType?.FullName}.{target.Name}: {e.Message}");
            Logger?.Warning("[Embalmed Eats] Failed to patch {0}.{1} ({2}): {3}", target.DeclaringType?.FullName, target.Name, label, e.Message);
        }
    }

    public static void PatchAll(Harmony harmony, IEnumerable<MethodInfo> targets, Type holder, string? prefix = null, string? postfix = null, string? finalizer = null, string label = "")
    {
        foreach (MethodInfo m in targets) Patch(harmony, m, holder, prefix, postfix, finalizer, label);
    }

    public static int CountPatched(string label) => Patched.Count(p => p.StartsWith(label + ":"));

    public static void Reset()
    {
        Patched.Clear();
        Failures.Clear();
        alreadyPatched.Clear();
        derivedTypesCache.Clear();
    }
}
