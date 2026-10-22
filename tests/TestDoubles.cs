// Deliberately small native-boundary doubles. The regression runner compiles the
// actual production overlay and networking files, not a copy of their logic.
namespace Godot
{
    public class GodotObject
    {
        public bool Freed { get; private set; }
        public static bool IsInstanceValid(GodotObject? value) => value is { Freed: false };
        public virtual void Free() => Freed = true;
    }

    public readonly record struct StringName(string Value)
    {
        public static implicit operator StringName(string value) => new(value);
    }

    public readonly record struct Vector2(float X, float Y)
    {
        public static Vector2 One => new(1, 1);
        public static Vector2 Zero => new(0, 0);
        public static Vector2 operator *(Vector2 value, float scale) => new(value.X * scale, value.Y * scale);
        public static Vector2 operator *(Vector2 first, Vector2 second) => new(first.X * second.X, first.Y * second.Y);
        public static Vector2 operator /(Vector2 first, Vector2 second) => new(first.X / second.X, first.Y / second.Y);
        public static Vector2 operator +(Vector2 first, Vector2 second) => new(first.X + second.X, first.Y + second.Y);
        public static Vector2 operator -(Vector2 first, Vector2 second) => new(first.X - second.X, first.Y - second.Y);
    }

    public readonly record struct Rect2(Vector2 Position, Vector2 Size)
    {
        public Rect2(float x, float y, float width, float height) : this(new(x, y), new(width, height)) { }
    }

    public class Node : GodotObject
    {
        public static readonly List<Node> All = new();
        public static bool FailNextAudioAdd;
        public static SceneTree Tree { get; } = new();
        public readonly List<Node> Children = new();
        public Node? Parent;
        public bool QueuedForDeletion;
        public event Action? TreeExiting;
        public event Action? TreeExited;
        public enum ProcessModeEnum { Inherit, Pausable, WhenPaused, Always, Disabled }
        public ProcessModeEnum ProcessMode { get; set; }
        public Node() => All.Add(this);
        public virtual void AddChild(Node child)
        {
            if (child is AudioStreamPlayer && FailNextAudioAdd)
            {
                FailNextAudioAdd = false;
                throw new InvalidOperationException("injected AddChild(audio) failure");
            }
            child.Parent = this;
            Children.Add(child);
        }
        public void MoveChild(Node child, int index) { }
        public virtual void QueueFree() => QueuedForDeletion = true;
        public bool IsQueuedForDeletion() => QueuedForDeletion;
        public bool IsInsideTree() => !Freed && !QueuedForDeletion;
        public SceneTree GetTree() => Tree;
        public Node? GetParent() => Parent;
        public void RemoveChild(Node child) { Children.Remove(child); child.Parent = null; }
        public override void Free()
        {
            if (Freed) return;
            TreeExiting?.Invoke();
            foreach (Node child in Children.ToArray()) child.Free();
            base.Free();
            Parent?.Children.Remove(this);
            TreeExited?.Invoke();
        }
        public static void FlushDeletion()
        {
            foreach (Node node in All.Where(node => node.QueuedForDeletion).ToArray()) node.Free();
        }
    }

    public class Node2D : Node
    {
        private bool _visible = true;
        public bool FailNextVisibilitySet;
        public bool Visible
        {
            get => _visible;
            set
            {
                if (FailNextVisibilitySet)
                {
                    FailNextVisibilitySet = false;
                    throw new InvalidOperationException("injected visibility failure");
                }
                _visible = value;
            }
        }
        public bool ZAsRelative { get; set; }
        public int ZIndex { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 GlobalPosition
        {
            get => ToGlobal(Vector2.Zero);
            set => Position = Parent is Node2D parent ? parent.ToLocal(value) : value;
        }
        public Vector2 Scale { get; set; } = Vector2.One;
        public Vector2 GlobalScale => Parent is Node2D parent ? parent.GlobalScale * Scale : Scale;
        public Vector2 ToGlobal(Vector2 position)
        {
            Vector2 local = Position + position * Scale;
            return Parent is Node2D parent ? parent.ToGlobal(local) : local;
        }
        public Vector2 ToLocal(Vector2 position)
        {
            Vector2 local = Parent is Node2D parent ? parent.ToLocal(position) : position;
            return (local - Position) / Scale;
        }
    }

    public class Control : Node2D
    {
        public Vector2 Size { get; set; } = new(270, 330);
        public Rect2 GetGlobalRect() => new(GlobalPosition, Size * GlobalScale);
    }

    public class Texture2D : GodotObject { }
    public class AtlasTexture : Texture2D
    {
        public Texture2D? Atlas { get; set; }
        public Rect2 Region { get; set; }
    }
    public class AudioStream : GodotObject { }
    public class SpriteFrames : GodotObject
    {
        public void AddAnimation(StringName name) { }
        public void SetAnimationSpeed(StringName name, double speed) { }
        public void SetAnimationLoop(StringName name, bool loop) { }
        public void AddFrame(StringName name, Texture2D frame) { }
    }
    public static class ResourceLoader
    {
        public static T? Load<T>(string path) where T : GodotObject, new() => new();
    }

