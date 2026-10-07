# Shipping your own art

> **Read this first: a mod with its own art needs the AshForge Hub.**
>
> The game's own mod system cannot load a mod's own textures, meshes or sounds. Only the AshForge loader can,
> through its asset bridge — **Hub 1.0.30 and later**. A player who installs your mod straight into the
> game's `Mods` folder, without the Hub, gets the rest of your mod but none of your art: references to it
> from your definitions don't resolve. Say so plainly on your mod's page. (Code that reads a file from your
> mod's own folder itself, at runtime, is a different matter and works either way.)

Put your art in an `Assets\` folder in your project; the build packages it into your mod's `Assets\`
folder, and warns you that such references resolve only through the Hub.

Your mod can ship its own meshes and textures. The game reads them at **runtime**, and that is one word
doing a lot of work: the game's own art was processed by the Godot **editor** on the way into the build, and
yours never is. Almost everything on this page follows from that single asymmetry.

The traps here are worse than most, because a mesh that is wrong still *renders*. Nothing throws, the log
looks clean, and the model is simply subtly incorrect in a way you will blame on your material, your
lighting, or yourself.

---

## ★★ Export your meshes with tangents — your exporter almost certainly does not

**What breaks:** normal maps. Every one of them, silently. The surface stays lit and textured, so it reads
as a weak normal map or a bad bake rather than missing vertex data.

A normal map is defined in *tangent space*. That space is built per-vertex from the mesh's `TANGENT`
attribute — and if the attribute is not in the file, there is no space to sample in and the result is
undefined.

Godot's editor generates tangents on import. The importer option `meshes/ensure_tangents` defaults to
**true** and computes them with Mikktspace whenever the source file has none. That is why the game's own
art is fine, and why nobody upstream ever had to think about this.

**Your mesh is not imported.** It is loaded from disk while the game is running, by the same glTF reader,
but with no importer and no such option in front of it. It gets exactly what is in the file.

Most exporters write no tangents unless asked. Blender's glTF exporter is the common case:

```
Export glTF 2.0  ▸  Data  ▸  Mesh  ▸  Tangents        # OFF by default
```

In a Blender script, that is `export_tangents=True` on `bpy.ops.export_scene.gltf` — the parameter
defaults to `False`.

**How to tell you have it wrong.** Godot logs this, once, the first time such a mesh is drawn:

```
WARNING: Attempting to use a shader (res://Textures/CustomOutline.tres) that requires tangents
with a mesh that doesn't contain tangents.
```

That line names the outline shader rather than your material, which makes it easy to dismiss. It is about
your mesh. The game hangs its outline pass off pawn and item materials as a `next_pass`, and that shader
needs tangents, so it complains for **any** tangent-less mesh — including ones with no normal map, where
nothing is actually wrong.

⚠ **The warning and the damage are not the same thing.** Sort your meshes into two piles:

| your mesh | missing tangents costs you |
|---|---|
| no normal map | nothing. One line in the log, and that is all. |
| a normal map | the map is sampled in a space that does not exist. Re-export. |

**The rule in one line: if any material on the mesh uses a normal map, the mesh must ship with tangents.**

Blender needs a UV map to compute them. If your model has no unwrap, enabling the option changes nothing —
you need the unwrap first.

---

## ★ A mesh or icon reference carries no file extension

The game appends the extension itself. A reference to your own art is your mod's id, a slash, and the file
name **without** its suffix:

```xml
<MeshPath>YourModId/crate</MeshPath>       <!-- ✓ loads YourModId/crate.glb  -->
<Icon>YourModId/ui/badge</Icon>            <!-- ✓ loads YourModId/ui/badge.png -->

<MeshPath>YourModId/crate.glb</MeshPath>   <!-- ✗ the game asks for crate.glb.glb -->
```

Subfolders are preserved, so `ui/badge` stays `ui/badge`. Only the extension comes off.

---

## ★ Some renderers load sibling files you did not reference

**What breaks:** the whole definition, at load, with an exception — not a placeholder.

A few of the game's renderers derive extra file names from the one path you gave them, and load those too.
A plant loads growth stages; a storage building loads fill levels:

```
crop.glb            <- the path you wrote
crop_Sprout.glb     <- and these, which the renderer asks for on its own
crop_Half.glb
```

The game dereferences the result **without a null check**, so a missing sibling does not fall back to
anything — it throws while definitions are loading, and takes your definition with it.

**Do:** ship every sibling the renderer for your definition asks for, or point that definition at a
base-game mesh until you have them all. Half a mesh family is worse than none.

---

## Quick checklist before you ship art

- [ ] Every mesh with a normal map was exported **with tangents**
- [ ] Every mesh reference is `ModId/name`, with **no extension**
- [ ] Plants and storage buildings ship their **whole** mesh family, not just the base file
- [ ] You launched once **through the Hub** and read the log — a tangent warning names *your* mesh, not the
      game's shader
- [ ] Your mod's page says the art needs the AshForge Hub
