using System;
using EmbalmedEats.Core;
using EmbalmedEats.Toxicity;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Detects when a preserved item was actually eaten (server side) and triggers the toxicity effect.
/// Covers the normal eating path (tryEatStop, including every override in vanilla and mods)
/// and the meal path (bowls, pies, pots) which uses tryFinishEatMeal.
/// A depth counter makes sure an override that calls base only triggers once.
/// </summary>
public static class EatingPatches
{
    public const string Label = "eating";

    [ThreadStatic] static int depth;

    public sealed class EatState
    {
        public ItemSlot Slot = null!;
        public ItemStack Stack = null!;
        public ItemStack Snapshot = null!;
        public int StackSize;
        public float Servings;
        public string Method = "";
        public EntityAgent Eater = null!;
    }

    public static void Apply(Harmony harmony)
    {
        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(CollectibleObject), "tryEatStop", new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent) }),
            typeof(EatingPatches), prefix: nameof(EatPrefix), postfix: nameof(EatPostfix), finalizer: nameof(EatFinalizer), label: Label);

        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(BlockMeal), "tryFinishEatMeal", new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(bool) }),
            typeof(EatingPatches), prefix: nameof(EatPrefix), postfix: nameof(EatPostfix), finalizer: nameof(EatFinalizer), label: Label);
    }

    public static void EatPrefix(object[] __args, out EatState? __state)
    {
        __state = null;
        depth++;
        if (depth != 1 || __args.Length < 3) return;
        if (__args[1] is not ItemSlot slot || __args[2] is not EntityAgent eater) return;
        if (eater.World?.Side != EnumAppSide.Server) return;

        ItemStack? stack = slot.Itemstack;
        string? method = Preservation.GetMethod(stack);
        if (stack == null || method == null) return;

        __state = new EatState
        {
            Slot = slot,
            Stack = stack,
            Snapshot = stack.Clone(),
            StackSize = stack.StackSize,
            Servings = stack.Collectible is BlockMeal meal ? meal.GetQuantityServings(eater.World, stack) : 0,
            Method = method,
            Eater = eater
        };
    }

    public static void EatPostfix(EatState? __state)
    {
        if (__state == null) return;

        ItemStack? now = __state.Slot.Itemstack;
        bool consumed =
            now == null
            || !ReferenceEquals(now, __state.Stack)
            || now.StackSize < __state.StackSize
            || (now.Collectible is BlockMeal meal && meal.GetQuantityServings(__state.Eater.World, now) < __state.Servings - 0.001f);

        if (!consumed) return;

        try
        {
            ToxicitySystem.Instance?.OnPreservedFoodEaten(__state.Eater, __state.Snapshot, __state.Method);
        }
        catch (Exception e)
        {
            __state.Eater.World.Logger.Error("[Embalmed Eats] Toxicity effect failed: {0}", e);
        }
    }

    public static Exception? EatFinalizer(Exception? __exception)
    {
        depth--;
        if (depth < 0) depth = 0;
        return __exception;
    }
}
