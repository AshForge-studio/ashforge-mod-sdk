# Talking to other mods

A **capability** is a named value one mod publishes and others can read — a heat level, a threat score, a
count. It lets mods cooperate without depending on each other.

The design rule it exists to enforce: **your mod must work standalone.** You ask for a value; if the mod
that would publish it isn't installed, you find out and carry on with your own default. No hard
dependency, no load-order dance, no "requires X" in your description.

---

## Publishing a value

```csharp
void PublishCapability<T>(string owner, string name, int version, Func<T> provide, T fallback,
                          Func<T, bool> validate = null, string unit = null, Func<T, double> scalar = null,
                          double scalarMin = double.NaN, double scalarMax = double.NaN, int refreshEveryTicks = 0);
```

```csharp
host.PublishCapability<float>("yourname.coolmod", "heat", 1,
    provide: () => _heat,
    fallback: 0f,
    validate: v => v >= 0f && v <= 1f);
```

- **`owner` + `name` + `version`** identify the value. Readers must use exactly the same three.
- **`provide`** is called when someone reads the value. If it throws, or `validate` rejects the result,
  readers get `fallback` instead.
- **`unit`, `scalar`, `scalarMin`, `scalarMax`, `refreshEveryTicks`** describe the value to the AshForge
  loader's world-state broker. They matter only on the Hub route.

Each value has **one** publisher.

### Name it under your own namespace

Use `owner` in the form `author.mod` — the same shape as your mod id. The `ashforge.*` family is reserved:
on the Hub route the loader refuses a definition there unless the mod is AshForge-signed.

Version from `1` and treat a published shape as permanent. Publishing version `2` alongside `1` is fine;
changing what `1` means breaks every reader silently.

---

## Reading a value

```csharp
bool TryQuery<T>(string owner, string name, int version, out T value);
```

```csharp
float heat = host.TryQuery<float>("othername.othermod", "heat", 1, out float v) ? v : 0f;
```

`TryQuery` returns `false` when nobody publishes that value, so this is safe to call whether or not the
other mod is installed. Query when you need the value, not in `Start` — the other mod may not have started
yet.

---

## ★ The value type must be one both mods have

Use a system type: `float`, `int`, `double`, `bool`, `string` and the like. **Never a type defined in
either mod.** Each mod is its own assembly; a class you declare is a different type from the one another
mod declares with the same name, and the read fails.

If you need several numbers, publish several capabilities.

---

## What happens on each route

- **Through the Hub**, a published value goes to the AshForge loader's world-state broker, with its unit
  and validation.
- **On both routes**, it also goes into the SDK's shared registry inside the game process. So mods
  installed only in the game's `Mods` folder can read each other with no Hub at all.

When your mod was started through the Hub, `TryQuery` asks the loader's broker first, then the shared
registry. When it was started by the game's own mod system, it reads the shared registry.

---

## What isn't available in 2.0

The SDK exposes publishing and reading, and nothing else from the broker. The loader's full broker API —
custom provider classes, aggregate contracts, invalidation and change notifications, and the loader's own
contract types — is **not reachable from an SDK mod in 2.0**. Those contract types live in the loader, and
your mod's code never references the loader.

---

## When to use this instead of just referencing the other mod

Use a capability when you want *optional* enrichment — better behaviour if a mod is present, fine without
it. That covers almost every cross-mod case, and it's why our own mods can ship in any combination.

Referencing another mod's assembly directly creates a hard dependency: if it's missing, your mod fails
when it first touches that type. If you truly need that, say so prominently in your description — a player
who installs you without it just sees a mod that doesn't work.
