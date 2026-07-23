using ACE.Entity.Enum;
using ACE.Server.Mods;
using ACE.Server.WorldObjects;

using HarmonyLib;

namespace UnlimitedStatAugmentation;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.UnlimitedStatAugmentation";

    public static IReadOnlyList<AugmentationType> AttributeTypes { get; } = new[]
    {
        AugmentationType.Strength,
        AugmentationType.Endurance,
        AugmentationType.Coordination,
        AugmentationType.Quickness,
        AugmentationType.Focus,
        AugmentationType.Self
    };

    private readonly Dictionary<AugmentationType, int> originalLimits = new();
    private readonly Harmony harmony = new(HarmonyId);
    private bool initialized;

    public void Initialize()
    {
        if (initialized)
            return;

        foreach (var type in AttributeTypes)
        {
            if (!AugmentationDevice.MaxAugs.TryGetValue(type, out var originalLimit))
                throw new InvalidOperationException($"ACE does not define a stat-augmentation limit for {type}.");

            originalLimits[type] = originalLimit;
            AugmentationDevice.MaxAugs[type] = int.MaxValue;
        }

        try
        {
            harmony.PatchAll(typeof(Mod).Assembly);
            initialized = true;
        }
        catch
        {
            RestoreOriginalLimits();
            throw;
        }

        Console.WriteLine("[Unlimited Stat Augmentation Gems] Enabled unlimited stat-gem uses with a maximum innate stat of 100.");
    }

    public void Dispose()
    {
        harmony.UnpatchAll(HarmonyId);
        RestoreOriginalLimits();
        initialized = false;
    }

    private void RestoreOriginalLimits()
    {
        foreach (var (type, originalLimit) in originalLimits)
        {
            if (AugmentationDevice.MaxAugs.TryGetValue(type, out var currentLimit) && currentLimit == int.MaxValue)
                AugmentationDevice.MaxAugs[type] = originalLimit;
        }

        originalLimits.Clear();
    }
}
