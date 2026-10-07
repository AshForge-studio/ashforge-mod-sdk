# Changing content the game already has

[Content, decs and Harmony](05-content-and-harmony.md) covers **adding** decs. This page covers the
other half: **replacing or editing content the base game already defines** — renaming things,
translating name lists, rebalancing an existing item.

It also covers the two mistakes that stop a data-only mod dead, both of which look like "the mod
loaded and did nothing".

---

## A data-only mod needs no C#

Translations, name lists, tuning tweaks and new items are all just XML. You have two ways to package
them.

### With the SDK (recommended)

A project with no `.cs` files of your own:

```
YourMod/
  YourMod.csproj      the same .csproj as the template
  mod.json
  Decs/
    YourFile.xml
```

`dotnet build` produces the full mod folder for both routes, exactly as for a code mod. Your mod's log
will say `no class implements INativeMod — nothing to start.` — expected for a content-only mod.

### By hand

A folder in the game's `Mods` folder, carrying both routes' files:

```
<game>\Mods\YourMod\
  About.xml           the game's manifest
  Dec\
    YourFile.xml      read by the game's own mod system
  mod.json            the AshForge manifest
  Decs\
    YourFile.xml      read by the AshForge loader — the same file again
```

`About.xml` needs `Name`, `Author`, `ModVersion` and `GameVersion`:

```xml
<ModInfo>
  <Name>Your Mod</Name>
  <Author>Your Name</Author>
  <ModVersion>1.0.0</ModVersion>
  <GameVersion>0.2.0-RC4</GameVersion>
</ModInfo>
```

`mod.json` needs at least `id`, `name` and `version` ([fields](02-mod-json.md)). If you only care about
the game's own route, `About.xml` and `Dec\` are enough; the Hub needs `mod.json` and `Decs\`.

---

## ⚠ Your XML must be in the right folder

The game's own mod system reads definitions **only from `Dec\`**. The AshForge loader reads them **only
from `Decs\`**. An XML file anywhere else — at the root of your mod folder, or in the other route's
folder — is never opened.

This fails **silently and confusingly**: the mod shows up, enables, and nothing happens. There is no
error, because a mod with no definitions folder is simply a mod that ships no content.

With the SDK you can't get this wrong: put your XML in your project's `Decs\` and the build copies it to
both. By hand, keep the two folders identical.

---

## ⚠ Redeclaring a dec is not the same as overriding it

Here is the trap. Say you want to replace the game's colonist name lists. You find them in the game's
own `Decs/Things/Agents/Names.xml`, copy the structure, and write your own version:

```xml
<Decs>
    <NameListDec decName="NeutralNames">      <!-- ✗ collides -->
        <First>
            <li>...</li>
        </First>
    </NameListDec>
</Decs>
```

This does **not** override the game's `NeutralNames`. Your mod is parsed as its own module layered
over the base game, so what you have written is a *second, conflicting* declaration of a dec that
already exists — a collision, not a replacement.

The fix is one attribute:

```xml
<NameListDec decName="NeutralNames" mode="replace">   <!-- ✓ overrides -->
```

### The modes

`mode` goes on the **dec element** and tells the parser what to do about the existing dec of that name.

| mode | what it does |
|---|---|
| `replace` | Discard the existing dec, use yours. The usual choice for an override. |
| `patch` | Merge your fields into the existing dec, leaving the rest alone. Best for changing one value. |
| `create` | Declare a new dec. The default, and what collides if the dec already exists. |
| `delete` | Remove the existing dec. Errors if it isn't there. |
| `deleteIfExists` | Remove it if present, do nothing if not. |
| `replaceIfExists` / `patchIfExists` | As above, but silent when the dec is absent. |

`replace` falls back to creating the dec if it doesn't exist, so it stays safe across a game update
that renames or removes what you were overriding.

These are dec's own parse modes rather than anything we added. **`replace` is the one we've used and
verified end-to-end**; the rarer ones are documented from dec's behaviour, so if one of them surprises
you, tell us and we'll correct this page.

### Don't confuse this with the `mode` on child elements

You will see `mode` used one level down, inside a dec, in the game's own files:

```xml
<NameListDec decName="MaleNames" parent="NeutralNames">
    <First mode="append">        <!-- append to the list inherited from the parent -->
```

That is **list behaviour within dec inheritance** — how a child dec extends its parent's collection.
It is unrelated to the dec-level `mode` above, which decides the fate of an existing dec. Both can
appear in the same file, meaning different things at different levels.

### Changing one value

Prefer `patch` when you only want to move a number. It survives game updates far better than
`replace`, because you inherit any new fields the update adds instead of pinning a stale full copy:

```xml
<ItemDec decName="SomeExistingItem" mode="patch">
    <MarketValue>45</MarketValue>
</ItemDec>
```

---

## Two mods overriding the same dec

Overriding base-game content is inherently a claim on exclusivity: if two mods override the same dec, one
of them loses. Through the Hub, the mod with the **higher `loadOrder`** is parsed later and wins. Keep
overrides narrow — override the one dec you care about rather than replacing a whole file.

---

## Coming from another Godot mod loader?

Some Godot games use a GDScript mod loader with `mods-unpacked/`, a `manifest.json` carrying
`namespace` / `version_number`, and an `overwrites.gd` returning a map of `res://` paths. **None of
that applies here.** Ascent of Ashes mods are dec XML and C# assemblies; both routes feed your XML into
the game's dec parser, and there is no resource-overwrite mechanism.

The translation is straightforward:

| That convention | Ascent of Ashes |
|---|---|
| `mods-unpacked/<ns>-<name>/` | `<game>\Mods\<YourMod>\` |
| `manifest.json` | `About.xml` for the game, `mod.json` for the Hub ([fields](02-mod-json.md)) — the SDK writes `About.xml` for you |
| `overwrites.gd` replacing a whole file | a dec with `mode="replace"` |
| whole-file resource swap | per-dec override |

---

## Before you ship

Read the `decName` rule in [Content, decs and Harmony](05-content-and-harmony.md#rules-for-decs) —
**a save that references a dec which no longer exists will not load.** Overriding decs makes that rule
sharper, not softer: if you `replace` a dec, keep its `decName` exactly as the game spells it.
