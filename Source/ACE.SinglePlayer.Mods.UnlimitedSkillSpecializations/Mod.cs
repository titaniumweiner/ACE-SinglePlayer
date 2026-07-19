using ACE.Server.Mods;

using HarmonyLib;

namespace UnlimitedSkillSpecializations;

public sealed class Mod : IHarmonyMod
{
    public const string HarmonyId = "opendereth.UnlimitedSkillSpecializations";

    private readonly Harmony harmony = new(HarmonyId);

    public void Initialize()
    {
        harmony.PatchAll(typeof(Mod).Assembly);
        Console.WriteLine("[Unlimited Skill Specializations] Removed the 70-credit specialization ceiling; normal skill-credit costs still apply.");
    }

    public void Dispose() => harmony.UnpatchAll(HarmonyId);
}
