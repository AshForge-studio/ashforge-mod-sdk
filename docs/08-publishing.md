# Publishing

> **Status: submitting works today** — package in Depot, send it through the submission form, we review
> and sign. What doesn't exist yet is the *automated* audit; review is done by a person, so expect it to
> take as long as reading code takes.

---

## How distribution works

Mods on [ashforge.dev](https://ashforge.dev) are **signed by AshForge**. The loader carries our public
key and refuses anything that isn't signed with the matching private key, or that has been modified since
signing.

That's the whole point of the system: a player installing from our catalogue is running code we've looked
at, delivered unmodified.

Consequences for you:

- **You can't sign your own mod.** The private key never leaves offline storage. We sign on your behalf
  as part of publishing.
- **The signature covers `Assemblies/` and `Decs/`** — everything the loader executes or injects. Not
  `mod.json`, because the Mod Manager rewrites it when a player enables or reorders mods.
- **Any change means re-signing.** A rebuilt DLL invalidates the signature. There is no "sign once".
- **Revocation exists.** If a published build turns out to be harmful, we can revoke that exact build or
  the key, and players' loaders stop running it on next launch.

Players who'd rather vet mods themselves can turn signature checking off in the Hub. That's their call,
and the warning stays on screen while it's off.

---

## Before you submit

**Correctness**

- [ ] Works from a fresh colony and from a loaded save
- [ ] Save → quit → reload restores your state
- [ ] Behaves at every game speed, and while paused
- [ ] Doesn't break a save when removed — or says so plainly in the description
- [ ] Every box in [Rules that bite](04-rules-that-bite.md#quick-checklist-before-you-publish)

**Hygiene**

- [ ] `id` is namespaced and final
- [ ] `version` is accurate semver — the Hub compares it to show players an update is available
- [ ] `description` is written for a player deciding whether to install, not for a developer
- [ ] No dev-only cheats exposed as **player** commands (`AddDevCommand` is invisible to players; use it)
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

Packaging and publishing go through **Depot**, one of the suite tools in the Hub — whether you built the
mod with this SDK or with the content tools. It has two targets:

- **Local** — writes the package to a folder and stops there. Nothing is sent to us, nothing is reviewed,
  and it isn't signed. For keeping it yourself, sharing it directly, or running it in a third-party
  loader. The official Hub won't load it unsigned, which is the point.
- **Submit for review** — builds the `.mod.zip` and opens the submission form with your mod's details
  filled in. Attach the package, send it, and we review, sign and publish it to the library. A GitHub
  account is all you need.

Full detail on both, and on what review involves:
**[ashforge.dev/submit](https://ashforge.dev/submit)**.

Whichever you use, **Depot can package and submit but can never sign** — the signing key never ships
inside any tool.

---

## Updating

Bump `version` in `mod.json`, send us the new build, we re-sign and republish. The Hub shows players an
update is available for first-party mods and links out for others.

**Don't break saves in an update** unless you say so loudly in the changelog. A player who updates
mid-colony and loses it will not come back. Adding decs is safe; renaming or removing them is not.
