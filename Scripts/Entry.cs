using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.RuntimeInput;

namespace Nndx.Scripts;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public const string ModId = "sts_nndx";

    public static Logger Logger { get; private set; } = null!;

    public static void Init()
    {
        Logger = RitsuLibFramework.CreateLogger(ModId);

        RuntimeHotkeyService.Register(
            "C",
            NndxVideoOverlay.Toggle,
            new RuntimeHotkeyOptions
            {
                Id = $"{ModId}.toggle_video",
                DisplayName = "Toggle NNDX Video",
                Description = "In combat, hide your character and play the NNDX video at their position.",
                Category = "sts_nndx",
            });

        NndxOverlaySync.Initialize();

        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => NndxVideoOverlay.Preload());
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(_ =>
        {
            NndxVideoOverlay.ForceStop();
#if NNDX_TEST_TOOLS
            NndxScaleConsoleCmd.RestoreOriginalSize();
#endif
        });
        RitsuLibFramework.SubscribeLifecycle<CreatureDiedEvent>(OnCreatureDied);
    }

    private static void OnCreatureDied(CreatureDiedEvent e)
    {
        if (e.WasRemovalPrevented)
        {
            return;
        }

        NndxVideoOverlay.HandlePlayerDeath(e.Creature);
    }
}
