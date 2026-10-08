using System.Globalization;
using AshForge.NativeKit;

namespace MyFirstMod
{
    /// <summary>
    /// Your mod. The SDK finds this class (the one that implements <see cref="INativeMod"/>) and calls
    /// <see cref="Start"/> once per game session, whichever way the player installed the mod:
    ///
    ///   • extracted into the game's Mods folder and enabled in the in-game Mods menu, or
    ///   • installed through the AshForge Hub.
    ///
    /// The host works the same either way. Settings show under ESC ▸ Mod Settings when AshForge Tools is
    /// installed, and are kept in one file either way; save data rides beside the save in the same format
    /// either way, so a colony can switch between the two kinds of install without losing it.
    ///
    /// Start runs while the game is still reading its definitions, before any map exists. Set things up here;
    /// do the work later, from a tick, an event or a command.
    /// </summary>
    public sealed class ModMain : INativeMod
    {
        private ISetting _enabled;
        private int _ticks;

        public void Start(IModHost2 host)
        {
            // Your log: %TEMP%\<mod id>.log, and the game's own log (the only one a player without the Hub has).
            var log = ModEntry.Logger(host.ModId, "My First Mod");

            // A player-facing setting. Read it where you use it (it changes live).
            _enabled = host.AddToggle("enabled", "Count frames", true,
                tooltip: "An example toggle. Turn it off and the counter stops.");

            // Periodic work. 600 frames is about 10 seconds; frames run while the game is paused, so for
            // anything tied to the game's own time, read the game clock instead of counting calls.
            host.ScheduleEvery(600, () => { if (_enabled.Bool) _ticks++; });

            // State that should survive a save. Kept beside the save file, never inside it, so removing your
            // mod can never stop a save from loading. The key is shared by every mod in the save: use your id.
            host.RegisterSaveData(host.ModId, 1,
                save: () => _ticks.ToString(CultureInfo.InvariantCulture),
                load: s => _ticks = s.Existed && int.TryParse(s.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0);

            // A button in the AshForge Tools console, shown only in a dev session. Safe to leave in a release.
            host.AddDevCommand(null, "Say hello", () => log($"hello — {_ticks} ticks counted"));

            // Changing how the game behaves: Harmony is referenced and shipped for you.
            //   GamePatches.Apply(new HarmonyLib.Harmony(host.ModId), typeof(ModMain).Assembly);
            // Mark patch classes [GamePatch(...)], never [HarmonyPatch(...)] — the build refuses that one, because
            // it breaks the game. Read docs/05-content-and-harmony.md first.

            // More: docs/03-lifecycle-and-api.md (the host), docs/06-capabilities.md (talking to other mods),
            // docs/07-testing-and-debugging.md (where your log goes and what to look for).
        }
    }
}
