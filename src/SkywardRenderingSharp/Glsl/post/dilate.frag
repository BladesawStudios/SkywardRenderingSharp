#version 430 core
// The game's Paint_Constant / Paint_Constant_NewKernel: a four tap max filter on bilinear taps. Each pass lets bright colour bleed
// outward by about a texel, which repeated passes turn into a brush-stroke smear. Kernel 0 is the square pattern, 1 the pinwheel one.

in vec2 vUv;
uniform sampler2D uSource;
uniform vec2 uTexelSize;
uniform int uKernel;
out vec4 oColor;

void main() {
    vec2 t = uTexelSize;
    vec3 a, b, c, d;
    if (uKernel == 0) {
        a = texture(uSource, vUv + vec2(0.5, 0.5) * t).rgb;
        b = texture(uSource, vUv + vec2(-0.5, 0.5) * t).rgb;
        c = texture(uSource, vUv + vec2(0.5, -0.5) * t).rgb;
        d = texture(uSource, vUv + vec2(-0.5, -0.5) * t).rgb;
    } else {
        a = texture(uSource, vUv + vec2(-0.5, -0.5) * t).rgb;
        b = texture(uSource, vUv + vec2(0.5, 0.5) * t).rgb;
        c = texture(uSource, vUv + vec2(1.5, -0.5) * t).rgb;
        d = texture(uSource, vUv + vec2(0.5, -1.5) * t).rgb;
    }
    oColor = vec4(max(max(a, b), max(c, d)), 1.0);
}
