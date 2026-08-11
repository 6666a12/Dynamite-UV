shader_type canvas_item;
render_mode unshaded;

uniform vec2 size_px = vec2(100.0, 24.0);
uniform float cut_px = 7.0;
uniform float border_px = 1.5;
uniform bool vertical = false;
uniform float detail_strength = 1.0;
uniform int detail_kind = 0;
uniform vec4 top_color : source_color = vec4(0.85, 0.96, 1.0, 1.0);
uniform vec4 mid_color : source_color = vec4(0.30, 0.72, 1.0, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.09, 0.42, 0.68, 1.0);
uniform vec4 border_color : source_color = vec4(0.72, 0.92, 1.0, 1.0);
uniform vec4 flash_color : source_color = vec4(1.0);
uniform float flash_mix = 0.0;

float chamfer_distance(vec2 point, vec2 size, float cut) {
	float distance = min(min(point.x, size.x - point.x),
		min(point.y, size.y - point.y));
	const float diagonal_scale = 0.70710678;
	distance = min(distance, (point.x + point.y - cut) * diagonal_scale);
	distance = min(distance, (size.x - point.x + point.y - cut) * diagonal_scale);
	distance = min(distance, (point.x + size.y - point.y - cut) * diagonal_scale);
	distance = min(distance,
		(size.x - point.x + size.y - point.y - cut) * diagonal_scale);
	return distance;
}

void fragment() {
	vec4 input_color = COLOR;
	vec2 point = UV * size_px;
	float cut = min(cut_px, min(size_px.x, size_px.y) * 0.45);
	float edge = chamfer_distance(point, size_px, cut);
	if (edge < 0.0) {
		discard;
	}

	float axis = vertical ? UV.x : UV.y;
	vec4 surface = axis < 0.56
		? mix(top_color, mid_color, axis / 0.56)
		: mix(mid_color, bottom_color, (axis - 0.56) / 0.44);

	float axis_px = vertical ? point.x : point.y;
	float axis_size = vertical ? size_px.x : size_px.y;
	float upper_highlight = 1.0 - smoothstep(1.0, 4.5, axis_px);
	float lower_shade = smoothstep(axis_size - 4.5, axis_size - 1.0, axis_px);
	surface.rgb = mix(surface.rgb, vec3(1.0), upper_highlight * 0.56 * detail_strength);
	surface.rgb = mix(surface.rgb, bottom_color.rgb * 0.54,
		lower_shade * 0.62 * detail_strength);

	float long_axis = vertical ? UV.y : UV.x;
	float cross_axis = vertical ? UV.x : UV.y;
	float detail = 0.0;
	if (detail_kind == 1) {
		float dash_phase = fract(long_axis * 9.0);
		float dash = step(0.12, dash_phase) * (1.0 - step(0.68, dash_phase));
		detail = (1.0 - smoothstep(0.055, 0.14, abs(cross_axis - 0.5))) * dash;
	} else if (detail_kind == 2) {
		float cell = fract(long_axis * 6.0);
		float chevron_distance = abs(abs(cross_axis - 0.5) - abs(cell - 0.5) * 0.82);
		detail = 1.0 - smoothstep(0.045, 0.105, chevron_distance);
		detail *= step(0.08, cell) * (1.0 - step(0.92, cell));
	} else if (detail_kind == 3) {
		float stripe_distance = abs(fract(long_axis * 6.0 + cross_axis * 1.7) - 0.5);
		detail = 1.0 - smoothstep(0.16, 0.26, stripe_distance);
	} else if (detail_kind == 4) {
		detail = 1.0 - smoothstep(0.06, 0.22, abs(cross_axis - 0.5));
	} else if (detail_kind == 5) {
		float marker_phase = fract(long_axis * 7.0);
		float marker = step(0.20, marker_phase) * (1.0 - step(0.52, marker_phase));
		detail = (1.0 - smoothstep(0.12, 0.25, abs(cross_axis - 0.5))) * marker;
	}
	surface.rgb = mix(surface.rgb, vec3(1.0), detail * 0.62 * detail_strength);

	float outline = 1.0 - smoothstep(border_px, border_px + 0.8, edge);
	surface.rgb = mix(surface.rgb, border_color.rgb, outline);
	surface.a = max(surface.a, border_color.a * outline);
	surface.rgb = mix(surface.rgb, flash_color.rgb, flash_mix);
	COLOR = surface * input_color;
}
