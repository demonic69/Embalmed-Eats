using System;
using EmbalmedEats.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Cosmetic: wax and resin are an ingredient in one generated recipe per food, which would put
/// hundreds of food icons on the beeswax/resin handbook pages. This keeps a small sample there.
/// Food pages still show their own preserved versions. Purely visual; skipped if the handbook changes.
/// </summary>
public static class HandbookPatches
{
    public const string Label = "handbook";
    const int MaxPreservedSamples = 6;

    [ThreadStatic] static ItemStack? currentPage;
    [ThreadStatic] static int shown;

    public static void Apply(Harmony harmony)
    {
        var t = typeof(CollectibleBehaviorHandbookTextAndExtraInfo);
        var outer = AccessTools.Method(t, "addIngredientForInfo");
        var inner = AccessTools.Method(t, "addToListUniquely");
        if (outer == null || inner == null)
        {
            PatchUtil.Failures.Add("handbook: methods not found (cosmetic only)");
            return;
        }

        PatchUtil.Patch(harmony, outer, typeof(HandbookPatches), prefix: nameof(OuterPrefix), finalizer: nameof(OuterFinalizer), label: Label);
        PatchUtil.Patch(harmony, inner, typeof(HandbookPatches), prefix: nameof(InnerPrefix), label: Label);
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
