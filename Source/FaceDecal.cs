using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Facial Animation's face on a 3D head (option faceDecal). With Facial Animation active, a person drawn
    // as a model gets that mod's animated face (eyes, lids, brows, mouth, blush...) instead of IsoRim's eyes: each
    // face layer's south texture is projected straight from the front onto the front of the head model, layer over
    // layer, lit like the head. Facial Animation is read by reflection only (no reference to its assembly). Its own
    // head cover and skin layers are left out: they carry the sprite's dark head outline, which would wrap round the
    // sides of the 3D head. The game does not run a person's render tree while the model stands in for it, so the
    // face's animation is stepped here, once a tick.
    public static class FaceDecal
    {
        // Where the game's head sprite lands on the 3D head. The head sprite (128 px, Facial Animation's 512 px alike) is
        // 49 px tall, the head model 0.505 cells (0.0103 a pixel); IsoRim's own eyes were traced bigger (0.0131). Between
        // the two, so the eyes keep about their size and the mouth (pixel row ~80) still lands on the face rather than
        // under the chin: 0.0115 cells a pixel, the eyes' middle (pixel row 70.6 of 128) at height 0.945.
        private const float SpritePixelCells = 0.0115f;
        private const float SpriteCells = SpritePixelCells * 128f;             // the whole texture, in cells
        private const float CentreHeight = 0.945f + (70.6f - 64f) * SpritePixelCells;   // the texture's middle
        private const float HeadMiddle = 0.98f;                                // People.HeadHeight

        private static bool tried, available;
        private static Type partType, masterType;
        private static FieldInfo controllerField, layerTypeField, overrideRotField;
        private static MethodInfo updateMethod;

        private class Face
        {
            public PawnRenderNode root;
            public PawnRenderNode master;
            public readonly List<PawnRenderNode> parts = new List<PawnRenderNode>();
            public readonly List<object> controllers = new List<object>();
            public readonly List<object> layers = new List<object>();
            public int tick = -1;
        }

        private static readonly Dictionary<int, Face> faces = new Dictionary<int, Face>();

        private static bool Available
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    Init();
                }
                return available;
            }
        }

        private static void Init()
        {
            try
            {
                if (!ModsConfig.IsActive("Nals.FacialAnimation"))
                {
                    return;
                }
                partType = GenTypes.GetTypeInAnyAssembly("FacialAnimation.NLFacialAnimationPartNode");
                masterType = GenTypes.GetTypeInAnyAssembly("FacialAnimation.NLFacialAnimationMasterNode");
                if (partType == null || masterType == null)
                {
                    return;
                }
                controllerField = partType.GetField("controller");
                layerTypeField = partType.GetField("layerType");
                overrideRotField = partType.GetField("overrideHeadRot");
                updateMethod = masterType.GetMethod("UpdateAnimation", Type.EmptyTypes);
                available = controllerField != null && layerTypeField != null && overrideRotField != null;
                Log.Message("[IsoRim] Facial Animation face on 3D heads: " + (available ? "ready" : "not found"));
            }
            catch (Exception e)
            {
                Log.Warning("[IsoRim] Facial Animation face on 3D heads unavailable: " + e.Message);
                available = false;
            }
        }

        // True if this person's 3D head should carry Facial Animation's face (and leave out IsoRim's eyes).
        public static bool Wanted(Pawn pawn)
        {
            if (!IsoRimMod.Settings.faceDecal || !Available || pawn == null)
            {
                return false;
            }
            Face f = FaceOf(pawn);
            return f != null && f.parts.Count > 0;
        }

        private static Face FaceOf(Pawn pawn)
        {
            try
            {
                PawnRenderTree tree = pawn.Drawer != null && pawn.Drawer.renderer != null ? pawn.Drawer.renderer.renderTree : null;
                if (tree == null)
                {
                    return null;
                }
                if (!tree.Resolved)
                {
                    tree.EnsureInitialized(PawnRenderFlags.None);
                }
                if (tree.rootNode == null)
                {
                    return null;
                }
                Face f;
                if (faces.TryGetValue(pawn.thingIDNumber, out f) && f.root == tree.rootNode)
                {
                    return f;
                }
                f = new Face { root = tree.rootNode };
                List<KeyValuePair<float, int>> order = new List<KeyValuePair<float, int>>();
                Stack<PawnRenderNode> open = new Stack<PawnRenderNode>();
                open.Push(tree.rootNode);
                while (open.Count > 0)
                {
                    PawnRenderNode n = open.Pop();
                    if (n == null)
                    {
                        continue;
                    }
                    if (masterType.IsInstanceOfType(n))
                    {
                        f.master = n;
                    }
                    else if (partType.IsInstanceOfType(n))
                    {
                        object controller = controllerField.GetValue(n);
                        string kind = controller != null ? controller.GetType().Name : "";
                        // The head cover and the skin carry the sprite head's outline: left to IsoRim's head.
                        if (controller != null && kind != "HeadControllerComp" && kind != "SkinControllerComp")
                        {
                            order.Add(new KeyValuePair<float, int>(n.Props != null ? n.Props.baseLayer : 0f, f.parts.Count));
                            f.parts.Add(n);
                            f.controllers.Add(controller);
                            f.layers.Add(layerTypeField.GetValue(n));
                        }
                    }
                    if (n.children != null)
                    {
                        for (int i = 0; i < n.children.Length; i++)
                        {
                            open.Push(n.children[i]);
                        }
                    }
                }
                // Drawn bottom layer first, each a little further out.
                order.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
                Face sorted = new Face { root = f.root, master = f.master };
                foreach (KeyValuePair<float, int> o in order)
                {
                    sorted.parts.Add(f.parts[o.Value]);
                    sorted.controllers.Add(f.controllers[o.Value]);
                    sorted.layers.Add(f.layers[o.Value]);
                }
                faces[pawn.thingIDNumber] = sorted;
                return sorted;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Reading Facial Animation's face failed: " + e, 93611071);
                return null;
            }
        }

        // Draws the face on the head placed like People's head meshes (position, yaw, and headLocal: the figure's pose
        // and size times the head's widening). head is IsoRim's person_head model.
        public static void Draw(Pawn pawn, Model head, Vector3 position, float yaw, Matrix4x4 headLocal)
        {
            Face f = FaceOf(pawn);
            if (f == null || f.parts.Count == 0 || head == null)
            {
                return;
            }
            try
            {
                int tick = Find.TickManager.TicksGame;
                if (f.tick != tick && f.master != null && updateMethod != null)
                {
                    f.tick = tick;
                    updateMethod.Invoke(f.master, null);
                }
                IsoRimSettings st = IsoRimMod.Settings;
                float size = Mathf.Clamp(st.faceDecalSize, 0.5f, 1.5f);
                Mesh m = MeshFor(faceMesh, head, size, st.faceDecalHeight, 1f);
                Mesh mouth = MeshFor(mouthMesh, head, size, st.faceDecalHeight + st.faceDecalMouthHeight, Mathf.Clamp(st.faceDecalMouth, 0.5f, 3f));
                if (m == null)
                {
                    return;
                }
                float s = View3D.Squash;
                Matrix4x4 placed = Matrix4x4.Translate(new Vector3(position.x, position.y / s, position.z))
                    * Matrix4x4.Scale(new Vector3(1f, 1f / s, 1f))
                    * Matrix4x4.Rotate(Quaternion.AngleAxis(yaw, Vector3.up))
                    * headLocal;
                for (int i = 0; i < f.parts.Count; i++)
                {
                    PawnRenderNode node = f.parts[i];
                    overrideRotField.SetValue(node, Rot4.South);
                    Graphic g = node.GraphicFor(pawn);
                    Material gm = g != null ? g.MatSouth : null;
                    if (gm == null || gm.mainTexture == null || IsDummy(g))
                    {
                        continue;   // an empty layer
                    }
                    // Facial Animation's own material (its colours, iris mask and soft edges), on the front of the head,
                    // each layer a little further out than the one under it. It writes no depth, so the head's light
                    // pass (queue 3099) still darkens it at night with the head's own cell.
                    float grow = 1.008f + 0.003f * i;
                    Matrix4x4 local = Matrix4x4.Translate(new Vector3(0f, HeadMiddle, 0f))
                        * Matrix4x4.Scale(new Vector3(grow, grow, grow))
                        * Matrix4x4.Translate(new Vector3(0f, -HeadMiddle, 0f));
                    bool isMouth = f.controllers[i].GetType().Name == "MouthControllerComp";
                    Graphics.DrawMesh(isMouth && mouth != null ? mouth : m, Floors.DrawnMatrix(placed * local), gm, 0);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Drawing Facial Animation's face on a 3D head failed: " + e, 93611072);
            }
        }

        // Facial Animation's empty layer (GraphicPool.dummyGraphic, Things/Common/dummy): see-through in the game's
        // shaders, but opaque grey under the head's cut-out material.
        private static bool IsDummy(Graphic g)
        {
            return g.path != null && g.path.EndsWith("/dummy", StringComparison.OrdinalIgnoreCase);
        }

        // A projected mesh and what it was made for.
        private class Projection
        {
            public Mesh mesh;
            public Model of;
            public float size = -1f, shift, zoom;
        }

        private static readonly Projection faceMesh = new Projection();
        private static readonly Projection mouthMesh = new Projection();

        // Where Facial Animation's mouth is in its texture (u, v from the bottom): the mouth's own size is set around it.
        private static readonly Vector2 MouthCentre = new Vector2(0.5f, 1f - 319.5f / 512f);

        // The front of the head model, its texture coordinates projected straight from the front. The .obj files face
        // -Z, but loading flips Z (right- to left-handed), so a loaded figure faces +Z. zoom enlarges the texture round
        // the mouth (1 for the rest of the face).
        private static Mesh MeshFor(Projection pr, Model head, float size, float shift, float zoom)
        {
            if (pr.mesh != null && pr.of == head && pr.size == size && pr.shift == shift && pr.zoom == zoom)
            {
                return pr.mesh;
            }
            if (pr.mesh != null)
            {
                DynamicModels.Retire(pr.mesh);
            }
            pr.of = head;
            pr.size = size;
            pr.shift = shift;
            pr.zoom = zoom;
            pr.mesh = Build(head, size, shift, zoom);
            return pr.mesh;
        }

        private static Mesh Build(Model head, float size, float shift, float zoom)
        {
            float cells = SpriteCells * size;
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color32> colors = new List<Color32>();
            List<int> tris = new List<int>();
            Vector3 middle = new Vector3(0f, HeadMiddle, 0f);
            Vector3[] p = head.positions;
            for (int i = 0; i + 2 < p.Length; i += 3)
            {
                Vector3 a = p[i], b = p[i + 1], c = p[i + 2];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(n, centroid - middle) < 0f)
                {
                    n = -n;   // outward
                }
                if (n.sqrMagnitude < 1e-12f || n.normalized.z < 0.05f)
                {
                    continue;   // only what faces the front
                }
                foreach (Vector3 v in new[] { a, b, c })
                {
                    tris.Add(verts.Count);
                    verts.Add(v);
                    // Seen from the front (+Z, looking back along -Z) the viewer's right is -X.
                    Vector2 uv = new Vector2(0.5f - v.x / cells, 0.5f + (v.y - CentreHeight - shift) / cells);
                    uvs.Add(MouthCentre + (uv - MouthCentre) / zoom);
                    colors.Add(new Color32(255, 255, 255, 255));
                }
            }
            if (verts.Count == 0)
            {
                return null;
            }
            Mesh m = new Mesh();
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetUVs(0, uvs);
            List<Vector3> tags = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                tags.Add(RoofClip.MovingTag);
            }
            m.SetUVs(1, tags);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
