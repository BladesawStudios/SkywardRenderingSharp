#version 430 core
// The game's GaussianBlur_Horizontal / _Vertical: five linear-sampled taps (a nine tap Gaussian). The game's constants sum to 2 per pass (gain 4 over both); the blends that use the result
// evidently halve it, so the weights here are halved to give a unit-gain blur (the shipped constants make every blurred region four times too bright).

in vec2 vUv;
uniform sampler2D uSource;
uniform vec2 uStep;             // one texel along the blur axis
out vec4 oColor;

void main() {
    const float offsets[4] = float[](1.38462, -1.38462, 3.23077, -3.23077);
    const float weights[3] = float[](0.227025, 0.316215, 0.07027);
    vec4 sum = texture(uSource, vUv) * weights[0];
    sum += (texture(uSource, vUv + uStep * offsets[0]) + texture(uSource, vUv + uStep * offsets[1])) * weights[1];
    sum += (texture(uSource, vUv + uStep * offsets[2]) + texture(uSource, vUv + uStep * offsets[3])) * weights[2];
    oColor = sum;
}
