using System.Reflection;
using System.Runtime.CompilerServices;
using System.Numerics;

using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;

using HarmonyLib;

namespace AquafirCreatureVariants;

public sealed class CreatureVariantState
{
    internal CreatureVariantState(
        IReadOnlyList<CreatureVariantType> types,
        string originalName,
        CreatureVariantSettings settings)
    {
        Types = types.ToArray();
        OriginalName = originalName;
        Shields = settings.ShieldCount;
        ExploderTicksRemaining = settings.ExploderCountdownHeartbeats;
        DrainMana = ThreadSafeRandom.Next(0.0f, 1.0f) < 0.5;
        HordeInitialMembers = HasTrait(CreatureVariantType.Horde)
            ? ThreadSafeRandom.Next(settings.HordeMinimumMembers, settings.HordeMaximumMembers)
            : 1;
        BossWeakness = CreatureVariantRuntime.RollBossWeakness();
    }

    public IReadOnlyList<CreatureVariantType> Types { get; }
    public string OriginalName { get; }
    public bool DrainMana { get; }
    public bool BerserkerEnraged { get; set; }
    public int ComboHits { get; set; }
    public int Shields { get; set; }
    public double NextShieldRecharge { get; set; }
    public double NextHealTime { get; set; }
    public int ExploderTicksRemaining { get; set; }
    public bool Exploded { get; set; }
    public double NextRogueProcTime { get; set; }
    public int HordeInitialMembers { get; }
    public List<Creature> PuppetCopies { get; } = new();
    public double NextPuppetSpawnTime { get; set; }
    public DamageType BossWeakness { get; set; }
    public double NextBossWeaknessTime { get; set; }
    public double NextBossSpellTime { get; set; }
    public double NextStunTime { get; set; }

    public bool HasTrait(CreatureVariantType type) => Types.Contains(type);
}

public static class CreatureVariantRuntime
{
    private static readonly DamageType[] BossWeaknesses =
    {
        DamageType.Slash,
        DamageType.Pierce,
        DamageType.Bludgeon,
        DamageType.Cold,
        DamageType.Fire,
        DamageType.Acid,
        DamageType.Electric,
        DamageType.Nether
    };

    private static ConditionalWeakTable<Creature, CreatureVariantState> states = new();

    public static void Reset()
    {
        PuppetCopyRuntime.Reset();
        PlayerStunRuntime.Reset();
        states = new ConditionalWeakTable<Creature, CreatureVariantState>();
    }

    public static bool TryGet(Creature creature, out CreatureVariantState state) =>
        states.TryGetValue(creature, out state!);

    public static CreatureVariantState? TryAssign(WorldObject? worldObject)
    {
        if (worldObject is not Creature creature || creature.GetType() != typeof(Creature) ||
            !creature.Attackable || creature.IsNPC || states.TryGetValue(creature, out _))
            return null;

        var settings = Mod.Settings;
        var level = creature.Level ?? 0;
        if (!settings.IsEligible(creature.WeenieClassId, level))
            return null;

        var maximumRolls = Math.Max(1, settings.Source.MaximumVariantsPerCreature * 2 - 1);
        var randomRolls = Enumerable.Range(0, maximumRolls)
            .Select(_ => (double)ThreadSafeRandom.Next(0.0f, 1.0f))
            .ToArray();
        var types = settings.SelectTraits(
            creature.WeenieClassId,
            ThreadSafeRandom.Next(0.0f, 1.0f),
            randomRolls);
        if (types.Count == 0)
            return null;

        var state = new CreatureVariantState(types, creature.Name, settings.Source);
        states.Add(creature, state);

        if (state.HasTrait(CreatureVariantType.Boss) || state.HasTrait(CreatureVariantType.Horde))
        {
            creature.Health.Current = creature.Health.MaxValue;
            if (state.HasTrait(CreatureVariantType.Boss))
            {
                creature.Stamina.Current = creature.Stamina.MaxValue;
                creature.Mana.Current = creature.Mana.MaxValue;
                creature.PhysicsObj?.SetScaleStatic(creature.ObjScale ?? 1.0f);
            }
        }

        return state;
    }

    public static string GetNamePrefix(CreatureVariantType type, CreatureVariantState? state = null) => type switch
    {
        CreatureVariantType.Accurate => "Accurate ",
        CreatureVariantType.Berserker => "Raging ",
        CreatureVariantType.Comboer => "Frenetic ",
        CreatureVariantType.Drainer => state?.DrainMana == true ? "Stultifying " : "Atrophying ",
        CreatureVariantType.Duelist => "Dueling ",
        CreatureVariantType.Evader => "Evasive ",
        CreatureVariantType.Exploder => "Exploding ",
        CreatureVariantType.Healer => "Mending ",
        CreatureVariantType.Shielded => "Shielded ",
        CreatureVariantType.SpellBreaker => "Breaking ",
        CreatureVariantType.Stomper => "Massive ",
        CreatureVariantType.Vampire => "Vampiric ",
        CreatureVariantType.Rogue => "Rakish ",
        CreatureVariantType.Horde => "Swarm of ",
        CreatureVariantType.Puppeteer => "Conniving ",
        CreatureVariantType.Boss => "Tyrannical ",
        CreatureVariantType.Tank => "Guardian ",
        CreatureVariantType.Stunner => "Debilitating ",
        _ => string.Empty
    };

