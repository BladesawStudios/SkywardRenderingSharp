#version 430 core
// One oversized triangle for the game's post fragment programs, which read their texture coordinate from attribute 0.
// The game's own vertex programs only place a quad and scale its coordinate by engine constants, so this stands in for them.

layout (location = 0) out vec4 out_attr0;

void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    out_attr0 = vec4(p, 0.0, 1.0);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
