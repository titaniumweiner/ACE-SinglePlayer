using System.Globalization;

namespace AquafirCreatureVariants;

public enum CreatureVariantType
{
    Accurate,
    Berserker,
    Comboer,
    Drainer,
    Duelist,
    Evader,
    Exploder,
    Healer,
    Shielded,
    SpellBreaker,
    Stomper,
    Vampire,
    Rogue,
    Horde,
    Puppeteer,
    Boss,
    Tank,
    Stunner
}

public sealed class CreatureVariantSettings
{
    public bool Enabled { get; set; } = true;
    public double AssignmentChance { get; set; } = 0.50;
    public bool AllowVariantStacking { get; set; } = true;
    public double AdditionalVariantChance { get; set; } = 0.50;
    public int MaximumVariantsPerCreature { get; set; } = 3;
    public bool ShowTraitInCreatureName { get; set; } = true;
    public int MinimumLevel { get; set; } = 1;
    public int MaximumLevel { get; set; } = 275;
    public List<uint> AllowedWeenieClassIds { get; set; } = new();
    public List<uint> ExcludedWeenieClassIds { get; set; } = new();
    public Dictionary<string, CreatureVariantType> ForcedTraitsByWeenieClassId { get; set; } = new();
    public Dictionary<CreatureVariantType, double> TraitWeights { get; set; } = DefaultWeights();

    public double AccurateAutoHitChance { get; set; } = 0.25;
    public double BerserkerHealthThreshold { get; set; } = 0.60;
    public double BerserkerAttributeMultiplier { get; set; } = 1.30;
    public int ComboHitThreshold { get; set; } = 5;
    public double ComboDamageMultiplier { get; set; } = 1.50;
    public double DrainDamageFraction { get; set; } = 1.0;
    public double DuelistFrontArcDegrees { get; set; } = 40.0;
    public double EvaderAutoEvadeChance { get; set; } = 0.25;
    public double ExploderTriggerRange { get; set; } = 5.0;
    public int ExploderCountdownHeartbeats { get; set; } = 3;
    public double ExploderMaximumDamage { get; set; } = 250.0;
    public double HealerIntervalSeconds { get; set; } = 15.0;
    public double HealerRange { get; set; } = 15.0;
    public int HealerMaximumTargets { get; set; } = 3;
    public double HealerHealthFraction { get; set; } = 0.20;
    public int ShieldCount { get; set; } = 3;
    public double ShieldRechargeSeconds { get; set; } = 25.0;
    public double SpellBreakerManaDamageFraction { get; set; } = 1.0;
    public double SpellBreakerMaximumDamage { get; set; } = 250.0;
    public double StomperRange { get; set; } = 10.0;
    public int StomperMaximumTargets { get; set; } = 5;
    public double StomperMaximumSplashFraction { get; set; } = 0.80;
    public double VampireLeechFraction { get; set; } = 1.0;
    public double RogueFumbleChance { get; set; } = 0.25;
    public double RogueCooldownSeconds { get; set; } = 10.0;
    public int RogueStaminaDrain { get; set; } = 20;
    public int HordeMinimumMembers { get; set; } = 3;
    public int HordeMaximumMembers { get; set; } = 6;
    public double HordeHealthPerAdditionalMember { get; set; } = 0.50;
    public double HordeDamagePerAdditionalLivingMember { get; set; } = 0.15;
    public double HordeMaximumDamageMultiplier { get; set; } = 3.0;
    public int PuppeteerMaximumCopies { get; set; } = 3;
    public double PuppeteerSpawnIntervalSeconds { get; set; } = 15.0;
    public double PuppeteerCopyLifetimeSeconds { get; set; } = 45.0;
    public double PuppeteerSpawnRadius { get; set; } = 3.0;
    public double BossScaleMultiplier { get; set; } = 1.75;
    public double BossAttributeMultiplier { get; set; } = 1.50;
    public double BossVitalMultiplier { get; set; } = 2.0;
    public double BossDamageMultiplier { get; set; } = 1.25;
    public double BossExperienceMultiplier { get; set; } = 5.0;
    public double BossOtherDamageMultiplier { get; set; } = 0.50;
    public double BossWeaknessDamageMultiplier { get; set; } = 1.50;
    public double BossWeaknessIntervalSeconds { get; set; } = 15.0;
    public bool BossAttacksCannotBeEvaded { get; set; } = true;
    public bool BossSpellsCannotBeResisted { get; set; } = true;
    public double BossSpecialSpellIntervalSeconds { get; set; } = 15.0;
    public double BossSpecialSpellRange { get; set; } = 40.0;
    public List<uint> BossSpellIds { get; set; } = new() { 63, 69, 74, 80, 85, 91, 97 };
    public double TankProtectionRange { get; set; } = 10.0;
    public double TankRedirectDamageMultiplier { get; set; } = 0.50;
    public double StunnerIntervalSeconds { get; set; } = 20.0;
    public double StunnerRange { get; set; } = 8.0;
    public double StunnerDurationSeconds { get; set; } = 2.0;

