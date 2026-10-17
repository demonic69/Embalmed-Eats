using EmbalmedEats.Core;
using HarmonyLib;
using Vintagestory.API.Common;

namespace EmbalmedEats.Patches;

/// <summary>
/// Stops preserved stacks from rotting, drying, curing, ripening or changing temperature.
/// The vanilla "timeFrozen" flag already covers most items; these patches cover every class
/// (vanilla or modded) that overrides the transition code without honouring that flag.
/// Arguments are read positionally (__args) because overrides name their parameters differently.
/// </summary>
public static class TransitionPatches
{
    public const string Label = "transitions";

    public static void Apply(Harmony harmony)
    {
        var t = typeof(CollectibleObject);

        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(t, "UpdateAndGetTransitionStates", new[] { typeof(IWorldAccessor), typeof(ItemSlot) }),
            typeof(TransitionPatches), prefix: nameof(StatesPrefix), label: Label);

        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(t, "UpdateAndGetTransitionState", new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(EnumTransitionType) }),
            typeof(TransitionPatches), prefix: nameof(StatePrefix), label: Label);

        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(t, "RequiresTransitionableTicking", new[] { typeof(IWorldAccessor), typeof(ItemStack) }),
            typeof(TransitionPatches), prefix: nameof(TickingPrefix), label: Label);
    }

    public static bool StatesPrefix(object[] __args, ref TransitionState[]? __result)
    {
        if (__args.Length > 1 && __args[1] is ItemSlot slot && Preservation.IsPreserved(slot.Itemstack))
        {
            __result = null;
            return false;
        }
        return true;
    }

    public static bool StatePrefix(object[] __args, ref TransitionState? __result)
    {
        if (__args.Length > 1 && __args[1] is ItemSlot slot && Preservation.IsPreserved(slot.Itemstack))
        {
            __result = null;
            return false;
        }
        return true;
    }

    public static bool TickingPrefix(object[] __args, ref bool __result)
    {
        if (__args.Length > 1 && __args[1] is ItemStack stack && Preservation.IsPreserved(stack))
        {
            __result = false;
            return false;
        }
        return true;
    }
}
