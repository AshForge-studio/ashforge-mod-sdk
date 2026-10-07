# Rules that bite

Every rule on this page exists because it already broke something real — in our own mods, usually after
hours of hunting. Most of them fail **silently**, and two of them can **destroy a player's save**.

If you read nothing else in these docs, read this.

---

## ★★ `PostLoad` may run more than once — never append to a static from it

**What breaks:** saves that will not load. Not "load with a warning" — the map never finishes activating
and the colony never appears.

Definitions can be parsed more than once in one launch — the AshForge loader, for one, re-runs the parse
to work out which mod is at fault when any installed mod fails validation. Every parse calls `PostLoad` on
every dec again. (The SDK's own bootstrap dec is started from a `PostLoad` too, which is why `Start` is
guarded to run once.)

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

## ★★ Never patch a method the AshForge loader patches

**What breaks:** another mod's features — or every mod's save data — silently, and only for players who
also have the AshForge Hub.

The game doesn't ship Harmony, so your mod ships Harmony 2.3.3. The AshForge loader patches the game with
its **own, renamed copy** of Harmony. Two Harmony copies patching the **same game method** don't combine:
the later one replaces the earlier one's patches, and nothing reports it.

We reproduced this with a mod that patched `SaveMenu.StartSaveToFile`. It switched off the loader's save
data for **every** mod in the game. Each side worked perfectly when tested alone.

**Do instead:** don't patch a method the loader, or a mod it loaded, already patches. Prefer the host's
events and save data — they exist so you don't have to patch save, load or world activation yourself.

**The SDK checks this for you.** At start, and again on the first frame, it compares your mod's patches with
the loader's. A collision logs a line beginning:

```
[harmony] ✗ <Type.Method> is patched by this mod AND through the AshForge loader
```

naming the exact method. Methods the loader is known to patch include `SaveMenu.StartSaveToFile`,
`SaveMenu.StartLoadFromFile`, `World.ActivateMap`, `MouseMode.Update`, and `ExpeditionManager.AddDestination`
and `StartExpedition` — but **the guard is the authority, not this list.** It only runs when the loader is
present, so test through the Hub at least once ([Testing and debugging](07-testing-and-debugging.md)).

---

## ★★ Never count frames to measure game time

**What breaks:** anything timed. Silently, and differently on every machine.

`host.OnGameTick` is **a render frame**, not a game tick, and `ScheduleEvery` counts the same frames. That
means:

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

`OnGameTick` is still the right tool for "do this every frame" work — input, polling something cheap. It
is the wrong tool for "how long has this been going on".

---

## ★ Your mod's data can make a save unloadable

**What breaks:** the player's save refuses to open, and uninstalling your mod doesn't fix it.

Two things get baked into a save **by name**:

- **work categories and damage types** you define (they're stored as a dictionary keyed by the dec)
- **any dec a saved object references**

If a save names a dec that no longer exists, the save **will not load**. That means renaming or removing
one of your decs after players have saves is a breaking change for them, not a tidy-up.

If your mod is in this category, set `"saveCritical": true` in `mod.json` so the Hub warns players before
they remove it, and say so in your description — the game's own Mods menu gives no warning
([mod.json → saveCritical](02-mod-json.md#savecritical-and-savewarning--protecting-your-players-saves)).

**Do instead:**

- Decide your dec names before release; treat them as permanent from your 1.0.
- Add new ones freely — **additive is always safe**. Renaming and deleting are not.
- If you keep a tombstone of a retired name so old saves still resolve, **leave it in place**, and put a
  comment on it saying why. Someone will otherwise "clean up the unused dec" and break every old save.

---

## ★ Save your own state through the host, not into the game's save

Use `host.RegisterSaveData(host.ModId, version, save, load)`. Your data rides in a **sidecar next to the
save file**, never inside it, so a player can remove your mod without corrupting their save — and the same
sidecar works on both routes.

Version it from day one and actually handle the migration:

```csharp
host.RegisterSaveData(host.ModId, 2,
    save: () => Serialise(),
    load: l => {
        if (!l.Existed) { ResetToDefaults(); return; }   // fresh colony, no prior data
        if (l.Version == 1) MigrateV1toV2(l.Data);
        else Deserialise(l.Data);
    });
```

`Existed == false` means "no prior data" and is **not** an error — it's a new colony, or a player who
just installed your mod. Reset cleanly instead of throwing; don't keep the previous colony's state.

---

## ★ Don't copy the game's assemblies next to your mod

**What breaks:** your Harmony patches silently do nothing.

Your mod is loaded into the game's own process. If you ship a copy of `Ascent of Ashes.dll` or
`GodotSharp.dll` alongside your mod, the runtime can end up with two copies of those types loaded. Your
patches then apply to the copy nobody is running.

The SDK references every game assembly as copy-local **false** for exactly this reason. `0Harmony.dll` is
the one exception — the game doesn't ship it, so the build puts the SDK's copy in your `Assembly\` folder.
If you add references by hand, do the same:

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

## ★ If you ship meshes, export them with tangents

**What breaks:** every normal map on your own models, silently.

The game's art is processed by Godot's editor on the way into the build, and that importer generates
vertex tangents for any mesh that arrives without them. Your art is read off disk at runtime and never
meets that importer, so it gets exactly what your exporter wrote — and most exporters, Blender's glTF
exporter included, write none unless asked.

A mod's own art is served only through the AshForge Hub. Full detail, with the log line to look for and the
two other art traps: **[Shipping your own art](12-shipping-your-own-art.md)**.

---

## Faults are contained, and reported

`Start` runs inside a guard: if it throws, the SDK logs `start failed: …` with the exception, and the game
carries on without your mod.

Your tick handlers, scheduled work and event handlers are guarded too, so a throw never takes the game or
another mod down — the handler just doesn't finish that call. Each one is reported:

- **On the game's own route**, in your mod's log: `[fault] a game-tick handler threw (time 1): …`, and
  likewise `scheduled work` and `the 'EventName' handler`. The first five of each kind are logged in full,
  then every thousandth, so a handler that throws on every frame shows up without burying everything else.
- **Through the AshForge Hub**, the loader isolates and reports the fault in `%TEMP%shloader.log`,
  against your mod.

A feature missing right at launch usually means `Start` threw: search your mod's log for `start failed`.

---

## Quick checklist before you publish

- [ ] `PostLoad` is idempotent — no appending to statics
- [ ] No Harmony patch collides with the loader — no `[harmony] ✗` line in your log on the Hub route
- [ ] Nothing durational counts frames; game time comes from the world clock
- [ ] Dec names are final, and `saveCritical` is set if they're baked into saves
- [ ] Save data is versioned, keyed by your mod id, and handles `Existed == false`
- [ ] No game assemblies copied next to your DLL
- [ ] Patched the thing that runs repeatedly, not a one-time constructor
- [ ] Any mesh you ship that uses a normal map was exported **with tangents**
- [ ] Tested with the game **paused** and at **every game speed**
- [ ] Tested on **both routes**, and a save carried from one route to the other
