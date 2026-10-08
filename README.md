# AshForge Mod SDK

Everything you need to write a C# mod for **Ascent of Ashes**.

You write one class. A `dotnet build` turns it into a single mod folder that works both ways a player can
install it: dropped into the game's own `Mods` folder and enabled in the in-game Mods menu, or installed
through the AshForge Hub. Your code gets the same API either way — settings players can change, state that
survives a save, periodic work, events and cross-mod values — plus Harmony for patching the game itself.

---

## Before you start

You need:

- **The .NET 8 SDK** — the game runs on .NET 8. `dotnet --version` should print `8.x` or newer.
  [Download](https://dotnet.microsoft.com/download)
- **Ascent of Ashes**, installed.

The AshForge Hub is **not** required to build, test or play your mod. It is the second install route, and it
adds things on top: signature checking, updates, the world-state broker, and serving a mod's own art.

Windows only for now: the SDK's game detection is Windows-specific.

---

## Your first mod in about a minute

```
1.  Copy  template/MyFirstMod  somewhere of your own.
2.  cd into it.
3.  dotnet build -c Release
```

The build finds your game install, compiles your mod, lays out the finished mod folder and copies it into
the game's `Mods` folder:

```
<game>\Mods\MyFirstMod\
  About.xml                        the game's mod manifest, written from mod.json
  Assembly\MyFirstMod.Native.dll   your code
  Harmony\0Harmony.dll             Harmony 2.3.3 (outside Assembly\: see docs/05)
  Dec\Bootstrap.xml                starts your mod in the game's mod system
  Assemblies\MyFirstMod.Loader.dll the AshForge adapter (Hub route only)
  mod.json                         the AshForge manifest
  THIRD-PARTY-NOTICES.txt
```

Then launch the game, open the **Mods** menu, enable **My First Mod** once, and play. Your mod's log is
`%TEMP%\yourname.myfirstmod.log` — the file is named after the `id` in `mod.json`.

Full walkthrough: [Your first mod](docs/01-your-first-mod.md).

### If the build can't find your game

Set the folder containing `aoa.exe`, either in your `.csproj`:

```xml
<AshForgeGameDir>D:\Games\Ascent of Ashes</AshForgeGameDir>
```

or as an environment variable:

```
set ASHFORGE_GAME_DIR=D:\Games\Ascent of Ashes
```

Detection already handles custom Steam libraries on other drives — it reads Steam's own library list, not
just the default path. If it still misses you, the override always wins.

---

## What's in here

| | |
|---|---|
| `build/AshForge.Mod.props` | Import this in your `.csproj`. Finds the game, compiles the kit into your mod, generates the entry point and the adapter, packages and deploys. |
| `build/kit/` | NativeKit — the API your mod codes against. Compiled into your mod as source. |
| `build/adapter/` | Source of the small adapter that lets the AshForge loader start your mod. |
| `lib/AshLoader.dll` | The AshForge loader, used only to compile the adapter. Never shipped in your mod. |
| `lib/native/0Harmony.dll` | Harmony 2.3.3 (MIT). Shipped in your mod's `Harmony\` folder. |
| `template/MyFirstMod/` | A working mod. Copy it and start editing. |
| `docs/` | The manual. |

---

## Documentation

Read them in this order if you're new:

1. **[Your first mod](docs/01-your-first-mod.md)** — the template, line by line, and the two install routes.
2. **[mod.json](docs/02-mod-json.md)** — the manifest, every field, and the folder layout the build produces.
3. **[Lifecycle and the API](docs/03-lifecycle-and-api.md)** — when your code starts, and everything `IModHost2` gives you.
4. **[★ Rules that bite](docs/04-rules-that-bite.md)** — the non-obvious ones. **Read this one.** Every rule in it exists because it already broke something real, and several fail silently or destroy saves.
5. **[Content, decs and Harmony](docs/05-content-and-harmony.md)** — adding content and changing behaviour.
6. **[Talking to other mods](docs/06-capabilities.md)** — publishing and reading shared values.
7. **[Testing and debugging](docs/07-testing-and-debugging.md)** — testing both routes, logs, common failures.
8. **[Publishing](docs/08-publishing.md)** — sharing a zip, and the AshForge catalog.
9. **[Changing existing content](docs/09-changing-existing-content.md)** — data-only mods, and how to override a dec the game already defines.
10. **[Loader compatibility](docs/10-loader-compatibility.md)** — the release history of the AshForge loader.
11. **[Withdrawing a bad build, and retiring a mod](docs/11-withdrawing-and-retiring.md)** — how a broken release is fixed, and what happens at the end of a mod's life.
12. **[Shipping your own art](docs/12-shipping-your-own-art.md)** — meshes and textures. Needs the AshForge Hub.

If you only read one page after the walkthrough, make it **Rules that bite**.

Writing a translation, a name list, or a rebalance? You may not need C# at all — start at
**[Changing existing content](docs/09-changing-existing-content.md)**.

---

## Getting help

- Questions and bug reports: [AshForge-studio/feedback](https://github.com/AshForge-studio/feedback/issues)
- Mod catalogue and player docs: [ashforge.dev](https://ashforge.dev)

---

## A note on stability

Your mod's code never references the AshForge loader. It codes against NativeKit, which is compiled into
your mod, and the adapter the build generates hands the loader's services to your mod by name at runtime.
So a player whose loader is older or newer than the one in `lib/` still runs your mod: if their loader
lacks a service, that one feature degrades (a setting falls back to its default, an event isn't delivered)
instead of the whole mod failing to load. A very old Hub may lack a service — say so if your mod depends
on one.

`IModHost` and `ISetting` are frozen: their members will not change. New services arrive in new
interfaces, as `IModHost2` did. Changes are announced in the [changelog](https://ashforge.dev/changelog)
with a migration note.
