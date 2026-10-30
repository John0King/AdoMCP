using AdoMcp.Models;

namespace AdoMcp.Services;

/// <summary>Database metadata and SQL operations. SQL access checks are enforced here.</summary>
public interface IDatabaseService
{
    Task<List<TableInfo>> ListDbObjectsAsync(string connectionName, string? nameFilter = null, string? schemaFilter = null, CancellationToken ct = default);
    Task<TableSchema> GetTableSchemaAsync(string connectionName, string tableName, string? schema = null, CancellationToken ct = default);
    Task<List<IndexInfo>> GetTableIndexesAsync(string connectionName, string tableName, string? schema = null, CancellationToken ct = default);
    Task<QueryResult> QueryAsync(string connectionName, string sql, int maxRows = 200, CancellationToken ct = default);
    Task<int> ExecuteAsync(string connectionName, string sql, CancellationToken ct = default);
}
