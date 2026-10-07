using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace AshForge.NativeKit
{
    // ★ NATIVEKIT 2 — save data and events. A NEW interface, because IModHost is frozen (an adapter may pre-load
    //   another build's core; see NativeKit.cs). Probe for it: `host as IModHost2`. Same rules as NativeKit.cs:
    //   compiled into each mod, cross-copy state in AppDomain data with system types only.

    /// <summary>What a mod's save-data loader receives. Same meaning as the AshForge loader's ModSaveLoad.</summary>
    public sealed class SaveLoad
    {
        /// <summary>The stored blob, or null if none existed for this key in the save's sidecar.</summary>
        public string Data;
        /// <summary>The version the blob was written with (0 if none). Migrate from older versions.</summary>
        public int Version;
        /// <summary>True if this key had a saved blob in the loaded save; false on a fresh or absent one.</summary>
        public bool Existed;
    }

    public interface IModHost2 : IModHost
    {
        /// <summary>
        /// Attach this mod's own state to the game save, in the SAME sidecar the AshForge loader uses
        /// (&lt;save&gt;.xml.ashforge, one blob per key), so a colony's mod data survives switching between a Hub install and
        /// a native one. <paramref name="load"/> runs once the loaded world is live; Existed=false means reset to defaults.
        /// </summary>
        void RegisterSaveData(string key, int currentVersion, Func<string> save, Action<SaveLoad> load);

        /// <summary>
        /// Hear a named event. With the AshForge loader present its bus is THE bus, on both routes; without it, NativeKit
        /// raises the game events itself (DestinationDiscovered, ExpeditionStarted). The handler receives the payload:
        /// read it with <see cref="Events.Field"/>, which works for the loader's payload classes and NativeKit's own.
        /// </summary>
        void Subscribe(string eventName, Action<object> handler);

        void Emit(string eventName, object data = null);
    }

    /// <summary>Event names shared with the AshForge loader's BusEvents, and a payload reader for either route.</summary>
    public static class Events
    {
        public const string DestinationDiscovered = "DestinationDiscovered";   // payload: Dec, Label, Description
        public const string ExpeditionStarted = "ExpeditionStarted";           // payload: Dec
        public const string RaidIncoming = "RaidIncoming";

        /// <summary>A field of an event payload: a NativeKit payload (a dictionary) or a loader payload object
        /// (public field or property of that name). Null when absent.</summary>
        public static object Field(object payload, string name)
        {
            if (payload == null) return null;
            if (payload is IDictionary d) return d.Contains(name) ? d[name] : null;
            Type t = payload.GetType();
            return t.GetField(name)?.GetValue(payload) ?? t.GetProperty(name)?.GetValue(payload);
        }
    }

    /// <summary>The AshForge loader's bus, by reflection — names no loader type at compile time.</summary>
    internal static class LoaderBus
    {
        private static Assembly Loader => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AshLoader");
        internal static bool Present => Loader != null;

        private sealed class Relay
        {
            private readonly Action<object> _h;
            internal Relay(Action<object> h) { _h = h; }
            // Bound to the loader's Action<BusEvent> by delegate contravariance; hands the handler the payload.
            public void Handle(object busEvent)
            {
                object data = busEvent?.GetType().GetProperty("Data")?.GetValue(busEvent);
                try { _h(data); } catch { }
            }
        }

        internal static bool Subscribe(string eventName, string modId, Action<object> handler)
        {
            try
            {
                Type bus = Loader?.GetType("AshForge.ModLoader.Bus");
                Type evt = Loader?.GetType("AshForge.ModLoader.BusEvent");
                MethodInfo sub = bus?.GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(string), typeof(Action<>).MakeGenericType(evt) }, null);
                if (sub == null) return false;
                // The MethodInfo overload: only it binds Handle(object) to Action<BusEvent> by contravariance (the by-name
                // overload demands an exact signature and fails, which silently sent every subscriber to the native bus).
                Delegate d = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(evt), new Relay(handler),
                    typeof(Relay).GetMethod(nameof(Relay.Handle)));
                sub.Invoke(null, new object[] { eventName, modId, d });
                return true;
            }
            catch { return false; }
        }

        internal static bool Emit(string eventName, object data)
        {
            try
            {
                MethodInfo emit = Loader?.GetType("AshForge.ModLoader.Bus")?.GetMethod("Emit", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(object) }, null);
                if (emit == null) return false;
                emit.Invoke(null, new[] { eventName, data });
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>The AshForge loader's save data, by reflection: ModSaveData.Register(modId, key, version, save, Action&lt;ModSaveLoad&gt;).</summary>
    internal static class LoaderSaveData
    {
        private sealed class Relay
        {
            private readonly Action<string, int, bool> _load;
            internal Relay(Action<string, int, bool> load) { _load = load; }
            public void Handle(object info)
            {
                object v = Events.Field(info, "Version"), e = Events.Field(info, "Existed");
                _load(Events.Field(info, "Data") as string, v is int i ? i : 0, e is bool b && b);
            }
        }

        internal static bool TryRegister(string modId, string key, int version, Func<string> save, Action<string, int, bool> load, Action<string> log)
        {
            try
            {
                Assembly asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AshLoader");
                Type info = asm?.GetType("AshForge.ModLoader.ModSaveLoad");
                MethodInfo reg = asm?.GetType("AshForge.ModLoader.ModSaveData")?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(string), typeof(int), typeof(Func<string>), typeof(Action<>).MakeGenericType(info ?? typeof(object)) }, null);
                if (info == null || reg == null) return false;
                Delegate d = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(info), new Relay(load), typeof(Relay).GetMethod(nameof(Relay.Handle)));
                reg.Invoke(null, new object[] { modId, key, version, save, d });
                return true;
            }
            catch (Exception e) { log?.Invoke("[savedata] the AshForge loader's save data refused the registration: " + e.Message); return false; }
        }
    }

    /// <summary>NativeKit's own bus, used only when the AshForge loader is absent. Shared across copies. Its emit points
    /// patch methods the loader also patches, so they are installed only with no loader in the process (see SaveData).</summary>
    internal static class NativeBus
    {
        private const string Key = "ashforge.native.bus";   // Dictionary<string, List<Action<object>>>
        private const string HooksKey = "ashforge.native.bushooks";

        internal static void Subscribe(string eventName, Action<object> handler, string modId, Action<string> log)
        {
            var subs = Shared.Get<Dictionary<string, List<Action<object>>>>(Key);
            lock (subs)
            {
                if (!subs.TryGetValue(eventName, out var list)) subs[eventName] = list = new List<Action<object>>();
                list.Add(handler);
            }
            if (!LoaderBus.Present) EnsureEmitPoints(modId, log);
        }

        internal static void Emit(string eventName, object data)
        {
            var subs = Shared.Get<Dictionary<string, List<Action<object>>>>(Key);
            Action<object>[] snap;
            lock (subs) snap = subs.TryGetValue(eventName, out var list) ? list.ToArray() : new Action<object>[0];
            foreach (var h in snap) { try { h(data); } catch { } }
        }

        // The game events the AshForge loader raises from its own emit points, raised the same way when it is absent:
        // postfixes on ExpeditionManager.AddDestination (DestinationDiscovered) and StartExpedition (ExpeditionStarted).
        private static void EnsureEmitPoints(string modId, Action<string> log)
        {
            lock (typeof(NativeBus))
            {
                if (AppDomain.CurrentDomain.GetData(HooksKey) != null) return;
                AppDomain.CurrentDomain.SetData(HooksKey, modId);
                try
                {
                    var h = new HarmonyLib.Harmony("ashforge.nativekit.emit." + modId);
                    var mgr = typeof(Ascent.Maps.ExpeditionManager);
                    var add = HarmonyLib.AccessTools.Method(mgr, "AddDestination");
                    if (add != null) h.Patch(add, postfix: new HarmonyLib.HarmonyMethod(typeof(NativeBus), nameof(AddDestinationPostfix)));
                    var start = HarmonyLib.AccessTools.Method(mgr, "StartExpedition");
                    if (start != null) h.Patch(start, postfix: new HarmonyLib.HarmonyMethod(typeof(NativeBus), nameof(StartExpeditionPostfix)));
                    log?.Invoke($"[events] native emit points installed (AddDestination={add != null}, StartExpedition={start != null}).");
                }
                catch (Exception e) { log?.Invoke("[events] could not install native emit points: " + e.Message); }
            }
        }

        public static void AddDestinationPostfix(object __0)
        {
            if (__0 == null || LoaderBus.Present) return;   // the loader raises it when present
            Emit(Events.DestinationDiscovered, new Dictionary<string, object>
            {
                ["Dec"] = __0,
                ["Label"] = Events.Field(__0, "Label") as string,
                ["Description"] = Events.Field(__0, "Description") as string,
            });
        }

        public static void StartExpeditionPostfix(object __0)
        {
            if (__0 == null || LoaderBus.Present) return;
            object dest = Events.Field(__0, "Destination");
            object dec = dest != null ? Events.Field(dest, "Dec") : null;
            if (dec != null) Emit(Events.ExpeditionStarted, new Dictionary<string, object> { ["Dec"] = dec });
        }
    }

    /// <summary>
    /// Native save data — THE AshForge LOADER'S SIDECAR, same file and format: &lt;save&gt;.xml.ashforge, JSON
    /// {"format":1,"world":"seed:N","mods":{"&lt;key&gt;":{"version":v,"data":"…"}}}, and the same origin check (a sidecar
    /// stamped with a different world is ignored: a deleted colony's file under a reused name).
    ///
    /// ★ MERGE, NEVER CLOBBER. A player can have some mods on the loader route and some native. The loader writes its
    ///   providers' blobs first (its StartSaveToFile postfix was patched at startup); this postfix runs at the LOWEST
    ///   priority, re-reads the file and adds the native providers' blobs, keeping every other key — unless the file on
    ///   disk belongs to a different world, in which case nothing of it is kept.
    /// One shared provider list and one set of hooks for every NativeKit copy in the process.
    ///
    /// ★★ NOT USED WHEN THE AshForge LOADER IS PRESENT. The loader patches these same game methods with its own Harmony
    ///   copy (AshHarmony); a second Harmony copy patching the same method REPLACES the first one's patches instead of
    ///   joining them. Reproduced 2026-10-07: a native mod's hooks here silently switched off the loader's sidecar
    ///   save AND load for every loader-route mod. With the loader present, providers register with the loader itself
    ///   (LoaderSaveData) and nothing here patches. ⇒ NativeKit must never patch a method the loader patches.
    /// </summary>
    internal static class SaveData
    {
        private const string ProvidersKey = "ashforge.native.savedata";      // List<Dictionary<string, object>>
        private const string HooksKey = "ashforge.native.savedatahooks";
        private const string PendingKey = "ashforge.native.savedatapending"; // string: the staged sidecar's JSON ("" = none)
        private const string Suffix = ".ashforge";

        internal static void Register(string modId, string key, int version, Func<string> save, Action<string, int, bool> load, Action<string> log)
        {
            if (LoaderBus.Present && LoaderSaveData.TryRegister(modId, key, version, save, load, log))
            {
                log?.Invoke($"[savedata] '{key}' registered with the AshForge loader's save data.");
                return;
            }
            var list = Shared.Get<List<Dictionary<string, object>>>(ProvidersKey);
            lock (list)
            {
                list.RemoveAll(p => string.Equals((string)p["key"], key, StringComparison.OrdinalIgnoreCase));
                list.Add(new Dictionary<string, object> { ["modId"] = modId, ["key"] = key, ["version"] = version, ["save"] = save, ["load"] = load });
            }
            EnsureHooks(modId, log);
        }

        private static void EnsureHooks(string modId, Action<string> log)
        {
            lock (typeof(SaveData))
            {
                if (AppDomain.CurrentDomain.GetData(HooksKey) != null) return;
                AppDomain.CurrentDomain.SetData(HooksKey, modId);
                try
                {
                    var h = new HarmonyLib.Harmony("ashforge.nativekit.savedata." + modId);
                    var menu = typeof(Ascent.UI.SaveMenu);
                    var save = HarmonyLib.AccessTools.Method(menu, "StartSaveToFile");
                    var load = HarmonyLib.AccessTools.Method(menu, "StartLoadFromFile");
                    var activate = HarmonyLib.AccessTools.Method(typeof(Ascent.Worlds.World), "ActivateMap");
                    if (save == null || load == null || activate == null)
                    { log?.Invoke("[savedata] game save/load methods not found — mod save data unavailable."); return; }
                    // Lowest priority: after the AshForge loader's own sidecar write, so we merge into what it wrote.
                    var last = new HarmonyLib.HarmonyMethod(typeof(SaveData), nameof(SavePostfix)) { priority = HarmonyLib.Priority.Last };
                    h.Patch(save, postfix: last);
                    h.Patch(load, postfix: new HarmonyLib.HarmonyMethod(typeof(SaveData), nameof(LoadPostfix)));
                    h.Patch(activate, postfix: new HarmonyLib.HarmonyMethod(typeof(SaveData), nameof(ActivatePostfix)));
                    log?.Invoke("[savedata] sidecar hooks installed (save, load, world activate).");
                }
                catch (Exception e) { log?.Invoke("[savedata] could not install hooks: " + e.Message); }
            }
        }

        private static string PathFor(object fileName)
        {
            try { return fileName is string f ? Ascent.UI.SaveMenu.GetSaveFilePath(f) : null; } catch { return null; }
        }

        /// <summary>"seed:&lt;home map seed&gt;" — the loader's SaveIdentity, so both routes stamp and check the same world.</summary>
        internal static string WorldStamp()
        {
            try
            {
                object seed = Ascent.Worlds.World.Active?.HomeMap?.Data.Seed;
                return seed == null ? null : "seed:" + Convert.ToString(seed, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        public static void SavePostfix(object __0)
        {
            try
            {
                string path = PathFor(__0);
                var list = AppDomain.CurrentDomain.GetData(ProvidersKey) as List<Dictionary<string, object>>;
                if (path == null || list == null) return;
                Dictionary<string, object>[] providers;
                lock (list) providers = list.ToArray();
                if (providers.Length == 0) return;

                string world = WorldStamp();
                string sidecar = path + Suffix;
                var mods = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(sidecar))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
                        string onDisk = doc.RootElement.TryGetProperty("world", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                        bool sameWorld = onDisk == null || world == null || onDisk == world;
                        if (sameWorld && doc.RootElement.TryGetProperty("mods", out var m) && m.ValueKind == JsonValueKind.Object)
                            foreach (var e in m.EnumerateObject())
                                mods[e.Name] = new Dictionary<string, object>
                                {
                                    ["version"] = e.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0,
                                    ["data"] = e.Value.TryGetProperty("data", out var dv) && dv.ValueKind == JsonValueKind.String ? dv.GetString() : null,
                                };
                    }
                }
                catch { mods.Clear(); }   // an unreadable sidecar is replaced, never fatal

                foreach (var p in providers)
                {
                    try
                    {
                        string blob = ((Func<string>)p["save"])();
                        if (blob != null) mods[(string)p["key"]] = new Dictionary<string, object> { ["version"] = (int)p["version"], ["data"] = blob };
                    }
                    catch { }
                }

                var env = new Dictionary<string, object> { ["format"] = 1, ["world"] = world, ["mods"] = mods };
                File.WriteAllText(sidecar, JsonSerializer.Serialize(env));
            }
            catch { }
        }

        public static void LoadPostfix(object __0)
        {
            string json = "";
            try
            {
                string path = PathFor(__0);
                if (path != null && File.Exists(path + Suffix)) json = File.ReadAllText(path + Suffix);
            }
            catch { json = ""; }
            AppDomain.CurrentDomain.SetData(PendingKey, json);   // always armed: a load with no sidecar resets mods to defaults
        }

        public static void ActivatePostfix()
        {
            if (!(AppDomain.CurrentDomain.GetData(PendingKey) is string json)) return;
            AppDomain.CurrentDomain.SetData(PendingKey, null);
            var list = AppDomain.CurrentDomain.GetData(ProvidersKey) as List<Dictionary<string, object>>;
            if (list == null) return;
            Dictionary<string, object>[] providers;
            lock (list) providers = list.ToArray();

            var blobs = new Dictionary<string, (int version, string data)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (json.Length > 0)
                {
                    using var doc = JsonDocument.Parse(json);
                    string onDisk = doc.RootElement.TryGetProperty("world", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                    string live = WorldStamp();
                    // Refuse only a definite mismatch (the loader's rule): both known and different.
                    if (!(onDisk != null && live != null && onDisk != live)
                        && doc.RootElement.TryGetProperty("mods", out var m) && m.ValueKind == JsonValueKind.Object)
                        foreach (var e in m.EnumerateObject())
                            blobs[e.Name] = (e.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0,
                                             e.Value.TryGetProperty("data", out var dv) && dv.ValueKind == JsonValueKind.String ? dv.GetString() : null);
                }
            }
            catch { blobs.Clear(); }

            foreach (var p in providers)
            {
                var load = (Action<string, int, bool>)p["load"];
                try
                {
                    if (blobs.TryGetValue((string)p["key"], out var b)) load(b.data, b.version, true);
                    else load(null, 0, false);
                }
                catch { }
            }
        }
    }

    public sealed partial class NativeHost : IModHost2
    {
        public void RegisterSaveData(string key, int currentVersion, Func<string> save, Action<SaveLoad> load)
        {
            if (save == null || load == null) return;
            SaveData.Register(ModId, string.IsNullOrWhiteSpace(key) ? ModId : key, currentVersion, save,
                (data, version, existed) => load(new SaveLoad { Data = data, Version = version, Existed = existed }), _log);
        }

        public void Subscribe(string eventName, Action<object> handler)
        {
            if (handler == null) return;
            if (LoaderBus.Present)
            {
                if (LoaderBus.Subscribe(eventName, ModId, handler)) return;
                _log($"[events] could not subscribe to '{eventName}' on the AshForge loader's bus; this mod will not receive it.");
            }
            NativeBus.Subscribe(eventName, handler, ModId, _log);
        }

        public void Emit(string eventName, object data = null)
        {
            if (LoaderBus.Present && LoaderBus.Emit(eventName, data)) return;
            NativeBus.Emit(eventName, data);
        }
    }
}
