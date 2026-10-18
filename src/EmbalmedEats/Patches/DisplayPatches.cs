using System.Reflection;
using EmbalmedEats.Core;
using EmbalmedEats.Decoration;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace EmbalmedEats.Patches;

/// <summary>
/// Lets preserved food go on shelves and in display cases even when that food normally can't,
/// and shrinks it to a sensible size there (and on the ground) if the food has no display transform of its own.
/// </summary>
public static class DisplayPatches
{
    public const string Label = "display";

    static MethodInfo? displayCaseTryPut;

    public static void Apply(Harmony harmony)
    {
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(BlockEntityShelf), nameof(BlockEntityShelf.GetShelvableLayout), new[] { typeof(ItemStack) }),
            typeof(DisplayPatches), postfix: nameof(ShelvableLayoutPostfix), label: Label);

        displayCaseTryPut = AccessTools.Method(typeof(BlockEntityDisplayCase), "TryPut", new[] { typeof(ItemSlot), typeof(BlockSelection), typeof(IPlayer) });
        if (displayCaseTryPut != null)
        {
            PatchUtil.Patch(harmony, AccessTools.Method(typeof(BlockEntityDisplayCase), "OnInteract", new[] { typeof(IPlayer), typeof(BlockSelection) }),
                typeof(DisplayPatches), prefix: nameof(DisplayCaseInteractPrefix), label: Label);
        }
        else
        {
            PatchUtil.Failures.Add("display: BlockEntityDisplayCase.TryPut not found, display case support disabled");
        }

        PatchUtil.Patch(harmony, AccessTools.Method(typeof(BlockEntityDisplay), "applyDefaultTranforms", new[] { typeof(ItemStack), typeof(MeshData) }),
            typeof(DisplayPatches), postfix: nameof(ApplyDefaultTransformsPostfix), label: Label);
    }

    public static void ShelvableLayoutPostfix(ItemStack? stack, ref EnumShelvableLayout? __result)
    {
        if (__result != null || stack == null || !Preservation.Config.AllowOnShelves) return;
        if (!Preservation.IsPreserved(stack)) return;
        __result = stack.Class == EnumItemClass.Block ? EnumShelvableLayout.Halves : EnumShelvableLayout.Quadrants;
    }

    public static bool DisplayCaseInteractPrefix(BlockEntityDisplayCase __instance, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (!Preservation.Config.AllowInDisplayCases || displayCaseTryPut == null) return true;

        ItemSlot? slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        if (slot == null || slot.Empty) return true;

        ItemStack stack = slot.Itemstack;
        if (stack.Collectible.Attributes?["displaycaseable"].AsBool(false) == true) return true; // vanilla already allows it
        if (!Preservation.IsPreserved(stack)) return true;

        var sound = stack.Block?.Sounds?.Place;
        bool put = (bool)(displayCaseTryPut.Invoke(__instance, new object[] { slot, blockSel, byPlayer! }) ?? false);
        if (put)
        {
            __instance.Api.World.PlaySoundAt(sound ?? GlobalConstants.DefaultBuildSound, byPlayer!.Entity, byPlayer);
        }

        __result = put;
        return false;
    }

    public static void ApplyDefaultTransformsPostfix(BlockEntityDisplay __instance, ItemStack stack, MeshData mesh)
    {
        if (stack == null || mesh == null || !Preservation.IsPreserved(stack)) return;

        float scale = Preservation.Config.FallbackDisplayScale;
        if (scale <= 0 || scale == 1) return;

        CollectibleObject coll = stack.Collectible;
        var attrs = coll.Attributes;

        if (__instance is BlockEntityGroundStorage)
        {
            // Food vanilla already lets on the ground keeps its vanilla look there
            if (!GroundStorageSupport.IsAddedFor(coll)) return;
            if (attrs?["groundStorageTransform"].Exists == true) return;
        }
        else
        {
            // Food that has its own display setup already looks right
            if (attrs?[__instance.AttributeTransformCode].Exists == true) return;
            if (attrs?["onDisplayTransform"].Exists == true) return;
            if (coll.GetCollectibleInterface<IShelvable>() != null) return;

            // Food the vanilla game already allows there keeps its vanilla look
            if (__instance is BlockEntityShelf && attrs?["shelvable"].Exists == true) return;
            if (__instance is BlockEntityDisplayCase && attrs?["displaycaseable"].AsBool(false) == true) return;
        }

        // Flat (voxelized) items were already shrunk by vanilla
        if (stack.Class == EnumItemClass.Item && (stack.Item.Shape == null || stack.Item.Shape.VoxelizeTexture)) return;

        mesh.Scale(new Vec3f(0.5f, 0, 0.5f), scale, scale, scale);
    }
}
