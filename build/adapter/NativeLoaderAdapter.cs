using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Ascent.Engine;
using AshForge.ModLoader;

namespace AshForge.NativeAdapter
{
    /// <summary>
    /// ★ SHARED SOURCE, compiled into each mod's <c>&lt;Mod&gt;.Loader.dll</c> (linked, not referenced — a shared dll
    /// would be one more assembly name to keep identical across mods). Patches 1.1.1 proved the pattern by hand.
    ///
    /// Every AshForge mod is NATIVE: <c>Assembly/&lt;Mod&gt;.Native.dll</c> is loaded by the game's own mod loader and
    /// started by <c>Dec/Bootstrap.xml</c>, and references nothing of ours. The AshForge loader reads only
    /// <c>Assemblies/</c>, where this adapter is the only file: it loads the same native assembly into the game's
    /// context and calls the same entry point, so a Hub install runs exactly what a native install runs. No loader or
    /// Hub change; works on every loader already shipped.
    ///
    /// ★ If an active native copy of the same mod exists (found by the mod id in its mod.json, so ANY version counts),
    ///   the adapter stands down BEFORE loading anything: the game's mod loader will load that copy a few lines later,
    ///   and the mod's own once-marker would only catch the double start after a second assembly had been loaded.
    /// </summary>
    public abstract class NativeLoaderAdapter : IAshForgeModContext
    {
        protected abstract string ModId { get; }          // mod.json id
        protected abstract string DisplayName { get; }
        protected abstract string CoreAssembly { get; }   // Assembly/<this>.dll, never a name an older build shipped
        protected abstract string CoreType { get; }       // static class with public static void <EntryMethod>(string route)
        protected virtual string EntryMethod => "ApplyAll";
        protected virtual string LogFile => "ashforge-adapters.log";

        /// <summary>Assembly names an older NATIVE build of this mod shipped without a mod.json (e.g. RU 0.10.0's
        /// AshRuLocalizerNative). An active native folder holding one of these counts as an active native copy.</summary>
        protected virtual string[] LegacyNativeDlls => Array.Empty<string>();

        /// <summary>The loader context, for adapters that hand their core a NativeKit LoaderHost.</summary>
        protected ModContext Context { get; private set; }

        public void Init(ModContext ctx)
        {
            Context = ctx;
            try
            {
                string modDir = Path.GetDirectoryName(Path.GetDirectoryName(GetType().Assembly.Location));
                string asmDir = Path.Combine(modDir, "Assembly");

                string native = ActiveNativeCopy();
                if (native != null)
                {
                    Log($"standing down: {DisplayName} is also installed in the game's Mods folder ({native}) and is active "
                      + "there; the game's mod loader runs that copy.");
                    // ★★ PRE-LOAD THE CORE ANYWAY — FROM THE NATIVE COPY'S OWN FOLDER — WITHOUT STARTING IT.
                    //    This adapter assembly is already loaded, and an adapter that hands its core a LoaderHost has types
                    //    whose signatures name the core (LoaderHost : IModHost). The game's ModManager.LoadMods scans every
                    //    loaded assembly's types (dec's GetAllUserTypes, unguarded) BEFORE it loads native mods; with the core
                    //    absent that scan throws and the game RESETS THE PLAYER'S MOD LIST (reproduced 2026-10-07). Loading
                    //    the exact file the game is about to load makes the scan resolve, and the game's own load of that same
                    //    file then gets this assembly back. The native copy still starts itself, from its dec PostLoad.
                    string nativeAsm = Path.Combine(ModManager.ModFolderPath, native, "Assembly");
                    Assembly pre = Load(File.Exists(Path.Combine(nativeAsm, CoreAssembly + ".dll")) ? nativeAsm : asmDir);
                    if (pre != null)
                    {
                        // The loader is still here, so loader-only extras still apply to the native copy.
                        try { WhileNativeRuns(pre); }
                        catch (Exception ex) { Log("loader extras for the native copy failed: " + ex.Message); }
                    }
                    return;
                }

                Assembly core = Load(asmDir);
                if (core != null) Start(core);
            }
            catch (Exception e) { Log("adapter failed: " + e); }
        }

