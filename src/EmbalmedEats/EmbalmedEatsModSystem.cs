using System;
using System.Linq;
using EmbalmedEats.Config;
using EmbalmedEats.Core;
using EmbalmedEats.Decoration;
using EmbalmedEats.Patches;
using EmbalmedEats.Recipes;
using EmbalmedEats.Toxicity;
using HarmonyLib;
using Newtonsoft.Json;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EmbalmedEats;

[ProtoContract]
public class ConfigSyncPacket
{
    [ProtoMember(1)] public string Json = "";
}

public class EmbalmedEatsModSystem : ModSystem
{
    public const string ModId = "embalmedeats";
    public const string ConfigFile = "embalmedeats.json";

    static readonly object patchLock = new();
    static Harmony? harmony;
    static int patchUsers;

    ICoreAPI api = null!;
    ICoreServerAPI? sapi;
    IServerNetworkChannel? serverChannel;
    ToxicitySystem? toxicity;

    // Late, so other mods have finished adjusting their food before we generate recipes
    public override double ExecuteOrder() => 5;

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;
        LoadConfig(api);
    }

    public override void Start(ICoreAPI api)
    {
        PatchUtil.Logger = api.Logger;

        // Client and server share one process in singleplayer, Harmony patches are process wide
        lock (patchLock)
        {
            if (harmony == null)
            {
                harmony = new Harmony(ModId);
                ApplyPatches(harmony, api);
            }
            patchUsers++;
        }

        api.Network.RegisterChannel(ModId).RegisterMessageType<ConfigSyncPacket>();
    }

    static void ApplyPatches(Harmony harmony, ICoreAPI api)
    {
        void Run(string name, Action<Harmony> apply)
        {
            try { apply(harmony); }
            catch (Exception e)
            {
                PatchUtil.Failures.Add($"{name}: {e.Message}");
                api.Logger.Error("[Embalmed Eats] Patch group {0} failed: {1}", name, e);
            }
        }

        Run(TransitionPatches.Label, TransitionPatches.Apply);
        Run(NutritionPatches.Label, NutritionPatches.Apply);
        Run(EatingPatches.Label, EatingPatches.Apply);
        Run(CraftingPatches.Label, CraftingPatches.Apply);
        Run(NamePatches.Label, NamePatches.Apply);
        Run(BarrelPatches.Label, BarrelPatches.Apply);
        Run(DisplayPatches.Label, DisplayPatches.Apply);
        Run(GroundStoragePatches.Label, GroundStoragePatches.Apply);
        Run(HandbookPatches.Label, HandbookPatches.Apply);

        api.Logger.Notification("[Embalmed Eats] Applied {0} patches ({1} transition, {2} nutrition, {3} eating, {4} crafting, {5} barrel, {6} display). {7} problems.",
            PatchUtil.Patched.Count,
            PatchUtil.CountPatched(TransitionPatches.Label), PatchUtil.CountPatched(NutritionPatches.Label),
            PatchUtil.CountPatched(EatingPatches.Label), PatchUtil.CountPatched(CraftingPatches.Label),
            PatchUtil.CountPatched(BarrelPatches.Label), PatchUtil.CountPatched(DisplayPatches.Label),
            PatchUtil.Failures.Count);

        foreach (string f in PatchUtil.Failures) api.Logger.Warning("[Embalmed Eats] Patch problem: {0}", f);
    }

    void LoadConfig(ICoreAPI api)
    {
        EmbalmedEatsConfig? cfg = null;
        try
        {
            cfg = api.LoadModConfig<EmbalmedEatsConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error("[Embalmed Eats] Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
        }

        cfg ??= new EmbalmedEatsConfig();
        if (cfg.ToxicityStages == null || cfg.ToxicityStages.Length == 0) cfg.ToxicityStages = new EmbalmedEatsConfig().ToxicityStages;

        if (api.Side == EnumAppSide.Server)
        {
            try { api.StoreModConfig(cfg, ConfigFile); } // writes defaults / new options back
            catch (Exception e) { api.Logger.Warning("[Embalmed Eats] Could not write config: {0}", e.Message); }
        }

        Preservation.Config = cfg;
        Preservation.ClearCache();
    }

    /// <summary>
    /// On the server: wax/resin recipes and ground storage. The ground storage behavior reaches clients with the
    /// item definitions (the same way vanilla adds behaviors); the client repeats it only for items that didn't
    /// get it that way. Creative tabs are client only.
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        Preservation.ClearCache();

        if (api is ICoreServerAPI sapi)
        {
            Step(api, "generate wax/resin recipes", () => GridRecipeGenerator.Generate(sapi));
        }

        Step(api, "add ground storage", () =>
        {
            int n = GroundStorageSupport.AddToPreservableItems(api);
            api.Logger.Notification("[Embalmed Eats] Ground storage added to {0} preservable items that didn't have it ({1}).", n, api.Side);
        });
        if (api.Side == EnumAppSide.Client) AddCreativeTabs(api);
    }

    /// <summary>Client only (see CreativeTabs). Runs at the first of two points after the items arrive; the second finds nothing left to do.</summary>
    void AddCreativeTabs(ICoreAPI api)
    {
        Step(api, "add creative tabs", () =>
        {
            int n = CreativeTabs.AddToCreativeInventory(api);
            if (n > 0) api.Logger.Notification("[Embalmed Eats] Creative tabs: {0} foods added.", n);
        });
    }

    static void Step(ICoreAPI api, string what, Action action)
    {
        try { action(); }
        catch (Exception e) { api.Logger.Error("[Embalmed Eats] Failed to {0}: {1}", what, e); }
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        toxicity = new ToxicitySystem(sapi);
        serverChannel = sapi.Network.GetChannel(ModId);
        sapi.Event.PlayerJoin += SendConfig;
        Commands.Register(sapi, this);
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        capi.Network.GetChannel(ModId).SetMessageHandler<ConfigSyncPacket>(OnConfigReceived);
        // The documented point where a client has all blocks and items from the server
        capi.Event.BlockTexturesLoaded += () =>
        {
            Step(capi, "add ground storage", () => GroundStorageSupport.AddToPreservableItems(capi));
            AddCreativeTabs(capi);
        };
    }

    void SendConfig(IServerPlayer player)
    {
        try
        {
            serverChannel?.SendPacket(new ConfigSyncPacket { Json = JsonConvert.SerializeObject(Preservation.Config) }, player);
        }
        catch (Exception e)
        {
            sapi?.Logger.Warning("[Embalmed Eats] Could not send config to {0}: {1}", player.PlayerName, e.Message);
        }
    }

    void OnConfigReceived(ConfigSyncPacket packet)
    {
        // In singleplayer the server already set the shared config
        if (api is ICoreClientAPI capi && capi.IsSinglePlayer) return;
        try
        {
            var cfg = JsonConvert.DeserializeObject<EmbalmedEatsConfig>(packet.Json);
            if (cfg != null)
            {
                Preservation.Config = cfg;
                Preservation.ClearCache();
            }
        }
        catch (Exception e)
        {
            api.Logger.Warning("[Embalmed Eats] Bad config from server: {0}", e.Message);
        }
    }

    public override void Dispose()
    {
        if (sapi != null) sapi.Event.PlayerJoin -= SendConfig;
        toxicity?.Dispose();

        lock (patchLock)
        {
            patchUsers--;
            if (patchUsers <= 0 && harmony != null)
            {
                harmony.UnpatchAll(ModId);
                harmony = null;
                patchUsers = 0;
                PatchUtil.Reset();
            }
        }
    }
}
