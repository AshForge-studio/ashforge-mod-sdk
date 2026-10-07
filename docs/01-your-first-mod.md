# Your first mod

A walkthrough of `template/MyFirstMod`. Copy that folder somewhere of your own first — don't edit it in
place, so you always have a clean copy to come back to.

**Then rename it before anything else:** the folder and the `.csproj` (say `JaneDoeBetterHauling` and
`JaneDoeBetterHauling.csproj`), and the `id` in `mod.json`. The project name becomes the name the game knows
your mod and its assembly by, and two mods that both kept `MyFirstMod` are two different
`MyFirstMod.Native.dll` files: a player who installs both gets "Assembly with same name is already loaded",
and the game resets their whole mod list. The build warns while the template's names are still there, and
`-t:AshForgePack` refuses to package them.

You build one mod folder, and it works both ways a player can install it: in the game's own `Mods` folder,
enabled once in the in-game **Mods** menu, or through the AshForge Hub. Your code doesn't need to know which
route started it.

---

## The three files

```
MyFirstMod/
  MyFirstMod.csproj    what to build, and against what
  ModMain.cs           your code
  mod.json             who you are
```

### MyFirstMod.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\..\build\AshForge.Mod.props" />

  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>

    <!-- <AshForgeGameDir>D:\Games\Ascent of Ashes</AshForgeGameDir> -->
    <!-- <AshForgeDeploy>false</AshForgeDeploy> -->
  </PropertyGroup>
</Project>
```

The `Import` does all the work: it finds your game, references the game's assemblies and Harmony, compiles
the SDK's kit into your mod, generates the entry point, builds the AshForge adapter, and lays out and
deploys the finished mod. **Fix the relative path** to wherever you put the SDK. Keep the import first.

What the SDK sets for you — leave these alone:

- **Target framework `net8.0`.** The game runs on .NET 8 and your assembly is loaded into its process.
- **Assembly name `<Project>.Native`.** Your code is built as `MyFirstMod.Native.dll`. Set `AssemblyName`
  yourself without the `.Native` suffix and the build stops with an error.
- **Folder name = project name.** The game identifies a mod by its **folder name**, so keep it the same
  from release to release. To decouple it from the project name, set `<AshForgeModFolder>` — and then never
  change that either.

The two commented lines are the knobs you may need: `AshForgeGameDir` if the build can't find your install,
and `AshForgeDeploy` set to `false` to build without copying into the game's `Mods` folder.

### mod.json

```json
{
  "id": "yourname.myfirstmod",
  "name": "My First Mod",
  "version": "0.1.0",
  "author": "Your Name",
  "description": "A starting point. Says hello in the log and adds one setting.",
  "loadOrder": 100,
  "enabled": true
}
```

**Change the `id` to something of your own** before you build anything real. It keys your settings, your
save data and your log file, and changing it later silently wipes the first two for anyone who installed
your mod. The build reads `id`, `name`, `version` and `author` from here and writes the game's `About.xml`
from them, so you never edit `About.xml` by hand.

Full field reference: [mod.json](02-mod-json.md).

### ModMain.cs

```csharp
using System.Globalization;
using AshForge.NativeKit;

public sealed class ModMain : INativeMod
{
    private ISetting _enabled;
    private int _ticks;

    public void Start(IModHost2 host) { ... }
}
```

There is no registration step. The SDK finds the one class in your mod that implements `INativeMod`,
constructs it, and calls `Start` once per game session. **Exactly one** class may implement it — with none,
or with two, nothing is started and the log says why.

---

## Build and run

```
dotnet build -c Release
```

You should see:

```
AshForge SDK: game at C:\...\Ascent of Ashes
AshForge SDK: packaged My First Mod 0.1.0 at ...\bin\Release\net8.0\mod\MyFirstMod
AshForge SDK: deployed to C:\...\Ascent of Ashes\Mods\MyFirstMod — enable 'My First Mod' once in the game's Mods menu.
```

Then:

1. Launch the game.
2. Open the **Mods** menu and enable **My First Mod**. A player does this once.
3. Start or load a colony, and check `%TEMP%\yourname.myfirstmod.log`.

You should find `started through the game mod loader.` If the game was running when you built, the deploy
was skipped with a warning — close the game and build again.

Testing through the AshForge Hub as well is covered in [Testing and debugging](07-testing-and-debugging.md).

---

## What the template demonstrates

**A log.**

```csharp
var log = ModEntry.Logger(host.ModId, "My First Mod");
```

Writes to `%TEMP%\<mod id>.log` and to the game's own log. The game's log is the only one a player without
the Hub has, so it is what they will send you.

**A setting.**

```csharp
_enabled = host.AddToggle("enabled", "Count frames", true,
    tooltip: "An example toggle. Turn it off and the counter stops.");
...
if (_enabled.Bool) _ticks++;
```

Keep the handle; read `.Bool` when you need it. Don't copy the value into a field — the player can change
it while the game is running. Settings appear in the settings screen of the **AshForge Tools** mod when
Tools is installed, on either route.

**Periodic work.**

```csharp
host.ScheduleEvery(600, () => { if (_enabled.Bool) _ticks++; });
```

Every 600 **frames** — roughly ten seconds, and it keeps running while the game is paused. For anything tied
to the game's own time, read the game clock instead
([Rules that bite](04-rules-that-bite.md#-never-count-frames-to-measure-game-time)).

**Save data.**

```csharp
host.RegisterSaveData(host.ModId, 1,
    save: () => _ticks.ToString(CultureInfo.InvariantCulture),
    load: s => _ticks = s.Existed && int.TryParse(s.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0);
```

Kept in a file beside the save, never inside it, so removing your mod can never stop a save from loading.
The key is shared by every mod in the save — use your mod id, as here. `Existed == false` means a fresh
colony: reset to defaults.

**A dev command.**

```csharp
host.AddDevCommand(null, "Say hello", () => log($"hello — {_ticks} ticks counted"));
```

A button in the AshForge Tools console, shown only in a dev session, so you can leave it in a release.
A `null` category groups it under your mod id.

**Harmony, commented out.**

```csharp
//   new HarmonyLib.Harmony(host.ModId).PatchAll(typeof(ModMain).Assembly);
```

Harmony is referenced and shipped for you. Read [Content, decs and Harmony](05-content-and-harmony.md)
before you uncomment it — one rule there decides whether your patch works.

---

## Where to go next

Change something small and rebuild — add a slider, log a game value, react to an event. Then:

- **[Rules that bite](04-rules-that-bite.md)** — before you write anything timed, anything that touches
  saves, or any Harmony patch. This is the page that saves you a bad week.
- [Lifecycle and the API](03-lifecycle-and-api.md) — everything `IModHost2` offers.
- [Content, decs and Harmony](05-content-and-harmony.md) — changing the game itself.
