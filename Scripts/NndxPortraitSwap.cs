using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace Nndx.Scripts;

internal static class NndxPortraitSwap
{
    internal const byte FaceCount = 4;
    private static readonly Texture2D?[] Faces = new Texture2D?[FaceCount];
    private static bool _preloaded;

    internal sealed record Handle(TextureRect Icon, Texture2D? Original, Texture2D Replacement)
    {
        internal void Restore()
        {
            // Do not overwrite an icon another mod has subsequently replaced.
            if (GodotObject.IsInstanceValid(Icon) && ReferenceEquals(Icon.Texture, Replacement))
                Icon.Texture = Original;
        }
    }

    internal static void Preload()
    {
        if (_preloaded) return;
        _preloaded = true;
        for (int i = 0; i < FaceCount; i++)
        {
            string path = $"res://res/face/face{i + 1}.png";
            try
            {
                Faces[i] = ResourceLoader.Load<Texture2D>(path);
                if (Faces[i] == null) Entry.Logger.Warn($"NNDX portrait missing: {path}");
            }
            catch (Exception ex) { Entry.Logger.Error($"NNDX portrait preload failed: {ex}"); }
        }
    }

    internal static Handle? Apply(ulong playerNetId, byte face)
    {
        if (RunManager.Instance?.NetService.Type is not (NetGameType.Host or NetGameType.Client)) return null;
        var container = NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer;
        if (container == null) return null;
        foreach (Node child in container.GetChildren())
            if (child is NMultiplayerPlayerState state && state.Player.NetId == playerNetId)
                return Apply(state, face);
        return null;
    }

    internal static Handle? Apply(NMultiplayerPlayerState state, byte face)
    {
        if (RunManager.Instance?.NetService.Type is not (NetGameType.Host or NetGameType.Client)
            || face >= FaceCount) return null;
        Preload();
        TextureRect? icon = state.GetNodeOrNull<TextureRect>("%CharacterIcon");
        Texture2D? replacement = Faces[face];
        if (icon == null || replacement == null) return null;
        var handle = new Handle(icon, icon.Texture, replacement);
        icon.Texture = replacement;
        return handle;
    }
}

// Event-driven handling if the game's player panel is rebuilt during playback.
[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
internal static class NndxPortraitPanelReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerPlayerState __instance)
        => NndxVideoOverlay.OnPortraitPanelReady(__instance);
}
