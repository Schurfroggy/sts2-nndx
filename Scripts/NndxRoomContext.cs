using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Nndx.Scripts;

/// <summary>Only known body layouts opt into replacement; every other room is audio/portrait only.</summary>
internal static class NndxRoomContext
{
    private static WeakReference<AbstractRoom>? _blockedRoom;

    internal sealed record BodyTarget(Node2D Parent, Node2D Body, float Height);

    internal static RunState? State => RunManager.Instance?.DebugOnlyGetState();

    private static bool IsCombatRoom(AbstractRoom? room) => room is CombatRoom
        || room?.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss;

    private static bool IsPostCombat => IsCombatRoom(State?.CurrentRoom)
        && State?.CurrentRoom?.IsPreFinished == true && !CombatManager.Instance.IsInProgress;

    internal static bool CanPlay()
    {
        var manager = RunManager.Instance;
        var room = State?.CurrentRoom;
        if (manager?.IsInProgress != true || manager.IsCleaningUp || manager.IsAbandoned || CombatManager.Instance.IsStarting
            || room?.Id is not >= 0 || !IsUsable(NRun.Instance)
            || room.RoomType == RoomType.RestSite
            || (_blockedRoom?.TryGetTarget(out var blocked) == true && ReferenceEquals(blocked, room)))
            return false;

        // Victory stops the old session, but players may start a new one while collecting
        // rewards or viewing the map. Body availability is resolved independently below.
        if (IsCombatRoom(room))
            return IsPostCombat || (CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsEnding
                && !room.IsPreFinished && NCombatRoom.Instance?.Mode != CombatRoomMode.FinishedCombat);
        return true;
    }

    internal static void CombatStarting()
    {
        NndxVideoOverlay.ForceStop();
        _blockedRoom = null;
        NndxVideoOverlay.Preload();
    }

    internal static void BeginTransition()
    {
        if (State?.CurrentRoom is { } room) _blockedRoom = new(room);
        NndxVideoOverlay.ForceStop();
    }

    internal static bool IsUsable(Node? node) => node != null && GodotObject.IsInstanceValid(node)
        && !node.IsQueuedForDeletion() && node.IsInsideTree();

    internal static BodyTarget? FindBody(ulong playerNetId)
    {
        // Dead/spectating players can still use their portrait, but have no replaceable body.
        if (State?.GetPlayer(playerNetId)?.Creature.IsDead == true) return null;
        try
        {
            // Includes VisualOnly combat layouts used by events such as the Architect.
            if (NCombatRoom.Instance is { } combat && IsUsable(combat))
            {
                var creature = combat.CreatureNodes.FirstOrDefault(c => c.Entity.Player?.NetId == playerNetId);
                return creature != null && IsUsable(creature) ? FromVisuals(creature.Visuals) : null;
            }

            var players = State?.Players;
            if (players == null) return null;
            var local = LocalContext.GetMe(players);
            // Both vanilla shop layouts create local-player-first, preserving the other players' order.
            var ordered = players.Where(p => p != local).ToList();
            if (local != null) ordered.Insert(0, local);
            int index = ordered.FindIndex(p => p.NetId == playerNetId);
            if (index < 0) return null;

            if (NRun.Instance?.MerchantRoom is { } shop && shop.PlayerVisuals.Count == ordered.Count)
            {
                var character = shop.PlayerVisuals[index];
                // Unknown/modded merchant visuals must keep their original body.
                var children = character.GetChildren().OfType<Node2D>().Where(IsUsable).ToArray();
                if (!character.IsVisibleInTree() || children.Length != 1 || children[0].GetClass() != "SpineSprite") return null;
                var body = children[0];
                var skeleton = new MegaSprite(body).GetSkeleton();
                if (skeleton == null) return null;
                float height = Math.Abs(skeleton.GetBounds().Size.Y * body.Scale.Y);
                return float.IsFinite(height) && height > 0 && IsUsable(body) && body.IsVisibleInTree() ? new(character, body, height) : null;
            }

            if (NRun.Instance?.EventRoom?.CustomEventNode is NFakeMerchant fake)
            {
                var container = fake.GetNodeOrNull<Control>("%CharacterContainer");
                // The game moves each newly-created visual to child 0, reversing creation order.
                var visuals = container?.GetChildren().OfType<NCreatureVisuals>().Reverse().ToArray();
                if (visuals != null && visuals.Length == ordered.Count) return FromVisuals(visuals[index]);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"NNDX body layout unavailable; using audio and portrait: {ex.Message}");
        }
        return null;
    }

    private static BodyTarget? FromVisuals(NCreatureVisuals visuals)
    {
        if (!IsUsable(visuals) || !visuals.IsVisibleInTree()) return null;
        var body = visuals.GetCurrentBody();
        return IsUsable(body) && body.IsVisibleInTree() ? new(visuals, body, visuals.Bounds.Size.Y) : null;
    }
}
