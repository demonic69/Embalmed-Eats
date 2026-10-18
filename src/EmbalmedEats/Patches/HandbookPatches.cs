using System;
using System.Collections.Generic;
using EmbalmedEats.Core;
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

        // No pages for preserved copies
        PatchUtil.PatchAll(harmony, PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), nameof(CollectibleObject.GetHandBookStacks), new[] { typeof(ICoreClientAPI) }),
            typeof(HandbookPatches), postfix: nameof(HandbookStacksPostfix), label: Label);

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
