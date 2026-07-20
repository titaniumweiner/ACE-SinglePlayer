using System.Reflection;
using System.Text.Json;

using ACE.Common;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;

using HarmonyLib;

namespace UniversalLootLuck;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.UniversalLootLuck";

    private readonly Harmony harmony = new(HarmonyId);
    private bool initialized;

    internal static LootLuckSettings Settings { get; private set; } = new();

    public void Initialize()
    {
        if (initialized)
            return;

        var assemblyFolder = Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? throw new InvalidOperationException("The UniversalLootLuck assembly folder could not be determined.");
        Settings = LoadSettings(Path.Combine(assemblyFolder, "Settings.json"));
        harmony.PatchAll(typeof(Mod).Assembly);
        initialized = true;

        Console.WriteLine($"[All-Tier Salvage & Loot Luck] Enabled. All materials: {Settings.AllMaterialsEveryTier}; " +
            $"quality bonus: {Settings.LootQualityBonus:0.##}; generated loot: {Settings.GeneratedLootChanceMultiplier:0.##}x; " +
            $"trophies: {Settings.TrophyDropRateMultiplier:0.##}x; rares: {Settings.RareDropRateMultiplier:0.##}x.");
    }

    public void Dispose()
    {
        harmony.UnpatchAll(HarmonyId);
        Settings = new LootLuckSettings();
        initialized = false;
    }

    public static LootLuckSettings LoadSettings(string path)
    {
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<LootLuckSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new LootLuckSettings()
            : new LootLuckSettings();

        settings.Validate();
        return settings;
    }
}

public sealed class LootLuckSettings
{
    public bool AllMaterialsEveryTier { get; set; } = true;

    /// <summary>
    /// Added to ACE's LootQualityMod for generated treasure. Zero keeps stock quality.
    /// Values approaching 1 progressively skip more low-quality outcomes.
    /// </summary>
    public float LootQualityBonus { get; set; } = 0.0f;

    public double GeneratedLootChanceMultiplier { get; set; } = 1.0;

    public double TrophyDropRateMultiplier { get; set; } = 1.0;

    public double RareDropRateMultiplier { get; set; } = 1.0;

    public void Validate()
    {
        if (!float.IsFinite(LootQualityBonus) || LootQualityBonus is < 0.0f or > 0.95f)
            throw new InvalidDataException("LootQualityBonus must be between 0 and 0.95.");

        ValidateMultiplier(GeneratedLootChanceMultiplier, nameof(GeneratedLootChanceMultiplier), 10.0);
        ValidateMultiplier(TrophyDropRateMultiplier, nameof(TrophyDropRateMultiplier), 10.0);
        ValidateMultiplier(RareDropRateMultiplier, nameof(RareDropRateMultiplier), 100.0);
    }

    private static void ValidateMultiplier(double value, string name, double maximum)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > maximum)
            throw new InvalidDataException($"{name} must be between 0 and {maximum:0.##}.");
    }
}

[HarmonyPatch]
public static class LootProfilePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(LootGenerationFactory),
        nameof(LootGenerationFactory.CreateRandomLootObjects), new[] { typeof(TreasureDeath) })
        ?? throw new MissingMethodException(typeof(LootGenerationFactory).FullName,
            nameof(LootGenerationFactory.CreateRandomLootObjects));

    [HarmonyPrefix]
    public static void ApplyConfiguredLuck(ref TreasureDeath profile)
    {
        var settings = Mod.Settings;
        if (settings.LootQualityBonus == 0.0f && settings.GeneratedLootChanceMultiplier == 1.0)
            return;

        profile = LootProfileAdjuster.CreateAdjusted(profile, settings);
    }
}

public static class LootProfileAdjuster
{
    public static TreasureDeath CreateAdjusted(TreasureDeath source, LootLuckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        return new TreasureDeath
        {
            Id = source.Id,
            TreasureType = source.TreasureType,
            Tier = source.Tier,
            LootQualityMod = Math.Clamp(source.LootQualityMod + settings.LootQualityBonus, 0.0f, 0.95f),
            UnknownChances = source.UnknownChances,
            ItemChance = ScaleChance(source.ItemChance, settings.GeneratedLootChanceMultiplier),
            ItemMinAmount = source.ItemMinAmount,
            ItemMaxAmount = source.ItemMaxAmount,
            ItemTreasureTypeSelectionChances = source.ItemTreasureTypeSelectionChances,
            MagicItemChance = ScaleChance(source.MagicItemChance, settings.GeneratedLootChanceMultiplier),
            MagicItemMinAmount = source.MagicItemMinAmount,
            MagicItemMaxAmount = source.MagicItemMaxAmount,
            MagicItemTreasureTypeSelectionChances = source.MagicItemTreasureTypeSelectionChances,
            MundaneItemChance = ScaleChance(source.MundaneItemChance, settings.GeneratedLootChanceMultiplier),
            MundaneItemMinAmount = source.MundaneItemMinAmount,
            MundaneItemMaxAmount = source.MundaneItemMaxAmount,
            MundaneItemTypeSelectionChances = source.MundaneItemTypeSelectionChances,
            LastModified = source.LastModified
        };
    }

    public static int ScaleChance(int chance, double multiplier) =>
        Math.Clamp((int)Math.Round(chance * multiplier, MidpointRounding.AwayFromZero), 0, 100);
}

