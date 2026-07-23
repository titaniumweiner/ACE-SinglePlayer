using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using ACE.Entity.Enum;
using ACE.Server.Command;
using ACE.Server.Entity;
using ACE.Server.Mods;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

using HarmonyLib;

namespace LandblockSummonBalance;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.LandblockSummonBalance";
    private const string CommandName = "summonbalance";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Harmony harmony = new(HarmonyId);
    private bool initialized;
    private bool commandRegistered;

    public static CompiledLandblockSettings Settings { get; private set; } = CompiledLandblockSettings.Empty;

    public static string SettingsPath { get; private set; } = string.Empty;

    public void Initialize()
    {
        if (initialized)
            return;

        var assemblyFolder = Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? throw new InvalidOperationException("The Landblock Summon Balance assembly folder could not be determined.");
        SettingsPath = Path.Combine(assemblyFolder, "Settings.json");
        Settings = LoadSettings(SettingsPath);

        harmony.PatchAll(typeof(Mod).Assembly);
        commandRegistered = CommandManager.TryAddCommand(
            HandleCommand,
            CommandName,
            AccessLevel.Developer,
            CommandHandlerFlag.None,
            "Shows, reloads, or inspects Landblock Summon Balance settings.",
            "where | status | reload",
            overrides: false);
        initialized = true;

        Console.WriteLine($"[Landblock Summon Balance] Enabled with {Settings.EnabledZones.Count} configured landblock zone(s). " +
            $"@{CommandName} where reports the current landblock ID.");
    }

    public void Dispose()
    {
        if (commandRegistered)
            CommandManager.TryRemoveCommand(CommandName);

        harmony.UnpatchAll(HarmonyId);
        Settings = CompiledLandblockSettings.Empty;
        SettingsPath = string.Empty;
        commandRegistered = false;
        initialized = false;
    }

    public static CompiledLandblockSettings LoadSettings(string path)
    {
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<LandblockSummonSettings>(File.ReadAllText(path), JsonOptions)
                ?? new LandblockSummonSettings()
            : new LandblockSummonSettings();

        return settings.Compile();
    }

    private static void ReloadSettings()
    {
        if (string.IsNullOrWhiteSpace(SettingsPath))
            throw new InvalidOperationException("The mod has not been initialized.");

        // Compile fully before swapping the shared reference. An invalid edit therefore leaves
        // the last known-good configuration active.
        var replacement = LoadSettings(SettingsPath);
        Settings = replacement;
    }

    private static void HandleCommand(Session session, string[] parameters)
    {
        if (session.Player is null)
            return;

        var action = parameters.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "status";
        switch (action)
        {
            case "where":
            case "location":
                var cell = session.Player.Location.LandblockId.Raw;
                var landblock = (ushort)(cell >> 16);
                var zone = Settings.Resolve(cell, cell);
                session.Player.SendMessage($"Landblock: 0x{landblock:X4}; full cell: 0x{cell:X8}; " +
                    $"matching zone: {zone?.Name ?? "none"}.");
                break;

            case "status":
                var names = Settings.EnabledZones.Count == 0
                    ? "none"
                    : string.Join(", ", Settings.EnabledZones.Select(zone => zone.Name));
                session.Player.SendMessage($"Landblock Summon Balance has {Settings.EnabledZones.Count} enabled zone(s): {names}. " +
                    $"Settings: {SettingsPath}");
                break;

            case "reload":
                try
                {
                    ReloadSettings();
                    session.Player.SendMessage($"Landblock Summon Balance reloaded {Settings.EnabledZones.Count} enabled zone(s).");
                }
                catch (Exception exception)
                {
                    session.Player.SendMessage($"Settings were not reloaded: {exception.Message}");
                }
                break;

            default:
                session.Player.SendMessage($"Usage: @{CommandName} where | status | reload");
                break;
        }
    }
}

public sealed class LandblockSummonSettings
{
    public List<SummonBalanceZone> Zones { get; set; } = new();

