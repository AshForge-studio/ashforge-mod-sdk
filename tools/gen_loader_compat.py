#!/usr/bin/env python3
"""Generate docs/10-loader-compatibility.md from data/loader-compat.json.

One structured source, one generated page. The point is that a compatibility claim lives in
exactly one place: this project has repeatedly been bitten by the same fact being restated in
several files and then drifting apart.

    python tools/gen_loader_compat.py           # write the doc
    python tools/gen_loader_compat.py --check   # exit 1 if the doc is stale (for CI)
"""

import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(ROOT, "data", "loader-compat.json")
OUT = os.path.join(ROOT, "docs", "10-loader-compatibility.md")


def load():
    with io.open(SRC, encoding="utf-8") as fh:
        return json.load(fh)


def render(d):
    L = []
    w = L.append

    w("# Loader compatibility")
    w("")
    w("<!-- GENERATED FILE - do not edit. Source: data/loader-compat.json.")
    w("     Regenerate with: python tools/gen_loader_compat.py -->")
    w("")
    ref = d["sdk_reference_assembly"]
    minimum = d["minimum_documented_api"]
    w("You compile against `lib/AshLoader.dll`, which is built from loader **`{}`** (Hub **{}**).".format(
        ref["loader"], ref["hub"]))
    w("The oldest loader that still has every API these docs describe is **`{}`** (Hub **{}**).".format(
        minimum["loader"], minimum["hub"]))
    w("")
    w("Those are two different numbers on purpose. **Compiling successfully does not prove your mod")
    w("loads on a player's install** — the reference assembly can carry APIs an older loader lacks and")
    w("the compiler cannot warn you. See")
    w("[Rules that bite](04-rules-that-bite.md), *Never assume the player's loader is as new as yours*.")
    w("")

    lanes = d["lanes"]
    w("## Lanes")
    w("")
    for name in ("stable", "beta"):
        lane = lanes[name]
        w("- **{}** — latest `{}`. {}".format(name, lane["latest"], lane["status"]))
    w("")

    w("## Every shipped release")
    w("")
    w("| Hub | Lane | Date | Loader | Public types | Changes a mod can observe |")
    w("|---|---|---|---|---|---|")
    for r in d["releases"]:
        loader = "`{}`".format(r["loader"]) if r["loader"] else "—"
        types = str(r["api_types"]) if r["api_types"] is not None else "—"
        if r["mod_facing"]:
            changes = "{} — see below".format(len(r["mod_facing"]))
        else:
            changes = r.get("note", "none").split(".")[0] if r.get("note") else "none"
        w("| **{}** | {} | {} | {} | {} | {} |".format(
            r["hub"], r["lane"], r["date"], loader, types, changes))
    w("")

    w("## What changed, release by release")
    w("")
    for r in d["releases"]:
        if not r["mod_facing"] and not r.get("note"):
            continue
        w("### Hub {} ({}, loader {})".format(
            r["hub"], r["date"], "`{}`".format(r["loader"]) if r["loader"] else "n/a"))
        w("")
        if r.get("note"):
            w("{}".format(r["note"]))
            w("")
        for item in r["mod_facing"]:
            w("- {}".format(item))
        if r["mod_facing"]:
            w("")

    w("## What this adds up to")
    w("")
    for f in d["compatibility_findings"]:
        w("### {}".format(f["claim"]))
        w("")
        w("**Evidence.** {}".format(f["evidence"]))
        w("")
        w("**What it means for you.** {}".format(f["consequence"]))
        w("")

    w("---")
    w("")
    w("Derived from the `shipped/hub-*` release tags and the loader source on {}.".format(
        d["derived_on"]))
    w("")
    w("— The AshForge Team")
    return "\n".join(L) + "\n"


def main():
    text = render(load())
    if "--check" in sys.argv:
        if not os.path.exists(OUT):
            print("MISSING: {}".format(OUT))
            return 1
        with io.open(OUT, encoding="utf-8") as fh:
            if fh.read() != text:
                print("STALE: {} does not match data/loader-compat.json".format(OUT))
                return 1
        print("ok: {} is current".format(os.path.basename(OUT)))
        return 0
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print("wrote {} ({} bytes)".format(OUT, len(text.encode("utf-8"))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
