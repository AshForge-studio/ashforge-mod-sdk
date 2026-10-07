# Withdrawing a bad build, and retiring a mod

What happens when a published mod turns out to be wrong, and what happens when one reaches the end of
its life. Both are written down because the answers are constrained by decisions made elsewhere, and
guessing at them costs someone their colony.

---

## ⚠ Start here: nothing published can be recalled

The loader has **no network path of its own** — no background service, no call home at startup. That
is deliberate, and it is a trust decision rather than an oversight: a modding tool that phones out on
every boot is a modding tool nobody should install.

The consequence is absolute and worth stating plainly:

> **There is no kill switch.** A mod that is already on a player's disk keeps working until that
> player takes an action. Nothing we do to the catalogue, the feed, or the signing key reaches
> backwards onto an installed copy.

Everything below is therefore about **what the next update does**, never about undoing the last one.

---

## Withdrawing a bad build

### We roll forward. We do not roll back.

If `1.2.1` is bad, the fix is **`1.2.2`, carrying the known-good payload of `1.2.0`**. We do not
re-point the catalogue at the old version.

Why this and not the obvious alternative:

- **Versions stay monotonic.** The Hub's update path, the feed and every player's installed-version
  record all assume versions only go up. Publishing a lower number to fix a higher one puts the
  player who already updated into a state nothing else in the system models.
- **It uses the path that already works.** First-party mods auto-update through the Hub. A
  roll-forward reaches players the same way every other fix does, with no special handling.
- **It survives being described.** "The fix is always a new version" is a sentence a player and a
  third-party author can both act on.

The re-shipped payload does not need a fresh review in substance — it was reviewed when it first
shipped — but it is signed again, because a signature covers a build, not a decision.

### For third-party authors

Same rule, and one extra step: the Hub **notifies** for third-party mods rather than updating them
silently, so your users act on a prompt rather than waking up patched. Players who installed your mod
straight into the game's `Mods` folder get no prompt at all — they update only when they download the new
version from wherever you shared it. Say in the changelog that the new version reverts something, and say
what. A notification a player does not understand gets dismissed.

### ⚠ Going backwards can break a save, and we do not promise otherwise

**A rollback is not guaranteed to be save-safe, in either direction, ever.**

Your mod's state lives in a versioned sidecar next to the save. Going forward, you get a migration
hook and can handle an old blob. Going *backwards*, an older build meets a blob written by a newer
one and has no idea what it is. Whether that is harmless, lossy or fatal depends entirely on what you
changed, and only you know.

⇒ **If a version of your mod cannot be safely downgraded, say so in that version's changelog, in
plain words.** Not "internal changes" — "colonies saved with 1.3 will not load on 1.2". That line is
the whole protection a player gets, and it costs you one sentence.

We will not add a blanket guarantee we cannot keep. Adding decs is safe; renaming or removing them is
not, and no policy makes it so.

---

## Retiring a mod

When a mod stops being supported — yours or ours — three things happen, and the third is the one
people get wrong.

1. **It is delisted from the catalogue**, so no new player installs something nobody is maintaining.
2. **Its page stays up and is marked deprecated**, with the reason and, where there is one, the
   replacement. A page that quietly vanishes turns every existing link into a dead end and every
   existing user into someone with no way to find out what happened.
3. **The package keeps serving.** Delisting removes a mod from the catalogue; it does not delete the
   artifact. Every previously published version stays downloadable at its own URL.

That third point is a deliberate commitment, not an implementation detail. Someone is running that
mod right now. Someone else has a save that will not load without it. Pulling the file punishes the
people who trusted us earliest, and it buys nothing — the code is already on their disk.

### What retirement does not mean

- **It does not disable anything.** See the top of this page: installed mods keep running.
- **It does not un-sign anything, and that is narrower than it sounds.** The signature on a retired
  package keeps verifying, and keeps meaning exactly what it meant on release day: *this build passed
  review then*. It is not an endorsement, it is not a safety certificate, and it says nothing about
  whether the mod still works against anything released since. Nothing can revoke it — see the top of
  this page — so it is worth being precise about how little it claims.
- **It does not mean the mod was bad.** Most retirements are authors moving on. The page should say
  which it was.

### If you want your own mod retired

Say so in the submissions repo and we will delist and badge it. If you would rather keep it listed
with a "no longer maintained" note instead, say that — it is your mod, and an unmaintained mod that
still works is often more useful to players than a missing one.

---

## Summary

| | What we do |
|---|---|
| A published build is broken | Ship the next version carrying the good payload. Never re-point at an old one. |
| A player already installed it | Nothing reaches them until they update. There is no kill switch. |
| Going back might eat a save | We promise nothing. The changelog must say so, in plain words. |
| A mod is no longer supported | Delist from the catalogue, badge the page with the reason, keep every package downloadable. |
| A mod is retired | Its signature still verifies and still means only "this build passed review on release day". Installed copies keep working. |

— The AshForge Team
