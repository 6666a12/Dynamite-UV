using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Left-pinned perspective cylinder in Y/Z. The positive-depth eye is over x=0;
/// ±4° is measured at the actual pane edges. Normalise arc length to the native scroll page so
/// both physical end rows and EnsureControlVisible reach the original first/last material pixel.</summary>
internal readonly struct CatalogueProjection(float height)
{
    private const float A = Mathf.Pi / 45f;
    private float H => Mathf.Max(height, 1f);
    public float Eye => H * (450f / 456f);
    public float Radius => H * .5f * Eye / (Eye * Mathf.Sin(A) - H * .5f * (1f - Mathf.Cos(A)));
    public float HalfArc => Radius * A;

    public Vector2 MaterialAt(Vector2 p)
    {
        float eye = Eye, r = Radius, middle = H * .5f, raw = p.Y - middle;
        float y = Mathf.Clamp(raw, -middle, middle);
        float a = Mathf.Asin(Mathf.Clamp(y * (eye + r) / (r * Mathf.Sqrt(eye * eye + y * y)), -1f, 1f)) - Mathf.Atan2(y, eye);
        float k = eye / (eye + r * (1f - Mathf.Cos(a)));
        float derivative = k * Mathf.Cos(a) - r * k * k * Mathf.Sin(a) * Mathf.Sin(a) / eye;
        return new(p.X / k, middle + (r * a + (raw - y) / Mathf.Max(.22f, derivative)) * middle / HalfArc);
    }

    public Vector2 Project(Vector2 q)
    {
        float a = (q.Y / H * 2f - 1f) * A;
        float k = Eye / (Eye + Radius * (1f - Mathf.Cos(a)));
        return new(q.X * k, H * .5f + Radius * Mathf.Sin(a) * k);
    }
}