    public CompiledLandblockSettings Compile()
    {
        if (Zones is null)
            throw new InvalidDataException("Zones cannot be null.");

        var duplicateName = Zones
            .GroupBy(zone => zone?.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
            throw new InvalidDataException($"Zone names must be unique. Duplicate: '{duplicateName.Key}'.");

        var compiled = Zones.Select((zone, index) =>
        {
            if (zone is null)
                throw new InvalidDataException($"Zone {index + 1} cannot be null.");
            return zone.Compile(index);
        }).ToArray();

        return new CompiledLandblockSettings(compiled);
    }
}

public sealed class SummonBalanceZone
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int Priority { get; set; }

    public ZoneMatchLocation MatchLocation { get; set; } = ZoneMatchLocation.Opponent;

    public List<string> Landblocks { get; set; } = new();

    public List<string> ExactCells { get; set; } = new();

    public double PhysicalDamageMultiplier { get; set; } = 1.0;

    public double SpellDamageMultiplier { get; set; } = 1.0;

    public double PhysicalAttackSkillMultiplier { get; set; } = 1.0;

    public double PhysicalDefenseSkillMultiplier { get; set; } = 1.0;

    public double DamageTakenMultiplier { get; set; } = 1.0;

    internal CompiledSummonBalanceZone Compile(int order)
    {
        Name = Name?.Trim() ?? string.Empty;
        if (Name.Length == 0)
            throw new InvalidDataException($"Zone {order + 1} must have a name.");
        if (Priority is < -100000 or > 100000)
            throw new InvalidDataException($"Zone '{Name}' Priority must be between -100000 and 100000.");

        Landblocks ??= new List<string>();
        ExactCells ??= new List<string>();

        ValidateMultiplier(PhysicalDamageMultiplier, nameof(PhysicalDamageMultiplier));
        ValidateMultiplier(SpellDamageMultiplier, nameof(SpellDamageMultiplier));
        ValidateMultiplier(PhysicalAttackSkillMultiplier, nameof(PhysicalAttackSkillMultiplier));
        ValidateMultiplier(PhysicalDefenseSkillMultiplier, nameof(PhysicalDefenseSkillMultiplier));
        ValidateMultiplier(DamageTakenMultiplier, nameof(DamageTakenMultiplier));

        var landblocks = Landblocks.Select(value => ParseHex(value, 4, "landblock", Name))
            .Select(value => checked((ushort)value)).ToHashSet();
        var exactCells = ExactCells.Select(value => ParseHex(value, 8, "full cell", Name)).ToHashSet();

        if (Enabled && landblocks.Count == 0 && exactCells.Count == 0)
            throw new InvalidDataException($"Enabled zone '{Name}' must contain at least one Landblocks or ExactCells entry.");

        return new CompiledSummonBalanceZone(
            Name, Enabled, Priority, order, MatchLocation, landblocks, exactCells,
            PhysicalDamageMultiplier, SpellDamageMultiplier, PhysicalAttackSkillMultiplier,
            PhysicalDefenseSkillMultiplier, DamageTakenMultiplier);
    }

    private static uint ParseHex(string? value, int digits, string kind, string zoneName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (normalized.Length != digits ||
            !uint.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException(
                $"Zone '{zoneName}' has invalid {kind} '{value}'. Use exactly {digits} hexadecimal digits, such as " +
                (digits == 4 ? "0xA9B4." : "0xA9B4012F."));
        }

        return parsed;
    }