    public CompiledCreatureVariantSettings Compile()
    {
        ValidateUnitInterval(AssignmentChance, nameof(AssignmentChance));
        ValidateUnitInterval(AdditionalVariantChance, nameof(AdditionalVariantChance));
        ValidateIntRange(MaximumVariantsPerCreature, nameof(MaximumVariantsPerCreature), 1,
            Enum.GetValues<CreatureVariantType>().Length);
        if (MinimumLevel < 0 || MaximumLevel < MinimumLevel || MaximumLevel > 10000)
            throw new InvalidDataException("MinimumLevel and MaximumLevel must define a range from 0 through 10000.");

        AllowedWeenieClassIds ??= new();
        ExcludedWeenieClassIds ??= new();
        ForcedTraitsByWeenieClassId ??= new();
        TraitWeights ??= new();

        var allowed = AllowedWeenieClassIds.ToHashSet();
        var excluded = ExcludedWeenieClassIds.ToHashSet();
        if (allowed.Overlaps(excluded))
            throw new InvalidDataException("A WCID cannot appear in both AllowedWeenieClassIds and ExcludedWeenieClassIds.");

        var forced = new Dictionary<uint, CreatureVariantType>();
        foreach (var pair in ForcedTraitsByWeenieClassId)
        {
            var wcid = ParseWeenieClassId(pair.Key);
            if (!forced.TryAdd(wcid, pair.Value))
                throw new InvalidDataException($"ForcedTraitsByWeenieClassId contains WCID {wcid} more than once.");
            if (excluded.Contains(wcid))
                throw new InvalidDataException($"Forced WCID {wcid} is also excluded.");
        }

        var weightedTraits = new List<WeightedCreatureVariant>();
        foreach (var type in Enum.GetValues<CreatureVariantType>())
        {
            var weight = TraitWeights.GetValueOrDefault(type);
            ValidateFiniteRange(weight, $"TraitWeights.{type}", 0.0, 1000.0);
            if (weight > 0.0)
                weightedTraits.Add(new WeightedCreatureVariant(type, weight));
        }

        if (AssignmentChance > 0.0 && weightedTraits.Count == 0)
            throw new InvalidDataException("At least one TraitWeights entry must be greater than zero when AssignmentChance is greater than zero.");

        ValidateUnitInterval(AccurateAutoHitChance, nameof(AccurateAutoHitChance));
        ValidateUnitInterval(BerserkerHealthThreshold, nameof(BerserkerHealthThreshold));
        ValidateFiniteRange(BerserkerAttributeMultiplier, nameof(BerserkerAttributeMultiplier), 1.0, 5.0);
        ValidateIntRange(ComboHitThreshold, nameof(ComboHitThreshold), 1, 100);
        ValidateFiniteRange(ComboDamageMultiplier, nameof(ComboDamageMultiplier), 1.0, 10.0);
        ValidateFiniteRange(DrainDamageFraction, nameof(DrainDamageFraction), 0.0, 10.0);
        ValidateFiniteRange(DuelistFrontArcDegrees, nameof(DuelistFrontArcDegrees), 0.0, 360.0);
        ValidateUnitInterval(EvaderAutoEvadeChance, nameof(EvaderAutoEvadeChance));
        ValidateFiniteRange(ExploderTriggerRange, nameof(ExploderTriggerRange), 0.5, 100.0);
        ValidateIntRange(ExploderCountdownHeartbeats, nameof(ExploderCountdownHeartbeats), 1, 100);
        ValidateFiniteRange(ExploderMaximumDamage, nameof(ExploderMaximumDamage), 0.0, 100000.0);
        ValidateFiniteRange(HealerIntervalSeconds, nameof(HealerIntervalSeconds), 1.0, 3600.0);
        ValidateFiniteRange(HealerRange, nameof(HealerRange), 0.5, 100.0);
        ValidateIntRange(HealerMaximumTargets, nameof(HealerMaximumTargets), 1, 100);
        ValidateUnitInterval(HealerHealthFraction, nameof(HealerHealthFraction));
        ValidateIntRange(ShieldCount, nameof(ShieldCount), 1, 100);
        ValidateFiniteRange(ShieldRechargeSeconds, nameof(ShieldRechargeSeconds), 1.0, 3600.0);
        ValidateFiniteRange(SpellBreakerManaDamageFraction, nameof(SpellBreakerManaDamageFraction), 0.0, 10.0);
        ValidateFiniteRange(SpellBreakerMaximumDamage, nameof(SpellBreakerMaximumDamage), 0.0, 100000.0);
        ValidateFiniteRange(StomperRange, nameof(StomperRange), 0.5, 100.0);
        ValidateIntRange(StomperMaximumTargets, nameof(StomperMaximumTargets), 1, 100);
        ValidateUnitInterval(StomperMaximumSplashFraction, nameof(StomperMaximumSplashFraction));
        ValidateFiniteRange(VampireLeechFraction, nameof(VampireLeechFraction), 0.0, 10.0);
        ValidateUnitInterval(RogueFumbleChance, nameof(RogueFumbleChance));
        ValidateFiniteRange(RogueCooldownSeconds, nameof(RogueCooldownSeconds), 1.0, 3600.0);
        ValidateIntRange(RogueStaminaDrain, nameof(RogueStaminaDrain), 0, 10000);
        ValidateIntRange(HordeMinimumMembers, nameof(HordeMinimumMembers), 1, 20);
        ValidateIntRange(HordeMaximumMembers, nameof(HordeMaximumMembers), HordeMinimumMembers, 20);
        ValidateFiniteRange(HordeHealthPerAdditionalMember, nameof(HordeHealthPerAdditionalMember), 0.0, 5.0);
        ValidateFiniteRange(HordeDamagePerAdditionalLivingMember, nameof(HordeDamagePerAdditionalLivingMember), 0.0, 2.0);
        ValidateFiniteRange(HordeMaximumDamageMultiplier, nameof(HordeMaximumDamageMultiplier), 1.0, 20.0);
        ValidateIntRange(PuppeteerMaximumCopies, nameof(PuppeteerMaximumCopies), 0, 20);
        ValidateFiniteRange(PuppeteerSpawnIntervalSeconds, nameof(PuppeteerSpawnIntervalSeconds), 1.0, 3600.0);
        ValidateFiniteRange(PuppeteerCopyLifetimeSeconds, nameof(PuppeteerCopyLifetimeSeconds), 1.0, 3600.0);
        ValidateFiniteRange(PuppeteerSpawnRadius, nameof(PuppeteerSpawnRadius), 0.5, 25.0);
        ValidateFiniteRange(BossScaleMultiplier, nameof(BossScaleMultiplier), 1.0, 5.0);
        ValidateFiniteRange(BossAttributeMultiplier, nameof(BossAttributeMultiplier), 1.0, 10.0);
        ValidateFiniteRange(BossVitalMultiplier, nameof(BossVitalMultiplier), 1.0, 20.0);
        ValidateFiniteRange(BossDamageMultiplier, nameof(BossDamageMultiplier), 0.0, 20.0);
        ValidateFiniteRange(BossExperienceMultiplier, nameof(BossExperienceMultiplier), 0.0, 1000.0);
        ValidateFiniteRange(BossOtherDamageMultiplier, nameof(BossOtherDamageMultiplier), 0.0, 20.0);
        ValidateFiniteRange(BossWeaknessDamageMultiplier, nameof(BossWeaknessDamageMultiplier), 0.0, 20.0);
        ValidateFiniteRange(BossWeaknessIntervalSeconds, nameof(BossWeaknessIntervalSeconds), 1.0, 3600.0);
        ValidateFiniteRange(BossSpecialSpellIntervalSeconds, nameof(BossSpecialSpellIntervalSeconds), 1.0, 3600.0);
        ValidateFiniteRange(BossSpecialSpellRange, nameof(BossSpecialSpellRange), 1.0, 200.0);
        BossSpellIds ??= new();
        if (BossSpellIds.Count > 100 || BossSpellIds.Any(id => id == 0))
            throw new InvalidDataException("BossSpellIds must contain at most 100 positive spell IDs.");
        BossSpellIds = BossSpellIds.Distinct().ToList();
        ValidateFiniteRange(TankProtectionRange, nameof(TankProtectionRange), 0.5, 100.0);
        ValidateFiniteRange(TankRedirectDamageMultiplier, nameof(TankRedirectDamageMultiplier), 0.0, 10.0);
        ValidateFiniteRange(StunnerIntervalSeconds, nameof(StunnerIntervalSeconds), 1.0, 3600.0);
        ValidateFiniteRange(StunnerRange, nameof(StunnerRange), 0.5, 100.0);
        ValidateFiniteRange(StunnerDurationSeconds, nameof(StunnerDurationSeconds), 0.25, 30.0);

        return new CompiledCreatureVariantSettings(this, allowed, excluded, forced, weightedTraits);
    }

