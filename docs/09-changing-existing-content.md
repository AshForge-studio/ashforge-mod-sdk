# Changing content the game already has

[Content, decs and Harmony](05-content-and-harmony.md) covers **adding** decs. This page covers the
other half: **replacing or editing content the base game already defines** — renaming things,
translating name lists, rebalancing an existing item.

It also covers the two mistakes that stop a data-only mod dead, both of which look like "the mod
loaded and did nothing".

---

## A data-only mod needs no C#

No compiler, no `dotnet build`, no DLL. A folder, a manifest, and your XML:

```
mods/YourMod/
  mod.json
  Decs/
    YourFile.xml
```

`mod.json` is the whole manifest:

```json
{
  "id": "yourname.yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "author": "Your Name",
  "loadOrder": 100,
  "enabled": true
}
```

That's a complete, working mod. Translations, name lists, tuning tweaks and new items all fit here.

---

## ⚠ Your XML must be inside `Decs/`

The loader reads decs from **`<yourmod>/Decs/`** and nowhere else. An XML file sitting at the root of
your mod folder is never opened.

This fails **silently and confusingly**: the loader finds your folder, reports the mod as discovered,
loads it, and nothing happens. There is no error, because from the loader's point of view a mod with
no `Decs/` folder is simply a mod that ships no content.

To check, look in `%TEMP%\ashloader.log` for your mod:

```
Discovered mod: id=yourname.yourmod order=100 decs=True
```

**`decs=True` is the part that matters.** If it says `decs=False`, your XML is in the wrong place —
move it into `Decs/` and relaunch.

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

## Load order decides who wins

If two mods override the same dec, the one with the **higher `loadOrder`** is parsed later and wins.
Overriding base-game content is inherently a claim on exclusivity, so keep it narrow — override the
one dec you care about rather than replacing a whole file.

---

## Coming from another Godot mod loader?

Some Godot games use a GDScript mod loader with `mods-unpacked/`, a `manifest.json` carrying
`namespace` / `version_number`, and an `overwrites.gd` returning a map of `res://` paths. **None of
that applies here.** AshForge is a C#/.NET loader that feeds your XML into the game's dec parser; it
does not execute GDScript and has no resource-overwrite mechanism.

The translation is straightforward:

| That convention | AshForge |
|---|---|
| `mods-unpacked/<ns>-<name>/` | `mods/<YourMod>/` |
| `manifest.json` | `mod.json` ([fields](02-mod-json.md)) |
| `overwrites.gd` replacing a whole file | a dec with `mode="replace"` |
| whole-file resource swap | per-dec override |

---

## Before you ship

Read the `decName` rule in [Content, decs and Harmony](05-content-and-harmony.md#rules-for-decs) —
**a save that references a dec which no longer exists will not load.** Overriding decs makes that rule
sharper, not softer: if you `replace` a dec, keep its `decName` exactly as the game spells it.
