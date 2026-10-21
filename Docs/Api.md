# IsoRim Waylith Api (for mod authors)

IsoRim's public surface is the static class `IsoRim.Api` and the small types next to it (`FigureFrame`, `FigureHook`,
`ModelHandle`, `ProjectileCardHook`, `IFloorLayout`). Everything else in the assembly is internal detail and can change
in any update. Reference `IsoRim.dll` (Publicize is not needed) and keep the reference optional if IsoRim is optional for
your mod.

## Versioning

`Api.Version` goes up whenever members are added. Check it before you use newer members:

```csharp
if (IsoRim.Api.Version >= 8) { /* figure hooks and model drawing are there */ }
```

Members are never removed or changed in meaning within a version. Version 8 adds everything on this page.

## Spaces

- **True space**: x and z in map cells, y the height over the ground in cells.
- **Drawn space**: where IsoRim puts things: drawn y = true height / `Api.HeightSquash` (a very small number, so drawn
  y values are large). The figure frames below map into drawn space.
- Model files are OBJ, one unit = one cell, +y up. IsoRim flips the file's z, as for its own models.

## Part 1: figure attachment points

Register a `FigureHook` once at startup. IsoRim calls it for each person figure it draws (people and creepjoiners shown as
3D figures, alive or dead, also figures drawn by `Api.DrawFigure`; not statues), after the figure is placed, normally once
a frame per figure. With no hook registered nothing extra runs.

| Member | What it does |
|---|---|
| `Api.AddFigureHook(FigureHook hook)` / `Api.RemoveFigureHook(hook)` | Register / remove a hook. |
| `frame.Pawn` | The person (a corpse's inner pawn). |
| `frame.Head`, `frame.Body` | `Matrix4x4` from the head / body frame to drawn space. A frame is the space the figure's own head / body parts are modelled in: cells, feet at the origin, y up, +z the way the figure faces, already posed (walking, lying, sitting, crawling) and scaled to the person's size. |
| `frame.HeadWidth`, `frame.BodyScale` | The head type's widening (x) and the body type's scale. IsoRim applies them to its own parts; they are not in the frames. |
| `frame.HeadCentre`, `frame.HeadSize` | The head's box in the head frame (with `HeadWidth`; the skull's box when dried to bones). |
| `frame.BodyCentre`, `frame.BodySize` | The body's box in the body frame (with `BodyScale`). |
| `frame.HeadSprite` | Maps a point of the game's south-facing head sprite (x right, y up, z into the picture; game cells from the head sprite's middle) onto the head frame: where the game's head art lands on the figure. z 0 is the head's middle plane. |
| `frame.BodySprite` | The same for the game's south-facing adult body sprite of the person's body type (origin at the pawn's draw position): sprite feet on the figure's feet, the head sprite's chin line on the figure's neck, the sprite's width across the body. |
| `frame.Yaw`, `frame.Facing` | The figure's turn (degrees, 0 north, 90 east; smooth) and the `Rot4` from it. |
| `frame.Size` | 1 for adults, smaller for children (already in the frames). |
| `frame.HeadHidden` | No head is drawn: a garment hides the whole head (skip flag Head, e.g. a burka); its covering stands in. |
| `frame.HeadgearShown`, `frame.FullHeadCovered` | Headgear is drawn; it covers the whole head (FullHead group: marine helmets and the like). |
| `frame.HairShown`, `frame.FaceHidden` | The game draws the hair now; the game leaves out the eyes. |
| `frame.SkipFlags` | The game's head render skip flags as the person is now (`(frame.SkipFlags & RenderSkipFlagDefOf.Hair) != 0`). |
| `frame.Skull`, `frame.Headless` | Dried to bones (a skull stands in); the game's own "has no head" state (IsoRim still draws the head). |
| `frame.Lying`, `frame.Crawling`, `frame.Seated`, `frame.Dead`, `frame.Carried`, `frame.Lift`, `frame.BodyShown` | Pose and state; `BodyShown` is false in a bed that shows only the head. |
| `frame.AttachToHead(ModelHandle model, Matrix4x4 local, Color colour, string swatch = null)` / `AttachToBody(...)` | Draws a model with the figure, `local` in the head / body frame. It follows pose and facing and is lit, outlined and shadowed like the figure's own parts (and moves with it on stacked floors and in `DrawFigure`). |
| `frame.AttachMeshToHead(Mesh mesh, Material material, Matrix4x4 local)` / `AttachMeshToBody(...)` | The same for your own mesh and material (no outline). |

The frame is only valid during the call: IsoRim reuses it for the next figure.

```csharp
static ModelHandle horn;
static void Init(ModContentPack content)                      // e.g. from your Mod constructor, after IsoRim loaded
{
    if (IsoRim.Api.Version < 8) return;
    horn = IsoRim.Api.LoadModel(content, "Models/horn.obj");   // faces under a "stuff..." material take the colour
    IsoRim.Api.AddFigureHook(f =>
    {
        if (f.HeadHidden || f.FullHeadCovered || f.Skull) return;   // under a full helmet: hidden
        Vector3 top = f.HeadCentre + new Vector3(0f, f.HeadSize.y * 0.5f, 0f);
        f.AttachToHead(horn, Matrix4x4.TRS(top, Quaternion.identity, Vector3.one * 0.2f), f.Pawn.story.HairColor);
    });
}
```

## Part 2: generic drawing

| Member | What it does |
|---|---|
| `Api.ModelsOn` | The 3D view is on and IsoRim draws models (its option). |
| `Api.LoadModel(ModContentPack mod, string path)` | Loads an OBJ (and its .mtl) from your mod's folder once; `null` if missing. `Api.LoadModel(string path)` finds the mod from the calling assembly. |
| `handle.Min`, `handle.Max` | The model's box in its own space. |
| `Api.DrawModel(ModelHandle m, Vector3 position, float yaw, Vector3 scale, Color colour, string swatch = null)` | Draws a model this frame as IsoRim draws its moving models. `position` true space, `yaw` degrees about the vertical, `scale` per axis. Does nothing while the 3D view is off. |
| `Api.DrawModel(ModelHandle m, Vector3 position, float yaw, Matrix4x4 local, Color colour, string swatch = null)` | The same with a whole placing in the model's own space (cells, true heights) before the turn and move. |
| `Api.StuffSwatch(ThingDef stuff)` / `Api.StuffSwatch(Thing thing)` | "Wood", "Stone", "Metal" or "Fabric": the surface IsoRim gives models of that stuff; pass it as `swatch`. |
| `Api.SpriteColour(Thing thing)` | The mean colour of a thing's own sprite, as IsoRim colours models from sprites. |
| `Api.DrawFigure(Pawn pawn, Vector3 position, float yaw, Matrix4x4 local, bool shadow)` | Draws a pawn's 3D figure (person, animal or mech; pass a corpse's `InnerPawn`) elsewhere, posed as IsoRim poses it now, without held or carried things. Its own placing is taken out; `local` is applied in the figure's own space (point under it at the origin, facing +z), then `yaw` and `position`. `shadow` false leaves out its ground blob. Returns false if it is not drawn as a figure now: draw its sprite then. |
| `Api.FigureBounds(Pawn pawn, out Vector3 min, out Vector3 max)` | The box the figure fills in that own space, measured without drawing. For fitting it into a slot. |

