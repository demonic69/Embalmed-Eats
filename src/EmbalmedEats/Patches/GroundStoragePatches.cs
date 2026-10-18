using System;
using EmbalmedEats.Core;
using EmbalmedEats.Decoration;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Keeps fresh food that only got ground storage from this mod off the ground, so vanilla play is unchanged:
/// only its preserved version can be placed, added to a pile, or shows the "place" hint.
/// </summary>
public static class GroundStoragePatches
{
    public const string Label = "groundstorage";

    public static void Apply(Harmony harmony)
    {
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(CollectibleBehaviorGroundStorable), nameof(CollectibleBehaviorGroundStorable.Interact)),
            typeof(GroundStoragePatches), prefix: nameof(InteractPrefix), label: Label);

        PatchUtil.PatchAll(harmony,
            PatchUtil.FindBaseAndOverrides(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.OnPlayerInteractStart), new[] { typeof(IPlayer), typeof(BlockSelection) }),
            typeof(GroundStoragePatches), prefix: nameof(PileInteractPrefix), label: Label);

        PatchUtil.Patch(harmony, AccessTools.Method(typeof(CollectibleBehaviorGroundStorable), nameof(CollectibleBehaviorGroundStorable.GetHeldInteractionHelp)),
            typeof(GroundStoragePatches), postfix: nameof(HelpPostfix), label: Label);
    }

    /// <summary>Shift + right-click with a held item: placing it on the ground, or on an existing pile.</summary>
    public static bool InteractPrefix(ItemSlot itemslot) => !GroundStorageSupport.Blocked(itemslot?.Itemstack);

    /// <summary>Right-clicking a pile: stops a fresh item being added to a pile of preserved food.</summary>
    public static bool PileInteractPrefix(IPlayer player, ref bool __result)
    {
        if (GroundStorageSupport.Blocked(player?.InventoryManager?.ActiveHotbarSlot?.Itemstack))
        {
            __result = false;
            return false;
        }
        return true;
    }

    public static void HelpPostfix(CollectibleBehaviorGroundStorable __instance, ItemSlot inSlot, ref WorldInteraction[] __result)
    {
        if (GroundStorageSupport.IsAdded(__instance) && !Preservation.IsPreserved(inSlot?.Itemstack))
        {
            __result = Array.Empty<WorldInteraction>();
        }
    }
}
