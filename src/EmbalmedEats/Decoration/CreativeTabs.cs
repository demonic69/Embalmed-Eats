using System;
using System.Collections.Generic;
using System.Linq;
using EmbalmedEats.Core;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace EmbalmedEats.Decoration;

/// <summary>
/// One creative inventory tab per preservation method ("Embalmed Eats: Wax-sealed" and so on), each with every
/// food preserved that way. Works for modded food too: the stacks come from each food's own creative inventory entries.
///
/// A collectible's creative entries are a list of (tabs, stacks). When a food only had tabs, its normal entry is
/// kept as the first list item so it still shows where it did before. Preserved stacks are kept out of the
/// handbook's page list by HandbookPatches.
///
/// The server and every player must end up with exactly the same lists, in the same order: the server hands
/// out creative items by their position in its own copy of the list, so any difference gives players the
/// wrong item. So the lists are built on both sides by the same code, from data both sides share, without the
/// config (which may not have reached the player yet). Vanilla code that calls GetHandBookStacks on the
/// server without a client API is handled by HandbookPatches.
/// </summary>
public static class CreativeTabs
{
    public const string Prefix = "embalmedeats-";

    public static string TabFor(string method) => Prefix + method;

    public static bool IsOurTab(string? tab) => tab?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>Adds the preserved stacks to every food's creative entries. Safe to run more than once. Returns how many foods got them.</summary>
    public static int AddToCreativeInventory(ICoreAPI api)
    {
        int foods = 0;
        foreach (CollectibleObject coll in api.World.Collectibles)
        {
            try
            {
                if (AddFor(coll)) foods++;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[Embalmed Eats] Couldn't add {0} to the creative tabs: {1}", coll?.Code, e.Message);
            }
        }
        return foods;
    }

    static bool AddFor(CollectibleObject? coll)
    {
        CreativeTabAndStackList[]? lists = Build(coll);
        if (lists == null) return false;
        coll!.CreativeInventoryStacks = lists;
        return true;
    }

    /// <summary>
    /// The collectible's creative entries with the preserved ones added, or null if it gets none or already has them.
    /// With <paramref name="rebuild"/>, entries already added are ignored and built again (for the self test).
    /// </summary>
    public static CreativeTabAndStackList[]? Build(CollectibleObject? coll, bool rebuild = false)
    {
        if (coll?.Code == null || coll.IsMissing) return null;
        if (!Preservation.IsFoodByDefault(coll)) return null;

        CreativeTabAndStackList[]? existing = coll.CreativeInventoryStacks;
        bool hasOurs = existing != null && existing.Any(list => list?.Tabs?.Any(IsOurTab) == true);
        if (hasOurs && !rebuild) return null; // already done (on this side, or received with the item)
        // Rebuilding: start from the entries that aren't ours (for a food that only had tabs, that's the normal entry this class made)
        if (hasOurs) existing = existing!.Where(list => list?.Tabs?.Any(IsOurTab) != true).ToArray();

        var lists = new List<CreativeTabAndStackList>();
        var sources = new List<ItemStack>();

        if (existing != null && existing.Length > 0)
        {
            lists.AddRange(existing);
            foreach (var list in existing)
            {
                if (list?.Stacks == null) continue;
                foreach (var jstack in list.Stacks)
                {
                    if (jstack?.ResolvedItemstack is ItemStack stack) sources.Add(stack);
                }
            }
        }
        else if (coll.CreativeInventoryTabs != null && coll.CreativeInventoryTabs.Length > 0)
        {
            var normal = new ItemStack(coll);
            lists.Add(new CreativeTabAndStackList { Tabs = coll.CreativeInventoryTabs, Stacks = new[] { ToJson(normal) } });
            sources.Add(normal);
        }
        else
        {
            return null; // not in the creative inventory at all
        }

        // Each different stack once, and only what can be preserved (e.g. a meal bowl needs a meal in it)
        var unique = new List<ItemStack>();
        foreach (var stack in sources)
        {
            if (!Preservation.CanPreserveByDefault(stack)) continue;
            string key = AttributesKey(stack);
            if (unique.Any(u => u.Collectible == stack.Collectible && AttributesKey(u) == key)) continue;
            unique.Add(stack);
        }
        if (unique.Count == 0) return null;

        foreach (string method in PreservationMethods.All)
        {
            var preserved = unique.Select(stack =>
            {
                ItemStack copy = stack.Clone();
                copy.StackSize = 1;
                Preservation.Apply(copy, method);
                return ToJson(copy);
            }).ToArray();

            lists.Add(new CreativeTabAndStackList { Tabs = new[] { TabFor(method) }, Stacks = preserved });
        }

        return lists.ToArray();
    }

    static string AttributesKey(ItemStack stack) => stack.Attributes?.ToJsonToken() ?? "";

    /// <summary>A text form of a list of creative entries, to check two sides built the same thing.</summary>
    public static string Signature(CreativeTabAndStackList[]? lists) => lists == null ? "" : string.Join(" | ", lists.Select(list =>
        string.Join(",", list?.Tabs ?? Array.Empty<string>()) + ": " +
        string.Join(";", (list?.Stacks ?? Array.Empty<JsonItemStack>()).Select(j => j?.ResolvedItemstack is ItemStack s ? s.Collectible.Code + AttributesKey(s) : "?"))));

    static JsonItemStack ToJson(ItemStack stack) => new()
    {
        Type = stack.Class,
        Code = stack.Collectible.Code.Clone(),
        StackSize = 1,
        Attributes = stack.Attributes != null && stack.Attributes.Count > 0 ? new JsonObject(JToken.Parse(stack.Attributes.ToJsonToken())) : null,
        ResolvedItemstack = stack
    };

    /// <summary>The preserved stacks in a list of creative entries, for the self test.</summary>
    public static IEnumerable<(string tab, ItemStack stack)> PreservedEntries(CreativeTabAndStackList[]? lists) =>
        (lists ?? Array.Empty<CreativeTabAndStackList>())
            .Where(list => list?.Tabs?.Any(IsOurTab) == true && list.Stacks != null)
            .SelectMany(list => list.Stacks.Where(j => j?.ResolvedItemstack != null).Select(j => (list.Tabs.First(IsOurTab), j.ResolvedItemstack!)));
}
