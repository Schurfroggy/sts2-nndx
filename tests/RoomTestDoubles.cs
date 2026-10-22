namespace MegaCrit.Sts2.Core.Map
{
    public record struct MapCoord(int col, int row);
}
namespace MegaCrit.Sts2.Core.Rooms
{
    public enum RoomType { Unassigned, Monster, Elite, Boss, Treasure, Shop, Event, RestSite, Map }
    public enum CombatRoomMode { ActiveCombat, FinishedCombat, VisualOnly }
    public class AbstractRoom
    {
        public int? Id { get; set; } = 42;
        public RoomType RoomType { get; set; } = RoomType.Event;
        public bool IsPreFinished { get; set; }
    }
    public class CombatRoom : AbstractRoom
    {
        public CombatRoom() => RoomType = RoomType.Monster;
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Rooms
{
    public class NMerchantRoom : Godot.Control
    {
        public List<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCharacter> PlayerVisuals { get; } = new();
    }
    public class NEventRoom : Godot.Control
    {
        public Godot.Node? CustomEventNode { get; set; }
    }
}
namespace MegaCrit.Sts2.Core.Nodes.Screens.Shops
{
    public class NMerchantCharacter : Godot.Node2D { }
}
namespace MegaCrit.Sts2.Core.Nodes.Events.Custom
{
    public class NFakeMerchant : Godot.Control { }
}
namespace MegaCrit.Sts2.Core.Bindings.MegaSpine
{
    public class TestSpineSprite : Godot.Node2D
    {
        public bool SkeletonReady { get; set; } = true;
        public Godot.Rect2 SkeletonBounds { get; set; } = new(0, -600, 300, 600);
        public override string GetClass() => "SpineSprite";
    }
    public class MegaSprite(Godot.Node2D body)
    {
        public MegaSkeleton? GetSkeleton() => ((TestSpineSprite)body).SkeletonReady ? new((TestSpineSprite)body) : null;
    }
    public class MegaSkeleton(TestSpineSprite body)
    {
        public Godot.Rect2 GetBounds() => body.SkeletonBounds;
    }
}
