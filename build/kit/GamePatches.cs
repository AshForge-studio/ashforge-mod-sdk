using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AshForge.NativeKit
{
    /// <summary>What a <see cref="GamePatchAttribute"/> patches on its member: the method itself, a property's getter or
    /// setter, or a constructor.</summary>
    internal enum GamePatchKind { Method, Getter, Setter, Constructor }

    /// <summary>
    /// Marks a patch class — the place a Harmony mod would write <c>[HarmonyPatch(...)]</c>. Same meaning, same
    /// conventions: static <c>Prefix</c> / <c>Postfix</c> / <c>Transpiler</c> / <c>Finalizer</c> methods, an optional
    /// <c>TargetMethod()</c> / <c>TargetMethods()</c> instead of a target in the attribute, an optional <c>Prepare()</c>
    /// returning false to skip. Applied by <see cref="GamePatches.Apply"/>.
    ///
    /// ★★ WHY NOT [HarmonyPatch]. Harmony ships OUTSIDE Assembly/ (Harmony/0Harmony.dll), so the game's mod loader never
    ///   loads it — a second, different 0Harmony.dll in Assembly/ is what deletes a player's mod list. But the game's dec
    ///   system reads every attribute on every type of every mod (GetCustomAttribute, unguarded) BEFORE any mod code runs,
    ///   and a type carrying [HarmonyPatch] makes that read load 0Harmony. With Harmony not loaded yet, the read throws and
    ///   the game breaks for good (reproduced 2026-10-08). This attribute lives in the mod's own assembly, so the read
    ///   always resolves. No type may carry a Harmony attribute or derive from a Harmony type; tools/native_gate.py refuses
    ///   one that does.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    internal sealed class GamePatchAttribute : Attribute
    {
        public Type Type { get; }
        public string Member { get; }
        public Type[] Args { get; }
        public GamePatchKind Kind { get; }

        /// <summary>No target here: the class supplies <c>TargetMethod()</c> or <c>TargetMethods()</c>.</summary>
        public GamePatchAttribute() { }
        public GamePatchAttribute(Type type, string member) : this(type, member, null, GamePatchKind.Method) { }
        public GamePatchAttribute(Type type, string member, Type[] args) : this(type, member, args, GamePatchKind.Method) { }
        public GamePatchAttribute(Type type, string member, GamePatchKind kind) : this(type, member, null, kind) { }
        public GamePatchAttribute(Type type, string member, Type[] args, GamePatchKind kind)
        {
            Type = type; Member = member; Args = args; Kind = kind;
        }
    }

    internal static class GamePatches
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly string[] Kinds = { "Prefix", "Postfix", "Transpiler", "Finalizer" };

        /// <summary>
        /// Apply every <see cref="GamePatchAttribute"/> class in <paramref name="asm"/> — the equivalent of
        /// <c>harmony.PatchAll(asm)</c>, and like it, throws on the first class that cannot be applied (naming it).
        /// Returns the number of patch classes applied.
        /// </summary>
        internal static int Apply(HarmonyLib.Harmony harmony, Assembly asm)
        {
            int applied = 0;
            foreach (Type t in asm.GetTypes())
            {
                GamePatchAttribute[] marks = (GamePatchAttribute[])t.GetCustomAttributes(typeof(GamePatchAttribute), false);
                if (marks.Length == 0) continue;
                try
                {
                    MethodInfo prepare = t.GetMethod("Prepare", Statics, null, Type.EmptyTypes, null);
                    if (prepare != null && prepare.ReturnType == typeof(bool) && !(bool)prepare.Invoke(null, null)) continue;

                    HarmonyLib.HarmonyMethod[] patch = Kinds
                        .Select(k => t.GetMethod(k, Statics))
                        .Select(m => m == null ? null : new HarmonyLib.HarmonyMethod(m))
                        .ToArray();
                    if (patch.All(p => p == null))
                        throw new InvalidOperationException("no Prefix, Postfix, Transpiler or Finalizer method");

                    foreach (MethodBase original in Targets(t, marks))
                        harmony.Patch(original, patch[0], patch[1], patch[2], patch[3]);
                    applied++;
                }
                catch (Exception e) when (!(e is GamePatchException))
                {
                    throw new GamePatchException($"patch class {t.FullName} could not be applied: {e.Message}", e);
                }
            }
            return applied;
        }

        private static IEnumerable<MethodBase> Targets(Type t, GamePatchAttribute[] marks)
        {
            MethodInfo one = t.GetMethod("TargetMethod", Statics, null, Type.EmptyTypes, null);
            if (one != null)
            {
                MethodBase m = one.Invoke(null, null) as MethodBase;
                if (m == null) throw new InvalidOperationException("TargetMethod() returned null");
                return new[] { m };
            }
            MethodInfo many = t.GetMethod("TargetMethods", Statics, null, Type.EmptyTypes, null);
            if (many != null)
            {
                List<MethodBase> ms = ((IEnumerable<MethodBase>)many.Invoke(null, null))?.Where(m => m != null).ToList();
                if (ms == null || ms.Count == 0) throw new InvalidOperationException("TargetMethods() returned nothing");
                return ms;
            }
            return marks.Select(Resolve).ToList();
        }

        private static MethodBase Resolve(GamePatchAttribute a)
        {
            if (a.Type == null) throw new InvalidOperationException("no target: give the attribute a type, or add TargetMethod()");
            MethodBase m;
            switch (a.Kind)
            {
                case GamePatchKind.Getter: m = HarmonyLib.AccessTools.PropertyGetter(a.Type, a.Member); break;
                case GamePatchKind.Setter: m = HarmonyLib.AccessTools.PropertySetter(a.Type, a.Member); break;
                case GamePatchKind.Constructor: m = HarmonyLib.AccessTools.Constructor(a.Type, a.Args); break;
                default: m = HarmonyLib.AccessTools.Method(a.Type, a.Member, a.Args); break;
            }
            if (m == null)
                throw new InvalidOperationException($"{a.Kind} {a.Type.FullName}.{a.Member}"
                    + (a.Args == null ? "" : "(" + string.Join(", ", a.Args.Select(x => x.Name)) + ")") + " not found");
            return m;
        }
    }

    internal sealed class GamePatchException : Exception
    {
        public GamePatchException(string message, Exception inner) : base(message, inner) { }
    }
}
