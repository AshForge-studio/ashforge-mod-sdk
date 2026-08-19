# Loader compatibility

<!-- GENERATED FILE - do not edit. Source: data/loader-compat.json.
     Regenerate with: python tools/gen_loader_compat.py -->

You compile against `lib/AshLoader.dll`, which is built from loader **`b7f057c`** (Hub **1.0.28**).
The oldest loader that still has every API these docs describe is **`5880219`** (Hub **1.0.20**).

Those are two different numbers on purpose. **Compiling successfully does not prove your mod
loads on a player's install** — the reference assembly can carry APIs an older loader lacks and
the compiler cannot warn you. See
[Rules that bite](04-rules-that-bite.md), *Never assume the player's loader is as new as yours*.

## Lanes

- **stable** — latest `1.0.28`. Retired 2026-08-03, superseded by the beta lane.
- **beta** — latest `1.0.29-beta.1`. Live.

## Every shipped release

| Hub | Lane | Date | Loader | Public types | Changes a mod can observe |
|---|---|---|---|---|---|
| **1.0.20** | stable | 2026-07-25 | `5880219` | — | 1 — see below |
| **1.0.21** | stable | 2026-07-26 | `8aaba75` | — | none |
| **1.0.22** | stable | 2026-07-27 | `191522e` | — | 2 — see below |
| **1.0.23** | stable | 2026-07-28 | `191522e` | — | Hub-side fix to in-Hub mod updating |
| **1.0.24** | stable | 2026-07-28 | `59a8b6a` | 66 | 1 — see below |
| **1.0.25** | linux-only | 2026-07-28 | — | — | No Windows release carries this number |
| **1.0.26** | stable | 2026-07-29 | `b7f057c` | 66 | The loader gained a version number of its own (1 |
| **1.0.27** | stable | 2026-07-29 | `b7f057c` | 66 | none |
| **1.0.28** | stable | 2026-07-30 | `b7f057c` | 66 | Last Windows release on the stable lane |
| **1.0.29-beta.1** | beta | 2026-08-03 | `9391c30` | 68 | 7 — see below |

## What changed, release by release

### Hub 1.0.20 (2026-07-25, loader `5880219`)

- Minimum loader for the documented API.

### Hub 1.0.22 (2026-07-27, loader `191522e`)

- Once a mod is signed, its id, display name and capability declaration are read from inside the signed envelope rather than from mod.json.
- A capability declared in mod.json now matches the provider registered in code. Before this, every third-party id failed that comparison and a correct manifest was still reported REGISTERED-NOT-DECLARED.

### Hub 1.0.23 (2026-07-28, loader `191522e`)

Hub-side fix to in-Hub mod updating. Same loader commit as 1.0.22, so no loader change at all.

### Hub 1.0.24 (2026-07-28, loader `59a8b6a`)

Was this SDK's reference assembly until 2026-08-18.

- The reserved ashforge.* capability FAMILY check is stricter: it compares the joined family name rather than the namespace alone, so DEFINING a contract under a namespace like ashforge.something is now refused. Providing and consuming ashforge.* capabilities are unchanged.

### Hub 1.0.25 (2026-07-28, loader n/a)

No Windows release carries this number. The Linux lane shares the version counter but ships independently, so Windows goes 1.0.24 to 1.0.26. Not a withdrawn release.

### Hub 1.0.26 (2026-07-29, loader `b7f057c`)

The loader gained a version number of its own (1.0.26). No public API change from 59a8b6a.

### Hub 1.0.28 (2026-07-30, loader `b7f057c`)

Last Windows release on the stable lane. THIS is the loader shipped in the SDK as lib/AshLoader.dll.

### Hub 1.0.29-beta.1 (2026-08-03, loader `9391c30`)

Migrated to game build 24493575 (beta). This is the live lane.

- Harmony is shipped under our own name and kept out of the Default load context. This is the fix for two 0Harmony copies being loaded at once, which broke patches applied by native-format mods.
- ASHLOADER_NO_HARMONY_SERVE added — an environment switch that stops the loader serving its Harmony copy to anything else.
- Mods built against a different game build are quarantined rather than loaded.
- A mod's Decs are validated against the installed game build before injection.
- AshForge mods are published into the game's own Mods dialog.
- Signature coverage widened to what a mod actually ships, not only Assemblies/ and Decs/.
- Two public types added: Binding, GameBindings. Nothing removed.

## What this adds up to

### The mod-facing API has been additive-only across every shipped loader.

**Evidence.** Public surface of src/AshLoader compared at three commits - 59a8b6a (Hub 1.0.24), b7f057c (Hub 1.0.26-1.0.28, the assembly this SDK ships) and 9391c30 (Hub 1.0.29-beta.1). Types: 66, 66, 68. Public methods: 122, 122, 141. Public fields and properties: 191, 191, 220. NOTHING was removed at either step, and no method signature changed - a changed signature would appear as a removal, and there are none. The beta additions are the Binding and GameBindings types and their members. These are counts of DECLARATIONS IN SOURCE at each commit, compared by sorted signature rather than by total, so a remove-and-add pair cannot hide behind an unchanged count. Counting the compiled assemblies instead gives different absolute totals - a decompiler renders accessors and compiler-generated members differently - but the same result: decompiling the shipped binaries and diffing them shows the beta loader ADDS 2 types, 19 methods and 27 fields/properties over the stable one and REMOVES nothing. The 1.0.24 and 1.0.28 assemblies decompile to identical public surfaces.

**What it means for you.** A mod using only the documented API and built against this SDK still compiles and loads on every shipped loader from 1.0.20 onward, including the beta lane. Caveat worth knowing: this compares declarations, so it proves nothing was removed or renamed. It cannot prove that a method kept doing the same thing - for behaviour changes, read the per-release notes above.

### RESOLVED 2026-08-18 - the SDK's reference assembly is now the newest loader on the lane the public installs.

**Evidence.** It used to be built from 59a8b6a (Hub 1.0.24, 2026-07-28) while stable had already moved to b7f057c (Hub 1.0.28, 2026-07-30). It now ships b7f057c itself - the exact file Hub 1.0.28 deploys. The beta lane's 9391c30 is deliberately NOT the target: the beta Hub is password-gated, so nobody who installs a mod from ashforge.dev is running it.

**What it means for you.** Because the API is additive-only, compiling against this assembly is still safe today - nothing it declares has been removed from any newer loader. What you cannot do is assume the reverse: the newer loaders carry behaviour this assembly knows nothing about. VERSION.txt used to claim you compile against exactly what players run. That claim is true again: this assembly is the file Hub 1.0.28 deploys, byte for byte.

### The beta lane carries mod-facing behaviour changes that no author-facing document describes.

**Evidence.** 24 loader commits between b7f057c and 9391c30, including Harmony isolation, build-mismatch quarantine, dec validation before injection, and widened signature coverage.

**What it means for you.** An author whose mod is quarantined for a game-build mismatch has nothing to read that explains it.

---

Derived from the `shipped/hub-*` release tags and the loader source on 2026-08-18.

— The AshForge Team
