#version 450

// IsoRim: the light pass of the plant cards (def IsoRimLitMarkCard, vertex stage IsoRimCard.vert.glsl, so it covers
// exactly the pixels of the card, swaying with it). As IsoRimSolid.frag.glsl in its light pass (_GlowMode 1): the
// colour already there is multiplied by the light of the plant's own cell, by the game's LightOverlay formula, and
// stencil bit 32 is set (by the def) so the pulled overlay skips the pixel. Without it a card took the light of the
// pulled overlay sheet in front of it, which is not there wherever the card stands against the sky (a low or
// first-person camera): the part of a plant above the horizon stayed lit at night.

layout(location = 1) in vec2 vUv;
layout(location = 3) flat in vec2 vRoofOwner;   // the plant's hinge in the map mesh: its own cell (IsoRimCard.vert.glsl)

layout(location = 0) out vec4 outColor;

// The first members as in IsoRimCardPlant.frag.glsl (IsoRimCard.vert.glsl reads them; the layout is read from this
// stage), then the glow globals of IsoRimSolid.frag.glsl.
layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;      // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MainTex_ST; // @default = vec4(0.0, 0.0, 1.0, 1.0)
    float _SwayHead;  // @default = 0.0
    vec4 _IsoView;    // global, no default: w = Squash (read by IsoRimCard.vert.glsl)
    vec4 _IsoCard;    // global, no default: sin a, cos a (a = pitch - 90), sin yaw, cos yaw
    vec4 _IsoSky;     // global, no default: rgb = the game's sky light colour (the LightOverlay _Color)
    vec4 _IsoGlowMap; // global, no default: (origin x, origin z, 1/(W+1), 1/(H+1)) of the glow texture
} uMaterial;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _IsoGlowTex;   // global: texel (x, z) = cell corner (x, z)

// The game's LightOverlay.frag.glsl as a multiplier (the same as GlowLight in IsoRimSolid.frag.glsl).
vec3 GlowLight(vec4 g)
{
    vec3 s = uMaterial._IsoSky.rgb * (1.0 - g.a);
    vec3 L = s + g.rgb;
    float clampBase = 1.5 - 0.5 * min(s.r, s.b);
    if (L.r > clampBase) L *= clampBase / L.r;
    if (L.g > clampBase) L *= clampBase / L.g;
    if (L.b > clampBase) L *= clampBase / L.b;
    return L;
}

void main()
{
    // Cut out as the card itself (IsoRimCardPlant.frag.glsl), so its holes are left to the overlay.
    vec4 texVal = texture(_MainTex, vUv * uMaterial._MainTex_ST.zw + uMaterial._MainTex_ST.xy);
    if (texVal.a < 0.35)
        discard;

    // The whole card takes the light of its own cell (its hinge is the middle of the cell's bottom edge, in the
    // mesh's own space: on an upper floor that is already the floor's own row).
    vec4 gm = uMaterial._IsoGlowMap;
    vec2 xz = vRoofOwner;
    vec2 uv = vec2((xz.x - gm.x + 0.5) * gm.z, 1.0 - (xz.y - gm.y + 0.5) * gm.w);
    vec3 glowLight = GlowLight(textureLod(_IsoGlowTex, uv, 0.0));

    // Blend DstColor/SrcColor: the colour already there times 2 * this, as the game's overlay.
    outColor = vec4(glowLight * 0.5, 1.0);
}
