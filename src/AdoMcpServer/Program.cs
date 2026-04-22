using System.CommandLine;
using AdoMcpServer.Models;
using AdoMcpServer.Services;
using AdoMcpServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// CLI definition
// ─────────────────────────────────────────────────────────────────────────────

var httpOption = new Option<bool>("--http")
{
    Description = "Start in HTTP/SSE mode instead of the default stdio mode.",
};

// --stdio is accepted for backwards-compatibility; it is a no-op because stdio
// is the default, but some MCP clients pass it explicitly.
var stdioOption = new Option<bool>("--stdio")
{
    Description = "Start in stdio mode (default; accepted for compatibility).",
};

var allowAnySqlOption = new Option<bool>("--allow-any-sql")
{
    Description = "Enable the execute_sql MCP tool. Omit for read-only/safe mode.",
};

var rootCommand = new RootCommand(
    "AdoMCP Server — MCP server for database schema discovery and SQL execution.")
{
    httpOption,
    stdioOption,
    allowAnySqlOption,
};

// Allow unrecognised tokens (e.g. --urls, --environment) to pass through to the host.
rootCommand.TreatUnmatchedTokensAsErrors = false;

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    bool isHttp = parseResult.GetValue(httpOption)
        || string.Equals(
               Environment.GetEnvironmentVariable("ADOMCP_MODE"),
               "http",
               StringComparison.OrdinalIgnoreCase);

    bool allowAnySql = parseResult.GetValue(allowAnySqlOption);

    // Forward any unrecognised tokens (e.g. --urls, --environment) to the host.
    var extraArgs = parseResult.UnmatchedTokens.ToArray();

    if (isHttp)
        await RunHttpModeAsync(extraArgs, allowAnySql, ct);
    else
        await RunStdioModeAsync(extraArgs, allowAnySql, ct);
});

return await rootCommand.Parse(args).InvokeAsync();

// ─────────────────────────────────────────────────────────────────────────────
// HTTP / SSE mode — full ASP.NET Core web application
// ─────────────────────────────────────────────────────────────────────────────

static async Task RunHttpModeAsync(string[] args, bool allowAnySql, CancellationToken ct)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureConfiguration(builder.Configuration, builder.Environment.EnvironmentName);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    ConfigureServices(builder.Services, builder.Configuration, allowAnySql);
    builder.Services
        .AddMcpServer()
        .WithToolsFromAssembly(typeof(DatabaseTools).Assembly)
        .WithHttpTransport();

    var app = builder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync(ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// stdio mode — pure console host (no Kestrel / web server started).
// stdout is the MCP JSON-RPC transport channel; ALL logging MUST go to stderr
// so it never corrupts the message stream.
// ─────────────────────────────────────────────────────────────────────────────

static async Task RunStdioModeAsync(string[] args, bool allowAnySql, CancellationToken ct)
{
    var builder = Host.CreateApplicationBuilder(args);

    ConfigureConfiguration(builder.Configuration, builder.Environment.EnvironmentName);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
    ConfigureServices(builder.Services, builder.Configuration, allowAnySql);
    builder.Services
        .AddMcpServer()
        .WithToolsFromAssembly(typeof(DatabaseTools).Assembly)
        .WithStdioServerTransport();

    await builder.Build().RunAsync(ct);
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared setup helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Rebuilds the configuration pipeline, layering JSON files, environment variables
/// (prefixed <c>ADOMCP_</c>), and user secrets on top of each other.</summary>
static void ConfigureConfiguration(IConfigurationBuilder config, string environmentName)
{
    config.Sources.Clear();
    config
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
        .AddEnvironmentVariables("ADOMCP_")
        .AddUserSecrets<Program>(optional: true);
}

/// <summary>Registers core application services and parses startup flags.</summary>
static void ConfigureServices(
    IServiceCollection services, IConfiguration configuration, bool allowAnySql)
{
    services.Configure<List<DatabaseConfig>>(configuration.GetSection("Databases"));
    services.AddSingleton<IDatabaseService, DatabaseService>();
    services.AddSingleton(new ServerOptions { AllowAnySql = allowAnySql });
}