    public static int GetHordeLivingMembers(Creature creature, CreatureVariantState state)
    {
        if (creature.IsDead || state.HordeInitialMembers <= 0)
            return 0;
        return Math.Clamp((int)Math.Ceiling(creature.Health.Percent * state.HordeInitialMembers),
            1, state.HordeInitialMembers);
    }

    public static double GetHordeDamageMultiplier(int livingMembers, CreatureVariantSettings settings) =>
        Math.Min(settings.HordeMaximumDamageMultiplier,
            1.0 + Math.Max(0, livingMembers - 1) * settings.HordeDamagePerAdditionalLivingMember);

    public static DamageType RollBossWeakness() =>
        BossWeaknesses[ThreadSafeRandom.Next(0, BossWeaknesses.Length - 1)];

    internal static IEnumerable<Creature> GetNearbyCreatures(Creature origin, double range)
    {
        if (origin.Location is null || origin.PhysicsObj?.ObjMaint is null)
            yield break;

        foreach (var target in origin.PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature())
        {
            if (ReferenceEquals(origin, target) || target.Location is null || target.IsDead)
                continue;

            float distance;
            try
            {
                distance = origin.GetDistance(target);
            }
            catch
            {
                continue;
            }

            if (distance <= range)
                yield return target;
        }
    }

    public static string GetDisplayName(Creature creature, CreatureVariantState state)
    {
        var prefixes = string.Concat(state.Types
            .Where(type => type != CreatureVariantType.Horde)
            .Select(type => GetNamePrefix(type, state)));
        var baseName = state.HasTrait(CreatureVariantType.Horde)
            ? $"Swarm of {GetHordeLivingMembers(creature, state)}/{state.HordeInitialMembers} {state.OriginalName}"
            : state.OriginalName;
        return prefixes + baseName;
    }
}

internal static class PuppetCopyRuntime
{
    private sealed record Registration(Creature Owner, double ExpiresAt);

    private static readonly object Sync = new();
    private static readonly Dictionary<Creature, Registration> Copies = new();

    public static void Register(Creature copy, Creature owner, double expiresAt)
    {
        lock (Sync)
            Copies[copy] = new Registration(owner, expiresAt);
    }

    public static bool ProcessHeartbeat(Creature creature, double currentUnixTime)
    {
        Registration? registration;
        lock (Sync)
        {
            if (!Copies.TryGetValue(creature, out registration))
                return false;

            if (currentUnixTime < registration.ExpiresAt && !registration.Owner.IsDead && !registration.Owner.IsDestroyed)
                return true;

            Copies.Remove(creature);
        }

        DestroyCopy(creature);
        return true;
    }

    public static void DestroyForOwner(Creature owner)
    {
        Creature[] copies;
        lock (Sync)
        {
            copies = Copies.Where(pair => ReferenceEquals(pair.Value.Owner, owner))
                .Select(pair => pair.Key).ToArray();
            foreach (var copy in copies)
                Copies.Remove(copy);
        }

        foreach (var copy in copies)
            DestroyCopy(copy);
    }

    public static void Reset()
    {
        Creature[] copies;
        lock (Sync)
        {
            copies = Copies.Keys.ToArray();
            Copies.Clear();
        }

        foreach (var copy in copies)
            DestroyCopy(copy);
    }

    private static void DestroyCopy(Creature copy)
    {
        if (!copy.IsDestroyed)
            copy.Destroy(false);
    }
}

internal static class PlayerStunRuntime
{
    private sealed class ActiveStun
    {
        public required bool WasFrozen { get; init; }
        public double ExpiresAt { get; set; }
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<Player, ActiveStun> Stuns = new();

    public static void Apply(Player player, double currentUnixTime, double durationSeconds, string sourceName)
    {
        var expiresAt = currentUnixTime + durationSeconds;
        lock (Sync)
        {
            if (Stuns.TryGetValue(player, out var active))
                active.ExpiresAt = Math.Max(active.ExpiresAt, expiresAt);
            else
                Stuns[player] = new ActiveStun { WasFrozen = player.IsFrozen ?? false, ExpiresAt = expiresAt };
        }

        player.IsFrozen = true;
        if (player.Attacking || player.AttackTarget is not null)
            player.OnAttackDone();
        if (player.MagicState.IsCasting)
            player.FailCast(false);
        player.EnqueueBroadcastMotion(new Motion(MotionStance.NonCombat, MotionCommand.Kneel));
        player.SendMessage($"{sourceName} stuns you.");

        var release = new ActionChain();
        release.AddDelaySeconds(durationSeconds);
        release.AddAction(player, () => ReleaseIfCurrent(player, expiresAt));
        release.EnqueueChain();
    }

