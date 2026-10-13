shader_type spatial;
render_mode cull_disabled;
uniform bool frozen = false;
varying vec2 world_position;
void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}
void fragment() {
    float phase = frozen ? 0.0 : mod(floor(TIME * 4.0), 4.0);
    vec2 p = world_position;
    float ripple = sin(p.x * 9.0 + p.y * 5.0 + phase * 1.570796);
    float crest = smoothstep(0.82, 0.98, ripple);
    ALBEDO = mix(vec3(0.025, 0.29, 0.25), vec3(0.20, 0.61, 0.47), crest * 0.65);
    ROUGHNESS = 0.65;
}
