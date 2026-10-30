using AdoMcp.Models;

namespace AdoMcp.Services;

/// <summary>Named connection configurations and their startup write policy.</summary>
public interface IConnectionRegistry
{
    /// <summary>Returns detached copies with AllowAnySql resolved to the effective policy.</summary>
    IReadOnlyList<DatabaseConfig> GetConfigurations();
    DatabaseConfig GetConfiguration(string name);
    /// <summary>Runtime configurations inherit the global policy. Errors are raised as exceptions.</summary>
    Task AddAsync(DatabaseConfig config, bool testFirst = true, CancellationToken ct = default);
    bool Remove(string name);
}
