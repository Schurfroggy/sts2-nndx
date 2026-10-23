using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

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

        public required Node Owner { get; init; }

        public Node2D? Body { get; init; }

        public Action? OwnerExiting { get; set; }

        public bool IsLocallyControlled { get; init; }

        public byte FaceIndex { get; init; }

        public NndxPortraitSwap.Handle? Portrait { get; set; }

        public AnimatedSprite2D? Sprite { get; set; }

        public AudioStreamPlayer? Audio { get; set; }

        public Dictionary<Creature, NndxPetOverlay> Pets { get; } = new();

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

        if (!NndxRoomContext.CanPlay()
            || NndxRoomContext.State?.GetPlayer(playerNetId) is not { } player
            || NRun.Instance is not { } owner) return;
        if (faceIndex is >= NndxPortraitSwap.FaceCount) return;

        var target = NndxRoomContext.FindBody(playerNetId);
        PlayerOverlaySession session = new()
        {
            PlayerNetId = playerNetId,
            Owner = owner,
            Body = target?.Body,
            IsLocallyControlled = locallyControlled,
            BodyWasVisible = target?.Body.Visible ?? false,
            // Cosmetic RNG is independent of the run's deterministic RNG streams.
            FaceIndex = faceIndex ?? (byte)System.Random.Shared.Next(NndxPortraitSwap.FaceCount),
        };

        // Register ownership before hiding the body or allocating child nodes, so every
        // partial startup can be rolled back through the same cleanup path.
        _sessions[playerNetId] = session;
        try
        {
            session.PlaybackEnded = () => OnPlaybackEnded(session);
            session.OwnerExiting = () => StopOwnedSession(session, broadcast: false);
            owner.TreeExiting += session.OwnerExiting;
            if (target != null && GetSpriteFrames() is { } spriteFrames)
            {
                AnimatedSprite2D animatedSprite = new()
                {
                    SpriteFrames = spriteFrames,
                    Animation = AnimationName,
                    Centered = false,
                    Visible = ShouldShowOverlaySprites(),
                    ZAsRelative = true,
                    ZIndex = 0,
                };
                session.Sprite = animatedSprite;
                animatedSprite.AnimationFinished += session.PlaybackEnded;
                animatedSprite.TreeExiting += session.PlaybackEnded;
                target.Parent.AddChild(animatedSprite);
                target.Parent.MoveChild(animatedSprite, 0);
                ApplyLayout(target.Height, animatedSprite);
                target.Body.Visible = false;
                animatedSprite.Play();
            }

            StartAudio(session);
            RefreshPets(playerNetId);
            session.Timeout = owner.GetTree().CreateTimer(PlaybackTimeoutSeconds, processAlways: false);
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

    internal static void RefreshPets(ulong playerNetId)
    {
        if (!_sessions.TryGetValue(playerNetId, out var session)) return;
        if (!NndxInput.PetsLaughEnabled) { ClearPets(session); return; }
        TryCleanup(() =>
        {
            foreach (var pair in session.Pets.Where(p => !p.Value.IsActive).ToArray())
            {
                session.Pets.Remove(pair.Key);
                pair.Value.Dispose();
            }
            var nodes = NCombatRoom.Instance?.CreatureNodes
                .Where(c => c.Entity.PetOwner?.NetId == playerNetId && !c.Entity.IsDead).ToArray();
            if (nodes == null || nodes.Length == 0 || GetSpriteFrames() is not { } frames) return;
            int frame;
            float progress;
            if (session.Sprite is { } main && GodotObject.IsInstanceValid(main))
            {
                frame = main.Frame;
                progress = main.FrameProgress;
            }
            else
            {
                double position = (session.Audio?.GetPlaybackPosition() ?? 0) * FrameRate;
                frame = Math.Clamp((int)position, 0, FrameCount - 1);
                progress = (float)(position - Math.Floor(position));
            }
            foreach (var node in nodes)
            {
                if (session.Pets.ContainsKey(node.Entity)) continue;
                TryCleanup(() =>
                {
                    if (NndxPetOverlay.Create(node, frames, frame, progress) is { } pet)
                        session.Pets.Add(node.Entity, pet);
                }, "attach pet visual");
            }
            SyncPlayerCreatureDrawOrder();
        }, "refresh pet visuals");
    }

    internal static void RefreshPetPreference()
    {
        foreach (ulong playerId in _sessions.Keys.ToArray()) RefreshPets(playerId);
        TryCleanup(SyncPlayerCreatureDrawOrder, "update pet draw order");
    }

    internal static void RefreshPet(Creature creature)
    {
        if (!NndxInput.PetsLaughEnabled || creature.IsDead || creature.PetOwner is not { } owner
            || !_sessions.TryGetValue(owner.NetId, out var session)) return;
        if (session.Pets.TryGetValue(creature, out var pet) && pet.IsActive) return;
        RefreshPets(owner.NetId);
    }

    private static void ClearPets(PlayerOverlaySession session)
    {
        var pets = session.Pets.Values.ToArray();
        session.Pets.Clear();
        foreach (var pet in pets) TryCleanup(pet.Dispose, "restore pet");
    }

    public static void HandleCreatureDeath(Creature creature)
    {
        if (creature.PetOwner is { } owner && creature.IsDead)
        {
            if (_sessions.TryGetValue(owner.NetId, out var session)
                && session.Pets.Remove(creature, out var pet) && pet.IsActive)
            {
                TryCleanup(pet.Dispose, "restore dead pet");
                TryCleanup(() => PlayDeadSound(creature), "play pet death sound");
            }
            return;
        }
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
        TryCleanup(() => PlayDeadSound(creature), "play player death sound");
    }

    public static void StopForPlayer(ulong playerNetId, bool broadcast)
    {
        // Remove first: failed native cleanup must not leave a permanently occupied slot.
        if (!_sessions.Remove(playerNetId, out PlayerOverlaySession? session))
        {
            return;
        }

        TryCleanup(() => session.Portrait?.Restore(), "restore portrait");
        ClearPets(session);
        TryCleanup(() => RestoreBody(session), "restore body");
        TryCleanup(() => CleanupAnimatedSprite(session), "remove sprite");
        TryCleanup(() => CleanupAudio(session), "remove audio");
        TryCleanup(() => CleanupTimeout(session), "remove timeout");
        TryCleanup(() =>
        {
            if (GodotObject.IsInstanceValid(session.Owner) && session.OwnerExiting != null)
                session.Owner.TreeExiting -= session.OwnerExiting;
        }, "disconnect owner exit");
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

        if (!CanToggleInCurrentScreen()) return false;
        Player? localPlayer = LocalContext.GetMe(NndxRoomContext.State);
        if (localPlayer == null) return false;
        playerNetId = localPlayer.NetId;
        return true;
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
    /// While any overlay is active, assign players and pets a small relative ZIndex (1..9) by row depth so
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

        var activePets = _sessions.Values.SelectMany(s => s.Pets.Values)
            .Where(p => p.IsActive).Select(p => p.Creature).ToHashSet();
        List<NCreature> players = room.CreatureNodes
            .Where(creature => creature.Entity.IsPlayer || activePets.Contains(creature.Entity))
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

    internal static bool CanToggleInCurrentScreen()
    {
        if (!NndxRoomContext.CanPlay()) return false;
        // UI presentation never controls network playback. Only local input requires
        // the multiplayer portrait to be present; solo runs retain their existing behavior.
        if (RunManager.Instance?.NetService.Type is not (NetGameType.Host or NetGameType.Client)) return true;
        var player = LocalContext.GetMe(NndxRoomContext.State);
        return player != null && NndxPortraitSwap.HasVisiblePortrait(player.NetId);
    }

    // Opening the map changes visual presentation, not playback ownership or input eligibility.
    internal static bool ShouldShowOverlaySprites()
        => NndxRoomContext.CanPlay() && NMapScreen.Instance?.IsOpen != true
            && NCapstoneContainer.Instance?.InUse != true;

    private static void SyncOverlaySpriteVisibility()
    {
        if (_sessions.Count == 0)
        {
            return;
        }

        bool visible = ShouldShowOverlaySprites();

        foreach (PlayerOverlaySession session in _sessions.Values)
        {
            foreach (var pet in session.Pets.Values)
                if (pet.IsActive) pet.Sprite.Visible = visible;
            if (session.Sprite == null || !GodotObject.IsInstanceValid(session.Sprite))
            {
                continue;
            }

            session.Sprite.Visible = visible;
        }
    }

    private static void ApplyLayout(float targetHeight, AnimatedSprite2D sprite)
    {
        // Use the height in the replacement parent's coordinates. Parent scaling is inherited once.
        if (!float.IsFinite(targetHeight) || targetHeight <= 0f)
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
        => StopOwnedSession(session, session.IsLocallyControlled);

    private static void StopOwnedSession(PlayerOverlaySession session, bool broadcast)
    {
        // An old sprite/timer must never stop a newer playback for the same player.
        if (!_sessions.TryGetValue(session.PlayerNetId, out PlayerOverlaySession? current)
            || !ReferenceEquals(current, session))
        {
            return;
        }

        // Every peer restores its own visuals even when a remote stop never arrives.
        StopForPlayer(session.PlayerNetId, broadcast);
    }

    private static void RestoreBody(PlayerOverlaySession session)
    {
        // GetCurrentBody() can change after Visible=false (e.g. phobia-mode visuals).
        if (session.Body != null && GodotObject.IsInstanceValid(session.Body))
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

        Node audioParent = session.Owner;
        AudioStreamPlayer audioPlayer = new()
        {
            Stream = audio,
            Bus = SfxBus,
            VolumeLinear = 1f,
        };
        session.Audio = audioPlayer;
        // Audio-only rooms have no sprite completion signal.
        audioPlayer.Finished += session.PlaybackEnded;
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
            if (session.PlaybackEnded != null) audio.Finished -= session.PlaybackEnded;
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
        Node? audioParent = (Node?)NCombatRoom.Instance ?? NRun.Instance ?? (Node?)creatureNode;
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
        audioPlayer.Finished += audioPlayer.QueueFree;
        try
        {
            audioParent.AddChild(audioPlayer);
            audioPlayer.Play();
        }
        catch
        {
            audioPlayer.QueueFree();
            throw;
        }
    }
}