    private void ValidateMultiplier(double value, string setting)
    {
        if (!double.IsFinite(value) || value is < 0.0 or > 10.0)
            throw new InvalidDataException($"Zone '{Name}' {setting} must be between 0 and 10.");
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ZoneMatchLocation
{
    Opponent,
    Summon,
    Either
}

public sealed class CompiledLandblockSettings
{
    public static CompiledLandblockSettings Empty { get; } = new(Array.Empty<CompiledSummonBalanceZone>());

    public CompiledLandblockSettings(IReadOnlyList<CompiledSummonBalanceZone> zones)
    {
        Zones = zones;
        EnabledZones = zones.Where(zone => zone.Enabled).ToArray();
    }

    public IReadOnlyList<CompiledSummonBalanceZone> Zones { get; }

    public IReadOnlyList<CompiledSummonBalanceZone> EnabledZones { get; }

    public CompiledSummonBalanceZone? Resolve(uint summonCell, uint opponentCell)
    {
        CompiledSummonBalanceZone? best = null;
        var bestSpecificity = 0;

        foreach (var zone in EnabledZones)
        {
            var specificity = zone.MatchSpecificity(summonCell, opponentCell);
            if (specificity == 0)
                continue;

            if (best is null || specificity > bestSpecificity ||
                specificity == bestSpecificity && zone.Priority > best.Priority)
            {
                best = zone;
                bestSpecificity = specificity;
            }
        }

        return best;
    }
}

public sealed class CompiledSummonBalanceZone
{
    internal CompiledSummonBalanceZone(
        string name,
        bool enabled,
        int priority,
        int order,
        ZoneMatchLocation matchLocation,
        IReadOnlySet<ushort> landblocks,
        IReadOnlySet<uint> exactCells,
        double physicalDamageMultiplier,
        double spellDamageMultiplier,
        double physicalAttackSkillMultiplier,
        double physicalDefenseSkillMultiplier,
        double damageTakenMultiplier)
    {
        Name = name;
        Enabled = enabled;
        Priority = priority;
        Order = order;
        MatchLocation = matchLocation;
        Landblocks = landblocks;
        ExactCells = exactCells;
        PhysicalDamageMultiplier = physicalDamageMultiplier;
        SpellDamageMultiplier = spellDamageMultiplier;
        PhysicalAttackSkillMultiplier = physicalAttackSkillMultiplier;
        PhysicalDefenseSkillMultiplier = physicalDefenseSkillMultiplier;
        DamageTakenMultiplier = damageTakenMultiplier;
    }

    public string Name { get; }
    public bool Enabled { get; }
    public int Priority { get; }
    public int Order { get; }
    public ZoneMatchLocation MatchLocation { get; }
    public IReadOnlySet<ushort> Landblocks { get; }
    public IReadOnlySet<uint> ExactCells { get; }
    public double PhysicalDamageMultiplier { get; }
    public double SpellDamageMultiplier { get; }
    public double PhysicalAttackSkillMultiplier { get; }
    public double PhysicalDefenseSkillMultiplier { get; }
    public double DamageTakenMultiplier { get; }

    internal int MatchSpecificity(uint summonCell, uint opponentCell) => MatchLocation switch
    {
        ZoneMatchLocation.Summon => MatchCell(summonCell),
        ZoneMatchLocation.Opponent => MatchCell(opponentCell),
        ZoneMatchLocation.Either => Math.Max(MatchCell(summonCell), MatchCell(opponentCell)),
        _ => 0
    };

    private int MatchCell(uint cell)
    {
        if (ExactCells.Contains(cell))
            return 2;
        return Landblocks.Contains((ushort)(cell >> 16)) ? 1 : 0;
    }
}

public static class SummonBalanceResolver
{
    public static bool IsPlayerOwnedCombatSummon(Creature creature) =>
        creature is CombatPet pet && pet.P_PetOwner is not null;

    public static CompiledSummonBalanceZone? ResolveFor(
        Creature summon,
        Creature opponent,
        CompiledLandblockSettings settings)
    {
        var summonLocation = summon.Location;
        var opponentLocation = opponent.Location;
        if (summonLocation is null || opponentLocation is null)
            return null;

        return settings.Resolve(summonLocation.LandblockId.Raw, opponentLocation.LandblockId.Raw);
    }

    public static float ScaleDamage(float damage, double multiplier)
    {
        if (damage <= 0.0f || multiplier == 1.0)
            return damage;

        var scaled = damage * multiplier;
        return (float)Math.Clamp(scaled, 0.0, float.MaxValue);
    }

    public static uint ScaleSkill(uint skill, double multiplier)
    {
        if (multiplier == 1.0)
            return skill;

        var scaled = Math.Round(skill * multiplier, MidpointRounding.AwayFromZero);
        return (uint)Math.Clamp(scaled, 0.0, uint.MaxValue);
    }
}

[HarmonyPatch]
public static class PhysicalDamagePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DamageEvent), "DoCalculateDamage",
        new[] { typeof(Creature), typeof(Creature), typeof(WorldObject) })
        ?? throw new MissingMethodException(typeof(DamageEvent).FullName, "DoCalculateDamage");

    [HarmonyPostfix]
    public static void ApplyLandblockMultipliers(
        DamageEvent __instance,
        Creature attacker,
        Creature defender,
        ref float __result)
    {
        var multiplier = 1.0;
        var settings = Mod.Settings;

        if (SummonBalanceResolver.IsPlayerOwnedCombatSummon(attacker))
        {
            var zone = SummonBalanceResolver.ResolveFor(attacker, defender, settings);
            multiplier *= zone?.PhysicalDamageMultiplier ?? 1.0;
        }

        if (SummonBalanceResolver.IsPlayerOwnedCombatSummon(defender))
        {
            var zone = SummonBalanceResolver.ResolveFor(defender, attacker, settings);
            multiplier *= zone?.DamageTakenMultiplier ?? 1.0;
        }

        if (multiplier == 1.0 || __result <= 0.0f)
            return;

        __result = SummonBalanceResolver.ScaleDamage(__result, multiplier);
        __instance.Damage = __result;
        __instance.DamageMitigated = __instance.DamageBeforeMitigation - __instance.Damage;
    }
}

