using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmbalmedEats.Core;
using EmbalmedEats.Patches;
using EmbalmedEats.Recipes;
using EmbalmedEats.Toxicity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EmbalmedEats;

/// <summary>
/// Built-in checks run with "/embalmedeats selftest". They exercise the real game objects
/// (items, recipes, barrels) on a live server, so they catch problems after game updates.
/// The CI pipeline runs this on a dedicated server for every change.
/// </summary>
public static class SelfTest
{
    public static string Run(ICoreServerAPI sapi)
    {
        var world = sapi.World;
        var cfg = Preservation.Config;
        var results = new List<(bool ok, string name, string detail)>();

        void Check(string name, Func<string?> test)
        {
            try
            {
                string? err = test();
                results.Add((err == null, name, err ?? ""));
            }
            catch (Exception e)
            {
                results.Add((false, name, e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()));
            }
        }

        Item? meat = world.GetItem(new AssetLocation("game:redmeat-raw"));
        Item? beeswax = world.GetItem(new AssetLocation("game:beeswax"));
        Item? fluid = world.GetItem(new AssetLocation(EmbalmingBarrelRecipe.FormaldehydeCode));
        Item? curingSalt = world.GetItem(new AssetLocation(EmbalmingBarrelRecipe.CuringSaltCode));

        Check("core patches applied", () =>
        {
            foreach (string label in new[] { TransitionPatches.Label, NutritionPatches.Label, EatingPatches.Label, CraftingPatches.Label, NamePatches.Label, BarrelPatches.Label, DisplayPatches.Label })
            {
                if (PatchUtil.CountPatched(label) == 0) return $"no {label} patches applied";
            }
            return null;
        });

        Check("mod items loaded", () =>
        {
            if (meat == null) return "game:redmeat-raw missing";
            if (beeswax == null) return "game:beeswax missing";
            if (fluid == null) return "formaldehyde solution item missing";
            if (curingSalt == null) return "curing salt item missing";
            return null;
        });
        if (meat == null || beeswax == null || fluid == null || curingSalt == null) return Report(sapi, results);

        Check("food detection", () =>
        {
            if (!Preservation.IsPreservableCollectible(meat)) return "raw meat not detected as food";
            if (Preservation.IsPreservableCollectible(beeswax)) return "beeswax wrongly detected as food";
            var rot = world.GetItem(new AssetLocation("game:rot"));
            if (rot != null && Preservation.IsPreservableCollectible(rot)) return "rot wrongly detected as food";
            var water = world.GetItem(new AssetLocation("game:waterportion"));
            if (water != null && Preservation.IsPreservableCollectible(water)) return "water wrongly detected as food";
            int foods = world.Collectibles.Count(c => c?.Code != null && Preservation.IsPreservableCollectible(c));
            if (foods < 20) return $"only {foods} foods detected";
            return null;
        });

        Check("preserving stops rotting", () =>
        {
            var stack = new ItemStack(meat, 3);
            var slot = new DummySlot(stack);
            var before = meat.UpdateAndGetTransitionStates(world, slot);
            if (before == null || before.Length == 0) return "raw meat had no transition states before preserving";

            Preservation.Apply(stack, PreservationMethods.Wax);
            if (meat.UpdateAndGetTransitionStates(world, slot) != null) return "still has transition states";
            if (meat.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish) != null) return "still has a perish state";
            if (meat.RequiresTransitionableTicking(world, stack)) return "still requires transition ticking";
            if (stack.Attributes.HasAttribute("transitionstate")) return "old freshness data not cleared";
            if (stack.Collectible != meat) return "item changed (model/texture would change)";
            return null;
        });

        Check("preserved food gives no nutrition but stays edible", () =>
        {
            var fresh = new ItemStack(meat, 1);
            var preserved = new ItemStack(meat, 1);
            Preservation.Apply(preserved, PreservationMethods.Resin);
            var freshProps = meat.GetNutritionProperties(world, fresh, null);
            var presProps = meat.GetNutritionProperties(world, preserved, null);
            if (freshProps == null || freshProps.Satiety <= 0) return "fresh meat has no nutrition (test assumption broken)";
            if (presProps == null) return "preserved food became inedible";
            if (presProps.Satiety != 0 || presProps.Health != 0) return $"preserved food still gives {presProps.Satiety} satiety";
            if (meat.GetNutritionProperties(world, fresh, null)?.Satiety <= 0) return "fresh food nutrition was modified";
            return null;
        });

        Check("names, tooltips and lang", () =>
        {
            var fresh = new ItemStack(meat, 1);
            foreach (string method in PreservationMethods.All)
            {
                var s = new ItemStack(meat, 1);
                Preservation.Apply(s, method);
                string name = s.GetName();
                if (name == fresh.GetName()) return $"{method}: name not changed";
                if (name.Contains("embalmedeats:")) return $"{method}: missing lang entry ({name})";
                if (Preservation.MethodName(method).Contains("embalmedeats:")) return $"{method}: missing method lang entry";
                string desc = s.GetDescription(world, new DummySlot(s));
                if (desc.Contains("embalmedeats:")) return $"{method}: missing description lang entry";
            }
            foreach (var key in new[] { "item-formaldehydeportion", "item-curingsalt", "toxicity-mild" }.Concat(cfg.ToxicityStages.Select(st => "toxicity-" + st.Name)))
            {
                if (!Lang.HasTranslation("embalmedeats:" + key)) return "missing lang key " + key;
            }
            return null;
        });

