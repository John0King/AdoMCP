using AdoMcp.Models;
using AdoMcp.Services;
using AdoMcp.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdoMcp.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public async Task ServiceEnforcesQueryAndWriteChecksBeforeOpeningTheConnection()
    {
        var registry = ConnectionRegistryTests.Create(false, new DatabaseConfig
        {
            Name = "main", DbType = DbType.Sqlite, ConnectionString = "invalid connection string",
        });
        var service = new DatabaseService(registry, NullLogger<DatabaseService>.Instance);
        var writeError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync("main", "DELETE FROM t"));
        Assert.Contains("disabled", writeError.Message);
        var queryError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueryAsync("main", "SELECT 1; DELETE FROM t"));
        Assert.Contains("one SQL statement", queryError.Message);
    }

    [Fact]
    public async Task SqliteQueriesWritesMetadataAndToolFormattingWorkTogether()
    {
        // Keep the named in-memory database alive across the service's short-lived connections.
        var connectionString = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var registry = ConnectionRegistryTests.Create(true, new DatabaseConfig
        {
            Name = "main", DbType = DbType.Sqlite, ConnectionString = connectionString,
        });
        var service = new DatabaseService(registry, NullLogger<DatabaseService>.Instance);
        var tools = new DatabaseTools(registry, service);
        await service.ExecuteAsync("main", "CREATE TABLE items (id INTEGER PRIMARY KEY, label TEXT); CREATE INDEX ix_label ON items(label);");
        Assert.Equal(3, await service.ExecuteAsync("main", "INSERT INTO items(label) VALUES ('DELETE'), ('two'), ('three')"));
        var result = await service.QueryAsync("main", "SELECT label FROM items ORDER BY id", maxRows: 2);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("DELETE", result.Rows[0]["label"]);
        Assert.Single((await service.QueryAsync("main", "SELECT * FROM items", maxRows: 0)).Rows);
        Assert.Contains("items", (await service.ListDbObjectsAsync("main")).Select(o => o.Name));
        Assert.Equal(2, (await service.GetTableSchemaAsync("main", "items")).Columns.Count);
        Assert.Contains("ix_label", (await service.GetTableIndexesAsync("main", "items")).Select(i => i.IndexName));
        Assert.Equal("label\nDELETE", await tools.QuerySqlAsync("main", "SELECT label FROM items WHERE id = 1"));
        Assert.Equal("value,missing\n\"one,\"\"two\"\"\nthree\",",
            await tools.QuerySqlAsync("main", "SELECT 'one,\"two\"' || char(10) || 'three' AS value, NULL AS missing"));
        Assert.Equal("schema,objectType,objectName,comment\n,TABLE,items,",
            await tools.ListObjectsAsync("main", objectType: "table"));
        Assert.StartsWith("Error:", await tools.QuerySqlAsync("main", "SELECT 1; DELETE FROM items"));
        Assert.Equal(3, (await service.QueryAsync("main", "SELECT * FROM items")).Rows.Count);
        Assert.Equal("1 row(s) affected.", await tools.ExecuteSqlAsync("main", "DELETE FROM items WHERE id = 1"));
        Assert.StartsWith("Error:", await tools.AddConnectionAsync("Data Source=:memory:", "99"));
        Assert.Contains("main,Sqlite,", tools.ListConnections());
        Assert.StartsWith("name,dbType,description,canWrite\n", tools.ListConnections());
    }

    [Fact]
    public async Task ToolCancellationPropagatesInsteadOfBecomingAnErrorString()
    {
        var registry = ConnectionRegistryTests.Create(false, new DatabaseConfig
        {
            Name = "main", DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:",
        });
        var service = new DatabaseService(registry, NullLogger<DatabaseService>.Instance);
        var tools = new DatabaseTools(registry, service);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.QuerySqlAsync("main", "SELECT 1", cancellationToken: new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.AddConnectionAsync("Data Source=:memory:", "Sqlite", testConnection: false, cancellationToken: new CancellationToken(true)));
    }
}
