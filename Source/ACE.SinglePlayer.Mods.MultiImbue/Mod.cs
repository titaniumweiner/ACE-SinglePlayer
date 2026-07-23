using System.Text.Json;

using ACE.Server.Mods;

using HarmonyLib;

namespace MultiImbue;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.MultiImbue";

    private readonly Harmony harmony = new(HarmonyId);
    private bool initialized;

    internal static MultiImbueSettings Settings { get; private set; } = new();

    public void Initialize()
    {
        if (initialized)
            return;

        var assemblyFolder = Path.GetDirectoryName(typeof(Mod).Assembly.Location)
            ?? throw new InvalidOperationException("The MultiImbue assembly folder could not be determined.");
        Settings = LoadSettings(Path.Combine(assemblyFolder, "Settings.json"));
        harmony.PatchAll(typeof(Mod).Assembly);
        initialized = true;

        Console.WriteLine($"[Three Imbues] Enabled. Items may receive up to {Settings.MaximumImbues} distinct standard imbues.");
    }

    public void Dispose()
    {
        harmony.UnpatchAll(HarmonyId);
        Settings = new MultiImbueSettings();
        initialized = false;
    }

    public static MultiImbueSettings LoadSettings(string path)
    {
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<MultiImbueSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new MultiImbueSettings()
            : new MultiImbueSettings();

        settings.Validate();
        return settings;
    }
}

public sealed class MultiImbueSettings
{
    /// <summary>
    /// May be lowered to one or two, but never raised above the mod's tested three-slot limit.
    /// </summary>
    public int MaximumImbues { get; set; } = 3;

    public void Validate()
    {
        if (MaximumImbues is < 1 or > MultiImbueRules.HardMaximumImbues)
            throw new InvalidDataException($"MaximumImbues must be between 1 and {MultiImbueRules.HardMaximumImbues}.");
    }
}
