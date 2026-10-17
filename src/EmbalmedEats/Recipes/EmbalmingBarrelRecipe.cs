using System;
using EmbalmedEats.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace EmbalmedEats.Recipes;

/// <summary>
/// A barrel "recipe" created on the fly for whatever food is in the barrel, so it works for every
/// vanilla and modded food without a JSON recipe per food. The output is the same food, marked as
/// preserved, so it keeps its model, texture and any extra attributes.
/// It is never registered in the recipe list; <see cref="Patches.BarrelPatches"/> hands it to the barrel.
/// </summary>
public class EmbalmingBarrelRecipe : BarrelRecipe
{
    public const string FormaldehydeCode = "embalmedeats:formaldehydeportion";
    public const string CuringSaltCode = "embalmedeats:curingsalt";

    public string Method = PreservationMethods.Formaldehyde;
    public int FoodCount;

    /// <summary>Which preservation method an agent stack (liquid slot contents) provides, if any.</summary>
    public static string? MethodForAgent(ItemStack? agent)
    {
        if (agent?.Collectible?.Code == null) return null;
        string code = agent.Collectible.Code.ToString();
        var cfg = Preservation.Config;

        if (code == FormaldehydeCode && cfg.EnableFormaldehydeBarrel) return PreservationMethods.Formaldehyde;
        if (code == CuringSaltCode && cfg.EnableSaltLimeCure) return PreservationMethods.SaltCure;
        return null;
    }

    /// <summary>Number of agent items (liquid portions or curing salt) needed for this many food items.</summary>
    public static int RequiredAgent(string method, ItemStack agent, int foodCount)
    {
        var cfg = Preservation.Config;
        if (method == PreservationMethods.Formaldehyde)
        {
            float itemsPerLitre = BlockLiquidContainerBase.GetContainableProps(agent)?.ItemsPerLitre ?? 100f;
            return Math.Max(1, (int)Math.Ceiling(foodCount * Math.Max(0, cfg.FormaldehydeLitresPerFood) * itemsPerLitre));
        }
        return Math.Max(1, foodCount * Math.Max(1, cfg.CuringSaltPerFood));
    }

    static bool FindSlots(ItemSlot[] slots, out ItemSlot food, out ItemSlot agent, out string method)
    {
        food = agent = null!;
        method = "";
        if (slots == null || slots.Length < 2) return false;

        for (int i = 0; i < 2; i++)
        {
            ItemSlot f = slots[i], a = slots[1 - i];
            if (f?.Itemstack == null || a?.Itemstack == null) continue;

            string? m = MethodForAgent(a.Itemstack);
            if (m == null || !Preservation.CanPreserve(f.Itemstack)) continue;

            food = f;
            agent = a;
            method = m;
            return true;
        }
        return false;
    }

    /// <summary>Returns a recipe for the current barrel contents, or null if they don't form one.</summary>
    public static EmbalmingBarrelRecipe? TryCreate(ItemSlot[] slots)
    {
        if (!FindSlots(slots, out ItemSlot food, out ItemSlot agent, out string method)) return null;

        ItemStack foodStack = food.Itemstack!, agentStack = agent.Itemstack!;
        int foodCount = foodStack.StackSize;
        if (agentStack.StackSize < RequiredAgent(method, agentStack, foodCount)) return null;

        ItemStack preview = foodStack.Clone();
        Preservation.Apply(preview, method);

        var cfg = Preservation.Config;
        return new EmbalmingBarrelRecipe
        {
            Method = method,
            FoodCount = foodCount,
            Code = "embalmedeats-" + method,
            Name = new AssetLocation("embalmedeats", "barrel/" + method),
            SealHours = Math.Max(0.1, method == PreservationMethods.Formaldehyde ? cfg.FormaldehydeSealHours : cfg.SaltCureSealHours),
            Ingredients = Array.Empty<BarrelRecipeIngredient>(),
            Output = new BarrelOutputStack
            {
                Type = preview.Class,
                Code = preview.Collectible.Code,
                StackSize = foodCount,
                ResolvedItemstack = preview
            }
        };
    }

    /// <summary>Called instead of BarrelRecipe.TryCraftNow. Returns true when the barrel should unseal.</summary>
    public bool CraftNow(double nowSealedHours, ItemSlot[] slots)
    {
        if (SealHours > 0 && nowSealedHours < SealHours) return false;

        // Contents changed or config changed since sealing: just unseal without doing anything
        if (!FindSlots(slots, out ItemSlot food, out ItemSlot agent, out string method) || method != Method) return true;

        ItemStack foodStack = food.Itemstack!, agentStack = agent.Itemstack!;
        int required = RequiredAgent(method, agentStack, foodStack.StackSize);
        if (agentStack.StackSize < required) return true;

        agentStack.StackSize -= required;
        if (agentStack.StackSize <= 0) agent.Itemstack = null;

        Preservation.Apply(foodStack, method);

        food.MarkDirty();
        agent.MarkDirty();
        return true;
    }
}
