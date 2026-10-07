using System;
using System.Collections.Generic;

namespace AshForge.NativeKit
{
    /// <summary>
    /// The host a mod gets when the AshForge loader started it through a DELEGATE SPEC — the adapter pattern for every
    /// mod converted after Optics and Tools.
    ///
    /// ★★ WHY DELEGATES. An adapter's assembly stays loaded even when it stands down for an active native copy, and the
    ///   game scans every loaded assembly's types before loading native mods. If any adapter type names a core type
    ///   (LoaderHost : IModHost) and the core that ends up loaded is a different build without that exact type, the scan
    ///   throws and the game resets the player's mod list. So the adapter references NOTHING from the core: it builds a
    ///   Dictionary&lt;string, object&gt; of system-typed delegates backed by the loader (AshForge.NativeKit.LoaderSpec) and
    ///   passes it by reflection to the core's StartFromLoader. This class turns that dictionary back into IModHost2.
    ///   Version skew between adapter and core can no longer break the scan; a missing key simply degrades that service.
    /// </summary>
    public sealed class DelegateHost : IModHost2
    {
        private readonly Dictionary<string, object> _s;
        private readonly Action<string> _log;

        public DelegateHost(Dictionary<string, object> spec, Action<string> log)
        {
            _s = spec ?? new Dictionary<string, object>();
            _log = log ?? (_ => { });
            Shared.RegisterModName(ModId, Get<string>("displayName"));
        }

        private T Get<T>(string key) where T : class => _s.TryGetValue(key, out object v) ? v as T : null;

        public string ModId => Get<string>("modId") ?? "unknown";
        public string Route => Get<string>("route") ?? "AshForge loader";

        private sealed class Setting : ISetting
        {
            private readonly Func<object> _get;
            public Setting(Func<object> get) { _get = get; }
            private object V { get { try { return _get?.Invoke(); } catch { return null; } } }
            public bool Bool => V is bool b && b;
            public double Number => V is IConvertible c ? Convert.ToDouble(c, System.Globalization.CultureInfo.InvariantCulture) : 0;
            public int Int => (int)Math.Round(Number);
            public int Index => V is int i ? i : 0;
        }

        // Each add* delegate registers with the loader and returns object[] { Func<object> get, Action<object> set }.
        private ISetting Row(object[] r, string key, string label, string tooltip, string kind, double min, double max,
                             bool integral, string[] options, object dflt)
        {
            var get = r?.Length > 0 ? r[0] as Func<object> : null;
            var set = r?.Length > 1 ? r[1] as Action<object> : null;
            if (get != null && set != null)
                Shared.RegisterSettingRow(ModId, key, label, tooltip, kind, min, max, integral, options, dflt, get, set);
            return new Setting(get ?? (() => dflt));
        }

        public ISetting AddToggle(string key, string label, bool defaultValue, string tooltip = null, Action onChanged = null)
        {
            var f = Get<Func<string, string, bool, string, Action, object[]>>("addToggle");
            return Row(f?.Invoke(key, label, defaultValue, tooltip, onChanged), key, label, tooltip, "toggle", 0, 0, false, null, defaultValue);
        }

        public ISetting AddSlider(string key, string label, double min, double max, double defaultValue,
                                  bool integral = false, string tooltip = null, Action onChanged = null)
        {
            var f = Get<Func<string, string, double, double, double, bool, string, Action, object[]>>("addSlider");
            double d = Math.Clamp(integral ? Math.Round(defaultValue) : defaultValue, min, max);
            return Row(f?.Invoke(key, label, min, max, defaultValue, integral, tooltip, onChanged), key, label, tooltip, "slider", min, max, integral, null, d);
        }

        public ISetting AddChoice(string key, string label, string[] options, int defaultIndex, string tooltip = null, Action onChanged = null)
        {
            var f = Get<Func<string, string, string[], int, string, Action, object[]>>("addChoice");
            return Row(f?.Invoke(key, label, options, defaultIndex, tooltip, onChanged), key, label, tooltip, "choice", 0, 0, false, options, defaultIndex);
        }

        public void OnGameTick(Action handler) => Get<Action<Action>>("onGameTick")?.Invoke(handler);
        public void ScheduleEvery(int intervalTicks, Action work) => Get<Action<int, Action>>("scheduleEvery")?.Invoke(intervalTicks, work);
        public void AddDevCommand(string category, string label, Action action) => Get<Action<string, string, Action>>("addDevCommand")?.Invoke(category ?? ModId, label, action);
        public void AddPlayerCommand(string category, string label, Action action) => Get<Action<string, string, Action>>("addPlayerCommand")?.Invoke(category ?? ModId, label, action);

        public void PublishCapability<T>(string owner, string name, int version, Func<T> provide, T fallback,
                                         Func<T, bool> validate = null, string unit = null, Func<T, double> scalar = null,
                                         double scalarMin = double.NaN, double scalarMax = double.NaN, int refreshEveryTicks = 0)
        {
            Func<object> p = () => { try { T v = provide(); return validate != null && !validate(v) ? fallback : v; } catch { return fallback; } };
            Get<Action<string, string, int, Type, Func<object>, object, Func<object, bool>, string, Func<object, double>, double, double, int>>("publishCapability")
                ?.Invoke(owner, name, version, typeof(T), p, fallback,
                         validate == null ? null : (Func<object, bool>)(o => o is T t && validate(t)), unit,
                         scalar == null ? null : (Func<object, double>)(o => o is T t ? scalar(t) : double.NaN),
                         scalarMin, scalarMax, refreshEveryTicks);
            Shared.Publish(owner, name, version, p);
        }

        public bool TryQuery<T>(string owner, string name, int version, out T value)
        {
            var q = Get<Func<string, string, int, Type, object[]>>("tryQuery");
            try
            {
                object[] r = q?.Invoke(owner, name, version, typeof(T));
                if (r != null && r.Length > 1 && r[0] is bool found && found && r[1] is T t) { value = t; return true; }
            }
            catch { }
            return Shared.TryRead(owner, name, version, out value);
        }

        public void RegisterSaveData(string key, int currentVersion, Func<string> save, Action<SaveLoad> load)
        {
            if (save == null || load == null) return;
            Get<Action<string, int, Func<string>, Action<string, int, bool>>>("registerSaveData")
                ?.Invoke(string.IsNullOrWhiteSpace(key) ? ModId : key, currentVersion, save,
                         (data, version, existed) => load(new SaveLoad { Data = data, Version = version, Existed = existed }));
        }

        public void Subscribe(string eventName, Action<object> handler) => Get<Action<string, Action<object>>>("subscribe")?.Invoke(eventName, handler);
        public void Emit(string eventName, object data = null) => Get<Action<string, object>>("emit")?.Invoke(eventName, data);
    }
}
