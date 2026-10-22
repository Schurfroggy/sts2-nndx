using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Nndx.Scripts;
using STS2RitsuLib.Networking.Sidecar;

// Exercise the real production entry points with native-boundary doubles. The
// assertions describe geometry and lifecycle invariants, not the layout formula.
NndxOverlaySync.Initialize();
int passed = 0;
List<object> nativeCases = new();
Run("All five character heights keep their feet fixed and scale only once", () =>
{
    foreach (float height in new[] { 244f, 260f, 270f, 278f, 335f })
    foreach (float scale in new[] { 0.2f, 0.5f, 0.65f, 1f, 1.35f, 2f, 3f })
    {
        NCreature creature = Setup(height, scale);
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
        AssertGeometry(creature, Sprite(creature), height * 1.5f * scale);
    }
});
Run("Scaling during playback preserves the feet and uses the current scale", () =>
{
    foreach (float initialScale in new[] { 0.2f, 0.5f, 1f, 2f })
    {
        NCreature creature = Setup(278, initialScale);
        NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
        AnimatedSprite2D sprite = Sprite(creature);
        foreach (float scale in new[] { 3f, 0.5f, 1.35f, 0.2f, 1f })
        {
            creature.Visuals.Scale = Vector2.One * scale;
            AssertGeometry(creature, sprite, 417f * scale);
        }
    }
});
Run("Viewport/ancestor scaling and asymmetric bounds do not shift the anchor", () =>
{
    NCreature creature = Setup(278, 2);
    creature.Scale = new(1.25f, 0.75f);
    creature.Visuals.Bounds.Position = new(-90, -260);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 417f * 2 * 0.75f);
});
Run("Missing bounds height falls back without reapplying default scale", () =>
{
    NCreature creature = Setup(0, 3);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 330f * 1.5f * 3);
});
Run("Remote playback restores the body when its animation finishes", () =>
{
    NCreature creature = Setup(278, 1.35f);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Sprite(creature).Finish();
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Remote body was not restored");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Check(Sprite(creature).IsPlaying(), "Remote playback could not restart");
});
Run("Old animation and timeout callbacks cannot stop replacement playback", () =>
{
    NCreature creature = Setup(278, 1);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    Action oldFinished = Sprite(creature).CaptureFinished();
    Action oldTimeout = Node.Tree.Timers.Last().CaptureTimeout();
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    AnimatedSprite2D replacement = Sprite(creature);
    oldFinished();
    oldTimeout();
    Check(replacement.IsPlaying() && !replacement.IsQueuedForDeletion(), "Old callback stopped replacement");
});
Run("Partial startup and sprite cleanup failures do not lock playback", () =>
{
    NCreature creature = Setup(278, 0.5f);
    AudioStreamPlayer.FailNextPlay = true;
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Failed startup left the body hidden");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AnimatedSprite2D.FailNextStop = true;
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    Node.FlushDeletion();
    Check(creature.Visuals.Body.Visible, "Failed sprite stop left the body hidden");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false);
    AssertGeometry(creature, Sprite(creature), 208.5f);
});
Run("A delayed host relay for our player cannot recreate or stop our session", () =>
{
    NCreature creature = Setup(278, 2);
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    NndxVideoOverlay.StopForPlayer(1001, broadcast: false);
    RitsuLibSidecarTypedMessageRegistry.Deliver(new NndxOverlaySyncMessage { PlayerNetId = 1001, IsPlaying = true }, 1);
    Callable.Flush();
    Check(creature.Visuals.Body.Visible, "Own relayed start recreated a remote session");
    NndxVideoOverlay.StartForPlayer(1001, broadcast: false, locallyControlled: true);
    AnimatedSprite2D current = Sprite(creature);
    RitsuLibSidecarTypedMessageRegistry.Deliver(new NndxOverlaySyncMessage { PlayerNetId = 1001, IsPlaying = false }, 1);
    Callable.Flush();
    Check(current.IsPlaying() && !current.IsQueuedForDeletion(), "Own relayed stop interrupted local session");
});
Run("Scale console changes and restores only the requested visual default scale", () =>
{
    NCreature creature = Setup(278, 0.98f);
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    NndxScaleConsoleCmd command = new();
    foreach (string size in new[] { "0.2", "0.5", "1", "1.35", "2", "3" })
    {
        Check(command.Process(LocalContext.Local, new[] { size }).Success, "Valid scale was rejected");
        Near(creature.Visuals.DefaultScale, float.Parse(size, System.Globalization.CultureInfo.InvariantCulture), "Requested scale");
    }
    Check(command.Process(LocalContext.Local, new[] { "reset" }).Success, "Reset failed");
    Near(creature.Visuals.DefaultScale, 0.98f, "Pre-test scale");
});
Run("Scale console rejects invalid input and multiplayer usage", () =>
{
    NCreature creature = Setup(278, 1);
    NndxScaleConsoleCmd command = new();
    Check(!command.Process(LocalContext.Local, new[] { "2" }).Success, "Multiplayer scale test was allowed");
    RunManager.Instance!.NetService.Type = NetGameType.Singleplayer;
    foreach (string invalid in new[] { "0", "-1", "3.01", "NaN", "Infinity", "1e50", "1,2", "bad" })
    {
        Check(!command.Process(LocalContext.Local, new[] { invalid }).Success, "Invalid scale was accepted");
        Near(creature.Visuals.DefaultScale, 1, "Invalid input changed scale");
    }
    Check(command.Process(LocalContext.Local, new[] { "2" }).Success, "Valid command failed");
    NndxScaleConsoleCmd.RestoreOriginalSize();
    Near(creature.Visuals.DefaultScale, 1, "Lifecycle restore");
});
NndxVideoOverlay.ForceStop();
Node.FlushDeletion();
Console.WriteLine($"PASS: {passed} regression groups");
if (args.Length == 1)
{
    File.WriteAllText(args[0], System.Text.Json.JsonSerializer.Serialize(nativeCases));
    Console.WriteLine($"Wrote {nativeCases.Count} production layout cases for native Godot verification");
}

