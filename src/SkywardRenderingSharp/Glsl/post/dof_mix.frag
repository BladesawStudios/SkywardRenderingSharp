#version 430 core
// Mixes the blurred scene over the sharp one by the depth-of-field amount the game's Dof_FogToAlpha and Dof_MaskNear wrote (clamped, as
// the game's unsigned-normalised target clamps it).

in vec2 vUv;
uniform sampler2D uSharp;
uniform sampler2D uBlur;
uniform sampler2D uAmount;
uniform float uBlurGain;
out vec4 oColor;

void main() {
    vec4 sharp = texture(uSharp, vUv);
    float amount = clamp(texture(uAmount, vUv).r, 0.0, 1.0);
    oColor = vec4(mix(sharp.rgb, texture(uBlur, vUv).rgb * uBlurGain, amount), sharp.a);
}