        Check("preserved and fresh food don't stack together", () =>
        {
            var fresh = new ItemStack(meat, 1);
            var preserved = new ItemStack(meat, 1);
            Preservation.Apply(preserved, PreservationMethods.Wax);
            if (fresh.Equals(world, preserved, GlobalConstants.IgnoredStackAttributes)) return "they would merge";
            var preserved2 = new ItemStack(meat, 1);
            Preservation.Apply(preserved2, PreservationMethods.Wax);
            if (!preserved.Equals(world, preserved2, GlobalConstants.IgnoredStackAttributes)) return "two identical preserved stacks would not merge";
            return null;
        });

        GridRecipe? waxRecipe = GridRecipeGenerator.Recipes.FirstOrDefault(r =>
            r.Attributes?["embalmedeatsMethod"].AsString() == PreservationMethods.Wax && r.Output?.Code?.Equals(meat.Code) == true);

        Check("wax and resin recipes generated", () =>
        {
            if (cfg.EnableWaxDipping && GridRecipeGenerator.Recipes.All(r => r.Attributes?["embalmedeatsMethod"].AsString() != PreservationMethods.Wax)) return "no wax recipes";
            if (cfg.EnableResinLacquer && GridRecipeGenerator.Recipes.All(r => r.Attributes?["embalmedeatsMethod"].AsString() != PreservationMethods.Resin)) return "no resin recipes";
            if (!world.GridRecipes.Contains(waxRecipe!)) return "wax recipe for raw meat not registered";
            return null;
        });

        Check("wax recipe makes a preserved copy of the same food", () =>
        {
            if (waxRecipe?.ResolvedIngredients == null) return "no resolved wax recipe for raw meat";
            var food = new ItemStack(meat, 1);
            food.Attributes.SetString("embalmedeatsSelfTest", "kept");
            var wax = new ItemStack(beeswax, 1);

            var foodIngredient = waxRecipe.ResolvedIngredients.FirstOrDefault(i => i?.Code?.Equals(meat.Code) == true);
            var waxIngredient = waxRecipe.ResolvedIngredients.FirstOrDefault(i => i?.Code?.Equals(beeswax.Code) == true);
            if (foodIngredient == null || waxIngredient == null) return "ingredients missing";
            if (!foodIngredient.SatisfiesAsIngredient(food) || !waxIngredient.SatisfiesAsIngredient(wax)) return "inputs don't satisfy the recipe";

            var slots = new ItemSlot[] { new DummySlot(food), new DummySlot(wax) };
            var outSlot = new DummySlot();
            waxRecipe.GenerateOutputStack(slots, outSlot);

            var output = outSlot.Itemstack;
            if (output?.Collectible != meat) return "output is a different item";
            if (Preservation.GetMethod(output) != PreservationMethods.Wax) return "output not marked as wax-preserved";
            if (output.Attributes.GetString("embalmedeatsSelfTest") != "kept") return "input attributes were not kept (modded data would be lost)";
            return null;
        });

        Check("preserved food refused by recipes", () =>
        {
            var foodIngredient = waxRecipe?.ResolvedIngredients?.FirstOrDefault(i => i?.Code?.Equals(meat.Code) == true);
            if (foodIngredient == null) return "no ingredient to test with";
            var preserved = new ItemStack(meat, 1);
            Preservation.Apply(preserved, PreservationMethods.Formaldehyde);
            if (cfg.BlockPreservedFoodInRecipes && foodIngredient.SatisfiesAsIngredient(preserved)) return "preserved food accepted as an ingredient";
            if (meat.GetGrindingProperties(world, preserved) != null && cfg.BlockPreservedFoodInRecipes) return "preserved food grindable";
            return null;
        });

        Check("formaldehyde barrel soak", () => BarrelTest(fluid, meat, 1000, PreservationMethods.Formaldehyde, cfg.FormaldehydeSealHours,
            expectLeft: 1000 - (int)Math.Ceiling(8 * cfg.FormaldehydeLitresPerFood * 100)));

        Check("salt & lime barrel cure", () => BarrelTest(curingSalt, meat, 64, PreservationMethods.SaltCure, cfg.SaltCureSealHours,
            expectLeft: 64 - 8 * cfg.CuringSaltPerFood));

        Check("barrel needs enough agent", () =>
        {
            var slots = new ItemSlot[] { new DummySlot(new ItemStack(meat, 8)), new DummySlot(new ItemStack(fluid, 10)) };
            return EmbalmingBarrelRecipe.TryCreate(slots) != null ? "accepted too little formaldehyde" : null;
        });

