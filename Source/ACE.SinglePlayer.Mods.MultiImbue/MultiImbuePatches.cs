using System.Reflection;

using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

using HarmonyLib;

namespace MultiImbue;

[HarmonyPatch]
public static class ImbueRequirementPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(RecipeManager),
        nameof(RecipeManager.VerifyRequirements),
        new[] { typeof(Recipe), typeof(Player), typeof(WorldObject), typeof(WorldObject) })
        ?? throw new MissingMethodException(typeof(RecipeManager).FullName, nameof(RecipeManager.VerifyRequirements));

    [HarmonyPrefix]
    public static bool AllowAnotherDistinctImbue(Recipe recipe, Player player, WorldObject source,
        WorldObject target, ref bool __result, out TemporarilyHiddenPrimaryImbue? __state)
    {
        __state = null;
        if (!recipe.IsImbuing() ||
            !MultiImbueRules.TryGetCraftedEffect(source.MaterialType, out var requestedEffect))
            return true;

        var currentCount = MultiImbueRules.CountImbues(target);
        if (MultiImbueRules.ContainsEffect(target, requestedEffect))
        {
            player.SendMessage($"The target item already has the {FormatEffect(requestedEffect)} imbue.", ChatMessageType.Craft);
            __result = false;
            return false;
        }

        if (currentCount >= Mod.Settings.MaximumImbues)
        {
            player.SendMessage($"The target item already has the maximum of {Mod.Settings.MaximumImbues} imbues.", ChatMessageType.Craft);
            __result = false;
            return false;
        }

        // Stock recipes reject any nonzero PropertyInt.ImbuedEffect. Hide only that property while ACE
        // evaluates every other stock target/source/player requirement, then restore it immediately.
        var primary = target.GetProperty(PropertyInt.ImbuedEffect);
        if (currentCount > 0 && primary is not null and not 0)
        {
            __state = new TemporarilyHiddenPrimaryImbue(target, primary);
            target.RemoveProperty(PropertyInt.ImbuedEffect);
        }

        return true;
    }

    [HarmonyPostfix]
    public static void RestoreAfterVerification(TemporarilyHiddenPrimaryImbue? __state) => __state?.Restore();

    [HarmonyFinalizer]
    public static Exception? RestoreAfterVerificationFailure(Exception? __exception,
        TemporarilyHiddenPrimaryImbue? __state)
    {
        __state?.Restore();
        return __exception;
    }

    private static string FormatEffect(ImbuedEffectType effect) => effect switch
    {
        ImbuedEffectType.CriticalStrike => "critical strike",
        ImbuedEffectType.CripplingBlow => "crippling blow",
        ImbuedEffectType.ArmorRending => "armor rending",
        ImbuedEffectType.MeleeDefense => "melee defense",
        ImbuedEffectType.MissileDefense => "missile defense",
        ImbuedEffectType.MagicDefense => "magic defense",
        _ => effect.ToString().Replace("Rending", " rending").ToLowerInvariant()
    };

    public sealed class TemporarilyHiddenPrimaryImbue(WorldObject target, int? value)
    {
        private bool restored;

        public void Restore()
        {
            if (restored)
                return;

            MultiImbueRules.RestoreProperty(target, PropertyInt.ImbuedEffect, value);
            restored = true;
        }
    }
}

[HarmonyPatch]
public static class ImbueMutationPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(RecipeManager), nameof(RecipeManager.TryMutate),
        new[]
        {
            typeof(Player), typeof(WorldObject), typeof(WorldObject), typeof(Recipe), typeof(uint), typeof(HashSet<uint>)
        }) ?? throw new MissingMethodException(typeof(RecipeManager).FullName, nameof(RecipeManager.TryMutate));

    [HarmonyPrefix]
    public static void PreserveEarlierImbues(WorldObject source, WorldObject target, Recipe recipe, uint dataId,
        out PendingAdditionalImbue? __state)
    {
        __state = null;
        if (!recipe.IsImbuing() || MultiImbueRules.CountImbues(target) == 0 ||
            !MultiImbueRules.IsSuccessfulRecipeMutation(recipe, dataId) ||
            !MultiImbueRules.TryGetCraftedEffect(source.MaterialType, out var requestedEffect))
            return;

        __state = new PendingAdditionalImbue(target, requestedEffect,
            target.GetProperty(PropertyInt.ImbuedEffect));
    }

    [HarmonyPostfix]
    public static void StoreAdditionalImbue(bool __result, PendingAdditionalImbue? __state) =>
        __state?.Complete(__result);

    [HarmonyFinalizer]
    public static Exception? RestoreAfterMutationFailure(Exception? __exception, PendingAdditionalImbue? __state)
    {
        __state?.Complete(false);
        return __exception;
    }

    public sealed class PendingAdditionalImbue(WorldObject target, ImbuedEffectType requestedEffect,
        int? originalPrimary)
    {
        private bool completed;

        public void Complete(bool mutationSucceeded)
        {
            if (completed)
                return;

            var mutationPrimary = (ImbuedEffectType)(uint)(target.GetProperty(PropertyInt.ImbuedEffect) ?? 0);
            MultiImbueRules.RestoreProperty(target, PropertyInt.ImbuedEffect, originalPrimary);

            if (mutationSucceeded && mutationPrimary.HasFlag(requestedEffect))
            {
                var slot = MultiImbueRules.FindFirstWritableSlot(target)
                    ?? throw new InvalidOperationException("No persistent imbue slot remained after successful validation.");
                target.SetProperty(slot, (int)requestedEffect);
            }

            completed = true;
        }
    }
}

[HarmonyPatch(typeof(WorldObject), nameof(WorldObject.HasImbuedEffect))]
public static class CombinedWeaponImbuePatch
{
    [HarmonyPostfix]
    public static void IncludeSecondarySlots(WorldObject __instance, ImbuedEffectType type, ref bool __result)
    {
        if (!__result)
            __result = MultiImbueRules.GetCombinedEffects(__instance).HasFlag(type);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.GetDefenseImbues))]
public static class CombinedDefenseImbuePatch
{
    [HarmonyPrefix]
    public static bool IncludeSecondarySlots(Creature __instance, ImbuedEffectType imbuedEffectType,
        ref int __result)
    {
        __result = __instance.EquippedObjects.Values.Count(item =>
            MultiImbueRules.GetCombinedEffects(item).HasFlag(imbuedEffectType));
        return false;
    }
}
