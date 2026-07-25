# Rules that bite

Every rule on this page exists because it already broke something real — in our own mods, usually after
hours of hunting. Most of them fail **silently**, and two of them can **destroy a player's save**.

If you read nothing else in these docs, read this.

---

## ★★ `PostLoad` may run more than once — never append to a static from it

**What breaks:** saves that will not load. Not "load with a warning" — the map never finishes activating
and the colony never appears.

The loader re-parses the game's definitions whenever *any* installed mod fails validation: it re-runs the
parse repeatedly to work out which mod is at fault. Every parse calls `PostLoad` on every dec again.

So if your dec's `PostLoad` does this:

```csharp
public override void PostLoad(Action<string> reporter)
{
    AllMyThings.Add(this);        // ✗ NEVER
}
```

…then after six parses that static list holds six generations of your dec. Only the last generation is
the one in the database. Anything that picks from the *list* rather than the database hands out a
discarded instance — and when the game saves a reference to a discarded instance it writes it as
`SomeName_DELETED`, which reads back as a silent `null` on load.

This exact bug cost us six of seven colonists in a test save, and it looked haunted because it only
triggered when a mod had been rebuilt since the last launch.

**Do instead:** derive the list on demand from the dec database, or rebuild the static from scratch each
time rather than appending:

```csharp
public override void PostLoad(Action<string> reporter)
{
    AllMyThings.Clear();                                   // idempotent
    AllMyThings.AddRange(Dec.Database<MyDec>.List);
}
```

The rule in one line: **`PostLoad` must be idempotent.** Running it five times must leave the world
identical to running it once.

---

## ★★ Never count frames to measure game time

**What breaks:** anything timed. Silently, and differently on every machine.

`ctx.OnGameTick` is **a render frame**, not a game tick. That means:

- it keeps firing while the game is **paused**
- it does **not** scale with game speed
- it runs as fast as the player's GPU allows — on a fast machine, several times faster than on a slow one

We shipped a prisoner-carry timeout counted in ticks. On an uncapped-framerate machine it expired in
about 17 seconds of real time, mid-carry, and dropped the prisoner. It also kept counting down while the
player had the game paused.

**Do instead:** read the world clock.

```csharp
double now = World.Time.CurrentTime.TotalSeconds;   // game time, respects pause and speed
if (now - _startedAt > 3 * 3600) { /* three game-hours have passed */ }
```

Note the scale: **one agent-second is one hundred game-seconds.**

`OnGameTick` is still the right tool for "do this every frame" work — drawing, input, polling something
cheap. It is the wrong tool for "how long has this been going on".

For periodic work, prefer `ctx.ScheduleEvery(ticks, work)` over counting in `OnGameTick` yourself. The
loader staggers each mod's periodic work onto different frames, so twenty mods don't all wake on the same
one.

---

## ★★ Never assume the player's loader is as new as yours

**What breaks:** your mod doesn't load at all. Not a degraded feature — the whole mod is gone.

This is the one people get wrong most, because the intuition is backwards. If you call a loader API that
the player's loader doesn't have, .NET does not give you a graceful failure:

- a **type** the loader lacks throws `TypeLoadException` **the instant your class is touched**
- a **method** it lacks throws `MissingMethodException`

The loader catches it, attributes the fault to you, and disables your mod. From the player's side your
mod simply doesn't work, and the reason is buried in a log.

The trap is that a *field's type* counts. This class fails to load entirely on an older loader:

```csharp
static class MyStuff
{
    static readonly CapabilityRequest Req = ...;   // ✗ forces the type to resolve at class load
}
```

That is worse than it looks for `struct` types specifically: a `static readonly` **struct** field forces
the struct's layout to be resolved when the declaring class loads, so the class dies even if nothing ever
reads the field. A `class`-typed field is just a pointer and survives. We shipped this regression
ourselves and took down a mod's whole UI path with it.

**Do instead** — three rules, all of them necessary:

1. **Keep anything the hot path reads in a plain data class** that names no loader type at all.
2. **Put loader-API calls behind a probe that itself names no loader type** — reflection by string:

```csharp
static bool SettingsSupported =>
    Type.GetType("AshForge.ModLoader.ModSetting, AshLoader") != null;

// then, only if supported, call into a SEPARATE class that uses the API:
if (SettingsSupported) MySettingsSetup.Register(ctx);
```

