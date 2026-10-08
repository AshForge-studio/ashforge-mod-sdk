using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AshForge.NativeKit
{
    /// <summary>
    /// Makes 0Harmony available to this mod, from <c>&lt;mod&gt;/Harmony/0Harmony.dll</c> — a folder the game's mod loader
    /// never reads. Call <see cref="Ensure"/> FIRST in the bootstrap dec's PostLoad, before anything that touches Harmony.
    ///
    /// ★★★ WHY HARMONY IS NOT IN Assembly/. The game loads every dll in every active mod's Assembly/ folder, and when two
    ///   of them share a name but are not byte-identical (two builds of 0Harmony.dll, say) it throws "Assembly with same
    ///   name is already loaded" and DELETES ActiveMods.xml and ModOrder.xml — the player's whole mod list, silently, in
    ///   either load order (reproduced 2026-10-08). Any mod that ships its own Harmony there can be half of that pair.
    ///   Ours never are: by the time a bootstrap PostLoad runs the game has loaded every Assembly/ dll, so if another
    ///   mod brought a 0Harmony, this finds it and our code binds to it; if none did, this loads ours. One Harmony, one
    ///   patch registry, either way.
    ///
    /// ★ NOTHING IN THIS FILE MAY NAME A HARMONY TYPE. It runs before Harmony is loaded; a Harmony token in any method the
    ///   JIT compiles before Ensure returns would fail to resolve. The caller must likewise keep its own body free of
    ///   Harmony and call into the mod through a [MethodImpl(NoInlining)] method.
    /// </summary>
    internal static class HarmonyHome
    {
        private const string Name = "0Harmony";
        private static readonly Version Built = new Version(2, 3, 3, 0);   // the 0Harmony this mod was compiled against
        private static bool? _result;                                       // PostLoad may run more than once per launch

        /// <summary>Ensure, reporting to <c>%TEMP%\&lt;logFile&gt;</c> and the game's log. Once per launch.</summary>
        internal static bool Ensure(string modName, string logFile)
        {
            if (_result.HasValue) return _result.Value;
            _result = Ensure(modName, m => Report(modName, logFile, m));
            return _result.Value;
        }

        /// <summary>One line to the mod's own log and the game's log — used by a bootstrap that could not start.</summary>
        internal static void Report(string modName, string logFile, string message)
        {
            try { Godot.GD.Print($"[{modName}] {message}"); } catch { }
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), logFile),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// True when a usable 0Harmony is loaded in this mod's load context. <paramref name="report"/> says which one —
        /// the only place a player's log names another mod's Harmony when that is the one in use.
        /// </summary>
        internal static bool Ensure(string modName, Action<string> report)
        {
            try
            {
                Assembly self = typeof(HarmonyHome).Assembly;
                AssemblyLoadContext ctx = AssemblyLoadContext.GetLoadContext(self) ?? AssemblyLoadContext.Default;

                // ★ This context's assemblies, not AppDomain.GetAssemblies(): a 0Harmony in another context (the AshForge
                //   loader's, or a tool's) is not what our references bind to from here.
                Assembly have = ctx.Assemblies.FirstOrDefault(a => a.GetName().Name == Name);
                if (have != null) return Usable(have, "already loaded", modName, report);

                string modDir = Path.GetDirectoryName(Path.GetDirectoryName(self.Location));
                string path = Path.Combine(modDir, "Harmony", Name + ".dll");
                if (!File.Exists(path))
                {
                    report?.Invoke($"[harmony] {modName}: no Harmony found ({path} is missing); the mod cannot start. "
                                 + "Reinstall it from its original download.");
                    return false;
                }
                return Usable(ctx.LoadFromAssemblyPath(path), "loaded", modName, report);
            }
            catch (Exception e)
            {
                report?.Invoke($"[harmony] {modName}: could not load Harmony — {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        private static bool Usable(Assembly h, string how, string modName, Action<string> report)
        {
            Version v = h.GetName().Version;
            string where = string.IsNullOrEmpty(h.Location) ? "(no file)" : h.Location;
            if (v < Built)
            {
                report?.Invoke($"[harmony] ✗ {modName}: another mod loaded an older Harmony ({v}, {where}); this mod needs "
                             + $"{Built} or newer and does not start. The game itself is unaffected. Updating or removing "
                             + "the mod that ships that file fixes it.");
                return false;
            }
            report?.Invoke($"[harmony] {how}: 0Harmony {v} from {where}");
            return true;
        }
    }
}
