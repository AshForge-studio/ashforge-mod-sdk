# Content, decs and Harmony

Two ways to change the game: **add content** (decs — data), or **change behaviour** (Harmony — code).
Most mods do some of both.

---

## Content: decs

The game's content lives in `dec` XML — items, rocks, factions, work categories, alerts. Drop your own in
`Decs/` and the loader parses them alongside the game's own, **after** your assembly is loaded, so any
custom dec *types* you declare in C# resolve in time.

```
mods/YourMod/
  Decs/
    MyThings.xml
```

A real example, trimmed:

```xml
<Decs>
    <RockDec decName="AshOreVein">
        <Label>ore vein</Label>
        <Description>A seam of raw metal threading through the rock.</Description>
        <Bounds>1,1,1</Bounds>
        <IsNavigationObstacle>true</IsNavigationObstacle>
        <Layer>Static</Layer>
        <ThingType>Rock</ThingType>
        <Renderer class="MeshRendererProps">
            ...
        </Renderer>
    </RockDec>
</Decs>
```

### Rules for decs

**`decName` is permanent.** Anything the game saves that references your dec stores this string. If a
save names a dec that no longer exists, **the save will not load**. Choose names before your 1.0 and
treat them as frozen. Adding is always safe; renaming and deleting are not.

**Prefer self-contained decs.** Inheriting from another mod's dec makes your validation depend on load
order. A dec with no parent validates independently, which is one less thing to go wrong in a combination
you never tested.

**Reuse the game's own systems where you can.** Our ore vein deliberately mirrors the native rock so it's
mined by the game's own mining work — no custom work type, no parallel system to keep in sync, and it
behaves correctly in situations we never thought about.

**A content-only mod needs no DLL at all.** `mod.json` plus `Decs/` is a complete mod.

**Changing content the game already has** — overriding, patching or translating an existing dec —
needs one extra attribute, and redeclaring the dec without it silently collides instead of
overriding. See [Changing existing content](09-changing-existing-content.md).

### Custom dec types in C#

If you declare your own `Dec` subclass, remember that its `PostLoad` **may run several times in one
launch**. Making it non-idempotent is the single most damaging mistake in these docs — it has produced
saves that would not load. Read
[Rules that bite](04-rules-that-bite.md#-postload-may-run-more-than-once--never-append-to-a-static-from-it)
before writing one.

---

## Behaviour: Harmony

[Harmony](https://harmony.pardeike.net/) is referenced for you. Patch in `Init`:

```csharp
using HarmonyLib;

public void Init(ModContext ctx)
{
    new Harmony(ctx.ModId).PatchAll();
}
```

Use your mod id as the Harmony id — it's unique, and it makes conflicts traceable to you.

```csharp
[HarmonyPatch(typeof(SomeGameClass), nameof(SomeGameClass.SomeMethod))]
static class MyPatch
{
    static void Postfix(SomeGameClass __instance) { ... }
}
```

### Patch the thing that actually runs

The most common wasted afternoon: patching a **constructor** on something the game builds **once per
process**. Your patch fires once at startup and nothing you do afterwards has any effect.

We shipped a camera zoom slider patched onto a camera constructor. The camera is created once, so the
slider did nothing. The fix was to patch the **clamp in the property setter** — the code that runs every
time the value changes.

Before choosing a target, ask: *when does this actually execute?*

### Prefer a narrow patch

A `Postfix` that reads state is far safer than a `Prefix` that replaces behaviour, and both are safer
than a transpiler. Transpilers break on any game update that shifts the IL — and this game updates.

### Don't copy game assemblies next to your mod

Your mod loads into the game's own `AssemblyLoadContext`. Shipping your own copy of
`Ascent of Ashes.dll` or `GodotSharp.dll` can put a second copy of those types in the process, and your
patches then apply to the copy nobody is running. The SDK marks every reference copy-local **false** for
this reason; keep it that way.

### Patching other mods

You can, and the broker is usually the better answer — it's designed for optional cooperation and doesn't
break when the other mod updates. If you do patch another mod, use reflection by name so a missing target
degrades instead of throwing at type-load, and expect to re-check it on their every release.

---

## Finding what to patch

There's no published API reference for the game itself. Decompile your local `Ascent of Ashes.dll` with
[ILSpy](https://github.com/icsharpcode/ILSpy) or dnSpy and read it — that's what we do, and it's the only
reliable source. Namespaces begin with `Ascent.`.

Two habits worth having:

- **Check the decompiled source before believing a theory.** Nearly every wrong conclusion in our own
  history came from reasoning about what the game "probably" does instead of reading it.
- **Re-check after a game update.** Method signatures move. A patch that silently stops matching is worse
  than one that throws, because nothing tells you.
