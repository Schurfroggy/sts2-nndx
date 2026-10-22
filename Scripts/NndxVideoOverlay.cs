using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace Nndx.Scripts;

internal static class NndxVideoOverlay
{
    private const string SheetPath = "res://res/nndx_sheet.png";
    private const string AudioPath = "res://res/nndx.mp3";
    private const string DeadAudioPath = "res://res/dead.mp3";
    private const string AnimationName = "default";
    private const float FrameRate = 24f;
    private const float LayoutScaleMultiplier = 1.5f;
    private const int MaxPlayerDrawOrderZIndex = 9;
    private const int SheetColumns = 19;
    private const int FrameCount = 474;
    private const int FrameWidth = 270;
    private const int FrameHeight = 330;
    // Grounded poses in nndx_sheet.png end at y=315; the last 15 rows are transparent.
    private const int FrameFootY = 315;
    private const ulong ToggleCooldownMsec = 150;
    private const double PlaybackTimeoutSeconds = FrameCount / (double)FrameRate + 2;

    private static readonly StringName SfxBus = new("SFX");

    private static readonly Dictionary<ulong, PlayerOverlaySession> _sessions = new();

    private static SpriteFrames? _cachedFrames;
    private static AudioStream? _cachedAudio;
    private static AudioStream? _cachedDeadAudio;
    private static bool _preloadAttempted;
    private static bool _screenContextHooked;
    private static bool _toggleInProgress;
    private static ulong? _lastToggleMsec;

    private sealed class PlayerOverlaySession
    {
        public required ulong PlayerNetId { get; init; }

        public required NCreature Creature { get; init; }

        public required Node2D Body { get; init; }

        public bool IsLocallyControlled { get; init; }

        public byte FaceIndex { get; init; }

        public NndxPortraitSwap.Handle? Portrait { get; set; }

        public AnimatedSprite2D? Sprite { get; set; }

        public AudioStreamPlayer? Audio { get; set; }

        public bool BodyWasVisible { get; set; } = true;

        public Action? PlaybackEnded { get; set; }

        public SceneTreeTimer? Timeout { get; set; }
    }

    public static void Preload()
    {
        if (_preloadAttempted)
        {
            return;
        }

        _preloadAttempted = true;
        NndxPortraitSwap.Preload();

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
        if (_toggleInProgress)
        {
            return;
        }

        _toggleInProgress = true;
        try
        {
            if (!TryGetLocalPlayerNetId(out ulong localNetId))
            {
                return;
            }

            ulong now = Time.GetTicksMsec();
            if (_lastToggleMsec is ulong last && now - last < ToggleCooldownMsec)
            {
                return;
            }

            _lastToggleMsec = now;

            // The local key must also recover a stale session created by a network message.
            if (_sessions.ContainsKey(localNetId))
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
        finally
        {
            _toggleInProgress = false;
        }
    }

    public static void StartForPlayer(ulong playerNetId, bool broadcast, bool locallyControlled = false, byte? faceIndex = null)
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

        if (faceIndex is >= NndxPortraitSwap.FaceCount) return;
        Node2D body = creatureNode.Visuals.GetCurrentBody();
        PlayerOverlaySession session = new()
        {
            PlayerNetId = playerNetId,
            Creature = creatureNode,
            Body = body,
            IsLocallyControlled = locallyControlled,
            BodyWasVisible = body.Visible,
            // Cosmetic RNG is independent of the run's deterministic RNG streams.
            FaceIndex = faceIndex ?? (byte)System.Random.Shared.Next(NndxPortraitSwap.FaceCount),
        };

        // Register ownership before hiding the body or allocating child nodes, so every
        // partial startup can be rolled back through the same cleanup path.
        _sessions[playerNetId] = session;
        try
        {
            body.Visible = false;

            AnimatedSprite2D animatedSprite = new()
            {
                SpriteFrames = spriteFrames,
                Animation = AnimationName,
                Centered = false,
                ZAsRelative = true,
                ZIndex = 0,
            };

            session.Sprite = animatedSprite;
            session.PlaybackEnded = () => OnPlaybackEnded(session);
            animatedSprite.AnimationFinished += session.PlaybackEnded;
            animatedSprite.TreeExiting += session.PlaybackEnded;
            creatureNode.Visuals.AddChild(animatedSprite);
            creatureNode.Visuals.MoveChild(animatedSprite, 0);
            ApplyLayout(creatureNode, animatedSprite);
            animatedSprite.Play();

            StartAudio(session);
            session.Timeout = creatureNode.GetTree().CreateTimer(PlaybackTimeoutSeconds, processAlways: false);
            session.Timeout.Timeout += session.PlaybackEnded;
            EnsureScreenContextHook();
            SyncPlayerCreatureDrawOrder();
            SyncOverlaySpriteVisibility();

            TryCleanup(() => session.Portrait = NndxPortraitSwap.Apply(playerNetId, session.FaceIndex), "replace portrait");
            if (broadcast)
            {
                NndxOverlaySync.Send(playerNetId, isPlaying: true, session.FaceIndex);
            }
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay start failed for player {playerNetId}: {ex}");
            StopForPlayer(playerNetId, broadcast: false);
        }
    }

