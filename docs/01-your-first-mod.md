# Your first mod

A walkthrough of `template/MyFirstMod`. Copy that folder somewhere of your own first — don't edit it in
place, so you always have a clean copy to come back to.

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
    <AssemblyName>MyFirstMod</AssemblyName>
    <TargetFramework>net6.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The `Import` does all the work: finds your game install, references the loader and the game's assemblies,
and deploys the built mod into your mods folder. **Fix the relative path** to wherever you put the SDK,
or copy the SDK's `build/` folder alongside your project.

`net6.0` is not a preference. The game is .NET 6 and your assembly is loaded into its process — a
different target framework will not load.

### mod.json

```json
{
  "id": "yourname.myfirstmod",
  "name": "My First Mod",
  "version": "0.1.0",
  "loadOrder": 100,
  "enabled": true
}
```

**Change the `id` to something of your own** before you build anything real. It keys your settings and
your save data, and changing it later silently wipes both for anyone who installed your mod.

Full field reference: [mod.json](02-mod-json.md).

### ModMain.cs

```csharp
public sealed class ModMain : IAshForgeModContext
{
    public void Init(ModContext ctx) { ... }
}
```

There is no registration step anywhere. The loader scans your assembly, finds the type implementing the
interface, constructs it, and calls `Init` once. That's the whole contract.

It must be **public**, **non-abstract**, and have a **parameterless constructor**.

---

## Build and run

```
dotnet build -c Release
```

You should see:

```
AshForge SDK: game at C:\...\Ascent of Ashes
AshForge SDK: deployed to C:\Users\you\AshForgeModLoader\mods\MyFirstMod
```

Then:

1. **Turn off signature checking** — Hub → Manage Mods → ▸ Advanced. Your mod isn't signed, so the loader
   will refuse it otherwise and you'll see nothing at all.
   ([Testing and debugging](07-testing-and-debugging.md))
2. Launch the game.
3. Check `%TEMP%\myfirstmod.log`.

You should find `hello from yourname.myfirstmod`.

If not, open `%TEMP%\ashloader.log` and look for your id — the loader records exactly what it did with
your mod and why.

---

## What the template demonstrates

**A setting.** Declared once in `Init`, drawn for the player under ESC ▸ Mod Settings:

```csharp
_greetOnLoad = ctx.AddToggle("greet_on_load", "Say hello in the log", true);
...
if (_greetOnLoad.Bool) Log("greeting enabled");
```

Keep the handle; read `.Bool` when you need it. Don't copy the value into a field — the player can change
it while the game is running.

**Periodic work.**

```csharp
ctx.ScheduleEvery(600, () => Log("still here"));
```

Prefer this over doing work in `OnGameTick`. The loader staggers each mod's periodic work across
different frames so mods don't all wake on the same one.

**A dev command.**

```csharp
ctx.AddDevCommand("Say hello now", () => Log("hello, on demand"));
```

Invisible to players, so you can leave it in a shipped mod — no strip-before-release step.

---

## Where to go next

Change something small and rebuild — add a slider, log a game value, react to an event. Then:

- **[Rules that bite](04-rules-that-bite.md)** — before you write anything timed, anything that touches
  saves, or anything using a loader API. This is the page that saves you a bad week.
- [Lifecycle and the API](03-lifecycle-and-api.md) — everything `ModContext` offers.
- [Content, decs and Harmony](05-content-and-harmony.md) — changing the game itself.
