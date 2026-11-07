#version 430 core
// The game's BloomMask_Custom, evaluated on the scene in linear light (colour textures are sRGB and decode to linear when sampled, the lighting is linear, and the final composite encodes to sRGB): the part of each colour channel above a luma threshold, doubled and scaled by the scene's mask alpha.

in vec2 vUv;
uniform sampler2D uScene;
uniform sampler2D uDepth;       // the sky (depth 1) never blooms
uniform vec4 uThreshold;        // per channel (r, g, b, a)
out vec4 oColor;

void main() {
    vec4 c = texture(uScene, vUv);
    float luma = dot(c.rgb, vec3(0.257, 0.504, 0.098));
    float mask = texture(uDepth, vUv).r < 0.9999 ? c.a : 0.0;
    // The game writes this into an unsigned-normalised target, which clamps the part below the threshold to zero.
    oColor = max(2.0 * c * mask * (vec4(luma) - uThreshold), 0.0);
}
