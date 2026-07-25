# mod.json

Every mod folder has one, next to your `Assemblies/` folder. It's how the loader knows who you are.

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
| `enabled` | bool | `false` means the loader skips you entirely. The Hub's Mod Manager and the Dev Launcher both write this field. |
| `capabilities` | object | Declares what you provide and consume. See [Talking to other mods](06-capabilities.md). |

**Pick your `id` carefully and never change it.** It keys the player's settings and your save data. Renaming it silently resets both — the player's configuration is simply gone, with no error.

Use a namespaced form — `yourname.modname` — so you can't collide with someone else.

---

## Fields that are read by people, not code

| Field | Notes |
|---|---|
| `version` | Nothing enforces this. The Hub compares it against the catalogue to show "Update available", so keep it accurate and use semver. |
| `author` | Displayed. |
| `description` | Displayed. Write it for a player deciding whether to install you, not for another developer. |

---

## `saveCritical` — declared, but not yet enforced

You'll see `"saveCritical": true` in some AshForge manifests. It's intended to mean *"removing this mod
may break existing saves"* — true when a mod defines decs, work categories or damage types that get baked
into saves by name.

**Be aware: nothing currently reads this field.** Not the loader, not the Hub, not the website. Setting
it today documents your intent and nothing more; it does not produce a warning for players.

Set it anyway if it applies to you — it costs nothing, it's honest, and it will start meaning something.
But do not rely on it to protect anyone. If removing your mod can break a save, **say so in your
description**, where a player will actually see it.

See [Rules that bite → your mod's data can make a save unloadable](04-rules-that-bite.md#-your-mods-data-can-make-a-save-unloadable).

---

## What the signature covers

When your mod is signed for publication, the signature covers **`Assemblies/` and `Decs/` only**.

`mod.json` is deliberately **not** signed, because the Mod Manager rewrites it whenever a player enables,
disables or reorders your mod — a signature over it would break the moment anyone touched their own mod
list.

The practical consequence for you: **never put a security decision in `mod.json`.** It's metadata a
player (or anything else) can edit. Read your id and version from it for convenience; don't trust it for
anything that matters.

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
