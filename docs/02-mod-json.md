# mod.json

Every mod folder has one, next to your `Assemblies/` folder. It's how the loader knows what your mod is.

```json
{
  "id": "yourname.coolmod",
  "name": "Cool Mod",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "One or two sentences a player will read in the mod list.",
  "loadOrder": 100,
  "enabled": true
}
```

That's a complete, valid manifest. Everything beyond it is optional.

---

## Fields the loader actually reads

| Field | Type | What it does |
|---|---|---|
| `id` | string | **Your identity.** Used for settings storage, save data, fault attribution, dev-command grouping, and capability ownership. If absent, the loader falls back to the **folder name**. |
| `name` | string | Display name, shown in the Mod Manager and the Mod Settings screen. Falls back to the folder name. |
| `loadOrder` | int | Lower loads first. Default `100`. Also the tiebreaker when two mods provide the same capability at equal priority. |
| `enabled` | bool | `false` means the loader skips your mod entirely. The Hub's Mod Manager and the Dev Launcher both write this field. |
| `capabilities` | object | Declares what you provide and consume. See [Talking to other mods](06-capabilities.md). |

**Pick your `id` carefully and never change it.** It keys the player's settings and your save data. Renaming it silently resets both — the player's configuration is simply gone, with no error.

Use a namespaced form — `yourname.modname` — so you can't collide with someone else.

---

## Fields that are read by people, not code

| Field | Notes |
|---|---|
| `version` | Nothing enforces this. The Hub compares it against the catalogue to show "Update available", so keep it accurate and use semver. |
| `author` | Displayed. |
| `description` | Displayed. Write it for a player deciding whether to install your mod, not for another developer. |

---

## `saveCritical` and `saveWarning` — protecting your players' saves

```json
{
  "saveCritical": true,
  "saveWarning": "Colonists' job priorities reference this mod's work categories. Disabling it leaves them pointing at work types that no longer exist."
}
```

Set `saveCritical` when turning your mod off can damage an existing save — which is true whenever your
mod defines **decs, work categories or damage types** that get written into saves **by name**. See
[Rules that bite](04-rules-that-bite.md#-your-mods-data-can-make-a-save-unloadable).

**This is enforced, and it matters more than it looks.** The Hub has no separate uninstall button —
**disabling a mod *is* the uninstall path, and it's one click.** So when a player unticks a `saveCritical`
mod that was enabled, the Hub stops them with a full modal: *"Disabling this mod will break your save and
may result in permanent data loss. This action cannot be safely reversed."*

It covers the routes that never touch your checkbox too — **Disable all**, and importing a saved load
order both hit the same gate. Consent is remembered per mod so a player isn't nagged twice, and it's
**withdrawn if they turn your mod back on**.

`saveWarning` is optional and is **your own words for why**, shown in that dialog in place of the generic
line. Use it. "This mod stores data inside your saved games" is true but tells a player nothing; naming
what actually breaks lets them make a real decision.

Costs you one line, and it's the difference between a player losing a colony and a player being warned.
Say it in your `description` as well — that's what they read *before* installing.

---

## What the signature covers

When your mod is signed for publication, the signature covers **`Assemblies/` and `Decs/` only**.

`mod.json` is deliberately **not** signed, because the Mod Manager rewrites it whenever a player enables,
disables or reorders your mod — a signature over it would break the moment anyone touched their own mod
list.

The practical consequence for you: **never put a security decision in `mod.json`.** It's metadata a
player (or anything else) can edit. Read your id and version from it for convenience; don't trust it for
anything that matters.

### Your identity is sealed when you sign (loader 1.0.22+)

There was a gap in the arrangement above. The loader read your **id**, your **display name** and your
**capability declaration** out of `mod.json` — the one file the signature doesn't cover — so on a signed
mod those were all still editable *after* signing. The name shown to players in the unverified-mods
warning is one of them, which meant an attacker-editable string was being presented inside a trusted
frame.

Signing now copies those values into `ashforge.manifest.json`, which **is** covered by the signature. The
loader reads them back only once your bytes are proven authentic, intact and un-revoked, and where the
signed manifest and `mod.json` disagree, **the signed value wins** and the swap is named in the log.

What this means in practice:

- **Nothing to do.** Signing handles it; a manifest without these fields behaves exactly as before, so
  older signed mods keep working unchanged.
- **A mismatch is a warning, not a rejection.** The Mod Manager legitimately rewrites `mod.json`, so
  refusing to load on a disagreement would break the game for a player who did nothing wrong.
- **Re-sign to get it.** The sealed identity only appears once a mod is signed again — an existing signed
  mod gains nothing until then.
- Your **capability declaration** is read from the signed manifest too, so it is a baseline an audit can
  actually trust rather than one anybody can edit.

---

## Folder layout

```
mods/
  YourModFolder/
    mod.json
    Assemblies/
      YourMod.dll          ← loaded, and signed
    Decs/
      Whatever.xml         ← parsed into the game's definition database, and signed
```

The folder name and your `id` don't have to match, and in practice often don't — our own mods use folder
`AshWealth` with id `ashforge.wealth`. The SDK deploys to a folder named after your assembly; override it
with `<AshForgeModId>` in your `.csproj` if you want something else.

Multiple DLLs in `Assemblies/` are fine. All of them get loaded and scanned for entry points.
