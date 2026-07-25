using System;
using AshForge.ModLoader;

namespace MyFirstMod
{
    /// <summary>
    /// A mod's entry point is any public class implementing IAshForgeModContext. The loader scans your
    /// assembly for it, constructs it, and calls Init exactly once, early — before the game parses its
    /// definitions, so any Dec types you declare are available in time.
    ///
    /// You never register yourself anywhere. Implementing the interface IS the registration.
    ///
    /// If Init throws, the loader catches it, attributes the fault to your mod by name, and keeps the
    /// game running. You are not going to crash someone's colony by getting this wrong.
    /// </summary>
    public sealed class ModMain : IAshForgeModContext
    {
        private ModSetting _greetOnLoad;

        public void Init(ModContext ctx)
        {
            // ctx.ModId is your id from mod.json. Everything you register through ctx is attributed to
            // it automatically, so you never have to repeat the string.
            Log("hello from " + ctx.ModId);

            // ── A player-facing setting, drawn under ESC ▸ Mod Settings ──────────────────────────
            // Declare it once here and KEEP THE HANDLE. Read the current value off the handle whenever
            // you need it — don't cache the value itself, the player can change it mid-game.
            _greetOnLoad = ctx.AddToggle(
                key: "greet_on_load",
                label: "Say hello in the log",
                defaultValue: true,
                tooltip: "Writes a line to the AshForge log when this mod starts up.");

            if (_greetOnLoad.Bool)
                Log("greeting enabled");

            // ── Periodic work ────────────────────────────────────────────────────────────────────
            // Prefer this over doing work every single frame. The loader staggers each mod's periodic
            // work onto different frames so twenty mods don't all wake up on the same one.
            ctx.ScheduleEvery(600, () => Log("still here"));

            // ── A developer command ──────────────────────────────────────────────────────────────
            // Shows up in the console in a dev session only — a player never sees it, so it is safe to
            // leave in a shipped mod. No strip-before-release step.
            ctx.AddDevCommand("Say hello now", () => Log("hello, on demand"));

            // ── Reading shared state from other mods ─────────────────────────────────────────────
            // The broker lets mods answer questions for each other. This never throws and never
            // requires the other mod to be installed — you get a sensible default if nobody answers,
            // so your mod still works standalone.
            //
            //   ColonyNetWorth worth = ctx.Query<ColonyNetWorth>(Capabilities.ColonyNetWorth);
            //   int tier = worth.Tier;
            //
            // See docs/07-capabilities.md.
        }

        // The loader writes to %TEMP%\ashloader.log. Your own log file is usually nicer for debugging;
        // see docs/09-testing.md.
        private static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "myfirstmod.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { /* never let logging break the mod */ }
        }
    }
}
