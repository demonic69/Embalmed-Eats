using System.Linq;
using System.Text;
using EmbalmedEats.Core;
using EmbalmedEats.Patches;
using EmbalmedEats.Recipes;
using EmbalmedEats.Toxicity;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EmbalmedEats;

/// <summary>
/// /embalmedeats commands.
///   help                list the commands (also what plain /embalmedeats shows)
///   preserve [method]   preserve the held stack (creative builders, needs gamemode privilege)
///   unpreserve          undo preservation on the held stack
///   toxicity [player]   show toxicity
///   cure [player]       clear toxicity and its effects
///   info                show what the mod detected and patched
///   selftest            run the built-in checks (server console or admin)
/// </summary>
public static class Commands
{
    public static void Register(ICoreServerAPI sapi, EmbalmedEatsModSystem mod)
    {
        var p = sapi.ChatCommands.Parsers;

        sapi.ChatCommands.Create("embalmedeats")
            .WithDescription("Embalmed Eats: preserve food as decoration. /embalmedeats help lists the commands.")
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(args => TextCommandResult.Success(HelpText(args.Caller, LangCode(args))))

            .BeginSubCommand("help")
                .WithDescription("List the Embalmed Eats commands")
                .HandleWith(args => TextCommandResult.Success(HelpText(args.Caller, LangCode(args))))
            .EndSubCommand()

            .BeginSubCommand("preserve")
                .WithDescription("Preserve the stack in your hand (methods: wax, resin, formaldehyde, saltcure)")
                .RequiresPrivilege(Privilege.gamemode)
                .RequiresPlayer()
                .WithArgs(p.OptionalWordRange("method", PreservationMethods.All))
                .HandleWith(args =>
                {
                    var slot = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot;
                    if (slot?.Itemstack == null) return TextCommandResult.Error("Hold some food first.");
                    string method = args[0] as string ?? PreservationMethods.Formaldehyde;
                    if (Preservation.IsPreserved(slot.Itemstack)) return TextCommandResult.Error("That is already preserved.");
                    if (!Preservation.CanPreserve(slot.Itemstack)) return TextCommandResult.Error("That can't be preserved (not detected as food).");
                    Preservation.Apply(slot.Itemstack, method);
                    slot.MarkDirty();
                    return TextCommandResult.Success($"Preserved: {slot.Itemstack.GetName()}");
                })
            .EndSubCommand()

            .BeginSubCommand("unpreserve")
                .WithDescription("Remove preservation from the stack in your hand")
                .RequiresPrivilege(Privilege.gamemode)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    var slot = args.Caller.Player?.InventoryManager?.ActiveHotbarSlot;
                    if (slot?.Itemstack == null || !Preservation.IsPreserved(slot.Itemstack)) return TextCommandResult.Error("Hold a preserved item first.");
                    Preservation.Remove(slot.Itemstack);
                    slot.MarkDirty();
                    return TextCommandResult.Success($"No longer preserved: {slot.Itemstack.GetName()}");
                })
            .EndSubCommand()

            .BeginSubCommand("toxicity")
                .WithDescription("Show toxicity of yourself or another player")
                .WithArgs(p.OptionalPlayerUids("player"))
                .HandleWith(args => ForPlayers(sapi, args, (plr, tox) =>
                    $"{plr.PlayerName}: toxicity {tox:0.##} (stage: {ToxicitySystem.StageFor(tox, Preservation.Config.ToxicityStages)?.Name ?? "none"})"))
            .EndSubCommand()

            .BeginSubCommand("cure")
                .WithDescription("Clear toxicity and its effects")
                .RequiresPrivilege(Privilege.commandplayer)
                .WithArgs(p.OptionalPlayerUids("player"))
                .HandleWith(args => ForPlayers(sapi, args, (plr, tox) =>
                {
                    ToxicitySystem.Instance?.Cure(plr.Entity);
                    return $"{plr.PlayerName}: cured (was {tox:0.##})";
                }))
            .EndSubCommand()

            .BeginSubCommand("info")
                .WithDescription("Show detected foods, generated recipes and patches")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(args => TextCommandResult.Success(Info(sapi)))
            .EndSubCommand()

            .BeginSubCommand("selftest")
                .WithDescription("Run the mod's built-in checks")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(args => TextCommandResult.Success(SelfTest.Run(sapi)))
            .EndSubCommand();
    }

    /// <summary>Every command, with its arguments, lang key for what it does, and the privilege it needs (null = everyone).</summary>
    public static readonly (string usage, string langKey, string? privilege)[] HelpEntries =
    {
        ("/embalmedeats help", "help-help", null),
        ("/embalmedeats preserve [wax|resin|formaldehyde|saltcure]", "help-preserve", Privilege.gamemode),
        ("/embalmedeats unpreserve", "help-unpreserve", Privilege.gamemode),
        ("/embalmedeats toxicity [player]", "help-toxicity", null),
        ("/embalmedeats cure [player]", "help-cure", Privilege.commandplayer),
        ("/embalmedeats info", "help-info", Privilege.controlserver),
        ("/embalmedeats selftest", "help-selftest", Privilege.controlserver),
    };

    static string LangCode(TextCommandCallingArgs args) =>
        (args.Caller.Player as IServerPlayer)?.LanguageCode ?? args.LanguageCode ?? Lang.CurrentLocale ?? "en";

    /// <summary>The command list. Commands the caller can't use are still listed, marked with the privilege they need.</summary>
    public static string HelpText(Caller? caller, string langCode = "en")
    {
        var sb = new StringBuilder();
        sb.AppendLine(Lang.GetL(langCode, "embalmedeats:help-title"));
        foreach (var (usage, langKey, privilege) in HelpEntries)
        {
            string line = usage + " - " + Lang.GetL(langCode, "embalmedeats:" + langKey);
            if (privilege != null && !HasPrivilege(caller, privilege))
            {
                line += " (" + Lang.GetL(langCode, "embalmedeats:help-needs", privilege) + ")";
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    static bool HasPrivilege(Caller? caller, string privilege) => caller == null || caller.HasPrivilege(privilege);

    static TextCommandResult ForPlayers(ICoreServerAPI sapi, TextCommandCallingArgs args, System.Func<IServerPlayer, float, string> fn)
    {
        var tox = ToxicitySystem.Instance;
        if (tox == null) return TextCommandResult.Error("Toxicity system not running.");

        var players = (args[0] as PlayerUidName[])?.Select(pu => sapi.World.PlayerByUid(pu.Uid) as IServerPlayer).Where(x => x != null).ToList();
        if (players == null || players.Count == 0)
        {
            if (args.Caller.Player is IServerPlayer self) players = new() { self };
            else return TextCommandResult.Error("Name a player.");
        }

        var sb = new StringBuilder();
        foreach (var plr in players)
        {
            if (plr!.Entity == null) { sb.AppendLine($"{plr.PlayerName}: not in the world"); continue; }
            sb.AppendLine(fn(plr, tox.GetToxicity(plr.Entity)));
        }
        return TextCommandResult.Success(sb.ToString().TrimEnd());
    }

    public static string Info(ICoreServerAPI sapi)
    {
        int foods = sapi.World.Collectibles.Count(c => c?.Code != null && Preservation.IsPreservableCollectible(c));
        var sb = new StringBuilder();
        sb.AppendLine($"Preservable foods detected: {foods}");
        sb.AppendLine($"Wax/resin crafting recipes generated: {GridRecipeGenerator.Generated}");
        sb.AppendLine($"Patched methods: {PatchUtil.Patched.Count}");
        foreach (var group in PatchUtil.Patched.GroupBy(x => x.Split(':')[0])) sb.AppendLine($"  {group.Key}: {group.Count()}");
        sb.AppendLine($"Patch problems: {PatchUtil.Failures.Count}");
        foreach (var f in PatchUtil.Failures) sb.AppendLine("  " + f);
        return sb.ToString().TrimEnd();
    }
}
