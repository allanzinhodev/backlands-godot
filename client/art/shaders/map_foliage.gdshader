shader_type spatial;
uniform bool frozen = false;
void vertex() {
    float phase = frozen ? 0.0 : mod(floor(TIME * 4.0), 4.0);
    float sway = sin(phase * 1.570796 + MODEL_MATRIX[3].x * 0.7);
    VERTEX.x += sway * 0.045 * max(VERTEX.y + 0.5, 0.0);
}
void fragment() {
    ALBEDO = COLOR.rgb;
    ROUGHNESS = 1.0;
}
