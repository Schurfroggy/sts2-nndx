using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Nndx.Scripts;

/// <summary>A child visual of a player's laugh, with independent death/removal cleanup.</summary>
internal sealed class NndxPetOverlay : IDisposable
{
    private readonly Node2D _body;
    private readonly NCreature _node;
    private readonly int _originalZIndex;
    private readonly bool _originalZAsRelative;
    private readonly bool _bodyWasVisible;
    private bool _disposed;
    internal Creature Creature { get; }
    internal AnimatedSprite2D Sprite { get; }
    internal bool IsActive => !_disposed && NndxRoomContext.IsUsable(Sprite);

    private NndxPetOverlay(NCreature node, SpriteFrames frames, int frame, float progress)
    {
        Creature = node.Entity;
        _node = node;
        _originalZIndex = node.ZIndex;
        _originalZAsRelative = node.ZAsRelative;
        _body = node.Visuals.GetCurrentBody();
        _bodyWasVisible = _body.Visible;
        Sprite = new AnimatedSprite2D
        {
            SpriteFrames = frames,
            Animation = "default",
            Centered = false,
            Visible = NndxVideoOverlay.ShouldShowOverlaySprites(),
            ZAsRelative = true,
            ZIndex = 0,
        };
        try
        {
            Sprite.TreeExiting += OnTreeExiting;
            Sprite.AnimationFinished += Dispose;
            node.Visuals.AddChild(Sprite);
            node.Visuals.MoveChild(Sprite, 0);
            ApplyLayout(node.Visuals, Sprite);
            _body.Visible = false;
            Sprite.Play();
            Sprite.SetFrameAndProgress(frame, progress);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static NndxPetOverlay? Create(NCreature node, SpriteFrames frames, int frame, float progress)
    {
        if (node.Entity.PetOwner == null || node.Entity.IsDead
            || !NndxRoomContext.IsUsable(node) || !NndxRoomContext.IsUsable(node.Visuals)
            || !node.Visuals.IsVisibleInTree()) return null;
        var body = node.Visuals.GetCurrentBody();
        var bounds = node.Visuals.Bounds;
        if (!NndxRoomContext.IsUsable(body) || !body.IsVisibleInTree() || bounds == null) return null;
        var size = bounds.Size;
        // Unknown custom layouts keep their original body rather than guessing dimensions.
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0) return null;
        return new(node, frames, frame, progress);
    }

    private void ApplyLayout(NCreatureVisuals visuals, AnimatedSprite2D sprite)
    {
        // Osty retains its square canvas; other pets fit the same bounds without distortion.
        // Visuals already supplies growth / mod scaling; inherit it exactly once.
        Vector2 size = visuals.Bounds.Size;
        if (Creature.Monster is Osty)
        {
            float side = Math.Min(size.X, size.Y);
            sprite.Scale = new Vector2(side / 270f, side / 330f);
        }
        else
        {
            float scale = Math.Min(size.X / 270f, size.Y / 330f);
            sprite.Scale = Vector2.One * scale;
        }
        sprite.Offset = new Vector2(-135f, -315f);
        sprite.Position = Vector2.Zero;
    }

    private void OnTreeExiting()
    {
        // Some pets are removed before AfterDeath. Resolve their sound while this visual
        // is still active; the later death event is deduplicated by the session registry.
        if (!_disposed && Creature.IsDead) NndxVideoOverlay.HandleCreatureDeath(Creature);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup(() =>
        {
            if (!GodotObject.IsInstanceValid(_node)) return;
            _node.ZIndex = _originalZIndex;
            _node.ZAsRelative = _originalZAsRelative;
        });
        // Cleanup steps are isolated: a native failure must not leave the sprite alive.
        Cleanup(() => { if (GodotObject.IsInstanceValid(_body)) _body.Visible = _bodyWasVisible; });
        if (GodotObject.IsInstanceValid(Sprite))
        {
            Cleanup(() => Sprite.TreeExiting -= OnTreeExiting);
            Cleanup(() => Sprite.AnimationFinished -= Dispose);
            Cleanup(Sprite.Stop);
            Cleanup(() => { if (!Sprite.IsQueuedForDeletion()) Sprite.QueueFree(); });
        }
    }

    private static void Cleanup(Action action)
    {
        try { action(); }
        catch (Exception ex) { Entry.Logger.Error($"NNDX pet cleanup failed: {ex}"); }
    }
}
