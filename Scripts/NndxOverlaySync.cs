using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Networking.Sidecar;

namespace Nndx.Scripts;

internal static class NndxOverlaySync
{
    private static readonly RitsuLibSidecarMessageDescriptor<NndxOverlaySyncMessage> Descriptor = new(
        Entry.ModId,
        "overlay",
        NndxOverlaySyncMessage.Serialize,
        NndxOverlaySyncMessage.Deserialize,
        RitsuLibSidecarDeliverySemantics.BestEffort,
        false);

    private static IDisposable? _subscription;

    public static void Initialize()
    {
        RitsuLibSidecarTypedMessageRegistry.Register(Descriptor);
        _subscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(Descriptor, OnMessageReceived);
    }

    public static void Send(ulong playerNetId, bool isPlaying)
    {
        if (!IsNetworkedMultiplayer())
        {
            return;
        }

        RunManager? runManager = RunManager.Instance;
        if (runManager == null)
        {
            return;
        }

        NndxOverlaySyncMessage message = new()
        {
            PlayerNetId = playerNetId,
            IsPlaying = isPlaying,
        };

        if (runManager.NetService.Type == NetGameType.Host)
        {
            RitsuLibSidecarTypedMessageRegistry.Broadcast(runManager, Descriptor, message);
            return;
        }

        RitsuLibSidecarTypedMessageRegistry.SendToHost(runManager, Descriptor, message);
    }

    private static void OnMessageReceived(RitsuLibSidecarTypedDispatchContext<NndxOverlaySyncMessage> context)
    {
        Player? localPlayer = LocalContext.GetMe(CombatManager.Instance.DebugOnlyGetState());
        if (localPlayer != null && context.SenderNetId == localPlayer.NetId)
        {
            return;
        }

        ApplyMessage(context.Message);

        RunManager? runManager = RunManager.Instance;
        if (runManager?.NetService.Type != NetGameType.Host)
        {
            return;
        }

        if (context.SenderNetId == runManager.NetService.NetId)
        {
            return;
        }

        RitsuLibSidecarTypedMessageRegistry.Broadcast(runManager, Descriptor, context.Message);
    }

    private static void ApplyMessage(NndxOverlaySyncMessage message)
    {
        if (message.IsPlaying)
        {
            NndxVideoOverlay.StartForPlayer(message.PlayerNetId, broadcast: false);
            return;
        }

        NndxVideoOverlay.StopForPlayer(message.PlayerNetId, broadcast: false);
    }

    private static bool IsNetworkedMultiplayer()
    {
        RunManager? runManager = RunManager.Instance;
        if (runManager == null || !runManager.IsInProgress)
        {
            return false;
        }

        NetGameType type = runManager.NetService.Type;
        return type is NetGameType.Host or NetGameType.Client;
    }
}
