# mod.json

`mod.json` sits next to your `.csproj` and is the one place your mod's identity lives. The build requires
it, reads it, and writes everything else from it — including the game's own manifest, `About.xml`.

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

That's a complete, valid manifest. The build stops with an error unless at least `id`, `name` and
`version` are present.

---

## Fields

| Field | Used by | What it does |
|---|---|---|
| `id` | your code, both routes | **Your identity.** It is `host.ModId`, it keys your settings and your save data, and it names your log file (`%TEMP%\<id>.log`). The AshForge loader identifies your mod by it. |
| `name` | both routes | Display name. Written into `About.xml` as `Name` for the game's Mods menu, and shown by the Hub. |
| `version` | both routes | Written into `About.xml` as `ModVersion`. The Hub compares it against the catalogue to show "Update available", so keep it accurate and use semver. Also names the release zip. |
| `author` | both routes | Written into `About.xml` as `Author`. |
| `description` | Hub | Displayed. Write it for a player deciding whether to install your mod, not for another developer. |
| `loadOrder` | Hub | Lower loads first. Default `100`. |
| `enabled` | Hub | `false` means the AshForge loader skips your mod. The Hub's Mod Manager writes this field. |

**Pick your `id` carefully and never change it.** Renaming it silently resets every player's settings for
your mod and orphans your save data — no error, the data is simply gone.

Use a namespaced form — `yourname.modname` — so you can't collide with someone else.

### The folder name is an identity too

The game's own mod system doesn't read `mod.json`. It identifies a mod by its **folder name**, which the
SDK takes from your project name (or from `<AshForgeModFolder>` in your `.csproj`). Treat it exactly like
the `id`: choose it once and keep it stable across releases.

### About.xml

Generated on every build — don't write one by hand in an SDK project:

```xml
<ModInfo>
  <Name>Cool Mod</Name>
  <Author>Your Name</Author>
  <ModVersion>1.0.0</ModVersion>
  <GameVersion>0.2.0-RC4</GameVersion>
</ModInfo>
```

`GameVersion` comes from `<AshForgeGameVersion>` in your `.csproj`, default `0.2.0-RC4` — the current
public build of the game (25465256). The game flags a mod built for another version as possibly
incompatible.

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

In the Hub, disabling a mod *is* the uninstall path, and it's one click. So when a player unticks a
`saveCritical` mod that was enabled, the Hub stops them with a full modal: *"Disabling this mod will break
your save and may result in permanent data loss. This action cannot be safely reversed."* **Disable all**
and importing a saved load order hit the same gate. Consent is remembered per mod, and withdrawn if they
turn your mod back on.

`saveWarning` is optional and is **your own words for why**, shown in that dialog in place of the generic
line. Name what actually breaks, so a player can make a real decision.

This protection is the Hub's. The game's own Mods menu reads `About.xml`, not `mod.json`, and gives no such
warning — so **say it in your `description` as well**, and on any page where you share the mod.

---

## What the signature covers

When AshForge signs your mod for the catalogue, the signature covers **`Assemblies/`, `Decs/`,
`Assembly/`, `Dec/` and `Parcels/`** — everything either route executes or injects.

`mod.json` is deliberately **not** signed, because the Hub's Mod Manager rewrites it whenever a player
enables, disables or reorders mods. So **never put a security decision in `mod.json`.** It's metadata
anyone can edit.

Signing also copies your id, display name and capability declaration into `ashforge.manifest.json`, which
is signed. The AshForge loader reads them back from there once the signature checks out; where the signed
manifest and `mod.json` disagree, the signed value wins and the swap is named in the loader's log. There is
nothing for you to do — signing handles it.

---

## Folder layout

`dotnet build` writes the finished mod to `bin\<Config>\net8.0\mod\<Folder>\`:

```
<Folder>/
  About.xml                     the game's manifest, generated from mod.json
  Assembly/
    <Project>.Native.dll        your code
    0Harmony.dll                Harmony 2.3.3
  Dec/
    Bootstrap.xml               generated; starts your mod in the game's mod system
    ...                         your Decs\ content
  Assemblies/
    <Folder>.Loader.dll         the AshForge adapter — the only file the AshForge loader loads from here
  Decs/
    ...                         your Decs\ content again, byte-identical
  mod.json
  Parcels/  Assets/             if your project has them
  CHANGELOG.md                  if your project has one
  THIRD-PARTY-NOTICES.txt       the Harmony notice
```

The game reads `About.xml`, `Assembly\` and `Dec\`. The AshForge loader reads `mod.json`, `Assemblies\` and
`Decs\`; the adapter it loads from `Assemblies\` then starts your code from the same `Assembly\` folder.

The folder name and your `id` don't have to match — the SDK defaults the folder to your project name.
