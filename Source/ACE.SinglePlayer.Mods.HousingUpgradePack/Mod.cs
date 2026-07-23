using System.Reflection;
using System.Text.Json;

using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;

using HarmonyLib;

namespace HousingUpgradePack;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.HousingUpgradePack";

    private readonly Harmony harmony = new(HarmonyId);
    private bool initialized;

    public static HousingUpgradeSettings Settings { get; private set; } = new();

    public void Initialize()
    {
        if (initialized)
            return;

        var assemblyFolder = Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? throw new InvalidOperationException("The Housing Upgrade Pack assembly folder could not be determined.");
        Settings = LoadSettings(Path.Combine(assemblyFolder, "Settings.json"));
        harmony.PatchAll(typeof(Mod).Assembly);
        initialized = true;

        Console.WriteLine($"[Housing Upgrade Pack] Enabled. Storage: {Settings.IncreaseStorageCapacity}; " +
            $"hook limits removed: {Settings.RemoveHookLimits}; maintenance removed: {Settings.RemoveRentAndMaintenance}; " +
            $"mansion rank removed: {Settings.RemoveMansionAllegianceRankRequirement}; " +
            $"purchase timers removed: {Settings.RemoveHousePurchaseTimers}.");
    }

    public void Dispose()
    {
        harmony.UnpatchAll(HarmonyId);
        Settings = new HousingUpgradeSettings();
        initialized = false;
    }

    public static HousingUpgradeSettings LoadSettings(string path)
    {
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<HousingUpgradeSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new HousingUpgradeSettings()
            : new HousingUpgradeSettings();

        settings.Validate();
        return settings;
    }
}

public sealed class HousingUpgradeSettings
{
    public bool IncreaseStorageCapacity { get; set; } = true;

    public HousingCapacitySettings Apartment { get; set; } = new(150, 12);

    public HousingCapacitySettings Cottage { get; set; } = new(180, 15);

    public HousingCapacitySettings Villa { get; set; } = new(220, 20);

    public HousingCapacitySettings Mansion { get; set; } = new(255, 25);

    public bool RemoveHookLimits { get; set; } = true;

    public bool RemoveRentAndMaintenance { get; set; }

    public bool RemoveMansionAllegianceRankRequirement { get; set; }

    public bool RemoveHousePurchaseTimers { get; set; }

    public void Validate()
    {
        ValidateCapacity(Apartment, nameof(Apartment));
        ValidateCapacity(Cottage, nameof(Cottage));
        ValidateCapacity(Villa, nameof(Villa));
        ValidateCapacity(Mansion, nameof(Mansion));
    }

    public HousingCapacitySettings? GetCapacity(HouseType houseType)
    {
        if (!IncreaseStorageCapacity)
            return null;

        return houseType switch
        {
            HouseType.Apartment => Apartment,
            HouseType.Cottage => Cottage,
            HouseType.Villa => Villa,
            HouseType.Mansion => Mansion,
            _ => null
        };
    }

    private static void ValidateCapacity(HousingCapacitySettings? capacity, string name)
    {
        if (capacity is null)
            throw new InvalidDataException($"{name} capacity settings are required.");
        if (capacity.ItemSlots is < 0 or > byte.MaxValue)
            throw new InvalidDataException($"{name}.ItemSlots must be between 0 and {byte.MaxValue}.");
        if (capacity.PackSlots is < 0 or > byte.MaxValue)
            throw new InvalidDataException($"{name}.PackSlots must be between 0 and {byte.MaxValue}.");
    }
}

public sealed class HousingCapacitySettings
{
    public HousingCapacitySettings()
    {
    }

    public HousingCapacitySettings(int itemSlots, int packSlots)
    {
        ItemSlots = itemSlots;
        PackSlots = packSlots;
    }

    public int ItemSlots { get; set; }

    public int PackSlots { get; set; }
}

public static class HousingCapacityResolver
{
    public static byte? ResolveItemCapacity(HouseType houseType, byte? stockCapacity,
        HousingUpgradeSettings settings) => Resolve(houseType, stockCapacity, settings, usePackSlots: false);

    public static byte? ResolvePackCapacity(HouseType houseType, byte? stockCapacity,
        HousingUpgradeSettings settings) => Resolve(houseType, stockCapacity, settings, usePackSlots: true);

    private static byte? Resolve(HouseType houseType, byte? stockCapacity,
        HousingUpgradeSettings settings, bool usePackSlots)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var configured = settings.GetCapacity(houseType);
        if (configured is null)
            return stockCapacity;

        var configuredCapacity = usePackSlots ? configured.PackSlots : configured.ItemSlots;
        return (byte)Math.Max(stockCapacity ?? 0, configuredCapacity);
    }
}

[HarmonyPatch]
public static class StorageItemCapacityPatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(WorldObject),
        nameof(WorldObject.ItemCapacity))
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, $"get_{nameof(WorldObject.ItemCapacity)}");

    [HarmonyPostfix]
    public static void ApplyCapacity(WorldObject __instance, ref byte? __result)
    {
        if (__instance is not Storage storage || storage.House is null)
            return;

        __result = HousingCapacityResolver.ResolveItemCapacity(storage.House.HouseType, __result, Mod.Settings);
    }
}

[HarmonyPatch]
public static class StoragePackCapacityPatch
{
    private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(WorldObject),
        nameof(WorldObject.ContainerCapacity))
        ?? throw new MissingMethodException(typeof(WorldObject).FullName, $"get_{nameof(WorldObject.ContainerCapacity)}");

    [HarmonyPostfix]
    public static void ApplyCapacity(WorldObject __instance, ref byte? __result)
    {
        if (__instance is not Storage storage || storage.House is null)
            return;

        __result = HousingCapacityResolver.ResolvePackCapacity(storage.House.HouseType, __result, Mod.Settings);
    }
}

public static class HousingPropertyOverrides
{
    public static Property<bool> ApplyBool(string key, Property<bool> original,
        HousingUpgradeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var forceDisabled = settings.RemoveHookLimits && key is "house_hook_limit" or "house_hookgroup_limit"
            || settings.RemoveRentAndMaintenance && key == "house_rent_enabled"
            || settings.RemoveHousePurchaseTimers && key is "house_15day_account" or "house_30day_cooldown";

        return forceDisabled ? new Property<bool>(false, original.Description) : original;
    }

    public static Property<long> ApplyLong(string key, Property<long> original,
        HousingUpgradeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.RemoveMansionAllegianceRankRequirement && key == "mansion_min_rank"
            ? new Property<long>(0, original.Description)
            : original;
    }
}

[HarmonyPatch]
public static class HousingBoolPropertyPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(PropertyManager),
        nameof(PropertyManager.GetBool), new[] { typeof(string), typeof(bool), typeof(bool) })
        ?? throw new MissingMethodException(typeof(PropertyManager).FullName, nameof(PropertyManager.GetBool));

    [HarmonyPostfix]
    public static void ApplyConfiguredOverrides(string key, ref Property<bool> __result) =>
        __result = HousingPropertyOverrides.ApplyBool(key, __result, Mod.Settings);
}

[HarmonyPatch]
public static class MansionRankPropertyPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(PropertyManager),
        nameof(PropertyManager.GetLong), new[] { typeof(string), typeof(long), typeof(bool) })
        ?? throw new MissingMethodException(typeof(PropertyManager).FullName, nameof(PropertyManager.GetLong));

    [HarmonyPostfix]
    public static void ApplyConfiguredOverride(string key, ref Property<long> __result) =>
        __result = HousingPropertyOverrides.ApplyLong(key, __result, Mod.Settings);
}
