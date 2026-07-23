using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using System.IO.Compression;
using System.Reflection.Emit;
using System.Security.Cryptography;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Database.Models.World;
using ACE.Server.Command;
using ACE.Server.Command.Handlers;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using ACE.SinglePlayer.Mods;

using HarmonyLib;

namespace ACE.SinglePlayer.Tests;

[TestClass]
public sealed class ModTests
{
    [TestMethod]
    public void AquafirCatalogHasUsefulDescriptionsAndSafetyPolicies()
    {
        Assert.AreEqual(22, AquafirSampleCatalog.Entries.Count);
        Assert.AreEqual(22, AquafirSampleCatalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(AquafirSampleCatalog.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.Description)));
        Assert.IsTrue(AquafirSampleCatalog.Entries.All(entry => !entry.Description.Contains("non-existent mod", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(AquafirSampleCatalog.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.SafetyNotice)));

        var criticalOverride = AquafirSampleCatalog.Entries.Single(entry => entry.Id == "aquafir.critical-override");
        Assert.AreEqual(ModCatalogAvailability.Ready, criticalOverride.Availability);
        Assert.AreEqual(ModRemovalPolicy.Safe, criticalOverride.RemovalPolicy);

        foreach (var preview in AquafirSampleCatalog.Entries.Where(entry => entry.Availability == ModCatalogAvailability.Preview))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PackageRelativePath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.SourceUrl));
            Assert.IsFalse(string.IsNullOrWhiteSpace(preview.PortSourceUrl));
        }
        Assert.AreEqual(2, AquafirSampleCatalog.Entries.Count(entry => entry.Availability == ModCatalogAvailability.Preview));
    }

    [TestMethod]
    public void CuratedCatalogIncludesCustomClothingBaseAsWarnedPreview()
    {
        Assert.AreEqual(31, CuratedModCatalog.Entries.Count);
        var entry = CuratedModCatalog.Entries.Single(item => item.Id == "optimshi.custom-clothing-base");
        Assert.AreEqual("OptimShi", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.WorldData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.DoNotRemove, entry.RemovalPolicy);
        Assert.IsTrue(entry.SourceUrl.Contains("OptimShi/CustomClothingBase", StringComparison.Ordinal));
        Assert.IsTrue(entry.PreviewNotice.Contains("not been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CuratedCatalogIncludesUniqueWeeniesProcAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "titaniumweiner.ace-unique-weenies-proc");

        Assert.AreEqual("Expanded Cast on Strike", entry.Name);
        Assert.AreEqual("titaniumweiner", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.SettingsOnly, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.Safe, entry.RemovalPolicy);
        Assert.IsTrue(entry.SourceUrl.Contains("titaniumweiner/ACEUniqueWeenies", StringComparison.Ordinal));
        Assert.IsTrue(entry.PreviewNotice.Contains("not been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CuratedCatalogIncludesUnlimitedStatAugmentationAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.unlimited-stat-augmentation-gems");

        Assert.AreEqual("Unlimited Stat Augmentation Gems", entry.Name);
        Assert.AreEqual("OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.CharacterData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.ChangesRemain, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("100", StringComparison.Ordinal));
        CollectionAssert.Contains(entry.ConflictIds?.ToArray() ?? Array.Empty<string>(), "aquafir.quality-of-life");
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DoNotParallelize]
    public void UnlimitedStatAugmentationChangesOnlyStatGemLimitsAndCanBeRemoved()
    {
        var originalLimits = AugmentationDevice.MaxAugs.ToDictionary(pair => pair.Key, pair => pair.Value);
        var safetyGetter = AccessTools.PropertyGetter(typeof(AugmentationDevice),
            nameof(AugmentationDevice.AttributeAugmentationSafetyCapEnabled));
        Assert.IsNotNull(safetyGetter);

        var mod = new UnlimitedStatAugmentation.Mod();
        try
        {
            mod.Initialize();

            CollectionAssert.AreEquivalent(new[]
            {
                AugmentationType.Strength,
                AugmentationType.Endurance,
                AugmentationType.Coordination,
                AugmentationType.Quickness,
                AugmentationType.Focus,
                AugmentationType.Self
            }, UnlimitedStatAugmentation.Mod.AttributeTypes.ToArray());

            foreach (var type in UnlimitedStatAugmentation.Mod.AttributeTypes)
                Assert.AreEqual(int.MaxValue, AugmentationDevice.MaxAugs[type]);

            foreach (var (type, limit) in originalLimits.Where(pair =>
                         !UnlimitedStatAugmentation.Mod.AttributeTypes.Contains(pair.Key)))
                Assert.AreEqual(limit, AugmentationDevice.MaxAugs[type], $"{type} should keep its stock limit.");

            var patchInfo = Harmony.GetPatchInfo(safetyGetter);
            Assert.IsNotNull(patchInfo);
            Assert.IsTrue(patchInfo.Postfixes.Any(patch =>
                patch.owner == UnlimitedStatAugmentation.Mod.HarmonyId));
            var safetyCapEnabled = false;
            UnlimitedStatAugmentation.AttributeSafetyCapPatch.KeepStatMaximumAtOneHundred(ref safetyCapEnabled);
            Assert.IsTrue(safetyCapEnabled);
        }
        finally
        {
            try
            {
                mod.Dispose();
            }
            finally
            {
                foreach (var (type, limit) in originalLimits)
                    AugmentationDevice.MaxAugs[type] = limit;
            }
        }

        foreach (var (type, limit) in originalLimits)
            Assert.AreEqual(limit, AugmentationDevice.MaxAugs[type]);

        var remaining = Harmony.GetPatchInfo(safetyGetter);
        Assert.IsTrue(remaining is null || remaining.Postfixes.All(patch =>
            patch.owner != UnlimitedStatAugmentation.Mod.HarmonyId));
    }

    [TestMethod]
    public void CuratedCatalogIncludesUnlimitedSkillSpecializationsAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.unlimited-skill-specializations");

        Assert.AreEqual("Unlimited Skill Specializations", entry.Name);
        Assert.AreEqual("OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.CharacterData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.ChangesRemain, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("70", StringComparison.Ordinal));
        CollectionAssert.Contains(entry.ConflictIds?.ToArray() ?? Array.Empty<string>(), "aquafir.quality-of-life");
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CuratedCatalogIncludesUniversalLootLuckAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.universal-loot-luck");

        Assert.AreEqual("All-Tier Salvage & Loot Luck", entry.Name);
        Assert.AreEqual("OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.CharacterData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.ChangesRemain, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("material", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.Contains(entry.ConflictIds?.ToArray() ?? Array.Empty<string>(), "aquafir.expansion");
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CuratedCatalogIncludesThreeImbuesAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item => item.Id == "opendereth.three-imbues");

        Assert.AreEqual("Three Imbues", entry.Name);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.CharacterData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.DoNotRemove, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("three", StringComparison.OrdinalIgnoreCase));
        CollectionAssert.Contains(entry.ConflictIds?.ToArray() ?? Array.Empty<string>(), "aquafir.tinkering");
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ThreeImbuesMapsMaterialsCountsDistinctEffectsAndBoundsSettings()
    {
        Assert.IsTrue(MultiImbue.MultiImbueRules.TryGetCraftedEffect(
            MaterialType.Sunstone, out var armorRending));
        Assert.AreEqual(ImbuedEffectType.ArmorRending, armorRending);
        Assert.IsTrue(MultiImbue.MultiImbueRules.TryGetCraftedEffect(
            MaterialType.BlackOpal, out var criticalStrike));
        Assert.AreEqual(ImbuedEffectType.CriticalStrike, criticalStrike);
        Assert.IsTrue(MultiImbue.MultiImbueRules.TryGetCraftedEffect(
            MaterialType.FireOpal, out var cripplingBlow));
        Assert.AreEqual(ImbuedEffectType.CripplingBlow, cripplingBlow);
        Assert.IsFalse(MultiImbue.MultiImbueRules.TryGetCraftedEffect(
            MaterialType.Iron, out _));

        var combined = MultiImbue.MultiImbueRules.GetCombinedEffects(new int?[]
        {
            (int)armorRending,
            (int)criticalStrike,
            (int)cripplingBlow,
            (int)criticalStrike,
            null
        });
        Assert.AreEqual(3, MultiImbue.MultiImbueRules.CountImbues(combined),
            "A duplicate bit in another ACE slot must not consume a fourth imbue.");
        Assert.IsTrue(combined.HasFlag(ImbuedEffectType.ArmorRending));
        Assert.IsTrue(combined.HasFlag(ImbuedEffectType.CriticalStrike));
        Assert.IsTrue(combined.HasFlag(ImbuedEffectType.CripplingBlow));

        new MultiImbue.MultiImbueSettings { MaximumImbues = 1 }.Validate();
        new MultiImbue.MultiImbueSettings { MaximumImbues = 3 }.Validate();
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new MultiImbue.MultiImbueSettings { MaximumImbues = 0 }.Validate());
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new MultiImbue.MultiImbueSettings { MaximumImbues = 4 }.Validate());

        var recipe = new Recipe
        {
            RecipeMod = new List<RecipeMod>
            {
                new() { ExecutesOnSuccess = true, DataId = 0x10001234 },
                new() { ExecutesOnSuccess = false, DataId = 0x10005678 }
            }
        };
        Assert.IsTrue(MultiImbue.MultiImbueRules.IsSuccessfulRecipeMutation(recipe, 0x10001234));
        Assert.IsFalse(MultiImbue.MultiImbueRules.IsSuccessfulRecipeMutation(recipe, 0x10005678),
            "A failed imbue's mutation data must never be persisted as a successful secondary effect.");
    }

    [TestMethod]
    public void ThreeImbuesStoresSuccessfulEffectsInSeparatePersistentSlots()
    {
        var weapon = new MeleeWeapon(new Biota
        {
            Id = 0x7FFFFFF0,
            WeenieClassId = 1,
            WeenieType = WeenieType.MeleeWeapon,
            PropertiesInt = new Dictionary<PropertyInt, int>
            {
                [PropertyInt.ImbuedEffect] = (int)ImbuedEffectType.ArmorRending
            }
        });

        var second = new MultiImbue.ImbueMutationPatch.PendingAdditionalImbue(
            weapon, ImbuedEffectType.CriticalStrike, weapon.GetProperty(PropertyInt.ImbuedEffect));
        weapon.SetProperty(PropertyInt.ImbuedEffect, (int)ImbuedEffectType.CriticalStrike);
        second.Complete(true);

        Assert.AreEqual((int)ImbuedEffectType.ArmorRending,
            weapon.GetProperty(PropertyInt.ImbuedEffect));
        Assert.AreEqual((int)ImbuedEffectType.CriticalStrike,
            weapon.GetProperty(PropertyInt.ImbuedEffect2));

        var third = new MultiImbue.ImbueMutationPatch.PendingAdditionalImbue(
            weapon, ImbuedEffectType.CripplingBlow, weapon.GetProperty(PropertyInt.ImbuedEffect));
        weapon.SetProperty(PropertyInt.ImbuedEffect, (int)ImbuedEffectType.CripplingBlow);
        third.Complete(true);

        Assert.AreEqual((int)ImbuedEffectType.CripplingBlow,
            weapon.GetProperty(PropertyInt.ImbuedEffect3));
        Assert.AreEqual(3, MultiImbue.MultiImbueRules.CountImbues(weapon));
        Assert.IsNull(MultiImbue.MultiImbueRules.FindFirstWritableSlot(weapon));

        var failedWeapon = new MeleeWeapon(new Biota
        {
            Id = 0x7FFFFFF1,
            WeenieClassId = 2,
            WeenieType = WeenieType.MeleeWeapon,
            PropertiesInt = new Dictionary<PropertyInt, int>
            {
                [PropertyInt.ImbuedEffect] = (int)ImbuedEffectType.ArmorRending
            }
        });
        var failed = new MultiImbue.ImbueMutationPatch.PendingAdditionalImbue(
            failedWeapon, ImbuedEffectType.CriticalStrike,
            failedWeapon.GetProperty(PropertyInt.ImbuedEffect));
        failedWeapon.SetProperty(PropertyInt.ImbuedEffect, (int)ImbuedEffectType.CriticalStrike);
        failed.Complete(false);

        Assert.AreEqual((int)ImbuedEffectType.ArmorRending,
            failedWeapon.GetProperty(PropertyInt.ImbuedEffect));
        Assert.IsNull(failedWeapon.GetProperty(PropertyInt.ImbuedEffect2));
    }

    [TestMethod]
    [DoNotParallelize]
    public void ThreeImbuesTargetsPinnedAceSignaturesAndCanBeRemoved()
    {
        var targets = new[]
        {
            AccessTools.Method(typeof(RecipeManager), nameof(RecipeManager.VerifyRequirements),
                new[] { typeof(Recipe), typeof(Player), typeof(WorldObject), typeof(WorldObject) }),
            AccessTools.Method(typeof(RecipeManager), nameof(RecipeManager.TryMutate),
                new[]
                {
                    typeof(Player), typeof(WorldObject), typeof(WorldObject), typeof(Recipe), typeof(uint),
                    typeof(HashSet<uint>)
                }),
            AccessTools.Method(typeof(WorldObject), nameof(WorldObject.HasImbuedEffect),
                new[] { typeof(ImbuedEffectType) }),
            AccessTools.Method(typeof(Creature), nameof(Creature.GetDefenseImbues),
                new[] { typeof(ImbuedEffectType) })
        };
        Assert.IsTrue(targets.All(target => target is not null));

        var mod = new MultiImbue.Mod();
        try
        {
            mod.Initialize();
            foreach (var target in targets)
            {
                var patchInfo = Harmony.GetPatchInfo(target!);
                Assert.IsNotNull(patchInfo, target!.Name);
                Assert.IsTrue(patchInfo.Owners.Contains(MultiImbue.Mod.HarmonyId), target.Name);
            }
        }
        finally
        {
            mod.Dispose();
        }

        foreach (var target in targets)
        {
            var patchInfo = Harmony.GetPatchInfo(target!);
            Assert.IsTrue(patchInfo is null || !patchInfo.Owners.Contains(MultiImbue.Mod.HarmonyId),
                target!.Name);
        }
    }

    [TestMethod]
    public void CuratedCatalogIncludesHousingUpgradePackAsWarnedPreview()
    {
        Assert.AreEqual(31, CuratedModCatalog.Entries.Count);
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.housing-upgrade-pack");

        Assert.AreEqual("Housing Upgrade Pack", entry.Name);
        Assert.AreEqual("OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.WorldData, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.ChangesRemain, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("Mansion", StringComparison.Ordinal));
        Assert.IsTrue(entry.Details.Contains("15-day", StringComparison.Ordinal));
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void HousingUpgradeSettingsProvideBoundedProgressiveCapacities()
    {
        var settings = new HousingUpgradePack.HousingUpgradeSettings();
        settings.Validate();

        Assert.AreEqual((byte?)150, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Apartment, 120, settings));
        Assert.AreEqual((byte?)180, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Cottage, 120, settings));
        Assert.AreEqual((byte?)220, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Villa, 120, settings));
        Assert.AreEqual((byte?)255, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Mansion, 120, settings));

        Assert.AreEqual((byte?)12, HousingUpgradePack.HousingCapacityResolver.ResolvePackCapacity(
            HouseType.Apartment, 10, settings));
        Assert.AreEqual((byte?)15, HousingUpgradePack.HousingCapacityResolver.ResolvePackCapacity(
            HouseType.Cottage, 10, settings));
        Assert.AreEqual((byte?)20, HousingUpgradePack.HousingCapacityResolver.ResolvePackCapacity(
            HouseType.Villa, 10, settings));
        Assert.AreEqual((byte?)25, HousingUpgradePack.HousingCapacityResolver.ResolvePackCapacity(
            HouseType.Mansion, 10, settings));

        Assert.AreEqual((byte?)240, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Cottage, 240, settings), "A larger world-content capacity must not be reduced.");
        Assert.AreEqual((byte?)120, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Undef, 120, settings), "Non-housing storage must not change.");

        settings.IncreaseStorageCapacity = false;
        Assert.AreEqual((byte?)120, HousingUpgradePack.HousingCapacityResolver.ResolveItemCapacity(
            HouseType.Mansion, 120, settings));

        Assert.ThrowsExactly<InvalidDataException>(() => new HousingUpgradePack.HousingUpgradeSettings
        {
            Apartment = new HousingUpgradePack.HousingCapacitySettings(256, 12)
        }.Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new HousingUpgradePack.HousingUpgradeSettings
        {
            Villa = new HousingUpgradePack.HousingCapacitySettings(220, -1)
        }.Validate());
    }

    [TestMethod]
    public void HousingUpgradeOptionsOverrideOnlyTheirExactAceProperties()
    {
        var settings = new HousingUpgradePack.HousingUpgradeSettings
        {
            RemoveHookLimits = true,
            RemoveRentAndMaintenance = true,
            RemoveMansionAllegianceRankRequirement = true,
            RemoveHousePurchaseTimers = true
        };
        var enabled = new Property<bool>(true, "stock description");

        Assert.IsFalse(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_hook_limit", enabled, settings).Item);
        Assert.IsFalse(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_hookgroup_limit", enabled, settings).Item);
        Assert.IsFalse(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_rent_enabled", enabled, settings).Item);
        Assert.IsFalse(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_15day_account", enabled, settings).Item);
        Assert.IsFalse(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_30day_cooldown", enabled, settings).Item);
        Assert.IsTrue(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_purchase_requirements", enabled, settings).Item);
        Assert.AreEqual("stock description", HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_rent_enabled", enabled, settings).Description);

        var rank = new Property<long>(6, "mansion rank");
        Assert.AreEqual(0L, HousingUpgradePack.HousingPropertyOverrides.ApplyLong(
            "mansion_min_rank", rank, settings).Item);
        Assert.AreEqual(6L, HousingUpgradePack.HousingPropertyOverrides.ApplyLong(
            "unrelated_long", rank, settings).Item);

        var defaults = new HousingUpgradePack.HousingUpgradeSettings();
        Assert.IsTrue(HousingUpgradePack.HousingPropertyOverrides.ApplyBool(
            "house_rent_enabled", enabled, defaults).Item);
        Assert.AreEqual(6L, HousingUpgradePack.HousingPropertyOverrides.ApplyLong(
            "mansion_min_rank", rank, defaults).Item);
    }

    [TestMethod]
    [DoNotParallelize]
    public void HousingUpgradePackTargetsPinnedAcePropertiesAndCanBeRemoved()
    {
        var itemCapacity = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.ItemCapacity));
        var packCapacity = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.ContainerCapacity));
        var boolProperty = AccessTools.Method(typeof(PropertyManager), nameof(PropertyManager.GetBool),
            new[] { typeof(string), typeof(bool), typeof(bool) });
        var longProperty = AccessTools.Method(typeof(PropertyManager), nameof(PropertyManager.GetLong),
            new[] { typeof(string), typeof(long), typeof(bool) });

        Assert.IsNotNull(itemCapacity);
        Assert.IsNotNull(packCapacity);
        Assert.IsNotNull(boolProperty);
        Assert.IsNotNull(longProperty);

        var targets = new[] { itemCapacity, packCapacity, boolProperty, longProperty };
        var mod = new HousingUpgradePack.Mod();
        try
        {
            mod.Initialize();
            foreach (var target in targets)
            {
                var patchInfo = Harmony.GetPatchInfo(target);
                Assert.IsNotNull(patchInfo, target.Name);
                Assert.IsTrue(patchInfo.Postfixes.Any(patch =>
                    patch.owner == HousingUpgradePack.Mod.HarmonyId), target.Name);
            }
        }
        finally
        {
            mod.Dispose();
        }

        foreach (var target in targets)
        {
            var remaining = Harmony.GetPatchInfo(target);
            Assert.IsTrue(remaining is null || remaining.Postfixes.All(patch =>
                patch.owner != HousingUpgradePack.Mod.HarmonyId), target.Name);
        }
    }

    [TestMethod]
    public void CuratedCatalogIncludesLandblockSummonBalanceAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.landblock-summon-balance");

        Assert.AreEqual("Landblock Summon Balance", entry.Name);
        Assert.AreEqual("OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.SettingsOnly, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.Safe, entry.RemovalPolicy);
        Assert.IsTrue(entry.Description.Contains("landblock", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LandblockSummonBalanceResolvesOpponentZonesAndExactCellOverrides()
    {
        var settings = new LandblockSummonBalance.LandblockSummonSettings
        {
            Zones = new()
            {
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Whole landblock",
                    Enabled = true,
                    Priority = 100,
                    MatchLocation = LandblockSummonBalance.ZoneMatchLocation.Opponent,
                    Landblocks = new() { "0xA9B4" },
                    PhysicalDamageMultiplier = 0.5
                },
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Exact room",
                    Enabled = true,
                    Priority = -100,
                    MatchLocation = LandblockSummonBalance.ZoneMatchLocation.Opponent,
                    ExactCells = new() { "0xA9B4012F" },
                    PhysicalDamageMultiplier = 0.25
                },
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Summon location",
                    Enabled = true,
                    Priority = 200,
                    MatchLocation = LandblockSummonBalance.ZoneMatchLocation.Summon,
                    Landblocks = new() { "0xC0DE" },
                    PhysicalDamageMultiplier = 0.1
                }
            }
        }.Compile();

        Assert.AreEqual("Whole landblock", settings.Resolve(0x11110001, 0xA9B40020)?.Name);
        Assert.AreEqual("Exact room", settings.Resolve(0x11110001, 0xA9B4012F)?.Name,
            "An exact cell should win over a higher-priority whole-landblock rule.");
        Assert.AreEqual("Summon location", settings.Resolve(0xC0DE0001, 0x11110001)?.Name);
        Assert.IsNull(settings.Resolve(0x11110001, 0x22220001));
    }

    [TestMethod]
    public void LandblockSummonBalanceValidatesIdsAndBoundsScaling()
    {
        Assert.AreEqual(40.0f, LandblockSummonBalance.SummonBalanceResolver.ScaleDamage(100.0f, 0.4), 0.001f);
        Assert.AreEqual((uint)225, LandblockSummonBalance.SummonBalanceResolver.ScaleSkill(300, 0.75));

        Assert.ThrowsExactly<InvalidDataException>(() => new LandblockSummonBalance.LandblockSummonSettings
        {
            Zones = new()
            {
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Bad ID",
                    Landblocks = new() { "A9B" }
                }
            }
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new LandblockSummonBalance.LandblockSummonSettings
        {
            Zones = new()
            {
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Bad multiplier",
                    Landblocks = new() { "A9B4" },
                    PhysicalDamageMultiplier = 10.01
                }
            }
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new LandblockSummonBalance.LandblockSummonSettings
        {
            Zones = new()
            {
                new LandblockSummonBalance.SummonBalanceZone
                {
                    Name = "Enabled but empty"
                }
            }
        }.Compile());
    }

    [TestMethod]
    [DoNotParallelize]
    public void LandblockSummonBalanceTargetsPinnedAceSignaturesAndCanBeRemoved()
    {
        var physicalDamage = AccessTools.Method(typeof(DamageEvent), "DoCalculateDamage",
            new[] { typeof(Creature), typeof(Creature), typeof(WorldObject) });
        var physicalSkill = AccessTools.Method(typeof(DamageEvent), nameof(DamageEvent.GetEvadeChance),
            new[] { typeof(Creature), typeof(Creature) });
        var spellDamage = AccessTools.Method(typeof(SpellProjectile), nameof(SpellProjectile.CalculateDamage),
            new[] { typeof(WorldObject), typeof(Creature), typeof(bool).MakeByRefType(),
                typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() });
        Assert.IsNotNull(physicalDamage);
        Assert.IsNotNull(physicalSkill);
        Assert.IsNotNull(spellDamage);

        var targets = new[] { physicalDamage, physicalSkill, spellDamage };
        var mod = new LandblockSummonBalance.Mod();
        try
        {
            mod.Initialize();
            foreach (var target in targets)
            {
                var patchInfo = Harmony.GetPatchInfo(target);
                Assert.IsNotNull(patchInfo, target.Name);
                Assert.IsTrue(patchInfo.Postfixes.Any(patch =>
                    patch.owner == LandblockSummonBalance.Mod.HarmonyId), target.Name);
            }

            Assert.AreEqual(1, CommandManager.GetCommandByName("summonbalance").Count());
        }
        finally
        {
            mod.Dispose();
        }

        foreach (var target in targets)
        {
            var remaining = Harmony.GetPatchInfo(target);
            Assert.IsTrue(remaining is null || remaining.Postfixes.All(patch =>
                patch.owner != LandblockSummonBalance.Mod.HarmonyId), target.Name);
        }
        Assert.AreEqual(0, CommandManager.GetCommandByName("summonbalance").Count());
    }

    [TestMethod]
    public void CuratedCatalogIncludesAquafirCreatureVariantsAsWarnedPreview()
    {
        var entry = CuratedModCatalog.Entries.Single(item =>
            item.Id == "opendereth.aquafir-creature-variants");

        Assert.AreEqual("Aquafir Creature Variants", entry.Name);
        Assert.AreEqual("Aquafir and OpenDereth", entry.Author);
        Assert.AreEqual(ModCatalogAvailability.Preview, entry.Availability);
        Assert.AreEqual(ModDataImpact.SettingsOnly, entry.DataImpact);
        Assert.AreEqual(ModRemovalPolicy.Safe, entry.RemovalPolicy);
        Assert.IsTrue(entry.SourceUrl.Contains("aquafir/ACE.BaseMod", StringComparison.Ordinal));
        Assert.IsTrue(entry.Description.Contains("eighteen", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.Details.Contains("Rogue", StringComparison.Ordinal));
        Assert.IsTrue(entry.Details.Contains("Stunner", StringComparison.Ordinal));
        CollectionAssert.Contains(entry.ConflictIds?.ToArray() ?? Array.Empty<string>(), "aquafir.expansion");
        Assert.IsTrue(entry.PreviewNotice.Contains("not yet been thoroughly tested", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(entry.PackageRelativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AquafirCreatureVariantSettingsValidateAndSelectPredictably()
    {
        var defaults = new AquafirCreatureVariants.CreatureVariantSettings();
        Assert.AreEqual(18, Enum.GetValues<AquafirCreatureVariants.CreatureVariantType>().Length);
        Assert.AreEqual(0.50, defaults.AssignmentChance, 0.0001);
        Assert.IsTrue(defaults.AllowVariantStacking);
        Assert.AreEqual(0.50, defaults.AdditionalVariantChance, 0.0001);
        Assert.AreEqual(3, defaults.MaximumVariantsPerCreature);
        Assert.AreEqual(18, defaults.Compile().WeightedTraits.Count);
        foreach (var advanced in new[]
                 {
                     AquafirCreatureVariants.CreatureVariantType.Rogue,
                     AquafirCreatureVariants.CreatureVariantType.Horde,
                     AquafirCreatureVariants.CreatureVariantType.Puppeteer,
                     AquafirCreatureVariants.CreatureVariantType.Boss,
                     AquafirCreatureVariants.CreatureVariantType.Tank,
                     AquafirCreatureVariants.CreatureVariantType.Stunner
                 })
            Assert.IsTrue(defaults.TraitWeights[advanced] > 0.0, $"{advanced} must be available randomly by default.");

        var settings = new AquafirCreatureVariants.CreatureVariantSettings
        {
            AssignmentChance = 0.25,
            MinimumLevel = 10,
            MaximumLevel = 100,
            ExcludedWeenieClassIds = new() { 99 },
            ForcedTraitsByWeenieClassId = new()
            {
                ["1234"] = AquafirCreatureVariants.CreatureVariantType.Shielded
            },
            TraitWeights = new()
            {
                [AquafirCreatureVariants.CreatureVariantType.Accurate] = 1.0,
                [AquafirCreatureVariants.CreatureVariantType.Vampire] = 3.0
            }
        }.Compile();

        Assert.IsTrue(settings.IsEligible(100, 10));
        Assert.IsFalse(settings.IsEligible(100, 9));
        Assert.IsFalse(settings.IsEligible(99, 50));
        Assert.AreEqual(AquafirCreatureVariants.CreatureVariantType.Shielded,
            settings.SelectTrait(1234, 0.99, 0.99));
        Assert.IsNull(settings.SelectTrait(100, 0.25, 0.0));
        Assert.AreEqual(AquafirCreatureVariants.CreatureVariantType.Accurate,
            settings.SelectTrait(100, 0.10, 0.0));
        Assert.AreEqual(AquafirCreatureVariants.CreatureVariantType.Vampire,
            settings.SelectTrait(100, 0.10, 0.50));
        CollectionAssert.AreEqual(
            new[]
            {
                AquafirCreatureVariants.CreatureVariantType.Accurate,
                AquafirCreatureVariants.CreatureVariantType.Vampire
            },
            settings.SelectTraits(100, 0.10, new[] { 0.0, 0.0, 0.0 }).ToArray(),
            "A successful stacking roll must choose a distinct second weighted trait.");
        Assert.AreEqual(0, settings.SelectTraits(100, 0.25, new[] { 0.0 }).Count);

        var noStacking = new AquafirCreatureVariants.CreatureVariantSettings
        {
            AssignmentChance = 1.0,
            AllowVariantStacking = false,
            TraitWeights = new()
            {
                [AquafirCreatureVariants.CreatureVariantType.Accurate] = 1.0,
                [AquafirCreatureVariants.CreatureVariantType.Vampire] = 1.0
            }
        }.Compile();
        CollectionAssert.AreEqual(
            new[] { AquafirCreatureVariants.CreatureVariantType.Accurate },
            noStacking.SelectTraits(100, 0.0, new[] { 0.0 }).ToArray());

        var cappedStacking = new AquafirCreatureVariants.CreatureVariantSettings
        {
            AssignmentChance = 1.0,
            AllowVariantStacking = true,
            AdditionalVariantChance = 1.0,
            MaximumVariantsPerCreature = 3,
            TraitWeights = new()
            {
                [AquafirCreatureVariants.CreatureVariantType.Accurate] = 1.0,
                [AquafirCreatureVariants.CreatureVariantType.Berserker] = 1.0,
                [AquafirCreatureVariants.CreatureVariantType.Evader] = 1.0,
                [AquafirCreatureVariants.CreatureVariantType.Vampire] = 1.0
            }
        }.Compile();
        var cappedTraits = cappedStacking.SelectTraits(100, 0.0, new[] { 0.0, 0.0, 0.0, 0.0, 0.0 });
        Assert.AreEqual(3, cappedTraits.Count);
        Assert.AreEqual(3, cappedTraits.Distinct().Count(), "Stacked variants must never repeat.");
        Assert.AreEqual(1.0,
            AquafirCreatureVariants.CreatureVariantRuntime.GetHordeDamageMultiplier(1, defaults), 0.0001);
        Assert.AreEqual(1.75,
            AquafirCreatureVariants.CreatureVariantRuntime.GetHordeDamageMultiplier(6, defaults), 0.0001);

        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            AssignmentChance = 1.01
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            AdditionalVariantChance = -0.01
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            MaximumVariantsPerCreature = 19
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            AssignmentChance = 0.1,
            TraitWeights = new()
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            ForcedTraitsByWeenieClassId = new()
            {
                ["1234"] = AquafirCreatureVariants.CreatureVariantType.Accurate,
                ["0x4D2"] = AquafirCreatureVariants.CreatureVariantType.Evader
            }
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            HordeMinimumMembers = 7,
            HordeMaximumMembers = 6
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            BossSpellIds = new() { 0 }
        }.Compile());
        Assert.ThrowsExactly<InvalidDataException>(() => new AquafirCreatureVariants.CreatureVariantSettings
        {
            StunnerDurationSeconds = 31.0
        }.Compile());

        var forcedBoss = new AquafirCreatureVariants.CreatureVariantSettings
        {
            ForcedTraitsByWeenieClassId = new()
            {
                ["2000"] = AquafirCreatureVariants.CreatureVariantType.Boss
            }
        }.Compile();
        CollectionAssert.AreEqual(
            new[] { AquafirCreatureVariants.CreatureVariantType.Boss },
            forcedBoss.SelectTraits(2000, 0.99, Array.Empty<double>()).ToArray(),
            "A forced WCID mapping must remain exact and bypass random stacking.");
    }

    [TestMethod]
    [DoNotParallelize]
    public void AquafirCreatureVariantsTargetsPinnedAceSignaturesAndCanBeRemoved()
    {
        var factoryTargets = AccessTools.GetDeclaredMethods(typeof(WorldObjectFactory))
            .Where(method => method.Name == nameof(WorldObjectFactory.CreateWorldObject) &&
                method.ReturnType == typeof(WorldObject))
            .Cast<System.Reflection.MethodBase>()
            .ToArray();
        var damage = AccessTools.Method(typeof(DamageEvent), "DoCalculateDamage",
            new[] { typeof(Creature), typeof(Creature), typeof(WorldObject) });
        var evade = AccessTools.Method(typeof(DamageEvent), nameof(DamageEvent.GetEvadeChance),
            new[] { typeof(Creature), typeof(Creature) });
        var takeDamage = AccessTools.Method(typeof(Creature), nameof(Creature.TakeDamage),
            new[] { typeof(WorldObject), typeof(DamageType), typeof(float), typeof(bool) });
        var heartbeat = AccessTools.Method(typeof(Creature), nameof(Creature.Heartbeat),
            new[] { typeof(double) });
        var attribute = AccessTools.Method(typeof(CreatureAttribute), nameof(CreatureAttribute.GetCurrent),
            new[] { typeof(bool) });
        var vital = AccessTools.Method(typeof(CreatureVital), nameof(CreatureVital.GetMaxValue),
            new[] { typeof(bool) });
        var nameGetter = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.Name));
        var scaleGetter = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.ObjScale));
        var xpGetter = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.XpOverride));
        var spellResist = AccessTools.Method(typeof(WorldObject), nameof(WorldObject.TryResistSpell),
            new[] { typeof(WorldObject), typeof(ACE.Server.Entity.Spell), typeof(WorldObject), typeof(bool) });

        Assert.AreEqual(3, factoryTargets.Length);
        Assert.IsNotNull(damage);
        Assert.IsNotNull(evade);
        Assert.IsNotNull(takeDamage);
        Assert.IsNotNull(heartbeat);
        Assert.IsNotNull(attribute);
        Assert.IsNotNull(vital);
        Assert.IsNotNull(nameGetter);
        Assert.IsNotNull(scaleGetter);
        Assert.IsNotNull(xpGetter);
        Assert.IsNotNull(spellResist);
        var targets = factoryTargets.Concat(new[]
        {
            damage, evade, takeDamage, heartbeat, attribute, vital, nameGetter, scaleGetter, xpGetter, spellResist
        }).ToArray();

        var mod = new AquafirCreatureVariants.Mod();
        try
        {
            mod.Initialize();
            foreach (var target in targets)
            {
                var patchInfo = Harmony.GetPatchInfo(target);
                Assert.IsNotNull(patchInfo, target.Name);
                Assert.IsTrue(patchInfo.Prefixes.Concat(patchInfo.Postfixes).Any(patch =>
                    patch.owner == AquafirCreatureVariants.Mod.HarmonyId), target.Name);
            }

            Assert.AreEqual(1, CommandManager.GetCommandByName("creaturevariants").Count());
        }
        finally
        {
            mod.Dispose();
        }

        foreach (var target in targets)
        {
            var remaining = Harmony.GetPatchInfo(target);
            Assert.IsTrue(remaining is null || remaining.Prefixes.Concat(remaining.Postfixes).All(patch =>
                patch.owner != AquafirCreatureVariants.Mod.HarmonyId), target.Name);
        }
        Assert.AreEqual(0, CommandManager.GetCommandByName("creaturevariants").Count());
    }

    [TestMethod]
    public void UniversalLootLuckSettingsAndProfileAdjustmentAreBoundedAndNonMutating()
    {
        var source = new TreasureDeath
        {
            Id = 7,
            TreasureType = 8,
            Tier = 4,
            LootQualityMod = 0.10f,
            ItemChance = 40,
            MagicItemChance = 70,
            MundaneItemChance = 10,
            ItemMinAmount = 1,
            ItemMaxAmount = 2,
            MagicItemMinAmount = 3,
            MagicItemMaxAmount = 4,
            MundaneItemMinAmount = 5,
            MundaneItemMaxAmount = 6
        };
        var settings = new UniversalLootLuck.LootLuckSettings
        {
            LootQualityBonus = 0.25f,
            GeneratedLootChanceMultiplier = 2.0
        };

        settings.Validate();
        var adjusted = UniversalLootLuck.LootProfileAdjuster.CreateAdjusted(source, settings);

        Assert.AreNotSame(source, adjusted);
        Assert.AreEqual(0.10f, source.LootQualityMod);
        Assert.AreEqual(40, source.ItemChance);
        Assert.AreEqual(0.35f, adjusted.LootQualityMod, 0.0001f);
        Assert.AreEqual(80, adjusted.ItemChance);
        Assert.AreEqual(100, adjusted.MagicItemChance);
        Assert.AreEqual(20, adjusted.MundaneItemChance);
        Assert.AreEqual(source.ItemMinAmount, adjusted.ItemMinAmount);
        Assert.AreEqual(source.MagicItemMaxAmount, adjusted.MagicItemMaxAmount);

        Assert.ThrowsExactly<InvalidDataException>(() => new UniversalLootLuck.LootLuckSettings
        {
            LootQualityBonus = 0.96f
        }.Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => new UniversalLootLuck.LootLuckSettings
        {
            TrophyDropRateMultiplier = double.PositiveInfinity
        }.Validate());
    }

    [TestMethod]
    public void UniversalLootLuckCombinesTierWeightsAndAdjustsRareDenominator()
    {
        var entries = new (uint materialId, float probability)[]
        {
            (10, 0.25f),
            (20, 0.75f),
            (10, 0.25f)
        };

        Assert.AreEqual((uint)10, UniversalLootLuck.UniversalMaterialSelector.RollWeightedMaterialId(entries, 0.0f));
        Assert.AreEqual((uint)10, UniversalLootLuck.UniversalMaterialSelector.RollWeightedMaterialId(entries, 0.39f));
        Assert.AreEqual((uint)20, UniversalLootLuck.UniversalMaterialSelector.RollWeightedMaterialId(entries, 0.50f));
        Assert.AreEqual((uint)20, UniversalLootLuck.UniversalMaterialSelector.RollWeightedMaterialId(entries, 1.0f));
        Assert.IsNull(UniversalLootLuck.UniversalMaterialSelector.RollWeightedMaterialId(Array.Empty<(uint, float)>(), 0.5f));

        Assert.AreEqual(1300, UniversalLootLuck.RareDropRatePatch.CalculateAdjustedLuck(2500, 100, 2.0));
        Assert.AreEqual(-2300, UniversalLootLuck.RareDropRatePatch.CalculateAdjustedLuck(2500, 100, 0.5));
    }

    [TestMethod]
    [DoNotParallelize]
    public void UniversalLootLuckTargetsPinnedAceSignaturesAndCanBeRemoved()
    {
        var loot = AccessTools.Method(typeof(LootGenerationFactory),
            nameof(LootGenerationFactory.CreateRandomLootObjects), new[] { typeof(TreasureDeath) });
        var material = AccessTools.Method(typeof(LootGenerationFactory), "GetMaterialType",
            new[] { typeof(WorldObject), typeof(int) });
        var trophies = AccessTools.Method(typeof(Creature), nameof(Creature.CreateListSelect),
            new[] { typeof(List<PropertiesCreateList>) });
        var rare = AccessTools.Method(typeof(LootGenerationFactory), nameof(LootGenerationFactory.TryCreateRare),
            new[] { typeof(int) });
        Assert.IsNotNull(loot);
        Assert.IsNotNull(material);
        Assert.IsNotNull(trophies);
        Assert.IsNotNull(rare);

        var targets = new[] { loot, material, trophies, rare };
        var mod = new UniversalLootLuck.Mod();
        try
        {
            mod.Initialize();
            foreach (var target in targets)
            {
                var patchInfo = Harmony.GetPatchInfo(target);
                Assert.IsNotNull(patchInfo, target.Name);
                Assert.IsTrue(patchInfo.Prefixes.Any(patch =>
                    patch.owner == UniversalLootLuck.Mod.HarmonyId), target.Name);
            }
        }
        finally
        {
            mod.Dispose();
        }

        foreach (var target in targets)
        {
            var remaining = Harmony.GetPatchInfo(target);
            Assert.IsTrue(remaining is null || remaining.Prefixes.All(patch =>
                patch.owner != UniversalLootLuck.Mod.HarmonyId), target.Name);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void UnlimitedSkillSpecializationsPatchTargetsCurrentAceSignatureAndCanBeRemoved()
    {
        var original = AccessTools.Method(typeof(SkillAlterationDevice),
            nameof(SkillAlterationDevice.VerifyRequirements),
            new[] { typeof(Player), typeof(CreatureSkill), typeof(ACE.DatLoader.Entity.SkillBase) });
        Assert.IsNotNull(original);
        var verifier = AccessTools.Method(typeof(DeveloperFixCommands),
            nameof(DeveloperFixCommands.HandleVerifySkillCredits),
            new[] { typeof(Session), typeof(string[]) });
        Assert.IsNotNull(verifier);

        var mod = new UnlimitedSkillSpecializations.Mod();
        try
        {
            mod.Initialize();
            var patchInfo = Harmony.GetPatchInfo(original);
            Assert.IsNotNull(patchInfo);
            Assert.IsTrue(patchInfo.Transpilers.Any(patch =>
                patch.owner == UnlimitedSkillSpecializations.Mod.HarmonyId));
            var verifierPatchInfo = Harmony.GetPatchInfo(verifier);
            Assert.IsNotNull(verifierPatchInfo);
            Assert.IsTrue(verifierPatchInfo.Transpilers.Any(patch =>
                patch.owner == UnlimitedSkillSpecializations.Mod.HarmonyId));
        }
        finally
        {
            mod.Dispose();
        }

        var remaining = Harmony.GetPatchInfo(original);
        Assert.IsTrue(remaining is null || remaining.Transpilers.All(patch =>
            patch.owner != UnlimitedSkillSpecializations.Mod.HarmonyId));
        var remainingVerifier = Harmony.GetPatchInfo(verifier);
        Assert.IsTrue(remainingVerifier is null || remainingVerifier.Transpilers.All(patch =>
            patch.owner != UnlimitedSkillSpecializations.Mod.HarmonyId));
    }

    [TestMethod]
    public void UnlimitedSkillSpecializationsReplacesOnlyTheStockCapConstant()
    {
        var original = new[]
        {
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)69),
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)70),
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)71)
        };

        var rewritten = UnlimitedSkillSpecializations.SpecializationCapPatch
            .RemoveSpecializedCreditCap(original).ToArray();

        Assert.AreEqual(OpCodes.Ldc_I4_S, rewritten[0].opcode);
        Assert.AreEqual((sbyte)69, rewritten[0].operand);
        Assert.AreEqual(OpCodes.Ldc_I4, rewritten[1].opcode);
        Assert.AreEqual(int.MaxValue, rewritten[1].operand);
        Assert.AreEqual(OpCodes.Ldc_I4_S, rewritten[2].opcode);
        Assert.AreEqual((sbyte)71, rewritten[2].operand);

        var verifierInstructions = new[]
        {
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)70),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)70)
        };
        var rewrittenVerifier = UnlimitedSkillSpecializations.SpecializationCapPatch
            .ReplaceStockCapConstants(verifierInstructions, expectedReplacements: 2);
        Assert.AreEqual(int.MaxValue, rewrittenVerifier[0].operand);
        Assert.AreEqual(OpCodes.Ldc_I4_1, rewrittenVerifier[1].opcode);
        Assert.AreEqual(int.MaxValue, rewrittenVerifier[2].operand);
    }

    [TestMethod]
    public void HelloCommandRegistersAndRemovesCurrentAceCommands()
    {
        var mod = new HelloCommand.Mod();
        try
        {
            mod.Initialize();
            Assert.AreEqual(1, CommandManager.GetCommandByName("hello").Count());
            Assert.AreEqual(1, CommandManager.GetCommandByName("bye").Count());
        }
        finally
        {
            mod.Dispose();
        }

        Assert.AreEqual(0, CommandManager.GetCommandByName("hello").Count());
        Assert.AreEqual(0, CommandManager.GetCommandByName("bye").Count());
    }

    [TestMethod]
    public void SocietyTailoringPatchTargetsCurrentAceSignature()
    {
        var original = AccessTools.Method(typeof(Tailoring), nameof(Tailoring.VerifyUseRequirements),
            new[] { typeof(Player), typeof(WorldObject), typeof(WorldObject) });
        Assert.IsNotNull(original);

        var mod = new SocietyTailoring.Mod();
        try
        {
            mod.Initialize();
            var patchInfo = Harmony.GetPatchInfo(original);
            Assert.IsNotNull(patchInfo);
            Assert.IsTrue(patchInfo.Prefixes.Any(patch => patch.owner == "aquafir.SocietyTailoring.ace-single-player"));
        }
        finally
        {
            mod.Dispose();
        }

        var remaining = Harmony.GetPatchInfo(original);
        Assert.IsTrue(remaining is null || remaining.Prefixes.All(patch => patch.owner != "aquafir.SocietyTailoring.ace-single-player"));
    }

    [TestMethod]
    public void UniqueWeeniesProcPatchTargetsCurrentAceSignatureAndCanBeRemoved()
    {
        var original = AccessTools.Method(typeof(WorldObject), nameof(WorldObject.TryProcEquippedItems),
            new[] { typeof(WorldObject), typeof(Creature), typeof(bool), typeof(WorldObject) });
        Assert.IsNotNull(original);

        var mod = new ACEUniqueWeeniesProc.Mod();
        try
        {
            mod.Initialize();
            var patchInfo = Harmony.GetPatchInfo(original);
            Assert.IsNotNull(patchInfo);
            Assert.IsTrue(patchInfo.Prefixes.Any(patch =>
                patch.owner == "titaniumweiner.ACEUniqueWeeniesProc.ace-single-player"));
        }
        finally
        {
            mod.Dispose();
        }

        var remaining = Harmony.GetPatchInfo(original);
        Assert.IsTrue(remaining is null || remaining.Prefixes.All(patch =>
            patch.owner != "titaniumweiner.ACEUniqueWeeniesProc.ace-single-player"));
    }

    [TestMethod]
    public void UniqueWeeniesProcFilterMatchesDocumentedBehavior()
    {
        Assert.IsTrue(ACEUniqueWeeniesProc.ACEUniqueWeeniesProcPatch.IsEligibleEquippedProc(
            hasProc: true, cloakWeaveProc: null, procSpellSelfTargeted: false, selfTarget: false));
        Assert.IsTrue(ACEUniqueWeeniesProc.ACEUniqueWeeniesProcPatch.IsEligibleEquippedProc(
            hasProc: true, cloakWeaveProc: 2, procSpellSelfTargeted: true, selfTarget: true));
        Assert.IsFalse(ACEUniqueWeeniesProc.ACEUniqueWeeniesProcPatch.IsEligibleEquippedProc(
            hasProc: true, cloakWeaveProc: 1, procSpellSelfTargeted: false, selfTarget: false));
        Assert.IsFalse(ACEUniqueWeeniesProc.ACEUniqueWeeniesProcPatch.IsEligibleEquippedProc(
            hasProc: false, cloakWeaveProc: null, procSpellSelfTargeted: false, selfTarget: false));
        Assert.IsFalse(ACEUniqueWeeniesProc.ACEUniqueWeeniesProcPatch.IsEligibleEquippedProc(
            hasProc: true, cloakWeaveProc: null, procSpellSelfTargeted: true, selfTarget: false));
    }

    [TestMethod]
    public void CatalogBlocksMissingDependenciesBeforeInstall()
    {
        var dependency = new ModCatalogEntry(
            "dependency", "Dependency", "Author", "Description", "Details", "", ModCatalogAvailability.Ready,
            ModDataImpact.None, ModRemovalPolicy.Safe, "Safe", PackageRelativePath: "dependency.zip");
        var dependent = new ModCatalogEntry(
            "dependent", "Dependent", "Author", "Description", "Details", "", ModCatalogAvailability.Ready,
            ModDataImpact.None, ModRemovalPolicy.Safe, "Safe", DependencyIds: new[] { "dependency" }, PackageRelativePath: "dependent.zip");

        var service = new ModCatalogService(new[] { dependency, dependent }, TestPaths.CreateTemporaryDirectory());
        var result = service.Merge(Array.Empty<ModRecord>()).Single(item => item.Catalog.Id == "dependent");

        Assert.AreEqual(CompatibilityStatus.MissingDependency, result.CompatibilityStatus);
        Assert.IsTrue(result.CompatibilityMessage.Contains("Dependency", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PreviewPackageIsInstallableButKeepsLimitedTestingWarning()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "preview.zip"), "package placeholder");
            var preview = new ModCatalogEntry(
                "preview", "Preview", "Author", "Description", "Details", "https://example.invalid/original",
                ModCatalogAvailability.Preview, ModDataImpact.None, ModRemovalPolicy.Safe, "Back up first.",
                PackageRelativePath: "preview.zip", PortSourceUrl: "https://example.invalid/port");

            var item = new ModCatalogService(new[] { preview }, root).Merge(Array.Empty<ModRecord>()).Single();

            Assert.AreEqual(CompatibilityStatus.Compatible, item.CompatibilityStatus);
            Assert.AreEqual("Preview - limited testing", item.Status);
            Assert.IsTrue(item.CompatibilityMessage.Contains("not received thorough in-game testing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void CatalogListsInstalledAndReadyToInstallModsBeforeUnavailableMods()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "ready.zip"), "package placeholder");
            var unavailable = new ModCatalogEntry(
                "unavailable", "A Needs Port", "Author", "Description", "Details", "",
                ModCatalogAvailability.NeedsPort, ModDataImpact.None, ModRemovalPolicy.Safe, "Safe");
            var ready = new ModCatalogEntry(
                "ready", "Z Ready", "Author", "Description", "Details", "",
                ModCatalogAvailability.Ready, ModDataImpact.None, ModRemovalPolicy.Safe, "Safe",
                PackageRelativePath: "ready.zip");
            var installed = new ModRecord { Name = "Installed", Type = ModType.AceServer, Enabled = true };

            var result = new ModCatalogService(new[] { unavailable, ready }, root).Merge(new[] { installed });

            Assert.AreEqual("Installed", result[0].Name);
            Assert.AreEqual("Z Ready", result[1].Name);
            Assert.AreEqual("A Needs Port", result[2].Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ValidatedPackageInstallsAtomically()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            var package = Path.Combine(root, "test.zip");
            CreatePackage(package);
            var installer = new ModPackageInstaller();
            var installed = await installer.InstallAsync(
                package, "test.mod", Path.Combine(root, "Mods"), Path.Combine(root, "Staging"));

            Assert.AreEqual(Path.Combine(root, "Mods", "TestMod"), installed);
            Assert.IsTrue(File.Exists(Path.Combine(installed, "TestMod.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(installed, "Meta.json")));
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(Path.Combine(root, "Staging")).Any());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ValidatedPackageCanBeInspectedBeforeImport()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            var package = Path.Combine(root, "test.zip");
            CreatePackage(package);
            var manifest = await new ModPackageInstaller().InspectAsync(package);

            Assert.AreEqual("test.mod", manifest.Id);
            Assert.AreEqual("Test Mod", manifest.Name);
            Assert.AreEqual("1.0.0", manifest.Version);
            Assert.AreEqual("TestMod.dll", manifest.EntryAssembly);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task EmbeddedIntegrityPackageInstallsWithoutSidecar()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            var package = Path.Combine(root, "test-v2.zip");
            CreateEmbeddedIntegrityPackage(package);
            Assert.IsFalse(File.Exists(package + ".sha256"));

            var installer = new ModPackageInstaller();
            var manifest = await installer.InspectAsync(package);
            var installed = await installer.InstallAsync(
                package, "test.mod", Path.Combine(root, "Mods"), Path.Combine(root, "Staging"));

            Assert.AreEqual(2, manifest.FormatVersion);
            Assert.AreEqual("SHA256", manifest.Integrity?.Algorithm);
            Assert.IsTrue(File.Exists(Path.Combine(installed, "TestMod.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task EmbeddedIntegrityRejectsChangedOrUnlistedPayloads()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            var changed = Path.Combine(root, "changed.zip");
            CreateEmbeddedIntegrityPackage(changed, useIncorrectDllHash: true);
            var changedError = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ModPackageInstaller().InspectAsync(changed));
            StringAssert.Contains(changedError.Message, "does not match");

            var unlisted = Path.Combine(root, "unlisted.zip");
            CreateEmbeddedIntegrityPackage(unlisted, additionalUnlistedEntry: "mod/extra.txt");
            var unlistedError = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ModPackageInstaller().InspectAsync(unlisted));
            StringAssert.Contains(unlistedError.Message, "unhashed file");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task PackageTraversalIsRejected()
    {
        var root = TestPaths.CreateTemporaryDirectory();
        try
        {
            var package = Path.Combine(root, "unsafe.zip");
            CreatePackage(package, "mod/../escaped.txt");
            var installer = new ModPackageInstaller();

            await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(
                package, "test.mod", Path.Combine(root, "Mods"), Path.Combine(root, "Staging")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ServerMetadataParsesCurrentAceFields()
    {
        var directory = TestPaths.CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "Meta.json"), """
            { "Name":"Test Mod", "Author":"Author", "Description":"Description", "Version":"1.2.3", "Priority":7, "Enabled":false }
            """);
            var record = AceServerModProvider.Read(directory);
            Assert.IsFalse(record.IsMalformed);
            Assert.AreEqual("Test Mod", record.Name);
            Assert.AreEqual("Author", record.Author);
            Assert.AreEqual((uint)7, record.Priority);
            Assert.IsFalse(record.Enabled);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task EnabledTogglePreservesUnknownJsonFields()
    {
        var directory = TestPaths.CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "Meta.json");
            await File.WriteAllTextAsync(path, """{"Name":"Test","Enabled":true,"FutureField":{"answer":42}}""");
            await ModMetadataEditor.SetEnabledAsync(path, false);
            var metadata = ModMetadataEditor.Parse(await File.ReadAllTextAsync(path));
            Assert.IsFalse(metadata["Enabled"]!.GetValue<bool>());
            Assert.AreEqual(42, metadata["FutureField"]!["answer"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void MalformedMetadataIsVisibleInsteadOfSilentlySkipped()
    {
        var directory = TestPaths.CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "Meta.json"), "{not json");
            var record = AceServerModProvider.Read(directory);
            Assert.IsTrue(record.IsMalformed);
            Assert.AreEqual(CompatibilityStatus.LoadFailed, record.CompatibilityStatus);
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.LastLoadError));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CreatePackage(string path, string? additionalEntry = null)
    {
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "ace-mod.json", """
                {"formatVersion":1,"id":"test.mod","name":"Test Mod","version":"1.0.0","folderName":"TestMod","entryAssembly":"TestMod.dll"}
                """);
            WriteEntry(archive, "mod/Meta.json", """{"Name":"Test Mod","Enabled":true}""");
            WriteEntry(archive, "mod/TestMod.dll", "test assembly placeholder");
            if (additionalEntry is not null)
                WriteEntry(archive, additionalEntry, "unsafe");
        }

        File.WriteAllText(path + ".sha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    private static void CreateEmbeddedIntegrityPackage(
        string path,
        bool useIncorrectDllHash = false,
        string? additionalUnlistedEntry = null)
    {
        const string metadata = "{\"Name\":\"Test Mod\",\"Enabled\":true}";
        const string assembly = "test assembly placeholder";
        var hashes = new Dictionary<string, string>
        {
            ["mod/Meta.json"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata))),
            ["mod/TestMod.dll"] = useIncorrectDllHash
                ? new string('0', 64)
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assembly)))
        };
        var manifest = JsonSerializer.Serialize(new
        {
            formatVersion = 2,
            id = "test.mod",
            name = "Test Mod",
            version = "2.0.0",
            folderName = "TestMod",
            entryAssembly = "TestMod.dll",
            integrity = new { algorithm = "SHA256", files = hashes }
        });

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "ace-mod.json", manifest);
        WriteEntry(archive, "mod/Meta.json", metadata);
        WriteEntry(archive, "mod/TestMod.dll", assembly);
        if (additionalUnlistedEntry is not null)
            WriteEntry(archive, additionalUnlistedEntry, "not listed in the integrity manifest");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