    private static Dictionary<CreatureVariantType, double> DefaultWeights() => new()
    {
        [CreatureVariantType.Accurate] = 1.0,
        [CreatureVariantType.Berserker] = 1.0,
        [CreatureVariantType.Comboer] = 1.0,
        [CreatureVariantType.Drainer] = 1.0,
        [CreatureVariantType.Duelist] = 1.0,
        [CreatureVariantType.Evader] = 1.0,
        [CreatureVariantType.Exploder] = 0.25,
        [CreatureVariantType.Healer] = 1.0,
        [CreatureVariantType.Shielded] = 1.0,
        [CreatureVariantType.SpellBreaker] = 1.0,
        [CreatureVariantType.Stomper] = 0.5,
        [CreatureVariantType.Vampire] = 1.0,
        [CreatureVariantType.Rogue] = 1.0,
        [CreatureVariantType.Horde] = 0.5,
        [CreatureVariantType.Puppeteer] = 0.25,
        [CreatureVariantType.Boss] = 0.1,
        [CreatureVariantType.Tank] = 0.75,
        [CreatureVariantType.Stunner] = 0.5
    };

    private static uint ParseWeenieClassId(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var style = NumberStyles.Integer;
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!uint.TryParse(normalized, style, CultureInfo.InvariantCulture, out var wcid) || wcid == 0)
            throw new InvalidDataException($"Invalid forced WCID '{value}'. Use a positive decimal number or 0x-prefixed hexadecimal number.");
        return wcid;
    }

    private static void ValidateUnitInterval(double value, string name) => ValidateFiniteRange(value, name, 0.0, 1.0);

    private static void ValidateFiniteRange(double value, string name, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException($"{name} must be between {minimum} and {maximum}.");
    }

    private static void ValidateIntRange(int value, string name, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
            throw new InvalidDataException($"{name} must be between {minimum} and {maximum}.");
    }
}

