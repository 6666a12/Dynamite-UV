namespace DynamiteUniverse.Server.Hosting;

internal static class UnavailableFeature
{
    internal static IResult Result(string feature) => Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Feature not implemented",
        detail: $"{feature} is not available in this scaffold.",
        extensions: new Dictionary<string, object?> { ["code"] = "feature_not_implemented" });
}
