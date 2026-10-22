using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;

namespace Nndx.Scripts;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public const string ModId = "sts_nndx";

    public static Logger Logger { get; private set; } = null!;

    public static void Init()
    {
        Logger = RitsuLibFramework.CreateLogger(ModId);

        NndxInput.Initialize();

        NndxOverlaySync.Initialize();
        new Harmony(ModId + ".patches").PatchAll(typeof(Entry).Assembly);

        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => NndxRoomContext.CombatStarting());
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
