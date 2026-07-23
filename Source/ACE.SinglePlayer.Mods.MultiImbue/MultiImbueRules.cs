using System.Numerics;

using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace MultiImbue;

public static class MultiImbueRules
{
    public const int HardMaximumImbues = 3;

    // ACE reserves five persistent properties for imbued effects. This mod writes only the first three,
    // but reads all five so it cooperates safely with imported items and other content.
    public static IReadOnlyList<PropertyInt> EffectSlots { get; } = new[]
    {
        PropertyInt.ImbuedEffect,
        PropertyInt.ImbuedEffect2,
        PropertyInt.ImbuedEffect3,
        PropertyInt.ImbuedEffect4,
        PropertyInt.ImbuedEffect5
    };

    public static IReadOnlyList<PropertyInt> WritableSlots { get; } = EffectSlots.Take(HardMaximumImbues).ToArray();

    private const ImbuedEffectType CountedEffects =
        ImbuedEffectType.CriticalStrike |
        ImbuedEffectType.CripplingBlow |
        ImbuedEffectType.ArmorRending |
        ImbuedEffectType.SlashRending |
        ImbuedEffectType.PierceRending |
        ImbuedEffectType.BludgeonRending |
        ImbuedEffectType.AcidRending |
        ImbuedEffectType.ColdRending |
        ImbuedEffectType.ElectricRending |
        ImbuedEffectType.FireRending |
        ImbuedEffectType.MeleeDefense |
        ImbuedEffectType.MissileDefense |
        ImbuedEffectType.MagicDefense |
        ImbuedEffectType.Spellbook |
        ImbuedEffectType.NetherRending;

    public static bool TryGetCraftedEffect(MaterialType? material, out ImbuedEffectType effect)
    {
        effect = material switch
        {
            MaterialType.BlackOpal => ImbuedEffectType.CriticalStrike,
            MaterialType.FireOpal => ImbuedEffectType.CripplingBlow,
            MaterialType.Sunstone => ImbuedEffectType.ArmorRending,
            MaterialType.ImperialTopaz => ImbuedEffectType.SlashRending,
            MaterialType.BlackGarnet => ImbuedEffectType.PierceRending,
            MaterialType.WhiteSapphire => ImbuedEffectType.BludgeonRending,
            MaterialType.Emerald => ImbuedEffectType.AcidRending,
            MaterialType.Aquamarine => ImbuedEffectType.ColdRending,
            MaterialType.Jet => ImbuedEffectType.ElectricRending,
            MaterialType.RedGarnet => ImbuedEffectType.FireRending,
            MaterialType.Peridot => ImbuedEffectType.MeleeDefense,
            MaterialType.YellowTopaz => ImbuedEffectType.MissileDefense,
            MaterialType.Zircon => ImbuedEffectType.MagicDefense,
            _ => ImbuedEffectType.Undef
        };

        return effect != ImbuedEffectType.Undef;
    }

    public static bool IsSuccessfulRecipeMutation(Recipe recipe, uint dataId) =>
        recipe.RecipeMod.Any(mod => mod.ExecutesOnSuccess && unchecked((uint)mod.DataId) == dataId);

    public static ImbuedEffectType GetCombinedEffects(WorldObject target) =>
        GetCombinedEffects(EffectSlots.Select(slot => target.GetProperty(slot)));

    public static ImbuedEffectType GetCombinedEffects(IEnumerable<int?> slotValues)
    {
        var combined = ImbuedEffectType.Undef;
        foreach (var value in slotValues)
            combined |= (ImbuedEffectType)(uint)(value ?? 0);
        return combined;
    }

    public static int CountImbues(WorldObject target) => CountImbues(GetCombinedEffects(target));

    public static int CountImbues(ImbuedEffectType effects) =>
        BitOperations.PopCount((uint)(effects & CountedEffects));

    public static bool ContainsEffect(WorldObject target, ImbuedEffectType effect) =>
        GetCombinedEffects(target).HasFlag(effect);

    public static PropertyInt? FindFirstWritableSlot(WorldObject target)
    {
        foreach (var slot in WritableSlots)
        {
            if ((target.GetProperty(slot) ?? 0) == 0)
                return slot;
        }

        return null;
    }

    public static void RestoreProperty(WorldObject target, PropertyInt property, int? value)
    {
        if (value is null or 0)
            target.RemoveProperty(property);
        else
            target.SetProperty(property, value.Value);
    }
}
