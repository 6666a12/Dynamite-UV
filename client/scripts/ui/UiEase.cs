namespace DuxCommunity.Ui;

/// <summary>Semantic UI easing curves shared by manual progress and Tween callbacks.</summary>
public static class UiEase
{
    public static float Standard(float progress) => CubicBezier(progress, 0.2f, 0.8f, 0.2f, 1f);
    public static float Enter(float progress) => CubicBezier(progress, 0.16f, 1f, 0.3f, 1f);
    public static float Exit(float progress) => CubicBezier(progress, 0.4f, 0f, 1f, 1f);
    public static float Signal(float progress) => CubicBezier(progress, 0.65f, 0f, 0.15f, 1f);
    public static float Echo(float progress) => CubicBezier(progress, 0.22f, 1f, 0.36f, 1f);

    public static float CubicBezier(float progress, float x1, float y1, float x2, float y2)
    {
        var target = Math.Clamp(progress, 0f, 1f);
        if (target is <= 0f or >= 1f)
            return target;

        var parameter = target;
        for (var i = 0; i < 6; i++)
        {
            var error = Sample(parameter, x1, x2) - target;
            var slope = SampleDerivative(parameter, x1, x2);
            if (Math.Abs(error) < 0.00001f || Math.Abs(slope) < 0.00001f)
                break;
            parameter = Math.Clamp(parameter - error / slope, 0f, 1f);
        }

        var low = 0f;
        var high = 1f;
        for (var i = 0; i < 8; i++)
        {
            var sampled = Sample(parameter, x1, x2);
            if (Math.Abs(sampled - target) < 0.00001f)
                break;
            if (sampled < target)
                low = parameter;
            else
                high = parameter;
            parameter = (low + high) * 0.5f;
        }

        return Math.Clamp(Sample(parameter, y1, y2), 0f, 1f);
    }

    private static float Sample(float t, float point1, float point2)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * t * point1 +
            3f * inverse * t * t * point2 + t * t * t;
    }

    private static float SampleDerivative(float t, float point1, float point2)
    {
        var inverse = 1f - t;
        return 3f * inverse * inverse * point1 +
            6f * inverse * t * (point2 - point1) +
            3f * t * t * (1f - point2);
    }
}
