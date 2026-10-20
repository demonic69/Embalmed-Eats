using System;
using System.Linq;
using System.Runtime.CompilerServices;
using EmbalmedEats.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace EmbalmedEats.Core;

public static class PreservationMethods
{
    public const string Wax = "wax";
    public const string Resin = "resin";
    public const string Formaldehyde = "formaldehyde";
    public const string SaltCure = "saltcure";

    public static readonly string[] All = { Wax, Resin, Formaldehyde, SaltCure };

    public static bool IsKnown(string? method) => method != null && Array.IndexOf(All, method) >= 0;
}

/// <summary>
/// Preserved food stays the exact same item (so it keeps its own model, texture and any
/// attributes other mods put on it). Preservation is only a marker in the stack attributes
/// plus the vanilla "timeFrozen" flag, which the base game already honours for rotting.
/// </summary>
public static class Preservation
{
    public const string AttrKey = "embalmedeats";
    public const string MethodKey = "method";
    public const string TimeFrozenKey = "timeFrozen";
    const int MaxContentDepth = 3;

    public static EmbalmedEatsConfig Config = new();

    static ConditionalWeakTable<CollectibleObject, object> preservableCache = new();
    static readonly object True = true, False = false;

    public static void ClearCache() => preservableCache = new ConditionalWeakTable<CollectibleObject, object>();

    /// <summary>
    /// Returns the preservation method of a stack, or null when it is not preserved.
    /// A container stack (meal bowl, pie, pot) also counts as preserved when every item inside it is.
    /// This runs in hot paths (every transition tick), so it only does dictionary lookups.
    /// </summary>
    public static string? GetMethod(ItemStack? stack) => GetMethod(stack, 0);

    static string? GetMethod(ItemStack? stack, int depth)
    {
        if (stack?.Attributes is not ITreeAttribute attrs) return null;

        if (attrs[AttrKey] is ITreeAttribute tree)
        {
            return tree.GetString(MethodKey) ?? PreservationMethods.Formaldehyde;
        }

        if (depth < MaxContentDepth && attrs["contents"] is ITreeAttribute contents && contents.Count > 0)
        {
            string? method = null;
            foreach (var kv in contents)
            {
                if (kv.Value is not ItemstackAttribute isa || isa.value == null) continue;
                string? inner = GetMethod(isa.value, depth + 1);
                if (inner == null) return null;
                method ??= inner;
            }
            return method;
        }

        return null;
    }

    public static bool IsPreserved(ItemStack? stack) => GetMethod(stack, 0) != null;

    /// <summary>Marks a stack (and anything inside it) as preserved. Safe to call on any side.</summary>
    public static void Apply(ItemStack stack, string method) => Apply(stack, method, 0);

    static void Apply(ItemStack stack, string method, int depth)
    {
        stack.Attributes ??= new TreeAttribute();
        ITreeAttribute attrs = stack.Attributes;

        attrs.GetOrAddTreeAttribute(AttrKey).SetString(MethodKey, method);
        attrs.SetBool(TimeFrozenKey, true);
        attrs.RemoveAttribute("transitionstate");
        attrs.RemoveAttribute("temperature");

        if (depth < MaxContentDepth && attrs["contents"] is ITreeAttribute contents)
        {
            foreach (var kv in contents)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) Apply(isa.value, method, depth + 1);
            }
        }
    }

    /// <summary>Removes preservation from a stack (admin command). The food will start rotting again.</summary>
    public static void Remove(ItemStack stack) => Remove(stack, 0);

    static void Remove(ItemStack stack, int depth)
    {
        if (stack.Attributes == null) return;
        stack.Attributes.RemoveAttribute(AttrKey);
        stack.Attributes.RemoveAttribute(TimeFrozenKey);

        if (depth < MaxContentDepth && stack.Attributes["contents"] is ITreeAttribute contents)
        {
            foreach (var kv in contents)
            {
                if (kv.Value is ItemstackAttribute isa && isa.value != null) Remove(isa.value, depth + 1);
            }
        }
    }

    /// <summary>True if this kind of item/block can be preserved at all (cached per collectible).</summary>
    public static bool IsPreservableCollectible(CollectibleObject? coll)
    {
        if (coll?.Code == null) return false;
        if (preservableCache.TryGetValue(coll, out object? cached)) return (bool)cached;

        bool result = ComputePreservable(coll);
        preservableCache.AddOrUpdate(coll, result ? True : False);
        return result;
    }

    static bool ComputePreservable(CollectibleObject coll)
    {
        if (coll.IsMissing) return false;
        if (MatchesAny(Config.NeverPreservable, coll.Code)) return false;
        if (MatchesAny(Config.AlwaysPreservable, coll.Code)) return true;
        return IsFoodByDefault(coll);
    }

    /// <summary>
    /// The built-in food detection, without the config's always/never lists. Used where the server and every
    /// player must get the same answer before the server's config has reached the player (the creative tabs).
    /// </summary>
    public static bool IsFoodByDefault(CollectibleObject? coll)
    {
        if (coll?.Code == null || coll.IsMissing) return false;

        // Modders can opt in or out with an attribute: "embalmedeatsPreservable": true/false
        var attrs = coll.Attributes;
        if (attrs?["embalmedeatsPreservable"].Exists == true) return attrs["embalmedeatsPreservable"].AsBool();

        if (coll.Code.Domain == "embalmedeats") return false;
        if (coll.MatterState == EnumMatterState.Liquid || coll.MatterState == EnumMatterState.Gas) return false;
        if (attrs?["waterTightContainerProps"].Exists == true) return false;

        // Meal bowls, pies, cooking pots with a meal, crocks
        if (coll is IBlockMealContainer) return true;

        if (coll.NutritionProps != null) return true;
        if (attrs?["nutritionPropsWhenInMeal"].Exists == true) return true;
        if (coll.TransitionableProps?.Any(p => p?.Type == EnumTransitionType.Perish) == true) return true;

        return false;
    }

    /// <summary>True if this particular stack can be preserved right now.</summary>
    public static bool CanPreserve(ItemStack? stack) =>
        stack?.Collectible != null && IsPreservableCollectible(stack.Collectible) && CanPreserveStack(stack);

    /// <summary>The per-stack part of CanPreserve, with the built-in food detection only (see IsFoodByDefault).</summary>
    public static bool CanPreserveByDefault(ItemStack? stack) =>
        stack?.Collectible != null && IsFoodByDefault(stack.Collectible) && CanPreserveStack(stack);

    static bool CanPreserveStack(ItemStack stack)
    {
        if (IsPreserved(stack)) return false;

        // Containers only make sense with something in them (an empty bowl is not food)
        if (stack.Collectible is IBlockMealContainer)
        {
            return stack.Attributes?["contents"] is ITreeAttribute contents && contents.Count > 0;
        }

        return true;
    }

    public static bool MatchesAny(string[]? patterns, AssetLocation code)
    {
        if (patterns == null) return false;
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (WildcardUtil.Match(new AssetLocation(pattern), code)) return true;
        }
        return false;
    }

    public static string MethodName(string method) => Lang.Get("embalmedeats:method-" + method);
}