[HarmonyPatch]
public static class UniversalMaterialPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(LootGenerationFactory),
        "GetMaterialType", new[] { typeof(WorldObject), typeof(int) })
        ?? throw new MissingMethodException(typeof(LootGenerationFactory).FullName, "GetMaterialType");

    [HarmonyPrefix]
    public static bool SelectFromEveryTier(WorldObject wo, ref MaterialType __result)
    {
        if (!Mod.Settings.AllMaterialsEveryTier || !UniversalMaterialSelector.TryRoll(wo, out var material))
            return true;

        __result = material;
        return false;
    }
}

public static class UniversalMaterialSelector
{
    private const int FirstMaterialTier = 1;
    private const int LastMaterialTier = 6;

    public static bool TryRoll(WorldObject wo, out MaterialType material)
    {
        material = MaterialType.Unknown;
        if (wo?.TsysMutationData is not int mutationData)
            return false;

        var materialCode = mutationData & 0xFF;
        var materialBase = new List<(uint materialId, float probability)>();

        for (var tier = FirstMaterialTier; tier <= LastMaterialTier; tier++)
        {
            var tierEntries = DatabaseManager.World.GetCachedTreasureMaterialBase(materialCode, tier);
            if (tierEntries is not null)
                materialBase.AddRange(tierEntries.Select(entry => (entry.MaterialId, entry.Probability)));
        }

        var baseId = RollWeightedMaterialId(materialBase, (float)ThreadSafeRandom.Next(0.0f, 1.0f));
        if (baseId is null)
            return false;

        if (baseId.Value == (uint)MaterialType.Ivory)
        {
            material = MaterialType.Ivory;
            return true;
        }

        var materialGroup = new List<(uint materialId, float probability)>();
        for (var tier = FirstMaterialTier; tier <= LastMaterialTier; tier++)
        {
            var tierEntries = DatabaseManager.World.GetCachedTreasureMaterialGroup((int)baseId.Value, tier);
            if (tierEntries is not null)
                materialGroup.AddRange(tierEntries.Select(entry => (entry.MaterialId, entry.Probability)));
        }

        var materialId = RollWeightedMaterialId(materialGroup, (float)ThreadSafeRandom.Next(0.0f, 1.0f));
        if (materialId is null)
            return false;

        material = (MaterialType)materialId.Value;
        return true;
    }

    /// <summary>
    /// Merges duplicate material rows from every tier and performs one weighted roll.
    /// Each ACE tier table contributes its normalized weights to the combined pool.
    /// </summary>
    public static uint? RollWeightedMaterialId(
        IEnumerable<(uint materialId, float probability)> entries, float unitRoll)
    {
        var merged = entries
            .Where(entry => entry.probability > 0.0f && float.IsFinite(entry.probability))
            .GroupBy(entry => entry.materialId)
            .Select(group => (materialId: group.Key, probability: group.Sum(entry => entry.probability)))
            .Where(entry => entry.probability > 0.0f)
            .OrderBy(entry => entry.materialId)
            .ToArray();

        if (merged.Length == 0)
            return null;

        var total = merged.Sum(entry => entry.probability);
        var boundedRoll = Math.Clamp(unitRoll, 0.0f, MathF.BitDecrement(1.0f));
        var target = boundedRoll * total;
        var cumulative = 0.0f;

        foreach (var entry in merged)
        {
            cumulative += entry.probability;
            if (target < cumulative)
                return entry.materialId;
        }

        return merged[^1].materialId;
    }
}

[HarmonyPatch]
public static class TrophyDropRatePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(Creature),
        nameof(Creature.CreateListSelect), new[] { typeof(List<PropertiesCreateList>) })
        ?? throw new MissingMethodException(typeof(Creature).FullName, nameof(Creature.CreateListSelect));

    [HarmonyPrefix]
    public static bool ApplyMultiplier(List<PropertiesCreateList> createList,
        ref List<PropertiesCreateList> __result)
    {
        var multiplier = Mod.Settings.TrophyDropRateMultiplier;
        if (multiplier == 1.0)
            return true;

        var serverRate = PropertyManager.GetDouble("trophy_drop_rate").Item;
        var effectiveRate = Math.Max(0.0, serverRate * multiplier);
        __result = Creature.CreateListSelect(createList, (float)effectiveRate);
        return false;
    }
}

[HarmonyPatch]
public static class RareDropRatePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(LootGenerationFactory),
        nameof(LootGenerationFactory.TryCreateRare), new[] { typeof(int) })
        ?? throw new MissingMethodException(typeof(LootGenerationFactory).FullName,
            nameof(LootGenerationFactory.TryCreateRare));

    [HarmonyPrefix]
    public static bool ApplyMultiplier(ref int luck, ref WorldObject __result)
    {
        var multiplier = Mod.Settings.RareDropRateMultiplier;
        if (multiplier == 1.0)
            return true;

        if (multiplier == 0.0)
        {
            __result = null!;
            return false;
        }

        var rarePercent = PropertyManager.GetDouble("rare_drop_rate_percent").Item;
        if (rarePercent <= 0.0)
            return true;

        var baseChance = Math.Max((int)Math.Round(1.0 / Math.Min(rarePercent / 100.0, 1.0)), 1);
        luck = CalculateAdjustedLuck(baseChance, luck, multiplier);
        return true;
    }

    public static int CalculateAdjustedLuck(int baseChance, int existingLuck, double multiplier)
    {
        if (baseChance < 1)
            throw new ArgumentOutOfRangeException(nameof(baseChance));
        if (!double.IsFinite(multiplier) || multiplier <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(multiplier));

        var currentChance = Math.Max((long)baseChance - existingLuck, 1L);
        var targetChance = Math.Clamp((long)Math.Round(currentChance / multiplier), 1L, int.MaxValue);
        return checked(baseChance - (int)targetChance);
    }
}