        Check("formaldehyde and curing salt are craftable", () =>
        {
            if (!sapi.GetBarrelRecipes().Any(r => r.Output?.ResolvedItemStack?.Collectible == fluid)) return "no barrel recipe makes formaldehyde solution";
            if (!world.GridRecipes.Any(r => r.Output?.ResolvedItemStack?.Collectible == curingSalt)) return "no grid recipe makes curing salt";
            return null;
        });

        Check("meal containers preserved through their contents", () =>
        {
            BlockMeal? mealBlock = world.Blocks.OfType<BlockMeal>().FirstOrDefault(b => b is not BlockPie && b.Code != null && !b.IsMissing);
            if (mealBlock == null) return null; // nothing to test
            var bowl = new ItemStack(mealBlock, 1);
            mealBlock.SetContents(null, bowl, new[] { new ItemStack(meat, 1) }, 1);
            if (!Preservation.CanPreserve(bowl)) return "filled meal bowl not preservable";
            Preservation.Apply(bowl, PreservationMethods.Formaldehyde);
            bowl.Attributes.RemoveAttribute(Preservation.AttrKey);
            if (!Preservation.IsPreserved(bowl)) return "bowl not seen as preserved via its contents";
            if (mealBlock.UpdateAndGetTransitionStates(world, new DummySlot(bowl)) != null) return "meal bowl still transitions";
            return null;
        });

        Check("shelves accept preserved food", () =>
        {
            var candidate = world.Collectibles.FirstOrDefault(c => c?.Code != null && c is Item && Preservation.IsPreservableCollectible(c)
                && c.Attributes?["shelvable"].Exists != true && c.GetCollectibleInterface<IShelvable>() == null);
            if (candidate == null) return null;
            var stack = new ItemStack(candidate, 1);
            if (BlockEntityShelf.GetShelvableLayout(stack) != null) return null; // vanilla allows it some other way
            Preservation.Apply(stack, PreservationMethods.Wax);
            return BlockEntityShelf.GetShelvableLayout(stack) == null ? $"{candidate.Code} refused by shelves" : null;
        });

        Check("toxicity escalates from vomiting to lethal", () =>
        {
            var stages = cfg.ToxicityStages;
            if (ToxicitySystem.StageFor(0.5f, stages) != null) return "effect below first threshold";
            var first = ToxicitySystem.StageFor(1f, stages);
            if (first == null || first.PoisonDamage > 0 || first.SatietyLossPercent <= 0) return "first stage should be vomiting only";
            var last = ToxicitySystem.StageFor(99f, stages);
            if (last == null || last.PoisonDamage <= first.PoisonDamage) return "later stages should be worse";
            return null;
        });

        return Report(sapi, results);

        string? BarrelTest(Item agentItem, Item food, int agentAmount, string method, double sealHours, int expectLeft)
        {
            var slots = new ItemSlot[] { new DummySlot(new ItemStack(food, 8)), new DummySlot(new ItemStack(agentItem, agentAmount)) };
            var recipe = EmbalmingBarrelRecipe.TryCreate(slots);
            if (recipe == null) return "barrel did not recognise food + agent";
            if (recipe.Method != method) return $"wrong method {recipe.Method}";
            if (Math.Abs(recipe.SealHours - sealHours) > 0.01) return $"seal time {recipe.SealHours}h, expected {sealHours}h";
            if (recipe.RecipeOutput.ResolvedItemStack?.GetName() is not string preview || !preview.Contains(food.GetHeldItemName(new ItemStack(food)))) return "barrel preview name wrong";
            if (recipe.CraftNow(sealHours - 1, slots)) return "finished before the seal time";
            if (!recipe.CraftNow(sealHours + 1, slots)) return "did not finish after the seal time";
            if (Preservation.GetMethod(slots[0].Itemstack) != method) return "food not preserved";
            if (slots[0].Itemstack?.StackSize != 8) return "food amount changed";
            int left = slots[1].Itemstack?.StackSize ?? 0;
            if (left != expectLeft) return $"agent left {left}, expected {expectLeft}";
            if (EmbalmingBarrelRecipe.TryCreate(slots) != null) return "already preserved food matched again";
            return null;
        }
    }

    static string Report(ICoreServerAPI sapi, List<(bool ok, string name, string detail)> results)
    {
        var sb = new StringBuilder();
        foreach (var (ok, name, detail) in results)
        {
            string line = ok ? $"SELFTEST ok: {name}" : $"SELFTEST FAILED: {name} - {detail}";
            sb.AppendLine(line);
            if (ok) sapi.Logger.Notification("[Embalmed Eats] " + line);
            else sapi.Logger.Warning("[Embalmed Eats] " + line);
        }

        int failed = results.Count(r => !r.ok);
        string summary = failed == 0 ? $"SELFTEST PASS ({results.Count}/{results.Count})" : $"SELFTEST FAIL ({failed} of {results.Count} failed)";
        sb.AppendLine(summary);
        sapi.Logger.Notification("[Embalmed Eats] " + summary);
        return sb.ToString().TrimEnd();
    }
}
