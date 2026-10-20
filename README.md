# Embalmed Eats

A Vintage Story mod (game version 1.22) that turns food into permanent decoration.

Preserved food **keeps its exact original model and texture**, **never rots**, and can be shown off on shelves and in display cases. It is still edible, but eating it is a very bad idea.

It works on **any food, vanilla or modded**, without needing a compatibility patch per mod: if the game considers something food (it has nutrition, rots, or is a meal), it can be preserved.

## Preservation methods

| Method | How | Default cost |
|---|---|---|
| **Formaldehyde soak** | Seal food in a barrel with formaldehyde solution | 0.25 L per food item, 2 days |
| **Salt & lime cure** | Seal food in a barrel with curing salt | 1 curing salt per food item, 5 days |
| **Wax dipping** | Food + beeswax in the crafting grid | 1 beeswax per food item |
| **Resin lacquer** | Food + resin in the crafting grid | 1 resin per food item |

New items:

- **Formaldehyde solution**: in a barrel, mix 5 L of alcohol with 1 powdered borax.
- **Curing salt**: salt + lime in the crafting grid (makes 2).

The preserved item gets a name prefix (Embalmed, Mummified, Wax-sealed, Lacquered) and a tooltip warning. There is a guide page in the handbook, and clicking any preserved item in the handbook (or pressing the handbook key while hovering one) opens it.

In creative mode, every food comes ready-made in four **Embalmed Eats** tabs, one per method: Wax-sealed, Lacquered, Embalmed and Mummified.

## Eating preserved food

Preserved food gives no nutrition. Instead, each preserved item you eat adds toxicity, which wears off over time (one point per 8 in-game hours by default):

1. **1st item**: you vomit and lose 30% of your hunger bar.
2. **2nd**: heavier vomiting (50%) and slight poisoning.
3. **3rd**: vomiting (80%), poisoning and nausea.
4. **4th**: empty stomach, strong poisoning, nausea and slowness.
5. **5th and beyond**: a lethal dose.

Every stage can be tuned or replaced in the config.

## Decoration

Preserved food can go on **shelves**, in **display cases** and **on the ground** (shift + right-click) even if that food normally can't. Food without its own display setup is scaled down to fit. Fresh food that can't normally go on the ground still can't; only its preserved version can.

Preserved food is kept out of food processing so it can't be turned back into edible food: crafting and barrel recipes, cooking pots, firepit cooking and querns all refuse it.

## Commands

| Command | Who | What |
|---|---|---|
| `/embalmedeats help` (or just `/embalmedeats`) | everyone | List these commands |
| `/embalmedeats preserve [wax\|resin\|formaldehyde\|saltcure]` | gamemode privilege | Preserve the held stack instantly (handy for creative builds) |
| `/embalmedeats unpreserve` | gamemode privilege | Undo preservation on the held stack |
| `/embalmedeats toxicity [player]` | everyone | Show toxicity |
| `/embalmedeats cure [player]` | commandplayer privilege | Clear toxicity and its effects |
| `/embalmedeats info` | controlserver privilege | Show detected foods, generated recipes and patches |
| `/embalmedeats selftest` | controlserver privilege | Run the built-in checks |

## Config

`VintagestoryData/ModConfig/embalmedeats.json` is created on first start. The server's config is sent to players when they join. Options include:

- turning each method on or off
- which items count as wax and resin (wildcards allowed, so modded waxes work)
- barrel amounts and seal times
- `AlwaysPreservable` / `NeverPreservable` code lists (wildcards like `mymod:*`)
- shelf, display case and ground support, display scale
- whether preserved food is blocked from recipes
- toxicity per item, decay rate and every toxicity stage

Mod authors can also add `"embalmedeatsPreservable": true` or `false` to an item's attributes.

## Compatibility

- Recipes for wax and resin are generated at server start for every food in the game, including modded food.
- The barrel methods work with any food and any barrel that is based on the vanilla barrel.
- Rot prevention, nutrition and eating hooks are applied to every class that overrides the relevant game code, in vanilla and in all loaded mods.
- Preservation is stored on the item stack as a marker plus the vanilla `timeFrozen` flag, so other mods' item data is left intact.

Known limit: a preserved block food (pie, meal bowl) placed in the world as a block is kept fresh through its contents, but the visible name prefix comes back only once it is picked up again.

## Building

Requires the .NET 10 SDK and a Vintage Story 1.22 install or dedicated server.

```
dotnet build src/EmbalmedEats/EmbalmedEats.csproj -c Release -p:VintageStoryPath=/path/to/vintagestory
```

Package `modinfo.json`, `modicon.png`, `assets/` and `EmbalmedEats.dll` into a zip and drop it in your `Mods` folder.

Every push runs the GitHub Actions workflow, which compiles against the real game DLLs, boots a dedicated server with the mod and runs `/embalmedeats selftest`. The ready-to-install zip is attached to each workflow run as an artifact.
