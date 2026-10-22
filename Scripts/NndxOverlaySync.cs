using Godot;
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
        "overlay_v3",
        NndxOverlaySyncMessage.Serialize,
        NndxOverlaySyncMessage.Deserialize,
        RitsuLibSidecarDeliverySemantics.StableSync,
        false);

    private static IDisposable? _subscription;
    // A process restart/rejoin starts above earlier counters under a normally advancing clock.
    private static ulong _nextSequence = (ulong)DateTime.UtcNow.Ticks;
    private static readonly Dictionary<ulong, ulong> ReceivedSequences = new();
    private static WeakReference<RunState>? _sequenceScope;

    public static void Initialize()
    {
        RitsuLibSidecarTypedMessageRegistry.Register(Descriptor);
        _subscription = RitsuLibSidecarTypedMessageRegistry.Subscribe(Descriptor, OnMessageReceived);
    }

    public static void Send(ulong playerNetId, bool isPlaying, byte faceIndex = NndxOverlaySyncMessage.NoFace)
    {
        // A transport failure must never interrupt local playback cleanup or game input.
        try
        {
            SendCore(playerNetId, isPlaying, faceIndex);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay sync send failed: {ex}");
        }
    }

    private static void SendCore(ulong playerNetId, bool isPlaying, byte faceIndex)
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

        var state = NndxRoomContext.State;
        if (state?.CurrentRoom?.Id is not int roomId) return;
        NndxOverlaySyncMessage message = new()
        {
            PlayerNetId = playerNetId,
            IsPlaying = isPlaying,
            FaceIndex = isPlaying ? faceIndex : NndxOverlaySyncMessage.NoFace,
            Sequence = ++_nextSequence,
            RunSeed = state.Rng.Seed,
            RoomId = roomId,
            ActIndex = state.CurrentActIndex,
            MapCol = state.CurrentMapCoord?.col ?? -1,
            MapRow = state.CurrentMapCoord?.row ?? -1,
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
        // Sidecar delivery (including StableSync) does not guarantee the Godot main thread.
        NndxOverlaySyncMessage message = context.Message;
        ulong senderNetId = context.SenderNetId;
        Callable.From(() => ReceiveOnMainThread(message, senderNetId)).CallDeferred();
    }

    private static void ReceiveOnMainThread(NndxOverlaySyncMessage message, ulong senderNetId)
    {
        try
        {
            ReceiveCore(message, senderNetId);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay sync receive failed: {ex}");
        }
    }

    private static void ReceiveCore(NndxOverlaySyncMessage message, ulong senderNetId)
    {
        if (!IsNetworkedMultiplayer() || !NndxRoomContext.CanPlay())
        {
            return;
        }

        Player? localPlayer = LocalContext.GetMe(NndxRoomContext.State);
        // A host relay has the host's sender ID, but may still describe our own player.
        if (localPlayer == null || message.PlayerNetId == localPlayer.NetId)
        {
            return;
        }

        RunManager? runManager = RunManager.Instance;
        if (runManager?.NetService.Type == NetGameType.Host && senderNetId != message.PlayerNetId)
        {
            return;
        }

        var state = NndxRoomContext.State;
        if (!message.IsValid || state?.GetPlayer(message.PlayerNetId) == null
            || state.CurrentRoom?.Id != message.RoomId || state.Rng.Seed != message.RunSeed
            || state.CurrentActIndex != message.ActIndex
            || (state.CurrentMapCoord?.col ?? -1) != message.MapCol
            || (state.CurrentMapCoord?.row ?? -1) != message.MapRow)
            return;
        if (_sequenceScope == null || !_sequenceScope.TryGetTarget(out var previousState)
            || !ReferenceEquals(previousState, state))
        {
            _sequenceScope = new WeakReference<RunState>(state);
            ReceivedSequences.Clear();
        }
        if (ReceivedSequences.TryGetValue(message.PlayerNetId, out ulong last) && message.Sequence <= last)
            return;
        ReceivedSequences[message.PlayerNetId] = message.Sequence;
        ApplyMessage(message);

        if (runManager?.NetService.Type != NetGameType.Host)
        {
            return;
        }

        if (senderNetId == runManager.NetService.NetId)
        {
            return;
        }

        RitsuLibSidecarTypedMessageRegistry.Broadcast(runManager, Descriptor, message);
    }

    private static void ApplyMessage(NndxOverlaySyncMessage message)
    {
        if (message.IsPlaying)
        {
            // A newer start can arrive even if a preceding stop was missed.
            NndxVideoOverlay.StopForPlayer(message.PlayerNetId, broadcast: false);
            NndxVideoOverlay.StartForPlayer(message.PlayerNetId, broadcast: false, faceIndex: message.FaceIndex);
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
