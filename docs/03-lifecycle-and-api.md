# Lifecycle and the API

## How your code gets run

There is no registration step. **Implementing the interface is the registration.**

```csharp
using AshForge.NativeKit;

public sealed class ModMain : INativeMod
{
    public void Start(IModHost2 host)
    {
        // everything starts here
    }
}
```

`INativeMod` has one method, `void Start(IModHost2 host)`. Your mod must contain **exactly one**
non-abstract class implementing it, with a parameterless constructor. With none, the log says
`no class implements INativeMod — nothing to start.`; with two or more, it names them and starts neither.

### On either route

The game's own mod system calls nothing in a mod's assembly — it only reads definitions. So the build
generates a small bootstrap dec in `Dec\Bootstrap.xml`; when the game reads it, the SDK starts your mod.
Through the AshForge Hub, the adapter in `Assemblies\` starts the same code instead.

Both end in the same place, `ModEntry.Run`, which:

1. finds your `INativeMod` class,
2. builds the host for the route it came through,
3. calls `Start` — **once per game session**, however many times it is reached,
4. logs `started through the game mod loader.` or `started through the AshForge loader.`,
5. checks your Harmony patches against the AshForge loader's (see
   [Rules that bite](04-rules-that-bite.md#-never-patch-a-method-the-ashforge-loader-patches)).

If both copies of your mod are installed — one in the game's `Mods` folder, one through the Hub — exactly
one runs. The adapter detects an active copy in the game's `Mods` folder and stands down, logging
`standing down: …`; that copy runs.

`Start` runs inside a guard: if it throws, the exception is logged as `start failed: …` and the game
carries on without your mod.

### When `Start` runs — and what not to do in it

The timing differs by route. Through the game's mod system, `Start` runs while the game is reading its
definitions. Through the AshForge loader, it runs before the game parses definitions. **Either way no map
exists yet.**

So: **set things up in `Start`** — settings, ticks, save data, events, commands, patches — and **do the
work later**, from a tick, an event or a command. Don't read definitions or the world in `Start`.

---

## The host

`Start` receives an `IModHost2`. It extends `IModHost`; both are in `AshForge.NativeKit`, and everything
registered through them is attributed to your mod id.

```csharp
host.ModId   // your id from mod.json
host.Route   // "game mod loader" or "AshForge loader" — for log lines
```

Don't branch your mod's behaviour on `Route`. It is there so your log says which route started you.

### Logging

```csharp
Action<string> log = ModEntry.Logger(host.ModId, "Cool Mod");
log("hello");
```

Writes to `%TEMP%\<mod id>.log` and to the game's own log. See
[Testing and debugging](07-testing-and-debugging.md#where-the-logs-are).

### Timing

```csharp
void OnGameTick(Action handler);                // every RENDER FRAME, paused or not
void ScheduleEvery(int intervalTicks, Action work);  // every N frames, staggered per mod
```

> ⚠ **`OnGameTick` is a render frame, not a game tick.** It fires while paused and does not scale with
> game speed. Never use it to measure elapsed time — read the world clock instead:
> [Rules that bite](04-rules-that-bite.md#-never-count-frames-to-measure-game-time).

`ScheduleEvery` counts the same frames, but staggers each mod's work onto a different frame so twenty mods
don't all wake on the same one. Prefer it for periodic work. Keep `OnGameTick` handlers cheap.

### Settings

```csharp
ISetting AddToggle(string key, string label, bool defaultValue, string tooltip = null, Action onChanged = null);
ISetting AddSlider(string key, string label, double min, double max, double defaultValue,
                   bool integral = false, string tooltip = null, Action onChanged = null);
ISetting AddChoice(string key, string label, string[] options, int defaultIndex, string tooltip = null, Action onChanged = null);
```

```csharp
ISetting hard  = host.AddToggle("hardmode", "Hard mode", false, tooltip: "...", onChanged: Recalc);
ISetting rate  = host.AddSlider("rate", "Spawn rate", 0.5, 2.0, 1.0);
ISetting style = host.AddChoice("style", "Style", new[] { "Calm", "Busy" }, 0);