public sealed record WeightedCreatureVariant(CreatureVariantType Type, double Weight);

public sealed class CompiledCreatureVariantSettings
{
    internal CompiledCreatureVariantSettings(
        CreatureVariantSettings source,
        IReadOnlySet<uint> allowed,
        IReadOnlySet<uint> excluded,
        IReadOnlyDictionary<uint, CreatureVariantType> forced,
        IReadOnlyList<WeightedCreatureVariant> weightedTraits)
    {
        Source = source;
        AllowedWeenieClassIds = allowed;
        ExcludedWeenieClassIds = excluded;
        ForcedTraitsByWeenieClassId = forced;
        WeightedTraits = weightedTraits;
        TotalWeight = weightedTraits.Sum(item => item.Weight);
    }

    public CreatureVariantSettings Source { get; }
    public IReadOnlySet<uint> AllowedWeenieClassIds { get; }
    public IReadOnlySet<uint> ExcludedWeenieClassIds { get; }
    public IReadOnlyDictionary<uint, CreatureVariantType> ForcedTraitsByWeenieClassId { get; }
    public IReadOnlyList<WeightedCreatureVariant> WeightedTraits { get; }
    public double TotalWeight { get; }

    public bool IsEligible(uint weenieClassId, int level)
    {
        if (!Source.Enabled || level < Source.MinimumLevel || level > Source.MaximumLevel)
            return false;
        if (ExcludedWeenieClassIds.Contains(weenieClassId))
            return false;
        return AllowedWeenieClassIds.Count == 0 || AllowedWeenieClassIds.Contains(weenieClassId);
    }

