using AdoMcpServer.Models;
using AdoMcpServer.Services;
using AdoMcpServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────────────────────
// Transport-mode detection
//
// stdio is the default transport mode (suitable for MCP clients that launch
// the process directly).  Pass --http (or set ADOMCP_MODE=http) to start an
// HTTP/SSE server instead.
//
//   --http            force HTTP/SSE mode
//   --stdio           force stdio mode (explicit, same as default)
//   ADOMCP_MODE=http  env-var override to HTTP
// ─────────────────────────────────────────────────────────────────────────────
var modeArg = args.FirstOrDefault(a =>
    a.Equals("--stdio", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--http",  StringComparison.OrdinalIgnoreCase));

// ─────────────────────────────────────────────────────────────────────────────
// Feature flags
//
//   --allow-any-sql   Enables the execute_sql MCP tool.
//                     Omit this flag to run in a read-only/safe mode where the
//                     LLM cannot execute arbitrary SQL against your databases.
// ─────────────────────────────────────────────────────────────────────────────
bool allowAnySql = args.Any(a =>
    a.Equals("--allow-any-sql", StringComparison.OrdinalIgnoreCase));

var envMode = Environment.GetEnvironmentVariable("ADOMCP_MODE");

// HTTP mode only when explicitly requested; stdio is the default.
bool isHttp =
    modeArg?.Equals("--http", StringComparison.OrdinalIgnoreCase) == true
    || string.Equals(envMode, "http", StringComparison.OrdinalIgnoreCase);

bool isStdio = !isHttp;

// ─────────────────────────────────────────────────────────────────────────────
// Builder
// ─────────────────────────────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables("ADOMCP_")
    .AddUserSecrets<Program>(optional: true);

// ── Logging ──────────────────────────────────────────────────────────────────
// In stdio mode the stdout stream is the MCP transport channel — ALL log output
// MUST go to stderr so it never corrupts the JSON-RPC message stream.
builder.Logging.ClearProviders();
if (isStdio)
{
    builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
}
else
{
    builder.Logging.AddConsole();
}

// ── Kestrel: suppress HTTP server in stdio mode ───────────────────────────────
// In stdio mode there is no HTTP traffic; prevent Kestrel from binding any port
// so we don't waste resources or conflict with other services.
if (isStdio)
{
    builder.WebHost.UseUrls(string.Empty);
}

// ── Database configs ──────────────────────────────────────────────────────────
builder.Services.Configure<List<DatabaseConfig>>(
    builder.Configuration.GetSection("Databases"));

// ── App services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton(new ServerOptions { AllowAnySql = allowAnySql });

// ── MCP server ────────────────────────────────────────────────────────────────
var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithToolsFromAssembly(typeof(DatabaseTools).Assembly);

if (isStdio)
{
    mcpBuilder.WithStdioServerTransport();
}
else
{
    mcpBuilder.WithHttpTransport();
}

// ─────────────────────────────────────────────────────────────────────────────
// App
// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!isStdio)
{
    app.MapMcp("/mcp");
}

await app.RunAsync();
