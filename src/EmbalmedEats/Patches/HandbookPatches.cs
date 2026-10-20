using System;
using System.Collections.Generic;
using System.Linq;
using EmbalmedEats.Core;
using EmbalmedEats.Decoration;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Handbook changes.
/// <list type="bullet">
/// <item>Preserved items open the Embalmed Eats guide, which says how each kind is made: clicking one anywhere in
/// the handbook, or pressing the handbook key while hovering one. (Without this, a preserved item's page code
/// includes its attributes, matches no page, and clicking it does nothing.)</item>
/// <item>Preserved copies don't get handbook pages of their own, so the creative tabs don't add thousands of
/// near-duplicate entries to the handbook list.</item>
/// <item>Cosmetic: wax and resin are an ingredient in one generated recipe per food, which would put hundreds of
/// food icons on the beeswax/resin pages. This keeps a small sample there.</item>
/// </list>
/// </summary>
public static class HandbookPatches
{
    public const string Label = "handbook";
    /// <summary>The guide's page code, from assets/embalmedeats/config/handbook/embalmedeats-guide.json.</summary>
    public const string GuidePageCode = "embalmedeats-guide";
    const int MaxPreservedSamples = 6;

    [ThreadStatic] static ItemStack? currentPage;
    [ThreadStatic] static int shown;

    public static void Apply(Harmony harmony)
    {
        // Every way the handbook turns a stack into a page code
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(GuiHandbookItemStackPage), nameof(GuiHandbookItemStackPage.PageCodeForStack), new[] { typeof(ItemStack) }),
            typeof(HandbookPatches), prefix: nameof(PageCodePrefix), label: Label);
        var providerArgs = new[] { typeof(IWorldAccessor), typeof(ItemStack) };
        PatchUtil.PatchAll(harmony, PatchUtil.FindBaseAndOverrides(typeof(BlockMeal), "HandbookPageCodeForStack", providerArgs),
            typeof(HandbookPatches), postfix: nameof(ProviderPageCodePostfix), label: Label);
        PatchUtil.PatchAll(harmony, PatchUtil.FindBaseAndOverrides(typeof(BlockCookedContainerBase), "HandbookPageCodeForStack", providerArgs),
            typeof(HandbookPatches), postfix: nameof(ProviderPageCodePostfix), label: Label);

        // No pages for preserved copies; and a safe answer when vanilla calls this on the server (see the prefix)
        PatchUtil.PatchAll(harmony, PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), nameof(CollectibleObject.GetHandBookStacks), new[] { typeof(ICoreClientAPI) }),
            typeof(HandbookPatches), prefix: nameof(HandbookStacksPrefix), postfix: nameof(HandbookStacksPostfix), label: Label);

        var t = typeof(CollectibleBehaviorHandbookTextAndExtraInfo);
        var outer = AccessTools.Method(t, "addIngredientForInfo");
        var inner = AccessTools.Method(t, "addToListUniquely");
        if (outer == null || inner == null)
        {
            PatchUtil.Failures.Add("handbook: ingredient list methods not found (cosmetic only)");
            return;
        }

        PatchUtil.Patch(harmony, outer, typeof(HandbookPatches), prefix: nameof(OuterPrefix), finalizer: nameof(OuterFinalizer), label: Label);
        PatchUtil.Patch(harmony, inner, typeof(HandbookPatches), prefix: nameof(InnerPrefix), label: Label);
    }

    public static bool PageCodePrefix(ItemStack stack, ref string __result)
    {
        if (!Preservation.IsPreserved(stack)) return true;
        __result = GuidePageCode;
        return false;
    }

    public static void ProviderPageCodePostfix(object[] __args, ref string? __result)
    {
        if (__args.Length > 1 && __args[1] is ItemStack stack && Preservation.IsPreserved(stack)) __result = GuidePageCode;
    }

    /// <summary>
    /// Vanilla crock and cooking pot code calls GetHandBookStacks on the server, with no client API. The vanilla
    /// method only copes with that for collectibles without creative stack lists, and this mod gives foods one.
    /// For those, on the server, return what vanilla returned before: the food's normal creative stacks.
    /// </summary>
    public static bool HandbookStacksPrefix(CollectibleObject __instance, ICoreClientAPI? capi, ref List<ItemStack>? __result)
    {
        if (capi != null) return true;
        var lists = __instance.CreativeInventoryStacks;
        if (lists == null || !lists.Any(list => list?.Tabs?.Any(CreativeTabs.IsOurTab) == true)) return true;

        if (__instance.Attributes?["handbook"]?["exclude"].AsBool() == true)
        {
            __result = null;
            return false;
        }

        var stacks = new List<ItemStack>();
        foreach (var list in lists)
        {
            if (list?.Stacks == null || list.Tabs?.Any(CreativeTabs.IsOurTab) == true) continue;
            foreach (var jstack in list.Stacks)
            {
                if (jstack?.ResolvedItemstack is not ItemStack resolved) continue;
                ItemStack stack = resolved.Clone();
                stack.StackSize = stack.Collectible.MaxStackSize;
                string key = stack.Attributes?.ToJsonToken() ?? "";
                if (!stacks.Any(s => s.Collectible == stack.Collectible && (s.Attributes?.ToJsonToken() ?? "") == key)) stacks.Add(stack);
            }
        }
        __result = stacks;
        return false;
    }

    public static void HandbookStacksPostfix(List<ItemStack>? __result)
    {
        __result?.RemoveAll(Preservation.IsPreserved);
    }

    public static void OuterPrefix(object[] __args)
    {
        currentPage = __args.Length > 3 ? __args[3] as ItemStack : null;
        shown = 0;
    }

    public static Exception? OuterFinalizer(Exception? __exception)
    {
        currentPage = null;
        return __exception;
    }

    public static bool InnerPrefix(object[] __args)
    {
        if (currentPage == null || __args.Length < 3 || __args[2] is not ItemStack entry) return true;
        if (!Preservation.IsPreserved(entry)) return true;

        // On a food's own page, show its preserved versions normally
        if (Preservation.IsPreservableCollectible(currentPage.Collectible)) return true;

        if (shown >= MaxPreservedSamples) return false;
        shown++;
        return true;
    }
}