    public class AnimatedSprite2D : Node2D
    {
        public static bool FailNextPlay;
        public static bool FailNextStop;
        public SpriteFrames? SpriteFrames { get; set; }
        public StringName Animation { get; set; }
        public bool Centered { get; set; }
        public Vector2 Offset { get; set; }
        public bool Playing;
        public event Action? AnimationFinished;
        public void Play()
        {
            if (FailNextPlay) { FailNextPlay = false; throw new InvalidOperationException("injected sprite Play failure"); }
            Playing = true;
        }
        public void Stop()
        {
            if (FailNextStop) { FailNextStop = false; throw new InvalidOperationException("injected sprite Stop failure"); }
            Playing = false;
        }
        public bool IsPlaying() => Playing;
        public Action CaptureFinished() => AnimationFinished ?? (() => { });
        public void Finish() { Playing = false; AnimationFinished?.Invoke(); }
    }

    public class AudioStreamPlayer : Node
    {
        public static bool FailNextPlay;
        public static bool FailNextStop;
        public AudioStream? Stream { get; set; }
        public StringName Bus { get; set; }
        public float VolumeLinear { get; set; }
        public event Action? Finished;
        public bool Playing;
        public void Play()
        {
            if (FailNextPlay) { FailNextPlay = false; throw new InvalidOperationException("injected audio Play failure"); }
            Playing = true;
        }
        public void Stop()
        {
            if (FailNextStop) { FailNextStop = false; throw new InvalidOperationException("injected audio Stop failure"); }
            Playing = false;
        }
        public void Finish() { Playing = false; Finished?.Invoke(); }
    }