    public CreatureVariantType? SelectTrait(uint weenieClassId, double assignmentRoll, double traitRoll)
    {
        if (ForcedTraitsByWeenieClassId.TryGetValue(weenieClassId, out var forced))
            return forced;
        if (assignmentRoll < 0.0 || assignmentRoll >= 1.0 || assignmentRoll >= Source.AssignmentChance || TotalWeight <= 0.0)
            return null;

        return SelectWeightedTrait(traitRoll, new HashSet<CreatureVariantType>());
    }

    public IReadOnlyList<CreatureVariantType> SelectTraits(
        uint weenieClassId,
        double assignmentRoll,
        IReadOnlyList<double> traitAndStackingRolls)
    {
        if (ForcedTraitsByWeenieClassId.TryGetValue(weenieClassId, out var forced))
            return new[] { forced };
        if (assignmentRoll < 0.0 || assignmentRoll >= 1.0 ||
            assignmentRoll >= Source.AssignmentChance || TotalWeight <= 0.0)
            return Array.Empty<CreatureVariantType>();
        if (traitAndStackingRolls.Count == 0)
            throw new ArgumentException("At least one trait roll is required.", nameof(traitAndStackingRolls));

        var selected = new List<CreatureVariantType>(Source.MaximumVariantsPerCreature);
        var selectedSet = new HashSet<CreatureVariantType>();
        var rollIndex = 0;
        var first = SelectWeightedTrait(GetRoll(traitAndStackingRolls, rollIndex++), selectedSet);
        if (first is null)
            return selected;

        selected.Add(first.Value);
        selectedSet.Add(first.Value);
        if (!Source.AllowVariantStacking)
            return selected;

        while (selected.Count < Source.MaximumVariantsPerCreature && selected.Count < WeightedTraits.Count)
        {
            if (GetRoll(traitAndStackingRolls, rollIndex++) >= Source.AdditionalVariantChance)
                break;

            var next = SelectWeightedTrait(GetRoll(traitAndStackingRolls, rollIndex++), selectedSet);
            if (next is null)
                break;

            selected.Add(next.Value);
            selectedSet.Add(next.Value);
        }

        return selected;
    }

    private CreatureVariantType? SelectWeightedTrait(double traitRoll, IReadOnlySet<CreatureVariantType> excluded)
    {
        var remainingWeight = WeightedTraits
            .Where(item => !excluded.Contains(item.Type))
            .Sum(item => item.Weight);
        if (remainingWeight <= 0.0)
            return null;

        var cursor = Math.Clamp(traitRoll, 0.0, Math.BitDecrement(1.0)) * remainingWeight;
        CreatureVariantType? last = null;
        foreach (var item in WeightedTraits)
        {
            if (excluded.Contains(item.Type))
                continue;
            last = item.Type;
            if (cursor < item.Weight)
                return item.Type;
            cursor -= item.Weight;
        }

        return last;
    }

    private static double GetRoll(IReadOnlyList<double> rolls, int index)
    {
        if (index >= rolls.Count)
            throw new ArgumentException("Not enough random rolls were supplied for the configured stacking limit.",
                nameof(rolls));

        var roll = rolls[index];
        if (!double.IsFinite(roll) || roll < 0.0 || roll >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(rolls), "Random rolls must be in the range [0, 1).");
        return roll;
    }
}
