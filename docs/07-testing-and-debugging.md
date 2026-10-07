# Testing and debugging

Your mod runs on two routes. Most of your testing happens on the first; test the second before you
publish.

---

## Route 1: the game's Mods folder

`dotnet build` copies your mod into `<game>\Mods\<Folder>\`. Then:

1. Launch the game.
2. Open the **Mods** menu and enable your mod. Once is enough.
3. Play, and read your log.

No signature check applies on this route — the game runs your build as it is.

**Rebuilding while the game runs:** the game holds your files open, so the build skips the deploy with a
warning (`Not deploying: Ascent of Ashes is running …`). The packaged mod is still written to
`bin\<Config>\net8.0\mod\<Folder>\`. Close the game and build again.

The build never replaces a folder in `Mods` that isn't this mod — one without a `mod.json` carrying your
id. If one with your folder name exists, it warns and leaves it alone; rename your project or set
`<AshForgeModFolder>`.

---

## Route 2: the AshForge Hub

1. Copy `bin\<Config>\net8.0\mod\<Folder>\` into the AshForge loader's mods folder. Its path is printed as
   `Mods root:` in `%TEMP%\ashloader.log`.
2. **Turn off signature checking.** The loader runs only AshForge-signed mods, and yours isn't signed, so out
   of the box it is refused and you see nothing at all. Open
   **Hub → Manage Mods → ▸ Advanced → turn off _Require AshForge signatures_**, or set the environment
   variable `ASHLOADER_ALLOW_UNSIGNED=1`. A warning stays visible while checking is off, because then *any*
   mod in your folder runs. Turn it back on when you're done.
3. **Remove or disable the copy in the game's `Mods` folder.** With both enabled, the game-Mods copy runs
   and the Hub copy stands down — you'd be testing route 1 again. Set `<AshForgeDeploy>false</AshForgeDeploy>`
   while you test this route so the build doesn't put it back.
4. Launch through the Hub and check your log for `started through the AshForge loader.`

### Test the crossing

Make a save on one route, then load it on the other, and check your state came back. The save-data sidecar
is the same file on both routes; this proves your mod treats it that way.

---

## Where the logs are

| File | What's in it | Route |
|---|---|---|
| `%TEMP%\<mod id>.log` | Your mod: everything you log through `ModEntry.Logger`, plus the SDK's own lines about your mod | both |
| `%APPDATA%\Godot\app_userdata\Ascent of Ashes\logs\godot.log` | The game's log. Your `ModEntry.Logger` lines appear here too, tagged with your display name | both |
| `%TEMP%\ashloader.log` | The AshForge loader: what it found, what it refused and why, every fault with the mod that caused it | Hub only |

**The game's log is the only log a player without the Hub has**, so it's what a player will send you. It is
written when the game exits — read it after closing the game, not while it runs.

The loader keeps previous runs as `ashloader.log.1` … `.3`. A crash post-mortem is about the run that
*broke* something, not the one you're in now. Check timestamps before you trust a log.

---

## Lines worth knowing

In your mod's log:

```
started through the game mod loader.
started through the AshForge loader.
```
→ `Start` ran. If neither appears, your mod wasn't started — see below.

```
already started through the game mod loader; the copy loaded through the AshForge loader stands down.
standing down: Cool Mod is also installed in the game's Mods folder (…) and is active there; …
```
→ Both copies are installed. Expected; only one runs.

```
no class implements INativeMod — nothing to start.
more than one class implements INativeMod (…) — not started.
```
→ Exactly one class must implement `INativeMod`.

```
start failed: …
```
→ `Start` threw. The exception follows.

```
[harmony] ✗ <Type.Method> is patched by this mod AND through the AshForge loader …
```
→ A patch collision. One side's patches are lost. See
[Rules that bite](04-rules-that-bite.md#-never-patch-a-method-the-ashforge-loader-patches).

---

## Common failures, and what they actually mean

**Nothing in your log at all.**
On route 1: the mod isn't enabled in the Mods menu, or the folder isn't in `<game>\Mods`. On route 2: the
loader refused it (signature checking is on — `%TEMP%\ashloader.log` says so), or the folder isn't under
the `Mods root:` the loader prints.

**Your mod starts, but a feature is missing.**
Something threw in a handler. Search your mod's log for `[fault]` (game's own route) or
`%TEMP%shloader.log` for your mod's id (through the Hub)
([Rules that bite](04-rules-that-bite.md#faults-are-contained-and-reported)).

**A feature works on one route and not the other.**
On the Hub route, look for a `[harmony] ✗` line. Also check whether the player's Hub is very old — a loader
that lacks a service degrades that one feature rather than failing the mod.

**Your Harmony patch never fires.**
Two usual causes: you patched a **constructor** on something built once per process, or you copied game
assemblies next to your DLL and are patching a dead second copy. Both are covered in
[Rules that bite](04-rules-that-bite.md).

**A dev command doesn't show.**
Dev commands appear only in a dev session: `ASHFORGE_DEV=1`, `ASHLOADER_ALLOW_UNSIGNED=1`, or a file named
`ashforge.dev` in `%TEMP%`. They are shown in the AshForge Tools console, so Tools must be installed.

---

## Things to test before you ship

- **Both routes**, as above — and a save made on one loading on the other.
- **Paused.** Anything driven by `OnGameTick` or `ScheduleEvery` keeps running while paused. Confirm that's
  what you want.
- **Every game speed.** Frame-counted logic behaves differently at each; world-clock logic doesn't.
- **A fresh colony *and* a loaded save.** `Existed == false` on first run is a different path.
- **Save, quit, reload.** Then check your state actually came back.
- **With your mod removed from a save that used it.** If that breaks the save, set `saveCritical` so the
  Hub warns players before they disable your mod — see
  [mod.json → saveCritical](02-mod-json.md#savecritical-and-savewarning--protecting-your-players-saves).
- **Alongside other mods.** Conflicts only show up in company.