    public static bool ProcessHeartbeat(Player player, double currentUnixTime)
    {
        ActiveStun? active;
        lock (Sync)
        {
            if (!Stuns.TryGetValue(player, out active))
                return false;
            if (!player.IsDestroyed && currentUnixTime < active.ExpiresAt)
                return true;
            Stuns.Remove(player);
        }

        if (!player.IsDestroyed)
        {
            player.IsFrozen = active.WasFrozen;
            player.SendMessage("The stun wears off.");
        }
        return true;
    }

    public static void Reset()
    {
        KeyValuePair<Player, ActiveStun>[] active;
        lock (Sync)
        {
            active = Stuns.ToArray();
            Stuns.Clear();
        }

        foreach (var pair in active)
        {
            if (!pair.Key.IsDestroyed)
                pair.Key.IsFrozen = pair.Value.WasFrozen;
        }
    }

    private static void ReleaseIfCurrent(Player player, double expectedExpiry)
    {
        ActiveStun? active;
        lock (Sync)
        {
            if (!Stuns.TryGetValue(player, out active) || active.ExpiresAt > expectedExpiry)
                return;
            Stuns.Remove(player);
        }

        if (!player.IsDestroyed)
        {
            player.IsFrozen = active.WasFrozen;
            player.SendMessage("The stun wears off.");
        }
    }
}

[HarmonyPatch]
public static class CreatureVariantNamePatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.Name))
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, $"get_{nameof(WorldObject.Name)}");

    [HarmonyPostfix]
    public static void ShowTraitPrefix(WorldObject __instance, ref string __result)
    {
        if (!Mod.Settings.Source.Enabled || !Mod.Settings.Source.ShowTraitInCreatureName ||
            __instance is not Creature creature || !CreatureVariantRuntime.TryGet(creature, out var state))
            return;

        __result = CreatureVariantRuntime.GetDisplayName(creature, state);
    }
}

[HarmonyPatch]
public static class CreatureFactoryPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(WorldObjectFactory))
            .Where(method => method.Name == nameof(WorldObjectFactory.CreateWorldObject) &&
                method.ReturnType == typeof(WorldObject));

    [HarmonyPostfix]
    public static void AssignVariant(WorldObject? __result) => CreatureVariantRuntime.TryAssign(__result);
}