void Run(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

static NCreature Setup(float height, float scale)
{
    NndxScaleConsoleCmd.RestoreOriginalSize();
    NndxVideoOverlay.ForceStop();
    Node.FlushDeletion();
    Node.All.Clear();
    Node.Tree.Timers.Clear();
    CombatManager.Instance.State = new();
    CombatManager.Instance.IsInProgress = true;
    NCombatRoom.Instance = new();
    RunManager.Instance = new();
    RunManager.Instance.NetService.Type = NetGameType.Client;
    RunManager.Instance.NetService.NetId = 1001;
    NCreature creature = new() { Position = new(600, 500) };
    creature.AddChild(creature.Visuals);
    creature.Visuals.AddChild(creature.Visuals.Bounds);
    creature.Visuals.AddChild(creature.Visuals.Body);
    creature.Visuals.Bounds.Position = new(-121, -height);
    creature.Visuals.Bounds.Size = new(242, height);
    creature.Visuals.DefaultScale = scale;
    creature.Visuals.Scale = Vector2.One * scale;
    Creature entity = new() { Node = creature };
    Player player = new() { NetId = 1001, Creature = entity };
    entity.Player = player;
    creature.Entity = entity;
    CombatManager.Instance.State.Players.Add(player);
    LocalContext.Local = player;
    NCombatRoom.Instance.CreatureNodes.Add(creature);
    return creature;
}

static AnimatedSprite2D Sprite(NCreature creature) => creature.Visuals.Children
    .OfType<AnimatedSprite2D>().Single(sprite => !sprite.IsQueuedForDeletion() && !sprite.Freed);

void AssertGeometry(NCreature creature, AnimatedSprite2D sprite, float expectedHeight)
{
    // The texture's authored ground line is y=315; y=330 includes transparent padding.
    Vector2 bottomCenter = sprite.ToGlobal(sprite.Offset + new Vector2(135, 315));
    Vector2 baseline = creature.Visuals.GlobalPosition;
    Near(bottomCenter.X, baseline.X, "Foot X");
    Near(bottomCenter.Y, baseline.Y, "Foot Y");
    float height = sprite.ToGlobal(sprite.Offset + new Vector2(135, 330)).Y
        - sprite.ToGlobal(sprite.Offset + new Vector2(135, 0)).Y;
    Near(height, expectedHeight, "Rendered height");
    nativeCases.Add(new
    {
        parent_scale = new[] { creature.Scale.X, creature.Scale.Y },
        body_scale = new[] { creature.Visuals.Scale.X, creature.Visuals.Scale.Y },
        sprite_scale = new[] { sprite.Scale.X, sprite.Scale.Y },
        sprite_position = new[] { sprite.Position.X, sprite.Position.Y },
        sprite_offset = new[] { sprite.Offset.X, sprite.Offset.Y },
        expected_height = expectedHeight,
    });
}

static void Near(float actual, float expected, string label) =>
    Check(Math.Abs(actual - expected) < 0.005f, $"{label}: expected {expected}, got {actual}");

static void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
