#version 430 core
// The Skyward Sword HD material fragment stage: up to 16 GX TEV combiners, alpha test and eight texture maps.
// Combiner input codes are the GX ones; see docs/ubershader.md.

in vec4 vColor0;
in vec4 vColor1;
in vec3 vTexCoord[8];
in vec3 vViewNormal;

uniform sampler2D uTexture[8];

uniform int uStageCount;
uniform ivec4 uColorInputs[16];      // a, b, c, d
uniform ivec4 uColorOps[16];         // bias, subtract, clamp, scale
uniform ivec4 uAlphaInputs[16];
uniform ivec4 uAlphaOps[16];
uniform ivec4 uStageDest[16];        // colour dest, alpha dest, texmap (-1 none), texcoord
uniform ivec4 uStageRas[16];         // ras channel, ras swap, tex swap, konst colour select
uniform ivec4 uStageKonst[16];       // konst alpha select, texcoord is projective

uniform vec4 uTevRegister[4];        // PREV, REG0, REG1, REG2
uniform vec4 uKonst[4];
uniform ivec4 uSwap[4];
uniform ivec4 uAlphaTest;            // ref0, ref1, comp0, comp1
uniform int uAlphaLogic;
uniform int uIrradianceMap;            // texture unit of the engine's "SSS" irradiance map, or -1
uniform ivec2 uOutputRegister;       // the registers the last stage wrote: colour, alpha

out vec4 oColor;

vec4 reg[4];

vec4 sampleMap(int map, vec2 uv) {
    switch (map) {
        case 0: return texture(uTexture[0], uv);
        case 1: return texture(uTexture[1], uv);
        case 2: return texture(uTexture[2], uv);
        case 3: return texture(uTexture[3], uv);
        case 4: return texture(uTexture[4], uv);
        case 5: return texture(uTexture[5], uv);
        case 6: return texture(uTexture[6], uv);
        default: return texture(uTexture[7], uv);
    }
}

vec4 swizzle(vec4 c, ivec4 s) { return vec4(c[s.x], c[s.y], c[s.z], c[s.w]); }

vec3 konstColor(int sel) {
    if (sel < 8) return vec3(1.0 - float(sel) * 0.125);
    if (sel < 16) return uKonst[sel - 12].rgb;
    return vec3(uKonst[sel & 3][(sel - 16) >> 2]);
}

float konstAlpha(int sel) {
    if (sel < 8) return 1.0 - float(sel) * 0.125;
    return uKonst[sel & 3][(sel - 16) >> 2];
}

vec3 colorInput(int code, vec4 tex, vec4 ras, vec3 konst) {
    if (code < 8) return (code & 1) != 0 ? vec3(reg[code >> 1].a) : reg[code >> 1].rgb;
    switch (code) {
        case 8: return tex.rgb;
        case 9: return vec3(tex.a);
        case 10: return ras.rgb;
        case 11: return vec3(ras.a);
        case 12: return vec3(1.0);
        case 13: return vec3(0.5);
        case 14: return konst;
        default: return vec3(0.0);
    }
}

float alphaInput(int code, vec4 tex, vec4 ras, float konst) {
    switch (code) {
        case 0: return reg[0].a;
        case 1: return reg[1].a;
        case 2: return reg[2].a;
        case 3: return reg[3].a;
        case 4: return tex.a;
        case 5: return ras.a;
        case 6: return konst;
        default: return 0.0;
    }
}

float biasOf(int b) { return b == 1 ? 0.5 : b == 2 ? -0.5 : 0.0; }
float scaleOf(int s) { return s == 1 ? 2.0 : s == 2 ? 4.0 : s == 3 ? 0.5 : 1.0; }

// out = (d + (subtract ? -1 : 1) * lerp(a, b, c) + bias) * scale, the GX combiner equation.
vec3 combine(vec3 a, vec3 b, vec3 c, vec3 d, ivec4 op) {
    vec3 mixed = mix(a, b, c);
    vec3 r = (d + (op.y != 0 ? -mixed : mixed) + biasOf(op.x)) * scaleOf(op.w);
    return op.z != 0 ? clamp(r, 0.0, 1.0) : clamp(r, -1.0, 2.0);
}

bool compare(int func, float value, float ref) {
    switch (func) {
        case 0: return false;
        case 1: return value < ref;
        case 2: return value == ref;
        case 3: return value <= ref;
        case 4: return value > ref;
        case 5: return value != ref;
        case 6: return value >= ref;
        default: return true;
    }
}

// Stand-in for the engine's runtime "SSS" map (the pass that fills it was not found): a rim term, bright where the surface turns edge-on to the eye and small elsewhere, which is how thin translucent
// skin lights up. The material adds (0.09 + this) twice (times the albedo and squared), so a constant brightens the whole body.
float subsurface() {
    float rim = 1.0 - abs(normalize(vViewNormal).z);
    return 0.34 + 0.6 * rim * rim;
}

void main() {
    for (int i = 0; i < 4; i++) reg[i] = uTevRegister[i];
    for (int s = 0; s < uStageCount; s++) {
        ivec4 dest = uStageDest[s], rasInfo = uStageRas[s];
        vec4 tex = vec4(0.0);
        if (dest.z >= 0) {
            vec3 t = vTexCoord[dest.w];
            vec2 uv = uStageKonst[s].y != 0 ? t.xy / t.z : t.xy;
            // The engine renders the SSS map (lit irradiance, blurred, in UV space) at runtime; here the interpolated lit colour stands in for it.
            tex = dest.z == uIrradianceMap ? vec4(vec3(subsurface()), 1.0) : swizzle(sampleMap(dest.z, uv), uSwap[rasInfo.z]);
        }
        vec4 ras = rasInfo.x == 0 ? vColor0 : rasInfo.x == 1 ? vColor1 : vec4(0.0);
        ras = swizzle(ras, uSwap[rasInfo.y]);

        vec3 kc = konstColor(rasInfo.w);
        float ka = konstAlpha(uStageKonst[s].x);
        ivec4 ci = uColorInputs[s], ai = uAlphaInputs[s];
        vec3 color = combine(colorInput(ci.x, tex, ras, kc), colorInput(ci.y, tex, ras, kc), colorInput(ci.z, tex, ras, kc), colorInput(ci.w, tex, ras, kc), uColorOps[s]);
        float alpha = combine(vec3(alphaInput(ai.x, tex, ras, ka)), vec3(alphaInput(ai.y, tex, ras, ka)), vec3(alphaInput(ai.z, tex, ras, ka)), vec3(alphaInput(ai.w, tex, ras, ka)), uAlphaOps[s]).x;
        reg[dest.x].rgb = color;
        reg[dest.y].a = alpha;
    }
    vec4 result = vec4(reg[uOutputRegister.x].rgb, reg[uOutputRegister.y].a);
    float a = result.a * 255.0;
    bool t0 = compare(uAlphaTest.z, a, float(uAlphaTest.x)), t1 = compare(uAlphaTest.w, a, float(uAlphaTest.y));
    bool pass = uAlphaLogic == 0 ? (t0 && t1) : uAlphaLogic == 1 ? (t0 || t1) : uAlphaLogic == 2 ? (t0 != t1) : (t0 == t1);
    if (!pass) discard;
    oColor = vec4(clamp(result.rgb, 0.0, 1.0), clamp(result.a, 0.0, 1.0));
}
