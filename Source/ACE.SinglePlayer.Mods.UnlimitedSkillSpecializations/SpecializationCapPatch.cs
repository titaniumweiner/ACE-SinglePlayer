using System.Reflection.Emit;

using ACE.DatLoader.Entity;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;

using HarmonyLib;

namespace UnlimitedSkillSpecializations;

[HarmonyPatch(typeof(SkillAlterationDevice), nameof(SkillAlterationDevice.VerifyRequirements),
    new[] { typeof(Player), typeof(CreatureSkill), typeof(SkillBase) })]
public static class SpecializationCapPatch
{
    public const int StockSpecializedCreditCap = 70;

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> RemoveSpecializedCreditCap(IEnumerable<CodeInstruction> instructions) =>
        ReplaceStockCapConstants(instructions, expectedReplacements: 1);

    public static IReadOnlyList<CodeInstruction> ReplaceStockCapConstants(
        IEnumerable<CodeInstruction> instructions,
        int expectedReplacements)
    {
        var rewritten = new List<CodeInstruction>();
        var replacements = 0;

        foreach (var instruction in instructions)
        {
            if (!LoadsStockCap(instruction))
            {
                rewritten.Add(instruction);
                continue;
            }

            var replacement = new CodeInstruction(OpCodes.Ldc_I4, int.MaxValue);
            replacement.labels.AddRange(instruction.labels);
            replacement.blocks.AddRange(instruction.blocks);
            rewritten.Add(replacement);
            replacements++;
        }

        if (replacements != expectedReplacements)
            throw new InvalidOperationException($"Expected {expectedReplacements} ACE specialization-cap constants, found {replacements}.");

        return rewritten;
    }

    private static bool LoadsStockCap(CodeInstruction instruction) =>
        (instruction.opcode == OpCodes.Ldc_I4_S || instruction.opcode == OpCodes.Ldc_I4) &&
        Convert.ToInt32(instruction.operand) == StockSpecializedCreditCap;
}
