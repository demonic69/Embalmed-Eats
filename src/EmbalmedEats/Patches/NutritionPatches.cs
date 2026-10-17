using System.Runtime.CompilerServices;
using EmbalmedEats.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Preserved food stays edible (so the eat animation still works) but gives no nutrition.
/// What happens instead is handled by <see cref="EatingPatches"/> and the toxicity system.
/// </summary>
public static class NutritionPatches
{
    public const string Label = "nutrition";

    static readonly ConditionalWeakTable<FoodNutritionProperties, FoodNutritionProperties> zeroedCache = new();
    static readonly ConditionalWeakTable<FoodNutritionProperties, object> zeroedSet = new();

    public static void Apply(Harmony harmony)
    {
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), "GetNutritionProperties", new[] { typeof(IWorldAccessor), typeof(ItemStack), typeof(Entity) }),
            typeof(NutritionPatches), postfix: nameof(NutritionPostfix), label: Label);

        // Meals (bowls, pies, pots) compute nutrition from their contents in a static helper
        PatchUtil.Patch(harmony,
            AccessTools.Method(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionProperties),
                new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) }),
            typeof(NutritionPatches), postfix: nameof(MealNutritionPostfix), label: Label);
    }

    public static FoodNutritionProperties Zeroed(FoodNutritionProperties props)
    {
        if (zeroedSet.TryGetValue(props, out _)) return props;

        return zeroedCache.GetValue(props, p =>
        {
            FoodNutritionProperties z = p.Clone();
            z.Satiety = 0;
            z.Health = 0;
            z.Intoxication = 0;
            z.Psychedelic = 0;
            zeroedSet.AddOrUpdate(z, true);
            return z;
        });
    }

    public static void NutritionPostfix(object[] __args, ref FoodNutritionProperties? __result)
    {
        if (__result == null || __args.Length < 2) return;
        if (__args[1] is ItemStack stack && Preservation.IsPreserved(stack))
        {
            __result = Zeroed(__result);
        }
    }

    public static void MealNutritionPostfix(ItemSlot inSlot, FoodNutritionProperties[] __result)
    {
        if (__result == null || !Preservation.IsPreserved(inSlot?.Itemstack)) return;
        foreach (var props in __result)
        {
            if (props == null) continue;
            // These are fresh clones made for this call, so zeroing them in place is safe
            props.Satiety = 0;
            props.Health = 0;
            props.Intoxication = 0;
            props.Psychedelic = 0;
        }
    }
}
