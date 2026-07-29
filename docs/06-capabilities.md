# Talking to other mods

The **world-state broker** lets mods answer questions for each other without depending on each other.

The design rule it exists to enforce: **your mod must work standalone.** You ask a question; if the mod
that would answer it isn't installed, you get a sensible default instead of an exception. No hard
dependency, no load-order dance, no "requires X" in your description.

---

## Asking a question

```csharp
using AshForge.ModLoader;

ColonyNetWorth worth = ctx.Query<ColonyNetWorth>(Capabilities.ColonyNetWorth);
if (worth.Tier >= 3) { /* wealthy colony */ }
```

`Query` **never throws** for a capability in the official catalogue. If nothing provides it you get the
loader's registered default, so this is safe to call whether or not the providing mod exists.

### The official catalogue

Defined in `AshForge.ModLoader.Capabilities`:

| Id | Payload | Meaning |
|---|---|---|
| `Capabilities.ColonyNetWorth` | `ColonyNetWorth` (`Total`, `Tier`) | How wealthy the colony is |
| `Capabilities.FactionStanding` | — | Reputation with factions |
| `Capabilities.RegionThreat` | — | Threat level of the region |
| `Capabilities.WorldLocations` | — | Known locations on the world map |
| `Capabilities.PrisonerRoster` | — | Current captives |

Check the XML docs on `BrokerCatalog` in `lib/AshLoader.dll` for each payload's exact shape — your IDE
will show them.

### When you need to know whether anyone really answered

`Query` hides the difference between "a real provider answered" and "you got the default". When that
distinction matters — because you have a genuinely better standalone path — use `TryQuery`:

```csharp
var req = new CapabilityRequest(Capabilities.ColonyNetWorth, minVersion: 1, maxVersion: 1);
if (ctx.TryQuery(req, out ColonyNetWorth worth, out int version))
    UseTheRealNumber(worth);
else
    UseMyOwnEstimate();
```

> ⚠ Build that `CapabilityRequest` as a **local inside a guarded method**, never as a `static readonly`
> field. It's a struct, and a static struct field forces its type to resolve when your class loads — on
> an older loader that kills the whole class. See
> [Rules that bite](04-rules-that-bite.md#-never-assume-the-players-loader-is-as-new-as-yours).

---

## Answering a question

Implement `ICapabilityProvider<T>` and register it:

```csharp
ctx.RegisterProvider(new MyNetWorthProvider());
```

Declare it in `mod.json` too, so the Hub can show players which mods work well together:

```json
"capabilities": {
  "provides": [
    { "id": "ashforge.colony.net_worth@1", "priority": 100, "mode": "exclusive" }
  ],
  "consumes": [
    "ashforge.optics.ui.text_scale@1..1"
  ]
}
```

- **`priority`** — higher wins when several mods provide the same thing.
- **`mode`** — `exclusive` means one winner takes it.
- Ties on priority are broken by **`loadOrder`**, lowest first. Deterministic, never random.

The manifest declaration is advisory metadata for the Hub's "better with" graph. **The registration in
code is what actually takes effect.** Keep them in step; a mismatch isn't an error today but it makes
your mod's page lie.

The loader reconciles the two at startup and logs `REGISTERED-NOT-DECLARED` when you register something
your manifest doesn't mention. On loaders before 1.0.22 that check misfired for **every** third-party
capability — the id string couldn't be matched back to the id your code registered, so a correct manifest
was reported as missing. If you're chasing that warning on an older loader, it isn't your manifest.

Two things worth knowing when you write the id:

- Your own contracts must live under **`author.mod.*`** — the `ashforge.*` **family** is reserved for
  first-party contract *definition* and a mod registering there is refused. Providing or consuming an
  `ashforge.*` capability is always fine; only defining one is reserved.
  ★ As of loader `59a8b6a` (Hub 1.0.24) the check compares the **joined family name**, not the namespace
  alone. A namespace like `ashforge.something` is now refused where it previously slipped through, because
  `ashforge.colony` + `net_worth` and `ashforge` + `colony.net_worth` are the same family. A correctly
  namespaced mod sees no difference.
- The id in the manifest is matched by its **full canonical name**, `namespace.name@version`. Write it
  exactly as your code constructs it.

When your underlying data changes:

```csharp
ctx.InvalidateCapability(Capabilities.ColonyNetWorth);
```

Recomputation is deferred to the scheduler, and `OnChanged` subscribers only fire if the value genuinely
differs — so calling this often is cheap.

---

## Reacting to change

```csharp
IDisposable token = ctx.OnCapabilityChanged(Capabilities.ColonyNetWorth, () => Recalculate());
```

Fires only on a real change, not on every recompute. Dispose the token to unsubscribe.

---

## Defining your own capability

If you want *other* mods to be able to ask *you* something, define the contract:

```csharp
ctx.RegisterCapability(new CapabilityDefinition<MyThing>( ... ));
```

**The `ashforge.*` family is reserved** for first-party contracts and the loader will reject a
third-party definition that uses it — including a deeper namespace such as `ashforge.something`, since
that lands in the same family. Yours must be `author.mod.*` — the same shape as your mod id:

```
yourname.coolmod.some_thing@1
```

Version your contract from `@1` and treat published shapes as permanent. Adding `@2` alongside `@1` is
fine; changing what `@1` means breaks every consumer silently.

---

## When to use this instead of just referencing the other mod

Use the broker when you want *optional* enrichment — better behaviour if a mod is present, fine without
it. That covers almost every cross-mod case, and it's why our own mods can ship in any combination.

Referencing another mod's assembly directly creates a hard dependency: if it's missing, your mod throws
`TypeLoadException` on first touch and **doesn't load at all**. If you truly need that, say so
prominently in your description — a player who installs you without it just sees a mod that doesn't work.
