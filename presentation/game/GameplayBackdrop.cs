using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

/// <summary>A still, diffuse ion field behind the playfield. One untextured pass; no timing,
/// note data, overlay capture or geometry that could be confused for a lane.</summary>
public partial class GameplayBackdrop : Node2D
{
    private static Shader? _shader;
    public float TopPadding { get; set; }

    public override void _Ready()
    {
        _shader ??= new Shader { Code = EnergyFieldShader };
        const float width = GameplayStageGeometry.DesignWidth;
        float height = GameplayStageGeometry.DesignHeight + TopPadding;
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("lane_guides", new Vector3(
            GameplayStageGeometry.LeftLineX / width,
            GameplayStageGeometry.RightLineX / width,
            (GameplayStageGeometry.CenterLineY + TopPadding) / height));
        AddChild(new ColorRect
        {
            Name = "EnergyField",
            Position = new Vector2(0, -TopPadding),
            Size = new Vector2(width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = material,
        });
    }

    // Source-color uniforms preserve the intended dark palette when the canvas uses linear color.
    private const string EnergyFieldShader = """
        shader_type canvas_item;
        render_mode unshaded;

        uniform vec3 lane_guides;
        uniform vec4 dark_ink : source_color = vec4(0.034, 0.046, 0.073, 1.0);
        uniform vec4 depth_ink : source_color = vec4(0.059, 0.078, 0.109, 1.0);
        uniform vec4 ion_blue : source_color = vec4(0.080, 0.345, 0.462, 1.0);

        float mist(vec2 p) {
            return exp2(-dot(p, p));
        }

        float cell_value(vec2 cell) {
            vec3 h = fract(cell.xyx * vec3(0.1031, 0.11369, 0.13787));
            h += dot(h, h.yzx + 33.33);
            return fract((h.x + h.y) * h.z);
        }

        float cloud(vec2 p) {
            vec2 cell = floor(p);
            vec2 a = fract(p);
            a = a * a * (3.0 - 2.0 * a);
            float bottom = mix(cell_value(cell), cell_value(cell + vec2(1.0, 0.0)), a.x);
            float top = mix(cell_value(cell + vec2(0.0, 1.0)), cell_value(cell + vec2(1.0, 1.0)), a.x);
            return mix(bottom, top, a.y);
        }

        void fragment() {
            float x = UV.x * 2.0 - 1.0;
            float y = UV.y;
            float top_clear = smoothstep(0.075, 0.31, y);
            float floor_clear = 1.0 - smoothstep(0.69, lane_guides.z + 0.045, y);
            float side_bias = mix(0.21, 1.0, smoothstep(0.10, 0.82, abs(x)));

            // Two coarse scales bend the plumes and make their density uneven. No fine grain,
            // contours, sparks, or on-beat flicker that could read as incoming chart objects.
            float bend = cloud(UV * vec2(6.9, 4.4) + vec2(8.1, 1.3));
            vec2 swirl = vec2(UV.x + 0.28 * y, y - 0.30 * UV.x);
            float curl = cloud(swirl * vec2(9.8, 7.0) + vec2(2.9, bend * 0.48));
            float w = y + (bend - 0.5) * 0.177 + (curl - 0.5) * 0.053;
            float left = mist(vec2((x + 0.69) * 1.73, (w - 0.48 - 0.17 * x) * 5.6));
            float right = mist(vec2((x - 0.61) * 1.96, (w - 0.52 + 0.16 * x) * 6.5));
            float tuft = mist(vec2((x + 0.49) * 2.35, (w - 0.66 + 0.09 * x) * 7.2));
            float density = smoothstep(0.23, 0.71, mix(bend, curl, 0.51));
            float charge = (left * 0.67 + right * 0.77 + tuft * 0.27) * (0.32 + density * 0.68);
            float air = mist(vec2(x * 0.91, (y - 0.47) * 3.12));
            vec3 ink = mix(dark_ink.rgb, depth_ink.rgb, air * top_clear * floor_clear * 0.73);

            // Union of the three exterior half-planes, blurred across the boundaries. This is
            // color only: notes are composited on top on either side of all three lines.
            float outside_distance = max(max(lane_guides.x - UV.x, UV.x - lane_guides.y),
                                         (y - lane_guides.z) * 0.58);
            float exterior = smoothstep(-0.041, 0.067, outside_distance);
            float strength = charge * top_clear * floor_clear * side_bias * 0.68;
            ink = mix(ink, ion_blue.rgb, strength * (1.0 - 0.18 * exterior));

            // Recolor the same fog rather than blending in a separate purple layer. The violet
            // vector has unit weighted luminance, so crossing a rail adds hue, not a glow step.
            float luma = dot(ink, vec3(0.2126, 0.7152, 0.0722));
            vec3 violet = luma * vec3(1.12, 0.92, 1.44);
            ink = mix(ink, violet, exterior * (0.63 + 0.12 * density));

            // Keep inherited canvas modulation so the existing stage transition still fades it.
            COLOR = vec4(ink * COLOR.rgb, COLOR.a);
        }
        """;
}
