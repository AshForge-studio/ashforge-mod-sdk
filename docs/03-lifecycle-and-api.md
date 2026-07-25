# Lifecycle and the API

## How your code gets run

There is no registration step. **Implementing the interface is the registration.**

When the loader starts, for each enabled mod it:

1. Verifies the mod's signature (or skips that check if the player turned it off).
2. Loads every `.dll` in `Assemblies/` — into the **game's own** `AssemblyLoadContext`, so your types
   bind to the live game assemblies rather than loading a second copy.
3. Scans every type in those assemblies for one implementing `IAshForgeModContext` or `IAshForgeMod`.
4. Constructs it and calls `Init` — **once**.

All of this happens **before the game parses its definitions**, so any `Dec` types you declare exist in
time to be parsed.

Your constructor and your `Init` both run inside a fault guard. Throwing is safe: the loader catches it,
names your mod in the log, and carries on. It does not take the game down.

```csharp
using AshForge.ModLoader;

public sealed class ModMain : IAshForgeModContext
{
    public void Init(ModContext ctx)
    {
        // everything starts here
    }
}
```

Requirements: **public**, **non-abstract**, and a **parameterless constructor** (the loader uses
`Activator.CreateInstance`).

There's an older `IAshForgeMod` with a bare `Init()` and no context. It still works, but you have no
reason to use it — without the context you'd have to hardcode your own id everywhere. If a type
implements both, the loader prefers the context form.

> A mod that only adds content — decs, XML — needs **no** entry point and no DLL at all. Just `Decs/`.

---

## ModContext

Your handle on everything. Every registration made through it is attributed to your mod id
automatically, which is how faults get blamed correctly and how the loader knows what to disable.

```csharp
ctx.ModId      // your id from mod.json
ctx.LoadOrder  // your load order; the tiebreaker for equal-priority capability providers
```

### Events and timing

```csharp
ctx.Subscribe("some.event", e => { ... });   // string-keyed event bus
ctx.Emit("my.event", payload);               // fire-and-forget

ctx.OnGameTick(t => { ... });                // every RENDER FRAME — see the warning below
ctx.ScheduleEvery(600, () => { ... });       // periodic work, staggered against other mods
```

> ⚠ **`OnGameTick` is a render frame, not a game tick.** It fires while paused and does not scale with
> game speed. Never use it to measure elapsed time — read the world clock instead. This has bitten us
> more than once: [Rules that bite](04-rules-that-bite.md#-never-count-frames-to-measure-game-time).

Prefer `ScheduleEvery` for anything periodic. The loader hash-staggers each mod's work onto different
frames so twenty mods don't all wake up on the same one.

### Settings

Declared once at init, drawn for the player under **ESC ▸ Mod Settings**, stored in their own config file
— never in the save, so preferences follow a player across colonies and uninstalling you can't corrupt
anything.

```csharp
ModSetting hard  = ctx.AddToggle("hardmode", "Hard mode", false, tooltip: "...", onChanged: Recalc);
ModSetting rate  = ctx.AddSlider("rate", "Spawn rate", 0.5, 2.0, 1.0, integral: false);
ModSetting style = ctx.AddChoice("style", "Style", new[]{ "Calm", "Busy" }, 0);
```

**Keep the handle and read the value off it when you need it.** Don't cache the value — the player can
change it mid-game.

```csharp
hard.Bool        // toggle
rate.Number      // slider, as double
rate.Int         // slider, rounded
style.Index      // choice, as index
style.Selected   // choice, as the option string
```

`key` is stable storage — renaming it resets that setting for every player. `label` is what they read, so
reword it freely. For `AddChoice` the stored value is the **index**, so rewording an option is safe but
**reordering the list silently changes what players have selected**.

### Save data

One versioned blob per mod, in a **sidecar next to the save file** — never inside it. A player can remove
your mod without corrupting their save.

```csharp
ctx.RegisterSaveData(
    currentVersion: 2,
    save: () => JsonSerializer.Serialize(_state),
    load: l =>
    {
        if (!l.Existed) { _state = new State(); return; }  // new colony — not an error
        _state = l.Version == 1 ? MigrateV1(l.Data) : JsonSerializer.Deserialize<State>(l.Data);
    });
```

`ModSaveLoad` gives you `Data` (the blob, or null), `Version` (what it was written with, 0 if none), and
`Existed`. Use `ctx.RegisterSaveData(key, ...)` if you want several independently-versioned blobs.

Version it from the very first release. Migration is cheap to design up front and painful to retrofit.

### Commands

```csharp
ctx.AddDevCommand("Dump state", DumpState);                  // dev sessions only
ctx.AddDevCommand("Spawning", "Force a raid", ForceRaid);    // with a category
ctx.AddPlayerCommand("Trade", "Send caravans home", Rescue); // visible to players
```

**Dev commands are invisible to players**, so you can leave them in a shipped mod with no
strip-before-release step. Use them for force-triggers, state dumps and test fixtures.

**Player commands are a high bar.** They exist so a player can dig themselves out when your mod has left
their colony stuck — "the traders who won't leave, send them home". Something they could plausibly need
and cannot hurt themselves with. Cheats, scaffolding and perf harnesses stay dev-only.

### Talking to other mods

```csharp
ColonyNetWorth worth = ctx.Query<ColonyNetWorth>(Capabilities.ColonyNetWorth);
int tier = worth.Tier;
```

Never throws, never requires the other mod to be installed — you get the loader's default if nobody
answers, so your mod still works standalone. Full detail in [Talking to other mods](06-capabilities.md).

---

## What you don't get

- **No unload/shutdown callback.** Mods live for the process. Don't hold OS resources expecting a tidy
  close.
- **No ordering guarantee between mods beyond `loadOrder`.** Don't assume another mod has already
  initialised — ask the broker instead, which is built for exactly this.
- **No sandbox.** Your mod runs with full access to the player's machine. That is the whole reason
  signing exists.