    internal static void OnPortraitPanelReady(NMultiplayerPlayerState panel)
    {
        if (!_sessions.TryGetValue(panel.Player.NetId, out var session)) return;
        TryCleanup(() =>
        {
            if (session.Portrait is { } current && GodotObject.IsInstanceValid(current.Icon)
                && ReferenceEquals(current.Icon, panel.GetNodeOrNull<TextureRect>("%CharacterIcon"))) return;
            session.Portrait?.Restore();
            session.Portrait = NndxPortraitSwap.Apply(panel, session.FaceIndex);
        }, "refresh portrait");
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
        // Remove first: failed native cleanup must not leave a permanently occupied slot.
        if (!_sessions.Remove(playerNetId, out PlayerOverlaySession? session))
        {
            return;
        }

        TryCleanup(() => session.Portrait?.Restore(), "restore portrait");
        TryCleanup(() => RestoreBody(session), "restore body");
        TryCleanup(() => CleanupAnimatedSprite(session), "remove sprite");
        TryCleanup(() => CleanupAudio(session), "remove audio");
        TryCleanup(() => CleanupTimeout(session), "remove timeout");
        TryCleanup(SyncPlayerCreatureDrawOrder, "restore draw order");
        TryCleanup(SyncOverlaySpriteVisibility, "update visibility");
        TryCleanup(ReleaseScreenContextHookIfIdle, "release screen hook");

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

        _lastToggleMsec = null;
    }

    private static void TryCleanup(Action cleanup, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX overlay cleanup failed ({operation}): {ex}");
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
        if (player?.Creature.IsDead == true || creatureNode == null || !GodotObject.IsInstanceValid(creatureNode)
            || creatureNode.IsQueuedForDeletion() || !creatureNode.IsInsideTree())
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
    /// While any overlay is active, assign each player a small relative ZIndex (1..9) by row depth so
    /// front-row characters draw above back-row ones and enemies, without exceeding GlobalUi (z=0).
    /// CombatSceneContainer is z=-10; player z=1 yields effective -9, above enemies at -10.
    /// </summary>
    private static void SyncPlayerCreatureDrawOrder()
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return;
        }

        List<NCreature> players = room.CreatureNodes
            .Where(creature => creature.Entity.IsPlayer)
            .ToList();

        if (_sessions.Count == 0)
        {
            foreach (NCreature creature in players)
            {
                creature.ZAsRelative = true;
                creature.ZIndex = 0;
            }

            return;
        }

        List<NCreature> sortedByDepth = players
            .OrderBy(creature => creature.GlobalPosition.Y)
            .ToList();