[HarmonyPatch]
public static class CreatureVariantEvadePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DamageEvent),
        nameof(DamageEvent.GetEvadeChance), new[] { typeof(Creature), typeof(Creature) })
        ?? throw new MissingMethodException(typeof(DamageEvent).FullName, nameof(DamageEvent.GetEvadeChance));

    [HarmonyPrefix]
    public static bool ApplyFlatChances(Creature attacker, Creature defender, ref float __result)
    {
        var settings = Mod.Settings.Source;
        if (!settings.Enabled)
            return true;

        if (settings.BossAttacksCannotBeEvaded &&
            CreatureVariantRuntime.TryGet(attacker, out var bossState) &&
            bossState.HasTrait(CreatureVariantType.Boss))
        {
            __result = 0.0f;
            return false;
        }

        if (CreatureVariantRuntime.TryGet(attacker, out var attackerState) &&
            attackerState.HasTrait(CreatureVariantType.Accurate) &&
            ThreadSafeRandom.Next(0.0f, 1.0f) < settings.AccurateAutoHitChance)
        {
            __result = 0.0f;
            if (defender is Player player)
                player.SendMessage($"{attacker.Name} skillfully connects.");
            return false;
        }

        if (CreatureVariantRuntime.TryGet(defender, out var defenderState) &&
            defenderState.HasTrait(CreatureVariantType.Evader) &&
            ThreadSafeRandom.Next(0.0f, 1.0f) < settings.EvaderAutoEvadeChance)
        {
            __result = 1.0f;
            if (attacker is Player player)
                player.SendMessage($"{defender.Name} skillfully dodges.");
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
public static class CreatureVariantDamagePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DamageEvent), "DoCalculateDamage",
        new[] { typeof(Creature), typeof(Creature), typeof(WorldObject) })
        ?? throw new MissingMethodException(typeof(DamageEvent).FullName, "DoCalculateDamage");

    [HarmonyPostfix]
    public static void ApplyAttackTrait(
        DamageEvent __instance,
        Creature attacker,
        Creature defender,
        ref float __result)
    {
        var settings = Mod.Settings.Source;
        if (!settings.Enabled || __instance.Damage <= 0.0f ||
            !CreatureVariantRuntime.TryGet(attacker, out var state))
            return;

        if (state.HasTrait(CreatureVariantType.Comboer))
            ApplyCombo(__instance, state, defender, settings, ref __result);
        if (state.HasTrait(CreatureVariantType.Drainer))
            ApplyDrain(__instance, state, attacker, defender, settings);
        if (state.HasTrait(CreatureVariantType.SpellBreaker))
            ApplySpellBreak(state, attacker, defender, settings);
        if (state.HasTrait(CreatureVariantType.Stomper))
            ApplyStomp(__instance, attacker, defender, settings);
        if (state.HasTrait(CreatureVariantType.Vampire))
            ApplyVampire(__instance, attacker, defender, settings);
        if (state.HasTrait(CreatureVariantType.Rogue))
            ApplyRogue(__instance, state, attacker, defender, settings);
        if (state.HasTrait(CreatureVariantType.Horde))
            ScaleDamage(__instance,
                CreatureVariantRuntime.GetHordeDamageMultiplier(
                    CreatureVariantRuntime.GetHordeLivingMembers(attacker, state), settings),
                ref __result);
        if (state.HasTrait(CreatureVariantType.Boss))
            ScaleDamage(__instance, settings.BossDamageMultiplier, ref __result);
    }

    private static void ApplyCombo(
        DamageEvent damageEvent,
        CreatureVariantState state,
        Creature defender,
        CreatureVariantSettings settings,
        ref float result)
    {
        if (defender is not Player player)
            return;

        state.ComboHits++;
        if (state.ComboHits < settings.ComboHitThreshold)
            return;

        state.ComboHits = 0;
        var original = damageEvent.Damage;
        var boosted = (float)Math.Clamp(original * settings.ComboDamageMultiplier, 0.0, float.MaxValue);
        damageEvent.Damage = boosted;
        damageEvent.DamageMitigated = damageEvent.DamageBeforeMitigation - boosted;
        result = boosted;
        player.SendMessage($"{damageEvent.Attacker.Name} completes a combo for {boosted - original:0} bonus damage.");
    }

    private static void ApplyDrain(
        DamageEvent damageEvent,
        CreatureVariantState state,
        Creature attacker,
        Creature defender,
        CreatureVariantSettings settings)
    {
        if (defender is not Player player)
            return;

        var requested = (int)Math.Clamp(
            Math.Round(damageEvent.Damage * settings.DrainDamageFraction, MidpointRounding.AwayFromZero),
            0.0,
            int.MaxValue);
        if (requested <= 0)
            return;

        var vital = state.DrainMana ? player.Mana : player.Stamina;
        var drained = -player.UpdateVitalDelta(vital, -requested);
        if (drained > 0)
            player.SendMessage($"{attacker.Name} drains {drained} {(state.DrainMana ? "mana" : "stamina")}.");
    }

    private static void ApplySpellBreak(
        CreatureVariantState state,
        Creature attacker,
        Creature defender,
        CreatureVariantSettings settings)
    {
        if (defender is not Player player || player.CombatMode != CombatMode.Magic ||
            !player.MagicState.IsCasting || player.MagicState.CastSpellParams is null)
            return;

        var manaUsed = player.MagicState.CastSpellParams.ManaUsed;
        var damage = (float)Math.Clamp(
            manaUsed * settings.SpellBreakerManaDamageFraction,
            0.0,
            settings.SpellBreakerMaximumDamage);
        player.FailCast(true);
        var taken = damage > 0.0f ? player.TakeDamage(attacker, DamageType.Fire, damage) : 0;
        player.SendMessage($"{attacker.Name} breaks your spell and burns you for {taken} damage.");
    }

    private static void ApplyStomp(
        DamageEvent damageEvent,
        Creature attacker,
        Creature defender,
        CreatureVariantSettings settings)
    {
        if (defender is not Player primary)
            return;

        var targets = CreatureVariantRuntime.GetNearbyCreatures(attacker, settings.StomperRange)
            .OfType<Player>()
            .Where(player => !ReferenceEquals(player, primary))
            .Take(settings.StomperMaximumTargets);

        foreach (var player in targets)
        {
            var distance = attacker.GetDistance(player);
            var falloff = Math.Clamp(1.0 - distance / settings.StomperRange, 0.0, 1.0);
            var splash = (float)(damageEvent.Damage * settings.StomperMaximumSplashFraction * falloff);
            if (splash <= 0.0f)
                continue;

            var taken = player.TakeDamage(attacker, damageEvent.DamageType, splash);
            if (taken > 0)
                player.SendMessage($"{attacker.Name}'s stomp splashes you for {taken} damage.");
        }
    }

    private static void ApplyVampire(
        DamageEvent damageEvent,
        Creature attacker,
        Creature defender,
        CreatureVariantSettings settings)
    {
        if (defender is not Player player)
            return;

        var requested = (int)Math.Clamp(
            Math.Round(damageEvent.Damage * settings.VampireLeechFraction, MidpointRounding.AwayFromZero),
            0.0,
            int.MaxValue);
        var healed = requested > 0 ? attacker.UpdateVitalDelta(attacker.Health, requested) : 0;
        if (healed > 0)
            player.SendMessage($"{attacker.Name} leeches {healed} health.");
    }

    private static void ApplyRogue(
        DamageEvent damageEvent,
        CreatureVariantState state,
        Creature attacker,
        Creature defender,
        CreatureVariantSettings settings)
    {
        if (defender is not Player player || Math.Abs(player.GetAngle(attacker)) <= 90.0f ||
            Timers.RunningTime < state.NextRogueProcTime ||
            ThreadSafeRandom.Next(0.0f, 1.0f) >= settings.RogueFumbleChance)
            return;

        state.NextRogueProcTime = Timers.RunningTime + settings.RogueCooldownSeconds;
        if (player.Attacking || player.AttackTarget is not null)
            player.OnAttackDone();
        if (player.MagicState.IsCasting)
            player.FailCast(false);

        var drained = settings.RogueStaminaDrain > 0
            ? -player.UpdateVitalDelta(player.Stamina, -settings.RogueStaminaDrain)
            : 0;
        player.SendMessage(drained > 0
            ? $"{attacker.Name} catches you from behind, interrupts your action, and drains {drained} stamina."
            : $"{attacker.Name} catches you from behind and interrupts your action.");
    }

    private static void ScaleDamage(DamageEvent damageEvent, double multiplier, ref float result)
    {
        if (multiplier == 1.0)
            return;

        var scaled = (float)Math.Clamp(damageEvent.Damage * multiplier, 0.0, float.MaxValue);
        damageEvent.Damage = scaled;
        damageEvent.DamageMitigated = damageEvent.DamageBeforeMitigation - scaled;
        result = scaled;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.TakeDamage),
    new[] { typeof(WorldObject), typeof(DamageType), typeof(float), typeof(bool) })]