[HarmonyPatch]
public static class PhysicalSkillPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(DamageEvent),
        nameof(DamageEvent.GetEvadeChance), new[] { typeof(Creature), typeof(Creature) })
        ?? throw new MissingMethodException(typeof(DamageEvent).FullName, nameof(DamageEvent.GetEvadeChance));

    [HarmonyPostfix]
    public static void ApplyLandblockSkills(
        DamageEvent __instance,
        Creature attacker,
        Creature defender,
        ref float __result)
    {
        var changed = false;
        var settings = Mod.Settings;

        if (SummonBalanceResolver.IsPlayerOwnedCombatSummon(attacker))
        {
            var zone = SummonBalanceResolver.ResolveFor(attacker, defender, settings);
            if (zone is not null && zone.PhysicalAttackSkillMultiplier != 1.0)
            {
                __instance.EffectiveAttackSkill = SummonBalanceResolver.ScaleSkill(
                    __instance.EffectiveAttackSkill, zone.PhysicalAttackSkillMultiplier);
                changed = true;
            }
        }

        if (SummonBalanceResolver.IsPlayerOwnedCombatSummon(defender))
        {
            var zone = SummonBalanceResolver.ResolveFor(defender, attacker, settings);
            if (zone is not null && zone.PhysicalDefenseSkillMultiplier != 1.0)
            {
                __instance.EffectiveDefenseSkill = SummonBalanceResolver.ScaleSkill(
                    __instance.EffectiveDefenseSkill, zone.PhysicalDefenseSkillMultiplier);
                changed = true;
            }
        }

        if (changed)
        {
            __result = (float)Math.Clamp(
                1.0 - SkillCheck.GetSkillChance(__instance.EffectiveAttackSkill, __instance.EffectiveDefenseSkill),
                0.0,
                1.0);
        }
    }
}

[HarmonyPatch]
public static class SpellDamagePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(SpellProjectile),
        nameof(SpellProjectile.CalculateDamage),
        new[] { typeof(WorldObject), typeof(Creature), typeof(bool).MakeByRefType(),
            typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() })
        ?? throw new MissingMethodException(typeof(SpellProjectile).FullName,
            nameof(SpellProjectile.CalculateDamage));

    [HarmonyPostfix]
    public static void ApplyLandblockMultipliers(WorldObject source, Creature target, ref float? __result)
    {
        if (__result is not > 0.0f)
            return;

        var multiplier = 1.0;
        var settings = Mod.Settings;

        if (source is Creature sourceCreature &&
            SummonBalanceResolver.IsPlayerOwnedCombatSummon(sourceCreature))
        {
            var zone = SummonBalanceResolver.ResolveFor(sourceCreature, target, settings);
            multiplier *= zone?.SpellDamageMultiplier ?? 1.0;
        }

        if (SummonBalanceResolver.IsPlayerOwnedCombatSummon(target) && source is Creature attacker)
        {
            var zone = SummonBalanceResolver.ResolveFor(target, attacker, settings);
            multiplier *= zone?.DamageTakenMultiplier ?? 1.0;
        }

        if (multiplier != 1.0)
            __result = SummonBalanceResolver.ScaleDamage(__result.Value, multiplier);
    }
}
