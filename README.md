# AshForge Mod SDK

Everything you need to write a mod for **Ascent of Ashes** that plugs into the AshForge Mod Loader.

You get a C# API for the things mods actually need — settings players can change, state that survives a
save, periodic work, cross-mod communication — plus Harmony for patching the game itself, and the game's
own assemblies to compile against.

---

## Before you start

You need:

- **.NET 6 SDK or newer** — `dotnet --version` should print something. [Download](https://dotnet.microsoft.com/download)
- **Ascent of Ashes**, installed.
- **The AshForge Hub**, with the mod loader enabled. If you can already play with mods, you're set.
  Otherwise: [ashforge.dev/docs/install-mods](https://ashforge.dev/docs/install-mods).

Windows only for now. The loader runs on Linux, but this SDK's game detection is Windows-specific.

---

## Your first mod in about a minute

```
1.  Copy  template/MyFirstMod  somewhere of your own.
2.  cd into it.
3.  dotnet build -c Release
```

That's it. The build finds your game install, compiles against the loader and the game, and drops the
result into your mods folder laid out the way the loader expects:

```
mods/MyFirstMod/
  mod.json
  Assemblies/MyFirstMod.dll
```

Then launch the game and check `%TEMP%\myfirstmod.log`.

**One thing first:** the loader only runs mods signed by AshForge. Yours isn't yet, so it will be
refused. To run your own builds, open the Hub → **Manage Mods** → **Advanced** and turn off *Require
AshForge signatures*. See [docs/07-testing-and-debugging.md](docs/07-testing-and-debugging.md) — read
that page before you spend an hour wondering why nothing loads.

### If the build can't find your game

Set the folder containing `aoa.exe`, either in your `.csproj`:

```xml
<AshForgeGameDir>D:\Games\Ascent of Ashes</AshForgeGameDir>
```

or as an environment variable:

```
set ASHFORGE_GAME_DIR=D:\Games\Ascent of Ashes
```

Detection already handles custom Steam libraries on other drives — it reads Steam's own library list,
not just the default path. If it still misses you, the override always wins.

---

## What's in here

| | |
|---|---|
| `build/AshForge.Mod.props` | Import this in your `.csproj`. Finds the game, wires every reference, deploys your build. |
| `lib/AshLoader.dll` | The loader API you compile against. Reference only — never ship it. |
| `template/MyFirstMod/` | A working mod. Copy it and start editing. |
| `docs/` | The manual. |

---

## Documentation

Read them in this order if you're new:

1. **[Your first mod](docs/01-your-first-mod.md)** — a walkthrough of the template, line by line.
2. **[mod.json](docs/02-mod-json.md)** — the manifest, every field, and which ones matter.
3. **[Lifecycle and the API](docs/03-lifecycle-and-api.md)** — how your code is found and run, and everything `ModContext` gives you.
4. **[★ Rules that bite](docs/04-rules-that-bite.md)** — the non-obvious ones. **Read this one.** Every rule in it exists because it already broke something real, and several of them fail silently or destroy saves.
5. **[Content, decs and Harmony](docs/05-content-and-harmony.md)** — adding and changing game content.
6. **[Talking to other mods](docs/06-capabilities.md)** — the world-state broker.
7. **[Testing and debugging](docs/07-testing-and-debugging.md)** — running unsigned, logs, common failures.
8. **[Publishing](docs/08-publishing.md)** — signing, submission, what we check.
9. **[Changing existing content](docs/09-changing-existing-content.md)** — data-only mods (no C# at all), and how to override a dec the game already defines.

If you only read one page after the walkthrough, make it **Rules that bite**.

Writing a translation, a name list, or a rebalance? You may not need C# at all — start at
**[Changing existing content](docs/09-changing-existing-content.md)**.

---

## Getting help

- Questions and bug reports: [AshForge-studio/feedback](https://github.com/AshForge-studio/feedback/issues)
- Mod catalogue and player docs: [ashforge.dev](https://ashforge.dev)

---

## A note on stability

The loader API is young. We'll avoid breaking it, but it isn't frozen yet — if something has to change
we'll say so in the [changelog](https://ashforge.dev/changelog) and give you a migration note.

The one guarantee worth stating plainly: **a mod compiled against an older loader keeps working on a
newer one.** The reverse is not true, and it fails hard rather than gracefully — see
[Rules that bite](docs/04-rules-that-bite.md#-never-assume-the-players-loader-is-as-new-as-yours).