        for (int i = 0; i < sortedByDepth.Count; i++)
        {
            NCreature creature = sortedByDepth[i];
            creature.ZAsRelative = true;
            creature.ZIndex = Math.Min(i + 1, MaxPlayerDrawOrderZIndex);
        }
    }

    private static void EnsureScreenContextHook()
    {
        if (_screenContextHooked)
        {
            return;
        }

        ActiveScreenContext.Instance.Updated += OnActiveScreenContextUpdated;
        _screenContextHooked = true;
    }

    private static void ReleaseScreenContextHookIfIdle()
    {
        if (!_screenContextHooked || _sessions.Count > 0)
        {
            return;
        }

        ActiveScreenContext.Instance.Updated -= OnActiveScreenContextUpdated;
        _screenContextHooked = false;
    }

    private static void OnActiveScreenContextUpdated()
    {
        TryCleanup(SyncOverlaySpriteVisibility, "update screen visibility");
    }

    internal static bool ShouldShowOverlaySprites()
    {
        if (!CombatManager.Instance.IsInProgress || NCombatRoom.Instance == null)
        {
            return false;
        }

        if (NMapScreen.Instance?.IsOpen == true)
        {
            return false;
        }

        if (NCapstoneContainer.Instance?.InUse == true)
        {
            return false;
        }

        if (NOverlayStack.Instance is { ScreenCount: > 0 })
        {
            return false;
        }

        return true;
    }

    private static void SyncOverlaySpriteVisibility()
    {
        if (_sessions.Count == 0)
        {
            return;
        }

        bool visible = ShouldShowOverlaySprites();

        foreach (PlayerOverlaySession session in _sessions.Values)
        {
            if (session.Sprite == null || !GodotObject.IsInstanceValid(session.Sprite))
            {
                continue;
            }

            session.Sprite.Visible = visible;
        }
    }

    private static void ApplyLayout(NCreature creatureNode, AnimatedSprite2D sprite)
    {
        Control bounds = creatureNode.Visuals.Bounds;
        // Both the bounds and sprite live under Visuals. Use the unscaled height;
        // Visuals already applies the character's default and temporary body scales.
        float targetHeight = bounds.Size.Y;
        if (targetHeight <= 0f)
        {
            targetHeight = FrameHeight;
        }

        float scale = targetHeight / FrameHeight * LayoutScaleMultiplier;
        sprite.Scale = Vector2.One * scale;
        // The Visuals origin is the game's stable foot baseline next to the HP bar.
        // Put the artwork's foot baseline there, excluding transparent bottom padding.
        // Inherited scaling can change without moving the feet or a new layout pass.
        sprite.Offset = new Vector2(-FrameWidth * 0.5f, -FrameFootY);
        sprite.Position = Vector2.Zero;
        sprite.ZAsRelative = true;
        sprite.ZIndex = 0;
    }

    private static void OnPlaybackEnded(PlayerOverlaySession session)
    {
        // An old sprite/timer must never stop a newer playback for the same player.
        if (!_sessions.TryGetValue(session.PlayerNetId, out PlayerOverlaySession? current)
            || !ReferenceEquals(current, session))
        {
            return;
        }

        // Every peer restores its own visuals even when a remote stop never arrives.
        StopForPlayer(session.PlayerNetId, broadcast: session.IsLocallyControlled);
    }

    private static void RestoreBody(PlayerOverlaySession session)
    {
        // GetCurrentBody() can change after Visible=false (e.g. phobia-mode visuals).
        if (GodotObject.IsInstanceValid(session.Body))
        {
            session.Body.Visible = session.BodyWasVisible;
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
            AnimatedSprite2D sprite = session.Sprite;
            if (session.PlaybackEnded != null)
            {
                TryCleanup(() => sprite.AnimationFinished -= session.PlaybackEnded, "disconnect animation");
                TryCleanup(() => sprite.TreeExiting -= session.PlaybackEnded, "disconnect tree exit");
            }

            TryCleanup(sprite.Stop, "stop sprite");
            if (!sprite.IsQueuedForDeletion())
            {
                sprite.QueueFree();
            }
        }

        session.Sprite = null;
    }

    private static void CleanupTimeout(PlayerOverlaySession session)
    {
        if (session.Timeout != null && GodotObject.IsInstanceValid(session.Timeout)
            && session.PlaybackEnded != null)
        {
            session.Timeout.Timeout -= session.PlaybackEnded;
        }

        session.Timeout = null;
        session.PlaybackEnded = null;
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

    private static void StartAudio(PlayerOverlaySession session)
    {
        AudioStream? audio = GetAudio();
        if (audio == null)
        {
            Entry.Logger.Error($"Failed to load audio at {AudioPath}.");
            return;
        }

        Node audioParent = NDebugAudioManager.Instance ?? NCombatRoom.Instance ?? (Node)session.Creature;
        AudioStreamPlayer audioPlayer = new()
        {
            Stream = audio,
            Bus = SfxBus,
            VolumeLinear = 1f,
        };
        session.Audio = audioPlayer;
        audioParent.AddChild(audioPlayer);
        audioPlayer.Play();
    }

    private static void CleanupAudio(PlayerOverlaySession session)
    {
        if (session.Audio == null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(session.Audio))
        {
            AudioStreamPlayer audio = session.Audio;
            TryCleanup(audio.Stop, "stop audio");
            if (!audio.IsQueuedForDeletion())
            {
                audio.QueueFree();
            }
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
