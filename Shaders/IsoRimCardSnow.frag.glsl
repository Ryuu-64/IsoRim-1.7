#version 450

// IsoRim: the game's Data\Core\Shaders\Map\TransparentPlant.frag.glsl (1.7.4976) unchanged apart from two global
// members after _SwayHead that IsoRimCard.vert.glsl reads (the material layout is read from this
// stage). Compare with the game's file after a game update.

layout(location = 1) in vec2 vUv;
layout(location = 3) flat in vec2 vRoofOwner;   // roof clipping (IsoRimCard.vert.glsl)
layout(location = 4) in vec3 vRoofPos;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;      // @color @default = vec4(1.0, 1.0, 1.0, 1.0) (unused, kept for layout)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _SwayHead;  // @default = 0.0 (consumed by PlantWindSway.vert, kept for layout)
    vec4 _IsoView;    // global, no default: w = Squash (read by IsoRimCard.vert.glsl)
    vec4 _IsoCard;    // global, no default: sin a, cos a (a = pitch - 90), sin yaw, cos yaw
    vec4 _IsoCellMap; // global, no default: (origin x, origin z, W, H) of the cell texture (IsoCellMap)
    vec4 _IsoRoof;    // global, no default: roof clipping (RoofClip): x = 1 on, y = wall height, z = Rise, w = Pitch
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 3) uniform sampler2D _IsoCellTex;   // @default = black   global: the cell texture (IsoCellMap)
layout(set = 2, binding = 4) uniform sampler2D _IsoRoofTex;   // @default = black   global: the roof over each cell (RoofClip)

// ---- Roof clipping (Source\RoofClip.cs). The same in IsoRimSolid.frag.glsl, IsoRimCardPlant.frag.glsl and
// IsoRimCardSnow.frag.glsl. _IsoRoofTex: one texel per cell, r = the lid drawn over it (1 built roof, 2 rock, 3 ship
// deck, 0 none), g = corner levels SW | SE << 4, b = NW | NE << 4 (built roofs). _IsoRoof: x = 1 while roofs are drawn,
// y = wall height, z = Rise, w = Pitch (SectionLayer_IsoRimRoofs). Roof space: x, z in map cells of the owner's floor,
// y in true cells above that floor.
const float RoofOverhang = 0.07;   // SectionLayer_IsoRimRoofs.Overhang: the eave strip outside a built roof
const float RoofUnder = 0.012;     // the plain copy under a built roof's slates (SectionLayer_IsoRimRoofs.Facet)
const float RoofMargin = 0.004;

ivec2 RoofTexel(ivec2 c, out bool inside)
{
    ivec2 size = ivec2(uMaterial._IsoCellMap.zw);
    c -= ivec2(floor(uMaterial._IsoCellMap.xy + 0.5));
    inside = c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y;
    return ivec2(c.x, size.y - 1 - c.y);   // rows stored north first
}

// The lid kind over cell c (0 none or open, 1 built, 2 rock, 3 ship), with its texel.
int RoofLid(ivec2 c, bool openToo, out vec4 r)
{
    bool inside;
    ivec2 t = RoofTexel(c, inside);
    r = vec4(0.0);
    if (!inside)
        return 0;
    r = texelFetch(_IsoRoofTex, t, 0);
    int kind = int(round(r.r * 255.0));
    if (kind < 1 || kind > 3)
        return 0;
    if (!openToo && (int(round(texelFetch(_IsoCellTex, t, 0).a * 255.0)) & 2) != 0)
        return 0;   // opened by the cut-away: not drawn
    return kind;
}

// The underside of the lid of kind over a cell at f (0..1 across it), less the margin, in true cells.
float RoofUnderside(int kind, vec4 r, vec2 f)
{
    vec4 R = uMaterial._IsoRoof;
    if (kind == 2)
        return R.y - 0.005 - RoofMargin;   // rock: a flat lid just under the wall tops
    if (kind == 3)
        return R.y - RoofMargin;           // ship deck (ShipRoofs: wall tops + 0.005)
    int g = int(round(r.g * 255.0));
    int b = int(round(r.b * 255.0));
    float base = R.y + R.z - RoofUnder - RoofMargin;
    float sw = base + R.w * float(g & 15);
    float se = base + R.w * float(g >> 4);
    float nw = base + R.w * float(b & 15);
    float ne = base + R.w * float(b >> 4);
    float u = f.x, v = f.y;
    // Split along the same diagonal as the drawn facets.
    if (abs(sw - ne) >= abs(nw - se))
        return v > u ? sw + (ne - nw) * u + (nw - sw) * v : sw + (se - sw) * u + (ne - se) * v;
    return u + v < 1.0 ? sw + (se - sw) * u + (nw - sw) * v : ne + (ne - nw) * (u - 1.0) + (ne - se) * (v - 1.0);
}

float RoofEave(ivec2 n, vec2 f)
{
    vec4 r;
    if (RoofLid(n, false, r) != 1)
        return 1e9;
    return RoofUnderside(1, r, f) - RoofOverhang * uMaterial._IsoRoof.w;   // the eave slopes down by this
}

// True: a fragment of a thing standing in cell owner, at p (roof space), is above the underside of a roof drawn over it.
bool RoofHides(vec3 p, vec2 owner)
{
    if (uMaterial._IsoRoof.x < 0.5)
        return false;
    vec4 r;
    if (RoofLid(ivec2(floor(owner)), true, r) == 0)
        return false;   // only things that stand under a roof (open or not) are clipped
    ivec2 c = ivec2(floor(p.xz));
    vec2 f = p.xz - vec2(c);
    int kind = RoofLid(c, false, r);
    if (kind != 0)
        return p.y > RoofUnderside(kind, r, f);
    // Outside the roof: only the eave strip of a built roof beside it.
    float h = 1e9;
    if (f.x < RoofOverhang)
        h = min(h, RoofEave(c + ivec2(-1, 0), vec2(1.0, f.y)));
    if (f.x > 1.0 - RoofOverhang)
        h = min(h, RoofEave(c + ivec2(1, 0), vec2(0.0, f.y)));
    if (f.y < RoofOverhang)
        h = min(h, RoofEave(c + ivec2(0, -1), vec2(f.x, 1.0)));
    if (f.y > 1.0 - RoofOverhang)
        h = min(h, RoofEave(c + ivec2(0, 1), vec2(f.x, 0.0)));
    return p.y > h;
}

void main()
{
    if (RoofHides(vRoofPos, vRoofOwner))
        discard;
    outColor = texture(_MainTex, vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy);
}
