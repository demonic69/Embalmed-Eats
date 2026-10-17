using System;
using EmbalmedEats.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Keeps preserved food out of normal food processing, so it can't be laundered back into
/// edible food: crafting grid and barrel recipes, cooking pots, firepit cooking, querns.
/// Also finishes the wax/resin grid recipes by marking their output as preserved.
/// </summary>
public static class CraftingPatches
{
    public const string Label = "crafting";

    public static void Apply(Harmony harmony)
    {
        // Grid recipes, barrel recipes and anything else built on CraftingRecipeIngredient
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CraftingRecipeIngredient), "SatisfiesAsIngredient", new[] { typeof(ItemStack), typeof(bool) }),
            typeof(CraftingPatches), postfix: nameof(SatisfiesPostfix), label: Label);

        // Cooking pots
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(CookingRecipe), nameof(CookingRecipe.Matches), new[] { typeof(ItemStack[]) }),
            typeof(CraftingPatches), postfix: nameof(CookingMatchesPostfix), label: Label);
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(CookingRecipe), nameof(CookingRecipe.Matches), new[] { typeof(ItemStack[]), typeof(int).MakeByRefType() }),
            typeof(CraftingPatches), postfix: nameof(CookingMatchesPostfix), label: Label);

        // Firepit cooking of single items
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), "CanSmelt", new[] { typeof(IWorldAccessor), typeof(ISlotProvider), typeof(ItemStack), typeof(ItemStack) }),
            typeof(CraftingPatches), prefix: nameof(CanSmeltPrefix), label: Label);

        // Quern and pulverizer
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), "GetGrindingProperties", new[] { typeof(IWorldAccessor), typeof(ItemStack) }),
            typeof(CraftingPatches), postfix: nameof(GrindingPostfix), label: Label);
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), "GetCrushingProperties", new[] { typeof(IWorldAccessor), typeof(ItemStack) }),
            typeof(CraftingPatches), postfix: nameof(CrushingPostfix), label: Label);

        // Wax/resin recipes: turn the output into a preserved copy of the input food
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(RecipeBase), nameof(RecipeBase.GenerateOutputStack), new[] { typeof(ItemSlot[]), typeof(ItemSlot) }),
            typeof(CraftingPatches), postfix: nameof(GenerateOutputPostfix), label: Label);
    }

    static bool Blocking => Preservation.Config.BlockPreservedFoodInRecipes;

    public static void SatisfiesPostfix(object[] __args, ref bool __result)
    {
        if (!__result || !Blocking || __args.Length < 1) return;
        if (__args[0] is ItemStack stack && Preservation.IsPreserved(stack)) __result = false;
    }

    public static void CookingMatchesPostfix(ItemStack?[] inputStacks, ref bool __result)
    {
        if (!__result || !Blocking || inputStacks == null) return;
        foreach (var stack in inputStacks)
        {
            if (Preservation.IsPreserved(stack)) { __result = false; return; }
        }
    }

    public static bool CanSmeltPrefix(object[] __args, ref bool __result)
    {
        if (Blocking && __args.Length > 2 && __args[2] is ItemStack input && Preservation.IsPreserved(input))
        {
            __result = false;
            return false;
        }
        return true;
    }

    public static void GrindingPostfix(object[] __args, ref GrindingProperties? __result)
    {
        if (__result != null && Blocking && __args.Length > 1 && __args[1] is ItemStack stack && Preservation.IsPreserved(stack)) __result = null;
    }

    public static void CrushingPostfix(object[] __args, ref CrushingProperties? __result)
    {
        if (__result != null && Blocking && __args.Length > 1 && __args[1] is ItemStack stack && Preservation.IsPreserved(stack)) __result = null;
    }

    public static void GenerateOutputPostfix(RecipeBase __instance, ItemSlot outputSlot)
    {
        if (__instance.Name?.Domain != "embalmedeats") return;
        string? method = __instance.Attributes?["embalmedeatsMethod"].AsString();
        if (!PreservationMethods.IsKnown(method) || outputSlot?.Itemstack == null) return;

        Preservation.Apply(outputSlot.Itemstack, method!);
    }
}
