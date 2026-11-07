#version 430 core
// The Skyward Sword HD material vertex stage: the GX emulation of the game's shader template (position transform, two light
// channels, eight texgens), driven by uniforms instead of #defines. See docs/ubershader.md.

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aColor;
layout(location = 3) in vec4 aUv0;              // texcoord sets 0 and 1
layout(location = 4) in vec4 aUv1;              // sets 2 and 3
layout(location = 5) in vec4 aUv2;              // sets 4 and 5
layout(location = 6) in vec4 aUv3;              // sets 6 and 7
layout(location = 7) in vec4 aBoneIds;          // up to four skin influences
layout(location = 8) in vec4 aBoneWeights;      // all zero: the vertex is not skinned

uniform mat4 uModelView;
uniform mat4 uProjection;
uniform mat3 uNormalMatrix;
uniform mat4 uSkin[64];                          // inverse bind * animated world, per bone

// Light channels: [0] is colour/alpha channel 0, [1] channel 1. Controls are the raw XF register words.
uniform vec4 uMaterialColor[2];
uniform vec4 uAmbientColor[2];
uniform uint uChannelControl[4];                 // colour0, alpha0, colour1, alpha1
uniform vec3 uLightDirection[8];                 // view space, towards the light
uniform vec3 uLightColor[8];

// Texgens: x = source row, y = projective (STQ), z = unused, w = dual-matrix normalise.
uniform int uTexGenCount;
uniform ivec4 uTexGen[8];
uniform mat4 uTexMatrix[8];
uniform mat4 uPostMatrix[8];

out vec4 vColor0;
out vec4 vColor1;
out vec3 vTexCoord[8];
out vec3 vViewNormal;

// One channel's light sum: GX clamps ambient + sum(diffuse) and multiplies by the material colour.
vec3 lightChannel(uint control, vec3 material, vec3 ambient, vec3 vertexColor, vec3 n) {
    vec3 mat = (control & 1u) != 0u ? vertexColor : material;
    if ((control & 2u) == 0u) return mat;
    vec3 amb = (control & 64u) != 0u ? vertexColor : ambient;
    uint mask = ((control >> 2u) & 15u) | (((control >> 11u) & 15u) << 4u);
    uint diffuseFunction = (control >> 7u) & 3u;
    vec3 sum = amb;
    for (int i = 0; i < 8; i++) {
        if (((mask >> uint(i)) & 1u) == 0u) continue;
        float d = diffuseFunction == 0u ? 1.0 : dot(n, uLightDirection[i]);
        if (diffuseFunction == 2u) d = max(d, 0.0);
        sum += uLightColor[i] * d;
    }
    return clamp(sum, 0.0, 1.0) * mat;
}

vec2 uvSet(int set) {
    vec4 pair = set < 2 ? aUv0 : set < 4 ? aUv1 : set < 6 ? aUv2 : aUv3;
    return (set & 1) == 0 ? pair.xy : pair.zw;
}

vec3 texGenSource(int row, vec3 viewPosition, vec3 viewNormal) {
    // As on the GX hardware, position and normal inputs are already in view space.
    if (row == 0) return viewPosition;
    if (row == 1) return viewNormal;
    if (row == 2) return aColor.rgb;
    return vec3(uvSet(row - 5), 1.0);
}

vec3 texGen(int i, vec3 viewPosition, vec3 viewNormal) {
    ivec4 g = uTexGen[i];
    vec3 t = (uTexMatrix[i] * vec4(texGenSource(g.x, viewPosition, viewNormal), 1.0)).xyz;
    if (g.y == 0) t.z = 1.0;
    if (g.w != 0) t = normalize(t);
    return (uPostMatrix[i] * vec4(t, 1.0)).xyz;
}

// Linear blend skinning: the bind-space vertex is carried to the animated pose by each influencing bone's skin matrix.
void skin(out vec3 position, out vec3 normal) {
    position = aPosition; normal = aNormal;
    if (aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w <= 0.0) return;
    vec3 p = vec3(0.0), n = vec3(0.0);
    for (int i = 0; i < 4; i++) {
        float w = aBoneWeights[i];
        if (w <= 0.0) continue;
        mat4 m = uSkin[int(aBoneIds[i])];
        p += w * (m * vec4(aPosition, 1.0)).xyz;
        n += w * (mat3(m) * aNormal);
    }
    position = p; normal = n;
}

void main() {
    vec3 skinnedPosition, skinnedNormal;
    skin(skinnedPosition, skinnedNormal);
    vec4 view = uModelView * vec4(skinnedPosition, 1.0);
    gl_Position = uProjection * view;
    vec3 n = normalize(uNormalMatrix * skinnedNormal);

    vColor0 = vec4(lightChannel(uChannelControl[0], uMaterialColor[0].rgb, uAmbientColor[0].rgb, aColor.rgb, n),
                   (uChannelControl[1] & 1u) != 0u ? aColor.a : uMaterialColor[0].a);
    vColor1 = vec4(lightChannel(uChannelControl[2], uMaterialColor[1].rgb, uAmbientColor[1].rgb, aColor.rgb, n),
                   (uChannelControl[3] & 1u) != 0u ? aColor.a : uMaterialColor[1].a);

    vViewNormal = n;
    for (int i = 0; i < 8; i++) vTexCoord[i] = i < uTexGenCount ? texGen(i, view.xyz, n) : vec3(0.0, 0.0, 1.0);
}