public static class CreatureVariantTakeDamagePatch
{
    [HarmonyPrefix]
    public static bool ApplyDefenseTrait(
        Creature __instance,
        WorldObject source,
        DamageType damageType,
        ref float amount,
        bool crit,
        ref uint __result)
    {
        var settings = Mod.Settings.Source;
        if (!settings.Enabled || amount <= 0.0f)
            return true;

        var hasState = CreatureVariantRuntime.TryGet(__instance, out var state);
        if (source is Player playerSource && __instance is not Player && __instance.Attackable &&
            (!hasState || !state.HasTrait(CreatureVariantType.Tank)))
        {
            var tank = CreatureVariantRuntime.GetNearbyCreatures(__instance, settings.TankProtectionRange)
                .Where(candidate => CreatureVariantRuntime.TryGet(candidate, out var candidateState) &&
                    candidateState.HasTrait(CreatureVariantType.Tank))
                .OrderBy(candidate => __instance.GetDistance(candidate))
                .FirstOrDefault();
            if (tank is not null)
            {
                var redirected = (float)Math.Clamp(amount * settings.TankRedirectDamageMultiplier,
                    0.0, float.MaxValue);
                var taken = tank.TakeDamage(source, damageType, redirected, crit);
                playerSource.SendMessage($"{tank.Name} intercepts the attack and takes {taken} damage for {__instance.Name}.");
                __result = 0;
                return false;
            }
        }

        if (!hasState)
            return true;

        if (state.HasTrait(CreatureVariantType.Boss))
        {
            var weaknessHit = (state.BossWeakness & damageType) != 0;
            var multiplier = weaknessHit
                ? settings.BossWeaknessDamageMultiplier
                : settings.BossOtherDamageMultiplier;
            amount = (float)Math.Clamp(amount * multiplier, 0.0, float.MaxValue);
        }

        if (state.HasTrait(CreatureVariantType.Comboer))
            state.ComboHits = 0;

        if (state.HasTrait(CreatureVariantType.Shielded) && state.Shields > 0)
        {
            state.Shields--;
            __result = 0;
            if (source is Player player)
                player.SendMessage($"{__instance.Name} blocks the hit with a shield ({state.Shields} remaining).");
            return false;
        }

        if (state.HasTrait(CreatureVariantType.Duelist) && source is Player duelistOpponent &&
            __instance.Location is not null && source.Location is not null &&
            Math.Abs(__instance.GetAngle(duelistOpponent)) <= Mod.Settings.Source.DuelistFrontArcDegrees / 2.0)
        {
            __result = 0;
            duelistOpponent.SendMessage($"{__instance.Name} parries the attack by facing you.");
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    public static void CleanupPuppetsOnDeath(Creature __instance)
    {
        if (__instance.IsDead && CreatureVariantRuntime.TryGet(__instance, out var state) &&
            state.HasTrait(CreatureVariantType.Puppeteer))
            PuppetCopyRuntime.DestroyForOwner(__instance);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.Heartbeat), new[] { typeof(double) })]
public static class CreatureVariantHeartbeatPatch
{
    [HarmonyPostfix]
    public static void RunTimedTrait(Creature __instance, double currentUnixTime)
    {
        if (__instance is Player stunnedPlayer)
        {
            PlayerStunRuntime.ProcessHeartbeat(stunnedPlayer, currentUnixTime);
            return;
        }

        if (PuppetCopyRuntime.ProcessHeartbeat(__instance, currentUnixTime))
            return;

        var settings = Mod.Settings.Source;
        if (!settings.Enabled || __instance.IsDead ||
            !CreatureVariantRuntime.TryGet(__instance, out var state))
            return;

        if (state.HasTrait(CreatureVariantType.Berserker) &&
            !state.BerserkerEnraged && __instance.Health.Percent <= settings.BerserkerHealthThreshold)
        {
            state.BerserkerEnraged = true;
            if (__instance.AttackTarget is Player player)
                player.SendMessage($"{__instance.Name} becomes enraged.");
        }

        if (state.HasTrait(CreatureVariantType.Exploder))
            RunExploder(__instance, state, settings);
        if (__instance.IsDead)
            return;
        if (state.HasTrait(CreatureVariantType.Healer))
            RunHealer(__instance, state, settings, currentUnixTime);
        if (state.HasTrait(CreatureVariantType.Shielded))
            RunShieldRecharge(state, settings, currentUnixTime);
        if (state.HasTrait(CreatureVariantType.Puppeteer))
            RunPuppeteer(__instance, state, settings, currentUnixTime);
        if (state.HasTrait(CreatureVariantType.Boss))
            RunBoss(__instance, state, settings, currentUnixTime);
        if (state.HasTrait(CreatureVariantType.Stunner))
            RunStunner(__instance, state, settings, currentUnixTime);
    }

