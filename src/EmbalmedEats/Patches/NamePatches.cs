using System.Text;
using EmbalmedEats.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace EmbalmedEats.Patches;

/// <summary>
/// Adds a prefix to the name ("Wax-sealed Carrot") and a warning to the tooltip of preserved food.
/// ItemStack.GetName/GetDescription are the single entry points every collectible goes through,
/// so this works for all vanilla and modded food without per-class patches.
/// </summary>
public static class NamePatches
{
    public const string Label = "names";

    public static void Apply(Harmony harmony)
    {
        PatchUtil.Patch(harmony, AccessTools.Method(typeof(ItemStack), nameof(ItemStack.GetName), System.Type.EmptyTypes),
            typeof(NamePatches), postfix: nameof(GetNamePostfix), label: Label);

        PatchUtil.Patch(harmony, AccessTools.Method(typeof(ItemStack), nameof(ItemStack.GetDescription), new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(bool) }),
            typeof(NamePatches), postfix: nameof(GetDescriptionPostfix), label: Label);
    }

    public static void GetNamePostfix(ItemStack __instance, ref string __result)
    {
        string? method = Preservation.GetMethod(__instance);
        if (method == null) return;
        __result = Lang.Get("embalmedeats:preservedname-" + method, __result);
    }

    public static void GetDescriptionPostfix(ItemStack __instance, ref string __result)
    {
        string? method = Preservation.GetMethod(__instance);
        if (method == null) return;

        var sb = new StringBuilder();
        sb.AppendLine(Lang.Get("embalmedeats:desc-preserved", Preservation.MethodName(method)));
        sb.AppendLine(Lang.Get("embalmedeats:desc-norot"));
        sb.AppendLine(Lang.Get("embalmedeats:desc-toxic"));
        sb.AppendLine();
        __result = sb.ToString() + __result;
    }
}