        /// <summary>
        /// Load 0Harmony (if nothing has yet; from <c>Harmony/</c> beside <paramref name="dir"/>, else <paramref name="dir"/>)
        /// and the core from <paramref name="dir"/> into the game's context; an assembly
        /// already loaded under that name is reused, never loaded twice. Null if the core is missing.
        /// </summary>
        private Assembly Load(string dir)
        {
            AssemblyLoadContext gameCtx = AssemblyLoadContext.GetLoadContext(typeof(ModManager).Assembly) ?? AssemblyLoadContext.Default;
            Assembly core = null;
            foreach (string name in new[] { "0Harmony", CoreAssembly })
            {
                // ★ The game context's assemblies: an assembly of this name in another context (the loader's own, a tool's)
                //   is not what the core's references bind to.
                Assembly have = gameCtx.Assemblies.FirstOrDefault(a => a.GetName().Name == name);
                if (have == null)
                {
                    string path = Path.Combine(dir, name + ".dll");
                    // ★★ 2026-10-08: Harmony ships in <mod>/Harmony/, outside the folder the game loads — a second,
                    //   different 0Harmony.dll in Assembly/ makes the game delete the player's mod list. Older native
                    //   builds still carry it in Assembly/; either is the same pinned file.
                    if (name == "0Harmony")
                    {
                        string moved = Path.Combine(Path.GetDirectoryName(dir), "Harmony", name + ".dll");
                        if (File.Exists(moved)) path = moved;
                    }
                    if (!File.Exists(path))
                    {
                        if (name == "0Harmony") continue;   // a mod that does not patch ships no Harmony
                        Log("missing " + Path.GetFileName(path) + " in " + dir + "; not started.");
                        return null;
                    }
                    have = gameCtx.LoadFromAssemblyPath(path);
                }
                if (name == CoreAssembly) core = have;
            }
            return core;
        }

        /// <summary>
        /// Start the core, already loaded into the game's context. Default: <c>CoreType.EntryMethod("AshForge loader")</c>
        /// by reflection (mods that use no loader services). A NativeKit mod overrides this to call its core's
        /// <c>Start(IModHost)</c> with a LoaderHost — the types that call names are resolved only when this runs, after
        /// the core assembly is in.
        /// </summary>
        protected virtual void Start(Assembly core)
        {
            MethodInfo entry = core.GetType(CoreType)?.GetMethod(EntryMethod, BindingFlags.Public | BindingFlags.Static);
            if (entry == null) { Log($"{CoreType}.{EntryMethod} not found; not started."); return; }
            entry.Invoke(null, new object[] { "AshForge loader" });
        }

        /// <summary>
        /// Called when this adapter stood down for an active native copy, once the game's mod loader has loaded that
        /// copy's core. The core will start itself natively; override to add what only the AshForge loader can give
        /// it (e.g. mirroring the loader's own settings into NativeKit's registry). Default: nothing.
        /// </summary>
        protected virtual void WhileNativeRuns(Assembly core) { }

        private string ActiveNativeCopy()
        {
            try
            {
                string modsDir = ModManager.ModFolderPath;
                string activePath = Path.Combine(DirectoryUtility.GlobalizePath("user://"), "ActiveMods.xml");
                if (!Directory.Exists(modsDir) || !File.Exists(activePath)) return null;
                foreach (XElement li in XDocument.Load(activePath).Root?.Elements("li") ?? Enumerable.Empty<XElement>())
                {
                    string folder = li.Value.Trim();
                    if (folder.Length == 0) continue;
                    string dir = Path.Combine(modsDir, folder);
                    if (!File.Exists(Path.Combine(dir, "About.xml"))) continue;
                    string json = Path.Combine(dir, "mod.json");
                    if (File.Exists(json) && File.ReadAllText(json).Contains("\"" + ModId + "\"")) return folder;
                    foreach (string legacy in LegacyNativeDlls)
                        if (File.Exists(Path.Combine(dir, "Assembly", legacy + ".dll"))) return folder;
                }
            }
            catch (Exception e) { Log("could not check for a native install (assuming none): " + e.Message); }
            return null;
        }

        protected void Log(string m)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), LogFile),
                    DateTime.Now.ToString("HH:mm:ss") + $"  [{ModId} adapter] " + m + System.Environment.NewLine);
            }
            catch { }
        }
    }
}