    private static void RunExploder(
        Creature creature,
        CreatureVariantState state,
        CreatureVariantSettings settings)
    {
        if (state.Exploded || creature.AttackTarget is not Player target || target.Location is null ||
            creature.Location is null || creature.GetDistance(target) > settings.ExploderTriggerRange)
        {
            state.ExploderTicksRemaining = settings.ExploderCountdownHeartbeats;
            return;
        }

        state.ExploderTicksRemaining--;
        if (state.ExploderTicksRemaining > 0)
        {
            target.SendMessage($"{creature.Name}: {new string('.', state.ExploderTicksRemaining)}");
            return;
        }

        state.Exploded = true;
        var damage = (float)(settings.ExploderMaximumDamage * creature.Health.Percent);
        foreach (var player in CreatureVariantRuntime.GetNearbyCreatures(creature, settings.ExploderTriggerRange)
                     .OfType<Player>().Take(10))
        {
            var taken = player.TakeDamage(creature, DamageType.Fire, damage, true);
            if (taken > 0)
                player.SendMessage($"{creature.Name} explodes for {taken} fire damage.");
        }

        creature.TakeDamage(creature, DamageType.Fire, creature.Health.Current, true);
    }

    private static void RunHealer(
        Creature creature,
        CreatureVariantState state,
        CreatureVariantSettings settings,
        double currentUnixTime)
    {
        if (creature.AttackTarget is not Player player)
            return;
        if (state.NextHealTime <= 0.0)
            state.NextHealTime = currentUnixTime + settings.HealerIntervalSeconds;
        if (currentUnixTime < state.NextHealTime)
            return;

        state.NextHealTime = currentUnixTime + settings.HealerIntervalSeconds;
        foreach (var ally in CreatureVariantRuntime.GetNearbyCreatures(creature, settings.HealerRange)
                     .Where(candidate => candidate.GetType() == typeof(Creature) && candidate.Attackable &&
                         candidate.Health.Percent < 0.95f)
                     .OrderBy(candidate => candidate.Health.Percent)
                     .Take(settings.HealerMaximumTargets))
        {
            var requested = (int)Math.Round(ally.Health.MaxValue * settings.HealerHealthFraction,
                MidpointRounding.AwayFromZero);
            var healed = ally.UpdateVitalDelta(ally.Health, requested);
            if (healed > 0)
                player.SendMessage($"{creature.Name} heals {ally.Name} for {healed} health.");
        }
    }

    private static void RunShieldRecharge(
        CreatureVariantState state,
        CreatureVariantSettings settings,
        double currentUnixTime)
    {
        if (state.Shields >= settings.ShieldCount)
        {
            state.Shields = settings.ShieldCount;
            state.NextShieldRecharge = currentUnixTime + settings.ShieldRechargeSeconds;
            return;
        }

        if (state.NextShieldRecharge <= 0.0)
            state.NextShieldRecharge = currentUnixTime + settings.ShieldRechargeSeconds;
        if (currentUnixTime < state.NextShieldRecharge)
            return;

        state.Shields = settings.ShieldCount;
        state.NextShieldRecharge = currentUnixTime + settings.ShieldRechargeSeconds;
    }

