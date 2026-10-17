using System.Collections.Generic;
using System.Linq;
using EmbalmedEats.Core;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace EmbalmedEats.Recipes;

/// <summary>
/// Generates the wax dipping and resin lacquer crafting grid recipes: one shapeless recipe per
/// food per preservative ("food + beeswax"). Generated at server start from every item and block
/// in the game, so modded food is included automatically. They are ordinary grid recipes, so the
/// engine syncs them to clients and shows them in the handbook like any other recipe.
/// </summary>
public static class GridRecipeGenerator
{
    public static int Generated { get; private set; }
    public static readonly List<GridRecipe> Recipes = new();

    public static int Generate(ICoreServerAPI sapi)
    {
        Generated = 0;
        Recipes.Clear();
        var cfg = Preservation.Config;
        var world = sapi.World;

        var methods = new List<(string method, string[] patterns)>();
        if (cfg.EnableWaxDipping) methods.Add((PreservationMethods.Wax, cfg.WaxItems));
        if (cfg.EnableResinLacquer) methods.Add((PreservationMethods.Resin, cfg.LacquerItems));
        if (methods.Count == 0) return 0;

        List<CollectibleObject> foods = world.Collectibles
            .Where(c => c?.Code != null && Preservation.IsPreservableCollectible(c))
            .ToList();

        foreach (var (method, patterns) in methods)
        {
            List<CollectibleObject> preservatives = world.Collectibles
                .Where(c => c?.Code != null && !c.IsMissing && Preservation.MatchesAny(patterns, c.Code))
                .ToList();

            if (preservatives.Count == 0)
            {
                sapi.Logger.Warning("[Embalmed Eats] No items found for {0} ({1}); that method has no recipes.", method, string.Join(", ", patterns ?? new string[0]));
                continue;
            }

            foreach (CollectibleObject preservative in preservatives)
            {
                foreach (CollectibleObject food in foods)
                {
                    if (food == preservative) continue;

                    GridRecipe recipe = Build(method, food, preservative, cfg.PreservativePerFood);
                    recipe.OnParsed(world);
                    if (recipe.Resolve(world, "Embalmed Eats generated recipe"))
                    {
                        sapi.RegisterCraftingRecipe(recipe);
                        Recipes.Add(recipe);
                        Generated++;
                    }
                }
            }
        }

        RecipeBase.CollectiblePreSearchResultsCache.Clear();
        sapi.Logger.Notification("[Embalmed Eats] Generated {0} wax/resin crafting recipes for {1} preservable foods.", Generated, foods.Count);
        return Generated;
    }

    static GridRecipe Build(string method, CollectibleObject food, CollectibleObject preservative, int preservativeQuantity)
    {
        var outputAttributes = new JObject
        {
            [Preservation.AttrKey] = new JObject { [Preservation.MethodKey] = method },
            [Preservation.TimeFrozenKey] = true
        };

        return new GridRecipe
        {
            Name = new AssetLocation("embalmedeats", $"grid/{method}/{preservative.Code.Domain}/{preservative.Code.Path}/{food.Code.Domain}/{food.Code.Path}"),
            IngredientPattern = "FP",
            Width = 2,
            Height = 1,
            Shapeless = true,
            ShowInCreatedBy = false,
            CopyAttributesFrom = "F",
            Attributes = new JsonObject(new JObject { ["embalmedeatsMethod"] = method }),
            Ingredients = new Dictionary<string, CraftingRecipeIngredient>
            {
                ["F"] = new CraftingRecipeIngredient { Type = food.ItemClass, Code = food.Code.Clone(), Quantity = 1 },
                ["P"] = new CraftingRecipeIngredient { Type = preservative.ItemClass, Code = preservative.Code.Clone(), Quantity = System.Math.Max(1, preservativeQuantity) }
            },
            Output = new CraftingRecipeIngredient
            {
                Type = food.ItemClass,
                Code = food.Code.Clone(),
                Quantity = 1,
                Attributes = new JsonObject(outputAttributes)
            }
        };
    }
}
