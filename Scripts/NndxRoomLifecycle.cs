using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Nndx.Scripts;

// Prefixes run before async transitions yield, so sound cannot bleed into the next room.
[HarmonyPatch]
internal static class NndxRoomDeparturePatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "ExitCurrentRoom", "EnterRoomWithoutExitingCurrentRoom",
            "EnterAct", "EnterNextAct", "Abandon", "CleanUp", "OnEnded", "LocalPlayerDisconnected" })
            yield return AccessTools.Method(typeof(RunManager), name);
    }

    [HarmonyPrefix]
    private static void Prefix() => NndxRoomContext.BeginTransition();
}

[HarmonyPatch]
internal static class NndxCombatBoundaryPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "EndCombatInternal", "LoseCombat" })
            yield return AccessTools.Method(typeof(CombatManager), name);
    }

    [HarmonyPrefix]
    private static void Prefix() => NndxVideoOverlay.ForceStop();
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class NndxCombatSetupPatch
{
    [HarmonyPrefix]
    private static void Prefix() => NndxRoomContext.BeginTransition();
}

[HarmonyPatch(typeof(NRun), nameof(NRun.SetCurrentRoom))]
internal static class NndxRoomScenePatch
{
    [HarmonyPrefix]
    private static void Prefix() => NndxVideoOverlay.ForceStop();
}

[HarmonyPatch(typeof(RunManager), "RemotePlayerDisconnected")]
internal static class NndxPlayerDisconnectedPatch
{
    [HarmonyPrefix]
    private static void Prefix(ulong playerId) => NndxVideoOverlay.StopForPlayer(playerId, broadcast: false);
}

[HarmonyPatch]
internal static class NndxTravelAndExitPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NMapScreen), nameof(NMapScreen.TravelToMapCoord));
        yield return AccessTools.Method(typeof(NGame), nameof(NGame.ReturnToMainMenu));
        yield return AccessTools.Method(typeof(NGame), nameof(NGame.Quit));
    }

    [HarmonyPrefix]
    private static void Prefix() => NndxRoomContext.BeginTransition();
}