    private static void RunPuppeteer(
        Creature owner,
        CreatureVariantState state,
        CreatureVariantSettings settings,
        double currentUnixTime)
    {
        state.PuppetCopies.RemoveAll(copy => copy.IsDestroyed);
        if (settings.PuppeteerMaximumCopies == 0 || owner.AttackTarget is not Player || owner.Location is null)
            return;

        if (state.NextPuppetSpawnTime <= 0.0)
            state.NextPuppetSpawnTime = currentUnixTime + settings.PuppeteerSpawnIntervalSeconds;
        if (currentUnixTime < state.NextPuppetSpawnTime ||
            state.PuppetCopies.Count >= settings.PuppeteerMaximumCopies)
            return;

        state.NextPuppetSpawnTime = currentUnixTime + settings.PuppeteerSpawnIntervalSeconds;
        var weenie = DatabaseManager.World.GetCachedWeenie(owner.WeenieClassId);
        if (weenie is null)
            return;

        var copy = new Creature(weenie, GuidManager.NewDynamicGuid())
        {
            Name = $"Illusion of {state.OriginalName}",
            Attackable = false,
            Invincible = true,
            NeverAttack = true,
            XpOverride = 0,
            DeathTreasureType = null
        };
        copy.GeneratorProfiles?.Clear();
        copy.CurrentTargetingTactic = TargetingTactic.None;

        var position = new Position(owner.Location);
        var angle = ThreadSafeRandom.Next(0.0f, (float)(Math.PI * 2.0));
        var radius = ThreadSafeRandom.Next(0.5f, (float)settings.PuppeteerSpawnRadius);
        position.Pos += new Vector3((float)Math.Cos(angle) * (float)radius,
            (float)Math.Sin(angle) * (float)radius, 0.05f);
        copy.Location = position;

        if (!copy.EnterWorld())
        {
            copy.Destroy(false);
            return;
        }

        state.PuppetCopies.Add(copy);
        PuppetCopyRuntime.Register(copy, owner, currentUnixTime + settings.PuppeteerCopyLifetimeSeconds);
        if (owner.AttackTarget is Player player)
            player.SendMessage($"{owner.Name} creates an untouchable illusion.");
    }

    private static void RunBoss(
        Creature boss,
        CreatureVariantState state,
        CreatureVariantSettings settings,
        double currentUnixTime)
    {
        if (state.NextBossWeaknessTime <= 0.0)
        {
            state.NextBossWeaknessTime = currentUnixTime + settings.BossWeaknessIntervalSeconds;
            if (boss.AttackTarget is Player initialTarget)
                initialTarget.SendMessage($"{boss.Name} is weak to {state.BossWeakness} damage.");
        }
        if (currentUnixTime >= state.NextBossWeaknessTime)
        {
            var previous = state.BossWeakness;
            for (var attempt = 0; attempt < 4 && state.BossWeakness == previous; attempt++)
                state.BossWeakness = CreatureVariantRuntime.RollBossWeakness();
            state.NextBossWeaknessTime = currentUnixTime + settings.BossWeaknessIntervalSeconds;
            if (boss.AttackTarget is Player target)
                target.SendMessage($"{boss.Name} is now weak to {state.BossWeakness} damage.");
        }

        if (settings.BossSpellIds.Count == 0 || boss.AttackTarget is not Player player ||
            boss.Location is null || player.Location is null || boss.GetDistance(player) > settings.BossSpecialSpellRange)
            return;

        if (state.NextBossSpellTime <= 0.0)
            state.NextBossSpellTime = currentUnixTime + settings.BossSpecialSpellIntervalSeconds;
        if (currentUnixTime < state.NextBossSpellTime)
            return;

        state.NextBossSpellTime = currentUnixTime + settings.BossSpecialSpellIntervalSeconds;
        var spellId = settings.BossSpellIds[ThreadSafeRandom.Next(0, settings.BossSpellIds.Count - 1)];
        var spell = new Spell(spellId);
        if (!spell.NotFound)
        {
            player.SendMessage($"{boss.Name} prepares a special spell.");
            boss.TryCastSpell_WithRedirects(spell, player);
        }
    }

