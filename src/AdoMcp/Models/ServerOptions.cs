namespace AdoMcp.Models;

/// <summary>Runtime options parsed from command-line arguments at startup.</summary>
public class ServerOptions
{
    /// <summary>
    /// Global default write policy for dynamic and non-overriding static connections, resolved at startup.
    /// </summary>
    public bool AllowAnySql { get; init; }
}