hard.Bool      // toggle
rate.Number    // slider, as double
rate.Int       // slider, rounded
style.Index    // choice, as index — look the string up in your own array
```

**Keep the handle and read the value off it when you need it.** Don't cache the value — the player can
change it mid-game.

Settings are stored in one shared settings file, never in a save, so preferences follow a player across
colonies and removing your mod can't corrupt anything. The same file is used on both routes:
`ashforge.settings.cfg` in the AshForge loader's mods folder when the loader is installed, otherwise in the
game's user data folder. A player's settings follow them between a Hub install and a game-Mods install.

Players change settings under **ESC ▸ Mod Settings**, the settings screen of the **AshForge Tools** mod,
which works on either route.
Without Tools installed, your settings keep their stored or default values.

`key` is stable storage — renaming it resets that setting for every player. `label` is what they read, so
reword it freely. For `AddChoice` the stored value is the **index**, so rewording an option is safe but
**reordering the list silently changes what players have selected**.

### Commands

```csharp
void AddDevCommand(string category, string label, Action action);
void AddPlayerCommand(string category, string label, Action action);
```

```csharp
host.AddDevCommand(null, "Dump state", DumpState);              // null category = your mod id
host.AddDevCommand("Spawning", "Force a raid", ForceRaid);
host.AddPlayerCommand("Trade", "Send caravans home", Rescue);
```

Both appear as buttons in the AshForge Tools console.

**Dev commands are shown only in a dev session** — when the environment variable `ASHFORGE_DEV=1` or
`ASHLOADER_ALLOW_UNSIGNED=1` is set, or a file named `ashforge.dev` exists in `%TEMP%`. Players never see
them, so you can leave them in a shipped mod. Use them for force-triggers, state dumps and test fixtures.

**Player commands are always shown, so they are a high bar.** They exist so a player can dig themselves out
when your mod has left their colony stuck — "the traders who won't leave, send them home". Something they
could plausibly need and cannot hurt themselves with. Cheats, scaffolding and perf harnesses stay dev-only.

### Save data

```csharp
void RegisterSaveData(string key, int currentVersion, Func<string> save, Action<SaveLoad> load);
```

```csharp
host.RegisterSaveData(host.ModId, 2,
    save: () => JsonSerializer.Serialize(_state),
    load: l =>
    {
        if (!l.Existed) { _state = new State(); return; }   // new colony — not an error
        _state = l.Version == 1 ? MigrateV1(l.Data) : JsonSerializer.Deserialize<State>(l.Data);
    });
```

`SaveLoad` gives you `Data` (the stored string, or null), `Version` (what it was written with, 0 if none)
and `Existed`. `load` runs once the loaded world is live.

Your data goes in **one sidecar file next to the save**, `<save>.xml.ashforge` — never inside the save, so a
player can remove your mod without corrupting their save. It is JSON:

```json
{ "format": ..., "world": ..., "mods": { "yourname.coolmod": { "version": 2, "data": "..." } } }
```

The file is identical on both routes, so a colony can move between a game-Mods install and a Hub install
without losing your data.

- **The key is shared by every mod in the save.** Use your mod id, as above. Register several keys of the
  form `yourname.coolmod.something` if you want independently versioned blobs. A null or blank key means
  your mod id.
- **`Existed == false` means reset to defaults** — a new colony, or a player who just installed your mod.
- **A sidecar stamped with a different world is ignored.** If a player deletes a colony and reuses its save
  name, the new colony doesn't inherit the old one's mod data; you get `Existed == false`.
- **A mod that isn't running when a save is written loses its entry in that save.** Don't count on data
  surviving a session in which your mod was disabled.

Version it from the very first release. Migration is cheap to design up front and painful to retrofit.

### Events

```csharp
void Subscribe(string eventName, Action<object> handler);
void Emit(string eventName, object data = null);
```

Event names shared by AshForge mods are in `AshForge.NativeKit.Events`:

| Name | Raised when | Payload members |
|---|---|---|
| `Events.DestinationDiscovered` | a world-map destination is discovered | `Dec`, `Label`, `Description` |
| `Events.ExpeditionStarted` | an expedition sets out | `Dec` |
| `Events.RaidIncoming` | the Raiders mod announces a raid | — |

The payload is the loader's event object on the Hub route and a dictionary otherwise, so read it with
`Events.Field`, which handles both:

```csharp
host.Subscribe(Events.DestinationDiscovered, e =>
{
    string label = Events.Field(e, "Label") as string;
    log("discovered " + label);
});
```

With the AshForge loader present, events go through its bus. Without it, NativeKit raises
`DestinationDiscovered` and `ExpeditionStarted` itself. `RaidIncoming` exists only when the Raiders mod is
installed.

Your own events: name them under your mod id (`yourname.coolmod.something`) and document the payload.

### Talking to other mods

```csharp
host.PublishCapability<float>("yourname.coolmod", "heat", 1, () => _heat, 0f);
if (host.TryQuery<float>("othername.othermod", "threat", 1, out float threat)) { ... }
```

Full detail in [Talking to other mods](06-capabilities.md).

---

## What you don't get

- **No unload/shutdown callback.** Mods live for the process. Don't hold OS resources expecting a tidy
  close.
- **No ordering guarantee between mods.** Don't assume another mod has already started — query for its
  value when you need it, and handle the answer being absent.
- **No sandbox.** Your mod runs with full access to the player's machine. That is the whole reason
  signing exists.
