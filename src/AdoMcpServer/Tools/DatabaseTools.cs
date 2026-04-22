using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdoMcpServer.Models;
using AdoMcpServer.Services;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace AdoMcpServer.Tools;

/// <summary>MCP tools that expose database metadata and query execution to AI models.</summary>
[McpServerToolType]
public class DatabaseTools(IDatabaseService db, ServerOptions serverOptions)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly CsvConfiguration CsvConfig =
        new(CultureInfo.InvariantCulture) { NewLine = "\n" };

    // Matches destructive DML/DDL keywords at word boundaries (case-insensitive).
    private static readonly Regex DestructiveKeywordsRegex = new(
        @"\b(DROP|DELETE|UPDATE|ALTER)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches any write (non-SELECT) SQL keywords — used to guard query_sql.
    private static readonly Regex WriteKeywordsRegex = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|REPLACE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns <c>true</c> when the SQL text contains at least one destructive keyword
    /// (DROP, DELETE, UPDATE, ALTER).
    /// </summary>
    private static bool IsDestructiveSql(string sql) =>
        DestructiveKeywordsRegex.IsMatch(sql);

    /// <summary>
    /// Converts a user-provided name pattern into a SQL LIKE filter.
    /// If the pattern contains no SQL wildcards (% or _), it is treated as a
    /// substring search by wrapping it in %. If null, returns null (no filter).
    /// </summary>
    private static string? ToLikeFilter(string? namePattern)
    {
        if (namePattern is null) return null;
        return namePattern.Contains('%') || namePattern.Contains('_')
            ? namePattern
            : $"%{namePattern}%";
    }

    /// <summary>Serialises a sequence of records to a CSV string using CsvHelper.</summary>
    private static string ToCsv<T>(IEnumerable<T> records)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CsvConfig);
        csv.WriteRecords(records);
        return writer.ToString().TrimEnd();
    }

    /// <summary>
    /// Converts a <see cref="QueryResult"/> (dynamic columns) to a CSV string.
    /// Returns an empty string when the result has no columns.
    /// </summary>
    private static string QueryResultToCsv(QueryResult result)
    {
        if (result.Columns.Count == 0) return string.Empty;

        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CsvConfig);

        // Header row
        foreach (var col in result.Columns)
            csv.WriteField(col);
        csv.NextRecord();

        // Data rows
        foreach (var row in result.Rows)
        {
            foreach (var col in result.Columns)
                csv.WriteField(row.TryGetValue(col, out var val) ? val?.ToString() : null);
            csv.NextRecord();
        }

        return writer.ToString().TrimEnd();
    }

    /// <summary>
    /// Rewrites a SQLite connection string so that the <c>Data Source</c> value is an
    /// absolute path.  In-memory databases (<c>:memory:</c> or empty source) and
    /// already-absolute paths are returned unchanged.
    /// </summary>
    private static string ResolveSqliteDataSource(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        var dataSource = builder.DataSource;

        // Leave special values and already-absolute paths untouched.
        if (string.IsNullOrEmpty(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(dataSource))
        {
            return connectionString;
        }

        // Resolve relative path against the current working directory.
        builder.DataSource = Path.GetFullPath(dataSource);
        return builder.ToString();
    }

    // CSV row types ────────────────────────────────────────────────────────────

    private sealed record ObjectCsvRow(
        [property: Name("schema")]     string Schema,
        [property: Name("objectType")] string ObjectType,
        [property: Name("objectName")] string ObjectName,
        [property: Name("comment")]    string? Comment);

    private sealed record ConnectionCsvRow(
        [property: Name("name")]        string Name,
        [property: Name("dbType")]      string DbType,
        [property: Name("description")] string? Description);

    // ─────────────────────────────────────────────────────────────────────────
    // list_connections
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_connections")]
    [Description("List all configured database connections (pre-configured + dynamically added at runtime). Use this to discover available connection names for other tools. Returns CSV (name,dbType,description).")]
    public string ListConnections()
    {
        var configs = db.GetConfigurations();
        if (configs.Count == 0)
            return "No database connections are configured. Use the add_connection tool to add one, or add a Databases section to appsettings.json.";

        return ToCsv(configs.Select(c =>
            new ConnectionCsvRow(c.Name, c.DbType.ToString(), c.Description)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // add_connection
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "add_connection")]
    [Description("""
        Dynamically add (or replace) a database connection at runtime without modifying config files.
        The connection is immediately available to all other tools via its name.
        Supported dbType values: SqlServer | MySql | PostgreSql | Sqlite | Oracle

        SQLite note: if the Data Source path in the connection string is relative (e.g. "Data Source=mydb.db"),
        it is automatically resolved to an absolute path based on the current working directory.
        Use an absolute path or ":memory:" to avoid any ambiguity.
        """)]
    public async Task<string> AddConnectionAsync(
        [Description("Connection string. SQLite example: \"Data Source=mydb.db\" or \"Data Source=/absolute/path/mydb.db\". SQL Server example: \"Server=host;Database=db;User Id=sa;Password=***;TrustServerCertificate=true;\"")]
        string connectionString,
        [Description("Database engine type: SqlServer | MySql | PostgreSql | Sqlite | Oracle")]
        string dbType,
        [Description("Logical name for this connection, used as connectionName in other tools. Auto-generated if omitted.")]
        string? name = null,
        [Description("Optional human-readable description of the connection.")]
        string? description = null,
        [Description("Test the connection before saving it. Defaults to true.")]
        bool testConnection = true,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<Models.DbType>(dbType, ignoreCase: true, out var parsedDbType))
        {
            var validValues = string.Join(", ", Enum.GetNames<Models.DbType>());
            return $"Error: unrecognized database type '{dbType}'. Supported values: {validValues}";
        }

        // For SQLite, ensure the Data Source is an absolute path so the file is
        // always found regardless of the working directory at the time of the call.
        if (parsedDbType == Models.DbType.Sqlite)
            connectionString = ResolveSqliteDataSource(connectionString);

        var connectionName = string.IsNullOrWhiteSpace(name)
            ? $"{parsedDbType.ToString().ToLower()}-{Guid.NewGuid().ToString("N")[..6]}"
            : name;

        var config = new DatabaseConfig
        {
            Name             = connectionName,
            DbType           = parsedDbType,
            ConnectionString = connectionString,
            Description      = description,
        };

        var error = await db.AddConnectionAsync(config, testFirst: testConnection, ct: cancellationToken);
        if (error is not null)
            return $"Error: {error}";

        return $"Connection '{connectionName}' ({parsedDbType}) added successfully. You can now use this name with other database tools.";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // remove_connection
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "remove_connection")]
    [Description("""
        Remove a dynamically-added database connection.
        Note: connections pre-configured in appsettings.json cannot be removed this way.
        """)]
    public string RemoveConnection(
        [Description("Name of the connection to remove.")]
        string connectionName)
    {
        return db.RemoveConnection(connectionName)
            ? $"Connection '{connectionName}' removed successfully."
            : $"No dynamic connection named '{connectionName}' was found (pre-configured connections cannot be removed).";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // list_objects
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "list_objects")]
    [Description("""
        List all objects in the database: tables, views, stored procedures, functions,
        triggers, sequences, synonyms, etc.  Includes the schema (owner in Oracle),
        object type, and comment for each object.
        Supports filtering by schema, name keyword, and object type.
        Returns CSV (schema,objectType,objectName,comment).
        """)]
    public async Task<string> ListObjectsAsync(
        [Description("Database connection name from list_connections.")]
        string connectionName,
        [Description("Filter by object type (exact match, case-insensitive): TABLE, VIEW, PROCEDURE, FUNCTION, TRIGGER, SEQUENCE, SYNONYM, etc. Leave empty to return all types.")]
        string? objectType = null,
        [Description("Filter by schema name (SQL Server/PostgreSQL: schema name; MySQL: database name; Oracle: owner/user). Supports SQL LIKE wildcards: % = any string, _ = single character. Leave empty to return objects from the current schema/user.")]
        string? schemaFilter = null,
        [Description("Filter by object name. Without wildcards, treated as a substring search (e.g. \"user\" matches all objects containing \"user\"). Supports SQL LIKE wildcards. Leave empty to return all.")]
        string? namePattern = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var objects = await db.ListDbObjectsAsync(
                connectionName,
                ToLikeFilter(namePattern),
                ToLikeFilter(schemaFilter),
                cancellationToken);

            // Post-filter by exact objectType if provided (case-insensitive).
            if (objectType is not null)
                objects = objects.Where(o =>
                    o.Type.Equals(objectType, StringComparison.OrdinalIgnoreCase)).ToList();

            if (objects.Count == 0)
                return namePattern is null && schemaFilter is null && objectType is null
                    ? "No objects found in the database."
                    : $"No objects matched the filters (namePattern='{namePattern}', schema='{schemaFilter}', objectType='{objectType}').";

            return ToCsv(objects.Select(o => new ObjectCsvRow(o.Schema, o.Type, o.Name, o.Comment)));
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // get_table_schema
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_table_schema")]
    [Description("""
        Get the detailed structure of a table, including:
        - Column name, data type, nullability, primary-key flag, default value, max length
        - Table comment and per-column comments (important for understanding business semantics)
        """)]
    public async Task<string> GetTableSchemaAsync(
        [Description("Database connection name.")]
        string connectionName,
        [Description("Table name (without schema prefix).")]
        string tableName,
        [Description("Schema name. Defaults: SQL Server = dbo, PostgreSQL = public, Oracle = current user, MySQL/SQLite = leave empty.")]
        string? schema = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await db.GetTableSchemaAsync(connectionName, tableName, schema, cancellationToken);
            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // get_table_indexes
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_table_indexes")]
    [Description("Get all indexes defined on a table, including index name, uniqueness, primary-key flag, and the columns involved.")]
    public async Task<string> GetTableIndexesAsync(
        [Description("Database connection name.")]
        string connectionName,
        [Description("Table name.")]
        string tableName,
        [Description("Schema name. Leave empty to use the default schema.")]
        string? schema = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await db.GetTableIndexesAsync(connectionName, tableName, schema, cancellationToken);
            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // query_sql
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "query_sql")]
    [Description("""
        Execute a read-only SQL query and return the results as CSV.
        Suitable for SELECT statements and any SQL that produces a result set.
        Destructive operations (DROP / DELETE / UPDATE / ALTER) are not allowed;
        use execute_sql (with --allow-any-sql) for those.
        Returns CSV with column headers on the first row followed by data rows.
        Returns a message when the query produces no rows.
        """)]
    public async Task<string> QuerySqlAsync(
        [Description("Database connection name.")]
        string connectionName,
        [Description("SQL query to execute (SELECT or any read-only statement).")]
        string sql,
        [Description("Maximum number of rows to return. Default 200, max 1000.")]
        int maxRows = 200,
        CancellationToken cancellationToken = default)
    {
        if (WriteKeywordsRegex.IsMatch(sql))
            return """
                Error: the SQL contains a write operation (INSERT / UPDATE / DELETE / DROP / ALTER / CREATE / TRUNCATE).
                Use the execute_sql tool for write operations (requires --allow-any-sql).
                """;

        maxRows = Math.Clamp(maxRows, 1, 1000);
        try
        {
            var result = await db.ExecuteSqlAsync(connectionName, sql, maxRows, cancellationToken);

            if (result.ErrorMessage is not null)
                return $"Error: {result.ErrorMessage}";

            if (result.Columns.Count == 0)
                return "Query executed successfully but returned no result set (non-SELECT statement?). Use execute_sql for DML/DDL.";

            if (result.Rows.Count == 0)
                return "Query returned 0 rows.";

            return QueryResultToCsv(result);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // execute_sql
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "execute_sql")]
    [Description("""
        Execute a SQL statement that modifies data or schema (INSERT / UPDATE / DELETE / DDL).
        Requires the server to be started with the --allow-any-sql argument.
        Returns the number of rows affected.
        WARNING: Use with caution — DDL and bulk DELETE/UPDATE may not be reversible.
        For read-only SELECT queries use query_sql instead.
        """)]
    public async Task<string> ExecuteSqlAsync(
        [Description("Database connection name.")]
        string connectionName,
        [Description("SQL statement to execute (INSERT / UPDATE / DELETE / DDL).")]
        string sql,
        CancellationToken cancellationToken = default)
    {
        if (!serverOptions.AllowAnySql)
            return """
                Error: execute_sql is disabled. Start the server with --allow-any-sql to enable write operations.
                Example: dotnet run --project src/AdoMcpServer -- --allow-any-sql
                """;

        try
        {
            var result = await db.ExecuteSqlAsync(connectionName, sql, maxRows: 0, cancellationToken);

            if (result.ErrorMessage is not null)
                return $"Error: {result.ErrorMessage}";

            return result.RowsAffected >= 0
                ? $"{result.RowsAffected} row(s) affected."
                : "Statement executed successfully.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
