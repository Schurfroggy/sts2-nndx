using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Nndx.Scripts;

internal static class NndxVideoOverlay
{
    private const string SheetPath = "res://res/nndx_sheet.png";
    private const string AudioPath = "res://res/nndx.mp3";
    private const string DeadAudioPath = "res://res/dead.mp3";
    private const string AnimationName = "default";
    private const float FrameRate = 24f;
    private const float LayoutScaleMultiplier = 1.5f;
    private const int OverlaySpriteZIndex = -10;
    private const int SheetColumns = 19;
    private const int FrameCount = 474;
    private const int FrameWidth = 270;
    private const int FrameHeight = 330;

    private static readonly StringName SfxBus = new("SFX");

    private static readonly Dictionary<ulong, PlayerOverlaySession> _sessions = new();

    private static SpriteFrames? _cachedFrames;
    private static AudioStream? _cachedAudio;
    private static AudioStream? _cachedDeadAudio;
    private static bool _preloadAttempted;

    private sealed class PlayerOverlaySession
    {
        public required ulong PlayerNetId { get; init; }

        public required NCreature Creature { get; init; }

        public bool IsLocallyControlled { get; init; }

        public AnimatedSprite2D? Sprite { get; set; }

        public AudioStreamPlayer? Audio { get; set; }

        public bool BodyWasVisible { get; set; } = true;
    }