    public class SceneTreeTimer : GodotObject
    {
        public event Action? Timeout;
        public double TimeLeft { get; set; }
        public void Expire() { TimeLeft = 0; Timeout?.Invoke(); }
        public Action CaptureTimeout() => Timeout ?? (() => { });
    }
    public class SceneTree : GodotObject
    {
        public List<SceneTreeTimer> Timers { get; } = new();
        public SceneTreeTimer CreateTimer(double duration, bool processAlways = true, bool processInPhysics = false, bool ignoreTimeScale = false)
        {
            SceneTreeTimer timer = new() { TimeLeft = duration };
            Timers.Add(timer);
            return timer;
        }
    }
    public readonly struct Callable(Action action)
    {
        private static readonly Queue<Action> Pending = new();
        public static Callable From(Action action) => new(action);
        public void CallDeferred() => Pending.Enqueue(action);
        public static void Flush()
        {
            while (Pending.TryDequeue(out Action? action)) action();
        }
    }
    public static class Time
    {
        public static ulong Now = 1000000;
        public static ulong GetTicksMsec() => Now;
        public static ulong GetTicksUsec() => Now * 1000;
    }
}

namespace MegaCrit.Sts2.Core.Entities.Players
{
    public class Player
    {
        public ulong NetId { get; set; }
        public MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature { get; set; } = null!;
    }
}
namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    public class Creature
    {
        public bool IsPlayer { get; set; } = true;
        public bool IsDead { get; set; }
        public MegaCrit.Sts2.Core.Entities.Players.Player? Player { get; set; }
        public MegaCrit.Sts2.Core.Nodes.Combat.NCreature Node { get; set; } = null!;
        public MegaCrit.Sts2.Core.Nodes.Combat.NCreature GetCreatureNode() => Node;
    }
}
namespace MegaCrit.Sts2.Core.Combat
{
    public class CombatState
    {
        public List<MegaCrit.Sts2.Core.Entities.Players.Player> Players { get; } = new();
        public MegaCrit.Sts2.Core.Entities.Players.Player? GetPlayer(ulong id) => Players.SingleOrDefault(player => player.NetId == id);
    }
    public class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        public bool IsInProgress { get; set; } = true;
        public CombatState State { get; set; } = new();
        public CombatState DebugOnlyGetState() => State;
    }
}
namespace MegaCrit.Sts2.Core.Context
{
    public static class LocalContext
    {
        public static MegaCrit.Sts2.Core.Entities.Players.Player? Local;
        public static MegaCrit.Sts2.Core.Entities.Players.Player? GetMe(MegaCrit.Sts2.Core.Combat.CombatState? state) => Local;
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Combat
{
    public class NCreatureVisuals : Godot.Node2D
    {
        public Godot.Node2D Body { get; set; } = new();
        public Godot.Control Bounds { get; } = new();
        public float DefaultScale { get; set; } = 1;
        public Godot.Node2D GetCurrentBody() => Body;
    }
    public class NCreature : Godot.Node2D
    {
        public MegaCrit.Sts2.Core.Entities.Creatures.Creature Entity { get; set; } = null!;
        public NCreatureVisuals Visuals { get; } = new();
        public void SetDefaultScaleTo(float size, float duration)
        {
            Visuals.DefaultScale = size;
            Visuals.Scale = Godot.Vector2.One * size;
        }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    public class NCombatRoom : Godot.Node
    {
        public static NCombatRoom? Instance { get; set; } = new();
        public List<MegaCrit.Sts2.Core.Nodes.Combat.NCreature> CreatureNodes { get; } = new();
    }
}
namespace MegaCrit.Sts2.Core.Audio.Debug
{
    public class NDebugAudioManager : Godot.Node
    {
        public static NDebugAudioManager? Instance { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.Capstones
{
    public class NCapstoneContainer
    {
        public static NCapstoneContainer? Instance { get; set; }
        public bool InUse { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.Map
{
    public class NMapScreen
    {
        public static NMapScreen? Instance { get; set; }
        public bool IsOpen { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.Overlays
{
    public class NOverlayStack
    {
        public static NOverlayStack? Instance { get; set; }
        public int ScreenCount { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext
{
    public class ActiveScreenContext
    {
        public static ActiveScreenContext Instance { get; } = new();
        public event Action? Updated;
        public void Update() => Updated?.Invoke();
    }
}
namespace MegaCrit.Sts2.Core.Multiplayer.Game
{
    public enum NetGameType { Singleplayer, Host, Client }
    public class FakeNetService
    {
        public NetGameType Type { get; set; } = NetGameType.Client;
        public ulong NetId { get; set; } = 1001;
    }
}
namespace MegaCrit.Sts2.Core.Runs
{
    public class RunManager
    {
        public static RunManager? Instance { get; set; } = new();
        public bool IsInProgress { get; set; } = true;
        public MegaCrit.Sts2.Core.Multiplayer.Game.FakeNetService NetService { get; } = new();
    }
}
namespace STS2RitsuLib.Networking.Sidecar
{
    public enum RitsuLibSidecarDeliverySemantics { BestEffort, StableSync }
    public delegate T Deserializer<T>(ReadOnlySpan<byte> data);
    public class RitsuLibSidecarMessageDescriptor<T>(string mod, string key, Func<T, byte[]> serialize, Deserializer<T> deserialize, RitsuLibSidecarDeliverySemantics delivery, bool required)
    {
        public string Mod { get; } = mod;
        public string Key { get; } = key;
        public Func<T, byte[]> Serialize { get; } = serialize;
        public Deserializer<T> Deserialize { get; } = deserialize;
        public RitsuLibSidecarDeliverySemantics Delivery { get; } = delivery;
        public bool Required { get; } = required;
    }
    public readonly record struct RitsuLibSidecarTypedDispatchContext<T>(T Message, ulong SenderNetId);
    public static class RitsuLibSidecarTypedMessageRegistry
    {
        private static object? _handler;
        public static readonly List<object> Sent = new();
        public static object? Descriptor;
        public static bool FailNextSend;
        public static void Register<T>(RitsuLibSidecarMessageDescriptor<T> descriptor) => Descriptor = descriptor;
        public static IDisposable Subscribe<T>(RitsuLibSidecarMessageDescriptor<T> descriptor, Action<RitsuLibSidecarTypedDispatchContext<T>> callback)
        {
            _handler = callback;
            return new Subscription();
        }
        public static void Deliver<T>(T message, ulong sender) => ((Action<RitsuLibSidecarTypedDispatchContext<T>>)_handler!)(new(message, sender));
        public static void Broadcast<T>(MegaCrit.Sts2.Core.Runs.RunManager run, RitsuLibSidecarMessageDescriptor<T> descriptor, T message) => Send(message);
        public static void SendToHost<T>(MegaCrit.Sts2.Core.Runs.RunManager run, RitsuLibSidecarMessageDescriptor<T> descriptor, T message) => Send(message);
        private static void Send<T>(T message)
        {
            if (FailNextSend) { FailNextSend = false; throw new InvalidOperationException("injected network send failure"); }
            Sent.Add(message!);
        }
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
namespace MegaCrit.Sts2.Core.DevConsole
{
    public readonly record struct CmdResult(bool Success, string Message);
}
namespace MegaCrit.Sts2.Core.DevConsole.ConsoleCommands
{
    public abstract class AbstractConsoleCmd
    {
        public abstract string CmdName { get; }
        public abstract string Args { get; }
        public abstract string Description { get; }
        public abstract bool IsNetworked { get; }
        public abstract MegaCrit.Sts2.Core.DevConsole.CmdResult Process(
            MegaCrit.Sts2.Core.Entities.Players.Player? player, string[] args);
    }
}
namespace Nndx.Scripts
{
    public static class Entry
    {
        public const string ModId = "sts_nndx";
        public static TestLogger Logger { get; } = new();
    }
    public class TestLogger
    {
        public List<string> Errors { get; } = new();
        public void Error(string message) => Errors.Add(message);
        public void Warn(string message) => Errors.Add(message);
        public void Info(string message) { }
    }
}
