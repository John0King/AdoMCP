using AdoMcp.Models;
using AdoMcp.Services.Providers;

namespace AdoMcp.Services;

internal sealed class DatabaseService(IConnectionRegistry connections, ILogger<DatabaseService> logger) : IDatabaseService
{
    private readonly Dictionary<DbType, IDbProvider> _providers = new()
    {
        [DbType.SqlServer] = new SqlServerDbProvider(logger),
        [DbType.MySql] = new MySqlDbProvider(logger),
        [DbType.PostgreSql] = new PostgreSqlDbProvider(logger),
        [DbType.Sqlite] = new SqliteDbProvider(logger),
        [DbType.Oracle] = new OracleDbProvider(logger),
    };

    public async Task<List<TableInfo>> ListDbObjectsAsync(
        string connectionName, string? nameFilter = null,
        string? schemaFilter = null, CancellationToken ct = default)
    {
        var cfg = connections.GetConfiguration(connectionName);
        var conn = await DbConnections.OpenAsync(cfg, ct);
        await using (conn)
            return await GetProvider(cfg).ListDbObjectsAsync(conn, nameFilter, schemaFilter, ct);
    }

    public async Task<TableSchema> GetTableSchemaAsync(
        string connectionName, string tableName, string? schema = null, CancellationToken ct = default)
    {
        var cfg = connections.GetConfiguration(connectionName);
        var conn = await DbConnections.OpenAsync(cfg, ct);
        await using (conn)
            return await GetProvider(cfg).GetTableSchemaAsync(conn, tableName, schema, ct);
    }

    public async Task<List<IndexInfo>> GetTableIndexesAsync(
        string connectionName, string tableName, string? schema = null, CancellationToken ct = default)
    {
        var cfg = connections.GetConfiguration(connectionName);
        var conn = await DbConnections.OpenAsync(cfg, ct);
        await using (conn)
            return await GetProvider(cfg).GetIndexesAsync(conn, tableName, schema, ct);
    }

    public async Task<QueryResult> QueryAsync(string connectionName, string sql, int maxRows = 200, CancellationToken ct = default)
    {
        var config = connections.GetConfiguration(connectionName);
        SqlQueryGuard.Validate(sql, config.DbType);
        await using var connection = await DbConnections.OpenAsync(config, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        logger.LogDebug("Query on '{Connection}': {Sql}", connectionName, sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new QueryResult();
        for (var i = 0; i < reader.FieldCount; i++) result.Columns.Add(reader.GetName(i));
        maxRows = Math.Clamp(maxRows, 1, 1000);
        while (result.Rows.Count < maxRows && await reader.ReadAsync(ct))
        {
            var row = new QueryRow();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Rows.Add(row);
        }
        result.RowsAffected = reader.RecordsAffected;
        return result;
    }

    public async Task<int> ExecuteAsync(string connectionName, string sql, CancellationToken ct = default)
    {
        var config = connections.GetConfiguration(connectionName);
        if (config.AllowAnySql != true)
            throw new InvalidOperationException($"execute_sql is disabled for connection '{connectionName}'.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        await using var connection = await DbConnections.OpenAsync(config, ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        logger.LogDebug("Execute on '{Connection}': {Sql}", connectionName, sql);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private IDbProvider GetProvider(DatabaseConfig config) => _providers[config.DbType];
}
