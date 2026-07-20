using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Reflection.Emit;
using System.Security.Cryptography;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Database.Models.World;
using ACE.Server.Command;
using ACE.Server.Command.Handlers;
using ACE.Server.Entity;
using ACE.Server.Factories;
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
        Assert.AreEqual(27, CuratedModCatalog.Entries.Count);
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

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
