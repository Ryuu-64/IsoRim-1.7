#version 450

// IsoRim sky: zenith to horizon gradient, the horizon colour below it (the distance fog,
// IsoRimDistFog, fades the far map into the same colour, so the cut at the view distance never
// shows), a sun disc with a glow and a moon disc.

layout(location = 0) in vec3 vRay;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _SkyFwd;      // @default = vec4(0.0, 0.0, 1.0, 0.0)
    vec4 _SkyRight;    // @default = vec4(1.0, 0.0, 0.0, 0.0)
    vec4 _SkyUp;       // @default = vec4(0.0, 1.0, 0.0, 0.0)
    vec4 _SkyZenith;   // @default = vec4(0.3, 0.5, 0.85, 1.0)
    vec4 _SkyHorizon;  // @default = vec4(0.75, 0.82, 0.9, 1.0)
    vec4 _SkySun;      // @default = vec4(0.0, 1.0, 0.0, 0.0)
    vec4 _SkySunColor; // @default = vec4(1.0, 0.95, 0.8, 1.0)
    vec4 _SkyMoon;     // @default = vec4(0.0, 1.0, 0.0, 0.0)
} u;

void main()
{
    vec3 r = normalize(vRay);
    float e = r.y;
    vec3 c = u._SkyHorizon.rgb;
    if (e > 0.0)
    {
        c = mix(u._SkyHorizon.rgb, u._SkyZenith.rgb, pow(clamp(e, 0.0, 1.0), 0.55));
    }

    // Sun: a soft glow round it, warm at the horizon, and the disc.
    float sd = dot(r, normalize(u._SkySun.xyz));
    float glow = pow(max(sd, 0.0), 24.0) * 0.35 + pow(max(sd, 0.0), 300.0) * 0.6;
    float disc = smoothstep(0.99965, 0.99985, sd);
    c += u._SkySunColor.rgb * (glow + disc * 1.5) * u._SkySun.w;

    // Moon: a small pale disc and a faint halo.
    float md = dot(r, normalize(u._SkyMoon.xyz));
    float mdisc = smoothstep(0.99975, 0.9999, md);
    float mhalo = pow(max(md, 0.0), 120.0) * 0.12;
    c += vec3(0.85, 0.9, 1.0) * (mdisc * 0.9 + mhalo) * u._SkyMoon.w;

    outColor = vec4(c, 1.0);
}
