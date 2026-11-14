#version 430 core
// The last pass: fog by eye distance (not on the sky), the two bloom layers added, and the linear scene encoded to sRGB.

in vec2 vUv;
uniform sampler2D uScene;       // the scene after depth of field and paint
uniform sampler2D uDepth;       // the scene's depth buffer
uniform sampler2D uBloom;       // the blurred bloom mask
uniform sampler2D uBloom2;      // the same, blurred again (the wider layer)
uniform vec2 uBloomWeights;     // layer intensities from the bloom parameters, times strength and blur gain
uniform vec3 uBloomTint;        // the bloom block's second colour (+0x18), which the engine multiplies the layers by

uniform vec2 uNearFar;          // camera planes, for linearising depth
uniform vec3 uFogColor;
uniform vec2 uFogRange;         // eye distance where fog starts and where it is complete
uniform int uFlags;             // 4 bloom, 8 fog
out vec4 oColor;

float eyeDistance(float depth) {
    float n = uNearFar.x, f = uNearFar.y;
    return 2.0 * n * f / (f + n - (depth * 2.0 - 1.0) * (f - n));
}

void main() {
    vec3 color = texture(uScene, vUv).rgb;
    float depth = texture(uDepth, vUv).r;
    if ((uFlags & 8) != 0 && depth < 0.9999)
        color = mix(color, uFogColor, clamp((eyeDistance(depth) - uFogRange.x) / max(uFogRange.y - uFogRange.x, 1e-3), 0.0, 1.0));
    if ((uFlags & 4) != 0) color += (texture(uBloom, vUv).rgb * uBloomWeights.x + texture(uBloom2, vUv).rgb * uBloomWeights.y) * uBloomTint;
    // The scene is linear light; the display wants sRGB.
    oColor = vec4(mix(color * 12.92, 1.055 * pow(max(color, 0.0), vec3(1.0 / 2.4)) - 0.055, step(0.0031308, color)), 1.0);
}
