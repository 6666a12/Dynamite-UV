shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float u_energy = 1.0;
uniform float u_glow = 1.6;
uniform float u_threshold = 0.42;

vec3 energy_at(sampler2D tex, vec2 uv) {
    vec3 color = texture(tex, uv).rgb;
    float peak = max(color.r, max(color.g, color.b));
    float gate = smoothstep(u_threshold, u_threshold + 0.55, peak);
    return color * gate;
}

vec3 field(sampler2D tex, vec2 uv, vec2 pixel, float radius) {
    vec3 value = energy_at(tex, uv) * 0.55;
    for (int i = 0; i < 16; i++) {
        float angle = float(i) * 0.39269908;
        vec2 direction = vec2(cos(angle), sin(angle));
        value = max(value, energy_at(tex, uv + direction * pixel * radius));
    }
    return value;
}

void fragment() {
    vec4 base = texture(TEXTURE, UV);
    float energy = max(u_energy, 0.0);
    float spread = 2.5 + energy * 3.0;
    vec3 near_field = field(TEXTURE, UV, TEXTURE_PIXEL_SIZE, spread) * exp(-1.0);
    vec3 far_field = field(TEXTURE, UV, TEXTURE_PIXEL_SIZE, spread * 2.45) * exp(-0.166667);
    vec3 halo = (near_field * 0.25 + far_field * 0.075) * (0.48 + energy * 0.65) * u_glow;
    float outside = 1.0 - smoothstep(0.035, 0.92, base.a);
    halo *= outside;
    // COLOR carries the entrance modulate; additive blending ignores alpha for RGB,
    // so the fade is premultiplied here instead of relying on the alpha channel.
    COLOR = vec4(clamp(halo, vec3(0.0), vec3(1.2)) * COLOR.rgb * COLOR.a, 1.0);
}
