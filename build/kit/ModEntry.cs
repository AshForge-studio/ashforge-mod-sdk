using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AshForge.NativeKit
{
    /// <summary>
    /// The one thing a mod built with the AshForge Mod SDK implements. <see cref="Start"/> runs once per game
    /// session, whichever way the mod was installed — extracted into the game's Mods folder, or through the AshForge
    /// Hub — and the host it receives works the same either way.
    /// </summary>
    public interface INativeMod
    {
        void Start(IModHost2 host);
    }

    /// <summary>
    /// Start-up shared by every SDK-built mod. The SDK generates a two-line entry class per mod that calls
    /// <see cref="Run"/>: <c>ApplyAll(route)</c> from the mod's bootstrap dec (the game's mod loader), and
    /// <c>StartFromLoader(spec)</c> from the mod's AshForge adapter. This finds the mod's <see cref="INativeMod"/>,
    /// starts it once per process, and checks it for Harmony patches that collide with the AshForge loader's.
    /// </summary>
    public static class ModEntry
    {
        private static readonly object Gate = new object();

        public static void Run(Assembly mod, string modId, string displayName, string route, Dictionary<string, object> loaderSpec)
        {
            Action<string> log = Logger(modId, displayName);
            string key = "ashforge.mod.applied." + modId;
            lock (Gate)
            {
                if (AppDomain.CurrentDomain.GetData(key) is string first)
                {
                    if (first != route) log($"already started through the {first}; the copy loaded through the {route} stands down.");
                    return;
                }
                AppDomain.CurrentDomain.SetData(key, route);
            }

            try
            {
                Type[] types;
                try { types = mod.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                Type[] mains = types.Where(t => typeof(INativeMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToArray();
                if (mains.Length != 1)
                {
                    log(mains.Length == 0
                        ? "no class implements INativeMod — nothing to start."
                        : "more than one class implements INativeMod (" + string.Join(", ", mains.Select(t => t.FullName)) + ") — not started.");
                    return;
                }

                IModHost2 host = loaderSpec != null
                    ? new DelegateHost(loaderSpec, log)
                    : new NativeHost(modId, route, log, displayName);
                ((INativeMod)Activator.CreateInstance(mains[0])).Start(host);
                log($"started through the {route}.");

                HarmonyGuard.Check(mod, log);
                bool rechecked = false;
                host.OnGameTick(() => { if (rechecked) return; rechecked = true; HarmonyGuard.Check(mod, log); });
            }
            catch (Exception e) { log("start failed: " + e); }
        }

        /// <summary>To %TEMP%/&lt;mod id&gt;.log and to the game's own log, which is the only log a native player has.</summary>
        public static Action<string> Logger(string modId, string displayName)
        {
            string file = Path.Combine(Path.GetTempPath(), modId + ".log");
            string tag = "[" + (string.IsNullOrEmpty(displayName) ? modId : displayName) + "] ";
            return m =>
            {
                try { File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine); } catch { }
                try { Godot.GD.Print(tag + m); } catch { }
            };
        }
    }

    /// <summary>
    /// ★★ TWO HARMONY COPIES CANNOT SHARE A METHOD. The AshForge loader patches with its own copy of Harmony
    /// (AshHarmony); a mod patches with the standard 0Harmony it ships. When both patch the same game method, the
    /// later one replaces the earlier one's patches instead of joining them, and nothing reports it (found
    /// 2026-10-07: it silently switched off the loader's save data). This compares the two copies' registries —
    /// exact, with no list to keep current — and says so loudly. Checked once at start and again on the first frame.
    /// </summary>
    public static class HarmonyGuard
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        public static void Check(Assembly mod, Action<string> log)
        {
            try
            {
                Assembly ash = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AshHarmony");
                if (ash == null) return;   // no AshForge loader in the process: nothing to collide with
                var theirs = new HashSet<MethodBase>(Patched(ash.GetType("HarmonyLib.Harmony")));
                if (theirs.Count == 0) return;
                foreach (MethodBase m in Patched(typeof(HarmonyLib.Harmony)))
                {
                    // Only this mod's own patches: another native mod's collision is reported by that mod.
                    if (!theirs.Contains(m) || !PatchedBy(m, mod)) continue;
                    string name = m.DeclaringType?.FullName + "." + m.Name;
                    lock (Reported) if (!Reported.Add(mod.GetName().Name + "|" + name)) continue;
                    log($"[harmony] ✗ {name} is patched by this mod AND through the AshForge loader (the loader itself, or a mod it loaded). Two copies of Harmony cannot "
                      + "share a method: one side's patches are lost. Patch a different method, or use the host's events "
                      + "and save data instead of patching this one.");
                }
            }
            catch (Exception e) { log("[harmony] collision check skipped: " + e.Message); }
        }

        private static bool PatchedBy(MethodBase method, Assembly mod)
        {
            HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info == null) return false;
            return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                       .Any(p => p.PatchMethod?.DeclaringType?.Assembly == mod);
        }

        private static IEnumerable<MethodBase> Patched(Type harmony)
        {
            object all = harmony?.GetMethod("GetAllPatchedMethods", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            return all is IEnumerable e ? e.OfType<MethodBase>().ToList() : new List<MethodBase>();
        }
    }
}
