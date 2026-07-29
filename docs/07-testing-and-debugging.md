# Testing and debugging

## Run your own unsigned build

The loader only runs mods signed by AshForge. Yours isn't — so out of the box **your mod will be refused
and you'll see nothing at all**. This surprises everyone once.

To run your own builds:

> **Hub → Manage Mods → ▸ Advanced → turn off _Require AshForge signatures_**

You'll be asked to confirm, and a warning stays visible while it's off. That's deliberate — with it off,
*any* mod in your folder runs, including anything you didn't write. Turn it back on when you're done.

Requires **Hub 1.0.20 or newer**. On older builds the only route was the `ASHLOADER_ALLOW_UNSIGNED=1`
environment variable, which doesn't reach the game when it's launched from Steam.

---

## Where the logs are

| File | What's in it |
|---|---|
| `%TEMP%\ashloader.log` | The loader: what it found, what it rejected and why, which mods initialised, every fault with the mod that caused it |
| `%TEMP%\ashloader.log.1` … `.3` | Previous runs |

**The rotation matters.** A crash post-mortem is about the run that *broke* something, not the one you're
in now. We once read the wrong session's log and concluded the exact opposite of the truth, nearly
burying a save-corruption bug. Check timestamps before you trust a log.

Your own log file is usually easier to read than sharing the loader's:

```csharp
static void Log(string msg)
{
    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "mymod.log"),
                             DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine); }
    catch { }
}
```

---

## Reading the loader log

Lines worth knowing:

```
Discovered mod: id=yourname.coolmod order=100 decs=True
Loaded mod assembly: yourname.coolmod/CoolMod.dll (into game ALC)
initialized entrypoint (context) CoolMod.ModMain
```

That sequence means everything worked. If one is missing, that's your failure point.

```
REJECTED mod 'yourname.coolmod': not signed by AshForge (no manifest/signature) — not loading.
```
→ Signature checking is on. See above.

```
Skipping disabled mod: YourModFolder
```
→ `"enabled": false` in your `mod.json`. The Mod Manager and Dev Launcher both write this field, so it
may not be you who set it.

```
★ SIGNATURE GATE IS OFF — unsigned mods will be loaded.
```
→ Expected while you're developing.

---

## Common failures, and what they actually mean

**Nothing happens; no mention of your mod at all.**
The folder isn't where the loader is looking, or `mod.json` is malformed. Check the `Mods root:` line at
the top of the log.

**Assembly loads but `Init` never runs.**
Your entry class isn't public, is abstract, or has no parameterless constructor. The loader only
constructs types it can `Activator.CreateInstance`.

**`TypeLoadException` / `MissingMethodException` at startup.**
You called a loader API the player's loader doesn't have. This is fatal, not degrading — the mod is gone.
See [Rules that bite](04-rules-that-bite.md#-never-assume-the-players-loader-is-as-new-as-yours).

**Your Harmony patch never fires.**
Two usual causes: you patched a **constructor** on something built once per process, or you copied game
assemblies next to your DLL and are patching a dead second copy. Both are covered in
[Rules that bite](04-rules-that-bite.md).

**It works, then stops working after a while.**
Check for repeated faults in the log. The loader auto-disables a mod that keeps throwing, rather than
letting it degrade someone's colony.

---

## Things to test before you ship

- **Paused.** Anything driven by `OnGameTick` keeps running while paused. Confirm that's what you want.
- **Every game speed.** Frame-counted logic behaves differently at each; world-clock logic doesn't.
- **A fresh colony *and* a loaded save.** `Existed == false` on first run is a different path.
- **Save, quit, reload.** Then check your state actually came back.
- **With your mod removed from a save that used it.** If that breaks the save, set `saveCritical` so the
  Hub warns players before they disable your mod — see
  [mod.json → saveCritical](02-mod-json.md#savecritical-and-savewarning--protecting-your-players-saves).
- **Alongside other mods.** Load-order conflicts only show up in company.

---

## Rebuilding while the game runs

You can't — the game holds your DLL open. Close it, rebuild, relaunch.

One consequence worth knowing: **rebuilding any mod invalidates the loader's validation cache**, which
makes the loader re-parse definitions on the next launch. That's normal, and it's also the condition
under which a badly-behaved `PostLoad` corrupts saves — which is why
[that rule](04-rules-that-bite.md#-postload-may-run-more-than-once--never-append-to-a-static-from-it)
is first on the list.
