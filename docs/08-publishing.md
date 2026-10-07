# Publishing

Two ways to get your mod to players: share it yourself, or publish it in the AshForge catalog.

---

## Sharing it yourself

```
dotnet build -c Release -t:AshForgePack
```

This builds as usual and also writes `bin\Release\net8.0\<Folder>-<version>.zip`, containing your mod
folder. Players extract it into the game's `Mods` folder and enable it in the in-game Mods menu. That's the
whole install — no Hub needed.

The same folder also runs through the AshForge Hub, but the Hub's loader runs only AshForge-signed mods
unless the player turns signature checking off. For Hub players to install it normally, publish it in the
catalog.

Before you share a release:

- **Keep the folder name** the same as your last release. The game identifies your mod by it.
- **Bump `version`** in `mod.json`. It names the zip and goes into `About.xml`.
- If your mod defines decs that saves reference, **say so on the download page** — the game's Mods menu
  won't warn a player who removes it.

---

## The AshForge catalog

> **Status: submitting works today** — package in Depot, send it through the submission form, we review
> and sign. What doesn't exist yet is the *automated* audit; review is done by a person, so expect it to
> take as long as reading code takes.

Mods on [ashforge.dev](https://ashforge.dev) are **signed by AshForge** after review. The AshForge loader
carries our public key and refuses anything that isn't signed with the matching private key, or that has
been modified since signing.

That's the point of the system: a Hub player installing from our catalog is running code we've looked at,
delivered unmodified.

Consequences for you:

- **You can't sign your own mod.** The private key never leaves offline storage. We sign on your behalf as
  part of publishing.
- **The signature covers `Assemblies/`, `Decs/`, `Assembly/`, `Dec/` and `Parcels/`** — everything either
  route executes or injects. Not `mod.json`, because the Hub's Mod Manager rewrites it when a player enables
  or reorders mods.
- **Any change means re-signing.** A rebuilt DLL invalidates the signature. There is no "sign once".
- **Revocation exists.** If a published build turns out to be harmful, we can revoke that exact build or the
  key, and players' loaders stop running it on next launch.

Signature checking is the AshForge loader's. The game's own mod system doesn't check signatures, so a signed
mod installed straight into the game's `Mods` folder runs exactly as an unsigned one would.

---

## Before you submit

**Correctness**

- [ ] Works on both routes, and a save carries from one route to the other
- [ ] Works from a fresh colony and from a loaded save
- [ ] Save → quit → reload restores your state
- [ ] Behaves at every game speed, and while paused
- [ ] Doesn't break a save when removed — or says so plainly in the description
- [ ] Every box in [Rules that bite](04-rules-that-bite.md#quick-checklist-before-you-publish)

**Hygiene**

- [ ] `id` is namespaced and final, and the folder name matches your earlier releases
- [ ] `version` is accurate semver — the Hub compares it to show players an update is available
- [ ] `description` is written for a player deciding whether to install, not for a developer
- [ ] No dev-only cheats exposed as **player** commands (`AddDevCommand` is hidden from players; use it)
- [ ] No debug spam in the shipped build

**Things that will get you sent back**

- Reading or writing files outside your own data and log paths
- Network calls
- Starting processes, P/Invoke, unsafe code
- Obfuscated assemblies
- Bundling someone else's assets without the right to redistribute them

None of these are automatically fatal if there's a real reason — but they need explaining, and
"it's easier this way" isn't one.

---

## What we look at

Every build gets reviewed before signing. We're checking what your mod *can reach*, not just what it
claims to do: file and network access, process creation, reflection that hides its target, and whether
what you patch matches what you said you'd patch.

Two things we will never claim, and you shouldn't either:

- **A signature is not a safety certificate.** It says we reviewed this build and it came from you,
  unmodified. It doesn't prove the absence of anything.
- **Trusting an author isn't trusting a build.** Every build gets looked at, including ours.

---

## Submitting

Packaging and publishing go through **Depot**, one of the suite tools in the Hub. It has two targets:

- **Local** — writes the package to a folder and stops there. Nothing is sent to us, nothing is reviewed,
  and it isn't signed.
- **Submit for review** — builds the `.mod.zip` and opens the submission form with your mod's details
  filled in. Attach the package, send it, and we review, sign and publish it to the library. A GitHub
  account is all you need.

**The package must contain the whole mod folder** — `About.xml`, `Assembly/`, `Dec/`, `Assemblies/`,
`Decs/` and `mod.json` — so it runs on both routes once signed. Until Depot packages SDK-built mods, attach
the zip from `dotnet build -t:AshForgePack` to your submission.

Full detail on both targets, and on what review involves: **[ashforge.dev/submit](https://ashforge.dev/submit)**.

Whichever you use, **Depot can package and submit but can never sign** — the signing key never ships inside
any tool.

### Send your source with it

**Source is strongly preferred over a binary.** Add a link to your source repository alongside the package
— or attach the source if it isn't public.

This is not bureaucracy, it's turnaround time. Reviewing a mod means understanding what its code can
reach. With source that's an evening's read. Without it, it's decompiling your assembly and inferring
intent from IL, which takes a week and produces more questions for you to answer. A submission with
source gets looked at first and comes back faster.

If you can't share source, say why in the submission — it isn't an automatic refusal, but expect the
review to take considerably longer and to involve more back-and-forth.

---

## Updating

Bump `version` in `mod.json`, keep the folder name, send us the new build, and we re-sign and republish. The
Hub shows players an update is available for first-party mods and links out for others.

**If you published a code mod through the Hub before SDK 2.0**, its old build has nothing that notices a 2.0
copy in the game's `Mods` folder, so a player with both runs both. Release the 2.0 build through the Hub
first, and tell players to update there before also installing it in the game's `Mods` folder.

If a published build turns out to be broken, or a mod reaches the end of its life, see
[Withdrawing a bad build, and retiring a mod](11-withdrawing-and-retiring.md). The short version: we
always roll **forward** to a new version rather than re-pointing at an old one, and a retired mod
stays downloadable.

**Don't break saves in an update** unless you say so loudly in the changelog. A player who updates
mid-colony and loses it will not come back. Adding decs is safe; renaming or removing them is not.