    private static void RunStunner(
        Creature stunner,
        CreatureVariantState state,
        CreatureVariantSettings settings,
        double currentUnixTime)
    {
        if (state.NextStunTime <= 0.0)
            state.NextStunTime = currentUnixTime + settings.StunnerIntervalSeconds;
        if (currentUnixTime < state.NextStunTime || stunner.AttackTarget is not Player player ||
            stunner.Location is null || player.Location is null || stunner.GetDistance(player) > settings.StunnerRange)
            return;

        state.NextStunTime = currentUnixTime + settings.StunnerIntervalSeconds;
        PlayerStunRuntime.Apply(player, currentUnixTime, settings.StunnerDurationSeconds, stunner.Name);
    }
}

[HarmonyPatch]
public static class BerserkerAttributePatch
{
    private static readonly AccessTools.FieldRef<CreatureAttribute, Creature> CreatureField =
        AccessTools.FieldRefAccess<CreatureAttribute, Creature>("creature");

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureAttribute),
        nameof(CreatureAttribute.GetCurrent), new[] { typeof(bool) })
        ?? throw new MissingMethodException(typeof(CreatureAttribute).FullName, nameof(CreatureAttribute.GetCurrent));

    [HarmonyPostfix]
    public static void ScaleEnragedAttribute(CreatureAttribute __instance, ref uint __result)
    {
        var creature = CreatureField(__instance);
        if (!Mod.Settings.Source.Enabled ||
            !CreatureVariantRuntime.TryGet(creature, out var state))
            return;

        var multiplier = 1.0;
        if (state.HasTrait(CreatureVariantType.Berserker) && state.BerserkerEnraged)
            multiplier *= Mod.Settings.Source.BerserkerAttributeMultiplier;
        if (state.HasTrait(CreatureVariantType.Boss))
            multiplier *= Mod.Settings.Source.BossAttributeMultiplier;
        if (multiplier == 1.0)
            return;

        __result = (uint)Math.Clamp(
            Math.Round(__result * multiplier, MidpointRounding.AwayFromZero),
            0.0,
            uint.MaxValue);
    }
}

[HarmonyPatch]
public static class CreatureVariantVitalPatch
{
    private static readonly AccessTools.FieldRef<CreatureVital, Creature> CreatureField =
        AccessTools.FieldRefAccess<CreatureVital, Creature>("creature");

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureVital),
        nameof(CreatureVital.GetMaxValue), new[] { typeof(bool) })
        ?? throw new MissingMethodException(typeof(CreatureVital).FullName, nameof(CreatureVital.GetMaxValue));

    [HarmonyPostfix]
    public static void ScaleVariantVital(CreatureVital __instance, ref uint __result)
    {
        var creature = CreatureField(__instance);
        if (!Mod.Settings.Source.Enabled ||
            !CreatureVariantRuntime.TryGet(creature, out var state))
            return;

        var multiplier = 1.0;
        if (state.HasTrait(CreatureVariantType.Boss))
            multiplier *= Mod.Settings.Source.BossVitalMultiplier;
        if (state.HasTrait(CreatureVariantType.Horde) &&
            __instance.Vital == PropertyAttribute2nd.MaxHealth)
            multiplier *= 1.0 + Math.Max(0, state.HordeInitialMembers - 1) *
                Mod.Settings.Source.HordeHealthPerAdditionalMember;
        if (multiplier == 1.0)
            return;

        __result = (uint)Math.Clamp(
            Math.Round(__result * multiplier, MidpointRounding.AwayFromZero),
            1.0,
            uint.MaxValue);
    }
}

[HarmonyPatch]
public static class BossScalePatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.ObjScale))
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, $"get_{nameof(WorldObject.ObjScale)}");

    [HarmonyPostfix]
    public static void ScaleBossModel(WorldObject __instance, ref float? __result)
    {
        if (!Mod.Settings.Source.Enabled || __instance is not Creature creature ||
            !CreatureVariantRuntime.TryGet(creature, out var state) ||
            !state.HasTrait(CreatureVariantType.Boss))
            return;

        __result = (__result ?? 1.0f) * (float)Mod.Settings.Source.BossScaleMultiplier;
    }
}

[HarmonyPatch]
public static class BossExperiencePatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.XpOverride))
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, $"get_{nameof(WorldObject.XpOverride)}");

    [HarmonyPostfix]
    public static void ScaleBossExperience(WorldObject __instance, ref int? __result)
    {
        if (__result is null || !Mod.Settings.Source.Enabled || __instance is not Creature creature ||
            !CreatureVariantRuntime.TryGet(creature, out var state) ||
            !state.HasTrait(CreatureVariantType.Boss))
            return;

        __result = (int)Math.Clamp(
            Math.Round(__result.Value * Mod.Settings.Source.BossExperienceMultiplier,
                MidpointRounding.AwayFromZero),
            0.0,
            int.MaxValue);
    }
}

[HarmonyPatch]
public static class BossSpellResistancePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(WorldObject),
        nameof(WorldObject.TryResistSpell),
        new[] { typeof(WorldObject), typeof(Spell), typeof(WorldObject), typeof(bool) })
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, nameof(WorldObject.TryResistSpell));

    [HarmonyPrefix]
    public static bool MakeBossSpellUnresistable(WorldObject __instance, ref bool __result)
    {
        if (!Mod.Settings.Source.Enabled || !Mod.Settings.Source.BossSpellsCannotBeResisted ||
            __instance is not Creature creature || !CreatureVariantRuntime.TryGet(creature, out var state) ||
            !state.HasTrait(CreatureVariantType.Boss))
            return true;

        __result = false;
        return false;
    }
}
