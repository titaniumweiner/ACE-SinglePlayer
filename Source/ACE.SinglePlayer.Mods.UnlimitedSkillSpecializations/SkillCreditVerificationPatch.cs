using ACE.Server.Command.Handlers;
using ACE.Server.Network;

using HarmonyLib;

namespace UnlimitedSkillSpecializations;

[HarmonyPatch(typeof(DeveloperFixCommands), nameof(DeveloperFixCommands.HandleVerifySkillCredits),
    new[] { typeof(Session), typeof(string[]) })]
public static class SkillCreditVerificationPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> AcceptOverCapSpecializations(IEnumerable<CodeInstruction> instructions) =>
        SpecializationCapPatch.ReplaceStockCapConstants(instructions, expectedReplacements: 2);
}
