using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace Nndx.Scripts;

/// <summary>Compiled only with EnableNndxTestTools=true for manual singleplayer tests.</summary>
public sealed class NndxScaleConsoleCmd : AbstractConsoleCmd
{
    private static NCreature? _testCreature;
    private static float _originalDefaultScale;

    public override string CmdName => "nndx_scale";

    public override string Args => "<0.1-3|reset>";

    public override string Description => "Singleplayer visual test: set default body scale, or restore its pre-test value. Temporary shrink effects still multiply this scale.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (RunManager.Instance?.NetService.Type != NetGameType.Singleplayer
            || !CombatManager.Instance.IsInProgress || issuingPlayer == null)
        {
            return new CmdResult(false, "This visual test command is only available in singleplayer combat.");
        }

        if (args.Length != 1)
        {
            return new CmdResult(false, "Usage: nndx_scale <0.1-3|reset>");
        }

        try
        {
            if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                RestoreOriginalSize();
                return new CmdResult(true, "Restored the pre-test default body scale.");
            }

            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)
                || !float.IsFinite(scale) || scale < 0.1f || scale > 3f)
            {
                return new CmdResult(false, "Enter a scale from 0.1 to 3, e.g. nndx_scale 0.5 or nndx_scale 2.");
            }

            NCreature? creature = issuingPlayer.Creature.GetCreatureNode();
            if (creature == null || !GodotObject.IsInstanceValid(creature)
                || creature.IsQueuedForDeletion() || !creature.IsInsideTree())
            {
                return new CmdResult(false, "Player visual is unavailable.");
            }

            if (!ReferenceEquals(_testCreature, creature))
            {
                RestoreOriginalSize();
                _testCreature = creature;
                _originalDefaultScale = creature.Visuals.DefaultScale;
            }

            // Change only the visual default scale; do not add powers, change relics,
            // modify saved run data, or overwrite the game's temporary scale factor.
            creature.SetDefaultScaleTo(scale, 0f);
            return new CmdResult(true, $"Default body scale: {scale.ToString("P0", CultureInfo.InvariantCulture)}. Temporary scaling effects still apply. Use nndx_scale reset to restore.");
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX visual scale test failed: {ex}");
            return new CmdResult(false, "Could not change the body scale; see the game log.");
        }
    }

    internal static void RestoreOriginalSize()
    {
        NCreature? creature = _testCreature;
        _testCreature = null;
        if (creature == null || !GodotObject.IsInstanceValid(creature)
            || creature.IsQueuedForDeletion() || !creature.IsInsideTree())
        {
            return;
        }

        try
        {
            creature.SetDefaultScaleTo(_originalDefaultScale, 0f);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"NNDX visual scale test restore failed: {ex}");
        }
    }
}
