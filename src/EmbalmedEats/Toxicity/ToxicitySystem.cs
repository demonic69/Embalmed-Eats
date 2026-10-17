using System;
using System.Collections.Generic;
using System.Linq;
using EmbalmedEats.Config;
using EmbalmedEats.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EmbalmedEats.Toxicity;

/// <summary>
/// Server-side effect of eating preserved food. Each preserved item eaten adds toxicity, which
/// slowly wears off. The first bite makes you vomit; keep eating and it escalates to poisoning,
/// nausea, sluggishness and finally a lethal dose. All stages are configurable.
/// </summary>
public class ToxicitySystem
{
    public static ToxicitySystem? Instance { get; private set; }

    public const string AttrTree = "embalmedeats";
    public const string StatCode = "embalmedeats-toxicity";

    readonly ICoreServerAPI sapi;
    readonly Dictionary<long, long> slowUntilMs = new();

    public ToxicitySystem(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        Instance = this;
        sapi.Event.PlayerJoin += OnPlayerJoin;
    }

    public void Dispose()
    {
        sapi.Event.PlayerJoin -= OnPlayerJoin;
        if (Instance == this) Instance = null;
    }

    static EmbalmedEatsConfig Config => Preservation.Config;

    void OnPlayerJoin(IServerPlayer player)
    {
        // Slowness is not persisted across restarts; clear any leftover modifier.
        player.Entity?.Stats.Remove("walkspeed", StatCode);
    }

    /// <summary>Current toxicity of an entity with time decay applied.</summary>
    public float GetToxicity(Entity entity)
    {
        ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(AttrTree);
        if (tree == null) return 0;

        float tox = tree.GetFloat("toxicity");
        double now = sapi.World.Calendar.TotalHours;
        double last = tree.GetDouble("lastUpdateHours", now);
        double hours = Math.Max(0, now - last);
        return Math.Max(0, tox - (float)(hours * Math.Max(0, Config.ToxicityDecayPerGameHour)));
    }

    public void SetToxicity(Entity entity, float value)
    {
        ITreeAttribute tree = entity.WatchedAttributes.GetOrAddTreeAttribute(AttrTree);
        tree.SetFloat("toxicity", Math.Max(0, value));
        tree.SetDouble("lastUpdateHours", sapi.World.Calendar.TotalHours);
        entity.WatchedAttributes.MarkPathDirty(AttrTree);
    }

    public void Cure(Entity entity)
    {
        SetToxicity(entity, 0);
        entity.Stats.Remove("walkspeed", StatCode);
        slowUntilMs.Remove(entity.EntityId);
    }

    /// <summary>Highest stage whose threshold is reached, or null if none.</summary>
    public static ToxicityStage? StageFor(float toxicity, ToxicityStage[]? stages)
    {
        if (stages == null) return null;
        ToxicityStage? best = null;
        foreach (var s in stages)
        {
            if (s == null || toxicity < s.Threshold) continue;
            if (best == null || s.Threshold > best.Threshold) best = s;
        }
        return best;
    }

    public void OnPreservedFoodEaten(EntityAgent eater, ItemStack eaten, string method)
    {
        if (eater is not EntityPlayer eplayer) return;

        float tox = Math.Min(GetToxicity(eater) + Config.ToxicityPerItem, Math.Max(1, Config.MaxToxicity));
        SetToxicity(eater, tox);

        string foodName = eaten.GetName();
        ToxicityStage? stage = StageFor(tox, Config.ToxicityStages);

        sapi.Logger.Audit("[Embalmed Eats] {0} ate preserved {1} ({2}), toxicity now {3:0.##}, stage {4}",
            eplayer.Player?.PlayerName ?? "?", eaten.Collectible?.Code, method, tox, stage?.Name ?? "none");

        if (stage == null)
        {
            Tell(eplayer, "embalmedeats:toxicity-mild", foodName);
            return;
        }

        ApplyStage(eplayer, stage);
        Tell(eplayer, "embalmedeats:toxicity-" + stage.Name, foodName);
    }

    void ApplyStage(EntityPlayer ep, ToxicityStage stage)
    {
        if (stage.SatietyLossPercent > 0)
        {
            var hunger = ep.GetBehavior<EntityBehaviorHunger>();
            if (hunger != null)
            {
                float loss = hunger.MaxSaturation * GameMath.Clamp(stage.SatietyLossPercent, 0, 100) / 100f;
                hunger.Saturation = Math.Max(0, hunger.Saturation - loss);
            }
            SpawnVomit(ep);
        }

        if (stage.PoisonDamage > 0)
        {
            float duration = Math.Max(0, stage.PoisonDurationSeconds);
            ep.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Internal,
                Type = EnumDamageType.Poison,
                Duration = TimeSpan.FromSeconds(duration),
                TicksPerDuration = duration > 0 ? Math.Max(1, (int)Math.Ceiling(duration / 2f)) : 1,
                DamageOverTimeTypeEnum = EnumDamageOverTimeEffectType.Poison
            }, stage.PoisonDamage);
        }

        if (stage.Nausea > 0)
        {
            float intox = ep.WatchedAttributes.GetFloat("intoxication");
            ep.WatchedAttributes.SetFloat("intoxication", Math.Min(1.1f, intox + stage.Nausea));
        }

        if (stage.SlownessPercent > 0 && stage.SlownessSeconds > 0)
        {
            ep.Stats.Set("walkspeed", StatCode, -GameMath.Clamp(stage.SlownessPercent, 0, 95) / 100f, false);
            long until = sapi.World.ElapsedMilliseconds + (long)(stage.SlownessSeconds * 1000);
            slowUntilMs[ep.EntityId] = until;

            sapi.World.RegisterCallback(_ =>
            {
                if (slowUntilMs.TryGetValue(ep.EntityId, out long u) && sapi.World.ElapsedMilliseconds + 50 >= u)
                {
                    ep.Stats.Remove("walkspeed", StatCode);
                    slowUntilMs.Remove(ep.EntityId);
                }
            }, (int)(stage.SlownessSeconds * 1000) + 100);
        }
    }

    void SpawnVomit(EntityPlayer ep)
    {
        Vec3d mouth = ep.Pos.XYZ.Add(ep.LocalEyePos.X, ep.LocalEyePos.Y - 0.25, ep.LocalEyePos.Z);
        Vec3f dir = ep.Pos.GetViewVector().Normalize();

        var props = new SimpleParticleProperties(
            25, 40,
            ColorUtil.ToRgba(255, 112, 120, 38),
            mouth.AddCopy(-0.05, -0.05, -0.05), mouth.AddCopy(0.05, 0.05, 0.05),
            new Vec3f(dir.X * 1.5f - 0.4f, -0.4f, dir.Z * 1.5f - 0.4f),
            new Vec3f(dir.X * 2.5f + 0.4f, 0.6f, dir.Z * 2.5f + 0.4f),
            1.5f, 1f, 0.15f, 0.45f, EnumParticleModel.Cube);

        sapi.World.SpawnParticles(props);
        sapi.World.PlaySoundAt(new AssetLocation("game:sounds/effect/squish1"), ep, null, true, 16);
    }

    void Tell(EntityPlayer ep, string langKey, string foodName)
    {
        if (ep.Player is not IServerPlayer sp) return;
        sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, langKey, foodName), EnumChatType.Notification);
    }
}
