using System;
using System.Collections.Generic;
using System.Reflection;
using AshForge.ModLoader;

namespace AshForge.NativeKit
{
    /// <summary>
    /// ★ COMPILED INTO ADAPTERS ONLY (it references AshLoader). Builds the delegate spec a core's DelegateHost runs on:
    /// every service the core needs, backed by the real AshForge loader, as SYSTEM-TYPED delegates only — so the adapter
    /// assembly names no core type and can never break the game's type scan, whatever core build ends up loaded.
    /// Keys are the contract (see DelegateHost); add keys, never change one's shape.
    /// </summary>
    public static class LoaderSpec
    {
        public static Dictionary<string, object> Build(ModContext ctx, string displayName)
        {
            var s = new Dictionary<string, object>
            {
                ["modId"] = ctx.ModId,
                ["route"] = "AshForge loader",
                ["displayName"] = displayName ?? ModSettings.DisplayName(ctx.ModId),
            };

            object[] Handle(ModSetting m) => new object[]
            {
                // Already coerced by the loader, by kind: the core never has to guess how a reloaded value was boxed.
                (Func<object>)(() => m.Kind == SettingKind.Toggle ? (object)m.Bool : m.Kind == SettingKind.Slider ? (object)m.Number : m.Index),
                (Action<object>)(v => ModSettings.Apply(new Dictionary<ModSetting, object> { [m] = v })),
            };
            s["addToggle"] = (Func<string, string, bool, string, Action, object[]>)((k, l, d, t, c) => Handle(ctx.AddToggle(k, l, d, t, c)));
            s["addSlider"] = (Func<string, string, double, double, double, bool, string, Action, object[]>)(
                (k, l, mn, mx, d, i, t, c) => Handle(ctx.AddSlider(k, l, mn, mx, d, i, t, c)));
            s["addChoice"] = (Func<string, string, string[], int, string, Action, object[]>)((k, l, o, d, t, c) => Handle(ctx.AddChoice(k, l, o, d, t, c)));

            s["onGameTick"] = (Action<Action>)(h => { if (h != null) ctx.OnGameTick(_ => h()); });
            s["scheduleEvery"] = (Action<int, Action>)((n, w) => { if (w != null) ctx.ScheduleEvery(n, w); });
            s["addDevCommand"] = (Action<string, string, Action>)((c, l, a) => ctx.AddDevCommand(c, l, a));
            s["addPlayerCommand"] = (Action<string, string, Action>)((c, l, a) => ctx.AddPlayerCommand(c, l, a));

            s["publishCapability"] = (Action<string, string, int, Type, Func<object>, object, Func<object, bool>, string, Func<object, double>, double, double, int>)(
                (owner, name, version, t, provide, fallback, validate, unit, scalar, smin, smax, refresh) =>
                    PublishGeneric.MakeGenericMethod(t).Invoke(null, new object[] { ctx, owner, name, version, provide, fallback, validate, unit, scalar, smin, smax, refresh }));
            s["tryQuery"] = (Func<string, string, int, Type, object[]>)((owner, name, version, t) =>
                (object[])QueryGeneric.MakeGenericMethod(t).Invoke(null, new object[] { ctx, owner, name, version }));

            s["registerSaveData"] = (Action<string, int, Func<string>, Action<string, int, bool>>)((key, v, save, load) =>
                ctx.RegisterSaveData(key, v, save, l => load(l?.Data, l?.Version ?? 0, l != null && l.Existed)));
            s["subscribe"] = (Action<string, Action<object>>)((name, h) => { if (h != null) ctx.Subscribe(name, e => h(e?.Data)); });
            s["emit"] = (Action<string, object>)((name, data) => ctx.Emit(name, data));
            return s;
        }

        /// <summary>The adapter's Start: hand the core its spec through <c>CoreType.StartFromLoader(Dictionary)</c>, by
        /// reflection only — the adapter must never name a core type.</summary>
        public static void StartCore(Assembly core, string coreType, ModContext ctx, string displayName, Action<string> log)
        {
            MethodInfo entry = core.GetType(coreType)?.GetMethod("StartFromLoader", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Dictionary<string, object>) }, null);
            if (entry == null) { log?.Invoke($"{coreType}.StartFromLoader not found; not started."); return; }
            entry.Invoke(null, new object[] { Build(ctx, displayName) });
        }

        private static readonly MethodInfo PublishGeneric = typeof(LoaderSpec).GetMethod(nameof(Publish), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo QueryGeneric = typeof(LoaderSpec).GetMethod(nameof(Query), BindingFlags.NonPublic | BindingFlags.Static);

        private static void Publish<T>(ModContext ctx, string owner, string name, int version, Func<object> provide, object fallback,
                                       Func<object, bool> validate, string unit, Func<object, double> scalar, double smin, double smax, int refresh)
        {
            var id = new CapabilityId(owner, name, version);
            T fb = fallback is T f ? f : default;
            ctx.RegisterCapability(new CapabilityDefinition<T>(
                id, ResolutionMode.Exclusive, defaultProvider: _ => fb,
                validate: validate == null ? null : (Func<T, bool>)(v => validate(v)),
                unit: unit,
                scalarSelector: scalar == null ? null : (Func<T, double>)(v => scalar(v)),
                scalarMin: double.IsNaN(smin) ? (double?)null : smin,
                scalarMax: double.IsNaN(smax) ? (double?)null : smax,
                refreshEveryTicks: refresh));
            ctx.RegisterProvider(new Provider<T>(id, () => provide() is T v ? v : fb));
        }

        private static object[] Query<T>(ModContext ctx, string owner, string name, int version)
        {
            try
            {
                if (ctx.TryQuery(new CapabilityRequest(owner, name, version), out T v, out _)) return new object[] { true, v };
            }
            catch { }
            return new object[] { false, null };
        }

        private sealed class Provider<T> : ICapabilityProvider<T>
        {
            private readonly Func<T> _p;
            public Provider(CapabilityId id, Func<T> p) { Capability = id; _p = p; }
            public CapabilityId Capability { get; }
            public int Priority => 100;
            public T Provide(IWorldStateContext ctx) => _p();
        }
    }
}
