#!/usr/bin/env python3
"""
check_kit_sync.py — is this SDK's copy of NativeKit and the adapter the one the AshForge mods ship with?

build/kit/ and build/adapter/ are copies. The originals live with the AshForge loader, where the first-party mods
are built from them; a fix made there and not copied here would leave every SDK-built mod without it, and
nothing else would notice. Run before every SDK release:

    python tools/check_kit_sync.py <path to the loader repo>          report
    python tools/check_kit_sync.py <path to the loader repo> --check  exit 1 if anything differs
    python tools/check_kit_sync.py <path to the loader repo> --sync   copy the originals over

The loader repo path can also come from the ASHFORGE_LOADER_REPO environment variable.
Exit 2 means the check could not run, which is NOT a pass.
"""
import hashlib, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SDK = os.path.normpath(os.path.join(HERE, ".."))

# SDK path -> path inside the loader repo
FILES = {
    "build/kit/NativeKit.cs": "examples/NativeKit/NativeKit.cs",
    "build/kit/NativeKit2.cs": "examples/NativeKit/NativeKit2.cs",
    "build/kit/DelegateHost.cs": "examples/NativeKit/DelegateHost.cs",
    "build/kit/ModEntry.cs": "examples/NativeKit/ModEntry.cs",
    "build/adapter/NativeLoaderAdapter.cs": "examples/NativeAdapter/NativeLoaderAdapter.cs",
    "build/adapter/LoaderSpec.cs": "examples/NativeKit/LoaderSpec.cs",
}


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest() if os.path.isfile(path) else None


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    repo = args[0] if args else os.environ.get("ASHFORGE_LOADER_REPO", "")
    if not repo or not os.path.isdir(repo):
        print("loader repo not given or not found (argument or ASHFORGE_LOADER_REPO) — cannot check")
        return 2
    stale = []
    for mine, theirs in FILES.items():
        a, b = os.path.join(SDK, mine), os.path.join(repo, theirs)
        if sha(b) is None:
            print(f"  MISSING  {theirs} in the loader repo — cannot check")
            return 2
        same = sha(a) == sha(b)
        print(f"  {'same ' if same else 'STALE'}    {mine}")
        if not same:
            stale.append((a, b))
    if "--sync" in argv:
        for a, b in stale:
            os.makedirs(os.path.dirname(a), exist_ok=True)
            shutil.copy2(b, a)
        print(f"\ncopied {len(stale)} file(s)")
        return 0
    if stale:
        print(f"\n{len(stale)} FILE(S) DIFFER FROM THE LOADER REPO")
        return 1 if "--check" in argv else 0
    print("\nIN SYNC")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
