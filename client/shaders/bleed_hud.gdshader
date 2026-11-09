// Gameplay BLEED HUD: approved v8 geometry, mode color and full luminous rim.
shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform vec2 u_size = vec2(1040.0, 82.0);
uniform float u_pad = 56.0;
uniform float u_health = 1.0;
uniform float u_trail_health = 1.0;
uniform float u_glow = 1.0;
uniform vec3 u_color = vec3(0.211765, 0.537255, 0.850980);
uniform float u_impact_alpha = 0.0;
uniform float u_low_pulse = 0.0;
uniform float u_hardcore = 0.0;

float rail_sdf(vec2 p) {
    float dx = abs(p.x - u_size.x * 0.5);
    // Thin side wings: y=0..44. Central trapezoid reaches y=82.
    float wing = min(min(p.y, 44.0 - p.y), u_size.x * 0.5 - dx);
    wing = min(wing, (u_size.x * 0.5 + 44.0 - 16.0 - dx - p.y) * 0.70710678);
    float center = min(p.y, u_size.y - p.y);
    float slope = (144.0 - dx - max(p.y - 44.0, 0.0) * (34.0 / 38.0)) * 0.745;
    center = min(center, slope);
    // Carve a real opening: slopes run from x=+/-132 at the top to +/-62 at y=58.
    // The 24px bridge below the opening keeps low health visible below pause.
    float notch_top = clamp((132.0 - dx) * (58.0 / 70.0), 0.0, 58.0);
    float notch_edge = (p.y - notch_top) * ((dx > 62.0 && dx < 132.0) ? 0.770 : 1.0);
    return min(max(wing, center), notch_edge);
}

float segment_distance(vec2 p, vec2 a, vec2 b) {
    vec2 v = b - a;
    return length(p - a - v * clamp(dot(p - a, v) / dot(v, v), 0.0, 1.0));
}

float luminous_edge_distance(vec2 p) {
    vec2 q = vec2(abs(p.x - u_size.x * 0.5), p.y);
    float cut = segment_distance(q, vec2(62.0, 58.0), vec2(132.0, 0.0));
    float bridge = segment_distance(q, vec2(0.0, 82.0), vec2(110.0, 82.0));
    float shoulder = segment_distance(q, vec2(110.0, 82.0), vec2(144.0, 44.0));
    float wing = segment_distance(q, vec2(144.0, 44.0), vec2(504.0, 44.0));
    float tip = segment_distance(q, vec2(504.0, 44.0), vec2(520.0, 28.0));
    float upper = segment_distance(q, vec2(132.0, 0.0), vec2(520.0, 0.0));
    float outer = segment_distance(q, vec2(520.0, 0.0), vec2(520.0, 28.0));
    float notch_bottom = segment_distance(q, vec2(0.0, 58.0), vec2(62.0, 58.0));
    return min(min(upper, min(outer, notch_bottom)), min(cut, min(bridge, min(shoulder, min(wing, tip)))));
}

float box_mask(vec2 p, vec2 center, vec2 halfsize) {
    vec2 d = abs(p - center) - halfsize;
    return 1.0 - smoothstep(-0.5, 0.5, max(d.x, d.y));
}

vec4 over(vec4 front, vec4 back) {
    return vec4(front.rgb + back.rgb * (1.0 - front.a),
                front.a + back.a * (1.0 - front.a));
}

vec4 track_layer(vec2 p) {
    float edge = rail_sdf(p);
    float shape = smoothstep(-0.6, 0.6, edge);
    float rim = shape * (1.0 - smoothstep(0.4, 1.3, edge));
    vec3 color = mix(vec3(0.075, 0.105, 0.15), vec3(0.19, 0.23, 0.29), rim * 0.65);
    return vec4(color * shape, shape);
}

vec4 trail_layer(vec2 p) {
    float shape = smoothstep(-0.6, 0.6, rail_sdf(p));
    float distance_x = abs(p.x - u_size.x * 0.5);
    float current_half = u_size.x * 0.5 * clamp(u_health, 0.0, 1.0);
    float trail_half = u_size.x * 0.5 * clamp(u_trail_health, 0.0, 1.0);
    float interval = smoothstep(current_half - 0.5, current_half + 0.5, distance_x)
                   * (1.0 - smoothstep(trail_half - 0.5, trail_half + 0.5, distance_x));
    // Neutral pale loss trace: red is mode identity, not a health threshold.
    float alpha = shape * interval * 0.65;
    return vec4(mix(u_color, vec3(0.85, 0.87, 0.90), 0.65) * alpha, alpha);
}

