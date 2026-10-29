shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform float u_energy = 1.0;
uniform float u_glow = 1.0;
uniform float u_opacity = 1.0;
uniform vec3 u_halo_color : source_color = vec3(0.2, 0.85, 1.0);

float field(sampler2D tex, vec2 uv, vec2 pixel, float radius) {
    float value = texture(tex, uv).a * 0.55;
    vec2 d = vec2(radius, 0.0);
    value = max(value, texture(tex, uv + d.xy * pixel).a);
    value = max(value, texture(tex, uv - d.xy * pixel).a);
    value = max(value, texture(tex, uv + d.yx * pixel).a);
    value = max(value, texture(tex, uv - d.yx * pixel).a);
    value = max(value, texture(tex, uv + d.xx * pixel * 0.7071).a);
    value = max(value, texture(tex, uv - d.xx * pixel * 0.7071).a);
    value = max(value, texture(tex, uv + vec2(d.x, -d.x) * pixel * 0.7071).a);
    value = max(value, texture(tex, uv + vec2(-d.x, d.x) * pixel * 0.7071).a);
    return value;
}

void fragment() {
    vec4 item_modulate = COLOR;
    vec4 tex = texture(TEXTURE, UV);
    float energy = max(u_energy, 0.0);
    float silhouette = smoothstep(0.035, 0.92, tex.a);
    float spread = 3.0 + energy * 3.5;
    float near_field = field(TEXTURE, UV, TEXTURE_PIXEL_SIZE, spread);
    float far_field = field(TEXTURE, UV, TEXTURE_PIXEL_SIZE, spread * 2.45);
    float near_glow = near_field * exp(-1.0);
    float far_glow = far_field * exp(-0.166667);
    float halo = (near_glow * 0.25 + far_glow * 0.075) * (0.48 + energy * 0.65) * u_glow;
    halo = clamp(halo, 0.0, 0.8);
    float hot = clamp(0.08 + energy * 0.19, 0.0, 0.62);
    vec3 base = mix(tex.rgb, vec3(1.0), hot * silhouette);
    base *= 0.52 + energy * 0.48;
    vec3 halo_color = mix(u_halo_color, vec3(1.0), clamp((energy - 1.0) * 0.12, 0.0, 0.18));
    vec3 result = base * silhouette + halo_color * halo * (1.0 - silhouette);
    float alpha = silhouette + halo * (1.0 - silhouette);
    COLOR = vec4(result * u_opacity * item_modulate.a, alpha * u_opacity * item_modulate.a);
}
