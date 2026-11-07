#version 430 core
// A bilinear copy, used to downsample the scene into the paint and blur chains.

in vec2 vUv;
uniform sampler2D uSource;
out vec4 oColor;

void main() { oColor = texture(uSource, vUv); }
