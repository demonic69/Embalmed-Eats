using System.Reflection;
using EmbalmedEats.Recipes;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Barrel support for the formaldehyde soak and the salt &amp; lime cure. When no vanilla barrel
/// recipe matches, we check for "any food + embalming agent" and give the barrel a recipe made
/// for exactly that food. Works for any barrel class that derives from the vanilla barrel.
/// </summary>
public static class BarrelPatches
{
    public const string Label = "barrel";

    static FieldInfo? invDialogField;

    public static void Apply(Harmony harmony)
    {
        invDialogField = AccessTools.Field(typeof(BlockEntityBarrel), "invDialog");

        PatchUtil.Patch(harmony, AccessTools.Method(typeof(BlockEntityBarrel), "FindMatchingRecipe", new[] { typeof(IPlayer) }),
            typeof(BarrelPatches), postfix: nameof(FindMatchingRecipePostfix), label: Label);

        PatchUtil.Patch(harmony, AccessTools.Method(typeof(BarrelRecipe), nameof(BarrelRecipe.TryCraftNow), new[] { typeof(ICoreAPI), typeof(double), typeof(ItemSlot[]) }),
            typeof(BarrelPatches), prefix: nameof(TryCraftNowPrefix), label: Label);
    }

    public static void FindMatchingRecipePostfix(BlockEntityBarrel __instance)
    {
        if (__instance.CurrentRecipe != null) return; // a vanilla/modded recipe matched
        var inv = __instance.Inventory;
        if (inv == null || inv.Count < 2) return;

        EmbalmingBarrelRecipe? recipe = EmbalmingBarrelRecipe.TryCreate(new[] { inv[0], inv[1] });
        if (recipe == null) return;

        __instance.CurrentRecipe = recipe;
        __instance.CurrentOutSize = recipe.FoodCount;

        if (__instance.Api?.Side == EnumAppSide.Client)
        {
            (invDialogField?.GetValue(__instance) as GuiDialogBarrel)?.UpdateContents();
        }
    }

    public static bool TryCraftNowPrefix(BarrelRecipe __instance, double nowSealedHours, ItemSlot[] inputSlots, ref bool __result)
    {
        if (__instance is not EmbalmingBarrelRecipe recipe) return true;
        __result = recipe.CraftNow(nowSealedHours, inputSlots);
        return false;
    }
}
