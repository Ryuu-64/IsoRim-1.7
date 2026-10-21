# IsoRim Waylith Api (for mod authors)

IsoRim's public surface is the static class `IsoRim.Api` and the small types next to it (`FigureFrame`, `FigureHook`,
`ModelHandle`, `ProjectileCardHook`, `IFloorLayout`) and the def `IsoRim.ApparelModelDef` (Api 9). Everything else in the assembly is internal detail and can change
in any update. Reference `IsoRim.dll` (Publicize is not needed) and keep the reference optional if IsoRim is optional for
your mod.

## Versioning

`Api.Version` goes up whenever members are added. Check it before you use newer members:

```csharp
if (IsoRim.Api.Version >= 8) { /* figure hooks and model drawing are there */ }
if (IsoRim.Api.Version >= 9) { /* IsoRim.ApparelModelDef is there */ }
```

Members are never removed or changed in meaning within a version. Version 8 adds parts 1 and 2 below, version 9 part 3.

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

## Part 3: apparel models (version 9)

A mod can give any apparel its own 3D model with an `IsoRim.ApparelModelDef` in its Defs. Apparel with a binding is drawn
with it on IsoRim's person figures (and outfit stands); apparel without one keeps IsoRim's own look. A binding whose
apparel is not loaded is skipped quietly. Since the def type only exists from version 9, a mod that must also load on an
older IsoRim adds these defs through a patch operation of its own that checks `IsoRim.Api.Version` first (the VFE Medieval
2 pack's `PatchOperation_IfIsoRimApi` is an example).

| Field | What it does |
|---|---|
| `apparel` | The apparel ThingDef's defName. |
| `slot` | `Head`, `Face`, `Eyes`, `Neck`, `Body`, `Belt` or `Shield` (below). |
| `model` | The OBJ file (and its .mtl), relative to your mod's folder. For a def added by a patch (it has no mod), the first active mod that has the file. |
| `bodyModels` | Body: `<male>`, `<female>`, `<thin>`, `<fat>`, `<hulk>` model paths, each made on that build; `model` is used for builds without one. |
| `hides` | Head: `<li>Hair</li>`, `Beard`, `Face`: also hidden while it is drawn, on top of the game's own rules (renderSkipFlags, FullHead / UpperHead). |
| `visor` | Head: IsoRim's dark visor strip, as on its sealed helmets. |
| `under` | Body on the Shell layer: how a flak vest under it shows: `Hidden` (default), `FrontPlate`, `Whole`. |
| `mount` | Belt: `belt` (default: stretched round the outermost waist), `hip` or `pack`, as `IsoRim.WornGearDef`. |
| `offset`, `rotation`, `scale` | A shift, a turn (Euler degrees) and a size applied to the model in its own space first (Shield: about the grip). |
| `alwaysShown` | Shield: shown whenever the person stands (default: see below). |
| `spriteColour` | Colour the `stuff` faces with the mean of the worn sprite instead of the item's colour. |

Colours are as for other IsoRim models: faces under a material whose name starts with `stuff` take the item's colour (its
stuff, its dye or the colour its mod gives it, as the game tints the sprite); the others keep their own `Kd`. Faces under a
`flame...` material are taken out and burn with IsoRim's live flame (a torch).

Slots and frames (the file's axes as IsoRim's `person_*.obj`: one unit = one cell, +y up, the figure faces -z of the file):

- **Head**: made on `person_head.obj` (same origin); drawn as the hat, widened with the head type. One hat at a time, as
  IsoRim's own (an Overhead one wins). Hair, beard and eyes show under it as the game's own rules say, plus `hides`.
- **Face / Eyes / Neck**: the same frame; drawn with a hat (a mouth or face item, an eye cover, a collar).
- **Body**: made on `person_<build>_upper.obj` / `_lower.obj` of each build, drawn with the body type's scale. On the Shell
  layer it is the coat (closed coats and armour hide what is under them as IsoRim's own); on the Middle layer a shape over
  the shirt, which keeps its colour; on the OnSkin layer it is the shirt (the shirt and leg parts take its colour).
  On a layer the game draws over Shell (by the layer's drawOrder, e.g. VE Framework's `VFEC_OuterShell` tabards and
  mantles) it is drawn over the coat or armour, as in 2D: made on the bare build, it is pushed out over whatever is worn
  under it.
- **Belt**: made in the `person_belt_*` frame (the band round y 0.27..0.34); placed by `mount`.
- **Shield**: the grip at the origin, the face toward -z of the file. Held on the left forearm, a little in front of the
  body's side at hand height, its face to the front and outward, following facing, walking and sitting. By default it shows
  only while the game shows the person's weapon (drafted, aiming, a job or duty that shows weapons) and, for apparel that
  draws its own worn picture (VE Framework's `Apparel_Shield`), while the game draws it (so not with a two-handed weapon);
  that worn picture gives way to the model. Lying and in bed it is not shown.

```xml
<!-- a hat, a coat with a model per build, a belt and a shield -->
<IsoRim.ApparelModelDef>
  <defName>MyMod_R3D_Hood</defName>
  <apparel>MyMod_Apparel_Hood</apparel>
  <slot>Head</slot>
  <model>Models/my_hood.obj</model>
  <hides><li>Hair</li></hides>
</IsoRim.ApparelModelDef>

<IsoRim.ApparelModelDef>
  <defName>MyMod_R3D_Gambeson</defName>
  <apparel>MyMod_Apparel_Gambeson</apparel>
  <slot>Body</slot>
  <model>Models/gambeson_male.obj</model>
  <bodyModels>
    <female>Models/gambeson_female.obj</female>
    <thin>Models/gambeson_thin.obj</thin>
    <fat>Models/gambeson_fat.obj</fat>
    <hulk>Models/gambeson_hulk.obj</hulk>
  </bodyModels>
  <under>FrontPlate</under>
</IsoRim.ApparelModelDef>

<IsoRim.ApparelModelDef>
  <defName>MyMod_R3D_PouchBelt</defName>
  <apparel>MyMod_Apparel_PouchBelt</apparel>
  <slot>Belt</slot>
  <model>Models/pouch_belt.obj</model>
  <mount>belt</mount>
</IsoRim.ApparelModelDef>

<IsoRim.ApparelModelDef>
  <defName>MyMod_R3D_Buckler</defName>
  <apparel>MyMod_Shield_Buckler</apparel>
  <slot>Shield</slot>
  <model>Models/buckler.obj</model>
  <offset>(0, 0.04, 0)</offset>      <!-- optional: a little higher on the arm -->
  <rotation>(0, -10, 0)</rotation>   <!-- optional: turned a little more outward -->
</IsoRim.ApparelModelDef>
```

## Notes

- Figure hooks also run for figures another mod draws through IsoRim's internals; attachments then follow that mod's
  placing as well.
- The test modes for this Api are `Source/ProbeApi8.cs` and `Source/ProbeApi9.cs` / `ProbeApi9Check.cs` (harness only: dev mode, `-isorim3d` and a probe file).