3. **Build loader types as locals inside guarded methods**, never as static fields.

**How new is "new enough"?** `lib/VERSION.txt` records the loader this SDK ships. Anything documented
here exists in that version. If you need something newer, you are asking every player to update the Hub
first — assume some won't.

---

## ★ Your mod's data can make a save unloadable

**What breaks:** the player's save refuses to open, and uninstalling your mod doesn't fix it.

Two things get baked into a save **by name**:

- **work categories and damage types** you define (they're stored as a dictionary keyed by the dec)
- **any dec a saved object references**

If a save names a dec that no longer exists, the save **will not load**. That means renaming or removing
one of your decs after players have saves is a breaking change for them, not a tidy-up.

If your mod is in this category, set `"saveCritical": true` in `mod.json` so the loader and Hub can warn
players before they remove it.

**Do instead:**

- Decide your dec names before release; treat them as permanent from your 1.0.
- Add new ones freely — **additive is always safe**. Renaming and deleting are not.
- If you keep a tombstone of a retired name so old saves still resolve, **leave it in place**, and put a
  comment on it saying why. Someone will otherwise "clean up the unused dec" and break every old save.

---

## ★ Save your own state through the loader, not into the game's save

Use `ctx.RegisterSaveData(version, save, load)`. Your blob rides in a **sidecar next to the save file**,
never inside it, which means a player can remove your mod without corrupting their save.

Version it from day one and actually handle the migration:

```csharp
ctx.RegisterSaveData(currentVersion: 2,
    save: () => Serialise(),
    load: l => {
        if (!l.Existed) { ResetToDefaults(); return; }   // fresh colony, no prior data
        if (l.Version == 1) MigrateV1toV2(l.Data);
        else Deserialise(l.Data);
    });
```

`Existed == false` means "no prior data" and is **not** an error — it's a new colony, or a player who
just installed you. Reset cleanly instead of throwing.

---

## ★ Don't copy the game's assemblies next to your mod

**What breaks:** your Harmony patches silently do nothing.

Your mod is loaded into the **game's own** `AssemblyLoadContext`. If you ship a copy of
`Ascent of Ashes.dll` or `GodotSharp.dll` alongside your mod, the runtime can end up with two copies of
those types loaded. Your patches then apply to the copy nobody is running.

The SDK sets every reference to copy-local **false** for exactly this reason. If you add references by
hand, do the same:

```xml
<Reference Include="Whatever">
  <HintPath>...</HintPath>
  <Private>false</Private>     <!-- ← this -->
</Reference>
```

---

## ★ Patch the right thing: constructors run once

A camera, a manager, a controller — many game objects are built **once per process**. Patching a
constructor to change behaviour means your patch fires once, at startup, and a player changing a setting
later has no effect.

We shipped a zoom slider patched onto a camera constructor. The camera is created once
(`if (Target == null)`), so the slider did nothing at all.

**Do instead:** patch the thing that runs every time — the property setter, the clamp, the update method.
Ask "when does this actually execute?" before choosing a patch target.

---

## Faults are isolated, so use that

Every entry point the loader calls — your constructor, your `Init`, your bus handlers, your scheduled
work — runs inside a fault guard. If you throw, the loader catches it, names your mod, and keeps the game
alive. Repeated faults auto-disable your mod rather than letting it degrade someone's colony.

Two consequences:

- **You don't need defensive try/catch around everything.** Let it throw; you'll get a clean attributed
  log entry instead of a swallowed mystery.
- **A silently-missing feature usually means you faulted at init.** Check `%TEMP%\ashloader.log` before
  assuming your logic is wrong.

---

## Quick checklist before you publish

- [ ] `PostLoad` is idempotent — no appending to statics
- [ ] Nothing durational counts frames; game time comes from the world clock
- [ ] No loader type appears in a static field; new APIs are behind a name probe
- [ ] Dec names are final, and `saveCritical` is set if they're baked into saves
- [ ] Save data is versioned and handles `Existed == false`
- [ ] No game assemblies copied next to your DLL
- [ ] Patched the thing that runs repeatedly, not a one-time constructor
- [ ] Tested with the game **paused** and at **every game speed**
