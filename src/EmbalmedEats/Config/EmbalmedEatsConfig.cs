namespace EmbalmedEats.Config;

/// <summary>
/// Stored as ModConfig/embalmedeats.json. Every value can be changed by server owners.
/// Arrays are used (not lists) so a saved config replaces the defaults instead of appending to them.
/// </summary>
public class EmbalmedEatsConfig
{
    // ---- Which methods are available ----
    public bool EnableWaxDipping = true;
    public bool EnableResinLacquer = true;
    public bool EnableFormaldehydeBarrel = true;
    public bool EnableSaltLimeCure = true;

    // ---- Crafting grid methods ----
    /// <summary>Items that count as wax for wax dipping. Wildcards allowed.</summary>
    public string[] WaxItems = { "game:beeswax" };
    /// <summary>Items that count as resin/lacquer for resin lacquering. Wildcards allowed.</summary>
    public string[] LacquerItems = { "game:resin" };
    /// <summary>How many wax/resin items one food item needs.</summary>
    public int PreservativePerFood = 1;

    // ---- Barrel methods ----
    /// <summary>Litres of formaldehyde solution used per food item.</summary>
    public float FormaldehydeLitresPerFood = 0.25f;
    public double FormaldehydeSealHours = 48;
    /// <summary>Curing salt items used per food item.</summary>
    public int CuringSaltPerFood = 1;
    public double SaltCureSealHours = 120;

    // ---- What counts as food ----
    /// <summary>Codes (wildcards allowed, e.g. "mymod:*") that can always be preserved, even if not detected as food.</summary>
    public string[] AlwaysPreservable = System.Array.Empty<string>();
    /// <summary>Codes (wildcards allowed) that can never be preserved.</summary>
    public string[] NeverPreservable = { "game:rot" };

    // ---- Decoration ----
    public bool AllowOnShelves = true;
    public bool AllowInDisplayCases = true;
    /// <summary>Preserved food can be placed on the ground (shift + right-click), even food that normally can't.</summary>
    public bool AllowOnGround = true;
    /// <summary>Shrinks preserved items that have no display transform of their own so they fit shelves and display cases.</summary>
    public float FallbackDisplayScale = 0.5f;

    // ---- Misuse protection ----
    /// <summary>Stops preserved food being used in crafting/barrel recipes, cooking pots, firepit cooking and querns.</summary>
    public bool BlockPreservedFoodInRecipes = true;

    // ---- Eating preserved food ----
    /// <summary>Toxicity added for each preserved item eaten.</summary>
    public float ToxicityPerItem = 1f;
    /// <summary>Toxicity removed per in-game hour. Default clears one point every 8 in-game hours.</summary>
    public float ToxicityDecayPerGameHour = 0.125f;
    public float MaxToxicity = 10f;

    /// <summary>
    /// Effects by toxicity level after eating. The highest stage whose Threshold is reached applies.
    /// Starts with vomiting and escalates to poisoning the more preserved food is eaten.
    /// </summary>
    public ToxicityStage[] ToxicityStages =
    {
        new() { Threshold = 1, Name = "queasy",   SatietyLossPercent = 30 },
        new() { Threshold = 2, Name = "retching", SatietyLossPercent = 50, PoisonDamage = 2,  PoisonDurationSeconds = 10 },
        new() { Threshold = 3, Name = "poisoned", SatietyLossPercent = 80, PoisonDamage = 6,  PoisonDurationSeconds = 30, Nausea = 0.5f },
        new() { Threshold = 4, Name = "severe",   SatietyLossPercent = 100, PoisonDamage = 12, PoisonDurationSeconds = 45, Nausea = 1f, SlownessPercent = 30, SlownessSeconds = 60 },
        new() { Threshold = 5, Name = "lethal",   SatietyLossPercent = 100, PoisonDamage = 40, PoisonDurationSeconds = 20, Nausea = 1.1f, SlownessPercent = 50, SlownessSeconds = 60 },
    };
}

public class ToxicityStage
{
    public float Threshold;
    /// <summary>Lang key suffix for the chat message: embalmedeats:toxicity-{Name}.</summary>
    public string Name = "queasy";
    /// <summary>Percent of max satiety lost by vomiting.</summary>
    public float SatietyLossPercent;
    /// <summary>Total poison damage in hit points, dealt over PoisonDurationSeconds.</summary>
    public float PoisonDamage;
    public float PoisonDurationSeconds;
    /// <summary>Adds to the vanilla intoxication (wobbly vision) effect, 0 to 1.1.</summary>
    public float Nausea;
    public float SlownessPercent;
    public float SlownessSeconds;
}
