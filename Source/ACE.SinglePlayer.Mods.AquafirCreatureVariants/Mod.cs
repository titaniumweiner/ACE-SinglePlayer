using System.Text.Json;
using System.Text.Json.Serialization;

using ACE.Entity.Enum;
using ACE.Server.Command;
using ACE.Server.Mods;
using ACE.Server.Network;

using HarmonyLib;

namespace AquafirCreatureVariants;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.AquafirCreatureVariants";
    private const string CommandName = "creaturevariants";

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

    public static CompiledCreatureVariantSettings Settings { get; private set; } =
        new CreatureVariantSettings { Enabled = false, AssignmentChance = 0.0 }.Compile();

    public static string SettingsPath { get; private set; } = string.Empty;

    public void Initialize()
    {
        if (initialized)
            return;

        var assemblyFolder = Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? throw new InvalidOperationException("The Aquafir Creature Variants assembly folder could not be determined.");
        SettingsPath = Path.Combine(assemblyFolder, "Settings.json");
        Settings = LoadSettings(SettingsPath);

        CreatureVariantRuntime.Reset();
        harmony.PatchAll(typeof(Mod).Assembly);
        commandRegistered = CommandManager.TryAddCommand(
            HandleCommand,
            CommandName,
            AccessLevel.Developer,
            CommandHandlerFlag.None,
            "Shows or reloads Aquafir Creature Variants settings.",
            "status | list | reload",
            overrides: false);
        initialized = true;

        Console.WriteLine($"[Aquafir Creature Variants] Enabled={Settings.Source.Enabled}; " +
            $"random chance={Settings.Source.AssignmentChance:P1}; active traits={Settings.WeightedTraits.Count}; " +
            $"stacking={Settings.Source.AllowVariantStacking} (additional chance={Settings.Source.AdditionalVariantChance:P1}, " +
            $"maximum={Settings.Source.MaximumVariantsPerCreature}).");
    }

    public void Dispose()
    {
        if (commandRegistered)
            CommandManager.TryRemoveCommand(CommandName);

        harmony.UnpatchAll(HarmonyId);
        CreatureVariantRuntime.Reset();
        Settings = new CreatureVariantSettings { Enabled = false, AssignmentChance = 0.0 }.Compile();
        SettingsPath = string.Empty;
        commandRegistered = false;
        initialized = false;
    }

    public static CompiledCreatureVariantSettings LoadSettings(string path)
    {
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<CreatureVariantSettings>(File.ReadAllText(path), JsonOptions)
                ?? new CreatureVariantSettings()
            : new CreatureVariantSettings();
        return settings.Compile();
    }

    private static void ReloadSettings()
    {
        if (string.IsNullOrWhiteSpace(SettingsPath))
            throw new InvalidOperationException("The mod has not been initialized.");

        // Compile before swapping so a malformed edit leaves the last known-good settings active.
        Settings = LoadSettings(SettingsPath);
    }

    private static void HandleCommand(Session session, string[] parameters)
    {
        if (session.Player is null)
            return;

        var action = parameters.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "status";
        switch (action)
        {
            case "status":
                session.Player.SendMessage($"Aquafir Creature Variants: enabled={Settings.Source.Enabled}, " +
                    $"random chance={Settings.Source.AssignmentChance:P1}, active weighted traits={Settings.WeightedTraits.Count}, " +
                    $"stacking={Settings.Source.AllowVariantStacking}, additional chance={Settings.Source.AdditionalVariantChance:P1}, " +
                    $"maximum variants={Settings.Source.MaximumVariantsPerCreature}, " +
                    $"forced WCIDs={Settings.ForcedTraitsByWeenieClassId.Count}. Settings: {SettingsPath}");
                break;

            case "list":
                session.Player.SendMessage("Available variants: " +
                    string.Join(", ", Enum.GetNames<CreatureVariantType>()) + ".");
                break;

            case "reload":
                try
                {
                    ReloadSettings();
                    session.Player.SendMessage($"Creature variant settings reloaded. New spawns use a " +
                        $"{Settings.Source.AssignmentChance:P1} random chance with stacking " +
                        $"{(Settings.Source.AllowVariantStacking ? "enabled" : "disabled")}. " +
                        $"Existing assigned creatures keep their variants until despawn or restart.");
                }
                catch (Exception exception)
                {
                    session.Player.SendMessage($"Settings were not reloaded: {exception.Message}");
                }
                break;

            default:
                session.Player.SendMessage($"Usage: @{CommandName} status | list | reload");
                break;
        }
    }
}
