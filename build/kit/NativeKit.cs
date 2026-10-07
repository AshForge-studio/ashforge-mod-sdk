using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AshForge.NativeKit
{
    // ★★ NATIVEKIT — the services our mods used to get only from the AshForge loader, available in the game's OWN mod
    //   system. HARD RULE (2026-10-07): every mod must work unzipped into <game>/Mods with nothing else installed.
    //
    // ★ COMPILED INTO EACH MOD (linked source), NEVER SHIPPED AS A SHARED DLL. A shared dll would be one name carried by
    //   many mods; two mods with different builds of it, both active, is "Assembly with same name is already loaded"
    //   and the game resets the player's whole mod list (reproduced 2026-10-07). Compiled in, every mod has its own
    //   copy under its own assembly name, so nothing can collide.
    //
    // ★ CROSS-COPY STATE LIVES IN AppDomain DATA, AND ONLY IN SYSTEM TYPES. Several mods, each with its own NativeKit
    //   build, share one tick hook, one settings registry, one capability registry and one dev-command list. Those are
    //   stored under string keys as List<…>/Dictionary<string, object>/delegates — never as a NativeKit type, because
    //   one mod's NativeKit types are not another mod's.
    //
    // Two hosts implement IModHost: NativeHost (here) for the game's mod loader, and LoaderHost (compiled into each
    // mod's *.Loader adapter) which forwards to the real AshForge loader so Hub users keep its settings screen, broker
    // and sidecars. A mod codes against IModHost only.
    //
    // ★★ IModHost AND ISetting ARE A FROZEN CONTRACT (since 2026-10-07). An adapter that stands down for an active native
    //   copy pre-loads THAT copy's core — possibly an older or newer build — so its LoaderHost must implement whatever
    //   IModHost that core carries. Never add, remove or change a member of these two interfaces: a mismatch is a
    //   TypeLoadException in the game's unguarded type scan, i.e. a reset mod list. New services go in a NEW interface
    //   (IModHost2 : IModHost), probed for at runtime.

    public interface ISetting
    {
        bool Bool { get; }
        double Number { get; }
        int Int { get; }
        int Index { get; }
    }

    public interface IModHost
    {
        string ModId { get; }
        /// <summary>"game mod loader" or "AshForge loader" — for log lines.</summary>
        string Route { get; }

        ISetting AddToggle(string key, string label, bool defaultValue, string tooltip = null, Action onChanged = null);
        ISetting AddSlider(string key, string label, double min, double max, double defaultValue,
                           bool integral = false, string tooltip = null, Action onChanged = null);
        ISetting AddChoice(string key, string label, string[] options, int defaultIndex, string tooltip = null, Action onChanged = null);

        /// <summary>Once per frame, paused or not (natively: SceneTree.ProcessFrame; through the loader: its GameTick,
        /// which Tools pumps every frame). Poll input here, and keep the work cheap.</summary>
        void OnGameTick(Action handler);
        /// <summary>Every <paramref name="intervalTicks"/> frames, staggered per mod like the loader's ModScheduler.</summary>
        void ScheduleEvery(int intervalTicks, Action work);

        /// <summary>A developer command: shown only in a dev session (see <see cref="Shared.DevSession"/>).</summary>
        void AddDevCommand(string category, string label, Action action);
        /// <summary>A support command safe in a player's hands: always shown in the Tools console.</summary>
        void AddPlayerCommand(string category, string label, Action action);

        /// <summary>Publish a value other mods can read. Exclusive: one provider per id.</summary>
        void PublishCapability<T>(string owner, string name, int version, Func<T> provide, T fallback,
                                  Func<T, bool> validate = null, string unit = null, Func<T, double> scalar = null,
                                  double scalarMin = double.NaN, double scalarMax = double.NaN, int refreshEveryTicks = 0);
        /// <summary>Read a value another mod publishes, whichever route either mod came through.</summary>
        bool TryQuery<T>(string owner, string name, int version, out T value);
    }

    /// <summary>The shared, cross-copy registries. System types only (see the header).</summary>
    public static class Shared
    {
        public const string SettingsKey = "ashforge.native.settings";        // List<Dictionary<string, object>>
        public const string CapabilitiesKey = "ashforge.native.capabilities"; // Dictionary<string, Func<object>>
        public const string DevCommandsKey = "ashforge.native.devcommands";  // List<Dictionary<string, object>>
        public const string TickKey = "ashforge.native.tick";                // List<Action>
        public const string TickHookKey = "ashforge.native.tickhook";        // string: who installed it
        public const string ModNamesKey = "ashforge.native.modnames";        // Dictionary<string, string> id -> display name
        public const string RefreshKey = "ashforge.native.refresh";          // List<Action>: run before drawing settings/commands
        private static readonly object Gate = new object();

        /// <summary>
        /// A developer session — SAME RULE AS THE AshForge LOADER's DevConsole.Enabled: ASHFORGE_DEV=1, or
        /// ASHLOADER_ALLOW_UNSIGNED=1, or a %TEMP%/ashforge.dev marker file. Players get player-safe commands only.
        /// </summary>
        public static bool DevSession
        {
            get
            {
                try
                {
                    return System.Environment.GetEnvironmentVariable("ASHFORGE_DEV") == "1"
                        || System.Environment.GetEnvironmentVariable("ASHLOADER_ALLOW_UNSIGNED") == "1"
                        || File.Exists(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ashforge.dev"));
                }
                catch { return false; }
            }
        }

        public static void RegisterModName(string modId, string name)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(name)) return;
            var names = Get<Dictionary<string, string>>(ModNamesKey);
            lock (names) names[modId] = name;
        }

        public static string DisplayName(string modId)
        {
            var names = Get<Dictionary<string, string>>(ModNamesKey);
            lock (names) return names.TryGetValue(modId ?? "", out string n) ? n : modId;
        }

        /// <summary>Run every registered refresher (e.g. a LoaderHost mirroring the loader's own settings and commands),
        /// so a screen about to draw the registries sees everything current.</summary>
        public static void Refresh()
        {
            var list = Get<List<Action>>(RefreshKey);
            Action[] snap;
            lock (list) snap = list.ToArray();
            foreach (Action a in snap) { try { a(); } catch { } }
            // No adapter mirrored the AshForge loader (e.g. Tools installed only natively, alongside Hub-only mods):
            // mirror it here, by reflection, so those mods' settings and commands still appear.
            if (snap.Length == 0) LoaderBridge.Mirror();
        }

        public static void AddRefresher(Action a)
        {
            if (a == null) return;
            var list = Get<List<Action>>(RefreshKey);
            lock (list) list.Add(a);
        }

        public static T Get<T>(string key) where T : class, new()
        {
            lock (Gate)
            {
                if (AppDomain.CurrentDomain.GetData(key) is T have) return have;
                var made = new T();
                AppDomain.CurrentDomain.SetData(key, made);
                return made;
            }
        }

        public static string CapabilityKey(string owner, string name, int version) => owner + "/" + name + "@" + version;

        public static void Publish(string owner, string name, int version, Func<object> provide)
        {
            var caps = Get<Dictionary<string, Func<object>>>(CapabilitiesKey);
            lock (caps) caps[CapabilityKey(owner, name, version)] = provide;
        }

        public static bool TryRead<T>(string owner, string name, int version, out T value)
        {
            value = default;
            var caps = Get<Dictionary<string, Func<object>>>(CapabilitiesKey);
            Func<object> f;
            lock (caps) caps.TryGetValue(CapabilityKey(owner, name, version), out f);
            if (f == null) return false;
            try { if (f() is T t) { value = t; return true; } } catch { }
            return false;
        }

        /// <summary>One settings-registry row. Everything a settings screen needs, as plain data plus get/set.</summary>
        public static void RegisterSettingRow(string modId, string key, string label, string tooltip, string kind,
                                              double min, double max, bool integral, string[] options, object dflt,
                                              Func<object> get, Action<object> set)
        {
            var rows = Get<List<Dictionary<string, object>>>(SettingsKey);
            lock (rows)
            {
                rows.RemoveAll(r => (string)r["modId"] == modId && (string)r["key"] == key);
                rows.Add(new Dictionary<string, object>
                {
                    ["modId"] = modId, ["key"] = key, ["label"] = label, ["tooltip"] = tooltip, ["kind"] = kind,
                    ["min"] = min, ["max"] = max, ["integral"] = integral, ["options"] = options, ["default"] = dflt,
                    ["get"] = get, ["set"] = set,
                });
            }
        }

        public static void RegisterDevCommand(string modId, string category, string label, Action action, bool playerSafe = false)
        {
            if (action == null || string.IsNullOrEmpty(label)) return;
            var cmds = Get<List<Dictionary<string, object>>>(DevCommandsKey);
            lock (cmds)
            {
                cmds.RemoveAll(r => (string)r["modId"] == modId && (string)r["category"] == category && (string)r["label"] == label);
                cmds.Add(new Dictionary<string, object>
                {
                    ["modId"] = modId, ["category"] = category, ["label"] = label, ["action"] = action, ["playerSafe"] = playerSafe,
                });
            }
        }

        /// <summary>The commands this session may see: all of them in a dev session, the player-safe ones otherwise.</summary>
        public static List<Dictionary<string, object>> VisibleCommands()
        {
            var cmds = Get<List<Dictionary<string, object>>>(DevCommandsKey);
            bool dev = DevSession;
            lock (cmds) return cmds.Where(r => dev || (r.TryGetValue("playerSafe", out object p) && p is bool b && b)).ToList();
        }
    }

    /// <summary>
    /// REFLECTION-ONLY view of the AshForge loader, for when it is in the process but no adapter mirrored it. Names no
    /// loader type at compile time (this file is compiled into native cores). Mirrors the loader's ModSettings and
    /// DevConsole into the shared registries; a row's setter writes back through ModSettings.Apply, a command runs
    /// through DevConsole.Invoke, exactly as the loader's own screens would.
    /// </summary>
    internal static class LoaderBridge
    {
        internal static void Mirror()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AshLoader");
                if (asm == null) return;
                const System.Reflection.BindingFlags PS = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                Type settingsT = asm.GetType("AshForge.ModLoader.ModSettings");
                Type settingT = asm.GetType("AshForge.ModLoader.ModSetting");
                Type consoleT = asm.GetType("AshForge.ModLoader.DevConsole");
                if (settingsT == null || settingT == null) return;

                var displayName = settingsT.GetMethod("DisplayName", PS, null, new[] { typeof(string) }, null);
                var apply = settingsT.GetMethods(PS).FirstOrDefault(m => m.Name == "Apply" && m.GetParameters().Length == 1);
                Type dictT = typeof(Dictionary<,>).MakeGenericType(settingT, typeof(object));
                object P(object o, string n) => o.GetType().GetProperty(n)?.GetValue(o);

                if (settingsT.GetProperty("Settings", PS)?.GetValue(null) is Array all)
                {
                    foreach (object s in all)
                    {
                        object captured = s;
                        string modId = (string)P(s, "ModId");
                        string kind = P(s, "Kind")?.ToString() ?? "";
                        kind = kind == "Toggle" ? "toggle" : kind == "Slider" ? "slider" : "choice";
                        RegisterSettingRow(modId, (string)P(s, "Key"), (string)P(s, "Label"), (string)P(s, "Tooltip"), kind,
                            Convert.ToDouble(P(s, "Min") ?? 0.0), Convert.ToDouble(P(s, "Max") ?? 0.0), P(s, "Integral") is bool b && b,
                            P(s, "Options") as string[], P(s, "Default"),
                            () => P(captured, "Value") ?? P(captured, "Default"),
                            v =>
                            {
                                var d = (System.Collections.IDictionary)Activator.CreateInstance(dictT);
                                d[captured] = v;
                                apply?.Invoke(null, new object[] { d });
                            });
                        if (displayName != null) RegisterModName(modId, displayName.Invoke(null, new object[] { modId }) as string);
                    }
                }

                var invoke = consoleT?.GetMethod("Invoke", PS);
                if (consoleT?.GetProperty("Commands", PS)?.GetValue(null) is Array cmds)
                {
                    foreach (object c in cmds)
                    {
                        object captured = c;
                        string modId = (string)P(c, "ModId");
                        RegisterDevCommand(modId, (string)P(c, "Category"), (string)P(c, "Label"),
                            () => invoke?.Invoke(null, new[] { captured }), P(c, "PlayerSafe") is bool ps && ps);
                        if (displayName != null) RegisterModName(modId, displayName.Invoke(null, new object[] { modId }) as string);
                    }
                }
            }
            catch { }
        }

        private static void RegisterSettingRow(string modId, string key, string label, string tooltip, string kind, double min,
            double max, bool integral, string[] options, object dflt, Func<object> get, Action<object> set)
            => Shared.RegisterSettingRow(modId, key, label, tooltip, kind, min, max, integral, options, dflt, get, set);
        private static void RegisterDevCommand(string modId, string category, string label, Action action, bool playerSafe)
            => Shared.RegisterDevCommand(modId, category, label, action, playerSafe);
        private static void RegisterModName(string modId, string name) => Shared.RegisterModName(modId, name);
    }

    /// <summary>
    /// The settings file. SAME FILE AND FORMAT AS THE AshForge LOADER (ModSettings.cs): <c>modId/key=value</c> lines in
    /// <c>&lt;loader mods root&gt;/ashforge.settings.cfg</c>, invariant culture, <c>#</c> comments. When the loader is installed
    /// (its ashloader.mods.txt sits beside the game's assemblies) we use its file, so a player's settings follow them
    /// between a Hub install and a native one. Without it: user://ashforge.settings.cfg.
    /// Every write re-reads the file first and merges, so a value the loader wrote this session is not clobbered.
    /// </summary>
    internal static class SettingsFile
    {
        private static readonly object Gate = new object();
        private static string _path;

        internal static string PathOf()
        {
            if (_path != null) return _path;
            string p = null;
            try
            {
                string dataDir = System.IO.Path.GetDirectoryName(typeof(Ascent.Engine.ModManager).Assembly.Location);
                string cfg = System.IO.Path.Combine(dataDir, "ashloader.mods.txt");
                if (File.Exists(cfg))
                {
                    string root = File.ReadAllLines(cfg).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
                    if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) p = System.IO.Path.Combine(root, "ashforge.settings.cfg");
                }
            }
            catch { }
            if (p == null)
            {
                try { p = System.IO.Path.Combine(Ascent.Engine.DirectoryUtility.GlobalizePath("user://"), "ashforge.settings.cfg"); }
                catch { p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ashforge.settings.cfg"); }
            }
            return _path = p;
        }

        internal static Dictionary<string, string> Read()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string p = PathOf();
                if (!File.Exists(p)) return d;
                foreach (string line in File.ReadAllLines(p))
                {
                    string t = line?.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#", StringComparison.Ordinal)) continue;
                    int eq = t.IndexOf('=');
                    if (eq > 0) d[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return d;
        }

        internal static void Write(string storeKey, string value)
        {
            lock (Gate)
            {
                try
                {
                    var d = Read();
                    d[storeKey] = value;
                    var sb = new StringBuilder();
                    sb.AppendLine("# AshForge mod settings. Managed by the game's ESC ▸ Mod Settings screen;");
                    sb.AppendLine("# safe to edit by hand — an unreadable line is ignored, not fatal.");
                    foreach (var kv in d.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append('=').AppendLine(kv.Value);
                    string p = PathOf(), tmp = p + ".tmp";
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                    File.WriteAllText(tmp, sb.ToString());
                    if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                }
                catch { }
            }
        }
    }

    /// <summary>A setting owned by NativeHost: value in memory, persisted through <see cref="SettingsFile"/>.</summary>
    internal sealed class NativeSetting : ISetting
    {
        internal string ModId, Key, Kind, Label, Tooltip;
        internal double Min, Max;
        internal bool Integral;
        internal string[] Options;
        internal object Default, Value;
        internal Action OnChanged;

        public bool Bool => Value is bool b ? b : Default is bool d && d;
        public double Number => Value is double n ? n : (Default is double dn ? dn : 0);
        public int Int => (int)Math.Round(Number);
        public int Index => Value is int i ? i : (Default is int di ? di : 0);

        internal string StoreKey => ModId + "/" + Key;

        internal string Serialize() => Kind == "toggle" ? (Bool ? "true" : "false")
            : Kind == "slider" ? Number.ToString("R", CultureInfo.InvariantCulture)
            : Index.ToString(CultureInfo.InvariantCulture);

        /// <summary>Same parsing and clamping as the loader's ApplyStoredLocked: a bad stored value falls back to the default.</summary>
        internal void Load(string raw)
        {
            if (raw == null) return;
            try
            {
                if (Kind == "toggle" && bool.TryParse(raw, out bool b)) Value = b;
                else if (Kind == "slider" && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    Value = Math.Clamp(Integral ? Math.Round(n) : n, Min, Max);
                else if (Kind == "choice" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                         && Options != null && i >= 0 && i < Options.Length) Value = i;
            }
            catch { }
        }

        internal void Set(object v)
        {
            object nv = v;
            if (Kind == "slider" && v is IConvertible)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                nv = Math.Clamp(Integral ? Math.Round(d) : d, Min, Max);
            }
            if (Equals(nv, Value ?? Default)) return;
            Value = nv;
            SettingsFile.Write(StoreKey, Serialize());
            try { OnChanged?.Invoke(); } catch { }
        }
    }

    /// <summary>The host a mod gets when the game's own mod loader started it.</summary>
    public sealed partial class NativeHost : IModHost
    {
        public string ModId { get; }
        public string Route { get; }
        private readonly Action<string> _log;
        private readonly Dictionary<string, string> _stored;

        public NativeHost(string modId, string route, Action<string> log, string displayName = null)
        {
            ModId = modId; Route = route; _log = log ?? (_ => { });
            _stored = SettingsFile.Read();
            Shared.RegisterModName(modId, displayName ?? NameFromAbout());
        }

        /// <summary>The mod's own name from its About.xml (this NativeKit copy is compiled into the mod's core, which lives
        /// in &lt;mod&gt;/Assembly/), so the settings rail reads "Optics", not "ashforge.optics".</summary>
        private static string NameFromAbout()
        {
            try
            {
                string asmDir = System.IO.Path.GetDirectoryName(typeof(NativeHost).Assembly.Location);
                string about = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(asmDir), "About.xml");
                if (!File.Exists(about)) return null;
                string n = System.Xml.Linq.XDocument.Load(about).Root?.Element("Name")?.Value?.Trim();
                return string.IsNullOrEmpty(n) ? null : n;
            }
            catch { return null; }
        }

        private ISetting Add(NativeSetting s)
        {
            s.ModId = ModId;
            if (_stored.TryGetValue(s.StoreKey, out string raw)) s.Load(raw);
            Shared.RegisterSettingRow(ModId, s.Key, s.Label, s.Tooltip, s.Kind, s.Min, s.Max, s.Integral, s.Options,
                                      s.Default, () => s.Value ?? s.Default, v => s.Set(v));
            return s;
        }

        public ISetting AddToggle(string key, string label, bool defaultValue, string tooltip = null, Action onChanged = null)
        {
            return Add(new NativeSetting { Key = key, Label = label, Tooltip = tooltip, Kind = "toggle", Default = defaultValue, OnChanged = onChanged });
        }

        public ISetting AddSlider(string key, string label, double min, double max, double defaultValue,
                                  bool integral = false, string tooltip = null, Action onChanged = null)
        {
            return Add(new NativeSetting
            {
                Key = key, Label = label, Tooltip = tooltip, Kind = "slider", Min = min, Max = max, Integral = integral,
                OnChanged = onChanged, Default = Math.Clamp(integral ? Math.Round(defaultValue) : defaultValue, min, max),
            });
        }

        public ISetting AddChoice(string key, string label, string[] options, int defaultIndex, string tooltip = null, Action onChanged = null)
        {
            options ??= new string[0];
            return Add(new NativeSetting
            {
                Key = key, Label = label, Tooltip = tooltip, Kind = "choice", Options = options, OnChanged = onChanged,
                Default = defaultIndex >= 0 && defaultIndex < options.Length ? defaultIndex : 0,
            });
        }

        public void OnGameTick(Action handler)
        {
            if (handler == null) return;
            var list = Shared.Get<List<Action>>(Shared.TickKey);
            lock (list) list.Add(Guard(handler, "a game-tick handler"));
            TickHook.Ensure(ModId, _log);
        }

        // ★ The game calls no mod code itself, so nothing above a native mod's handlers would ever report a
        //   fault: the shared tick loop and bus must survive one mod's exception and keep running the others, and
        //   they did — silently. Every handler a native host installs is wrapped here and reported to the mod's
        //   own log: the first five of each kind in full, then every thousandth, so a fault on every frame cannot flood it.
        //   (Through the AshForge loader, its own fault isolation reports them instead.)
        private readonly Dictionary<string, int> _faults = new Dictionary<string, int>();

        internal Action Guard(Action handler, string what) => () =>
        {
            try { handler(); }
            catch (Exception e) { Fault(what, e); }
        };

        internal void Fault(string what, Exception e)
        {
            // Counted per kind of handler, so a fault on every frame cannot hide a rarer one behind it.
            int n;
            lock (_faults) { _faults.TryGetValue(what, out n); _faults[what] = ++n; }
            if (n <= 5 || n % 1000 == 0)
                _log($"[fault] {what} threw (time {n}): {e}");
        }

        public void ScheduleEvery(int intervalTicks, Action work)
        {
            if (work == null) return;
            int every = Math.Max(1, intervalTicks);
            // Stagger like ModScheduler: a stable per-(mod, slot) offset so mods do not all land on one frame.
            int slot;
            lock (_slots) { slot = _slots.Count; _slots.Add(every); }
            int offset = (int)((uint)StableHash(ModId + "#" + slot) % (uint)every);
            long tick = 0;
            Action guarded = Guard(work, "scheduled work");
            OnGameTick(() => { if ((tick++ + offset) % every == 0) guarded(); });
        }
        private readonly List<int> _slots = new List<int>();

        public void AddDevCommand(string category, string label, Action action) =>
            Shared.RegisterDevCommand(ModId, category ?? ModId, label, action, playerSafe: false);

        public void AddPlayerCommand(string category, string label, Action action) =>
            Shared.RegisterDevCommand(ModId, category ?? ModId, label, action, playerSafe: true);

        public void PublishCapability<T>(string owner, string name, int version, Func<T> provide, T fallback,
                                         Func<T, bool> validate = null, string unit = null, Func<T, double> scalar = null,
                                         double scalarMin = double.NaN, double scalarMax = double.NaN, int refreshEveryTicks = 0)
        {
            Shared.Publish(owner, name, version, () =>
            {
                try
                {
                    T v = provide();
                    if (validate != null && !validate(v)) return fallback;
                    return v;
                }
                catch { return fallback; }
            });
            _log($"[capability] published {Shared.CapabilityKey(owner, name, version)} (native registry).");
        }

        public bool TryQuery<T>(string owner, string name, int version, out T value) => Shared.TryRead(owner, name, version, out value);

        private static int StableHash(string s)
        {
            unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; }
        }
    }

    /// <summary>
    /// The one per-frame hook, shared by every NativeKit copy in the process. The first copy to arrive connects it;
    /// it walks the shared handler list, so later copies only add handlers.
    ///
    /// ★ SceneTree.ProcessFrame, NOT the loader's MouseMode.Update postfix. MouseMode.Update does not run every frame,
    ///   so input polled from it fires intermittently — which is why Tools has always re-pumped the loader's GameTick
    ///   from ProcessFrame. A native install has no such pump underneath, so NativeKit ticks from ProcessFrame
    ///   directly: every idle frame, paused or not — what a loader-route player already gets with Tools installed.
    ///   Falls back to the MouseMode.Update postfix if the scene tree is not reachable.
    /// </summary>
    internal static class TickHook
    {
        private static readonly object Gate = new object();

        internal static void Ensure(string modId, Action<string> log)
        {
            lock (Gate)
            {
                if (AppDomain.CurrentDomain.GetData(Shared.TickHookKey) is string by) return;
                try
                {
                    if (Godot.Engine.GetMainLoop() is Godot.SceneTree tree)
                    {
                        tree.ProcessFrame += Postfix;
                        AppDomain.CurrentDomain.SetData(Shared.TickHookKey, modId);
                        log("[tick] per-frame hook connected (SceneTree.ProcessFrame).");
                        return;
                    }
                    // ★ Never while the AshForge loader is in the process: it patches MouseMode.Update with its own Harmony copy,
                    //   and a second copy patching the same method replaces the loader's patch (see NativeKit2 SaveData).
                    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AshLoader"))
                    { log("[tick] no scene tree yet, and the AshForge loader owns MouseMode.Update — game ticks unavailable."); return; }
                    var update = HarmonyLib.AccessTools.Method(typeof(Ascent.UI.MouseMode), "Update");
                    if (update == null) { log("[tick] no scene tree and no MouseMode.Update — game ticks unavailable."); return; }
                    new HarmonyLib.Harmony("ashforge.nativekit.tick." + modId).Patch(update,
                        postfix: new HarmonyLib.HarmonyMethod(typeof(TickHook), nameof(Postfix)));
                    AppDomain.CurrentDomain.SetData(Shared.TickHookKey, modId);
                    log("[tick] per-frame hook installed (MouseMode.Update fallback).");
                }
                catch (Exception e) { log("[tick] could not install the per-frame hook: " + e.Message); }
            }
        }

        public static void Postfix()
        {
            var list = AppDomain.CurrentDomain.GetData(Shared.TickKey) as List<Action>;
            if (list == null || list.Count == 0) return;
            Action[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (Action a in snapshot)
            {
                try { a(); } catch { }
            }
        }
    }
}