vec4 fill_layer(vec2 p) {
    float width = u_size.x * clamp(u_health, 0.0, 1.0);
    if (width < 0.001) return vec4(0.0);
    // Both ends retreat toward the fixed center; remaining width is still proportional to HP.
    float edge = min(rail_sdf(p), width * 0.5 - abs(p.x - u_size.x * 0.5));
    float silhouette = smoothstep(-0.6, 0.6, edge);
    float dx = abs(p.x - u_size.x * 0.5);
    float half_width = width * 0.5;
    float vertical = clamp(p.y / u_size.y, 0.0, 1.0);
    // Broad subtle tonal body, not a thick plastic bevel or a Note-style white core.
    vec3 base = u_color * (0.66 + 0.14 * vertical);
    base += u_color * (0.055 * exp(-pow((p.y - 26.0) / 19.0, 2.0)));
    float light_distance = luminous_edge_distance(p);
    float line = exp(-light_distance * light_distance / 1.8);
    float inner_light = exp(-light_distance * light_distance / 22.0);
    vec3 hot = u_hardcore > 0.5 ? vec3(1.0, 0.16, 0.19) : vec3(0.20, 0.78, 1.0);
    base = mix(base, hot, line * 0.9);
    base += hot * inner_light * 0.15;
    // Strong near-edge glow, rapidly fading far field. Empty wings do not glow.
    float near_glow = exp(-light_distance * light_distance / 30.0) * 0.34;
    float far_glow = exp(-light_distance * light_distance / 160.0) * 0.065;
    float fill_gate = 1.0 - smoothstep(half_width, half_width + 3.0, dx);
    float halo = (near_glow + far_glow) * fill_gate * u_glow * (1.0 - silhouette);
    return vec4(clamp(base, 0.0, 1.0) * silhouette + hot * halo, silhouette + halo);
}

vec4 impact_layer(vec2 p) {
    if (u_impact_alpha <= 0.0001) return vec4(0.0);
    // Use nearest boundary rather than adding two flashes: no doubled flare at zero HP.
    vec2 q = vec2(abs(p.x - u_size.x * 0.5)
                   - u_size.x * 0.5 * clamp(u_health, 0.0, 1.0), p.y - u_size.y * 0.5);
    float slit = exp(-q.x * q.x / 1.6)
               * (1.0 - smoothstep(u_size.y * 0.5 - 2.0, u_size.y * 0.5, abs(q.y)));
    float alpha = slit * u_impact_alpha * 0.8 * smoothstep(-0.6, 0.6, rail_sdf(p));
    return vec4(mix(u_color, vec3(0.90, 0.92, 0.96), 0.65) * alpha, alpha);
}

vec4 anchors_layer(vec2 p) {
    // Three mirrored ticks per side, long nearest the rail and shorter outward.
    float outward = abs(p.x - u_size.x * 0.5) - u_size.x * 0.5;
    vec2 q = vec2(outward, p.y);
    float marks = box_mask(q, vec2(5.0, 14.0), vec2(1.0, 12.0));
    marks = max(marks, box_mask(q, vec2(12.0, 8.0), vec2(1.0, 6.0)));
    marks = max(marks, box_mask(q, vec2(19.0, 7.0), vec2(1.0, 4.0)));
    vec3 color = mix(u_color, vec3(0.65, 0.7, 0.78), 0.50);
    float alpha = marks * (0.72 + 0.20 * u_low_pulse);
    return vec4(color * alpha, alpha);
}

void fragment() {
    vec2 p = UV * (u_size + vec2(u_pad * 2.0)) - vec2(u_pad);
    vec4 result = track_layer(p);
    if (u_trail_health > u_health) result = over(trail_layer(p), result);
    result = over(fill_layer(p), result);
    result = over(impact_layer(p), result);
    result = over(anchors_layer(p), result);
    COLOR = result;
}
