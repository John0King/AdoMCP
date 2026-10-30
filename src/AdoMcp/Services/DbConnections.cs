using System.Data.Common;
using AdoMcp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DbType = AdoMcp.Models.DbType;

namespace AdoMcp.Services;

internal static class DbConnections
{
    public static async Task<DbConnection> OpenAsync(DatabaseConfig config, CancellationToken ct)
    {
        DbConnection connection = config.DbType switch
        {
            DbType.SqlServer => new SqlConnection(config.ConnectionString),
            DbType.MySql => new MySqlConnection(config.ConnectionString),
            DbType.PostgreSql => new NpgsqlConnection(config.ConnectionString),
            DbType.Sqlite => new SqliteConnection(config.ConnectionString),
            DbType.Oracle => new OracleConnection(config.ConnectionString),
            _ => throw new NotSupportedException($"DbType '{config.DbType}' is not supported."),
        };
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