Call the drawing members from draw-time code (a `Draw`/`DrawAt` override, a `MapComponentUpdate`), every frame you want
the thing shown.

```csharp
// A crate model on a shelf top and a stored corpse lying beside it, each frame.
void DrawContents(Thing shelf, Corpse corpse, float shelfTop)
{
    if (IsoRim.Api.Version < 8 || !IsoRim.Api.ModelsOn) { /* draw sprites as usual */ return; }
    Vector3 c = shelf.TrueCenter();
    IsoRim.Api.DrawModel(crate, new Vector3(c.x - 0.3f, shelfTop, c.z), shelf.Rotation.AsAngle, Vector3.one * 0.5f,
        shelf.DrawColor, IsoRim.Api.StuffSwatch(shelf));
    Pawn body = corpse.InnerPawn;
    if (IsoRim.Api.FigureBounds(body, out Vector3 lo, out Vector3 hi))
    {
        float fit = Mathf.Min(1f, 0.9f / (hi.z - lo.z));
        Matrix4x4 local = Matrix4x4.Scale(Vector3.one * fit) * Matrix4x4.Translate(new Vector3(-(lo.x + hi.x) / 2, -lo.y, -(lo.z + hi.z) / 2));
        if (!IsoRim.Api.DrawFigure(body, new Vector3(c.x + 0.3f, shelfTop, c.z), 90f, local, false))
            corpse.DrawNowAt(new Vector3(c.x + 0.3f, 0f, c.z));
    }
}
```

## Notes

- Figure hooks also run for figures another mod draws through IsoRim's internals; attachments then follow that mod's
  placing as well.
- The test mode for this Api is `Source/ProbeApi8.cs` (harness only: dev mode, `-isorim3d` and a probe file).
