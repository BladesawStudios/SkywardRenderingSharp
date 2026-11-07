#version 430 core
// The painterly composite. Distant (and very near) pixels are replaced by the dilated "paint" image, as the game's Paint_NearFar does;
// far pixels are first softened with the blurred image (the depth-of-field mask), then the bloom is added.

in vec2 vUv;
uniform sampler2D uSharp;       // the scene
uniform sampler2D uDepth;       // the scene's depth buffer
uniform sampler2D uPaint;       // the dilated, downsampled scene
uniform sampler2D uBlur;        // the blurred scene
uniform sampler2D uBloom;       // the blurred bloom mask
uniform sampler2D uBloom2;      // the same, blurred again (the wider layer)
uniform vec2 uBloomWeights;     // layer intensities from the bloom parameters
uniform vec3 uBloomTint;        // the bloom block's second colour (+0x18), which the engine multiplies the layers by

uniform vec2 uNearFar;          // camera planes, for linearising depth
uniform vec2 uPaintTexel;       // texel size of uPaint
uniform vec2 uPaintFar;         // eye distance where painting begins and where it is complete
uniform float uPaintConstant;
uniform vec2 uPaintNear;        // eye distance where the near painting is none and where it is complete (start > end)
uniform vec2 uDofFar;           // eye distance where blur begins and where it is complete
uniform float uDofNear;         // eye distance below which pixels are fully blurred (0: off)
uniform vec3 uFogColor;
uniform vec2 uFogRange;         // eye distance where fog starts and where it is complete
uniform float uBloomStrength;
uniform int uFlags;             // 1 paint, 2 depth of field, 4 bloom, 8 fog
out vec4 oColor;

float eyeDistance(float depth) {
    float n = uNearFar.x, f = uNearFar.y;
    return 2.0 * n * f / (f + n - (depth * 2.0 - 1.0) * (f - n));
}

// Paint_NearFar's four tap max over the paint image.
vec3 dilatedPaint(vec2 uv) {
    vec2 t = uPaintTexel * 0.5;
    vec3 a = texture(uPaint, uv + vec2(t.x, t.y)).rgb, b = texture(uPaint, uv + vec2(-t.x, t.y)).rgb;
    vec3 c = texture(uPaint, uv + vec2(t.x, -t.y)).rgb, d = texture(uPaint, uv + vec2(-t.x, -t.y)).rgb;
    return max(max(a, b), max(c, d));
}

void main() {
    vec3 color = texture(uSharp, vUv).rgb;
    float depth = texture(uDepth, vUv).r;
    float z = eyeDistance(depth);
    bool sky = depth >= 0.9999;

    if ((uFlags & 2) != 0 && !sky) {
        float coc = clamp((z - uDofFar.x) / max(uDofFar.y - uDofFar.x, 1e-3), 0.0, 1.0);
        if (uDofNear > 0.0 && z < uDofNear) coc = 1.0;
        color = mix(color, texture(uBlur, vUv).rgb, coc);
    }
    if ((uFlags & 1) != 0 && !sky) {
        float far = clamp((z - uPaintFar.x) / max(uPaintFar.y - uPaintFar.x, 1e-3), 0.0, 1.0);
        float near = clamp((uPaintNear.x - z) / max(uPaintNear.x - uPaintNear.y, 1e-3), 0.0, 1.0);
        color = mix(color, dilatedPaint(vUv), clamp(far + near + uPaintConstant, 0.0, 1.0));
    }
    if ((uFlags & 8) != 0 && !sky) color = mix(color, uFogColor, clamp((z - uFogRange.x) / max(uFogRange.y - uFogRange.x, 1e-3), 0.0, 1.0));
    if ((uFlags & 4) != 0) color += (texture(uBloom, vUv).rgb * uBloomWeights.x + texture(uBloom2, vUv).rgb * uBloomWeights.y) * uBloomStrength * uBloomTint;
    // The scene is linear light; the display wants sRGB.
    oColor = vec4(mix(color * 12.92, 1.055 * pow(max(color, 0.0), vec3(1.0 / 2.4)) - 0.055, step(0.0031308, color)), 1.0);
}
