using System.ComponentModel.DataAnnotations;

namespace DynamiteUniverse.Server.Hosting;

public sealed class InstanceOptions
{
    public const string Section = "Instance";
    // Operator-owned stable identity. Never invent a fresh identity on every restart.
    public Guid Id { get; init; }
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; init; } = "Dynamite Universe development server";
}
