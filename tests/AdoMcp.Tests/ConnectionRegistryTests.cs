using AdoMcp.Models;
using AdoMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AdoMcp.Tests;

public class ConnectionRegistryTests
{
    [Fact]
    public async Task DynamicConnectionsShadowStaticAndRemovalRestoresStaticPolicy()
    {
        var registry = Create(false, new DatabaseConfig { Name = "Main", ConnectionString = "static", AllowAnySql = true });
        var dynamicConfig = new DatabaseConfig { Name = "MAIN", ConnectionString = "dynamic", AllowAnySql = true };
        await registry.AddAsync(dynamicConfig, testFirst: false);
        var resolved = registry.GetConfiguration("main");
        Assert.Equal("dynamic", resolved.ConnectionString);
        Assert.False(resolved.AllowAnySql);
        Assert.True(dynamicConfig.AllowAnySql); // Registration must not mutate the caller's object.
        Assert.Single(registry.GetConfigurations());
        Assert.True(registry.Remove("main"));
        Assert.Equal("static", registry.GetConfiguration("main").ConnectionString);
        Assert.True(registry.GetConfiguration("main").AllowAnySql);
        Assert.False(registry.Remove("main"));
    }

    [Fact]
    public async Task ConfigurationCopiesCannotChangeStoredCredentialsOrPolicy()
    {
        var original = new DatabaseConfig { Name = "static", ConnectionString = "original", AllowAnySql = false };
        var registry = Create(true, original);
        original.AllowAnySql = true;
        original.ConnectionString = "changed";
        var dynamicConfig = new DatabaseConfig { Name = "dynamic", ConnectionString = "dynamic", AllowAnySql = false };
        await registry.AddAsync(dynamicConfig, testFirst: false);
        dynamicConfig.ConnectionString = "changed";
        foreach (var config in registry.GetConfigurations())
        {
            config.AllowAnySql = !config.AllowAnySql;
            config.ConnectionString = "changed";
        }
        registry.GetConfiguration("static").AllowAnySql = true;
        Assert.False(registry.GetConfiguration("static").AllowAnySql);
        Assert.Equal("original", registry.GetConfiguration("static").ConnectionString);
        Assert.True(registry.GetConfiguration("dynamic").AllowAnySql);
        Assert.Equal("dynamic", registry.GetConfiguration("dynamic").ConnectionString);
    }

    [Fact]
    public async Task FailedOrCancelledRegistrationDoesNotReplaceExistingConnection()
    {
        var registry = Create(false, new DatabaseConfig { Name = "main", ConnectionString = "original" });
        await Assert.ThrowsAnyAsync<Exception>(() => registry.AddAsync(new DatabaseConfig
        {
            Name = "main", DbType = DbType.Sqlite, ConnectionString = "not a connection string",
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.AddAsync(new DatabaseConfig
        {
            Name = "main", ConnectionString = "replacement",
        }, false, new CancellationToken(true)));
        await Assert.ThrowsAsync<NotSupportedException>(() => registry.AddAsync(new DatabaseConfig
        {
            Name = "main", DbType = (DbType)99, ConnectionString = "replacement",
        }, false));
        Assert.Equal("original", registry.GetConfiguration("main").ConnectionString);
    }

    internal static ConnectionRegistry Create(bool allowAnySql, params DatabaseConfig[] configs) => new(
        Options.Create(configs.ToList()), new ServerOptions { AllowAnySql = allowAnySql }, NullLogger<ConnectionRegistry>.Instance);
}
