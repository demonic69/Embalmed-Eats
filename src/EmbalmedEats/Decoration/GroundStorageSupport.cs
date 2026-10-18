using System.Runtime.CompilerServices;
using EmbalmedEats.Core;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace EmbalmedEats.Decoration;

/// <summary>
/// Lets preserved food be placed on the ground (shift + right-click), like vanilla does for bread or meat.
///
/// Food that vanilla already allows on the ground keeps its own setup. Every other preservable item gets
/// the vanilla "GroundStorable" behavior, marked with "embalmedeatsAdded" in its properties. For those
/// items only preserved stacks may go on the ground; fresh ones behave as before (see GroundStoragePatches).
///
/// Added on the server in AssetsFinalize, the same way vanilla adds behaviors, so it reaches clients with the
/// item definitions. The client repeats it only for items that didn't get it that way.
/// </summary>
public static class GroundStorageSupport
{
    public const string Marker = "embalmedeatsAdded";

    /// <summary>Four per block, low collision box: the same setup vanilla uses for bread, meat and vegetables.</summary>
    const string PropertiesJson =
        "{ \"layout\": \"Quadrants\", \"collisionBox\": { \"x1\": 0, \"y1\": 0, \"z1\": 0, \"x2\": 1, \"y2\": 0.125, \"z2\": 1 }, \"" + Marker + "\": true }";

    static ConditionalWeakTable<CollectibleBehavior, object> addedCache = new();
    static readonly object Yes = true, No = false;

    /// <summary>Adds ground storage to every preservable item that doesn't have it. Returns how many got it.</summary>
    public static int AddToPreservableItems(ICoreAPI api)
    {
        if (!Preservation.Config.AllowOnGround) return 0;

        int added = 0;
        foreach (Item item in api.World.Items)
        {
            if (item?.Code == null || item.IsMissing) continue;
            if (!Preservation.IsPreservableCollectible(item)) continue;
            if (item.HasBehavior<CollectibleBehaviorGroundStorable>()) continue;

            var behavior = new CollectibleBehaviorGroundStorable(item);
            behavior.Initialize(new JsonObject(JToken.Parse(PropertiesJson)));
            behavior.OnLoaded(api);
            item.CollectibleBehaviors = item.CollectibleBehaviors.Append(behavior);
            added++;
        }
        return added;
    }

    /// <summary>True if this ground storage behavior was added by this mod (rather than by the item itself).</summary>
    public static bool IsAdded(CollectibleBehavior? behavior)
    {
        if (behavior == null) return false;
        if (addedCache.TryGetValue(behavior, out object? cached)) return (bool)cached;
        bool added = behavior.propertiesAtString?.Contains(Marker) == true;
        addedCache.AddOrUpdate(behavior, added ? Yes : No);
        return added;
    }

    public static bool IsAddedFor(CollectibleObject? collectible) =>
        IsAdded(collectible?.GetBehavior<CollectibleBehaviorGroundStorable>());

    /// <summary>
    /// True for a fresh stack of food that only has ground storage because of this mod:
    /// it must not go on the ground (only its preserved version may).
    /// </summary>
    public static bool Blocked(ItemStack? stack) =>
        stack?.Collectible != null && IsAddedFor(stack.Collectible) && !Preservation.IsPreserved(stack);

    /// <summary>True if the stack can be put on the ground, by vanilla or by this mod.</summary>
    public static bool CanGoOnGround(ItemStack? stack) =>
        stack?.Collectible?.HasBehavior<CollectibleBehaviorGroundStorable>() == true && !Blocked(stack);
}