    public static void Preload()
    {
        if (_preloadAttempted)
        {
            return;
        }

        _preloadAttempted = true;

        try
        {
            _cachedFrames = BuildSpriteFrames();
            _cachedAudio = ResourceLoader.Load<AudioStream>(AudioPath);
            _cachedDeadAudio = ResourceLoader.Load<AudioStream>(DeadAudioPath);

            if (_cachedFrames == null)
            {
                Entry.Logger.Error($"NNDX overlay preload failed. Missing {SheetPath} in sts_nndx.pck?");
            }

            if (_cachedAudio == null)
            {
                Entry.Logger.Error($"NNDX audio missing at {AudioPath}.");
            }

            if (_cachedDeadAudio == null)
            {
                Entry.Logger.Error($"NNDX audio missing at {DeadAudioPath}.");
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay preload crashed: {ex}");
        }
    }

    public static void Toggle()
    {
        try
        {
            if (!TryGetLocalPlayerNetId(out ulong localNetId))
            {
                return;
            }

            if (_sessions.TryGetValue(localNetId, out PlayerOverlaySession? activeSession) && activeSession.IsLocallyControlled)
            {
                StopForPlayer(localNetId, broadcast: true);
                return;
            }

            if (GetCreatureForPlayer(localNetId) == null)
            {
                return;
            }

            StartForPlayer(localNetId, broadcast: true, locallyControlled: true);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay toggle crashed: {ex}");
            ForceStop();
        }
    }

    public static void StartForPlayer(ulong playerNetId, bool broadcast, bool locallyControlled = false)
    {
        if (_sessions.ContainsKey(playerNetId))
        {
            return;
        }

        NCreature? creatureNode = GetCreatureForPlayer(playerNetId);
        if (creatureNode == null)
        {
            Entry.Logger.Error($"NNDX overlay could not find creature for player {playerNetId}.");
            return;
        }

        SpriteFrames? spriteFrames = GetSpriteFrames();
        if (spriteFrames == null)
        {
            Entry.Logger.Error($"NNDX overlay unavailable. Missing {SheetPath} in sts_nndx.pck?");
            return;
        }

        PlayerOverlaySession session = new()
        {
            PlayerNetId = playerNetId,
            Creature = creatureNode,
            IsLocallyControlled = locallyControlled,
        };

        Node2D body = creatureNode.Visuals.GetCurrentBody();
        session.BodyWasVisible = body.Visible;
        body.Visible = false;

        AnimatedSprite2D animatedSprite = new()
        {
            SpriteFrames = spriteFrames,
            Animation = AnimationName,
            Centered = false,
            ZAsRelative = true,
            ZIndex = OverlaySpriteZIndex,
        };

        creatureNode.Visuals.AddChild(animatedSprite);
        creatureNode.Visuals.MoveChild(animatedSprite, 0);
        ApplyLayout(creatureNode, animatedSprite);
        animatedSprite.AnimationFinished += () => OnAnimationFinished(playerNetId);
        animatedSprite.Play();

        session.Sprite = animatedSprite;
        session.Audio = StartAudio(creatureNode);
        _sessions[playerNetId] = session;
        SyncPlayerCreatureDrawOrder();

        if (broadcast)
        {
            NndxOverlaySync.Send(playerNetId, isPlaying: true);
        }
    }

    public static void HandlePlayerDeath(Creature creature)
    {
        if (!creature.IsPlayer || creature.Player == null || !creature.IsDead)
        {
            return;
        }

        ulong playerNetId = creature.Player.NetId;
        if (!_sessions.ContainsKey(playerNetId))
        {
            return;
        }

        StopForPlayer(playerNetId, broadcast: false);
        PlayDeadSound(creature);
    }

    public static void StopForPlayer(ulong playerNetId, bool broadcast)
    {
        if (!_sessions.TryGetValue(playerNetId, out PlayerOverlaySession? session))
        {
            return;
        }

        RestoreBody(session);
        CleanupAnimatedSprite(session);
        CleanupAudio(session);
        _sessions.Remove(playerNetId);
        SyncPlayerCreatureDrawOrder();

        if (broadcast)
        {
            NndxOverlaySync.Send(playerNetId, isPlaying: false);
        }
    }

    public static void ForceStop()
    {
        foreach (ulong playerNetId in _sessions.Keys.ToArray())
        {
            StopForPlayer(playerNetId, broadcast: false);
        }
    }

    private static SpriteFrames? GetSpriteFrames()
    {
        Preload();
        if (_cachedFrames != null)
        {
            return _cachedFrames;
        }

        _cachedFrames = BuildSpriteFrames();
        return _cachedFrames;
    }

    private static bool TryGetLocalPlayerNetId(out ulong playerNetId)
    {
        playerNetId = 0;

        if (!CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        Player? localPlayer = LocalContext.GetMe(CombatManager.Instance.DebugOnlyGetState());
        if (localPlayer == null)
        {
            return false;
        }

        playerNetId = localPlayer.NetId;
        return true;
    }

    private static NCreature? GetCreatureForPlayer(ulong playerNetId)
    {
        if (!CombatManager.Instance.IsInProgress || NCombatRoom.Instance == null)
        {
            return null;
        }

        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        Player? player = combatState?.GetPlayer(playerNetId);
        NCreature? creatureNode = player?.Creature.GetCreatureNode();
        if (creatureNode == null || !GodotObject.IsInstanceValid(creatureNode))
        {
            return null;
        }

        return creatureNode;
    }

    private static SpriteFrames? BuildSpriteFrames()
    {
        Texture2D? sheet = ResourceLoader.Load<Texture2D>(SheetPath);
        if (sheet == null)
        {
            Entry.Logger.Error($"Failed to load sprite sheet at {SheetPath}.");
            return null;
        }

        SpriteFrames frames = new();
        frames.AddAnimation(AnimationName);
        frames.SetAnimationSpeed(AnimationName, FrameRate);
        frames.SetAnimationLoop(AnimationName, false);

        for (int i = 0; i < FrameCount; i++)
        {
            int col = i % SheetColumns;
            int row = i / SheetColumns;
            AtlasTexture atlas = new()
            {
                Atlas = sheet,
                Region = new Rect2(
                    col * FrameWidth,
                    row * FrameHeight,
                    FrameWidth,
                    FrameHeight),
            };
            frames.AddFrame(AnimationName, atlas);
        }

        return frames;
    }

    /// <summary>
    /// While any overlay is active, raise each player creature's ZIndex by screen depth so front-row
    /// characters (larger GlobalPosition.Y, closer to the bottom) draw above back-row ones.
    /// Matches NCombatRoom.PositionPlayersAndPets row layout.
    /// </summary>
    private static void SyncPlayerCreatureDrawOrder()
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        bool hasActiveOverlay = _sessions.Count > 0;

        foreach (NCreature creature in room.CreatureNodes)
        {
            if (!creature.Entity.IsPlayer)
            {
                continue;
            }

            creature.ZAsRelative = true;
            creature.ZIndex = hasActiveOverlay ? GetPlayerDepthZIndex(creature) : 0;
        }
    }

    private static int GetPlayerDepthZIndex(NCreature creature)
    {
        // Higher Y = closer to screen bottom = front row = should draw on top.
        return (int)Math.Round(creature.GlobalPosition.Y);
    }

    private static void ApplyLayout(NCreature creatureNode, AnimatedSprite2D sprite)
    {
        Control bounds = creatureNode.Visuals.Bounds;
        Rect2 boundsRect = bounds.GetGlobalRect();

        float targetHeight = boundsRect.Size.Y;
        if (targetHeight <= 0f)
        {
            targetHeight = FrameHeight * creatureNode.Visuals.DefaultScale;
        }

        float scale = targetHeight / FrameHeight * LayoutScaleMultiplier;
        sprite.Scale = Vector2.One * scale;
        float scaledHeight = FrameHeight * scale;
        Vector2 spriteTopLeft = boundsRect.Position + new Vector2(
            (boundsRect.Size.X - FrameWidth * scale) * 0.5f,
            boundsRect.Size.Y - scaledHeight);

        sprite.Position = creatureNode.Visuals.ToLocal(spriteTopLeft);
        sprite.ZAsRelative = true;
        sprite.ZIndex = OverlaySpriteZIndex;
    }

    private static void OnAnimationFinished(ulong playerNetId)
    {
        if (!_sessions.TryGetValue(playerNetId, out PlayerOverlaySession? session) || !session.IsLocallyControlled)
        {
            return;
        }

        StopForPlayer(playerNetId, broadcast: true);
    }

    private static void RestoreBody(PlayerOverlaySession session)
    {
        if (!GodotObject.IsInstanceValid(session.Creature))
        {
            return;
        }

        Node2D body = session.Creature.Visuals.GetCurrentBody();
        if (GodotObject.IsInstanceValid(body))
        {
            body.Visible = session.BodyWasVisible;
        }
    }

    private static void CleanupAnimatedSprite(PlayerOverlaySession session)
    {
        if (session.Sprite == null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(session.Sprite))
        {
            session.Sprite.Stop();
            session.Sprite.QueueFree();
        }

        session.Sprite = null;
    }

    private static AudioStream? GetAudio()
    {
        Preload();
        if (_cachedAudio != null)
        {
            return _cachedAudio;
        }

        _cachedAudio = ResourceLoader.Load<AudioStream>(AudioPath);
        return _cachedAudio;
    }

    private static AudioStreamPlayer? StartAudio(NCreature creatureNode)
    {
        AudioStream? audio = GetAudio();
        if (audio == null)
        {
            Entry.Logger.Error($"Failed to load audio at {AudioPath}.");
            return null;
        }

        Node audioParent = NDebugAudioManager.Instance ?? NCombatRoom.Instance ?? (Node)creatureNode;
        AudioStreamPlayer audioPlayer = new()
        {
            Stream = audio,
            Bus = SfxBus,
            VolumeLinear = 1f,
        };
        audioParent.AddChild(audioPlayer);
        audioPlayer.Play();
        return audioPlayer;
    }

    private static void CleanupAudio(PlayerOverlaySession session)
    {
        if (session.Audio == null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(session.Audio))
        {
            session.Audio.Stop();
            session.Audio.QueueFree();
        }

        session.Audio = null;
    }

    private static void PlayDeadSound(Creature creature)
    {
        if (_cachedDeadAudio == null)
        {
            _cachedDeadAudio = ResourceLoader.Load<AudioStream>(DeadAudioPath);
        }

        if (_cachedDeadAudio == null)
        {
            Entry.Logger.Error($"Failed to load audio at {DeadAudioPath}.");
            return;
        }

        NCreature? creatureNode = creature.GetCreatureNode();
        Node? audioParent = NDebugAudioManager.Instance ?? NCombatRoom.Instance ?? (Node?)creatureNode;
        if (audioParent == null)
        {
            return;
        }

        AudioStreamPlayer audioPlayer = new()
        {
            Stream = _cachedDeadAudio,
            Bus = SfxBus,
            VolumeLinear = 1f,
        };
        audioParent.AddChild(audioPlayer);
        audioPlayer.Play();
        audioPlayer.Finished += audioPlayer.QueueFree;
    }
}
