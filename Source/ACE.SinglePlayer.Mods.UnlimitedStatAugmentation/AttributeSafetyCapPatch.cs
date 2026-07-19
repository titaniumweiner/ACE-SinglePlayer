using ACE.Server.WorldObjects;

using HarmonyLib;

namespace UnlimitedStatAugmentation;

[HarmonyPatch(typeof(AugmentationDevice), nameof(AugmentationDevice.AttributeAugmentationSafetyCapEnabled), MethodType.Getter)]
public static class AttributeSafetyCapPatch
{
    [HarmonyPostfix]
    public static void KeepStatMaximumAtOneHundred(ref bool __result)
    {
        __result = true;
    }
}
