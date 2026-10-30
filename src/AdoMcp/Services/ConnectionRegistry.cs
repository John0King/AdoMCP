using AdoMcp.Models;
using Microsoft.Extensions.Options;

namespace AdoMcp.Services;

internal sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly Dictionary<string, DatabaseConfig> _static;
    private readonly Dictionary<string, DatabaseConfig> _dynamic = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly bool _allowAnySql;
    private readonly ILogger<ConnectionRegistry> _logger;

    public ConnectionRegistry(IOptions<List<DatabaseConfig>> options, ServerOptions serverOptions, ILogger<ConnectionRegistry> logger)
    {
        _allowAnySql = serverOptions.AllowAnySql;
        _logger = logger;
        _static = new(StringComparer.OrdinalIgnoreCase);
        foreach (var config in options.Value)
            _static.TryAdd(config.Name, Copy(config, config.AllowAnySql ?? _allowAnySql));
    }

    public IReadOnlyList<DatabaseConfig> GetConfigurations()
    {
        lock (_lock)
            return _dynamic.Values.Concat(_static.Values.Where(c => !_dynamic.ContainsKey(c.Name)))
                .Select(c => Copy(c, c.AllowAnySql)).ToList().AsReadOnly();
    }

    public DatabaseConfig GetConfiguration(string name)
    {
        lock (_lock)
        {
            if (_dynamic.TryGetValue(name, out var config) || _static.TryGetValue(name, out config))
                return Copy(config, config.AllowAnySql);
            throw new KeyNotFoundException($"No database configuration named '{name}' was found. Available: {string.Join(", ", _dynamic.Keys.Concat(_static.Keys).Distinct(StringComparer.OrdinalIgnoreCase))}");
        }
    }

    public async Task AddAsync(DatabaseConfig config, bool testFirst = true, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.ConnectionString);
        if (!Enum.IsDefined(config.DbType))
            throw new NotSupportedException($"DbType '{config.DbType}' is not supported.");

        // Snapshot before awaiting; callers cannot mutate the stored configuration or its policy.
        var snapshot = Copy(config, _allowAnySql);
        if (testFirst)
        {
            await using var connection = await DbConnections.OpenAsync(snapshot, ct);
        }
        ct.ThrowIfCancellationRequested();
        lock (_lock) _dynamic[snapshot.Name] = snapshot;
        _logger.LogInformation("Connection '{Name}' ({DbType}) added to global dynamic store.", snapshot.Name, snapshot.DbType);
    }

    public bool Remove(string name)
    {
        bool removed;
        lock (_lock) removed = _dynamic.Remove(name);
        if (removed) _logger.LogInformation("Connection '{Name}' removed from global dynamic store.", name);
        return removed;
    }

    private static DatabaseConfig Copy(DatabaseConfig config, bool? canWrite) => new()
    {
        Name = config.Name, DbType = config.DbType, ConnectionString = config.ConnectionString,
        Description = config.Description, AllowAnySql = canWrite,
    };
}
